#nullable enable
using System;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal static partial class SourceUi
{
    internal static string GitHubUrl(OverviewMod mod)
    {
        string[] links = { mod.HomepageUrl, ShopCatalog.Cached().FirstOrDefault(p => p.Team.Equals(mod.SourceNamespace, StringComparison.OrdinalIgnoreCase)
            && p.Name.Equals(mod.SourceName, StringComparison.OrdinalIgnoreCase))?.Homepage ?? "" };
        return links.FirstOrDefault(link => Uri.TryCreate(link, UriKind.Absolute, out var url)
            && (url.Scheme == "https" || url.Scheme == "http") && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) ?? "";
    }
    internal static string HomepageHint(OverviewMod mod)
    {
        bool local = ModSources.ExplicitLocal(mod);
        string text = local ? Loc.T("Local mod", "本地模组", "本機模組") : mod.SourceConfirmed
            ? Loc.T("Store source", "商店来源", "商店來源") : Loc.T("Source unidentified (pending)", "未识别到来源（待定）", "未識別到來源（待定）");
        if (!local && mod.SourceConfirmed) text += "\n" + ThunderstoreUpdateService.HomeUrl(mod.SourceNamespace, mod.SourceName);
        string github = GitHubUrl(mod);
        if (github.Length > 0) text += "\nGitHub: " + github;
        return text;
    }
}
