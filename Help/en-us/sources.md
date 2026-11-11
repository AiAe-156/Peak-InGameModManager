# Sources and updates {#top}

A mod's "source" answers one question: which package in the store is this? Once the source is confirmed, the manager can check for updates and write correct dependencies when you publish a modpack.

On this page: [Source types](#types) · [Identifying sources](#match) · [Checking for updates](#cache) · [Unread changes](#dots) · [Update queue](#update-queue) · [Update helper](#helper)

## Source types {#types}

- **Store**: the matching Thunderstore package is confirmed. Updates can be checked, and publishing writes it as a dependency.
- **Local**: you confirmed it has no store version (your own, or shared privately). Publishing can bundle it.
- **Pending**: store identity not confirmed yet. Mods with only a GitHub or other project page are pending too. Pending mods get no automatic update checks, but this **does not affect dependency checks**: if it is installed, enabled and new enough, no dependency issue is reported.

Source labels can be toggled from the "Sources and updates" menu; source assignments are shared by all profiles. Mods installed through the manager's store are confirmed from the install record and need no identification.

Click a mod's **home** icon to open the project page (such as GitHub) or the store page separately.

## Identifying sources {#match}

**Automatic**: searches the store with the mod's original package name and name (never its display name). An exact, unique name with no author conflict is matched directly; a similar name needs the same author, a high similarity and a clear lead over other candidates. Anything doubtful is left for you.

**Batch**: "Sources and updates" → "Find all missing sources" processes every pending mod. Results are split into "identified", "needs manual check" and "personal local"; items needing attention come first with the reason.

**Manual**: click a pending mod's home icon, or "Identify manually" in the batch results, then confirm after checking the team, version and update time in the candidate list. If there really is no store version, click "Mark as local"; batch searches will skip it, and you can restore automatic identification later.

Before publishing a modpack, it is best to resolve all pending mods; see [publishing steps](export#steps).

## Checking for updates {#cache}

- **Automatic**: only in the main menu or the Airport, every 30 minutes (sooner after a failure); an unfinished check is cancelled when you enter a level. It only checks and caches results — **nothing is downloaded or installed automatically**.
- **Manual**: "Sources and updates" → check for updates.
- **Permission**: you can turn update checks off globally or for a single mod. While the global switch is off, per-mod switches have no effect. Finding sources and reading store tags are not affected by this switch.
- Store catalogue, versions and tags are cached and re-requested only when needed and older than 24 hours; on failure the existing data is kept.
- Updates compare the **package version** (from the manifest), not the DLL version.

When a newer version exists, green markers appear beside the name and the version.

## Unread changes {#dots}

On first run the manager records the current state. After that, changed mod versions, new config entries, changed defaults or ranges, and author preset changes are marked with a **blue marker**.

Click the marker to see old and new content and the current value; clicking the related control marks it read. To work through them together use "Sources and updates" → "Review changes"; when done, "Mark all notification badges as read" (update-available markers and unsaved drafts are kept).

## Update queue {#update-queue}

The "Updates" entry at the top right of the overview shows the current task and progress. Click it for the "Update queue / Updated" window; closing the window does not cancel anything.

- **Update**: click the green marker and pick a version, or select several mods and click "Update selected"; "Update all" adds only confirmed store packages that have updates. A version fixed by the modpack takes priority over the store's latest.
- **Clicking queues it right away**, without Save configuration and without touching your draft.
- **Pause** keeps the downloaded part and resumes later; **Stop** ends this attempt; failed tasks can be retried; **Remove** clears the record (backups are kept).
- The "Updated" page groups history by batch, with download speed and time on each row.
- **Rollback**: a finished update can be rolled back to the files from before. Your current settings are kept by default; you can choose to restore the settings from before the update instead.
- Dependencies are read from the manifest; missing ones are **queued automatically** too.
- The network uses the system proxy by default; you can switch to direct or a custom HTTP(S) proxy (Management center → "Network").

**When it waits for you to quit the game**: only when it must **replace a DLL that is already loaded**.

- New mods and mods not loaded this session: installed while the game runs, effective next launch.
- Updates and rollbacks of running mods: downloaded and prepared while the game runs; the files are replaced after you quit.

## Update helper {#helper}

Install, update, uninstall, local import and folder organization are all run by the separate **update helper**:

- It starts only when there is work, shows a foreground progress window, and exits when done — **it never stays in the background**.
- While waiting for the game to exit it minimizes and does not steal focus; once you quit it carries on automatically and shows the result.
- Every file operation is backed up first and rolled back on failure; the manager checks on next launch that it took effect.
- Several tasks that do not conflict are prepared in parallel; ones that need the same files wait their turn — **your request is never refused**.
- r2modman and Thunderstore Mod Manager profile layouts are supported and found automatically; the manager's version records are updated after a successful commit (on next launch if the manager is running).

The helper and its data live in `BepInEx/MCE/`; queues, caches and backups are excluded automatically when you publish a modpack.

Related: [Modpack upgrades](packs#upgrade) · [Export and publish](export) · [FAQ](faq)
