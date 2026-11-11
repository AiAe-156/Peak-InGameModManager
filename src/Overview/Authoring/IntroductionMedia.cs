#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ModConfigEnhance.Overview;

internal sealed class IntroductionMediaState
{
    internal bool Playing=true;
    internal double Position;
}
internal sealed class IntroductionMedia : IDisposable
{
    internal const long ImportLimitBytes=1024L*1024*1024;
    internal static int PixelBudget=16*1024*1024;
    private static readonly List<IntroductionMedia> All=new();
    private static readonly LinkedList<IntroductionMedia> Active=new();
    private readonly GameObject _root;
    private readonly RectTransform _home,_viewport;
    private readonly Transform _modalParent;
    private readonly string _type,_path;
    private readonly IntroductionMediaState _state;
    private readonly Action<string>? _status;
    private readonly RawImage _raw;
    private readonly AspectRatioFitter _aspect;
    private readonly RectTransform _stage,_playRect,_zoomRect;
    private readonly string _align;
    private readonly Action? _layoutChanged;
    private Vector2 _lastSize=new(-1,-1);
    private float _lastRatio=-1;
    private bool _lastEnlarged;
    private readonly TextMeshProUGUI _hint;
    private Func<string>? _hintText;
    private int _languageRevision=-1;
    private readonly TMP_Text _playCaption;
    private readonly Slider? _progress;
    private readonly RectTransform? _volumeRect;
    private readonly Slider? _volume;
    private readonly OverviewIconGraphic? _speaker;
    private readonly GameObject? _scaleControls;
    private readonly Slider? _scale;
    private readonly TMP_Text? _scaleCaption;
    private float ControlsHeight=>36+(_scaleControls!=null&&!Enlarged?32:0);
    private Texture2D? _texture;
    private VideoPlayer? _video;
    private RenderTexture? _render;
    private IntroductionGifPlayer? _gif;
    private GameObject? _shade,_previousSelection;
    private bool _loaded,_failed,_disposed;
    private bool _resizing;
    private float _granted=-100,_lastGranted=-100;
    internal bool Enlarged=>_shade!=null;
    // A drag preview borrows this texture; its lifetime remains owned by this media.
    internal Texture? PreviewTexture=>_raw.texture;
    internal float AspectRatio=>Mathf.Max(.01f,_aspect.aspectRatio);
    internal const float ControlBarHeight=36;
    public IntroductionMedia(Transform parent,string type,string path,Func<Transform,string,float,TextMeshProUGUI> factory,Action<string>? status,Action? select,RectTransform viewport,Transform modalParent,IntroductionMediaState state,string align="center",Action? layoutChanged=null,Func<int>? getWidth=null,Action<int>? resize=null,Action? beginResize=null,Func<bool>? clickToggles=null)
    {
        _home=(RectTransform)parent;_viewport=viewport;_modalParent=modalParent;_type=type;_path=path;_status=status;_state=state;_align=align;_layoutChanged=layoutChanged;
        _root=Node(parent,"Media");Fill((RectTransform)_root.transform);
        var driver=_root.AddComponent<IntroductionMediaLifecycle>();driver.Tick=Tick;driver.Release=Release;
        _stage=(RectTransform)Node(_root.transform,"MediaStage").transform;
        var picture=Node(_stage,"Picture");Fill((RectTransform)picture.transform);((RectTransform)picture.transform).offsetMin=new(0,36);
        _raw=picture.AddComponent<RawImage>();_raw.color=Color.clear;
        _aspect=picture.AddComponent<AspectRatioFitter>();_aspect.aspectMode=AspectRatioFitter.AspectMode.None;_aspect.aspectRatio=16f/9;
        var hit=picture.AddComponent<IntroductionMediaInput>();hit.Toggle=()=>{select?.Invoke();if(_type!="image"&&clickToggles?.Invoke()!=false)Toggle();};hit.Enlarge=Enlarge;hit.targetGraphic=_raw;
        _hint=factory(_root.transform,"",20);Fill(_hint.rectTransform);_hint.raycastTarget=false;
        SetHint(()=>Loc.T("Waiting for visible media to play…", "正在等待可见媒体播放…", "正在等待可見媒體播放…"));
        var controls=Node(_stage,"Controls");var cr=(RectTransform)controls.transform;cr.anchorMin=new(0,0);cr.anchorMax=new(1,0);cr.sizeDelta=new(0,32);cr.pivot=new(.5f,0);
        controls.AddComponent<Image>().color=new(.13f,.12f,.1f,.9f);
        var play=OverviewUi.Button(controls.transform,Loc.T("Pause", "暂停", "暫停"),Toggle,70,28, role: UiActionRole.Neutral);_playRect=(RectTransform)play.transform;OverviewUi.Place(_playRect,0,0,70,28);_playCaption=play.GetComponentInChildren<TMP_Text>();_playCaption.enableAutoSizing=true;_playCaption.fontSizeMin=8;
        play.gameObject.AddComponent<IntroductionControlDrag>();
        play.gameObject.SetActive(type!="image");
        var zoom=OverviewUi.Button(controls.transform,Loc.T("Enlarge", "放大", "放大"),Enlarge,60,28, role: UiActionRole.Neutral);var zr=(RectTransform)zoom.transform;_zoomRect=zr;zr.anchorMin=zr.anchorMax=new(1,1);zr.pivot=new(1,1);zr.anchoredPosition=Vector2.zero;zr.sizeDelta=new(60,28);var zoomCaption=zoom.GetComponentInChildren<TMP_Text>();zoomCaption.enableAutoSizing=true;zoomCaption.fontSizeMin=8;
        zoom.gameObject.AddComponent<IntroductionControlDrag>();
        if(type=="gif"||type=="video")
        {
            var track=Node(controls.transform,"Progress");var tr=(RectTransform)track.transform;Fill(tr);tr.offsetMin=new(78,8);tr.offsetMax=new(-68,-8);
            track.AddComponent<Image>().color=new(.35f,.35f,.32f);
            var knob=Node(track.transform,"Handle");var kr=(RectTransform)knob.transform;kr.sizeDelta=new(12,20);var image=knob.AddComponent<Image>();image.color=OverviewUi.Ivory;
            _progress=track.AddComponent<Slider>();_progress.targetGraphic=image;_progress.handleRect=kr;_progress.minValue=0;_progress.maxValue=1;_progress.onValueChanged.AddListener(Seek);
        }
        if(type=="video")
        {
            // Speaker (mute toggle) + slider, between the progress bar and the zoom button.
            var volume=Node(controls.transform,"Volume");_volumeRect=(RectTransform)volume.transform;_volumeRect.anchorMin=_volumeRect.anchorMax=new(1,.5f);_volumeRect.pivot=new(1,.5f);_volumeRect.sizeDelta=new(96,28);
            var mute=Node(volume.transform,"Mute");var mr=(RectTransform)mute.transform;mr.anchorMin=mr.anchorMax=new(0,.5f);mr.pivot=new(0,.5f);mr.sizeDelta=new(26,26);mr.anchoredPosition=Vector2.zero;
            var muteHit=mute.AddComponent<Image>();muteHit.color=Color.clear;
            var icon=Node(mute.transform,"Icon");Fill((RectTransform)icon.transform);_speaker=icon.AddComponent<OverviewIconGraphic>();_speaker.raycastTarget=false;_speaker.color=OverviewUi.Ivory;
            var muteButton=mute.AddComponent<Button>();muteButton.targetGraphic=muteHit;muteButton.onClick.AddListener(ToggleMute);mute.AddComponent<IntroductionControlDrag>();
            var vtrack=Node(volume.transform,"VolumeSlider");var vr=(RectTransform)vtrack.transform;Fill(vr);vr.offsetMin=new(30,10);vr.offsetMax=new(-2,-10);vtrack.AddComponent<Image>().color=new(.35f,.35f,.32f);
            var vfillArea=Node(vtrack.transform,"FillArea");Fill((RectTransform)vfillArea.transform);var vfill=Node(vfillArea.transform,"Fill");Fill((RectTransform)vfill.transform);var vfillImage=vfill.AddComponent<Image>();vfillImage.color=new(.87f,.76f,.45f,.9f);vfillImage.raycastTarget=false;
            var vknob=Node(vtrack.transform,"Handle");var vkr=(RectTransform)vknob.transform;vkr.sizeDelta=new(10,18);var vknobImage=vknob.AddComponent<Image>();vknobImage.color=OverviewUi.Ivory;
            _volume=vtrack.AddComponent<Slider>();_volume.targetGraphic=vknobImage;_volume.handleRect=vkr;_volume.fillRect=(RectTransform)vfill.transform;_volume.minValue=0;_volume.maxValue=1;_volume.SetValueWithoutNotify(Volume);_volume.onValueChanged.AddListener(SetVolume);
            ApplyAudio();
        }
        if(getWidth!=null&&resize!=null)
        {
            _scaleControls=Node(_root.transform,"EditMediaScale");var rect=(RectTransform)_scaleControls.transform;rect.anchorMin=new(0,0);rect.anchorMax=new(1,0);rect.pivot=new(.5f,0);rect.sizeDelta=new(0,28);
            _scaleCaption=factory(_scaleControls.transform,Loc.F("Scale {0}%", "缩放 {0}%", "縮放 {0}%",getWidth()),14);OverviewUi.Place(_scaleCaption.rectTransform,0,0,75,28);_scaleCaption.raycastTarget=false;
            var track=Node(_scaleControls.transform,"ScaleSlider");var tr=(RectTransform)track.transform;Fill(tr);tr.offsetMin=new(80,8);tr.offsetMax=new(-8,-8);track.AddComponent<Image>().color=new(.35f,.35f,.32f);
            var knob=Node(track.transform,"Handle");var kr=(RectTransform)knob.transform;kr.sizeDelta=new(12,20);var image=knob.AddComponent<Image>();image.color=OverviewUi.Ivory;
            var slider=track.AddComponent<IntroductionScaleSlider>();slider.BeginResize=()=>{select?.Invoke();beginResize?.Invoke();_resizing=true;};slider.EndResize=()=>_resizing=false;_scale=slider;slider.targetGraphic=image;slider.handleRect=kr;slider.minValue=10;slider.maxValue=100;slider.wholeNumbers=true;slider.SetValueWithoutNotify(getWidth());
            slider.onValueChanged.AddListener(value=>{resize((int)value);_scaleCaption.text=Loc.F("Scale {0}%", "缩放 {0}%", "縮放 {0}%",(int)value);});
        }
        if(!File.Exists(path)){SetHint(()=>Loc.F("Missing media file: {0}", "媒体文件缺失：{0}", "媒體檔案缺失：{0}",Path.GetFileName(path)));_failed=true;}
        All.Add(this);
    }
    private static GameObject Node(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go;}
    private static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    private bool Visible()
    {
        if(_disposed||!_root.activeInHierarchy)return false;if(Enlarged)return true;
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(_viewport,_home);return bounds.max.y>_viewport.rect.yMin&&bounds.min.y<_viewport.rect.yMax&&bounds.max.x>_viewport.rect.xMin&&bounds.min.x<_viewport.rect.xMax;
    }
    private void Tick()
    {
        if(_languageRevision!=Loc.TranslationRevision)
        {
            _languageRevision=Loc.TranslationRevision;
            if(_hintText!=null)_hint.text=_hintText();
            if(_scaleCaption!=null&&_scale!=null)_scaleCaption.text=Loc.F("Scale {0}%", "缩放 {0}%", "縮放 {0}%",(int)_scale.value);
        }
        CommitVolume();
        LayoutStage();
        if(!Visible()&&!_resizing){if(_loaded)Release();return;}
        if(!_loaded&&!_failed)
        {
            bool moving=_type=="gif"||_type=="video";
            if(moving)
            {
                var next=All.Where(m=>!m._loaded&&!m._failed&&(m._type=="gif"||m._type=="video")&&m.Visible()).OrderBy(m=>m._lastGranted).FirstOrDefault();
                if(next!=this)return;
                if(Active.Count>=2){var oldest=Active.Where(m=>!m.Enlarged).OrderBy(m=>m._granted).FirstOrDefault();if(oldest==null||Time.unscaledTime-oldest._granted<12)return;oldest.Release();}
                Active.AddLast(this);_lastGranted=_granted=Time.unscaledTime;
            }
            Load();
        }
        _playCaption.text=_state.Playing?Loc.T("Pause", "暂停", "暫停"):Loc.T("Play", "播放", "播放");
        WatchStall();
        if(_progress!=null){double duration=_gif?.Duration??(_video!=null&&_video.isPrepared?_video.length:0);double position=_gif?.Position??(_video!=null&&_video.isPrepared?_video.time:0);_progress.interactable=duration>0&&(_gif!=null||(_video!=null&&_video.canSetTime));if(duration>0)_progress.SetValueWithoutNotify((float)(position/duration));}
    }
    private void Load()
    {
        _loaded=true;SetHint(()=>Loc.T("Loading media…", "正在加载媒体…", "正在載入媒體…"));
        try
        {
            if(_type=="video")
            {
                _video=_root.AddComponent<VideoPlayer>();_video.playOnAwake=false;_video.source=VideoSource.Url;_video.url=new Uri(_path).AbsoluteUri;_video.renderMode=VideoRenderMode.RenderTexture;_video.timeUpdateMode=VideoTimeUpdateMode.UnscaledGameTime;_video.audioOutputMode=VideoAudioOutputMode.Direct;_video.isLooping=false;
                _video.prepareCompleted+=Prepared;_video.errorReceived+=VideoError;_video.loopPointReached+=Ended;_video.sendFrameReadyEvents=!_state.Playing;_video.frameReady+=FrameReady;_video.Prepare();
            }
            else if(_type=="gif")
            {
                _gif=_root.AddComponent<IntroductionGifPlayer>();_gif.IsLooping=true;_gif.Open(_path,_raw,_aspect,PixelBudget,Fail);
                if(_failed)return;_gif.SetPlaying(_state.Playing);if(_state.Position>0)_gif.Seek((float)_state.Position);Ready();
            }
            else
            {
                var dimensions=ReadDimensions(_path);if(dimensions.width<1||dimensions.height<1||(long)dimensions.width*dimensions.height>PixelBudget)throw new IOException(Loc.T("The image exceeds the current pixel budget. Reduce its resolution.", "图片超过当前像素预算，请缩小分辨率", "圖片超過當前像素預算，請縮小分辨率"));
                if(new FileInfo(_path).Length>Math.Max(32L*1024*1024,(long)PixelBudget*4))throw new IOException(Loc.T("The image file exceeds the decoding memory budget.", "图片文件超过解码内存预算", "圖片文件超過解碼內存預算"));
                _texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!ImageConversion.LoadImage(_texture,File.ReadAllBytes(_path),true))throw new IOException(Loc.T("PNG/JPEG images are currently supported.", "目前支持 PNG/JPEG 图片", "目前支持 PNG/JPEG 圖片"));_raw.texture=_texture;_aspect.aspectRatio=(float)_texture.width/_texture.height;Ready();
            }
        }
        catch(Exception ex){Fail(ex.Message);}
    }
    private void Ready(){_raw.color=Color.white;_hint.gameObject.SetActive(false);_layoutChanged?.Invoke();LayoutStage();}
    // Width percentages scale one bounded display area. Tall media stay entirely visible;
    // decoder/render texture budgets remain independent of this UI measurement.
    internal float PreferredHeight(float availableWidth,int percent)
    {
        float edge=640*Mathf.Clamp(percent,10,100)/100f;
        float width=Mathf.Min(Mathf.Max(1,availableWidth),edge);
        float ratio=Mathf.Max(.01f,_aspect.aspectRatio);
        return Mathf.Min(width/ratio,edge)+36+(_scaleControls!=null?32:0);
    }
    internal float PreferredWidth(float availableWidth,int percent)=>Mathf.Min(Mathf.Max(1,availableWidth),(PreferredHeight(availableWidth,percent)-36-(_scaleControls!=null?32:0))*Mathf.Max(.01f,_aspect.aspectRatio));
    private void LayoutStage()
    {
        var size=((RectTransform)_root.transform).rect.size;float ratio=Mathf.Max(.01f,_aspect.aspectRatio);
        if((_lastSize-size).sqrMagnitude<.01f&&Math.Abs(_lastRatio-ratio)<.0001f&&_lastEnlarged==Enlarged)return;
        _lastSize=size;_lastRatio=ratio;_lastEnlarged=Enlarged;
        if(_scaleControls!=null)_scaleControls.SetActive(!Enlarged);
        float height=Mathf.Max(1,size.y-ControlsHeight),width=Mathf.Min(Mathf.Max(1,size.x),height*ratio);height=width/ratio;
        float align=_align=="right"?1:_align=="left"?0:.5f;
        _stage.anchorMin=_stage.anchorMax=Enlarged?new Vector2(.5f,.5f):new Vector2(align,1);
        _stage.pivot=Enlarged?new Vector2(.5f,.5f):new Vector2(align,1);_stage.anchoredPosition=Vector2.zero;_stage.sizeDelta=new(width,height+36);
        float playWidth=Mathf.Min(70,width*.24f),zoomWidth=Mathf.Min(60,width*.24f);
        _playRect.sizeDelta=new(playWidth,28);_zoomRect.sizeDelta=new(zoomWidth,28);
        float gap=Mathf.Min(8,width*.04f),volumeWidth=0;
        if(_volumeRect!=null){volumeWidth=Mathf.Min(96,width*.22f);_volumeRect.sizeDelta=new(volumeWidth,28);_volumeRect.anchoredPosition=new(-zoomWidth-gap,0);_volumeRect.gameObject.SetActive(volumeWidth>=40);if(volumeWidth<40)volumeWidth=0;}
        if(_progress!=null){var rect=(RectTransform)_progress.transform;rect.offsetMin=new(playWidth+gap,8);rect.offsetMax=new(-zoomWidth-gap-(volumeWidth>0?volumeWidth+gap:0),-8);_progress.handleRect.sizeDelta=new(Mathf.Min(12,width*.08f),20);}
    }
    private void Prepared(VideoPlayer player)
    {
        if(player!=_video)return;
        try{int width=(int)player.width,height=(int)player.height;if(width<=0||height<=0||(long)width*height>PixelBudget)throw new IOException(Loc.T("Video dimensions exceed the current pixel budget.", "视频画面超过当前像素预算", "視頻畫面超過當前像素預算"));_render=new RenderTexture(width,height,0);if(!_render.Create())throw new IOException(Loc.T("Could not create the video buffer.", "无法创建视频缓存", "無法創建視頻緩存"));player.targetTexture=_render;_raw.texture=_render;_aspect.aspectRatio=(float)width/height;if(player.canSetTime&&_state.Position>0)player.time=Math.Min(_state.Position,player.length);ApplyAudio();Ready();player.Play();}
        catch(Exception ex){Fail(ex.Message);}
    }
    // Frame-ready events force a CPU sync on every frame and make playback stutter; they are only
    // needed once, to show a still frame of a paused video.
    private void FrameReady(VideoPlayer player,long frame){if(player!=_video)return;player.sendFrameReadyEvents=false;if(!_state.Playing)player.Pause();}
    private void VideoError(VideoPlayer player,string error){if(player==_video)Fail(Loc.F("Video format or decoder unavailable: {0}", "视频格式或解码不可用：{0}", "視頻格式或解碼不可用：{0}", error));}
    private void Ended(VideoPlayer player){_state.Playing=false;_state.Position=player.length;}
    private void SetHint(Func<string> text){_hintText=text;_hint.text=text();}
    private void Fail(string error){Release();_failed=true;SetHint(()=>Loc.F("Could not display media: {0} (click to retry)", "媒体无法显示：{0}（点击重试）", "媒體無法顯示：{0}（點擊重試）",Loc.LocalizeMessage(error)));_status?.Invoke(_hint.text);}
    // Some decoders stop presenting frames (time frozen, audio still running) on streams whose declared
    // H.264 level is below their resolution. A seek restarts the pipeline, so nudge the clock when it stalls.
    private double _watchTime=-1;
    private float _watchSince;
    private int _stalls;
    private bool _stallReported;
    private void WatchStall()
    {
        if(_video==null||!_video.isPrepared||!_state.Playing||!_video.isPlaying){_watchTime=-1;return;}
        double time=_video.time;
        if(_watchTime<0||Math.Abs(time-_watchTime)>.001){_watchTime=time;_watchSince=Time.unscaledTime;return;}
        if(Time.unscaledTime-_watchSince<1f||!_video.canSetTime)return;
        _watchSince=Time.unscaledTime;_stalls++;
        _video.time=Math.Min(time+.05,Math.Max(0,_video.length-.1));
        if(_stalls>=3&&!_stallReported)
        {
            _stallReported=true;
            string message=Loc.F("Video repeatedly stalled ({0}×{1}). The decoder may not support its encoding settings. Re-export as H.264, 1080p, or a standard level: {2}", "视频画面多次停住（{0}×{1}）。解码器可能不支持该视频的编码规格，建议用 H.264、1080p 或标准 Level 重新导出：{2}", "影片畫面多次停住（{0}×{1}）。解碼器可能不支援該影片的編碼規格，建議用 H.264、1080p 或標準 Level 重新匯出：{2}",_video.width,_video.height,Path.GetFileName(_path));
            Plugin.Log.LogWarning("[介绍媒体] "+message);_status?.Invoke(message);
        }
    }
    // One volume for every introduction video. Dragging changes it in memory; the config file is
    // written once the mouse is released, not on every slider step.
    private static float? _sessionVolume;
    private static bool _volumeDirty;
    internal static float Volume=>_sessionVolume??Mathf.Clamp01(Plugin.IntroVideoVolume?.Value??.8f);
    internal static bool Muted=>Plugin.IntroVideoMuted?.Value??false;
    private static void SetVolume(float value)
    {
        _sessionVolume=Mathf.Clamp01(value);_volumeDirty=true;
        if(value>0&&Muted&&Plugin.IntroVideoMuted!=null)Plugin.IntroVideoMuted.Value=false;
        ApplyAudioAll();
    }
    private static void ToggleMute()
    {
        if(Plugin.IntroVideoMuted==null)return;
        bool mute=!(Muted||Volume<=0);
        Plugin.IntroVideoMuted.Value=mute;
        if(!mute&&Volume<=0)SetVolume(.5f);
        ApplyAudioAll();
    }
    private static void CommitVolume()
    {
        if(!_volumeDirty||Mouse.current?.leftButton.isPressed==true)return;
        _volumeDirty=false;
        if(Plugin.IntroVideoVolume!=null&&_sessionVolume is float value&&!Mathf.Approximately(Plugin.IntroVideoVolume.Value,value))Plugin.IntroVideoVolume.Value=value;
    }
    private static void ApplyAudioAll(){foreach(var media in All)media.ApplyAudio();}
    private void ApplyAudio()
    {
        bool silent=Muted||Volume<=0;
        if(_speaker!=null&&_speaker.Kind!=(silent?"speaker-muted":"speaker")){_speaker.Kind=silent?"speaker-muted":"speaker";_speaker.SetVerticesDirty();}
        if(_volume!=null&&!Mathf.Approximately(_volume.value,Volume))_volume.SetValueWithoutNotify(Volume);
        if(_video==null)return;
        ushort tracks=Math.Max((ushort)1,_video.audioTrackCount);
        for(ushort track=0;track<tracks;track++){_video.SetDirectAudioVolume(track,Volume);_video.SetDirectAudioMute(track,Muted);}
    }
    private void Toggle()
    {
        if(_failed){_failed=false;SetHint(()=>Loc.T("Reloading…", "正在重新加载…", "正在重新載入…"));return;}
        _state.Playing=!_state.Playing;if(_gif!=null)_gif.SetPlaying(_state.Playing);
        if(_video!=null&&_video.isPrepared){if(_state.Playing){if(_video.time>=_video.length-.05&&_video.canSetTime)_video.time=0;_video.Play();}else _video.Pause();}
        _playCaption.text=_state.Playing?Loc.T("Pause", "暂停", "暫停"):Loc.T("Play", "播放", "播放");
    }
    private void Seek(float fraction){if(_gif!=null){_state.Position=fraction*_gif.Duration;_gif.Seek((float)_state.Position);}else if(_video!=null&&_video.isPrepared&&_video.canSetTime){_state.Position=fraction*_video.length;_video.time=_state.Position;}}
    internal void Enlarge()
    {
        if(Enlarged||_disposed)return;_previousSelection=EventSystem.current?.currentSelectedGameObject;
        _shade=Node(_modalParent,"MediaEnlarged");Fill((RectTransform)_shade.transform);_shade.AddComponent<Image>().color=new(0,0,0,.94f);
        _root.transform.SetParent(_shade.transform,false);Fill((RectTransform)_root.transform);((RectTransform)_root.transform).offsetMin=new(24,24);((RectTransform)_root.transform).offsetMax=new(-24,-64);
        var close=OverviewUi.Button(_shade.transform,Loc.T("Back", "返回", "返回"),()=>CloseEnlarged(),90,36, role: UiActionRole.Neutral);OverviewUi.Place((RectTransform)close.transform,24,14,90,36);EventSystem.current?.SetSelectedGameObject(close.gameObject);
    }
    internal bool CloseEnlarged()
    {
        if(_shade==null)return false;_root.transform.SetParent(_home,false);Fill((RectTransform)_root.transform);var shade=_shade;_shade=null;shade.SetActive(false);UnityEngine.Object.Destroy(shade);if(_previousSelection!=null&&_previousSelection.activeInHierarchy)EventSystem.current?.SetSelectedGameObject(_previousSelection);return true;
    }
    private static (int width,int height) ReadDimensions(string path)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);byte[] start=reader.ReadBytes(24);
        if(start.Length>=24&&start[0]==137&&start[1]==80&&start[2]==78&&start[3]==71)return(Big(start,16),Big(start,20));
        stream.Position=0;if(reader.ReadByte()!=255||reader.ReadByte()!=216)throw new IOException(Loc.T("PNG/JPEG images are currently supported.", "目前支持 PNG/JPEG 图片", "目前支持 PNG/JPEG 圖片"));
        while(stream.Position<stream.Length){if(reader.ReadByte()!=255)continue;byte marker;do{marker=reader.ReadByte();}while(marker==255);if(marker==217||marker==218)break;if(marker==0||marker==216||marker>=208&&marker<=215)continue;int size=(reader.ReadByte()<<8)|reader.ReadByte();if(size<2)break;if(new[]{192,193,194,195,197,198,199,201,202,203,205,206,207}.Contains(marker)){reader.ReadByte();int h=(reader.ReadByte()<<8)|reader.ReadByte(),w=(reader.ReadByte()<<8)|reader.ReadByte();return(w,h);}stream.Seek(size-2,SeekOrigin.Current);}
        throw new IOException(Loc.T("Could not read image dimensions.", "无法读取图片尺寸", "無法讀取圖片尺寸"));
    }
    private static int Big(byte[] data,int offset)=>(data[offset]<<24)|(data[offset+1]<<16)|(data[offset+2]<<8)|data[offset+3];
    private void Release()
    {
        Active.Remove(this);_loaded=false;
        if(_gif!=null){_state.Position=_gif.Position;_gif.Close();UnityEngine.Object.Destroy(_gif);}_gif=null;
        if(_video!=null){if(_video.isPrepared)_state.Position=_video.time;_video.prepareCompleted-=Prepared;_video.errorReceived-=VideoError;_video.loopPointReached-=Ended;_video.frameReady-=FrameReady;_video.Stop();_video.targetTexture=null;UnityEngine.Object.Destroy(_video);}_video=null;
        if(_render!=null){_render.Release();UnityEngine.Object.Destroy(_render);}_render=null;
        if(_texture!=null)UnityEngine.Object.Destroy(_texture);_texture=null;
        _raw.texture=null;_raw.color=Color.clear;if(!_failed)SetHint(()=>Loc.T("Waiting for visible media (two streams maximum)", "等待可见媒体播放（最多同时两路）", "等待可見媒體播放（最多同時兩路）"));_hint.gameObject.SetActive(true);
    }
    public void Dispose(){if(_disposed)return;CloseEnlarged();_disposed=true;All.Remove(this);Release();if(_root!=null)UnityEngine.Object.Destroy(_root);}
}
internal sealed class IntroductionMediaLifecycle : MonoBehaviour
{
    internal Action? Release,Tick;
    private void Update()=>Tick?.Invoke();
    private void OnDisable()=>Release?.Invoke();
}
internal sealed class IntroductionMediaInput : Selectable,IPointerClickHandler,ISubmitHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    internal Action? Toggle,Enlarge;
    private float _single=-1;
    public void OnPointerClick(PointerEventData data){if(data.button!=PointerEventData.InputButton.Left)return;if(data.clickCount>=2){_single=-1;Enlarge?.Invoke();}else _single=Time.unscaledTime+.35f;}
    public void OnSubmit(BaseEventData data)=>Toggle?.Invoke();
    public void OnBeginDrag(PointerEventData data){_single=-1;data.eligibleForClick=false;}
    public void OnDrag(PointerEventData data){}
    public void OnEndDrag(PointerEventData data){}
    private void Update(){if(_single>=0&&Time.unscaledTime>=_single){_single=-1;Toggle?.Invoke();}if(EventSystem.current?.currentSelectedGameObject==gameObject&&Gamepad.current?.buttonWest.wasPressedThisFrame==true)Enlarge?.Invoke();}
    public override void OnDisable(){_single=-1;base.OnDisable();}
}
internal sealed class IntroductionScaleSlider : Slider
{
    internal Action? BeginResize,EndResize;
    public override void OnPointerDown(PointerEventData data){BeginResize?.Invoke();base.OnPointerDown(data);}
    public override void OnPointerUp(PointerEventData data){base.OnPointerUp(data);EndResize?.Invoke();}
    public override void OnMove(AxisEventData data){BeginResize?.Invoke();base.OnMove(data);EndResize?.Invoke();}
    public override void OnDisable(){EndResize?.Invoke();base.OnDisable();}
}
internal sealed class IntroductionControlDrag : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    public void OnBeginDrag(PointerEventData data){data.eligibleForClick=false;}
    public void OnDrag(PointerEventData data){}
    public void OnEndDrag(PointerEventData data){}
}

