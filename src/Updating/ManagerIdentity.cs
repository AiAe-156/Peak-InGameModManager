#nullable enable
using System;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Updating;

/// <summary>
/// 管理器自身身份的唯一来源（插件、更新助手、进度窗共用这一份源文件）。
///
/// 2026-11-11 拆分（用户拍板）：完整版独立成新模组「游戏内模组管理器」（In-Game Mod Manager，IGMM），
/// 旧的「ModConfig Enhance」（MCE）回到简化版、仍用旧 GUID 与 <c>config\ModConfigEnhance\</c>。两者可以同时装（只提示）。
/// 所以：**写一律用新名，认（读取、判断）新旧都要认**——旧整合包、旧作者资料、旧版写下的队列与日志里都是旧名。
///
/// 内部目录名（<c>BepInEx\IGMM\</c>、作者资料夹 <c>IGMMPack</c>、环境文件 <c>IGMMEnvironment.json</c>、助手程序名）
/// 也一起换（用户选「连内部名称一起换」）。旧名只在这里出现，别处一律引用本类。
/// </summary>
internal static class ManagerIdentity
{
    // ── 插件 ──────────────────────────────────────────────
    internal const string Guid = "com.aiae.ingamemodmanager";
    /// <summary>旧完整版（2.x 内部开发版）与简化版 MCE 共用的 GUID。完整环境、旧整合包要求里的宿主仍可能是它。</summary>
    internal const string LegacyGuid = "com.aiae.modconfigenhance";
    internal const string Name = "In-Game Mod Manager";
    internal const string AssemblyName = "InGameModManager";
    internal const string LegacyAssemblyName = "ModConfigEnhance";
    internal const string DllName = AssemblyName + ".dll";
    internal const string PackageKey = "AiAeT-InGameModManager";
    internal const string LegacyPackageKey = "AiAeT-ModConfigEnhance";
    internal const string ConfigFileName = Guid + ".cfg";
    internal const string LegacyConfigFileName = LegacyGuid + ".cfg";

    /// <summary>
    /// 是不是**正在运行的**本管理器（只认新 GUID）。用户 2026-11-12：拆分后旧 GUID 属于简化版 MCE，是一个普通模组，
    /// 新装 MCE 时 IGMM 不能把它当成自己保护起来（不能停用、卸载、导入），也不能拿它当完整环境里的管理宿主。
    /// </summary>
    internal static bool IsManagerGuid(string? guid) => Same(guid, Guid);
    /// <summary>
    /// 旧整合包与旧完整环境里记下的宿主身份（新旧都认）：只用于「新管理器接替旧版宿主要求」，
    /// 例如旧包要求 <c>com.aiae.modconfigenhance &gt;= 2.0.0</c> 时由本管理器满足。
    /// </summary>
    internal static bool IsManagerSuccession(string? guid) => Same(guid, Guid) || Same(guid, LegacyGuid);
    /// <summary>
    /// 商店里的「本管理器自身」：只认新包键。复查 #14：旧包键 <c>AiAeT-ModConfigEnhance</c> 现在是简化版 MCE 的商店包，
    /// 是一个普通模组，可以在 IGMM 的商店里装、更新、卸载（同装时概览会提醒）。
    /// 依赖展开时仍跳过旧包键（<see cref="IsManagerDependency"/>）：旧整合包把它列为依赖，本管理器已经接替它，不该再拉一份 MCE 进来。
    /// </summary>
    internal static bool IsManagerPackage(string? key) => Same(key, PackageKey);
    internal static bool IsManagerDependency(string? key) => Same(key, PackageKey) || Same(key, LegacyPackageKey);
    internal static bool IsManagerAssembly(string? name) => Same(name, AssemblyName);
    internal static bool IsManagerDll(string? fileName) => Same(fileName, DllName);
    internal static bool IsManagerConfigFile(string? fileName) => Same(fileName, ConfigFileName) || Same(fileName, LegacyConfigFileName);

    // ── 个人数据 config\<ConfigDirectory>\ ─────────────────
    internal const string ConfigDirectory = "InGameModManager";
    internal const string LegacyConfigDirectory = "ModConfigEnhance";
    internal static bool IsConfigDirectory(string? name) => Same(name, ConfigDirectory) || Same(name, LegacyConfigDirectory);

