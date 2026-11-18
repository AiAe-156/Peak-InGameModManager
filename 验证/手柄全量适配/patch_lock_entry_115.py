from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:100])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")
hv = "src/Overview/OverviewHotkeyView.cs"

# ── A. recording stays locked after each recorded key; only holding B alone for 1 s (or Esc) ends it ──
edit(hv, """                GamepadCombos.Clear(entry.Id); EndComboRecording();
                object? value=GamepadCombos.NativeValue(entry.Config,button);
                if(value==null){_status(Loc.T("This mod does not accept that button.","这个模组不接受这个键。","這個模組不接受這個鍵。"));Render();return;}
                WriteNow(()=>Service.TrySet(entry,value,out var error)?null:error); return;""",
"""                object? value=GamepadCombos.NativeValue(entry.Config,button);
                if(value==null){_status(Loc.T("This mod does not accept that button. Recording continues; hold B for 1 second to finish.","该模组不接受这个键。录制继续，长按 B 1 秒结束。","該模組不接受這個鍵。錄製繼續，長按 B 1 秒結束。"));return;}
                GamepadCombos.Clear(entry.Id);
                WriteNow(entry,()=>Service.TrySet(entry,value,out var error)?null:error,StillRecording); return;""")
edit(hv, """                _comboToPreset=false; EndComboRecording();
                GamepadCombos.SetPreset(_repo.State,entry.Id,entry.Guid,entry.Section,entry.Key,used,button); _repo.Save();
                _status(Loc.T("Controller key recorded in this profile.","手柄键已记入此方案。","手把鍵已記入此方案。")); Render(); return;""",
"""                GamepadCombos.SetPreset(_repo.State,entry.Id,entry.Guid,entry.Section,entry.Key,used,button); _repo.Save();
                _status(Loc.F("Recorded in this profile: {0}.","已记入此方案：{0}。","已記入此方案：{0}。",(used.Length>0?GamepadCombos.ButtonName(used)+" + ":"")+GamepadCombos.ButtonName(button))+StillRecording); Render(); return;""")
edit(hv, """            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();""",
"""            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            Service.RefreshConflicts();""")
edit(hv, """                +(other!=null?" "+Loc.F("(moved from {0})","（原来分给了 {0}）","（原來分給了 {0}）",OverviewLanguageBridge.GetDisplayName(other.Mod)+" / "+other.Name):""));
            Render();return;""",
"""                +(other!=null?" "+Loc.F("(moved from {0})","（原来分给了 {0}）","（原來分給了 {0}）",OverviewLanguageBridge.GetDisplayName(other.Mod)+" / "+other.Name):"")+StillRecording);
            Render();return;""")
edit(hv, "    private float _bDownAt=-1;\n",
"""    private float _bDownAt=-1;
    /// <summary>1.1.5 (user: "locked until a 1 s B hold, nothing else"): appended to every message while still recording.</summary>
    private static string StillRecording=>" "+Loc.T("Still recording: press another button to replace it, hold B for 1 second to finish.","仍在录制：再按其他键可覆盖，长按 B 1 秒结束。","仍在錄製：再按其他鍵可覆蓋，長按 B 1 秒結束。");
""")
edit(hv, """bool bAlone=_take.Count==1&&_take[0]=="buttonEast"&&GamepadLock.Pressed("buttonEast")&&_bDownAt>=0;
        HoldRing.Show(_root,bAlone?Mathf.Clamp01((Time.unscaledTime-_bDownAt)/1f):-1f);
        if(bAlone&&Time.unscaledTime-_bDownAt>=1f){_take.Clear();_bDownAt=-1;EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}""",
"""bool bAlone=_take.Count==1&&_take[0]=="buttonEast"&&GamepadLock.Pressed("buttonEast")&&_bDownAt>=0;
        HoldRing.Show(_root,bAlone?Mathf.Clamp01((Time.unscaledTime-_bDownAt)/1f):-1f);
        if(bAlone&&Time.unscaledTime-_bDownAt>=1f){_take.Clear();_bDownAt=-1;EndComboRecording();_status(Loc.T("Recording finished.","录制已结束。","錄製已結束。"));return;}""")

