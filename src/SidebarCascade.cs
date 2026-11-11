#nullable enable
using System.Collections;
using System.Collections.Generic;
using PEAKLib.ModConfig.Components;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 左栏的逐行出现：与原版设置页 / ModConfig 右侧单元格同一套做法——每行一个 CanvasGroup，alpha 从 0 Lerp 到 1，
/// 每行间隔 <see cref="Interval"/> 秒，出现时播单元格预制体上的 <c>fadeInSFX</c>（借 <c>Templates.SettingsCellPrefab</c> 的那份）。
/// 页面每次打开时模组列表跑一遍；选中模组、分区行重建时分区行跑一遍。
/// </summary>
internal sealed class SidebarCascade : MonoBehaviour
{
    private const float Interval = 0.05f;
    private const float LerpSpeed = 10f;

    internal PeakHorizontalTabs? ModTabs;
    internal GameObject? Header;

    private static SFX_Instance? _sfx;
    private static bool _sfxResolved;

    private Coroutine? _modRun;
    private Coroutine? _sectionRun;
    private readonly List<CanvasGroup> _modFading = new List<CanvasGroup>();
    private readonly List<CanvasGroup> _sectionFading = new List<CanvasGroup>();
    private readonly List<CanvasGroup> _modPrepared = new List<CanvasGroup>();
    private readonly List<CanvasGroup> _sectionPrepared = new List<CanvasGroup>();

    private static SFX_Instance? Sfx
    {
        get
        {
            if (!_sfxResolved)
            {
                _sfxResolved = true;
                try
                {
                    SettingsUICell? cell = Templates.SettingsCellPrefab != null ? Templates.SettingsCellPrefab.GetComponent<SettingsUICell>() : null;
                    _sfx = cell != null ? cell.fadeInSFX : null;
                }
                catch
                {
                    _sfx = null;
                }
            }

            return _sfx;
        }
    }

    private void OnEnable()
    {
        List<GameObject> rows = new List<GameObject>();
        if (Header != null)
        {
            rows.Add(Header);
        }

        if (ModTabs != null)
        {
            // 按当前显示顺序（置顶行在前）
            UnityEngine.UI.ScrollRect? scroll = ModTabs.GetComponent<UnityEngine.UI.ScrollRect>();
            if (scroll != null && scroll.content != null)
            {
                foreach (Transform child in scroll.content)
                {
                    if (child.gameObject.activeSelf && child.GetComponent<ModdedTABSButton>() != null)
                    {
                        rows.Add(child.gameObject);
                    }
                }
            }
        }

        List<List<GameObject>> waves = BuildModWaves(rows);
        _modRun = Restart(_modRun, waves, _modPrepared, _modFading);
    }

    private void OnDisable()
    {
        StopRun(ref _modRun, _modPrepared, _modFading);
        StopRun(ref _sectionRun, _sectionPrepared, _sectionFading);
    }

