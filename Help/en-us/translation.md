# Translation {#top}

Language selection controls the manager's interface and the mod texts it displays: mod names and descriptions in the overview, the sidebar, the description panel and the option names, sections and choices on the Mod Settings page. It is global and stays selected when switching profiles.

The manager uses **English source text with built-in Simplified and Traditional Chinese**, so all three display correctly with no language files installed. Author and personal language files can override the built-in wording; other languages use the same dictionary format — export the texts to translate and fill them in. XUnity AutoTranslator is not needed.

On this page: [Everyday use](#language) · [Edit and supplement](#supplement) · [Dictionary organization](#files) · [Where the files are](#folders) · [Regex rules](#regex) · [XUnity AutoTranslator](#xunity)

## Everyday use {#language}

Choose Language to select a translation. After changing a language file by hand, use Reload translations. For a new language or independent variant, use Refresh first. Use Show original/translated text to compare: it switches the mod texts the manager displays; the manager's own interface keeps the chosen language.

Double-click a mod name, description or short hotkey description in the overview to edit its entry in the current language. The change is written to your language file for that language; leaving the field empty restores the author or built-in text.

## Edit and supplement {#supplement}

1. Select the current language in the Mod Settings sidebar.
2. Use Export translations for all source text, or Missing translations for entries the current language does not cover yet.
3. To restrict the scope, check Export selected only and select mods. The manager itself is always included.
4. Open the file in `output/translation` and fill only the right side of each equals sign. Preserve the source text or key on the left exactly.
5. Save as UTF-8, then use Import additions and pick the file. Filled-in lines are merged into the current language file; the same source text is replaced by the imported one, empty lines are skipped and the language file is backed up first.

Preserve escapes and placeholders including `\=`, `\n` and `{0}`. Do not add, remove or alter characters on the left of the separator. For a brand-new language, save the export as `language/<code>.txt` (for example `de-de.txt`), use Refresh and select it.

## Dictionary organization {#files}

Author dictionaries live in `BepInEx/plugins/<modpack>/MCEPack/language/`. Personal dictionaries live in `BepInEx/config/ModConfigEnhance/language/`, one file per language.

Display precedence is **personal translation → author translation → built-in language → English source**. Within one file, an exact line wins over a regex rule. Lines starting with `MCE_` change the manager's own interface text; leave them out to keep the built-in wording.

The guide includes Simplified Chinese, English and Traditional Chinese articles, selected by interface language; other languages currently use English. Articles are read-only by default. Enable Allow editing user guide in the manager's settings to edit and save local overrides per language; personal overrides take precedence. Restoring the built-in page first backs up the override. See [Edit the guide](authoring#guide-edit).

`zh-cn-funny.txt` is an independent variant selected separately. Set a display name with `// display: Name` on the first line. Missing-translation export checks the currently selected language. Files whose names start with `_` and subfolders are ignored; use them for backups.

Part files are still read: `zh-cn_extra.txt` joins the `zh-cn` group, after the base file. Author packs use this; for your own additions Import additions is simpler.

## Where the files are {#folders}

Everything the manager keeps for you is under `BepInEx/config/ModConfigEnhance/`, in four folders:

- `language/`: your language files.
- `profiles/`: configuration profiles, the modpack introduction and its media.
- `output/`: things you export. `translation/` holds Export translations and Missing translations, `packs/` holds published modpacks and introduction exports, `logs/` holds log exports when the Save As window is unavailable.
- `data/`: the manager's own data. `runtime/` is state and store caches, `backups/` all automatic backups, `help/` your edits to this guide, `legacy/` data from versions before October 2026.

Installed modpacks and the update helper's queue and backups are in `BepInEx/MCE/`. Versions before 2.5.2 kept these folders side by side; the first launch moves them into place and backs up the state files to `BepInEx/MCE/layout-backups/<time>/` first.

## Regex rules {#regex}

For texts that differ only by a number or a name, write a regex rule: `r:"^(\d+) players online$"=$1 players online`. `$1` refers to the first bracketed group, `${name}` to a named group. The format is the same as XUnity AutoTranslator dictionaries, so rules can be copied from there. When several rules match, the one written later wins. A broken rule is skipped, and its file and line are shown when the language is applied.

## XUnity AutoTranslator {#xunity}

The manager translates on its own and no longer writes to XUnity's dictionaries, changes its language or blocks its machine translation. If XUnity is installed with online machine translation on (Endpoint in `AutoTranslatorConfig.ini` is not empty), its machine results would mix with these translations, so the manager reminds you once per launch the first time the overview opens. Choose Turn off its machine translation to back up the ini and clear its Endpoint lines (takes effect next launch), or Don't remind again. With machine translation off, XUnity and the manager do not interfere with each other.

Related: [Publishing languages](export#steps) · [Translation troubleshooting](faq#translation) · [Changelog](changelog#top)
