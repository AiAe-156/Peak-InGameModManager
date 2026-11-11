from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(p):return (r/p).read_text(encoding='utf-8-sig')
g=read('src/XUnityMachineTranslationGuard.cs');b=read('src/XUnityBridge.Guard.cs');x=read('src/XUnityBridge.cs');p=read('src/Plugin.cs')
checks={
'guard before other UI initialization':p.index('XUnityMachineTranslationGuard.StartGuard')<p.index('Loc.EnsureBuiltInFiles'),
'default enabled':'"Disable XUnity machine translation", true' in p,
'primary and fallback independently cleared':'"SetFallback"' in x and '_fallbackServiceEndpoint?.SetValue(null,string.Empty)' in x,
'runtime always considered':'if (_serviceEndpoint != null || _fallbackServiceEndpoint != null)' in x,
'actual state readback':'settingsOff&&runtimeOff' in x,
'resolve retries absent plugin':'if (!Installed) return;\n        _resolved = true' in x,
'cache pending writes cleared':'"_newTranslations"' in b,
'memory cache reload after sweep':'if(ReloadTranslations())_cleanedCache=cache' in b,
'retry uncleaned cache':'!ReferenceEquals(cache,_cleanedCache)' in b,
'event driven no periodic polling':'private void Update()' not in g and 'PatchAfter(plugin,"Initialize"' in g and 'PatchAfter(plugin,"ReloadTranslations"' in g,
'reload before pruning fenced':'Patch(plugin,"ReloadTranslations",nameof(BeforeReload))' in g,
'network queue execution and callback fence':all(n in g for n in ['"CreateTranslationJobFor"','"EnqueueTranslation"','"HandleNextJob"','"HandleNextBatch"','"OnSingleTranslationCompleted"','"OnBatchTranslationCompleted"','"Translate",nameof(BlockCoroutine)']),
'native localized originals preserved':'GetComponent<LocalizedText>()' in g and 'translatedText=source;return true' in g,
'mod dictionary methods not disabled':'DisableAutoTranslator(' not in g,
'endpoint setters re-selection fenced':'nameof(BlankEndpoint)' in g,
'guard hooks independently unload':'_harmony?.UnpatchSelf()' in g,
'INI case spacing handled':'string key=line.Substring(0,eq).Trim()' in x,
'long term deployment authorization':'自动' in read('AGENTS.md') and 'MCE.UpdateWorker' in read('AGENTS.md'),
}
for k,v in checks.items():print(('PASS ' if v else 'FAIL ')+k)
assert all(checks.values());print('RESULT',len(checks),'source checks passed')
