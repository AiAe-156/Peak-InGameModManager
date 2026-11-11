# MCE standalone progress viewer

Windows WPF / .NET Framework 4.8, no bundled runtime and no NuGet runtime dependencies. Uses the Windows Framework `System.Web.Extensions` JSON reader; read-only protocol projections tolerate unknown fields without linking the much larger worker dependency graph. No new shared launch contract is required.

## Bundle integration (main project owner)

1. Exclude `Desktop/**/*.cs` (and Desktop build resources, if default-globbed) from the main plugin project, just as for Updater. Build `Desktop/MCE.Desktop.csproj -c Release` independently.
2. Copy **only** `Desktop/bin/Release/net48/MCE.Desktop.exe` and `MCE.Desktop.exe.config` into the same final `Tools/Updater/` directory as `MCE.UpdateWorker.exe`. Do this **before** generating the worker protocol manifest. The complete cached tool inventory must include both files so the viewer survives replacement/removal of the original plugin. Do not copy the self-test directories or build residue.
3. Keep worker Framework dependencies/license files in that same bundle. This viewer adds approximately 42 KB; it adds no runtime distribution or third-party library notices. The parent must verify the final combined tools bundle is <=10 MB.
4. The replacement `src/Overview/PackSwitchProgressWindow.cs` calls existing `PrepareWorkerTools()` and launches the viewer from that immutable cache. Missing viewer fails visibly, not back to hidden PowerShell processing. Existing `ShowRecovery()` and `Show(..., recoveryOnly:true)` continue through `RecoveryLauncher` unchanged.

Invocation:

```text
MCE.Desktop.exe --root "<BepInEx directory>" --task <32-hex pack task id> --language en-us|zh-cn|zh-tw
```

`--root` deliberately matches the worker's existing root contract (not the PEAK root or MCE home). The launcher derives it from `PackLibrary.Home`. This process has no parent lifetime dependency and never terminates the worker on close.

## Safety and behavior

Opening/viewing polls `MCE/pack-task.json` and the selected switch journal asynchronously; it never starts a worker or processes a command. Retry is available only for failed/recovery-required confirm-switch tasks, requires explicit confirmation, revalidates task and complete manifest off the UI thread, then atomically publishes the existing protocol2 command. The worker remains authoritative for all safety checks. An unacknowledged or uncertain submission stays locked against duplicate clicks; there is no automatic retry. A fresh attempt for the same target pack is the only replacement task this window follows while awaiting an explicit retry.

Progress uses actual Removed/Placed journal counts against Originals/Desired, capped at 99% until task completion. These are journaled file-operation intents, not a fabricated time-based percentage or independent verification of each copy. Unknown preparation work uses indeterminate progress. The journal phase and rolled-back status remain explicit. Errors preserve worker-language evidence; all viewer controls and guidance have English, Simplified Chinese and Traditional Chinese labels.

Copy/export use the same redacted display report (root/user path, URLs and credential-like fields); no raw JSON, configuration or environment dump is exported. Free-form worker errors still deserve review before public sharing. Recovery opens the existing independent recovery-only script, whose separate selection and confirmation remain intact. Closing an active/unknown/pending/recovery state warns that work is not cancelled and PEAK must not restart prematurely; close is blocked during command submission only.

## Narrow verification

```text
dotnet build Desktop/MCE.Desktop.csproj -c Release
Desktop/bin/Release/net48/MCE.Desktop.exe --self-test
```

Self-test creates a unique directory below the build output, reads only synthetic task/journal fixtures, verifies schema rejection, strict IDs, shared reads, counts, quoting, redaction and missing-tool rejection. It neither launches a worker nor creates a command queue. Its `result.txt` records 10 passing checks. Release build verified with 0 warnings and 0 errors. Build outputs: executable 40,960 bytes, config 174 bytes at initial verification.

Real-game visual/DPI testing and the parent project's final manifest/package integration remain required. No game deployment, real task/queue edits, core writes or game startup are performed here.
