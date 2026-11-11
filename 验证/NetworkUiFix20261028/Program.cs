using System.Net;
using System.Diagnostics;
using ModConfigEnhance.Overview;
await PolicyChecks.Run();
ShopNetwork.Trace=Console.WriteLine;
Console.WriteLine("runtime="+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
foreach(var host in new[]{"thunderstore.io","gcdn.thunderstore.io"}) { try { Console.WriteLine("dns "+host+"="+string.Join(",",(await Dns.GetHostAddressesAsync(host)).Select(a=>a+" public="+ShopUrlPolicy.IsPublicAddress(a)))); } catch(Exception e){ Console.WriteLine(e); } }
foreach(var mode in args.Length>0?args:new[]{"direct","system","custom"}) {
 OverviewUpdateQueue.Snapshot.Network.Mode=mode; OverviewUpdateQueue.Snapshot.Network.ProxyAddress=mode=="custom"?"http://127.0.0.1:7897":"";
 var watch=Stopwatch.StartNew(); Console.WriteLine("START mode="+mode);
 using var token=new CancellationTokenSource(TimeSpan.FromSeconds(150));
 try { using var client=ShopNetwork.Client(); const string url="https://thunderstore.io/c/peak/api/v1/package-listing-index/";
 using(var response=await ShopNetwork.Send(client,url,token.Token)) { Console.WriteLine("HTTP="+(int)response.StatusCode+" headersChars="+(response.Headers.ToString()+response.Content.Headers).Length+" length="+response.Content.Headers.ContentLength+" encoding="+string.Join(",",response.Content.Headers.ContentEncoding)); var b=await ShopNetwork.ReadBody(response,64*1024*1024,token.Token); Console.WriteLine("raw="+b.Length+" magic="+Convert.ToHexString(b.Take(8).ToArray())); }
 var listings=await ShopCatalog.Load(token.Token,true); Console.WriteLine("SUCCESS listings="+listings.Count+" versions="+listings.Sum(x=>x.Versions.Count));
 }catch(Exception e){Console.WriteLine("FAIL stage=production GET/catalog "+e);}
 Console.WriteLine("END elapsed="+watch.Elapsed);
}
