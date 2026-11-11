# -*- coding: utf-8 -*-
"""续 move_old_mce.py：验证/ 第一次 rename 被后台 du 占用拒绝，shutil 退回复制并在长路径处中断，
留档里有一份不完整的 验证/ 副本，旧文件夹里的 验证/ 仍完整（复制不删源）。
这里：确认旧 验证/ 还在 → 删掉留档里的残缺副本（用 \\\\?\\ 长路径）→ 再 os.rename 整体移动（同盘改名，不复制）。
用法：python -I finish_move_verify.py [--apply]"""
import os
import shutil
import stat
import sys


def _writable(func, path, _):
    os.chmod(path, stat.S_IWRITE)
    func(path)

OLD = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance\验证"
PARTIAL = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\InGameModManager\备份\旧ModConfigEnhance工程留档_20261111\验证"
apply = "--apply" in sys.argv


def count(path):
    n = 0
    for _, _, fs in os.walk("\\\\?\\" + path):
        n += len(fs)
    return n


assert os.path.isdir(OLD), "旧 验证/ 不在了，停止"
print("旧 验证/ 文件数", count(OLD))
print("留档残缺副本文件数", count(PARTIAL) if os.path.isdir(PARTIAL) else 0)
if apply:
    if os.path.isdir(PARTIAL):
        shutil.rmtree("\\\\?\\" + PARTIAL, onexc=_writable)
    os.rename(OLD, PARTIAL)
    print("已移动；旧文件夹剩下：", os.listdir(os.path.dirname(OLD)))
    print("留档 验证/ 文件数", count(PARTIAL))
