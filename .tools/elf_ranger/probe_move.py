# -*- coding: utf-8 -*-
"""对比两种跑动/跳跃/闪避素材：GIF 逐帧 vs JPG 条带切格。
输出：每帧人物包围盒尺寸/基线一致性，以及白底占比。"""
import os
import glob
from PIL import Image

SRC = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
OUT = r"D:\unity\GameJam_Ring\.tools\elf_ranger\probe"
os.makedirs(OUT, exist_ok=True)


def bbox_of(im, thresh=245):
    """白底转 alpha 后取 bbox（R/G/B 都 > thresh 视为背景）。"""
    rgba = im.convert("RGBA")
    px = rgba.load()
    w, h = rgba.size
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            r, g, b, a = px[x, y]
            if a > 8 and not (r > thresh and g > thresh and b > thresh):
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
    if maxx < 0:
        return None
    return minx, miny, maxx, maxy


print("=== GIF frames ===")
for gif in sorted(glob.glob(os.path.join(SRC, "move", "*.gif"))):
    im = Image.open(gif)
    n = getattr(im, "n_frames", 1)
    print("---", os.path.basename(gif), im.size, "frames", n, "mode", im.mode)
    for i in range(n):
        im.seek(i)
        fr = im.convert("RGBA")
        bb = bbox_of(fr)
        if bb:
            print("   f%02d bbox=%s  h=%d  baseline_from_bottom=%d" % (
                i + 1, bb, bb[3] - bb[1], fr.height - 1 - bb[3]))
        else:
            print("   f%02d EMPTY" % (i + 1))

print()
print("=== JPG strips (assume 6 cells) ===")
for jpg in sorted(glob.glob(os.path.join(SRC, "ref", "*.jpg"))):
    im = Image.open(jpg).convert("RGB")
    n = 6
    cw = im.width // n
    print("---", os.path.basename(jpg), im.size, "cell", cw, "x", im.height)
    for i in range(n):
        cell = im.crop((i * cw, 0, (i + 1) * cw, im.height))
        bb = bbox_of(cell)
        if bb:
            print("   f%02d bbox=%s  w=%d h=%d  baseline_from_bottom=%d" % (
                i + 1, bb, bb[2] - bb[0], bb[3] - bb[1], cell.height - 1 - bb[3]))
        else:
            print("   f%02d EMPTY" % (i + 1))
