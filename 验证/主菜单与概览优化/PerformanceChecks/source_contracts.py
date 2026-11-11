"""Narrow wiring checks; these do not emulate Unity/EventSystem integration."""
from pathlib import Path
root = Path(__file__).resolve().parents[3]
view = (root/'src/Overview/OverviewListView.cs').read_text(encoding='utf-8-sig')
rows = (root/'src/Overview/OverviewListView.VirtualRows.cs').read_text(encoding='utf-8-sig')
queue = (root/'src/Overview/OverviewUpdateQueue.cs').read_text(encoding='utf-8-sig')
hints = (root/'src/Overview/OverviewDependencyHints.cs').read_text(encoding='utf-8-sig')
checks = {
 'no duplicate infrastructure summary': 'InfrastructurePackages.Summary()' not in view,
 'progress uses latest-value coalescer': '_checkProgress.Publish(' in view,
 'check pre-await inventory is background': 'await Task.Run(()=> { checkStarted = true; return service.CheckAsync' in view,
 'UI completion rejects changed context': 'context!=SourceUi.DependencyContext' in view,
 'structural refresh captures and restores focus': 'var oldFocus=CaptureRowFocus();' in view and 'RestoreRowFocus(oldFocus);' in view,
 'native scroll drag source remains pinned': 'OverviewRowInteraction>()?.IsDragging==true' in rows,
 'author drag source remains pinned': 'ReferenceEquals(row.Mod,_dragged)' in rows,
 'full model navigation wakes row': 'VirtualRowRange.Next(index,step,_virtualRows.Count)' in rows and 'ScrollToVirtualRow(candidate);' in rows,
 'release invalidates lease before listeners': rows.index('row.Lease.Release();') < rows.index('button.onClick.RemoveAllListeners();'),
 'release clears dependency bindings': '_dependencyHints.Unbind(view);' in rows,
 'release clears move and drag hooks': 'move.Move=null;' in rows and 'interaction.DragStateChanged=null;' in rows,
 'eviction deactivates full tree before deferred destroy': 'view.gameObject.SetActive(false);view.SetParent(_root,false);row.View=null;' in rows,
 'no descendant destroy/rebuild on viewport recycle': 'view.childCount' not in rows and 'if(!visible && !pinned)ParkRow(row);' in rows,
 'wake preserves full tree without bind': 'Same identity, same controls, same listeners' in rows,
 'separate idle budgets for mod component group': '_modViews=new(24), _componentViews=new(12), _groupViews=new(12)' in rows,
 'ordinary group headings use full layout model': 'AddVirtualRow("group:"+groupId' in view,
 'infrastructure heading uses full layout model': 'AddVirtualRow("infrastructure-group:"' in (root/'src/Overview/OverviewListView.Infrastructure.cs').read_text(encoding='utf-8-sig'),
 'inactive cached drop areas excluded': '!area.Key.gameObject.activeInHierarchy' in view,
 'worker publication filters unchanged snapshots': 'publication.Publish(() => JsonSerializer.Serialize(snapshot, Files.Json)' in (root/'Updater/QueueWorker.cs').read_text(encoding='utf-8-sig'),
 'dependency unbind clears cached and pending hints': '_layouts.Clear();' in hints and '_pendingSource.transform.IsChildOf(root)' in hints,
 'ordinary tooltip includes font-size invalidation': '_hintFontSize!=_hintText.fontSize' in view,
 'queue fingerprints independent transaction files': 'Path.Combine(dir,"transaction.json")' in queue,
 'queue publication rejects old environment': 'if(context!=_readContext)return;' in queue,
 'queue file parsing stays background': 'Task.Run(()=>RefreshRead(root,context))' in queue,
 'update completion uses local row refresh': 'if (rowsChanged) RefreshUpdateRows();' in view,
}
infra = (root/'src/Overview/OverviewListView.Infrastructure.cs').read_text(encoding='utf-8-sig')
flush = infra[infra.index('private void FlushInfrastructureRedraw()'):infra.index('private void BuildOverviewSearch')]
render = view[view.index('private void Render()'):view.index('private TextMeshProUGUI Label')]
stamp = rows[rows.index('private string RowUpdateStamp'):rows.index('private void RefreshUpdateRows')]
refresh = rows[rows.index('private void RefreshUpdateRows'):rows.index('private (string Key,int Column)? CaptureRowFocus')]
checks.update({
 'row owns stable model reference': 'internal OverviewMod? Mod;' in rows and '},mod);' in render,
 'stamp uses revision fast path before model work': stamp.index('if(row.StampRevision==_rowDataRevision)return row.CachedStamp;') < stamp.index('var mod=row.Mod;'),
 'stamp never scans model or component lists': '_mods.Find' not in stamp and 'InfrastructurePackages.Results' not in stamp,
 'data publication invalidates parked and unbuilt stamps': refresh.index('RefreshRowData();') < refresh.index('if(!_opened || _building)return;'),
 'data revision invalidation only at publication boundaries': view.count('RefreshRowData();') == 1 and rows.count('RefreshRowData();') == 1,
 'structural rebuild refreshes component snapshot': 'RefreshRowData();' in render,
 'bind failure invalidates committed viewport': 'catch { _virtualTop=float.NaN;ReleaseRow(row);throw; }' in rows,
 'infrastructure selected focus cannot starve redraw': 'currentSelectedGameObject' not in flush and '_infrastructureRedraw = false; Render();' in flush,
 'immediate virtual refresh before focus restore': render.index('finally { _building = false; }') < render.index('RefreshVirtualRows();') < render.index('RestoreRowFocus(oldFocus);'),
 'render removes obsolete buildingRow rollback': 'buildingRow' not in render,
})
for name, ok in checks.items():
    assert ok, name
    print('PASS', name)
print(f'RESULT {len(checks)} source contracts passed; no Unity runtime assertions')
