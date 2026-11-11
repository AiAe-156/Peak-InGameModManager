# Local mod import preparation checks

Source-only implementation, no deployment and no transaction registration.

## Parent integration

- Shared DTOs: `ModConfigEnhance.Updating.ModImportRequest`, `ModImportPlan`.
- Worker API: `MCE.UpdateWorker.ModImportPreview.Prepare(request)` and `Revalidate(plan)`.
- Add `src/Updating/ModImportContracts.cs` as a linked compile item to the worker project when integrating; this subtask deliberately does not edit existing project files. Existing all-worker wildcard test projects also need this shared link.
- Caller supplies an independent private `StagingParent`, a safe single `PackageFolder`, and an installed GUID/version snapshot (include runtime prerequisites, e.g. BepInEx, when applicable). Do not supply a live installation directory as scratch.
- One import is one package. DLLs, resources, core and patcher content remain one ownership unit. Paths in the preview are relative to BepInEx. Plugins/core/patchers each use `<area>/<PackageFolder>/<payload>`; config preserves `config/<payload>` so GUID config filenames remain effective.
- Input is always `local`. Store candidates are separate, explicitly unverified suggestions. No store/network access or automatic source association occurs.
- `CanProceed` only indicates static import validity. The parent must separately check destination ownership, existing file conflicts, protected manager/component policy, permissions, dependency snapshot freshness, and exit/queue/transaction policy. No preview authorizes a write.
- Before consuming bytes, require `CanProceed` and no `Revalidate` issues. Revalidation checks links, inventory additions/removals, file lengths and SHA-256. It is not a lock: the executor must use exclusive handles or copy to its own locked transaction payload and verify again to avoid TOCTOU.
- Preview getters and read-only copied collections freeze source, file hashes, targets, metadata and issues. Request DTOs remain mutable inputs only.
- A patcher-only package is accepted only with static public `void Patch(Mono.Cecil.AssemblyDefinition)` and static public `IEnumerable<string> TargetDLLs`. Native/support DLLs are allowed alongside an identified plugin/patcher, never as bare standalone libraries.
- Only unambiguous single-child wrappers and a leading BepInEx are stripped. Mixed/ambiguous BepInEx roots fail closed rather than guessing. Misspelled `pathcer`/`pathcers` blocks with a rename suggestion.

## Narrow fixture verification

```
dotnet run --project 验证/ModImportChecks -f net10.0
dotnet build 验证/ModImportChecks -f net48 --no-restore
验证/ModImportChecks/bin/Debug/net48/ModImportChecks.exe
```

Latest result: net10 **52 assertions PASS** (including actual filesystem symlink rejection); net48 **51 assertions PASS**, build **0 warnings / 0 errors**. Fixtures are generated with Cecil into this test project's bin directory, without loading imported assemblies. The net48 check actually executes under Windows Framework.

Coverage: nested plugin types, GUID/name/version, hard/minimum-version/soft dependencies, intra-package dependencies, duplicate GUIDs, multi-DLL mixed core/patchers/config/resources, native companions, pure patcher, wrappers, ZIP explicit-directory ordering, forbidden loader/game files, traversal/rooted/ADS/reserved names, pathcer suggestion, ZIP duplicate/case/file-directory conflicts, ZIP Unix links and Windows reparse attributes, ratio/count/file/total limits, source/staging overlap, unsafe package targets, same-length mutation and added/deleted staging files.

Generated fixture and build outputs are retained under `bin/` / `obj/`; no existing files outside this subtask were edited.
