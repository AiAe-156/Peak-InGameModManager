#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewDependencyHints
{
    // All bounds are converted into the card parent's local, top-left coordinate system.
    private Rect VisibleBounds()
    {
        if(_boundsFrame==Time.frameCount && _boundsSource==_source)return _boundsValue;
        _boundsFrame=Time.frameCount;_boundsSource=_source;
        Rect result = new Rect(0, 0, _parent.rect.width, _parent.rect.height);
        void Intersect(RectTransform rect)
        {
            var corners = _corners; rect.GetWorldCorners(corners);
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                Vector3 p = _parent.InverseTransformPoint(corner);
                float x = p.x - _parent.rect.xMin, y = _parent.rect.yMax - p.y;
                left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                top = Mathf.Min(top, y); bottom = Mathf.Max(bottom, y);
            }
            float xMin = Mathf.Max(result.xMin, left), yMin = Mathf.Max(result.yMin, top);
            result = new Rect(xMin, yMin, Mathf.Max(0, Mathf.Min(result.xMax, right) - xMin), Mathf.Max(0, Mathf.Min(result.yMax, bottom) - yMin));
        }
        var canvas = _parent.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas != null && canvas.transform is RectTransform root) Intersect(root);
        // Include the source's scrolling viewport as well as any containing window masks.
        _sourceMasks.Clear();_parentMasks.Clear();
        if (_source != null) _source.GetComponentsInParent(false,_sourceMasks);
        _parent.GetComponentsInParent(false,_parentMasks);
        foreach(var mask in _sourceMasks)if(mask.isActiveAndEnabled)Intersect(mask.rectTransform);
        foreach(var mask in _parentMasks)if(mask.isActiveAndEnabled)Intersect(mask.rectTransform);
        _boundsValue=result;return result;
    }

    internal void Bind(GameObject target, InfrastructureOverviewModel component,
        Func<ComponentDependencyGraph?> graph, Func<IReadOnlyList<InfrastructureOverviewModel>> components, Action? beforeShow = null)
        => Bind(target, chains => { beforeShow?.Invoke(); return ComponentLines(component, graph(), components(), chains); }, "component:" + component.Key);

    private List<string> ComponentLines(InfrastructureOverviewModel owner, ComponentDependencyGraph? graph,
        IReadOnlyList<InfrastructureOverviewModel> components, bool chains)
    {
        var lines = new List<string> {
            "<size=23><color=#EEE8CE>" + Safe(owner.Name) + "</color></size>",
            Secondary(owner.TypeTitle + " · " + owner.Version) };
        graph=SharedRelationshipSnapshot.Get(_mods(),components,graph);
        AddRelationships(lines,graph,owner.Key,new ComponentRelationshipLabels(graph,_mods(),components));
        return lines;
    }
    private void AddRelationships(List<string> lines,ComponentDependencyGraph graph,string root,ComponentRelationshipLabels labels,string? forwardEmpty=null)
    {
        void AddDirection(bool reverse)
        {
            lines.Add(Section(reverse ? Loc.T("Installed dependants", "依赖它的已安装模组", "依賴它的已安裝模組")
                : Loc.T("Dependencies", "依赖的模组", "依賴的模組")));
            var entries = ComponentRelationshipTree.Build(graph, root, reverse, _chains ? 2 : 1, reverse ? 32 : 2048, labels.Canonical);
            // A direct dependency must not disappear because the same node was visited
            // earlier inside another dependency's branch.
            var direct = new HashSet<string>(entries.Where(e => e.Depth == 1).Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            entries = entries.Where(e => e.Depth == 1 || !direct.Contains(e.Id)).ToArray();
            if (entries.Count == 0) { lines.Add(Secondary(!reverse&&forwardEmpty!=null ? forwardEmpty : Loc.T("None", "无", "無"))); return; }
            var branches = new List<List<ComponentRelationshipTree.Row>>();
            foreach (var entry in entries) {
                if (entry.Depth == 1 || branches.Count == 0) branches.Add(new List<ComponentRelationshipTree.Row>());
                branches[branches.Count - 1].Add(entry);
            }
            string Group(ComponentRelationshipTree.Row row) => labels.Mod(row.Id)?.Rule.groupId
                ?? (labels.Component(row.Id) != null ? "@infrastructure" : "");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { labels.Canonical(root) };
            var tree = new GroupTree(_repo.State.groups);
            foreach (var group in branches.GroupBy(b => Group(b[0]))
                .OrderBy(g => tree.Position(g.Key)))
            {
                string title = group.Key == "@infrastructure" ? InfrastructureOverviewModel.GroupTitle
                    : group.Key.Length > 0 ? (tree.Contains(group.Key) ? tree.Path(group.Key) : Loc.T("Ungrouped", "未分组", "未分組"))
                    : Loc.T("Resource packages and unidentified items", "资源包及未识别项目", "資源包及未識別項目");
                bool header = false;
                foreach (var branch in group.OrderBy(b => labels.Mod(b[0].Id)?.Rule.order ?? int.MaxValue))
                foreach (var entry in branch)
                {
                    if (!seen.Add(entry.Id)) continue;
                    if (!header) { lines.Add("<size=15><color=#B4AE8B>[" + Safe(title) + "]</color></size>"); header = true; }
                    // Use the exact ordinary-mod row formatter: number, name/original/version, status and arrow.
                    var item=SharedRelationshipSnapshot.Item(graph,entry,reverse);
                    item.Name=labels.Name(entry.Id);item.Version=labels.Version(entry.Id);item.Mod=labels.Mod(entry.Id);
                    lines.Add(Format(item, entry.Depth - 1, ""));
                    if (entry.Truncated) lines.Add("<indent=" + (entry.Depth * 20) + "><size=17>" + DependencyDepthStyle.Wrap(TreeArrow() + " …", entry.Depth) + "</size></indent>");
                }
            }
        }
        AddDirection(false); AddDirection(true);
    }
}
