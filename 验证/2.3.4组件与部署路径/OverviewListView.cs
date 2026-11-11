#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewListView : IDisposable
{
    private readonly OverviewRepository _repo;
    private readonly Action<string> _status;
    private readonly Action _headersChanged;
    private readonly RectTransform _root, _header, _viewport, _content, _tools;
    private readonly ScrollRect _scroll;
    private readonly OverviewCascade _cascade;
    private readonly OverviewListPump _pump;
    private readonly List<OverviewMod> _mods = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ThunderstoreUpdateService _updates;
    private readonly RectTransform _hint;
    private readonly TextMeshProUGUI _hintText;
    private readonly HintLru<string,float> _hintMetrics=new(64,StringComparer.Ordinal);
    private readonly Dictionary<OverviewMod,int> _displayNumbers=new();
    private readonly Camera? _eventCamera;
    private Vector2 _lastHintPointer=new(float.NaN,float.NaN);
    private Vector2 _lastHintRootSize;
    private int _hintMetricRevision=-1;
    private TMP_FontAsset? _hintMetricFont;
    private float _hintMetricWidth=-1,_hintMetricSize=-1;
    private readonly OverviewGroupEditor _groupEditor;
    private readonly Action<OverviewMod?>? _openSettings;
    private readonly OverviewDependencyHints _dependencyHints;
    private int _languageRevision = -1;
    private string _shownHint = "";
    private readonly HoverDwell _hintDwell=new();
    private readonly DeferredHint _factoryHint=new();
    private float _warmAfter, _warmFirstAt;
    private int _warmSettledFrames;
    private int _warmRow;
    private bool _warming, _arrowWarmed;
    private Vector2 _warmPointer;
    private int _warmInventory=-1,_warmContent=-1,_warmLanguage=-1;
    private ModConfigEnhance.Updating.ComponentDependencyGraph? _warmGraph;
    private void WarmVisibleHints()
    {
        if(_warming || !_opened || _residentPending || _hint.gameObject.activeSelf || OverviewUi.HasModal || _building || _dragged!=null || _scroll.velocity.sqrMagnitude>1 || _cascade.IsAnimating)return;
        var pointer=PointerPosition;
        if(pointer!=_warmPointer) { _warmPointer=pointer;_warmAfter=Time.unscaledTime+.25f;return; }
        if(Time.unscaledTime<_warmFirstAt || Time.unscaledTime<_warmAfter || Time.unscaledDeltaTime>.025f || _infrastructureLoading)return;
        if(++_warmSettledFrames<30)return;
        if(_warmInventory!=OverviewPageCache.Inventory || _warmContent!=OverviewPageCache.Content || _warmLanguage!=Loc.TranslationRevision || !ReferenceEquals(_warmGraph,SourceUi.DisplayDependencyGraph))
        {
            _warmRow=0;_warmInventory=OverviewPageCache.Inventory;_warmContent=OverviewPageCache.Content;
            _warmLanguage=Loc.TranslationRevision;_warmGraph=SourceUi.DisplayDependencyGraph;
        }
        _warmAfter=Time.unscaledTime+.05f;
        _warming=true;
        try
        {
            if(!_arrowWarmed) { _arrowWarmed=true;OverviewDependencyHints.TreeArrow();return; }
            if(_dependencyHints.HasWarmWork) { _dependencyHints.TickPrewarm();return; }
            // At most one live row's content is prepared in a frame. Cold native work
            // cannot be interrupted; diagnostics measure the real cost rather than promise a hard budget.
            if(_virtualRows.Count==0)return;
            for(;_warmRow<_virtualRows.Count;)
            {
                var row=_virtualRows[_warmRow++];
                if(row.View==null || !row.View.gameObject.activeInHierarchy)continue;
                if(_dependencyHints.Prewarm(row.View))break;
            }
        }
        finally { _warming=false; }
    }
    private ProfilePendingState? _markerState;
    private float _markerStateAt=-1;
    private int _markerStateRevision=-1;
    private ProfilePendingState? MarkerState()
    {
        // Follows the repository's invalidation revision rather than a short timer: a 0.19s window expired on
        // every maintenance sweep, so the projection was rebuilt in lockstep with the sweep.
        if(_markerState==null || _markerStateRevision!=_repo.PendingViewRevision || Time.unscaledTime>=_markerStateAt)
        { _markerState=PendingState?.Invoke();_markerStateRevision=_repo.PendingViewRevision;_markerStateAt=Time.unscaledTime+2f; }
        return _markerState;
    }
    private float _hintHeight, _hintWidth=-1, _hintFontSize=-1;
    private int _hintLanguage=-1;
    private TMP_FontAsset? _hintFont;
    private string _measuredHint="";
    private readonly RectTransform _dragGhost, _dropLine;
    private readonly TextMeshProUGUI _dragCaption;
    private readonly Dictionary<RectTransform, (string Group, OverviewMod? Before)> _dropAreas = new();
    private List<OverviewMod> _visible = new();
    private OverviewMod? _dragged;
    private int _anchor = -1;
    private bool _authorMode, _checking, _disposed, _building;
    private Task? _checkTask;
    private Task? _tagTask;
    private bool _syncingTags;
    private CancellationTokenSource? _round;
    private bool _opened, _reopenPending;
    private string _renderedUpdates = "";
    private long _seenUpdateCacheRevision = -1;
    private bool _cachedUpdatesNeedRender;
    private readonly CoalescedProgress _checkProgress=new();
    private string _checkDisplayContext="";
    public string PackLatestVersion { get; private set; } = "";
    public string PackCheckMessage { get; private set; } = "";
    public string PackUpdateNotes { get; private set; } = "";
    private static string T(string en, string zh) => Loc.T(en, zh, zh);
    private float _lastWidth;
    private const float RowHeight = 38;
    private const float GroupHeight = 32, GroupGapBefore = 4, GroupGapAfter = 2;
    public GameObject Root => _root.gameObject;
    public IReadOnlyList<OverviewMod> Mods => _mods;
    internal Func<ProfilePendingState>? PendingState;
    private OverviewTagSelection Tags => _repo.State.modTagFilter ??= new();
    internal void RefreshTagFilter() { _relatedMods = null; Render(); }
    internal float ScrollPosition => _scroll.verticalNormalizedPosition;
    internal void RestoreScrollPosition(float position) { _scroll.verticalNormalizedPosition = position; _pump.Enqueue(() => _scroll.verticalNormalizedPosition = position); }
    public bool IsEditingText
    {
        get
        {
            if (_root.GetComponentsInChildren<TMP_InputField>(false).Any(i => i.isFocused)) return true;
            var eventSystem = EventSystem.current; var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            return input != null && input.isFocused;
        }
    }

    public OverviewListView(Transform parent, OverviewRepository repo, Action<string> status, Action headersChanged, Action<OverviewMod?>? openSettings = null)
    {
        _repo = repo; _status = status; _headersChanged = headersChanged; _openSettings = openSettings;
        var canvas=parent.GetComponentInParent<Canvas>()?.rootCanvas;
        _eventCamera=canvas!=null && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        _updates = new ThunderstoreUpdateService(repo);
        _root = OverviewUi.Rect("OverviewModList", parent); OverviewUi.Fill(_root);
        _tools = OverviewUi.Rect("ListTools", _root);
        _header = OverviewUi.Panel("FixedHeader", _root, new Color(0.12f, 0.13f, 0.15f, .92f));
        RectTransform scrollRoot = OverviewUi.Rect("ModScroll", _root);
        scrollRoot.anchorMin = Vector2.zero; scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = new Vector2(0, 0); scrollRoot.offsetMax = new Vector2(0, -72);
        _scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        _viewport = OverviewUi.Panel("Viewport", scrollRoot, new Color(0, 0, 0, 0.01f));
        OverviewUi.Fill(_viewport); _viewport.offsetMax = new Vector2(-15, 0);
        _viewport.gameObject.AddComponent<RectMask2D>();
        _content = OverviewUi.Rect("Rows", _viewport);
        _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0, 1);
        _content.anchoredPosition = Vector2.zero;
        _scroll.viewport = _viewport; _scroll.content = _content; _scroll.horizontal = false; _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40f;
        RectTransform bar = OverviewUi.Panel("Scrollbar", scrollRoot, new Color(.1f, .1f, .1f, .5f));
        bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, 1); bar.offsetMin = new Vector2(-12, 0); bar.offsetMax = Vector2.zero;
        RectTransform handle = OverviewUi.Panel("Handle", bar, OverviewUi.Muted); OverviewUi.Fill(handle);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
        _scroll.verticalScrollbar = scrollbar;
        _cascade = _root.gameObject.AddComponent<OverviewCascade>();
        _hint = OverviewUi.Panel("FullTextHint", parent, new Color(.08f, .08f, .1f, .98f));
        HintCanvas.Attach(_hint,false);
        _hintText = OverviewUi.Text(_hint, "", 20); OverviewUi.Fill(_hintText.rectTransform, 12);
        _hintText.textWrappingMode = TextWrappingModes.Normal; _hintText.raycastTarget = false;
        _hint.GetComponent<Image>().raycastTarget = false; _hint.gameObject.SetActive(false);
        _dragGhost = OverviewUi.Panel("DragPreview", _root, new Color(.12f, .26f, .31f, .92f)); _dragGhost.GetComponent<Image>().raycastTarget = false;
        _dragCaption = OverviewUi.Text(_dragGhost, "", 20); OverviewUi.Fill(_dragCaption.rectTransform, 8); _dragGhost.gameObject.SetActive(false);
        _dropLine = OverviewUi.Panel("DropLine", _content, OverviewUi.Blue); _dropLine.GetComponent<Image>().raycastTarget = false; _dropLine.gameObject.SetActive(false);
        _dependencyHints = new OverviewDependencyHints((RectTransform)parent, repo, () => _mods, GetDisplayNumber, HideExternalHint);
        _pump = parent.gameObject.AddComponent<OverviewListPump>();
        _groupEditor = new OverviewGroupEditor(repo, _mods, status, () => { Save(); Render(); _headersChanged(); });
        _pump.Tick = () => {
            // The host may remain alive with another UI page selected. Drain completions only; no hidden-page ticks.
            if (!ListVisible) return;
            if(OverviewUi.HasModal)HideAllHints();
            else if(_factoryHint.Take(Time.unscaledTime,target=>target is GameObject go && go!=null && go.activeInHierarchy) is string generated) ShowHint(generated);
            else if(_hintDwell.Take(Time.unscaledTime) is string ready) ShowHint(ready);
            if (_hint.gameObject.activeSelf) PositionHint();
            if (ListVisible) SourceUi.PollDisplayContext();
            if (ListVisible && _checking && _checkDisplayContext!=SourceUi.DisplayDependencyContext)
            { _round?.Cancel();_checkProgress.Clear(); }
            if (ListVisible && _checking && _checkDisplayContext==SourceUi.DisplayDependencyContext && _checkProgress.Take(Time.unscaledTime) is string progress) _status(progress);
            if (_opened) RefreshInfrastructure();
            FlushOverviewSearch();
            FlushInfrastructureRedraw();
            if (_opened) RefreshVirtualRows();
            if (_opened) RefreshSharedUpdateCache();
            WarmVisibleHints();
            float width = Width;
            EnsureFixedChrome();
            if (ListVisible && !_building && (Math.Abs(width - _lastWidth) > 2 || _languageRevision != OverviewLanguageBridge.Revision)) Render();
        };
        EnsureFixedChrome();
        ShowInitialReadState();
        // 构造期不读数据：页面进入时 OnPageEnter 会统一加载一次，否则首次打开要扫描并建行两遍。
    }
    private float _fixedChromeWidth=-1, _fixedGoalWidth=60;
    private int _fixedChromeLanguage=-1;
    private bool _fixedChromeAuthor;
    private TextMeshProUGUI? _initialReadState;
    private Button? _compatibilityAdvice;
    private const string EnablementNote = "Checkboxes edit the configuration draft. Save configuration to commit; loaded mod changes take effect after restarting.";
    private string EnablementHint => Loc.T(EnablementNote, "勾选先修改草稿，点击“保存配置”提交；已加载模组的启停在重启游戏后生效。", "勾選先修改草稿，點擊「儲存設定」提交；已載入模組的啟停於重新啟動遊戲後生效。");
    private void ShowInitialReadState()
    {
        if(_virtualRows.Count>0)return;
        if(_initialReadState==null)_initialReadState=Label(_content, Loc.T("Reading mods and components…", "正在读取模组与组件……", "正在讀取模組與組件……"),20,15,Width-50,45,24);
        _content.sizeDelta=new Vector2(0,70);
    }
    private void EnsureFixedChrome()
    {
        float width=Width;
        int language=OverviewLanguageBridge.Revision;
        if(width==_fixedChromeWidth && _authorMode==_fixedChromeAuthor && language==_fixedChromeLanguage)return;
        _fixedChromeWidth=width;_fixedChromeAuthor=_authorMode;_fixedChromeLanguage=language;
        Clear(_tools);Clear(_header);_compatibilityAdvice=null;
        _tools.gameObject.SetActive(true);OverviewUi.Place(_tools,0,0,width,32);
        BuildOverviewSearch(width);
        string enablementNote=EnablementHint;
        var note=Label(_tools,enablementNote,SearchWidth+18,0,Mathf.Max(40,width-SearchWidth-218),32,15);note.color=OverviewUi.Muted;AddHint(note.gameObject,enablementNote);
        OverviewUi.Place(_header,0,33,width,39);
        string goalHeading = Loc.T("Enable mod", "启用", "啟用");
        _fixedGoalWidth=Mathf.Max(60,Measure(goalHeading,18)+16);
        const float indexWidth = 48, versionWidth = 110;
        float goalWidth = _fixedGoalWidth;
        const float stateWidth = 0; // loading state is encoded by the enable checkbox border
        float xEnable = 8, xIndex = xEnable + goalWidth, xName = xIndex + indexWidth;
        bool compact = width < 1000;
        float authorWidth = compact ? 0 : Mathf.Clamp(width * .12f, 90, 180);
        float iconWidth = 43;
        float nameWidth = Mathf.Max(80, Mathf.Min(ModNameLayout.ColumnWidth(width), width - xName - stateWidth - iconWidth * (_authorMode ? 4 : 3) - versionWidth - authorWidth - (compact ? 20 : 85)));
        float xState = xName + nameWidth, xSettings = xState + stateWidth, xHome = xSettings + iconWidth, homeWidth = 43;
        float xFolder = xHome + homeWidth, folderWidth = 43;
        float xCheck = xFolder + folderWidth, checkWidth = _authorMode ? 43 : 0, xVersion = xCheck + checkWidth;
        float xAuthor = xVersion + versionWidth, xDescription = xAuthor + authorWidth;
        var goalHeader = Label(_header, goalHeading, xEnable, 4, goalWidth, 30, 18); goalHeader.alignment = TextAlignmentOptions.Midline;
        AddHint(goalHeader.gameObject, enablementNote); Label(_header, "#", xIndex, 4, indexWidth, 30, 20);
        var settingsHeader = OverviewUi.IconButton(_header, "gear", () => _openSettings?.Invoke(null), 30, 32); OverviewUi.Place((RectTransform)settingsHeader.transform, xSettings + 2, 2, 30, 32); AddHint(settingsHeader.gameObject, T("Mod settings column. Click to open the general mod settings page.", "模组设置列，点击跳转到模组总设置页"));
        var homeHeader = OverviewUi.IconButton(_header, "home", OpenEnhanceHomepage, 30, 32); Tint(homeHeader, OverviewUi.Ivory); OverviewUi.Place((RectTransform)homeHeader.transform, xHome + (homeWidth - 9 - 30) / 2, 2, 30, 32); AddHint(homeHeader.gameObject, T("Mod homepage column. Click to open the Enhance homepage.", "模组主页列，点击跳转到Enhance主页")); Label(_header, T("Name", "名称"), xName, 4, nameWidth, 30, 21);
        var folderHeader = OverviewUi.IconButton(_header, "folder", () => OpenFolder(BepInEx.Paths.PluginPath), 30, 32); Tint(folderHeader, OverviewUi.Ivory); OverviewUi.Place((RectTransform)folderHeader.transform, xFolder + (folderWidth - 9 - 30) / 2, 2, 30, 32); AddHint(folderHeader.gameObject, T("Mod folder column. Click to open the plugins folder.", "模组文件夹列，点击打开 plugins 文件夹"));
        if (_authorMode) BuildUpdateHeader(_header, xCheck, checkWidth);
        Label(_header, T("Version", "版本"), xVersion, 4, versionWidth, 30, 21);
        if (!compact) { Label(_header, T("Author", "作者"), xAuthor, 4, authorWidth, 30, 21); Label(_header, T("Description", "说明"), xDescription, 4, Mathf.Max(50, width - xDescription - 20), 30, 21); }
        float[] edges = (_authorMode ? new[] { xEnable, xIndex, xName, xState, xSettings, xHome, xFolder, xCheck, xVersion, xAuthor, xDescription, width - 15 } : new[] { xEnable, xIndex, xName, xState, xSettings, xHome, xFolder, xVersion, xAuthor, xDescription, width - 15 }).Distinct().ToArray();
        for (int c = 0; c < edges.Length - 1; c++) { var line = OverviewUi.Panel("ColumnLine", _header, new Color(.77f, .76f, .66f, .8f)); line.GetComponent<Image>().raycastTarget = false; OverviewUi.Place(line, edges[c], 37, Mathf.Max(2, edges[c + 1] - edges[c] - 9), 1.5f); }
    }
    private System.Collections.Generic.List<PackConflictMatch> _currentConflicts=new();
    private void RefreshCompatibilityAdvice(System.Collections.Generic.List<PackConflictMatch> conflicts)
    {
        _currentConflicts=conflicts;
        int activeAdvice=conflicts.Count(c=>!c.Ignored);
        if(_compatibilityAdvice!=null) { _compatibilityAdvice.gameObject.SetActive(false);UnityEngine.Object.Destroy(_compatibilityAdvice.gameObject);_compatibilityAdvice=null; }
        if(activeAdvice==0)return;
        _compatibilityAdvice=OverviewUi.Button(_tools,Loc.F("Compatibility · {0}","冲突建议 · {0}","衝突建議 · {0}",activeAdvice),()=> { HideAllHints();PackConflictUi.Show(_repo,conflicts,()=> { Render();_headersChanged(); }); },172,26,UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)_compatibilityAdvice.transform,Width-192,0,172,26);UiActionStyle.Tint(_compatibilityAdvice,PackConflictUi.AdviceColor);
    }
    private float Width => Mathf.Max(700, _root.rect.width);
    private void RefreshSharedUpdateCache()
    {
        long revision = ThunderstoreUpdateService.CacheRevision;
        if (revision != _seenUpdateCacheRevision)
        {
            _updates.ApplyCachedCategories(_mods); _seenUpdateCacheRevision = revision;
            _cachedUpdatesNeedRender = true;
        }
        if (!ListVisible || !_cachedUpdatesNeedRender || _infrastructureLoading || _building || IsEditingText || _dragged != null || _scroll.velocity.sqrMagnitude > 1) return;
        _cachedUpdatesNeedRender = false; if(_renderedUpdates!=UpdateDisplayKey()) { RefreshUpdateRows(); _headersChanged(); }
    }
    public void SetAuthorMode(bool value) { _authorMode = value; _repo.SetAuthorMode(value); Render(); }
    public void SetHideDisabled(bool value) { _repo.State.hideDisabled = value; _repo.SaveUiState(); Render(); }
    public void Reload()
    {
        try
        {
            SourceUi.PollDisplayContext(true);
            SharedRelationshipSnapshot.InvalidateDisplay();
            _mods.Clear(); _mods.AddRange(new OverviewCatalogue().Scan(_repo)); _dependencyHints.Reload();
            RefreshInfrastructure(true);
            _updates.ApplyCachedCategories(_mods);
            _selected.RemoveWhere(id => !_mods.Any(m => m.Id == id));
            // The component result commits this generation once. Keep existing rows until then.
            _anchor = -1; _status(Loc.T("Reading components…", "正在读取组件……", "正在讀取組件……")); _headersChanged();
        }
        catch (Exception ex) { _status(T("Could not read mods: ", "读取模组失败：") + Loc.LocalizeMessage(ex.Message)); }
    }
    public void OnOpened(bool automaticCheck = true)
    {
        if (_disposed) return; SourceUi.PollDisplayContext(true); EnsureFixedChrome(); _opened = true; _cascade.Resume(); _warmSettledFrames=0;_warmFirstAt=Time.unscaledTime+3; RefreshVirtualRows();
        RefreshInfrastructure();
        if (_renderedUpdates.Length > 0 && _renderedUpdates != UpdateDisplayKey()) RefreshUpdateRows();
        // A completed resident generation is not animated/loaded again on re-entry.
        if(_residentPending && !_cascade.HasSequence)PlayAppearance();
        if (_checking) { _reopenPending = true; return; }
        // Automatic checks are owned by the scene-gated process-wide scheduler, not page entry.
    }
    internal void OnTabHidden() { _layerFocus=null; _opened=false; _cascade.PauseForExit(); HideAllHints(); EndDragFeedback(); }
    public void OnClosed() { _layerFocus=null; _cascade.PauseForExit(); _opened = false; _reopenPending = false; _round?.Cancel(); _checkProgress.Clear(); HideAllHints(); _groupEditor.Close(); EndDragFeedback(); }
    /// <summary>由概览页提供：当前向 ModConfig 提供了设置项的插件 GUID。空集合表示未知，不置灰。</summary>
    internal Func<HashSet<string>>? SettingsOwners;
    internal void PlayAppearance() => _cascade.Play(_scroll, AppearanceRows);
    private IEnumerable<OverviewCascade.Row> AppearanceRows()
    {
        foreach (var row in _virtualRows)
            if (row.View != null && row.Layers == null) yield return new OverviewCascade.Row(row.View);
    }
    private bool AnyUpdateAllowed => _repo.GetGlobalUpdateAllowed() || _mods.Any(m => _repo.IsUpdateAllowed(m.Rule)) || (_repo.IsPackUpdateAllowed(_repo.State.pack) && ThunderstoreUpdateService.ValidPart(_repo.State.pack.sourceNamespace) && ThunderstoreUpdateService.ValidPart(_repo.State.pack.sourceName));
    public void CheckUpdates(bool force = false)
    {
        if (_disposed || _checking || !_opened) return;
        if (!AnyUpdateAllowed) { _status(T("Update checks are not permitted.", "当前没有允许检查更新的项目。")); return; }
        _round?.Dispose(); _round = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _checkDisplayContext=SourceUi.DependencyContext; _checkProgress.Clear(); _checking = true; _status(T("Checking for updates…", "正在检查更新……"));
        _checkTask = RunChecks(force);
    }
    private async Task RunChecks(bool force)
    {
        ThunderstoreUpdateService? fresh = null;
        string context=_checkDisplayContext;
        (string Id, string Name)[] progressGroups = Array.Empty<(string, string)>();
        bool checkStarted = false;
        ThunderstoreUpdateService service = _updates;
        string ReportText()
        {
            if (!checkStarted) return "";
            var report = service.LastReport;
            return FormatCheckProgress(report.Summary, progressGroups,
                id => report.Groups.TryGetValue(id, out var count) ? (count[0], count[1]) : null, completed: true);
        }
        try
        {
            progressGroups = CaptureCheckProgressGroups(
                new GroupTree(_repo.State.groups) is var progressTree ? progressTree.Flat.Select(g => (g.id, progressTree.Path(g.id), progressTree.Position(g.id))) : Enumerable.Empty<(string, string, int)>(),
                _mods.Select(m => m.Rule.groupId), InfrastructureOverviewModel.GroupTitle, T("Ungrouped", "未分组"));
            service = force ? fresh = new ThunderstoreUpdateService(_repo, TimeSpan.Zero) : _updates;
            CancellationToken token = _round!.Token;
            var mods=_mods.ToArray();var packModel=_repo.State.pack;
            // Names/order were captured before Task.Run; filtering is display-only.
            // Loaded Unity metadata was captured by Reload on the main thread. The service's
            // synchronous pre-await inventory work must not block this UI entry point.
            await Task.Run(()=> { checkStarted = true; return service.CheckAsync(mods, token, (done, total) => {
                if (!token.IsCancellationRequested)
                {
                    var snapshot = service.LastProgress;
                    _checkProgress.Publish(FormatCheckProgress(snapshot.Summary, progressGroups, id => snapshot.Groups.TryGetValue(id, out var count) ? (count[0], count[1]) : null));
                }
            }); },token).ConfigureAwait(false);
            var pack = await service.CheckPackAsync(packModel, token).ConfigureAwait(false);
            _pump.Enqueue(() => {
                if (_disposed || !_opened || token.IsCancellationRequested || context!=SourceUi.DependencyContext) return;
                _checkProgress.Clear();
                bool packChanged = pack != null && (PackLatestVersion != pack.Version || PackUpdateNotes != pack.Notes || (pack.Error.Length > 0 && PackCheckMessage != pack.Error));
                if (pack != null) { PackLatestVersion = pack.Version; PackUpdateNotes = pack.Notes; PackCheckMessage = string.IsNullOrWhiteSpace(pack.Error) ? T("Store version checked. Pack author has not confirmed this upgrade.", "已查询商店版本，整合包作者尚未确认建议升级。") : pack.Error; }
                var report = service.LastReport;
                _status(FormatCheckProgress(report.Summary, progressGroups, id => report.Groups.TryGetValue(id, out var count) ? (count[0], count[1]) : null, completed: true)
                    + (pack?.Error.Length > 0 ? " · " + SingleLine(Loc.LocalizeMessage(pack.Error)) : ""));
                bool rowsChanged = _renderedUpdates != UpdateDisplayKey();
                if (rowsChanged) RefreshUpdateRows();
                if (rowsChanged || packChanged) _headersChanged();
            });
        }
        catch (OperationCanceledException)
        {
            string result = Loc.T("Update check cancelled.", "更新检查已取消。", "更新檢查已取消。") + " · " + ReportText();
            _pump.Enqueue(() => { if (!_disposed && _opened && context==SourceUi.DependencyContext) _status(result); });
        }
        catch (Exception ex)
        {
            string result = T("Update check failed: ", "更新检查失败：") + SingleLine(Loc.LocalizeMessage(ex.Message)) + " · " + ReportText();
            _pump.Enqueue(() => { if (!_disposed && _opened && context==SourceUi.DependencyContext) _status(result); });
        }
        finally { fresh?.Dispose(); _pump.Enqueue(() => { _checkProgress.Clear(); _checking = false; bool retry = _reopenPending; _reopenPending = false; if (retry && _opened && !_disposed) { _updates.ApplyCachedCategories(_mods); if(_renderedUpdates!=UpdateDisplayKey()) RefreshUpdateRows(); } }); }
    }
    internal void SyncCategories(Action<string> completed)
    {
        if (_disposed) return;
        if (_syncingTags) { completed(T("Store tags are already being read.", "商店标签正在读取中。")); return; }
        var eligible = _mods.Where(mod => mod.SourceConfirmed).ToArray();
        if (eligible.Length == 0) { completed(T("No confirmed store sources. Cached tags remain available.", "没有已确认的商店来源，仍可使用缓存标签。")); return; }
        _syncingTags = true; _tagTask = RunTagSync(eligible, completed);
    }
    private async Task RunTagSync(IReadOnlyList<OverviewMod> mods, Action<string> completed)
    {
        try
        {
            using var service = new ThunderstoreUpdateService(_repo, TimeSpan.Zero);
            await service.SyncCategoriesAsync(mods, _lifetime.Token, (done, total) => _pump.Enqueue(() => {
                if (!_disposed && _opened) _status(T("Reading store tags ", "读取商店标签 ") + done + "/" + total);
            })).ConfigureAwait(false);
            _pump.Enqueue(() => {
                if (_disposed) return;
                _updates.ApplyCachedCategories(_mods); Render(); _headersChanged();
                int known = _mods.Count(mod => mod.CategoriesKnown);
                string message = T("Cached tags: ", "已缓存标签：") + known + "/" + _mods.Count + T("; unknown sources remain under Unknown.", "；其余归入“标签未知”。");
                _status(message); completed(message);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _pump.Enqueue(() => { if (!_disposed) { string message = T("Tag read failed: ", "标签读取失败：") + Loc.LocalizeMessage(ex.Message); _status(message); completed(message); } }); }
        finally { _pump.Enqueue(() => _syncingTags = false); }
    }
    private string UpdateDisplayKey()
    {
        var mods = _mods.Select(m => string.Join("\t", new[] {
            m.Id, m.LatestVersion, m.HasUpdate.ToString(), m.CheckMessage, m.UpdateNotes, m.Author,
            m.HomepageUrl, m.CategoriesKnown.ToString(), string.Join("|", m.Categories),
            m.SourceKind.ToString(),m.SourceConfirmed.ToString(),m.SourceKey,m.IdentityConflict
        }));
        var components = InfrastructurePackages.Results.Select(p => "component\t" + p.Key + "\t" + p.Version + "\t" + p.Latest)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        return string.Join("\n", mods.Concat(components));
    }

    private void Save()
    {
        try { _repo.Save(); } catch (Exception ex) { _status(T("Save failed: ", "保存失败：") + Loc.LocalizeMessage(ex.Message)); }
    }
    private static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        { GameObject child = parent.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
    }
    private void Render()
    {
        // All structural requests (including resize/filter/language) coalesce while scanning.
        if (_disposed || _infrastructureLoading || !_cascade.BeforeRebuild()) return;
        _infrastructureRedraw = false;  _markerState=null;_building = true;
        var oldFocus=CaptureRowFocus();
        float oldPixelOffset = Mathf.Max(0, _content.anchoredPosition.y);
        bool cleared = false;
        try
        {
            RefreshRowData(); InvalidateGroupTree();
            float width = Width; _lastWidth = width; _languageRevision = OverviewLanguageBridge.Revision;
            _scroll.StopMovement();
            var eventSystem = EventSystem.current; var oldSelection = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (oldSelection != null && oldSelection.transform.IsChildOf(_content)) eventSystem!.SetSelectedGameObject(null);
            HideAllHints(); _dependencyHints.InvalidateDisplay(); _warmRow=0;_warmAfter=Time.unscaledTime+.5f; EndDragFeedback(); _dropAreas.Clear();
            // Keep the owned insertion marker out of the row-destruction pass.
            _conflictRefresh.Clear();ResetVirtualRows(); _dropLine.SetParent(_root, false); Clear(_content); _dropLine.SetParent(_content, false);
            cleared = true;
            EnsureFixedChrome();
            var conflicts = PackConflictService.Evaluate(_repo, _mods).Where(c => c.Any).ToList();
            RefreshCompatibilityAdvice(conflicts);
            HashSet<string> configurable = SettingsOwners?.Invoke() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var layoutRows = _mods.Where(m => MatchesRelated(m) && MatchesOverviewSearch(m) && (!_repo.State.hideDisabled || m.TargetEnabled || m.Loaded || _repo.PlannedEnabled(m)) && Tags.Matches(m, MatchesStatus) && (_sourceFilter < 0 || (int)m.SourceKind == _sourceFilter)).OrderBy(m => GroupSortKey(m.Rule.groupId)).ThenBy(m => m.Rule.order).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _visible = layoutRows.Where(m => _relatedMods != null || Tags.Count > 0 || _searchQuery.Length > 0 || !RowHiddenByCollapse(m.Rule.groupId)).ToList();
            _displayNumbers.Clear();for(int i=0;i<_visible.Count;i++)_displayNumbers[_visible[i]]=i+1;
            const float indexWidth = 48, versionWidth = 110;
            float goalWidth = _fixedGoalWidth;
            const float stateWidth = 0; // loading state is encoded by the enable checkbox border
            float xEnable = 8, xIndex = xEnable + goalWidth, xName = xIndex + indexWidth;
            bool compact = width < 1000;
            float authorWidth = compact ? 0 : Mathf.Clamp(width * .12f, 90, 180);
            float iconWidth = 43;
            float nameWidth = Mathf.Max(80, Mathf.Min(ModNameLayout.ColumnWidth(width), width - xName - stateWidth - iconWidth * (_authorMode ? 4 : 3) - versionWidth - authorWidth - (compact ? 20 : 85)));
            float xState = xName + nameWidth, xSettings = xState + stateWidth, xHome = xSettings + iconWidth, homeWidth = 43;
            float xFolder = xHome + homeWidth, folderWidth = 43;
            float xCheck = xFolder + folderWidth, checkWidth = _authorMode ? 43 : 0, xVersion = xCheck + checkWidth;
            float xAuthor = xVersion + versionWidth, xDescription = xAuthor + authorWidth;
            float y = BuildInfrastructure(0, width, xEnable, goalWidth, xName, nameWidth, xSettings, xHome, xFolder, xVersion, versionWidth, xAuthor, authorWidth, xDescription, compact); int index = 0;
            var headers = HeaderOrder(layoutRows); int nextHeader = 0;
            bool collapseApplies = _relatedMods == null && Tags.Count == 0 && _searchQuery.Length == 0;
            void EmitHeader(string id)
            {
                // A header under a collapsed ancestor is folded away with its parent.
                if (collapseApplies && Groups.HeaderHidden(id, _repo.State.collapsedGroups)) return;
                int depth = Math.Max(1, Groups.Depth(id));
                y += y > 0 ? GroupGapBefore : 2; BuildGroupHeader(id, y, width, layoutRows); y += HeaderHeight(depth) + GroupGapAfter;
            }
            void EmitHeadersThrough(string id)
            {
                string key = Groups.Contains(id) ? id : "";
                int at = headers.IndexOf(key, nextHeader);
                if (at < 0) return;
                for (; nextHeader <= at; nextHeader++) EmitHeader(headers[nextHeader]);
            }
            foreach (OverviewMod mod in layoutRows)
            {
                string groupId = mod.Rule.groupId;
                EmitHeadersThrough(groupId);
                if (collapseApplies && RowHiddenByCollapse(groupId)) continue;
                int capturedIndex=index++;
                float rowY=y;
                System.Collections.IEnumerator BuildModLayers(RectTransform row)
                {
                _dropAreas[row] = (groupId, mod);
                var enablement=row.gameObject.AddComponent<OverviewEnablementBinding>();
                enablement.Mod=mod;enablement.Target=()=>_repo.PlannedEnabled(mod);
                DrawRails(row, groupId, RowHeight);
                var interaction = row.GetComponent<OverviewRowInteraction>() ?? row.gameObject.AddComponent<OverviewRowInteraction>();
                interaction.DragStateChanged=()=>_warmAfter=Time.unscaledTime+.25f;
                interaction.NativeScroll = _scroll; interaction.ForwardScroll = () => !_authorMode;
                interaction.Clicked = data => Select(mod, capturedIndex, data);
                interaction.DragStarted = () => { if (_authorMode) BeginDragFeedback(mod); };
                interaction.DragMoved = MoveDragFeedback;
                interaction.Dropped = () => MoveSelected(groupId, mod);
                interaction.DragFinished = EndDragFeedback;
                bool ownMod = string.Equals(mod.Guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase);
                Label(row, (capturedIndex+1).ToString(), xIndex, 4, indexWidth, 32, 20);
                bool unrecorded = !_repo.HasRecordedMod(mod);
                bool sourceChip = OverviewOptions.ShowSourceLabels.Value && (_authorMode || mod.SourceKind != ModSourceKind.Pending);
                var chip = sourceChip ? RoundedChip.Add(row, mod.SourceKind, 0, (row.rect.height - 23) / 2) : null;
                float nameRight = xState - (unrecorded ? 26 : 4);
                float chipX = chip != null ? nameRight - chip.sizeDelta.x : nameRight;
                bool hasModUpdate = ChangeLedger.FindModUpdate("mod:"+mod.Guid)!=null;
                bool hasConflictBadge=conflicts.Any(c=>c.Involves(mod));
                var pendingMarker = MarkerState()?.Mod(mod.Guid);
                // The name column's lane is one row of chips: unread change, available update, then the pending
                // markers. Reserve room for exactly the chips this row will show, instead of a fixed guess.
                int pendingChips=(pendingMarker?.NeedsSave==true?1:0)+(pendingMarker?.NeedsRestart==true?1:0);
                bool nestedChip=mod.Outer!=null;
                // 2.3.4: one shape chip after the pending ones: resource pack (no assembly) or loose DLL in plugins/.
                bool shapeChip=mod.ResourcePack||mod.LooseFile;
                // Slots are positional (unread 0, update 1, pending from 2); the nested chip goes after the pending ones.
                int laneChips=nestedChip||shapeChip?StatusChip.FirstPendingSlot+pendingChips+(nestedChip?1:0)+(shapeChip?1:0):(hasModUpdate?1:0)+(mod.HasUpdate?1:0)+pendingChips;
                float updateSpace=StatusChip.Reserve(laneChips);
                float conflictRight=Mathf.Max(26,updateSpace);
                if(hasConflictBadge)updateSpace=Mathf.Max(updateSpace,conflictRight+18);
                float titleWidth = Mathf.Max(20, chipX - xName - (chip != null ? 4 : 0) - Mathf.Max(updateSpace,conflictRight+18));
                // A single TMP line lays out the main name first, then as much of the
                // smaller gray original as fits. Do not reserve two estimated boxes.
                var title = Label(row, OverviewLanguageBridge.DisplayNameWithOriginal(mod), xName, 4, titleWidth, 34, 19);
                OverviewUi.ReadableName(title, titleWidth);
                title.richText = true;
                title.margin = Vector4.zero;
                OverviewUi.PlaceClipped(title, xName, 4, titleWidth, 34);
                BindDependencyHint(title.gameObject, mod);
                if (ownMod) OverviewRowActions.HighlightSelf(title);
                if (chip != null) chip.anchoredPosition = new Vector2(chipX, chip.anchoredPosition.y);
                var titleClick = title.gameObject.AddComponent<OverviewTextClick>(); titleClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, capturedIndex, data); };
                OverviewEditableFrame.Set(title.rectTransform, _authorMode);
                var versionText = Label(row, UpdateVersionCaption(mod), xVersion, 4,
                    versionWidth - 7 - (mod.HasUpdate ? AvailableUpdateDot.VersionReserve : 0), 34, 20); versionText.color = OverviewUi.Ivory;
                if (mod.PreparedVersion.Length > 0) versionText.color = new Color(.88f, .72f, .43f);
                var versionClick = versionText.gameObject.AddComponent<OverviewTextClick>(); versionClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else if (data.button == PointerEventData.InputButton.Right) ShowVersion(mod); else _status(UpdateVersionHint(mod)); };
                OverviewEditableFrame.Set(versionText.rectTransform, _authorMode);
                string versionHint = UpdateVersionHint(mod);
                AvailableUpdateDot.BindVersion(versionText.gameObject,versionHint,()=>ShowVersion(mod),AddHint,ShowExternalHint,HideExternalHint,_scroll);
                string displayAuthor = mod.Author;
                var author = Label(row, displayAuthor, xAuthor, 4, Mathf.Max(1, authorWidth - 8), 34, 18); author.gameObject.SetActive(!compact); AddHint(author.gameObject, string.IsNullOrWhiteSpace(mod.Author) ? T("The local manifest does not declare an author and no project team is known.", "本地清单没有声明作者，也未找到项目所属团队。") : mod.Author + (mod.AuthorSource == "homepage" ? T("\nShown from the project homepage's owner/team.", "\n按项目主页所属作者／团队显示。") : ""));
                string translatedDescription = OverviewLanguageBridge.GetDescription(mod);
                var description = Label(row, translatedDescription, xDescription, 4, Mathf.Max(40, width - xDescription - 28), 34, 16); description.color = OverviewUi.Muted; description.gameObject.SetActive(!compact);
                AddHint(description.gameObject, translatedDescription);
                var descClick = description.gameObject.AddComponent<OverviewTextClick>(); descClick.Clicked = data => { if (data.clickCount >= 2) ShowModActions(mod, data); else Select(mod, capturedIndex, data); };
                OverviewEditableFrame.Set(description.rectTransform, _authorMode);
                enablement.Capture();enablement.Refresh(MarkerState()?.Mod(mod.Guid));
                yield return null;
                var enabled = OverviewUi.Check(row, _repo.PlannedEnabled(mod), value => Toggle(mod, value)); enabled.interactable = mod.CanToggle && !_repo.BlockedByOuter(mod);
                if (mod.Outer != null && _repo.BlockedByOuter(mod)) AddHint(enabled.gameObject, T("The outer mod is disabled: ", "外层模组已停用：") + OverviewLanguageBridge.GetDisplayName(mod.Outer));
                enablement.Checkbox=enabled;
                OverviewUi.Place((RectTransform)enabled.transform, xEnable + (goalWidth - 24) / 2, (row.rect.height - 24) / 2, 24, 24);
                OverviewEditableFrame.Set((RectTransform)enabled.transform, _authorMode && mod.CanToggle);
                yield return null;
                // 没有可显示设置项（或插件没加载）就灰掉齿轮，省得点进去只看到一句错误。
                bool configurableMod = configurable.Contains(mod.Guid) || mod.PluginGuids.Any(configurable.Contains);
                bool canOpenSettings = mod.Loaded && configurableMod;
                OverviewRowActions.Settings(row, xSettings + 2, (row.rect.height - OverviewRowActions.Size) / 2, () => _openSettings?.Invoke(mod), canOpenSettings,
                    canOpenSettings ? T("Click to open this mod's settings page.", "点击跳转到该模组的模组设置页")
                        : !mod.Loaded ? T("The mod is not loaded; its settings page is unavailable.", "该模组当前未加载，没有可打开的设置页")
                        : T("This mod does not expose any settings to ModConfig.", "该模组未向 ModConfig 提供可显示的设置项"), AddHint);
                yield return null;
                var home = OverviewRowActions.Home(row, xHome + (homeWidth - 9 - OverviewRowActions.Size) / 2, (row.rect.height - OverviewRowActions.Size) / 2,
                    () => Home(mod), true, SourceUi.HomepageHint(mod), AddHint, mod.SourceConfirmed && !ModSources.ExplicitLocal(mod));
                home.gameObject.AddComponent<OverviewTextClick>().Clicked = data => { if (_authorMode && data.button == PointerEventData.InputButton.Right) PopupSource(mod); };
                OverviewEditableFrame.Set((RectTransform)home.transform, _authorMode);
                yield return null;
                string? modFolder = ModFolder(mod);
                Button? folder = null;
                folder = OverviewRowActions.Folder(row, xFolder + (folderWidth - 9 - OverviewRowActions.Size) / 2, (row.rect.height - OverviewRowActions.Size) / 2,
                    () => { if (folder != null) FolderShortcuts.Open(mod, (RectTransform)folder.transform, _status, _repo); },
                    ownMod || modFolder != null, ownMod ? Loc.T("Folder shortcuts", "目录快捷方式", "資料夾捷徑")
                        : modFolder != null ? T("Click to open this mod's folder:\n", "点击打开该模组所在的文件夹：\n") + modFolder
                        : T("This mod's folder was not found.", "找不到该模组所在的文件夹。"), AddHint);
                yield return null;
                if (_authorMode) BuildUpdateCell(row, mod, xCheck, checkWidth);
                // Put status dots in the strip above the text. Attaching them to the
                // title reserved 46 units of right margin even with no pending state.
                var nameMarkers = OverviewUi.Rect("NameMarkers", row);
                OverviewUi.Place(nameMarkers, xName, 0, titleWidth + updateSpace, 32);
                ChangeLedger.Dot(title.transform, "mod:" + mod.Guid, _repo.ViewedProfile, markerHost: nameMarkers, showHint: ShowExternalHint, hideHint: HideExternalHint);
                ProfilePendingMarkers.Attach(nameMarkers, () => MarkerState()?.Mod(mod.Guid), ShowExternalHint, HideExternalHint, enabled.gameObject, reserveText: false, enablement: true,
                    detail: () => ModEnablementPresentation.Detail(mod,_repo.PlannedEnabled(mod),MarkerState()?.Mod(mod.Guid)),
                    statusColor: () => ModEnablementPresentation.IsChanging(mod,_repo.PlannedEnabled(mod),MarkerState()?.Mod(mod.Guid))?ModEnablementPresentation.Changing:mod.Loaded?ModEnablementPresentation.Enabled:ModEnablementPresentation.Disabled,
                    firstSlot: StatusChip.FirstPendingSlot);
                if (chip != null) chip.anchoredPosition = new Vector2(chipX, chip.anchoredPosition.y);
                if (!_repo.HasRecordedMod(mod))
                {
                    Button? marker = null;
                    marker = OverviewUi.IconButton(row, "plus", () => { _repo.IncludeModInProfile(mod); if(marker!=null)marker.gameObject.SetActive(false); RefreshEnablementRows(); }, 20, 20);
                    OverviewUi.Place((RectTransform)marker.transform, xState - 24, 10, 18, 18);
                    AddHint(marker.gameObject, T("This profile has not recorded this mod; current state is kept. Click to include it in the draft.", "此方案未记录此模组，保持当前状态；点击纳入方案草稿。"));
                }
                enablement.Capture();enablement.Refresh(MarkerState()?.Mod(mod.Guid));
                if(!_repo.PlannedEnabled(mod))Tint(home,OverviewUi.Blue);
                if (mod.HasUpdate) {
                    AttachAvailableUpdateBadge(nameMarkers, CurrentUpdateVersion(mod), mod.LatestVersion, () => ShowVersion(mod), hasModUpdate);
                    AvailableUpdateDot.Version(row,xVersion,4,versionWidth-7,versionHint,()=>ShowVersion(mod),AddHint,ShowExternalHint,HideExternalHint,_scroll);
                }
                BuildConflictBadge(nameMarkers, mod, _currentConflicts, conflictRight);
                if (nestedChip)
                {
                    var nested = StatusChip.Create(nameMarkers, StatusChip.FirstPendingSlot + pendingChips, StatusChip.Nested, raycast: true);
                    AddHint(nested.gameObject, T("Nested inside ", "内嵌于「") + OverviewLanguageBridge.GetDisplayName(mod.Outer!) + T(". Nested groups are recommended instead of nesting mod folders; folder organization moves it out.", "」。建议用嵌套分组代替文件夹嵌套；目录整理会把它拆出来。"));
                }
                if (shapeChip)
                {
                    var shape = StatusChip.Create(nameMarkers, StatusChip.FirstPendingSlot + pendingChips + (nestedChip ? 1 : 0), mod.ResourcePack ? StatusChip.ResourcePack : StatusChip.LooseFile, raycast: true);
                    AddHint(shape.gameObject, mod.ResourcePack
                        ? T("Resource pack: a package with no DLL (bundles, textures or data used by other mods). It is installed; enabling or disabling renames its manifest.", "资源包：没有 DLL 的包（其他模组使用的资源、贴图或数据）。它已安装；启停会改名它的 manifest。")
                        : T("Loose DLL directly in plugins/. Folder organization moves it into a folder of its own.", "直接放在 plugins 根下的单个 DLL。目录整理会把它收进一个单独的文件夹。"));
                }
                BuildEnablementStatus(enabled, mod, MarkerState()?.Mod(mod.Guid));
                enablement.Refresh(MarkerState()?.Mod(mod.Guid));
                }
                AddLayeredRow("mod:"+mod.Id, "Mod_"+mod.Id, rowY, RowHeight-2, width-15, BuildModLayers,mod);
                y += RowHeight;
            }
            for (; nextHeader < headers.Count; nextHeader++) EmitHeader(headers[nextHeader]);
            if (layoutRows.Count == 0) { Label(_content, T("No mods to display.", "没有可显示的模组。"), 20, y + 15, width - 50, 45, 24); y += 65; }
            _renderedUpdates = UpdateDisplayKey();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[概览] 列表刷新失败：" + ex);
            _status(Loc.T("The mod list could not finish refreshing. Please refresh to retry: ", "模组列表刷新未完成，请点击刷新重试：", "模組清單重新整理未完成，請點擊重新整理重試：") + ex.Message);
        }
        finally
        {
            try
            {
                if (cleared)
                {
                    // Count only live rows: Destroy is deferred, and an interrupted
                    // build must not leave the previous profile's scroll bounds behind.
                    float height = Mathf.Max(60,_virtualHeight);
                    foreach (RectTransform child in _content)
                        if (child != _dropLine && child.gameObject.activeSelf)
                            height = Mathf.Max(height, -child.anchoredPosition.y + child.rect.height + 8);
                    _content.sizeDelta = new Vector2(0, height);
                    Vector2 preserved = _content.anchoredPosition; preserved.y = Mathf.Clamp(oldPixelOffset, 0, Mathf.Max(0, _content.rect.height - _viewport.rect.height)); _content.anchoredPosition = preserved;
                    _scroll.StopMovement();
                }
            }
            finally { _building = false; }
            RefreshVirtualRows();
            if (_overviewSearch == null || !_overviewSearch.isFocused) RestoreRowFocus(oldFocus);
            if (cleared && _searchRefreshPending && _searchQuery == _searchInput.Trim() && Time.unscaledTime >= _searchAfter) { _searchRefreshPending = false; _scroll.verticalNormalizedPosition = 1; }
            if (cleared && _opened) PlayAppearance();
        }
    }
    private TextMeshProUGUI Label(Transform parent, string text, float x, float y, float width, float height, float size)
    {
        var label = OverviewUi.Text(parent, text, size);
        float lineHeight = height;
        OverviewUi.Place(label.rectTransform, x, y, width, lineHeight);
        label.overflowMode = parent == _header || parent.name.StartsWith("Group_", StringComparison.Ordinal) ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = true; return label;
    }
    private float Measure(string text, float size) => _hintText.GetPreferredValues(text, 1000, 40).x * size / Mathf.Max(1, _hintText.fontSize);
    private static string EscapeRich(string value) => value.Replace("<", "＜").Replace(">", "＞");
    private static void Tint(Button button, Color color) { foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true)) text.color = color; foreach (var icon in button.GetComponentsInChildren<OverviewIconGraphic>(true)) icon.color = color; }
    private void AddHint(GameObject target, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var hover = target.GetComponent<OverviewHover>() ?? target.AddComponent<OverviewHover>();
        hover.Enter = () => { _factoryHint.Cancel();_warmAfter=Time.unscaledTime+.25f;_hintDwell.Enter(text,Time.unscaledTime); _hint.gameObject.SetActive(false); };
        hover.Exit = HideExternalHint;
        var focus=target.GetComponent<OverviewHotkeyFocus>() ?? target.AddComponent<OverviewHotkeyFocus>();
        focus.Selected+=()=> { _factoryHint.Cancel();_hintDwell.Cancel();ShowHint(text); };
        focus.Deselected+=HideExternalHint;
    }
    private void AddHint(GameObject target,Func<string> factory)
    {
        var hover=target.GetComponent<OverviewHover>()??target.AddComponent<OverviewHover>();
        hover.Enter=()=>QueueExternalHint(target,factory);hover.Exit=HideExternalHint;
        var focus=target.GetComponent<OverviewHotkeyFocus>()??target.AddComponent<OverviewHotkeyFocus>();
        focus.Selected+=()=>QueueExternalHint(target,factory,true);focus.Deselected+=HideExternalHint;
    }
    private void ShowHint(string text)
    {
        if (_disposed || OverviewUi.HasModal) return;
        _dependencyHints.HideNow();_hintDwell.Cancel();
        _shownHint = text; _hintText.richText = true; _hintText.text = text;
        _lastHintPointer=new(float.NaN,float.NaN);PositionHint(); _hint.SetAsLastSibling(); _hint.gameObject.SetActive(true);
    }
    internal bool TryCloseLayer() => _groupEditor.TryCloseTop() || _dependencyHints.TryCloseLayer();
    internal void HideAllHints() { _factoryHint.Cancel();_hintDwell.Cancel(); _hint.gameObject.SetActive(false); _dependencyHints.HideNow(); }
    internal void BindDependencyHint(GameObject target, OverviewMod mod)
    {
        _dependencyHints.Bind(target, mod);
    }
    internal int? GetDisplayNumber(OverviewMod mod) => _displayNumbers.TryGetValue(mod,out int number)?number:null;
    internal void SetGlobalAllowed(bool value) => SetGlobalUpdateAllowed(value);
    internal void QueueExternalHint(object target, Func<string> factory, bool immediate=false)
    {
        if(_disposed || !_opened || OverviewUi.HasModal) { HideExternalHint();return; }
        _hintDwell.Cancel();
        if(immediate) ShowHint(_factoryHint.Focus(target,factory,Time.unscaledTime));
        else _factoryHint.Enter(target,factory,Time.unscaledTime);
    }
    internal void RefreshExternalHint(object target,Func<string> factory)
    {
        if(_disposed || !_opened || OverviewUi.HasModal) { HideExternalHint();return; }
        if(_factoryHint.Refresh(target,factory) is string ready)ShowHint(ready);
    }
    internal void ShowExternalHint(string text)
    {
        if(_disposed || OverviewUi.HasModal || (_hint.gameObject.activeSelf && _shownHint==text))return;
        _factoryHint.Cancel();_hintDwell.Enter(text,Time.unscaledTime);
    }
    internal void HideExternalHint() { _factoryHint.Cancel();_hintDwell.Cancel(); _hint.gameObject.SetActive(false); }
    private void PositionHint()
    {
        float width = Mathf.Min(700, Width - 30);
        if(_measuredHint!=_shownHint || _hintWidth!=width || _hintFont!=_hintText.font || _hintLanguage!=Loc.TranslationRevision || _hintFontSize!=_hintText.fontSize)
        {
            if(_hintMetricRevision!=Loc.TranslationRevision || _hintMetricFont!=_hintText.font || _hintMetricWidth!=width || _hintMetricSize!=_hintText.fontSize)
            { _hintMetrics.Clear();_hintMetricRevision=Loc.TranslationRevision;_hintMetricFont=_hintText.font;_hintMetricWidth=width;_hintMetricSize=_hintText.fontSize; }
            if(!_hintMetrics.TryGet(_shownHint,out _hintHeight))
            {
                _hintHeight=Mathf.Clamp(_hintText.GetPreferredValues(_shownHint,width-24,0).y+24,45,300);
                _hintMetrics.Set(_shownHint,_hintHeight);
            }
            _hint.sizeDelta=new Vector2(width,_hintHeight);
            _lastHintPointer=new(float.NaN,float.NaN);
            _measuredHint=_shownHint;_hintWidth=width;_hintFont=_hintText.font;_hintLanguage=Loc.TranslationRevision;_hintFontSize=_hintText.fontSize;
        }
        float height=_hintHeight;
        var screen=PointerPosition;var rootSize=_root.rect.size;
        if(screen==_lastHintPointer && rootSize==_lastHintRootSize)return;
        _lastHintPointer=screen;_lastHintRootSize=rootSize;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, EventCamera, out Vector2 pointer)) return;
        float x = pointer.x - _root.rect.xMin + 18;
        float pointerY = _root.rect.yMax - pointer.y;
        float y = pointerY + 22;
        if (y + height > _root.rect.height - 5) y = pointerY - height - 18;
        x = Mathf.Clamp(x, 5, Mathf.Max(5, Width - width - 20)); y = Mathf.Clamp(y, 5, Mathf.Max(5, _root.rect.height - height - 5));
        HintCanvas.Position(_hint, x, y);
    }
    private int GroupOrder(string id) => GroupSortKey(id);
    private Color GroupColor(string id) => string.IsNullOrEmpty(id) ? OverviewUi.Muted : ColorUtility.TryParseHtmlString(_repo.State.groups.FirstOrDefault(g => g.id == id)?.color ?? "", out Color color) ? color : OverviewUi.Blue;
    private void BuildGroupHeader(string groupId, float y, float width, IReadOnlyList<OverviewMod> layoutRows)
    {
        OverviewGroup? group = Groups.Get(groupId);
        int depth = Math.Max(1, Groups.Depth(groupId));
        float height = HeaderHeight(depth), font = HeaderFont(depth);
        Color groupTint = GroupColor(groupId);
        int count = group == null ? layoutRows.Count(m => !Groups.Contains(m.Rule.groupId)) : Groups.SubtreeCount(groupId, layoutRows, m => m.Rule.groupId);
        AddVirtualRow("group:"+groupId, "Group_"+groupId, y, height, width-15, row => {
        row.GetComponent<Image>().color=Color.Lerp(new Color(.1f,.11f,.12f,.95f), new Color(groupTint.r,groupTint.g,groupTint.b,.95f), depth <= 1 ? .14f : .09f);
        _dropAreas[row] = (groupId, null);
        float rails = DrawRails(row, groupId, height);
        float indent = (depth - 1) * GroupIndent, left = Mathf.Max(rails + 4, indent + 7);
        bool collapsed = Tags.Count == 0 && _repo.State.collapsedGroups.Contains(groupId);
        var arrow = OverviewUi.IconButton(row, collapsed ? "triangle-right" : "triangle-down", () => { if (!_repo.State.collapsedGroups.Remove(groupId)) _repo.State.collapsedGroups.Add(groupId); _repo.SaveUiState(); _selected.Clear(); _anchor = -1; Render(); }, 22, 26); OverviewUi.Place((RectTransform)arrow.transform, left, (height - 26) / 2, 22, 26);
        string groupName = group?.name ?? T("Ungrouped", "未分组");
        float captionX = left + 28, titleWidth = Mathf.Min(width * .45f, Measure(groupName, font) + 12);
        var caption = Label(row, EscapeRich(groupName), captionX, 0, titleWidth, height, font); caption.fontStyle = FontStyles.Normal; caption.color = groupTint; caption.characterSpacing = depth <= 1 ? 1.5f : 1f; OverviewUi.ReadableName(caption, titleWidth); OverviewUi.PlaceClipped(caption,captionX,0,titleWidth,height); AddHint(caption.gameObject, group == null ? groupName : Groups.Path(groupId)); OverviewEditableFrame.Set(caption.rectTransform, _authorMode);
        var counter = Label(row, count.ToString(), captionX + titleWidth + 6, 0, 60, height, font - 4); counter.color = OverviewUi.Muted; OverviewUi.PlaceClipped(counter, captionX + titleWidth + 6, 0, 60, height);
        float descriptionX = captionX + titleWidth + 6 + Mathf.Min(60, Measure(count.ToString(), font - 4) + 14);
        if (!string.IsNullOrWhiteSpace(group?.description)) { var description = Label(row, group!.description, descriptionX, 0, Mathf.Max(0, width - descriptionX - 21), height, 17); description.color = OverviewUi.Muted; OverviewUi.PlaceClipped(description,descriptionX,0,Mathf.Max(0,width-descriptionX-21),height); AddHint(description.gameObject, group.description); }
        var target = row.gameObject.AddComponent<OverviewRowInteraction>();
        target.NativeScroll = _scroll; target.ForwardScroll = () => !_authorMode || group == null;
        // A group heading dropped here becomes this group's subgroup; dropped mods join this group.
        target.Dropped = () => { if (_draggedGroup != null) { var dragged = _draggedGroup; EndGroupDrag(); if (group != null && dragged.id != groupId) AttachGroup(dragged.id, groupId); } else MoveSelected(groupId, null); };
        if (group != null) { target.DragStarted = () => BeginGroupDrag(group); target.DragMoved = MoveDragFeedback; target.DragFinished = EndGroupDrag; }
        Action<PointerEventData> clicked = data => {
            if (data.button == PointerEventData.InputButton.Right) { ShowGroupMenu(group, data); return; }
            if (data.clickCount >= 2) { if (group != null) EditGroup(group); else ManageGroups(); } };
        target.Clicked = clicked;
        caption.gameObject.AddComponent<OverviewTextClick>().Clicked = clicked;
        });
    }
    private void Select(OverviewMod mod, int index, PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left) return;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift), ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (shift && _anchor >= 0 && _anchor < _visible.Count)
        { if (!ctrl) _selected.Clear(); for (int i = Math.Min(index, _anchor); i <= Math.Max(index, _anchor); i++) _selected.Add(_visible[i].Id); }
        else if (ctrl) { if (!_selected.Add(mod.Id)) _selected.Remove(mod.Id); _anchor = index; }
        else { _selected.Clear(); _selected.Add(mod.Id); _anchor = index; }
        _status(T("Selected ", "已选 ") + _selected.Count + T(" mods", " 个模组") + (_authorMode ? T("; drag to reorder or change group.", "；可拖动排序或移动分组。") : T("; double-click name or description to edit.", "；双击名称改别名，双击说明编辑。")));
        // Do not destroy the click target before the UI can detect the second click.
        RefreshSelectionColors();
    }
    private Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
    private Camera? EventCamera => _eventCamera;
    private void BeginDragFeedback(OverviewMod mod)
    {
        if (!_selected.Contains(mod.Id)) { _selected.Clear(); _selected.Add(mod.Id); RefreshSelectionColors(); }
        _dragged = mod; HideAllHints();
        _dragCaption.text = OverviewLanguageBridge.GetDisplayName(mod) + (_selected.Count > 1 ? " + " + (_selected.Count - 1) : "");
        _dragGhost.gameObject.SetActive(true); _dragGhost.SetAsLastSibling(); _status(T("Drag to the blue insertion line, or to a group heading.", "拖到蓝色插入线或分组标题处松开。"));
    }
    private void MoveDragFeedback(PointerEventData data)
    {
        if (!_authorMode || _dragged == null && _draggedGroup == null) return;
        Camera? camera = EventCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, data.position, camera, out var point))
            OverviewUi.Place(_dragGhost, Mathf.Clamp(point.x - _root.rect.xMin + 15, 0, Mathf.Max(0, Width - 300)), Mathf.Clamp(_root.rect.yMax - point.y + 18, 0, Mathf.Max(0, _root.rect.height - 40)), 300, 40);
        _dropLine.gameObject.SetActive(false);
        foreach (var area in _dropAreas)
        {
            if (!area.Key.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(area.Key, data.position, camera)) continue;
            if (area.Value.Before != null && _selected.Contains(area.Value.Before.Id)) break;
            float top = -area.Key.anchoredPosition.y;
            OverviewUi.Place(_dropLine, 4, top, Mathf.Max(30, _viewport.rect.width - 8), 3); _dropLine.SetAsLastSibling(); _dropLine.gameObject.SetActive(true);
            break;
        }
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, data.position, camera, out var viewportPoint))
        {
            if (viewportPoint.y > _viewport.rect.yMax - 25) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + .018f);
            else if (viewportPoint.y < _viewport.rect.yMin + 25) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition - .018f);
            RefreshVirtualRows();
        }
    }
    private void EndDragFeedback() { _warmAfter=Time.unscaledTime+.25f;_dragged = null; _dragGhost.gameObject.SetActive(false); _dropLine.gameObject.SetActive(false); }
    private void RefreshSelectionColors()
    {
        foreach (Transform child in _content)
        {
            if (!child.name.StartsWith("Mod_", StringComparison.Ordinal)) continue;
            string id = child.name.Substring(4); var image = child.GetComponent<Image>();
            if (image != null) image.color = _selected.Contains(id) ? new Color(.18f, .34f, .43f, .72f) : new Color(.1f, .11f, .13f, .4f);
        }
    }
    private List<OverviewMod> Selected(OverviewMod? fallback = null)
    {
        var result = _mods.Where(m => _selected.Contains(m.Id)).ToList(); if (result.Count == 0 && fallback != null) result.Add(fallback); return result;
    }
    private void Toggle(OverviewMod mod, bool enabled)
    {
        if (_building || _disposed) return;
        try
        {
            // 勾选框直接作用于所在行；只有该行本身处于多选中时才批量作用于整个选区。
            var selected = _selected.Contains(mod.Id) ? Selected(mod) : new List<OverviewMod> { mod };
            _repo.SetPlannedEnabled(selected, enabled);
            _status(Loc.T("Enablement draft changed. Save configuration to commit; restart when indicated.", "启停草稿已修改；点击“保存配置”提交，按提示重启生效。", "啟停草稿已修改；點擊「儲存設定」提交，依提示重新啟動生效。"));
            RefreshEnablementRows();
        }
        catch (Exception ex) { _status(T("Could not change state: ", "启停失败：") + Loc.LocalizeMessage(ex.Message)); RefreshEnablementRows(); }
    }
    internal Action<OverviewMod>? ModInformationRequested;
    private void EditAlias(OverviewMod mod) => ModInformationRequested?.Invoke(mod);
    private void EditDescription(OverviewMod mod) => ModInformationRequested?.Invoke(mod);
    private void EditVersionLabel(OverviewMod mod) => ModInformationRequested?.Invoke(mod);
    private void Home(OverviewMod mod) => SourceUi.Menu(_repo, mod, Reload, _status, _mods);
    /// <summary>The package folder (manifest root); loose DLLs in plugins\ fall back to the folder that holds them.</summary>
    internal static string? ModFolder(OverviewMod mod)
    {
        try
        {
            if (!string.IsNullOrEmpty(mod.PackageRoot) && System.IO.Directory.Exists(mod.PackageRoot)) return mod.PackageRoot;
            string? folder = string.IsNullOrEmpty(mod.Location) ? null : System.IO.Path.GetDirectoryName(mod.Location);
            return folder != null && System.IO.Directory.Exists(folder) ? folder : null;
        }
        catch { return null; }
    }
    private void OpenFolder(string path) => OpenFolder(path, _status);
    internal static void OpenFolder(string path, Action<string> status)
    {
        try
        {
            if (!System.IO.Directory.Exists(path)) { status(T("Folder not found: ", "文件夹不存在：") + path); return; }
            // Explorer takes the raw path; a file:// URL would percent-encode the Chinese folder names.
            if (Application.platform == RuntimePlatform.WindowsPlayer) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            else Application.OpenURL(new Uri(path).AbsoluteUri);
            status(T("Opened: ", "已打开：") + path);
        }
        catch (Exception ex) { status(T("Could not open the folder: ", "无法打开文件夹：") + Loc.LocalizeMessage(ex.Message)); }
    }
    private void OpenEnhanceHomepage()
    {
        var own = _mods.FirstOrDefault(mod => mod.Guid == Plugin.PluginGuid);
        Application.OpenURL(own?.SourceConfirmed == true ? ThunderstoreUpdateService.HomeUrl(own.SourceNamespace, own.SourceName) : "https://github.com/AiAe-156/Peak-ModConfigEnhance");
    }
    private void EditSource()
    {
        var selected = Selected();
        if (selected.Count == 0) { _status(T("Select a mod first.", "请先选择模组。")); return; }
        OverviewMod first = selected[0];
        OverviewUi.Ask(T("Edit update source", "编辑更新来源"), new[] { (T("Thunderstore URL or Team-Package; empty clears the personal override", "Thunderstore地址或Team-Package；留空清除个人覆盖"), first.SourceConfirmed ? ThunderstoreUpdateService.HomeUrl(first.SourceNamespace, first.SourceName) : "") }, values =>
        {
            string ns = "", name = "";
            if (!string.IsNullOrWhiteSpace(values[0]) && !ThunderstoreUpdateService.TryParseSource(values[0], out ns, out name)) { _status(T("Invalid Thunderstore package address.", "Thunderstore包地址无效，未修改。")); return; }
            ModSources.Set(selected, string.IsNullOrWhiteSpace(values[0]) ? null : new ModSourceRecord { kind = ModSourceKind.Store, ns = ns, name = name, author = ns });
            Reload();
        });
    }
    private void ShowVersion(OverviewMod mod)
    {
        string pinned;
        try { pinned = OverviewUpdateQueue.AuthorVersion(_repo, mod); }
        catch (Exception error) { _status(error.Message); return; }
        var options = new List<(string, string)> {
            (Loc.T("Update to checked latest version", "更新到已检查最新版", "更新至已檢查最新版"), "update")
        };
        if (pinned.Length > 0) options.Add((Loc.F("Restore modpack version {0}", "恢复整合包指定版本 {0}", "還原整合包指定版本 {0}", pinned), "restore"));
        options.AddRange(new List<(string, string)> {
            (Loc.T("View installed, store, and modpack versions", "查看已安装、商店及整合包版本", "檢視已安裝、商店及整合包版本"), "versions"),
            (Loc.T("Read update notes", "查看更新说明", "查看更新說明"), "notes"),
            (Loc.T("Open homepage for manual download", "打开主页手动下载", "開啟首頁手動下載"), "home")
        });
        OverviewUi.Choose(mod.Name, options, "", selected => {
            if (selected == "update") OverviewUpdateQueue.Enqueue(_repo, new[] { mod }, _status);
            else if (selected == "restore") OverviewUpdateQueue.EnqueueModpackVersion(_repo, mod, _status);
            else if (selected == "versions") OverviewUi.ShowText(mod.Name, UpdateVersionHint(mod));
            else if (selected == "home") { if (Uri.TryCreate(mod.HomeUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) Application.OpenURL(mod.HomeUrl); }
            else OverviewUi.ShowText(mod.Name + " · " + UpdateVersionCaption(mod), string.IsNullOrWhiteSpace(mod.UpdateNotes) ? T("The author did not provide a changelog.", "作者未提供更新日志。") : mod.UpdateNotes);
        }, value => value is "update" or "restore" ? UiActionRole.Commit : UiActionRole.Neutral);
    }
    private string UpdateVersionHint(OverviewMod mod)
    {
        var lines = new List<string>();
        void Add(string text) { if (text.Trim().Length > 0) lines.Add(text.TrimStart('\n')); }
        if (mod.HasUpdate) Add(AvailableUpdateDot.Hint(CurrentUpdateVersion(mod), mod.LatestVersion));
        // Every other check message says something of its own (disabled, unconfirmed, cancelled, error). Only the
        // bare "update available" repeats the line above, so it is the one that gets dropped.
        string check = Loc.LocalizeMessage(mod.CheckMessage);
        if (!(mod.HasUpdate && check == Loc.T("Update available", "有新版本", "有新版本"))) Add(check);
        Add(Loc.F("\nInstalled package: {0}\nPlugin version: {1}\nStore latest: {2}", "\n已安装包版本：{0}\n插件版本：{1}\n商店最新版：{2}", "\n已安裝套件版本：{0}\n外掛版本：{1}\n商店最新版：{2}",
                mod.PackageVersion.Length > 0 ? mod.PackageVersion : Loc.T("Unknown version", "版本未知", "版本未知"), mod.PluginVersion,
                mod.LatestVersion.Length > 0 ? mod.LatestVersion : Loc.T("Not checked", "尚未检查", "尚未檢查")));
        if (mod.PreparedVersion.Length > 0) Add(Loc.F("\nPrepared version: {0}. See the update queue for its pending operation.",
            "\n已准备版本：{0}。可在更新队列查看待完成操作。", "\n已準備版本：{0}。可在更新佇列檢視待完成操作。", mod.PreparedVersion));
        try
        {
            string pinned = OverviewUpdateQueue.AuthorVersion(_repo, mod);
            if (pinned.Length > 0) Add(Loc.F("\nModpack target: {0}. Only explicit restore uses this version; Update all uses the checked latest version.", "\n整合包指定版本：{0}。仅显式恢复时使用；全部更新使用已检查最新版。", "\n整合包指定版本：{0}。僅明確還原時使用；全部更新使用已檢查最新版。", pinned));
        }
        catch (Exception error) { Add(error.Message); }
        return string.Join("\n", lines);
    }
    private static string UpdateVersionCaption(OverviewMod mod) => mod.DisplayVersion;
    private static string CurrentUpdateVersion(OverviewMod mod)=>mod.PackageVersion.Length>0 ? mod.PackageVersion
        :mod.PluginVersion.Length>0 ? mod.PluginVersion :Loc.T("Unknown version", "版本未知", "版本未知");
    private void AttachAvailableUpdateBadge(RectTransform host, string currentVersion, string latestVersion, Action open, bool unreadChanges = false)
    {
        AvailableUpdateDot.Name(host,unreadChanges,AvailableUpdateDot.Hint(currentVersion,latestVersion),open,
            AddHint,ShowExternalHint,HideExternalHint,_scroll);
    }
    private void EditPermissions()
    {
        _status(T("Use the global checkbox or the individual checkbox in edit mode.", "请通过全局方框或编辑模式下逐个模组的方框调整许可。"));
    }
    internal Action<OverviewGroup?>? GroupManagementRequested;
    /// <summary>Opens the group editor on a new group under <paramref name="parentId"/> (empty = top level).</summary>
    internal Action<string>? NewGroupRequested;
    internal void ManageGroups()
    {
        if (GroupManagementRequested != null) GroupManagementRequested(null); else _groupEditor.Show(null);
    }
    internal void NewGroupIn(string parentId)
    {
        if (NewGroupRequested != null) { NewGroupRequested(parentId); return; }
        _groupEditor.NewGroupParent = parentId; _groupEditor.Show(null);
    }
    internal void EditGroup(OverviewGroup? group)
    {
        if (GroupManagementRequested != null) GroupManagementRequested(group); else _groupEditor.Show(group);
    }
    private void AssignGroup()
    {
        if (_selected.Count == 0) { _status(T("Select mods first. Ctrl selects multiple mods; Shift selects a range.", "先选择模组。Ctrl多选，Shift选范围。")); return; }
        string choices = string.Join("\n", _repo.State.groups.Select(g => g.id + " · " + g.name));
        OverviewUi.Ask(T("Move selected mods", "移动所选模组"), new[] { (T("Group ID or name (empty means Ungrouped)", "分组ID或名称（留空为未分组）"), ""), (T("Available groups", "可用分组"), choices) }, values =>
        {
            string value = values[0].Trim(); OverviewGroup? group = _repo.State.groups.FirstOrDefault(g => g.id == value || g.name == value);
            if (value.Length > 0 && group == null) { _status(T("Group not found; mods were not moved.", "未找到该分组，未移动模组。")); return; }
            MoveSelected(group?.id ?? "", null, true);
        });
    }
    private void MoveSelected(string groupId, OverviewMod? before, bool explicitAction = false)
    {
        if (!_authorMode || (!explicitAction && _dragged == null)) return;
        var moving = Selected(); if (moving.Count == 0 || before != null && moving.Contains(before)) return;
        var target = _mods.Where(m => m.Rule.groupId == groupId && !moving.Contains(m)).OrderBy(m => m.Rule.order).ToList();
        int index = before == null ? target.Count : target.IndexOf(before); if (index < 0) index = target.Count;
        target.InsertRange(index, moving.OrderBy(m => _visible.IndexOf(m)));
        for (int i = 0; i < target.Count; i++) { target[i].Rule.groupId = groupId; target[i].Rule.order = i; }
        Save(); _dragged = null; _pump.Enqueue(Render); _status(T("Groups and display order saved.", "已保存模组分组和显示顺序。"));
    }
    public void Dispose()
    {
        if (_disposed) return; HideAllHints();_componentProtectionRefresh.Clear(); ResetVirtualRows(); _disposed = true; _round?.Cancel(); _lifetime.Cancel(); _pump.Tick = null; _groupEditor.Dispose(); _dependencyHints.Dispose();
        // Dispose the HTTP client only after in-flight work has observed cancellation.
        var pending = new[] { _checkTask, _tagTask }.Where(task => task != null && !task.IsCompleted).Cast<Task>().ToArray();
        if (pending.Length > 0) _ = Task.WhenAll(pending).ContinueWith(_ => { _updates.Dispose(); _lifetime.Dispose(); }, TaskScheduler.Default);
        else { _updates.Dispose(); _lifetime.Dispose(); }
        UnityEngine.Object.Destroy(_root.gameObject);
    }
}

