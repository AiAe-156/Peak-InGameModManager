#nullable enable
using TMPro;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

// All document wheel events end here once, including nested multiline fields.
internal sealed class IntroductionScrollRect : ScrollRect
{
    public override void OnScroll(PointerEventData data){StopMovement();base.OnScroll(data);}
}

internal sealed class IntroductionTextInput : TMP_InputField
{
    internal ScrollRect? DocumentScroll;
    private bool _selectText, _documentDrag;
    public override void OnScroll(PointerEventData data)
    {
        if(DocumentScroll!=null)DocumentScroll.OnScroll(data);else base.OnScroll(data);
    }
    public override void OnPointerDown(PointerEventData data)
    {
        // A field already being edited keeps normal selection gestures. A first drag
        // across another paragraph navigates the document; a first click still edits.
        _selectText=isFocused;_documentDrag=false;base.OnPointerDown(data);
    }
    public override void OnBeginDrag(PointerEventData data)
    {
        _documentDrag=DocumentScroll!=null&&!_selectText&&data.button==PointerEventData.InputButton.Left;
        if(_documentDrag){DocumentScroll!.OnInitializePotentialDrag(data);DocumentScroll.OnBeginDrag(data);}
        else base.OnBeginDrag(data);
    }
    public override void OnDrag(PointerEventData data){if(_documentDrag)DocumentScroll?.OnDrag(data);else base.OnDrag(data);}
    public override void OnEndDrag(PointerEventData data){if(_documentDrag)DocumentScroll?.OnEndDrag(data);else base.OnEndDrag(data);_documentDrag=false;}
}

// One mesh, irrespective of page width; no per-dash GameObjects.
internal sealed class IntroductionDashedRule : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;float y=r.center.y;
        for(float x=r.xMin;x<r.xMax;x+=16)
        {
            float end=Mathf.Min(x+9,r.xMax);int start=mesh.currentVertCount;
            mesh.AddVert(new Vector3(x,y-1),color,Vector2.zero);mesh.AddVert(new Vector3(x,y+1),color,Vector2.zero);
            mesh.AddVert(new Vector3(end,y+1),color,Vector2.zero);mesh.AddVert(new Vector3(end,y-1),color,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}

internal sealed class IntroductionTextMaterial : MonoBehaviour
{
    private bool _fontLease;
    private TMP_FontAsset? _originalFont;
    internal void Apply(TextMeshProUGUI text)
    {
        text.fontStyle=FontStyles.Normal;text.color=new Color(.94f,.92f,.87f,1);
        foreach(var effect in text.GetComponents<Shadow>())effect.enabled=false;
        if (!_fontLease) { _fontLease = true; IntroductionFontRegistry.Acquire(); _originalFont = text.font; IntroductionFontRegistry.ObserveGameFont(_originalFont); }
        // Default is only the base face. Inline <font> runs select the requested face.
        if (IntroductionFontRegistry.TryResolve(IntroductionFontRegistry.DefaultId, out var font, out _) && font != null) text.font = font;
        IntroductionFontRegistry.EnsureRichFonts(text.text);
        MceTextWeight.Apply(text);
    }
    private void OnDestroy()
    {
        if (!_fontLease) return; _fontLease = false; IntroductionFontRegistry.Release();
    }
}
