import sys,io
p=sys.argv[1]
t=io.open(p,encoding='utf-8-sig').read().split('\n')
bs=chr(92)
out=[]
for i,l in enumerate(t,1):
    if bs+bs in l: out.append("DBL %d: %s"%(i,l[:200]))
    if l.endswith(bs): out.append("END %d: %s"%(i,l[:200]))
io.open(sys.argv[2],'w',encoding='utf-8').write("\n".join(out)+"\n")
print(len(out))
