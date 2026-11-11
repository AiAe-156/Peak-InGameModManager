#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ModConfigEnhance.Overview;

// One policy for rendering, prefetching, disk-cache hits and every redirect hop.
internal static class ShopUrlPolicy
{
    private static readonly string[] Hosts = { "thunderstore.io", "ccdn.thunderstore.io", "gcdn.thunderstore.io", "thunderstore.org", "gcdn.thunderstore.org", "raw.githubusercontent.com", "github.com", "i.imgur.com" };
    internal static bool TryNormalize(string value, out string normalized)
    {
        normalized = "";
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.HostNameType != UriHostNameType.Dns || !Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)) return false;
        if (uri.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/');
            if (parts.Length > 4 && (parts[3] == "blob" || parts[3] == "raw"))
                uri = new Uri("https://raw.githubusercontent.com/" + parts[1] + "/" + parts[2] + "/" + string.Join("/", parts, 4, parts.Length - 4) + uri.Query);
        }
        normalized = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
        return true;
    }
    internal static string PrepareMarkdown(string source)
    {
        // Validate the original URL before legacy GitHub rewriting could discard
        // an unsafe scheme, credentials or port.
        source = System.Text.RegularExpressions.Regex.Replace(source, @"!\[([^\]]*)\]\(([^\s\)]+)",
            m => "![" + m.Groups[1].Value + "](" + (TryNormalize(m.Groups[2].Value, out var safe) ? safe : ""));
        source = System.Text.RegularExpressions.Regex.Replace(source, "(<img\\b[^>]*?src=[\"'])([^\"']+)([\"'])",
            m => m.Groups[1].Value + (TryNormalize(m.Groups[2].Value, out var safe) ? safe : "") + m.Groups[3].Value,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return ShopDocumentRules.Prepare(source);
    }
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                !(b[0] == 192 && (b[1] == 168 || b[1] == 0)) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                !IsFakeIp(address);
        // Only global-unicast IPv6; exclude documentation and transition ranges too.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] == 0 || (b[2] == 0x0d && b[3] == 0xb8))) &&
            !(b[0] == 0x20 && b[1] == 0x02);
    }
    /// <summary>198.18.0.0/15 is the benchmarking range that Clash/mihomo TUN "fake-ip" mode hands out for every domain.
    /// Such an address is not the destination, it is the local proxy's handle for it; the connection still goes through
    /// the user's proxy, which resolves and validates the real host. The update worker already accepts it.</summary>
    internal static bool IsFakeIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] == 198 && (b[1] == 18 || b[1] == 19);
    }
    internal static async Task ValidateDestination(Uri uri, CancellationToken token)
    {
        if (!TryNormalize(uri.AbsoluteUri, out _)) throw new IOException("Untrusted store resource URL.");
        var addresses = await ShopNetwork.WithCancellation(Dns.GetHostAddressesAsync(uri.IdnHost), token).ConfigureAwait(false);
        // A fake-ip answer is accepted only when the whole answer is fake-ip (a TUN proxy owns resolution); a real private
        // address, or one mixed in with fake ones, is still refused.
        bool fakeIp = addresses.Length > 0 && addresses.All(IsFakeIp);
        if (addresses.Length == 0 || !fakeIp && addresses.Any(a => !IsPublicAddress(a))) throw new IOException("Store URL resolves to a non-public address.");
        if (fakeIp) ShopNetwork.Report("dns=fake-ip host=" + uri.IdnHost);
    }
}

internal static class ShopNetwork
{
    // Optional diagnostics never include query strings, proxy addresses or credentials.
    internal static Action<string>? Trace { get; set; }
    internal static void Report(string message) => Trace?.Invoke(message);
    // Clients belong to a request, not a mutable global. A proxy change affects the next
    // request without disposing any handler that an existing request is still using.
    private sealed class Route
    {
        internal IWebProxy? Proxy;
        internal bool UsesProxy(Uri uri) => Proxy != null && !Proxy.IsBypassed(uri) && Proxy.GetProxy(uri) != uri;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, Route> Routes = new();
    internal static HttpClient Client()
    {
        var network = OverviewUpdateQueue.Network;
        if (network.Mode != "direct" && network.Mode != "system" && network.Mode != "custom") throw new IOException("Unknown store proxy mode.");
        IWebProxy? proxy = network.Mode == "custom" ? new WebProxy(network.ProxyAddress) : network.Mode == "system" ? WebRequest.DefaultWebProxy : null;
        var handler = new HttpClientHandler { UseProxy = proxy != null, AllowAutoRedirect = false };
        // Unity Mono rejects assigning Proxy=null even before the first request.
        if (proxy != null) handler.Proxy = proxy;
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModConfigEnhance/2.0.0");
        Routes.Add(client, new Route { Proxy = proxy });
        Report("client mode=" + network.Mode);
        return client;
    }
    internal static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken token)
    {
        if (!token.CanBeCanceled) return await task.ConfigureAwait(false);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.Register(() => canceled.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                // Some Mono streams/DNS implementations ignore cancellation. Do not hold a
                // download slot forever, and observe a possible late fault after disposal.
                _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new OperationCanceledException(token);
            }
            return await task.ConfigureAwait(false);
        }
    }
    internal static async Task<HttpResponseMessage> Send(HttpClient client, string url, CancellationToken token,
        Func<Uri, CancellationToken, Task>? validate = null)
    {
        for (int hop = 0; hop <= 5; hop++)
        {
            if (!ShopUrlPolicy.TryNormalize(url, out var normalized)) throw new IOException("Untrusted store resource URL (redirect hop " + hop + ", host " + (Uri.TryCreate(url, UriKind.Absolute, out var rejected) ? rejected.IdnHost : "invalid") + ").");
            url = normalized;
            var uri = new Uri(url);
            Report("validate hop=" + hop + " host=" + uri.IdnHost);
            if (validate != null) await validate(uri, token).ConfigureAwait(false);
            else if (Routes.TryGetValue(client, out var route) && route.UsesProxy(uri))
            {
                // HTTPS CONNECT delegates destination DNS to the user's trusted proxy.
                // Local DNS is not the destination (Clash may return 198.18/15 fake IPs).
                // Keep exact host/HTTPS/port/credential checks above on EVERY hop, TLS
                // certificate validation, redirect count, body bounds and request deadlines.
                Report("dns=proxy-resolved host=" + uri.IdnHost);
            }
            else await ShopUrlPolicy.ValidateDestination(uri, token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            Report("response host=" + uri.IdnHost + " status=" + status + " headersChars=" + (response.Headers.ToString() + response.Content.Headers).Length + " length=" + response.Content.Headers.ContentLength + " encoding=" + string.Join(",", response.Content.Headers.ContentEncoding));
            if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null || hop == 5) throw new IOException("Invalid or excessive store redirects.");
            url = (location.IsAbsoluteUri ? location : new Uri(uri, location)).AbsoluteUri;
            Report("redirect host=" + new Uri(url).IdnHost);
        }
        throw new IOException("Excessive store redirects.");
    }
    internal static async Task<byte[]> ReadBody(HttpResponseMessage response, int limit, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Store resource exceeds size limit.");
        using var stream = await WithCancellation(response.Content.ReadAsStreamAsync(), token).ConfigureAwait(false);
        using var abort = token.Register(() => { try { stream.Dispose(); } catch { } });
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        while (true)
        {
            int n = await WithCancellation(stream.ReadAsync(buffer, 0, buffer.Length, token), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (n == 0) return output.ToArray();
            if (output.Length + n > limit) throw new IOException("Store resource exceeds size limit.");
            output.Write(buffer, 0, n);
        }
    }
}
