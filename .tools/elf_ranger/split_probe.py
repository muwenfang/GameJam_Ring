# -*- coding: utf-8 -*-
"""按等分位置附近的墨量谷值切分条带，并导出 GIF 逐帧大图用于人工确认。"""
import os
import glob
import numpy as np
from PIL import Image, ImageDraw

SRC = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
OUT = r"D:\unity\GameJam_Ring\.tools\elf_ranger\probe"
os.makedirs(OUT, exist_ok=True)


def white_to_alpha(im, thresh=248):
    rgb = im.convert("RGB")
    w, h = rgb.size
    arr = np.asarray(rgb).astype(np.int16)
    near = (arr[:, :, 0] >= thresh) & (arr[:, :, 1] >= thresh) & (arr[:, :, 2] >= thresh)
    mask = Image.fromarray((near * 255).astype(np.uint8), "L")
    for seed in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        if mask.getpixel(seed) == 255:
            ImageDraw.floodfill(mask, seed, 128, thresh=1)
    m = np.asarray(mask)
    alpha = np.where(m == 128, 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([np.asarray(rgb), alpha]), "RGBA")


def split_points(im, n=6):
    arr = np.asarray(im)
    ink = (arr[:, :, 3] > 8).sum(axis=0).astype(np.float64)
    w = im.width
    cell = w / float(n)
    cuts = [0]
    for i in range(1, n):
        expect = int(round(i * cell))
        lo, hi = max(1, expect - 140), min(w - 1, expect + 140)
        window = ink[lo:hi]
        # 平滑后取最小
        k = 9
        smooth = np.convolve(window, np.ones(k) / k, mode="same")
        cuts.append(lo + int(np.argmin(smooth)))
    cuts.append(w)
    return cuts, ink


for p in sorted(glob.glob(os.path.join(SRC, "ref", "*.jpg"))):
    im = white_to_alpha(Image.open(p))
    cuts, ink = split_points(im)
    print(os.path.basename(p), "cuts=", cuts, "widths=", [cuts[i + 1] - cuts[i] for i in range(6)])
    # 打印每 172px 的墨量，观察分布
    step = 172
    print("   ink:", [int(ink[x:x + step].mean()) for x in range(0, im.width, step)])

# GIF 逐帧大图
for g in sorted(glob.glob(os.path.join(SRC, "move", "*.gif"))):
    im = Image.open(g)
    n = getattr(im, "n_frames", 1)
    frames = []
    for i in range(n):
        im.seek(i)
        fr = im.convert("RGB")
        # 白底转透明后再贴到深灰底上，3 倍放大
        fr = white_to_alpha(fr).resize((fr.width * 3, fr.height * 3), Image.NEAREST)
        frames.append(fr)
    sheet = Image.new("RGBA", (frames[0].width * n, frames[0].height), (32, 32, 36, 255))
    for i, f in enumerate(frames):
        sheet.paste(f, (i * f.width, 0), f)
    out = os.path.join(OUT, "gif_" + os.path.basename(g).replace(".gif", ".png"))
    sheet.convert("RGB").save(out)
    print("saved", out)
