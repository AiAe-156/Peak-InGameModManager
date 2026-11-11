#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ModConfigEnhance.Overview;

internal enum ProfileReviewCategory { Conflicts, Enablement, Parameters, Hotkeys, Groups, Information, Introduction, Unavailable }
internal sealed class ProfileReviewItem
{
    internal ProfileReviewCategory Category;
    internal ConfigurationEffectTiming EffectTiming;
    internal string GroupId = "", GroupName = "", ModId = "", ModName = "", Title = "", Before = "", Target = "", Note = "";
    internal Color GroupColor = OverviewUi.Blue;
    internal int GroupOrder = int.MaxValue, ModOrder;
    internal PackConflictMatch? Conflict;
}
internal sealed class ProfileChangeReview
{
    internal string ProfileName = "", AppliedName = "";
    internal bool SaveOnly;
    internal readonly List<ProfileReviewItem> Items = new();
    internal Action<PackConflictMatch>? IgnoreConflict;
}

internal sealed partial class OverviewRepository
{
    internal ProfileChangeReview ReviewProfileChanges(IReadOnlyList<OverviewMod> mods, bool saveOnly, bool introductionEdited = false)
    {
        var pending = GetPendingState(introductionEdited);
        var result = new ProfileChangeReview { ProfileName = ViewedProfile.name, AppliedName = AppliedProfileName, SaveOnly = saveOnly };
        {
            result.IgnoreConflict = match => PackConflictService.Ignore(this, match);
            foreach (var conflict in PackConflictService.Evaluate(this, mods).Where(c => c.Planned && !c.Ignored))
                result.Items.Add(new ProfileReviewItem { Category = ProfileReviewCategory.Conflicts, GroupId = conflict.PackId, GroupName = conflict.PackName,
                    GroupColor = PackConflictUi.AdviceColor, GroupOrder = -1, ModId = conflict.Rule.id, ModName = conflict.Title, Conflict = conflict });
        }
        var previous = saveOnly ? _savedDraftProfile : _appliedSnapshot ?? Profiles.Applied;
        var byGuid = mods.GroupBy(m => m.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        string ModName(string guid) => byGuid.TryGetValue(guid, out var m) ? OverviewLanguageBridge.GetDisplayName(m) : _draftMods.FirstOrDefault(m => m.guid.Equals(guid, StringComparison.OrdinalIgnoreCase))?.name ?? guid;
        var currentTree = new GroupTree(State.groups); var previousTree = new GroupTree(previous.state.groups);
        string GroupName(string id) => currentTree.Contains(id) ? currentTree.Path(id) : previousTree.Contains(id) ? previousTree.Path(id) : Loc.T("Ungrouped", "未分组", "未分組");
        void Add(ProfileReviewCategory category, string guid, string title, string before, string target, string note = "", string? groupId = null)
        {
            string group = groupId ?? (byGuid.TryGetValue(guid, out var mod) ? mod.Rule.groupId : State.mods.FirstOrDefault(m => m.id.Equals(guid, StringComparison.OrdinalIgnoreCase))?.groupId ?? "");
            var definition = State.groups.FirstOrDefault(g => g.id == group);
            var tint = ColorUtility.TryParseHtmlString(definition?.color ?? "", out var color) ? color : OverviewUi.Blue;
            result.Items.Add(new ProfileReviewItem { Category = category, GroupId = group, GroupName = guid.Length == 0 && group.Length == 0 ? Loc.T("Profile", "配置方案", "設定方案") : GroupName(group), GroupColor = tint,
                GroupOrder = currentTree.Position(group), ModOrder = byGuid.TryGetValue(guid, out var ordered) ? ordered.Rule.order : 0,
                ModId = guid, ModName = guid.Length == 0 ? "" : ModName(guid), Title = title, Before = before, Target = target, Note = note });
        }
        string OnOff(bool value) => value ? Loc.T("Enabled", "启用", "啟用") : Loc.T("Disabled", "停用", "停用");
        string NotRecorded() => Loc.T("Not recorded", "未记录", "未記錄");
        var savedValues = previous.config.values.GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var draftValues = _draftConfig.values.GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var live = Configs.KnownLoadedEntries();
        foreach (string id in (saveOnly ? savedValues.Keys.Union(draftValues.Keys) : draftValues.Keys).OrderBy(v => v, StringComparer.Ordinal))
        {
            savedValues.TryGetValue(id, out var old); draftValues.TryGetValue(id, out var wanted);
            var value = wanted ?? old!; var state = pending.Parameters.TryGetValue(id, out var item) ? item : null;
            bool unavailable = wanted != null && state?.UnavailableReason.Length > 0;
            if (!(saveOnly ? state?.NeedsSave == true : state?.NeedsApply == true || state?.NeedsRestart == true)) continue;
            bool key = value.type.IndexOf("KeyCode", StringComparison.OrdinalIgnoreCase) >= 0 || value.type.IndexOf("KeyboardShortcut", StringComparison.OrdinalIgnoreCase) >= 0;
            string before = saveOnly ? old?.serializedValue ?? NotRecorded() : state?.NeedsRestart == true ? state.RestartPrevious : state?.CurrentAvailable == true ? state.Current : Loc.T("Not loaded", "未加载", "未載入");
            string target = wanted?.serializedValue ?? NotRecorded();
            string note;
            if (unavailable) note = Loc.LocalizeMessage(state!.UnavailableReason);
            else note = wanted == null ? Loc.T("Remove the recorded target; keep the live value.", "移除方案记录，实际值保持原样。", "移除方案記錄，實際值維持原樣。") :
                OverviewRuntimeSettingsBridge.TimingLabel(live.TryGetValue(id, out var timingEntry) ? OverviewRuntimeSettingsBridge.Timing(timingEntry) : ConfigurationEffectTiming.WhenEnabled);
            string title = value.section + " / " + Loc.T(value.key, value.key, value.key);
            Add(unavailable ? ProfileReviewCategory.Unavailable : key ? ProfileReviewCategory.Hotkeys : ProfileReviewCategory.Parameters, value.pluginGuid, title, before, target, note);
            result.Items[result.Items.Count - 1].EffectTiming = live.TryGetValue(id, out var effectEntry) ? OverviewRuntimeSettingsBridge.Timing(effectEntry) : ConfigurationEffectTiming.WhenEnabled;
        }
        var savedMods = previous.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var target in _draftMods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
        {
            savedMods.TryGetValue(target.guid, out var old); byGuid.TryGetValue(target.guid, out var mod);
            bool loaded = Chainloader.PluginInfos.TryGetValue(target.guid, out var info) && info.Instance != null;
            if (mod == null && !target.enabled) continue;
            bool changed = saveOnly ? old == null || old.enabled != target.enabled : mod != null && mod.TargetEnabled != target.enabled;
            bool restart = !saveOnly && pending.Mod(target.guid)?.NeedsRestart == true;
            if (!changed && !restart && mod != null) continue;
            string before = saveOnly ? old == null ? NotRecorded() : OnOff(old.enabled) : OnOff(restart ? loaded : mod?.TargetEnabled ?? false);
            string note = mod == null ? Loc.T("The mod is not installed; its target is retained.", "未找到已安装模组，保留目标。", "找不到已安裝模組，保留目標。") : saveOnly ? Loc.T("Saved enablement takes effect next launch.", "保存后于下次启动采用该启停状态。", "儲存後於下次啟動採用此啟停狀態。") : Loc.T("Takes effect after restarting PEAK.", "重新启动 PEAK 后生效。", "重新啟動 PEAK 後生效。");
            Add(mod == null ? ProfileReviewCategory.Unavailable : ProfileReviewCategory.Enablement, target.guid, Loc.T("Enable mod", "启用", "啟用"), before, OnOff(target.enabled), note);
            result.Items[result.Items.Count - 1].EffectTiming = mod == null ? ConfigurationEffectTiming.WhenEnabled : ConfigurationEffectTiming.GameRestart;
        }
        void Fields(ProfileReviewCategory category, string guid, object before, object target, (string Key, string Label)[] fields, string? groupId = null)
        {
            var a = JObject.FromObject(before); var b = JObject.FromObject(target);
            string Display(JToken? token) => token == null ? NotRecorded() : token.Type == JTokenType.Boolean ? OnOff(token.Value<bool>()) : token.ToString();
            foreach (var field in fields)
                if (!JToken.DeepEquals(a[field.Key], b[field.Key])) Add(category, guid, field.Label, Display(a[field.Key]), Display(b[field.Key]), "", groupId);
        }
        var groupFields = new[] { ("name", Loc.T("Group name", "分组名称", "分組名稱")), ("color", Loc.T("Group color", "分组颜色", "分組顏色")), ("description", Loc.T("Group description", "分组说明", "分組說明")), ("order", Loc.T("Order", "顺序", "順序")), ("parentId", Loc.T("Parent group", "上级分组", "上級分組")) };
        foreach (string id in previous.state.groups.Select(g => g.id).Union(State.groups.Select(g => g.id)))
        {
            var old = previous.state.groups.FirstOrDefault(g => g.id == id); var target = State.groups.FirstOrDefault(g => g.id == id);
            if (old == null || target == null) Add(ProfileReviewCategory.Groups, "", Loc.T("Group", "分组", "分組"), old?.name ?? NotRecorded(), target?.name ?? NotRecorded(), "", id);
            else
            {
                Fields(ProfileReviewCategory.Groups, "", old, target, groupFields.Where(f => f.Item1 != "parentId").ToArray(), id);
                if ((old.parentId ?? "") != (target.parentId ?? ""))
                    Add(ProfileReviewCategory.Groups, "", Loc.T("Parent group", "上级分组", "上級分組"),
                        (old.parentId ?? "").Length == 0 ? Loc.T("Top level", "顶层", "頂層") : GroupName(old.parentId!),
                        (target.parentId ?? "").Length == 0 ? Loc.T("Top level", "顶层", "頂層") : GroupName(target.parentId!), "", id);
            }
        }
        foreach (string id in previous.state.mods.Select(m => m.id).Union(State.mods.Select(m => m.id), StringComparer.OrdinalIgnoreCase))
        {
            var old = previous.state.mods.FirstOrDefault(m => m.id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? new OverviewModRule();
            var target = State.mods.FirstOrDefault(m => m.id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? new OverviewModRule();
            if (old.groupId != target.groupId) Add(ProfileReviewCategory.Groups, id, Loc.T("Assigned group", "所属分组", "所屬分組"), GroupName(old.groupId), GroupName(target.groupId));
            if (old.order != target.order) Add(ProfileReviewCategory.Groups, id, Loc.T("Order", "顺序", "順序"), old.order.ToString(), target.order.ToString());
            Fields(ProfileReviewCategory.Information, id, old, target, new[] { ("versionLabel", Loc.T("Display version", "显示版本", "顯示版本")), ("alias", Loc.T("Display name", "显示名称", "顯示名稱")), ("description", Loc.T("Description", "简介", "簡介")), ("updateAllowed", Loc.T("Update permission", "更新许可", "更新許可")) });
        }
        Fields(ProfileReviewCategory.Information, "", previous.state.pack, State.pack, new[] {
            ("name", Loc.T("Pack name", "整合包名称", "整合包名稱")), ("version", Loc.T("Pack version", "整合包版本", "整合包版本")), ("author", Loc.T("Author", "作者", "作者")),
            ("contact", Loc.T("Contact", "联系方式", "聯絡方式")), ("date", Loc.T("Date", "日期", "日期")), ("introTitle", Loc.T("Introduction title", "介绍标题", "介紹標題")), ("introEnabled", Loc.T("Show introduction", "显示介绍", "顯示介紹")) });
        if (introductionEdited || !saveOnly && IntroductionHash(PackDirectory, State.pack) != _appliedIntroductionHash)
            Add(ProfileReviewCategory.Introduction, "", Loc.T("Introduction and resources", "介绍与引用资源", "介紹與引用資源"), Loc.T("Previous content", "原有内容", "原有內容"), Loc.T("Edited content", "已编辑内容", "已編輯內容"));
        return result;
    }
}
