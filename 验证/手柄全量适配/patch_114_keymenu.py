# -*- coding: utf-8 -*-
"""1.1.4 (user screenshot): key cell menu order and wording; all items write at once."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
p = os.path.abspath(os.path.join(HERE, '..', '..', 'src', 'Overview', 'OverviewHotkeyView.cs'))
s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
a = '''        var items=new List<(string Label,Action Action,bool Enabled)>{
            (T("Set profile target to unbound","方案目标设为未绑定"),()=>Write(()=>Service.Clear(entry,out var error)?null:error),entry.CanClear),
            (T("Record original mod default","记录原模组默认"),()=>Write(()=>Service.RestoreDefault(entry,out var error)?null:error),true)};
        foreach(var conflict in entry.Conflicts.Where(k=>!k.IsGame).ToList()){var c=conflict;items.Add((T("Ignore: ","忽略与：")+OverviewLanguageBridge.GetDisplayName(c.Mod)+" / "+c.Key,()=>Write(()=>Service.IgnoreConflict(entry,c.EntryId,out var error)?null:error),true));}
        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>Write(()=>Service.RestoreIgnoredFor(entry,out var error)?null:error),true));
        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();_status(Loc.T("Controller combo removed.","已移除手柄组合键。","已移除手把組合鍵。"));Render();},true));
        Popup(anchor,items);'''
b = '''        // 1.1.4 (user screenshot): reset to mod default / reset to profile preset / ignore ↔ … / set current as preset.
        // Every item writes at once like recording does (the keybindings page has no draft step).
        var preset=entry.PackPreset;object? presetValue=null;
        if(preset!=null&&!preset.cleared)try{presetValue=entry.Adapter.Deserialize(preset.value);}catch{presetValue=null;}
        var items=new List<(string Label,Action Action,bool Enabled)>{
            (Loc.T("Reset to the mod's default key","重设为模组默认预设的快捷键","重設為模組預設的快捷鍵"),()=>WriteNow(()=>Service.RestoreDefault(entry,out var error)?null:error),true),
            (Loc.T("Reset to the key set in the current profile preset","重设为当前方案预设中设定的快捷键","重設為目前方案預設中設定的快捷鍵"),()=>{if(presetValue!=null)WriteNow(()=>Service.TrySet(entry,presetValue,out var error)?null:error);},presetValue!=null)};
        foreach(var conflict in entry.Conflicts.Where(k=>!k.IsGame).ToList()){var c=conflict;items.Add((Loc.T("Ignore conflict ↔ ","忽略冲突 ↔ ","忽略衝突 ↔ ")+OverviewLanguageBridge.GetDisplayName(c.Mod)+" / "+c.Key,()=>{string error;if(Service.IgnoreConflict(entry,c.EntryId,out error)){_status(Loc.T("Conflict ignored.","已忽略这条冲突。","已忽略這條衝突。"));Render();_headersChanged();}else _status(Loc.LocalizeMessage(error));},true));}
        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>{string error;if(Service.RestoreIgnoredFor(entry,out error)){Render();_headersChanged();}else _status(Loc.LocalizeMessage(error));},true));
        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();_status(Loc.T("Controller combo removed.","已移除手柄组合键。","已移除手把組合鍵。"));Render();},true));
        items.Add((Loc.T("Set the current key as the current profile preset","将当前快捷键设为当前的方案预设","將目前快捷鍵設為目前的方案預設"),()=>WriteNow(()=>Service.SetPackPreset(entry,entry.Config.BoxedValue,out var error)?null:error),true));
        Popup(anchor,items);'''
a = a.replace('\n', nl); b = b.replace('\n', nl)
assert s.count(a) == 1, s.count(a)
s = s.replace(a, b)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
