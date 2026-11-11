#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;

namespace ModConfigEnhance;

/// <summary>
/// 2.5.0：与 XUnity AutoTranslator 只做「兼容」，不再联动。
/// 翻译全部由本管理器自己的语言文件完成；不写它的词典、不切它的语言、不再常驻拦截它的机翻。
/// 只做一件事：它开着在线机翻（ini 的 <c>[Service] Endpoint</c> 或 <c>FallbackEndpoint</c> 不为空）时明确提醒，
/// 用户点了才备份并清空这两行（下次启动生效）。只装了它、没开机翻时两边互不干涉，不提醒。
/// </summary>
internal static class XUnityBridge
{
    internal const string Guid = "gravydevsupreme.xunity.autotranslator";
    internal static bool Installed => Chainloader.PluginInfos.ContainsKey(Guid);
    internal static string IniPath => Path.Combine(Paths.ConfigPath, "AutoTranslatorConfig.ini");

    /// <summary>ini 里开着的在线翻译服务（Endpoint / FallbackEndpoint 的值）；没装或都为空返回空表。</summary>
    internal static List<string> OnlineEndpoints()
    {
        List<string> result = new List<string>();
        if (!Installed) return result;
        try
        {
            if (!File.Exists(IniPath)) return result; // 首次启动它还没写 ini：出厂默认就是在线机翻，等它写好下次再查
            string section = "";
            foreach (string raw in File.ReadAllLines(IniPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)) { section = line; continue; }
                if (!section.Equals("[Service]", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if ((key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) || key.Equals("FallbackEndpoint", StringComparison.OrdinalIgnoreCase)) && value.Length > 0)
                    result.Add(value);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[XUnity 兼容] 读取 AutoTranslatorConfig.ini 失败：" + ex.Message); }
        return result;
    }

    internal static bool MachineTranslationOn => OnlineEndpoints().Count > 0;

    /// <summary>用户点「关闭它的在线机翻」：先备份 ini，再只清空 [Service] 的两行，其余原样。返回备份路径。</summary>
    internal static string DisableMachineTranslation()
    {
        byte[] bytes = File.ReadAllBytes(IniPath);
        string backup = IniPath + ".mce-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
        File.WriteAllBytes(backup, bytes);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        string[] lines = text.Split('\n');
        string section = "";
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r'), trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal)) { section = trimmed; continue; }
            if (!section.Equals("[Service]", StringComparison.OrdinalIgnoreCase)) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) || key.Equals("FallbackEndpoint", StringComparison.OrdinalIgnoreCase))
                lines[i] = key + "=" + (lines[i].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
        }

        string temporary = IniPath + ".mce.tmp";
        File.WriteAllText(temporary, string.Join("\n", lines), new UTF8Encoding(bom));
        File.Copy(temporary, IniPath, true);
        File.Delete(temporary);
        Plugin.Log.LogInfo("[XUnity 兼容] 已按用户选择清空 AutoTranslatorConfig.ini 的在线翻译服务，原文件备份为 " + backup);
        return backup;
    }
}
