#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>Portable configuration only. No pack information, introductions, media or binaries.</summary>
internal sealed class ConfigurationPresetFile
{
    public int schemaVersion = 1;
    public string kind = "mce-configuration";
    public string name = "";
    public OverviewConfigSnapshot config = new();
    public List<OverviewProfileMod> mods = new();

    internal static string Export(IEnumerable<OverviewProfile> profiles, string parent)
    {
        var selected = profiles.ToList();
        if (selected.Count == 0) throw new InvalidDataException("Select at least one configuration.");
        string directory = Path.Combine(parent, "Configurations_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(directory);
        foreach (var profile in selected)
        {
            // The chosen copy is a folder on this machine; shared presets carry only the per-GUID state.
            var mods = profile.mods.Select(m => { var portable = JsonConvert.DeserializeObject<OverviewProfileMod>(JsonConvert.SerializeObject(m))!; portable.copy = ""; portable.nestedEnabled.Clear(); return portable; }).ToList();
            var file = new ConfigurationPresetFile { name = profile.name, config = profile.config, mods = mods };
            foreach (var value in file.config.values)
                if (!PublicationFilePolicy.ProfileConfigPath(value.configFile)) throw new InvalidDataException("Configuration preset includes a private or unsupported file.");
            File.WriteAllText(Path.Combine(directory, OverviewProfileExport.PortableId(profile.id) + ".mceconfig.json"), JsonConvert.SerializeObject(file, Formatting.Indented), new UTF8Encoding(false));
        }
        return directory;
    }

    internal static ConfigurationPresetFile Read(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid configuration preset file.");
        var file = OverviewProfileStore.DeserializeJson<ConfigurationPresetFile>(File.ReadAllText(path, Encoding.UTF8));
        if (file.schemaVersion != 1 || file.kind != "mce-configuration" || string.IsNullOrWhiteSpace(file.name)
            || file.config == null || file.config.schemaVersion != 1 || file.config.values == null || file.mods == null)
            throw new InvalidDataException("Unsupported or incomplete configuration preset.");
        foreach (var value in file.config.values)
        {
            string relative = value.configFile.Replace('\\', '/');
            if (System.IO.Path.IsPathRooted(relative) || relative.Split('/').Any(p => p == ".." || p.Length == 0) || relative.Contains(':') || !PublicationFilePolicy.ProfileConfigPath(relative))
                throw new InvalidDataException("Configuration target path is outside the supported configuration boundary.");
            new ConfigurationDocument("").Merge(new[] { new ConfigurationDocument.Edit { Key = new ConfigurationDocument.Key(value.section, value.key), Value = value.serializedValue } });
        }
        if (file.config.values.GroupBy(v => v.Identity).Any(g => g.Count() > 1) || file.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate target in configuration preset.");
        return file;
    }
}
