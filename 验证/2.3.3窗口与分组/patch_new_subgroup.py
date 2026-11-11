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


# The list's "New subgroup" opens the same editor as "New group" (colour, description, direct member selection),
# preset to the parent. The one-field name prompt it used before had none of that.
edit("OverviewListView.cs", [
    ("    internal Action<OverviewGroup?>? GroupManagementRequested;\n"
     "    internal void ManageGroups()\n"
     "    {\n"
     "        if (GroupManagementRequested != null) GroupManagementRequested(null); else _groupEditor.Show(null);\n"
     "    }",
     "    internal Action<OverviewGroup?>? GroupManagementRequested;\n"
     "    /// <summary>Opens the group editor on a new group under <paramref name=\"parentId\"/> (empty = top level).</summary>\n"
     "    internal Action<string>? NewGroupRequested;\n"
     "    internal void ManageGroups()\n"
     "    {\n"
     "        if (GroupManagementRequested != null) GroupManagementRequested(null); else _groupEditor.Show(null);\n"
     "    }\n"
     "    internal void NewGroupIn(string parentId)\n"
     "    {\n"
     "        if (NewGroupRequested != null) { NewGroupRequested(parentId); return; }\n"
     "        _groupEditor.NewGroupParent = parentId; _groupEditor.Show(null);\n"
     "    }"),
])
edit("OverviewListView.Groups.cs", [
    ('Invoke = () => NewGroup("") });', 'Invoke = () => NewGroupIn("") });'),
    ("Invoke = () => NewGroup(group.id) });", "Invoke = () => NewGroupIn(group.id) });"),
])
edit("OverviewController.cs", [
    ("        _list.GroupManagementRequested = group => { _managementSelectedGroup = group; OpenManagement(ManagementTab.Groups); };",
     "        _list.GroupManagementRequested = group => { _managementSelectedGroup = group; _managementNewGroupParent = null; OpenManagement(ManagementTab.Groups); };\n"
     "        _list.NewGroupRequested = parent => { _managementSelectedGroup = null; _managementNewGroupParent = parent; OpenManagement(ManagementTab.Groups); };"),
])
edit("OverviewPage.Management.cs", [
    ("    private OverviewGroup? _managementSelectedGroup;",
     "    private OverviewGroup? _managementSelectedGroup;\n"
     "    /// <summary>Non-null: the groups page opens on a new group under this parent (\"\" = top level).</summary>\n"
     "    private string? _managementNewGroupParent;"),
    ("        _managementPublisher = null; _managementSelectedGroup = null; _managementSelectedMod = null;",
     "        _managementPublisher = null; _managementSelectedGroup = null; _managementNewGroupParent = null; _managementSelectedMod = null;"),
    ("                _managementGroups.ShowEmbedded(host, _managementSelectedGroup ?? new GroupTree(_repo.State.groups).Flat.FirstOrDefault());",
     "                if (_managementNewGroupParent != null) { _managementGroups.NewGroupParent = _managementNewGroupParent; _managementNewGroupParent = null; _managementGroups.ShowEmbedded(host, null); }\n"
     "                else _managementGroups.ShowEmbedded(host, _managementSelectedGroup ?? new GroupTree(_repo.State.groups).Flat.FirstOrDefault());"),
])
print("ok")
