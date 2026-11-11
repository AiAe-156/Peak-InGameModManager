#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Diagnostics;

internal sealed class LogExportSource
{
    internal string Name = "", Path = "";
    internal MceLogRecord[]? Records;
}
internal static class LogExport
{
    internal static void Save(string destination, IReadOnlyList<LogExportSource> sources, JObject? metadata)
    {
        if (sources.Count == 0) throw new IOException("没有选择日志来源。");
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        var handles = new List<(LogExportSource Source, FileStream Stream, long Length, DateTime Created, byte[] Prefix, byte[] Tail)>();
        var started = DateTimeOffset.Now;
        try
        {
            foreach (var source in sources.Where(s => s.Records == null))
            {
                var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                try
                {
                    long length = stream.Length; byte[] prefix = new byte[(int)Math.Min(256, length)]; LogParser.ReadPrefix(stream, prefix); stream.Position = 0;
                    byte[] tail = new byte[(int)Math.Min(256, length)]; stream.Position = length - tail.Length; LogParser.ReadPrefix(stream, tail); stream.Position = 0;
                    handles.Add((source, stream, length, File.GetCreationTimeUtc(source.Path), prefix, tail));
                }
                catch { stream.Dispose(); throw; }
            }
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var source in sources)
                {
                    var entry = zip.CreateEntry(source.Name, CompressionLevel.Optimal);
                    using var output = entry.Open();
                    if (source.Records != null)
                    {
                        using var writer = new StreamWriter(output, new UTF8Encoding(false), 65536, true);
                        foreach (var record in source.Records) writer.WriteLine(LogParser.Plain(record));
                    }
                    else
                    {
                        var h = handles.Single(h => ReferenceEquals(h.Source, source)); byte[] buffer = new byte[65536]; long left = h.Length;
                        while (left > 0) { int n = h.Stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left)); if (n == 0) throw new IOException("采集期间日志被截断：" + source.Name); output.Write(buffer, 0, n); left -= n; }
                        using var check = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        byte[] prefix = new byte[h.Prefix.Length]; int count = LogParser.ReadPrefix(check, prefix);
                        byte[] tail = new byte[h.Tail.Length]; if (check.Length >= h.Length) check.Position = h.Length - tail.Length;
                        int tailCount = LogParser.ReadPrefix(check, tail);
                        if (check.Length < h.Length || File.GetCreationTimeUtc(source.Path) != h.Created || count != prefix.Length || !prefix.SequenceEqual(h.Prefix) || tailCount != tail.Length || !tail.SequenceEqual(h.Tail)) throw new IOException("采集期间日志已轮换，请重试：" + source.Name);
                    }
                }
                if (metadata != null)
                {
                    metadata["collection"] = new JObject { ["started"] = started, ["completed"] = DateTimeOffset.Now, ["scope"] = "所选来源采集开始时的完整可用日志；不受搜索筛选影响", ["sources"] = new JArray(sources.Select(s => new JObject { ["file"] = s.Name, ["source"] = RedactPath(s.Path), ["bytes"] = s.Records == null ? handles.Single(h => ReferenceEquals(h.Source, s)).Length : 0, ["records"] = s.Records?.Length })) };
                    using var writer = new StreamWriter(zip.CreateEntry("运行信息.json").Open(), new UTF8Encoding(false)); writer.Write(metadata.ToString());
                }
            }
            if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
        }
        finally
        {
            foreach (var h in handles) h.Stream.Dispose();
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    internal static string RedactPath(string path)
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return user.Length > 0 ? path.Replace(user, "%USERPROFILE%") : path;
    }
    internal static bool Sensitive(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, @"password|passwd|secret|token|api[ _-]?key|authorization|credential|密码|密钥|令牌", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    internal static JToken Scrub(JToken token, string key = "")
    {
        if (Sensitive(key)) return new JValue("[已遮盖]");
        if (token is JObject obj)
        {
            bool sensitiveRecord = obj.Properties().Any(p => (p.Name is "key" or "section" or "name") && Sensitive(p.Value.ToString()))
                || obj["target"] is JObject target && target.Properties().Any(p => (p.Name is "key" or "section") && Sensitive(p.Value.ToString()));
            var copy = new JObject(); foreach (var p in obj.Properties()) copy[p.Name] = sensitiveRecord && (p.Name is "value" or "previous" or "current" or "serializedValue" or "target" or "default") ? new JValue("[已遮盖]") : Scrub(p.Value, p.Name); return copy;
        }
        if (token is JArray array) return new JArray(array.Select(t => Scrub(t)));
        return token.Type == JTokenType.String ? new JValue(RedactPath(token.ToString())) : token.DeepClone();
    }
}
