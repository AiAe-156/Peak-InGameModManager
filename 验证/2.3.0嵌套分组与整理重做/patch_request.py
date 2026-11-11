from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
I = 'OverviewPage.Installation.cs'
sub(I, '''    private void PreviewFolderLayout(bool translated)
    {
        var applied = new OverviewRepository(appliedView: true);
        var request = new LayoutQueueRequest {
            Mode = translated ? InstallationLayoutMode.GroupAndTranslatedOuterFolder : InstallationLayoutMode.GroupOnly,
            AppliedProfileId = applied.Profiles.AppliedId,
            Groups = applied.State.groups.Select(g => new LayoutQueueGroup { Id = g.id, Name = g.name }).ToList(),
            Mods = new OverviewCatalogue().Scan(applied).SelectMany(m => m.PluginGuids.Concat(new[] { m.Guid }).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => new LayoutQueueMod { Guid = id, GroupId = string.IsNullOrEmpty(applied.GroupOf(id)) ? "ungrouped" : applied.GroupOf(id), Name = OverviewLanguageBridge.GetDisplayName(m) })).ToList()
        };''',
'''    /// <summary>2.3.0: one organizer — whole mod folders move, names unchanged, into folders that mirror the (nested)
    /// group tree; each overview row goes where the user sees it grouped.</summary>
    private void PreviewFolderLayout()
    {
        var applied = new OverviewRepository(appliedView: true);
        var rows = new OverviewCatalogue().Scan(applied);
        var request = new LayoutQueueRequest {
            Mode = InstallationLayoutMode.GroupOnly,
            AppliedProfileId = applied.Profiles.AppliedId,
            UngroupedFolder = Loc.T("Ungrouped", "未分组", "未分組"),
            Groups = applied.State.groups.Select(g => new LayoutQueueGroup { Id = g.id, Name = g.name, ParentId = g.parentId ?? "" }).ToList(),
            Rows = rows.Select(m => new LayoutQueueRow { Guids = m.PluginGuids.Concat(new[] { m.Guid }).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                GroupId = m.Rule.groupId ?? "", PackageRoot = m.PackageRoot, Version = m.DisplayVersion, Loaded = m.Loaded }).ToList()
        };''')
M = 'OverviewPage.Management.cs'
sub(M, '''        form.Button(Loc.T("Preview group folders", "预览按分组整理", "預覽依分組整理"), () => PreviewFolderLayout(false));
        form.Button(Loc.T("Preview groups and translated folders", "预览分组并使用译名", "預覽分組並使用譯名"), () => PreviewFolderLayout(true));''',
'''        form.Button(Loc.T("Organize into group folders", "整理到分组文件夹", "整理到分組資料夾"), PreviewFolderLayout);''')
print('ok')
