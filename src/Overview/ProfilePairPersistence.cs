#nullable enable
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>profile.json is the atomic commit point; config-values.json remains a legacy/export mirror.</summary>
internal static class ProfilePairPersistence
{
    private const string Embedded = "_pairedConfig";
    internal static (JObject Profile, JObject Config) Read(string profilePath, string configPath)
    {
        var profile = JObject.Parse(File.ReadAllText(profilePath, Encoding.UTF8));
        var config = profile[Embedded] as JObject;
        if (profile[Embedded] != null && config == null) throw new InvalidDataException("Invalid paired configuration snapshot.");
        config ??= JObject.Parse(File.ReadAllText(configPath, Encoding.UTF8));
        Validate(profile, config);
        return (profile, config);
    }
    private static void Validate(JObject profile, JObject config)
    {
        // profile.json is version 2 when it carries nested groups (older versions accept only 1 and so refuse it);
        // the configuration snapshot never changed shape and stays 1.
        if (profile["schemaVersion"]?.Type != JTokenType.Integer || !GroupTree.SupportedSchema(profile.Value<int>("schemaVersion")))
            throw new InvalidDataException("Profile schemaVersion must be integer 1 or 2.");
        if (config["schemaVersion"]?.Type != JTokenType.Integer || config.Value<int>("schemaVersion") != 1)
            throw new InvalidDataException("Configuration snapshot schemaVersion must be integer 1.");
        if (config["values"] is not JArray || config["files"] is not JArray)
            throw new InvalidDataException("Configuration snapshot is missing values or files.");
    }
    internal static void Write(string directory, JObject profile, JObject config)
    {
        Validate(profile, config);
        Directory.CreateDirectory(directory);
        string meta = Path.Combine(directory, "profile.json"), values = Path.Combine(directory, "config-values.json");
        if (File.Exists(meta))
        {
            // Before touching either mirror, upgrade the previous complete generation to a single-file pair.
            var previous = Read(meta, values);
            previous.Profile[Embedded] = previous.Config.DeepClone();
            Atomic(meta + ".lastvalid", previous.Profile.ToString(Formatting.Indented));
            Atomic(meta, previous.Profile.ToString(Formatting.Indented));
        }
        profile[Embedded] = config.DeepClone();
        Atomic(values, config.ToString(Formatting.Indented));
        Atomic(meta, profile.ToString(Formatting.Indented));
        // Metadata replacement above is the commit point. A first-generation recovery copy is
        // auxiliary: failure must not tell callers to roll back a pair already committed on disk.
        // On later saves the previous generation backup is required and is written before commit.
        if (!File.Exists(meta + ".lastvalid"))
        {
            try { Atomic(meta + ".lastvalid", profile.ToString(Formatting.Indented)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    internal static void Atomic(string path, string text)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            { writer.Write(text); writer.Flush(); output.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally
        {
            // Cleanup is best-effort both before and after commit; it must not mask the original
            // write error, nor turn a successful rename into a reported persistence failure.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
