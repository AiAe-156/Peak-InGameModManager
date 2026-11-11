namespace ModConfigEnhance { internal static class Loc { internal static string RootDirectory = Path.Combine(AppContext.BaseDirectory,"isolated-cache"); } internal static class Plugin { internal static Logger Log = new(); internal class Logger { internal void LogWarning(string s)=>Console.WriteLine(s); } } }
namespace ModConfigEnhance.Overview {
internal static class OverviewUpdateQueue { internal static SnapshotData Snapshot = new(); internal class SnapshotData { internal NetworkData Network = new(); } internal class NetworkData { internal string Mode="direct",ProxyAddress=""; } }
internal static class OverviewRepository { internal static void AtomicWrite(string path,string text)=>File.WriteAllText(path,text); }
internal static class ThunderstoreUpdateService { internal static bool ValidPart(string s)=>System.Text.RegularExpressions.Regex.IsMatch(s,"^[A-Za-z0-9_]+$"); }
internal static class ShopResourceCache { internal static async Task<byte[]> Get(string url,int limit,CancellationToken token=default) { using var c=ShopNetwork.Client(); using var r=await ShopNetwork.Send(c,url,token); return await ShopNetwork.ReadBody(r,limit,token); } }
}
