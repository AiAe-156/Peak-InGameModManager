# -*- coding: utf-8 -*-
"""用户 2026-11-11：「旧MCE文件夹全部清空 将1.0.2的源码拉回进去」。
把 MODs/ModConfigEnhance/ 顶层除 .git 外的所有内容整体移到 IGMM 的 备份/旧ModConfigEnhance工程留档_20261111/（同盘改名，不复制不删除）。
之后由调用方在旧文件夹 git reset --hard c932da0（1.0.2，已推送 GitHub）。
用法：python -I move_old_mce.py [--apply]"""
import os
import shutil
import sys

OLD = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance"
DEST = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\InGameModManager\备份\旧ModConfigEnhance工程留档_20261111"
apply = "--apply" in sys.argv

assert os.path.isdir(os.path.join(OLD, ".git")), "旧文件夹没有 .git"
names = sorted(n for n in os.listdir(OLD) if n != ".git")
print("将移动", len(names), "项 →", DEST)
for n in names:
    print("  ", n)
if apply:
    assert not os.path.exists(DEST), "留档目录已存在，停止"
    os.makedirs(DEST)
    for n in names:
        shutil.move(os.path.join(OLD, n), os.path.join(DEST, n))
    left = sorted(os.listdir(OLD))
    print("旧文件夹剩下：", left)
