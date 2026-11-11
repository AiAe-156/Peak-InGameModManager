#nullable enable
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>MCE-only materials. Font/atlas/character metrics and author markup stay unchanged.</summary>
internal static class MceTextWeight
{
    private const string Suffix = " (MCE balanced)";
    private static readonly Dictionary<Material, Material> Materials = new();
    private static readonly HashSet<Material> Balanced = new();
    internal static Material MaterialFor(Material source)
    {
        if (Balanced.Contains(source)) return source;
        if (!Materials.TryGetValue(source, out var material) || material == null)
        {
            material = new Material(source) { name = source.name + Suffix };
            void Set(string key, float value) { if (material.HasProperty(key)) material.SetFloat(key, value); }
            Set("_WeightNormal", 0); Set("_WeightBold", .16f); Set("_OutlineWidth", 0); Set("_FaceDilate", -.04f);
            material.DisableKeyword("OUTLINE_ON"); material.DisableKeyword("UNDERLAY_ON"); material.DisableKeyword("UNDERLAY_INNER");
            Object.DontDestroyOnLoad(material); Materials[source] = material; Balanced.Add(material);
        }
        return material;
    }
    internal static void Apply(TextMeshProUGUI text)
    {
        text.fontWeight = FontWeight.Regular;
        foreach (var shadow in text.GetComponents<Shadow>()) shadow.enabled = false;
        if (text.fontSharedMaterial != null) text.fontSharedMaterial = MaterialFor(text.fontSharedMaterial);
        var binding = text.GetComponent<MceTextWeightBinding>() ?? text.gameObject.AddComponent<MceTextWeightBinding>();
        binding.Target = text; binding.Refresh();
    }
}

/// <summary>Font switches and inline/fallback submeshes appear after initial text construction.</summary>
internal sealed class MceTextWeightBinding : MonoBehaviour
{
    internal TextMeshProUGUI? Target;
    private TMP_FontAsset? _font;
    private Material? _base;
    private TextMeshProUGUI? _subscribed;
    private bool _dirty = true;
    private int _children = -1;
    private void MeshChanged(TMP_TextInfo _) { _dirty=true;OverviewMaintenance.Mark(this,Check); }
    private void OnEnable() { _dirty=true;OverviewMaintenance.RegisterEvent(this,Check); }
    private void OnTransformChildrenChanged() { _dirty=true;OverviewMaintenance.Mark(this,Check); }
    private void OnDisable() { OverviewMaintenance.Remove(this); if (_subscribed != null) _subscribed.OnPreRenderText -= MeshChanged; _subscribed = null; }
    private void Check()
    {
        if(Target!=null && (_dirty || _base!=Target.fontSharedMaterial || _font!=Target.font || _children!=transform.childCount)) Refresh();
    }
    internal void Refresh()
    {
        var text = Target; if (text == null) return;
        if(_subscribed!=text) { if(_subscribed!=null)_subscribed.OnPreRenderText-=MeshChanged; _subscribed=text; text.OnPreRenderText+=MeshChanged; }
        _dirty=false; _children=transform.childCount;
        var material = text.fontSharedMaterial;
        if (material != null && (_base != material || _font != text.font))
        { text.fontSharedMaterial = MceTextWeight.MaterialFor(material); _base = text.fontSharedMaterial; _font = text.font; }
        foreach (var sub in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
        {
            // Sprite runs use a different shader; only rebalance actual font submeshes.
            if (sub.fontAsset == null || sub.sharedMaterial == null || !sub.sharedMaterial.HasProperty("_WeightNormal")) continue;
            var balanced = MceTextWeight.MaterialFor(sub.sharedMaterial);
            if (sub.sharedMaterial != balanced) sub.sharedMaterial = balanced;
        }
    }
}
