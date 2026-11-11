using System.Net;
using ModConfigEnhance.Overview;
internal static class PolicyChecks
{
 internal static async Task Run() {
 int count=0; void Check(bool ok,string s) {if(!ok)throw new Exception(s);Console.WriteLine("PASS "+s);count++;}
 foreach(var s in new[]{"https://ccdn.thunderstore.io/a","https://thunderstore.io/a"}) Check(ShopUrlPolicy.TryNormalize(s,out _),"official host "+s);
 foreach(var s in new[]{"http://ccdn.thunderstore.io/a","https://ccdn.thunderstore.io:444/a","https://u:p@ccdn.thunderstore.io/a","https://evil.ccdn.thunderstore.io/a","https://ccdn.thunderstore.io.evil.test/a","https://127.0.0.1/a"}) Check(!ShopUrlPolicy.TryNormalize(s,out _),"reject "+new Uri(s).Host);
 foreach(var s in new[]{"198.18.0.1","198.19.255.254","127.0.0.1","::ffff:198.18.0.1"}) Check(!ShopUrlPolicy.IsPublicAddress(IPAddress.Parse(s)),"direct DNS rejects "+s);
 foreach(var target in new[]{"https://ccdn.thunderstore.io/a","https://evil.ccdn.thunderstore.io/a"}) {
 int calls=0,validations=0;
 using var c=new HttpClient(new Handler(_=>{calls++;var r=new HttpResponseMessage(calls==1?HttpStatusCode.Redirect:HttpStatusCode.OK);if(calls==1)r.Headers.Location=new Uri(target);return r;}));
 bool failed=false; try {using var r=await ShopNetwork.Send(c,"https://thunderstore.io/a",default,(_,_)=>{validations++;return Task.CompletedTask;});}catch(IOException){failed=true;}
 Check(target.Contains("evil")?failed&&calls==1:!failed&&calls==2&&validations==2,"redirect exact allowlist "+new Uri(target).Host);
 }
 var options=OverviewUpdateQueue.Snapshot.Network;
 options.Mode="custom"; options.ProxyAddress="http://127.0.0.1:7897";
 using var custom=ShopNetwork.Client(); options.Mode="direct"; using var direct=ShopNetwork.Client();
 var routes=typeof(ShopNetwork).GetField("Routes",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
 var lookup=routes.GetType().GetMethod("TryGetValue")!;
 bool Routed(HttpClient client){object?[] a={client,null};lookup.Invoke(routes,a);return (bool)a[1]!.GetType().GetMethod("UsesProxy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(a[1],new object[]{new Uri("https://ccdn.thunderstore.io/a")})!;}
 Check(Routed(custom)&&!Routed(direct),"proxy route captured per client; next mode applies without disposing in-flight client");
 Console.WriteLine("POLICY PASSED "+count);
 }
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));}
}
