# -*- coding: utf-8 -*-
"""1.1.4 (user: "remove the draft system from the mod settings page completely"): ModConfig's native controls write
the ConfigEntry directly again (its own Save/Apply/Default run unchanged; BepInEx saves the cfg). IGMM no longer
intercepts them, no draft file, no deferred commit. The viewed profile's in-memory record follows the live value so
the overview shows no false "unsaved" mark; nothing is written to disk by IGMM for it."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(ROOT, 'src')

p = os.path.join(SRC, 'Overview', 'NativeConfigurationDraft.cs')
io.open(p, 'w', encoding='utf-8', newline='\n').write('''#nullable enable
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PEAKLib.ModConfig;
using Zorro.Settings;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4（用户：「模组设置页面完全移除草稿系统」）：ModConfig 原生控件不再被拦截，改值由 ModConfig 自己写进
/// ConfigEntry（BepInEx 默认立即存盘），当场生效。IGMM 不写草稿、不写恢复文件、不延迟提交。
/// 只做一件事：把当前查看方案的内存记录跟上实际值，概览里不会因此出现「未保存」。
/// 之前的草稿拦截（Save/ApplyValue/SetDefaultValue 前缀、RefreshValueFromConfig 后缀）全部移除。
/// </summary>
internal static class NativeConfigurationDraft
{
    internal static OverviewRepository? Repository;
    [ThreadStatic] private static int _committing;

    /// <summary>Kept for callers that push values into native settings (runtime sync); no longer changes behaviour here.</summary>
    internal sealed class CommitScope : IDisposable
    {
        internal CommitScope() { _committing++; }
        public void Dispose() { _committing--; }
    }

    internal static void Install(Harmony harmony)
    {
        // Follow the live value after ModConfig writes it (Save → its save callback sets ConfigEntry.BoxedValue).
        // Walk each concrete setting type and its ancestors, so closed generic parents (e.g. a Setting<int> that declares
        // Save) are patched once through the closed type; open generic definitions are never patched.
        foreach (var type in typeof(IBepInExProperty).Assembly.GetTypes())
        {
            if (type.ContainsGenericParameters || !typeof(IBepInExProperty).IsAssignableFrom(type) || !typeof(Setting).IsAssignableFrom(type)) continue;
            for (Type? current = type; current != null && current != typeof(Setting) && current != typeof(object); current = current.BaseType)
            {
                if (current.ContainsGenericParameters) continue;
                foreach (var method in current.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (method.Name is not ("Save" or "SetDefaultValue") || method.IsAbstract || !Patched.Add(method)) continue;
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(NativeConfigurationDraft), nameof(Follow)));
                }
            }
        }
    }
    private static readonly HashSet<System.Reflection.MethodBase> Patched = new();

    /// <summary>After a native write: record the live value in the viewed profile (memory only, no file, no draft).</summary>
    private static void Follow(object __instance)
    {
        if (_committing != 0 || __instance is not IBepInExProperty property) return;
        try { Repository?.FollowLiveValue(property.Pluginfo.Metadata.GUID, property.ConfigBase); }
        catch (Exception error) { Plugin.Log.LogDebug("[设置页] 方案记录跟随失败：" + error.Message); }
    }
}
''')

def edit(rel, pairs):
    path = os.path.join(ROOT, rel)
    s = io.open(path, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    io.open(path, 'w', encoding='utf-8', newline='').write(s)

# repository: follow a live value in memory, both in the working copy and the saved baseline (so it is not "unsaved")
edit('src/Overview/OverviewRepository.Profiles.cs', [
('''    public bool SetDraftConfig(string guid, ConfigEntryBase entry, string serialized, out string error)''',
 '''    /// <summary>1.1.4: the settings page writes values directly. Mirror the live value into the viewed profile's working
    /// copy and its saved baseline (memory only), so the overview neither shows it as unsaved nor reverts it on a later
    /// save. Only for parameters the profile already records; nothing is written to disk here.</summary>
    internal void FollowLiveValue(string guid, ConfigEntryBase entry)
    {
        if (_appliedView || guid == Plugin.PluginGuid) return;
        string file = RelativeConfig(entry.ConfigFile.ConfigFilePath); if (file.Length == 0) return;
        string live = entry.GetSerializedValue();
        void Update(OverviewConfigSnapshot? snapshot)
        {
            if (snapshot?.values == null) return;
            foreach (var value in snapshot.values)
                if (value.pluginGuid.Equals(guid, StringComparison.OrdinalIgnoreCase) && value.configFile.Replace('\\\\', '/').Equals(file.Replace('\\\\', '/'), StringComparison.OrdinalIgnoreCase)
                    && value.section == entry.Definition.Section && value.key == entry.Definition.Key) value.serializedValue = live;
        }
        bool wasClean = !IsProfileDirty;
        Update(_draftConfig); Update(_savedDraftProfile.config); Update(_editingRuntimeBaseline);
        if (wasClean) _savedDraftSignature = DraftSignature(State, _draftConfig, _draftMods);
        InvalidatePendingView();
    }
    public bool SetDraftConfig(string guid, ConfigEntryBase entry, string serialized, out string error)'''),
])

# the debounce pump is no longer used by the settings page
p = os.path.join(SRC, 'Overview', 'SettingCommitPump.cs')
if os.path.exists(p): os.remove(p)

# fixture: production Harmony checks for the new behaviour
fx = os.path.join(ROOT, '验证', '统一保存重构', 'HarmonyChecks', 'Program.cs')
s = io.open(fx, encoding='utf-8').read()
old_start = s.index('var setting=new FakeSetting(entry);')
old_end = s.index('int notifications=0;')
s = s[:old_start] + '''var setting=new FakeSetting(entry);setting.Value=2;setting.ApplyValue();setting.Save(null!);Check(entry.Value==2&&setting.Applies==1&&setting.Saves==1,"native control writes the live config directly (no draft)");Check(repo.Followed==1,"profile record follows the live value");
setting.SetDefaultValue();Check(entry.Value==1&&repo.Followed==2,"native default writes directly and is followed");
using(new NativeConfigurationDraft.CommitScope()){setting.Value=4;setting.Save(null!);}Check(entry.Value==4&&repo.Followed==2,"runtime sync writes are not echoed back");
var generic=new FakeKeySetting(entry);generic.Value=5;generic.Save(null!);Check(entry.Value==5&&repo.Followed==3,"closed generic Save is followed");
''' + s[old_end:]
s = s.replace('internal sealed class OverviewRepository{internal string? Target;','internal sealed class OverviewRepository{internal int Followed;internal void FollowLiveValue(string g,ConfigEntryBase e){Followed++;}internal string? Target;')
s = s.replace('internal sealed class SettingCommitPump{internal static void Schedule(string guid,System.Action<string> commit)=>commit(guid);internal static void EnsureModalHost(){}}', '')
io.open(fx, 'w', encoding='utf-8', newline='').write(s)
print('ok')
