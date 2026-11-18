# -*- coding: utf-8 -*-
"""1.1.4 second review (keys / settings / export), confirmed items:
1 scrub missed the profile snapshot (config-values.json / profile.json carry every serializedValue) and Data mode;
  relative paths for the language skip; secret/token need a word end too;
2 settings commit could vanish silently when ModalParent is null (author-profile choice / external-change prompts);
4 modifier unify: duplicate combos on the same button, and choosing "none" was undone by Load;
5 "ignore conflict" for a controller-combo clash always failed → those items are left out of the menu;
6 official icon TMP without a font measured 0 wide while inactive;
7 newcomer doorstop ini: only the standard one (no override paths) is copied;
8 EntryNavLink.ReturnFocus cleared when the overview fails to open."""
import io, os, re
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

# 1 scrub: shared rule for snapshot values, relative path skip, word-bounded secret/token
edit('Overview/PublicationScrub.cs', [
('''(?<![a-z])secret|(?<![a-z])token|''', '''(?<![a-z])secrets?(?![a-z])|(?<![a-z])tokens?(?![a-z])|'''),
('''            string folder = file.Replace(Path.DirectorySeparatorChar, '/').ToLowerInvariant();''',
 '''            string folder = ("/" + file.Substring(configRoot.Length).TrimStart('\\\\', '/')).Replace(Path.DirectorySeparatorChar, '/').ToLowerInvariant();'''),
('''    private static bool Private(string key, string value)''', '''    /// <summary>1.1.4: the profile snapshot (config-values.json, profile.json) carries every value too; clear the same
    /// values there before it is serialised, or the importer would restore what the cfg scrub removed.</summary>
    internal static List<string> ScrubSnapshot(OverviewConfigSnapshot? snapshot)
    {
        var cleared = new List<string>();
        if (snapshot?.values == null) return cleared;
        foreach (var value in snapshot.values)
        {
            if (value == null || !Private(value.section + " " + value.key, value.serializedValue ?? "")) continue;
            value.serializedValue = "";
            cleared.Add((value.configFile.Length > 0 ? value.configFile : value.pluginGuid) + " → " + value.section + " / " + value.key);
        }
        return cleared;
    }
    internal static bool Private(string key, string value)'''),
])
edit('Overview/OverviewRepository.Profiles.cs', [
('''        // Export reads committed content. Browsing/exporting must never commit unrelated drafts.
        var profiles = Profiles.ListProfiles().Select(Clone).ToList();''',
 '''        // Export reads committed content. Browsing/exporting must never commit unrelated drafts.
        var profiles = Profiles.ListProfiles().Select(Clone).ToList();
        // 1.1.4: private-looking values never leave in a profile snapshot (clones only; stored profiles are untouched).
        foreach (var profile in profiles) PublicationScrub.ScrubSnapshot(profile.config);'''),
])
edit('Overview/PackPublisher.cs', [
('''        var items = repo.PrepareExport(request.Profiles);''', '''        PublicationScrub.ScrubSnapshot(defaults.config); // the default profile's values are written into the package too
        var items = repo.PrepareExport(request.Profiles);'''),
])

# 2 commit with no modal host: give the settings page a host for the duration of the commit
edit('Overview/NativeConfigurationDraft.cs', [
('''            var mods = new OverviewCatalogue().Scan(repo);
            ConfigurationSaveRequest.Run(repo, mods, ConfigurationSaveScope.ModParameters, guid, result => {''',
 '''            var mods = new OverviewCatalogue().Scan(repo);
            // 1.1.4 review: the save can ask (author profile → save as, file changed outside → overwrite?). Those prompts
            // need a modal host; on the settings page there may be none, and the save would end silently unsaved.
            if (OverviewUi.ModalParent == null) OverviewUi.ModalParent = SettingsHost();
            ConfigurationSaveRequest.Run(repo, mods, ConfigurationSaveScope.ModParameters, guid, result => {'''),
('''    internal static void Commit(string guid)''', '''    /// <summary>The top-level canvas of the pause / main menu (where ModConfig's settings page lives).</summary>
    private static UnityEngine.Transform? SettingsHost()
    {
        var page = UnityEngine.Object.FindAnyObjectByType<PEAKLib.ModConfig.Components.ModSettingsMenu>();
        if (page == null) return null;
        var canvas = page.GetComponentInParent<UnityEngine.Canvas>();
        return canvas != null ? canvas.rootCanvas.transform : page.transform.root;
    }
    internal static void Commit(string guid)'''),
])

