#nullable enable
using System.Linq;
using BepInEx.Bootstrap;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    internal void GuideFontsChanged() => _introLoaded = false;
    private bool _listLoaded, _introLoaded, _hotkeysStale = true;
    private int _inventoryVersion = -1, _contentVersion = -1, _configurationVersion = -1, _schemaCount = -1;
    private bool _sourceLabels;

    private void PrepareCachedPage()
    {
        OverviewPageCache.OnPageEnter();
        int inventory = OverviewPageCache.Inventory, content = OverviewPageCache.Content;
        int schema = Chainloader.PluginInfos.Count + Chainloader.PluginInfos.Values.Sum(p => p.Instance != null ? p.Instance.Config.Count : 0);
        bool repositoryChanged = _repo.IsStale || _listLoaded && (!OverviewPageCache.Reliable || inventory != _inventoryVersion || content != _contentVersion || schema != _schemaCount);
        if (repositoryChanged && !ProfileHasUnsaved)
        {
            ModSources.InvalidateRecords();
            PublishedPack.InvalidateCache();
            _repo.Reload(); _listLoaded = false; _introLoaded = false; _hotkeysStale = true;
        }
        
        if (!_listLoaded)
        {
            _list?.Reload(); _listLoaded = true; _hotkeysStale = true;
            // 2.3.7: this scan already reflects every finished update job; restart the "job finished" baseline so
            // PollUpdates does not reload the list a second time right after.
            _updateInventoryKey = OverviewUpdateQueue.HasRead ? UpdateInventoryKey() : null;
            
        }
        else if (_languageRevision != OverviewLanguageBridge.Revision || _sourceLabels != OverviewOptions.ShowSourceLabels.Value || _configurationVersion != OverviewPageCache.Configuration)
        {
            _list?.RefreshTagFilter(); _hotkeysStale = true;
            
        }

        if (_configurationVersion != OverviewPageCache.Configuration || schema != _schemaCount) _hotkeysStale = true;
        _inventoryVersion = inventory; _contentVersion = content; _schemaCount = schema;
        _configurationVersion = OverviewPageCache.Configuration; _sourceLabels = OverviewOptions.ShowSourceLabels.Value;
        EnsureSelectedPage();
    }
    private void EnsureSelectedPage()
    {
        if (_tab == PageTab.Introduction && !_introLoaded)
        {
            EnsureDocument().Reload(); _introLoaded = true; 
        }
        if (_tab == PageTab.Hotkeys && _hotkeysStale)
        {
            _hotkeys?.Reload(); _hotkeysStale = false; 
        }
    }
    private void RefreshLazyViews(bool introduction = true)
    {
        _hotkeysStale = true;
        if (introduction) _introLoaded = false;
        EnsureSelectedPage();
    }
}
