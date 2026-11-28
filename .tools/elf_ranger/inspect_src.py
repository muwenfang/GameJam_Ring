# -*- coding: utf-8 -*-
"""检查桌面「精灵游侠素材」：GIF 帧数、各帧 PNG 尺寸、切片图尺寸。"""
import os
import glob
from PIL import Image

ROOT = r"C:\Users\Muwen\OneDrive\Desktop\绮剧伒娓镐緺绱犳潗"


def dec(s):
    try:
        return s.encode("gbk").decode("utf-8")
    except Exception:
        return s


print("=== GIF ===")
for p in sorted(glob.glob(os.path.join(ROOT, "**", "*.gif"), recursive=True)):
    try:
        im = Image.open(p)
        n = getattr(im, "n_frames", 1)
        print("%-32s %5dx%-5d frames=%-3d duration=%s mode=%s" % (
            os.path.basename(p), im.width, im.height, n,
            im.info.get("duration"), im.mode))
    except Exception as e:
        print("ERR", p, e)

print()
print("=== JPG ===")
for p in sorted(glob.glob(os.path.join(ROOT, "**", "*.jpg"), recursive=True)):
    im = Image.open(p)
    print("%-60s %dx%d" % (os.path.basename(p), im.width, im.height))

print()
print("=== PNG (frames) ===")
groups = {}
for p in sorted(glob.glob(os.path.join(ROOT, "**", "*.png"), recursive=True)):
    name = os.path.basename(p)
    im = Image.open(p)
    key = dec(os.path.dirname(p))
    groups.setdefault(key, []).append((name, im.width, im.height, im.mode))

for k in sorted(groups):
    rows = groups[k]
    sizes = sorted(set((r[1], r[2]) for r in rows))
    modes = sorted(set(r[3] for r in rows))
    print("--- %s  n=%d  sizes=%s modes=%s" % (k, len(rows), sizes, modes))
    for r in rows[:6]:
        print("     ", r[0])
    if len(rows) > 6:
        print("      ... 共 %d 个" % len(rows))
