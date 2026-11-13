#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModConfigEnhance.Diagnostics;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class LogWindow : MonoBehaviour
{
    private OverviewRepository _repo = null!;
    private IReadOnlyList<OverviewMod> _installedMods = Array.Empty<OverviewMod>();
    private RectTransform _panel = null!, _viewport = null!, _content = null!;
    private ScrollRect _scroll = null!;
    private TextMeshProUGUI _status = null!;
    private TMP_InputField _search = null!;
    private string _modFilter = "";
    private bool _showPackageId, _favoritesOnly, _expandAllErrors;
    private readonly HashSet<string> _favoriteSources = new(StringComparer.OrdinalIgnoreCase), _expandedRecords = new(StringComparer.Ordinal);
    private readonly List<TextMeshProUGUI> _pinTimes = new(), _pinMods = new();
    private string FavoritesPath => Path.Combine(Loc.RuntimeDirectory,"log-favorites.json");
    private readonly HashSet<string> _foldedModGroups = new();
    private Button _levelButton = null!, _modButton = null!, _exportFolderButton = null!, _logFolderButton = null!;
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly Dictionary<string, string> _modNames = new(StringComparer.Ordinal);
    private readonly List<string> _filteredRecords = new();
    private TextMeshProUGUI _activity = null!, _nameTip = null!;
    private int _renderFirst = -1, _renderRevision, _drawnRevision = -1;
    private float _renderY;
    private readonly List<TextMeshProUGUI> _times = new();
    private readonly List<TextMeshProUGUI> _mods = new();
    private Scrollbar _vertical = null!, _horizontal = null!;
    private long _lastSequence, _addedTotal;
    private int _batchAdded, _repeat;
    private float _lastReadAt, _rate;
    private bool _exportSucceeded;
    private string _liveEpoch = "", _repeatText = "";
    private const float TimeWidth = 104, ModWidth = 140, GutterWidth = TimeWidth + ModWidth;
    private string _recordStamp = "";
    private readonly List<Image> _searchHighlights = new();
    private readonly List<TextMeshProUGUI> _rows = new();
    private readonly List<Image> _highlights = new();
    private readonly HashSet<string> _collapsedStacks = new();
    private readonly LogRepeatIndex _repeatIndex = new();
    private readonly Dictionary<string, string[]> _wrapped = new(StringComparer.Ordinal);
    private TextMeshProUGUI _measure = null!;
    private RectTransform _pinPanel = null!, _pinViewport = null!, _pinContent = null!;
    private ScrollRect _pinScroll = null!;
    private readonly List<TextMeshProUGUI> _pinRows = new();
    private readonly List<Button> _pinButtons = new();
    private RectTransform? _errorDetails;
    private DateTime _exportStarted;
    private readonly List<(string Text, MceLogLevel Level, string Record, string Time, string Mod, bool Manager)> _pinLines = new();
    private bool _selectionPinned;
    private Button _pauseButton = null!, _followButton = null!, _consoleButton = null!;
    private readonly List<Button> _exportChoices = new();
    private string _exportDestination = "";
    private float _textWidth;
    private readonly List<(string Text, MceLogLevel Level, string Record, string Time, string Mod, bool Manager)> _lines = new();
    private MceLogRecord[] _records = Array.Empty<MceLogRecord>();
    private Task<MceLogRecord[]>? _read;
    private Task? _export;
    private LogFileReader? _reader;
    private MceLogApi.Source? _custom;
    private string _path = "", _tab = "bep", _error = "", _readError = "";
    private string? _exportFallbackNote;
    private bool _pendingRefilter;
    private bool _paused, _follow = true, _selecting, _includeMetadata = true, _loadEarlier;
    private int _level, _scope = 2, _generation, _readGeneration;
    private float _nextRead;
    private (int Line, int Char) _selectionStart, _selectionEnd;
    private bool _hasSelection;
    private RectTransform? _options;
    private const float LineHeight = 23;
    internal static void Open(OverviewRepository repo, IReadOnlyList<OverviewMod> installedMods)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_LogWindow", 1580, 980); if (panel == null) return;
        var view = panel.gameObject.AddComponent<LogWindow>(); view._repo = repo; view._installedMods = installedMods.ToArray(); view._panel = panel; view.Build();
        // 1.1.4 (gamepad review): B closes the error details / option panel first, the window only when none is open.
        OverviewUi.SetModalCloseHandler(() => { if (view == null || !view.CloseTopLayer()) { if (BackGuard.Allow()) OverviewUi.CloseModal(); } });
        GamepadSupport.MarkSelectable(view._search.gameObject, null, null);
    }
    private string T(string en, string zh) => Loc.T(en, zh, zh);
    private void Build()
    {
        float w = _panel.rect.width, h = _panel.rect.height;
        try { if(File.Exists(FavoritesPath)) foreach(var id in JArray.Parse(File.ReadAllText(FavoritesPath)).Values<string>()) if(id!=null)_favoriteSources.Add(id); } catch(Exception e) when(e is IOException or Newtonsoft.Json.JsonException){_error=e.Message;}
        OverviewUi.Place(OverviewUi.Text(_panel, T("Runtime logs", "运行日志"), 24).rectTransform, 22, 12, w - 44, 32);
        Tab("BepInEx", "bep", 22); Tab(T("Game log", "游戏日志"), "game", 160); Tab(T("Custom logs", "自定义日志"), "custom", 298);
        _logFolderButton = Btn(T("Log folder", "打开日志文件夹"), OpenLogFolder, 442, 56, 154, UiActionRole.Overview);
        _exportFolderButton = Btn(T("Export folder", "打开导出文件夹"), () => { if (_exportSucceeded) PackFileShortcuts.Open(_exportDestination, true); }, w - 322, 56, 164, UiActionRole.Overview);
        Btn(T("Export…", "导出……"), ShowExportOptions, w - 148, 56, 126, UiActionRole.Commit);
        _search = OverviewUi.Input(_panel, ""); OverviewUi.Place((RectTransform)_search.transform, 22, 100, Math.Max(150, w - 570), 34);
        var placeholder = OverviewUi.Text(_search.textViewport, Loc.T("Search messages or mods...", "搜索日志正文、模组名……", "搜尋日誌正文、模組名稱……"), 15); OverviewUi.Fill(placeholder.rectTransform); placeholder.color = OverviewUi.Muted; _search.placeholder = placeholder;
        _search.textComponent.fontSize = 17; _search.onValueChanged.AddListener(_ => Refilter());
        _levelButton = Btn("日志等级：全部", PickLevel, w - 536, 100, 160, UiActionRole.Edit);
        _levelButton.gameObject.AddComponent<LogLevelWheel>().Changed=direction=>{_level=LogViewRules.CycleLevel(_level,direction,LevelNames.Length);Refilter();};
        _modButton = Btn("模组：全部", PickMod, w - 366, 100, 180, UiActionRole.Edit);
        _pauseButton = Btn(T("Pause", "暂停刷新"), () => { _paused = !_paused; }, 22, h - 48, 104, UiActionRole.Edit);
        _followButton = Btn(T("Follow", "跟随最新"), () => { _follow = !_follow; if (_follow) ScrollTo(0); }, 144, h - 48, 104, UiActionRole.Commit);
        Btn("跳到顶部", () => { _follow = false; ScrollTo(1); }, 266, h - 48, 104, UiActionRole.Overview);
        Btn("跳到底部", () => { _follow = true; _hasSelection = false; ScrollTo(0); }, 388, h - 48, 104, UiActionRole.Overview);
        if (GamepadSupport.IsGamepad) _follow = false; // 1.1.5 (user: "why does it jump to the bottom"): follow only when asked
        _consoleButton=Btn(T("Console: off","控制台：关闭"),()=>{try{BepInExConsoleControl.Toggle();}catch(Exception e){_error=e.Message;}},938,h-48,120,UiActionRole.Overview);
        _activity = OverviewUi.Text(_panel, "", 15); OverviewUi.Place(_activity.rectTransform, 22, h - 130, w - 44, 24);
        var timeHeading = OverviewUi.Text(_panel, Loc.T("Time (* received)", "时间（* 接收时间）", "時間（* 接收時間）"), 12); OverviewUi.Place(timeHeading.rectTransform, 30, 142, TimeWidth, 24);
        var modHeading = OverviewUi.Text(_panel, Loc.T("Mod name / package ID", "模组译名／包 ID", "模組譯名／套件 ID"), 12); OverviewUi.Place(modHeading.rectTransform, 30 + TimeWidth, 142, ModWidth, 24);
        modHeading.raycastTarget = true; modHeading.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => { _showPackageId = !_showPackageId; _modFilter=""; _modNames.Clear(); Refilter(); };
        var bodyHeading = OverviewUi.Text(_panel, Loc.T("Log message", "日志正文", "日誌正文"), 12); OverviewUi.Place(bodyHeading.rectTransform, 30 + GutterWidth, 142, 180, 24);
        _viewport = OverviewUi.Panel("LogViewport", _panel, ModConfigLook.Surface(new Color(.065f, .075f, .08f, 1))); OverviewUi.Place(_viewport, 22, 168, w - 62, h - 268); _viewport.gameObject.AddComponent<RectMask2D>();
        _content = OverviewUi.Rect("Lines", _viewport); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(0, 1); _content.pivot = new Vector2(0, 1); _content.sizeDelta = Vector2.zero;
        _scroll = _viewport.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = _viewport; _scroll.content = _content; _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 46;
        _scroll.inertia = false;
        var reader = _viewport.gameObject.AddComponent<LogReadingSurface>();
        reader.Scroll = _scroll; reader.targetGraphic = _viewport.GetComponent<Image>(); reader.transition = Selectable.Transition.None;
        reader.TryMovePage = amount => { _follow = false; float before = _scroll.verticalNormalizedPosition;
            float range = Mathf.Max(0, _content.rect.height - _viewport.rect.height);
            if (range < 1) return false;
            _scroll.StopMovement(); _scroll.verticalNormalizedPosition = Mathf.Clamp01(before - amount / range);
            return Mathf.Abs(_scroll.verticalNormalizedPosition - before) > .0001f; };
        _measure = OverviewUi.Text(_panel, "", 16); _measure.richText = false; _measure.textWrappingMode = TextWrappingModes.Normal; _measure.gameObject.SetActive(false);
        var selection = _viewport.gameObject.AddComponent<LogTextSelection>(); selection.Owner = this;
        for (int i = 0; i < Math.Ceiling(_viewport.rect.height / LineHeight) + 3; i++)
        {
            var highlight = OverviewUi.Panel("Selection", _content, new Color(.2f, .49f, .65f, .65f)).GetComponent<Image>(); highlight.raycastTarget = false; _highlights.Add(highlight);
            var match = OverviewUi.Panel("SearchMatch", _content, ModConfigLook.Surface(new Color(.7f, .72f, .68f, .06f))).GetComponent<Image>(); match.raycastTarget = false; _searchHighlights.Add(match);
            var text = OverviewUi.Text(_content, "", 16); text.richText = true; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft; text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular; _rows.Add(text);
            var time = OverviewUi.Text(_viewport, "", 13); time.color = ModConfigLook.Surface(new Color(.57f, .63f, .65f)); time.alignment = TextAlignmentOptions.MidlineLeft; _times.Add(time);
            var mod = OverviewUi.Text(_viewport, "", 14); mod.color = new Color(.49f, .77f, .82f); mod.overflowMode = TextOverflowModes.Ellipsis; mod.alignment = TextAlignmentOptions.MidlineLeft; _mods.Add(mod);
        }
        _horizontal = MakeScrollbar("HorizontalScrollbar", 22, h - 130, w - 62, 10, false);
        _vertical = MakeScrollbar("VerticalScrollbar", w - 34, 168, 12, h - 268, true);
        _scroll.horizontalScrollbar = _horizontal; _scroll.verticalScrollbar = _vertical;
        var gutter = OverviewUi.Panel("FixedColumns", _viewport, ModConfigLook.Surface(new Color(.08f, .10f, .11f, 1))); OverviewUi.Place(gutter, 0, 0, GutterWidth, _viewport.rect.height); gutter.GetComponent<Image>().raycastTarget = false;
        foreach (var time in _times) time.transform.SetAsLastSibling(); foreach (var mod in _mods) mod.transform.SetAsLastSibling();
        BuildPinnedArea();
        _nameTip = OverviewUi.Text(_panel, "", 15); _nameTip.color = new Color(.68f, .88f, .92f); OverviewUi.Place(_nameTip.rectTransform, 30, 180, w - 60, 28); _nameTip.gameObject.SetActive(false);
        var nameHover = _viewport.gameObject.AddComponent<LogNameHover>(); nameHover.Owner = this;
        _status = OverviewUi.Text(_panel, "", 13); OverviewUi.Place(_status.rectTransform, 22, h - 98, w - 44, 44); _status.textWrappingMode = TextWrappingModes.Normal; _status.overflowMode = TextOverflowModes.Ellipsis; _status.maxVisibleLines = 2; _status.alignment = TextAlignmentOptions.BottomLeft;
        Btn(T("Copy selection", "复制选中"), CopySelection, 490, h - 48, 104, UiActionRole.Edit);
        Btn(T("Copy record", "复制整条"), () => { var lines = _selectionPinned ? _pinLines : _lines; if (_selectionEnd.Line >= 0 && _selectionEnd.Line < lines.Count) GUIUtility.systemCopyBuffer = lines[_selectionEnd.Line].Record; }, 602, h - 48, 104, UiActionRole.Edit);
        Btn(T("Copy filtered", "复制筛选结果"), () => GUIUtility.systemCopyBuffer = string.Join("\n", _filteredRecords), 714, h - 48, 104, UiActionRole.Edit);
        Btn(T("Stack", "展开／收起栈"), ToggleStack, 826, h - 48, 104);

        Btn(T("Close", "关闭"), OverviewUi.RequestCloseModal, w - 130, h - 48, 108);
        // 1.1.5 (user screenshot): the "|" divider was placed inside the jump-to-bottom button (472..482 vs 388..492); removed.
        foreach (var label in _panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (!label.transform.IsChildOf(_viewport) && label != _measure) label.text = DiagnosticUiLanguage.Text(label.text);
        Switch("bep");
    }
    private Button Btn(string caption, Action action, float x, float y, float width, UiActionRole role = UiActionRole.Neutral)
    { var b = OverviewUi.Button(_panel, DiagnosticUiLanguage.Text(caption), action, width, 32, role); OverviewUi.Place((RectTransform)b.transform, x, y, width, 32); return b; }
    private void Tab(string name, string id, float x) => _tabs[id] = Btn(name, () => { Switch(id); if (id == "custom") PickSource(); }, x, 56, 128, UiActionRole.Overview);
    private void Switch(string tab)
    {
        if (_options != null) { Destroy(_options.gameObject); _options = null; }
        _generation++; _paused = false; _hasSelection = false; _tab = tab; _records = Array.Empty<MceLogRecord>(); _error = ""; _readError = ""; _pendingRefilter = false; _path = ""; _custom = null; _recordStamp = ""; _repeatIndex.Clear(); _wrapped.Clear(); _lastSequence = 0; _addedTotal = 0; _batchAdded = 0; _rate = 0; _repeat = 0; _repeatText = ""; _liveEpoch = ""; _modFilter = "";
        // A reader belongs to one background job. Dispose after that job completes.
        var old = _reader; _reader = null; if (_read == null) old?.Dispose(); else _ = _read.ContinueWith(_ => old?.Dispose());
        if (tab == "bep") _path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        if (tab == "game") _path = GamePaths().FirstOrDefault(File.Exists) ?? GamePaths().First();
        if (tab == "custom") { _custom = OrderCustomSources(MceLogApi.List()).FirstOrDefault(); _path = _custom?.File ?? ""; }
        if (_path.Length > 0) _reader = new LogFileReader(_path);
        _nextRead = 0; Refilter();
    }
    private static readonly string[] LevelNames = { "全部", "调试", "信息", "警告", "错误", "严重错误", "未识别" }; // display goes through DiagnosticUiLanguage
    private static readonly MceLogLevel[] LevelValues = { MceLogLevel.Unknown, MceLogLevel.Debug, MceLogLevel.Info, MceLogLevel.Warning, MceLogLevel.Error, MceLogLevel.Fatal, MceLogLevel.Unknown };
    private void ScrollTo(float value)
    {
        _scroll.verticalNormalizedPosition = value;
    }
    private Scrollbar MakeScrollbar(string name, float x, float y, float width, float height, bool vertical)
    {
        // Plain images: paper-panel decoration expands tiny scrollbar handles into full-size banners.
        var root = OverviewUi.Rect(name, _panel); OverviewUi.Place(root, x, y, width, height);
        root.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.11f, .15f, .16f, 1));
        var sliding = OverviewUi.Rect("SlidingArea", root); OverviewUi.Fill(sliding, 1);
        var handle = OverviewUi.Rect("Handle", sliding); OverviewUi.Fill(handle);
        var image = handle.gameObject.AddComponent<Image>(); image.color = new Color(.38f, .59f, .61f, 1);
        var bar = root.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle; bar.targetGraphic = image;
        bar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;
        if (vertical) root.gameObject.AddComponent<LogScrollbarInput>().Owner = this;
        return bar;
    }
    private void OpenLogFolder()
    {
        if (_path.Length > 0) PackFileShortcuts.Open(_path, true);
    }
    private static readonly Color ModSourceColor = new(.49f, .77f, .82f), ManagerSourceColor = new(.95f, .78f, .38f);
    private static Color LevelColor(MceLogLevel level) => level switch { MceLogLevel.Warning => new Color(1,.8f,.3f), MceLogLevel.Error or MceLogLevel.Fatal => new Color(1,.4f,.37f), MceLogLevel.Debug => ModConfigLook.Surface(new Color(.66f,.64f,.8f)), _ => OverviewUi.Ivory };
    private void Near(RectTransform popup, RectTransform anchor)
    {
        // Multi-control dialogs keep their reviewed size; compact menus use the shared helper below.
        Vector3 p=_panel.InverseTransformPoint(anchor.TransformPoint(new Vector3(0,-anchor.rect.height,0)));
        float x=p.x+_panel.rect.width*.5f,y=_panel.rect.height*.5f-p.y+5;
        if(y+popup.rect.height>_panel.rect.height-12)y=Math.Max(12,y-popup.rect.height-anchor.rect.height-10);
        OverviewUi.Place(popup,Mathf.Clamp(x,12,Math.Max(12,_panel.rect.width-popup.rect.width-12)),y,popup.rect.width,popup.rect.height);
    }
    private void PickLevel()
    {
        if(_options!=null)Destroy(_options.gameObject);
        _options = null;
        WindowPopover.Menu(_panel, (RectTransform)_levelButton.transform, T("Log level", "日志等级"),
            LevelNames.Select((name, index) => (DiagnosticUiLanguage.Text(name), index.ToString())).ToArray(), _level.ToString(),
            value => { _level = int.Parse(value); Refilter(); });
    }
    private string FavoriteKey(MceLogRecord record)
    {
        var mod=_installedMods.FirstOrDefault(m=>m.Guid.Equals(record.Category,StringComparison.OrdinalIgnoreCase)||m.RawName.Equals(record.Category,StringComparison.OrdinalIgnoreCase));
        return mod?.Guid??record.Category;
    }
    private void SaveFavorites(){Directory.CreateDirectory(Path.GetDirectoryName(FavoritesPath)!);File.WriteAllText(FavoritesPath,new JArray(_favoriteSources.OrderBy(s=>s)).ToString());}
    private void PickMod()
    {
        if (_options != null) Destroy(_options.gameObject);
        float width = Mathf.Min(1000, _panel.rect.width - 60), height = Mathf.Min(650, _panel.rect.height - 190);
        _options = OverviewUi.Panel("LogModTree", _panel, ModConfigLook.Surface(new Color(.10f, .13f, .14f, 1))); OverviewUi.Place(_options, 30, 150, width, height);Near(_options,(RectTransform)_modButton.transform);
        var all = OverviewUi.Button(_options, T("All mods", "全部模组"), () => { _modFilter = ""; Refilter(); Destroy(_options.gameObject); _options = null; }, 130, 32); OverviewUi.Place((RectTransform)all.transform, 14, 10, 130, 32);
        var close = OverviewUi.Button(_options, T("Close", "关闭"), () => { Destroy(_options.gameObject); _options = null; }, 104, 32); OverviewUi.Place((RectTransform)close.transform, width - 118, 10, 104, 32);
        var favorites=OverviewUi.Button(_options,_favoritesOnly?T("Favorites: on","仅收藏：开"):T("Favorites: off","仅收藏：关"),()=>{_favoritesOnly=!_favoritesOnly;Refilter();PickMod();},150,32,UiActionRole.Commit);OverviewUi.Place((RectTransform)favorites.transform,154,10,150,32);
        var host = OverviewUi.Rect("TreeViewport", _options); OverviewUi.Place(host, 12, 54, width - 24, height - 68); host.gameObject.AddComponent<RectMask2D>(); host.gameObject.AddComponent<Image>().color = Color.clear;
        var content = OverviewUi.Rect("TreeContent", host); OverviewUi.Place(content, 0, 0, width - 42, 1);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.viewport = host; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 120; scroll.movementType = ScrollRect.MovementType.Clamped;
        var entries = _records.Select(r => r.Category).Distinct().Select(category => {
            var mod = _installedMods.FirstOrDefault(m => m.Guid.Equals(category, StringComparison.OrdinalIgnoreCase) || m.RawName.Equals(category, StringComparison.OrdinalIgnoreCase));
            return (Category: category, Mod: mod, Group: mod?.Rule.groupId ?? "runtime");
        }).ToList();
        float y = 0;
        var tree = new GroupTree(_repo.State.groups);
        foreach (var group in entries.GroupBy(e => e.Group).OrderBy(g => tree.Position(g.Key)))
        {
            string id = group.Key; var info = tree.Get(id);
            string name = info == null ? T("Runtime / other sources", "运行时／其他来源") : tree.Path(id);
            Color color = OverviewUi.Blue; if (info != null) ColorUtility.TryParseHtmlString(info.color, out color);
            var header = OverviewUi.Button(content, (_foldedModGroups.Contains(id) ? "+ " : "- ") + name + " (" + group.Count() + ")", () => { if (!_foldedModGroups.Add(id)) _foldedModGroups.Remove(id); PickMod(); }, width - 54, 36, UiActionRole.Overview);
            OverviewUi.Place((RectTransform)header.transform, 0, y, width - 54, 36); UiActionStyle.Tint(header, color); y += 42;
            if (_foldedModGroups.Contains(id)) continue;
            foreach (var entry in group.OrderBy(e => e.Mod?.Rule.order ?? 0))
            {
                string nameText = entry.Mod == null ? entry.Category.Length > 0 ? entry.Category : T("Unidentified source", "未识别来源") : OverviewLanguageBridge.GetDisplayName(entry.Mod);
                string metadata = entry.Mod == null ? "" : PackageId(entry.Mod) + ", " + entry.Mod.DisplayVersion;
                string label = Escape(nameText) + (metadata.Length > 0 ? " <color=#8C918E><size=80%>(" + Escape(metadata) + ")</size></color>" : "");
                string favoriteId=entry.Mod?.Guid??entry.Category;
                var favorite=OverviewUi.Button(content,_favoriteSources.Contains(favoriteId)?"[+]":"[ ]",()=>{if(!_favoriteSources.Add(favoriteId))_favoriteSources.Remove(favoriteId);SaveFavorites();Refilter();PickMod();},42,32,UiActionRole.Neutral);OverviewUi.Place((RectTransform)favorite.transform,24,y,42,32);
                var button = OverviewUi.Button(content, label, () => { _modFilter = favoriteId; Refilter(); Destroy(_options.gameObject); _options = null; }, width - 128, 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)button.transform, 72, y, width - 128, 32);
                var text = button.GetComponentInChildren<TextMeshProUGUI>(); text.alignment = TextAlignmentOptions.MidlineLeft; text.margin = new Vector4(14, 0, 14, 0); text.richText=true; text.fontStyle = FontStyles.Normal; text.overflowMode = TextOverflowModes.Ellipsis; y += 38;
            }
        }
        content.sizeDelta = new Vector2(width - 42, y + 8);
    }
    private void ShowChoices(string heading, IReadOnlyList<(string Label, Action Action)> choices)
    {
        if (_options != null) Destroy(_options.gameObject);
        float width = Mathf.Min(360, _panel.rect.width - 24), height = Mathf.Min(420, choices.Count * 42 + 64);
        _options = OverviewUi.Panel("LogChoice", _panel, ModConfigLook.Surface(new Color(.12f, .16f, .18f, 1))); OverviewUi.Place(_options, 0, 0, width, height);WindowPopover.Place(_options,_panel,(RectTransform)_tabs["custom"].transform,width,height);
        width = _options.rect.width; height = _options.rect.height;
        var title = OverviewUi.Text(_options, DiagnosticUiLanguage.Text(heading), 20); OverviewUi.Place(title.rectTransform, 14, 8, width - 140, 32);
        var host = OverviewUi.Rect("ChoiceViewport", _options); OverviewUi.Place(host, 10, 48, width - 20, height - 64); host.gameObject.AddComponent<RectMask2D>(); host.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(0, 0, 0, .1f));
        var content = OverviewUi.Rect("Choices", host); OverviewUi.Place(content, 0, 0, width - 40, choices.Count * 42);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.viewport = host; scroll.content = content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 110;
        int i = 0; foreach (var choice in choices)
        {
            var local = choice; var b = OverviewUi.Button(content, DiagnosticUiLanguage.Text(choice.Label), () => { if (_options != null) Destroy(_options.gameObject); _options = null; local.Action(); }, width - 50, 34, UiActionRole.Edit);
            OverviewUi.Place((RectTransform)b.transform, 4, i++ * 42, width - 50, 34);
        }
        var close = OverviewUi.Button(_options, Loc.T("Close", "关闭", "關閉"), () => { Destroy(_options.gameObject); _options = null; }, 100, 30); OverviewUi.Place((RectTransform)close.transform, width - 115, 8, 100, 30);
    }
    private static string PackageId(OverviewMod mod) => mod.SourceName.Length > 0 ? mod.SourceNamespace + "-" + mod.SourceName : mod.Guid;
    private string ModName(MceLogRecord record)
    {
        string key = record.Category.Length > 0 ? record.Category : record.Message;
        if (_modNames.TryGetValue(key, out var cached)) return cached;
        if (_modNames.Count > 12000) _modNames.Clear();
        if (record.Category.Length > 0)
        {
            var known = _installedMods.FirstOrDefault(m => string.Equals(m.Guid, record.Category, StringComparison.OrdinalIgnoreCase) || string.Equals(m.RawName, record.Category, StringComparison.OrdinalIgnoreCase));
            return _modNames[key] = known == null ? record.Category : _showPackageId ? PackageId(known) : OverviewLanguageBridge.GetDisplayName(known);
        }
        var match = _installedMods.FirstOrDefault(m => m.Guid.Length > 0 && record.Message.IndexOf(m.Guid, StringComparison.OrdinalIgnoreCase) >= 0 || m.RawName.Length > 3 && record.Message.IndexOf(m.RawName, StringComparison.OrdinalIgnoreCase) >= 0);
        return _modNames[key] = match == null ? "" : _showPackageId ? PackageId(match) : OverviewLanguageBridge.GetDisplayName(match);
    }
    private void TrackActivity(MceLogRecord[] records)
    {
        float now = Time.unscaledTime;
        if (records.Length == 0) { _rate = 0; _lastReadAt = now; return; }
        string epoch = records[^1].Epoch; long sequence = records[^1].Sequence;
        if (_liveEpoch != epoch) { _liveEpoch = epoch; _lastSequence = sequence; _rate = 0; _lastReadAt = now; return; }
        _batchAdded = (int)Math.Min(int.MaxValue, Math.Max(0, sequence - _lastSequence));
        _rate = _batchAdded / Math.Max(.05f, now - _lastReadAt); _addedTotal += _batchAdded;
        foreach (var record in records.Where(r => r.Sequence > _lastSequence))
        {
            string text = record.Category + "|" + record.Message;
            if (text == _repeatText) _repeat++; else { _repeatText = text; _repeat = 1; }
        }
        _lastSequence = sequence; _lastReadAt = now;
    }
    private static string[] GamePaths()
    {
        return new[] { Application.consoleLogPath, Path.Combine(Application.persistentDataPath, "Player.log"), Path.Combine(BepInEx.Paths.GameRootPath, "output_log.txt") }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <summary>This mod's own source stays first in the custom-source list so manager reports are one click away.</summary>
    private static MceLogApi.Source[] OrderCustomSources(MceLogApi.Source[] sources)
        => sources.OrderByDescending(source => source.Id.EndsWith("/" + Diagnostics.ManagerLog.SourceId, StringComparison.Ordinal)).ToArray();
    private void PickSource()
    {
        var sources = OrderCustomSources(MceLogApi.List());
        ShowChoices("自定义日志来源", sources.Select(source => ((source.Id.EndsWith("/" + Diagnostics.ManagerLog.SourceId, StringComparison.Ordinal) ? "★ " : "") + source.Name, (Action)(() => {
            Switch("custom"); var old = _reader; _reader = null;
            if (_read == null) old?.Dispose(); else _ = _read.ContinueWith(_ => old?.Dispose());
            _generation++; _custom = source; _path = source.File; _reader = _path.Length == 0 ? null : new LogFileReader(_path); _recordStamp = "";
        }))).ToArray());
    }
    private void StartRead(bool earlier = false)
    {
        _readGeneration = _generation; _loadEarlier = earlier; var reader = _reader; var source = _custom;
        _read = Task.Run(() => reader != null ? reader.Read(earlier) : source != null ? MceLogApi.Read(source) : Array.Empty<MceLogRecord>());
    }
    private void Update()
    {
        if (_read?.IsCompleted == true)
        {
            if (_readGeneration == _generation && (!_paused || _loadEarlier))
            {
                if (_read.IsFaulted) _readError = _read.Exception!.GetBaseException().Message;
                else {
                    _readError = ""; var records = _read.Result;
                    string stamp = LogRepeatIndex.SnapshotStamp(records);
                    TrackActivity(records);
                    // Untimed file entries remain unknown. Runtime channel owns original emission times.
                    _repeatIndex.Update(records);
                    if (stamp != _recordStamp) { _recordStamp = stamp; _records = records; if (!_selecting && !_hasSelection) Refilter(); else _pendingRefilter = true; }
                }
            }
            _read = null;
        }
        if (_pendingRefilter && !_selecting && !_hasSelection) { _pendingRefilter = false; Refilter(); }
        if (_export?.IsCompleted == true) { _exportSucceeded = !_export.IsFaulted; _error = _export.IsFaulted ? "导出失败：" + _export.Exception!.GetBaseException().Message : (_exportFallbackNote != null ? "导出完成（固定位置）：" : "导出完成：") + _exportDestination; _export = null; }
        _consoleButton.GetComponentInChildren<TextMeshProUGUI>().text=BepInExConsoleControl.Active?T("Console: on","控制台：开启"):T("Console: off","控制台：关闭");
        bool foldersChanged = _exportFolderButton.interactable != _exportSucceeded || _logFolderButton.interactable != (_path.Length > 0);
        _exportFolderButton.interactable = _exportSucceeded; _logFolderButton.interactable = _path.Length > 0;
        if (foldersChanged) PublicationNavigation.Invalidate(_panel);
        foreach (var tab in _tabs) UiActionStyle.Select(tab.Value, tab.Key == _tab);
        UiActionStyle.Set(_pauseButton, _paused ? UiActionRole.Commit : UiActionRole.Edit);
        UiActionStyle.Set(_followButton, _follow ? UiActionRole.Commit : UiActionRole.Neutral);
        _pauseButton.GetComponentInChildren<TextMeshProUGUI>().text = _paused ? "继续刷新" : "暂停刷新";
        _followButton.GetComponentInChildren<TextMeshProUGUI>().text = _follow ? "跟随：开" : "跟随：关";
        for (int i = 0; i < _exportChoices.Count; i++) if (_exportChoices[i] != null) UiActionStyle.Set(_exportChoices[i], i == _scope ? UiActionRole.Commit : UiActionRole.Neutral);
        if (!_paused && !_selecting && !_hasSelection && _read == null && Time.unscaledTime >= _nextRead) { _nextRead = Time.unscaledTime + .6f; StartRead(); }
        // 1.1.4 (gamepad review): the right stick scrolled, then follow pulled the view straight back to the newest line.
        if (_follow && !_selecting && !_hasSelection) ScrollTo(0);
        WireLayer(_errorDetails); WireLayer(_options);
        _activity.gameObject.SetActive(false);
        _activity.text = "新增 " + _addedTotal + " 条 · " + _rate.ToString("0.0") + " 条/秒" + (_repeat > 1 ? " · 连续重复 ×" + _repeat : "");
        _activity.color = _rate > 10 ? new Color(1f, .65f, .3f) : new Color(.55f, .8f, .75f);
        _horizontal.gameObject.SetActive(false);
        _vertical.gameObject.SetActive(_content.sizeDelta.y > _viewport.rect.height + 1);
        Render(); RenderPinned();
        string level = LevelNames[_level];
        _levelButton.GetComponentInChildren<TextMeshProUGUI>().text = "日志等级：" + level;
        _levelButton.GetComponentInChildren<TextMeshProUGUI>().color=_level==0?OverviewUi.Ivory:LevelColor(LevelValues[_level]);
        _modButton.GetComponentInChildren<TextMeshProUGUI>().text = "模组：" + (_modFilter.Length == 0 ? "全部" : _modFilter);
        _status.text = (_error.Length > 0 ? _error + "\n" : "") + (_readError.Length > 0 ? _readError + "\n" : "") + (_path.Length > 0 ? _path : _custom?.Name ?? "未接入自定义日志") + " · " + level + " · " + _lines.Count + " 行 · 新增 " + _addedTotal + " 条 · " + _rate.ToString("0.0") + " 条/秒" + (_repeat > 1 ? " · 连续重复 ×" + _repeat : "") + "（旧日志无时间显示 —；导出完整来源）" + (_paused ? " · 已暂停刷新" : "") + (_hasSelection ? " · 文字选择中，点击空白清除选择" : "");
        _activity.text = DiagnosticUiLanguage.Text(_activity.text);
        _status.text = DiagnosticUiLanguage.Text(_status.text);
        _levelButton.GetComponentInChildren<TextMeshProUGUI>().text = DiagnosticUiLanguage.Text(_levelButton.GetComponentInChildren<TextMeshProUGUI>().text);
        _modButton.GetComponentInChildren<TextMeshProUGUI>().text = DiagnosticUiLanguage.Text(_modButton.GetComponentInChildren<TextMeshProUGUI>().text);
        _pauseButton.GetComponentInChildren<TextMeshProUGUI>().text = DiagnosticUiLanguage.Text(_pauseButton.GetComponentInChildren<TextMeshProUGUI>().text);
        _followButton.GetComponentInChildren<TextMeshProUGUI>().text = DiagnosticUiLanguage.Text(_followButton.GetComponentInChildren<TextMeshProUGUI>().text);
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C) && !_search.isFocused) CopySelection();
    }
    private void Refilter()
    {
        _renderRevision++; _lines.Clear(); _pinLines.Clear(); _filteredRecords.Clear(); _hasSelection = false; string query = _search?.text ?? ""; _textWidth = _viewport.rect.width;
        var groups = _repeatIndex.Groups;   // sorting every group is not free; this used to run three times per rebuild
        var repeated = groups.Where(g => g.First.Level is MceLogLevel.Error or MceLogLevel.Fatal).ToDictionary(g => g.Key, StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in groups.OrderBy(g => g.LatestOrder))
        {
            var record = entry.Latest;
            if(_favoritesOnly && !_favoriteSources.Contains(FavoriteKey(record)))continue;
            if (_level > 0 && record.Level != LevelValues[_level]) continue;
            string mod = ModName(record);
            if (_modFilter.Length > 0 && FavoriteKey(record) != _modFilter) continue;
            string plain = entry.Count > 1 ? entry.CopyText : LogParser.Plain(record); if (query.Length > 0 && plain.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string key = LogRepeatIndex.Key(record);
            if (repeated.TryGetValue(key, out var group))
            {
                if (!emitted.Add(key)) continue;
                _filteredRecords.Add(group.CopyText);
                continue;
            }
            _filteredRecords.Add(plain);
            string displayed = (entry.Count > 1 ? "[x" + entry.Count + "] " : "") + (record.Level == MceLogLevel.Unknown ? "" : "[" + record.Level + "] ") + (record.SubjectName.Length == 0 ? "" : record.SubjectName + ": ") + record.Message;
            if (record.StackTrace.Length > 0) displayed += _collapsedStacks.Contains(plain) ? "  [调用栈已收起]" : "\n" + record.StackTrace;
            string time = record.Time == default ? "-" : record.Time.ToString("HH:mm:ss.fff") + (record.Fields.TryGetValue("timestampKind", out var kind) && kind == "received" ? "*" : "");
            bool first = true; bool ours = record.Category == Diagnostics.ManagerLog.CategoryLabel;
            foreach (string line in LogViewRules.Collapse(Wrap(displayed, _viewport.rect.width - GutterWidth - 24),_expandedRecords.Contains(plain))) { _lines.Add((line, record.Level, plain, first ? time : "", first ? mod : "", ours)); first = false; }
        }
        foreach (var group in repeated.Values.OrderBy(g => g.Order))
        {
            string mod = ModName(group.Latest), plain = group.CopyText;
            if(_favoritesOnly && !_favoriteSources.Contains(FavoriteKey(group.Latest)))continue;
            if (_level > 0 && group.Latest.Level != LevelValues[_level] || _modFilter.Length > 0 && FavoriteKey(group.Latest) != _modFilter || query.Length > 0 && plain.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (emitted.Add(group.Key)) _filteredRecords.Add(plain);
            string text = "[x" + group.Count + "] " + group.Latest.Message.Replace("\r", " ").Replace("\n", " ");
            _pinLines.Add((text, group.Latest.Level, plain, group.Latest.Time==default?"-":group.Latest.Time.ToString("HH:mm:ss.fff"), mod, group.Latest.Category == Diagnostics.ManagerLog.CategoryLabel));
        }
        int hidden=Math.Max(0,_pinLines.Count-6);
        if(!_expandAllErrors && hidden>0){_pinLines.RemoveRange(6,hidden);_pinLines.Add(("···+"+hidden,MceLogLevel.Error,"","","",false));}
        float pinHeight = Math.Min(_viewport.rect.height * .42f, _pinLines.Count * LineHeight + 8);
        _pinPanel.gameObject.SetActive(_pinLines.Count > 0); OverviewUi.Place(_pinPanel, 0, 0, _viewport.rect.width, pinHeight);
        OverviewUi.Place(_pinViewport, 4, 4, _viewport.rect.width - 12, Math.Max(20, pinHeight - 8));
        _pinContent.sizeDelta = new Vector2(_pinViewport.rect.width, _pinLines.Count * LineHeight);
        _content.sizeDelta = new Vector2(_textWidth, Math.Max(_viewport.rect.height, _lines.Count * LineHeight + 10));
    }
    private string[] Wrap(string text, float width)
    {
        string key = ((int)width) + "|" + text;
        if (_wrapped.TryGetValue(key, out var cached)) return cached;
        if (_wrapped.Count > 12000) _wrapped.Clear();
        width = Math.Max(80, width);
        // Hidden TMP mesh layout can retain a zero-width internal margin. Never use its lineInfo to split source.
        _measure.margin = Vector4.zero; _measure.textWrappingMode = TextWrappingModes.NoWrap;
        return _wrapped[key] = LogTextWrap.Lines(text, width, value => _measure.GetPreferredValues(value, float.PositiveInfinity, float.PositiveInfinity).x);
    }
    private void BuildPinnedArea()
    {
        _pinPanel = OverviewUi.Rect("RepeatedErrorsOverlay", _viewport);
        _pinPanel.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.17f, .09f, .09f, .99f));

        OverviewUi.Place(_pinPanel, 0, 0, _viewport.rect.width, _viewport.rect.height * .42f);
        _pinViewport = OverviewUi.Rect("PinnedViewport", _pinPanel); OverviewUi.Place(_pinViewport, 4, 4, _viewport.rect.width - 12, _viewport.rect.height * .42f - 8); _pinViewport.gameObject.AddComponent<RectMask2D>(); _pinViewport.gameObject.AddComponent<Image>().color = Color.clear;
        _pinContent = OverviewUi.Rect("PinnedLines", _pinViewport); OverviewUi.Place(_pinContent, 0, 0, _viewport.rect.width - 24, 1);
        _pinScroll = _pinViewport.gameObject.AddComponent<ScrollRect>(); _pinScroll.viewport = _pinViewport; _pinScroll.content = _pinContent; _pinScroll.horizontal = false; _pinScroll.inertia = false; _pinScroll.scrollSensitivity = 46; _pinScroll.movementType = ScrollRect.MovementType.Clamped;

        for (int i = 0; i < Math.Ceiling(_viewport.rect.height / LineHeight) + 3; i++)
        {
            int slot = i; var button = FlatListRow.Create(_pinContent, () => OpenPinnedDetails(slot), _viewport.rect.width - 24, LineHeight,ModConfigLook.Surface(new Color(.20f,.09f,.09f,1)));
            var row = OverviewUi.Text(button.transform,"",14); OverviewUi.Fill(row.rectTransform); row.richText = false; row.fontSize = 14; row.margin = new Vector4(8, 0, 8, 0); row.textWrappingMode = TextWrappingModes.NoWrap; row.overflowMode = TextOverflowModes.Ellipsis; row.alignment = TextAlignmentOptions.MidlineLeft; _pinRows.Add(row); _pinButtons.Add(button);
            var time=OverviewUi.Text(button.transform,"",13);time.alignment=TextAlignmentOptions.MidlineLeft;time.color=OverviewUi.Muted;_pinTimes.Add(time);
            var mod=OverviewUi.Text(button.transform,"",14);mod.alignment=TextAlignmentOptions.MidlineLeft;mod.color=OverviewUi.Blue;_pinMods.Add(mod);
        }
        _pinPanel.SetAsLastSibling(); _pinPanel.gameObject.SetActive(false);
    }
    private void OpenPinnedDetails(int slot)
    {
        int index = Math.Max(0, (int)(_pinContent.anchoredPosition.y / LineHeight)) + slot;
        if (index >= _pinLines.Count) return;
        if(_pinLines[index].Record.Length==0){_expandAllErrors=!_expandAllErrors;Refilter();return;}
        if (_errorDetails != null) Destroy(_errorDetails.gameObject);
        _errorDetails = OverviewUi.Panel("ErrorDetails", _panel, ModConfigLook.Surface(new Color(.12f, .10f, .10f, 1))); OverviewUi.Place(_errorDetails, 26, 170, _panel.rect.width - 52, _panel.rect.height - 280);
        Near(_errorDetails,(RectTransform)_pinButtons[slot].transform);
        string record = _pinLines[index].Record;
        var input = OverviewUi.Input(_errorDetails, record, true); input.readOnly = true; input.textComponent.fontSize = 16; input.textComponent.margin = Vector4.zero;
        OverviewUi.Place((RectTransform)input.transform, 12, 12, _errorDetails.rect.width - 24, _errorDetails.rect.height - 68);
        var close = OverviewUi.Button(_errorDetails, T("Close", "关闭"), () => { Destroy(_errorDetails.gameObject); _errorDetails = null; }, 110, 32); OverviewUi.Place((RectTransform)close.transform, 12, _errorDetails.rect.height - 44, 110, 32);
        var copy = OverviewUi.Button(_errorDetails, T("Copy record", "复制整条"), () => GUIUtility.systemCopyBuffer = record, 130, 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)copy.transform, 134, _errorDetails.rect.height - 44, 130, 32);
    }
    private void RenderPinned()
    {
        if (!_pinPanel.gameObject.activeSelf) return;
        int first = Math.Max(0, (int)(_pinContent.anchoredPosition.y / LineHeight));
        for (int i = 0; i < _pinRows.Count; i++)
        {
            int index = first + i; var row = _pinRows[i]; _pinButtons[i].gameObject.SetActive(index < _pinLines.Count);
            if (index >= _pinLines.Count) continue;
            string pinText=_pinLines[index].Text;if(row.text!=pinText)row.text=pinText;
            var (a, b) = Ordered(); row.color = _hasSelection && _selectionPinned && index >= a.Line && index <= b.Line ? new Color(1, .86f, .55f) : new Color(1, .48f, .42f);
            OverviewUi.Place((RectTransform)_pinButtons[i].transform, 0, index * LineHeight, _pinViewport.rect.width, LineHeight);
            row.margin=new Vector4(GutterWidth+4,0,8,0);
            _pinTimes[i].text=_pinLines[index].Time;OverviewUi.Place(_pinTimes[i].rectTransform,4,0,TimeWidth-12,LineHeight);
            _pinMods[i].text=MiddleName(_pinLines[index].Mod);OverviewUi.PlaceClipped(_pinMods[i],TimeWidth+4,0,ModWidth-16,LineHeight);
        }
    }
    private void Render()
    {
        int first = Math.Max(0, (int)(_content.anchoredPosition.y / LineHeight));
        if (!_hasSelection && !_selecting && _renderFirst == first && _drawnRevision == _renderRevision && Math.Abs(_renderY - _content.anchoredPosition.y) < .1f) return;
        _renderFirst = first; _drawnRevision = _renderRevision; _renderY = _content.anchoredPosition.y;
        for (int i = 0; i < _rows.Count; i++)
        {
            int index = first + i; var row = _rows[i]; row.gameObject.SetActive(index < _lines.Count); _times[i].gameObject.SetActive(index < _lines.Count); _mods[i].gameObject.SetActive(index < _lines.Count); _highlights[i].gameObject.SetActive(false); _searchHighlights[i].gameObject.SetActive(false); if (index >= _lines.Count) continue;
            string styled = Escape(_lines[index].Text);
            if(_lines[index].Text.StartsWith("···+",StringComparison.Ordinal))styled="<alpha=#99>"+styled+"<alpha=#FF>";
            string find = _search.text; if (find.Length > 0) { string escapedFind = Escape(find); styled = System.Text.RegularExpressions.Regex.Replace(styled, System.Text.RegularExpressions.Regex.Escape(escapedFind), m => "<mark=#C0A44799>" + m.Value + "</mark>", System.Text.RegularExpressions.RegexOptions.IgnoreCase); } if (row.text != styled) row.text = styled; row.color = _lines[index].Level switch { MceLogLevel.Error or MceLogLevel.Fatal => new Color(1, .42f, .4f), MceLogLevel.Warning => new Color(1, .81f, .36f), MceLogLevel.Debug => ModConfigLook.Surface(new Color(.6f, .59f, .72f)), _ => ModConfigLook.Surface(new Color(.85f, .88f, .87f)) };
            OverviewUi.Place(row.rectTransform, GutterWidth + 8, index * LineHeight, _textWidth - GutterWidth - 16, LineHeight);
            float visibleY = index * LineHeight - _content.anchoredPosition.y;
            _times[i].text = _lines[index].Time; OverviewUi.Place(_times[i].rectTransform, 8, visibleY, TimeWidth - 12, LineHeight);
            _mods[i].text = MiddleName(_lines[index].Mod); OverviewUi.PlaceClipped(_mods[i], TimeWidth + 8, visibleY, ModWidth - 16, LineHeight);
            // Our own records keep a distinct source colour so manager lines stand out in every source view.
            _mods[i].color = _lines[index].Manager ? ManagerSourceColor : ModSourceColor;
            if (_search.text.Length > 0 && _lines[index].Text.IndexOf(_search.text, StringComparison.OrdinalIgnoreCase) >= 0) { _searchHighlights[i].gameObject.SetActive(true); OverviewUi.Place((RectTransform)_searchHighlights[i].transform, 5, index * LineHeight, _textWidth - 10, LineHeight); }
            if (_hasSelection && !_selectionPinned)
            {
                var (a, b) = Ordered(); if (index >= a.Line && index <= b.Line) { row.ForceMeshUpdate(); float left = index == a.Line ? CharacterX(row, a.Char) : 0, right = index == b.Line ? CharacterX(row, b.Char) : row.preferredWidth; _highlights[i].gameObject.SetActive(right > left); OverviewUi.Place((RectTransform)_highlights[i].transform, GutterWidth + 8 + left, index * LineHeight, Math.Max(0, right - left), LineHeight); }
            }
        }
    }
    internal void SelectAt(PointerEventData e, bool begin, bool pinned = false)
    {
        if (begin) _selectionPinned = pinned;
        var content = pinned ? _pinContent : _content; var lines = pinned ? _pinLines : _lines; var rows = pinned ? _pinRows : _rows;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, e.position, e.pressEventCamera, out var point);
        int index = Mathf.Clamp((int)(-point.y / LineHeight), 0, Math.Max(0, lines.Count - 1)); int character = 0;
        var row = rows.FirstOrDefault(r => r.gameObject.activeSelf && Math.Abs(r.rectTransform.anchoredPosition.y + index * LineHeight) < 1);
        if (row != null) { row.ForceMeshUpdate(); int nearest = TMP_TextUtilities.FindNearestCharacter(row, e.position, e.pressEventCamera, false); if (nearest >= 0 && nearest < row.textInfo.characterCount) { var c = row.textInfo.characterInfo[nearest]; character = nearest; RectTransformUtility.ScreenPointToLocalPointInRectangle(row.rectTransform, e.position, e.pressEventCamera, out var local); if (local.x > (c.bottomLeft.x + c.topRight.x) * .5f) character++; } }
        if (begin) { if (pinned) _pinScroll.enabled = false; else _scroll.enabled = false; _selectionStart = (index, character); _selecting = true; _hasSelection = false; _follow = false; }
        _selectionEnd = (index, character); if (!begin) _hasSelection = _selectionEnd != _selectionStart;
        if(begin && !pinned && index<lines.Count && lines[index].Text.StartsWith("···+",StringComparison.Ordinal)) { _expandedRecords.Add(lines[index].Record); _selecting=false; _scroll.enabled=true; Refilter(); }
    }
    internal void EndSelection() { _selecting = false; _scroll.enabled = true; _pinScroll.enabled = true; _drawnRevision = -1; }
    internal void UserScroll() { _follow = false; }
    private RectTransform? _wiredLayer; private bool _layerWired; private GameObject? _layerOpener; private CanvasGroup? _baseGroup;
    /// <summary>1.1.4 (gamepad review): option / detail panels are built under the window, outside its navigation, so the
    /// controller could not reach them. Each new panel gets its own navigation and the first control; the window behind
    /// it stops taking input until it closes, then the focus returns to the button that opened it.</summary>
    private void WireLayer(RectTransform? layer)
    {
        var top = _errorDetails != null ? _errorDetails : _options;
        if (layer != top) return;
        if (top == null)
        {
            if (!_layerWired) return;
            _wiredLayer = null; _layerWired = false; SetBaseInteractable(true);
            if (_layerOpener != null && _layerOpener.activeInHierarchy && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(_layerOpener);
            _layerOpener = null; return;
        }
        if (_layerWired && top == _wiredLayer) return;
        // First panel of a chain: remember the opener. A rebuilt panel (e.g. "favourites only") keeps the first opener.
        if (!_layerWired) _layerOpener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        _wiredLayer = top; _layerWired = true;
        var group = top.GetComponent<CanvasGroup>() ?? top.gameObject.AddComponent<CanvasGroup>(); group.ignoreParentGroups = true;
        SetBaseInteractable(false);
        PublicationNavigation.AttachBoundary(top);
        foreach (var input in top.GetComponentsInChildren<TMP_InputField>(true)) { if (input.readOnly) input.shouldActivateOnSelect = false; else GamepadSupport.MarkSelectable(input.gameObject, null, null); }
        var first = top.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.IsInteractable() && s is not TMP_InputField);
        if (first != null && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(first.gameObject);
    }
    private void SetBaseInteractable(bool value)
    {
        if (_baseGroup == null) _baseGroup = _panel.GetComponent<CanvasGroup>() ?? _panel.gameObject.AddComponent<CanvasGroup>();
        _baseGroup.interactable = value;
    }
    private void LateUpdate() { if (_errorDetails == null && _options == null && _layerWired) WireLayer(null); }
    private void OnDisable() { if (_baseGroup != null) _baseGroup.interactable = true; _layerWired = false; }
    /// <summary>B: closes the top panel; false when none is open.</summary>
    internal bool CloseTopLayer()
    {
        if (_errorDetails != null) { Destroy(_errorDetails.gameObject); _errorDetails = null; return true; }
        if (_options != null) { Destroy(_options.gameObject); _options = null; return true; }
        return false;
    }
    internal void HoverName(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, e.position, e.enterEventCamera, out var point);
        int index = Mathf.FloorToInt((_content.anchoredPosition.y - point.y) / LineHeight);
        bool show = point.x >= TimeWidth && point.x <= GutterWidth && index >= 0 && index < _lines.Count && _lines[index].Mod.Length > 18;
        _nameTip.gameObject.SetActive(show); if (show) _nameTip.text = _lines[index].Mod;
    }
    internal void HideName() => _nameTip.gameObject.SetActive(false);
    private ((int Line, int Char), (int Line, int Char)) Ordered() => _selectionStart.Line < _selectionEnd.Line || _selectionStart.Line == _selectionEnd.Line && _selectionStart.Char <= _selectionEnd.Char ? (_selectionStart, _selectionEnd) : (_selectionEnd, _selectionStart);
    private static string MiddleName(string name) => name.Length <= 18 ? name : name.Substring(0, 9) + "…" + name.Substring(name.Length - 7);
    private static string Escape(string value) => value.Replace("<", "＜");
    private static string Styled(string value)
    {
        string safe = Escape(value); int end = safe.IndexOf(']');
        if (safe.StartsWith("[", StringComparison.Ordinal) && end > 0)
        {
            bool time = safe.Length > 3 && char.IsDigit(safe[1]);
            string rest = safe.Substring(end + 1);
            if (time)
            {
                int levelEnd = rest.IndexOf(']');
                if (rest.TrimStart().StartsWith("[", StringComparison.Ordinal) && levelEnd >= 0)
                {
                    int sourceStart = levelEnd + 1;
                    while (sourceStart < rest.Length && rest[sourceStart] == ' ') sourceStart++;
                    if (sourceStart < rest.Length && rest[sourceStart] == '[') { int sourceEnd = rest.IndexOf(']', sourceStart); if (sourceEnd > sourceStart) rest = rest.Substring(0, sourceStart) + "<color=#7EBBC4>" + rest.Substring(sourceStart, sourceEnd - sourceStart + 1) + "</color>" + rest.Substring(sourceEnd + 1); }
                }
            }
            return "<color=" + (time ? "#899698" : "#7EBBC4") + ">" + safe.Substring(0, end + 1) + "</color>" + rest;
        }
        return safe;
    }
    private static float CharacterX(TextMeshProUGUI row, int character)
    {
        if (row.textInfo.characterCount == 0) return 0;
        if (character >= row.textInfo.characterCount) return row.textInfo.characterInfo[row.textInfo.characterCount - 1].xAdvance;
        return row.textInfo.characterInfo[Math.Max(0, character)].origin;
    }
    private void ToggleStack()
    {
        var lines = _selectionPinned ? _pinLines : _lines;
        if (_selectionEnd.Line < 0 || _selectionEnd.Line >= lines.Count) return;
        string record = lines[_selectionEnd.Line].Record;
        if (!_collapsedStacks.Add(record)) _collapsedStacks.Remove(record);
        Refilter();
    }
    private void CopySelection()
    {
        var lines = _selectionPinned ? _pinLines : _lines;
        if (!_hasSelection || lines.Count == 0) return; var (a, b) = Ordered(); var parts = new List<string>();
        for (int i = a.Line; i <= b.Line && i < lines.Count; i++) { string line = lines[i].Text; int start = i == a.Line ? Math.Min(a.Char, line.Length) : 0, end = i == b.Line ? Math.Min(b.Char, line.Length) : line.Length; parts.Add(line.Substring(start, Math.Max(0, end - start))); }
        GUIUtility.systemCopyBuffer = string.Join("\n", parts);
    }
    private void ShowExportOptions()
    {
        if (_options != null) Destroy(_options.gameObject);
        _options = OverviewUi.Panel("LogExportOptions", _panel, ModConfigLook.Surface(new Color(.12f, .16f, .18f, 1))); OverviewUi.Place(_options, 30, 140, Math.Min(830, _panel.rect.width - 60), 290);Near(_options,(RectTransform)_exportFolderButton.transform);
        _exportChoices.Clear();
        var choices = _tab == "custom" ? new[] { Loc.T("Export current custom log", "导出当前自定义日志", "匯出目前自訂日誌") }
            : new[] { Loc.T("BepInEx log", "BepInEx 日志", "BepInEx 日誌"), Loc.T("Game log", "游戏日志", "遊戲日誌"), Loc.T("Both logs", "两种日志", "兩種日誌") };
        for (int i = 0; i < choices.Length; i++) { int scope = i; var b = OverviewUi.Button(_options, choices[i], () => _scope = scope, 230, 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)b.transform, 14 + i * 245, 14, 230, 32); _exportChoices.Add(b); }
        var check = OverviewUi.Check(_options, _includeMetadata, value => _includeMetadata = value); OverviewUi.Place((RectTransform)check.transform, 14, 64, 28, 28);
        var label = OverviewUi.Text(_options, Loc.T("Include mod and configuration information", "附带模组与配置信息", "附帶模組與設定資訊"), 18); OverviewUi.Place(label.rectTransform, 50, 64, 500, 28);
        var tip = OverviewUi.Text(_options, Loc.T("For debugging and troubleshooting only: includes versions, mods, loaded/enabled states, applied configuration differences and pending restart items. Sensitive settings are redacted.", "仅用于 Debug 和故障排查：附带版本、模组列表、加载／启用状态、已应用配置差异和待重启项目；敏感配置值会遮盖。", "僅用於 Debug 和故障排查：附帶版本、模組清單、載入／啟用狀態、已套用設定差異和待重新啟動項目；敏感設定值會遮蔽。"), 14); OverviewUi.Place(tip.rectTransform, 14, 100, _options.rect.width - 28, 65); tip.textWrappingMode = TextWrappingModes.Normal;
        var hover = check.gameObject.AddComponent<LogHoverTip>(); hover.Text = tip;
        var labelHover = label.gameObject.AddComponent<LogHoverTip>(); label.raycastTarget = true; labelHover.Text = tip; tip.gameObject.SetActive(false);
        var note = OverviewUi.Text(_options, Loc.T("Exports the complete source, regardless of search filters. Log text is preserved.", "导出完整来源，不受搜索筛选影响。日志正文保留原文。", "匯出完整來源，不受搜尋篩選影響。日誌正文保留原文。"), 14); OverviewUi.Place(note.rectTransform, 14, 174, _options.rect.width - 28, 28);
        var save = OverviewUi.Button(_options, GamepadSupport.IsGamepad ? Loc.T("Export to the export folder", "导出到导出文件夹", "匯出到匯出資料夾") : Loc.T("Choose location and export", "选择位置并导出", "選擇位置並匯出"), BeginExport, 190, 34, UiActionRole.Commit); OverviewUi.Place((RectTransform)save.transform, 14, 235, 190, 34);
        var cancel = OverviewUi.Button(_options, Loc.T("Cancel", "取消", "取消"), () => { Destroy(_options.gameObject); _options = null; }, 100, 34); OverviewUi.Place((RectTransform)cancel.transform, 220, 235, 100, 34);

        foreach (var labelText in _options.GetComponentsInChildren<TextMeshProUGUI>(true)) labelText.text = DiagnosticUiLanguage.Text(labelText.text);
    }
    private void BeginExport()
    {
        if (_export != null) return;
        try
        {
            _exportStarted = DateTime.Now;
            string fileName = "PEAK日志_" + _exportStarted.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip";
            // 2.4.3: a dialog that cannot open no longer aborts the export; fall back to the fixed export folder and say so.
            string? fallbackNote = null;
            string? destination = null, pickerFailure = null;
            // 1.1.4 (gamepad review): the Win32 save dialog cannot be driven with a controller; use the fixed folder.
            if (GamepadSupport.IsGamepad) pickerFailure = "controller";
            if (pickerFailure != null || !LogSavePicker.TrySelect(fileName, out destination, out pickerFailure))
            {
                destination = LogSavePicker.FallbackPath(_repo.ConfigRootDirectory, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                fallbackNote = "fixed";
                Plugin.Log.LogWarning("[日志导出] 保存对话框不可用（" + pickerFailure + "），改存到 " + destination);
            }
            if (destination == null) return;
            JObject? metadata = _includeMetadata ? _repo.CaptureDiagnosticMetadata(_installedMods) : null;
            if (metadata != null) metadata["exportRequestedAt"] = _exportStarted;
            var sources = new List<LogExportSource>(); var custom = _custom;
            if (_tab == "custom")
            {
                if (custom == null) throw new IOException("未选择自定义日志来源。");
                // Provider snapshot is acquired on the worker, not on the Unity thread.
                sources.Add(new LogExportSource { Name = "自定义日志.log", Path = custom.File });
            }
            else
            {
                if (_scope is 0 or 2) sources.Add(new LogExportSource { Name = "BepInEx_LogOutput.log", Path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log") });
                if (_scope is 1 or 2) { string path = _tab == "game" ? _path : GamePaths().FirstOrDefault(File.Exists) ?? GamePaths().First(); sources.Add(new LogExportSource { Name = "PEAK_" + Path.GetFileName(path), Path = path }); }
            }
            _exportDestination = destination; _exportSucceeded = false;
            _export = Task.Run(() => { if (custom != null && custom.File.Length == 0) sources[0].Records = MceLogApi.Read(custom); LogExport.Save(destination, sources, metadata); });
            _exportFallbackNote = fallbackNote;
            _error = "正在后台导出……"; if (_options != null) Destroy(_options.gameObject); _options = null;
        }
        catch (Exception e) { _error = "导出失败：" + e.Message; }
    }
    private void OnDestroy() { var reader = _reader; if (_read == null) reader?.Dispose(); else _ = _read.ContinueWith(_ => reader?.Dispose()); }
}
internal sealed class LogTextSelection : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler, IScrollHandler
{
    internal LogWindow Owner = null!;
    internal bool Pinned = false;
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.SelectAt(e, true, Pinned); }
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.SelectAt(e, false, Pinned); }
    public void OnEndDrag(PointerEventData e) => Owner.EndSelection();
    public void OnPointerUp(PointerEventData e) => Owner.EndSelection();
    public void OnScroll(PointerEventData e) => Owner.UserScroll();
}
internal sealed class LogLevelWheel : MonoBehaviour, IScrollHandler
{
    internal Action<int>? Changed;
    public void OnScroll(PointerEventData e){if(Math.Abs(e.scrollDelta.y)>.01f){Changed?.Invoke(e.scrollDelta.y>0?-1:1);e.Use();}}
}
internal sealed class LogNameHover : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler
{
    internal LogWindow Owner = null!;
    public void OnPointerMove(PointerEventData e) => Owner.HoverName(e);
    public void OnPointerExit(PointerEventData e) => Owner.HideName();
}
internal sealed class LogScrollbarInput : MonoBehaviour, IPointerDownHandler, IBeginDragHandler
{
    internal LogWindow Owner = null!;
    public void OnPointerDown(PointerEventData e) => Owner.UserScroll();
    public void OnBeginDrag(PointerEventData e) => Owner.UserScroll();
}
internal sealed class LogHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal TextMeshProUGUI Text = null!;
    public void OnPointerEnter(PointerEventData e) => Text.gameObject.SetActive(true);
    public void OnPointerExit(PointerEventData e) => Text.gameObject.SetActive(false);
}

/// <summary>Controller reading without forwarding mouse drags, which belong to log text selection.</summary>
internal sealed class LogReadingSurface : ControllerReadingSurface { }
