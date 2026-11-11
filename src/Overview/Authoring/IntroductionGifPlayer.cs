#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

// Original bounded GIF89a decoder. Keeps one canvas and one restore canvas; image data
// is read directly from GIF sub-blocks rather than retaining the file or frame textures.
internal sealed class IntroductionGifPlayer : MonoBehaviour
{
    private BinaryReader? _reader;
    private Texture2D? _texture;
    private Color32[] _canvas=Array.Empty<Color32>(),_restore=Array.Empty<Color32>(),_global=Array.Empty<Color32>();
    private int _width,_height,_disposal,_transparent=-1,_delay=10,_lastDisposal,_lastX,_lastY,_lastWidth,_lastHeight,_backgroundIndex;
    private long _framesStart;
    private float _next;
    private readonly List<float> _times=new();
    private int _frame=-1,_seekTarget=-1;
    internal bool IsPlaying { get; private set; } = true;
    internal bool IsLooping { get; set; } = true;
    internal float Duration { get; private set; }
    internal float Position => _frame>=0&&_frame<_times.Count ? _times[_frame] : 0;
    internal void SetPlaying(bool playing){IsPlaying=playing;_next=Time.unscaledTime+Math.Max(.02f,_delay/100f);}
    internal void Seek(float seconds)
    {
        if(_reader==null||_times.Count==0)return;
        int index=_times.BinarySearch(Mathf.Clamp(seconds,0,Duration));if(index<0)index=Math.Max(0,~index-1);
        _seekTarget=Math.Min(index,_times.Count-1);_reader.BaseStream.Position=_framesStart;FillBackground();_lastDisposal=0;_lastWidth=0;_frame=-1;_disposal=0;_transparent=-1;_delay=10;
    }
    private void IndexFrames()
    {
        var reader=_reader!;float elapsed=0;int delay=10;
        while(true){int marker=reader.ReadByte();if(marker==59)break;
            if(marker==33){int label=reader.ReadByte();if(label==249){if(reader.ReadByte()!=4)throw new InvalidDataException(Loc.T("Invalid GIF control block", "GIF 控制块无效", "GIF 控制塊無效"));reader.ReadByte();delay=reader.ReadUInt16();reader.ReadByte();if(reader.ReadByte()!=0)throw new InvalidDataException(Loc.T("Invalid GIF control block", "GIF 控制块无效", "GIF 控制塊無效"));}else SkipBlocks();}
            else if(marker==44){if(_times.Count>=100000)throw new InvalidDataException(Loc.T("GIF exceeds the 100,000-frame limit", "GIF 帧数超过100000上限", "GIF 幀數超過100000上限"));_times.Add(elapsed);elapsed+=Math.Max(.02f,delay/100f);delay=10;reader.BaseStream.Seek(8,SeekOrigin.Current);int flags=reader.ReadByte();if((flags&128)!=0)reader.BaseStream.Seek(3*(1<<((flags&7)+1)),SeekOrigin.Current);reader.ReadByte();SkipBlocks();}
            else throw new InvalidDataException(Loc.T("Invalid GIF frame marker", "GIF 帧标记无效", "GIF 幀標記無效"));
        }
        if(_times.Count==0)throw new InvalidDataException(Loc.T("GIF has no playable frames", "GIF 没有可播放的帧", "GIF 沒有可播放的幀"));Duration=elapsed;reader.BaseStream.Position=_framesStart;
    }
    private Action<string>? _status;
    private bool _closed;
    private Color32 _background;
    internal void Open(string path,RawImage image,AspectRatioFitter aspect,int budget,Action<string>? status)
    {
        _status=status;
        try
        {
            _reader=new BinaryReader(File.OpenRead(path));string signature=new string(_reader.ReadChars(6));if(signature!="GIF87a"&&signature!="GIF89a")throw new InvalidDataException(Loc.T("Invalid GIF header", "GIF 文件头无效", "GIF 文件頭無效"));
            _width=_reader.ReadUInt16();_height=_reader.ReadUInt16();if(_width<1||_height<1||(long)_width*_height>budget)throw new InvalidDataException(Loc.T("GIF exceeds the current pixel budget", "GIF 超过当前像素预算", "GIF 超過當前像素預算"));
            byte packed=_reader.ReadByte();int background=_reader.ReadByte();_backgroundIndex=background;_reader.ReadByte();if((packed&128)!=0)_global=Palette(1<<((packed&7)+1));_background=background<_global.Length?_global[background]:new Color32(0,0,0,0);
            _canvas=new Color32[_width*_height];_restore=new Color32[_canvas.Length];FillBackground();_texture=new Texture2D(_width,_height,TextureFormat.RGBA32,false);image.texture=_texture;aspect.aspectRatio=(float)_width/_height;_framesStart=_reader.BaseStream.Position;IndexFrames();ReadFrame();
        }
        catch(Exception ex){Close();status?.Invoke(Loc.T("Could not display GIF: ", "GIF 无法显示：", "GIF 無法顯示：")+ex.Message);}
    }
    private Color32[] Palette(int count){var result=new Color32[count];for(int i=0;i<count;i++)result[i]=new Color32(_reader!.ReadByte(),_reader.ReadByte(),_reader.ReadByte(),255);return result;}
    private void Update()
    {
        if(_closed||_reader==null)return;
        try{if(_seekTarget>=0){for(int i=0;i<2&&_frame<_seekTarget;i++)ReadFrame();if(_frame>=_seekTarget)_seekTarget=-1;}
            else if(IsPlaying&&Time.unscaledTime>=_next)ReadFrame();}
        catch(Exception ex){Close();_status?.Invoke(Loc.T("GIF playback stopped: ", "GIF 播放停止：", "GIF 播放停止：")+ex.Message);}
    }
    private void FillBackground(){for(int i=0;i<_canvas.Length;i++)_canvas[i]=_background;}
    private void ReadFrame()
    {
        var reader=_reader!;int loops=0;
        while(true)
        {
            int marker=reader.ReadByte();
            if(marker==59)
            {if(!IsLooping){IsPlaying=false;return;}if(++loops>1)throw new InvalidDataException(Loc.T("GIF has no playable frames", "GIF 没有可播放的帧", "GIF 沒有可播放的幀"));reader.BaseStream.Position=_framesStart;FillBackground();_lastDisposal=0;_frame=-1;continue;}
            if(marker==33)
            {
                int label=reader.ReadByte();if(label==249){int length=reader.ReadByte();if(length!=4)throw new InvalidDataException(Loc.T("Invalid GIF control block", "GIF 控制块无效", "GIF 控制塊無效"));int packed=reader.ReadByte();_disposal=(packed>>2)&7;_delay=reader.ReadUInt16();int transparent=reader.ReadByte();_transparent=(packed&1)!=0?transparent:-1;if(_transparent==_backgroundIndex){_background.a=0;if(_lastWidth==0)FillBackground();}if(reader.ReadByte()!=0)throw new InvalidDataException(Loc.T("Invalid GIF control block terminator", "GIF 控制块终止符无效", "GIF 控制塊終止符無效"));}
                else SkipBlocks();continue;
            }
            if(marker!=44)throw new InvalidDataException(Loc.T("Invalid GIF frame marker", "GIF 帧标记无效", "GIF 幀標記無效"));
            if(_lastDisposal==2)for(int y=_lastY;y<_lastY+_lastHeight;y++)for(int x=_lastX;x<_lastX+_lastWidth;x++)_canvas[(_height-1-y)*_width+x]=_background;
            else if(_lastDisposal==3)Array.Copy(_restore,_canvas,_canvas.Length);
            int left=reader.ReadUInt16(),top=reader.ReadUInt16(),width=reader.ReadUInt16(),height=reader.ReadUInt16();if(width<1||height<1||left+width>_width||top+height>_height)throw new InvalidDataException(Loc.T("GIF frame dimensions are out of bounds", "GIF 帧尺寸越界", "GIF 幀尺寸越界"));
            int flags=reader.ReadByte();var palette=(flags&128)!=0?Palette(1<<((flags&7)+1)):_global;if(palette.Length==0)throw new InvalidDataException(Loc.T("GIF color table is missing", "GIF 缺少色表", "GIF 缺少色表"));
            if(_disposal==3)Array.Copy(_canvas,_restore,_canvas.Length);
            int minimum=reader.ReadByte();var bits=new GifBits(reader);int pixel=0,pass=0,row=0;
            Decode(bits,minimum,width*height,index=>
            {
                int x=pixel%width;if(index!=_transparent){if(index>=palette.Length)throw new InvalidDataException(Loc.T("GIF color index is out of bounds", "GIF 颜色索引越界", "GIF 顏色索引越界"));_canvas[(_height-1-(top+row))*_width+left+x]=palette[index];}pixel++;
                if(pixel%width==0){if((flags&64)==0)row++;else{int[] steps={8,8,4,2},starts={0,4,2,1};row+=steps[pass];while(row>=height&&pass<3)row=starts[++pass];}}
            });bits.Drain();
            if(pixel!=width*height)throw new InvalidDataException(Loc.T("GIF frame pixels are incomplete", "GIF 帧像素不完整", "GIF 幀像素不完整"));_texture!.SetPixels32(_canvas);_texture.Apply(false,false);
            _frame++;_lastX=left;_lastY=top;_lastWidth=width;_lastHeight=height;_lastDisposal=_disposal;_next=Time.unscaledTime+Math.Max(.02f,_delay/100f);_disposal=0;_transparent=-1;_delay=10;return;
        }
    }
    private static void Decode(GifBits bits,int minimum,int budget,Action<byte> output)
    {
        if(minimum<2||minimum>8)throw new InvalidDataException(Loc.T("Invalid GIF LZW bit count", "GIF LZW 位数无效", "GIF LZW 位數無效"));
        int clear=1<<minimum,end=clear+1,next=end+1,size=minimum+1,old=-1;byte first=0;var prefix=new short[4096];var suffix=new byte[4096];var stack=new byte[4096];for(int i=0;i<clear;i++)suffix[i]=(byte)i;int produced=0;
        while(true)
        {
            int code=bits.Read(size);if(code<0)throw new EndOfStreamException(Loc.T("GIF data is truncated", "GIF 数据截断", "GIF 數據截斷"));if(code==end)break;if(code==clear){next=end+1;size=minimum+1;old=-1;continue;}int original=code,count=0;
            if(code>=next){if(code!=next||old<0)throw new InvalidDataException(Loc.T("Invalid GIF LZW dictionary", "GIF LZW 字典错误", "GIF LZW 字典錯誤"));stack[count++]=first;code=old;}
            while(code>=clear){if(code>=next||count>=4095)throw new InvalidDataException(Loc.T("Invalid GIF LZW chain", "GIF LZW 链错误", "GIF LZW 鏈錯誤"));stack[count++]=suffix[code];code=prefix[code];}
            first=suffix[code];stack[count++]=first;
            for(int i=count-1;i>=0;i--){if(++produced>budget)throw new InvalidDataException(Loc.T("GIF frame decoding exceeded bounds", "GIF 帧解码越界", "GIF 幀解碼越界"));output(stack[i]);}
            if(old>=0&&next<4096){prefix[next]=(short)old;suffix[next]=first;next++;if(next==(1<<size)&&size<12)size++;}old=original;
        }
    }
    private void SkipBlocks(){int n;while((n=_reader!.ReadByte())!=0){if(_reader.ReadBytes(n).Length!=n)throw new EndOfStreamException();}}
    internal void Close(){_closed=true;_reader?.Dispose();_reader=null;if(_texture!=null)Destroy(_texture);_texture=null;_canvas=_restore=_global=Array.Empty<Color32>();}
    private void OnDestroy()=>Close();
    private sealed class GifBits
    {
        private readonly BinaryReader _reader;private int _remaining,_bits,_buffer;private bool _end;
        internal GifBits(BinaryReader reader){_reader=reader;}
        private int Byte(){if(_end)return -1;if(_remaining==0){_remaining=_reader.ReadByte();if(_remaining==0){_end=true;return -1;}}_remaining--;return _reader.ReadByte();}
        internal int Read(int count){while(_bits<count){int b=Byte();if(b<0)return -1;_buffer|=b<<_bits;_bits+=8;}int value=_buffer&((1<<count)-1);_buffer>>=count;_bits-=count;return value;}
        internal void Drain(){if(_end)return;while(_remaining>0){_reader.ReadByte();_remaining--;}int n;while((n=_reader.ReadByte())!=0){if(_reader.ReadBytes(n).Length!=n)throw new EndOfStreamException();}_end=true;}
    }
}
