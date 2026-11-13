#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using BepInEx.Configuration;
using HarmonyLib;
using PEAKLib.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.UI;

namespace ModConfigEnhance.Overview;

internal static class OverviewOptions
{
    internal static ConfigEntry<bool> Enabled = null!, ShowPackName = null!, ShowPackVersion = null!;
    internal static ConfigEntry<bool> ShowSourceLabels = null!;
    internal static ConfigEntry<float> EntryLeft = null!, EntryTop = null!, AppearanceSpeed = null!;
    internal static ConfigEntry<string> ManagerProfileDirectory = null!;
    internal static ConfigEntry<bool> AutomaticUpdates = null!;
    internal static void Bind(ConfigFile config)
    {
        const string section = "4. Mod overview";
        AutomaticUpdates=config.Bind(section,"Automatic update checks",true,"Check update metadata every 30 minutes in the main menu or Airport only. Downloads and installation require confirmation.");
        ShowSourceLabels = config.Bind(section, "Show source labels", true, "Show rounded Store / Local source labels; pending labels appear in edit mode.");
        Enabled = config.Bind(section, "Enabled", true, "Show Mod overview in the pause menu and main-menu settings.");
        EntryLeft = config.Bind(section, "Entry left", 32f, new ConfigDescription("Distance from the left edge, in game canvas units.", new AcceptableValueRange<float>(0, 1800)));
        EntryTop = config.Bind(section, "Entry top", 75f, new ConfigDescription("Distance from the top edge, in game canvas units.", new AcceptableValueRange<float>(0, 1000)));
        ShowPackName = config.Bind(section, "Show pack name", true, "Display the pack name above the overview entry when filled.");
        ShowPackVersion = config.Bind(section, "Show pack version", true, "Display the pack version on a small second line when filled.");
        AppearanceSpeed = config.Bind(section, "Appearance speed", 2.5f, new ConfigDescription("Speed multiplier for the overview's row reveal and fade. 1 matches the settings page, larger values are faster. Applies on the next overview entry or tab change; the settings page is unchanged.", new AcceptableValueRange<float>(.5f, 5f)));
        ManagerProfileDirectory = config.Bind(section, "Manager profile directory", "", "Optional exact r2modman/Thunderstore profile folder containing mods.yml and BepInEx. Leave empty for manual installation. Close the manager before changing enabled state.");
    }
}

internal static class OverviewController
{
    private static readonly List<OverviewEntry> Entries = new();
    private static MenuAPI.BuilderDelegate? _builder;
    private static MenuAPI.BuilderDelegate? _settingsBuilder;
    private static bool _running;
    internal static void Register(Harmony harmony)
    {
        _running = true; _builder ??= Build; MenuAPI.AddToPauseMenu(_builder);
        _settingsBuilder ??= BuildSettings; MenuAPI.AddToSettingsMenus(_settingsBuilder);
        TypingGuard.TryApply(harmony);
        OverviewSettingsNavigator.Register(harmony);
    }
    private static void BuildSettings(Transform parent)
    {
        var page = parent.GetComponent<MainMenuSettingsPage>();
        if (!_running || page == null || parent.Find("MCE_OverviewSettingsEntry") != null) return;
        try
        {
            ModConfigPaperStyle.Resolve(parent);
            var root = OverviewUi.Rect("MCE_OverviewSettingsEntry", parent);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var entry = root.gameObject.AddComponent<OverviewEntry>();
            entry.Initialize(page, true); Entries.Add(entry);
        }
        catch (Exception ex) { Plugin.Log.LogError("[模组概览] 创建设置入口失败：" + ex); }
    }
    private static void Build(Transform parent)
    {
        if (!_running || parent.GetComponent<PauseMenuMainPage>() == null || parent.Find("MCE_OverviewEntry") != null) return;
        try
        {
            ModConfigPaperStyle.Resolve(parent);
            var root = OverviewUi.Rect("MCE_OverviewEntry", parent);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var entry = root.gameObject.AddComponent<OverviewEntry>();
            entry.Initialize(parent.GetComponent<PauseMenuMainPage>()); Entries.Add(entry);
        }
        catch (Exception ex) { Plugin.Log.LogError("[模组概览] 创建入口失败：" + ex); }
    }
    internal static void RefreshEntries() { foreach (var entry in Entries) if (entry != null) entry.Refresh(); }
    /// <summary>
    /// 2.4.1：模组设置页表头「跳转到快捷键设置页」。找与设置页同一个菜单（同一 UIPageHandler）的概览入口，
    /// 打开概览并切到快捷键设置页签；概览未建好时走入口的分帧打开，建好后再切页签。
    /// </summary>
    internal static void OpenHotkeys(UIPage? from)
    {
        if (from == null) return;
        var handler = from.GetComponentInParent<UIPageHandler>(true);
        foreach (var entry in Entries)
            if (entry != null && entry.BelongsTo(handler)) { entry.OpenOnHotkeys(from); return; }
        Plugin.Log.LogWarning("[模组概览] 跳转快捷键设置页：找不到当前菜单的概览入口。");
    }
    internal static void Dispose()
    {
        OverviewSettingsNavigator.Dispose();
        _running = false;
        var field = AccessTools.Field(typeof(MenuAPI), "pauseMenuBuilderDelegate");
        if (field != null && _builder != null && field.GetValue(null) is Delegate callbacks) field.SetValue(null, Delegate.Remove(callbacks, _builder));
        var settingsField = AccessTools.Field(typeof(MenuAPI), "settingsMenuBuilderDelegateNEW");
        if (settingsField != null && _settingsBuilder != null && settingsField.GetValue(null) is Delegate settingsCallbacks) settingsField.SetValue(null, Delegate.Remove(settingsCallbacks, _settingsBuilder));
        foreach (var entry in Entries) if (entry != null) UnityEngine.Object.Destroy(entry.gameObject);
        Entries.Clear(); OverviewUi.CloseModal(); OverviewUi.ModalParent = null;
        OverviewEditableFrame.ResetFrames();
    }
}

