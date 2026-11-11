# Mod management {#top}

The mod overview lists every mod and base component in the current environment. Here you can find, enable, group and configure mods, or organize the plugins folder to match your groups.

On this page: [Reading a row](#list) · [Enabling and disabling](#enable) · [Groups](#groups) · [Search and filters](#filters) · [Base components](#components) · [Special mods](#special) · [Folder organization](#organize) · [Compatibility advice](#conflicts) · [Opening settings](#settings) · [Controls and appearance](#controls)

## Reading a row {#list}

From left to right: the **enable box**, row number, **name** (status markers at its top right, then the source label), **gear** (open settings), **home** (project and store pages), **folder** (open its location), version, author, description.

- Hover over the name to see its dependency card: which mods it needs, and which mods need it.
- Double-click the name to open the mod's action menu.
- If the gear is grey, hover over it for the reason: usually the plugin is not loaded this session, or it does not offer any settings.
- When an update is available, a green marker also appears beside the version; click it to choose a version.

See the [interface legend](overview#legend) for every marker and box colour.

## Enabling and disabling {#enable}

Tick or untick the enable box. The change goes into the draft (an orange marker appears by the name). Click **Save configuration** to write it; it **takes effect the next time PEAK starts**, and plugins already loaded keep running until you quit. Enabling and disabling ask no confirmation.

- When the same mod is installed in several folders, each copy has its own row; **ticking one copy disables the others**, so only one stays on.
- Disabling an outer mod also disables the nested mods inside its folder; enabling it again restores only the ones that were on.
- MCE itself and the libraries it runs on (ModConfig, PEAKLib.Core, PEAKLib.UI) cannot be disabled; their box is grey. Every other mod can be disabled. If other mods depend on it, saving warns you and you can confirm to continue.
- With r2modman or Thunderstore Mod Manager profiles, MCE finds the profile location automatically and enabling works as usual. While the manager is running, its own version records are synced on the next launch.

## Groups {#groups}

Groups only affect **how the list is shown**. They sort mods by purpose and belong to the modpack information, shared by all profiles.

- **Groups can nest up to three levels.** Right-click a group heading for "New subgroup", "Move to top level" or "Remove group"; drag a group heading onto another heading to put it underneath. Each level has its own coloured bar on the left.
- **Removing a group** never deletes files: its mods and subgroups move up one level (mods of a top-level group become ungrouped).
- Folding a group folds its subgroups too.
- In edit mode, drag selected mods onto a group heading or the blue insertion line to move them or change their order.
- The Management center's "Groups" tab edits names, colours, descriptions, parent groups and members in one place.

**First-time smart grouping**: when a profile has no groups at all, the first visit to the overview offers to build a group tree from the category folders already in plugins. It only reads folders, moves nothing, saves only if you confirm, and does not ask again.

## Search and filters {#filters}

- **Search**: filters about 0.25 s after you stop typing, matching original names, display names, package names, authors, versions and descriptions; separate keywords with spaces.
- **Tags**: filter by state (update available, duplicate, enabled, disabled, issues, changed) and by official store tags. Every included condition must match; any excluded condition hides the mod.
- **Hide disabled**: show only enabled mods.
- **Source filter**: show only store, local or pending mods; see [source types](sources#types).

The Mods and Hotkeys tabs each remember their own filters.

## Base components {#components}

The "Base components" section at the top of the list shows shared libraries in BepInEx's core, patchers and similar folders (such as MonoDetour and HookGen). Click a name to open its details:

- **Display name**: give it a friendlier name; the package name stays visible in grey.
- **Enable / Disable**: renames its DLLs to `.dll.d` (disabled) or back to `.dll` (enabled), effective next launch. Asks once.
- **Uninstall**: first an extra reminder ("base components are shared libraries; other mods may stop working without it"), then the safe preview checks files and dependencies before anything is removed.
- The components MCE runs on cannot be disabled or uninstalled.
- Base components **do not take part in groups or folder organization**.

## Special mods {#special}

Markers point out a few situations worth knowing about:

- **Nested mod** (amber): the mod's folder sits inside another mod's folder. Nested groups are recommended instead; folder organization moves it out.
- **Resource pack** (teal): a package with a manifest but no DLL, usually textures, audio or data for other mods. It is listed normally and can be enabled or disabled (its manifest is renamed).
- **Loose DLL** (pink): a single DLL directly in the plugins folder. Folder organization moves it into a folder of its own.
- **Disabled files**: any file ending in `.dll` followed by more characters (such as `.dll.old` or `.dll.disabled`) counts as a disabled copy. Files disabled by another tool should be restored with that tool.

## Folder organization {#organize}

Management center → "Tools" → "Organize into group folders" makes the plugins folder structure match your groups. It shows a preview tree first and runs only after you confirm.

- **Move only, never rename**: mod folder names never change. Only when two folders would collide does the later one get its version number appended.
- Group names become folder paths; nested groups become nested folders; mods without a group go to "Ungrouped".
- Mods already in the right place are not touched. MCE itself is organized too, moved last.
- Folders left empty are listed and ticked by default; they can go to the Windows Recycle Bin (restorable).
- The move runs as a task in the update helper; while the game is running it waits for you to quit. A window reports the result afterwards.
- A mod is skipped only when moving it could damage files (links, paths outside the game folder or too long, destination taken, and so on); the preview gives the reason.

## Compatibility advice {#conflicts}

An installed modpack can ship compatibility advice: mods that should not be enabled together. Affected mods show a pale yellow **!** marker; click it for the reason and source.

Before saving, conflicts you have not ignored are listed separately. You can go back and adjust, save anyway, or ignore it on this PC. Ignoring only turns off the active reminder; the marker and details stay, and a changed rule reminds you again.

## Opening settings {#settings}

Click the gear on a mod's row to open its settings; the gear column heading opens the general mod settings page. The tree sidebar on the left can be searched and pinned; the right side shows each option's description, default and allowed range. Values changed on a settings page also go into the draft and are committed on save. Every MCE screen comes in two colour themes: **Blue** (follows the game's settings menu) and **Purple** (the night style). Switch with the moon icon in the overview toolbar or to the right of **Search** on the settings page, or pick one under MCE's own **Layout › Colour theme**; **PurpleAtNight** turns purple at night when TimeTheme is installed. The settings page switches at once, the overview the next time it opens. **Go to keybindings** at the right of the sidebar header opens the overview on its Keybindings tab. Pinned rows have a thin lime line on the left; group rails get thinner and shorter with each level.

## Controls and appearance {#controls}

- Mouse: click to select, scroll to read, ESC closes dropdowns and dialogs first.
- Controller: stick or D-pad moves focus, A confirms, B goes back, LB/RB switch mods. Type with a physical keyboard or the platform's on-screen keyboard (Steam Deck: Steam+X).
- Theme, entry position and list animation speed are in MCE's own settings.
- Click "Refresh" to re-read changes on disk right away.

Related: [Profiles and hotkeys](profiles) · [Sources and updates](sources) · [FAQ](faq)
