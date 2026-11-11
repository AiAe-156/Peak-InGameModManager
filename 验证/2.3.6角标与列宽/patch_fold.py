import os
root = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance\src\Overview"
def edit(name, pairs):
    p = os.path.join(root, name)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (name, a[:70])
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8').write(s)

edit('OverviewListView.VirtualRows.cs', [(
'''    private void AddVirtualRow(string key,string name,float y,float height,float width,Action<RectTransform> bind,OverviewMod? mod=null)
    {
        _virtualRows.Add(new VirtualRow {Key=key,Name=name,Y=y,Height=height,Width=width,Bind=bind,Mod=mod});
        _virtualHeight=Mathf.Max(_virtualHeight,y+height+8);
    }''',
'''    private void AddVirtualRow(string key,string name,float y,float height,float width,Action<RectTransform> bind,OverviewMod? mod=null)
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
    private int VisibleIndex(OverviewMod mod)=>_displayNumbers.TryGetValue(mod,out int number)?number-1:0;'''),
# ResetVirtualRows releases every remaining row, but parked ones were already taken out of _virtualRows.
])

edit('OverviewListView.cs', [
('''            _conflictRefresh.Clear();ResetVirtualRows(); _dropLine.SetParent(_root, false); Clear(_content); _dropLine.SetParent(_content, false);''',
'''            string rowsSignature=RowsSignature(width);
            bool reuseRows=_reuseRows && rowsSignature==_rowsSignature;
            if(reuseRows)ParkBuiltModRows(); else { DestroyParkedRows();_conflictRefresh.Clear(); }
            _rowsSignature=rowsSignature;
            ResetVirtualRows(); _dropLine.SetParent(_root, false); Clear(_content); _dropLine.SetParent(_content, false);'''),
('''                interaction.Clicked = data => Select(mod, capturedIndex, data);''',
 '''                interaction.Clicked = data => Select(mod, VisibleIndex(mod), data);'''),
('''                Label(row, (capturedIndex+1).ToString(), xIndex, 4, indexWidth, 32, 20);''',
 '''                Label(row, (capturedIndex+1).ToString(), xIndex, 4, indexWidth, 32, 20).name = "RowIndex";'''),
('''titleClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, capturedIndex, data); };''',
 '''titleClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, VisibleIndex(mod), data); };'''),
('''descClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, capturedIndex, data); };''',
 '''descClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, VisibleIndex(mod), data); };'''),
('''                AddLayeredRow("mod:"+mod.Id, "Mod_"+mod.Id, rowY, RowHeight-2, width-15, BuildModLayers,mod);''',
 '''                AddLayeredRow("mod:"+mod.Id, "Mod_"+mod.Id, rowY, RowHeight-2, width-15, BuildModLayers,mod);
                var added = _virtualRows[_virtualRows.Count - 1];
                if (added.View != null) RebindReusedModRow(added.View, mod, groupId);'''),
('''            _renderedUpdates = UpdateDisplayKey();
        }
        catch (Exception ex)''',
 '''            _renderedUpdates = UpdateDisplayKey();
            if (reuseRows) { RefreshConflictBadges(conflicts); RefreshSelectionColors(); }
        }
        catch (Exception ex)'''),
('''_repo.SaveUiState(); _selected.Clear(); _anchor = -1; Render(); }, 22, 26);''',
 '''_repo.SaveUiState(); _selected.Clear(); _anchor = -1; RenderFold(); }, 22, 26);'''),
('''if (_disposed) return; HideAllHints();_componentProtectionRefresh.Clear(); ResetVirtualRows();''',
 '''if (_disposed) return; HideAllHints();_componentProtectionRefresh.Clear(); DestroyParkedRows(); ResetVirtualRows();'''),
])

edit('OverviewListView.Infrastructure.cs', [(
'_infrastructureCollapsed = !_infrastructureCollapsed; Render();',
'_infrastructureCollapsed = !_infrastructureCollapsed; RenderFold();')])
print('ok')