internal sealed class OverviewEntry : MonoBehaviour
{
    private UIPage? _parent;
    private bool _settingsEntry;
    private OverviewPage? _page;
    private IEnumerator? _shell;
    private bool _opening;
    private float _shellAt=-1;
    private int _stage;
    private int _builtEpoch=-1;
    private void EnsureShell()
    {
        // 2.4.0: colours are baked in when the page is built; a page built under the other theme is rebuilt.
        if(_page!=null && !_opening && _builtEpoch!=ModConfigLook.Epoch && !_page.gameObject.activeSelf)
        { (_shell as IDisposable)?.Dispose();_shell=null;_page.DisposeResources();UnityEngine.Object.Destroy(_page.gameObject);_page=null; }
        if(_page!=null || _parent==null)return;
        _builtEpoch=ModConfigLook.Epoch;
        var rect=OverviewUi.Rect("MCE_OverviewPage",_parent.transform.parent);OverviewUi.Fill(rect);
        rect.gameObject.SetActive(false);
        _page=rect.gameObject.AddComponent<OverviewPage>();_shell=_page.BuildShell(_parent);
    }
    private void PumpShell()
    {
        try
        {
            if(_shell==null)return;
            // Advance cheap shell steps together; only yield after actual work used the budget.
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int steps=0;steps<8 && _shell!=null;steps++)
            {
                if(!_shell.MoveNext()) { (_shell as IDisposable)?.Dispose();_shell=null;break; }
                if((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency>=4)break;
            }
        }
        catch(Exception error) { FailOpen(error); }
    }
    private void FailOpen(Exception error)
    {
        Plugin.Log.LogError("[概览] 页面准备失败："+error);
        (_shell as IDisposable)?.Dispose();_shell=null;
        if(_page!=null) { _page.DisposeResources();UnityEngine.Object.Destroy(_page.gameObject);_page=null; }
        _opening=false;_driven=false;_openHotkeys=false;_shellAt=Time.unscaledTime+5;
    }
    /// <summary>2.5.4（二次审查）：打开中途用户离开——不再推进，也不在下次回来时突然切页；跳快捷键的意图一并作废。</summary>
    private void CancelOpen() { _opening=false; _driven=false; _openHotkeys=false; }
    private TextMeshProUGUI? _name, _version;
    private Button? _button;
    private RectTransform? _settingsReference;
    private TextMeshProUGUI? _settingsReferenceText, _settingsCaption;
    private float _settingsReferenceRetryAt;
    private SettingsEntryLayout? _settingsLayout;
    private float _left, _top;
    private bool _showName, _showVersion;
    internal void Initialize(UIPage parent, bool settingsEntry = false)
    {
        _parent = parent; _settingsEntry = settingsEntry;
        
        _button = OverviewUi.Button(transform, Loc.T("Mod overview", "模组概览", "模組概覽"), Open, 180, 43, role: UiActionRole.Overview);
        var caption = _button.GetComponentInChildren<TextMeshProUGUI>(true);
        _settingsCaption = caption;
        MatchNativeRibbon(_button, caption);
        // 1.1.4 (user): the entry sits top-left, away from the menu column, so the controller could never reach it.
        _button.gameObject.AddComponent<EntryNavLink>();
        GamepadSupport.MarkSelectable(_button.gameObject, null, null);
        caption.enableAutoSizing = true; caption.fontSizeMin = 13; caption.fontSizeMax = 18;
        _name = OverviewUi.Text(transform, "", 29); _name.gameObject.AddComponent<OverviewFlowingName>().Text = _name;
        _version = OverviewUi.Text(transform, "", 18); _version.color = OverviewUi.Muted;
        Refresh(reload: false);
    }
    /// <summary>青绿缎带：与 ModConfig 的「模组设置」蓝、「返回」红同一档饱和度与明度，两套主题都不变（它挨着原生按钮）。</summary>
    internal static readonly Color EntryRibbon = new Color32(0x2a, 0x80, 0x6e, 255);
    /// <summary>
    /// 2.4.7（用户：「模组概览」字体与上面的「返回」「模组设置」不一样、颜色不搭）：入口挨着游戏原生缎带，
    /// 不用 MCE 自己的正文字体与功能色，改回按钮模板原样的字体、材质、字色与阴影，底色用固定青绿。
    /// </summary>
    private static void MatchNativeRibbon(Button button, TextMeshProUGUI caption)
    {
        var style = button.GetComponent<UiActionStyle>(); if (style != null) UnityEngine.Object.DestroyImmediate(style);
        var peak = button.GetComponent<PEAKLib.UI.Elements.PeakMenuButton>(); if (peak != null) peak.SetColor(EntryRibbon);
        var weight = caption.GetComponent<MceTextWeightBinding>(); if (weight != null) UnityEngine.Object.DestroyImmediate(weight);
        var template = PEAKLib.UI.Elements.Templates.ButtonTemplate;
        var source = template != null ? template.transform.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
        if (source == null) return;
        caption.font = source.font; caption.fontSharedMaterial = source.fontSharedMaterial;
        caption.fontStyle = source.fontStyle; caption.fontWeight = source.fontWeight; caption.characterSpacing = source.characterSpacing;
        caption.color = source.color;
        var shadows = caption.GetComponents<Shadow>(); var sourceShadows = source.GetComponents<Shadow>();
        for (int i = 0; i < shadows.Length && i < sourceShadows.Length; i++) shadows[i].enabled = sourceShadows[i].enabled;
    }
    private bool _openHotkeys;
    internal bool BelongsTo(UIPageHandler? handler) => _parent != null && handler != null && _parent.GetComponentInParent<UIPageHandler>(true) == handler;
    /// <summary>
    /// 2.5.1：从模组设置页跳过来时，入口所在的「设置」页是隐藏的，入口自己的 Update 不跑，分帧打开停在第一步（点了没反应）。
    /// 这时由当前可见页面上的 <see cref="OpenDriver"/> 代为推进，打开完成后自毁。
    /// </summary>
    internal void OpenOnHotkeys(UIPage from)
    {
        _openHotkeys = true; Open();
        if (_opening && (_parent == null || !_parent.gameObject.activeInHierarchy))
        {
            _driven = true;
            (from.gameObject.GetComponent<OpenDriver>() ?? from.gameObject.AddComponent<OpenDriver>()).Entry = this;
        }
    }
    private bool _driven;
    internal bool Opening => _opening;
    internal void PumpOpen() { if (_opening) StepOpen(); }
    internal sealed class OpenDriver : MonoBehaviour
    {
        internal OverviewEntry? Entry;
        private void Update()
        {
            if (Entry == null || !Entry.Opening) { if (Entry != null) Entry._driven = false; Destroy(this); return; }
            Entry.PumpOpen();
        }
        // 2.5.4（二次审查）：挂它的设置页被隐藏 = 用户在打开完成前离开了 → 取消这次打开（已切到概览时 Opening 已为 false，不受影响）
        private void OnDisable()
        {
            if (Entry != null && Entry.Opening) Entry.CancelOpen();
            Entry = null;
            Destroy(this);
        }
    }
    private void Open()
    {
        if (_parent == null) return;
        if(_opening)return;
        EntryNavLink.ReturnFocus = true;
        try { EnsureShell(); } catch(Exception error) { EntryNavLink.ReturnFocus = false; FailOpen(error);return; }
        _opening=true;_stage=0;
        StepOpen();
    }
    // A stage boundary is the only place the frame can be given back, so the shell, the repository
    // binding, the first-entry preparation and the page transition stay separate stages of the pump.
    private void StepOpen()
    {
        if(_parent==null || _page==null) { CancelOpen();return; }
        if(_stage==0)
        {
            if(_shell!=null) { PumpShell();if(_shell!=null)return; _stage=1; return; }
            _stage=1;
        }
        if(_stage==1)
        {
            if(!_page.IsBound)
            {
                if(_shell==null) _shell=_page.BindSteps(NativeConfigurationDraft.Repository ?? new OverviewRepository());
                PumpShell();if(_shell!=null)return;
                _stage=2;return;
            }
            _stage=2;
        }
        if(_stage==2)
        {
            if(_shell==null) _shell=_page.EnterSteps();
            PumpShell();if(_shell!=null)return;
            _stage=3;
        }
        FinishOpen();
    }
    private void FinishOpen()
    {
        if(_parent==null || _page==null) { CancelOpen();return; }
        // 2.5.5（用户实机：跳快捷键只到概览）：切页会隐藏当前的设置页，触发 OpenDriver.OnDisable；
        // 若此时仍算「打开中」，它会取消打开并清掉跳快捷键的意图。所以先把状态收尾、记下意图，再切页。
        bool hotkeys = _openHotkeys;
        _openHotkeys = false; _opening = false; _driven = false;
        try
        {
            var handler = _parent.GetComponentInParent<UIPageHandler>(true);
            if (handler != null) handler.TransistionToPage(_page, new SetActivePageTransistion());
            if (hotkeys) _page.ShowHotkeysTab();
        }
        catch(Exception error)
        {
            FailOpen(error);
        }
    }
    internal void Refresh() => Refresh(true);
    internal void Refresh(bool reload)
    {
        if (_name == null || _version == null || _button == null) return;
        var rect = (RectTransform)transform; _left = OverviewOptions.EntryLeft.Value; _top = OverviewOptions.EntryTop.Value;
        _showName = OverviewOptions.ShowPackName.Value; _showVersion = OverviewOptions.ShowPackVersion.Value;
        if (_settingsEntry)
        {
            _name.gameObject.SetActive(false); _version.gameObject.SetActive(false);
            PositionSettingsEntry(); return;
        }
        var pack = OverviewPageCache.Header(reload);
        OverviewUi.Place(rect, _left, _top, 340, 120);
        float y = 0;
        _name.text = pack.name; _name.gameObject.SetActive(OverviewOptions.Enabled.Value && OverviewOptions.ShowPackName.Value && pack.name.Length > 0);
        if (_name.gameObject.activeSelf) { float nameHeight = Mathf.Max(38, Mathf.Ceil(_name.GetPreferredValues(_name.text).y) + 2); OverviewUi.Place(_name.rectTransform, 0, y, 335, nameHeight); y += nameHeight; }
        _version.text = pack.version; _version.gameObject.SetActive(OverviewOptions.Enabled.Value && OverviewOptions.ShowPackVersion.Value && pack.version.Length > 0);
        if (_version.gameObject.activeSelf) { OverviewUi.Place(_version.rectTransform, 1, y, 330, 25); y += 28; }
        OverviewUi.Place((RectTransform)_button.transform, 0, y + 3, 180, 43); _button.gameObject.SetActive(OverviewOptions.Enabled.Value);
    }
    private void OnEnable() { if (_button != null) Refresh(false); }
    private static float _metadataWarmAt=-1;
    private static bool _metadataWarmed;
    private void Update()
    {
        if(_opening)
        {
            // 入口所在页被隐藏：用户离开了就取消；由设置页跳转代推进（OpenDriver）的不取消
            if(_parent==null || !_parent.gameObject.activeInHierarchy && !_driven) { CancelOpen();return; }
            if(_parent.gameObject.activeInHierarchy) StepOpen();
            return;
        }
        if(OverviewRepository.IsMainMenuScene && OverviewOptions.Enabled.Value && !OverviewUi.HasModal)
        {
            if(_shellAt<0)_shellAt=Time.unscaledTime+3;
            if(Time.unscaledTime>=_shellAt && PEAKLib.UI.Elements.Templates.ButtonTemplate!=null)
            { try { EnsureShell();PumpShell(); } catch(Exception error) { FailOpen(error); } }
        }
        if(!_metadataWarmed && OverviewRepository.IsMainMenuScene)
        {
            if(_metadataWarmAt<0)_metadataWarmAt=Time.unscaledTime+3;
            if(Time.unscaledTime>=_metadataWarmAt && PEAKLib.UI.Elements.Templates.ButtonTemplate!=null)
            { _metadataWarmed=true;OverviewCatalogue.Prefetch(); }
        }
        if (_settingsEntry) { PositionSettingsEntry(); return; }
        if (_left != OverviewOptions.EntryLeft.Value || _top != OverviewOptions.EntryTop.Value || _showName != OverviewOptions.ShowPackName.Value || _showVersion != OverviewOptions.ShowPackVersion.Value || _button == null || _button.gameObject.activeSelf != OverviewOptions.Enabled.Value) Refresh(false);
    }
    private void PositionSettingsEntry()
    {
        if (_parent == null || _button == null) return;
        // Only discover a missing/replaced direct child. Hovering never scans the page tree.
        if (_settingsReference == null || _settingsReference.parent != _parent.transform)
        {
            _settingsReference = null; _settingsReferenceText = null; _settingsLayout = null;
            if (Time.unscaledTime >= _settingsReferenceRetryAt)
            {
                _settingsReferenceRetryAt = Time.unscaledTime + .25f;
                _settingsReference = _parent.transform.Find("UI_MainMenuButton_MOD SETTINGS") as RectTransform;
                if (_settingsReference != null)
                    _settingsReferenceText = _settingsReference.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }
        var reference = _settingsReference;
        bool visible = OverviewOptions.Enabled.Value && reference != null;
        if (_button.gameObject.activeSelf != visible) _button.gameObject.SetActive(visible);
        if (reference == null || _settingsCaption == null) return;
        // localScale/rotation and TMP's animated/current auto-size are deliberately excluded.
        // 2.4.7：字号与上面的「模组设置」一致（原先 0.85 倍、上限 22，看着比原生小一号）
        float fontMax = _settingsReferenceText == null ? 22 : Mathf.Min(30,
            _settingsReferenceText.enableAutoSizing ? _settingsReferenceText.fontSizeMax : _settingsReferenceText.fontSize);
        var layout = new SettingsEntryLayout(reference, fontMax, Loc.TranslationRevision);
        if (_settingsLayout.HasValue && _settingsLayout.Value.Equals(layout)) return;
        _settingsLayout = layout; 
        var rect = (RectTransform)transform;
        rect.anchorMin = layout.AnchorMin; rect.anchorMax = layout.AnchorMax; rect.pivot = layout.Pivot;
        rect.sizeDelta = layout.Size;
        rect.anchoredPosition = layout.Position - new Vector2(0, layout.Height + 10);
        OverviewUi.Fill((RectTransform)_button.transform);
        _settingsCaption.fontSizeMax = fontMax;
        _settingsCaption.fontSizeMin = Mathf.Min(13, fontMax);
    }
    private void OnDestroy() { (_shell as IDisposable)?.Dispose();_shell=null; if (_page != null) { _page.DisposeResources();UnityEngine.Object.Destroy(_page.gameObject); } }
}

internal sealed partial class OverviewPage : UIPage, IHaveParentPage
{
    private UIPage _parent = null!;
    private OverviewRepository _repo = null!;
    private OverviewListView? _list;
    private Editor.IntroductionEditorView? _document;
    private OverviewHotkeyView? _hotkeys;
    private enum PageTab { Mods, Introduction, Hotkeys }
    private PageTab _tab;
    private RectTransform _frame = null!, _body = null!, _listTab = null!, _introTab = null!, _hotkeyTab = null!, _controls = null!;
    private TextMeshProUGUI _packName = null!, _metadata = null!, _status = null!;
    private Button _edit = null!, _hide = null!, _language = null!, _theme = null!;
    /// <summary>2.4.1：工具条主题钮。切换后概览要按新主题重建：先回到上一层再立即重新打开（见 OverviewEntry 的 Epoch 判断）。</summary>
    private void ToggleTheme()
    {
        RequestProfileLeave(() =>
        {
            ModConfigSidebarWidgets.ToggleTheme();
            SetStatus(ModConfigLook.IsDark ? Loc.T("Purple theme. Reopen the overview to apply it here.", "已切到紫色主题，重新打开概览后此页生效。", "已切到紫色主題，重新開啟概覽後此頁生效。")
                : Loc.T("Blue theme. Reopen the overview to apply it here.", "已切到蓝色主题，重新打开概览后此页生效。", "已切到藍色主題，重新開啟概覽後此頁生效。"));
        });
    }
    private Button _refresh = null!, _groups = null!, _tagFilter = null!;
    private int _languageRevision = -1;
    private bool _author, _intro;
    private bool _entryPrepared;
    private bool _returnFromSettings;
    private float _settingsScrollPosition = 1;
    private Button _back = null!;
    private Button _help = null!, _logs = null!, _shop = null!;
    private Help.HelpPage? _helpPage;
    private float _width;
    internal bool ShellReady { get; private set; }
    private bool _bound, _bindingStarted;
    internal bool IsBound => _bound;
    internal IEnumerator BuildShell(UIPage parent)
    {
        _parent=parent;
        var background = OverviewUi.Panel("Backdrop", transform, ModConfigLook.Surface(new Color(.05f, .07f, .07f, .84f))); OverviewUi.Fill(background);
        var border = OverviewUi.Panel("Frame", transform, OverviewUi.Gold); OverviewUi.Fill(border, 14);
        _frame = OverviewUi.Panel("Paper", border, OverviewUi.Brown); OverviewUi.Fill(_frame, 2);
        _back = OverviewUi.Button(_frame, Loc.T("Back", "返回", "返回"), Back, 85, 35, role: UiActionRole.Neutral); OverviewUi.Place((RectTransform)_back.transform, 14, 15, 85, 35);
        _listTab = Tab(Loc.T("Mod list", "模组列表", "模組列表"), () => { SwitchTab(PageTab.Mods); FocusTabContent(); });
        _introTab = Tab("", () => SwitchTab(PageTab.Introduction));
        _hotkeyTab = Tab(Loc.T("Keybindings", "快捷键设置", "快捷鍵設定"), () => { SwitchTab(PageTab.Hotkeys); FocusTabContent(); });
        _controls = OverviewUi.Rect("CommonTools", _frame);
        yield return null;
        _refresh = OverviewUi.Button(_controls, Loc.T("Refresh", "刷新", "重新整理"), RefreshData, 90, 34, role: UiActionRole.Neutral);
        yield return null;
        _edit = OverviewUi.IconButton(_controls, "pencil", ToggleAuthor); _hide = OverviewUi.Button(_controls, Loc.T("Hide disabled", "隐藏已停用", "隱藏已停用"), () => { _repo.State.hideDisabled = !_repo.State.hideDisabled; _list?.SetHideDisabled(_repo.State.hideDisabled); RefreshHeader(); });
        yield return null;
        _language = OverviewUi.Button(_controls, Loc.T("Language", "语言", "語言"), PickLanguage, 95, 34, role: UiActionRole.Edit);
        yield return null;
        _groups = OverviewUi.Button(_controls, Loc.T("Management center", "管理中心", "管理中心"), () => OpenManagement(), 115, 34, role: UiActionRole.Edit);
        yield return null;
        _tagFilter = OverviewUi.Button(_controls, "", ShowTagFilter, 115, 34, role: UiActionRole.Neutral);
        yield return null;
        _sourceTools = OverviewUi.Button(_controls, Loc.T("Sources and updates", "来源与更新", "來源與更新"), SourceTools, 140, 34, role: UiActionRole.Edit);
        yield return null;
        BuildSourceToolsIcon();
        BuildImportTool();
        yield return null;
        _shop = OverviewUi.Button(_controls, Loc.T("Mod store","模组商店","模組商店"), () => ShopWindow.Open(_repo,_list?.Mods??Array.Empty<OverviewMod>()),115,34,UiActionRole.Overview);
        yield return null;
        _logs = OverviewUi.Button(_controls, Loc.T("Logs", "日志", "日誌"), () => LogWindow.Open(_repo, _list?.Mods ?? Array.Empty<OverviewMod>()), 76, 34, role: UiActionRole.Edit);
        yield return null;
        _help = OverviewUi.Button(_controls, Loc.T("User guide", "使用说明", "使用說明"), () => RequestProfileLeave(() => { MarkReturnFromSettings(); _helpPage ??= Help.HelpPage.Create(this); _helpPage.Open(); }), 105, 34, role: UiActionRole.Guide);
        yield return null;
        _theme = OverviewUi.IconButton(_controls, "theme", ToggleTheme); OverviewUi.UseSpriteIcon(_theme, ModConfigSidebarWidgets.ThemeIconSprite());
        yield return null;
        _more = OverviewUi.Button(_controls, Loc.T("More", "更多", "更多"), ShowToolbarMore, 76, 34, role: UiActionRole.Neutral);
        yield return null;
        var helpCaption = _help.GetComponentInChildren<TextMeshProUGUI>(true);
        helpCaption.enableAutoSizing = true; helpCaption.fontSizeMin = 11; helpCaption.fontSizeMax = 13;
        ShellReady=true;
    }
    internal IEnumerator BindSteps(OverviewRepository repo)
    {
        if(_bound)yield break;
        if(!ShellReady)throw new InvalidOperationException("Overview shell is not ready.");
        _repo=repo;_bindingStarted=true;
        NativeConfigurationDraft.Repository = repo;
        ShopWindow.ResumeGrouping(_repo);
        BuildProfileTools();
        ChangeLedger.Dot(_language.transform, "summary:language");
        ChangeLedger.Dot(_profileChoice.transform, "summary:profiles");
        var editTip = _edit.gameObject.AddComponent<OverviewHover>(); editTip.Enter = () => _list?.ShowExternalHint((_author ? Loc.T("On · ", "已开启 · ", "已開啟 · ") : Loc.T("Off · ", "已关闭 · ", "已關閉 · ")) + Loc.T("Edit mode: organize groups and change pack details and update permissions.", "编辑模式：调整分组、整合包信息和更新检查许可。", "編輯模式：調整分組、整合包資訊和更新檢查許可。")); editTip.Exit = () => _list?.HideExternalHint();
        var hideTip = _hide.gameObject.AddComponent<OverviewHover>(); hideTip.Enter = () => _list?.ShowExternalHint((_repo.State.hideDisabled ? Loc.T("On · ", "已开启 · ", "已開啟 · ") : Loc.T("Off · ", "已关闭 · ", "已關閉 · ")) + Loc.T("Hide fully disabled mods; loaded or planned-enabled mods remain visible.", "隐藏已停用的模组；本次已加载或计划启用的模组继续显示。", "隱藏已停用的模組；本次已載入或計畫啟用的模組繼續顯示。")); hideTip.Exit = () => _list?.HideExternalHint();
        yield return null;
        _packName = OverviewUi.Text(_frame, "", 29); _packName.alignment = TextAlignmentOptions.Center; _packName.gameObject.AddComponent<OverviewFlowingName>().Text = _packName;
        _packName.raycastTarget = true; _packName.gameObject.AddComponent<OverviewHeaderClick>().Clicked = data => { if (_author || data.clickCount >= 2) EditPack(false); };
        _metadata = OverviewUi.Text(_frame, "", 16); _metadata.color = OverviewUi.Muted; _metadata.alignment = TextAlignmentOptions.TopRight; _metadata.textWrappingMode = TextWrappingModes.NoWrap;
        _metadata.raycastTarget = true; _metadata.gameObject.AddComponent<OverviewHeaderClick>().Clicked = data => { if (_author) EditPack(true); else if (data.clickCount >= 2) EditPackVersion(); else if (!string.IsNullOrWhiteSpace(_repo.State.pack.contact)) { GUIUtility.systemCopyBuffer = _repo.State.pack.contact; SetStatus(Loc.T("Contact copied.", "联系方式已复制。", "聯絡方式已複製。")); } };
        yield return null;
        _body = OverviewUi.Rect("Content", _frame); OverviewUi.Fill(_body); _body.offsetMin = new Vector2(20, 64); _body.offsetMax = new Vector2(-20, -72);
        _status = OverviewUi.Text(_frame, "", 17); _status.color = OverviewUi.Muted;
        _status.textWrappingMode = TextWrappingModes.Normal; _status.overflowMode = TextOverflowModes.Ellipsis;
        _status.enableAutoSizing = true; _status.fontSizeMin = 12; _status.fontSizeMax = 17;
        _status.richText = false; _status.raycastTarget = true;
        AttachToolHint(_status.gameObject, () => _fullStatus);
        yield return null;
        BuildUpdateTools();
        
        yield return null;
        _list = new OverviewListView(_body, _repo, SetStatus, OnListChanged, OpenModSettings); 
        // The document editor is expensive and unrelated to the default mod-list tab.
        yield return null;
        _hotkeys = new OverviewHotkeyView(_body, _repo, () => _list.Mods, SetStatus, RefreshHeader, OpenModSettings, () => { EnsureDocument().ExportPackData(); }, (target, mod) => _list.BindDependencyHint(target, mod), mod => _list.GetDisplayNumber(mod));
        _hotkeys.ToolbarAbove = from => _controls.GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && s.IsInteractable()).OrderBy(s => Mathf.Abs(s.transform.position.x - from.transform.position.x)).FirstOrDefault() ?? _hotkeyTab.GetComponent<Button>();
        _list.PendingState = () => _repo.GetPendingState(_document?.IsDirty == true);
        _hotkeys.PendingState = () => _repo.GetPendingState(_document?.IsDirty == true);
        _list.SettingsOwners = () => OverviewSettingsNavigator.SettingsOwners(this);
        _list.GroupManagementRequested = group => { _managementSelectedGroup = group; _managementNewGroupParent = null; OpenManagement(ManagementTab.Groups); };
        _list.NewGroupRequested = parent => { _managementSelectedGroup = null; _managementNewGroupParent = parent; OpenManagement(ManagementTab.Groups); };
        _list.ModInformationRequested = mod => { _managementSelectedMod = mod; OpenManagement(ManagementTab.Information); };
        _hotkeys.ManageGroups = () => _list.ManageGroups(); _hotkeys.EditGroup = group => _list.EditGroup(group);
        _hotkeys.Root.SetActive(false);
        yield return null;
        AttachToolHint(_refresh.gameObject, () => Loc.T("Refresh installed mods and configuration values.", "重新读取已安装模组和配置值。", "重新讀取已安裝模組和設定值。"));
        AttachToolHint(_sourceTools.gameObject, UpdateCheckHint);
        AttachToolHint(_groups.gameObject, () => Loc.T("Management center", "管理中心", "管理中心"));
        AttachToolHint(_tagFilter.gameObject, () => Loc.T("Include or exclude multiple store tags. Mod list and keybindings remember separate filters.", "勾选包含或排除的商店标签，模组列表和快捷键页分别记住筛选条件。", "勾選包含或排除的商店標籤，模組清單和快捷鍵頁分別記住篩選條件。"));
        RefreshHeader();
        yield return null;
        gameObject.AddComponent<TypingGuard.Driver>();_bound=true;
    }
    private Editor.IntroductionEditorView EnsureDocument()
    {
        if(_document!=null)return _document;
        _document=new Editor.IntroductionEditorView(_body,_repo,OverviewUi.Text);
        _document.StatusChanged+=SetStatus;_document.DraftChanged+=RefreshHeader;
        _document.SetAuthorMode(_author);_document.Root.SetActive(_intro);
        return _document;
    }
    private RectTransform Tab(string label, Action click)
    {
        var rect = OverviewUi.Rect("Tab", _frame); var shape = rect.gameObject.AddComponent<OverviewTabGraphic>(); shape.color = ModConfigLook.Surface(new Color(.29f, .25f, .17f));
        shape.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = shape; button.onClick.AddListener(() => click());
        var text = OverviewUi.Text(rect, label, 20); OverviewUi.Fill(text.rectTransform); text.margin = new Vector4(6, 0, 6, 0); text.alignment = TextAlignmentOptions.Midline;
        text.enableAutoSizing = true; text.fontSizeMax = 20; text.fontSizeMin = 12; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false; return rect;
    }
    // First entry's expensive half, run while the page is still inactive: styling the page tree, the
    // repository-backed page preparation and the config observers. OnPageEnter skips what already ran.
    internal IEnumerator EnterSteps()
    {
        ModConfigPaperStyle.Resolve(_parent.transform);
        yield return null;
        OverviewPaperPanel.Refresh(transform);
        yield return null;
        PrepareCachedPage();
        yield return null;
        ObserveProfileParameters();
        _entryPrepared = true;
    }
    public override void OnPageEnter()
    {
        base.OnPageEnter(); OverviewUi.ModalParent = transform;
        bool returning = _returnFromSettings; _returnFromSettings = false;
        if(_entryPrepared) _entryPrepared=false;
        else
        {
            ModConfigPaperStyle.Resolve(_parent.transform); OverviewPaperPanel.Refresh(transform);
            PrepareCachedPage();
            ObserveProfileParameters();
        }
        RefreshHeader();
        if (returning) _list?.RestoreScrollPosition(_settingsScrollPosition);
        if (_tab == PageTab.Mods) _list?.OnOpened(!returning);
        
        if (_repo.ReadWarning.Length > 0) SetStatus(_repo.ReadWarning);
        if (_tab == PageTab.Hotkeys) _hotkeys?.OnOpened();
        if (_intro) _document?.OnOpened();
        if (_intro && _author && _document != null) _document.ActivateEditor();
        MaybeOfferSmartGrouping();
        MaybeWarnXUnity(); // 2.5.0：智能分组弹窗在前，它开着就留到下次进入
        MaybeWarnLegacyMce(); // 1.0.0 拆分：简化版 MCE 同时启用时提醒一次
    }
    public override void OnPageExit()
    {
        GamepadIntent.Release(this); ClearToolHint(); CloseManagement(); CloseUpdateQueue(); StopProfileObservation(); _document?.OnClosed(); _hotkeys?.OnClosed(); _list?.OnClosed(); OverviewUi.ModalParent = null; OverviewUi.CloseModal(); _repo.SetAuthorMode(false); TypingGuard.Active = false; base.OnPageExit();
    }
    public (UIPage, PageTransistion) GetParentPage()
    {
        // 1.1.4: recording a key is a lock; only a 1 s B hold leaves it (handled by the keybindings page).
        if (_hotkeys?.IsRecording == true) return (this, GamepadSupport.NoopTransistion.Shared);
        if (GamepadSupport.ConsumeTypingBack()) return (this, GamepadSupport.NoopTransistion.Shared); // B only left a text box
        if (TryCloseEditingLayer()) return (this, GamepadSupport.NoopTransistion.Shared);
        if (_document?.IsImporting == true) { SetStatus(Loc.T("Media is being imported. Wait before leaving this page.", "素材正在导入，请稍后再离开。", "素材正在導入，請稍后再離開。")); return (this, GamepadSupport.NoopTransistion.Shared); }
        if (!BackGuard.Allow()) return (this, GamepadSupport.NoopTransistion.Shared); // 1.1.5: B twice quickly to leave
        if (ProfileHasUnsaved) { RequestProfileLeave(NavigateBack); return (this, GamepadSupport.NoopTransistion.Shared); }
        return (_parent, new SetActivePageTransistion());
    }
    internal void MarkReturnFromSettings() { _returnFromSettings = true; _settingsScrollPosition = _list?.ScrollPosition ?? 1; }
    internal void ShowHotkeysTab() => SwitchTab(PageTab.Hotkeys);
    private void OpenModSettings(OverviewMod? mod) => RequestProfileLeave(() => { if (!OverviewSettingsNavigator.Open(this, mod, out string error)) { _returnFromSettings = false; SetStatus(error); } });
    private void Back()
    {
        if (_hotkeys?.IsRecording == true) return; // recording lock
        if (GamepadSupport.ConsumeTypingBack()) return; // B only left a text box
        if (TryCloseEditingLayer()) return;
        if (!BackGuard.Allow()) return;
        RequestProfileLeave(NavigateBack);
    }
    private bool TryCloseEditingLayer()
    {
        if (OverviewHotkeyCapture.ConsumedCancelFrame == Time.frameCount) return true;
        if (OverviewUi.TryCloseTopModal()) return true;
        if (CloseUpdateQueue()) return true;
        if (_managementGroups?.TryCloseSecondaryLayer() == true) return true;
        if (_managementPublisher?.TryClosePreviewLayer() == true) return true;
        if (_management?.TryClose() == true) return true;
        if (_hotkeys?.TryCloseLayer() == true) return true;
        if (_list?.TryCloseLayer() == true) return true;
        return _document?.TryCloseLayer() == true;
    }
    private void NavigateBack()
    {
        var handler = _parent.GetComponentInParent<UIPageHandler>(); if (handler != null) handler.TransistionToPage(_parent, new SetActivePageTransistion());
    }
    private void SwitchTab(PageTab tab)
    {
        if (tab == _tab) return;
        if (tab == PageTab.Introduction && !CanShowIntroduction && !_author) return;
        if (_intro && tab != PageTab.Introduction && ProfileHasUnsaved) { RequestProfileLeave(() => SwitchTab(tab)); return; }
        if (_tab == PageTab.Mods) _list?.OnTabHidden();
        if (_tab == PageTab.Hotkeys) _hotkeys?.OnClosed();
        if (_intro) _document?.OnClosed();
        _list?.HideAllHints();
        _tab = tab; _intro = tab == PageTab.Introduction;
        EnsureSelectedPage();
        if (_list != null) _list.Root.SetActive(tab == PageTab.Mods);
        if (_document != null) _document.Root.SetActive(_intro);
        if (_hotkeys != null) { _hotkeys.Root.SetActive(tab == PageTab.Hotkeys); if (tab == PageTab.Hotkeys) _hotkeys.OnOpened(); }
        RefreshHeader();
        ModConfigPaperStyle.Resolve(_parent.transform); OverviewPaperPanel.Refresh(transform);
        if (tab == PageTab.Mods) _list?.OnOpened();
        if (_intro) _document?.OnOpened();
        if (_intro && _author && _document != null) _document.ActivateEditor();
    }
    private bool CanShowIntroduction => _repo.ViewedProfile.kind != OverviewProfileKind.Default && _repo.State.pack.introEnabled && (HasContent(_repo.IntroductionDocumentPath) || HasContent(_repo.IntroductionPath));
    private static bool HasContent(string path) => path.Length != 0 && File.Exists(path) && new FileInfo(path).Length > 0;
    private void ToggleAuthor()
    {
        bool requested = !_author;
        if (!requested && ProfileHasUnsaved) { RequestProfileLeave(() => ToggleAuthor()); return; }
        if (!requested && _document?.IsImporting == true) { SetStatus(Loc.T("Media import is still running. Wait before leaving author mode.", "素材仍在导入，请完成后再退出作者模式。", "素材仍在匯入，請完成後再退出作者模式。")); return; }
        _author = requested; _repo.SetAuthorMode(_author); _list?.SetAuthorMode(_author); _document?.SetAuthorMode(_author); _hotkeys?.SetAuthorMode(_author);
        SetStatus(_author ? Loc.T("Author mode: changes save to your local copy. Double-click a name or description to edit; Shift selects a range.", "作者模式：更改保存到本地副本。双击名称或介绍可编辑，Shift 可批量选择。", "作者模式：更改儲存至本地副本。雙擊名稱或介紹可編輯，Shift 可批量選取。") : "");
        if (!_author && _intro && !CanShowIntroduction) SwitchTab(PageTab.Mods); else RefreshHeader();
        if (_author && _intro && _document != null) _document.ActivateEditor();
    }
    private static OverviewPack CopyPack(OverviewPack pack)
        => JsonConvert.DeserializeObject<OverviewPack>(JsonConvert.SerializeObject(pack))!;
    private static bool TryPackDate(string value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return true;
        string[] formats = { "yyyy-M-d", "yyyy-MM-dd", "yyyy-M-dd", "yyyy-MM-d", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "o" };
        if (!DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime date)) return false;
        normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return true;
    }
    private void SavePackCandidate(OverviewPack candidate)
    {
        OverviewPack previous = _repo.State.pack;
        bool previousOverridden = _repo.State.packOverridden;
        try { _repo.SavePack(candidate); }
        catch
        {
            _repo.State.pack = previous; _repo.State.packOverridden = previousOverridden;
            throw;
        }
    }
    private void EditPack(bool all) => OpenManagement(ManagementTab.Information);
    private void EditPackVersion() => OpenManagement(ManagementTab.Information);
    private void RefreshHeader()
    {
        if (_packName == null) return;
        var pack = _repo.State.pack; _width = _frame.rect.width; _languageRevision = OverviewLanguageBridge.Revision;
        float width = Mathf.Max(_width, 380);
        bool showIntro = _repo.ViewedProfile.kind != OverviewProfileKind.Default && (_author || CanShowIntroduction); _introTab.gameObject.SetActive(showIntro);
        _listTab.GetComponentInChildren<TextMeshProUGUI>(true).text = Loc.T("Mod list", "模组列表", "模組列表");
        _introTab.GetComponentInChildren<TextMeshProUGUI>(true).text = pack.introTitle.Length > 0 ? pack.introTitle : Loc.T("Modpack info", "整合包介绍", "整合包介紹");
        _hotkeyTab.GetComponentInChildren<TextMeshProUGUI>(true).text = Loc.T("Keybindings", "快捷键设置", "快捷鍵設定");
        float tabX = 112;
        float tabScale = Mathf.Min(1, Mathf.Max(.25f, (width - 112 - Mathf.Clamp(width * .17f, 90, 220) - 38) / (showIntro ? 395 : 260)));
        void PlaceTab(RectTransform tab, PageTab kind, float tabWidth)
        {
            tabWidth *= tabScale;
            bool active = _tab == kind; OverviewUi.Place(tab, tabX, active ? 12 : 16, tabWidth, active ? 38 : 32); tabX += tabWidth - 1;
            tab.GetComponent<OverviewTabGraphic>().color = active ? ModConfigLook.Surface(new Color(.20f, .48f, .60f)) : UiActionStyle.ColorFor(UiActionRole.Neutral, ModConfigLook.IsDark);
            var caption = tab.GetComponentInChildren<TextMeshProUGUI>(true); caption.overflowMode = TextOverflowModes.Overflow;
        }
        PlaceTab(_listTab, PageTab.Mods, 120); if (showIntro) PlaceTab(_introTab, PageTab.Introduction, 135); PlaceTab(_hotkeyTab, PageTab.Hotkeys, 140);
        float packRight = LayoutUpdateTools(width, tabX);
        OverviewUi.Place(_controls, 20, 68, width - 40, 36);
        LayoutToolbar(width);
        _packName.text = pack.name.Length > 0 ? pack.name : _author ? Loc.T("Pack name", "整合包名称", "整合包名稱") : "";
        float nameLeft = tabX + 20, informationRoom = Mathf.Max(0, packRight - nameLeft);
        float metadataWidth = informationRoom > 320 ? Mathf.Min(220, informationRoom * .42f) : informationRoom > 200 ? Mathf.Min(120, informationRoom * .4f) : 0;
        float metadataX = packRight - metadataWidth, nameRight = metadataX - 16;
        float nameWidth = Mathf.Min(400, width * .25f, Mathf.Max(0, nameRight - nameLeft));
        float nameX = Mathf.Clamp(width * .5f - nameWidth * .5f, nameLeft, Mathf.Max(nameLeft, nameRight - nameWidth));
        // Ellipsis also truncates vertically: if the first line is taller than the box, TMP renders nothing.
        // CJK glyphs come from the taller fallback face, so size the box to the real line height.
        float nameHeight = Mathf.Max(43, Mathf.Ceil(_packName.GetPreferredValues(_packName.text.Length > 0 ? _packName.text : "国").y) + 4);
        _packName.alignment = TextAlignmentOptions.Center;
        OverviewUi.Place(_packName.rectTransform, nameX, 11 + (43 - nameHeight) / 2, nameWidth, nameHeight); _packName.overflowMode = TextOverflowModes.Ellipsis;
        _packName.gameObject.SetActive(nameWidth > 1); _metadata.gameObject.SetActive(metadataWidth > 1);
        OverviewEditableFrame.Set(_packName.rectTransform, _author && nameWidth > 1); OverviewEditableFrame.Set(_metadata.rectTransform, _author && metadataWidth > 1);
        UpdateProfileStatus(width - 40, 39);
        _controls.sizeDelta = new Vector2(width - 40, 67);
        _body.offsetMax = new Vector2(-20, -140);
        bool packUpdate = _list != null && _repo.IsPackUpdateAllowed(pack) && ThunderstoreUpdateService.IsNewer(_list.PackLatestVersion, pack.version);
        string version = pack.version + (packUpdate ? " → " + _list!.PackLatestVersion : "");
        string info = string.Join(" | ", new[] { version, pack.author }).Trim(' ', '|');
        string lower = string.Join(" | ", new[] { pack.contact, pack.date }).Trim(' ', '|');
        _metadata.text = info + (lower.Length > 0 ? "\n" + lower : ""); if (_metadata.text.Length == 0 && _author) _metadata.text = Loc.T("Click to edit pack information", "点击填写整合包信息", "點擊填寫整合包資訊");
        _metadata.overflowMode = TextOverflowModes.Ellipsis;
        OverviewUi.Place(_metadata.rectTransform, metadataX, 10, metadataWidth, 44); OverviewUi.Place(_status.rectTransform, 20, _frame.rect.height - 58, width - 40, _promptShown ? 28 : 52);
    }
    private void AttachToolHint(GameObject target, Func<string> text)
    {
        var hover = target.AddComponent<OverviewHover>();
        hover.Enter = () => { _activeToolHintTarget = target; _activeToolHintText = text; _list?.QueueExternalHint(target,text); };
        hover.Exit = () => { if (_activeToolHintTarget != target) return; ClearToolHint(); _list?.HideExternalHint(); };
        var focus = target.AddComponent<OverviewHotkeyFocus>();
        focus.Selected = () => { _activeToolHintTarget=target;_activeToolHintText=text;_list?.QueueExternalHint(target,text,true); }; focus.Deselected = hover.Exit;
    }
    private void OnListChanged() { RefreshHeader(); _hotkeysStale = true; if (_tab == PageTab.Hotkeys && _hotkeys?.IsRecording != true) EnsureSelectedPage(); }
    private void ShowTagFilter()
    {
        if (_list == null || _tab == PageTab.Introduction) return;
        if (_hotkeys?.IsRecording == true) _hotkeys.TryCloseLayer();
        _list.HideAllHints();
        var selection = _tab == PageTab.Hotkeys ? _repo.State.hotkeyTagFilter ??= new() : _repo.State.modTagFilter ??= new();
        OverviewTagFilter.Show(selection, _list.Mods, () => {
            try { _repo.SaveUiState(); } catch (Exception ex) { SetStatus(Loc.T("Could not save filters: ", "筛选保存失败：", "篩選儲存失敗：") + ex.Message); }
            _list.RefreshTagFilter(); _hotkeys?.RefreshTagFilter(); RefreshHeader();
        }, _list.SyncCategories, _list.MatchesStatus, componentCount: _tab == PageTab.Mods ? _list.ComponentFilterCount : null);
        if (_hotkeys != null) _hotkeys.StatusMatch = _list.MatchesStatus;
    }
    private void RefreshData()
    {
        OverviewCatalogue.InvalidateCaches(); OverviewPageCache.Invalidate();
        PublishedPack.InvalidateCache(); ModSources.InvalidateRecords();
        if (!ProfileHasUnsaved) _repo.Reload();
        _list?.Reload(); _listLoaded = true;
        RefreshLazyViews(_document?.IsDirty != true);
        _inventoryVersion = OverviewPageCache.Inventory; _contentVersion = OverviewPageCache.Content;
        ObserveProfileParameters(); RefreshHeader();
    }
    private string _fullStatus = "";
    private void SetStatus(string text)
    {
        _fullStatus = text;
        if (_status != null) _status.text = OverviewListView.SingleLine(text);
        if (_activeToolHintTarget == _status?.gameObject) RefreshActiveToolHint();
    }
    private void PickLanguage()
    {
        var options = new List<(string Label, string Value)> { (Loc.Get("LANG_FOLLOW"), "") };
        foreach (var file in OverviewLanguageBridge.LanguageOptions) options.Add((file.DisplayOrStem, file.Key));
        OverviewUi.Dropdown(ToolAnchor(_language), Loc.T("Language file", "选择语言文件", "選擇語言檔案"), options, OverviewLanguageBridge.Selection, value => {
            if (!OverviewLanguageBridge.Select(value, out string error)) { SetStatus(error); return; }
            _list?.RefreshTagFilter(); RefreshLazyViews(false); RefreshHeader(); SetStatus(OverviewLanguageBridge.CurrentDisplayName);
        });
    }
    private void Update()
    {
        if (_frame == null) return; if (Math.Abs(_width - _frame.rect.width) > 1 || _languageRevision != OverviewLanguageBridge.Revision) RefreshHeader();
        SourceUi.PollDisplayContext();
        if (_resourceDraftRevision != ResourceOperationDraft.Revision) { _resourceDraftRevision = ResourceOperationDraft.Revision; RefreshHeader(); }
        RefreshAutomaticUpdateHint();
        if (_management?.IsOpen == true && Time.unscaledTime >= _managementDirtyAt)
        { _managementDirtyAt = Time.unscaledTime + .2f; _management.RefreshDirtyIndicators(); }
        PollUpdates();
        PollPacks();
        PollInstallationPreview();
        if (_profileActualChanged) { RefreshActualProfileStatus(); RefreshHeader(); }
        WatchToolbarJump();
        GamepadPageTick();
    }
    private float _managementDirtyAt;
    private bool _resourcesDisposed;
    internal void DisposeResources()
    {
        if(_resourcesDisposed)return;_resourcesDisposed=true;
        AnimatorRestGate.DetachTree(transform);
        if(!_bindingStarted)return;
        void Release(Action action) { try { action(); } catch(Exception error) { Plugin.Log.LogWarning("[概览] 清理："+error.Message); } }
        Release(CloseManagement);Release(DisposeUpdateTools);
        if (_helpPage != null) UnityEngine.Object.Destroy(_helpPage.gameObject);
        OverviewSettingsNavigator.ClearOrigin(this);Release(StopProfileObservation);
        Release(()=>_hotkeys?.Dispose());Release(()=>_list?.Dispose());
        if(_document!=null) { _document.StatusChanged-=SetStatus;_document.DraftChanged-=RefreshHeader;Release(_document.Dispose); }
        if(OverviewUi.ModalParent==transform)OverviewUi.CloseModal();
    }
    private void OnDestroy() => DisposeResources();
}

internal sealed class OverviewHeaderClick : MonoBehaviour, IPointerClickHandler
{
    internal Action<PointerEventData>? Clicked;
    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(eventData);
}


