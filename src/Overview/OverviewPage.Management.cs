#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    private OverviewManagementCenter? _management;
    private OverviewGroupEditor? _managementGroups;
    private OverviewNetworkSettings? _managementNetwork;
    private PublishWizard? _managementPublisher;
    private OverviewGroup? _managementSelectedGroup;
    /// <summary>Non-null: the groups page opens on a new group under this parent ("" = top level).</summary>
    private string? _managementNewGroupParent;
    private OverviewMod? _managementSelectedMod;
    private readonly List<Action> _modInformationStages = new();

    private void OpenManagement(ManagementTab tab = ManagementTab.Information)
    {
        if (tab == ManagementTab.Updates) { OpenUpdateQueue(); return; }
        CloseUpdateQueue(); _list?.HideAllHints();
        if (_management == null) _management = new OverviewManagementCenter(_frame, BuildManagementPage, CloseManagement, OpenUpdateQueue);
        _management.Show(tab);
    }
    private void CloseManagement()
    {
        _managementGroups?.Dispose(); _managementGroups = null;
        _managementNetwork?.Dispose(); _managementNetwork = null;
        _managementPublisher = null; _managementSelectedGroup = null; _managementNewGroupParent = null; _managementSelectedMod = null;
        _management?.Dispose(); _management = null;
    }
    private void ManagementStatus(string value) { _management?.Status(value); SetStatus(value); }
    private void ManagementAction(Action action)
    { try { action(); } catch (Exception error) { Plugin.Log.LogError(error); ManagementStatus(Loc.LocalizeMessage(error.Message)); } }
    private void BuildManagementPage(ManagementTab tab, RectTransform host)
    {
        switch (tab)
        {
            case ManagementTab.Information: BuildManagementInformation(host); break;
            case ManagementTab.Packs: BuildManagementPacks(host); break;
            case ManagementTab.Groups:
                _managementGroups?.Dispose();
                _managementGroups = new OverviewGroupEditor(_repo, _list?.Mods ?? Array.Empty<OverviewMod>(), ManagementStatus,
                    () => { _list?.Reload(); RefreshHeader(); });
                if (_managementNewGroupParent != null) { _managementGroups.NewGroupParent = _managementNewGroupParent; _managementNewGroupParent = null; _managementGroups.ShowEmbedded(host, null); }
                else _managementGroups.ShowEmbedded(host, _managementSelectedGroup ?? new GroupTree(_repo.State.groups).Flat.FirstOrDefault());
                _management!.SetDirty(tab, () => _managementGroups?.HasDirty == true);
                _management.SetStage(tab, () => _managementGroups?.StageDraft()); break;
            case ManagementTab.Profiles: BuildProfileManager(host, _managedProfileId.Length > 0 ? _managedProfileId : null); break;
            case ManagementTab.Network:
                _managementNetwork = new OverviewNetworkSettings(host, ManagementStatus);
                _management!.SetDirty(tab, () => _managementNetwork?.HasDirty == true); break;
            case ManagementTab.Tools: BuildManagementTools(host); break;
            case ManagementTab.Publication:
                Publisher().ShowOptions(host, () => OpenManagement(ManagementTab.Preview));
                _management!.SetDirty(tab, () => _managementPublisher?.HasDirty == true); break;
            case ManagementTab.Preview:
                Publisher().ShowPreview(host, () => OpenManagement(ManagementTab.Publication));
                _management!.SetDirty(tab, () => _managementPublisher?.HasDirty == true);
                _management.SetLeave(tab, next => Publisher().RequestLeavePreview(next)); break;
        }
    }
    private PublishWizard Publisher()
    {
        if (_managementPublisher == null) { _managementPublisher = new PublishWizard(_repo, new[] { _repo.ViewedProfileId }, ManagementStatus, _author); }
        return _managementPublisher;
    }
    private void ShowManagementSources()
    {
        var options = new List<(string, string)> {
            (T("Find all missing sources", "批量查找缺失来源"), "find"), (T("Show source labels", "显示来源标签"), "labels"),
            (T("Global update permission", "全局更新检查许可"), "permission"), (T("Review update changes", "查看更新变动"), "changes"),
            (T("Mark update changes as read", "更新变动标记为已读"), "read") };
        OverviewUi.Choose(T("Sources and permissions", "来源与更新许可"), options, "", value => ManagementAction(() => {
            if (value == "find") _list?.BatchFindSources();
            if (value == "labels") { OverviewOptions.ShowSourceLabels.Value = !OverviewOptions.ShowSourceLabels.Value; _list?.RefreshTagFilter(); }
            if (value == "permission") _list?.SetGlobalAllowed(!_repo.GetGlobalUpdateAllowed());
            if (value == "changes") OverviewUi.ShowText(T("Update changes", "更新变动"), ChangeLedger.Summary());
            if (value == "read") { ChangeLedger.Clear(); ManagementStatus(T("All updates marked as read.", "全部更新已标记为已读。")); }
        }));
    }
    private void BuildManagementInformation(RectTransform host)
    {
        var packHost = OverviewUi.Rect("PackInformation", host); OverviewUi.Place(packHost, 0, 48, host.rect.width, host.rect.height - 48);
        var modHost = OverviewUi.Rect("ModInformation", host); OverviewUi.Place(modHost, 0, 48, host.rect.width, host.rect.height - 48);
        Button? packButton = null, modButton = null;
        void SelectInformation(bool individual)
        {
            packHost.gameObject.SetActive(!individual); modHost.gameObject.SetActive(individual);
            if (packButton != null) UiActionStyle.Select(packButton, !individual);
            if (modButton != null) UiActionStyle.Select(modButton, individual);
        }
        packButton = OverviewUi.Button(host, T("Modpack information", "整合包信息"), () => SelectInformation(false), 180, 34, UiActionRole.Neutral);
        modButton = OverviewUi.Button(host, T("Individual mod", "单个模组"), () => SelectInformation(true), 150, 34, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)packButton.transform, 0, 0, 180, 34); OverviewUi.Place((RectTransform)modButton.transform, 192, 0, 150, 34);
        var candidate = CopyPack(_repo.State.pack); string baseline = JsonConvert.SerializeObject(candidate);
        var modDrafts = new Dictionary<string, ManagementModDraft>();
        _modInformationStages.Clear();
        _management!.SetStage(ManagementTab.Information, () => { foreach (var stage in _modInformationStages) stage(); });
        var form = new ManagementForm(packHost, ManagementAction);
        if (_repo.ViewedProfile.kind == OverviewProfileKind.Default)
        {
            form.Text(T("The default configuration is personal mode. Create or choose a personal profile to enter modpack information.", "默认配置是个人模式；请创建或选择个人方案，再填写整合包资料。"));
            form.Button(T("Open profiles", "打开方案管理"), () => OpenManagement(ManagementTab.Profiles));
        }
        else
        {
            form.Text(T("Editing: ", "正在编辑：") + _repo.ViewedProfile.name);
            form.Field(T("Name", "名称"), candidate.name, changed: v => candidate.name = v);
            form.Field(T("Version", "版本"), candidate.version, changed: v => candidate.version = v);
            form.Field(T("Author", "作者"), candidate.author, changed: v => candidate.author = v);
            form.Field(T("Contact", "联系方式"), candidate.contact, changed: v => candidate.contact = v);
            form.Field(T("Publication date (YYYY-M-D)", "发布日期（年-月-日）"), candidate.date, changed: v => candidate.date = v);
            string homepage = candidate.sourceName.Length > 0 ? ThunderstoreUpdateService.HomeUrl(candidate.sourceNamespace, candidate.sourceName) : "";
            var sourceField = form.Field(T("Thunderstore package URL", "整合包商店主页"), homepage);
            sourceField.onValueChanged.AddListener(_ => { });
            form.Field(T("Introduction title", "介绍页标题"), candidate.introTitle, changed: v => candidate.introTitle = v);
            form.Check(T("Show introduction", "显示整合包介绍"), candidate.introEnabled, v => candidate.introEnabled = v);
            form.Check(T("Allow pack update checks", "允许检查整合包更新"), candidate.updateAllowed, v => candidate.updateAllowed = v);
            form.Button(T("Edit introduction body", "编辑介绍正文"), () => {
                _management?.RequestTransition(() => { CloseManagement(); _author = true; _repo.SetAuthorMode(true); _list?.SetAuthorMode(true); _hotkeys?.SetAuthorMode(true); SwitchTab(PageTab.Introduction); _document?.SetAuthorMode(true); RefreshHeader(); });
            });
            void StageInfo()
            {
                if (JsonConvert.SerializeObject(candidate) == baseline && sourceField.text == homepage) return;
                if (!TryPackDate(candidate.date, out string normalized)) throw new ArgumentException(T("Enter a valid publication date.", "请填写有效的发布日期。"));
                var saved = CopyPack(candidate); saved.name = saved.name.Trim(); saved.version = saved.version.Trim(); saved.date = normalized;
                if (!string.IsNullOrWhiteSpace(sourceField.text))
                {
                    if (!ThunderstoreUpdateService.TryParseSource(sourceField.text.Trim(), out saved.sourceNamespace, out saved.sourceName)) throw new ArgumentException(T("The store address is invalid.", "商店主页地址无效。"));
                }
                else saved.sourceNamespace = saved.sourceName = "";
                _repo.EnsureEditableProfile(); _repo.State.pack = saved; _repo.PersistRecoveryDraft();
                candidate = CopyPack(_repo.State.pack); baseline = JsonConvert.SerializeObject(candidate); homepage = sourceField.text;
                _managementPublisher?.RefreshPackMetadata(); _management?.Invalidate(ManagementTab.Publication); _management?.Invalidate(ManagementTab.Preview);
                RefreshHeader();
            }
            _management!.SetStage(ManagementTab.Information, () => { StageInfo(); foreach (var stage in _modInformationStages) stage(); });
            form.Action(T("Save information", "保存资料"), () => { StageInfo(); _repo.SavePackInformation(ConfigurationSaveScope.Information); ManagementStatus(T("Information saved.", "资料已保存。")); });
            form.Action(T("Reset draft", "撤销修改"), () => _management?.Rebuild(ManagementTab.Information), UiActionRole.Neutral);
            _management!.SetDirty(ManagementTab.Information, () => JsonConvert.SerializeObject(candidate) != baseline || sourceField.text != homepage || modDrafts.Values.Any(v => v.Dirty));
        }
        BuildManagementModInformation(modHost, modDrafts);
        SelectInformation(_managementSelectedMod != null);
        if (_repo.ViewedProfile.kind == OverviewProfileKind.Default) _management!.SetDirty(ManagementTab.Information, () => modDrafts.Values.Any(v => v.Dirty));
    }
    private sealed class ManagementModDraft
    {
        internal string Name = "", Description = "", Version = "", InitialName = "", InitialDescription = "", InitialVersion = "";
        internal bool Dirty => Name != InitialName || Description != InitialDescription || Version != InitialVersion;
        internal void Saved() { InitialName = Name; InitialDescription = Description; InitialVersion = Version; }
    }
    private void BuildManagementModInformation(RectTransform host, Dictionary<string, ManagementModDraft> drafts)
    {
        var mods = (_list?.Mods ?? Array.Empty<OverviewMod>()).ToList();
        if (mods.Count == 0) { new ManagementForm(host, ManagementAction).Text(T("No mods available.", "暂无可显示的模组。")); return; }
        OverviewMod selected = mods.FirstOrDefault(m => m.Id == _managementSelectedMod?.Id) ?? mods[0];
        float listWidth = Mathf.Clamp(host.rect.width * .28f, 220, 360);
        var selector = OverviewUi.Rect("ModSelector", host); OverviewUi.Place(selector, 0, 0, listWidth, host.rect.height);
        var detailHost = OverviewUi.Rect("ModInformationBody", host); OverviewUi.Place(detailHost, listWidth + 22, 0, host.rect.width - listWidth - 22, host.rect.height);
        _ = new ManagementModSelector(selector, _repo, mods, selected.Id, mod => { selected = mod; _managementSelectedMod = mod; ShowDetails(); });
        void ShowDetails()
        {
            foreach (Transform child in detailHost) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            string key = selected.Id.Length > 0 ? selected.Id : selected.Guid;
            if (!drafts.TryGetValue(key, out var draft))
            {
                draft = new ManagementModDraft { Name = OverviewLanguageBridge.GetDisplayName(selected), Description = OverviewLanguageBridge.GetDescription(selected), Version = selected.Rule.versionLabel };
                draft.Saved(); drafts[key] = draft;
            }
            var form = new ManagementForm(detailHost, ManagementAction);
            form.Text(selected.Name, true);
            form.Text(T("Original name: ", "原始名称：") + selected.RawName);
            form.Text(T("Author: ", "作者：") + selected.Author);
            form.Text(T("Plugin version: ", "插件实际版本：") + selected.PluginVersion + "\n" + T("Package version: ", "安装包版本：") + selected.PackageVersion);
            form.Text(T("Language file: ", "编辑语言文件：") + OverviewLanguageBridge.CurrentDisplayName);
            form.Field(T("Display name", "显示名称"), draft.Name, changed: v => draft.Name = v);
            form.Field(T("Display description", "显示简介"), draft.Description, true, v => draft.Description = v);
            form.Field(T("Display version label (blank uses actual version)", "显示版本标签（留空显示实际版本）"), draft.Version, changed: v => draft.Version = v);
            SourceUi.Actions(form, _repo, selected, () => { _list?.Reload(); ShowDetails(); }, ManagementStatus, mods);
            form.Text(T("Folders", "常用目录"), true);
            form.Button(T("Open mod folder", "打开模组目录"), () => ManagementUi.OpenFolder(Directory.Exists(selected.PackageRoot) ? selected.PackageRoot : Path.GetDirectoryName(selected.PackageRoot)!));
            void StageModInformation()
            {
                if (!draft.Dirty) return;
                _repo.EnsureEditableProfile(); var rule = _repo.GetRule(key);
                rule.versionLabel = draft.Version.Trim(); rule.alias = draft.Name.Trim(); rule.descriptionOverridden = true; rule.description = draft.Description;
                _repo.PersistRecoveryDraft(); draft.Version = draft.Version.Trim(); draft.Saved();
            }
            _modInformationStages.Add(StageModInformation);
            form.Action(T("Save mod information", "保存模组资料"), () => {
                StageModInformation(); _repo.SavePackInformation(ConfigurationSaveScope.ModInformation, key);
                _list?.Reload(); ManagementStatus(T("Mod information saved.", "模组资料已保存。"));
            });
            form.Action(T("Reset draft", "撤销修改"), () => { drafts.Remove(key); ShowDetails(); }, UiActionRole.Neutral);
        }
        ShowDetails();
    }
    private void BuildManagementTools(RectTransform host)
    {
        var form = new ManagementForm(host, ManagementAction);
        form.Text(T("Unsaved mod operations", "未保存的模组变更"), true);
        foreach (var operation in ResourceOperationDraft.Snapshot())
        {
            if (ResourceOperationDraft.AwaitingAcknowledgement) { form.Text(Loc.LocalizeMessage("Submitted; waiting for worker acknowledgement.") + "\n" + ResourceOperationDraft.Label(operation)); continue; }
            string id = operation.Id;
            form.Button(T("Remove pending: ", "撤销待办：") + ResourceOperationDraft.Label(operation), () => {
                ResourceOperationDraft.Remove(id); _management?.Rebuild(ManagementTab.Tools); RefreshHeader();
            }, UiActionRole.Danger);
        }
        form.Text(Loc.T("Installation folders", "安装目录整理", "安裝目錄整理"), true);
        form.Button(Loc.T("Organize into group folders", "整理到分组文件夹", "整理到分組資料夾"), PreviewFolderLayout);
        form.Text(T("Compatibility", "模组兼容"), true);
        form.Button(T("Configure mod conflicts", "设置模组冲突"), () => {
            string id=PackPublisher.PackId();
            PackConflictEditor.Show(_repo,id,PackConflictStore.LoadAuthorDraft(id),file=>{ PackConflictStore.SaveAuthorDraft(id,file,_list?.Mods); ManagementStatus(T("Conflict presets saved.","冲突预设已保存。")); });
        },UiActionRole.Edit);
        form.Text(T("Updates", "更新"), true);
        form.Button(T("Open update queue", "打开更新小窗"), OpenUpdateQueue);
        form.Button(T("Sources and permissions", "来源与更新许可"), ShowManagementSources);
        form.Text(T("Profiles and data", "方案与资料"), true);
        form.Button(T("Restore original preset to draft", "恢复原版预设到草稿"), () => {
            _repo.RestoreOriginalPresetDraft(); ReloadProfileViews();
            ManagementStatus(T("Original preset restored to draft. Save configuration to use it.", "原版预设已恢复到草稿，保存配置后使用。"));
        }, UiActionRole.Edit);
        form.Button(T("Import modpack", "导入整合包"), () => OpenManagement(ManagementTab.Packs));
        form.Button(T("Export profile data", "导出方案资料"), () => ShowExportPanel(_repo.ViewedProfileId));
        form.Button(T("Open export folder", "打开导出目录"), () => ManagementUi.OpenFolder(Updating.MceDirectories.PackOutput(_repo.ConfigRootDirectory)), UiActionRole.Neutral);
        form.Text(T("Signing identity", "发布签名身份"), true);
        form.Button(T("Back up signing identity", "备份签名身份"), () => Publisher().BackupSigningIdentity());
        form.Button(T("Import signing identity", "导入签名身份"), () => Publisher().ImportSigningIdentity());
        form.Text(T("Folders", "常用目录"), true);
        form.Button(T("Mods folder", "模组目录"), () => ManagementUi.OpenFolder(Paths.PluginPath), UiActionRole.Neutral);
        form.Button(T("Configuration folder", "配置目录"), () => ManagementUi.OpenFolder(Paths.ConfigPath), UiActionRole.Neutral);
        form.Button(T("Profiles folder", "方案目录"), () => ManagementUi.OpenFolder(_repo.ConfigRootDirectory), UiActionRole.Neutral);
        form.Text(T("Overview display", "概览显示"), true);
        form.Check(T("Show modpack name", "入口显示整合包名称"), OverviewOptions.ShowPackName.Value, v => OverviewOptions.ShowPackName.Value = v);
        form.Check(T("Show modpack version", "入口显示整合包版本"), OverviewOptions.ShowPackVersion.Value, v => OverviewOptions.ShowPackVersion.Value = v);
        form.Button(T("User guide", "使用说明"), () => _management?.RequestTransition(() => { CloseManagement(); _helpPage ??= Help.HelpPage.Create(this); _helpPage.Open(); }), UiActionRole.Guide);
    }
    private void ShowProfileImport()
    {
        OverviewUi.Ask(T("Import profile data", "导入方案资料"), new[] { (T("Configuration file or legacy directory", "配置文件路径或旧格式目录"), "") }, values => {
            string path = values[0].Trim().Trim('"');
            if (File.Exists(path))
            {
                _repo.ImportConfigurationDraft(path); ReloadProfileViews();
                ManagementStatus(T("Configuration imported into the draft. Save to use it.", "配置已导入草稿，保存后使用。"));
            }
            else
            {
                var imported = _repo.Profiles.ImportDirectory(path); _management?.Rebuild(ManagementTab.Profiles);
                ManagementStatus(T("Legacy profile imported: ", "旧格式方案已导入：") + imported.name);
            }
        });
    }
    private void RequestManagementProfileLeave(Action leave)
    {
        _management?.StageDrafts();
        if (_management?.HasDirty == true) OverviewUi.ConfirmDiscard(T("Discard management drafts before switching profiles?", "切换方案前放弃管理中心中未保存的修改？"), () => { CloseManagement(); RequestProfileLeave(leave); });
        else RequestProfileLeave(leave);
    }
    private void ManageProfiles(string? select)
    { _managedProfileId = select ?? _repo.ViewedProfileId; OpenManagement(ManagementTab.Profiles); _management?.Rebuild(ManagementTab.Profiles); }
}
