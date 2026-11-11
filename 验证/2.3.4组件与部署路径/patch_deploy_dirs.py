"""Point every project whose deploy target vanished in the 2026-11-09 folder organization at its live install. Each
replacement names the exact old text, so a project that changed since the probe fails loudly instead of being guessed."""
import io
import os
import sys

MODS = os.path.abspath(sys.argv[1])

EDITS = {
    r"BetterPing\BetterPing.csproj": [
        (r"$(PluginsDir)0核心&amp;基础模组\[功能]更好的中键标记-BetterPing\</DeployDir>",
         r"$(PluginsDir)0基础模组\[功能]更好的中键标记-BetterPing\</DeployDir>")],
    r"Better_MicIndicator\Better_MicIndicator.csproj": [
        (r"BepInEx\plugins\0核心&amp;基础模组\[功能]更好的麦克风指示器(Better_MicIndicator)\</DeployDir>",
         r"BepInEx\plugins\0基础模组\[功能]更好的麦克风指示器(Better_MicIndicator)\</DeployDir>")],
    r"LocalFix\LocalFix.csproj": [
        (r"$(PluginsDir)0核心&amp;基础模组\[机制]综合本地修复-LocalFix\</DeployDir>",
         r"$(PluginsDir)0基础模组\[机制]综合本地修复-LocalFix\</DeployDir>")],
    r"MicEnhance\MicEnhance.csproj": [
        (r"$(PluginsDir)0核心&amp;基础模组\[功能]麦克风增强-MicEnhance\</DeployDir>",
         r"$(PluginsDir)0基础模组\[功能]麦克风增强-MicEnhance\</DeployDir>")],
    r"PeakVoiceFixFix\PeakVoiceFix\BetterPeakVoiceFix.csproj": [
        (r"BepInEx\plugins\0核心&amp;基础模组\[优化]更好的语音修复CN-1.1.6\</DeployDir>",
         r"BepInEx\plugins\0基础模组\[优化]更好的语音修复CN-1.1.8\</DeployDir>")],
    r"RecentlyPlayedWith\RecentlyPlayedWith.csproj": [
        (r"BepInEx\plugins\0核心&amp;基础模组\[功能]最近遇到的玩家-RecentlyPlayedWith-1.0.1\</DeployDir>",
         r"BepInEx\plugins\0基础模组\[功能]最近遇到的玩家-RecentlyPlayedWith-1.0.2\</DeployDir>")],
    r"ZombieTweaks\ZombieTweaks.csproj": [
        (r"$(PluginsDir)1拓展模组\[机制]僵尸调整-ZombieTweaks\</DeployDir>",
         r"$(PluginsDir)3玩法模组\[玩法][纯主机]僵尸追猎_快跑吧！-ZombieTweaks\</DeployDir>")],
    r"汉化\BetterReviveTeleport\PeakLateJoinButItActuallyWorks.csproj": [
        (r"BepInEx\plugins\0核心&amp;基础模组\[机制]更好的手动复活&amp;传送\</OutputPath>",
         r"BepInEx\plugins\0基础模组\[机制]更好的手动复活&amp;传送\</OutputPath>")],
    r"汉化\PlantholderOverhaul_ZH\PlantholderOverhaul_ZH.csproj": [
        (r"BepInEx\plugins\2额外模组\[装饰]迷你景观台-汉化-PlantholderOverhaul-0.1.4\PlantholderOverhaul_ZH",
         r"BepInEx\plugins\1拓展模组\[美化]迷你景观台-汉化-PlantholderOverhaul-0.1.7\PlantholderOverhaul_ZH")],
    r"汉化\SoFly_ZH\SoFly_ZH.csproj": [
        (r"$(PeakDir)BepInEx\plugins\2额外模组\[作弊]飞行模组So_Fly-2.1.0\SoFly_ZH\</DeployDir>",
         r"$(PeakDir)BepInEx\plugins\2额外模组\[调试]飞行模组So_Fly-2.1.0\SoFly_ZH\</DeployDir>")],
    r"随机地形增强\RandomTerrainEnhance.csproj": [
        (r"<DeployDir>$(PeakDir)\BepInEx\plugins\RandomTerrainEnhance</DeployDir>",
         r"<DeployDir>$(PeakDir)\BepInEx\plugins\3玩法模组\[补丁]随机地形增强</DeployDir>")],
}

for relative, pairs in EDITS.items():
    path = os.path.join(MODS, relative)
    raw = io.open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    for old, new in pairs:
        count = text.count(old)
        if count != 1:
            raise SystemExit("%s: expected 1 match, found %d: %s" % (relative, count, old))
        text = text.replace(old, new)
    io.open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
    print("updated", relative)
