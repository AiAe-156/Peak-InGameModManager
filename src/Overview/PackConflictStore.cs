#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>Author drafts are pack-wide and separate from every profile. Installed rules remain read-only.</summary>
internal static class PackConflictStore
{
    internal const int FileLimitBytes = 512 * 1024, RuleLimit = 256, MemberLimit = 32;
    internal const string FileName = "conflicts.json";
    private static readonly JsonSerializerSettings Settings = new() { MaxDepth = 16, TypeNameHandling = TypeNameHandling.None };

    internal static PackConflictFile LoadAuthorDraft(string packId, string? configRoot = null)
    {
        string path = AuthorDraftPath(packId, configRoot);
        if (File.Exists(path)) return Read(path);
        var installed = PublishedPack.Packs.FirstOrDefault(p => p.Valid && p.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));
        return installed == null ? new() : ReadPublished(installed);
    }

    internal static void SaveAuthorDraft(string packId, PackConflictFile file, IReadOnlyList<OverviewMod>? mods = null, string? configRoot = null)
    {
        var errors = Validate(file, mods);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        string text = Serialize(file);
        if (Encoding.UTF8.GetByteCount(text) > FileLimitBytes) throw new InvalidDataException(Loc.T("Conflict presets exceed the 512 KB limit.", "冲突预设超过 512 KB 上限。", "衝突預設超過 512 KB 上限。"));
        OverviewRepository.AtomicWrite(AuthorDraftPath(packId, configRoot), text);
    }

    internal static string AuthorDraftPath(string packId, string? configRoot = null)
    {
        if (string.IsNullOrWhiteSpace(packId) || packId.Length > 256) throw new InvalidDataException(Loc.T("The pack needs a stable identity before editing conflict presets.", "编辑冲突预设前需要稳定的整合包标识。", "編輯衝突預設前需要穩定的整合包識別碼。"));
        return Path.Combine(Updating.MceDirectories.Runtime(configRoot ?? Loc.RootDirectory), "pack-conflicts", Digest(packId.Trim().ToLowerInvariant()) + ".json");
    }

    internal static PackConflictFile ReadPublished(PublishedPack pack)
    {
        if (!pack.Valid) throw new InvalidDataException(pack.Error);
        string path = Path.Combine(pack.Root, FileName);
        return File.Exists(path) ? Read(path) : new();
    }

    internal static PackConflictFile Read(string path)
    {
        if (new FileInfo(path).Length > FileLimitBytes) throw new InvalidDataException(Loc.T("Conflict presets exceed the 512 KB limit.", "冲突预设超过 512 KB 上限。", "衝突預設超過 512 KB 上限。"));
        PackConflictFile file;
        try { file = JsonConvert.DeserializeObject<PackConflictFile>(File.ReadAllText(path, Encoding.UTF8), Settings) ?? throw new InvalidDataException("Empty conflict preset file."); }
        catch (JsonException e) { throw new InvalidDataException(Loc.T("Invalid conflict preset JSON: ", "冲突预设 JSON 无效：", "衝突預設 JSON 無效：") + e.Message, e); }
        var errors = Validate(file);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        return file;
    }

    internal static PackConflictFile Clone(PackConflictFile file) => JsonConvert.DeserializeObject<PackConflictFile>(Serialize(file), Settings)!;
    internal static string Serialize(PackConflictFile file) => JsonConvert.SerializeObject(file, Formatting.Indented, Settings);
    internal static void WritePublished(string payload, PackConflictFile file, IReadOnlyList<OverviewMod>? mods = null)
    {
        var errors = Validate(file, mods);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        string text = Serialize(file);
        if (Encoding.UTF8.GetByteCount(text) > FileLimitBytes) throw new InvalidDataException(Loc.T("Conflict presets exceed the 512 KB limit.", "冲突预设超过 512 KB 上限。", "衝突預設超過 512 KB 上限。"));
        OverviewRepository.AtomicWrite(Path.Combine(payload, FileName), text);
    }

    internal static IReadOnlyList<string> Validate(PackConflictFile file, IReadOnlyList<OverviewMod>? mods = null)
    {
        var errors = new List<string>();
        if (file == null || file.schemaVersion != 1) { errors.Add(Loc.T("Unsupported conflict preset format.", "不支持的冲突预设格式。", "不支援的衝突預設格式。")); return errors; }
        if (file.rules == null || file.rules.Count > RuleLimit) { errors.Add(Loc.T("Use at most 256 conflict rules.", "冲突规则最多 256 条，且规则列表不能为空。", "衝突規則最多 256 條，且規則清單不能為空。")); return errors; }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < file.rules.Count; i++)
        {
            var rule = file.rules[i];
            string prefix = Loc.F("Rule {0}: ", "规则 {0}：", "規則 {0}：", i + 1);
            if (rule == null) { errors.Add(prefix + Loc.T("The rule is empty.", "规则为空。", "規則為空。")); continue; }
            if (string.IsNullOrWhiteSpace(rule.id) || rule.id.Length > 128 || rule.id.Any(char.IsControl) || !ids.Add(rule.id.Trim())) errors.Add(prefix + Loc.T("Use a unique rule ID of at most 128 characters.", "规则标识不能为空、重复或超过 128 字。", "規則識別碼不能為空、重複或超過 128 字。"));
            if (rule.mode != PackConflictRule.AnyTwo && rule.mode != PackConflictRule.All) errors.Add(prefix + Loc.T("Select a supported trigger mode.", "请选择有效的触发模式。", "請選擇有效的觸發模式。"));
            if (rule.title == null || rule.title.Length > 160) errors.Add(prefix + Loc.T("Keep the title within 160 characters.", "标题最多 160 字。", "標題最多 160 字。"));
            if (string.IsNullOrWhiteSpace(rule.reason) || rule.reason.Length > 4000) errors.Add(prefix + Loc.T("Enter a reason within 4,000 characters.", "请填写冲突原因，最多 4000 字。", "請填寫衝突原因，最多 4000 字。"));
            if (rule.members == null || rule.members.Count < 2 || rule.members.Count > MemberLimit) { errors.Add(prefix + Loc.T("Select 2 to 32 mods.", "请选择 2 至 32 个模组。", "請選擇 2 至 32 個模組。")); continue; }
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in rule.members)
            {
                if (member == null || string.IsNullOrWhiteSpace(member.guid) || member.guid.Length > 256 || member.guid.Any(char.IsWhiteSpace) || member.guid.Any(char.IsControl) || member.name == null || member.name.Length > 256)
                { errors.Add(prefix + Loc.T("A mod identity or fallback name is invalid.", "模组标识或备用名称无效。", "模組識別碼或備用名稱無效。")); continue; }
                if (!guids.Add(member.guid)) errors.Add(prefix + Loc.T("The same mod occurs more than once.", "同一个模组重复出现。", "同一個模組重複出現。"));
                var mod = mods?.FirstOrDefault(m => m.Guid.Equals(member.guid, StringComparison.OrdinalIgnoreCase))
                    ?? mods?.FirstOrDefault(m => m.PluginGuids.Contains(member.guid, StringComparer.OrdinalIgnoreCase));
                // Missing members stay editable and count as their own possible toggle unit.
                units.Add(mod == null ? "guid:" + member.guid : PackConflictService.ToggleUnit(mod));
            }
            if (mods != null && units.Count < 2) errors.Add(prefix + Loc.T("These plugins toggle together. Select at least two independent mod packages.", "这些插件随包联动，请至少选择两个可独立启停的模组包。", "這些外掛隨包連動，請至少選擇兩個可獨立啟停的模組包。"));
        }
        return errors;
    }

    internal static string Digest(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
}
