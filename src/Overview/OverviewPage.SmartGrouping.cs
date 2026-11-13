#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>First-run smart grouping offer (2.3.0). Shown once, on entering the overview, when the profile has no groups
/// at all and plugins/ has category folders. Reads folders only; on "Group like this" the proposed groups and memberships
/// are written and saved at once. "Not now" records the choice so it is not offered again.</summary>
internal sealed partial class OverviewPage
{
    private bool _smartGroupingChecked;

    private string SmartGroupingMarker => Path.Combine(_repo.RootDirectory, "smart-grouping-offered");

    private void MaybeOfferSmartGrouping()
    {
        if (_smartGroupingChecked) return;
        _smartGroupingChecked = true;
        try
        {
            if (_repo.State.groups.Count > 0 || File.Exists(SmartGroupingMarker) || _repo.ViewedProfile.readOnly) return;
            var entries = ScanPluginFolders();
            if (!SmartGrouping.HasCategories(entries)) return;
            var proposal = SmartGrouping.Propose(entries);
            if (!proposal.Any) return;
            ShowSmartGrouping(proposal);
        }
        catch (Exception error) { Plugin.Log.LogWarning("[智能分组] 读取目录失败：" + error.Message); }
    }

    /// <summary>Folders and files under plugins/ (relative to BepInEx). Only names, manifests and DLL extensions are read.</summary>
    private static List<FolderEntry> ScanPluginFolders()
    {
        string bep = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar);
        string plugins = Path.GetFullPath(Paths.PluginPath);
        var entries = new List<FolderEntry>();
        string Rel(string path) => path.Substring(bep.Length + 1).Replace('\\', '/');
        var pending = new Stack<string>(); pending.Push(plugins);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] children, files;
            try { children = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); } catch (Exception) { continue; }
            foreach (string child in children)
            {
                string rel = Rel(child);
                if (GroupFolderPlanner.NeverTouch(rel) || Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                bool link = (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0;
                entries.Add(new FolderEntry { Path = rel, IsDirectory = true, HasManifest = File.Exists(Path.Combine(child, "manifest.json")) || File.Exists(Path.Combine(child, "manifest.json.old")), HasLink = link });
                if (!link) pending.Push(child);
            }
            foreach (string file in files)
            {
                string rel = Rel(file);
                if (!GroupFolderPlanner.NeverTouch(rel)) entries.Add(new FolderEntry { Path = rel, IsAssembly = PluginFileNames.IsAssembly(file) });
            }
        }
        return entries;
    }

    private void ShowSmartGrouping(SmartGrouping.Proposal proposal)
    {
        var tree = new GroupTree(proposal.Groups);
        int grouped = proposal.PackageGroup.Count;
        var panel = OverviewUi.CreateModalPanel("MCE_SmartGrouping", 760, 620); if (panel == null) return;
        OverviewUi.SaveDialogHeader(panel, Loc.T("Group mods like your folders?", "按现有文件夹分组？", "按現有資料夾分組？"),
            Loc.T("Your plugins folder is already sorted into folders. The manager can turn that layout into groups. No folder is moved or renamed.",
                  "plugins 里已经按文件夹分好类了，可以直接按这个结构生成分组。不会移动或改名任何文件夹。",
                  "plugins 裡已經按資料夾分好類了，可以直接按這個結構產生分組。不會移動或改名任何資料夾。"));
        var host = OverviewUi.Panel("SmartGroupingTree", panel, ModConfigLook.Surface(new Color(.065f, .08f, .095f, 1))); OverviewUi.Fill(host);
        host.offsetMin = new Vector2(24, 82); host.offsetMax = new Vector2(-24, -124);
        var scroll = OverviewUi.Rect("Scroll", host).gameObject.AddComponent<ScrollRect>();
        var area = (RectTransform)scroll.transform; OverviewUi.Fill(area, 8);
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", area); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-14, 0);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var content = OverviewUi.Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content;
        ManagementUi.Scrollbar(area, scroll);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        void Draw()
        {
            PublicationNavigation.Preserve(content);
            for (int i = content.childCount - 1; i >= 0; i--) { var old = content.GetChild(i).gameObject; old.SetActive(false); UnityEngine.Object.Destroy(old); }
            float width = Mathf.Max(200, viewport.rect.width), y = 0;
            var summary = OverviewUi.Text(content, Loc.F("{0} groups · {1} mod folders grouped", "{0} 个分组 · {1} 个模组文件夹归组", "{0} 個分組 · {1} 個模組資料夾歸組", proposal.Groups.Count, grouped), 14);
            summary.color = OverviewUi.Muted; OverviewUi.PlaceClipped(summary, 4, y, width - 8, 20); y += 26;
            foreach (var group in tree.Flat)
            {
                int depth = tree.Depth(group.id);
                float indent = (depth - 1) * OverviewListView.GroupIndent;
                var row = OverviewUi.Panel("Group_" + group.id, content, Color.Lerp(ModConfigLook.Surface(new Color(.1f, .11f, .12f, .95f)), Parse(group.color), depth <= 1 ? .14f : .09f));
                float height = depth <= 1 ? 32 : 28;
                float railX = 0;
                int railLevel = 0; foreach (var level in tree.Chain(group.id)) { var rail = OverviewUi.Rail("Rail", row, Parse(level.color)); float rw = GroupRailShape.Width(OverviewListView.RailWidth, railLevel); var (ry, rh) = GroupRailShape.Vertical(0, height, railLevel); OverviewUi.Place(rail, railX, ry, rw, rh); railX += rw + OverviewListView.RailGap; railLevel++; }
                string id = group.id;
                var members = proposal.PackageGroup.Where(p => p.Value == id).Select(p => GroupFolderPlanner.Leaf(p.Key)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                bool open = expanded.Contains(id);
                var toggle = OverviewUi.IconButton(row, open ? "triangle-down" : "triangle-right", () => { if (!expanded.Add(id)) expanded.Remove(id); Draw(); }, 22, 22);
                OverviewUi.Place((RectTransform)toggle.transform, indent + 8, (height - 22) / 2, 22, 22);
                toggle.interactable = members.Count > 0;
                var name = OverviewUi.Text(row, group.name, depth <= 1 ? 18 : 16); name.color = Parse(group.color);
                OverviewUi.PlaceClipped(name, indent + 36, 0, width - indent - 140, height);
                var count = OverviewUi.Text(row, tree.SubtreeCount(id, proposal.PackageGroup, p => p.Value).ToString(), 14); count.color = OverviewUi.Muted; count.alignment = TextAlignmentOptions.MidlineRight;
                OverviewUi.PlaceClipped(count, width - 80, 0, 70, height);
                OverviewUi.Place(row, 0, y, width, height); y += height + 3;
                if (!open) continue;
                foreach (string member in members)
                {
                    var line = OverviewUi.Text(content, member, 13); line.color = OverviewUi.Ivory;
                    OverviewUi.PlaceClipped(line, indent + 44, y, width - indent - 52, 20); y += 22;
                }
                y += 2;
            }
            content.sizeDelta = new Vector2(0, y + 4);
        }
        Draw();
        void Decline()
        {
            OverviewUi.CloseModal();
            try { File.WriteAllText(SmartGroupingMarker, DateTime.UtcNow.ToString("O")); } catch (Exception) { }
        }
        void Accept()
        {
            try
            {
                _repo.EnsureLocalPackCopy();
                _repo.State.groups.AddRange(proposal.Groups);
                string bep = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar);
                foreach (var mod in new OverviewCatalogue().Scan(_repo))
                {
                    if (string.IsNullOrEmpty(mod.PackageRoot)) continue;
                    string rel = Path.GetFullPath(mod.PackageRoot).Substring(bep.Length + 1).Replace('\\', '/');
                    // A row's folder may sit inside its package's own layout; use the nearest proposed package above it.
                    string? key = proposal.PackageGroup.Keys.Where(k => rel.Equals(k, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(k + "/", StringComparison.OrdinalIgnoreCase)).OrderByDescending(k => k.Length).FirstOrDefault();
                    if (key == null) continue;
                    foreach (string guid in mod.PluginGuids.Concat(new[] { mod.Guid }).Distinct(StringComparer.OrdinalIgnoreCase)) _repo.GetRule(guid).groupId = proposal.PackageGroup[key];
                }
                _repo.SavePackInformation(ConfigurationSaveScope.Groups);
                try { File.WriteAllText(SmartGroupingMarker, DateTime.UtcNow.ToString("O")); } catch (Exception) { }
                OverviewUi.CloseModal();
                _list?.Reload();
                SetStatus(Loc.F("{0} groups created from your folders.", "已按文件夹生成 {0} 个分组。", "已按資料夾產生 {0} 個分組。", proposal.Groups.Count));
            }
            catch (Exception error) { SetStatus(Loc.T("Could not save the groups: ", "分组保存失败：", "分組儲存失敗：") + Loc.LocalizeMessage(error.Message)); }
        }
        OverviewUi.SaveDialogAction(panel, Loc.T("Not now", "不用了", "不用了"), Decline, 20, 110, UiActionRole.Neutral);
        var accept = OverviewUi.SaveDialogAction(panel, Loc.T("Group like this", "按此分组", "按此分組"), Accept, 140, 150, UiActionRole.Commit);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(accept.gameObject);
    }

    private static Color Parse(string hex) => ColorUtility.TryParseHtmlString(hex ?? "", out var color) ? color : OverviewUi.Blue;
}
