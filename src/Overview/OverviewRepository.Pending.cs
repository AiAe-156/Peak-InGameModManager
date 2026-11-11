#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Bootstrap;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    private OverviewProfile _savedDraftProfile = new();
    private PendingConfiguration _pendingConfiguration = new();
    private string _appliedIntroductionHash = "";
    private readonly Dictionary<string, (long Length, DateTime Written, string Hash)> _introductionHashes = new(StringComparer.OrdinalIgnoreCase);

    private void MarkSavedDraftBaseline()
    {
        InvalidatePendingView();
        _savedDraftProfile = new OverviewProfile { state = Clone(State), config = Clone(_draftConfig), mods = Clone(_draftMods) };
        _savedDraftSignature = DraftSignature(State, _draftConfig, _draftMods);
    }

    private ProfilePendingState? _pendingView;
    private float _pendingViewUntil;
    private bool _pendingIntroductionDirty;
    // Bumped whenever the projection is dropped, so view-level memos can follow invalidation instead of a timer.
    internal int PendingViewRevision { get; private set; }
    internal void InvalidatePendingView()
    {
        _pendingView = null; _authorComparisonUntil = 0; PendingViewRevision++;
        // Pending dots refresh through the maintenance sweep; ask it for a fresh round now so an edit
        // lands promptly instead of waiting for the next periodic tick.
        OverviewMaintenance.PendingInvalidated = true;
    }
    internal ProfilePendingState GetPendingState(bool introductionDirty = false)
    {
        if (_pendingView != null && _pendingIntroductionDirty == introductionDirty && UnityEngine.Time.unscaledTime < _pendingViewUntil) return _pendingView;
        MergeEditedRules();
        var loaded = Configs.KnownLoadedEntries();
        var liveMods = Chainloader.PluginInfos.ToDictionary(p => p.Key, p => p.Value.Instance != null, StringComparer.OrdinalIgnoreCase);
        var draft = new OverviewProfile { state = State, config = _draftConfig, mods = _draftMods };
        // Deliberately slow. Real edits go through InvalidatePendingView, so this window is only a safety net
        // for changes that bypass those hooks. At 0.2s it expired on every maintenance sweep, rebuilding the
        // whole projection (and its per-parameter/per-module allocations) every 0.2 seconds.
        _pendingViewUntil = UnityEngine.Time.unscaledTime + 2f; _pendingIntroductionDirty = introductionDirty;
        return _pendingView = ProfilePendingState.Compare(_savedDraftProfile, draft, _appliedSnapshot ?? Profiles.Applied, loaded, liveMods,
            _pendingConfiguration.session == RuntimeSession ? _pendingConfiguration.restartParameters : Array.Empty<ProfileRestartParameter>(),
            _pendingConfiguration.session == RuntimeSession ? _pendingConfiguration.restartMods : Array.Empty<ProfileRestartMod>(), introductionDirty,
            IntroductionHash(PackDirectory, State.pack) != _appliedIntroductionHash, Configs.ReadOfflineValues(_draftConfig));
    }

    private string IntroductionHash(string directory, OverviewPack pack)
    {
        var content = new StringBuilder();
        foreach (string relative in new[] { pack.introDocument, pack.introFile })
        {
            string path = SafePath(directory, relative);
            if (path.Length == 0 || !File.Exists(path)) { content.Append("\n<missing>"); continue; }
            var info = new FileInfo(path);
            if (!_introductionHashes.TryGetValue(path, out var remembered) || remembered.Length != info.Length || remembered.Written != info.LastWriteTimeUtc)
            {
                using var sha = SHA256.Create();
                using var file = File.OpenRead(path);
                remembered = (info.Length, info.LastWriteTimeUtc, Convert.ToBase64String(sha.ComputeHash(file)));
                _introductionHashes[path] = remembered;
            }
            content.Append('\n').Append(remembered.Hash);
        }
        return content.ToString();
    }

    internal void AcknowledgeNewRound()
    {
        var loaded = Configs.LoadedEntries();
        var ready = _pendingConfiguration.restartParameters.Where(p => p.effectTiming == ConfigurationEffectTiming.NextRound
            && loaded.TryGetValue(p.target.Identity, out var entry) && entry.GetSerializedValue() == p.target.serializedValue).ToList();
        if (ready.Count == 0) return;
        foreach (var pending in ready)
        {
            var entry = loaded[pending.target.Identity];
            ConfigurationNotificationGate.DeliverNextRound(entry);
            using (var notifications = new ConfigurationNotificationGate.Scope(new[] { entry }))
                ConfigurationNotificationGate.DeliverBridge(entry, () => OverviewRuntimeSettingsBridge.Synchronize(new[] { entry }, newRound: true));
            if (entry.GetSerializedValue() != pending.target.serializedValue)
                throw new InvalidOperationException("A next-round callback changed the requested value; delivery remains pending: " + pending.target.Identity);
            var accepted = Clone(_pendingConfiguration);
            accepted.restartParameters.RemoveAll(p => p.target.Identity == pending.target.Identity);
            AtomicWrite(Path.Combine(ProfileRuntimeDirectory, "pending-profile.json"), JsonConvert.SerializeObject(accepted, Formatting.Indented));
            _pendingConfiguration = accepted;
            ConfigurationNotificationGate.ResetDelivery(entry);
        }
        _loadedRevision = ++_sharedRevision;
        InvalidatePendingView();
    }

    private void LoadPendingState()
    {
        string path = Path.Combine(ProfileRuntimeDirectory, "pending-profile.json");
        _pendingConfiguration = File.Exists(path) ? OverviewProfileStore.DeserializeJson<PendingConfiguration>(File.ReadAllText(path, Encoding.UTF8)) : new();
        if (_pendingConfiguration.schemaVersion != 1) throw new InvalidDataException(Loc.T("Unsupported pending-profile record.", "待应用方案记录格式不受支持。", "待套用方案記錄格式不受支援。"));
        _pendingConfiguration.restartParameters ??= new(); _pendingConfiguration.restartMods ??= new();
        _pendingConfiguration.config ??= new();
        var applied = _appliedSnapshot ?? Profiles.Applied;
        _appliedIntroductionHash = _pendingConfiguration.profileId == Profiles.AppliedId && !string.IsNullOrEmpty(_pendingConfiguration.appliedIntroductionHash)
            ? _pendingConfiguration.appliedIntroductionHash : IntroductionHash(applied.directory, applied.state.pack);
        if (_pendingConfiguration.session == RuntimeSession) return;
        var loaded = Configs.LoadedEntries();
        // A later process has already loaded values from disk. Retain only records whose
        // target is still missing or was rejected during startup.
        _pendingConfiguration.restartParameters.RemoveAll(p => loaded.TryGetValue(p.target.Identity, out var entry) &&
            p.target.type == (entry.SettingType.FullName ?? entry.SettingType.Name) && p.target.serializedValue == entry.GetSerializedValue());
        _pendingConfiguration.restartMods.RemoveAll(m => (Chainloader.PluginInfos.TryGetValue(m.guid, out var info) && info.Instance != null) == m.enabled);
    }

    private PendingConfiguration AcceptedPendingState(OverviewConfigService.OverviewConfigTransaction parameters, IReadOnlyList<OverviewMod> mods, OverviewConfigSnapshot? scope = null)
    {
        var result = Clone(_pendingConfiguration);
        result.profileId = ViewedProfileId; result.session = RuntimeSession;
        if (scope == null) result.config = new OverviewConfigSnapshot();
        else
        {
            result.config.values.Clear();
            // Skipped invalid/schema-changed fields remain editable recovery drafts;
            // they are not a background instruction to apply at a later launch.
        }
        result.pending = false;
        result.appliedIntroductionHash = IntroductionHash(PackDirectory, State.pack);
        var loaded = Configs.LoadedEntries();
        result.restartParameters.RemoveAll(p => !loaded.TryGetValue(p.target.Identity, out var entry) ||
            p.target.serializedValue != entry.GetSerializedValue() || p.target.type != (entry.SettingType.FullName ?? entry.SettingType.Name));
        foreach (OverviewConfigValue target in parameters.RestartSnapshot.values)
        {
            var old = _pendingConfiguration.restartParameters.FirstOrDefault(p => p.target.Identity == target.Identity);
            string previous = old?.previous ?? parameters.RestartPrevious[target.Identity];
            result.restartParameters.RemoveAll(p => p.target.Identity == target.Identity);
            if (target.serializedValue != previous) result.restartParameters.Add(new ProfileRestartParameter { target = Clone(target), previous = previous, effectTiming = parameters.RestartTiming[target.Identity] });
        }
        var installed = new HashSet<string>(mods.SelectMany(m => m.PluginGuids.Count > 0 ? m.PluginGuids : new List<string> { m.Guid }), StringComparer.OrdinalIgnoreCase);
        foreach (OverviewProfileMod target in _draftMods)
        {
            bool loadedMod = Chainloader.PluginInfos.TryGetValue(target.guid, out var info) && info.Instance != null;
            result.restartMods.RemoveAll(m => m.guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase));
            if (installed.Contains(target.guid) && loadedMod != target.enabled) result.restartMods.Add(new ProfileRestartMod { guid = target.guid, enabled = target.enabled, wasEnabled = loadedMod });
        }
        return result;
    }
}
