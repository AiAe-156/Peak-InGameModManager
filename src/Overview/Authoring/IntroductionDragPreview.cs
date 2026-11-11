#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

// The preview only borrows the existing player texture. It never creates a decoder
// and never destroys a shared texture when the gesture ends.
internal sealed class IntroductionDragPreview
{
    private readonly GameObject _root;
    private readonly RectTransform _parent,_ghost,_marker;
    private readonly Image _markerImage;
    internal IntroductionDragPreview(RectTransform parent,Texture? texture,string caption,System.Func<Transform,string,float,TextMeshProUGUI> factory,System.Action cancel)
    {
        _parent=parent;_root=Node(parent,"DocumentDragPreview");var rect=(RectTransform)_root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        var canvas=_root.AddComponent<CanvasGroup>();canvas.alpha=.72f;canvas.blocksRaycasts=false;canvas.interactable=false;
        _ghost=(RectTransform)Node(_root.transform,"MovingBlock").transform;_ghost.anchorMin=_ghost.anchorMax=new(.5f,.5f);_ghost.pivot=new(0,1);_ghost.sizeDelta=new(176,108);_ghost.gameObject.AddComponent<Image>().color=new(.17f,.2f,.24f,.96f);
        if(texture!=null){var image=Node(_ghost,"Thumbnail").AddComponent<RawImage>();image.texture=texture;image.color=Color.white;image.raycastTarget=false;var imageRect=(RectTransform)image.transform;imageRect.anchorMin=Vector2.zero;imageRect.anchorMax=Vector2.one;imageRect.offsetMin=new(4,4);imageRect.offsetMax=new(-4,-4);var aspect=image.gameObject.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=(float)texture.width/Mathf.Max(1,texture.height);}
        else{var text=factory(_ghost,caption,18);text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Truncate;var textRect=text.rectTransform;textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=new(8,8);textRect.offsetMax=new(-8,-8);}
        _marker=(RectTransform)Node(_root.transform,"DropPosition").transform;_marker.anchorMin=_marker.anchorMax=new(.5f,.5f);_marker.pivot=new(.5f,.5f);_markerImage=_marker.gameObject.AddComponent<Image>();_markerImage.raycastTarget=false;_marker.gameObject.SetActive(false);
        var cleanup=_root.AddComponent<IntroductionDragPreviewLifetime>();cleanup.Closed=cancel;
    }
    private static GameObject Node(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go;}
    internal void Move(PointerEventData data)
    {
        if(RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,data.position,data.pressEventCamera,out var point))_ghost.anchoredPosition=point+new Vector2(16,-16);
    }
    internal void Mark(RectTransform target,bool after,bool inline)
    {
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(_parent,target);
        _markerImage.color=inline?new Color(.22f,.63f,1,.36f):new Color(.22f,.7f,1,.96f);
        _marker.anchoredPosition=inline?new Vector2(after?(bounds.center.x+bounds.max.x)/2:(bounds.min.x+bounds.center.x)/2,bounds.center.y):new Vector2(bounds.center.x,after?bounds.min.y:bounds.max.y);
        _marker.sizeDelta=inline?new Vector2(Mathf.Max(8,bounds.size.x/2),Mathf.Max(8,bounds.size.y)):new Vector2(Mathf.Max(8,bounds.size.x),3);
        _marker.gameObject.SetActive(true);
    }
    internal void HideMarker()=>_marker.gameObject.SetActive(false);
    internal void Close(){if(_root==null)return;_root.SetActive(false);Object.Destroy(_root);}
}
internal sealed class IntroductionDragPreviewLifetime : MonoBehaviour
{
    internal System.Action? Closed;
    private bool _closed;
    private void OnDisable(){if(_closed)return;_closed=true;Closed?.Invoke();}
}
