#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private sealed class VirtualRow
    {
        internal string Key="",Name="",DisplayStamp="";
        internal float Y,Height,Width;
        internal OverviewMod? Mod;
        internal long StampRevision=-1;
        internal string CachedStamp="";
        internal Action<RectTransform> Bind=null!;
        internal Func<RectTransform,IEnumerator>? BuildLayers;
        internal IEnumerator? Layers;
        internal RectTransform? View;
        internal readonly RowIdentityLease Lease=new();
    }
    // All rows are resident. Scroll movement never evicts or rebinds a row.
    private readonly List<VirtualRow> _virtualRows=new();
    private readonly HashSet<string> _loadedRowSounds = new(StringComparer.Ordinal);
    private void CompleteRowSound(VirtualRow row)
    {
        if (ListVisible && row.BuildLayers != null && _loadedRowSounds.Add(row.Key)) _cascade.PlayLoadedRowSound();
    }
    private bool _residentPending;
    private readonly List<VirtualRow> _buildOrder = new();
    private bool _buildOrderCaptured;
    private int _buildCursor;
    private void CaptureBuildOrder()
    {
        if(_buildOrderCaptured)return;
        _buildOrderCaptured=true;_buildOrder.Clear();
        float top=Mathf.Max(0,_content.anchoredPosition.y);
        int first=_virtualRows.FindIndex(row=>row.Y+row.Height>top);
        foreach(int index in ResidentBuildOrder.Create(_virtualRows.Count,first))_buildOrder.Add(_virtualRows[index]);
    }
    // One shared time budget, finish this row before starting the next one.
    private readonly RowBuildBudget _rowBuildBudget = new(maxSteps:16);
    private bool ListVisible => _opened && _root.gameObject.activeInHierarchy;
    private (VirtualRow Row,int Column,GameObject? Source)? _layerFocus;
    private MoveDirection _layerMoveDirection;
    private void CompleteLayerFocus(VirtualRow row)
    {
        if (!_layerFocus.HasValue || !ReferenceEquals(_layerFocus.Value.Row, row)) return;
        var focus = _layerFocus.Value; var direction = _layerMoveDirection; _layerFocus = null; _layerMoveDirection = MoveDirection.None;
        if (!_opened || OverviewUi.HasModal || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != focus.Source) return;
        var target = ColumnTarget(row, focus.Column);
        if (target != null) SelectColumn(target, focus.Column);
        else if (direction is MoveDirection.Up or MoveDirection.Down) MoveVirtualColumn(row, focus.Source, focus.Column, direction);
    }
    private void AddLayeredRow(string key,string name,float y,float height,float width,Func<RectTransform,IEnumerator> layers,OverviewMod? mod=null)
    {
        AddVirtualRow(key,name,y,height,width,_=>{},mod);
        _virtualRows[_virtualRows.Count-1].BuildLayers=layers;
    }
    private long _rowDataRevision;
    private readonly Dictionary<string,string> _componentUpdateStamps=new(StringComparer.Ordinal);
    private float _virtualHeight;
    private void AddVirtualRow(string key,string name,float y,float height,float width,Action<RectTransform> bind,OverviewMod? mod=null)
    {
        _virtualHeight=Mathf.Max(_virtualHeight,y+height+8);
        if(_reuseRows && mod!=null && _parkedRows.TryGetValue(key,out var parked))
        {
            _parkedRows.Remove(key);
            if(ReferenceEquals(parked.Mod,mod) && parked.View!=null && parked.Layers==null && parked.DisplayStamp==RowUpdateStamp(parked))
            {
                // Same VirtualRow object: its focus/navigation closures and identity lease stay valid; only its slot moves.
                parked.Name=name;parked.Y=y;parked.Height=height;parked.Width=width;parked.Bind=bind;
                var view=parked.View;view.SetParent(_content,false);OverviewUi.Place(view,0,y,width,height);
                _virtualRows.Add(parked);
                return;
            }
            ReleaseRow(parked);
        }
        _virtualRows.Add(new VirtualRow {Key=key,Name=name,Y=y,Height=height,Width=width,Bind=bind,Mod=mod});
    }

    // 2.3.6: folding or unfolding a group used to go through the full Render(), which destroyed and rebuilt every row
    // (hundreds of controls each). A fold only changes which rows show and where, so built mod rows are parked and
    // moved instead; rows folded away stay parked (under an inactive holder) for the next unfold. Any other Render
    // discards them, so data, filter, width, language and mode changes still rebuild from scratch.
    private bool _reuseRows;
    private string _rowsSignature="";
    private readonly Dictionary<string,VirtualRow> _parkedRows=new(StringComparer.Ordinal);
    private RectTransform? _parking;
    private void RenderFold() { _reuseRows=true; try { Render(); } finally { _reuseRows=false; } }
    /// <summary>Everything a built mod row's layout depends on besides its own data (which the update stamp covers).</summary>
    private string RowsSignature(float width)=>Mathf.RoundToInt(width)+"|"+OverviewLanguageBridge.Revision+"|"+_authorMode+"|"+OverviewOptions.ShowSourceLabels.Value;
    private void ParkBuiltModRows()
    {
        if(_parking==null) { _parking=OverviewUi.Rect("ParkedRows",_root);_parking.gameObject.SetActive(false); }
        for(int i=_virtualRows.Count-1;i>=0;i--)
        {
            var row=_virtualRows[i];
            if(row.Mod==null || row.View==null || row.Layers!=null || !row.Key.StartsWith("mod:",StringComparison.Ordinal))continue;
            if(_layerFocus.HasValue && ReferenceEquals(_layerFocus.Value.Row,row))continue;
            if(row.DisplayStamp!=RowUpdateStamp(row))continue;
            _virtualRows.RemoveAt(i);
            if(_parkedRows.TryGetValue(row.Key,out var older) && !ReferenceEquals(older,row))ReleaseRow(older);
            row.View.SetParent(_parking,false);_parkedRows[row.Key]=row;
        }
    }
    private void DestroyParkedRows()
    {
        foreach(var row in _parkedRows.Values)ReleaseRow(row);
        _parkedRows.Clear();
    }
    /// <summary>A row moved by a fold keeps its controls; refresh the parts that depend on its new position.</summary>
    private void RebindReusedModRow(RectTransform view,OverviewMod mod,string groupId)
    {
        _dropAreas[view]=(groupId,mod);
        var number=view.Find("RowIndex")?.GetComponent<TMP_Text>();
        if(number!=null)number.text=(VisibleIndex(mod)+1).ToString();
        view.GetComponent<OverviewEnablementBinding>()?.Refresh(MarkerState()?.Mod(mod.Guid));
    }
    private int VisibleIndex(OverviewMod mod)=>_displayNumbers.TryGetValue(mod,out int number)?number-1:0;
    private void ResetVirtualRows()
    {
        _dependencyHints.HideNow();
        foreach(var row in _virtualRows)ReleaseRow(row);
        _virtualRows.Clear();_buildOrder.Clear();_buildCursor=0;_buildOrderCaptured=false;_virtualHeight=0;_residentPending=true;
    }
    private void ReleaseRow(VirtualRow row)
    {
        if(_layerFocus.HasValue && ReferenceEquals(_layerFocus.Value.Row,row))_layerFocus=null;
        (row.Layers as IDisposable)?.Dispose();row.Layers=null;
        var view=row.View;if(view==null)return;
        row.Lease.Release();
        var selected=EventSystem.current?.currentSelectedGameObject;
        if(selected!=null && selected.transform.IsChildOf(view))EventSystem.current!.SetSelectedGameObject(null);
        HideAllHints();_dropAreas.Remove(view);
        _dependencyHints.Unbind(view);
        // Eviction / structural invalidation only. Idle same-identity views retain every
        // descendant and listener; opaque prefab callbacks are never redirected to another mod.
        foreach(var interaction in view.GetComponentsInChildren<OverviewRowInteraction>(true))
        { interaction.DragStateChanged=null;interaction.IsDragging=false;interaction.Clicked=null;interaction.DragStarted=interaction.DragFinished=interaction.Dropped=null;interaction.DragMoved=null;interaction.NativeScroll=null;interaction.ForwardScroll=null; }
        foreach(var click in view.GetComponentsInChildren<OverviewTextClick>(true))click.Clicked=null;
        foreach(var hover in view.GetComponentsInChildren<OverviewHover>(true))hover.Enter=hover.Exit=null;
        foreach(var focus in view.GetComponentsInChildren<OverviewHotkeyFocus>(true))focus.Selected=focus.Deselected=null;
        foreach(var move in view.GetComponentsInChildren<OverviewVirtualMove>(true))move.Move=null;
        foreach(var button in view.GetComponentsInChildren<Button>(true))button.onClick.RemoveAllListeners();
        foreach(var toggle in view.GetComponentsInChildren<Toggle>(true))toggle.onValueChanged.RemoveAllListeners();
        view.gameObject.SetActive(false);view.SetParent(_root,false);row.View=null;
        UnityEngine.Object.Destroy(view.gameObject);
    }
    private void RefreshVirtualRows(VirtualRow? immediate=null)
    {
        if(_disposed || !ListVisible || _building || _infrastructureLoading)return;
        if(!_residentPending && immediate==null && !_layerFocus.HasValue)return;
        CaptureBuildOrder();
        // Scrolling/focus changes only the viewport. Even a requested focus target
        // waits for this generation's fixed build cursor instead of jumping the queue.
        while(_buildCursor<_buildOrder.Count)
        {
            if(!BuildResidentRow(_buildOrder[_buildCursor]))break;
            _buildCursor++;
        }
        _residentPending=_buildCursor<_buildOrder.Count;
    }
    private bool BuildResidentRow(VirtualRow row,bool immediate=false)
    {
        if(row.View!=null && row.DisplayStamp!=RowUpdateStamp(row))ReleaseRow(row);
        if(row.View==null)
        {
            if(!_rowBuildBudget.TryTake(Time.frameCount,System.Diagnostics.Stopwatch.GetTimestamp(),immediate))return false;
            var view=OverviewUi.Panel("Row",_content,Color.clear);view.name=row.Name;
            view.GetComponent<Image>().color=row.Mod!=null && _selected.Contains(row.Mod.Id)
                ? ModConfigLook.Surface(new Color(.18f,.34f,.43f,.72f)) : ModConfigLook.Surface(new Color(.1f,.11f,.13f,.4f));
            OverviewUi.Place(view,0,row.Y,row.Width,row.Height);row.View=view;row.Lease.Bind(row.Key);
            try
            {
                row.Bind(view);
                row.Layers=row.BuildLayers?.Invoke(view);
                row.DisplayStamp=RowUpdateStamp(row);
                if(row.Layers==null) { WireRowNavigation(row);_cascade.RegisterBuiltRow(view);CompleteRowSound(row); }
            }
            catch { ReleaseRow(row);throw; }
            
        }
        while(row.Layers!=null)
        {
            if(!_rowBuildBudget.TryTake(Time.frameCount,System.Diagnostics.Stopwatch.GetTimestamp()))return false;
            try
            {
                if(row.Layers.MoveNext())continue;
                (row.Layers as IDisposable)?.Dispose();row.Layers=null;
                WireRowNavigation(row);_cascade.RegisterBuiltRow(row.View!);CompleteRowSound(row);CompleteLayerFocus(row);
            }
            catch(Exception error)
            {
                (row.Layers as IDisposable)?.Dispose();row.Layers=null;
                Plugin.Log.LogError("[概览] 行控件加载失败："+error);
                _status(Loc.T("The mod list could not finish refreshing. Please refresh to retry: ", "模组列表刷新未完成，请点击刷新重试：", "模組清單重新整理未完成，請點擊重新整理重試：")+error.Message);
                WireRowNavigation(row);CompleteLayerFocus(row);
            }
            
        }
        return true;
    }
    private void WireRowNavigation(VirtualRow row)
    {
        var controls = RowControls(row.View!);
        for (int c=0; c<controls.Length; c++)
        {
            var selectable=controls[c];
            if (selectable.GetComponent<OverviewRowColumn>() == null) {
                var rect = (RectTransform)selectable.transform; float x = row.View!.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).x;
                selectable.gameObject.AddComponent<OverviewRowColumn>().Column = IsNavigationGroup(row) ? 1 : ListColumnPolicy.FromPosition(x, _columnStarts);
            }
            var nav=selectable.navigation; nav.mode=Navigation.Mode.Explicit;
            nav.selectOnUp=null; nav.selectOnDown=null;
            nav.selectOnLeft = controls.Take(c).LastOrDefault(s => s.IsActive() && s.IsInteractable());
            nav.selectOnRight = controls.Skip(c + 1).FirstOrDefault(s => s.IsActive() && s.IsInteractable());
            selectable.navigation=nav;
            var move=selectable.GetComponent<OverviewVirtualMove>() ?? selectable.gameObject.AddComponent<OverviewVirtualMove>();
            move.Move=direction=> { if(!TryReturnFocusToView(row,selectable)) MoveVirtualFocus(row,selectable,direction); };
            move.Sideways=()=>TryReturnFocusToView(row,selectable);
            var focus=selectable.GetComponent<OverviewHotkeyFocus>() ?? selectable.gameObject.AddComponent<OverviewHotkeyFocus>();
            // 1.1.0: every cell gets the gamepad focus ring, and the focused row is lit so the eye finds it.
            GamepadSupport.MarkSelectable(selectable.gameObject,null,null);
            var view=row.View;
            focus.Selected+=()=>SetRowFocusGlow(view,true);focus.Deselected+=()=>SetRowFocusGlow(view,false);
            focus.Selected+=()=> { if (!_selectingColumn) { _preferredColumn = ColumnOf(view!, selectable); _verticalScrollBeforeMove = null; } };
            focus.Selected+=row.Lease.Guard(()=> {
                float top=VirtualRowRange.ScrollTo(row.Y,row.Height,_content.anchoredPosition.y,_viewport.rect.height);
                if(Mathf.Abs(top-_content.anchoredPosition.y)>.1f)
                { var position=_content.anchoredPosition;position.y=top;_content.anchoredPosition=position;RefreshVirtualRows(); }
            });
        }
    }
    // Called only at a data-publication / structural boundary, never on scrolling.
    private void RefreshRowData()
    {
        unchecked { _rowDataRevision++; }
        _componentUpdateStamps.Clear();
        foreach(var group in InfrastructurePackages.Results.GroupBy(p=>p.Key,StringComparer.Ordinal))
            _componentUpdateStamps["component:"+group.Key]=string.Join("\n",group.Select(p=>p.Version+"	"+p.Latest));
    }
    private string RowUpdateStamp(VirtualRow row)
    {
        if(row.StampRevision==_rowDataRevision)return row.CachedStamp;
        var mod=row.Mod;
        string stamp=mod==null
            ? (_componentUpdateStamps.TryGetValue(row.Key,out var component)?component:"")
            : string.Join("	",mod.LatestVersion,mod.HasUpdate,mod.CheckMessage,mod.UpdateNotes,mod.Author,
                mod.HomepageUrl,mod.CategoriesKnown,string.Join("|",mod.Categories),mod.SourceKind,mod.SourceConfirmed,mod.SourceKey,mod.IdentityConflict);
        row.CachedStamp=stamp;row.StampRevision=_rowDataRevision;
        return stamp;
    }
    private void RefreshUpdateRows()
    {
        if (_infrastructureLoading) return; // The pending full commit includes the latest update results.
        // Invalidate ALL row stamps, including parked/unbuilt rows. They compare lazily on wake.
        RefreshRowData();
        // Category/source filtering is structural; ordinary version/result changes are not.
        if(Tags.Count>0 || _sourceFilter>=0) { Render();return; }
        if(!_opened || _building)return;
        var focus=CaptureRowFocus();
        foreach(var row in _virtualRows)
            if(row.View!=null && row.DisplayStamp!=RowUpdateStamp(row))ReleaseRow(row);
        _buildCursor=0;_residentPending=true;RefreshVirtualRows();RestoreRowFocus(focus);
        _renderedUpdates=UpdateDisplayKey();
    }
    private (string Key,int Column)? CaptureRowFocus()
    {
        var selected=EventSystem.current?.currentSelectedGameObject;
        if(selected==null)return null;
        foreach(var row in _virtualRows)
        {
            if(row.View==null || !selected.transform.IsChildOf(row.View))continue;
            var controls=RowControls(row.View);
            return (row.Key, IsNavigationGroup(row) && _preferredColumn.HasValue ? _preferredColumn.Value : ColumnOf(row.View, selected.GetComponent<Selectable>()));
        }
        return null;
    }
    private void RestoreRowFocus((string Key,int Column)? focus)
    {
        if(!focus.HasValue || EventSystem.current==null || OverviewUi.HasModal)return;
        var row=_virtualRows.Find(r=>r.Key==focus.Value.Key);
        if(row==null)return;
        ScrollToVirtualRow(row);
        if(row.View==null || row.Layers!=null) { _layerMoveDirection = MoveDirection.None; _layerFocus=(row,focus.Value.Column,EventSystem.current.currentSelectedGameObject);return; }
        var controls=(row.View==null?null:RowControls(row.View));
        if(controls==null || controls.Length==0)return;
        var target = ColumnTarget(row, focus.Value.Column);
        if (target != null) SelectColumn(target, focus.Value.Column);
    }
    private void ScrollToVirtualRow(VirtualRow row)
    {
        var position=_content.anchoredPosition;
        position.y=VirtualRowRange.ScrollTo(row.Y,row.Height,position.y,_viewport.rect.height);
        _content.anchoredPosition=position;RefreshVirtualRows(row);
    }
    private void MoveVirtualFocus(VirtualRow row, Selectable source, MoveDirection direction)
    {
        if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
        int column = IsNavigationGroup(row) && _preferredColumn.HasValue ? _preferredColumn.Value : ColumnOf(row.View!, source);
        MoveVirtualColumn(row, source.gameObject, column, direction);
    }
    private void MoveVirtualColumn(VirtualRow row, GameObject? sourceObject, int column, MoveDirection direction)
    {
        _preferredColumn = column;
        _verticalScrollBeforeMove ??= _content.anchoredPosition.y;
        int index = _virtualRows.IndexOf(row), step = direction == MoveDirection.Down ? 1 : -1;
        for (int next = VirtualRowRange.Next(index, step, _virtualRows.Count); next >= 0; next = VirtualRowRange.Next(next, step, _virtualRows.Count)) {
            var candidate = _virtualRows[next]; ScrollToVirtualRow(candidate);
            if (candidate.View == null || candidate.Layers != null) { _layerMoveDirection = direction; _layerFocus = (candidate, column, sourceObject); return; }
            var target = ColumnTarget(candidate, column);
            if (target != null) { SelectColumn(target, column); return; }
        }
        if (_verticalScrollBeforeMove.HasValue) {
            var position = _content.anchoredPosition; position.y = _verticalScrollBeforeMove.Value; _content.anchoredPosition = position;
            _verticalScrollBeforeMove = null; RefreshVirtualRows();
        }
        // 1.1.4 (user): Up from the first row went nowhere, so the controller could never reach the buttons above the
        // list. Leave to the nearest control in the column headers / list tools (they lead on up to the page toolbar).
        if(direction==MoveDirection.Up)
        {
            var above=_header.GetComponentsInChildren<Selectable>(false).Concat(_tools.GetComponentsInChildren<Selectable>(false))
                .Where(s=>s.IsActive() && s.IsInteractable() && s.navigation.mode!=Navigation.Mode.None)
                .OrderBy(s=>s.transform.IsChildOf(_header)?0:1).ThenBy(s=>Mathf.Abs(s.transform.position.x - (sourceObject != null ? sourceObject.transform.position.x : 0))).FirstOrDefault();
            if(above!=null){_content.anchoredPosition=new Vector2(_content.anchoredPosition.x,0);RefreshVirtualRows();EventSystem.current?.SetSelectedGameObject(above.gameObject);}
        }
    }

}

// Selectable's explicit vertical links are intentionally null. This hook wakes an offscreen model row.
internal sealed class OverviewVirtualMove : MonoBehaviour, IMoveHandler
{
    internal Action<MoveDirection>? Move;
    /// <summary>1.1.0: a left/right move from a row scrolled out of view returns to the view instead (true = handled).</summary>
    internal Func<bool>? Sideways;
    public void OnMove(AxisEventData data)
    {
        if(data.moveDir==MoveDirection.Up || data.moveDir==MoveDirection.Down) { Move?.Invoke(data.moveDir);data.Use(); }
        else if(Sideways?.Invoke()==true) data.Use();
    }
}
