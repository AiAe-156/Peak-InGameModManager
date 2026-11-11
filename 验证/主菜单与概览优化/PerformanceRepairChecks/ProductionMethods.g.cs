using System; using System.Linq; using System.Collections.Generic;
partial class Harness {
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
private void RefreshVirtualRows()
    {
        if(_disposed || !_opened || _building)return;
        float top=Mathf.Max(0,_content.anchoredPosition.y),height=_viewport.rect.height;
        if(Mathf.Abs(top-_virtualTop)<.1f && Mathf.Abs(height-_virtualViewport)<.1f)return;
        _virtualTop=top;_virtualViewport=height;
        // Do not recycle the drag source during an EventSystem drag. Release after drop.
        foreach(var row in _virtualRows)
        {
            bool visible=VirtualRowRange.Contains(row.Y,row.Height,top,height,RowHeight*2);
            var selected=EventSystem.current?.currentSelectedGameObject;
            bool pinned=(row.View!=null && row.View.GetComponent<OverviewRowInteraction>()?.IsDragging==true)
                || (_dragged!=null && ReferenceEquals(row.Mod,_dragged))
                || (row.View!=null && selected!=null && selected.transform.IsChildOf(row.View));
            if(!visible && !pinned)ParkRow(row);
        }
        foreach(var row in _virtualRows)
        {
            if(!VirtualRowRange.Contains(row.Y,row.Height,top,height,RowHeight*2))continue;
            if(row.View!=null && row.DisplayStamp!=RowUpdateStamp(row))ReleaseRow(row);
            if(row.View!=null)
            {
                if(row.View.gameObject.activeSelf)continue;
                Cache(row).Remove(row);
                row.View.SetParent(_content,false);row.View.gameObject.SetActive(true);
                OverviewUi.Place(row.View,0,row.Y,row.Width,row.Height);
                continue; // Same identity, same controls, same listeners: no Bind/AddListener on wake.
            }
            var view=OverviewUi.Panel("Row",_content,Color.clear);
            view.name=row.Name;
            view.GetComponent<Image>().color=new Color(.1f,.11f,.13f,.4f);
            OverviewUi.Place(view,0,row.Y,row.Width,row.Height);row.View=view;
            row.Lease.Bind(row.Key);
            try { row.Bind(view); row.DisplayStamp=RowUpdateStamp(row); WireRowNavigation(row); }
            catch { _virtualTop=float.NaN;ReleaseRow(row);throw; }
        }
        RefreshSelectionColors();
        LinkRowNavigation();
    }
private void FlushInfrastructureRedraw()
    {
        if (!_infrastructureRedraw || !_opened || _building || IsEditingText || _dragged != null || _scroll.velocity.sqrMagnitude > 1) return;
        // Render captures/restores row focus; a selected row must not starve publication.
        _infrastructureRedraw = false; Render();
    }
}
