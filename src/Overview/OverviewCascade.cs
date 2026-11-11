#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 原版节奏的逐行渐入：和 <c>SharedSettingsMenu.FadeInCells</c> 一样，整列排成一条队列按顺序跑完，
/// 每行间隔 <see cref="Interval"/> 秒、出现时播一次原版单元格音效，中途不看视口、不因滚动重排。
/// </summary>
internal sealed class OverviewCascade : MonoBehaviour
{
    private const float Interval = .05f, LerpSpeed = 10f;

    internal readonly struct Row
    {
        internal readonly RectTransform Target, Bounds;
        internal Row(RectTransform target, RectTransform? bounds = null) { Target = target; Bounds = bounds ?? target; }
    }

    private sealed class Entry
    {
        internal Row Row;
        internal CanvasGroup Group = null!;
        internal float Alpha;
        internal int Wave;
        internal bool Interactable, BlocksRaycasts, Started, Done;
    }

    private readonly List<Entry> _rows = new();
    private readonly List<Row> _targets = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private Entry? _header;
    private ScrollRect? _scroll;
    private RectTransform? _headerRect;
    private Func<IEnumerable<Row>>? _getRows;
    private Action? _layout;
    private Coroutine? _run;
    private SFX_Instance? _sfx;
    private bool _preparing, _closing, _refreshPending;
    private float _nextStartTime;
    private int _appendWave;
    private float _speed = 2.5f;
    private GameObject? _selection;
    internal bool HasSequence => _scroll != null;
    internal void Resume() { _closing=false; if(_preparing && _run==null && isActiveAndEnabled)_run=StartCoroutine(Run()); }
    internal bool IsAnimating => _preparing || _refreshPending || _rows.Count > 0 || (_header != null && !_header.Done);

    internal void Play(ScrollRect scroll, Func<IEnumerable<Row>> rows, RectTransform? header = null, Action? layout = null)
    {
        Finish();
        _scroll = scroll; _getRows = rows; _headerRect = header; _layout = layout;
        _speed = OverviewOptions.AppearanceSpeed.Value;
        _preparing = true;_appendWave=0;
        if (isActiveAndEnabled) _run = StartCoroutine(Run());
    }

    internal void RegisterBuiltRow(RectTransform row)
    {
        if(_closing || _scroll==null)return;
        if(_rows.Exists(e=>e.Row.Target==row) || _targets.Exists(e=>e.Target==row))return;
        _targets.Add(new Row(row));Enqueue(new Row(row),_appendWave++);
    }

    // Preserve an opening request through its first rebuild. Later rebuilds replace the tracking
    // baseline once layout is ready, without replaying a background refresh.
    internal bool BeforeRebuild()
    {
        // Hidden data changes may rebuild the model; the next entry starts a fresh sequence.
        if (_closing) Finish();
        ResetRows(); ClearTargets();
        if (!_preparing && _scroll != null && _getRows != null) _refreshPending = true;
        return true;
    }

    internal void CompleteAppearance()
    {
        if (_closing) return;
        if (_run != null) { StopCoroutine(_run); _run = null; }
        ResetRows();
        if (_preparing && _scroll != null && _getRows != null) _refreshPending = true;
        _preparing = false;
    }

    internal void PauseForExit()
    {
        if (_run != null) { StopCoroutine(_run); _run = null; }
        // Keep the original queue, opacity and sound progress across tab/page exits.
        _closing = true;
    }

    internal void Finish()
    {
        if (_run != null) { StopCoroutine(_run); _run = null; }
        ResetRows(); ClearTargets(); _preparing = false; _closing = false; _refreshPending = false;
        _getRows = null; _layout = null;
        _scroll = null; _headerRect = null;
        _selection = null;
    }

    private void OnEnable() { if (!_closing && _preparing && _scroll != null) _run = StartCoroutine(Run()); }
    private void OnDisable() { if(_run!=null) { StopCoroutine(_run);_run=null; } }
    private void OnDestroy() => Finish();

    private IEnumerator Run()
    {
        Prepare(true); // Hide once; the normal Canvas pass resolves the next frame's geometry.
        yield return null;
        _preparing = false;
        if (_header != null) _header.Started = true;
        if (_rows.Count == 0 && _header != null) PlaySound();
        _run = null;
    }

    private void Prepare(bool animate)
    {
        ResetRows(); ClearTargets();
        _layout?.Invoke(); // Never force a global Canvas rebuild from a local appearance effect.
        if (_scroll == null || _scroll.viewport == null || _getRows == null) return;
        var targets = new HashSet<RectTransform>();
        foreach (var row in _getRows())
        {
            if (row.Target == null || row.Bounds == null || !row.Target.gameObject.activeInHierarchy ||
                !targets.Add(row.Target)) continue;
            _targets.Add(row);
        }
        if (animate) { Queue();_appendWave=_targets.Count; }
        if (animate && _headerRect != null && _headerRect.gameObject.activeInHierarchy) _header = Hide(new Row(_headerRect));
        _nextStartTime = Time.unscaledTime;
        _selection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
    }