internal sealed class OverviewListPump : MonoBehaviour
{
    private readonly ConcurrentQueue<Action> _queue = new();
    internal Action? Tick;
    internal void Enqueue(Action action) => _queue.Enqueue(action);
    private void Update()
    {
        var budget=System.Diagnostics.Stopwatch.StartNew();
        while (budget.ElapsedMilliseconds<4 && _queue.TryDequeue(out Action? action)) { try { action(); } catch (Exception ex) { Plugin.Log.LogWarning("[概览] 列表更新失败：" + Loc.LocalizeMessage(ex.Message)); } }
        Tick?.Invoke();
    }
}
internal sealed class OverviewRowInteraction : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    internal Action<PointerEventData>? Clicked;
    internal Action? DragStarted, DragFinished, Dropped;
    internal Action<PointerEventData>? DragMoved;
    internal bool IsDragging;
    internal Action? DragStateChanged;
    internal ScrollRect? NativeScroll;
    internal Func<bool>? ForwardScroll;
    public void OnPointerClick(PointerEventData data) => Clicked?.Invoke(data);
    public void OnBeginDrag(PointerEventData data) { IsDragging=true; DragStateChanged?.Invoke(); if (ForwardScroll?.Invoke() == true) NativeScroll?.OnBeginDrag(data); else DragStarted?.Invoke(); }
    public void OnDrag(PointerEventData data) { if (ForwardScroll?.Invoke() == true) NativeScroll?.OnDrag(data); else DragMoved?.Invoke(data); }
    public void OnEndDrag(PointerEventData data) { IsDragging=false; DragStateChanged?.Invoke(); if (ForwardScroll?.Invoke() == true) NativeScroll?.OnEndDrag(data); else DragFinished?.Invoke(); }
    public void OnDrop(PointerEventData data) => Dropped?.Invoke();
    private void OnDisable()=>IsDragging=false;
}
internal sealed class OverviewTextClick : MonoBehaviour, IPointerClickHandler
{
    internal Action<PointerEventData>? Clicked;
    public void OnPointerClick(PointerEventData data) => Clicked?.Invoke(data);
}
internal sealed class OverviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal Action? Enter, Exit;
    public void OnPointerEnter(PointerEventData data) => Enter?.Invoke();
    public void OnPointerExit(PointerEventData data) => Exit?.Invoke();
}







