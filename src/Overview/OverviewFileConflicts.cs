#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Overview;

internal static class OverviewFileConflicts
{
    internal static string Copies(string mod, IEnumerable<string> paths) => Loc.F(
        "Plugin file conflict: {0}\nMultiple active/disabled copies remain. No files were changed. Keep one intended copy, or restore foreign-disabled files using their original tool.\n{1}",
        "模组文件冲突：{0}\n存在多个在用/停用副本，未修改文件。请确认保留哪个副本；其他工具停用的文件请使用原工具恢复。\n{1}",
        "模組文件衝突：{0}\n存在多個在用/停用副本，未修改文件。請確認保留哪個副本；其他工具停用的文件請使用原工具恢復。\n{1}",
        mod, string.Join("\n", paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)));
}
