# Installation layout planner checks / 安装目录纯规划验证

Source-only API: `InstallationLayoutPlanner.Plan(InstallationLayoutInput)`.
No runtime integration, live filesystem inspection or directory moves are implemented here.
Only newly added planner/contracts and this verification directory are owned by this subtask; existing production files were not edited, so no pre-existing file required a backup.

## Integration contract

- Collect a bounded, complete BepInEx-relative inventory outside this planner. Supply the absolute local-drive BepInEx root for conservative UTF-16 full-path length checks (default 240). UNC/device roots are deliberately unsupported.
- Build one `InstallationLayoutUnit` per independently owned **whole package directory**, including every internal file and plugin. Do not build units per DLL. Set ownership/file completeness only after proving all directory contents belong to that unit. Shared directories/files cannot be reorganized.
- Supply every plugin's stable ID/group ID/loaded fact; every file's relative path, exact owning unit ID and link fact; package lock/link/shared facts. Defaults are unknown and therefore defer. Populate `IsCoreOrPatcher` and `IsProtectedManager` from authoritative protection/closure collection, not display names. The manager GUID is independently guarded.
- Disabled, group-only and group-plus-translated-outer-folder modes are supported. Group-only preserves the original outer directory leaf. Translated mode uses the translated name, then original name, then the source leaf. Internal paths/DLL filenames are never rewritten.
- IDs are case-insensitive identities, separate from translated labels. Generated names are sanitized readable labels plus a deterministic SHA-256-derived identity suffix (not a deployment checksum). Persist emitted `GroupFolderName` and `OuterFolderName` keyed by their stable IDs, and pass them back through `StableFolderName`/`StableOuterFolderName` to freeze directory names across display renames or locale changes. Without a persisted name, a changed label intentionally generates a new suggestion. Collisions defer all candidates; no enumeration-order winner or opportunistic numbering.
- Preview `Units` includes original/destination, per-file paths, disposition and stable reason codes. Deferred destinations are suggestions only. Rejected/disabled entries retain their original destination. `HasMoves` is a convenience, **not authorization**.
- Future integration must bind the preview to pack/profile and inventory revision, show it to the user, obtain explicit confirmation, then use the existing serialized exit-time transaction/backup/rollback machinery. Recollect locks/load/link/ownership/path evidence at commit time. Re-run planning with post-exit facts rather than editing a deferred preview. Update exact-path ownership records atomically. This planner does not do any of these steps.
- `InventoryComplete` and completeness facts are caller attestations, not inferred proof. All source tree files must be enumerated in `Files`; optional `ExistingPaths` adds occupancy/link evidence and detects exposed unowned source files. Unknown facts never grant a move. Explicit unsafe paths/links are rejected or deferred; source/core/patcher paths outside plugins are rejected.

## Reproduction

```powershell
dotnet run --project 验证/InstallationLayoutChecks -c Release
dotnet build 验证/InstallationLayoutChecks/Compatibility -c Release
```

The standalone net10 executable links only the two production sources and uses real assertions. Compatibility compilation targets netstandard2.1 and net48 with C# 8 and warnings-as-errors; no Unity, game, helper or deployment build is invoked. See `run.log` and `compatibility.log` for actual latest results. These checks are not game UI, transaction or runtime integration acceptance.
