#nullable enable
using System;
using System.IO;

namespace ModConfigEnhance.Updating;

/// <summary>
/// <c>BepInEx\config\ModConfigEnhance\</c> 下所有子目录的唯一来源（插件与更新助手共用，链接同一份源文件）。
/// 用户 2026-11-11 拍板的四大类：
/// <code>
/// language\          语言文件
/// profiles\          配置方案、整合包介绍与素材（位置不变；同级的 pack-information.json 也不动）
/// output\translation 导出翻译、补漏翻
/// output\packs       整合包导出、介绍导出
/// output\logs        日志导出兜底
/// data\runtime       运行状态、商店缓存（原 runtime\）
/// data\backups       全部自动备份（原 backups\ 与 backup\）
/// data\help          使用说明本地覆盖（原 help\）
/// data\legacy        10 月前的旧版概览数据与个人副本资源（原 overview\）
/// readme.md
/// </code>
/// 2.5.4（二次审查）：**逐个目录回退**。某个旧目录还在、新目录还没有时，该目录的路径继续返回旧位置——
/// 搬家失败或还没搬时，代码读写的仍是用户真实数据，不会在新位置另起一份空的（旧做法失败后会新建默认文件，
/// 重试时把真实数据挤成 .old-layout）。搬家由插件的 <c>MceDirectoryMigration</c> 完成，搬完一个目录该目录即切到新位置。
/// 助手（<c>MCE.UpdateWorker</c>）用同一套判断，所以两边看到的永远是同一个位置。
/// </summary>
internal static class MceDirectories
{
    internal const string LanguageName = "language", ProfilesName = "profiles", OutputName = "output", DataName = "data";

    /// <param name="root"><c>…\config\ModConfigEnhance</c></param>
    internal static string Language(string root) => Path.Combine(root, LanguageName);
    internal static string Profiles(string root) => Path.Combine(root, ProfilesName);
    internal static string Output(string root) => Path.Combine(root, OutputName);
    internal static string TranslationOutput(string root) => Resolve(root, "translation-export", OutputName + "/translation");
    internal static string PackOutput(string root) => Resolve(root, "exports", OutputName + "/packs");
    internal static string LogOutput(string root) => Path.Combine(root, OutputName, "logs");
    internal static string Data(string root) => Path.Combine(root, DataName);
    internal static string Runtime(string root) => Resolve(root, "runtime", DataName + "/runtime");
    internal static string Backups(string root) => Resolve(root, "backups", DataName + "/backups");
    internal static string Help(string root) => Resolve(root, "help", DataName + "/help");
    internal static string Legacy(string root) => Resolve(root, "overview", DataName + "/legacy");

    /// <summary>新位置（不看旧目录在不在）：搬家和核对用。</summary>
    internal static string Target(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>旧目录在、新目录不在 → 还没搬（或搬失败了），继续用旧目录；其余一律新目录。</summary>
    private static string Resolve(string root, string oldName, string newName)
    {
        string target = Target(root, newName), old = Path.Combine(root, oldName);
        try { return !Directory.Exists(target) && Directory.Exists(old) ? old : target; }
        catch { return target; }
    }

    /// <summary>从 BepInEx 根（助手的 <c>--root</c>）算 config\ModConfigEnhance。</summary>
    internal static string RootFromBepInEx(string bepInExRoot) => Path.Combine(bepInExRoot, "config", "ModConfigEnhance");

    /// <summary>旧布局 → 新布局（相对 config\ModConfigEnhance）。顺序有意义：先搬整目录，再并入。</summary>
    internal static readonly (string Old, string New)[] Moves =
    {
        ("runtime", DataName + "/runtime"),
        ("backups", DataName + "/backups"),
        ("backup", DataName + "/backups/legacy-backup"),
        ("help", DataName + "/help"),
        ("overview", DataName + "/legacy"),
        ("exports", OutputName + "/packs"),
        ("translation-export", OutputName + "/translation"),
        ("export", OutputName + "/translation"),
        ("log-favorites.json", DataName + "/runtime/log-favorites.json"),
        ("shop-favorites.json", DataName + "/runtime/shop-favorites.json"),
    };

    /// <summary>把旧绝对路径改到新布局；不在 config\ModConfigEnhance 下或已是新布局的原样返回。</summary>
    internal static string Remap(string root, string path)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch { return path; }
        string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return path;
        string relative = full.Substring(prefix.Length).Replace('\\', '/');
        foreach (var (oldName, newName) in Moves)
        {
            if (relative.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                return Target(root, newName);
            if (relative.StartsWith(oldName + "/", StringComparison.OrdinalIgnoreCase))
                return Target(root, newName + relative.Substring(oldName.Length));
        }

        return path;
    }
}
