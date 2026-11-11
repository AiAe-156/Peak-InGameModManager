#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;
internal sealed class PublishWizard
{
    private readonly OverviewRepository _repo;
    private readonly Action<string> _status;
    private readonly PublishRequest _request;
    private readonly PublicationDrafts _drafts;
    private readonly bool _authorMode;
    private PublishFilePreview? _preview;
    private bool _allowEmptyConfig;
    private RectTransform? _host;
    private RectTransform? _optionsHost, _previewHost;
    private Action? _navigatePreview, _navigateOptions;
    private static bool _busy;
    private string _baseline = "";
    private bool _returnToTools;
    internal bool HasDirty => Fingerprint() != _baseline || _preview?.HasDirty == true;
    internal void RequestLeavePreview(Action next) { if (_preview != null) _preview.RequestLeave(next); else next(); }
    internal bool TryClosePreviewLayer() => _preview?.TryCloseLayer() == true;
    private string Fingerprint() => JsonConvert.SerializeObject(new { _request.Mode, _request.Name, _request.DisplayName, _request.Version, _request.Description, _request.Website, _request.DefaultProfile, _request.Icon, _request.OutputDirectory, _drafts.Revision,
        _request.Sign, _request.IncludeBootstrap, _request.LoaderDependency, _request.Profiles, _request.Languages, _request.Conflicts, _request.ExcludedExtras, Extras = _request.ExtraFiles.OrderBy(p => p), Configs = _request.ExtraConfigFiles.OrderBy(p => p), Inline = _request.InlineRoots.OrderBy(p => p), _request.Dependencies });
    private static string T(string en, string zh) => Loc.T(en, zh, zh);
    /// <summary>2.3.9: the format button caps at 360 px; the full names stay in the title and the chooser.</summary>
    private static string ShortFormatLabel(PublishMode mode) => mode switch {
        PublishMode.Local => Loc.T("Local modpack", "完整本地整合包", "完整本機整合包"),
        PublishMode.ThunderstoreComplete => Loc.T("Store ZIP (complete)", "完整商店包", "完整商店包"),
        PublishMode.Thunderstore => Loc.T("Store ZIP (legacy)", "依赖式商店包", "相依式商店包"), _ => Loc.T("Author data only", "仅作者资料", "僅作者資料") };
    internal static string FormatLabel(PublishMode mode) => mode switch {
        PublishMode.Local => T("Complete local modpack", "完整本地整合包"),
        PublishMode.ThunderstoreComplete => T("Complete store ZIP (fixed dependencies / inline)", "完整商店包（固定依赖 / 内置）"),
        PublishMode.Thunderstore => T("Dependency-based store ZIP (legacy)", "依赖式商店包（legacy，非独立完整环境）"), _ => T("Author data only", "仅作者资料") };
    internal PublishWizard(OverviewRepository repo, IEnumerable<string> ids, Action<string> status, bool authorMode = false)
    {
        _repo = repo; _status = status; _authorMode = authorMode;
        _request = new PublishRequest { Profiles = ids.ToList(), Languages = Loc.Scan().Select(g => g.Key).ToList(), Name = ThunderstoreUpdateService.ValidPart(repo.State.pack.name) ? repo.State.pack.name : "MCEPack_" + PackPublisher.PackId().Substring(0, 8), Version = System.Version.TryParse(repo.State.pack.version, out var version) && version.Build >= 0 ? version.ToString(3) : "1.0.0", Website = repo.State.pack.sourceNamespace.Length > 0 ? ThunderstoreUpdateService.HomeUrl(repo.State.pack.sourceNamespace, repo.State.pack.sourceName) : "", OutputDirectory = PackPublisher.DefaultOutputDirectory(repo) };
        _request.Description = repo.State.pack.name.Length > 0 ? repo.State.pack.name : T("A PEAK modpack", "一个 PEAK 整合包");
        _request.DisplayName = repo.State.pack.name.Length > 0 ? repo.State.pack.name : repo.ViewedProfile.name;
        _drafts = new PublicationDrafts(repo, _request);
        _request.Conflicts = PackConflictStore.LoadAuthorDraft(PackPublisher.PackId());
        _request.DefaultProfile = _request.Profiles.Contains(repo.ViewedProfileId) ? repo.ViewedProfileId : _request.Profiles.First();
        string icon = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "icon.png"); if (File.Exists(icon)) _request.Icon = icon;
        _baseline = Fingerprint();
    }
    internal void ShowOptions(RectTransform host, Action preview) { _returnToTools = false; _optionsHost = host; _host = host; _navigatePreview = preview; Options(); }
    internal void ShowPreview(RectTransform host, Action options) { _returnToTools = false; _previewHost = host; _host = host; _navigateOptions = options; Run(Preview); }
    internal void BackupSigningIdentity() { _returnToTools = true; Run(BackupIdentity); }
    internal void ImportSigningIdentity() { _returnToTools = true; Run(ImportIdentity); }
    internal void RefreshPackMetadata()
    {
        var pack = _repo.State.pack;
        if (ThunderstoreUpdateService.ValidPart(pack.name)) _request.Name = pack.name;
        if (System.Version.TryParse(pack.version, out var version) && version.Build >= 0) _request.Version = version.ToString(3);
        _request.Description = pack.name;
        _request.DisplayName = pack.name;
        _request.Website = pack.sourceName.Length > 0 ? ThunderstoreUpdateService.HomeUrl(pack.sourceNamespace, pack.sourceName) : "";
    }
    internal static void Show(OverviewRepository repo, IEnumerable<string> ids, Action<string> status)
    {
        if (_busy) { status(T("A publication is still being built.", "发布包仍在生成中。")); return; }
        string before = repo.ViewedProfileId; var selection = ids.ToArray(); repo.Save();
        if (before != repo.ViewedProfileId) selection = selection.Select(id => id == before ? repo.ViewedProfileId : id).ToArray();
        var wizard = new PublishWizard(repo, selection, status);
        OverviewUi.Choose(T("Publish format", "选择发布方式"), new List<(string,string)> { (T("Complete local modpack", "完整本地整合包"), "Local"), (T("Complete store ZIP (fixed dependencies / inline)", "完整商店包（固定依赖 / 明确内置）"), "ThunderstoreComplete"), (T("Dependency-based store ZIP (legacy)", "依赖式商店包（legacy，非独立完整环境）"), "Thunderstore"), (T("Author data only", "仅作者资料"), "Data") }, "Local", value => { wizard._request.Mode = (PublishMode)Enum.Parse(typeof(PublishMode), value); if (wizard._request.Mode == PublishMode.Thunderstore) wizard.LoadCatalog(); else wizard.Options(); });
    }
    private async void LoadCatalog()
    {
        var panel = OverviewUi.CreateModalPanel("MCE_PublishCatalog", 560, 320); if (panel == null) return;
        PublicationNavigation.Attach(panel);
        var text = OverviewUi.Text(panel, T("Reading store catalog…", "正在读取商店目录……"), 22); OverviewUi.Fill(text.rectTransform, 26);
        var cancel = OverviewUi.Button(panel, T("Cancel", "取消"), OverviewUi.CloseModal, 100, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 22, 16);
        var cancellation = new CancellationTokenSource(); panel.gameObject.AddComponent<SourceSearchLifetime>().Cancellation = cancellation;
        try { await ModSources.Catalog(cancellation.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { if (panel != null) ShowFailure(e); return; }
        if (panel != null) { if (_host != null) OverviewUi.CloseModal(); Options(); }
    }
    private void Options()
    {
        if (_optionsHost != null) _host = _optionsHost;
        RectTransform? panel;
        if (_host != null) { ClearHost(_host); panel = _host; }
        else panel = OverviewUi.CreateModalPanel("MCE_PublishWizard", 850, 650);
        if (panel == null) return;
        PublicationNavigation.Attach(panel);
        var title = OverviewUi.Text(panel, T("Publish modpack", "发布整合包") + " · " + FormatLabel(_request.Mode), 25); OverviewUi.Place(title.rectTransform, 22, 15, panel.rect.width - 44, 38);
        var content = SourceUi.Rows(panel, 62, panel.rect.height - 162, out var optionsScroll); ManagementUi.Scrollbar((RectTransform)optionsScroll.transform, optionsScroll); float y = 0, width = panel.rect.width - 58, buttonX = 0;
        void EndButtons() { if (buttonX <= 0) return; buttonX = 0; y += 46; }
        void Note(string text) { EndButtons(); var label = OverviewUi.Text(content, text, 16); label.textWrappingMode = TextWrappingModes.Normal; float h = Mathf.Max(32, label.GetPreferredValues(text, width - 8, 0).y + 8); OverviewUi.Place(label.rectTransform, 0, y, width - 8, h); y += h + 10; }
        void Field(string caption, string value, Action<string> set, bool multiline = false)
        {
            EndButtons();
            var label = OverviewUi.Text(content, caption, 17); OverviewUi.Place(label.rectTransform, 0, y, width, 28); y += 28;
            var input = OverviewUi.Input(content, value, multiline); input.onValueChanged.AddListener(v => set(v)); OverviewUi.Place((RectTransform)input.transform, 0, y, width - 8, multiline ? 68 : 34); y += multiline ? 82 : 46;
        }
        void Button(string caption, Action action, UiActionRole role = UiActionRole.Edit)
        { var button = OverviewUi.Button(content, caption, () => Run(action), 160, 34, role); float w = Mathf.Clamp(button.GetComponentInChildren<TMP_Text>(true).GetPreferredValues(caption).x + 26, 100, Mathf.Min(360, width - 8)); if (buttonX > 0 && buttonX + w > width - 8) EndButtons(); OverviewUi.Place((RectTransform)button.transform, buttonX, y, w, 34); buttonX += w + 10; }
        void Check(string caption, bool value, Action<bool> set)
        {
            EndButtons();
            var check = OverviewUi.Check(content, value, v => set(v)); OverviewUi.Place((RectTransform)check.transform, 2, y, 28, 28);
            var label = OverviewUi.Text(content, caption, 18); OverviewUi.Place(label.rectTransform, 42, y, width - 52, 32); y += 42;
        }
        Field(_request.Mode == PublishMode.Thunderstore ? T("Package name (letters, numbers, underscore)", "发布名称（英文、数字、下划线）") : T("Package name", "发布包名称"), _request.Name, v => _request.Name = v.Trim());
        Field(T("Modpack name in exported filenames", "整合包名称（用于导出文件名）"), _request.DisplayName, v => _request.DisplayName = v.Trim());
        Field(T("Version", "发布版本"), _request.Version, v => _request.Version = v.Trim());
        Field(T("Description", "发布简介"), _request.Description, v => _request.Description = v, true);
        Field(T("Project website", "项目主页"), _request.Website, v => _request.Website = v.Trim());
        Field(T("Output folder", "发布包保存目录"), _request.OutputDirectory, v => _request.OutputDirectory = v);
        Button(T("Choose output folder", "选择输出目录"), () => {
            if (PublishFolderPicker.TrySelect(out var path)) { if (!string.IsNullOrWhiteSpace(path)) _request.OutputDirectory = path!; Options(); }
            else OverviewUi.Ask(T("Publication output folder", "发布包输出目录"), new[] { (T("Folder path", "目录路径"), _request.OutputDirectory) }, values => { _request.OutputDirectory = values[0].Trim().Trim('"'); Options(); });
        });
        Button(T("Open output folder", "打开输出目录"), OpenOutputFolder);
        Button(T("Publish format: ", "发布方式：") + ShortFormatLabel(_request.Mode), () => OverviewUi.Choose(T("Publish format", "选择发布方式"), new List<(string,string)> { (T("Complete local modpack", "完整本地整合包"), "Local"), (T("Complete store ZIP (fixed dependencies / inline)", "完整商店包（固定依赖 / 明确内置）"), "ThunderstoreComplete"), (T("Dependency-based store ZIP (legacy)", "依赖式商店包（legacy，非独立完整环境）"), "Thunderstore"), (T("Author data only", "仅作者资料"), "Data") }, _request.Mode.ToString(), value => { _request.Mode = (PublishMode)Enum.Parse(typeof(PublishMode), value); if (_request.Mode == PublishMode.Thunderstore) LoadCatalog(); else Options(); }));
        Button(T("Default profile: ", "默认方案：") + _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile).name, () => OverviewUi.Choose(T("Default profile", "默认方案"), _repo.Profiles.ListProfiles().Where(p => _request.Profiles.Contains(p.id)).Select(p => (p.name, p.id)).ToList(), _request.DefaultProfile, id => { _request.DefaultProfile = id; Options(); }));
        Button(T("Languages: ", "发布语言：") + _request.Languages.Count, Languages);
        if (_authorMode) Button(Loc.T("Modpack conflict presets", "整合包冲突预设", "整合包衝突預設") + " · " + _request.Conflicts.rules.Count,
            () => RequestLeavePreview(() => PackConflictEditor.Show(_repo, PackPublisher.PackId(), _request.Conflicts, file => {
                _request.Conflicts = PackConflictStore.Clone(file); Options();
                _status(Loc.T("Conflict presets saved for every profile. Mod enablement is unchanged.", "冲突预设已保存，所有方案共用；当前模组启停保持原样。", "衝突預設已儲存，所有方案共用；目前模組啟停維持原樣。"));
            })));
        Check(T("Sign author data", "为作者资料签名"), _request.Sign, value => _request.Sign = value);
        if (PackPublisher.CompleteMode(_request.Mode))
        {
            Note(T("Complete packages always include independent BepInEx core. Game-root Doorstop remains a platform prerequisite.", "完整包始终包含独立 BepInEx core；游戏根 Doorstop 仍是平台前置条件。"));
            Note(PackPublisher.PublicationSourceLabel());
            var profile = _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile);
            int configCount = PackPublisher.ProfileConfigFiles(profile.config).Count();
            Note(Loc.F("Recorded: {0} config files / {1} settings", "已收录：{0} 个配置文件／{1} 项参数", "已收錄：{0} 個設定檔／{1} 項參數", configCount, profile.config.values.Count));
            if (configCount == 0) Note(T("No ordinary mod configuration will be included until you collect settings or choose additional files.", "当前未收录普通模组配置；可收录当前配置，或选择附加配置文件。"));
            Button(T("Collect current configuration", "收录当前配置"), CollectConfiguration, UiActionRole.Commit);
            Button(Loc.T("Additional configuration files: ", "附加配置文件：", "附加設定檔：") + _request.ExtraConfigFiles.Count, ConfigFiles);
            Note(T("Local export produces one ZIP only; working folders are cleaned automatically.", "完整本地导出只保留一个 ZIP，生成过程的临时目录会自动清理。"));
        }
        if (_request.Mode == PublishMode.ThunderstoreComplete)
        {
            Note(T("Host/loader tools remain inline. Uniquely mapped store packages use fixed dependencies; downloads require import consent.", "宿主/加载器工具保留内置；可唯一映射的商店组件使用固定依赖，导入同意后下载，不借当前环境。无法映射请明确内置。"));
            Button(T("Choose inline / fixed store packages", "选择组件内置 / 固定商店依赖"), Resolve);
            Button(T("Choose 256×256 PNG icon", "选择 256×256 PNG 图标"), () => { if (IntroductionFilePicker.TrySelect("image", out var path) && path != null) _request.Icon = path; Options(); });
        }
        if (_request.Mode == PublishMode.Thunderstore)
        {
            Field(T("Loader requirement (checked only on import)", "加载器要求（导入仅检查）"), _request.LoaderDependency, v => _request.LoaderDependency=v.Trim());
            Button(T("Choose 256×256 PNG icon", "选择 256×256 PNG 图标"), () => { if (IntroductionFilePicker.TrySelect("image", out var path) && path != null) _request.Icon = path; Options(); });
            EndButtons();
            var icon = OverviewUi.Text(content, _request.Icon, 14); OverviewUi.PlaceClipped(icon, 0, y, width - 10, 30); y += 40;
            Button(T("Review bundled mods and dependencies", "核对商店依赖与随包模组"), Resolve);
        }
        if (_request.Mode != PublishMode.Data) Button(T("Additional files: ", "附加文件：") + _request.ExtraFiles.Count, Extras);
        Button(T("Back up signing identity", "备份发布签名身份"), BackupIdentity, UiActionRole.Commit);
        Button(T("Import signing identity", "导入发布签名身份"), ImportIdentity, UiActionRole.Commit);
        EndButtons();
        content.sizeDelta = new Vector2(0, y + 10);
        var step = OverviewUi.Text(panel, Loc.T("Review the file list next, then generate the publication.", "下一步核对文件清单，再确认生成发布包。", "下一步核對檔案清單，再確認產生發布包。"), 16);
        OverviewUi.Place(step.rectTransform, 22, panel.rect.height - 90, panel.rect.width - 44, 28);
        float previewWidth = Mathf.Min(270, panel.rect.width - 160);
        var preview = OverviewUi.Button(panel, Loc.T("Next: preview and generate", "下一步：预览并生成", "下一步：預覽並產生"), () => Run(() => { if (_host != null) _navigatePreview?.Invoke(); else Preview(); }), previewWidth, 34, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)preview.transform, 22, 16);
        if (_host == null) { var cancel = OverviewUi.Button(panel, T("Cancel", "取消"), OverviewUi.CloseModal, 100, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 35 + previewWidth, 16); }
    }
    private void CollectConfiguration()
        => CollectConfiguration(Options);
    private void CollectConfiguration(Action completed)
    {
        if (_request.DefaultProfile != _repo.ViewedProfileId) throw new InvalidOperationException(T("View the default publication profile before collecting its configuration.", "请先在方案页切换到本次发布的默认方案，再收录配置。"));
        OverviewUi.Confirm(T("Store current settings into the default profile?", "将当前配置收录到默认方案？"), () => Run(() => {
            string before = _repo.ViewedProfileId; _repo.CaptureCurrentIntoProfile();
            _request.Profiles = _request.Profiles.Select(id => id == before ? _repo.ViewedProfileId : id).Distinct().ToList(); _request.DefaultProfile = _repo.ViewedProfileId;
            foreach (string key in _request.DraftFiles.Keys.Where(k => k.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)).ToList()) _request.DraftFiles.Remove(key);
            var recorded = new HashSet<string>(PackPublisher.ProfileConfigFiles(_repo.DraftConfig), StringComparer.OrdinalIgnoreCase); _request.ExtraConfigFiles.RemoveWhere(recorded.Contains);
            _allowEmptyConfig = false; completed(); _status(T("Current configuration collected into the profile.", "当前配置已收录到方案。"));
        }));
    }
    private void Languages()
    {
        var choices = Loc.Scan().Select(g => (g.DisplayOrStem + " · " + g.Key, g.Key)).ToList();
        Multi(T("Languages", "选择发布语言"), choices, new HashSet<string>(_request.Languages, StringComparer.OrdinalIgnoreCase), selected => { _request.Languages = selected.ToList(); Options(); });
    }
    private void Extras()
        => Extras(Options);
    private void Extras(Action completed)
    {
        var choices = PackPublisher.Extras().Where(p => _request.Mode != PublishMode.Thunderstore || p.Relative.StartsWith("BepInEx/core/") || p.Relative.StartsWith("BepInEx/patchers/") || p.Relative.StartsWith("BepInEx/monomod/")).ToList();
        if (PackPublisher.CompleteMode(_request.Mode)) choices.RemoveAll(p => p.Relative.StartsWith("BepInEx/core/") || new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }.Contains(p.Relative));
        Multi(T("Additional files — unchecked patchers are explicitly excluded", "选择附加文件（未勾选的启动补丁将明确排除）"), choices, new HashSet<string>(_request.ExtraFiles, StringComparer.OrdinalIgnoreCase), selected => {
            _request.ExtraFiles = selected;
            foreach(var choice in choices.Where(p=>p.Relative.StartsWith("BepInEx/patchers/")||p.Relative.StartsWith("BepInEx/monomod/")))
                if(selected.Contains(choice.Relative)) _request.ExcludedExtras.Remove(choice.Relative); else _request.ExcludedExtras[choice.Relative]=T("Author excluded after attachment review", "作者核对附带内容后排除");
            completed();
        });
    }
    private void ConfigFiles()
        => ConfigFiles(Options);
    private void ConfigFiles(Action completed)
    {
        var profile = _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile);
        var choices = PackPublisher.AdditionalConfigFiles(profile.config).Select(p => (p, p)).ToList();
        Multi(Loc.T("Select additional configs — check for personal connection details before sharing", "选择附加配置——分享前检查其中的个人连接信息", "選擇附加設定——分享前檢查其中的個人連線資訊"), choices,
            new HashSet<string>(_request.ExtraConfigFiles.Where(p => choices.Any(c => c.Item2.Equals(p, StringComparison.OrdinalIgnoreCase))), StringComparer.OrdinalIgnoreCase), selected => { _request.ExtraConfigFiles = selected; completed(); });
    }
    private static void Multi(string title, IReadOnlyList<(string Label,string Value)> choices, HashSet<string> selected, Action<HashSet<string>> confirm)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_PublishSelection", 800, 600); if (panel == null) return;
        var heading = OverviewUi.Text(panel, title, 23); OverviewUi.PlaceClipped(heading, 22, 12, panel.rect.width - 44, 38);
        var content = SourceUi.Rows(panel, 60, panel.rect.height - 125, out _);
        for (int i = 0; i < choices.Count; i++)
        {
            var item = choices[i]; var check = OverviewUi.Check(content, selected.Contains(item.Value), value => { if (value) selected.Add(item.Value); else selected.Remove(item.Value); }); OverviewUi.Place((RectTransform)check.transform, 0, i * 42, 28, 28);
            var label = OverviewUi.Text(content, item.Label, 18); OverviewUi.PlaceClipped(label, 40, i * 42, panel.rect.width - 90, 34);
        }
        content.sizeDelta = new Vector2(0, choices.Count * 42);
        var done = OverviewUi.Button(panel, T("Confirm", "确认"), () => { OverviewUi.CloseModal(); confirm(selected); }, 110, 34, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)done.transform, 22, 16);
    }
    private void Resolve()
    {
        var groups = PackPublisher.SelectInventory(_repo, _request, PackPublisher.Inventory(_repo)).GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var panel = OverviewUi.CreateModalPanel("MCE_PublishDependencies", 850, 600); if (panel == null) return;
        var heading = OverviewUi.Text(panel, T("Dependency or bundled local mod", "下载依赖／本地随包附带"), 23); OverviewUi.Place(heading.rectTransform, 22, 14, panel.rect.width - 44, 38);
        var content = SourceUi.Rows(panel, 60, panel.rect.height - 125, out _);
        for (int i = 0; i < groups.Count; i++)
        {
            var mod = groups[i]; bool inline = PackPublisher.IsInline(_request, mod);
            string dependency = _request.Dependencies.TryGetValue(mod.PackageRoot, out var chosen) ? chosen : PackPublisher.Dependency(mod);
            string state = inline ? T("Bundled local", "本地随包") : dependency.Length > 0 ? dependency : T("Pending", "待定");
            var label = OverviewUi.Text(content, mod.Name + " · " + state, 17); OverviewUi.PlaceClipped(label, 0, i * 45, panel.rect.width - 180, 36);
            var button = OverviewUi.Button(content, T("Choose", "选择"), () => OverviewUi.Choose(mod.Name, new List<(string,string)> { (T("Bundle local files", "作为本地模组随包附带"), "local"), (T("Set store dependency", "填写商店依赖"), "store"), (T("Find store source", "查找商店来源"), "find") }, "", action => {
                if (action == "local") { _request.InlineRoots.Add(mod.PackageRoot); _request.Dependencies.Remove(mod.PackageRoot); Resolve(); }
                if (action == "store") OverviewUi.Ask(T("Store dependency", "商店依赖"), new[] { ("Team-Package-1.2.3", dependency) }, values => { if (!PackPublisher.ValidDependency(values[0].Trim())) throw new ArgumentException(Loc.T("Enter the complete, actual store dependency string.", "请输入完整的真实商店依赖串。", "請輸入完整的真實商店依賴串。")); _request.Dependencies[mod.PackageRoot] = values[0].Trim(); _request.InlineRoots.Remove(mod.PackageRoot); Resolve(); });
                if (action == "find") SourceUi.Search(_repo, mod, Resolve, _status);
            }), 95, 34, role: UiActionRole.Edit); OverviewUi.Place((RectTransform)button.transform, panel.rect.width - 165, i * 45, 95, 34);
        }
        content.sizeDelta = new Vector2(0, groups.Count * 45);
        var done = OverviewUi.Button(panel, T("Done", "完成核对"), () => { OverviewUi.CloseModal(); Options(); }, 120, 34, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)done.transform, 22, 16);
    }
    private void Preview()
    {
        if (_previewHost != null) _host = _previewHost;
        if (PackPublisher.StoreMode(_request.Mode) && !PackPublisher.IsIcon(_request.Icon)) { Options(); OverviewUi.Ask(T("Publication icon", "发布图标"), new[] { (T("256×256 PNG path", "256×256 PNG 文件路径"), _request.Icon) }, values => { _request.Icon = values[0].Trim().Trim('"'); Options(); }); return; }
        if (_request.Description.Length > 250 && PackPublisher.StoreMode(_request.Mode)) throw new InvalidOperationException(T("Description exceeds 250 characters.", "商店简介超过250字符。"));
        if (_request.Languages.Count == 0) throw new InvalidOperationException(T("Select at least one language.", "请至少选择一种发布语言。"));
        _request.OutputDirectory = PackPublisher.ValidateOutputDirectory(_repo, _request.OutputDirectory);
        var mods = PackPublisher.SelectInventory(_repo, _request, PackPublisher.Inventory(_repo));
        if (_request.Mode == PublishMode.Thunderstore && mods.Any(m => !PackPublisher.IsInline(_request, m) && !PackPublisher.ValidDependency(_request.Dependencies.TryGetValue(m.PackageRoot, out var dependency) ? dependency : PackPublisher.Dependency(m)))) { _status(T("Resolve pending packages first.", "请先处理待定模组的发布方式。")); Resolve(); return; }
        var files = PackPublisher.PreviewFiles(_repo, _request, mods);
        var missing=PackPublisher.MissingTargets(_repo,_request,mods).Concat(_request.ExcludedExtras.Select(p=>p.Key+"："+p.Value)).ToList(); if(missing.Count>0) _status(string.Join("\n",missing));
        RectTransform? previewHost = _host ?? OverviewUi.CreateModalPanel("MCE_PublishPreview", 1040, 700);
        if (previewHost == null) return;
        _preview = PublishFilePreview.Show(previewHost, files, _request, _repo, _drafts, _status, ReturnOptions, Build, OpenOutputFolder,
            () => CollectConfiguration(Preview), () => ConfigFiles(Preview), () => Extras(Preview), _preview?.SelectedCategory);
    }
    private async void Build()
    {
        if (_request.Mode == PublishMode.Local && !_allowEmptyConfig && _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile).config.IsEmpty && _request.ExtraConfigFiles.Count == 0)
        {
            OverviewUi.Confirm(T("No ordinary mod configurations are recorded. Generate this package without them?", "尚未收录普通模组配置。仍然生成不含这些配置的发布包？"), () => { _allowEmptyConfig = true; Build(); }); return;
        }
        if (_busy) { _status(T("A publication is still being built.", "发布包仍在生成中。")); return; } _busy = true;
        var panel = OverviewUi.CreateModalPanel("MCE_PublishProgress", 660, 340); if (panel == null) { _busy = false; return; }
        var text = OverviewUi.Text(panel, T("Building publication…", "正在生成发布包……"), 23); OverviewUi.Fill(text.rectTransform, 26);
        try
        {
            var frozen = _request.Snapshot(); string builtFingerprint = Fingerprint();
            await Task.Yield();
            string output = await PackPublisher.PublishAsync(_repo, frozen);
            if (panel == null) { Plugin.Log.LogInfo("[发布包] " + output); return; }
            _status(T("Publication generated: ", "发布包已生成：") + output);
            _baseline = builtFingerprint;
            if (panel != null) ShowResult(T("Publication ready", "发布包已生成"), output, Directory.Exists(output) ? output : Path.GetDirectoryName(output));
        }
        catch (Exception e) { if (panel != null) ShowFailure(e); else Plugin.Log.LogWarning("[发布包] " + e.Message); }
        finally { _busy = false; }
    }
    private void BackupIdentity()
    {
        if (!File.Exists(PublishedPack.KeyPath))
        {
            ShowResult(Loc.T("Signing identity not created", "签名身份尚未建立", "簽名身分尚未建立"),
                Loc.T("Generate a publication with 'Sign author data' checked first. Its signing identity can then be backed up here.", "请先勾选“为作者资料签名”，并成功生成一次发布包，然后再来备份签名身份。", "請先勾選「為作者資料簽名」，並成功產生一次發布包，然後再來備份簽名身分。"));
            return;
        }
        string destination = Path.Combine(Path.GetDirectoryName(PublishedPack.KeyPath)!, "backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
        File.Copy(PublishedPack.KeyPath, destination, false);
        string message = T("Private signing backup: ", "签名身份备份（含私钥）：") + "\n" + destination;
        _status(message);
        ShowResult(Loc.T("Signing identity backed up", "签名身份已备份", "簽名身分已備份"), message, Path.GetDirectoryName(destination));
    }
    private void ImportIdentity()
    {
        OverviewUi.Ask(T("Import signing identity", "导入签名身份"), new[] { (T("Private identity backup file", "身份备份文件路径"), "") }, values => {
            string source = values[0].Trim().Trim('"'); using var rsa = RSA.Create(); rsa.ImportParameters((JsonConvert.DeserializeObject<SigningKey>(File.ReadAllText(source)) ?? throw new InvalidDataException(Loc.T("The signing identity file is empty.", "签名身份文件为空。", "簽名身份文件為空。"))).Parameters()); rsa.ExportParameters(true);
            string? previous = null;
            if (File.Exists(PublishedPack.KeyPath)) { previous = PublishedPack.KeyPath + ".before-import-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"); File.Copy(PublishedPack.KeyPath, previous, false); }
            Directory.CreateDirectory(Path.GetDirectoryName(PublishedPack.KeyPath)!); OverviewRepository.AtomicWrite(PublishedPack.KeyPath, File.ReadAllText(source));
            string title = Loc.T("Signing identity import complete", "签名身份导入完成", "簽名身分匯入完成");
            string message = title + "\n" + PublishedPack.KeyPath;
            if (previous != null) message += "\n\n" + T("Private signing backup: ", "签名身份备份（含私钥）：") + "\n" + previous;
            _status(message); ShowResult(title, message, Path.GetDirectoryName(PublishedPack.KeyPath));
        });
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { ShowFailure(error); }
    }

    private void ShowFailure(Exception error)
    {
        Plugin.Log.LogError("[发布包] " + error);
        string message = Loc.LocalizeMessage(error.Message);
        if (message.Length > 360) message = message.Substring(0, 357) + "…";
        _status(message);
        ShowResult(Loc.T("Publication could not continue", "发布操作未完成", "發布操作未完成"), message);
    }

    private void ShowResult(string title, string message, string? folder = null)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_PublishResult", 760, 440); if (panel == null) return;
        var heading = OverviewUi.Text(panel, title, 24); OverviewUi.PlaceClipped(heading, 22, 14, panel.rect.width - 44, 38);
        var content = SourceUi.Rows(panel, 62, panel.rect.height - 132, out _);
        var body = OverviewUi.Text(content, message, 20); body.textWrappingMode = TextWrappingModes.Normal;
        float width = panel.rect.width - 56, height = Mathf.Max(60, body.GetPreferredValues(message, width, 0).y + 16);
        OverviewUi.Place(body.rectTransform, 0, 0, width, height); content.sizeDelta = new Vector2(0, height);
        var back = OverviewUi.Button(panel, _returnToTools ? T("Close", "关闭") : T("Back to options", "返回调整"), _returnToTools ? OverviewUi.CloseModal : ReturnOptions, 150, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)back.transform, 22, 16);
        if (folder != null)
        {
            var open = OverviewUi.Button(panel, Loc.T("Open containing folder", "打开所在文件夹", "開啟所在資料夾"), () => Run(() => {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(T("Folder not found: ", "文件夹不存在：") + folder);
                Application.OpenURL(new Uri(Path.GetFullPath(folder)).AbsoluteUri);
            }), 185, 34, role: UiActionRole.Edit);
            OverviewUi.Bottom((RectTransform)open.transform, 185, 16);
        }
    }

    private void ReturnOptions()
    {
        if (_host != null) { OverviewUi.CloseModal(); if (_navigateOptions != null) _navigateOptions(); else Options(); }
        else Options();
    }

    private void OpenOutputFolder()
    {
        string path = _request.OutputDirectory;
        Directory.CreateDirectory(PackPublisher.ValidateOutputDirectory(_repo, path));
        Application.OpenURL(new Uri(Path.GetFullPath(path)).AbsoluteUri);
    }

    private static void ClearHost(RectTransform host)
    {
        for (int i = host.childCount - 1; i >= 0; i--) { var child = host.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
    }
}
