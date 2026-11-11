#nullable enable
using System;
using System.Threading;
using UnityEngine;
namespace ModConfigEnhance.Overview;
internal static partial class SourceUi
{
    private static async void ResetAndIdentify(OverviewRepository repo, OverviewMod mod, Action refresh, Action<string> status)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_SourceIdentifyProgress", 580, 260); if (panel == null) return;
        var text = OverviewUi.Text(panel, Loc.T("Identifying store source…", "正在识别商店来源……", "正在識別商店來源……"), 20);
        OverviewUi.Fill(text.rectTransform, 24);
        var lifetime = panel.gameObject.AddComponent<SourceSearchLifetime>();
        var cancellation = new CancellationTokenSource(); lifetime.Cancellation = cancellation;
        ModSources.Set(new[] { mod }, null);
        mod.Rule.sourceNamespace = mod.Rule.sourceName = "";
        string context = ModSources.Context;
        try
        {
            await ModSources.Coordinator.WaitAsync(cancellation.Token);
            try { await ModSources.Discover(new[] { mod }, cancellation.Token, force: true); }
            finally { ModSources.Coordinator.Release(); }
            if (panel == null || cancellation.IsCancellationRequested || context != ModSources.Context) return;
            bool resolved = mod.SourceConfirmed;
            OverviewUi.CloseModal(); refresh();
            if (resolved) status(Loc.T("Store source identified: ", "已识别商店来源：", "已識別商店來源：") + mod.SourceKey);
            else Search(repo, mod, refresh, status, query: ModSources.SearchName(mod));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (panel != null) text.text = Loc.LocalizeMessage(e.Message); }
    }
}
