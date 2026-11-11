#nullable enable
namespace ModConfigEnhance.Overview;
/// <summary>Separate installation-version observations from author targets and config defaults.</summary>
internal static class ChangeRecordText
{
    internal static string Describe(string key,ChangeRecord change,string? current=null)
    {
        if(change.kind=="summary")return change.after;
        string Empty(string value)=>value.Length==0?Loc.T("Not recorded","未记录","未記錄"):value;
        if(key.StartsWith("mod:",System.StringComparison.Ordinal))
        {
            if(System.Version.TryParse(change.after,out _) || System.Version.TryParse(change.before,out _))
            {
                if(change.before.Length==0)return Loc.T("First observed in this environment","首次在此环境中记录到此模组","首次在此環境中記錄到此模組")+"\n"+Loc.T("Version: ","版本：","版本：")+change.after;
                if(change.after.Length==0)return Loc.T("No longer observed in this environment","当前环境不再记录此模组","目前環境不再記錄此模組")+"\n"+Loc.T("Previous version: ","此前版本：","先前版本：")+change.before;
                return Loc.T("Observed mod version changed","检测到模组版本变化","偵測到模組版本變化")+"\n"+Loc.T("Previous version: ","此前版本：","先前版本：")+change.before+"\n"+Loc.T("Current version: ","当前版本：","目前版本：")+change.after;
            }
            string State(string value)=>bool.TryParse(value,out bool on)?on?Loc.T("Enabled","启用","啟用"):Loc.T("Disabled","禁用","停用"):Empty(value);
            return Loc.T("Author enablement changed","作者启停设置发生变化","作者啟停設定發生變化")+"\n"+Loc.T("Previous state: ","此前状态：","先前狀態：")+State(change.before)+"\n"+Loc.T("New state: ","新状态：","新狀態：")+State(change.after);
        }
        if(key.StartsWith("data:",System.StringComparison.Ordinal))return Loc.T("Author data changed: ","作者资料发生变化：","作者資料發生變化：")+key.Substring(5);
        return Loc.T("Previous default: ","原默认：","原預設：")+Empty(change.before)+"\n"+Loc.T("New default: ","新默认：","新預設：")+Empty(change.after)+(current==null?"":"\n"+Loc.T("Current value: ","当前值：","目前值：")+current);
    }
}
