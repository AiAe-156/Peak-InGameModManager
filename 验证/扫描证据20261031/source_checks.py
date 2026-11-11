from pathlib import Path
root = Path(__file__).resolve().parents[2]
source = (root / 'src/Overview/OverviewCatalogue.cs').read_text(encoding='utf-8-sig')
before = (root / '备份/20261031_性能续修前/src/Overview/OverviewCatalogue.cs').read_text(encoding='utf-8-sig')
checks = 0
def check(value, label):
    global checks
    assert value, label
    checks += 1
    print('PASS', label)
def section(text, start, end):
    return text[text.index(start):text.index(end, text.index(start))]
check(section(source, '    private static List<OverviewMod> Consolidate', '    internal List<OverviewMod> PublicationCache') == section(before, '    private static List<OverviewMod> Consolidate', '    internal List<OverviewMod> PublicationCache'), 'consolidation unchanged: candidate, loaded precedence, duplicate GUID, enablement and protection')
check(source[source.index('        if (parsed != null)'):] == before[before.index('        if (parsed != null)'):], 'all manifest/source/translation/protection projection policy unchanged')
check(section(source, '        foreach (var entry in Chainloader.PluginInfos.Values)', '        // Only physical evidence') == section(before, '        foreach (var entry in Chainloader.PluginInfos.Values)', '        if (Directory.Exists(Paths.PluginPath))'), 'runtime loader evidence captured freshly, including moved disabled copies')
scan = section(source, '    public List<OverviewMod> Scan', '    private static List<OverviewMod> Consolidate')
check('known.Contains(physical.Path)' in scan, 'loaded DLL excluded from physical projection')
check('Create(repository, record.Guid' in scan and 'new OverviewDependency' in scan, 'each scan creates fresh rows and dependency objects')
check('Rule = repository.GetRule(guid)' in source and 'ModSources.Apply(row)' in source, 'current repository rule and current sources reapplied')
check('JObject.Parse(evidenceJson)' in source, 'manifest projection receives private JSON, not mutable shared data')
check('ManifestPaths' not in source, 'negative manifest path cache removed, no stale ancestor selection')
check('InvalidateScanEvidence();' in source, 'explicit refresh invalidates physical discovery')
profile = (root / 'src/Overview/OverviewRepository.Profiles.cs').read_text(encoding='utf-8-sig')
reload = section(profile, '    public void Reload()', '    private void MigrateRuntimeFiles()')
check(reload.index('new OverviewCatalogue().Scan(this)') < reload.index('Profiles.Initialize(') < reload.index('LoadProfileDraft();'), 'profile migration and draft loading ordering unchanged')
print(f'RESULT {checks} source policy contracts passed; these are not runtime Unity tests')
