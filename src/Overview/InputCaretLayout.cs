#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
/// <summary>One visual caret; TMP still owns editing, selection, scrolling and IME.</summary>
internal sealed class InputCaretLayout : MonoBehaviour
{
    private TMP_InputField? _input;
    private RectTransform? _caret;
    private Color _original;
    private bool _originalCustom, _captured;
    private void LateUpdate()
    {
        _input??=GetComponent<TMP_InputField>();if(_input==null||_input.textComponent==null||_input.textViewport==null)return;
        if(!_captured){_original=_input.caretColor;_originalCustom=_input.customCaretColor;_captured=true;}
        var text=_input.textComponent;
        if(_caret==null){_caret=OverviewUi.Rect("AlignedCaret",_input.textViewport);var image=_caret.gameObject.AddComponent<Image>();image.color=_original;image.raycastTarget=false;}
        _input.customCaretColor=true;_input.caretColor=Color.clear;
        bool show=_input.isFocused&&(_input.caretBlinkRate<=0||(Time.unscaledTime*_input.caretBlinkRate)%1f<.5f)&&_input.selectionAnchorPosition==_input.selectionFocusPosition;
        _caret.gameObject.SetActive(show);if(!show)return;
        text.ForceMeshUpdate();var info=text.textInfo;int position=_input.caretPosition;
        float x=0,top,bottom;
        if(info.characterCount>0&&(!string.IsNullOrEmpty(_input.text)||!string.IsNullOrEmpty(Input.compositionString))){var edge=UiGeometry.CaretCharacter(position,info.characterCount);var c=info.characterInfo[edge.Index];x=edge.Advance?c.xAdvance:c.origin;var line=info.lineInfo[Mathf.Clamp(c.lineNumber,0,info.lineCount-1)];top=line.ascender;bottom=line.descender;}
        else
        {
            x=text.rectTransform.rect.xMin+text.margin.x;
            var face=text.font.faceInfo;float scale=text.fontSize/Mathf.Max(1,face.pointSize)*face.scale;
            if(_input.lineType==TMP_InputField.LineType.SingleLine)
            {
                var metrics=UiGeometry.EmptyCaret(text.rectTransform.rect.center.y,face.ascentLine*scale,face.descentLine*scale);
                top=metrics.Top;bottom=top-metrics.Height;
            }
            else {top=text.rectTransform.rect.yMax-text.margin.y;bottom=top-(face.ascentLine-face.descentLine)*scale;}
        }
        var viewport=_input.textViewport;
        Vector3 local=viewport.InverseTransformPoint(text.rectTransform.TransformPoint(new Vector3(x,top,0)));
        Vector3 lower=viewport.InverseTransformPoint(text.rectTransform.TransformPoint(new Vector3(x,bottom,0)));
        _caret.anchorMin=_caret.anchorMax=new Vector2(0,1);_caret.pivot=new Vector2(0,1);
        _caret.anchoredPosition=new Vector2(local.x-viewport.rect.xMin,local.y-viewport.rect.yMax);_caret.sizeDelta=new Vector2(_input.caretWidth,Mathf.Max(1,local.y-lower.y));
    }
    private void OnDisable()
    {
        if(_caret!=null)_caret.gameObject.SetActive(false);
        if(_input!=null&&_captured){_input.customCaretColor=_originalCustom;_input.caretColor=_original;}_captured=false;
    }
    private void OnDestroy(){if(_caret!=null)Destroy(_caret.gameObject);}
}

/// <summary>TMP normally keeps placeholders until the first keystroke; here focus alone hides it.</summary>
internal sealed class InputPlaceholderFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private TMP_InputField? _input;
    private void Sync(){_input??=GetComponent<TMP_InputField>();if(_input?.placeholder!=null)_input.placeholder.gameObject.SetActive(!_input.isFocused&&string.IsNullOrEmpty(_input.text));}
    public void OnSelect(BaseEventData data){if(GetComponent<TMP_InputField>().placeholder is Graphic hint)hint.gameObject.SetActive(false);}
    public void OnDeselect(BaseEventData data){_input??=GetComponent<TMP_InputField>();if(_input.placeholder!=null)_input.placeholder.gameObject.SetActive(string.IsNullOrEmpty(_input.text));}
    private void LateUpdate()=>Sync();
}
