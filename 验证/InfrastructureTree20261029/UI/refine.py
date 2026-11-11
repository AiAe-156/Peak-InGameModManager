from pathlib import Path
def edit(f,a,b):
 p=Path(f);s=p.read_text(encoding='utf-8');assert s.count(a)==1,(f,a[:60],s.count(a));p.write_text(s.replace(a,b),encoding='utf-8')
edit('src/Overview/OverviewListView.Infrastructure.cs','var name = Label(row, EscapeRich(package.Name), xName + 22, 4, Mathf.Max(20, nameWidth - 26), 34, 19);','''if (reason.Length > 0) {
                var locked = Label(row, "⊘", xName + nameWidth - 20, 4, 18, 34, 16); locked.color = OverviewUi.Muted; AddHint(locked.gameObject, EscapeRich(reason));
            }
            float titleWidth = Mathf.Max(20, nameWidth - (reason.Length > 0 ? 46 : 26));
            var name = Label(row, EscapeRich(package.Name), xName + 22, 4, titleWidth, 34, 19);''')
edit('src/Overview/OverviewListView.Infrastructure.cs','OverviewUi.PlaceClipped(name, xName + 22, 4, Mathf.Max(20, nameWidth - 26), 34);','OverviewUi.PlaceClipped(name, xName + 22, 4, titleWidth, 34);')
edit('src/Overview/OverviewListView.Infrastructure.cs','string caption = "├─ " + InfrastructureOverviewModel.TreeCaption(entry);','''var consumer = _mods.FirstOrDefault(m => m.SourceKey.Equals(entry.Id, StringComparison.OrdinalIgnoreCase) || m.Guid.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
                var component = _infrastructure.FirstOrDefault(p => p.Key.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
                string identity = consumer != null ? consumer.Name + " · " + (consumer.PackageVersion.Length > 0 ? consumer.PackageVersion : consumer.PluginVersion)
                    : component != null ? component.Name + " · " + component.Version : entry.Id + " [unknown version]";
                string caption = "├─ " + identity + " · " + InfrastructureOverviewModel.TreeCaption(entry);
                // A small vertical rail keeps the hierarchy legible inside the name column.
                var line = OverviewUi.Panel("ConsumerBranch", row, OverviewUi.Muted);
                OverviewUi.Place(line, xName + indent - 4, ty, 1, 24); line.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;''')
edit('src/Overview/ShopWindow.cs','Namespace=selected.Team,Package=selected.Name,Version=_version},_panel)','Namespace=selected.Team,Package=selected.Name,Version=_installation.Version(selected.Key)},_panel)')
edit('src/Overview/ShopWindow.cs','Namespace=p.Team,Package=p.Name,Version=_version,GroupId=_group','Namespace=p.Team,Package=p.Name,Version=uninstall?_installation.Version(p.Key):_version,GroupId=_group')
# Selection details must refresh protection even when the inventory bytes did not change.
edit('src/Overview/ShopWindow.cs','            _inventory=null;\n            if(changed)InstallationChanged();','            _inventory=null;\n            if(changed)InstallationChanged();else if(_selected!=null&&!_showingTask)UpdateInstallationDetails();')
