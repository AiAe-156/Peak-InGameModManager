#nullable enable
using System;

namespace ModConfigEnhance.Updating;

/// <summary>Turns worker problem strings into short, neutral descriptions. Shared by the game and the worker.</summary>
public static class PackReportText
{
    public static string Pretty(string package)
    {
        int dash = package.IndexOf('-');
        return (dash >= 0 ? package[(dash + 1)..] : package).Replace('_', ' ');
    }

    public static PackReportLine Problem(string raw)
    {
        var line = new PackReportLine { Section = "problem", State = "error", Title = raw };
        const string store = "商店依赖未补齐：";
        string inner = raw.StartsWith(store, StringComparison.Ordinal) ? raw[store.Length..] : raw;
        bool Starts(string prefix, out string rest)
        {
            bool match = inner.StartsWith(prefix, StringComparison.Ordinal); rest = match ? inner[prefix.Length..] : "";
            return match;
        }
        void Set(string title, string detail = "", string hint = "") { line.Title = title; line.Detail = detail; line.Hint = hint; }
        string rest;
        if (Starts("已选择版本低于传递依赖要求：", out rest) || Starts("作者固定版本低于传递依赖要求：", out rest))
        {
            // "<key> <chosen> < <needed>（<who> 要求至少 <v>）"
            int open = rest.IndexOf('（'); string head = open >= 0 ? rest[..open] : rest, who = open >= 0 ? rest[(open + 1)..].TrimEnd('）') : "";
            var part = head.Split(' ');
            if (part.Length >= 4)
            {
                bool author = raw.Contains("作者固定版本");
                Set("依赖版本冲突：" + Pretty(part[0]),
                    (who.Length > 0 ? who + "。" : "") + (author ? "整合包作者将其固定为 " + part[1] + "，" : "整合包指定的版本为 " + part[1] + "，") + "低于所需的 " + part[3] + "。",
                    "需由整合包作者将其改为 " + part[3] + " 或更高版本后重新发布。");
                return line;
            }
        }
        if (Starts("缺少模组：", out rest)) Set("缺少已启用的模组：" + rest, "方案要求启用该模组，但整合包内没有，也无法从 Thunderstore 获取。", "获取完整的整合包，或在方案中取消启用该模组。");
        else if (Starts("缺少预装加载器：", out rest)) Set("未检测到 BepInEx", "整合包需要 " + rest + "。", "先安装 BepInEx 5 后重新预览。");
        else if (Starts("BepInEx 5 加载器不完整或主版本不兼容：", out rest)) Set("本机 BepInEx 版本不符合要求", "整合包需要 " + rest + "。", "修复或更换为 BepInEx 5.x 后重新预览。");
        else if (Starts("预装核心辅助包版本不满足：", out rest)) Set("本机已安装的组件版本不符合要求", rest, "安装或升级 BepInEx/core 中的该组件后重新预览。");
        else if (Starts("商店依赖存在循环：", out rest)) Set("依赖关系循环，无法解析：" + Pretty(rest), "", "需由整合包或依赖的作者修正。");
        else if (Starts("重复插件身份：", out rest)) Set("重复的模组（同一标识出现多次）：" + rest, "包内有多个文件使用同一个模组标识。", "只保留其中一份后重新打包。");
        else if (Starts("核心兼容检查未通过（不会写入）：", out rest)) Set("核心文件与整合包不一致：" + rest, "MCE 不会修改核心文件。", "将本机 BepInEx 调整为整合包所用的版本后重新预览。");
        else if (Starts("宿主版本不足：", out rest)) Set("MCE 或其依赖的版本过低", rest, "先更新 MCE（导入整合包不会升级它），再重新预览。");
        else if (Starts("随包本地版本与依赖要求冲突：", out rest)) Set("随包依赖的版本不符合要求", rest, "需由整合包作者核对打包的版本。");
        else if (Starts("文件目标冲突：", out rest)) Set("多个来源写入同一位置且内容不同：" + rest, "", "去掉其中一份后重新打包。");
        else if (Starts("复用本地文件存在冲突：", out rest)) Set("本机文件与整合包内容不一致：" + rest, "", "需由整合包作者核对打包内容。");
        else if (Starts("缺少必需插件依赖：", out rest)) Set("缺少前置模组：" + rest, "整合包内的某个模组依赖它。", "补充该模组，或先在本机安装。");
        else if (Starts("插件依赖版本不足：", out rest)) Set("前置模组版本过低", rest.Replace(" 要求 ", "，至少需要 "), "需由整合包作者升级打包的版本。");
        else if (Starts("需人工安装的不支持内容：", out rest)) Set("包含不会自动处理的内容：" + rest, "monomod 和脚本类文件不会被自动放入游戏。", "如确有需要，请手动安装；整合包仍可存入。");
        else if (Starts("含本机运行数据：", out rest)) Set("包内含有 MCE 运行数据：" + rest, "", "重新打包时排除 MCE 运行目录。");
        else if (Starts("BepInEx 下的额外内容需要人工归类：", out rest)) Set("无法判断存放位置的文件：" + rest, "", "将其放入 plugins、patchers 或 config 后重新打包。");
        else if (Starts("作者根清单版本锁冲突：", out rest)) Set("同一依赖被固定为多个版本：" + Pretty(rest), "", "需由整合包作者统一版本后重新发布。");
        else if (Starts("同一 DLL 中的模组启停目标冲突：", out rest)) Set("同一文件内的模组启停目标冲突：" + rest, "", "需由整合包作者修正默认方案。");
        else if (Starts("默认启停目标重名：", out rest)) Set("默认方案的启停目标会产生重名文件：" + rest, "", "需由整合包作者修正默认方案。");
        else if (inner.StartsWith("存在多份作者资料根目录", StringComparison.Ordinal)) Set("存在多份作者资料", "", "只保留一份后重新打包。");
        else if (raw.StartsWith(store, StringComparison.Ordinal)) Set("依赖获取失败", inner, "网络相关的问题请检查网络或代理设置后重新预览。");
        return line;
    }
}
