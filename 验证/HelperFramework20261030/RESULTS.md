# Framework helper stage results

- Source-only. No deployment, game/core writes, queue changes or shared src/Updating edits.
- Pre-edit snapshots: 备份/helper-framework_20261030/ (existing source files and project/notices).
- Framework publish initially succeeded using the regular command: dotnet publish Updater/MCE.UpdateWorker.csproj -c Release -o Updater/bin/framework-bundle -p:EnableDeployment=false.
- Parallel work subsequently added unfinished Updater/ModImportPreview.cs, whose contracts are not yet included in the production worker project. The final repeat used the same command plus -p:CustomAfterMicrosoftCommonTargets=<absolute project path>/验证/HelperFramework20261030/StageOnly.targets. That validation-only target excludes that one parallel new file; production csproj does NOT exclude it. Import integration must add contracts and rerun the regular publish.
- Final stage publish: 0 compile warnings/errors. Bundle: 17 files, 2,221,192 bytes (2.1183 MiB), including all dependencies, notices and protocol manifest. See size.json. No runtime bundled.
- Executable --protocol reports protocol=2; --validate-protocol-manifest passes (ab9e7062a51bc509507c78d723e9bfe3fcd5e53703d17af9b0743db891e08259).
- Main CollectReleaseFiles succeeds and references framework-bundle (including recursive license files), with deployment disabled.
- Full main build was blocked by parallel Desktop/ProgressWindow.cs being included in the plugin without WPF references; not changed here.
- Exact-production-list isolated net48 checks: 26 existing protocol/recovery tests passed, plus adapter checks for JSON Unicode/atomic journals, atomic overwrite, SHA256, traversal rejection, Windows path semantics and RSA SHA256 PKCS1.
- Same isolated checks passed on net10 (26 + adapter group), preserving modern test harness compatibility. Existing wildcard Worker harness temporarily failed on parallel InstallationLayoutPlanner.cs syntax. Existing LocalDeploymentChecks lacks ComponentDependencyGraph source inclusion. Neither existing harness was modified.
- Framework long paths: manifest and AppContext switches added. This machine still failed Directory.CreateDirectory at the very deep original sandbox path; shortening the project-local fixture root passed. Production long-path acceptance remains required; do not claim parity for arbitrary >260-character paths.
- HTTP(S) proxy/system/direct settings retained. Framework explicitly rejects SOCKS5, without direct fallback. No HTTPS/hash/signature/process/reparse/protocol checks were disabled.
- Logs are under Updater/bin/framework-*.log. Test source/fixtures remain under 验证/HelperFramework20261030/ and 验证/HF-*.
