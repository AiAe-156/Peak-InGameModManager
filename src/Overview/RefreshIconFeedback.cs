#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModConfigEnhance.Overview;

internal sealed class RefreshIconFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private readonly RefreshIconFeedbackState _state = new();
    private OverviewIconGraphic _icon = null!;
    private Color _baseColor;
    private Vector3 _baseScale;
    private Quaternion _baseRotation;
    private Coroutine? _transition;
    internal void Bind(OverviewIconGraphic icon)
    {
        _icon = icon; _baseColor = icon.color;
        // The graphic stretches inside a stable 32px hit target. A top-left pivot
        // makes rotation orbit the corner; symmetric offsets stay identical with
        // a centered pivot, including after later parent-width changes.
        var rect=icon.rectTransform;var min=rect.offsetMin;var max=rect.offsetMax;
        rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=min;rect.offsetMax=max;
        _baseScale = icon.transform.localScale; _baseRotation = icon.transform.localRotation;
    }
    internal void SetAppearance(bool available, Color color)
    {
        if (_baseColor == color && _state.Available == available) return;
        _baseColor = color;
        if (_state.Available != available)
        {
            _state.Available = available;
            if (!available) { StopTransition(); _state.Reset(); }
        }
        Apply();
    }
    public void OnPointerEnter(PointerEventData e) { _state.Pointer = true; Wake(); }
    public void OnPointerExit(PointerEventData e) { _state.Pointer = false; Wake(); }
    public void OnSelect(BaseEventData e) { _state.Focus = true; Wake(); }
    public void OnDeselect(BaseEventData e) { _state.Focus = false; Wake(); }
    private void Wake()
    {
        if (isActiveAndEnabled && _icon != null && _state.NeedsFrame && _transition == null)
            _transition = StartCoroutine(Animate());
    }
    private IEnumerator Animate()
    {
        // Yield before stepping so the Coroutine handle is assigned even at zero delta.
        while (_state.NeedsFrame)
        {
            yield return null;
            _state.Step(Time.unscaledDeltaTime); Apply();
        }
        _transition = null;
    }
    private void Apply()
    {
        if (_icon == null) return;
        float amount = _state.Amount;
        _icon.transform.localScale = _baseScale * (1 + .09f * amount);
        _icon.transform.localRotation = _baseRotation * Quaternion.Euler(0, 0, -18 * amount);
        Color color = Color.Lerp(_baseColor, Color.white, .28f * amount);
        if (_icon.color != color) _icon.color = color;
    }
    private void StopTransition() { if (_transition != null) StopCoroutine(_transition); _transition = null; }
    private void OnDisable() { StopTransition(); _state.Reset(); Apply(); }
}
