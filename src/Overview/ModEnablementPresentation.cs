#nullable enable
using TMPro;
using UnityEngine;
namespace ModConfigEnhance.Overview;
internal static class ModEnablementPresentation
{
    internal static readonly Color Enabled = new(.48f,.78f,.46f,1), Changing = new(.95f,.76f,.30f,1), Disabled = new(.73f,.73f,.69f,1);
    /// <summary>2.3.6: a resource pack has no DLL, so the loader never reports it as loaded. Its "current state" is
    /// whether its files are enabled; otherwise every enabled resource pack showed a grey box and
    /// "Enabled file did not load; see the game log".</summary>
    internal static bool Active(OverviewMod mod) => mod.ResourcePack ? mod.TargetEnabled : mod.Loaded;
    internal static Color Tint(OverviewMod mod, bool target, ProfileItemPending? pending) => IsChanging(mod, target, pending) ? Changing : Active(mod) ? Enabled : Disabled;
    internal static bool IsChanging(OverviewMod mod, bool target, ProfileItemPending? pending) => target != mod.TargetEnabled || pending?.NeedsRestart == true || mod.Loaded && !mod.TargetEnabled;
    internal static string Detail(OverviewMod mod, bool target, ProfileItemPending? pending)
    {
        string State(bool value) => value ? Loc.T("Enabled","启用","啟用") : Loc.T("Disabled","禁用","停用");
        string ColorValue(string text, Color color) => "<color=#"+ColorUtility.ToHtmlStringRGB(color)+">"+text+"</color>";
        bool changing=IsChanging(mod,target,pending);
        string stage=target!=mod.TargetEnabled?Loc.T("pending apply","待应用","待套用"):changing?Loc.T("pending restart","待重启","待重新啟動"):"";
        string result=mod.Name.Replace("<","＜")+"\n"+Loc.T("Current state: ","当前状态：","目前狀態：")+ColorValue(State(Active(mod)),Active(mod)?Enabled:Disabled)+"\n";
        result+=Loc.T("Target state: ","目标状态：","目標狀態：")+ColorValue(State(target)+(stage.Length>0?" ("+stage+")":""),changing?Changing:target?Enabled:Disabled)+"\n";
        result+=Loc.T("Configuration write: ","配置写入：","設定寫入：")+(pending?.NeedsSave==true?Loc.T("Temporary, pending save","临时，待保存","暫存，待儲存"):Loc.T("Saved","已保存","已儲存"));
        if(!mod.ResourcePack&&!mod.Loaded&&mod.TargetEnabled&&pending?.NeedsRestart!=true)result+="\n"+Loc.T("Enabled file did not load; see the game log.","文件已启用但本次未加载，原因见游戏日志。","檔案已啟用但本次未載入，原因見遊戲日誌。");
        if(mod.DuplicateCopy)result+="\n"+Loc.T("This plugin is installed in several folders; enabling this copy disables the others.","该模组装了多份副本；启用这份会停用其它副本，只保留一份。","該模組安裝了多份副本；啟用這份會停用其它副本，只保留一份。");
        if(!mod.CanToggle)result+="\n"+Loc.LocalizeMessage(mod.ToggleReason);
        return result;
    }
}
