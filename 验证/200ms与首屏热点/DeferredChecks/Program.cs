using System;
using ModConfigEnhance.Overview;
int passed=0,calls=0;void Check(bool ok,string n){if(!ok)throw new Exception(n);passed++;Console.WriteLine("PASS "+n);}
var hint=new DeferredHint();object a=new(),b=new();string Factory(){calls++;return "hint";}
for(int i=0;i<100;i++){hint.Enter(a,Factory,i);Check(hint.Take(i+.05f)==null,"quick sweep does not open");hint.Cancel();}
Check(calls==0,"100 fast sweeps execute zero factories");
hint.Enter(a,Factory,100);Check(hint.Take(100.199f)==null&&calls==0,"before 200ms factory not evaluated");Check(hint.Take(100.201f)=="hint"&&calls==1,"stable 200ms evaluates once");Check(hint.Take(101)==null&&calls==1,"pump does not repeat factory");
hint.Cancel();hint.Enter(a,Factory,200);hint.Enter(a,Factory,200.15f);Check(hint.Refresh(a,Factory)==null&&calls==1,"refresh pending does not evaluate");Check(hint.Take(200.201f)=="hint"&&calls==2,"same target refresh preserves deadline");
hint.Cancel();hint.Enter(a,Factory,300);hint.Enter(b,Factory,300.15f);Check(hint.Take(300.201f)==null&&calls==2,"changed target resets deadline");Check(hint.Take(300.351f)=="hint"&&calls==3,"replacement evaluates only latest factory");
hint.Cancel();hint.Enter(a,Factory,400);hint.Cancel();Check(hint.Take(401)==null&&calls==3,"exit modal close cancellation prevents factory");
hint.Enter(a,Factory,500);Check(hint.Take(501,_=>false)==null&&calls==3,"inactive target cancelled before evaluation");
Check(hint.Focus(a,Factory,600)=="hint"&&calls==4,"controller focus evaluates immediately");Check(hint.Take(601)==null&&calls==4,"focus is not replayed by pump");
Check(hint.Refresh(a,Factory)=="hint"&&calls==5,"already visible content refreshes explicitly");
Console.WriteLine($"RESULT {passed} deferred factory checks passed");
