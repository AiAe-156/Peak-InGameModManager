#nullable enable
using System;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private bool _sourceRetrying;
    internal void RetryUnidentifiedSources() => BatchFindSources();
    internal async void ManualUnidentifiedSources()
    {
        var all = _mods.ToArray();
        if (_disposed || _sourceRetrying) return;
        string context = ModSources.Context;
        try {
            var catalog = await ModSources.Catalog(_lifetime.Token);
            if (_disposed || !_opened || context != ModSources.Context) return;
            foreach (var mod in all) ModSources.Apply(mod, catalog);
            var batch = new SourceIdentificationBatch(all, SourceIdentificationBatch.CaptureBefore(all), catalog, context);
            SourceUi.BatchResults(_repo, batch, Reload, _status);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) _status(Loc.LocalizeMessage(ex.Message)); }
    }
}
