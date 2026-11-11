import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Overview")


def edit(name, pairs):
    path = os.path.join(ROOT, name)
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in s else "\n"
    for a, b in pairs:
        a = a.replace("\n", nl)
        b = b.replace("\n", nl)
        n = s.count(a)
        assert n == 1, (name, n, a[:80])
        s = s.replace(a, b)
    io.open(path, "w", encoding="utf-8", newline="").write(s)


edit("ComponentDetailsView.cs", [
    ("    internal static void Open(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,\n"
     "        InfrastructureOverviewModel? owner, string key, ComponentDependencyGraph? rawGraph,\n"
     "        IReadOnlyList<InfrastructureOverviewModel> components)\n"
     "    {",
     "    internal static void Open(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,\n"
     "        InfrastructureOverviewModel? owner, string key, ComponentDependencyGraph? rawGraph,\n"
     "        IReadOnlyList<InfrastructureOverviewModel> components, Action? changed = null)\n"
     "    {"),
    ("        string name = owner?.Name ?? labels?.Name(key) ?? key;",
     "        string alias = owner != null ? repo.GetRule(OverviewListView.ComponentRuleId(owner)).alias : \"\";\n"
     "        string name = alias.Length > 0 ? alias + \"（\" + owner!.Name + \"）\" : owner?.Name ?? labels?.Name(key) ?? key;"),
    ("                }, identified && SourceUi.UninstallReason(key).Length == 0, UiActionRole.Danger);\n"
     "                y += (4 / columns) * 36 + 2;",
     "                }, identified && SourceUi.UninstallReason(key).Length == 0, UiActionRole.Danger);\n"
     "                // 2.3.4 (user): components can be renamed in the overview (display alias) and enabled/disabled like mods,\n"
     "                // except the ones MCE runs on. They stay in the components group and never take part in folder organization.\n"
     "                var component = owner;\n"
     "                PlaceAction(4, Loc.T(\"Display name\", \"改译名\", \"改譯名\"), () => {\n"
     "                    var rule = repo.GetRule(OverviewListView.ComponentRuleId(component));\n"
     "                    OverviewUi.Ask(Loc.T(\"Display name for \", \"显示名称：\", \"顯示名稱：\") + component.Name,\n"
     "                        new[] { (Loc.T(\"Display name (blank uses the package name)\", \"显示名称（留空使用包名）\", \"顯示名稱（留空使用套件名）\"), rule.alias) }, values => {\n"
     "                        repo.EnsureEditableProfile(); var target = repo.GetRule(OverviewListView.ComponentRuleId(component));\n"
     "                        string previous = target.alias; target.alias = values[0].Trim();\n"
     "                        try { repo.SavePackInformation(ConfigurationSaveScope.ModInformation, target.id); }\n"
     "                        catch { target.alias = previous; throw; }\n"
     "                        changed?.Invoke(); OverviewUi.CloseModal();\n"
     "                    });\n"
     "                });\n"
     "                bool on = component.Enabled;\n"
     "                string toggleReason = ComponentToggle.BlockReason(component, !on);\n"
     "                var toggle = PlaceAction(5, on ? Loc.T(\"Disable\", \"停用\", \"停用\") : Loc.T(\"Enable\", \"启用\", \"啟用\"), () => {\n"
     "                    OverviewUi.ConfirmText(on ? Loc.T(\"Disable component\", \"停用组件\", \"停用組件\") : Loc.T(\"Enable component\", \"启用组件\", \"啟用組件\"),\n"
     "                        component.Name + \"\\n\\n\" + (on\n"
     "                            ? Loc.T(\"Its assemblies are renamed to .dll.d and are not loaded from the next launch. Mods that need this component may stop working.\",\n"
     "                                \"它的程序集会改名为 .dll.d，从下次启动起不再加载。依赖此组件的模组可能无法工作。\", \"它的程式集會改名為 .dll.d，從下次啟動起不再載入。依賴此組件的模組可能無法運作。\")\n"
     "                            : Loc.T(\"Its assemblies are renamed back to .dll and load from the next launch.\",\n"
     "                                \"它的程序集会改回 .dll，从下次启动起加载。\", \"它的程式集會改回 .dll，從下次啟動起載入。\")),\n"
     "                        () => {\n"
     "                            try { ComponentToggle.Set(component, !on); OverviewCatalogue.InvalidateCaches(); OverviewPageCache.Invalidate(); changed?.Invoke(); OverviewUi.CloseModal(); }\n"
     "                            catch (Exception error) { OverviewUi.ShowText(Loc.T(\"Could not change the component\", \"组件启停未完成\", \"組件啟停未完成\"), Loc.LocalizeMessage(error.Message)); }\n"
     "                        }, on ? UiActionRole.Danger : UiActionRole.Commit);\n"
     "                }, toggleReason.Length == 0, on ? UiActionRole.Danger : UiActionRole.Commit);\n"
     "                if (toggleReason.Length > 0) { var hover = toggle.gameObject.AddComponent<OverviewHover>(); var tip = toggleReason; hover.Enter = () => OverviewUi.ShowText(Loc.T(\"Enable / disable\", \"启停\", \"啟停\"), tip); }\n"
     "                y += (6 + columns - 1) / columns * 36 + 2;"),
])
edit("SourceUi.cs", [
    ("    internal static void ComponentMenu(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,\n"
     "        InfrastructureOverviewModel package, IReadOnlyList<InfrastructureOverviewModel> components)\n"
     "        => ComponentDetailsView.Open(repo, mods, package, package.Key, DependencyGraph, components);",
     "    internal static void ComponentMenu(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,\n"
     "        InfrastructureOverviewModel package, IReadOnlyList<InfrastructureOverviewModel> components, Action? changed = null)\n"
     "        => ComponentDetailsView.Open(repo, mods, package, package.Key, DependencyGraph, components, changed);"),
])
edit("OverviewListView.Infrastructure.cs", [
    ("            void Menu() => SourceUi.ComponentMenu(_repo, _mods, package, _infrastructure);",
     "            void Menu() => SourceUi.ComponentMenu(_repo, _mods, package, _infrastructure, () => { RefreshInfrastructure(true); _pump.Enqueue(Render); });"),
    ("            var placeholder = Label(row, \"—\", xEnable, 4, goalWidth, 32, 20); placeholder.alignment = TextAlignmentOptions.Midline;\n"
     "            AddHint(placeholder.gameObject, Loc.T(\"Component enablement is not a profile mod target.\", \"组件启停不作为方案模组目标。\", \"組件啟停不作為方案模組目標。\"));",
     "            // 2.3.4: the column shows the component's actual state; switching happens in its details (it renames files at\n"
     "            // once, outside the profile's save, because components are not profile targets).\n"
     "            var placeholder = Label(row, package.Enabled ? \"✓\" : \"○\", xEnable, 4, goalWidth, 32, 20); placeholder.alignment = TextAlignmentOptions.Midline;\n"
     "            placeholder.color = package.Enabled ? OverviewUi.Green : OverviewUi.Muted;\n"
     "            AddHint(placeholder.gameObject, (package.Enabled ? Loc.T(\"Enabled\", \"已启用\", \"已啟用\") : Loc.T(\"Disabled\", \"已停用\", \"已停用\"))\n"
     "                + \" · \" + Loc.T(\"Click the name to enable, disable or rename it. Components are not part of profiles.\", \"点名称可启停或改译名；组件不属于方案。\", \"點名稱可啟停或改譯名；組件不屬於方案。\"));"),
])
print("ok")
