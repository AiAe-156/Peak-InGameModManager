#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal sealed class SigningKey
{
    public byte[]? Exponent, Modulus, D, DP, DQ, InverseQ, P, Q;
    internal static SigningKey From(RSAParameters key) => new() { Exponent = key.Exponent, Modulus = key.Modulus, D = key.D, DP = key.DP, DQ = key.DQ, InverseQ = key.InverseQ, P = key.P, Q = key.Q };
    internal RSAParameters Parameters() => new() { Exponent = Exponent, Modulus = Modulus, D = D, DP = DP, DQ = DQ, InverseQ = InverseQ, P = P, Q = Q };
}
internal sealed class PublishedPack
{
    internal string Root = "", Id = "", Version = "", DefaultProfile = "", Error = "";
    internal JObject Release = new();
    internal bool Valid => Error.Length == 0;
    private static List<PublishedPack>? _packs;
    internal static Func<IEnumerable<string>?>? AuthorRootsProvider;
    internal static Func<string>? RuntimeRootProvider;
    internal static Func<bool>? ManagedProvider;
    internal static string BootstrapPath => Path.Combine(RuntimeRootProvider?.Invoke() ?? Loc.RuntimeDirectory, "bootstrap.json");
    internal static void InvalidateCache() => _packs = null;
    internal static IReadOnlyList<PublishedPack> Packs => _packs ??= AuthorRootsProvider?.Invoke() is IEnumerable<string> roots
        ? roots.SelectMany(Discover).ToList() : Discover(Paths.PluginPath);
    internal static PublishedPack? Active => Packs.FirstOrDefault(p => p.Valid && p.Release.HasValues);
    internal static IEnumerable<string> LanguageDirectories => Packs.Where(p => p.Valid)
        .Select(p => Path.Combine(p.Root, "language")).Concat(ManagedProvider?.Invoke() == true && RuntimeRootProvider?.Invoke() is string runtime
            ? new[] { Path.Combine(Path.GetDirectoryName(runtime)!, "language") }
            : Array.Empty<string>()) // 未托管时个人目录就是 Loc.LanguageDirectory，Loc.Scan 自己会读，不重复加（2.5.4）
        .Where(Directory.Exists);
    internal static List<PublishedPack> Discover(string plugins)
    {
        var result = new List<PublishedPack>();
        foreach (string file in OverviewRepository.SafeFiles(plugins).Where(p => Path.GetFileName(p) == "profiles.json" && Path.GetFileName(Path.GetDirectoryName(p)) == "MCEPack"))
        {
            if (ArchivePath(file, plugins)) continue;
            var pack = new PublishedPack { Root = Path.GetDirectoryName(file)! };
            try
            {
                string release = Path.Combine(pack.Root, "release.json");
                if (File.Exists(release))
                {
                    pack.Release = JObject.Parse(File.ReadAllText(release, Encoding.UTF8));
                    pack.Id = pack.Release.Value<string>("packId") ?? "";
                    pack.Version = pack.Release.Value<string>("version") ?? "";
                    pack.DefaultProfile = pack.Release.Value<string>("defaultProfile") ?? "";
                    Verify(pack.Root, pack.Release.Value<bool?>("signed") == true);
                }
            }
            catch (Exception e) { pack.Error = e.Message; Plugin.Log.LogWarning("[发布包] " + file + "：" + e.Message); }
            result.Add(pack);
        }
        return result;
    }
    internal static bool CanRead(string directory)
    {
        if (ArchivePath(directory)) return false;
        string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return !Packs.Any(p => !p.Valid && full.StartsWith(Path.GetFullPath(p.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
    private static bool ArchivePath(string path, string? root = null)
    {
        string prefix = Path.GetFullPath(root ?? Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path); if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string relative = full.Substring(prefix.Length).Replace('\\', '/');
        return relative.IndexOf("Demo/profile-format/", StringComparison.OrdinalIgnoreCase) >= 0 || relative.Split('/').Any(p => p == "验证" || p == "备份" || p.StartsWith("待安装", StringComparison.OrdinalIgnoreCase));
    }
    internal static string Read(string root, string relative) => File.ReadAllText(OverviewProfileAssets.CheckedPath(root, relative), Encoding.UTF8);
    internal static string KeyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModConfigEnhance", "signing", "author-key.json");
    internal static void Sign(string root, string? keyPath = null)
    {
        keyPath ??= KeyPath;
        using var rsa = RSA.Create();
        if (File.Exists(keyPath)) rsa.ImportParameters((JsonConvert.DeserializeObject<SigningKey>(File.ReadAllText(keyPath)) ?? throw new InvalidDataException(Loc.T("The signing identity file is empty.", "签名身份文件为空。", "簽名身份文件為空。"))).Parameters());
        else
        {
            rsa.KeySize = 3072; Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            OverviewRepository.AtomicWrite(keyPath, JsonConvert.SerializeObject(SigningKey.From(rsa.ExportParameters(true)), Formatting.Indented));
        }
        var files = new JArray();
        foreach (string file in OverviewRepository.SafeFiles(root).Where(p => Path.GetFileName(p) != "signature.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
            using var stream = File.OpenRead(OverviewProfileAssets.CheckedPath(root, relative)); using var sha = SHA256.Create();
            files.Add(new JObject { ["path"] = relative, ["sha256"] = Convert.ToBase64String(sha.ComputeHash(stream)) });
        }
        byte[] bytes = Encoding.UTF8.GetBytes(files.ToString(Formatting.None));
        OverviewRepository.AtomicWrite(Path.Combine(root, "signature.json"), new JObject {
            ["algorithm"] = "RSA-SHA256-PKCS1", ["publicKey"] = JObject.FromObject(SigningKey.From(rsa.ExportParameters(false))),
            ["files"] = files, ["signature"] = Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        }.ToString(Formatting.Indented));
    }
    internal static void Verify(string root, bool required)
    {
        string path = Path.Combine(root, "signature.json");
        if (!File.Exists(path)) { if (required) throw new InvalidDataException(Loc.T("The publication package has no signature.", "发布包缺少签名。", "發布包缺少簽名。")); return; }
        JObject data = JObject.Parse(File.ReadAllText(path));
        if (data.Value<string>("algorithm") != "RSA-SHA256-PKCS1" || data["files"] is not JArray files || data["publicKey"] == null) throw new InvalidDataException(Loc.T("Invalid publication signature format.", "发布签名格式无效。", "發布簽名格式無效。"));
        using var rsa = RSA.Create(); rsa.ImportParameters((data["publicKey"]!.ToObject<SigningKey>() ?? throw new InvalidDataException(Loc.T("The signing public key is empty.", "签名公钥为空。", "簽名公鑰為空。"))).Parameters());
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(files.ToString(Formatting.None)), Convert.FromBase64String(data.Value<string>("signature") ?? ""), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException(Loc.T("Publication signature verification failed.", "发布签名验证失败。", "發布簽名驗證失敗。"));
        var signed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject item in files.OfType<JObject>())
        {
            string relative = item.Value<string>("path") ?? "";
            if (!signed.Add(relative)) throw new InvalidDataException(Loc.T("The signature manifest contains duplicate paths.", "签名清单存在重复路径。", "簽名清單存在重復路徑。"));
            using var stream = File.OpenRead(OverviewProfileAssets.CheckedPath(root, relative)); using var sha = SHA256.Create();
            if (Convert.ToBase64String(sha.ComputeHash(stream)) != item.Value<string>("sha256")) throw new InvalidDataException(Loc.F("A published file has changed: {0}", "发布文件已改变：{0}", "發布文件已改變：{0}", relative));
        }
        foreach (string file in OverviewRepository.SafeFiles(root).Where(p => Path.GetFileName(p) != "signature.json"))
            if (!signed.Contains(file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'))) throw new InvalidDataException(Loc.F("The publication package contains an unsigned file: {0}", "发布包出现未签名文件：{0}", "發布包出現未簽名文件：{0}", Path.GetFileName(file)));
    }
    internal static JObject CaptureDefaults(ConfigFile config)
    {
        var result = new JObject();
        foreach (var pair in config)
            if (pair.Key.Section != "3. State" && pair.Key.Key != "Manager profile directory" && pair.Key.Key != "Allow editing user guide")
                result[pair.Key.Section + "\n" + pair.Key.Key] = pair.Value.GetSerializedValue();
        return result;
    }
    internal static void InitializeDefaults(ConfigFile config)
    {
        var pack = Active; if (pack == null || pack.Id.Length == 0) return;
        string file = BootstrapPath;
        JObject state; try { state = File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : new(); } catch { return; }
        if (state.Value<string>("packId") == pack.Id) return;
        // An existing personal installation is retained when adopting the publication format.
        // 2.5.4（二次审查）：2.5.2 起未托管时 bootstrap.json 在 data/runtime，往上两级是 data/ 而不是 ModConfigEnhance/，
        // 原写法永远找不到 profiles/index.json，把已有个人安装当成新装、套用作者默认方案。按托管 / 未托管分别取方案索引。
        string profilesIndex = ManagedProvider?.Invoke() == true
            ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(file))!, "profiles", "index.json")
            : Path.Combine(Updating.MceDirectories.Profiles(Loc.RootDirectory), "index.json");
        bool existing = File.Exists(profilesIndex);
        if (!existing && ManagedProvider?.Invoke() != true && pack.Release["enhanceDefaults"] is JObject defaults)
            foreach (var pair in config)
            {
                if (pair.Key.Section == "3. State" || pair.Key.Key == "Manager profile directory" || pair.Key.Key == "Allow editing user guide") continue;
                string? value = defaults.Value<string>(pair.Key.Section + "\n" + pair.Key.Key);
                if (value != null) try { pair.Value.SetSerializedValue(value); } catch (Exception e) { Plugin.Log.LogWarning("[作者设置] " + pair.Key + "：" + e.Message); }
            }
        state["packId"] = pack.Id; state["profileApplied"] = existing;
        OverviewRepository.AtomicWrite(file, state.ToString(Formatting.Indented));
    }
}
