from pathlib import Path
def edit(f,a,b):
 p=Path(f);s=p.read_text(encoding='utf-8');assert s.count(a)==1,(f,a[:60],s.count(a));p.write_text(s.replace(a,b),encoding='utf-8')
edit('src/Overview/SourceUi.cs','InfrastructureOverviewModel package)\n    {','InfrastructureOverviewModel package, RectTransform anchor)\n    {')
edit('src/Overview/SourceUi.cs','        OverviewUi.Choose(package.Name, options, "", action => {','        void ExecuteComponent(string action) {')
edit('src/Overview/SourceUi.cs','        }, action => action == "uninstall" ? (reason.Length == 0 ? UiActionRole.Danger : UiActionRole.Neutral) : UiActionRole.Neutral);','''        }
        var entries = options.Select(option => new UiMenuEntry {
            Label = option.Item1, Enabled = option.Item2 != "uninstall" || reason.Length == 0,
            Role = option.Item2 == "uninstall" ? UiActionRole.Danger : UiActionRole.Neutral,
            CanInvoke = () => option.Item2 != "uninstall" || UninstallReason(package.Key).Length == 0,
            Invoke = () => ExecuteComponent(option.Item2)
        }).ToList();
        // A readable explanation survives the disabled action and can be opened in full.
        if (reason.Length > 0) entries.Add(new UiMenuEntry {
            Label = Loc.T("Why is uninstall unavailable?", "为什么不能卸载？", "為什麼不能解除安裝？"),
            Invoke = () => OverviewUi.ShowText(package.Name, UninstallReason(package.Key)) });
        OverviewUi.Dropdown(anchor, package.Name, entries, 480);''')
edit('src/Overview/OverviewListView.Infrastructure.cs','void Menu() => SourceUi.ComponentMenu(_repo, _mods, package);','void Menu() => SourceUi.ComponentMenu(_repo, _mods, package, row);')
# Update narrowly changed wiring assertions.
for f in ['验证/ComponentActions20261028/概览展示/Program.cs','验证/InfrastructureTree20261029/UI/Program.cs']:
 p=Path(f);s=p.read_text(encoding='utf-8');s=s.replace('SourceUi.ComponentMenu(_repo, _mods, package)', 'SourceUi.ComponentMenu(_repo, _mods, package, row)');p.write_text(s,encoding='utf-8')
