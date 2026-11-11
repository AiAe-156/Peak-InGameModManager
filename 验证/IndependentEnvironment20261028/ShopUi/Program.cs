using ModConfigEnhance.Overview;
int passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
var state=new ShopInstallationState();state.Reset("A");state.CompleteCurrentPack=true;
Check(!state.CanInstall("T-Core")&&!state.CanUninstall("T-Core",true),"complete pack never bypasses first-frame Reading");
var snapshot=new ShopInstalled.Snapshot{Context="A"};snapshot.Packages["T-Core"]=new(){new(){Key="T-Core",Version="1.0.0",Path="core/a.dll",Area="core"}};
state.Complete(state.Generation,"A",snapshot);
Check(state.CanInstall("T-Core")&&state.CanUninstall("T-Core",false),"ready complete core may enter exact safe previews only");
state.CompleteCurrentPack=false;
Check(!state.CanInstall("T-Core")&&state.BlockReason("T-Core").Contains("Complete the current pack"),"legacy core gives migration blocker");
state.CompleteCurrentPack=true;snapshot.Packages["T-Core"].Add(new(){Key="T-Core",Version="2.0.0",Path="core/b.dll",Area="core"});
Check(!state.CanInstall("T-Core"),"complete core ambiguity still blocked");snapshot.Packages["T-Core"].RemoveAt(1);
Check(!state.CanUninstall("AiAeT-ModConfigEnhance",true)&&state.UninstallBlockReason("AiAeT-ModConfigEnhance").Contains("management host"),"required MCE uninstall has explicit host blocker");
state.Complete(state.Generation,"A",null,"read failed");Check(!state.CanInstall("T-Core"),"failed refresh shuts complete core operations");
int old=state.Generation;state.Reset("B");Check(!state.CompleteCurrentPack&&!state.Complete(old,"A",snapshot),"cross-pack reset drops completeness and stale inventory");
Console.WriteLine(passed+" passed");