    // ── 运行目录 BepInEx\<RuntimeDirectory>\（队列、整合包库、商店、工具缓存）─────
    internal const string RuntimeDirectory = "IGMM";
    internal const string LegacyRuntimeDirectory = "MCE";
    internal static bool IsRuntimeDirectory(string? name) => Same(name, RuntimeDirectory) || Same(name, LegacyRuntimeDirectory);
    /// <summary>
    /// <c>BepInEx\IGMM</c>。复查 #1：**旧在新不在时返回旧目录**（与 <see cref="MceDirectories"/> 同一个回退规则）。
    /// 改名被推迟或失败时，同一次启动里后面的代码（写锁、队列、整合包库）继续用 <c>BepInEx\MCE</c> 里的真实数据，
    /// 不会先建出一个空的 <c>BepInEx\IGMM</c>，否则下次启动改名看到目标已在就不做了，旧数据被永久孤立。
    /// 插件与助手共用这一份判断，两边永远看到同一个位置。
    /// </summary>
    internal static string Runtime(string bepInExRoot)
    {
        string target = Path.Combine(bepInExRoot, RuntimeDirectory), legacy = Path.Combine(bepInExRoot, LegacyRuntimeDirectory);
        try { return !Directory.Exists(target) && Directory.Exists(legacy) ? legacy : target; }
        catch { return target; }
    }
    internal static string Runtime(string bepInExRoot, params string[] parts) => Path.Combine(new[] { Runtime(bepInExRoot) }.Concat(parts).ToArray());
    /// <summary>新位置，不看旧目录在不在：只给迁移本身用。</summary>
    internal static string RuntimeTarget(string bepInExRoot) => Path.Combine(bepInExRoot, RuntimeDirectory);
    internal static string LegacyRuntime(string bepInExRoot) => Path.Combine(bepInExRoot, LegacyRuntimeDirectory);
    /// <summary>相对 BepInEx 的路径以运行目录开头（新旧都认），如 <c>IGMM/…</c>、<c>MCE/…</c>。</summary>
    internal static bool UnderRuntime(string relative)
    {
        string p = relative.Replace('\\', '/').TrimStart('/');
        int slash = p.IndexOf('/');
        return IsRuntimeDirectory(slash < 0 ? p : p.Substring(0, slash));
    }

    // ── 整合包作者资料夹 plugins\…\<PackFolder>\ ─────────
    internal const string PackFolder = "IGMMPack";
    internal const string LegacyPackFolder = "MCEPack";
    internal static bool IsPackFolder(string? name) => Same(name, PackFolder) || Same(name, LegacyPackFolder);
    /// <summary>路径里含 <c>/IGMMPack/</c> 或 <c>/MCEPack/</c>（统一用 / 分隔后比较）。</summary>
    internal static bool ContainsPackFolder(string path)
    {
        string p = "/" + path.Replace('\\', '/').Trim('/') + "/";
        return p.IndexOf("/" + PackFolder + "/", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("/" + LegacyPackFolder + "/", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    /// <summary><c>/IGMMPack/sub/</c> 或 <c>/MCEPack/sub/</c> 在路径里的位置与匹配长度；没有返回 -1。</summary>
    internal static int IndexOfPackSubfolder(string path, string sub, out int length)
    {
        foreach (string folder in new[] { PackFolder, LegacyPackFolder })
        {
            string marker = "/" + folder + "/" + sub.Trim('/') + "/";
            int at = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) { length = marker.Length; return at; }
        }

        length = 0;
        return -1;
    }
    /// <summary>plugins 下的整合包资料包目录名前缀（<c>IGMMPack_xxxx</c>，旧 <c>MCEPack_xxxx</c>）。</summary>
    internal static bool IsPackFolderPrefixed(string? name) => name != null
        && (name.StartsWith(PackFolder + "_", StringComparison.OrdinalIgnoreCase) || name.StartsWith(LegacyPackFolder + "_", StringComparison.OrdinalIgnoreCase));

    // ── 完整环境描述文件 ──────────────────────────────────
    internal const string EnvironmentFile = "IGMMEnvironment.json";
    internal const string LegacyEnvironmentFile = "MCEEnvironment.json";

    // ── 工具 ───────────────────────────────────────────────
    internal const string WorkerExecutable = "IGMM.UpdateWorker.exe";
    internal const string DesktopExecutable = "IGMM.Desktop.exe";
    internal const string RecoveryScript = "IGMM-Recovery.ps1";
    internal const string RecoveryEntry = "IGMM-Recovery.cmd";

    // ── 语言文件 ───────────────────────────────────────────
    /// <summary>管理器自己界面词条的键前缀。旧文件里是 <c>MCE_</c>，读取时两种都认，导出写新的。</summary>
    internal const string TermPrefix = "IGMM_";
    internal const string LegacyTermPrefix = "MCE_";
    /// <summary>快捷键短描述键的前缀，同理新旧都认。</summary>
    internal const string HotkeyPrefix = "IGMM.HotkeyDescription.";
    internal const string LegacyHotkeyPrefix = "ModConfigEnhance.HotkeyDescription.";

    private static bool Same(string? a, string b) => a != null && a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
