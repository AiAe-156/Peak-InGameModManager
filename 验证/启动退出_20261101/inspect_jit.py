import mmap,struct,sys
from pathlib import Path
p=Path(sys.argv[1]);f=p.open('rb');m=mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ)
u32=lambda o:struct.unpack_from('<I',m,o)[0]
u64=lambda o:struct.unpack_from('<Q',m,o)[0]
ranges=[]
for i in range(u32(8)):
 o=u32(12)+12*i;t,size,rva=struct.unpack_from('<III',m,o)
 if t==9:
  n,base=struct.unpack_from('<QQ',m,rva);offset=base
  for j in range(n):
   va,sz=struct.unpack_from('<QQ',m,rva+16+16*j);ranges.append((va,sz,offset));offset+=sz
 elif t==5:
  n=u32(rva)
  for j in range(n):
   va,sz,off=struct.unpack_from('<QII',m,rva+4+16*j);ranges.append((va,sz,off))
def read(va,n):
 for v,s,o in ranges:
  if v<=va and va+n<=v+s:return m[o+va-v:o+va-v+n]
 return b''
def ptr(va):
 b=read(va,8);return struct.unpack('<Q',b)[0] if len(b)==8 else 0
if len(sys.argv)>2:
 va=int(sys.argv[2],16);b=read(va,256)
 print(hex(va),b.hex(' '))
 for i in range(0,len(b),8):
  value=struct.unpack_from('<Q',b,i)[0];s=read(value,160).split(b'\0')[0]
  if s and all(32<=x<127 for x in s):print(hex(i),hex(value),s.decode())
else:
 needle=struct.pack('<Q',0x1fb2a014ab0);start=0;hits=[]
 while len(hits)<60:
  pos=m.find(needle,start)
  if pos<0:break
  for va,sz,off in ranges:
   if off<=pos<off+sz:hits.append(va+pos-off);break
  start=pos+8
 for va in hits:
  print('reference',hex(va),'context',read(va-32,80).hex(' '))
