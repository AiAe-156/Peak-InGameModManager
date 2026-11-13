#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewDependencyHints : IDisposable
{
    private readonly RectTransform _parent, _card, _viewport;
    private readonly Camera? _camera;
    private readonly Action? _beforeShow;
    private readonly HintLru<string, CardLayout> _layouts = new(64, StringComparer.Ordinal);
    private string _sourceKey = "";
    private Coroutine? _layoutJob;
    private int _layoutEpoch;
    private bool _displayDirty;
    private IEnumerator? _warmSteps;
    private int _warmEpoch;
    private GameObject? _warmSource;
    private string _warmKey="";
    private Func<bool,List<string>>? _warmContent;
    private void CancelWarm()
    {
        (_warmSteps as IDisposable)?.Dispose();_warmSteps=null;_warmSource=null;_warmContent=null;
    }
    private string? _pendingKey;
    private int _contentAtShow, _configurationAtShow;
    private TMP_FontAsset? _fontAtShow;
    private TMP_FontAsset? _fallbackAtShow;
    private bool _cacheFixed, _cacheChains;
    private Rect _cacheBounds;
    private int _boundsFrame = -1;
    private GameObject? _boundsSource;
    private Rect _boundsValue;
    private readonly List<RectMask2D> _sourceMasks = new(), _parentMasks = new();
    private sealed class CardLayout
    {
        internal string Text = "", Footer = "";
        internal float Width, Height, ContentWidth, FullHeight, ViewHeight, FooterHeight;
        internal bool Long;
    }
    private readonly OverviewRepository _repo;
    private readonly Func<IReadOnlyList<OverviewMod>> _mods;
    private readonly Func<OverviewMod, int?> _number;
    private readonly TextMeshProUGUI _text, _footer, _measureText;
    private readonly ScrollRect _scroll;
    private readonly CanvasGroup _canvasGroup;
    private readonly List<OverviewDependencyHover> _bindings = new();
    private readonly OverviewDependencyDriver _driver;
    private ModConfigEnhance.Updating.ComponentDependencyGraph? _graph;
    private ModConfigEnhance.Updating.ComponentDependencyGraph? _rawAtShow;
    private int _inventoryAtShow,_languageAtShow;
    private Func<bool, List<string>>? _content;
    private GameObject? _source;
    private bool _onSource, _onCard, _selected, _fixed, _scrollableLong;
    private float _hideAt = -1, _showAt = -1;
    private GameObject? _pendingSource;
    private Func<bool,List<string>>? _pendingContent;
    private readonly Vector3[] _corners=new Vector3[4];
    private bool _chains;
    private Rect _layoutBounds;
    private Vector2 _positionAnchor;
    private Camera? Camera => _camera;
    private static bool Fixed => OverviewDependencySettings.Mode != null && OverviewDependencySettings.Mode.Value == OverviewDependencyHintMode.Fixed;
    private static bool Chains => OverviewDependencySettings.ShowDependencyChains == null || OverviewDependencySettings.ShowDependencyChains.Value;

    internal OverviewDependencyHints(RectTransform parent, OverviewRepository repo, Func<IReadOnlyList<OverviewMod>> mods, Func<OverviewMod, int?> displayNumber, Action? beforeShow = null)
    {
        _parent = parent; _repo = repo; _mods = mods; _number = displayNumber; _beforeShow = beforeShow;
        var canvas = parent.GetComponentInParent<Canvas>()?.rootCanvas;
        _camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        _card = OverviewUi.Panel("DependencyCard", parent, OverviewUi.Brown);
        HintCanvas.Attach(_card, true);
        _canvasGroup = _card.gameObject.AddComponent<CanvasGroup>();
        _card.gameObject.AddComponent<RectMask2D>();
        _viewport = OverviewUi.Panel("Viewport", _card, Color.clear); _viewport.gameObject.AddComponent<RectMask2D>();
        _text = OverviewUi.Text(_viewport, "", 18); _text.richText = true; _text.fontStyle = FontStyles.Normal;
        _text.lineSpacing = 1; _text.textWrappingMode = TextWrappingModes.Normal; _text.raycastTarget = false;
        _measureText=OverviewUi.Text(_parent,"",18);_measureText.richText=true;_measureText.lineSpacing=1;
        _measureText.textWrappingMode=TextWrappingModes.Normal;_measureText.gameObject.SetActive(false);
        _footer = OverviewUi.Text(_card, "", 14); _footer.fontStyle = FontStyles.Normal; _footer.color = OverviewUi.Muted;
        _scroll = _card.gameObject.AddComponent<ScrollRect>(); _scroll.horizontal = false; _scroll.scrollSensitivity = 40;
        _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.inertia = false; _scroll.viewport = _viewport; _scroll.content = _text.rectTransform;
        var cardHover = _card.gameObject.AddComponent<OverviewDependencyHover>();
        cardHover.Enter = () => { if (!_fixed && !_scrollableLong) return; _onCard = true; _hideAt = -1; };
        cardHover.Exit = () => { _onCard = false; ScheduleHide(); };
        var driverObject = new GameObject("DependencyHintDriver"); driverObject.transform.SetParent(parent, false);
        _driver = driverObject.AddComponent<OverviewDependencyDriver>(); _driver.Tick = Update;
        _card.gameObject.SetActive(false);
    }
    /// <summary>1.1.0: the gamepad chip card sits to the right of this card (same parent, top-left anchored).</summary>
    internal bool CardBox(GameObject source, out Vector2 topLeft, out Vector2 size)
    {
        topLeft = new Vector2(_card.anchoredPosition.x, -_card.anchoredPosition.y); size = _card.rect.size;
        return _card.gameObject.activeSelf && _source == source;
    }
    /// <summary>1.1.0: focus (gamepad) places the card under the focused name instead of on top of it.</summary>
    private Vector2 FocusScreenPoint(GameObject target)
    {
        ((RectTransform)target.transform).GetWorldCorners(_corners);
        return RectTransformUtility.WorldToScreenPoint(Camera, _corners[0]);
    }
    internal void Reload() { HideNow(); _layouts.Clear(); SharedRelationshipSnapshot.InvalidateDisplay(); _graph = SharedRelationshipSnapshot.Get(_mods(), Array.Empty<InfrastructureOverviewModel>(), SourceUi.DisplayDependencyGraph); _bindings.RemoveAll(b => b == null); }
    internal void Bind(GameObject target, OverviewMod mod) => Bind(target, _ => Lines(mod), "mod:" + mod.Id + "\n" + mod.PackageRoot);
    // Component rows provide content without inventing a plugin GUID or profile target.
    internal void Bind(GameObject target, Func<bool, List<string>> content, string? identity = null)
    {
        var graphic = target.GetComponent<Graphic>(); if (graphic != null) graphic.raycastTarget = true;
        if (target.GetComponent<Selectable>() == null) { var selectable = target.AddComponent<Selectable>(); selectable.transition = Selectable.Transition.None; selectable.targetGraphic = graphic; }
        var hook = target.GetComponent<OverviewDependencyHover>() ?? target.AddComponent<OverviewDependencyHover>();
        if (!_bindings.Contains(hook)) _bindings.Add(hook);
        string key = identity ?? "view:" + target.GetInstanceID();
        hook.Content=content;hook.Identity=key;
        hook.Enter = () => { HideNow(); _pendingSource=target; _pendingContent=content; _pendingKey=key; _showAt=Time.unscaledTime+HoverDwell.Delay; };
        hook.Exit = () => { if(_pendingSource==target) CancelPending(); if (_source != target) return; _onSource = false; ScheduleHide(); };
        hook.Selected = () => Show(target, content, true, key);
        hook.Deselected = () => { if (_source != target) return; _selected = false; ScheduleHide(); };
    }
    private void Show(GameObject target, Func<bool, List<string>> content, bool selected, string? key = null)
    {
        CancelPending();
        if (OverviewUi.HasModal || _graph == null || target == null) return;
        bool same = _source == target;
        CancelWarm();CancelLayout();_displayDirty=false;_beforeShow?.Invoke();
        _sourceKey = key ?? _sourceKey;
        _source = target; _boundsFrame = -1; _content = content; _selected = selected || (same && _selected); _onSource = !selected; _onCard = false; _hideAt = -1;
        _fixed = Fixed; _chains = Chains;
        _canvasGroup.blocksRaycasts = _fixed; _canvasGroup.interactable = _fixed; _scroll.enabled = _fixed;
        _card.gameObject.SetActive(true); _card.SetAsLastSibling();
        
        RefreshCard(content, true);
        if (selected) Position(FocusScreenPoint(target)); else Position(Pointer);
    }
    private bool RefreshCard(Func<bool,List<string>> content, bool deferred = false)
    {
        var bounds = VisibleBounds();
        var fallback = FontFallbackSwapper.instance != null ? FontFallbackSwapper.instance.mainBaseFont : null;
        if (!ReferenceEquals(_rawAtShow,SourceUi.DisplayDependencyGraph) || _inventoryAtShow!=OverviewPageCache.Inventory
            || _contentAtShow!=OverviewPageCache.Content || _configurationAtShow!=OverviewPageCache.Configuration
            || _languageAtShow!=Loc.TranslationRevision || _fontAtShow!=_text.font || _fallbackAtShow!=fallback
            || _cacheBounds!=bounds || _cacheFixed!=_fixed || _cacheChains!=_chains)
            _layouts.Clear();
        _rawAtShow=SourceUi.DisplayDependencyGraph;_inventoryAtShow=OverviewPageCache.Inventory;
        _contentAtShow=OverviewPageCache.Content;_configurationAtShow=OverviewPageCache.Configuration;
        _languageAtShow=Loc.TranslationRevision;_fontAtShow=_text.font;_fallbackAtShow=fallback;
        _cacheBounds=bounds;_cacheFixed=_fixed;_cacheChains=_chains;
        if (_layouts.TryGet(_sourceKey,out var cached)) { if(_card.gameObject.activeSelf)ApplyLayout(cached);return false; }
        if(deferred)
        {
            CancelLayout();
            // Cheap shell is already visible at the dwell deadline. Full rich layout is
            // split across frames and may only commit to this exact display generation.
            var graphic=_source!=null?_source.GetComponent<TMP_Text>():null;
            string title=graphic!=null?graphic.text:Loc.T("Dependencies","依赖关系","依賴關係");
            ApplyLayout(new CardLayout {Text=title,Width=320,Height=60,ContentWidth=288,FullHeight=36,ViewHeight=36});
            int epoch=_layoutEpoch;string key=_sourceKey;
            _layoutJob=_driver.StartCoroutine(BuildDeferred(content,key,epoch));return true;
        }
        var lines=content(_chains);
        var steps=LayoutSteps(lines,result=> { _layouts.Set(_sourceKey,result);if(_card.gameObject.activeSelf)ApplyLayout(result); });
        while(steps.MoveNext()) { }
        return true;
    }
    private IEnumerator BuildDeferred(Func<bool,List<string>> content,string key,int epoch)
    {
        int inventory=OverviewPageCache.Inventory,revision=OverviewPageCache.Content,configuration=OverviewPageCache.Configuration,language=Loc.TranslationRevision;
        var raw=SourceUi.DisplayDependencyGraph;var font=_text.font;
        bool Current()=>epoch==_layoutEpoch && inventory==OverviewPageCache.Inventory && revision==OverviewPageCache.Content
            && configuration==OverviewPageCache.Configuration && language==Loc.TranslationRevision && ReferenceEquals(raw,SourceUi.DisplayDependencyGraph)
            && font==_text.font && Fixed==_fixed && Chains==_chains && _source!=null && _source.activeInHierarchy && !OverviewUi.HasModal;
        yield return null;
        if(!Current())yield break;
        var lines=content(_chains);
        yield return null;
        var steps=LayoutSteps(lines,result=> { if(Current()) { _layouts.Set(key,result);ApplyLayout(result); } });
        while(Current() && steps.MoveNext())yield return null;
        if(Current()) { _layoutJob=null;Position(_fixed?_positionAnchor:_selected&&_source!=null?FocusScreenPoint(_source):Pointer); }
    }
    private void CancelLayout()
    {
        _layoutEpoch++;CancelWarm();
        if(_layoutJob!=null) { _driver.StopCoroutine(_layoutJob);_layoutJob=null; }
    }
    private IEnumerator LayoutSteps(List<string> lines,Action<CardLayout> complete)
    {
        Rect bounds = VisibleBounds(); _layoutBounds = bounds;
        _measureText.font=_text.font;_measureText.fontSize=_text.fontSize;
        float maxWidth = Mathf.Max(1, Mathf.Min(760, bounds.width - 16));
        _measureText.text = DependencyCardGeometry.WrapTokens(string.Join("\n", lines));
        
        float desired = _measureText.GetPreferredValues(_measureText.text, Mathf.Max(1, maxWidth - 32), 0).x + 32;
        
        yield return null;
        float width = Mathf.Min(maxWidth, Mathf.Max(Mathf.Min(220, maxWidth), desired));
        float contentWidth = Mathf.Max(1, width - 32), limit = Mathf.Max(1, Mathf.Min(480 - 24, bounds.height - 40));
        float fullHeight = MeasureContent(lines, contentWidth);
        yield return null;
        string footer = Loc.T("Double-click a name to view dependency details.", "双击名称查看依赖详情", "雙擊名稱檢視依賴詳情");
        _footer.textWrappingMode = TextWrappingModes.Normal;
        _footer.overflowMode = TextOverflowModes.Truncate;
        
        float footerHeight = Mathf.Min(Mathf.Max(0, limit - 1), _footer.GetPreferredValues(footer, contentWidth, 0).y + 8);
        
        yield return null;
        float viewLimit = Mathf.Max(1, limit - footerHeight);
        bool longCard = fullHeight > viewLimit;
        // The initial full-content measurement remains valid when no truncation is needed.
        float viewHeight = Mathf.Min(fullHeight, viewLimit);
        var result=new CardLayout {Text=_measureText.text,Footer=footer,Width=width,
            Height=Mathf.Max(1,Mathf.Min(bounds.height-16,viewHeight+footerHeight+24)),
            ContentWidth=contentWidth,FullHeight=fullHeight,ViewHeight=viewHeight,FooterHeight=footerHeight,Long=longCard};
        complete(result);
    }
    private void ApplyLayout(CardLayout layout)
    {
        _layoutBounds=VisibleBounds();
        _scrollableLong=layout.Long;
        _canvasGroup.blocksRaycasts=_canvasGroup.interactable=_scroll.enabled=_fixed || _scrollableLong;
        _text.text=layout.Text;_text.overflowMode=TextOverflowModes.Masking;
        _footer.gameObject.SetActive(true);_footer.text=layout.Footer;
        OverviewUi.Place(_card,8,8,layout.Width,layout.Height);
        OverviewUi.Place(_viewport,16,12,layout.ContentWidth,layout.ViewHeight);
        OverviewUi.Place(_text.rectTransform,0,0,layout.ContentWidth,layout.FullHeight);
        OverviewUi.Place(_footer.rectTransform,16,12+layout.ViewHeight+4,layout.ContentWidth,Mathf.Max(1,layout.FooterHeight-4));
        _scroll.StopMovement();_scroll.verticalNormalizedPosition=1;
    }
    private float MeasureContent(IReadOnlyList<string> lines, float width)
    {
        _measureText.text = DependencyCardGeometry.WrapTokens(string.Join("\n", lines));
        // Preferred metrics do not require a giant temporary RectTransform or overflow mesh.
        float measured = _measureText.GetPreferredValues(_measureText.text, Mathf.Max(1, width), 0).y;
        
        return Mathf.Max(24, float.IsNaN(measured) || float.IsInfinity(measured) ? 24 : measured + 4);
    }
    private static string Safe(string value) => DependencyRowText.Escape(value);
    private List<string> Lines(OverviewMod mod)
    {
        var lines = new List<string> { "<size=23><color=#EEE8CE>" + OverviewLanguageBridge.DisplayNameWithOriginal(mod) + "</color></size>",
            "<size=14><color=#92998F>ID：" + Safe(mod.Guid) + "</color></size>" };
        if (!mod.Loaded) lines.Add(Secondary(mod.TargetEnabled ? Loc.T("Installed, currently not loaded", "已安装，当前未加载", "已安裝，當前未加載") : Loc.T("Disabled, currently not loaded", "已停用，当前未加载", "已停用，當前未加載")));
        else if (!mod.TargetEnabled) lines.Add(Secondary(Loc.T("Currently loaded; disabled after restarting", "当前已加载，重启后停用", "當前已加載，重啟后停用")));
        var graph=SharedRelationshipSnapshot.Get(_mods(),Array.Empty<InfrastructureOverviewModel>(),SourceUi.DisplayDependencyGraph);
        AddRelationships(lines,graph,SharedRelationshipSnapshot.RootId(mod,graph),new ComponentRelationshipLabels(graph,_mods(),Array.Empty<InfrastructureOverviewModel>()),
            !mod.DependencyMetadataKnown ? Loc.T("Dependency information could not be read.", "依赖资料未能读取", "依賴資料未能讀取")
                :Loc.T("No declared dependencies", "无已声明依赖", "無已聲明依賴"));
        return lines;
    }
    private static string Secondary(string text) => "<size=14><color=#A9ADA0>" + Safe(text) + "</color></size>";
    private static string Section(string text) => "<size=18><color=#DDC374>" + Safe(text) + "</color></size>";
    private string Format(OverviewDependencyItem item, int depth, string note)
    {
        string status = item.Unknown ? Loc.T("<color=#B8B5A5>Source unconfirmed</color>", "<color=#B8B5A5>来源未确认</color>", "<color=#B8B5A5>來源未確認</color>") : item.Installed ? "<color=#80C57D>√</color>" : item.Optional ? Loc.T("<color=#A9ADA0>Optional, not enabled</color>", "<color=#A9ADA0>可选，未启用</color>", "<color=#A9ADA0>可選，未啟用</color>") : Loc.T("<color=#EFAB80>Required dependency missing</color>", "<color=#EFAB80>缺少必需依赖</color>", "<color=#EFAB80>缺少必需依賴</color>");
        string number = item.Mod != null ? (_number(item.Mod)?.ToString() ?? "—") : "—";
        string primary = item.Mod != null ? OverviewLanguageBridge.GetDisplayName(item.Mod) : item.Name;
        string original = item.Mod != null ? (string.IsNullOrWhiteSpace(item.Mod.RawName) ? item.Mod.Name : item.Mod.RawName) : item.Name;
        string actualVersion = item.Mod != null ? (item.Mod.PackageVersion.Length > 0 ? item.Mod.PackageVersion : item.Mod.PluginVersion) : item.Version;
        string name = DependencyRowText.Name(primary, original, actualVersion);
        var flags = new List<string>(); if (item.Optional && item.Installed) flags.Add(Loc.T("Optional", "可选", "可選"));
        if (item.Mod != null) { if (!item.Mod.TargetEnabled) flags.Add(Loc.T("Disabled", "已停用", "已停用")); else if (!item.Mod.Loaded) flags.Add(Loc.T("Not loaded", "未加载", "未加載")); if (_number(item.Mod) == null) flags.Add(Loc.T("Not shown", "未显示", "未顯示")); }
        string version = OverviewDependencies.VersionState(item); if (version.Length > 0) flags.Add(version); if (note.Length > 0) flags.Add(note);
        // TMP indent keeps wrapped continuation lines at the same tree level.
        return "<indent=" + (depth * 20) + "><size=17>" + DependencyDepthStyle.Wrap(TreeArrow() + " #" + number + "  " + name, depth) + "  " + status + "</size>" + (flags.Count == 0 ? "" : Secondary("（" + string.Join("；", flags) + "）")) + "</indent>";
    }

    private const string ArrowFontName = "MCE_TreeArrow";
    private static string? _treeArrow;
    /// <summary>
    /// U+21B3 exists only in the game's NotInter-Regular, which no UI font asset uses. When that font is
    /// loaded, render the arrow from a private dynamic asset; otherwise use U+2514, which the Chinese
    /// fallback font (NotoSansSC/TC) provides.
    /// </summary>
    internal static string TreeArrow()
    {
        if (_treeArrow != null) return _treeArrow;
        _treeArrow = "└";
        try
        {
            var source = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f != null && f.name == "NotInter-Regular");
            if (source == null) { Plugin.Log.LogInfo("[依赖链] 游戏未载入 NotInter-Regular，层级符号使用 └。"); return _treeArrow; }
            var asset = TMP_FontAsset.CreateFontAsset(source, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 128, 128, AtlasPopulationMode.Dynamic, false);
            if (asset == null || !asset.HasCharacter('\u21B3', false, true)) { Plugin.Log.LogInfo("[依赖链] NotInter-Regular 无法生成 ↳，层级符号使用 └。"); return _treeArrow; }
            asset.name = ArrowFontName; UnityEngine.Object.DontDestroyOnLoad(asset);
            MaterialReferenceManager.instance.m_FontAssetReferenceLookup[TMP_TextUtilities.GetHashCode(ArrowFontName)] = asset;
            _treeArrow = "<font=\"" + ArrowFontName + "\">\u21B3</font>";
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[依赖链] 生成 ↳ 字形失败，层级符号使用 └：" + ex.Message); }
        return _treeArrow;
    }
    private Vector2 Pointer => Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
    private void Position(Vector2 screen)
    {
        _positionAnchor = screen;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, Camera, out var point)) return;
        Rect bounds = VisibleBounds();
        var box = DependencyCardGeometry.Position(bounds.x, bounds.y, bounds.width, bounds.height,
            point.x - _parent.rect.xMin, _parent.rect.yMax - point.y, _card.rect.width, _card.rect.height);
        HintCanvas.Position(_card, box.X, box.Y);
    }
    private void ScheduleHide()
    {
        if (_onSource || _selected || ((_fixed || _scrollableLong) && _onCard)) return;
        if (!_fixed && !_scrollableLong) HideNow(); else _hideAt = Time.unscaledTime + .35f;
    }
    private void Update()
    {
        if(_pendingSource!=null)
        {
            if(!_pendingSource.activeInHierarchy || OverviewUi.HasModal) CancelPending();
            else if(Time.unscaledTime>=_showAt && _pendingContent!=null) Show(_pendingSource,_pendingContent,false,_pendingKey);
        }
        if (!_card.gameObject.activeSelf) return;
        if (_source == null || !_source.activeInHierarchy || OverviewUi.HasModal) { HideNow(); return; }
        if (_fixed != Fixed || _chains != Chains) { CancelLayout();if (_content != null) Show(_source, _content, _selected); return; }
        if (_hideAt >= 0 && Time.unscaledTime >= _hideAt) { HideNow(); return; }
        if ((_displayDirty || VisibleBounds() != _layoutBounds || !ReferenceEquals(_rawAtShow,SourceUi.DisplayDependencyGraph)
            ||_inventoryAtShow!=OverviewPageCache.Inventory||_languageAtShow!=Loc.TranslationRevision
            ||_contentAtShow!=OverviewPageCache.Content||_configurationAtShow!=OverviewPageCache.Configuration
            ||_fontAtShow!=_text.font||_fallbackAtShow!=(FontFallbackSwapper.instance!=null?FontFallbackSwapper.instance.mainBaseFont:null)) && _content != null)
        {
            _displayDirty=false;CancelLayout();RefreshCard(_content,true);
            Position(_fixed ? _positionAnchor : _selected ? RectTransformUtility.WorldToScreenPoint(Camera, _source.transform.position) : Pointer);
        }
        if (!_fixed && !_selected && !_onCard && _hideAt < 0) Position(Pointer);
    }
    internal bool HasWarmWork => _warmSteps!=null || _warmContent!=null;
    internal void TickPrewarm()
    {
        if(!HasWarmWork)return;
        if(_card.gameObject.activeSelf || _pendingSource!=null || OverviewUi.HasModal || _warmEpoch!=_layoutEpoch || _warmSource==null || !_warmSource.activeInHierarchy
            || _rawAtShow!=SourceUi.DisplayDependencyGraph || _inventoryAtShow!=OverviewPageCache.Inventory || _contentAtShow!=OverviewPageCache.Content
            || _configurationAtShow!=OverviewPageCache.Configuration || _languageAtShow!=Loc.TranslationRevision || _cacheFixed!=Fixed || _cacheChains!=Chains
            || _fontAtShow!=_text.font || _fallbackAtShow!=(FontFallbackSwapper.instance!=null?FontFallbackSwapper.instance.mainBaseFont:null))
        { CancelWarm();return; }
        _source=_warmSource;_sourceKey=_warmKey;_boundsFrame=-1;_fixed=Fixed;_chains=Chains;
        if(VisibleBounds()!=_cacheBounds) { _source=null;CancelWarm();return; }
        try
        {
            if(_warmSteps==null && _warmContent!=null)
            {
                var factory=_warmContent;_warmContent=null;
                // Snapshot lookup may rebuild the shared graph; run content in its own frame.
                var lines=factory(_chains);
                _warmSteps=LayoutSteps(lines,result=>_layouts.Set(_warmKey,result));return;
            }
            if(_warmSteps!=null && !_warmSteps.MoveNext())CancelWarm();
        }
        finally { _source=null;_boundsFrame=-1; }
    }
    internal bool Prewarm(Transform row)
    {
        if(HasWarmWork || _layoutJob!=null || _card.gameObject.activeSelf || _pendingSource!=null || OverviewUi.HasModal || _graph==null)return false;
        foreach(var hook in _bindings)
        {
            if(hook==null || hook.Content==null || !hook.transform.IsChildOf(row))continue;
            _source=hook.gameObject;_sourceKey=hook.Identity;_boundsFrame=-1;_fixed=Fixed;_chains=Chains;
            try
            {
                // Validate generations without generating/laying out a whole card synchronously.
                var bounds=VisibleBounds();
                if(_cacheBounds!=bounds || _rawAtShow!=SourceUi.DisplayDependencyGraph || _inventoryAtShow!=OverviewPageCache.Inventory
                    || _contentAtShow!=OverviewPageCache.Content || _configurationAtShow!=OverviewPageCache.Configuration
                    || _languageAtShow!=Loc.TranslationRevision || _cacheFixed!=_fixed || _cacheChains!=_chains)
                { _layouts.Clear();_cacheBounds=bounds;_rawAtShow=SourceUi.DisplayDependencyGraph;_inventoryAtShow=OverviewPageCache.Inventory;
                  _contentAtShow=OverviewPageCache.Content;_configurationAtShow=OverviewPageCache.Configuration;
                  _languageAtShow=Loc.TranslationRevision;_cacheFixed=_fixed;_cacheChains=_chains; }
                var fallback=FontFallbackSwapper.instance!=null?FontFallbackSwapper.instance.mainBaseFont:null;
                if(_fontAtShow!=_text.font || _fallbackAtShow!=fallback)_layouts.Clear();
                _fontAtShow=_text.font;_fallbackAtShow=FontFallbackSwapper.instance!=null?FontFallbackSwapper.instance.mainBaseFont:null;
                if(_layouts.TryGet(_sourceKey,out _))return false;
                _warmSource=hook.gameObject;_warmKey=hook.Identity;_warmContent=hook.Content;_warmEpoch=_layoutEpoch;return true;
            }
            finally { _source=null;_boundsFrame=-1; }
        }
        return false;
    }
    internal void InvalidateDisplay() { CancelLayout();_layouts.Clear();_boundsFrame=-1;_displayDirty=true; }
    internal bool TryCloseLayer() { if (_card.gameObject.activeSelf) { HideNow(); return true; } return false; }
    private void CancelPending() { _pendingSource=null; _pendingContent=null; _pendingKey=null; _showAt=-1; }
    internal void HideNow()
    {
        CancelPending();CancelLayout();
        if (_card != null) _card.gameObject.SetActive(false);
        _source = null; _content = null; _onSource = _onCard = _selected = false; _hideAt = -1;
    }
    internal void Unbind(Transform root)
    {
        if((_source!=null && _source.transform.IsChildOf(root)) || (_pendingSource!=null && _pendingSource.transform.IsChildOf(root)))HideNow();
        if(_warmSource!=null && _warmSource.transform.IsChildOf(root))CancelWarm();
        // Cached values contain only text/geometry and are keyed by stable physical identity.
        // Row eviction must not flush unrelated prepared entries.
        for(int i=_bindings.Count-1;i>=0;i--)
        {
            var hook=_bindings[i];
            if(hook==null) { _bindings.RemoveAt(i);continue; }
            if(!hook.transform.IsChildOf(root))continue;
            hook.Enter=hook.Exit=hook.Selected=hook.Deselected=null;hook.Content=null;_bindings.RemoveAt(i);
        }
    }
    public void Dispose()
    {
        HideNow(); foreach (var hook in _bindings) if (hook != null) { hook.Enter = hook.Exit = hook.Selected = hook.Deselected = null; hook.Content=null; UnityEngine.Object.Destroy(hook); }
        _bindings.Clear(); if (_driver != null) { _driver.Tick = null; UnityEngine.Object.Destroy(_driver.gameObject); } if (_card != null) UnityEngine.Object.Destroy(_card.gameObject); if(_measureText!=null)UnityEngine.Object.Destroy(_measureText.gameObject);
    }
}

internal sealed class OverviewDependencyHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    internal Action? Enter, Exit, Selected, Deselected;
    internal Func<bool,List<string>>? Content;
    internal string Identity="";
    public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
    public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
    public void OnSelect(BaseEventData e)
    {
        // Unity often sends BaseEventData even for mouse clicks. A click must not
        // pin a following tooltip to the name after the pointer has left it.
        if (!(e is PointerEventData) && (Mouse.current == null || !Mouse.current.leftButton.isPressed)) Selected?.Invoke();
    }
    public void OnDeselect(BaseEventData e) => Deselected?.Invoke();
}
internal sealed class OverviewDependencyDriver : MonoBehaviour { internal Action? Tick; private void Update() => Tick?.Invoke(); }
