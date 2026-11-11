#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Updating;

/// <summary>
/// 2.4.4：同一个商店包在磁盘上有多份 manifest.json 时，选出「说了算」的那份，其余的标成旧说明文件残留。
///
/// 背景（2026-11-10 实机）：MonoDetour 的 core\MonoDetour-MonoDetour\manifest.json 是 09-09 手装留下的 0.7.16，
/// 商店更新写的是 plugins\MonoDetour-MonoDetour 那份（0.7.1701）。基础组件扫描读 core 那份，于是每次都提示
/// 「0.7.16 → 0.7.1701」，点更新后助手判定无文件变化、记成功，提醒永远不消。
/// 同类还有 Profile_Cards：包根是 0.1.5，嵌套 plugins\ 里是 0.1.7，DLL 是 0.1.7.0。
///
/// 规则（纯逻辑，插件扫描与更新助手共用）：
///   · 只比较同一个包键（Team-Name）的副本；
///   · 版本取所有副本里**最高**的那份——更新只会写入新版，留下的低版本说明文件就是残留；
///   · 版本解析不了的副本不参与取最高、也不算残留（交给原有的「清单无效」路径）。
/// </summary>
public static class ManifestCopies
{
    public sealed class Copy
    {
        public string Key = "";
        public string Version = "";
        public string ManifestPath = "";
    }

    public sealed class Verdict
    {
        public string Key = "";
        /// <summary>说了算的版本（最高）。</summary>
        public string Version = "";
        public string Authoritative = "";
        /// <summary>版本低于 <see cref="Version"/> 的副本，即旧说明文件残留。</summary>
        public List<Copy> Stale = new List<Copy>();
        public bool HasStale => Stale.Count > 0;
    }

    /// <summary>按包键分组，每组给出结论；只有一份或版本全同的组也返回（Stale 为空）。</summary>
    public static IReadOnlyList<Verdict> Resolve(IEnumerable<Copy> copies)
    {
        var result = new List<Verdict>();
        foreach (var group in copies.Where(c => c.Key.Length > 0).GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = group.Where(c => TryVersion(c.Version, out _)).ToList();
            if (parsed.Count == 0)
            {
                continue;
            }

            Copy best = parsed[0];
            foreach (var copy in parsed.Skip(1))
            {
                if (Compare(copy.Version, best.Version) > 0)
                {
                    best = copy;
                }
            }

            result.Add(new Verdict
            {
                Key = group.Key,
                Version = best.Version,
                Authoritative = best.ManifestPath,
                Stale = parsed.Where(c => !ReferenceEquals(c, best) && Compare(c.Version, best.Version) < 0).ToList(),
            });
        }

        return result;
    }

    /// <summary>
    /// 读一份 manifest.json 的包键与版本（只要 namespace/author + name + version_number；文件夹名 Team-Name 可补 namespace）。
    /// 读不出返回 null。插件侧与助手侧都用它，避免两边各自解析出不同的键。
    /// </summary>
    public static Copy? Read(string manifestPath, Func<string, string> readAllText)
    {
        string text;
        try
        {
            text = readAllText(manifestPath);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }

        string name = Field(text, "name"), team = Field(text, "namespace"), version = Field(text, "version_number");
        if (team.Length == 0)
        {
            team = Field(text, "author");
        }

        if (team.Length == 0)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? "") ?? "";
            string[] parts = folder.Split('-');
            if (parts.Length == 2 && parts[1].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                team = parts[0];
            }
        }

        if (!Valid(team) || !Valid(name) || !TryVersion(version, out _))
        {
            return null;
        }

        return new Copy { Key = team + "-" + name, Version = version, ManifestPath = manifestPath };
    }

    /// <summary>
    /// 2.4.4 对账兜底：同一个包已经有一条「完成 / 等待重启」的更新记录装到了 <paramref name="completedVersion"/>，
    /// 而扫描读到的版本仍低于它——说明显示的版本来自另一份没被更新到的文件。此时不该再提示更新同一版本。
    /// 返回 true 表示「版本信息不一致」：调用方应取消这条更新提示，改为提示不一致。
    /// </summary>
    public static bool Contradicts(string scannedVersion, string latestVersion, string completedVersion)
    {
        if (!TryVersion(scannedVersion, out _) || !TryVersion(completedVersion, out _) || !TryVersion(latestVersion, out _)) return false;
        return Compare(completedVersion, scannedVersion) > 0 && Compare(latestVersion, completedVersion) <= 0;
    }

    private static bool Valid(string value) => value.Length > 0 && Regex.IsMatch(value, "^[A-Za-z0-9_]+$");

    /// <summary>只取顶层字符串字段；manifest 是扁平 JSON，这里不需要完整解析器（助手侧没有 Newtonsoft）。</summary>
    private static string Field(string json, string name)
    {
        var match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    internal static bool TryVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        string core = (value ?? "").Split('-', '+')[0].Trim();
        return core.Length > 0 && System.Version.TryParse(core.IndexOf('.') >= 0 ? core : core + ".0", out version!);
    }

    /// <summary>数字段比较；同数字段时无预发布标记的更大（与 ThunderstoreUpdateService.IsNewer 一致的方向）。</summary>
    public static int Compare(string left, string right)
    {
        TryVersion(left, out var a);
        TryVersion(right, out var b);
        int compare = a.CompareTo(b);
        if (compare != 0)
        {
            return compare;
        }

        bool ap = left.IndexOf('-') >= 0, bp = right.IndexOf('-') >= 0;
        return ap == bp ? 0 : ap ? -1 : 1;
    }
}
