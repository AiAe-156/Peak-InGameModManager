using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed class StoreClient : IDisposable
{
    readonly HttpClient http;
    const long MaxArchive = 512L * 1024 * 1024;
    internal StoreClient(UpdateNetworkOptions options, HttpMessageHandler? testHandler = null)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = options.Mode != "direct" };
        if (options.Mode == "custom")
        {
            if (!Uri.TryCreate(options.ProxyAddress, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https" or "socks5")) throw new IOException("自定义代理地址无效。");
#if NETFRAMEWORK
            if (proxy.Scheme == "socks5") throw new IOException("Framework requires an HTTP(S) proxy; no direct fallback was used.");
#endif
            handler.Proxy = new WebProxy(proxy);
        }
        else if (options.Mode == "system") handler.Proxy = WebRequest.DefaultWebProxy;
        else if (options.Mode != "direct") throw new IOException("联网方式须为 system、direct 或 custom。");
        if (testHandler != null) handler.Dispose();
        http = new HttpClient(testHandler ?? handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MCE.UpdateWorker/" + typeof(StoreClient).Assembly.GetName().Version!.ToString(3));
    }
    public void Dispose() => http.Dispose();
    internal async Task<StoreVersion> Resolve(UpdateJob job, string version, CancellationToken cancellation)
    {
        string uri = "https://thunderstore.io/api/experimental/package/" + Uri.EscapeDataString(job.Namespace) + "/" + Uri.EscapeDataString(job.Package) + "/" + (version.Length == 0 ? "" : Uri.EscapeDataString(version) + "/");
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(35));
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                using var data = JsonDocument.Parse(await response.Content.ReadTextAsync(timeout.Token));
                var root = data.RootElement;
                if (root.TryGetProperty("namespace", out var ns) && ns.GetString() != job.Namespace || root.TryGetProperty("name", out var name) && name.GetString() != job.Package)
                    throw new IOException("商店返回的包身份不匹配。");
                var value = root.TryGetProperty("latest", out var latest) ? latest : root;
                string resolved = value.GetProperty("version_number").GetString()!;
                if (version.Length != 0 && resolved != version) throw new IOException("商店返回的版本与指定版本不同。");
                WorkerLog.Debug("resolve " + job.Namespace + "-" + job.Package + " " + (version.Length == 0 ? "latest" : version) + " → " + resolved + " url=" + value.GetProperty("download_url").GetString());
                return new StoreVersion(resolved, value.GetProperty("download_url").GetString()!,
                    value.TryGetProperty("dependencies", out var dependencies) ? dependencies.EnumerateArray().Select(d => d.GetString()!).ToArray() : []);
            }
            catch (Exception error) when (attempt < 2 && !cancellation.IsCancellationRequested && error is HttpRequestException or TaskCanceledException)
            { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellation); }
        }
    }
    internal async Task<string> Download(string url, string path, Action<long, long, string> progress, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new IOException("更新包必须从 HTTPS 下载。");
        string partial = path + ".partial", metaPath = path + ".download.json";
        if (File.Exists(path))
        {
            var cached = File.Exists(metaPath) ? Files.Read<DownloadMeta>(metaPath) : new DownloadMeta();
            if (cached.Url == url && cached.CompletedHash.Length > 0 && Files.Hash(path) == cached.CompletedHash) return path;
            Files.MoveAside(path, path + ".unverified");
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (offset == 0 && File.Exists(partial)) Files.MoveAside(partial, partial + ".empty");
                DownloadMeta meta = File.Exists(metaPath) ? Files.Read<DownloadMeta>(metaPath) : new DownloadMeta();
                if (offset != 0 && (meta.Url != url || meta.ETag.Length == 0)) { Files.MoveAside(partial, partial + ".previous"); offset = 0; }
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (offset > 0) { request.Headers.Range = new RangeHeaderValue(offset, null); request.Headers.TryAddWithoutValidation("If-Range", meta.ETag); }
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); headersTimeout.CancelAfter(TimeSpan.FromSeconds(35));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                // Preserve the old fragment when a server does not honor byte ranges or changes the entity.
                string etag = response.Headers.ETag?.ToString() ?? "";
                bool resumed = response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset && offset > 0 && etag == meta.ETag;
                if (offset > 0 && !resumed)
                {
                    Files.MoveAside(partial, partial + ".previous");
                    offset = 0;
                    progress(0, 0, "服务器未接受续传，保留旧片段并重新下载。");
                    if (response.StatusCode == HttpStatusCode.PartialContent || response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent && !resumed) throw new IOException("服务器返回了非预期的分段内容。");
                long expected = response.Content.Headers.ContentLength ?? -1, total = expected < 0 ? 0 : offset + expected;
                if (total > MaxArchive) throw new IOException("更新包超过 512 MB 限额，请手动下载。");
                Files.Save(metaPath, new DownloadMeta { Url = url, ETag = etag });
                using var input = await response.Content.ReadStreamAsync(cancellation);
                using (var output = new FileStream(partial, resumed ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    byte[] buffer = new byte[81920]; long written = offset;
                    // Report the starting offset first, so a caller can tell this run's transfer from a resumed prefix.
                    progress(written, total, resumed ? "正在续传" : "正在下载");
                    while (true)
                    {
                        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                        int count = await input.ReadAsync(buffer, 0, buffer.Length, readTimeout.Token);
                        if (count == 0) break;
                        written += count; if (written > MaxArchive) throw new IOException("更新包超过下载限额。");
                        await output.WriteAsync(buffer, 0, count, cancellation);
                        progress(written, total, resumed ? "正在续传" : "正在下载");
                    }
                    await output.FlushAsync(cancellation);
                    if (expected >= 0 && written != offset + expected) throw new IOException("下载长度不完整。");
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(partial, path);
                Files.Save(metaPath, new DownloadMeta { Url = url, ETag = etag, CompletedHash = Files.Hash(path) }); return path;            }
            catch (Exception error) when (attempt < 2 && !cancellation.IsCancellationRequested && error is HttpRequestException or IOException or TaskCanceledException)
            { WorkerLog.Debug("download retry " + (attempt + 1) + "/2 " + url + ": " + error.Message); progress(File.Exists(partial) ? new FileInfo(partial).Length : 0, 0, "连接中断，正在重试 " + (attempt + 1) + "/2"); await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellation); }
        }
    }
    internal async Task ProbeDownload(string url, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new IOException("商店提供的下载地址不是 HTTPS。");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(0, 1023);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
            throw new IOException("下载服务器没有返回可读取的文件响应。");
        using var input = await response.Content.ReadStreamAsync(cancellation);
        byte[] sample = new byte[1024];
        int count = await input.ReadAsync(sample, 0, sample.Length, cancellation);
        if (count == 0) throw new IOException("下载服务器返回了空文件。");
        bool zip = count >= 4 && sample[0] == (byte)'P' && sample[1] == (byte)'K'
            && ((sample[2] == 3 && sample[3] == 4) || (sample[2] == 5 && sample[3] == 6) || (sample[2] == 7 && sample[3] == 8));
        string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        string dispositionName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName ?? "";
        bool fileResponse = mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+zip", StringComparison.OrdinalIgnoreCase)
            || dispositionName.Trim('"').EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!zip && !fileResponse)
            throw new IOException("下载响应有内容，但不是 ZIP 文件或可识别的文件下载响应。");
    }
    internal sealed record StoreVersion(string Version, string DownloadUrl, string[] Dependencies);
    public sealed class DownloadMeta { public string Url { get; set; } = ""; public string ETag { get; set; } = ""; public string CompletedHash { get; set; } = ""; }
}