# ── B. writes commit only the one hotkey being changed (not every hotkey in the draft) ──
edit(hv, """    private void WriteNow(Func<string?> operation)
    {
        string? error=operation();
        if(!string.IsNullOrEmpty(error)){_status(Loc.LocalizeMessage(error));return;}
        try
        {
            ConfigurationSaveRequest.Run(_repo,_mods(),ConfigurationSaveScope.Hotkeys,"",result=>{
                _status(result.Success?Loc.T("Key saved and in use.","按键已保存并生效。","按鍵已儲存並生效。"):Loc.LocalizeMessage(result.Message));
                Render();_headersChanged();});
        }
        catch(Exception ex){_status(Loc.LocalizeMessage(ex.Message));Render();}
    }""",
"""    private void WriteNow(OverviewHotkeyEntry entry,Func<string?> operation,string suffix="")
    {
        string? error=operation();
        if(!string.IsNullOrEmpty(error)){_status(Loc.LocalizeMessage(error)+suffix);return;}
        try
        {
            // 1.1.5 (user: "resetting the fly modifier changed the fly toggle key"): commit only this hotkey. The hotkey
            // scope committed every hotkey value held in the profile draft, so a stale value of another key was written too.
            ConfigurationSaveRequest.Run(_repo,_mods(),ConfigurationSaveScope.HotkeyEntry,_repo.ConfigIdentity(entry.Guid,entry.Config),result=>{
                _status((result.Success?Loc.F("Saved: {0} / {1} = {2}.","已保存：{0} / {1} = {2}。","已儲存：{0} / {1} = {2}。",OverviewLanguageBridge.GetDisplayName(entry.Mod),entry.Name,entry.CurrentLabel):Loc.LocalizeMessage(result.Message))+suffix);
                Render();_headersChanged();});
        }
        catch(Exception ex){_status(Loc.LocalizeMessage(ex.Message)+suffix);Render();}
    }""")
s = (root / hv).read_text(encoding="utf-8")
s = s.replace("WriteNow(()=>Service.RestoreDefault(entry,out var error)?null:error)", "WriteNow(entry,()=>Service.RestoreDefault(entry,out var error)?null:error)")
s = s.replace("WriteNow(()=>Service.TrySet(entry,value,out var error)?null:error)", "WriteNow(entry,()=>Service.TrySet(entry,value,out var error)?null:error)")
s = s.replace("WriteNow(()=>Service.TrySet(entry,presetValue,out var error)?null:error)", "WriteNow(entry,()=>Service.TrySet(entry,presetValue,out var error)?null:error)")
s = s.replace("WriteNow(()=>Service.SetPackPreset(entry,entry.Config.BoxedValue,out var error)?null:error)", "WriteNow(entry,()=>Service.SetPackPreset(entry,entry.Config.BoxedValue,out var error)?null:error)")
(root / hv).write_text(s, encoding="utf-8")
assert "WriteNow(()" not in s, "unconverted WriteNow"

edit("src/Overview/OverviewRepository.SaveScope.cs",
     "internal enum ConfigurationSaveScope { All, Hotkeys, ModParameters, Parameters, Information, Groups, Introduction, ModInformation }",
     "internal enum ConfigurationSaveScope { All, Hotkeys, ModParameters, Parameters, Information, Groups, Introduction, ModInformation, HotkeyEntry }")
edit("src/Overview/OverviewRepository.SaveScope.cs",
     "            if (scope == ConfigurationSaveScope.ModParameters) return value.pluginGuid.Equals(modGuid, StringComparison.OrdinalIgnoreCase);",
     "            if (scope == ConfigurationSaveScope.ModParameters) return value.pluginGuid.Equals(modGuid, StringComparison.OrdinalIgnoreCase);\n            if (scope == ConfigurationSaveScope.HotkeyEntry) return value.Identity.Equals(modGuid, StringComparison.Ordinal); // modGuid carries the identity")
edit("src/Overview/OverviewRepository.Profiles.cs",
     "    internal void FollowLiveValue(string guid, ConfigEntryBase entry)",
     """    /// <summary>The draft identity of one config entry (same form as <see cref="OverviewConfigValue.Identity"/>).</summary>
    internal string ConfigIdentity(string guid, ConfigEntryBase entry)
        => new OverviewConfigValue { pluginGuid = guid, configFile = RelativeConfig(entry.ConfigFile.ConfigFilePath), section = entry.Definition.Section, key = entry.Definition.Key }.Identity;
    internal void FollowLiveValue(string guid, ConfigEntryBase entry)""")
print("ok")