    /// <summary>
    /// 原版是整列一条队列、从第一项排到最后一项，屏幕外的项目照样轮到、照样出声。这里唯一的偏差是起点：
    /// 概览页会保留滚动位置，所以从视口里的第一行起排，每一拍同时往下、往上各推进一行（与左栏 SidebarCascade 同样的做法），
    /// 一直排到两头为止，中途不因滚动重排。
    /// </summary>
    private void Queue()
    {
        int start = -1;
        for (int i = 0; i < _targets.Count; i++)
            if (Visible(_targets[i].Bounds)) { start = i; break; }
        if (start < 0) return;
        Enqueue(_targets[start], 0);
        for (int distance = 1; start - distance >= 0 || start + distance < _targets.Count; distance++)
        {
            if (start + distance < _targets.Count) Enqueue(_targets[start + distance], distance);
            if (start - distance >= 0) Enqueue(_targets[start - distance], distance);
        }
    }

    private void Enqueue(Row row, int wave)
    {
        var entry = Hide(row); if (entry == null) return;
        entry.Wave = wave; _rows.Add(entry);
    }

    private Entry? Hide(Row row)
    {
        // Never fade or disable a button through an ancestor CanvasGroup either.
        // Text-only document blocks keep their existing reveal; interactive rows
        // are presented by the resident builder, with native hover/click animation.
        if (row.Target.GetComponentInChildren<Selectable>(true) != null) return null;
        var group = row.Target.GetComponent<CanvasGroup>() ?? row.Target.gameObject.AddComponent<CanvasGroup>();
        if (group.alpha <= 0) return null; // Respect intentionally hidden content.
        var entry = new Entry { Row = row, Group = group, Alpha = group.alpha,
            Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts };
        // Keep Selectable navigation valid during the fade; mouse hits wait until it is visible.
        group.alpha = 0; group.blocksRaycasts = false;
        return entry;
    }

    private bool Visible(RectTransform rect)
    {
        if (_scroll == null || _scroll.viewport == null || rect == null || !rect.gameObject.activeInHierarchy) return false;
        Rect bounds = Bounds(rect);
        return bounds.width > 0 && bounds.height > 0 && bounds.Overlaps(_scroll.viewport.rect);
    }

    private Rect Bounds(RectTransform rect)
    {
        rect.GetWorldCorners(_corners);
        float left = float.PositiveInfinity, bottom = float.PositiveInfinity;
        float right = float.NegativeInfinity, top = float.NegativeInfinity;
        for (int i = 0; i < _corners.Length; i++)
        {
            Vector3 point = _scroll!.viewport.InverseTransformPoint(_corners[i]);
            left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
            bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y);
        }
        return Rect.MinMaxRect(left, bottom, right, top);
    }

    private void Update()
    {
        if (_closing || _preparing || _scroll == null) return;
        if (_refreshPending) { Prepare(false); _refreshPending = false; }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _selection)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            _selection = selected;
            // Focus/scrollbar movement must not complete or reorder an in-flight reveal.
            // The original queue continues even when the user navigates outside its current wave.
        }
        if (_header != null) Fade(_header);
        if (Time.unscaledTime >= _nextStartTime)
        {
            int wave = -1;
            foreach (var entry in _rows)
            {
                if (entry.Started || entry.Done) continue;
                // 队列按 wave 升序排好：同一拍里向下与向上那两行一起起步，之后才等下一拍。
                if (wave < 0) wave = entry.Wave; else if (entry.Wave != wave) break;
                entry.Started = true; PlaySound();
            }
            // 原版协程也是一项一等待，慢帧之后不会补发一串。
            if (wave >= 0) _nextStartTime = Time.unscaledTime + Interval / _speed;
        }
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var entry = _rows[i];
            if (entry.Group == null || entry.Row.Target == null || !entry.Row.Target.gameObject.activeInHierarchy) Restore(entry);
            Fade(entry);
            if (entry.Done) _rows.RemoveAt(i);
        }
    }

    private void Fade(Entry entry)
    {
        if (entry.Done || !entry.Started) return;
        if (entry.Group == null) { entry.Done = true; return; }
        entry.Group.alpha = Mathf.Lerp(entry.Group.alpha, entry.Alpha, Time.unscaledDeltaTime * LerpSpeed * _speed);
        if (entry.Group.alpha >= entry.Alpha * .995f) Restore(entry);
    }

    private static void Restore(Entry entry)
    {
        if (entry.Group != null)
        {
            entry.Group.alpha = entry.Alpha;
            entry.Group.interactable = entry.Interactable; entry.Group.blocksRaycasts = entry.BlocksRaycasts;
        }
        entry.Done = true;
    }

    private void ResetRows()
    {
        if (_header != null) Restore(_header);
        foreach (var entry in _rows) Restore(entry);
        _header = null; _rows.Clear();
    }

    private void ClearTargets()
    {
        _targets.Clear();
    }

    internal void PlayLoadedRowSound() => PlaySound();
    private void PlaySound()
    {
        if (_sfx == null)
        {
            var prefab = Templates.SettingsCellPrefab;
            _sfx = prefab != null ? prefab.GetComponent<SettingsUICell>()?.fadeInSFX : null;
        }
        if (_sfx != null) _sfx.Play();
    }
}

/// <summary>给预先建好的概览面板补上稍后才加载的原版动态材质；不改颜色。</summary>
internal sealed class OverviewPaperPanel : MonoBehaviour
{
    private void OnEnable() => Apply();
    private void Apply()
    {
        var image = GetComponent<Image>();
        if (image != null) ModConfigPaperStyle.Apply(image, 6);
    }
    internal static void Refresh(Transform root)
    {
        foreach (var panel in root.GetComponentsInChildren<OverviewPaperPanel>(true)) panel.Apply();
        foreach (var button in root.GetComponentsInChildren<PeakMenuButton>(true))
            if (button.Panel != null) ModConfigPaperStyle.ApplyWobble(button.Panel);
    }
}
