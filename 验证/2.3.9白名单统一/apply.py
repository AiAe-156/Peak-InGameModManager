"""2.3.9: one protected whitelist (user decision 2026-11-10).

MCE itself plus the three libraries it runs on (ModConfig, PEAKLib.Core, PEAKLib.UI) cannot be enabled/disabled or
uninstalled anywhere. Every other mod and base component may be. Uninstall always asks first; enable/disable does
not; base components get one extra reminder. Before this, toggling used a substring list and uninstalling the same
libraries was only a confirmation (2.2.8), so the two paths disagreed.
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, 'src', rel)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if s.count(a) != 1: sys.exit(f'{rel}: expected 1 match, got {s.count(a)}: {a[:90]}')
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', rel, len(pairs))

edit('Updating/ComponentDependencyGraph.cs', [(
'''    internal static IReadOnlyList<string> MceRequiredBy(Component component)''',
'''    /// <summary>2.3.9 (user decision): the protected whitelist — MCE plus <see cref="MceRequiredIdentities"/>. These
    /// cannot be enabled/disabled or uninstalled from MCE; everything else can.</summary>
    public static bool IsProtected(string guid) => !string.IsNullOrEmpty(guid)
        && (Compare.Equals(guid, MceGuid) || MceRequiredIdentities.Contains(guid, Compare));
    internal static IReadOnlyList<string> MceRequiredBy(Component component)''')])

edit('Overview/ModToggleService.cs', [(
'''            if (record.Guid.Equals(Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase) || record.Guid.Equals(ModConfigSupport.Guid, StringComparison.OrdinalIgnoreCase) || record.Guid.IndexOf("peaklib.core", StringComparison.OrdinalIgnoreCase) >= 0 || record.Guid.IndexOf("peaklib.ui", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException(Loc.F("The overview and its required libraries cannot be toggled here: {0}", "概览入口及其必要基础库不能由此页面启停：{0}", "概覽入口及其必要基礎庫不能由此頁面啟停：{0}", record.Guid));''',
'''            if (ModConfigEnhance.Updating.ComponentDependencyGraph.IsProtected(record.Guid))
                throw new InvalidOperationException(Loc.F("MCE and the libraries it runs on cannot be disabled or uninstalled: {0}", "MCE 及其运行所需的基础库不能停用或卸载：{0}", "MCE 及其執行所需的基礎庫不能停用或解除安裝：{0}", record.Guid));''')])

edit('Overview/OverviewCatalogue.cs', [(
'''        bool infrastructure = guid.Equals(Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase) || guid.IndexOf("peaklib.core", StringComparison.OrdinalIgnoreCase) >= 0
            || guid.IndexOf("peaklib.ui", StringComparison.OrdinalIgnoreCase) >= 0 || guid.IndexOf("peaklib.modconfig", StringComparison.OrdinalIgnoreCase) >= 0;''',
'''        // 2.3.9: the one protected whitelist (MCE + ModConfig + PEAKLib.Core + PEAKLib.UI), matched exactly.
        bool infrastructure = ModConfigEnhance.Updating.ComponentDependencyGraph.IsProtected(guid);''')])

# Uninstall: protected components are no longer "confirm and continue".
edit('Overview/DiagnosticSummary.cs', [
 ('''        Status.RequiredByMce => Loc.T("Required by MCE; uninstall asks for confirmation", "此组件是MCE运行所依赖的组件，卸载前会再次确认", "此組件是MCE執行所依賴的組件，解除安裝前會再次確認"),''',
  '''        Status.RequiredByMce => Loc.T("MCE runs on this component; it cannot be disabled or uninstalled", "MCE 运行依赖此组件，不能停用或卸载", "MCE 執行依賴此組件，不能停用或解除安裝"),'''),
 ('''        // Protection is a warning, not a veto: removing something MCE runs on is the user's call, made once in a
        // confirmation rather than blocked outright.
        return new DiagnosticSummary(state, capability.CanUninstall || capability.ProtectionPath.Count > 0, capability.CanUpdate, capability.Reason''',
  '''        // 2.3.9 (user decision): the protected whitelist is a hard stop for uninstall, as it is for enable/disable.
        return new DiagnosticSummary(state, capability.CanUninstall && capability.ProtectionPath.Count == 0, capability.CanUpdate, capability.Reason'''),
])

edit('Overview/ComponentToggle.cs', [(
'''            return Loc.T("MCE runs on this component; it cannot be disabled here.", "MCE 运行依赖此组件，不能在这里停用。", "MCE 執行依賴此組件，不能在這裡停用。");''',
'''            return Loc.T("MCE runs on this component; it cannot be disabled or uninstalled.", "MCE 运行依赖此组件，不能停用或卸载。", "MCE 執行依賴此組件，不能停用或解除安裝。");''')])

# Component uninstall: one reminder for every base component; protected ones are disabled with the reason above.
edit('Overview/ComponentDetailsView.cs', [(
'''                    if (!identified) { Render(); return; }
                    string protection = SourceUi.UninstallProtection(key);
                    if (protection.Length > 0)
                    {
                        OverviewUi.ConfirmText(Loc.T("Uninstall a component MCE runs on", "卸载 MCE 运行所依赖的组件", "解除安裝 MCE 執行所依賴的組件"),
                            protection + "\\n\\n" + Loc.T("The preview still verifies file ownership and dependencies first; nothing is removed before you confirm it.",
                                "接下来仍会先核对文件归属与依赖，确认预览内容前不会删除任何文件。",
                                "接下來仍會先核對檔案歸屬與依賴，確認預覽內容前不會刪除任何檔案。"),
                            () => ShopWindow.Open(repo, mods, false, key, true));
                        return;
                    }
                    if (SourceUi.UninstallReason(key).Length > 0) { Render(); return; }
                    ShopWindow.Open(repo, mods, false, key, true);''',
'''                    if (!identified || SourceUi.UninstallReason(key).Length > 0) { Render(); return; }
                    // 2.3.9 (user): base components get one extra reminder — other mods often share them.
                    OverviewUi.ConfirmText(Loc.T("Uninstall a base component", "卸载基础组件", "解除安裝基礎組件"),
                        (owner?.Name ?? key) + "\\n\\n" + Loc.T("Base components are shared libraries; other mods may stop working without it. The preview still checks file ownership and dependencies, and nothing is removed until you confirm it.",
                            "基础组件是共用的运行库，其他模组可能因此无法工作。接下来仍会先核对文件归属与依赖，确认预览内容前不会删除任何文件。",
                            "基礎組件是共用的執行庫，其他模組可能因此無法運作。接下來仍會先核對檔案歸屬與依賴，確認預覽內容前不會刪除任何檔案。"),
                        () => ShopWindow.Open(repo, mods, false, key, true), UiActionRole.Danger);''')])

# The confirmation-only helper has no callers left.
edit('Overview/SourceUi.cs', [(
'''    /// <summary>The warning shown when the target is something MCE itself runs on. It no longer blocks the
    /// uninstall; the caller confirms once with this text before opening the preview.</summary>
    internal static string UninstallProtection(string key)
    {
        var diagnostic = DependencyDiagnostic(key);
        return diagnostic.State == DiagnosticSummary.Status.RequiredByMce ? diagnostic.Summary : "";
    }
''', '')])
print('ok')
