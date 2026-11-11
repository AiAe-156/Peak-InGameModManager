using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

Harness.Run();
partial class Harness
{
    class OverviewMod
    {
        public string LatestVersion="1", CheckMessage="",UpdateNotes="",Author="",HomepageUrl="",SourceKind="",SourceKey="",IdentityConflict="";
        public bool HasUpdate=false,CategoriesKnown=false,SourceConfirmed=false;
        public string[] Categories=Array.Empty<string>();
    }
    class VirtualRow
    {
        public string Key="",Name="",DisplayStamp="",CachedStamp="";
        public long StampRevision=-1;
        public OverviewMod? Mod;
        public float Y,Height=38,Width=100;
        public RectTransform? View;
        public Action<RectTransform> Bind=_=>{};
        public Lease Lease=new();
    }
    class Lease { public void Bind(string key) {} }
    class Component { public string Key="",Version="",Latest=""; }
    static class InfrastructurePackages { public static List<Component> Results=new(); }
    long _rowDataRevision;
    Dictionary<string,string> _componentUpdateStamps=new(StringComparer.Ordinal);
    List<VirtualRow> _virtualRows=new();
    bool _disposed=false,_opened=true,_building=false,IsEditingText=false,_infrastructureRedraw;
    OverviewMod? _dragged=null;
    RectTransform _content=new(),_viewport=new();
    Scroll _scroll=new();
    float _virtualTop=float.NaN,_virtualViewport=-1;
    const float RowHeight=40;
    class Scroll { public Velocity velocity=new(); }
    class Velocity { public float sqrMagnitude=0; }
    class Idle { public void Remove(VirtualRow row) {} }
    Idle Cache(VirtualRow row)=>new();
    int released,rendered,restored,binds;
    void ReleaseRow(VirtualRow row) { row.View=null;released++; }
    void ParkRow(VirtualRow row) { if(row.View!=null)row.View.gameObject.activeSelf=false; }
    void RefreshSelectionColors() {}
    void LinkRowNavigation() {}
    void WireRowNavigation(VirtualRow row) {}
    // Render itself is too Unity-heavy for this harness: source contracts check its real
    // capture -> clear -> immediate refresh -> restore ordering. This stub models focus.
    void Render() { rendered++; var focus=EventSystem.current?.currentSelectedGameObject; RefreshVirtualRows(); if(focus!=null)restored++; }
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
    public static void Run()
    {
        var h=new Harness();
        for(int i=0;i<300;i++)h._virtualRows.Add(new VirtualRow{Key="mod:"+i,Mod=new OverviewMod(),Y=i*40});
        var group=new VirtualRow{Key="group:a"};
        var component=new VirtualRow{Key="component:core"};
        h._virtualRows.Add(group);h._virtualRows.Add(component);
        InfrastructurePackages.Results.Add(new Component{Key="core",Version="1",Latest="2"});
        h.RefreshRowData();
        foreach(var item in h._virtualRows)h.RowUpdateStamp(item);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<600;frame++)for(int i=0;i<16;i++)h.RowUpdateStamp(h._virtualRows[(frame+i)%300]);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes==0,"exact production RowUpdateStamp: 600 frames x 16 rows = "+bytes+" allocated bytes");
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<9600;i++){h.RowUpdateStamp(group);h.RowUpdateStamp(component);}
        Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"group/component cached stamp reads allocate zero");
        var hidden=h._virtualRows[200];string old=h.RowUpdateStamp(hidden);
        hidden.Mod!.LatestVersion="3";hidden.Mod.HasUpdate=true;
        InfrastructurePackages.Results[0].Latest="4";
        h.RefreshRowData();
        Check(h.RowUpdateStamp(hidden)!=old,"hidden model stamp invalidates on data revision");
        Check(h.RowUpdateStamp(component)=="1\t4","component result snapshot invalidates on revision");
        Check(h.RowUpdateStamp(group)=="","group has constant empty stamp");
        var r=h._virtualRows[0];r.Bind=_=>h.binds++;
        h.RefreshVirtualRows();var original=r.View;
        Check(original!=null && h.binds==1,"actual refresh binds initial visible row");
        h._content.anchoredPosition.y=8000;h.RefreshVirtualRows();
        Check(!original!.gameObject.activeSelf,"actual refresh parks hidden row");
        r.Mod!.LatestVersion="5";h.RefreshRowData();
        h._content.anchoredPosition.y=0;h.RefreshVirtualRows();
        Check(!ReferenceEquals(original,r.View) && h.binds==2,"hidden row rebuilds stale controls on wake");
        var unchanged=r.View;h._content.anchoredPosition.y=8000;h.RefreshVirtualRows();
        h._content.anchoredPosition.y=0;h.RefreshVirtualRows();
        Check(ReferenceEquals(unchanged,r.View) && h.binds==2,"unchanged wake keeps identity and listeners");
        var active=r.View;r.Mod!.CheckMessage="updated while visible";
        h.RefreshRowData();h._virtualTop=float.NaN;h.RefreshVirtualRows();
        Check(!ReferenceEquals(active,r.View),"visible row rebuilds changed content after publication");
        active=r.View;h.RefreshRowData();h._virtualTop=float.NaN;h.RefreshVirtualRows();
        Check(ReferenceEquals(active,r.View),"new revision with identical content retains controls");
        h.ReleaseRow(r);h._virtualTop=float.NaN;int attempts=0;
        r.Bind=_=>{if(++attempts==1)throw new InvalidOperationException("injected bind failure");};
        try{h.RefreshVirtualRows();throw new Exception("expected bind failure");}catch(InvalidOperationException){}
        Check(r.View==null && float.IsNaN(h._virtualTop),"Bind failure clears view and invalidates committed coordinates");
        h.RefreshVirtualRows();Check(attempts==2 && r.View!=null,"same scroll coordinates retry failed Bind next tick");
        EventSystem.current=new EventSystem{currentSelectedGameObject=new GameObject()};
        h._infrastructureRedraw=true;h.FlushInfrastructureRedraw();
        Check(h.rendered==1 && !h._infrastructureRedraw && h.restored==1,"selected row does not starve infrastructure redraw; focus reaches Render");
        h._infrastructureRedraw=true;h.IsEditingText=true;h.FlushInfrastructureRedraw();
        Check(h.rendered==1 && h._infrastructureRedraw,"active editing still defers redraw");
        h.IsEditingText=false;h.FlushInfrastructureRedraw();
        Check(h.rendered==2 && !h._infrastructureRedraw,"deferred publication flushes after editing");
        Console.WriteLine("RESULT 16 production-method checks passed; Unity focus restoration still requires in-game acceptance");
    }
}
class Vector { public float y; }
class Rect { public float height=600; }
class GameObject { public bool activeSelf=true;public RectTransform transform=new(false);public void SetActive(bool value)=>activeSelf=value; }
class RectTransform
{
    public GameObject gameObject=null!;
    public string name="";
    public Vector anchoredPosition=new();public Rect rect=new();
    public RectTransform(bool makeObject=true){if(makeObject)gameObject=new GameObject();}
    public bool IsChildOf(RectTransform other)=>true;
    public void SetParent(RectTransform parent,bool world){}
    public T? GetComponent<T>() where T:new()=>new T();
}
class Image { public Color color; }
struct Color { public Color(float r,float g,float b,float a){} public static Color clear=>new(); }
class OverviewRowInteraction { public bool IsDragging=false; }
class EventSystem { public static EventSystem? current;public GameObject? currentSelectedGameObject; }
static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a); }
static class OverviewUi
{
    public static RectTransform Panel(string name,RectTransform content,Color color)=>new();
    public static void Place(RectTransform r,float x,float y,float w,float h){}
}
static class VirtualRowRange { public static bool Contains(float y,float h,float top,float height,float buffer)=>y+h>=top-buffer && y<=top+height+buffer; }
