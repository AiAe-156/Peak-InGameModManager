# Changelog

## 1.0.0 — 2026-11-11

First release of **In-Game Mod Manager** (游戏内模组管理器): manage PEAK mods, settings, hotkeys, translation, store updates and modpacks without leaving the game.

- **Mod overview:** every mod in coloured, nestable groups with version, author, source, dependencies and status chips; enable and disable with a tick; update, install and uninstall from the Thunderstore store; organize the plugins folder by group (it only moves folders, never renames them).
- **Settings:** all mod settings in one tree sidebar with search, pinning and a description panel; every hotkey on one page with duplicates marked; changes go into a draft and are saved together.
- **Profiles and modpacks:** save complete setups as profiles, keep several modpacks in a library and switch between them with a backup; write a modpack introduction with images and video; export a local package or a Thunderstore package.
- **Translation:** English, Simplified and Traditional Chinese built in; export untranslated texts, fill them in and import them back; regex rules for sentences that differ only by numbers. XUnity AutoTranslator is not needed.
- **Works with PEAK 3.1:** mod decimal settings display and accept decimals again.

### Coming from ModConfig Enhance

In-Game Mod Manager grew out of ModConfig Enhance and now ships as its own mod. ModConfig Enhance stays available as the lightweight settings-page version.

- On the first launch your settings, language files, profiles and introductions are **copied** from `BepInEx/config/ModConfigEnhance/` to `BepInEx/config/InGameModManager/`. The original folder is left untouched for ModConfig Enhance. Old modpack exports are not copied; the log says where they are.
- The working folder `BepInEx/MCE/` (update queue, modpack library, store records) is renamed to `BepInEx/IGMM/`.
- Modpacks, author data (`MCEPack` folders), language files with `MCE_` keys and environments published by ModConfig Enhance are still read.
- Running both at once changes the settings page twice. Both show a reminder; disable one of them.
