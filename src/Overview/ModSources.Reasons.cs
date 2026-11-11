#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal static partial class ModSources
{
    internal static string UnresolvedReason(OverviewMod mod, IEnumerable<StorePackage> catalog)
    {
        if (ExplicitLocal(mod)) return Loc.T("Personal local choice", "个人已标记本地", "個人已標記本機");
        if (mod.IdentityConflict.Length > 0) return Loc.T("Conflicting installation identity; review manually", "安装身份存在冲突，请手动核对", "安裝身分衝突，請手動核對");
        string name = StoreIdentityIndex.Normalize(SearchName(mod));
        var matches = catalog.Where(p => StoreIdentityIndex.Normalize(p.Name) == name).ToArray();
        if (matches.Length == 0) return Loc.T("No exact original package in the current catalog; similar results require review", "当前商店目录没有同原包名项，相似候选须核对", "目前商店目錄無同原包名項，相似候選須核對");
        if (matches.Any(p => p.Deprecated)) return Loc.T("Original package is deprecated; review publisher and source manually", "原包存在但已弃用，请手动核对发布者与来源", "原包存在但已棄用，請手動核對發佈者與來源");
        if (matches.Length > 1) return Loc.T("Multiple publishers use this name; select manually", "同名有多个发布者，请手动选择", "同名多個發佈者，請手動選擇");
        return Loc.T("Name found; author, repository or saved identity needs review", "已找到同名项，作者、仓库或旧来源线索需核对", "已找到同名項，作者、倉庫或舊來源線索待核對");
    }
}
