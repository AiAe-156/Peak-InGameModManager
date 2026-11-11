using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(Types)) yield return nested;
    }

    private static string Body(MethodDefinition method)
    {
        if (!method.HasBody) return "";
        var list = method.Body.Instructions;
        var positions = list.Select((item, index) => new { item, index }).ToDictionary(x => x.item, x => x.index);
        return string.Join("\n", list.Select(i => i.OpCode.Code + " " + Operand(i.Operand, positions)))
            + "\n" + string.Join("\n", method.Body.ExceptionHandlers.Select(e =>
                e.HandlerType + " " + e.CatchType + " " + Operand(e.TryStart, positions) + " " + Operand(e.TryEnd, positions)
                + " " + Operand(e.HandlerStart, positions) + " " + Operand(e.HandlerEnd, positions)));
    }

    private static string Operand(object value, Dictionary<Instruction, int> positions)
    {
        if (value == null) return "";
        if (value is Instruction target) return "@" + positions[target];
        if (value is Instruction[] targets) return string.Join(",", targets.Select(t => "@" + positions[t]));
        if (value is VariableDefinition variable) return "v" + variable.Index;
        if (value is ParameterDefinition parameter) return "p" + parameter.Index;
        if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception("FAIL " + message);
        Console.WriteLine("PASS " + message);
    }

    private static int Main(string[] args)
    {
        string game = Path.GetFullPath(args[0]);
        string baseline = Path.GetFullPath(args[1]);
        string managed = Path.Combine(game, "PEAK_Data", "Managed");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        using (var before = AssemblyDefinition.ReadAssembly(baseline))
        using (var after = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll")))
        {
            var oldMethods = before.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
            var newMethods = after.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
            var changed = newMethods.Values.Where(m => oldMethods.ContainsKey(m.FullName) && Body(m) != Body(oldMethods[m.FullName])).ToArray();
            Console.WriteLine("CHANGED_METHOD_BODIES " + changed.Length);
            foreach (var method in changed.Where(m => !m.DeclaringType.FullName.Contains("UnitySourceGenerated")))
                Console.WriteLine("CHANGED " + method.FullName);
            foreach (var method in newMethods.Values.Where(m => !oldMethods.ContainsKey(m.FullName)))
                Console.WriteLine("ADDED " + method.FullName);

            Check(after.MainModule.GetType("CharacterCarrying").Methods.All(m => oldMethods.ContainsKey(m.FullName) && Body(m) == Body(oldMethods[m.FullName])), "背负类方法体全部保持原样");
            var warp = after.MainModule.GetType("Character").Methods.Single(m => m.Name == "WarpPlayerRPC");
            Check(warp.Parameters.Count == 3 && warp.Parameters.Last().ParameterType.FullName == "Photon.Pun.PhotonMessageInfo", "传送消息新增自动注入的发送者信息参数");
            Check(warp.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "get_IsMasterClient"), "传送接收端检查发送者是否为房主");
            var tabs = after.MainModule.GetType("SettingsTABSButton").Methods.Single(m => m.Name == "Update");
            Check(tabs.Body.Instructions.Count(i => i.Operand is MethodReference call && call.FullName == "System.Void UnityEngine.GameObject::SetActive(System.Boolean)") == 1, "MCE设置页签优化仍只有一个匹配位置");
        }

        var settingsAssembly = Assembly.LoadFrom(Path.Combine(managed, "Zorro.Settings.Runtime.dll"));
        Type ui = settingsAssembly.GetType("Zorro.Settings.UI.FloatSettingUI", true);
        object cell = FormatterServices.GetUninitializedObject(ui);
        var format = ui.GetMethod("GetFormattedString", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(format != null, "实际新版滑条格式化方法存在");
        string small = (string)format.Invoke(cell, new object[] { 0.35f });
        string multiplier = (string)format.Invoke(cell, new object[] { 1.25f });
        Console.WriteLine("ACTUAL_FORMAT 0.35=" + small + " 1.25=" + multiplier);
        Check(small == "0" && multiplier == "1", "实际调用确认普通小数被显示成整数");
        return 0;
    }
}
