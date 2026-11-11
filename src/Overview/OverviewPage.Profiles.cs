#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    private Button _profileChoice = null!, _profileApply = null!, _profileSave = null!;
    private TextMeshProUGUI _profileNotice = null!, _profileCaret = null!;
    private readonly HashSet<ConfigFile> _profileObserved = new();
    private bool _profileActualChanged;
    private ProfilePendingState _pendingState = new();
    private bool _pendingReadError;
    private string _managedProfileId = "";
    private int _resourceDraftRevision = -1;
    private bool ProfileHasUnsaved => _repo.IsProfileDirty || _document?.IsDirty == true || ResourceOperationDraft.Snapshot().Any(c => !ResourceOperationDraft.IsUpdate(c));
    private static string T(string en, string zh) => Loc.T(en, zh, zh);
    // 动作按钮按功能配色：绿＝生成新内容或主操作，蓝＝切换与编辑，红＝删除，默认褐＝中性。
    private const UiActionRole ActionCreate = UiActionRole.Commit, ActionEdit = UiActionRole.Edit, ActionDanger = UiActionRole.Danger;
    private static Button Tint(Button button, UiActionRole role)
    {
        UiActionStyle.Set(button, role); return button;
    }
    // Escape only the tag opener; wrapping each opener also handles a literal </noparse> in a profile name.
    private static string ProfileChoiceLiteral(string value) => value.Replace("<", "<noparse><</noparse>");
    private string ProfileChoiceCaption() => ProfileChoiceLiteral(T("Profile: ", "方案："))
        + "<size=80%>" + ProfileChoiceLiteral(_repo.ViewedProfile.name) + (_repo.IsModifiedFromAuthor ? " *" : "") + "</size>";

    private void BuildProfileTools()
    {
        _profileChoice = OverviewUi.Button(_controls, "", PickProfile, 230, 34, role: UiActionRole.Neutral);
        _profileCaret = OverviewUi.Text(_profileChoice.transform, OverviewUi.Caret, 17); _profileCaret.alignment = TextAlignmentOptions.Midline;
        _profileApply = OverviewUi.Button(_controls, Loc.T("Change review", "变更检查目录", "變更檢查目錄"), ConfirmApplyProfile, 115, 34, role: UiActionRole.Edit);
        _profileSave = OverviewUi.Button(_controls, Loc.T("Save configuration", "保存配置", "儲存設定"), () => SaveCurrentProfile(true), 115, 34, role: UiActionRole.Commit);
        _profileNotice = OverviewUi.Text(_controls, "", 17); _profileNotice.color = OverviewUi.Muted;
        _profileNotice.overflowMode = TextOverflowModes.Ellipsis;
        _profileNotice.enableAutoSizing = true; _profileNotice.fontSizeMin = 12; _profileNotice.fontSizeMax = 17;
        _profileNotice.raycastTarget = true;
        AttachToolHint(_profileNotice.gameObject, () => _profileNotice.text);
        AttachToolHint(_profileChoice.gameObject, () => T("Switching a profile only previews it. The language dropdown only switches global translations; the two never change each other.",
            "切换配置方案只是预览；语言下拉只切换全局译文，两者互不改变。") + "\n" + _repo.ViewedProfile.name);
        AttachToolHint(_profileApply.gameObject, () => _repo.ProfileSummary(_list?.Mods ?? Array.Empty<OverviewMod>()));
        AttachToolHint(_profileSave.gameObject, () => T("Save and use the edited configuration. Changes marked for restart are used next launch.",
            "保存并使用当前修改；标记需重启的内容将在下次启动生效。"));
    }

    private void ObserveProfileParameters()
    {
        StopProfileObservation();
        foreach (var entry in _repo.Configs.LoadedEntries().Values)
            if (_profileObserved.Add(entry.ConfigFile)) entry.ConfigFile.SettingChanged += ProfileActualSettingChanged;
        RefreshActualProfileStatus();
    }
    private void ProfileActualSettingChanged(object sender, SettingChangedEventArgs args) { _profileActualChanged = true; _repo.InvalidatePendingView(); }
    private void StopProfileObservation()
    {
        foreach (var file in _profileObserved) file.SettingChanged -= ProfileActualSettingChanged;
        _profileObserved.Clear();
    }
    private void RefreshActualProfileStatus()
    {
        _profileActualChanged = false;
    }
    private string _applyFailure="", _applyFailureProfile="";
    private void UpdateProfileStatus(float width, float top)
    {
        try { _pendingState = _repo.GetPendingState(_document?.IsDirty == true); _pendingReadError = false; }
        catch (Exception ex) { _pendingReadError = true; SetStatus(T("Could not compare parameters: ", "参数比较失败：") + Loc.LocalizeMessage(ex.Message)); }
        _profileNotice.text = string.Join("  ｜  ", new[]
        {
            Loc.F("Editing: {0}", "正在编辑：{0}", "正在編輯：{0}", _repo.ViewedProfile.name),
            ResourceOperationDraft.Snapshot().Count(c => !ResourceOperationDraft.IsUpdate(c)) is int pendingResources && pendingResources > 0 ? Loc.F("{0} unsaved mod operations", "{0} 项模组变更未保存", "{0} 項模組變更未儲存", pendingResources) : "",
            Loc.F("Game currently uses: {0}", "游戏当前使用：{0}", "遊戲目前使用：{0}", _repo.AppliedProfileName),
            _pendingState.UnsavedCount > 0 ? Loc.F("{0} pending save", "{0} 项待保存", "{0} 項待儲存", _pendingState.UnsavedCount) : "",
            "",
            _pendingState.RestartCount > 0 ? Loc.F("{0} pending restart", "{0} 项待重启", "{0} 項待重新啟動", _pendingState.RestartCount) : "",
            _pendingState.RoundRestartCount > 0 ? Loc.F("{0} pending next round", "{0} 项重新开局生效", "{0} 項重新開局生效", _pendingState.RoundRestartCount) : "",
            _applyFailureProfile==_repo.ViewedProfileId && _applyFailure.Length>0 ? Loc.T("Apply failed; this edited profile is not in use.","应用失败，正在编辑的方案尚未生效。","套用失敗，正在編輯的方案尚未生效。") : "",
            _pendingReadError ? Loc.T("Status unavailable", "状态暂不可用", "狀態暫不可用") : _pendingState.UnsavedCount + _pendingState.RestartCount + _pendingState.RoundRestartCount == 0 && !ResourceOperationDraft.Snapshot().Any(c => !ResourceOperationDraft.IsUpdate(c)) ? Loc.T("Saved and applied", "已保存并应用", "已儲存並套用") : "",
            Loc.T("Mod enable/disable and settings marked for restart take effect after restarting the game.", "提示：模组启停及标注需重启的设置，在重启游戏后生效。", "提示：模組啟停及標註需重新啟動的設定，在重新啟動遊戲後生效。")
        }.Where(part => part.Length > 0));
        OverviewUi.Place(_profileNotice.rectTransform, 0, top, width, 26);
        _profileApply.interactable = _repo.ViewedProfile.IsValid && _document?.IsImporting != true;
    }
    private void MarkProfileDirty()
    {
        _list?.RefreshTagFilter(); RefreshLazyViews(false); RefreshHeader();
    }
    private bool SaveCurrentProfile(bool showNotice = false, bool ignoreDependencyWarnings = true, bool resourcesApproved = false, bool overwriteExternal = false, bool skipEnablement = false, bool adviceApproved = false, Action? onSaved = null)
    {
        if (_document?.IsImporting == true) { SetStatus(T("Media import is still running; save the profile afterwards.", "素材导入尚未完成，请稍后保存方案。")); return false; }
        try
        {
            _management?.StageDrafts();
            if (!resourcesApproved && ResourceOperationDraft.Snapshot().Any(c => !ResourceOperationDraft.IsUpdate(c)))
            {
                string operations = string.Join("\n", ResourceOperationDraft.Snapshot().Where(c => !ResourceOperationDraft.IsUpdate(c)).Select(ResourceOperationDraft.Label));
                OverviewUi.ConfirmText(T("Save configuration and mod operations", "保存配置与模组变更"), operations + "\n\n" +
                    T("Saving authorizes downloading these packages and their required dependencies, backing up affected files, and executing the listed changes. Loaded components wait for game exit.",
                      "保存后允许下载以上资源及必需依赖，备份受影响文件并执行列出的变更；已加载组件等待游戏退出后处理。"),
                    () => SaveCurrentProfile(showNotice, ignoreDependencyWarnings, true, overwriteExternal, skipEnablement, adviceApproved, onSaved), confirmLabel: T("Save configuration", "保存配置"));
                return false;
            }
            if (_repo.Profiles.NeedsOriginalPresetSaveChoice(_repo.ViewedProfileId))
            {
                ShowOriginalPresetSaveChoice(() => SaveCurrentProfile(showNotice, ignoreDependencyWarnings, resourcesApproved, overwriteExternal, skipEnablement, adviceApproved, onSaved)); return false;
            }
            if (!adviceApproved)
            {
                string advice = ConfigurationIssueReport.Build(_repo, _list?.Mods ?? Array.Empty<OverviewMod>(), formatted: true);
                if (advice.Length > 0)
                {
                    OverviewUi.ShowSaveIssues(advice, _list?.Mods ?? Array.Empty<OverviewMod>(),
                        () => { SwitchTab(PageTab.Mods); _list?.ShowRelatedMods(ConfigurationIssueReport.Related(_repo, _list.Mods)); SetStatus(T("Showing mods involved in the check. Open tag filters to clear this view.", "仅显示检查涉及的模组；修改标签筛选可清除此临时视图。")); },
                        () => SaveCurrentProfile(showNotice, ignoreDependencyWarnings, resourcesApproved, overwriteExternal, skipEnablement, true, onSaved),
                        () => _list?.RetryUnidentifiedSources(), () => _list?.ManualUnidentifiedSources());
                    return false;
                }
            }
            bool introductionEdited = _document?.IsDirty == true;
            _repo.IncludeUnrecordedCurrentConfiguration();
            if (introductionEdited && !_document!.TrySave()) return false;
            var result = _repo.SaveConfiguration(_list?.Mods ?? Array.Empty<OverviewMod>(), ignoreDependencyWarnings: ignoreDependencyWarnings, overwriteExternal: overwriteExternal, skipEnablement: skipEnablement);
            SetStatus(result.Message);
            if (!result.Success)
            {
                Diagnostics.ManagerLog.Error("配置保存失败：" + result.Message.Replace("\r", " ").Split('\n')[0] + $" · 忽略依赖警告={ignoreDependencyWarnings} 跳过启停={skipEnablement} 覆盖外部={overwriteExternal}");
                _applyFailure = result.Message; _applyFailureProfile = _repo.ViewedProfileId; RefreshHeader();
                string issueReport = ConfigurationIssueReport.Build(_repo, _list?.Mods ?? Array.Empty<OverviewMod>(), result.Message);
                if (result.Message.StartsWith("MCE_EXTERNAL_CONFLICT:", StringComparison.Ordinal))
                    OverviewUi.ConfirmText(T("Configuration changed externally", "配置被外部修改"), issueReport + "\n\n" + T("Keep your edited value and save? Cancel keeps the external value and your draft for review.", "是否保留我的修改并保存？取消将保留外部值和当前草稿，供继续核对。"),
                        () => SaveCurrentProfile(showNotice, ignoreDependencyWarnings, resourcesApproved, true, skipEnablement, adviceApproved, onSaved), confirmLabel: T("Keep mine and save", "保留我的并保存"));
                else if (!ignoreDependencyWarnings && (result.Message.IndexOf("依赖", StringComparison.OrdinalIgnoreCase) >= 0 || result.Message.IndexOf("depend", StringComparison.OrdinalIgnoreCase) >= 0 || result.Message.IndexOf("requires", StringComparison.OrdinalIgnoreCase) >= 0))
                    OverviewUi.ConfirmText(T("Dependency warning", "依赖影响提醒"), issueReport + "\n\n" + T("Continue saving despite dependency warnings? Affected mods may not work. Required manager components remain protected.", "是否忽略依赖警告继续保存？受影响的模组可能无法工作，管理器必需组件仍受保护。"),
                        () => SaveCurrentProfile(true, true, resourcesApproved, overwriteExternal, skipEnablement, adviceApproved, onSaved), confirmLabel: T("Ignore and save", "忽略并保存"));
                else if (!skipEnablement && _list?.Mods.Any(m => _repo.PlannedEnabled(m) != m.TargetEnabled) == true)
                    OverviewUi.ConfirmText(T("Save remaining settings", "保存其他设置"), issueReport + "\n\n" + T("Skip these enablement changes and save the other settings? Skipped targets remain unsaved.", "是否跳过本次启停并保存其他设置？跳过的启停目标仍保留为未保存。"),
                        () => SaveCurrentProfile(true, ignoreDependencyWarnings, resourcesApproved, overwriteExternal, true, adviceApproved, onSaved), confirmLabel: T("Skip enablement and save", "跳过启停并保存"));
                else if (showNotice) OverviewUi.ShowText(T("Save configuration", "保存配置"), issueReport);
                return false;
            }
            _applyFailure = ""; _applyFailureProfile = "";
            Diagnostics.ManagerLog.Info("配置已保存：" + result.Message.Replace("\r", " ").Split('\n')[0]);
            if (_repo.IsProfileDirty)
                OverviewUi.ShowText(T("Saved available changes", "已保存有效修改"), result.Message + "\n\n" + _repo.LastSaveUncommittedDetails);
            ReloadProfileViews(); OverviewController.RefreshEntries();
            RefreshHeader();
            // Continue navigation only after all asynchronous confirmations and the save succeeded.
            if (!_repo.IsProfileDirty) onSaved?.Invoke();
            return true;
        }
        catch (Exception ex) { string message = T("Could not save the profile; the draft is kept: ", "方案保存失败，保留草稿：") + Loc.LocalizeMessage(ex.Message); SetStatus(message); if (showNotice) OverviewUi.ShowText(Loc.T("Configuration storage failed", "配置保存失败", "設定儲存失敗"), message); return false; }
    }
    private void ShowOriginalPresetSaveChoice(Action save)
    {
        OverviewUi.ShowPresetSaveChoice(_repo.ViewedProfile.name, () => {
            _repo.Profiles.ConfirmOriginalPresetSave(_repo.ViewedProfileId); save();
        }, () => OverviewUi.Ask(T("New configuration", "新配置名称"), new[] { (T("Name", "名称"), _repo.ViewedProfile.name) }, values => {
            _repo.SaveDraftAsNewProfile(values[0]); save();
        }));
    }

    private void DiscardCurrentProfile()
    {
        _repo.DiscardProfileDraft(); ReloadProfileViews();
    }
    private void RequestProfileLeave(Action leave)
    {
        if (_document?.IsImporting == true) { SetStatus(T("Media is still importing; finish it before switching or closing the profile editor.", "素材仍在导入，请完成后再切换或关闭方案编辑器。")); return; }
        if (!ProfileHasUnsaved) { leave(); return; }
        OverviewUi.ConfirmUnsaved(T("The profile has unsaved changes", "方案有未保存的修改"), () => SaveCurrentProfile(onSaved: leave),
            () => { try { DiscardCurrentProfile(); leave(); } catch (Exception ex) { SetStatus(T("Could not discard the draft: ", "无法放弃草稿：") + Loc.LocalizeMessage(ex.Message)); } },
            () => _document?.ActivateEditor());
    }

    // ---------------- 方案的标签与切换 ----------------

    // 分类只分两类：整合包配置（作者预设，以及从整合包现装自动生成的现有配置）与个人配置（Default 和玩家自己创建的）。
    private string KindLabel(OverviewProfile profile) => !profile.IsValid ? T("Unreadable", "读取失败")
        : profile.readOnly || profile.kind == OverviewProfileKind.Author ? T("Modpack configuration (read-only)", "整合包配置（只读）")
        : IsPackConfiguration(profile) ? T("Modpack configuration", "整合包配置")
        : T("Personal configuration", "个人配置");

    private bool IsPackConfiguration(OverviewProfile profile)
    {
        try { return _repo.Profiles.IsPackConfiguration(profile); } catch { return profile.kind == OverviewProfileKind.Author; }
    }

    private string ProfileLabel(OverviewProfile profile) => profile.name + " · " + KindLabel(profile);

    private void PickProfile()
    {
        RequestProfileLeave(() => {
            // 2.3.8 (user request): "Manage profiles" lives at the top of this dropdown in its own colour instead of
            // a separate toolbar button next to it.
            var entries = new List<UiMenuEntry> { new UiMenuEntry { Label = Loc.T("Manage profiles…", "管理配置方案…", "管理設定方案…"), Role = UiActionRole.Commit, Invoke = ManageProfiles } };
            entries.AddRange(_repo.Profiles.ListProfiles().Select(p => new UiMenuEntry { Label = ProfileLabel(p), Checked = p.id == _repo.ViewedProfileId, Role = UiActionRole.Edit,
                Invoke = () => SwitchViewedProfile(p.id) }));
            OverviewUi.Dropdown((RectTransform)_profileChoice.transform, T("Profile (preview only)", "配置方案（选择后仅预览）"), entries);
        });
    }

    private bool SwitchViewedProfile(string id)
    {
        try
        {
            bool reopenManagement = _management?.IsOpen == true;
            if (reopenManagement) CloseManagement();
            _repo.SelectProfile(id); ReloadProfileViews();
            SetStatus(T("Viewing ", "正在查看：") + _repo.ViewedProfile.name + T("; still using ", "；实际使用的仍是：") + _repo.AppliedProfileName +
                T(". Apply to change the live configuration.", "。点击应用才改变实际配置。"));
            if (reopenManagement) OpenManagement(ManagementTab.Profiles);
            return true;
        }
        catch (Exception ex) { SetStatus(T("Could not read the profile; the previous one is kept: ", "方案读取失败，保留原方案：") + Loc.LocalizeMessage(ex.Message)); return false; }
    }

    private void ReloadProfileViews()
    {
        _list?.Reload(); _listLoaded = true; RefreshLazyViews();
        if (_intro && _repo.ViewedProfile.kind == OverviewProfileKind.Default) SwitchTab(PageTab.Mods);
        ObserveProfileParameters(); RefreshHeader();
    }
    private void ConfirmApplyProfile()
    {
        if (_document?.IsImporting == true) { SetStatus(T("Media import is still running; apply afterwards.", "素材导入尚未完成，请稍后应用。")); return; }
        _management?.StageDrafts();
        var review = _repo.ReviewProfileChanges(_list?.Mods ?? Array.Empty<OverviewMod>(), true, _document?.IsDirty == true);
        foreach (var operation in ResourceOperationDraft.Snapshot().Where(c => !ResourceOperationDraft.IsUpdate(c))) review.Items.Add(new ProfileReviewItem {
            Category = ProfileReviewCategory.Enablement, ModId = operation.Id, ModName = ResourceOperationDraft.Label(operation),
            Title = ResourceOperationDraft.Label(operation), Before = T("Installed state", "当前安装状态"), Target = T("Submit on save", "保存后提交"),
            Note = T("Files are backed up; loaded components wait for game exit.", "文件先备份；已加载组件等待游戏退出后处理。") });
        var ignoreConflict = review.IgnoreConflict;
        review.IgnoreConflict = conflict => { ignoreConflict?.Invoke(conflict); _list?.RefreshTagFilter(); RefreshHeader(); };
        OverviewUi.ShowProfileReview(Loc.T("Change review", "变更检查目录", "變更檢查目錄"), review, () => SaveCurrentProfile(), confirmLabel: Loc.T("Save configuration", "保存配置", "儲存設定"));
    }
    private void CaptureActualProfile(Action? then = null)
    {
        OverviewUi.Confirm(T("Store the live parameters, keybindings and next-launch states into this profile? An author preset forks a personal copy.",
            "将当前实际参数、快捷键和下次启动状态保存到此方案？作者预设会建立个人副本。"), () => {
            if (_document?.IsDirty == true && !_document.TrySave()) throw new InvalidOperationException(T("The introduction is not saved yet.", "介绍尚未保存。"));
            _repo.CaptureCurrentIntoProfile(); ReloadProfileViews();
            SetStatus(T("The live configuration is now recorded in the profile; nothing live was changed.", "已将当前实际配置保存为方案记录，未改变实际配置。"));
            then?.Invoke();
        });
    }

    // ---------------- 方案管理面板 ----------------

    private void ManageProfiles() => OpenManagement(ManagementTab.Profiles);

    private void BuildProfileManager(RectTransform embeddedHost, string? select)
    {
        List<OverviewProfile> profiles;
        try { profiles = _repo.Profiles.ListProfiles().ToList(); }
        catch (Exception ex) { SetStatus(T("Could not read the profile list: ", "方案列表读取失败：") + Loc.LocalizeMessage(ex.Message)); return; }
        if (profiles.Count == 0) { SetStatus(T("No profiles found.", "没有可管理的方案。")); return; }
        string wanted = select ?? _repo.ViewedProfileId;
        _managedProfileId = profiles.Any(p => p.id == wanted) ? wanted : profiles[0].id;

        OverviewConfigSnapshot? capture = null; string captureError = "";
        try { capture = _repo.Configs.Capture(); }
        catch (Exception ex) { captureError = Loc.LocalizeMessage(ex.Message); }

        var panel = OverviewUi.Rect("MCE_ProfileManager", embeddedHost); OverviewUi.Fill(panel);
        float width = panel.rect.width, height = panel.rect.height, margin = 24;
        float listWidth = Mathf.Clamp(width * .33f, 200, 330);
        float detailX = margin + listWidth + 18, detailWidth = Mathf.Max(200, width - detailX - margin);
        float bodyTop = 92, bodyHeight = Mathf.Max(170, height - 78 - bodyTop);

        var title = OverviewUi.Text(panel, T("Profile manager", "配置方案管理"), 27);
        OverviewUi.Place(title.rectTransform, margin, 16, Mathf.Max(180, width * .42f), 38);
        var headline = OverviewUi.Text(panel, T("Applied profile: ", "已应用方案：") + _repo.AppliedProfileName + "　" + T("Viewing ", "查看 ") + _repo.ViewedProfile.name +
            (ProfileHasUnsaved ? T(" (unsaved)", "（未保存）") : ""), 17);
        headline.color = OverviewUi.Muted; headline.alignment = TextAlignmentOptions.MidlineRight;
        OverviewUi.PlaceClipped(headline, width * .42f, 24, width * .58f - margin, 26);
        var listCaption = OverviewUi.Text(panel, T("Profiles", "方案") + " (" + profiles.Count + ")", 19);
        OverviewUi.Place(listCaption.rectTransform, margin, 62, listWidth, 26);

        // 左栏：方案列表。单击只选中看详情，双击才真的切换查看。
        var host = OverviewUi.Panel("Profiles", panel, ModConfigLook.Surface(new Color(.09f, .09f, .08f, .85f)));
        OverviewUi.Place(host, margin, bodyTop, listWidth, bodyHeight);
        var scroll = host.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport, 4); viewport.gameObject.AddComponent<RectMask2D>();
        var content = OverviewUi.Rect("Rows", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1);
        string ProfileParent(OverviewProfile profile) => PackLibrary.Active?.Name ?? (profile.state.pack.name.Length > 0 ? profile.state.pack.name : T("Personal environment", "个人环境"));
        profiles = profiles.OrderBy(ProfileParent).ThenBy(p => p.readOnly ? 0 : 1).ThenBy(p => p.name).ToList();
        content.sizeDelta = new Vector2(0, profiles.Count * 54 + profiles.Select(ProfileParent).Distinct().Count() * 38);
        scroll.viewport = viewport; scroll.content = content;

        string appliedId = _repo.Profiles.AppliedId;
        var rowImages = new List<(string Id, Image Background)>();

        // 右栏：选中方案的详情
        var card = OverviewUi.Panel("Detail", panel, ModConfigLook.Surface(new Color(.09f, .09f, .08f, .85f)));
        float cardHeight = Mathf.Clamp(bodyHeight - 104, 120, 252);
        OverviewUi.Place(card, detailX, bodyTop, detailWidth, cardHeight);
        card.gameObject.AddComponent<RectMask2D>();
        float inner = detailWidth - 28;
        var detailName = OverviewUi.Text(card, "", 22); detailName.alignment = TextAlignmentOptions.MidlineLeft;
        OverviewUi.PlaceClipped(detailName, 14, 8, inner, 30);
        var detailMeta = OverviewUi.Text(card, "", 16); detailMeta.color = OverviewUi.Muted; detailMeta.alignment = TextAlignmentOptions.MidlineLeft;
        OverviewUi.PlaceClipped(detailMeta, 14, 40, inner, 22);
        var detailCounts = OverviewUi.Text(card, "", 16); detailCounts.color = OverviewUi.Muted; detailCounts.alignment = TextAlignmentOptions.MidlineLeft;
        OverviewUi.PlaceClipped(detailCounts, 14, 62, inner, 22);
        Rule(card, 14, 88, inner);
        string[] statLabels = {
            T("Enable/disable target changes", "启停目标变化"), T("Parameter changes", "参数变化"),
            T("New, not recorded (kept as is)", "未记录的新项（保持现值）"), T("Records not found (kept)", "失联记录（保留）")
        };
        var statValues = new TextMeshProUGUI[statLabels.Length];
        for (int i = 0; i < statLabels.Length; i++)
        {
            var label = OverviewUi.Text(card, statLabels[i], 17); label.color = OverviewUi.Muted;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            OverviewUi.PlaceClipped(label, 14, 94 + i * 25, Mathf.Max(60, inner - 74), 24);
            var value = OverviewUi.Text(card, "", 19); value.alignment = TextAlignmentOptions.MidlineRight;
            OverviewUi.Place(value.rectTransform, 14 + Mathf.Max(60, inner - 74), 94 + i * 25, 70, 24);
            statValues[i] = value;
        }
        Rule(card, 14, 198, inner);
        var detailNote = OverviewUi.Text(card, T("Saving commits configuration and shared modpack information. Plugin enable/disable takes effect next launch; each setting shows its declared effect timing.",
            "保存即提交配置与共用整合包资料；插件启停下次启动生效，参数按各项声明的生效时机使用。"), 15);
        detailNote.color = OverviewUi.Muted; detailNote.textWrappingMode = TextWrappingModes.Normal;
        OverviewUi.Place(detailNote.rectTransform, 14, 206, inner, Mathf.Max(20, cardHeight - 212));

        // 右栏：动作按钮贴着列表底边，按文字宽度排布，放不下自动换行
        float actionLeft = detailX + 56, actionX = actionLeft, limit = width - margin;
        float actionY = Mathf.Max(bodyTop + cardHeight + 12, bodyTop + bodyHeight - 80);
        var placed = new List<RectTransform>();
        Button Action(string caption, Action act)
        {
            var button = OverviewUi.Button(panel, caption, act, 96, 34, role: UiActionRole.Neutral);
            var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            float size = text != null ? text.GetPreferredValues(caption).x + 26 : 96;
            float buttonWidth = Mathf.Clamp(size, 76, Mathf.Max(90, limit - actionLeft));
            if (actionX > actionLeft && actionX + buttonWidth > limit) { actionX = actionLeft; actionY += 40; }
            var rect = (RectTransform)button.transform;
            OverviewUi.Place(rect, actionX, actionY, buttonWidth, 34); placed.Add(rect);
            actionX += buttonWidth + 10; return button;
        }
        void Group(string caption)
        {
            var label = OverviewUi.Text(panel, caption, 17); label.color = OverviewUi.Muted;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            // 2.3.9: sized to the text (English "Content" did not fit the old fixed 52 px); actions start after it.
            float labelWidth = Mathf.Max(52, label.GetPreferredValues(caption).x + 6);
            OverviewUi.Place(label.rectTransform, detailX, actionY, labelWidth, 34); placed.Add(label.rectTransform);
            actionX = Mathf.Max(actionX, detailX + labelWidth + 4);
        }
        Group(T("Profile", "方案"));
        var createButton = Tint(Action(T("New", "新建"), () => AskProfileName(T("New personal profile (records the live configuration)", "新建个人方案（记录当前实际配置）"),
            name => _repo.CreateProfile(_managedProfileId, name, true))), ActionCreate);
        var copyButton = Tint(Action(T("Duplicate", "复制"), () => AskProfileName(T("Duplicate profile", "复制方案"),
            name => _repo.CreateProfile(_managedProfileId, name, false))), ActionCreate);
        var renameButton = Tint(Action(T("Rename", "改名"), RenameSelectedProfile), ActionEdit);
        var deleteButton = Tint(Action(T("Delete", "删除"), ConfirmDeleteProfile), ActionDanger);
        actionX = actionLeft; actionY += 40;
        Group(T("Content", "内容"));
        var parametersButton = Tint(Action(T("Edit parameters", "编辑参数"), () => OverviewConfigView.Show(_repo, _list?.Mods ?? Array.Empty<OverviewMod>(), MarkProfileDirty, SetStatus, RequestProfileLeave, () => _pendingState)), ActionEdit);
        var captureButton = Tint(Action(T("Store the live configuration", "保存当前实际配置"), () => CaptureActualProfile(() => ManageProfiles(_repo.ViewedProfileId))), ActionCreate);
        var exportButton = Tint(Action(T("Export", "导出"), ExportProfileSelection), ActionCreate);
        // 窄窗口或长译名把动作挤成三行时整块上移，宁可压住详情卡，也不要压住底部按钮。
        float overflow = actionY + 34 - (bodyTop + bodyHeight);
        if (overflow > 0) foreach (var rect in placed) rect.anchoredPosition += new Vector2(0, overflow);

        // 底部：左关闭，右切换与应用
        var close = OverviewUi.Button(panel, T("Close", "关闭"), () => _management?.TryClose(), 110, 34, role: UiActionRole.Neutral);
        OverviewUi.Bottom((RectTransform)close.transform, margin, 18);
        var applyButton = Tint(OverviewUi.Button(panel, Loc.T("Save configuration", "保存配置", "儲存設定"), ApplySelectedProfile, 150, 34, role: UiActionRole.Commit), ActionCreate);
        OverviewUi.Bottom((RectTransform)applyButton.transform, width - margin - 150, 18);
        var switchButton = Tint(OverviewUi.Button(panel, T("Switch to this profile", "切换到此方案"), SwitchToSelectedProfile, 150, 34, role: UiActionRole.Edit), ActionEdit);
        OverviewUi.Bottom((RectTransform)switchButton.transform, width - margin - 310, 18);
        var notice = OverviewUi.Text(panel, "", 16); notice.color = OverviewUi.Muted;
        notice.alignment = TextAlignmentOptions.MidlineLeft; notice.textWrappingMode = TextWrappingModes.Normal;
        var noticeRect = notice.rectTransform; OverviewUi.Bottom(noticeRect, margin + 124, 16);
        noticeRect.sizeDelta = new Vector2(Mathf.Max(80, width - margin - 310 - margin - 134), 38);

        // 行放在最后建：行上的点击要调用下面的 RefreshManager，捕获的控件必须先就位。
        float profileY = 0; string? parentName = null;
        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i]; string parent = ProfileParent(profile);
            if(parentName != parent) { parentName=parent; var packLabel=OverviewUi.Text(content,"- " + parent,19); packLabel.color=OverviewUi.Blue; OverviewUi.PlaceClipped(packLabel,8,profileY,listWidth-24,32); profileY+=38; }
            var row = OverviewUi.Panel("Profile_" + profile.id, content, Color.clear);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0, 1);
            row.anchoredPosition = new Vector2(18, -profileY); row.sizeDelta = new Vector2(-26, 50); profileY += 54;
            var background = row.GetComponent<Image>(); rowImages.Add((profile.id, background));
            Color rail = !profile.IsValid ? new Color(.84f, .45f, .38f)
                : profile.id == appliedId ? OverviewUi.Green
                : profile.kind == OverviewProfileKind.Author ? OverviewUi.Gold
                : profile.kind == OverviewProfileKind.Default ? ModConfigLook.Surface(new Color(.55f, .55f, .52f)) : OverviewUi.Blue;
            var marker = OverviewUi.Rail("Rail", row, rail); OverviewUi.Place(marker, 0, 0, 3, 50);
            marker.GetComponent<Image>().raycastTarget = false;
            string badge = (profile.id == _repo.ViewedProfileId ? OverviewUi.Pointer : "") + (profile.id == appliedId ? "✓" : "");
            var name = OverviewUi.Text(row, profile.name, 20); name.alignment = TextAlignmentOptions.MidlineLeft;
            if (!profile.IsValid) name.color = new Color(.95f, .57f, .43f);
            OverviewUi.PlaceClipped(name, 12, 3, Mathf.Max(60, listWidth - 46 - (badge.Length > 0 ? 44 : 0)), 26);
            var kind = OverviewUi.Text(row, KindLabel(profile), 15);
            kind.color = OverviewUi.Muted; kind.alignment = TextAlignmentOptions.MidlineLeft;
            OverviewUi.PlaceClipped(kind, 12, 27, Mathf.Max(60, listWidth - 40), 20);
            if (badge.Length > 0)
            {
                var flag = OverviewUi.Text(row, badge, 18); flag.color = profile.id == appliedId ? OverviewUi.Green : OverviewUi.Blue;
                flag.alignment = TextAlignmentOptions.MidlineRight;
                // 2.4.1：行宽是 listWidth-26（左缩 18、右缩 8），原先按 listWidth-46 放，右端压到行外；改成行内右侧 10、垂直居中
                flag.rectTransform.anchorMin = flag.rectTransform.anchorMax = flag.rectTransform.pivot = new Vector2(1, .5f);
                flag.rectTransform.anchoredPosition = new Vector2(-10, 0); flag.rectTransform.sizeDelta = new Vector2(40, 30);
            }
            string id = profile.id;
            var pick = row.gameObject.AddComponent<Button>(); pick.transition = Selectable.Transition.None; pick.targetGraphic = background;
            pick.onClick.AddListener(() => { _managedProfileId = id; RefreshManager(); });
            row.gameObject.AddComponent<OverviewTextClick>().Clicked = data => {
                if (data.clickCount < 2 || id == _repo.ViewedProfileId) return;
                RequestManagementProfileLeave(() => { if (SwitchViewedProfile(id)) ManageProfiles(id); });
            };
            GamepadSupport.MarkSelectable(row.gameObject, scroll, row);
        }

        RefreshManager();

        void RefreshManager()
        {
            var profile = profiles.FirstOrDefault(p => p.id == _managedProfileId) ?? profiles[0];
            _managedProfileId = profile.id;
            bool viewed = profile.id == _repo.ViewedProfileId, applied = profile.id == appliedId;
            foreach (var (id, background) in rowImages)
                background.color = id == _managedProfileId ? ModConfigLook.Surface(new Color(.18f, .34f, .43f, .78f)) : ModConfigLook.Surface(new Color(.14f, .14f, .13f, .35f));
            detailName.text = profile.name;
            var facts = new List<string> { KindLabel(profile) };
            var origin = profile.source.Length > 0 ? profiles.FirstOrDefault(p => p.id == profile.source) : null;
            if (origin != null) facts.Add(Loc.F("from {0}", "源自「{0}」", "源自「{0}」", origin.name));
            if (profile.modifiedUtc.Year > 2000) facts.Add(T("modified ", "修改 ") + profile.modifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            detailMeta.text = string.Join(" · ", facts);

            var config = viewed ? _repo.DraftConfig : profile.config;
            IReadOnlyList<OverviewProfileMod> records = viewed ? _repo.DraftMods : profile.mods;
            detailCounts.text = T("Records: ", "记录：") + records.Count + T(" mods / ", " 个模组 / ") + config.values.Count + T(" settings", " 项参数") +
                (viewed && ProfileHasUnsaved ? T(" · unsaved draft included", " · 含未保存草稿") : "");

            int targets = 0;
            foreach (var mod in _list?.Mods ?? Array.Empty<OverviewMod>())
            {
                var record = records.FirstOrDefault(m => m.guid == mod.Guid);
                if (record != null && record.enabled != mod.TargetEnabled) targets++;
            }
            statValues[0].text = targets.ToString();
            string problem = captureError;
            if (capture != null && profile.IsValid)
            {
                try
                {
                    var diff = _repo.Configs.Diff(config, capture);
                    statValues[1].text = diff.Count(d => d.Kind == OverviewConfigDiffKind.Changed).ToString();
                    statValues[2].text = diff.Count(d => d.Kind == OverviewConfigDiffKind.CurrentOnlyNotRecorded).ToString();
                    statValues[3].text = diff.Count(d => d.Kind == OverviewConfigDiffKind.ProfileOnlyMissing).ToString();
                }
                catch (Exception ex) { problem = Loc.LocalizeMessage(ex.Message); for (int i = 1; i < statValues.Length; i++) statValues[i].text = "—"; }
            }
            else for (int i = 1; i < statValues.Length; i++) statValues[i].text = "—";

            bool editable = profile.IsValid && !profile.readOnly && profile.kind != OverviewProfileKind.Default;
            createButton.interactable = profile.IsValid;
            copyButton.interactable = profile.IsValid;
            renameButton.interactable = editable;
            deleteButton.interactable = editable && !applied;
            parametersButton.interactable = viewed && profile.IsValid;
            captureButton.interactable = viewed && profile.IsValid;
            exportButton.interactable = profile.IsValid;
            switchButton.interactable = !viewed && profile.IsValid;
            applyButton.interactable = profile.IsValid && _document?.IsImporting != true;

            notice.text =
                problem.Length > 0 ? T("Could not compare the live configuration: ", "实际配置比较失败：") + problem
                : !profile.IsValid ? T("This profile could not be read: ", "此方案读取失败：") + profile.loadError
                : !viewed ? T("Parameters and the live snapshot apply to the profile being viewed. Switch to it first.", "编辑参数、保存实际配置只作用于正在查看的方案，请先切换。")
                : applied ? T("This profile is the applied one.", "此方案就是当前已应用的方案。")
                : T("Switching only previews. Save configuration commits the settings and next-launch plugin states.", "切换只是预览；保存配置才会提交参数与下次启动的插件启停。");
        }

        void AskProfileName(string caption, Action<string> create)
        {
            var source = profiles.FirstOrDefault(p => p.id == _managedProfileId) ?? profiles[0];
            RequestManagementProfileLeave(() => OverviewUi.AskValidated(caption, new[] { (T("Name", "名称"), source.name + T(" - copy", " - 副本")) },
                values => string.IsNullOrWhiteSpace(values[0]) ? T("The name cannot be empty.", "名称不能为空。") : null,
                values => { create(values[0]); ReloadProfileViews(); ManageProfiles(_repo.ViewedProfileId); }));
        }

        void RenameSelectedProfile()
        {
            var target = profiles.FirstOrDefault(p => p.id == _managedProfileId) ?? profiles[0];
            OverviewUi.Ask(T("Rename profile", "方案改名"), new[] { (T("Name", "名称"), target.name) }, values => {
                _repo.RenameProfile(target.id, values[0]); RefreshHeader(); ManageProfiles(target.id);
            });
        }

        void ConfirmDeleteProfile()
        {
            var target = profiles.FirstOrDefault(p => p.id == _managedProfileId) ?? profiles[0];
            RequestManagementProfileLeave(() => OverviewUi.ConfirmText(T("Delete this personal profile?", "删除个人方案？"),
                Loc.F("Delete \"{0}\"?\nIts files move to the backup folder. Author originals and Default are never removed.",
                  "确认删除“{0}”？\n文件移入修改备份，作者原版和 Default 保留。", "確認刪除「{0}」？\n檔案移入修改備份，作者原版和 Default 保留。", target.name), () => {
                string backup = _repo.DeleteProfile(target.id); ReloadProfileViews();
                SetStatus(T("The personal profile was moved to: ", "个人方案已移入备份：") + backup);
                ManageProfiles(_repo.ViewedProfileId);
            }, UiActionRole.Danger));
        }

        void SwitchToSelectedProfile()
        {
            string id = _managedProfileId;
            if (id == _repo.ViewedProfileId) return;
            RequestManagementProfileLeave(() => { if (SwitchViewedProfile(id)) ManageProfiles(id); });
        }

        void ApplySelectedProfile()
        {
            string id = _managedProfileId;
            if (id == _repo.ViewedProfileId) { ConfirmApplyProfile(); return; }
            RequestManagementProfileLeave(() => { if (SwitchViewedProfile(id)) ConfirmApplyProfile(); });
        }

        void ExportProfileSelection() => RequestManagementProfileLeave(() => ShowExportPanel(_managedProfileId));
    }

    private static void Rule(Transform parent, float x, float y, float width)
    {
        var line = OverviewUi.Panel("Rule", parent, ModConfigLook.Surface(new Color(.77f, .76f, .66f, .4f)));
        line.GetComponent<Image>().raycastTarget = false; OverviewUi.Place(line, x, y, width, 1.5f);
    }

    private void ShowExportPanel(string preselected)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { preselected };
        var profiles = _repo.Profiles.ListProfiles().Where(p => p.IsValid).ToList();
        var panel = OverviewUi.CreateModalPanel("MCE_ProfileExport", 740, 620); if (panel == null) return;
        float width = panel.rect.width;
        var heading = OverviewUi.Text(panel, T("Choose the profiles to export", "选择要导出的方案"), 25);
        OverviewUi.Place(heading.rectTransform, 24, 16, width - 48, 36);
        var hint = OverviewUi.Text(panel, T("Exports saved parameters, hotkeys and enabled-mod targets only. Use Publish for a complete modpack.",
            "仅导出已保存的参数、快捷键和模组启停目标。完整模组与介绍资源请从发布导出整合包。"), 16);
        hint.color = OverviewUi.Muted; hint.overflowMode = TextOverflowModes.Ellipsis;
        OverviewUi.Place(hint.rectTransform, 24, 50, width - 48, 24);
        var host = OverviewUi.Panel("Choices", panel, Color.clear); OverviewUi.Fill(host);
        host.offsetMin = new Vector2(24, 70); host.offsetMax = new Vector2(-24, -80); host.gameObject.AddComponent<RectMask2D>();
        var content = OverviewUi.Rect("Rows", host);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1);
        content.sizeDelta = new Vector2(0, profiles.Count * 45);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.viewport = host; scroll.content = content;
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var check = OverviewUi.Check(content, selected.Contains(profile.id), yes => { if (yes) selected.Add(profile.id); else selected.Remove(profile.id); });
            OverviewUi.Place((RectTransform)check.transform, 0, i * 45, 28, 28);
            GamepadSupport.MarkSelectable(check.gameObject, scroll, (RectTransform)check.transform);
            var label = OverviewUi.Text(content, ProfileLabel(profile), 20); label.alignment = TextAlignmentOptions.MidlineLeft;
            OverviewUi.PlaceClipped(label, 40, i * 45, width - 98, 32);
        }
        var export = Tint(OverviewUi.Button(panel, T("Export", "导出"), () => {
            if (selected.Count == 0) throw new InvalidOperationException(T("Select at least one profile.", "请至少选择一个方案。"));
            string directory = _repo.ExportProfiles(selected);
            OverviewUi.CloseModal(); ManagementStatus(T("Configurations exported: ", "配置已导出：") + directory);
            ManagementUi.OpenFolder(directory);
        }, 120, 34, role: UiActionRole.Commit), ActionCreate);
        OverviewUi.Bottom((RectTransform)export.transform, 24, 18);
        var cancel = OverviewUi.Button(panel, T("Cancel", "取消"), () => ManageProfiles(preselected), 120, 34, role: UiActionRole.Neutral);
        OverviewUi.Bottom((RectTransform)cancel.transform, 156, 18);
    }
}
