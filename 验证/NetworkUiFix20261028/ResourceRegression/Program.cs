using System.Net;
using System.Net.Http;
using System.Reflection;
using ModConfigEnhance.Overview;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
async Task Reject(Func<Task> call, string name) { try { await call(); } catch { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
foreach (string url in new[] { "http://github.com/a/b/blob/main/a.png", "https://user:pw@github.com/a/b/blob/main/a.png", "https://localhost/a", "https://127.0.0.1/a", "https://10.0.0.1/a", "https://[::1]/a", "https://evil.thunderstore.io/a", "https://thunderstore.io.evil.test/a", "https://github.com:444/a", "https://陌生.example/a", "file:///a" })
    Check(!ShopUrlPolicy.TryNormalize(url, out _), "reject prefetch " + url);
Check(ShopUrlPolicy.TryNormalize("https://github.com/user/repo/blob/main/img.png?raw=1", out var normalized) && normalized == "https://raw.githubusercontent.com/user/repo/main/img.png?raw=1", "GitHub blob normalization");
Check(ShopUrlPolicy.TryNormalize("https://github.com/user/repo/raw/main/img.png", out normalized) && normalized == "https://raw.githubusercontent.com/user/repo/main/img.png", "GitHub raw normalization");
foreach (string address in new[] { "127.0.0.1", "10.1.2.3", "172.16.0.1", "192.168.0.1", "169.254.169.254", "100.64.0.1", "::1", "fe80::1", "fc00::1", "::ffff:127.0.0.1" })
    Check(!ShopUrlPolicy.IsPublicAddress(IPAddress.Parse(address)), "reject resolved private " + address);
Check(!ShopUrlPolicy.PrepareMarkdown("![x](http://u:p@github.com/a/b/blob/main/a.png)").Contains("raw.githubusercontent.com"), "unsafe GitHub URL not laundered by Markdown rewriting");
Check(!ShopUrlPolicy.PrepareMarkdown("<img src='http://github.com/a/b/blob/main/a.png'>").Contains("raw.githubusercontent.com"), "unsafe HTML image not laundered");
Check(ShopUrlPolicy.IsPublicAddress(IPAddress.Parse("8.8.8.8")), "public IPv4");
Check(ShopUrlPolicy.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")), "public IPv6");
foreach (string destination in new[] { "http://github.com/a", "https://user@github.com/a", "https://localhost/a", "https://10.0.0.1/a", "https://unknown.example/a", "https://x.thunderstore.io/a" })
{
    int requests = 0;
    using var client = new HttpClient(new Handler(_ => { requests++; var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri(destination); return r; }));
    await Reject(() => ShopNetwork.Send(client, "https://thunderstore.io/a", default, (_,_) => Task.CompletedTask), "redirect rejected " + destination);
    Check(requests == 1, "unsafe redirect never requested");
}
{
    int requests = 0, validations = 0;
    using var client = new HttpClient(new Handler(_ => { requests++; if (requests == 1) { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://gcdn.thunderstore.io/a"); return r; } return new HttpResponseMessage(HttpStatusCode.OK); }));
    using var response = await ShopNetwork.Send(client, "https://thunderstore.io/a", default, (_,_) => { validations++; return Task.CompletedTask; });
    Check(requests == 2 && validations == 2, "each allowed redirect hop validated");
}
var queue = new ShopDownloadQueue(4, TimeSpan.FromMilliseconds(150));
Task<byte[]> Stalled(CancellationToken token) => ShopNetwork.ReadBody(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }, 32, token);
await Task.WhenAll(Enumerable.Range(0,4).Select(i => Reject(() => queue.Run(Stalled), "stopped body timeout slot " + i)));
Check((await queue.Run(_ => Task.FromResult(new byte[] { 1 }))).Length == 1, "requests execute after 4 stalled bodies");
await Task.WhenAll(Enumerable.Range(0,4).Select(i => Reject(() => queue.Run(t => ShopNetwork.ReadBody(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent() }, 32, t)), "oversize body slot " + i)));
Check((await queue.Run(_ => Task.FromResult(new byte[] { 1 }))).Length == 1, "requests execute after oversize bodies");
using (var cancel = new CancellationTokenSource(30)) await Reject(() => queue.Run(Stalled, cancel.Token), "caller cancels stalled body");
Check((await queue.Run(_ => Task.FromResult(new byte[] { 1 }))).Length == 1, "requests execute after cancellation");
var shared = new TaskCompletionSource<byte[]>();
using (var cancel = new CancellationTokenSource())
{
    var first = ShopNetwork.WithCancellation(shared.Task, cancel.Token);
    var second = ShopNetwork.WithCancellation(shared.Task, default);
    cancel.Cancel(); await Reject(() => first, "shared subscriber cancellation");
    shared.SetResult(new byte[] { 1,2 }); Check((await second).Length == 2, "other shared subscriber survives");
}
// Exercise actual task-cache policy without external network: insert completed task entries through reflection.
var cacheType = typeof(ShopResourceCache);
var jobs = (System.Collections.IDictionary)cacheType.GetField("Jobs", BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
var entryType = cacheType.GetNestedType("Entry", BindingFlags.NonPublic)!;
void Insert(string key, Task<byte[]> task, long used)
{
    var entry = Activator.CreateInstance(entryType, true)!;
    entryType.GetField("Job", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(entry, task);
    entryType.GetField("Used", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(entry, used); jobs.Add(key, entry);
}
void Trim() => cacheType.GetMethod("Trim", BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
Insert("failure", Task.FromException<byte[]>(new IOException("fixture")), 0); Trim(); Check(!jobs.Contains("failure"), "failed job removed for retry");
Insert("canceled", Task.FromCanceled<byte[]>(new CancellationToken(true)), 0); Trim(); Check(!jobs.Contains("canceled"), "canceled job removed for retry");
for (int i=0;i<128;i++) Insert("count"+i, Task.FromResult(new byte[1]), i);
Trim(); Check(jobs.Count < 128, "task count bounded eviction"); jobs.Clear();
for (int i=0;i<4;i++) Insert("size"+i, Task.FromResult(new byte[10*1024*1024]), i);
Trim(); Check(jobs.Count == 3, "result cache byte cap eviction"); jobs.Clear();
Insert("32:https://thunderstore.io/test", Task.FromResult(new byte[32]), 0);
Check((await ShopResourceCache.Get("https://thunderstore.io/test",32)).Length == 32, "same limit shares cached job");
await Reject(() => ShopResourceCache.Get("https://localhost/x"), "cache validates before any fetch"); jobs.Clear();
var destroyed = new List<object>(); var cache = new ShopLeaseCache<object>(2, 10, destroyed.Add);
object a = new(), b = new(), c = new();
var leaseA = cache.Add("a", a, 4)!; var leaseA2 = cache.Acquire("a")!; var leaseB = cache.Add("b", b, 4)!;
cache.Release(leaseA); cache.Release(leaseB);
var leaseC = cache.Add("c", c, 4)!;
Check(destroyed.Contains(b) && !destroyed.Contains(a), "LRU never destroys active shared texture");
cache.Release(leaseA2); cache.Release(leaseC);
var leaseD = cache.Add("d",new object(), 4)!; Check(destroyed.Contains(a), "last release makes texture evictable");
var duplicate = new object(); var same = cache.Add("d", duplicate,4)!;
Check(ReferenceEquals(same,leaseD) && destroyed.Contains(duplicate), "duplicate texture disposal preserves shared lease");
var full = new ShopLeaseCache<object>(1,4,destroyed.Add); var live = full.Add("live",new object(),4)!; var rejected = new object();
Check(full.Add("next",rejected,4)==null && destroyed.Contains(rejected) && !destroyed.Contains(live.Value), "full active cache rejects rather than destroys live texture");
byte[] png = new byte[24]; byte[] sig = {137,80,78,71,13,10,26,10}; sig.CopyTo(png,0); png[18]=1; png[22]=1;
Check(ShopImageBounds.Allows(png,2048), "bounded PNG decode"); png[16]=1;
Check(!ShopImageBounds.Allows(png,2048), "oversized PNG rejected before Unity allocation");
Console.WriteLine($"ALL {checks} CHECKS PASSED (isolated handlers, no live installation or external network)");

sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(send(request)); }
sealed class UnknownLengthContent : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[100]).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(new byte[100]));
}
sealed class StalledStream : Stream
{
    public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException(); public override long Position {get=>0;set=>throw new NotSupportedException();}
    public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] b,int o,int c,CancellationToken t)=>new TaskCompletionSource<int>().Task;
    public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
    public override void SetLength(long l)=>throw new NotSupportedException(); public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}