# 4 modifier: dedupe by button; "none" sticks
edit('GamepadCombos.cs', [
('''            if (next.Length > 0)
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == next);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = next;
            }
            _modifier = next; Save();''', '''            if (next.Length > 0)
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == next);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = next;
            }
            else foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = ""; // "none": the combos become single buttons
            Dedupe();
            _modifier = next; Save();'''),
('''            if (_modifier.Length == 0) { var first = _items.FirstOrDefault(i => i.Mod.Length > 0); if (first != null) _modifier = first.Mod; }
            else if (_items.Any(i => i.Mod.Length > 0 && i.Mod != _modifier))
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == _modifier);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = _modifier;
                Save();
            }''', '''            if (_modifier.Length == 0 && !_storedNone) { var first = _items.FirstOrDefault(i => i.Mod.Length > 0); if (first != null) _modifier = first.Mod; }
            if (_items.Any(i => i.Mod.Length > 0 && i.Mod != _modifier))
            {
                if (_modifier.Length > 0) _items.RemoveAll(i => i.Mod.Length > 0 && i.button == _modifier);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = _modifier;
                Dedupe(); Save();
            }'''),
('''    private static void Save()''', '''    /// <summary>After unifying the modifier two combos can end up on the same buttons; keep the first.</summary>
    private static void Dedupe()
    {
        var seen = new HashSet<string>();
        _items.RemoveAll(i => !seen.Add(i.Mod + "+" + i.button));
    }
    private static void Save()'''),
])
p = os.path.join(SRC, 'GamepadCombos.cs'); s = io.open(p, encoding='utf-8').read()
assert 'var store = JsonConvert.DeserializeObject<Store>' in s

edit('GamepadCombos.cs', [
('''                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "select";''',
 '''                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "select";
                _storedNone = store?.modifier == ""; // the user chose "none"; Load must not re-derive a modifier'''),
('''    private static string _modifier = "select";''', '''    private static string _modifier = "select";
    private static bool _storedNone;'''),
])

# 5 combo clashes cannot be ignored through the keyboard-signature record: leave them out
edit('Overview/OverviewHotkeyView.cs', [
('''        foreach(var conflict in entry.Conflicts.Where(k=>!k.IsGame).ToList()){''', '''        foreach(var conflict in entry.Conflicts.Where(k=>!k.IsGame&&!k.Combo).ToList()){'''),
])

# 6 icon font
edit('KeyChips.cs', [
('''                icon.transform.SetParent(root, false); icon.spriteAsset = sheet; icon.richText = true; icon.parseCtrlCharacters = false;''',
 '''                icon.transform.SetParent(root, false); if (TMP_Settings.defaultFontAsset != null) icon.font = TMP_Settings.defaultFontAsset;
                icon.spriteAsset = sheet; icon.richText = true; icon.parseCtrlCharacters = false;'''),
])

# 7 doorstop ini: standard only when the override paths are empty
edit('Overview/PackPublisher.cs', [
('''                && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\\s*enabled\\s*=\\s*true\\s*$");''',
 '''                && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\\s*enabled\\s*=\\s*true\\s*$")
                && !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\\s*(dll_search_path_override|boot_config_override|doorstop_dll_search_override|mono_dll_search_path_override)\\s*=\\s*\\S");'''),
])

# 8 ReturnFocus cleared on failed open
edit('Overview/OverviewController.cs', [
('''        try { EnsureShell(); } catch(Exception error) { FailOpen(error);return; }''',
 '''        try { EnsureShell(); } catch(Exception error) { EntryNavLink.ReturnFocus = false; FailOpen(error);return; }'''),
])
print('ok')
