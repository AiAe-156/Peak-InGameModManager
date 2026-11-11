from pathlib import Path
root=Path(__file__).resolve().parents[3]
def edit(f,a,b):
 p=root/f;s=p.read_text(encoding='utf-8-sig');assert s.count(a)==1,(f,a[:80],s.count(a));p.write_text(s.replace(a,b),encoding='utf-8')
edit('src/Overview/InfrastructurePackages.cs','internal string Team="",Name="",Version="",Path="",Area="",Latest="";','internal string Team="",Name="",Version="",Path="",Area="",Latest="",Author="",Description="";')
edit('src/Overview/InfrastructurePackages.cs','string[] dependencies=Array.Empty<string>();','string[] dependencies=Array.Empty<string>();\n                string author=item.Key.Substring(0,split), description="";')
edit('src/Overview/InfrastructurePackages.cs','try { dependencies=(JObject.Parse(File.ReadAllText(file))["dependencies"] as JArray)?.Values<string>().Where(d=>!string.IsNullOrWhiteSpace(d)).Select(d=>d!).ToArray()??Array.Empty<string>(); }','try { var manifest=JObject.Parse(File.ReadAllText(file)); dependencies=(manifest["dependencies"] as JArray)?.Values<string>().Where(d=>!string.IsNullOrWhiteSpace(d)).Select(d=>d!).ToArray()??Array.Empty<string>(); author=(string?)manifest["author"]??author; description=(string?)manifest["description"]??""; }')
edit('src/Overview/InfrastructurePackages.cs','Area=area,Dependencies=dependencies','Area=area,Dependencies=dependencies,Author=author,Description=description')
edit('src/Overview/InfrastructureOverviewModel.cs','internal string Area { get; }','internal string Area { get; }\n    internal string Author { get; }\n    internal string Description { get; }\n    internal static string GroupTitle => Loc.T("Core/Patcher", "基础组件(Core/Patcher)", "基礎組件(Core/Patcher)");\n    internal string TypeTitle => Area=="core" ? Loc.T("Core", "核心组件(Core)", "核心組件(Core)") : Loc.T("Patcher", "启动补丁(Patcher)", "啟動補丁(Patcher)");')
edit('src/Overview/InfrastructureOverviewModel.cs','Path=package.Path; Dependencies=','Author=package.Author; Description=package.Description;\n        Path=package.Path; Dependencies=')
edit('src/Overview/InfrastructureOverviewModel.cs','=> graph.ReverseTree(Key);','=> graph.ReverseTree(Key);\n    internal IReadOnlyList<ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry> Consumers(ModConfigEnhance.Updating.ComponentDependencyGraph graph, int depth, int limit)\n        => graph.ReverseTree(Key, depth, limit);\n    internal static string TreeCaption(ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry entry)\n        => entry.Id + (entry.Cycle ? " [cycle]" : "") + (entry.Shared ? " [shared]" : "")\n            + (entry.Missing ? " [unknown]" : "") + (entry.Truncated ? " […]" : "") + " · " + entry.Evidence;')
edit('src/Overview/InfrastructureOverviewModel.cs','MatchesText(query,Key,Name,Area,Version,Path,','MatchesText(query,Key,Name,Area,Version,Path,Author,Description,')
edit('src/Overview/OverviewListView.cs','float y = 0; string? currentGroup = null; int index = 0;','float y = BuildInfrastructure(0, width, xEnable, goalWidth, xName, nameWidth, xSettings, xHome, xFolder, xVersion, versionWidth, xAuthor, authorWidth, xDescription, compact); string? currentGroup = null; int index = 0;')
edit('src/Overview/OverviewListView.cs','            y = BuildInfrastructure(y, width);\n','')
edit('src/Overview/OverviewListView.cs','            RefreshInfrastructure(true);\n            _mods.Clear(); _mods.AddRange(new OverviewCatalogue().Scan(_repo)); _dependencyHints.Reload();','            _mods.Clear(); _mods.AddRange(new OverviewCatalogue().Scan(_repo)); _dependencyHints.Reload();\n            RefreshInfrastructure(true);')
p=root/'src/Overview/OverviewListView.Infrastructure.cs'
s=p.read_text();start=s.index('    private void RefreshInfrastructure(');end=s.index('    private void FlushInfrastructureRedraw()',start)
s=s[:start]+'''    private ModConfigEnhance.Updating.ComponentDependencyGraph? _infrastructureGraph;
    private readonly Dictionary<string, int> _consumerDepth = new(StringComparer.OrdinalIgnoreCase);
    private int _infrastructureGeneration;
    private string _infrastructureContext = "";
    private void RefreshInfrastructure(bool force = false)
    {
        int revision = OverviewPageCache.Inventory;
        string context = SourceUi.DependencyContext;
        if (!force && revision == _infrastructureRevision && context == _infrastructureContext) return;
        _infrastructureRevision = revision; _infrastructureContext = context;
        int generation = ++_infrastructureGeneration;
        _infrastructureLoading = true; _infrastructureGraph = null;
        _infrastructure = Array.Empty<InfrastructureOverviewModel>();
        // Capture confirmed real file mappings before leaving the main thread.
        string root = BepInEx.Paths.BepInExRootPath;
        var graphconfirmed = _mods.Where(m => m.SourceConfirmed && m.IdentityConflict.Length == 0)
            .SelectMany(m => (m.AssemblyPaths.Count > 0 ? m.AssemblyPaths : new List<string> { m.Location })
                .Select(path => new ModConfigEnhance.Updating.ComponentDependencyCollector.ConfirmedFile { PackageKey = m.SourceKey, Path = path })).ToArray();
        _ = Task.Run(() => (Rows: InfrastructureOverviewModel.Create(InfrastructurePackages.Scan(root)),
            Graph: InfrastructureDependencyCollector.Collect(root, graphconfirmed))).ContinueWith(task => {
            _pump.Enqueue(() => {
                if (_disposed || generation != _infrastructureGeneration) return;
                if (revision != OverviewPageCache.Inventory || context != SourceUi.DependencyContext) { RefreshInfrastructure(true); return; }
                _infrastructureLoading = false;
                if (task.IsFaulted || task.IsCanceled) _infrastructureError = task.Exception?.GetBaseException().Message ?? "Component scan cancelled";
                else { _infrastructure = task.Result.Rows; _infrastructureGraph = task.Result.Graph; _infrastructureError = "";
                    SourceUi.PublishDependencyGraph(task.Result.Graph, context, revision); }
                _infrastructureRedraw = true;
            });
        }, TaskScheduler.Default);
    }
''' +s[end:]
s=s.replace('_infrastructureLoading, _infrastructureAgain, _infrastructureRedraw','_infrastructureLoading, _infrastructureRedraw')
start=s.index('    private float BuildInfrastructure(')
s=s[:start]+'''    private float BuildInfrastructure(float y, float width, float xEnable, float goalWidth, float xName, float nameWidth,
        float xSettings, float xHome, float xFolder, float xVersion, float versionWidth, float xAuthor, float authorWidth, float xDescription, bool compact)
    {
        _infrastructureRedraw = false;
        var rows = _infrastructure.Where(p => p.Matches(_searchQuery)).ToArray();
        y += 2;
        var header = OverviewUi.Panel("InfrastructureGroup", _content, new Color(.12f,.18f,.21f,.95f));
        OverviewUi.Place(header, 0, y, width - 15, GroupHeight);
        bool collapsed = _infrastructureCollapsed && _searchQuery.Length == 0;
        var arrow = OverviewUi.IconButton(header, collapsed ? "triangle-right" : "triangle-down", () => {
            _infrastructureCollapsed = !_infrastructureCollapsed; Render();
        }, 22, 26);
        OverviewUi.Place((RectTransform)arrow.transform, 7, 4, 22, 26);
        var title = Label(header, InfrastructureOverviewModel.GroupTitle + " · " + rows.Length, 35, 0, width - 65, GroupHeight, 21);
        AddHint(title.gameObject, Loc.T("Fixed group, outside profiles and exports. Expand a name to see mods that depend on it.",
            "固定分组，不进入方案或导出。展开名称可查看依赖此组件的模组。", "固定分組，不進入方案或匯出。展開名稱可查看依賴此組件的模組。"));
        y += GroupHeight + GroupGapAfter;
        if (collapsed) return y;
        int index = 0;
        foreach (var package in rows)
        {
            int depth = _consumerDepth.TryGetValue(package.Identity, out int saved) ? saved : 0;
            var tree = _infrastructureGraph == null || depth == 0 ? Array.Empty<ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry>()
                : package.Consumers(_infrastructureGraph, depth, 24).Skip(1).ToArray();
            bool more = tree.Any(t => t.Truncated) && depth < 16;
            float height = RowHeight + tree.Length * 24 + (depth > 0 && (more || tree.Length == 0) ? 24 : 0);
            var row = OverviewUi.Panel("Infrastructure_" + index, _content, new Color(.1f,.11f,.13f,index++ % 2 == 0 ? .5f : .28f));
            OverviewUi.Place(row, 0, y, width - 15, height - 2);
            // Deliberately no checkbox, editable frame, drag target or OverviewMod identity.
            var placeholder = Label(row, "—", xEnable, 4, goalWidth, 32, 20); placeholder.alignment = TextAlignmentOptions.Midline;
            AddHint(placeholder.gameObject, Loc.T("Component enablement is not a profile mod target.", "组件启停不作为方案模组目标。", "組件啟停不作為方案模組目標。"));
            var settings = OverviewUi.IconButton(row, "gear", () => { });
            OverviewUi.Place((RectTransform)settings.transform, xSettings + 2, 5, 30, 30);
            settings.interactable = false; Tint(settings, OverviewUi.Muted);
            AddHint(settings.gameObject, Loc.T("No ModConfig settings.", "没有 ModConfig 设置项。", "沒有 ModConfig 設定項。"));
            void Menu() => SourceUi.ComponentMenu(_repo, _mods, package);
            var home = OverviewUi.IconButton(row, "home", Menu);
            OverviewUi.Place((RectTransform)home.transform, xHome + 2, 5, 30, 30); Tint(home, OverviewUi.Blue);
            string reason = _infrastructureGraph == null ? Loc.T("Reading dependency protection…", "正在读取依赖保护……", "正在讀取依賴保護……") : package.Capability(_infrastructureGraph).Reason;
            string hint = package.Key + "\n" + package.TypeTitle + " · " + package.Version + "\n" + package.Path
                + "\n" + (reason.Length > 0 ? Loc.T("Uninstall unavailable: ", "无法卸载：", "無法解除安裝：") + reason : Loc.T("Source menu: uninstall preview / version operations", "来源菜单：卸载预览／版本操作", "來源選單：解除安裝預覽／版本操作"));
            AddHint(home.gameObject, EscapeRich(hint));
            var folder = OverviewUi.IconButton(row, "folder", () => OpenFolder(package.Path));
            OverviewUi.Place((RectTransform)folder.transform, xFolder + 2, 5, 30, 30); AddHint(folder.gameObject, EscapeRich(package.Path));
            var expand = OverviewUi.IconButton(row, depth == 0 ? "triangle-right" : "triangle-down", () => {
                _consumerDepth[package.Identity] = depth == 0 ? 1 : 0; Render();
            }, 18, 24);
            OverviewUi.Place((RectTransform)expand.transform, xName, 8, 18, 24);
            var name = Label(row, EscapeRich(package.Name), xName + 22, 4, Mathf.Max(20, nameWidth - 26), 34, 19);
            OverviewUi.PlaceClipped(name, xName + 22, 4, Mathf.Max(20, nameWidth - 26), 34);
            AddHint(name.gameObject, EscapeRich(hint)); name.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => Menu();
            var version = Label(row, EscapeRich(package.Version), xVersion, 4, versionWidth - 7, 34, 20);
            AddHint(version.gameObject, EscapeRich(hint)); version.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => Menu();
            var author = Label(row, EscapeRich(package.Author), xAuthor, 4, Mathf.Max(1, authorWidth - 8), 34, 18); author.gameObject.SetActive(!compact); AddHint(author.gameObject, Esc
apeRich(package.Author));
            string description = package.TypeTitle + (package.Description.Length > 0 ? " · " + package.Description : "");
            var desc = Label(row, EscapeRich(description), xDescription, 4, Mathf.Max(40, width - xDescription - 28), 34, 16); desc.color = OverviewUi.Muted; desc.gameObject.SetActive(!compact); AddHint(desc.gameObject, EscapeRich(description));
            float ty = RowHeight;
            foreach (var entry in tree)
            {
                float indent = Mathf.Min(entry.Depth, 8) * 12;
                string caption = "├─ " + InfrastructureOverviewModel.TreeCaption(entry);
                var node = Label(row, EscapeRich(caption), xName + indent, ty, Mathf.Max(20, nameWidth - indent - 4), 24, 16);
                OverviewUi.PlaceClipped(node, xName + indent, ty, Mathf.Max(20, nameWidth - indent - 4), 24);
                node.color = OverviewUi.Muted; AddHint(node.gameObject, EscapeRich(caption)); ty += 24;
            }
            if (depth > 0 && (more || tree.Length == 0))
            {
                string caption = more ? Loc.T("More levels… (24-row limit)", "展开更多层级…（上限24行）", "展開更多層級…（上限24行）")
                    : Loc.T("No known consumers", "没有已知反向依赖", "沒有已知反向依賴");
                var text = Label(row, caption, xName + 12, ty, nameWidth - 16, 24, 16); text.color = OverviewUi.Muted;
                if (more) text.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => { _consumerDepth[package.Identity] = Math.Min(16, depth + 1); Render(); };
            }
            y += height;
        }
        if (rows.Length == 0)
        {
            string message = _infrastructureLoading ? Loc.T("Reading components…", "正在读取组件……", "正在讀取組件……")
                : _infrastructureError.Length > 0 ? Loc.T("Could not read components: ", "组件读取失败：", "組件讀取失敗：") + _infrastructureError
                : Loc.T("No matching components with a verified manifest.", "没有匹配且清单身份可核实的组件。", "沒有符合且清單身分可核實的組件。");
            var empty = Label(_content, message, 12, y, width - 40, 36, 17); AddHint(empty.gameObject, EscapeRich(message)); y += 38;
        }
        return y;
    }
}
'''
s=s.replace('Esc\napeRich','EscapeRich')
p.write_text(s,encoding='utf-8')
