# -*- coding: utf-8 -*-
"""新装 MCE 不出问题：IGMM 里「是不是管理器自身」的判断改为只认新 GUID / 新 DLL；旧 GUID 只在「接替旧整合包要求」时认。
用法：python -I fix_mce_coexist.py"""
import io
import os

IG = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def sub(rel, old, new, n=1):
    p = os.path.join(IG, *rel.split("/"))
    raw = io.open(p, "rb").read(); bom = raw.startswith(b"\xef\xbb\xbf")
    t = raw.decode("utf-8-sig"); crlf = "\r\n" in t; t = t.replace("\r\n", "\n")
    assert t.count(old) == n, (rel, old[:90], t.count(old))
    t = t.replace(old, new)
    if crlf: t = t.replace("\n", "\r\n")
    io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + t.encode("utf-8"))


sub("src/Updating/ManagerIdentity.cs",
    "    /// <summary>是不是管理器本身（新旧完整版都算；简化版 MCE 与旧完整版同 GUID，按 GUID 无法区分，一并当作管理器保护）。</summary>\n    internal static bool IsManagerGuid(string? guid) => Same(guid, Guid) || Same(guid, LegacyGuid);",
    """    /// <summary>
    /// 是不是**正在运行的**本管理器（只认新 GUID）。用户 2026-11-12：拆分后旧 GUID 属于简化版 MCE，是一个普通模组，
    /// 新装 MCE 时 IGMM 不能把它当成自己保护起来（不能停用、卸载、导入），也不能拿它当完整环境里的管理宿主。
    /// </summary>
    internal static bool IsManagerGuid(string? guid) => Same(guid, Guid);
    /// <summary>
    /// 旧整合包与旧完整环境里记下的宿主身份（新旧都认）：只用于「新管理器接替旧版宿主要求」，
    /// 例如旧包要求 <c>com.aiae.modconfigenhance &gt;= 2.0.0</c> 时由本管理器满足。
    /// </summary>
    internal static bool IsManagerSuccession(string? guid) => Same(guid, Guid) || Same(guid, LegacyGuid);""")
sub("src/Updating/ManagerIdentity.cs",
    "    internal static bool IsManagerAssembly(string? name) => Same(name, AssemblyName) || Same(name, LegacyAssemblyName);\n    internal static bool IsManagerDll(string? fileName) => Same(fileName, DllName) || Same(fileName, LegacyAssemblyName + \".dll\");",
    """    internal static bool IsManagerAssembly(string? name) => Same(name, AssemblyName);
    internal static bool IsManagerDll(string? fileName) => Same(fileName, DllName);""")

# 旧整合包宿主要求：仍按「接替」认新旧
sub("Updater/PackImports.cs", "?? (ManagerIdentity.IsManagerGuid(plugin.Guid) ? request.HostPlugins.FirstOrDefault(h => ManagerIdentity.IsManagerGuid(h.Guid)) : null);",
    "?? (ManagerIdentity.IsManagerSuccession(plugin.Guid) ? request.HostPlugins.FirstOrDefault(h => ManagerIdentity.IsManagerGuid(h.Guid)) : null);")
sub("Updater/PackImports.cs", "bool managerSuccession = ManagerIdentity.IsManagerGuid(plugin.Guid) && !Files.Compare.Equals(host.Guid, plugin.Guid);",
    "bool managerSuccession = ManagerIdentity.IsManagerSuccession(plugin.Guid) && !Files.Compare.Equals(host.Guid, plugin.Guid);")
sub("Updater/PackImports.cs", "&& !ManagerIdentity.IsManagerGuid(p.Guid) && request.HostPlugins.All(",
    "&& !ManagerIdentity.IsManagerSuccession(p.Guid) && request.HostPlugins.All(")
sub("Updater/PackTransactions.cs", "if (ManagerIdentity.IsManagerGuid(requirement.Guid) && hosts.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) continue;",
    "if (ManagerIdentity.IsManagerSuccession(requirement.Guid) && hosts.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) continue;")
# 旧索引（接管时记下的 HostPlugins）可能还是旧 GUID：接管校验认接替身份
sub("Updater/PackTransactions.cs", "if (index.HostRoots.Count == 0 || !index.HostPlugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) throw",
    "if (index.HostRoots.Count == 0 || !index.HostPlugins.Any(p => ManagerIdentity.IsManagerSuccession(p.Guid))) throw")
print("ok")
