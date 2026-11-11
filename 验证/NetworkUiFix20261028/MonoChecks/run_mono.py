import ctypes, pathlib, subprocess, os
project=pathlib.Path(__file__).resolve().parents[3]
game=project.parent.parent
managed=game/'PEAK_Data/Managed'
here=pathlib.Path(__file__).resolve().parent
sources=[project/'src/Overview'/n for n in ['ShopNetwork.cs','ShopCatalog.cs','ShopDocumentRules.cs','ShopDownloadQueue.cs']]+[here/'Program.cs',here/'Stubs.cs']
refs=['mscorlib','System','System.Core','System.Net.Http','System.IO.Compression','netstandard','Newtonsoft.Json']
cmd=['C:/Program Files/dotnet/dotnet.exe','C:/Program Files/dotnet/sdk/10.0.401/Roslyn/bincore/csc.dll','-nologo','-noconfig','-nostdlib+','-langversion:12','-nullable:enable','-target:exe','-out:'+str(here/'MonoNetworkChecks.exe')]+['-r:'+str(managed/(n+'.dll')) for n in refs]+[str(p) for p in sources]
subprocess.run(cmd,check=True)
print('compiled against actual game Mono assemblies',flush=True)
# A separate Python process hosts the shipped Mono runtime only; no game entrypoint,
# Unity objects, BepInEx loader or other plugins are loaded.
os.add_dll_directory(str(game/'MonoBleedingEdge/EmbedRuntime'))
mono=ctypes.CDLL(str(game/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'))
mono.mono_set_dirs.argtypes=[ctypes.c_char_p,ctypes.c_char_p]
mono.mono_set_dirs(str(managed).encode(),str(game/'MonoBleedingEdge/etc').encode())
mono.mono_set_assemblies_path.argtypes=[ctypes.c_char_p]
mono.mono_set_assemblies_path(str(managed).encode())
mono.mono_config_parse.argtypes=[ctypes.c_char_p];mono.mono_config_parse(None)
mono.mono_dllmap_insert.argtypes=[ctypes.c_void_p,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p]
mono.mono_dllmap_insert(None,b'MonoPosixHelper',None,str(game/'MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll').encode(),None)
mono.mono_jit_init_version.argtypes=[ctypes.c_char_p,ctypes.c_char_p];mono.mono_jit_init_version.restype=ctypes.c_void_p
domain=mono.mono_jit_init_version(b'MCE isolated network checks',b'v4.0.30319')
mono.mono_domain_assembly_open.argtypes=[ctypes.c_void_p,ctypes.c_char_p];mono.mono_domain_assembly_open.restype=ctypes.c_void_p
assembly=mono.mono_domain_assembly_open(domain,str(here/'MonoNetworkChecks.exe').encode())
if not assembly: raise RuntimeError('Mono failed to open harness')
mono.mono_jit_exec.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_int,ctypes.POINTER(ctypes.c_char_p)]
argv=(ctypes.c_char_p*1)(str(here/'MonoNetworkChecks.exe').encode())
raise SystemExit(mono.mono_jit_exec(domain,assembly,1,argv))
