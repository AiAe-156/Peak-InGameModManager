import io
import os

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "ModConfigEnhance.csproj")
s = io.open(PATH, encoding="utf-8", newline="").read()
pairs = [
    (r"$(PluginsDir)0核心&amp;基础模组\[0依赖]Mods\[依赖]PEAKLib核心-PEAKLib_Core\plugins\com.github.PEAKModding.PEAKLib.Core.dll",
     r"$(PluginsDir)依赖模组\[依赖]PEAKLib核心-PEAKLib_Core\plugins\com.github.PEAKModding.PEAKLib.Core.dll"),
    (r"$(PluginsDir)0核心&amp;基础模组\[0依赖]Mods\[依赖]PEAKLib界面-PEAKLib_UI\com.github.PEAKModding.PEAKLib.UI.dll",
     r"$(PluginsDir)依赖模组\[依赖]PEAKLib界面-PEAKLib_UI\com.github.PEAKModding.PEAKLib.UI.dll"),
    (r"$(PluginsDir)0核心&amp;基础模组\[功能]游戏内更改mod配置-ModConfig-1.8.2\com.github.PEAKModding.PEAKLib.ModConfig.dll",
     r"$(PluginsDir)0基础模组\[功能]游戏内更改mod配置-ModConfig-1.8.2\com.github.PEAKModding.PEAKLib.ModConfig.dll"),
]
for a, b in pairs:
    n = s.count(a)
    assert n == 1, (n, a)
    s = s.replace(a, b)
io.open(PATH, "w", encoding="utf-8", newline="").write(s)
print("ok")
