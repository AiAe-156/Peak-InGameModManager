from pathlib import Path
root = Path(__file__).resolve().parents[2]
p = root / "src/Overview/ShopWindow.cs"
s = p.read_text(encoding="utf-8")
def rep(old, new, count=1):
    global s
    assert s.count(old) >= count, old[:80]
    s = s.replace(old, new)
# installation line of the opened package: name the package
rep("_status.text=InstallationText(p).Split('\\n')[0];", "_status.text=p.Name+\"：\"+InstallationText(p).Split('\\n')[0];", 2)
# task messages: name the target package
rep("_taskKey=key;_task=task;_status.text=ShopText(task.Message)+(task.Error.Length>0?\" · \"+ShopText(task.Error):\"\");",
    "_taskKey=key;_task=task;_status.text=(task.Request.Package.Length>0?task.Request.Package+\"：\":\"\")+ShopText(task.Message)+(task.Error.Length>0?\" · \"+ShopText(task.Error):\"\");")
# queued messages: name the package
rep("            _status.text=Loc.LocalizeMessage(\"Updates run independently; configuration changes remain unsaved.\");",
    "            _status.text=p.Name+\"：\"+Loc.LocalizeMessage(\"Updates run independently; configuration changes remain unsaved.\");")
rep("            _status.text=Loc.LocalizeMessage(\"Mod operation queued independently. Configuration changes remain unsaved.\");",
    "            _status.text=p.Name+\"：\"+Loc.LocalizeMessage(\"Mod operation queued independently. Configuration changes remain unsaved.\");")
p.write_text(s, encoding="utf-8")
print("ok")