    /// <summary>分区行重建 / 展开后调用（UpdateSectionTabs 的 postfix）。</summary>
    internal void PlaySections(IEnumerable<GameObject> rows)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        _sectionRun = Restart(_sectionRun, BuildSequentialWaves(new List<GameObject>(rows)), _sectionPrepared, _sectionFading);
    }

    private Coroutine? Restart(Coroutine? previous, List<List<GameObject>> waves,
        List<CanvasGroup> prepared, List<CanvasGroup> fading)
    {
        if (previous != null)
        {
            StopCoroutine(previous);
        }

        ResetFading(prepared, fading);
        if (waves.Count == 0)
        {
            return null;
        }

        List<List<CanvasGroup>> groups = new List<List<CanvasGroup>>(waves.Count);
        foreach (List<GameObject> wave in waves)
        {
            List<CanvasGroup> waveGroups = new List<CanvasGroup>(wave.Count);
            foreach (GameObject row in wave)
            {
                if (row == null || !row.activeInHierarchy)
                {
                    continue;
                }

                CanvasGroup group = row.GetComponent<CanvasGroup>() ?? row.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                prepared.Add(group);
                waveGroups.Add(group);
            }

            if (waveGroups.Count > 0)
            {
                groups.Add(waveGroups);
            }
        }

        return StartCoroutine(Run(groups, fading));
    }

    private void StopRun(ref Coroutine? run, List<CanvasGroup> prepared, List<CanvasGroup> fading)
    {
        if (run != null)
        {
            StopCoroutine(run);
            run = null;
        }

        ResetFading(prepared, fading);
    }

    private static void ResetFading(List<CanvasGroup> prepared, List<CanvasGroup> fading)
    {
        foreach (CanvasGroup group in prepared)
        {
            if (group != null)
            {
                group.alpha = 1f;
            }
        }

        foreach (CanvasGroup group in fading)
        {
            if (group != null)
            {
                group.alpha = 1f;
            }
        }

        fading.Clear();
        prepared.Clear();
    }

    private static IEnumerator Run(List<List<CanvasGroup>> waves, List<CanvasGroup> fading)
    {
        foreach (List<CanvasGroup> wave in waves)
        {
            foreach (CanvasGroup group in wave)
            {
                if (group == null)
                {
                    continue;
                }

                fading.Add(group);
                SFX_Instance? sfx = Sfx;
                if (sfx != null)
                {
                    sfx.Play();
                }
            }

            yield return new WaitForSecondsRealtime(Interval);
        }
    }

    private List<List<GameObject>> BuildModWaves(List<GameObject> rows)
    {
        List<List<GameObject>> waves = new List<List<GameObject>>();
        if (rows.Count == 0)
        {
            return waves;
        }

        int firstModIndex = rows[0].GetComponent<ModdedTABSButton>() != null ? 0 : 1;
        int selectedIndex = rows.FindIndex(row => row.GetComponent<ModdedTABSButton>()?.Selected == true);
        if (selectedIndex < 0)
        {
            return BuildSequentialWaves(rows);
        }

        // 表头与当前选中项同首波，避免表头的间隔拖慢选中项。
        List<GameObject> first = new List<GameObject>();
        if (rows[0].GetComponent<ModdedTABSButton>() == null)
        {
            first.Add(rows[0]);
        }

        first.Add(rows[selectedIndex]);
        waves.Add(first);
        for (int distance = 1; selectedIndex - distance >= firstModIndex || selectedIndex + distance < rows.Count; distance++)
        {
            List<GameObject> wave = new List<GameObject>(2);
            if (selectedIndex - distance >= firstModIndex)
            {
                wave.Add(rows[selectedIndex - distance]);
            }

            if (selectedIndex + distance < rows.Count)
            {
                wave.Add(rows[selectedIndex + distance]);
            }

            waves.Add(wave);
        }

        return waves;
    }

    private static List<List<GameObject>> BuildSequentialWaves(List<GameObject> rows)
    {
        List<List<GameObject>> waves = new List<List<GameObject>>(rows.Count);
        foreach (GameObject row in rows)
        {
            waves.Add(new List<GameObject> { row });
        }

        return waves;
    }

    private void Update()
    {
        if (_modFading.Count == 0 && _sectionFading.Count == 0)
        {
            return;
        }

        UpdateFading(_modFading);
        UpdateFading(_sectionFading);
    }

    private static void UpdateFading(List<CanvasGroup> fading)
    {
        for (int i = fading.Count - 1; i >= 0; i--)
        {
            CanvasGroup group = fading[i];
            if (group == null)
            {
                fading.RemoveAt(i);
                continue;
            }

            group.alpha = Mathf.Lerp(group.alpha, 1f, Time.unscaledDeltaTime * LerpSpeed);
            if (group.alpha > 0.995f)
            {
                group.alpha = 1f;
                fading.RemoveAt(i);
            }
        }
    }
}
