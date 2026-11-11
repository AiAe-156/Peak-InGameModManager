#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>Durable recovery draft, distinct from commands accepted by the worker.</summary>
internal static class ResourceOperationDraft
{
    private static string Context => PackLibrary.Managed ? PackLibrary.Workspace : Loc.RuntimeDirectory;
    private static string FilePath => Path.Combine(Context, "resource-draft.json");
    internal static int Revision { get; private set; }
    private static string _context = "";
    private static List<UpdateCommand> _commands = new();
    private static string SubmissionPath => FilePath + ".submission";
    internal static string SubmissionId
    {
        get
        {
            Load();
            string id = File.Exists(SubmissionPath) ? File.ReadAllText(SubmissionPath).Trim() : "";
            if (Guid.TryParseExact(id, "N", out _) && File.Exists(Path.Combine(OverviewUpdateQueue.Root, "command-rejections", id + ".json")))
            {
                // A durable rejection is not an acknowledgement. Unlock the unchanged
                // draft for correction; a later explicit save receives a fresh identity.
                File.Delete(SubmissionPath); Revision++; return "";
            }
            return id;
        }
    }
    internal static void BeginSubmission(string id, IEnumerable<UpdateCommand>? commands = null)
    {
        Load();
        OverviewRepository.AtomicWrite(SubmissionPath + ".items", JsonConvert.SerializeObject((commands ?? _commands).Select(c => c.Id).ToArray()));
        OverviewRepository.AtomicWrite(SubmissionPath, id);
    }
    internal static bool SubmissionAccepted()
    {
        string id = File.Exists(SubmissionPath) ? File.ReadAllText(SubmissionPath).Trim() : "";
        if (id.Length == 0) return false;
        string receipt = Path.Combine(OverviewUpdateQueue.Root, "command-receipts", id + ".json");
        return File.Exists(receipt) && Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(receipt)).Value<string>("Status") == "accepted";
    }
    private static void Load()
    {
        string path = FilePath;
        if (_context != path)
        {
            var commands = File.Exists(path) ? JsonConvert.DeserializeObject<List<UpdateCommand>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Resource draft is empty.") : new();
            _commands = commands; _context = path;
        }
        // Every consumer observes the same acknowledged state, independent of UI getter order.
        if (SubmissionAccepted())
        {
            var accepted = File.Exists(SubmissionPath + ".items") ? JsonConvert.DeserializeObject<string[]>(File.ReadAllText(SubmissionPath + ".items")) ?? Array.Empty<string>() : _commands.Select(c => c.Id).ToArray();
            var ids = new HashSet<string>(accepted, StringComparer.Ordinal);
            var remaining = _commands.Where(c => !ids.Contains(c.Id)).ToList();
            OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(remaining, Formatting.Indented)); _commands = remaining; Revision++;
            File.Delete(SubmissionPath); File.Delete(SubmissionPath + ".items");
        }
    }
    internal static int Count { get { Load(); return _commands.Count; } }
    internal static bool AwaitingAcknowledgement => SubmissionId.Length > 0;
    internal static IReadOnlyList<UpdateCommand> Snapshot()
    {
        Load(); return JsonConvert.DeserializeObject<List<UpdateCommand>>(JsonConvert.SerializeObject(_commands))!;
    }
    private static string Key(UpdateCommand command) => command.Shop != null ? "package:" + command.Shop.Namespace + "-" + command.Shop.Package :
        command.Job != null ? "package:" + command.Job.Namespace + "-" + command.Job.Package :
        command.LocalImport != null ? "local:" + command.LocalImport.PackageFolder : command.Id;
    internal static void Stage(UpdateCommand command)
    {
        Load();
        if (SubmissionId.Length > 0) throw new InvalidOperationException("Resource save is awaiting worker acknowledgement. Retry Save; if reported as historical-unknown, review installed operation history before explicitly discarding the draft (do not resubmit completed work).");
        var next = JsonConvert.DeserializeObject<List<UpdateCommand>>(JsonConvert.SerializeObject(_commands))!;
        string key = Key(command); next.RemoveAll(c => Key(c).Equals(key, StringComparison.OrdinalIgnoreCase));
        command.Id = Guid.NewGuid().ToString("N"); next.Add(command);
        OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(next, Formatting.Indented)); _commands = next; Revision++;
    }
    internal static void Remove(string id)
    {
        Load(); if (SubmissionId.Length > 0) throw new InvalidOperationException("Resource save is awaiting acknowledgement.");
        var next = _commands.Where(c => c.Id != id).ToList();
        OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(next, Formatting.Indented)); _commands = next; Revision++;
    }
    internal static void Clear()
    {
        Load(); ClearCore();
    }
    private static void ClearCore()
    {
        OverviewRepository.AtomicWrite(FilePath, "[]"); _commands.Clear(); Revision++;
        if (File.Exists(SubmissionPath)) File.Delete(SubmissionPath);
        if (File.Exists(SubmissionPath + ".items")) File.Delete(SubmissionPath + ".items");
    }
    internal static bool IsUpdate(UpdateCommand command) => command.Job != null || command.Shop?.Operation == "preview-install";
    internal static IReadOnlyList<UpdateCommand> PendingUpdates() => Snapshot().Where(IsUpdate).ToArray();
    internal static void RemoveSubmittedUpdates(IEnumerable<string> ids)
    {
        Load(); var selected = new HashSet<string>(ids, StringComparer.Ordinal);
        var next = _commands.Where(c => !selected.Contains(c.Id)).ToList();
        OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(next, Formatting.Indented)); _commands = next; Revision++;
    }
    internal static string Label(UpdateCommand command) => command.Shop != null ?
        (command.Shop.Operation == "preview-uninstall" ? Loc.T("Uninstall: ", "卸载：", "解除安裝：") : Loc.T("Install / update: ", "安装／更新：", "安裝／更新：")) + command.Shop.Namespace + "-" + command.Shop.Package + " " + command.Shop.Version :
        command.Job != null ? command.Job.DisplayName + " " + command.Job.InstalledVersion + " → " + command.Job.TargetVersion :
        command.LocalImport != null ? Loc.T("Import: ", "导入：", "匯入：") + command.LocalImport.PackageFolder : command.Action;
}
