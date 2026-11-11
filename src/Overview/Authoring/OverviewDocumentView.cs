#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class OverviewDocumentView : IDisposable
{
    public GameObject Root { get; }
    public event Action<string>? StatusChanged;
    public string SourceText
    {
        get
        {
            if(_sourceMode && _source != null)return _source.text;
            if(_active==null||_selected<0||_selected>=_blocks.Count||_active.text==_blocks[_selected].Rich)return IntroductionDocument.Serialize(_blocks);
            var block=_blocks[_selected];string rich=block.Rich;bool dirty=block.Dirty;
            try{block.Rich=_active.text;block.Dirty=true;return IntroductionDocument.Serialize(_blocks);}
            finally{block.Rich=rich;block.Dirty=dirty;}
        }
    }
    public bool IsDirty => _author && SourceText != _lastSavedSource;
    public bool IsEditingText => (_active != null && _active.isFocused) || (_source != null && _source.isFocused) || _path != null;
    public bool IsImporting => _importing;
    public void ActivateEditor(){if(_author&&Root!=null&&Root.activeInHierarchy)FocusBody();}
    private readonly OverviewRepository _repository;
    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private readonly RectTransform _content;
    private readonly GameObject _tools;
    private readonly List<IntroductionBlock> _blocks = new();
    private readonly Stack<string> _undo = new(), _redo = new();
    private TMP_InputField? _active, _source, _path;
    private int _selected = -1, _selectionStart, _selectionEnd;
    private bool _author, _sourceMode, _refreshing;
    private string _savePath = "", _lastSavedSource = "";
    private GameObject? _pathShade;
    private string _pathInitial = "";
    private RectTransform? _menuAnchor;
    private readonly List<IntroductionMedia> _media = new();
    private readonly Dictionary<string,IntroductionMediaState> _mediaStates=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Transform,Color> _toolTints=new();
    private readonly List<RectTransform> _rows = new();
    private readonly Dictionary<IntroductionBlock,IntroductionMedia> _blockMedia=new();
    private readonly Dictionary<RectTransform,int> _lineCounts=new();
    private bool _mediaLayoutPending;
    private float _mediaLineWidth=-1;
    private ScrollRect? _scroll;
    private IntroductionDragPreview? _dragPreview;
    private IntroductionBlock? _draggedBlock;
    private int _lastDragScrollFrame=-1;
    private bool _importing, _disposed;
    private GameObject? _popup;
    private float _typingHistoryTime=-10;
    private readonly CancellationTokenSource _importCancellation = new();
    private static readonly Color Paper = new(.91f, .85f, .71f, .95f), Ink = new(.25f, .15f, .09f), Panel = new(.25f,.16f,.11f,.95f);
    public OverviewDocumentView(Transform parent, OverviewRepository repository, Func<Transform,string,float,TextMeshProUGUI> textFactory)
    {
        _repository = repository; _factory = textFactory;
        Root = Node(parent, "Introduction"); Stretch((RectTransform)Root.transform);
        _tools = Node(Root.transform, "AuthorTools");
        var tr = (RectTransform)_tools.transform; tr.anchorMin = new(0,1); tr.anchorMax = new(1,1); tr.pivot = new(.5f,1); tr.sizeDelta = new(0,270);
        var toolLayout=_tools.AddComponent<HorizontalLayoutGroup>();toolLayout.spacing=6;toolLayout.padding=new RectOffset(4,4,4,4);toolLayout.childControlWidth=true;toolLayout.childForceExpandWidth=false;toolLayout.childControlHeight=true;toolLayout.childForceExpandHeight=true;
        Transform Group(string name,Color tint){var group=Node(_tools.transform,name);_toolTints[group.transform]=tint;var layout=group.AddComponent<HorizontalLayoutGroup>();layout.spacing=6;layout.padding=new RectOffset(0,0,3,3);layout.childControlWidth=true;layout.childForceExpandWidth=false;layout.childControlHeight=true;layout.childForceExpandHeight=true;return group.transform;}
        var history=Group("History",new Color(.19f,.24f,.29f,.96f));Button(history,"撤销",Undo);Button(history,"重做",Redo);
        var format=Group("Format",new Color(.29f,.23f,.27f,.96f));Button(format,"粗体",()=>Format("b"));Button(format,"斜体",()=>Format("i"));Button(format,"下划线",()=>Format("u"));
        MenuButton(format,"文字颜色",()=>Palette(false));MenuButton(format,"字号",FontSizes);MenuButton(format,"字体",FontMenu);MenuButton(format,"背景",()=>Palette(true));MenuButton(format,"布局",LayoutMenu);
        var media=Group("Media",new Color(.20f,.28f,.24f,.96f));MenuButton(media,"素材",MediaMenu);
        var files=Group("Files",new Color(.31f,.21f,.20f,.96f));MenuButton(files,"文件",FileMenu);Button(files,"保存",Save,UiActionRole.Commit);
        var scrollRoot=Node(Root.transform,"IntroductionScroll"); var sr=(RectTransform)scrollRoot.transform; Stretch(sr); sr.offsetMax=new(0,-274);
        var scroll=scrollRoot.AddComponent<IntroductionScrollRect>();_scroll=scroll; scroll.horizontal=false; scroll.scrollSensitivity=40;scroll.movementType=ScrollRect.MovementType.Clamped;
        var viewport=Node(scrollRoot.transform,"Viewport"); Stretch((RectTransform)viewport.transform);((RectTransform)viewport.transform).offsetMax=new(-18,0); viewport.AddComponent<RectMask2D>(); viewport.AddComponent<Image>().color=Color.clear;
        var bar=Node(scrollRoot.transform,"Scrollbar");var br=(RectTransform)bar.transform;br.anchorMin=new(1,0);br.anchorMax=new(1,1);br.pivot=new(1,.5f);br.sizeDelta=new(14,0);br.anchoredPosition=Vector2.zero;bar.AddComponent<Image>().color=Panel;
        var sliding=Node(bar.transform,"SlidingArea");Stretch((RectTransform)sliding.transform);var handle=Node(sliding.transform,"Handle");Stretch((RectTransform)handle.transform);var handleImage=handle.AddComponent<Image>();handleImage.color=Paper;
        var scrollbar=bar.AddComponent<Scrollbar>();scrollbar.handleRect=(RectTransform)handle.transform;scrollbar.targetGraphic=handleImage;scrollbar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=scrollbar;
        var content=Node(viewport.transform,"Content"); _content=(RectTransform)content.transform; _content.anchorMin=new(0,1); _content.anchorMax=new(1,1); _content.pivot=new(.5f,1); _content.sizeDelta=Vector2.zero;
        var layout=content.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight=true; layout.childForceExpandHeight=false; layout.childControlWidth=true; layout.childForceExpandWidth=true; layout.spacing=8; layout.padding=new RectOffset(8,8,8,8);
        content.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=(RectTransform)viewport.transform; scroll.content=_content;
        var toolbarLayout=Root.AddComponent<IntroductionToolbarLayout>();toolbarLayout.Tools=tr;toolbarLayout.Scroll=sr;toolbarLayout.Content=_content;toolbarLayout.Resize=ResizeContent;
        Root.AddComponent<IntroductionEditorKeyboard>().View=this;
        Root.AddComponent<IntroductionDocumentLayout>().Resize=ApplyMediaLayout;
        Reload(); SetAuthorMode(false);
    }
    public void SetAuthorMode(bool authorMode) { Commit(); if(authorMode){try{_repository.EnsureLocalPackCopy();_savePath=_repository.IntroductionPath;}catch(Exception ex){StatusChanged?.Invoke(Loc.T("Could not start editing: ", "无法开始编辑：", "無法開始編輯：")+ex.Message);return;}} _author=authorMode;if(authorMode){IntroductionDocument.MergeParagraphs(_blocks);_active=null;_selected=Math.Min(_selected,_blocks.Count-1);} _sourceMode=false;_tools.SetActive(authorMode); Root.GetComponent<IntroductionToolbarLayout>().Refresh(); Render();if(authorMode)FocusBody(); }
    public void Reload()
    {
        _savePath=_repository.IntroductionPath; _blocks.Clear();
        try { if (File.Exists(_savePath)) _blocks.AddRange(IntroductionDocument.Parse(File.ReadAllText(_savePath,Encoding.UTF8))); }
        catch(Exception e){StatusChanged?.Invoke(Loc.T("Could not read introduction: ", "介绍读取失败：", "介紹讀取失敗：")+e.Message);}
        if(_author)IntroductionDocument.MergeParagraphs(_blocks);_undo.Clear(); _redo.Clear(); Render(false);_lastSavedSource=SourceText;
    }
    public void Save()=>TrySave();
    private bool TrySave()
    {
        if (!_author) return false;
        if(Input.compositionString.Length>0){StatusChanged?.Invoke(Loc.T("Finish the current text input before saving the introduction.", "请先完成当前文字输入，再保存介绍。", "請先完成當前文字輸入，再保存介紹。"));return false;}
        try
        {
            Commit(); string text=SourceText;
            _repository.EnsureLocalPackCopy();if(string.IsNullOrEmpty(_repository.IntroductionPath))_repository.State.pack.introFile="introduction.md";_savePath=_repository.IntroductionPath;
            bool firstDocument = !File.Exists(_savePath) || new FileInfo(_savePath).Length == 0;
            OverviewRepository.AtomicWrite(_savePath,text);
            if(firstDocument && !string.IsNullOrWhiteSpace(text))_repository.State.pack.introEnabled=true;
            _repository.Save();_lastSavedSource=text;
            StatusChanged?.Invoke(Loc.T("Introduction saved to local modpack data: ", "介绍已保存到本地整合包资料：", "介紹已保存到本地整合包資料：")+_savePath);return true;
        }
        catch(Exception e){StatusChanged?.Invoke(Loc.T("Save failed: ", "保存失败：", "保存失敗：")+e.Message);return false;}
    }
    internal bool ExportPackData()
    {
        if(!_author){StatusChanged?.Invoke(Loc.T("Enable author mode before exporting data.", "请先启用作者模式，再导出资料。", "請先啟用作者模式，再導出資料。"));return false;}
        try
        {
            if(!TrySave())return false;Commit();string source=SourceText;string root=_repository.EnsureLocalPackCopy();
            string exportRoot=Path.Combine(Loc.PackOutputDirectory,"introduction",DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
            string package=Path.Combine(exportRoot,"plugins",ModConfigEnhance.Updating.ManagerIdentity.PackFolder);Directory.CreateDirectory(package);
            // A complete author-state snapshot includes grouping, aliases and global/per-mod update permissions.
            JObject metadata=JObject.Parse(JsonConvert.SerializeObject(_repository.State));
            metadata["pack"]!["introFile"]="introduction.md";metadata["pack"]!["introEnabled"]=_repository.State.pack.introEnabled;
            metadata["packOverridden"]=false;
            metadata.Remove("hotkeyPresets");metadata.Remove("hotkeyAuthorPresetsImported");
            metadata["packHotkeyPresets"]=JArray.FromObject(_repository.GetEffectivePackPresets());
            metadata["packHotkeyPresetMode"]=_repository.GetPackPresetMode();
            metadata["packHotkeyPresetModeOverridden"]=false;
            metadata.Remove("ignoredHotkeyConflicts");
            metadata["exportedHotkeyValues"]="independent pack presets; mod defaults come from loaded plugins";
            var assets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var block in IntroductionDocument.Parse(source))if(block.Type=="image"||block.Type=="gif"||block.Type=="video")assets.Add(block.Media);
            foreach(Match image in Regex.Matches(source,@"!\[[^\]]*\]\(<?([^\s)>]+)>?(?:\s+[^)]*)?\)"))assets.Add(image.Groups[1].Value);
            // Preserve unknown extensions' relative media as well, without following junctions or copying caches.
            string mediaDirectory=OverviewRepository.SafePath(root,"media");
            if(Directory.Exists(mediaDirectory))foreach(string file in OverviewRepository.SafeFiles(mediaDirectory))if(!file.EndsWith(".partial",StringComparison.OrdinalIgnoreCase))assets.Add(file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1));
            foreach(string relative in assets.Select(p=>Uri.UnescapeDataString(p).Replace('\\','/')).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(Uri.TryCreate(relative,UriKind.Absolute,out var uri)&&!uri.IsFile)continue;
                string from=OverviewRepository.SafePath(root,relative),to=OverviewRepository.SafePath(package,relative);
                if(from.Length==0||to.Length==0||!File.Exists(from))throw new IOException(Loc.T("Export is missing relative media: ", "导出缺少相对媒体：", "導出缺少相對媒體：")+relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);using(var input=File.OpenRead(from))using(var output=new FileStream(to,FileMode.CreateNew,FileAccess.Write))input.CopyTo(output,81920);
            }
            File.WriteAllText(Path.Combine(package,"introduction.md"),source,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(package,"packinfo.json"),metadata.ToString(Formatting.Indented),new UTF8Encoding(false));
            string languageRoot=Path.Combine(exportRoot,"config",ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory,"language");
            if(Directory.Exists(Loc.LanguageDirectory))foreach(string file in OverviewRepository.SafeFiles(Loc.LanguageDirectory).Where(f=>f.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)))
            {
                string relative=file.Substring(Path.GetFullPath(Loc.LanguageDirectory).TrimEnd(Path.DirectorySeparatorChar).Length+1);
                string destination=OverviewRepository.SafePath(languageRoot,relative);if(destination.Length==0)throw new IOException(Loc.T("Invalid language file path", "语言文件路径无效", "語言文件路徑無效"));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,false);
            }
            string fontsRoot=Path.Combine(exportRoot,"config",ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory,"fonts");
            if(Directory.Exists(IntroductionFontRegistry.FontsDirectory))foreach(string file in OverviewRepository.SafeFiles(IntroductionFontRegistry.FontsDirectory).Where(f=>f.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase)||f.EndsWith(".otf",StringComparison.OrdinalIgnoreCase)))
            {
                string relative=file.Substring(Path.GetFullPath(IntroductionFontRegistry.FontsDirectory).TrimEnd(Path.DirectorySeparatorChar).Length+1);
                string destination=OverviewRepository.SafePath(fontsRoot,relative);if(destination.Length==0)throw new IOException(Loc.T("Invalid font file path", "字体文件路径无效", "字體文件路徑無效"));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,false);
            }
            StatusChanged?.Invoke(Loc.T("Complete data exported: ", "已导出完整资料：", "已導出完整資料：")+exportRoot+Loc.T(". Add the plugins and config directories to your modpack ZIP. The package has not been uploaded.", "；把 plugins 和 config 目录放入整合包 ZIP 即可（未上传）。", "；把 plugins 和 config 目錄放入整合包 ZIP 即可（未上傳）。"));return true;
        }
        catch(Exception ex){StatusChanged?.Invoke(Loc.T("Data export failed; no complete deliverable package was generated: ", "资料导出失败，未生成可交付完整包：", "資料導出失敗，未生成可交付完整包：")+ex.Message);return false;}
    }
    private void Snapshot(){PushUndo(SourceText);_typingHistoryTime=-10;}
    private void Undo(){if(Input.compositionString.Length>0)return;Commit();if(_undo.Count==0)return;_redo.Push(SourceText);Load(_undo.Pop());}
    private void Redo(){if(Input.compositionString.Length>0)return;Commit();if(_redo.Count==0)return;_undo.Push(SourceText);Load(_redo.Pop());}
    private void Load(string s){_typingHistoryTime=-10;_blocks.Clear();_blocks.AddRange(IntroductionDocument.Parse(s));if(_author)IntroductionDocument.MergeParagraphs(_blocks);_selected=Math.Min(_selected,_blocks.Count-1);Render(false);}
    private void Commit()
    {
        if(_sourceMode && _source != null){string value=_source.text;_blocks.Clear();_blocks.AddRange(IntroductionDocument.Parse(value));if(_author)IntroductionDocument.MergeParagraphs(_blocks);}
        else if(_active!=null && _selected>=0 && _selected<_blocks.Count && _active.text!=_blocks[_selected].Rich)
        {PushUndo(IntroductionDocument.Serialize(_blocks));_typingHistoryTime=-10;_blocks[_selected].Rich=_active.text;_blocks[_selected].Dirty=true;}
    }
    private void Add(IntroductionBlock block){Commit();Snapshot();_blocks.Add(block);_selected=_blocks.Count-1;Render(false);}
    private void Change(Action<IntroductionBlock> change){Commit();if(_selected<0||_selected>=_blocks.Count)return;Snapshot();change(_blocks[_selected]);_blocks[_selected].Dirty=true;Render(false);}
    private void Move(int offset){Commit();int next=_selected+offset;if(_selected<0||next<0||next>=_blocks.Count)return;Snapshot();var block=_blocks[_selected];_blocks.RemoveAt(_selected);_blocks.Insert(next,block);_selected=next;Render(false);}
    private void ScrollDrag(PointerEventData data)
    {
        if(_scroll==null)return;var view=_scroll.viewport;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(view,data.position,data.pressEventCamera,out var point))return;
        float distance=point.y>view.rect.yMax-28?1:point.y<view.rect.yMin+28?-1:0;
        if(distance!=0){float overflow=Mathf.Max(1,_content.rect.height-view.rect.height);_scroll.StopMovement();_scroll.verticalNormalizedPosition=Mathf.Clamp01(_scroll.verticalNormalizedPosition+distance*360*Time.unscaledDeltaTime/overflow);}
    }
    private void BeginBlockDrag(IntroductionBlock block,PointerEventData data)
    {
        CancelBlockDrag();Commit();_selected=_blocks.IndexOf(block);_active=null;_draggedBlock=block;
        var texture=_blockMedia.TryGetValue(block,out var media)?media.PreviewTexture:null;
        string caption=IsMedia(block)?Loc.T("Move media", "移动媒体", "移動媒體"):block.Type=="rule"?Loc.T("Move divider", "移动分割线", "移動分割線"):Regex.Replace(block.Rich,@"<[^>]+>","");
        if(caption.Length>100)caption=caption.Substring(0,100)+"…";
        _dragPreview=new IntroductionDragPreview((RectTransform)Root.transform,texture,caption,_factory,CancelBlockDrag);UpdateBlockDrag(data);
    }
    private void CancelBlockDrag(){_draggedBlock=null;var preview=_dragPreview;_dragPreview=null;preview?.Close();}
    private void UpdateBlockDrag(PointerEventData data)
    {
        if(_draggedBlock==null||_dragPreview==null)return;if(_lastDragScrollFrame!=Time.frameCount){_lastDragScrollFrame=Time.frameCount;ScrollDrag(data);}_dragPreview.Move(data);
        if(TryDropPosition(_draggedBlock,data,out int target,out bool after,out bool inline))_dragPreview.Mark(_rows[target],after,inline);else _dragPreview.HideMarker();
    }
    private bool TryDropPosition(IntroductionBlock block,PointerEventData data,out int target,out bool after,out bool inline)
    {
        target=-1;after=false;inline=false;
        if(_rows.Count==0||_scroll==null||!RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport,data.position,data.pressEventCamera)||!RectTransformUtility.ScreenPointToLocalPointInRectangle(_content,data.position,data.pressEventCamera,out var point))return false;
        for(int i=0;i<_rows.Count;i++)
        {
            var row=_rows[i];var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(_content,row);
            bool media=IsMedia(_blocks[i]);var line=media?(RectTransform)row.parent:row;var lineBounds=media?RectTransformUtility.CalculateRelativeRectTransformBounds(_content,line):bounds;
            if(point.y>lineBounds.max.y){target=i;return true;}
            if(point.y<lineBounds.min.y)continue;
            if(media&&IsMedia(block)&&point.y<lineBounds.max.y-lineBounds.size.y*.18f&&point.y>lineBounds.min.y+lineBounds.size.y*.18f)
            {
                // The middle band offers the left/right column slot. Top and bottom
                // edges retain an unambiguous whole-row insertion target.
                int count=_lineCounts[line],end=i+count;
                for(int j=i;j<end;j++){var cell=RectTransformUtility.CalculateRelativeRectTransformBounds(_content,_rows[j]);if(point.x<=cell.max.x||j==end-1){target=j;after=point.x>cell.center.x;inline=true;return true;}}
            }
            bool below=point.y<lineBounds.center.y;target=media&&below?i+_lineCounts[line]-1:i;after=below;return true;
        }
        target=_rows.Count-1;after=true;return true;
    }
    private void DropBlock(IntroductionBlock block,PointerEventData data)
    {
        int old=_blocks.IndexOf(block),target=-1;bool after=false,inline=false;bool valid=_draggedBlock==block&&TryDropPosition(block,data,out target,out after,out inline);
        CancelBlockDrag();if(!valid||old<0)return;
        // Resolve against the same row and half-slot painted by the preview.
        int insertion=target+(after?1:0);var neighbour=_blocks[target];if(neighbour==block)return;if(insertion>old)insertion--;
        Commit();Snapshot();
        if(old+1<_blocks.Count&&_blocks[old+1].InlineWithPrevious){_blocks[old+1].InlineWithPrevious=block.InlineWithPrevious;_blocks[old+1].Dirty=true;}
        _blocks.RemoveAt(old);insertion=Mathf.Clamp(insertion,0,_blocks.Count);
        if(inline)
        {
            block.InlineWithPrevious=after||neighbour.InlineWithPrevious;
            if(!after){neighbour.InlineWithPrevious=true;neighbour.Dirty=true;}
        }
        else
        {
            block.InlineWithPrevious=false;
            if(insertion<_blocks.Count&&_blocks[insertion].InlineWithPrevious){_blocks[insertion].InlineWithPrevious=false;_blocks[insertion].Dirty=true;}
        }
        block.Dirty=true;_blocks.Insert(insertion,block);_selected=insertion;Render(false);
    }
    private void Format(string tag,bool link=false)
    {
        if(_active==null || _selected<0 || Input.compositionString.Length>0)return;
        Commit();Snapshot();string value=_active.text;int logicalAnchor=IntroductionFormatting.ToLogical(value,_selectionStart),logicalFocus=IntroductionFormatting.ToLogical(value,_selectionEnd);int start=Math.Min(_selectionStart,_selectionEnd),end=Math.Max(_selectionStart,_selectionEnd);start=Mathf.Clamp(start,0,value.Length);end=Mathf.Clamp(end,start,value.Length);
        value=IntroductionFormatting.Apply(value,start,end,tag,link);
        _selectionStart=IntroductionFormatting.ToRaw(value,logicalAnchor);_selectionEnd=IntroductionFormatting.ToRaw(value,logicalFocus);
        _blocks[_selected].Rich=value;_blocks[_selected].Dirty=true;
        _active.SetTextWithoutNotify(value);_active.ForceLabelUpdate();_active.selectionStringAnchorPosition=_selectionStart;_active.selectionStringFocusPosition=_selectionEnd;ResizeContent();FocusBody();
    }
    private void Render(bool preserveText=true)
    {
        CancelBlockDrag();
        if(_author&&_blocks.Count==0)_blocks.Add(new IntroductionBlock{Rich=""});
        string currentSource=preserveText?SourceText:IntroductionDocument.Serialize(_blocks);if(preserveText)Commit();
        float scrollOffset=_content.anchoredPosition.y;
        _refreshing=true;foreach(var media in _media)media.Dispose();_media.Clear();_blockMedia.Clear();_lineCounts.Clear();_rows.Clear();_active=null;_source=null;
        foreach(Transform child in _content){child.gameObject.SetActive(false);UnityEngine.Object.Destroy(child.gameObject);}
        if(_sourceMode&&_author){_source=InputField(_content,currentSource,560);_source.richText=false;_source.textComponent.richText=false;ResizeSource();string previous=currentSource;_source.onSelect.AddListener(_=>_typingHistoryTime=-10);_source.onValueChanged.AddListener(value=>{ResizeSource();if(Input.compositionString.Length>0)return;if(Time.unscaledTime-_typingHistoryTime>.7f)PushUndo(previous);_typingHistoryTime=Time.unscaledTime;previous=value;});_source.onEndEdit.AddListener(_=>Commit());}
        else if(_blocks.Count==0){var text=_factory(_content,Loc.T("No modpack introduction yet", "暂无整合包介绍", "暫無整合包介紹")+(_author?Loc.T(". Add a paragraph or switch to source mode to begin.", "，可新增段落或切换源码开始编写。", "，可新增段落或切換源碼開始編寫。"):"。"),24);text.gameObject.AddComponent<LayoutElement>().preferredHeight=60;}
        else for(int i=0;i<_blocks.Count;)
        {
            if(!IsMedia(_blocks[i])){RenderBlock(i++,_content);continue;}
            int end=i+1;
            while(end<_blocks.Count&&FitsMediaLine(end))end++;
            var line=MediaLine(i);var rect=(RectTransform)line.transform;
            _lineCounts[rect]=end-i;
            while(i<end)RenderBlock(i++,line.transform);
        }
        _mediaLineWidth=_content.rect.width;ResizeContent();LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        if(_scroll!=null){_scroll.StopMovement();_content.anchoredPosition=new Vector2(0,Mathf.Clamp(scrollOffset,0,Mathf.Max(0,_content.rect.height-_scroll.viewport.rect.height)));}
        _refreshing=false;
    }
    private static bool IsMedia(IntroductionBlock block)=>block.Type=="image"||block.Type=="gif"||block.Type=="video";
    private void RenderBlock(int index,Transform parent)
    {
        var b=_blocks[index];var row=Node(parent,"Block"+index);var rl=row.AddComponent<HorizontalLayoutGroup>();rl.childForceExpandWidth=false;rl.childControlWidth=true;rl.childForceExpandHeight=false;rl.childControlHeight=true;rl.padding=new RectOffset(b.Indent*24,0,0,0);rl.childAlignment=b.Align=="center"?TextAnchor.UpperCenter:b.Align=="right"?TextAnchor.UpperRight:TextAnchor.UpperLeft;
        if(IsMedia(b))row.AddComponent<LayoutElement>().flexibleWidth=0;
        _rows.Add((RectTransform)row.transform);
        GameObject? dragHandle=null;
        if(_author){var handle=OverviewUi.Button(row.transform,"↕",()=>{},26,27, role: UiActionRole.Neutral);dragHandle=handle.gameObject;var handleLayout=handle.GetComponent<LayoutElement>()??handle.gameObject.AddComponent<LayoutElement>();handleLayout.preferredWidth=26;handleLayout.preferredHeight=27;if(IsMedia(b)){handleLayout.ignoreLayout=true;var rect=(RectTransform)handle.transform;rect.anchorMin=rect.anchorMax=new(0,1);rect.pivot=new(0,1);rect.anchoredPosition=new(b.Indent*24,0);rect.sizeDelta=new(26,27);}var drag=handle.gameObject.AddComponent<IntroductionBlockDrag>();drag.Begin=data=>BeginBlockDrag(b,data);drag.Drag=UpdateBlockDrag;drag.End=data=>DropBlock(b,data);drag.Cancel=CancelBlockDrag;}
        var body=Node(row.transform,"Body");var le=body.AddComponent<LayoutElement>();le.flexibleWidth=0;le.preferredWidth=Math.Max(1,(_content.rect.width-16-b.Indent*24-(_author?30:0))*b.Width/100f);le.minWidth=0;
        if(b.Type!="text")OverviewEditableFrame.Set((RectTransform)body.transform,_author);
        if(b.Type=="rule"){body.AddComponent<Image>().color=Background(b);le.preferredHeight=18;var line=Node(body.transform,"Line");var lineRect=(RectTransform)line.transform;lineRect.anchorMin=new(0,.5f);lineRect.anchorMax=new(1,.5f);lineRect.sizeDelta=new(0,2);var dash=line.AddComponent<IntroductionDashedRule>();dash.color=new Color(.67f,.64f,.57f,.75f);dash.raycastTarget=false;if(_author){var select=body.AddComponent<IntroductionBlockSelect>();select.Selected=()=>{Commit();_selected=index;_active=null;};}return;}
        if(IsMedia(b))
        {body.AddComponent<Image>().color=Background(b);le.preferredHeight=240;string path=_repository.ResolvePackPath(b.Media);if(!File.Exists(path))path=OverviewRepository.SafePath(_repository.RootDirectory,b.Media);string stateKey=path+"|"+_blocks.Take(index).Count(prior=>IsMedia(prior)&&string.Equals(prior.Media,b.Media,StringComparison.OrdinalIgnoreCase));if(!_mediaStates.TryGetValue(stateKey,out var playback))_mediaStates[stateKey]=playback=new IntroductionMediaState();var media=new IntroductionMedia(body.transform,b.Type,path,_factory,StatusChanged,_author?()=>{Commit();_selected=index;_active=null;}:null,_scroll!.viewport,Root.transform,playback,b.Align,()=>_mediaLayoutPending=true,_author?()=>b.Width:null,_author?value=>ScaleMedia(b,value):null,()=>{Commit();Snapshot();});_media.Add(media);_blockMedia[b]=media;dragHandle?.transform.SetAsLastSibling();return;}
        if(_author&&!b.PreserveBody)
        {
            le.preferredHeight=Math.Max(40,TextHeight(b.Rich,le.preferredWidth));var input=InputField(body.transform,b.Rich,le.preferredHeight);
            input.textComponent.alignment=b.Align=="center"?TextAlignmentOptions.Top:textAlign(b.Align);
            input.GetComponent<Image>().color=Background(b);
            input.onValueChanged.AddListener(value=>{le.preferredHeight=Math.Max(40,input.textComponent.GetPreferredValues(value+"\u200B",le.preferredWidth-12,0).y+16);if(Input.compositionString.Length==0){if(Time.unscaledTime-_typingHistoryTime>.7f)PushUndo(IntroductionDocument.Serialize(_blocks));_typingHistoryTime=Time.unscaledTime;b.Rich=value;b.Dirty=true;}});
            input.onSelect.AddListener(_=>{_active=input;_selected=index;_typingHistoryTime=-10;});
            input.onEndEdit.AddListener(_=>{if(_refreshing||Input.compositionString.Length>0)return;if(input.text!=b.Rich){PushUndo(IntroductionDocument.Serialize(_blocks));b.Rich=input.text;b.Dirty=true;}if(_active==input){_selected=index;Commit();}});
            var capture=input.gameObject.AddComponent<IntroductionSelectionCapture>();capture.Field=input;capture.Capture=(s,e)=>{_active=input;_selected=index;_selectionStart=s;_selectionEnd=e;};
            if(index==_selected)_active=input;
        }
        else
        {body.AddComponent<Image>().color=Background(b);var text=_factory(body.transform,b.Rich,24);text.richText=true;ConfigureText(text);text.raycastTarget=true;text.alignment=b.Align=="center"?TextAlignmentOptions.Top:textAlign(b.Align);le.preferredHeight=Math.Max(40,text.GetPreferredValues(b.Rich,le.preferredWidth-12,0).y+16);Stretch((RectTransform)text.transform);((RectTransform)text.transform).offsetMin=new(6,6);((RectTransform)text.transform).offsetMax=new(-6,-6);var links=text.gameObject.AddComponent<IntroductionLinks>();links.Text=text;links.DocumentScroll=_scroll;links.Selected=()=>{Commit();_selected=index;_active=null;if(_author&&b.PreserveBody)StatusChanged?.Invoke(Loc.T("This block contains unsupported extensions. Its source was preserved; edit it in source mode.", "此块包含尚不支持的扩展，原文已保留；请在源码模式修改文字。", "此塊包含尚不支持的擴展，原文已保留；請在源碼模式修改文字。"));};}
    }
    private static TextAlignmentOptions textAlign(string align)=>align=="right"?TextAlignmentOptions.TopRight:TextAlignmentOptions.TopLeft;
    private static void ConfigureText(TextMeshProUGUI text){text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Overflow;var style=text.GetComponent<IntroductionTextMaterial>()??text.gameObject.AddComponent<IntroductionTextMaterial>();style.Apply(text);}
    private float TextHeight(string text,float width){var tmp=_factory(Root.transform,"",24);tmp.richText=true;ConfigureText(tmp);IntroductionFontRegistry.EnsureRichFonts(text);float height=tmp.GetPreferredValues(text+"\u200B",width-12,0).y;UnityEngine.Object.Destroy(tmp.gameObject);return height+16;}
    private TMP_InputField InputField(Transform parent,string value,float height)
    {
        var go=Node(parent,"RichInput");go.SetActive(false);go.AddComponent<Image>().color=Color.clear;var rect=(RectTransform)go.transform;Stretch(rect);go.AddComponent<LayoutElement>().preferredHeight=height;
        var viewport=Node(go.transform,"TextViewport");Stretch((RectTransform)viewport.transform);((RectTransform)viewport.transform).offsetMin=new(6,6);((RectTransform)viewport.transform).offsetMax=new(-6,-6);viewport.AddComponent<RectMask2D>();
        var text=_factory(viewport.transform,"",24);Stretch((RectTransform)text.transform);text.richText=true;ConfigureText(text);
        var field=go.AddComponent<IntroductionTextInput>();if(parent==_content||parent.IsChildOf(_content))field.DocumentScroll=_scroll;field.textViewport=(RectTransform)viewport.transform;field.textComponent=text;field.contentType=TMP_InputField.ContentType.Standard;field.characterValidation=TMP_InputField.CharacterValidation.None;field.lineType=TMP_InputField.LineType.MultiLineNewline;field.lineLimit=0;field.richText=true;field.isRichTextEditingAllowed=false;field.onFocusSelectAll=false;field.resetOnDeActivation=false;field.restoreOriginalTextOnEscape=false;field.customCaretColor=true;field.caretColor=Paper;field.selectionColor=new(.12f,.52f,.95f,.7f);field.caretWidth=2;IntroductionFontRegistry.EnsureRichFonts(value);field.SetTextWithoutNotify(value);go.SetActive(true);OverviewEditableFrame.Set(rect,_author);return field;
    }
    private void Prompt(string caption,Action<string> confirm)
    {
        if(_path!=null){StatusChanged?.Invoke(Loc.T("Finish the current input first.", "请先完成当前输入。", "請先完成當前輸入。"));return;}
        ClosePopup();
        var shade=Node(Root.transform,"PathPromptShade");_pathShade=shade;Stretch((RectTransform)shade.transform);shade.AddComponent<Image>().color=new(0,0,0,.7f);
        var panel=Node(shade.transform,"PathPrompt");var rect=(RectTransform)panel.transform;rect.anchorMin=new(.1f,.35f);rect.anchorMax=new(.9f,.65f);rect.offsetMin=rect.offsetMax=Vector2.zero;panel.AddComponent<Image>().color=Panel;
        var layout=panel.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(12,12,12,12);layout.childControlHeight=true;layout.childForceExpandHeight=false;
        _factory(panel.transform,caption,22).gameObject.AddComponent<LayoutElement>().preferredHeight=28;_pathInitial="";_path=InputField(panel.transform,_pathInitial,50);_path.richText=false;_path.textComponent.richText=false;
        Button(panel.transform,"确认",()=>{string value=_path?.text??"";ClosePath();confirm(value);});Button(panel.transform,"取消",()=>{TryCloseLayer();});_path.ActivateInputField();
    }
    private void Import(string type)
    {
        if(_importing||_disposed){if(!_disposed)StatusChanged?.Invoke(Loc.T("Importing assets. Wait for completion.", "正在导入素材，请等待完成。", "正在導入素材，請等待完成。"));return;}
        if(IntroductionFilePicker.TrySelect(type,out string? selected)){if(!string.IsNullOrEmpty(selected))ImportPath(type,selected);return;}
        Prompt(type=="video"?Loc.T("Full path of the local video file","输入本地视频文件完整路径","輸入本機影片檔案完整路徑"):type=="gif"?Loc.T("Full path of the local GIF file","输入本地GIF文件完整路径","輸入本機GIF檔案完整路徑"):Loc.T("Full path of the local image file","输入本地图片文件完整路径","輸入本機圖片檔案完整路徑"),path=>ImportPath(type,path));
    }
    private async void ImportPath(string type,string path)
    {
        if(_importing||_disposed)return;_importing=true;
        try
        {
            path=path.Trim().Trim('"');var file=new FileInfo(path);if(!file.Exists)throw new IOException(Loc.T("File does not exist", "文件不存在", "文件不存在"));if(file.Length>IntroductionMedia.ImportLimitBytes)throw new IOException(Loc.T("File exceeds the 1 GB import limit", "单文件超过1GB导入上限", "單文件超過1GB導入上限"));
            string folder=Path.Combine(_repository.EnsureLocalPackCopy(),"media");Directory.CreateDirectory(folder);string name=Guid.NewGuid().ToString("N")+file.Extension.ToLowerInvariant();string target=Path.Combine(folder,name),partial=target+".partial";
            StatusChanged?.Invoke(Loc.T("Importing assets. Keep this page open until completion.", "正在导入素材，完成前请勿关闭本页。", "正在導入素材，完成前請勿關閉本頁。"));
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous))
            using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
            {if(input.Length>IntroductionMedia.ImportLimitBytes)throw new IOException(Loc.T("File exceeds the 1 GB import limit", "单文件超过1GB导入上限", "單文件超過1GB導入上限"));await input.CopyToAsync(output,81920,_importCancellation.Token);if(output.Length>IntroductionMedia.ImportLimitBytes)throw new IOException(Loc.T("File exceeds the 1 GB import limit", "单文件超过1GB导入上限", "單文件超過1GB導入上限"));}
            _importCancellation.Token.ThrowIfCancellationRequested();if(_disposed)return;File.Move(partial,target);
            Add(new IntroductionBlock{Type=type,Media="media/"+name,Dirty=true}); StatusChanged?.Invoke(Loc.T("Media imported into the local modpack data directory.", "媒体已导入本地整合包资料目录。", "媒體已導入本地整合包資料目錄。"));
        }
        catch(OperationCanceledException){if(!_disposed)StatusChanged?.Invoke(Loc.T("Asset import canceled.", "素材导入已取消。", "素材導入已取消。"));}
        catch(Exception ex){if(!_disposed)StatusChanged?.Invoke(Loc.T("Asset import failed: ", "素材导入失败：", "素材導入失敗：")+ex.Message+Loc.T(". Incomplete .partial files were kept.", "；未完成的 .partial 文件会保留。", "；未完成的 .partial 文件會保留。"));}
        finally{_importing=false;if(_disposed)_importCancellation.Dispose();}
    }
    private void ConfirmDelete()
    {
        Commit();if(_selected<0||_selected>=_blocks.Count)return;var selected=_blocks[_selected];
        OverviewUi.Confirm(Loc.T("Remove this document block? Media files will remain.","确认删除此文档块？媒体文件会保留。","確認刪除此文件區塊？媒體檔案會保留。"),()=>{int index=_blocks.IndexOf(selected);if(index<0)return;Snapshot();_blocks.RemoveAt(index);_selected=Math.Min(index,_blocks.Count-1);Render(false);},UiActionRole.Danger);
    }
    private static Color Background(IntroductionBlock block)=>ColorUtility.TryParseHtmlString(block.Background,out var color)?color:Color.clear;
    private void SetBackground(string value)
    {
        Commit();if(_selected<0||_selected>=_blocks.Count)return;Snapshot();var block=_blocks[_selected];block.Background=value;block.Dirty=true;
        if(_selected<_rows.Count){var body=_rows[_selected].Find("Body");if(body!=null){var field=body.GetComponentInChildren<TMP_InputField>();var image=field!=null?field.GetComponent<Image>():body.GetComponent<Image>();if(image!=null)image.color=Background(block);}}
    }
    private void PushUndo(string value){if(_undo.Count==0||_undo.Peek()!=value)_undo.Push(value);if(_undo.Count>50){var keep=_undo.Take(50).Reverse().ToArray();_undo.Clear();foreach(string item in keep)_undo.Push(item);}_redo.Clear();}
    private GameObject MediaLine(int index)
    {
        var line=Node(_content,"MediaLine"+index);var layout=line.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlWidth=true;layout.childForceExpandWidth=false;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childAlignment=_blocks[index].Align=="right"?TextAnchor.UpperRight:_blocks[index].Align=="center"?TextAnchor.UpperCenter:TextAnchor.UpperLeft;return line;
    }
    // The explicit row relationship owns the columns; visual scaling never dissolves it.
    private bool FitsMediaLine(int next)=>IsMedia(_blocks[next])&&_blocks[next].InlineWithPrevious;
    private void ScaleMedia(IntroductionBlock block,int width)
    {
        width=Mathf.Clamp(width,10,100);if(width==block.Width)return;
        block.Width=width;block.Dirty=true;
        _mediaLayoutPending=true;
    }
    private void ReflowMediaRows()
    {
        _mediaLineWidth=_content.rect.width;
        // Reparent the existing cells only: GIF/video players and their position survive.
        var oldLines=_lineCounts.Keys.ToArray();foreach(var row in _rows)row.SetParent(_content,false);
        foreach(var line in oldLines){line.gameObject.SetActive(false);UnityEngine.Object.Destroy(line.gameObject);}_lineCounts.Clear();
        for(int i=0;i<_blocks.Count;)
        {
            if(!IsMedia(_blocks[i])){_rows[i].SetAsLastSibling();i++;continue;}
            int end=i+1;while(end<_blocks.Count&&FitsMediaLine(end))end++;
            var line=MediaLine(i);_lineCounts[(RectTransform)line.transform]=end-i;
            while(i<end)_rows[i++].SetParent(line.transform,false);
        }
    }
    private bool ResizeContent()
    {
        if(_sourceMode){ResizeSource();return true;}
        if(_rows.Count==_blocks.Count&&Mathf.Abs(_content.rect.width-_mediaLineWidth)>.5f)ReflowMediaRows();
        for(int i=0;i<_rows.Count&&i<_blocks.Count;i++)
        {
            var row=_rows[i];var body=row.Find("Body");if(body==null)continue;var layout=body.GetComponent<LayoutElement>();if(layout==null)continue;var b=_blocks[i];
            if(_blockMedia.TryGetValue(b,out var media))
            {
                int count=_lineCounts[(RectTransform)row.parent];float available=Mathf.Max(1,(_content.rect.width-16-8*(count-1))/count);float width=Mathf.Max(1,available-b.Indent*24);
                layout.minWidth=0;layout.preferredWidth=width;layout.preferredHeight=media.PreferredHeight(width,b.Width);
                var cell=row.GetComponent<LayoutElement>();cell.minWidth=0;cell.preferredWidth=layout.preferredWidth+b.Indent*24;
            }
            else
            {
                layout.preferredWidth=Math.Max(1,(_content.rect.width-16-b.Indent*24-(_author?30:0))*b.Width/100f);
                if(b.Type=="text"){var text=body.GetComponentInChildren<TextMeshProUGUI>();if(text!=null)layout.preferredHeight=Math.Max(40,text.GetPreferredValues(b.Rich+(body.GetComponentInChildren<TMP_InputField>()!=null?"\u200B":""),layout.preferredWidth-12,0).y+16);}
            }
        }
        return true;
    }
    private void ResizeSource()
    {
        if(_source==null)return;
        _source.GetComponent<LayoutElement>().preferredHeight=Mathf.Max(560,_source.textComponent.GetPreferredValues(_source.text+"\u200B",Mathf.Max(1,_content.rect.width-12),0).y+20);
    }
    private void ApplyMediaLayout()
    {
        if(!_mediaLayoutPending||_refreshing||_disposed)return;_mediaLayoutPending=false;
        float offset=_content.anchoredPosition.y;ResizeContent();LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        if(_scroll!=null)_content.anchoredPosition=new Vector2(0,Mathf.Clamp(offset,0,Mathf.Max(0,_content.rect.height-_scroll.viewport.rect.height)));
    }
    private void SetInlineWithPrevious(bool inline)
    {
        Commit();if(_selected<0||_selected>=_blocks.Count||!IsMedia(_blocks[_selected])){StatusChanged?.Invoke(Loc.T("Select an image, GIF, or video first.", "请先选中图片、GIF 或视频。", "請先選中圖片、GIF 或視頻。"));return;}
        if(inline&&(_selected==0||!IsMedia(_blocks[_selected-1]))){StatusChanged?.Invoke(Loc.T("The previous block must be an image, GIF, or video to place them side by side.", "上一块需要是图片、GIF 或视频，才能并排。", "上一塊需要是圖片、GIF 或視頻，才能并排。"));return;}
        Snapshot();var block=_blocks[_selected];block.InlineWithPrevious=inline;block.Dirty=true;
        Render(false);
    }
    private void FocusBody(){if(_sourceMode&&_source!=null){if(!_source.isFocused)_source.ActivateInputField();return;}if(_active==null){_active=_content.GetComponentsInChildren<TMP_InputField>().FirstOrDefault();if(_active!=null)_selected=0;}if(_active!=null&&!_active.isFocused){EventSystem.current?.SetSelectedGameObject(_active.gameObject);_active.ActivateInputField();}}
    private void MenuButton(Transform parent,string label,Action open)
    {
        GameObject? button=null;button=Button(parent,label,()=>{_menuAnchor=(RectTransform)button!.transform;open();});
    }
    private void ClosePopup(){if(_popup==null)return;_popup.SetActive(false);UnityEngine.Object.Destroy(_popup);_popup=null;}
    private void ClosePath(){_path=null;if(_pathShade!=null){_pathShade.SetActive(false);UnityEngine.Object.Destroy(_pathShade);_pathShade=null;}FocusBody();}
    public bool TryCloseLayer()
    {
        if(_draggedBlock!=null){CancelBlockDrag();return true;}
        foreach(var media in _media)if(media.CloseEnlarged())return true;
        if(_path!=null){if(Input.compositionString.Length>0)return true;if(_path.text!=_pathInitial)OverviewUi.ConfirmDiscard(Loc.T("Discard the unconfirmed input?", "放弃尚未确认的输入？", "放棄尚未確認的輸入？"),ClosePath,()=>_path?.ActivateInputField());else ClosePath();return true;}
        if(_popup!=null){ClosePopup();FocusBody();return true;}return false;
    }
    public void RequestLeave(Action leave)
    {
        if(_importing){StatusChanged?.Invoke(Loc.T("Importing assets. Wait for completion.", "正在导入素材，请等待完成。", "正在導入素材，請等待完成。"));return;}
        if(Input.compositionString.Length>0)return;
        if(TryCloseLayer())return;
        if(!IsDirty){leave();return;}
        OverviewUi.ConfirmUnsaved(Loc.T("The introduction has unsaved changes", "介绍有未保存的修改", "介紹有未保存的修改"),()=>{Save();if(!IsDirty)leave();},()=>{Load(_lastSavedSource);leave();},FocusBody);
    }
    private void Popover(Action<Transform,Action> build,float width=220,int columns=1,float cellWidth=200)
    {
        if(Input.compositionString.Length>0)return;Commit();ClosePopup();
        var shade=Node(Root.transform,"EditorDropdown");Stretch((RectTransform)shade.transform);shade.AddComponent<Image>().color=Color.clear;_popup=shade;
        var outside=shade.AddComponent<UnityEngine.UI.Button>();outside.onClick.AddListener(()=>{ClosePopup();FocusBody();});outside.navigation=new Navigation{mode=Navigation.Mode.None};
        var panel=Node(shade.transform,"Menu");var rect=(RectTransform)panel.transform;rect.anchorMin=rect.anchorMax=new(0,1);rect.pivot=new(0,1);panel.AddComponent<Image>().color=Panel;
        var menu=Node(panel.transform,"Options");var menuRect=(RectTransform)menu.transform;menuRect.anchorMin=new(0,1);menuRect.anchorMax=new(1,1);menuRect.pivot=new(.5f,1);menuRect.sizeDelta=Vector2.zero;
        var grid=menu.AddComponent<GridLayoutGroup>();grid.cellSize=new(cellWidth,30);grid.spacing=new(4,4);grid.padding=new RectOffset(10,10,10,10);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;
        void Close(){ClosePopup();}
        build(menu.transform,Close);
        int count=menu.transform.childCount;float height=20+Mathf.Ceil((float)count/columns)*34-4;
        var rootRect=(RectTransform)Root.transform;var corners=new Vector3[4];(_menuAnchor??(RectTransform)_tools.transform).GetWorldCorners(corners);var point=rootRect.InverseTransformPoint(corners[0]);
        float x=Mathf.Clamp(point.x-rootRect.rect.xMin,0,Mathf.Max(0,rootRect.rect.width-width));float y=Mathf.Max(0,rootRect.rect.yMax-point.y+4);
        float visibleHeight=Mathf.Min(height,Mathf.Max(30,rootRect.rect.height-y));rect.anchoredPosition=new(x,-y);rect.sizeDelta=new(width,visibleHeight);menuRect.sizeDelta=new(0,height);
        if(height>visibleHeight){panel.AddComponent<RectMask2D>();var scroll=panel.AddComponent<ScrollRect>();scroll.viewport=rect;scroll.content=menuRect;scroll.horizontal=false;scroll.scrollSensitivity=30;}
    }
    private void Palette(bool background)
    {
        Popover((parent,close)=>{if(background)Button(parent,"透明",()=>{close();SetBackground("");});foreach(string color in OverviewUi.Palette.Concat(new[]{"#FFFFFF","#000000","#392518","#E84545"})){string choice=color;var swatch=Button(parent,"●",()=>{close();if(background)SetBackground(choice);else Format("color="+choice);});if(ColorUtility.TryParseHtmlString(choice,out var tint)){var text=swatch.GetComponentInChildren<TMP_Text>();if(text!=null)text.color=tint;}}},300,7,36);
    }
    private void FontSizes()=>Popover((parent,close)=>{foreach(int size in new[]{12,16,20,24,28,32,40,48,64}){int choice=size;Button(parent,size+"号",()=>{close();Format("size="+choice);});}},130,1,110);
    private void FontMenu()=>Popover((parent,close)=>{
        void Item(string label,Action action){var button=Button(parent,label,action);foreach(var text in button.GetComponentsInChildren<TMP_Text>()){text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=new Vector4(9,0,5,0);text.enableAutoSizing=true;text.fontSize=12;text.fontSizeMax=12;text.fontSizeMin=9;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;}}
        foreach(var option in IntroductionFontRegistry.Options()){var choice=option;Item(choice.Label,()=>{if(!IntroductionFontRegistry.TryResolve(choice.Id,out _,out string error)){StatusChanged?.Invoke(error);return;}close();Format("font=\""+IntroductionFontRegistry.RegistryName(choice.Id)+"\"");});}
        Item("打开字体目录",()=>{try{IntroductionFontRegistry.EnsureDirectory();Application.OpenURL(new Uri(IntroductionFontRegistry.FontsDirectory).AbsoluteUri);}catch(Exception ex){StatusChanged?.Invoke(Loc.T("Could not open fonts directory: ", "无法打开字体目录：", "無法打開字體目錄：")+ex.Message);}});
        Item("刷新字体列表",()=>{close();FontMenu();});
    },260,1,240);
    private void LayoutMenu()=>Popover((parent,close)=>{
        void Item(string label,Action action)=>Button(parent,label,()=>{close();action();});
        Item("新增段落",()=>{Add(new IntroductionBlock{Rich="",Dirty=true});FocusBody();});Item("删除线",()=>Format("s"));Item("分割线",()=>Add(new IntroductionBlock{Type="rule",Dirty=true}));Item("删除块",ConfirmDelete);
        Item("上移",()=>Move(-1));Item("下移",()=>Move(1));Item("缩进＋",()=>Change(b=>b.Indent=Math.Min(6,b.Indent+1)));Item("缩进－",()=>Change(b=>b.Indent=Math.Max(0,b.Indent-1)));
        foreach(int width in new[]{25,50,75,100}){int choice=width;Item(width+"%",()=>Change(b=>b.Width=choice));}
        Item(Loc.T("Place beside previous block", "与上一块并排", "與上一塊并排"),()=>SetInlineWithPrevious(true));Item(Loc.T("Start a new row", "另起一行", "另起一行"),()=>SetInlineWithPrevious(false));
        Item("左对齐",()=>Change(b=>b.Align="left"));Item("居中",()=>Change(b=>b.Align="center"));Item("右对齐",()=>Change(b=>b.Align="right"));
    },240,2,108);
    private void MediaMenu()=>Popover((parent,close)=>{
        void Item(string label,Action action)=>Button(parent,label,()=>{close();action();});
        Item("图片",()=>Import("image"));Item("GIF",()=>Import("gif"));Item("短视频",()=>Import("video"));Item("链接",()=>Prompt(Loc.T("Link address","链接地址","連結網址"),value=>Format("link=\""+value.Replace("\"","%22")+"\"",true)));
        Item("资源预算",()=>Prompt(Loc.T("Decoded megapixels (1–256; up to 1 GB per file)","解码像素百万（1–256；单文件上限1GB）","解碼像素百萬（1–256；單檔上限1GB）"),value=>{if(int.TryParse(value,out int budget)&&budget>=1&&budget<=256)IntroductionMedia.PixelBudget=budget*1024*1024;}));
    });
    private void FileMenu()=>Popover((parent,close)=>{
        void Item(string label,Action action)=>Button(parent,label,()=>{close();action();});
        Item("源码/预览",()=>{Commit();_sourceMode=!_sourceMode;Render(false);FocusBody();});Item("保存",Save);Item("导出资料",()=>ExportPackData());
        Item("打开草稿",()=>{void Open(){string path=_repository.GetDraftIntroductionPath();if(File.Exists(path))Load(File.ReadAllText(path,Encoding.UTF8));}if(IsDirty)OverviewUi.ConfirmDiscard(Loc.T("Opening a draft discards the current unsaved introduction. Continue?", "打开草稿会放弃当前未保存的介绍，继续？", "打開草稿會放棄當前未保存的介紹，繼續？"),Open);else Open();});
        Item(Loc.T("Load demo draft", "载入示范草稿", "載入示范草稿"),LoadDemo);
    });
    private void LoadDemo()
    {
        void Open()
        {
            try
            {
                string directory=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"Demo");
                string file=Path.Combine(directory,"introduction.md");if(!File.Exists(file))throw new FileNotFoundException(Loc.T("Demo data is not installed", "示范资料未安装", "示范資料未安裝"),file);
                string source=File.ReadAllText(file,Encoding.UTF8), root=_repository.EnsureLocalPackCopy();
                string media=Path.Combine(directory,"media");
                if(Directory.Exists(media))foreach(string asset in OverviewRepository.SafeFiles(media))
                {
                    string relative="media/"+Path.GetFileName(asset), imported="media/demo-"+Plugin.PluginVersion+"-"+Path.GetFileName(asset);
                    string destination=OverviewRepository.SafePath(root,imported);if(destination.Length==0)throw new IOException(Loc.T("Invalid demo media destination path", "示范媒体目标路径无效", "示范媒體目標路徑無效"));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);if(!File.Exists(destination))File.Copy(asset,destination,false);
                    source=source.Replace(relative,imported);
                }
                Commit();Snapshot();Load(source);
                string draft=Path.Combine(_repository.RootDirectory,"drafts","demo-"+Plugin.PluginVersion+".md");
                if(!File.Exists(draft)){Directory.CreateDirectory(Path.GetDirectoryName(draft)!);File.WriteAllText(draft,source,new UTF8Encoding(false));}
                StatusChanged?.Invoke(Loc.T("Demo draft loaded but not saved. Save it to replace the current introduction.", "示范草稿已载入，尚未保存；点击保存才替换当前介绍。", "示范草稿已載入，尚未保存；點擊保存才替換當前介紹。"));FocusBody();
            }
            catch(Exception ex){StatusChanged?.Invoke(Loc.T("Could not load demo draft: ", "载入示范草稿失败：", "載入示范草稿失敗：")+ex.Message);}
        }
        if(IsDirty)OverviewUi.ConfirmDiscard(Loc.T("Loading the demo draft discards current unsaved changes. Continue?", "载入示范草稿会放弃当前未保存修改，继续？", "載入示范草稿會放棄當前未保存修改，繼續？"),Open);else Open();
    }
    internal void HandleEditorShortcut(string command)
    {
        if(!_author||_path!=null||_popup!=null||OverviewUi.HasModal||Input.compositionString.Length>0)return;
        if(command=="undo"){Undo();FocusBody();}else if(command=="redo"){Redo();FocusBody();}else if(command=="save")Save();else if(command=="link"&&!_sourceMode)Prompt(Loc.T("Link address","链接地址","連結網址"),value=>Format("link=\""+value.Replace("\"","%22")+"\"",true));else if(!_sourceMode)Format(command);
    }
    internal bool HasKeyboardFocus=>_author&&((_active!=null&&_active.isFocused)||(_source!=null&&_source.isFocused));
    private GameObject Button(Transform parent,string label,Action action,UiActionRole role = UiActionRole.Edit)
    {
        string english=label switch{"新增段落"=>"Paragraph","源码/预览"=>"Source/View","保存"=>"Save","撤销"=>"Undo","重做"=>"Redo","打开草稿"=>"Open draft","导出资料"=>"Export pack","删除块"=>"Remove block","资源预算"=>"Pixel budget","粗体"=>"Bold","斜体"=>"Italic","下划线"=>"Underline","删除线"=>"Strike","上移"=>"Move up","下移"=>"Move down","缩进＋"=>"Indent +","缩进－"=>"Indent -","左对齐"=>"Left","居中"=>"Center","右对齐"=>"Right","分割线"=>"Divider","链接"=>"Link","图片"=>"Image","短视频"=>"Video","确认"=>"Confirm","取消"=>"Cancel","文字颜色"=>"Color","字号"=>"Size","字体"=>"Font","背景"=>"Fill","布局"=>"Layout","素材"=>"Media","文件"=>"File","更多"=>"More","透明"=>"Transparent",_=>label.EndsWith("号")?label.Substring(0,label.Length-1)+" pt":label};
        string localized=Loc.T(english,label,TraditionalLabel(label));var nativeButton=OverviewUi.Button(parent,localized,action,86,27, role);bool toolbar=parent.parent==_tools.transform;nativeButton.navigation=new Navigation{mode=Navigation.Mode.None};var go=nativeButton.gameObject;var caption=go.GetComponentInChildren<TMP_Text>();float width=86;if(caption!=null){caption.enableAutoSizing=true;caption.fontSizeMin=10;caption.fontSizeMax=15;if(toolbar){caption.fontSize=15;width=Mathf.Max(40,caption.GetPreferredValues(localized,0,0).x+20);if(label=="字体")width=Mathf.Clamp(width,90,120);}}var element=go.GetComponent<LayoutElement>()??go.AddComponent<LayoutElement>();element.preferredHeight=27;element.preferredWidth=width;element.flexibleWidth=0;if(toolbar)go.AddComponent<IntroductionToolWidth>().PreferredWidth=width;return go;
    }
    /// <summary>2.3.8: Traditional Chinese for the editor's built-in labels (they are written in Simplified).</summary>
    private static string TraditionalLabel(string label)=>label switch{"新增段落"=>"新增段落","源码/预览"=>"原始碼/預覽","保存"=>"儲存","撤销"=>"復原","重做"=>"重做","打开草稿"=>"開啟草稿","导出资料"=>"匯出資料","删除块"=>"刪除區塊","资源预算"=>"資源預算","粗体"=>"粗體","斜体"=>"斜體","下划线"=>"底線","删除线"=>"刪除線","上移"=>"上移","下移"=>"下移","缩进＋"=>"縮排＋","缩进－"=>"縮排－","左对齐"=>"靠左","居中"=>"置中","右对齐"=>"靠右","分割线"=>"分隔線","链接"=>"連結","图片"=>"圖片","短视频"=>"短影片","确认"=>"確認","取消"=>"取消","文字颜色"=>"文字顏色","字号"=>"字級","字体"=>"字型","背景"=>"背景","布局"=>"版面","素材"=>"素材","文件"=>"檔案","更多"=>"更多","透明"=>"透明","打开字体目录"=>"開啟字型目錄","刷新字体列表"=>"重新整理字型清單","源码"=>"原始碼",_=>label.EndsWith("号")?label.Substring(0,label.Length-1)+" 級":label};
    private static GameObject Node(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go;}
    private static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    public void Dispose(){if(_disposed)return;CancelBlockDrag();_disposed=true;_importCancellation.Cancel();if(!_importing)_importCancellation.Dispose();foreach(var media in _media)media.Dispose();_media.Clear();if(Root!=null)UnityEngine.Object.Destroy(Root);}
}

