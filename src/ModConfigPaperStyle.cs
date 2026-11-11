#nullable enable
using System;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 「纸面」样式：游戏原版 UI 的底框是一张手绘的、带细微圆角与褶皱质感的 sprite，描边还会以大约每秒三帧的节奏
/// 轻微抖动（手绘动画的「沸腾线」）。本项在运行时从原版对象上把这套东西抄下来，给左栏的行、表头、
/// 工具行、复选框、说明面板用，让它们和原版一个手感。
///
/// 底图来源按顺序找：① 选项单元格预制体（PEAKLib.UI 的 <c>Templates.SettingsCellPrefab</c>）里不在选项内容容器下、
/// 面积最大的 Image；② 页面上的搜索框（原版 FloatSettingCell 输入框）。
/// 抖动不是换帧也不是 Animator，而是着色器 <c>Scouts/UI</c> 的材质「UI」自己在动（sprite UI_Blur / UI_Banner 都用它）；
/// 预制体那份叫「UI 1」的材质不抖。所以只要把会抖的那份材质借过来，自建元素就与原版同步抖动。
/// 全部找不到时退回自绘的圆角 sprite（<see cref="ModConfigSidebarWidgets.ApplyRounded"/>）。
/// </summary>
internal static class ModConfigPaperStyle
{
    private const string WobblyShader = "Scouts/UI";

    private static bool _resolved;
    private static Sprite? _sprite;
    private static Material? _material;
    private static Image.Type _type = Image.Type.Sliced;
    private static float _ppu = 1f;

    internal static bool Available => _sprite != null;

    internal static void Resolve(Transform page)
    {
        if (_resolved && _sprite != null && WobblyMaterial != null)
        {
            return;
        }

        // The pause-menu entry can be built before the settings page and its animated material.
        // Retry on later page entries rather than permanently caching that early fallback.
        _resolved = false;
        try
        {
            string source = "";
            Image? image = null;
            GameObject? prefab = Templates.SettingsCellPrefab;
            if (prefab != null)
            {
                image = FindBackdrop(prefab);
                source = "SettingsCell 预制体";
            }

            if (image == null || image.sprite == null)
            {
                Transform? search = page.Find("SearchInput");
                TMP_InputField? input = search != null ? search.GetComponentInChildren<TMP_InputField>(true) : null;
                image = input != null && input.image != null && input.image.sprite != null ? input.image : (search != null ? FindBackdrop(search.gameObject) : null);
                source = "搜索框";
            }

            if (image == null || image.sprite == null)
            {
                Plugin.Log.LogWarning("[面板布局] 没找到原版纸面底图（预制体与搜索框都没有带 sprite 的 Image），左栏用自绘圆角。");
                return;
            }

            _sprite = image.sprite;
            _material = image.material;
            _type = image.type == Image.Type.Simple ? Image.Type.Sliced : image.type;
            _ppu = image.pixelsPerUnitMultiplier;

            // 抖动在着色器里（Scouts/UI）：SettingsCell 预制体那份材质叫「UI 1」，实机不抖；搜索框 / 返回按钮 /
            // 筛选下拉框用的是「UI」，会抖。优先拿会抖的那份。
            Material? wobbly = FindWobblyMaterial(page);
            if (wobbly != null)
            {
                _material = wobbly;
            }

            _resolved = WobblyMaterial != null;

            Plugin.Log.LogInfo($"[面板布局] 纸面样式来源：{source} / {image.name}，sprite {_sprite.name}（{_type}，倍率 {_ppu:0.##}），材质 {(_material != null ? _material.name + "/" + (_material.shader != null ? _material.shader.name : "?") : "null")}{(WobblyMaterial != null ? "（会抖）" : "（不抖）")}。");
        }
        catch (Exception ex)
        {
            _sprite = null;
            Plugin.Log.LogWarning($"[面板布局] 解析原版纸面样式失败，左栏用自绘圆角: {ex.Message}");
        }
    }

