# Export and publish {#top}

Save profiles before exporting and choose a format for your purpose. The publishing wizard previews the contents, creates a new output directory and shows its location when finished.

Open Management center from the overview toolbar and select Publish. Choose an output folder, then review the files in the final Preview tab. Its collapsible tree shows full relative paths when you select a file; Back returns to publication settings.

On this page: [Choose an export type](#types) · [Publishing steps](#steps) · [Package contents](#contents) · [Author identity and signing](#signing)

## Choose an export type {#types}

- **Translation text:** use Export translations/Missing translations in the settings sidebar to maintain dictionaries. See [Translation supplements](translation#supplement). Author-language exports retain valid translations; intentionally exporting English source text and reading it back preserves the English source.
- **Author data only:** share profiles, groups, introductions, translations and referenced resources with players who already have the mods.
- **Complete local modpack:** include plugins, configuration and the local files required to run.
- **Thunderstore package:** combine store dependencies with included local files in a ZIP ready for the author to upload.

## Publishing steps {#steps}

1. Organize profile parameters, enablement targets and introductions. Check each mod's [source](sources#types). Manage profiles supports selecting several profiles for export.
2. Open Management center from the overview toolbar and select Publish. Profile management and introduction export entries can also open publishing.
3. Select the publication type and enter a name, version, description and homepage. Store names use English letters, numbers and underscores.
4. Choose which selected profile will be the package default, then select publication languages and retain at least one. This package default is separate from **Default configuration**, the personal mode.
5. For a store package, select a 256×256 PNG icon and review store dependencies and included mods. Confirm a real dependency or include the local mod for each item; resolve unknowns before continuing. Modpack-pinned versions and the latest store versions are shown separately, and skipped mods include a reason.
6. Select extra files and signing. A complete local package generates config only from parameters recorded by the profile selected as the package default; explicitly select any other config files under Additional config files and inspect the preview. Authors should review their personal connection details themselves before sharing, then preview files, profiles and dependencies.
7. Confirm generation and wait for the final output location. The author performs local installation or store upload.

Normal exports are under `BepInEx/config/ModConfigEnhance/output/packs/<timestamp>/`. Each generation uses a new directory; the completion prompt gives the actual location.

## Package contents {#contents}

Author data includes selected profiles, referenced media and fonts, selected languages and source information. Local packages also collect plugins, resources and patchers. They can include BepInEx startup files and core, plus selected root-directory extras. Config files are generated only from parameters recorded by the profile selected as the package default; the active config directory is not copied wholesale. Select any other config files explicitly under Additional config files and review them in the preview. Authors should review their personal connection details themselves before sharing. The profile selected as the package default determines initial settings and enablement in the exported copy. **Default configuration** remains personal mode.

Store packages list downloadable mods as dependencies and include other files according to your selections. Additional core and patcher dependencies are supported. When the real store version is missing, confirm the full dependency string or include the mod locally.

Caches, logs, backups, old exports and the manager's personal runtime state are excluded. Review the preview and confirm that distributed media and fonts have the required permissions.

## Author identity and signing {#signing}

Sign author material verifies the integrity of author profiles, dictionaries, introductions and referenced resources. The signing identity is kept outside the game and manager profiles at `%LOCALAPPDATA%/ModConfigEnhance/signing/`.

After generating the first signed package, use Back up publication signing identity. Use Import publication signing identity when moving to another computer. The backup contains a private key; store it separately.

If signed material is invalid, affected author data is disabled while actual settings and personal profiles are retained. Obtain a complete author package again. Author data stays read-only even when published without signing.

Related: [Build a modpack](authoring) · [Modpack features](packs) · [Translation](translation) · [Sources and updates](sources)
