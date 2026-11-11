# Changelog {#top}

## 1.0.0 — First release {#v1-0-0}

The first release of In-Game Mod Manager.

- **Mod overview:** every mod in coloured, nestable groups with version, author, source, dependencies and status chips; enable and disable with a tick; update, install and uninstall straight from the Thunderstore store; organize the plugins folder by group (it only moves folders, never renames them).
- **Settings:** all mod settings in one tree with search, pinning and a description panel; every hotkey on one page with duplicates marked; changes go into a draft and are saved together.
- **Profiles and modpacks:** save a whole setup as a profile; keep several environments in the modpack library and switch with a backup; write a modpack introduction with images and video; export a local package or a Thunderstore package.
- **Translation:** English, Simplified and Traditional Chinese built in; export untranslated texts, fill them in and import them back; regex rules for sentences that differ only by numbers. XUnity AutoTranslator is not needed.
- **PEAK 3.1 support:** mod decimal settings display and accept decimals again.

### Coming from ModConfig Enhance {#from-mce}

- On the first launch, settings, language files, profiles and introductions are **copied** from `BepInEx/config/ModConfigEnhance/` to `BepInEx/config/InGameModManager/`. The original folder is left for ModConfig Enhance. Old modpack exports are not copied; the log says where they are.
- The working folder `BepInEx/MCE/` is renamed to `BepInEx/IGMM/`.
- Modpacks, author data (`MCEPack` folders) and language files with `MCE_` keys published by ModConfig Enhance are still read.
- Running both changes the settings page twice. Both show a reminder; disable one of them.