    /// <summary>从搜索框、筛选下拉框、返回按钮的底图里找 Scouts/UI 着色器的材质，优先名字正好叫「UI」的。</summary>
    private static Material? FindWobblyMaterial(Transform page)
    {
        foreach (Image candidate in Candidates(page))
        {
            Material? material = candidate != null ? candidate.material : null;
            if (material == null || material.shader == null || material.shader.name != WobblyShader)
            {
                continue;
            }

            if (IsWobbly(material))
            {
                return material;
            }

        }

        // Original settings UI resources may already be loaded without an active settings page.
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            if (IsWobbly(material)) return material;
        return null;
    }

    private static bool IsWobbly(Material? material) => material != null && material.shader != null &&
        material.shader.name == WobblyShader && (material.name == "UI" || material.name == "UI (Instance)");

    private static System.Collections.Generic.IEnumerable<Image> Candidates(Transform page)
    {
        Transform? search = page.Find("SearchInput");
        if (search != null)
        {
            foreach (Image image in search.GetComponentsInChildren<Image>(true))
            {
                yield return image;
            }
        }

        PEAKLib.ModConfig.Components.ModSettingsMenu? menu = PEAKLib.ModConfig.Components.ModSettingsMenu.Instance;
        if (menu != null && menu.FilterDropdown != null && menu.FilterDropdown.Background != null)
        {
            yield return menu.FilterDropdown.Background;
        }

        Transform? back = page.Find("UI_MainMenuButton_Back");
        if (back != null)
        {
            foreach (Image image in back.GetComponentsInChildren<Image>(true))
            {
                yield return image;
            }
        }
    }

    private static Image? FindBackdrop(GameObject root)
    {
        SettingsUICell? cell = root.GetComponent<SettingsUICell>();
        Image? best = null;
        float bestArea = 0f;
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (cell != null && cell.m_settingsContentParent != null && image.transform.IsChildOf(cell.m_settingsContentParent))
            {
                continue;
            }

            if (image.sprite == null)
            {
                continue;
            }

            Rect r = image.rectTransform.rect;
            float area = Mathf.Abs(r.width * r.height);
            if (area > bestArea)
            {
                bestArea = area;
                best = image;
            }
        }

        return best;
    }

    /// <summary>
    /// 给 Image 套上纸面：sprite / 材质 / 类型 / 倍率。颜色不动（着色靠 Image.color，昼夜匹配也靠它）。
    /// 没解析到纸面时退回自绘圆角。
    /// </summary>
    internal static void Apply(Image image, float fallbackCorner)
    {
        if (_sprite == null)
        {
            ModConfigSidebarWidgets.ApplyRounded(image, fallbackCorner);
            return;
        }

        image.sprite = _sprite;
        image.material = _material;
        image.type = _type;
        image.pixelsPerUnitMultiplier = _ppu;
        image.preserveAspect = false;
    }

    /// <summary>会抖的那份材质（Scouts/UI 的「UI」）；没解析到就 null。</summary>
    internal static Material? WobblyMaterial =>
        IsWobbly(_material) ? _material : null;

    /// <summary>只换材质不换 sprite：✓ 细条、置顶竖条、滚动条这些自绘圆角图形也一起抖。</summary>
    internal static void ApplyWobble(Image image)
    {
        Material? material = WobblyMaterial;
        if (material != null)
        {
            image.material = material;
        }
    }

    /// <summary>
    /// ModConfig 右侧选项单元格的底图用的是预制体那份「UI 1」（不抖），换成会抖的「UI」，与左栏、说明面板同步。
    /// 只换材质，颜色不动。
    /// </summary>
    internal static void ApplyWobbleToCell(SettingsUICell cell)
    {
        Material? material = WobblyMaterial;
        if (material == null)
        {
            return;
        }

        Image? backdrop = FindBackdrop(cell.gameObject);
        if (backdrop != null && backdrop.material != material)
        {
            backdrop.material = material;
        }
    }

    /// <summary>说明面板从真实单元格抄底图时也走这里：sprite / 类型 / 倍率照抄，材质优先用会抖的那份。</summary>
    internal static void CopyFrom(Image source, Image target)
    {
        target.sprite = source.sprite;
        target.material = WobblyMaterial ?? source.material;
        target.type = source.type;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        target.preserveAspect = false;
    }
}