internal sealed class IntroductionSelectionCapture : MonoBehaviour
{
    internal TMP_InputField Field=null!;internal Action<int,int>? Capture;
    private void LateUpdate(){if(Field!=null&&Field.isFocused)Capture?.Invoke(Field.selectionStringAnchorPosition,Field.selectionStringFocusPosition);}
}
internal sealed class IntroductionDocumentLayout : MonoBehaviour
{
    internal Action? Resize;
    private void LateUpdate()=>Resize?.Invoke();
}
internal sealed class IntroductionEditorKeyboard : MonoBehaviour
{
    internal OverviewDocumentView View=null!;
    private void Update()
    {
        if(View==null||!View.HasKeyboardFocus)return;
        string? command=TypingGuard.ReadKeyboard(()=>{var keyboard=Keyboard.current;if(keyboard==null||!(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed))return null;
            if(keyboard.zKey.wasPressedThisFrame)return keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed?"redo":"undo";
            if(keyboard.yKey.wasPressedThisFrame)return "redo";if(keyboard.sKey.wasPressedThisFrame)return "save";if(keyboard.kKey.wasPressedThisFrame)return "link";if(keyboard.bKey.wasPressedThisFrame)return "b";if(keyboard.iKey.wasPressedThisFrame)return "i";if(keyboard.uKey.wasPressedThisFrame)return "u";return null;});
        if(command!=null)View.HandleEditorShortcut(command);
    }
}
internal sealed class IntroductionToolbarLayout : MonoBehaviour
{
    internal RectTransform Tools=null!,Scroll=null!;
    internal RectTransform Content=null!;internal Func<bool>? Resize;
    private float _width=-1,_contentWidth=-1;
    private void LateUpdate(){if(Tools!=null&&Math.Abs(Tools.rect.width-_width)>.5f)Refresh();if(Content!=null&&Content.rect.width>20&&Math.Abs(Content.rect.width-_contentWidth)>.5f&&Resize?.Invoke()==true)_contentWidth=Content.rect.width;}
    internal void Refresh()
    {
        if(Tools==null||Scroll==null)return;_width=Tools.rect.width;
        float natural=0;int buttons=0;foreach(Transform group in Tools)foreach(Transform child in group){natural+=child.GetComponent<IntroductionToolWidth>()?.PreferredWidth??86;buttons++;}
        float available=Mathf.Max(1,_width-8-6*Mathf.Max(0,buttons-1));float ratio=Mathf.Min(1,available/Mathf.Max(1,natural));
        foreach(Transform group in Tools)foreach(Transform child in group){var element=child.GetComponent<LayoutElement>();if(element!=null){element.preferredWidth=(child.GetComponent<IntroductionToolWidth>()?.PreferredWidth??86)*ratio;element.minWidth=0;element.flexibleWidth=0;}}
        float height=42;Tools.sizeDelta=new(0,height);Scroll.offsetMax=new(0,Tools.gameObject.activeSelf?-height-4:0);
        LayoutRebuilder.ForceRebuildLayoutImmediate(Tools);
    }
}
internal sealed class IntroductionToolWidth : MonoBehaviour
{
    internal float PreferredWidth;
}
internal sealed class IntroductionBlockSelect : MonoBehaviour,IPointerClickHandler
{
    internal Action? Selected;
    public void OnPointerClick(PointerEventData eventData)=>Selected?.Invoke();
}
internal sealed class IntroductionBlockDrag : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    internal Action? Cancel;
    internal Action<PointerEventData>? Begin,Drag,End;
    private PointerEventData? _pointer;
    public void OnBeginDrag(PointerEventData eventData){if(eventData.button!=PointerEventData.InputButton.Left)return;_pointer=eventData;eventData.eligibleForClick=false;Begin?.Invoke(eventData);}
    public void OnDrag(PointerEventData eventData){if(_pointer!=null)Drag?.Invoke(eventData);}
    public void OnEndDrag(PointerEventData eventData){if(_pointer==null)return;_pointer=null;End?.Invoke(eventData);}
    private void Update(){if(_pointer==null)return;if(TypingGuard.ReadKeyboard(()=>Keyboard.current?.escapeKey.wasPressedThisFrame==true)){_pointer=null;Cancel?.Invoke();return;}Drag?.Invoke(_pointer);}
    private void OnDisable(){if(_pointer!=null){_pointer=null;Cancel?.Invoke();}}
}
internal sealed class IntroductionLinks : MonoBehaviour, IPointerDownHandler,IPointerClickHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    internal TextMeshProUGUI Text=null!;
    internal ScrollRect? DocumentScroll;
    internal Action? Selected;
    private bool _documentDrag,_dragged;
    public void OnPointerDown(PointerEventData data){_dragged=false;}
    public void OnBeginDrag(PointerEventData data){_dragged=true;data.eligibleForClick=false;_documentDrag=TMP_TextUtilities.FindIntersectingLink(Text,data.pressPosition,data.pressEventCamera)<0;if(_documentDrag){DocumentScroll?.OnInitializePotentialDrag(data);DocumentScroll?.OnBeginDrag(data);}}
    public void OnDrag(PointerEventData data){if(_documentDrag)DocumentScroll?.OnDrag(data);}
    public void OnEndDrag(PointerEventData data){if(_documentDrag)DocumentScroll?.OnEndDrag(data);_documentDrag=false;}
    public void OnPointerClick(PointerEventData eventData){if(_dragged){_dragged=false;return;}Selected?.Invoke();int index=TMP_TextUtilities.FindIntersectingLink(Text,eventData.position,eventData.pressEventCamera);if(index<0)return;string url=Text.textInfo.linkInfo[index].GetLinkID();if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&(uri.Scheme=="https"||uri.Scheme=="http"))Application.OpenURL(url);else GUIUtility.systemCopyBuffer=url;}
}
