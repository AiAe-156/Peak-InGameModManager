#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static class PublishFolderPicker
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr owner, root;
        public IntPtr displayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string title;
        public uint flags;
        public IntPtr callback, data, image;
    }
    [DllImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr SHBrowseForFolderW(ref BrowseInfo info);
    [DllImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHGetPathFromIDListW(IntPtr pidl, System.Text.StringBuilder path);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr memory);
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    internal static bool TrySelect(out string? path)
    {
        path = null;
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
        IntPtr display = Marshal.AllocHGlobal(1024);
        try
        {
            var info = new BrowseInfo { owner = GetActiveWindow(), displayName = display,
                title = Loc.T("Choose publication output folder", "选择发布包输出目录", "選擇發布包輸出資料夾"),
                flags = 0x00000001 | 0x00000040 | 0x00000010 };
            if (info.owner == IntPtr.Zero) info.owner = GetForegroundWindow();
            IntPtr pidl = SHBrowseForFolderW(ref info);
            if (pidl == IntPtr.Zero) return true;
            try
            {
                var buffer = new System.Text.StringBuilder(32768);
                if (SHGetPathFromIDListW(pidl, buffer)) path = buffer.ToString();
                return true;
            }
            finally { CoTaskMemFree(pidl); }
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
        finally { Marshal.FreeHGlobal(display); }
    }
}

internal sealed class PublishFilePreview
{
    private sealed class Node
    {
        internal readonly string Name;
        internal readonly SortedDictionary<string, Node> Directories = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Files = new();
        internal Node(string name) => Name = name;
    }

    private static readonly string[] CategoryNames = { "模组", "方案作者资料", "配置", "加载器补丁", "附加文件" };
    private readonly RectTransform _host;
    private readonly IReadOnlyList<string> _files;
    private readonly PublishRequest _request;
    private readonly OverviewRepository _repo;
    private readonly PublicationDrafts _drafts;
    private readonly Action<string> _status;
    private PublicationContentView? _content;
    internal bool HasDirty => _content?.HasDirty == true;
    internal bool TryCloseLayer() => _content?.TryCloseLayer() == true;
    internal void RequestLeave(Action next) { if (_content != null) _content.RequestLeave(next); else next(); }
    private readonly Action _back, _generate, _openFolder;
    private readonly Action _collectConfig, _chooseConfig, _chooseExtras;
    internal string SelectedCategory => _category;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private string _category = CategoryNames[0], _selected = "";
    private RectTransform? _treeContent;
    private ScrollRect? _scroll;
    private TMP_Text? _countText;
    private float _treeWidth;
    private float _treeHeight;
    private string _initializedCategory = "";
    private readonly Dictionary<string, Button> _categoryButtons = new();

    private PublishFilePreview(RectTransform host, IReadOnlyList<string> files, PublishRequest request, OverviewRepository repo, PublicationDrafts drafts, Action<string> status, Action back, Action generate, Action openFolder, Action collectConfig, Action chooseConfig, Action chooseExtras, string? category)
    { _host = host; _files = files; _request = request; _repo = repo; _drafts = drafts; _status = status; _back = back; _generate = generate; _openFolder = openFolder; _collectConfig = collectConfig; _chooseConfig = chooseConfig; _chooseExtras = chooseExtras; if (category != null && CategoryNames.Contains(category)) _category = category; }

    internal static PublishFilePreview Show(RectTransform host, IReadOnlyList<string> files, PublishRequest request, OverviewRepository repo, PublicationDrafts drafts, Action<string> status, Action back, Action generate, Action openFolder, Action collectConfig, Action chooseConfig, Action chooseExtras, string? category = null)
    { var preview = new PublishFilePreview(host, files, request, repo, drafts, status, back, generate, openFolder, collectConfig, chooseConfig, chooseExtras, category); preview.Render(); return preview; }

    private void Render()
    {
        Clear(_host);
        float width = _host.rect.width, height = _host.rect.height;
        var heading = OverviewUi.Text(_host, Loc.T("Publication preview", "发布预览", "發布預覽"), 25); OverviewUi.PlaceClipped(heading, 18, 10, width - 36, 36);
        var totals = OverviewUi.Text(_host, Loc.T("Files ", "文件 ", "檔案 ") + _files.Count + " · " + _request.Name + " " + _request.Version + " · " + PublishWizard.FormatLabel(_request.Mode), 17);
        OverviewUi.PlaceClipped(totals, 18, 48, width - 36, 30);
        float bodyTop = 88, footerHeight = 116, bodyHeight = Math.Max(120, height - bodyTop - footerHeight);
        float leftWidth = Mathf.Clamp(width * .18f, 150, 210), rightX = leftWidth + 28, available = width - rightX - 26, rightWidth = available * .45f;
        float contentX = rightX + rightWidth + 18, contentWidth = available - rightWidth - 18;
        var categoryTitle = OverviewUi.Text(_host, Loc.T("Contents", "内容分类", "內容分類"), 18); OverviewUi.PlaceClipped(categoryTitle, 18, bodyTop, leftWidth, 30);
        BuildCategories(bodyTop + 38, leftWidth, bodyHeight - 38);
        var treeTitle = OverviewUi.Text(_host, Loc.T("Folders and files", "目录与文件", "資料夾與檔案"), 18); OverviewUi.PlaceClipped(treeTitle, rightX, bodyTop, rightWidth, 30);
        float treeTop = bodyTop + 38;
        var treeRoot = OverviewUi.Rect("PublishTree", _host); OverviewUi.Place(treeRoot, rightX, treeTop, rightWidth, bodyHeight - 72);
        treeRoot.gameObject.AddComponent<RectMask2D>(); treeRoot.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(0, 0, 0, .10f));
        _scroll = treeRoot.gameObject.AddComponent<ScrollRect>(); _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", treeRoot); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-17, 0); viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        _treeContent = OverviewUi.Rect("Content", viewport); _treeContent.anchorMin = new Vector2(0, 1); _treeContent.anchorMax = Vector2.one; _treeContent.pivot = new Vector2(0, 1); _treeContent.sizeDelta = Vector2.zero;
        _scroll.viewport = viewport; _scroll.content = _treeContent;
        AddScrollbar(treeRoot, _scroll);
        _treeWidth = rightWidth - 24; _treeHeight = bodyHeight - 72;
        var contentHost = OverviewUi.Rect("FileContentPreview", _host); OverviewUi.Place(contentHost, contentX, bodyTop, contentWidth, bodyHeight - 34);
        _content = new PublicationContentView(contentHost, _repo, _drafts, _status);
        contentHost.gameObject.AddComponent<PublicationPreviewLifetime>().Release = _content.Dispose;
        var fullPath = OverviewUi.Text(_host, Loc.T("Select a file to view its full relative path", "选择文件可查看完整相对路径", "選擇檔案可查看完整相對路徑"), 14); fullPath.gameObject.name = "SelectedPath"; OverviewUi.PlaceClipped(fullPath, rightX, treeTop + bodyHeight - 68, available, 30);
        fullPath.raycastTarget = true; fullPath.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => { if (_selected.Length > 0) OverviewUi.ShowText(Loc.T("Full relative path", "完整相对路径", "完整相對路徑"), _selected); };
        var countText = OverviewUi.Text(_host, "", 14); _countText = countText;
        OverviewUi.PlaceClipped(countText, 18, bodyTop + bodyHeight + 4, width - 36, 28); UpdateCount(countText);
        float footerY = height - 66;
        var generate = OverviewUi.Button(_host, Loc.T("Generate", "生成发布包", "產生發布包"), () => RequestLeave(_generate), 150, 38, UiActionRole.Commit); OverviewUi.Place((RectTransform)generate.transform, width - 174, footerY, 150, 38);
        var back = OverviewUi.Button(_host, Loc.T("Back to options", "返回调整", "返回調整"), () => RequestLeave(_back), 128, 38, UiActionRole.Neutral); OverviewUi.Place((RectTransform)back.transform, width - 316, footerY, 128, 38);
        var open = OverviewUi.Button(_host, Loc.T("Open output folder", "打开输出目录", "開啟輸出資料夾"), _openFolder, 140, 38, UiActionRole.Edit); OverviewUi.Place((RectTransform)open.transform, 18, footerY, 140, 38);
        DrawTree();
    }

    private void BuildCategories(float y, float width, float height)
    {
        var host = OverviewUi.Rect("CategoryScroll", _host); OverviewUi.Place(host, 18, y, width, Mathf.Max(70, height));
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-14, 0); viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var content = OverviewUi.Rect("Categories", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(0, CategoryNames.Length * 46);
        scroll.viewport = viewport; scroll.content = content; ManagementUi.Scrollbar(host, scroll);
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            string category = CategoryNames[i]; int files = _files.Count(file => Category(file) == category);
            string caption = category switch { "模组" => Loc.T("Mods", "模组", "模組"), "方案作者资料" => Loc.T("Profile and author data", "方案作者资料", "方案作者資料"), "配置" => Loc.T("Configuration", "配置", "設定"), "加载器补丁" => Loc.T("Loader and patches", "加载器补丁", "載入器與補丁"), _ => Loc.T("Attachments and extras", "附件与附加文件", "附件與附加檔案") };
            var button = OverviewUi.Button(content, caption + " · " + files, () => { _category = category; foreach (var item in _categoryButtons) UiActionStyle.Set(item.Value, item.Key == category ? UiActionRole.Edit : UiActionRole.Neutral); if (_countText != null) UpdateCount(_countText); DrawTree(); }, width - 16, 38, category == _category ? UiActionRole.Edit : UiActionRole.Neutral);
            _categoryButtons[category] = button;
            var label = button.GetComponentInChildren<TMP_Text>(true); label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 18;
            OverviewUi.Place((RectTransform)button.transform, 0, i * 46, width - 16, 38);
        }
    }

    private void UpdateCount(TMP_Text text)
    {
        var filtered = _files.Where(FileCategory).ToList();
        int dirs = filtered.SelectMany(p => ParentPaths(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        text.text = Loc.T("Selected category: ", "当前分类：", "目前分類：") + filtered.Count + Loc.T(" files · ", " 个文件 · ", " 個檔案 · ") + dirs + Loc.T(" folders", " 个目录", " 個資料夾");
    }

    private bool FileCategory(string file) => Category(file) == _category;
    private string Category(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (_request.ExtraFiles.Any(extra => normalized.Equals(extra.TrimEnd('/') , StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(extra.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))) return CategoryNames[4];
        bool profileResource = ModConfigEnhance.Updating.ManagerIdentity.IndexOfPackSubfolder("/" + normalized, "profiles", out _) >= 0;
        if (profileResource && (OverviewProfileAssets.SupportedMedia(normalized) || normalized.IndexOf("/fonts/", StringComparison.OrdinalIgnoreCase) >= 0)) return CategoryNames[4];
        if (normalized.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)) return CategoryNames[2];
        if (normalized.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("BepInEx/monomod/", StringComparison.OrdinalIgnoreCase) || new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return CategoryNames[3];
        if (ModConfigEnhance.Updating.ManagerIdentity.ContainsPackFolder("/" + normalized)) return CategoryNames[1];
        if (normalized is "manifest.json" or "README.md" or "icon.png") return CategoryNames[1];
        return CategoryNames[0];
    }

    private static IEnumerable<string> ParentPaths(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        for (int i = 1; i < parts.Length; i++) yield return string.Join("/", parts.Take(i));
    }

    private void DrawTree()
    {
        if (_treeContent == null || _scroll == null) return;
        Clear(_treeContent);
        var root = new Node("");
        foreach (string file in _files.Where(FileCategory))
        {
            string[] parts = file.Replace('\\', '/').Split('/').Where(p => p.Length > 0).ToArray();
            Node current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.Directories.TryGetValue(parts[i], out var child)) current.Directories[parts[i]] = child = new Node(parts[i]);
                current = child;
            }
            if (parts.Length > 0) current.Files.Add(file);
        }
        float y = 0;
        if (_category == CategoryNames[2])
        {
            float half = (_treeWidth - 8) / 2;
            var collect = OverviewUi.Button(_treeContent, Loc.T("Collect current settings", "收录当前配置", "收錄目前設定"), () => RequestLeave(_collectConfig), half, 32, UiActionRole.Commit); OverviewUi.Place((RectTransform)collect.transform, 0, y, half, 32);
            var choose = OverviewUi.Button(_treeContent, Loc.T("Choose extra configs", "选择附加配置", "選擇附加設定"), () => RequestLeave(_chooseConfig), half, 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)choose.transform, half + 8, y, half, 32); y += 42;
        }
        if (_category == CategoryNames[4])
        {
            var choose = OverviewUi.Button(_treeContent, Loc.T("Choose extra files", "选择附加文件", "選擇附加檔案"), () => RequestLeave(_chooseExtras), Mathf.Min(180, _treeWidth), 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)choose.transform, 0, y, Mathf.Min(180, _treeWidth), 32); y += 42;
        }
        if (!_files.Any(FileCategory))
        {
            string message = _category == CategoryNames[2] ? Loc.T("No config files are recorded. Collect current settings or choose extra configs above.", "尚未收录配置文件。可在上方收录当前配置，或选择附加配置。", "尚未收錄設定檔。可在上方收錄目前設定，或選擇附加設定。")
                : _category == CategoryNames[4] ? Loc.T("No referenced attachments or selected extra files. Introduction images, video and fonts appear here automatically.", "没有被介绍引用的附件，也未选择附加文件。介绍引用的图片、视频和字体会自动列在这里。", "沒有介紹引用的附件，也未選擇附加檔案。介紹引用的圖片、影片和字型會自動列在這裡。")
                : Loc.T("This category is empty.", "此分类暂无文件。", "此分類目前沒有檔案。");
            var empty = OverviewUi.Text(_treeContent, message, 16); empty.textWrappingMode = TextWrappingModes.Normal;
            float h = Mathf.Max(56, empty.GetPreferredValues(message, _treeWidth, 0).y + 12); OverviewUi.Place(empty.rectTransform, 0, y, _treeWidth, h); y += h;
        }
        RenderNode(root, "", 0, ref y);
        _treeContent.sizeDelta = new Vector2(0, Math.Max(_treeHeight, y));
        _initializedCategory = _category;
        _scroll.verticalNormalizedPosition = 1;
    }

    private void RenderNode(Node node, string path, int depth, ref float y)
    {
        foreach (var pair in node.Directories)
        {
            string relative = path.Length == 0 ? pair.Key : path + "/" + pair.Key;
            if (_initializedCategory != _category && depth < 2) _expanded.Add(relative);
            bool expanded = _expanded.Contains(relative);
            var button = OverviewUi.Button(_treeContent!, "", () => { if (expanded) _expanded.Remove(relative); else _expanded.Add(relative); DrawTree(); }, _treeWidth, 30, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)button.transform, 0, y, _treeWidth, 30);
            float indent = Mathf.Min(depth, 4) * 12;
            var label = OverviewUi.Text(button.transform, (expanded ? "− " : "+ ") + pair.Key, 16); OverviewUi.PlaceClipped(label, 8 + indent, 1, _treeWidth - 16 - indent, 28); y += 31;
            if (expanded) RenderNode(pair.Value, relative, depth + 1, ref y);
        }
        foreach (string file in node.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = file.Replace('\\', '/').Split('/').Last();
            var button = OverviewUi.Button(_treeContent!, "", () => _content?.Open(file, () => { _selected = file; var pathText = _host.Find("SelectedPath"); if (pathText != null) pathText.GetComponent<TMP_Text>().text = file; }), _treeWidth, 29, file == _selected ? UiActionRole.Edit : UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)button.transform, 0, y, _treeWidth, 29);
            float indent = Mathf.Min(depth, 4) * 12;
            var label = OverviewUi.Text(button.transform, name, 15); OverviewUi.PlaceClipped(label, 24 + indent, 1, _treeWidth - 32 - indent, 27);
            y += 30;
        }
    }

    private static void AddScrollbar(RectTransform root, ScrollRect scroll)
    {
        var track = OverviewUi.Rect("Scrollbar", root); track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one; track.pivot = new Vector2(1, 1); track.sizeDelta = new Vector2(14, 0); track.anchoredPosition = Vector2.zero;
        track.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(1, 1, 1, .10f));
        var handle = OverviewUi.Rect("Handle", track); OverviewUi.Fill(handle, 1); handle.gameObject.AddComponent<Image>().color = new Color(.85f, .78f, .58f, .85f);
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop; bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.size = .2f; scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent; scroll.verticalScrollbarSpacing = 0;
    }

    private static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) { var child = parent.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
    }
}
