# -*- coding: utf-8 -*-
"""把桌面素材归集到 .tools/elf_ranger/src，统一 ASCII 文件名，便于检查与后续切片。"""
import os
import shutil
import glob

ROOT = r"C:\Users\Muwen\OneDrive\Desktop\绮剧伒娓镐緺绱犳潗"
DST = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
os.makedirs(DST, exist_ok=True)

# (目标子目录, 文件名包含的关键字)
RULES = [
    ("idle", ["idle_0"]),
    ("attack", ["single_0", "chain_head_0", "chain_core_0", "chain_tail_0"]),
    ("heavy", ["heavy_0"]),
    ("heal", ["heal_0"]),
    ("bramble", ["bush_grow_0", "bush_idle_0", "bush_fade_0"]),
    ("halo", ["halo_0"]),
    ("arrow", ["pflow_0"]),
    ("move", ["move_run.gif", "jump.gif", "dodge_forward.gif", "dodge_backward.gif"]),
    ("ref", ["Gemini_Generated"]),
]

all_files = []
for dp, dn, fns in os.walk(ROOT):
    for f in fns:
        all_files.append(os.path.join(dp, f))

copied = []
for sub, keys in RULES:
    out = os.path.join(DST, sub)
    os.makedirs(out, exist_ok=True)
    for p in all_files:
        base = os.path.basename(p)
        if not any(k in base for k in keys):
            continue
        # 只取纯 ASCII 部分做新名（去掉乱码前缀）
        ascii_part = "".join(ch for ch in base if ord(ch) < 128)
        ascii_part = ascii_part.lstrip("_")
        if not ascii_part:
            continue
        dst = os.path.join(out, ascii_part)
        shutil.copy2(p, dst)
        copied.append((sub, ascii_part, os.path.getsize(dst)))

for sub, name, size in sorted(copied):
    print("%-8s %-28s %8d" % (sub, name, size))
print("total", len(copied))
