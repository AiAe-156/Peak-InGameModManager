# Modpack features {#top}

A **modpack** is a whole mod environment: mod files, configs, profiles, groups and introduction. The modpack library can hold several and switch between them at any time, each keeping its own configs and mod versions.

On this page: [Modpack library](#library) · [Switching modpacks](#switch) · [Temporary modpacks](#temporary) · [One environment, several profiles](#profiles) · [First install](#first-install) · [Upgrades and personal changes](#upgrade) · [Storage management](#storage) · [Default not applied](#retry)

## Modpack library {#library}

Open **Management center → Modpacks**, click **Import modpack**, choose a `.zip` or a folder with the right structure, and look at the preview:

1. **Banner**: name, version, type (a complete local pack works offline; a store-format pack needs some dependencies downloaded; an author-data pack has no mod files) and whether there are issues.
2. **Display language**: shown when the pack has language files; follows the game language by default and can be changed.
3. **Check results**: each issue with its cause and a suggestion. A pack with issues can still be stored, but cannot be enabled until they are fixed.
4. **Dependencies**: components already installed (only checked, never reinstalled), dependencies bundled or to be downloaded, and who uses them.
5. **Mods**: grouped by the author's folders.
6. **All files**: shown as a folder tree.

**Store in library (no game changes)** only saves it and **does not change your current game files**. A store-format pack needs "Approve download and complete" before anything is downloaded.

## Switching modpacks {#switch}

Select a pack in the library and click **Preview switch**; the preview lists what will be replaced. When confirming you have two options:

- **Save current and switch**: first save the current environment's final files and configs as a new version of the current pack, then switch.
- **Switch without saving**: the current pack's saved version stays as it is; the running environment is kept separately and restored when you switch back (still marked unsaved).

Switching replaces files that are running, so the update helper finishes it **after you quit PEAK**. Do not start the game before it is done; starting early cancels the switch and restores the previous environment.

- Updates waiting for a restart do not affect switching; updates still downloading, waiting for exit or needing recovery make the switch wait.
- MCE and the libraries it runs on always stay; BepInEx core files are checked, never changed; monomod and standalone scripts need manual handling.
- Configs the new pack does not have are not carried over from the old one. Language choice and local preferences are shared between modpacks (unless a pack chose its own language when stored).

## Temporary modpacks {#temporary}

If you have not used modpacks yet, before your first switch click **Save current setup as a temporary modpack** and give it a name. It copies a snapshot **immediately**, without quitting the game, so you can always switch back. Temporary modpacks are kept and never deleted automatically.

The library is in `BepInEx/MCE/packs/`; each pack keeps its own personal profiles, final configs and mod versions.

## One environment, several profiles {#profiles}

One modpack can have several profiles (for everyday play and different play styles). Profiles record settings, hotkeys and enable targets; groups, introduction, media and fonts are shared by all profiles of the pack. Language choice is global and does not follow profiles. See [Profiles and hotkeys](profiles).

## First install {#first-install}

After installing a modpack published by an author, the first time you reach the main menu MCE tries to apply the default profile the author chose, and tells you what still needs attention; if plugins were enabled or disabled, restart the game as prompted.

After that you can use the author preset as it is, or duplicate it into a personal profile under "Manage profiles…".

## Upgrades and personal changes {#upgrade}

The author's originals live in the plugin folder's `MCEPack`. When you edit an author preset its original baseline is kept; save in place or as your own, and restore the original whenever you like.

When a modpack is upgraded, your personal configs are backed up and kept first, then mods and author data are updated. Changed author profiles or translations are marked with blue markers so you can compare and decide what to adopt. After an upgrade it is worth opening "Manage profiles…" to compare the new author profile with your current configuration.

## Storage management {#storage}

**Storage management** on the Modpacks page lists the download cache, details cache, store downloads and operation backups, switch recovery backups and the modpack recycle area; expand an item to see its files.

- Deleting removes a whole item and rechecks the scope first; running tasks and protected items cannot be deleted.
- Deleted modpacks go to the recycle area first; clearing caches and emptying the recycle area cannot be undone.

## Default not applied {#retry}

If the first default profile could not be applied, a notice stays behind. Fix the missing mods, dependencies or locked files it mentions, go back to the main menu and click "Retry initial default" under "Sources and updates".

If an author signature warning appears, make sure the published files are complete and get the author's original package again; your live configuration and personal profiles are kept. See [Author identity and signing](export#signing).

Related: [Profiles and hotkeys](profiles) · [Build a modpack](authoring) · [Sources and updates](sources) · [Export and publish](export)
