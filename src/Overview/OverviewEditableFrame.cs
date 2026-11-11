#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Author-mode hint only. The overlay never changes its target's layout or handles input.</summary>
internal static class OverviewEditableFrame
{
    private sealed class Entry {internal OverviewEditableFrameGraphic Graphic=null!;internal bool Requested;}
    private static readonly Dictionary<RectTransform,Entry> Frames=new();
    private static bool _enabled=true;
    internal static void Set(RectTransform target,bool active)
    {
        if(target==null)return;
        if(!Frames.TryGetValue(target,out var entry)||entry.Graphic==null)
        {
            if(!active)return;
            var overlay=new GameObject("MCE_EditableFrame",typeof(RectTransform));overlay.transform.SetParent(target,false);
            var rect=(RectTransform)overlay.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=new Vector2(-2,-2);rect.offsetMax=new Vector2(2,2);
            overlay.AddComponent<LayoutElement>().ignoreLayout=true;
            var graphic=overlay.AddComponent<OverviewEditableFrameGraphic>();graphic.Target=target;graphic.raycastTarget=false;graphic.color=new Color(.82f,.75f,.56f,.85f);
            entry=new Entry{Graphic=graphic};Frames[target]=entry;
        }
        entry.Requested=active;entry.Graphic.gameObject.SetActive(active&&_enabled);
    }
    internal static void SetEnabled(bool active)
    {
        _enabled=active;foreach(var entry in Frames.Values)if(entry.Graphic!=null)entry.Graphic.gameObject.SetActive(active&&entry.Requested);
    }
    internal static void ResetFrames()
    {
        foreach(var entry in Frames.Values)if(entry.Graphic!=null){entry.Graphic.gameObject.SetActive(false);UnityEngine.Object.Destroy(entry.Graphic.gameObject);}
        Frames.Clear();_enabled=true;
    }
    internal static void Remove(RectTransform? target,OverviewEditableFrameGraphic graphic)
    {
        if(ReferenceEquals(target,null))return;if(Frames.TryGetValue(target!,out var entry)&&ReferenceEquals(entry.Graphic,graphic))Frames.Remove(target!);
    }
}

internal sealed class OverviewEditableFrameGraphic : MaskableGraphic
{
    internal RectTransform? Target;
    public override void OnDestroy(){OverviewEditableFrame.Remove(Target,this);base.OnDestroy();}
    public override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();SetVerticesDirty();}
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect rect=rectTransform.rect;if(rect.width<2||rect.height<2)return;
        const float thickness=1.2f,dash=6,gap=4;float radius=Math.Min(7,Math.Min(rect.width,rect.height)/2);float phase=0;
        var points=new List<Vector2>(49);
        Vector2[] centers={new(rect.xMax-radius,rect.yMax-radius),new(rect.xMin+radius,rect.yMax-radius),new(rect.xMin+radius,rect.yMin+radius),new(rect.xMax-radius,rect.yMin+radius)};
        for(int corner=0;corner<4;corner++)for(int step=0;step<=10;step++){float angle=(corner*90+step*9)*Mathf.Deg2Rad;points.Add(centers[corner]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius);}
        points.Add(points[0]);
        for(int i=0;i+1<points.Count;i++)
        {
            Vector2 a=points[i],delta=points[i+1]-a;float length=delta.magnitude;if(length<.001f)continue;Vector2 direction=delta/length;float walked=0;
            while(walked<length)
            {
                float cycle=phase%(dash+gap),piece=Math.Min(length-walked,(cycle<dash?dash:dash+gap)-cycle);if(piece<.001f){phase+=.001f;continue;}
                if(cycle<dash){Vector2 begin=a+direction*walked,end=begin+direction*piece,normal=new Vector2(-direction.y,direction.x)*thickness*.5f;int index=vh.currentVertCount;vh.AddVert(begin-normal,color,Vector2.zero);vh.AddVert(begin+normal,color,Vector2.zero);vh.AddVert(end+normal,color,Vector2.zero);vh.AddVert(end-normal,color,Vector2.zero);vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);}
                walked+=piece;phase+=piece;
            }
        }
    }
}
