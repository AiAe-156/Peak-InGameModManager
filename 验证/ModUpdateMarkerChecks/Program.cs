using ModConfigEnhance.Overview;
int n=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;}
Check(!ModUpdateMarkerPolicy.IsUpgrade(new(){kind="added",after="5.9.10"}),"first observation not update");
Check(ModUpdateMarkerPolicy.IsUpgrade(new(){kind="changed",before="1.0.0",after="1.1.0"}),"real upgrade");
Check(!ModUpdateMarkerPolicy.IsUpgrade(new(){kind="changed",before="2.0.0",after="1.0.0"}),"rollback not upgrade");
Check(!ModUpdateMarkerPolicy.IsUpgrade(new(){kind="changed",before="False",after="True"}),"author target not mod update");
Check(!ModUpdateMarkerPolicy.IsUpgrade(new(){kind="removed",before="1.0.0"}),"removal not update");
var j=new ChangeJournal();var a=ModUpdateMarkerPolicy.Scope("a");var b=ModUpdateMarkerPolicy.Scope("b");
j.Observe(a,new(){{"mod:x","1.0.0"}});j.Observe(b,new(){{"mod:x","2.0.0"}});Check(j.unread.Count==0,"pack switch independent baseline");
j.Observe(a,new(){{"mod:x","1.1.0"}});Check(ModUpdateMarkerPolicy.IsUpgrade(j.unread[a+"|mod:x"]),"upgrade inside pack detected");
j.Observe(a,new(){{"mod:x","1.1.0"},{"mod:new","1.0.0"}});Check(!ModUpdateMarkerPolicy.IsUpgrade(j.unread[a+"|mod:new"]),"newly installed mod excluded");
Console.WriteLine($"PASS {n} mod-update marker checks");
