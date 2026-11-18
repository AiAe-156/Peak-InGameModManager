# Changelog {#top}

## 0.0.2 {#v0-0-2}

- **Log window:** pick several log levels at once, with each level shown in the same color as the log text; view the logs of the previous three game launches (each copy capped at 10 MB); the mod filter uses the same drop-down menu as the mod list; the filter buttons line up on the right; exported logs mask your user folder in more path spellings, and export errors follow the interface language.
- **Mod Settings tab:** a third tab next to Keybindings opens the mod settings page directly; with a controller the focus lands on "Jump to keybindings" there.
- **Mod list:** a faint hint under the last row explains dragging and editing in edit mode; rows play their loading sound every time the list is rebuilt; the dashed borders of the "Mod overview" entry match the "Mod settings" button.
- **Groups:** subgroups are listed above their parent's own mods (mod list, keybindings page and settings sidebar); when dragging a group heading in edit mode, dropping on its top or bottom edge places it before or after that group, dropping in the middle makes it a subgroup; new groups are prefilled "New group 1" / "New subgroup 1".
- **Controller:** LB / RB jump between the toolbar buttons (inside the management center and the update queue they still switch those pages); RT + LB / RB move the focus onto a tab (including Mod Settings) and A opens it; holding the D-pad or left stick keeps moving; with a controller the focus frame follows the selection, with keyboard and mouse it follows the control under the pointer, and each move to a new control plays a short click (the large tabs and game-style buttons use the game's own hover sound); tabs and every control have a focus frame; on the keybindings page the controller and keyboard columns have separate menus and up / down moves row by row; the settings page header and search box can be reached and left with a controller.
- **Menus:** every drop-down uses one style with a clear title row; the language drop-down shows only the language name so it no longer overlaps the arrow.
- **Export:** before building, the publish wizard lists installed mods that are not recorded in any selected profile (so they would be left out of the package), with their version and whether they are enabled. "Record into profiles and export" saves the ticked ones: the package's default profile keeps each mod's current state and the other selected profiles record it as disabled; the profiles are backed up first. "Export without recording" asks once more, then builds the package without them as before.

## 0.0.1 — First public test release {#v0-0-1}

In-Game Mod Manager expands the settings-focused ModConfig Enhance 1.0.4 into an in-game mod and modpack manager.

- **Mod overview:** versions, sources, enablement, update indicators, dependency information, nested groups and local-folder access in one list.
- **Installation and maintenance:** Thunderstore browsing, installation, updates and uninstalling; queued work, history, backups and recovery through the included helper. Loaded-file replacements finish after the game exits.
- **Settings and keybinds:** the settings tree, search, pinning and descriptions, plus a dedicated keyboard/gamepad binding page, configurable gamepad modifiers and duplicate-binding indicators. Settings-page edits and recorded bindings are written immediately.
- **Configuration profiles:** named sets of parameters, bindings and enablement targets; preview before applying; separate author presets and personal changes, with a change review and effect-timing information.
- **Modpack library:** import previews, separate mod versions and configuration for each environment, saved environment revisions and switching after the game exits.
- **Modpack authoring:** illustrated introductions with images, GIFs and video; export author data, complete local archives or Thunderstore packages. Complete archives collect startup patches automatically and honor explicit exclusions. Newcomer archives include BepInEx startup files and bilingual installation instructions; recognized private configuration values are cleared in export copies.
- **Independent translation tools:** built-in English, Simplified Chinese and Traditional Chinese, custom language files, export all/missing texts, import supplements and regular-expression translations without requiring XUnity AutoTranslator.
- **Gamepad operation:** navigation across settings, overview tabs and dialogs, stable list columns, configurable combinations, Steam on-screen keyboard and A-to-lock/B-to-leave reading panes. D-pad reading steps are three times the ordinary size.
- **Everyday tools:** log browsing/filtering/export, previewed folder organization, and blue/purple themes with optional TimeTheme night switching.
- **Independent identity:** a separate plugin, configuration folder and runtime directory, with support for reading earlier author material and modpacks.
- **Font compatibility:** ordinary interface text reuses the game's localized font instead of creating a font from Windows system fonts.
- **Installation and diagnostics fixes:** recognize split MonoDetour components locally, avoid duplicate resource rows, and export declared manager versions plus complete component locations and files. The all-in-one archive includes the official BepInEx 5.4.23.5 managed core for Unity 6 logging compatibility.
