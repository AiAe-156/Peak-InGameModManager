# Changelog {#top}

## 2.3 — Folder organization, nested groups and release preparation {#v2-3}

- **Folder organization:** organizes plugins into folders that match your groups. **It only moves folders and never renames mod folders**; you see a preview tree first. Empty folders can go to the Recycle Bin, and a window reports the result.
- **Groups:** groups can nest three levels; right-click a heading for a new subgroup, or drag a heading under another group. On first visit the overview can build groups from your existing category folders. Folding a group no longer redraws the whole list.
- **Better recognition:** resource packs (packages without a DLL), loose DLLs in the plugins folder, disabled files with any suffix, and mods nested inside other mods' folders are recognized and marked.
- **Base components:** can be given a display name, enabled, disabled and uninstalled (with an extra reminder before uninstalling).
- **Protected whitelist:** only MCE and the libraries it runs on — ModConfig, PEAKLib.Core and PEAKLib.UI — cannot be disabled or uninstalled; everything else is allowed. Enabling and disabling ask nothing; uninstalling asks first.
- **Markers:** seven status markers, each with its own colour, a fixed slot and a hover explanation; the name column is wider.
- **Translation:** every interface string has English source text plus Simplified and Traditional Chinese, and all of it goes into the translation export. XUnity AutoTranslator is no longer needed: option names, sections and choices on the Mod Settings page are translated by the manager's own language files, which now support `r:` regex rules; Import additions merges a filled-in export into your language file, and a reminder appears when XUnity runs online machine translation. The personal data folder is organized into language, profiles, output and data; see Translation › Where the files are.
- **User guide:** rewritten, with a three-minute start, an interface legend and a glossary.
- **Performance:** opening the overview no longer builds the list twice.
- **Game 3.1 support:** mod parameters show their decimals again (the new game version showed them as whole numbers and could store a wrong value while typing); in setting cells, Save and Default sit side by side and the dropdown is as tall as the text boxes. Every screen now uses a blue theme, with a new purple theme you can pick or have switch on at night (TimeTheme's palette is no longer read); the pin line is thinner and lime; subgroup rails get thinner and shorter per level. Buttons are coloured by function again (one set per theme), with a separate colour for the User guide; the Hotkeys tab numbers each mod the same way as the overview list. Exporting logs no longer crashes the game. A package that left several manifest.json copies with different versions no longer keeps offering an update that is already installed. Renamed to In-Game Mod Manager; existing profiles and settings are kept. On the settings page the sidebar is wider, the Search label and box are smaller and no longer overlap, toolbar captions shrink to fit, and group rows now stand out above mod rows.

## 2.2 — New update core {#v2-2}

- **Update helper rewritten:** works from your local files and queues dependencies automatically; new mods do not wait for the game to exit, only replacing a loaded DLL does; tasks running at the same time do not block each other.
- **Runs on its own:** install, update and uninstall are queued as soon as you click and no longer go through Save configuration.
- **Foreground window:** the helper appears only when there is work, minimizes while waiting for the game to exit, and never stays in the background.
- **Update history:** grouped by batch, with download speed and time; finished records are no longer announced again on every launch.
- **Manager profiles:** r2modman and Thunderstore Mod Manager profile layouts are supported with every feature, and the manager's version records are kept in sync.
- **Duplicate copies:** copies of the same mod can be ticked separately; ticking one disables the others.
- **Interface performance:** the overview opens and refreshes over several frames, with fewer stutters.

## 2.1 — Unified saving and environment management {#v2-1}

- **Configuration:** shared drafts for settings, hotkeys and enablement; save commits, scoped saves stay scoped. Author originals and recovery drafts remain available.
- **Resources:** installation, import, updates and uninstall are validated and run by the helper (queued directly since 2.2, no longer drafted).
- **Browsing:** live multi-field search, status-first filters and related-mod navigation including providers.
- **Review:** compare duplicate DLLs individually; separate dependency problems from unidentified sources, with retry and manual identification.
- **Reading:** store and GitHub links, revised feature guidance and fewer hidden-page ticks. FrameDoctor owns performance diagnosis.

## 2.0.0 — In-game modpack management {#v2-0-0}

Our largest update since the public 1.0.2 release. This release combines the intervening local development versions into one public update.

### For players

- An isolated modpack library, automatic ZIP/directory import and exact-version recursive dependencies, with personal profiles for each pack.
- Previewed adoption and exit-time switching save final configuration and updated mod files, protect the local host and restore the old environment on failure.
- A native mod overview with colored groups, full version information, authors, sources, dependencies, and shortcuts to settings, homepages and folders.
- Configuration profiles for settings, hotkeys, mod enable targets, groups and introductions, with personal copies of read-only author presets.
- Clear Save only and Apply configuration actions with categorized, grouped change reviews, visible actual loading state, and separate pending-apply and pending-restart notices.
- Pack-wide compatibility advice with pale-yellow badges, optional apply warnings and local ignores; personal profiles keep the advice.
- A grouped hotkey overview comparing current bindings, mod defaults and profile presets, with duplicate/unbound-key tools.
- Direct Thunderstore downloads, an independent update queue, version selection, rollback history, progress and result messages, and exit/restart handling.
- System/direct/custom HTTP(S) proxy choices and separate store/download connection tests.
- English, Simplified Chinese and Traditional Chinese built in; optional language dictionaries, translation export/reload and XUnity integration.
- The tree settings sidebar, descriptions, search, filters, pinning, gamepad navigation and optional TimeTheme colors remain part of the interface.

### For modpack authors

- A management center for pack/mod information, groups, profiles, updates, networking, tools, publishing and preview.
- An in-game introduction editor with formatted text, links, images, GIFs, video, fonts, media layout and Markdown interchange.
- Conflict presets for two or more mods with any-pair/all-enabled triggers, persistent pack drafts and signed exports.
- Multiple recommended profiles, a package default, author dictionaries, source associations and optional signing of author material.
- Author-data, complete-local-modpack and Thunderstore-format exports, with explicit config/attachment choices and readable pack names.
- Categorized file preview with a directory tree, text/Markdown/media inspection and export-specific text/config editing.
- Local exports leave one ZIP in the chosen output folder. Publication drafts and current-profile introduction edits have distinct save actions and clear notices.
- The release archive keeps plugin libraries, the Windows x64 update helper, examples and license notices together in their installable directory structure.

### Upgrading

Exit PEAK before replacing MCE. Keep personal data in `BepInEx/config/ModConfigEnhance/`. Existing language and overview data are migrated; choose and apply a profile when ready, then restart for plugin enablement changes.
