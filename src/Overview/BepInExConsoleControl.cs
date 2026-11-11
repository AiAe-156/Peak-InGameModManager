#nullable enable
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
namespace ModConfigEnhance.Overview;
/// <summary>Use BepInEx's console driver, never close a console HWND or terminate the game.</summary>
internal static class BepInExConsoleControl
{
    private static Type Manager => typeof(BaseUnityPlugin).Assembly.GetType("BepInEx.ConsoleManager") ?? throw new InvalidOperationException("BepInEx console interface is unavailable.");
    private static ConsoleLogListener? _ownedListener;
    internal static bool Active => (bool?)Manager.GetProperty("ConsoleActive",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) == true;
    internal static void Toggle()
    {
        bool enabled=!Active;
        var type=Manager;
        try
        {
            type.GetMethod(enabled?"CreateConsole":"DetachConsole",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,null);
            if(enabled && !BepInEx.Logging.Logger.Listeners.OfType<ConsoleLogListener>().Any())
            { _ownedListener=new ConsoleLogListener();BepInEx.Logging.Logger.Listeners.Add(_ownedListener); }
            if(!enabled && _ownedListener!=null){BepInEx.Logging.Logger.Listeners.Remove(_ownedListener);_ownedListener.Dispose();_ownedListener=null;}
            if(type.GetField("ConfigConsoleEnabled",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) is ConfigEntry<bool> setting) setting.Value=enabled;
        }
        catch(TargetInvocationException e){throw new InvalidOperationException(e.InnerException?.Message??e.Message,e.InnerException??e);}
    }
}
