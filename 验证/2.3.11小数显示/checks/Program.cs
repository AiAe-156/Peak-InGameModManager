// Runs the real ModFloatDisplayPatch.Prefix/Format against minimal stubs of the game and ModConfig types.
using System.Globalization;
using HarmonyLib;
using PEAKLib.ModConfig;
using Zorro.Settings;
using Zorro.Settings.UI;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int fail = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name); if (!ok) fail++; }
string Run(Setting? setting, float value, string text = "", bool focused = false)
{
    var ui = new FloatSettingUI { _listeningSetting = setting, inputField = new TMPro.TMP_InputField { text = text, isFocused = focused } };
    string result = "<vanilla>";
    var method = typeof(ModConfigEnhance.ModFloatDisplayPatch).GetMethod("Prefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    object?[] args = { ui, value, result };
    bool runOriginal = (bool)method.Invoke(null, args)!;
    return runOriginal ? "<vanilla>" : (string)args[2]!;
}
var mod = new ModSetting(); var vanilla = new Setting();
Check(Run(mod, 0.35f) == "0.35", "mod 0.35 shows 0.35 (was 0)");
Check(Run(mod, 1.25f) == "1.25", "mod 1.25 shows 1.25 (was 1)");
Check(Run(mod, 2f) == "2", "mod integer stays plain");
Check(Run(mod, 0.0005f) == "0.001" || Run(mod, 0.0005f) == "0", "mod tiny value rounds to 3 decimals");
Check(Run(mod, 12.3456f) == "12.346", "mod at most three decimals, rounded");
Check(Run(vanilla, 0.35f) == "<vanilla>", "game setting keeps the game's own formatting");
Check(Run(null, 0.35f) == "<vanilla>", "unbound cell keeps the game's own formatting");
Check(Run(mod, 1.2f, "1.2", focused: true) == "1.2", "while typing, the typed text is kept");
Check(Run(mod, 1.25f, "1.25", focused: true) == "1.25", "while typing 1.25 stays 1.25 (was rewritten to 1)");
Check(Run(mod, 1f, "1.", focused: true) == "1.", "half-typed '1.' is left alone while typing (the dot is not swallowed)");
Check(Run(mod, 3f, "1.", focused: true) == "3", "focused but value changed elsewhere (slider): text is reformatted");
Check(Run(mod, 0.35f, "0.35", focused: false) == "0.35", "not focused: formatted normally");
Console.WriteLine(fail == 0 ? "decimal display checks passed: 12" : $"{fail} check(s) failed");
return fail == 0 ? 0 : 1;

class ModSetting : Setting, IBepInExProperty { }
namespace PEAKLib.ModConfig { public interface IBepInExProperty { } }
namespace Zorro.Settings { public class Setting { } }
namespace TMPro { public class TMP_InputField { public string text = ""; public bool isFocused; } }
namespace Zorro.Settings.UI { public class FloatSettingUI { public Zorro.Settings.Setting? _listeningSetting; public TMPro.TMP_InputField? inputField; } }
namespace HarmonyLib
{
    public class Harmony { public void Patch(System.Reflection.MethodBase m, HarmonyMethod? prefix = null) { } }
    public class HarmonyMethod { public HarmonyMethod(Type t, string n) { } }
    public static class AccessTools { public static System.Reflection.MethodInfo? Method(Type t, string n, Type[]? a = null) => null; }
}
namespace ModConfigEnhance { internal static class Plugin { internal static class Log { internal static void LogWarning(string s) { } } } }
