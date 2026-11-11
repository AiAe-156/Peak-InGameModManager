import ctypes as c
from pathlib import Path
import re,sys,os
BASE=Path(__file__).resolve().parent
DLL=BASE/'tools/dbgeng/content/amd64'
os.add_dll_directory(str(DLL))
engine=c.WinDLL(str(DLL/'dbgeng.dll'))
class GUID(c.Structure):
 _fields_=[('a',c.c_uint32),('b',c.c_uint16),('d',c.c_uint16),('e',c.c_ubyte*8)]
 def __init__(self,s):
  import uuid
  super().__init__();c.memmove(c.byref(self),uuid.UUID(s).bytes_le,16)
def method(obj,index,restype,*args):
 vtable=c.cast(obj,c.POINTER(c.POINTER(c.c_void_p))).contents
 return c.WINFUNCTYPE(restype,c.c_void_p,*args)(vtable[index])
header=(BASE/'tools/dbgeng.h').read_text(errors='replace')
def index(interface,name):
 block=header.split('DECLARE_INTERFACE_('+interface+', IUnknown)',1)[1].split('\n};',1)[0]
 names=re.findall(r'STDMETHOD(?:V)?(?:_\([^,]+,\s*|\()([A-Za-z0-9_]+)\)',block)
 return names.index(name)
client=c.c_void_p();iid=GUID('27fe5639-8407-4f47-8364-ee118fb08ac8')
engine.DebugCreate.argtypes=[c.POINTER(GUID),c.POINTER(c.c_void_p)]
hr=engine.DebugCreate(c.byref(iid),c.byref(client));assert hr==0,hex(hr)
control=c.c_void_p();cid=GUID('5182e668-105e-416e-ad92-24ef800424ba')
assert method(client,0,c.c_long,c.POINTER(GUID),c.POINTER(c.c_void_p))(client,c.byref(cid),c.byref(control))==0
path=Path(sys.argv[1]).resolve()
hr=method(client,index('IDebugClient','OpenDumpFile'),c.c_long,c.c_char_p)(client,str(path).encode('mbcs'));print('OpenDump',hex(hr&0xffffffff),flush=True)
hr=method(control,index('IDebugControl','WaitForEvent'),c.c_long,c.c_uint32,c.c_uint32)(control,0,0xffffffff);print('Wait',hex(hr&0xffffffff),flush=True)
# Engine log contains stack/module diagnostics only, not raw memory or token strings.
log=str(BASE/'dump-stack.txt').replace('\\','/')
execute=method(control,index('IDebugControl','Execute'),c.c_long,c.c_uint32,c.c_char_p,c.c_uint32)
commands=[f'.logopen "{log}"','.ecxr','kv 100','lm','.logclose']
if len(sys.argv)>2: commands=[f'.logopen "{log}"']+Path(sys.argv[2]).read_text().splitlines()+['.logclose']
for command in commands:
 print('Command',command,flush=True);hr=execute(control,2,command.encode('mbcs'),0);print('Result',hex(hr&0xffffffff),flush=True)
