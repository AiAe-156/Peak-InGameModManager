#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewListView
{
    private IReadOnlyList<InfrastructureOverviewModel> _infrastructure = Array.Empty<InfrastructureOverviewModel>();
    private bool _infrastructureCollapsed, _infrastructureLoading, _infrastructureRedraw;
    private string _infrastructureError = "", _searchQuery = "";
    private int _infrastructureRevision = -1;

    private ModConfigEnhance.Updating.ComponentDependencyGraph? _infrastructureGraph;
    private readonly List<Action> _componentProtectionRefresh = new();
    private int _infrastructureGeneration;
    private string _infrastructureContext = "";
    private void RefreshInfrastructure(bool force = false)
    {
        if (!force && !ListVisible) return;
        int revision = OverviewPageCache.Inventory;
        SourceUi.PollDisplayContext(force);
        string context = SourceUi.DisplayDependencyContext;
        if (!force && revision == _infrastructureRevision && context == _infrastructureContext) return;
        _infrastructureRevision = revision; _infrastructureContext = context;
        int generation = ++_infrastructureGeneration;
        _infrastructureLoading = true; _infrastructureRedraw = false; _infrastructureGraph = null;
        SourceUi.InvalidateDependencyGraph();
        foreach(var refresh in _componentProtectionRefresh)refresh();
        // Keep the displayed snapshot while scanning; publish both sections in one Render.
        // Capture confirmed real file mappings before leaving the main thread.
        string root = BepInEx.Paths.BepInExRootPath;
        var graphconfirmed = _mods.Where(m => m.SourceConfirmed && m.IdentityConflict.Length == 0)
            .SelectMany(m => (m.AssemblyPaths.Count > 0 ? m.AssemblyPaths : new List<string> { m.Location })
                .Select(path => new ModConfigEnhance.Updating.ComponentDependencyCollector.ConfirmedFile { PackageKey = m.SourceKey, Path = path })).ToArray();
        _ = Task.Run(() => {
            var rows = InfrastructureOverviewModel.Create(InfrastructurePackages.Scan(root));
            _pump.Enqueue(() => {
                if (_disposed || generation != _infrastructureGeneration) return;
                if (revision != OverviewPageCache.Inventory || context != SourceUi.DependencyContext) { RefreshInfrastructure(true); return; }
                _infrastructure = rows; _infrastructureLoading = false; _infrastructureError = "";
                _infrastructureRedraw = true;
                _status(Loc.T("Reading dependency graph…", "正在读取依赖关系……", "正在讀取相依關係……"));
            });
            return InfrastructureDependencyCollector.Collect(root, graphconfirmed);
        }).ContinueWith(task => {
            _pump.Enqueue(() => {
                if (_disposed || generation != _infrastructureGeneration) return;
                if (revision != OverviewPageCache.Inventory || context != SourceUi.DependencyContext) { RefreshInfrastructure(true); return; }
                // Metadata owns the one structural publication. Graph completion never rebuilds rows.
                bool metadataFailed = _infrastructureLoading;
                _infrastructureLoading = false;
                if (task.IsFaulted || task.IsCanceled)
                {
                    _infrastructureError = task.Exception?.GetBaseException().Message ?? "Component scan cancelled";
                    if(metadataFailed) _infrastructureRedraw = true;
                }
                else {
                    _infrastructureGraph = task.Result; _infrastructureError = "";
                    SourceUi.PublishDependencyGraph(task.Result, context, revision, _infrastructure);
                }
                _dependencyHints.HideNow(); _dependencyHints.InvalidateDisplay();
                foreach(var refresh in _componentProtectionRefresh)refresh();
                _status(_infrastructureError.Length == 0 ? "" : Loc.T("Could not read components: ", "组件读取失败：", "組件讀取失敗：") + _infrastructureError);
            });
        }, TaskScheduler.Default);
    }

    internal int ComponentFilterCount(string tag) => _infrastructure.Count(p => p.MatchesStatus(tag));

    private void FlushInfrastructureRedraw()
    {
        if (!_infrastructureRedraw || _infrastructureLoading || !_opened || _building || IsEditingText || _dragged != null || _scroll.velocity.sqrMagnitude > 1) return;
        // Render captures/restores row focus; a selected row must not starve publication.
        Render(); // Only an accepted Render consumes the pending publication.
    }
    private void BindDependencyCard(GameObject target, InfrastructureOverviewModel component)
    {
        _dependencyHints.Bind(target, component, () => _infrastructureGraph, () => _infrastructure,
            () => _hint.gameObject.SetActive(false));
    }
    internal static string ComponentRuleId(InfrastructureOverviewModel package) => "component:" + package.Key;
    private float BuildInfrastructure(float y, float width, float xEnable, float goalWidth, float xName, float nameWidth,
        float xSettings, float xHome, float xFolder, float xVersion, float versionWidth, float xAuthor, float authorWidth, float xDescription, bool compact)
    {
        _infrastructureRedraw = false;
        _componentProtectionRefresh.Clear();
        var rows = _infrastructure.Where(p => p.Matches(_searchQuery) && (_relatedMods == null || _relatedPackages.Contains(p.Key))
            && Tags.include.All(p.MatchesStatus) && !Tags.exclude.Any(p.MatchesStatus)).ToArray();
        if (_relatedMods != null && rows.Length == 0) return y;
        y += 2;
        bool collapsed = _infrastructureCollapsed && _searchQuery.Length == 0 && _relatedMods == null && Tags.Count == 0;
        AddVirtualRow("infrastructure-group:", "InfrastructureGroup", y, GroupHeight, width-15, header => {
        header.GetComponent<Image>().color=ModConfigLook.Surface(new Color(.12f,.18f,.21f,.95f));
        var arrow = OverviewUi.IconButton(header, collapsed ? "triangle-right" : "triangle-down", () => {
            _infrastructureCollapsed = !_infrastructureCollapsed; RenderFold();
        }, 22, 26);
        OverviewUi.Place((RectTransform)arrow.transform, 7, 4, 22, 26);
        var title = Label(header, InfrastructureOverviewModel.GroupTitle + " · " + Loc.F("Components: {0} · Locations: {1}", "组件 {0} 个 · 安装位置 {1} 处", "組件 {0} 個 · 安裝位置 {1} 處", rows.Length, rows.Sum(p => p.LocationCount)), 35, 0, width - 65, GroupHeight, 21);
        AddHint(title.gameObject, Loc.T("Packages are grouped across Core and Patcher. Hover for a summary; click a name for the full tree.",
            "同一资源包合并显示 Core 和 Patcher 位置。悬停看摘要，点击名称看完整依赖树。", "同一資源包合併顯示 Core 和 Patcher 位置。懸停看摘要，點擊名稱看完整依賴樹。"));
        });
        y += GroupHeight + GroupGapAfter;
        if (collapsed) return y;
        int index = 0;
        foreach (var package in rows)
        {
            const float height = RowHeight;
            int capturedIndex=index++;
            System.Collections.IEnumerator BuildComponentLayers(RectTransform row)
            {
            // Deliberately no checkbox, editable frame, drag target or OverviewMod identity.
            // 2.3.4: the column shows the component's actual state; switching happens in its details (it renames files at
            // once, outside the profile's save, because components are not profile targets).
            var placeholder = Label(row, package.Enabled ? "✓" : "○", xEnable, 4, goalWidth, 32, 20); placeholder.alignment = TextAlignmentOptions.Midline;
            placeholder.color = package.Enabled ? OverviewUi.Green : OverviewUi.Muted;
            AddHint(placeholder.gameObject, (package.Enabled ? Loc.T("Enabled", "已启用", "已啟用") : Loc.T("Disabled", "已停用", "已停用"))
                + " · " + Loc.T("Click the name to enable, disable or rename it. Components are not part of profiles.", "点名称可启停或改译名；组件不属于方案。", "點名稱可啟停或改譯名；組件不屬於方案。"));
            void Menu() => SourceUi.ComponentMenu(_repo, _mods, package, _infrastructure, () => { RefreshInfrastructure(true); _pump.Enqueue(Render); });
            var locked = Label(row, "⊘", xName + nameWidth - 20, 4, 18, 34, 16); locked.color = OverviewUi.Muted;
            string protectionHint = "";
            void RefreshProtection() {
                if(locked==null)return;
                var diagnostic=SourceUi.DependencyDiagnostic(package.Key);
                protectionHint=package.IdentityConfirmed ? diagnostic.Summary : Loc.T("Identity unconfirmed", "身份未确认", "身分未確認");
                locked.gameObject.SetActive(!package.IdentityConfirmed || !diagnostic.CanUninstall);

            }
            _componentProtectionRefresh.Add(RefreshProtection);RefreshProtection();
            var lockHover=locked.gameObject.AddComponent<OverviewHover>();
            lockHover.Enter=()=>ShowExternalHint(protectionHint);lockHover.Exit=HideExternalHint;
            var lockFocus=locked.gameObject.AddComponent<OverviewHotkeyFocus>();
            lockFocus.Selected=()=>ShowHint(protectionHint);lockFocus.Deselected=HideExternalHint;
            string latest = package.LatestVersion;
            bool hasUpdate = package.HasUpdate;
            float titleWidth = Mathf.Max(20, nameWidth - 24 - (hasUpdate ? StatusChip.Reserve(1) : 0));
            // 2.3.4: a component can carry a display name of its own (overview alias, same rule store as mods, keyed
            // "component:<Team-Name>"); the package name stays visible in grey like a mod's original name.
            string alias = _repo.GetRule(ComponentRuleId(package)).alias;
            string shown = string.IsNullOrWhiteSpace(alias) ? EscapeRich(package.Name)
                : EscapeRich(alias) + " <size=70%><color=#777E78>（" + EscapeRich(package.Name) + "）</color></size>";
            var name = Label(row, shown, xName, 4, titleWidth, 34, 19); name.richText = true;
            OverviewUi.PlaceClipped(name, xName, 4, titleWidth, 34);
            BindDependencyCard(name.gameObject, package); name.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => Menu();
            if (hasUpdate) {
                var markers = OverviewUi.Rect("ComponentUpdateMarkers", row);
                OverviewUi.Place(markers, xName, 0, titleWidth + StatusChip.Reserve(1), 32);
                AttachAvailableUpdateBadge(markers, package.Version, latest,
                    () => SourceUi.ComponentSourceMenu(_repo, _mods, package, _infrastructure));
            }
            var version = Label(row, EscapeRich(package.Version.Length > 0 ? package.Version : Loc.T("Unknown version", "版本未知", "版本未知")), xVersion, 4,
                versionWidth - 7 - (hasUpdate ? AvailableUpdateDot.VersionReserve : 0), 34, 20);
            string versionHint=EscapeRich(package.TypeTitle+" · "+package.Version)
                +(hasUpdate ? "\n"+AvailableUpdateDot.Hint(package.Version,latest) : "")
                // 2.4.4: say why no update is offered when the version read here disagrees with a finished update,
                // and list ignored lower-version manifest.json copies.
                +(package.VersionContradicted ? "\n"+Loc.T("Version information disagrees: an update to the newest version already finished, but a lower manifest.json is still read here.",
                    "版本信息不一致：已更新到最新版，但这里读到的仍是一份较低版本的 manifest.json。",
                    "版本資訊不一致：已更新到最新版，但這裡讀到的仍是一份較低版本的 manifest.json。") : "")
                +(package.StaleManifests.Count > 0 ? "\n"+Loc.T("Old manifest.json copies (ignored for the version):", "旧说明文件残留（不参与版本判断）：", "舊說明檔案殘留（不參與版本判斷）：")
                    +"\n"+string.Join("\n", package.StaleManifests.Select(EscapeRich)) : "");
            AvailableUpdateDot.BindVersion(version.gameObject,versionHint,Menu,AddHint,ShowExternalHint,HideExternalHint,_scroll);
            version.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => Menu();
            if(hasUpdate)AvailableUpdateDot.Version(row,xVersion,4,versionWidth-7,versionHint,Menu,AddHint,ShowExternalHint,HideExternalHint,_scroll);
            var author = Label(row, EscapeRich(package.Author), xAuthor, 4, Mathf.Max(1, authorWidth - 8), 34, 18); author.gameObject.SetActive(!compact); AddHint(author.gameObject, EscapeRich(package.Author));
            string description = package.TypeTitle + (!package.IdentityConfirmed ? " · " + protectionHint
                : package.Description.Length > 0 ? " · " + InfrastructureOverviewModel.ShortText(package.Description) : "");
            var desc = Label(row, EscapeRich(description), xDescription, 4, Mathf.Max(40, width - xDescription - 28), 34, 16); desc.color = OverviewUi.Muted; desc.gameObject.SetActive(!compact); AddHint(desc.gameObject, EscapeRich(description));
            yield return null;
            OverviewRowActions.Settings(row, xSettings + 2, (row.rect.height - OverviewRowActions.Size) / 2, () => { }, false,
                Loc.T("No ModConfig settings.", "没有 ModConfig 设置项。", "沒有 ModConfig 設定項。"), AddHint);
            yield return null;
            OverviewRowActions.Home(row, xHome + 2, (row.rect.height - OverviewRowActions.Size) / 2,
                () => SourceUi.ComponentSourceMenu(_repo, _mods, package, _infrastructure), true,
                Loc.T("Pages and source", "页面与来源", "頁面與來源"), AddHint, package.IdentityConfirmed && ModSources.Verified(package.Key.Split('-')[0], package.Name));
            yield return null;
            Button? folder = null;
            folder = OverviewRowActions.Folder(row, xFolder + 2, (row.rect.height - OverviewRowActions.Size) / 2, () => {
                if (folder != null) SourceUi.ComponentFolders(package, (RectTransform)folder.transform, _status);
            }, package.LocationCount > 0, EscapeRich(string.Join("\n", package.Locations.Select(l => l.Area + " · " + l.Path))), AddHint);
            }
            AddLayeredRow("component:"+package.Key,"Infrastructure_"+capturedIndex,y,height-2,width-15,BuildComponentLayers);
            y += height;
        }
        if (rows.Length == 0)
        {
            string message = _infrastructureLoading ? Loc.T("Reading components…", "正在读取组件……", "正在讀取組件……")
                : _infrastructureError.Length > 0 ? Loc.T("Could not read components: ", "组件读取失败：", "組件讀取失敗：") + _infrastructureError
                : Loc.T("No matching components.", "没有匹配的组件。", "沒有符合的組件。");
            var empty = Label(_content, message, 12, y, width - 40, 36, 17); AddHint(empty.gameObject, EscapeRich(message)); y += 38;
        }
        return y;
    }
}
