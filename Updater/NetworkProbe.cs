using System.Diagnostics;
using System.Text.RegularExpressions;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class NetworkProbe
{
    static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(12);
    static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(25);

    internal static async Task Run(string root, string id)
    {
        if (!Regex.IsMatch(id, "^[a-fA-F0-9]{32}$")) throw new IOException("探测编号必须为 32 位十六进制字符。");
        Files.ValidateRoot(root);
        string game = Directory.GetParent(root)!.FullName;
        string home = Path.Combine(root, "MCE");
        string directory = Path.Combine(home, "probes");
        Directory.CreateDirectory(directory);
        Files.RejectLinks(game, home);
        Files.RejectLinks(game, directory);
        string requestPath = Files.Child(directory, id + ".request.json");
        string resultPath = Files.Child(directory, id + ".result.json");
        Files.RejectLinks(game, requestPath);
        Files.RejectLinks(game, resultPath);

        var result = new ProbeResult();
        try
        {
            var request = Files.Read<ProbeRequest>(requestPath);
            Validate(request);
            using var total = new CancellationTokenSource(TotalTimeout);
            using var client = new StoreClient(request.Network);
            StoreClient.StoreVersion resolved;
            var timer = Stopwatch.StartNew();
            try
            {
                using var step = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                step.CancelAfter(StepTimeout);
                resolved = await client.Resolve(new UpdateJob { Namespace = request.Namespace, Package = request.Package }, "", step.Token);
                result.Store.Success = true;
            }
            catch (Exception error)
            {
                result.Store.Error = UpdateMessages.Encode(Describe(error, total.Token));
                result.Download.Error = UpdateMessages.Encode("商店探测失败，无法获取下载地址。");
                result.Store.Milliseconds = timer.ElapsedMilliseconds;
                result.Complete = true;
                Files.Save(resultPath, result);
                return;
            }
            result.Store.Milliseconds = timer.ElapsedMilliseconds;

            timer.Restart();
            try
            {
                using var step = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                step.CancelAfter(StepTimeout);
                await client.ProbeDownload(resolved.DownloadUrl, step.Token);
                result.Download.Success = true;
            }
            catch (Exception error) { result.Download.Error = UpdateMessages.Encode(Describe(error, total.Token)); }
            result.Download.Milliseconds = timer.ElapsedMilliseconds;
        }
        catch (Exception error)
        {
            if (result.Store.Error.Length == 0 && !result.Store.Success) result.Store.Error = UpdateMessages.Encode(error.Message);
            if (result.Download.Error.Length == 0 && !result.Download.Success) result.Download.Error = UpdateMessages.Encode(error.Message);
        }
        result.Complete = true;
        Files.Save(resultPath, result);
    }

    static void Validate(ProbeRequest request)
    {
        if (request.Network == null) throw new IOException("缺少联网设置。");
        if (string.IsNullOrWhiteSpace(request.Namespace) || request.Namespace.Length > 128
            || string.IsNullOrWhiteSpace(request.Package) || request.Package.Length > 128)
            throw new IOException("包的命名空间或名称无效。");
    }

    static string Describe(Exception error, CancellationToken total)
    {
        if (total.IsCancellationRequested || error is OperationCanceledException)
            return "网络探测超时（单步最多 12 秒，总计最多 25 秒）。";
        return string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    }
}
