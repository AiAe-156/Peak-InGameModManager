# Profiles and hotkeys {#top}

A **profile** stores exactly three things: settings, hotkeys and mod enable targets. Groups, introduction, media and mod display names are **modpack information**, shared by every profile of the current modpack; switching profiles does not switch them.

On this page: [Choosing and managing profiles](#manage) · [Drafts](#drafts) · [Saving](#apply) · [Effect timing](#timing) · [Checks before saving](#checks) · [The three hotkey columns](#keys)

## Choosing and managing profiles {#manage}

Click the **Profile: xxx ▾** dropdown at the top left of the toolbar:

- The first, green row **Manage profiles…** opens the Profiles tab of the Management center.
- Below it are all profiles; click one to **view** it. Switching only previews and does not change the configuration in use; to use it, click **Save configuration**.

The Profiles tab lets you create, duplicate, rename, delete and export profiles, edit settings, or record the current live configuration into a profile.

- **Author presets** keep the original. The first time you save changes you can save in place (keeping the author's identity) or as a personal profile; "Restore original preset to draft" is always available. A `*` after the name means it differs from the original.
- **Export** writes a plain configuration file `.mceconfig.json` with only the saved settings, hotkeys and enable targets — handy for sharing a setup. **Imported** content goes into the draft first. Full mods and assets need a modpack; see [Export and publish](export).

## Drafts {#drafts}

Settings in the overview, controls on the original mod settings pages, hotkeys and enable targets all go into the **draft** when changed.

- Items with draft changes show an orange marker; the profile name gets a `*`.
- When you leave the page or switch profiles, MCE asks whether to save, discard or keep editing.
- The draft is saved automatically, so you can continue after a crash or restart.
- If another tool changes a field you did not touch (for example editing a cfg by hand), it is merged automatically; if both sides changed the same field, you choose which value to keep.

## Saving {#apply}

1. Make the setting, hotkey, enable and modpack-information changes you want.
2. To check them, open **Change review**: it lists old and new values and effect timing by category and mod.
3. Click **Save configuration** to commit the whole draft of the current profile.

Pages also have partial saves (Save hotkeys, save settings, save groups, save introduction) that commit only their scope and keep other drafts.

**Installing, updating and uninstalling are not part of saving**: they go into the update queue as soon as you click, without needing or triggering Save configuration. See the [update queue](sources#update-queue).

## Effect timing {#timing}

Saving and taking effect are two different things. Each item shows when it actually applies:

- **Immediately**: the mod supports changes while running.
- **Next round**: used when the next round starts.
- **After restart**: always the case for enabling and disabling; plugins already loaded keep running until you quit.
- **Decided by the mod**: shown when the mod gives no reliable information.

Config files of mods that are not loaded can still be edited and saved offline; they are used once the mod is enabled.

## Checks before saving {#checks}

When you save, MCE lists issues you should know about. **Most are only warnings** — confirm once and continue:

- **Dependencies**: missing, disabled and too-old dependencies are told apart. Installed mods with a sufficient version are never reported. You can confirm and save anyway.
- **Duplicates**: the same plugin installed several times, each with its version and path.
- **Unidentified source**: only affects automatic update checks; re-identify in one click or choose manually.
- **Compatibility advice**: combinations the modpack author marked as not recommended; see [Compatibility advice](mods#conflicts).

Items that can be saved are committed; the odd invalid item (wrong type and so on) stays in the draft with the reason. If writing enable states fails, you can skip enable changes and save the rest. A failed file write rolls back this round of changes.

## The three hotkey columns {#keys}

Each key on the Hotkeys tab has three columns:

- **White: current key**, the actual binding. Click to record a new key into the draft; Save hotkeys commits it.
- **Green: mod default**, the key the mod declares; click to put it in the draft.
- **Blue: profile target**, the key recorded in the current profile; click to record, commit on save.

The grey number before each mod name matches its number in the overview list; mods not in the overview's current filter show no number.

The conflict check includes items hidden by search. Right-click to ignore a conflict between two items, or to turn the reminder back on.

Related: [First install](packs#first-install) · [Translation](translation) · [FAQ](faq#profile)
