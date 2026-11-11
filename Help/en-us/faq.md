# FAQ {#top}

Find the symptom, then follow the steps; links in each paragraph lead to the full explanation.

On this page: [Saving and taking effect](#profile) · [Enabling and uninstalling](#enable) · [Markers and hints](#markers) · [Updates and organizing](#updates) · [Translation](#translation) · [Introduction and media](#media) · [Input and other](#other)

## Saving and taking effect {#profile}

**Settings did not change after switching profiles**: switching only views; changes go into the draft. Click "Save configuration", then each item applies at its own timing. See [How a change takes effect](overview#flow).

**I changed enable states but nothing happened**: enabling and disabling take effect **the next time PEAK starts**; plugins already loaded keep running until you quit.

**Groups are unchanged**: make sure you clicked "Save configuration" or the save button on the groups page.

**How do I save an author preset**: the first save asks whether to save in place (keeps the author's identity and the original) or as a personal profile. A `*` after the name means it differs from the original.

**Where did old pending changes go**: they were turned into an unsaved draft; check and save them. Nothing runs automatically when you open the page.

## Enabling and uninstalling {#enable}

**Why are some enable boxes grey**: The manager itself and the libraries it runs on (ModConfig, PEAKLib.Core, PEAKLib.UI) cannot be disabled or uninstalled from the manager; loader files in BepInEx/core are never changed either. Files disabled by another tool should be restored with that tool. Every other mod can be enabled, disabled and uninstalled.

**Ticking one copy unticked another**: when the same mod is installed several times, only one copy can be enabled; the one you tick is kept.

**Other mods broke after disabling one**: the dependency warning when saving lists affected mods; enable it again, or check its dependency card to see who uses it.

**Why does uninstalling a base component ask twice**: base components are shared libraries often needed by several mods, so there is one extra reminder.

## Markers and hints {#markers}

**What are the little squares beside names**: see the [interface legend](overview#legend). Blue unread change, green update available, orange unsaved, violet pending restart, amber nested mod, teal resource pack, pink loose DLL; each has a fixed slot.

**Why does a disabled mod have a marker**: usually blue (it appeared or disappeared in this environment — an unread change) or violet (the enable change is written and waits for a restart). Hover over the marker for an explanation.

**How do I clear blue markers**: click the marker or the related control, or use "Sources and updates" → "Mark all notification badges as read".

**The gear is grey**: hover over it for the reason — usually the plugin is not loaded this session, or it offers no settings.

## Updates and organizing {#updates}

**An update keeps saying it is waiting for the game to exit**: it has to replace a DLL that is running; quit PEAK and the helper finishes automatically. New mods do not wait. See [Update queue](sources#update-queue).

**Where did the helper window go**: the helper appears only when there is work and minimizes while waiting for the game to exit; it exits when done.

**Update checks find nothing**: check the network, the global update permission and the mod's own permission; pending-source mods need their source confirmed first. On failure the old results are kept; try again later. See [Checking for updates](sources#cache).

**Does folder organization rename mod folders**: no. It only moves folders and keeps their names; only when two collide does the later one get its version appended. See [Folder organization](mods#organize).

**Empty folders left after organizing**: the result lists them, and you can send the ticked ones to the Windows Recycle Bin (restorable).

## Translation {#translation}

**Edited a dictionary but the old text stays**: check the current language group, then click "Reload texts"; if the original is showing, switch back to the translation first. When a personal and an author dictionary have the same entry, the personal one wins.

**A new language does not appear**: save a UTF-8 `.txt` into the personal language folder and click "Refresh". The file name starts with the lowercase language code; files starting with an underscore are hidden.

**A supplement file is not used**: the recommended way is Import additions, which merges a filled-in export into the language file. A part file such as `zh-cn_supplement.txt` placed directly in `language` still joins `zh-cn`; use Reload translations after changing it. `zh-cn-funny.txt` is a separate version chosen on its own. See [Edit and supplement](translation#supplement).

**Some texts are machine-translated or change between launches**: XUnity AutoTranslator is probably running online machine translation. Choose Turn off its machine translation in the reminder, or clear Endpoint in `AutoTranslatorConfig.ini`. See [XUnity AutoTranslator](translation#xunity).

## Introduction and media {#media}

**The introduction page does not appear**: make sure the current modpack has "Show introduction" on and the text is not empty.

**Images or fonts are missing**: check the resources referenced in the modpack workspace, then save or export the media again.

**A video does not play**: check the file format and the system's codecs, or use a file this PC can play. See [Media editing](authoring#media).

## Input and other {#other}

**Typing with a controller**: A enters the input box; use a physical keyboard or the platform's on-screen keyboard (Steam Deck: Steam+X); B leaves editing.

**Author signature warning**: get the author's complete original package again; your live configuration and personal profiles are kept. See [Author identity and signing](export#signing).

**Viewing the runtime log**: click "Logs" in the toolbar to filter by level and mod, search, copy, or export the log for a mod author. Exporting opens a Save As window to pick the location; if that window cannot open, the file is saved to `BepInEx\config\ModConfigEnhance\output\logs\` and the status line shows the path.

Related: [Feature overview](overview) · [Mod management](mods) · [Profiles and hotkeys](profiles) · [Sources and updates](sources)
