#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ModConfigEnhance.Overview;
internal static class ShopResourceCache
{
    private sealed class Entry
    {
        internal Task<byte[]> Job = null!;
        internal long Used;
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Jobs = new(StringComparer.Ordinal);
    private static readonly ShopDownloadQueue Downloads = new(4, TimeSpan.FromSeconds(40));
    internal const int MaxEntries = 128;
    internal const long MaxBytes = 32 * 1024 * 1024;
    private static long Clock;
    internal static Task<byte[]> Get(string url, int limit = 2 * 1024 * 1024, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<byte[]>(token);
        if (limit <= 0 || limit > 16 * 1024 * 1024) return Task.FromException<byte[]>(new ArgumentOutOfRangeException(nameof(limit)));
        if (!ShopUrlPolicy.TryNormalize(url, out url)) return Task.FromException<byte[]>(new IOException("Untrusted store resource URL."));
        // The cap is part of the identity: a 16MB caller cannot bypass a 2MB caller's limit.
        string key = limit + ":" + url;
        Task<byte[]> job;
        lock (Gate)
        {
            Trim();
            if (Jobs.TryGetValue(key, out var found)) { found.Used = ++Clock; job = found.Job; }
            else
            {
                if (Jobs.Count >= MaxEntries) return Task.FromException<byte[]>(new IOException("Store resource queue is full; retry later."));
                var entry = new Entry { Used = ++Clock };
                string target = url;
                entry.Job = job = Task.Run(() => Read(target, limit));
                Jobs.Add(key, entry);
                _ = job.ContinueWith(t => { lock (Gate) Trim(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        // A subscriber may stop waiting, but must never cancel a shared download.
        return token.CanBeCanceled ? ShopNetwork.WithCancellation(job, token) : job;
    }
    private static void Trim()
    {
        long bytes = 0;
        var remove = new List<string>();
        foreach (var pair in Jobs)
        {
            if (pair.Value.Job.IsFaulted || pair.Value.Job.IsCanceled) { _ = pair.Value.Job.Exception; remove.Add(pair.Key); }
            else if (pair.Value.Job.IsCompletedSuccessfully) bytes += pair.Value.Job.Result.LongLength;
        }
        foreach (var key in remove) Jobs.Remove(key);
        while (Jobs.Count >= MaxEntries || bytes > MaxBytes)
        {
            string? oldest = null; long used = long.MaxValue;
            foreach (var pair in Jobs)
                if (pair.Value.Job.IsCompletedSuccessfully && pair.Value.Used < used) { oldest = pair.Key; used = pair.Value.Used; }
            if (oldest == null) break;
            bytes -= Jobs[oldest].Job.Result.LongLength; Jobs.Remove(oldest);
        }
    }
    private static Task<byte[]> Read(string url, int limit) => Downloads.Run(async token =>
    {
        using var client = ShopNetwork.Client();
        using var response = await ShopNetwork.Send(client, url, token).ConfigureAwait(false);
        return await ShopNetwork.ReadBody(response, limit, token).ConfigureAwait(false);
    });
}
