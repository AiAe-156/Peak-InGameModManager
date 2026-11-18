# -*- coding: utf-8 -*-
"""1.1.4 (user: "处理"): dragging a slider wrote the recovery draft (two JSON serialisations + a disk write) on every
change. Only the hot path (SetDraftConfig) is deferred now: the draft is written once the value has been still for a
moment. Every other persist (save, apply, discard, group / pack edits) still writes at once and first settles a pending
deferred write, so save transactions snapshot an up-to-date file. The pending write is also flushed on shutdown."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/OverviewRepository.DraftRecovery.cs', [
('''    internal void PersistRecoveryDraft()
    {
        InvalidatePendingView();''', '''    /// <summary>1.1.4: rapid edits (slider drag, typing) defer the draft write; see <see cref="PersistRecoveryDraftSoon"/>.</summary>
    private bool _recoveryDeferred;
    private double _recoveryDueAt;
    private const double RecoveryDelay = 0.5;
    internal static Func<double>? RecoveryClock;
    /// <summary>Hot path: marks the draft for writing once edits have paused. Views still refresh immediately.</summary>
    internal void PersistRecoveryDraftSoon()
    {
        InvalidatePendingView();
        if (RecoveryClock == null) { PersistRecoveryDraft(); return; }
        _recoveryDeferred = true; _recoveryDueAt = RecoveryClock() + RecoveryDelay;
        PendingRecovery.Add(this);
    }
    /// <summary>Writes a deferred draft now (before saves, discards, shutdown, or once its delay has passed).</summary>
    internal void FlushRecoveryDraft()
    {
        if (!_recoveryDeferred) return;
        _recoveryDeferred = false; PendingRecovery.Remove(this);
        try { PersistRecoveryDraft(); } catch (Exception error) { ReadWarning = error.Message; }
    }
    internal static readonly System.Collections.Generic.HashSet<OverviewRepository> PendingRecovery = new();
    /// <summary>Called every frame by the plugin: writes drafts whose delay has passed.</summary>
    internal static void TickRecovery()
    {
        if (PendingRecovery.Count == 0 || RecoveryClock == null) return;
        double now = RecoveryClock();
        foreach (var repo in PendingRecovery.ToArray()) if (repo._recoveryDueAt <= now) repo.FlushRecoveryDraft();
    }
    internal static void FlushAllRecovery() { foreach (var repo in PendingRecovery.ToArray()) repo.FlushRecoveryDraft(); }
    internal void PersistRecoveryDraft()
    {
        _recoveryDeferred = false; PendingRecovery.Remove(this);
        InvalidatePendingView();'''),
])
edit('Overview/OverviewRepository.Profiles.cs', [
('''        bool success = Configs.TryEditDraft(_draftConfig, key, normalized, out error);
        if (success) PersistRecoveryDraft();''', '''        bool success = Configs.TryEditDraft(_draftConfig, key, normalized, out error);
        if (success) PersistRecoveryDraftSoon(); // 1.1.4: one write after a slider drag, not one per frame'''),
])
# save transactions snapshot the recovery file: settle a deferred write first
edit('Overview/OverviewRepository.SaveScope.cs', [
('''        var previousSaved = Clone(_savedDraftProfile);
        string previousSignature = _savedDraftSignature;''', '''        FlushRecoveryDraft(); // 1.1.4: the checkpoint below snapshots the recovery file
        var previousSaved = Clone(_savedDraftProfile);
        string previousSignature = _savedDraftSignature;'''),
])
edit('Overview/OverviewRepository.Applied.cs', [
('''        string pendingPath = Path.Combine(ProfileRuntimeDirectory, "pending-profile.json");
        string oldApplied = Profiles.AppliedId;''', '''        FlushRecoveryDraft(); // 1.1.4: the checkpoint below snapshots the recovery file
        string pendingPath = Path.Combine(ProfileRuntimeDirectory, "pending-profile.json");
        string oldApplied = Profiles.AppliedId;'''),
])
# plugin: clock, per-frame tick, flush on shutdown
edit('Plugin.cs', [
('''    private void OnDestroy() { Overview.AnimatorRestGate.Shutdown();''',
 '''    private void Update() => Overview.OverviewRepository.TickRecovery();
    private void OnApplicationQuit() => Overview.OverviewRepository.FlushAllRecovery();
    private void OnDestroy() { Overview.OverviewRepository.FlushAllRecovery(); Overview.AnimatorRestGate.Shutdown();'''),
('''        Overview.NativeConfigurationDraft.Install(harmony);''', '''        Overview.NativeConfigurationDraft.Install(harmony);
        Overview.OverviewRepository.RecoveryClock = () => UnityEngine.Time.unscaledTimeAsDouble; // 1.1.4: deferred recovery-draft writes'''),
])
print('ok')
