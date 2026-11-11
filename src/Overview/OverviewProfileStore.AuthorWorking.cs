#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewProfileStore
{
    private string AuthorWorkingDirectory(string id) => Path.Combine(RootDirectory, "author-working", OverviewProfileExport.PortableId(id));
    private bool HasAuthorWorkingProfile(string id) => File.Exists(Path.Combine(AuthorWorkingDirectory(id), "author-origin.json"));

    /// <summary>Keep the original author identity; only local content is writable, never signed input.</summary>
    private OverviewProfile AuthorWorkingProfile(OverviewProfile original)
    {
        var working = Clone(original);
        string directory = AuthorWorkingDirectory(original.id);
        if (!HasAuthorWorkingProfile(original.id))
        {
            if (Directory.Exists(directory)) throw new IOException("Incomplete author working copy; inspect before retrying: " + directory);
            string stage = directory + ".preparing-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            // Save the complete baseline separately, not as an extra selectable profile.
            AtomicWrite(Path.Combine(stage, "author-baseline.json"), JsonConvert.SerializeObject(original, Formatting.Indented));
            CopyResources(original.directory, stage);
            // A local working generation is not part of the author's signature.
            foreach (string auxiliary in Directory.GetFiles(stage, "profile.json*")) File.Delete(auxiliary);
            foreach (string auxiliary in Directory.GetFiles(stage, "config-values.json*")) File.Delete(auxiliary);
            AtomicWrite(Path.Combine(stage, "author-origin.json"), new JObject {
                ["schemaVersion"] = 1, ["id"] = original.id, ["directory"] = original.directory,
                ["firstSaveConfirmed"] = false
            }.ToString(Formatting.Indented));
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(stage, directory);
        }
        if (!File.Exists(Path.Combine(directory, "profile.json")))
        {
            var baseline = Clone(original); baseline.directory = directory; baseline.readOnly = false;
            baseline.kind = OverviewProfileKind.Personal; baseline.source = original.id;
            SaveFiles(baseline);
        }
        working.directory = directory; working.readOnly = false;
        working.kind = OverviewProfileKind.Personal; working.source = original.id;
        return working;
    }

    internal OverviewProfile BeginAuthorEditing(string id)
    {
        lock (_gate)
        {
            var original = GetRequired(id);
            if (!original.readOnly) return original;
            var working = AuthorWorkingProfile(original);
            // Adopt a local baseline under the same identity, without persisting edited
            // targets or creating a second selectable profile. This survives restart.
            _loaded[id] = working;
            UpsertReference(working); SaveIndex();
            return working;
        }
    }

    internal void RefreshCommittedCache()
    {
        lock (_gate) { _index = ReadIndex(); _loaded.Clear(); }
    }

    internal void RestoreBaseline(string id, OverviewProfile baseline)
    {
        lock (_gate)
        {
            var current = GetRequired(id); var restored = Clone(current);
            restored.state = Clone(baseline.state); restored.config = Clone(baseline.config); restored.mods = Clone(baseline.mods);
            if (restored.readOnly) restored = AuthorWorkingProfile(restored);
            SaveFiles(restored); UpsertReference(restored); SaveIndex();
        }
    }

    internal OverviewProfile GetOriginalPreset(string id)
    {
        var baseline = AuthorBaseline(id) ?? throw new InvalidOperationException("This configuration has no retained original preset.");
        return Clone(baseline);
    }

    internal bool NeedsOriginalPresetSaveChoice(string id) => IsPackConfiguration(GetRequired(id)) && !OriginalPresetSaveConfirmed(id);

    private bool OriginalPresetSaveConfirmed(string id)
    {
        string file = Path.Combine(AuthorWorkingDirectory(id), "author-origin.json");
        if (File.Exists(file)) return JObject.Parse(File.ReadAllText(file)).Value<bool?>("firstSaveConfirmed") == true;
        string confirmation = Path.Combine(RootDirectory, "preset-save-choices", OverviewProfileExport.PortableId(id) + ".json");
        return File.Exists(confirmation) && JObject.Parse(File.ReadAllText(confirmation)).Value<bool?>("confirmed") == true;
    }

    internal void ConfirmOriginalPresetSave(string id)
    {
        if (GetRequired(id).readOnly) BeginAuthorEditing(id);
        string file = Path.Combine(AuthorWorkingDirectory(id), "author-origin.json");
        if (File.Exists(file))
        {
            var origin = JObject.Parse(File.ReadAllText(file)); origin["firstSaveConfirmed"] = true;
            AtomicWrite(file, origin.ToString(Formatting.Indented));
        }
        else AtomicWrite(Path.Combine(RootDirectory, "preset-save-choices", OverviewProfileExport.PortableId(id) + ".json"), new JObject { ["confirmed"] = true }.ToString());
    }

    internal void CaptureOriginalPresetBaseline(OverviewProfile profile)
    {
        string directory = Path.Combine(RootDirectory, "preset-baselines");
        string file = Path.Combine(directory, OverviewProfileExport.PortableId(profile.id) + ".json");
        if (!File.Exists(file)) AtomicWrite(file, JsonConvert.SerializeObject(profile, Formatting.Indented));
    }

    private readonly System.Collections.Generic.Dictionary<string, OverviewProfile> _authorBaselines = new(StringComparer.OrdinalIgnoreCase);
    internal OverviewProfile? AuthorBaseline(string id)
    {
        if (_authorBaselines.TryGetValue(id, out var cached)) return cached;
        string file = Path.Combine(AuthorWorkingDirectory(id), "author-baseline.json");
        if (File.Exists(file)) return _authorBaselines[id] = DeserializeJson<OverviewProfile>(File.ReadAllText(file));
        string generated = Path.Combine(RootDirectory, "preset-baselines", OverviewProfileExport.PortableId(id) + ".json");
        if (File.Exists(generated)) return _authorBaselines[id] = DeserializeJson<OverviewProfile>(File.ReadAllText(generated));
        var current = GetRequired(id);
        return current.readOnly ? current : null;
    }
}
