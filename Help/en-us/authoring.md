# Build a modpack {#top}

Prepare a playable profile, organize the modpack information and introduction, then open the publishing wizard. This page covers introduction editing and preparation. See [Export and publish](export) to generate files. · [Edit the MCE guide](#guide-edit)

On this page: [Prepare the profile](#prepare) · [Text and Markdown](#markdown) · [Images, animation and video](#media) · [Save and prepare publication](#save)

## Prepare the profile {#prepare}

Create or copy a configuration preset in Manage profiles and record the settings, hotkeys and enablement to publish. Preset and modpack names are independent. Information, introduction, groups and media belong to the shared modpack workspace. Author presets retain their identity locally; first save offers updating in place or saving a new configuration. Use edit mode to fill and save the modpack name, version, author, date and introduction title.
Enable Show modpack introduction and prepare valid content to display the tab. An empty tab title uses the default title. Introduce the gameplay, installation requirements, common controls and updates so players know how to start.

## Text and Markdown {#markdown}

Enter edit mode on the introduction page and type as in a word processor. Enter creates a paragraph; Shift+Enter inserts a line break within a paragraph. Select text by dragging, then use the toolbar for headings, lists, quotations, font size, colors, background and alignment.

The link tool sets a website address. Click it while reading, or Ctrl+click while editing. Ctrl+Z/Y undoes or redoes, and Ctrl+S saves. Heading, list and quotation markers at paragraph starts can convert the paragraph's style.

More contains Markdown source, import/save Markdown and a sample draft. MCE Markdown includes extensions that preserve text formatting and media layout. Creating content with the editor is the easiest way to retain those features. Preview a loaded sample, then confirm and save to adopt it.

## Images, animation and video {#media}

Use Insert to choose an image, GIF or video. Click media to select it and drag its corners to resize. The floating toolbar controls alignment, dimensions, captions and replacement. Drag the six-dot handle on the left to move media; dropping beside another media item creates a side-by-side arrangement.

PNG/JPEG and GIF are supported, along with the existing MP4/WEBM/MOV/AVI/M4V/OGV video playback. Video requires a compatible system decoder. Controls below the media provide playback, pause, seeking and volume. Fonts can use TTF/OTF/TTC files from the modpack workspace font directory; check redistribution permission before publishing.

## Save and prepare publication {#save}

Content is saved as an MCE document with a compatible Markdown output. The introduction and referenced resources belong to the shared modpack workspace. Wait for an import to finish before saving or switching profiles.

Before publishing, inspect text, links, images and video in reading mode. Save the profile and verify its default configuration and [mod sources](sources#types). Export collects media and fonts referenced by the shared introduction. Then use the [publishing wizard](export#steps) to select languages, files and signing.

## Edit the MCE guide {#guide-edit}

Enable **Allow editing user guide** under **5. Authoring** in MCE settings. The default is off. All guide chapters and the changelog use the same toolbar as introductions, including formatting, links, images, video, fonts and Markdown source.

Changes are saved per language in `config/ModConfigEnhance/data/help/<language>/`, independently of profiles. Save before changing chapters or leaving; unsaved work offers Save, Discard or Continue editing. Disabling editing keeps saved changes visible. **Restore this page** restores the current language and chapter, moving its override into backups.

Choose **More → Export guide data** to export all three languages, rich documents and assets. Copy the exported `Help/` into the MCE project for the next build. Body text is embedded; media and fonts are distributed with the MCE plugin. Heading anchors survive title edits and export. The edit switch is personal and is not enabled by modpack defaults.

Related: [Modpack features](packs) · [Export and publish](export) · [Media issues](faq#media)

## Modpack conflict advice {#conflicts}

In author mode, open **Management center → Publish → Modpack conflict presets**. Add a rule, select at least two independently toggled mod packages, and explain the problem. Mods bundled together appear as one toggle unit. Missing saved members stay in the rule.

**Any two enabled** warns for A+B, A+C, B+C or all three. **All enabled together** warns only for A+B+C. These are advisory rules: players may always continue applying. Saved rule drafts are pack-wide and survive closing the management center. Export distributes them to all profiles through `MCEPack/conflicts.json`, including author-data signing. Personal-profile exports and local ignore choices remain separate.

