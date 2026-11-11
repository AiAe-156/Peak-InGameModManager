using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Display language for the worker's own window: set once from --language, then every string goes through here.</summary>
internal static class WorkerLanguage
{
    internal static string Code { get; private set; } = Detect(System.Globalization.CultureInfo.CurrentUICulture.Name);

    internal static void Set(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code)) Code = Detect(code!);
    }

    static string Detect(string code)
    {
        string lower = code.Trim().ToLowerInvariant();
        if (lower.StartsWith("zh"))
            return lower.Contains("tw") || lower.Contains("hk") || lower.Contains("hant") || lower.Contains("mo") ? "zh-tw" : "zh-cn";
        return "en-us";
    }

    internal static string T(string english, string simplified, string traditional) => Code == "zh-cn" ? simplified : Code == "zh-tw" ? traditional : english;

    /// <summary>Decodes @MCE[…] tokens line by line; plain Chinese worker strings are mapped through the shared catalog too.</summary>
    internal static string Localize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = LocalizeLine(lines[i]);
        return string.Join("\n", lines);
    }

    static string LocalizeLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0) return line;
        if (UpdateMessages.TryDecode(UpdateMessages.Encode(trimmed), out var term, out var argument) && term != null)
            return Pick(term) + argument;
        return line;
    }

    static string Pick(UpdateMessageTerm term) => Code == "zh-cn" ? term.SimplifiedChinese : Code == "zh-tw" ? term.TraditionalChinese : term.English;

    internal static string StateText(string state) => state switch
    {
        UpdateStates.Queued => T("Queued", "排队中", "排隊中"),
        UpdateStates.Resolving => T("Resolving", "读取版本", "讀取版本"),
        UpdateStates.Downloading => T("Downloading", "下载中", "下載中"),
        UpdateStates.Paused => T("Paused", "已暂停", "已暫停"),
        UpdateStates.Verifying => T("Checking package", "检查包内容", "檢查套件內容"),
        UpdateStates.Preparing => T("Preparing", "准备写入", "準備寫入"),
        UpdateStates.WaitingExit => T("Downloaded ✓ · deploys after the game exits", "下载成功 ✓ · 等待退出游戏后部署", "下載成功 ✓ · 等待結束遊戲後部署"),
        UpdateStates.Deploying => T("Installing", "正在安装", "正在安裝"),
        UpdateStates.PendingRestart => T("Downloaded ✓ · deployed ✓ · restart to apply", "下载成功 ✓ · 已部署 ✓ · 等待重启生效", "下載成功 ✓ · 已部署 ✓ · 等待重新啟動生效"),
        UpdateStates.Completed => T("Applied ✓", "已生效 ✓", "已生效 ✓"),
        UpdateStates.Stopped => T("Stopped", "已停止", "已停止"),
        UpdateStates.Failed => T("Failed", "更新失败", "更新失敗"),
        UpdateStates.Removed => T("Removed", "已移除", "已移除"),
        UpdateStates.RollingBack => T("Rolling back", "回退中", "回退中"),
        UpdateStates.RollbackWaiting => T("Rollback after exit", "退出后回退", "結束後回退"),
        UpdateStates.RolledBack => T("Rolled back", "已回退", "已回退"),
        UpdateStates.Superseded => T("Superseded", "已被接替", "已被接替"),
        "requested" or "resolving" => T("Preparing store plan", "准备商店计划", "準備商店計畫"),
        "preview" or "ready" or "uninstall-preview" => T("Ready", "就绪", "就緒"),
        "committing" => T("Installing", "安装中", "安裝中"),
        "recovery-required" => T("Recovery required", "需要恢复", "需要復原"),
        "cancelled" => T("Cancelled", "已取消", "已取消"),
        _ => state
    };
}
