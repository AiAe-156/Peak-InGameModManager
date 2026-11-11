#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>Pack-level presentation is shared by all presets in a workspace.</summary>
internal sealed class PackWorkspaceInformation
{
    public int schemaVersion = 1;
    public OverviewPack pack = new();
    public List<OverviewGroup> groups = new();
    public List<OverviewModRule> rules = new();
    public string resourceDirectory = "";

    internal static string PathFor(string profileRoot) => Path.Combine(Path.GetDirectoryName(profileRoot)!, "pack-information.json");
    internal static PackWorkspaceInformation? Read(string profileRoot)
    {
        string path = PathFor(profileRoot);
        if (!File.Exists(path)) return null;
        var information = OverviewProfileStore.DeserializeJson<PackWorkspaceInformation>(File.ReadAllText(path));
        if (!GroupTree.SupportedSchema(information.schemaVersion) || information.pack == null || information.groups == null || information.rules == null)
            throw new InvalidDataException("Unsupported pack information workspace.");
        string boundary = Path.GetFullPath(Path.GetDirectoryName(profileRoot)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resource = Path.GetFullPath(information.resourceDirectory);
        if (!resource.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack resources are outside their workspace.");
        for (string? part = resource; part != null && part.StartsWith(boundary, StringComparison.OrdinalIgnoreCase); part = Path.GetDirectoryName(part))
            if ((Directory.Exists(part) || File.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Pack resource workspace contains a link.");
        return information;
    }
    internal static void Write(string profileRoot, OverviewState state, string resources)
    {
        var information = new PackWorkspaceInformation { schemaVersion = GroupTree.SchemaFor(state.groups), pack = state.pack, groups = state.groups, rules = state.mods, resourceDirectory = resources };
        OverviewRepository.AtomicWrite(PathFor(profileRoot), JsonConvert.SerializeObject(information, Formatting.Indented));
    }
}
