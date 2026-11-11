#nullable enable
using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using ModConfigEnhance.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewRepository
{
    // Called on the game thread after the destination was selected. Compression never touches Unity objects.
    internal JObject CaptureDiagnosticMetadata(System.Collections.Generic.IReadOnlyList<OverviewMod> installedMods)
    {
        var applied = _appliedSnapshot;
        var current = Configs.Capture();
        var differences = applied == null ? new JArray() : new JArray(Configs.Diff(applied.config, current).Where(d => d.Kind != OverviewConfigDiffKind.Equal).Select(d => new JObject {
            ["kind"] = d.Kind.ToString(), ["pluginGuid"] = (d.ProfileValue ?? d.CurrentValue)?.pluginGuid,
            ["section"] = (d.ProfileValue ?? d.CurrentValue)?.section, ["key"] = (d.ProfileValue ?? d.CurrentValue)?.key,
            ["previous"] = d.ProfileValue?.serializedValue, ["current"] = d.CurrentValue?.serializedValue }));
        var saved = Profiles.Applied;
        var savedProfiles = new JArray(Profiles.ListProfiles().Where(p => p.IsValid && p.id != Profiles.AppliedId).Select(p => new JObject {
            ["id"] = p.id, ["name"] = p.name, ["status"] = "已保存但未应用；不是当前运行比较基准",
            ["config"] = JObject.FromObject(p.config), ["mods"] = JArray.FromObject(p.mods) }));
        var savedDiff = applied == null ? new JArray() : new JArray(Configs.Diff(applied.config, saved.config).Where(d => d.Kind != OverviewConfigDiffKind.Equal).Select(d => new JObject {
            ["pluginGuid"] = (d.ProfileValue ?? d.CurrentValue)?.pluginGuid, ["section"] = (d.ProfileValue ?? d.CurrentValue)?.section,
            ["key"] = (d.ProfileValue ?? d.CurrentValue)?.key, ["previous"] = d.ProfileValue?.serializedValue, ["current"] = d.CurrentValue?.serializedValue }));
        var mods = installedMods;
        var metadata = new JObject {
            ["schemaVersion"] = 1, ["purpose"] = "仅用于 Debug 和故障排查", ["captured"] = DateTimeOffset.Now,
            ["gameVersion"] = Application.version, ["unityVersion"] = Application.unityVersion,
            ["bepInExVersion"] = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString(),
            ["mceVersion"] = typeof(Plugin).Assembly.GetName().Version?.ToString(),
            ["pack"] = PackLibrary.Active?.Name ?? State.pack.name, ["appliedProfile"] = applied?.name ?? "",
            ["comparisonBaseline"] = applied == null ? "无比较基准" : "当前已应用方案快照",
            ["runtimeDifferences"] = differences, ["savedNotApplied"] = savedDiff, ["otherSavedProfiles"] = savedProfiles,
            ["pendingConfiguration"] = JObject.FromObject(_pendingConfiguration),
            ["mods"] = new JArray(mods.Select(m => new JObject { ["name"] = m.RawName, ["guid"] = m.Guid, ["version"] = m.PluginVersion,
                ["installed"] = File.Exists(m.Location), ["loaded"] = Chainloader.PluginInfos.TryGetValue(m.Guid, out var loaded) && loaded.Instance != null,
                ["enabledNextStart"] = m.Location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                ["appliedTarget"] = applied?.mods.FirstOrDefault(x => x.guid.Equals(m.Guid, StringComparison.OrdinalIgnoreCase))?.enabled,
                ["path"] = Path.GetRelativePath(Paths.BepInExRootPath, m.Location) })),
            ["notes"] = "日志正文保留原文；配置元信息遮盖已识别的敏感字段。未纳入方案的配置不进行基准比较。"
        };
        return (JObject)LogExport.Scrub(metadata);
    }
}
