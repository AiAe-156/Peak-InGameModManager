#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal static class PackLibrary
{
    internal static string Home => Path.Combine(Paths.BepInExRootPath, "MCE", "packs");
    internal static string TaskPath => Path.Combine(Paths.BepInExRootPath, "MCE", "pack-task.json");
    private static string _indexStamp = "", _taskStamp = "";
    private static PackIndex? _indexCache;
    private static PackTask? _taskCache;
    private static string Stamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks : path + "|missing";
    }
    internal static PackIndex Read()
    {
        string file = Path.Combine(Home, "index.json"), stamp = Stamp(file);
        if (_indexCache != null && stamp == _indexStamp) return _indexCache;
        if (!File.Exists(file)) { _indexStamp = stamp; return _indexCache = new(); }
        var index = JsonConvert.DeserializeObject<PackIndex>(ReadText(file)) ?? throw new InvalidDataException("整合包索引为空。");
        if ((index.SchemaVersion != 1 && index.SchemaVersion != 2) || index.MinimumWorkerProtocol < 1 || index.MinimumWorkerProtocol > 2 || index.SchemaVersion == 2 && index.MinimumWorkerProtocol != 2 || index.Packs == null || index.ActiveFiles == null || index.Packs.Any(p => !ValidId(p.Id))
            || index.Packs.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
            || index.ActivePackId.Length > 0 && index.Packs.All(p => p.Id != index.ActivePackId)) throw new InvalidDataException("整合包索引损坏，请先恢复备份。");
        foreach (var parent in index.Packs)
        foreach (var record in new[] { parent, parent.PendingUpdate, parent.UnsavedEnvironment }.Where(p => p != null))
        {
            if (record!.EnvironmentVersion != 1 && record.EnvironmentVersion != 2 || record.MinimumWorkerProtocol < 1 || record.MinimumWorkerProtocol > 2
                || record.EnvironmentVersion == 2 && (!PackEnvironmentPolicy.IsComplete(record) || index.SchemaVersion != 2 || string.IsNullOrEmpty(record.RevisionStateHash))
                || record.EnvironmentVersion == 1 && !string.IsNullOrEmpty(record.RevisionStateHash)) throw new InvalidDataException("整合包环境协议或版本状态标记无效。");
        }
        _indexStamp = stamp; _indexCache = index;
        return index;
    }
    internal static PackRecord? Active { get { var index = Read(); return index.Packs.FirstOrDefault(p => p.Id == index.ActivePackId); } }
    internal static bool Managed => Active != null;
    internal static string Workspace => Active is PackRecord pack ? OverviewProfileAssets.CheckedPath(Home, pack.Id + "/workspace") : Loc.RootDirectory;
    internal static string ProfileRoot => Path.Combine(Workspace, "profiles");
    /// <summary>整合包工作区是 workspace/runtime；没有激活整合包时是 config/ModConfigEnhance/data/runtime（2.5.2）。</summary>
    internal static string RuntimeRoot => Managed ? Path.Combine(Workspace, "runtime") : Loc.RuntimeDirectory;
    internal static IEnumerable<string>? AuthorRoots()
    {
        var index = Read(); if (index.ActivePackId.Length == 0) return null;
        return index.ActiveFiles.Where(f => Path.GetFileName(f.Path).Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(f.Path)), "MCEPack", StringComparison.OrdinalIgnoreCase))
            .Select(f => OverviewProfileAssets.CheckedPath(Paths.BepInExRootPath, Path.GetDirectoryName(f.Path)!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static PackTask? Task()
    {
        string stamp = Stamp(TaskPath);
        if (stamp == _taskStamp) return _taskCache;
        if (!File.Exists(TaskPath)) { _taskStamp = stamp; return _taskCache = null; }
        var task = JsonConvert.DeserializeObject<PackTask>(ReadText(TaskPath));
        if (task == null || task.SchemaVersion != 1 || !ValidId(task.Id)) throw new InvalidDataException("整合包任务损坏，请先恢复备份。");
        _taskStamp = stamp; _taskCache = task;
        return task;
    }
    internal static void EnsureAvailable()
    {
        Read();
        if (Task()?.BlocksUpdates == true) throw new InvalidOperationException("已安排整合包切换或恢复，请退出游戏，等待助手完成后再启动。");
    }
    internal static void ResumeWorker()
    {
        try
        {
            if (Task()?.State is "requested" or "preparing" or "waiting-exit" or "switching" or "recovery-required") OverviewUpdateQueue.EnsureWorker();
        }
        catch (Exception error) { Plugin.Log.LogWarning("[整合包恢复] " + error.Message); }
    }
    internal static PackRequest Request(string operation, OverviewRepository repo, string source = "", string packId = "")
    {
        // Host roots are solely a legacy compatibility mechanism, not providers for a complete target.
        var target = Read().Packs.FirstOrDefault(p => p.Id == (packId.Length > 0 ? packId : Read().ActivePackId));
        if (operation.Contains("adopt-environment") || operation.Contains("upgrade-environment") || target != null && PackEnvironmentPolicy.IsComplete(target.PendingUpdate ?? target) && !operation.Contains("import"))
            return new PackRequest { Operation = operation, Source = source, PackId = packId, GameLanguage = Loc.FollowStem() };
        var mods = new OverviewCatalogue().Scan(repo);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Plugin.PluginGuid };
        bool changed;
        do
        {
            changed = false;
            foreach (var info in Chainloader.PluginInfos.Values.Where(p => required.Contains(p.Metadata.GUID)).ToArray())
                foreach (var dependency in info.Dependencies.Where(d => (d.Flags & BepInDependency.DependencyFlags.SoftDependency) == 0))
                    changed |= required.Add(dependency.DependencyGUID);
            var roots = new HashSet<string>(mods.Where(m => required.Contains(m.Guid)).Select(m => m.PackageRoot), StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods.Where(m => roots.Contains(m.PackageRoot))) foreach (string guid in mod.PluginGuids.Count > 0 ? mod.PluginGuids : new List<string> { mod.Guid }) changed |= required.Add(guid);
        } while (changed);
        var missing = required.Where(g => !Chainloader.PluginInfos.ContainsKey(g)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("固定宿主依赖未加载：" + string.Join(", ", missing));
        string root = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var request = new PackRequest { Operation = operation, Source = source, PackId = packId, GameLanguage = Loc.FollowStem() };
        foreach (string guid in required)
        {
            var info = Chainloader.PluginInfos[guid]; request.HostPlugins.Add(new PackHostPlugin { Guid = guid, Version = info.Metadata.Version.ToString() });
            var mod = mods.FirstOrDefault(m => m.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(guid, StringComparer.OrdinalIgnoreCase));
            string package = Path.GetFullPath(mod?.PackageRoot ?? info.Location);
            if (!package.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("宿主文件不在当前 BepInEx 下：" + guid);
            request.HostRoots.Add(package.Substring(root.Length).Replace('\\', '/'));
        }
        request.HostRoots = request.HostRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return request;
    }
    internal static bool ValidId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit);
    private static string ReadText(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(input); return reader.ReadToEnd();
    }
}
