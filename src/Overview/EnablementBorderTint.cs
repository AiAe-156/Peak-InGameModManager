#nullable enable
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
/// <summary>Keep status border visible when the checkbox's tick alpha or selectable state changes.</summary>
internal sealed class EnablementBorderTint : MonoBehaviour
{
    internal Graphic? Target;
    internal Color Color;
    private void LateUpdate() { if(Target!=null && Target.color!=Color) Target.color=Color; }
}
