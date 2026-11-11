using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModConfigEnhance.Overview;
internal class Program {
 sealed class BodyHandler: System.Net.Http.HttpMessageHandler { readonly byte[] body; internal BodyHandler(byte[] b){body=b;} protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r,CancellationToken t)=>Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.ByteArrayContent(body)}); }
 static int Main(string[] args) { try { Run().GetAwaiter().GetResult();return 0; }catch(Exception e){Console.WriteLine(e);return 1;} }
 static async Task Run() {
 ShopNetwork.Trace=Console.WriteLine;
 using(var output=new System.IO.MemoryStream()) {
 using(var gz=new System.IO.Compression.GZipStream(output,System.IO.Compression.CompressionMode.Compress,true)) {byte[] data=System.Text.Encoding.UTF8.GetBytes(new string('x',9744475));gz.Write(data,0,data.Length);}
 using(var c=new System.Net.Http.HttpClient(new BodyHandler(output.ToArray()))) {
 var text=await ShopCatalog.Text(c,"https://thunderstore.io/test",CancellationToken.None);
 if(text.Length!=9744475)throw new Exception("decode mismatch");Console.WriteLine("MONO PASS production gzip bounded decode 9744475 bytes");
 }
 }
 foreach(string mode in new[]{"custom","direct","system"}) {
 OverviewUpdateQueue.Snapshot.Network.Mode=mode;OverviewUpdateQueue.Snapshot.Network.ProxyAddress=mode=="custom"?"http://127.0.0.1:7897":"";
 using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(140))) {
 try { var list=await ShopCatalog.Load(timeout.Token,true);Console.WriteLine("MONO SUCCESS mode="+mode+" packages="+list.Count+" versions="+list.Sum(x=>x.Versions.Count)); }
 catch(Exception e){Console.WriteLine("MONO FAILURE mode="+mode+" "+e);}
 }
 }
 }
}
