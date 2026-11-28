# -*- coding: utf-8 -*-
"""切分与匹配：软阈值抠底 → 列投影切 6 格 → 与 GIF 逐帧比对，输出映射表。"""
import os
import glob
import numpy as np
from PIL import Image

SRC = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
OUT = r"D:\unity\GameJam_Ring\.tools\elf_ranger\probe"
os.makedirs(OUT, exist_ok=True)

LO, HI = 215.0, 235.0


def to_rgba(im):
    rgb = im.convert("RGB")
    arr = np.asarray(rgb).astype(np.float32)
    m = arr.min(axis=2)
    a = np.clip((HI - m) / (HI - LO), 0.0, 1.0) * 255.0
    rgba = np.dstack([arr.astype(np.uint8), a.astype(np.uint8)])
    return Image.fromarray(rgba, "RGBA")


def split6(im):
    arr = np.asarray(im)
    ink = (arr[:, :, 3] > 8).sum(axis=0).astype(np.float64)
    w = im.width
    cell = w / 6.0
    cuts = [0]
    for i in range(1, 6):
        expect = int(round(i * cell))
        lo, hi = max(1, expect - 200), min(w - 1, expect + 200)
        win = ink[lo:hi]
        k = 15
        smooth = np.convolve(win, np.ones(k) / k, mode="same")
        cut = lo + int(np.argmin(smooth))
        cuts.append(cut)
    cuts.append(w)
    return cuts


def norm(im, th=150, canvas=(240, 200)):
    arr = np.asarray(im)
    ys, xs = np.where(arr[:, :, 3] > 8)
    if len(ys) == 0:
        return Image.new("RGBA", canvas, (255, 255, 255, 0))
    top, bot, left, right = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    crop = im.crop((left, top, right, bot))
    s = th / float(bot - top)
    crop = crop.resize((max(1, int(crop.width * s)), th), Image.LANCZOS)
    out = Image.new("RGBA", canvas, (255, 255, 255, 0))
    out.paste(crop, ((canvas[0] - crop.width) // 2, canvas[1] - th), crop)
    return out


def cost(a, b):
    aa = np.asarray(a).astype(np.float32) / 255.0
    bb = np.asarray(b).astype(np.float32) / 255.0
    la = aa[:, :, :3] * aa[:, :, 3:4] + 0.35 * (1 - aa[:, :, 3:4])
    lb = bb[:, :, :3] * bb[:, :, 3:4] + 0.35 * (1 - bb[:, :, 3:4])
    return float(np.abs(la - lb).mean())


strips = {}
for p in sorted(glob.glob(os.path.join(SRC, "ref", "*.jpg"))):
    im = to_rgba(Image.open(p))
    cuts = split6(im)
    print(os.path.basename(p), "cuts", cuts, "widths", [cuts[i + 1] - cuts[i] for i in range(6)])
    strips[os.path.basename(p)] = [norm(im.crop((cuts[i], 0, cuts[i + 1], im.height))) for i in range(6)]

gifs = {}
for p in sorted(glob.glob(os.path.join(SRC, "move", "*.gif"))):
    im = Image.open(p)
    n = getattr(im, "n_frames", 1)
    fr = []
    for i in range(n):
        im.seek(i)
        fr.append(norm(to_rgba(im.convert("RGB"))))
    gifs[os.path.basename(p)] = fr

print()
head = "%-20s" % "GIF\\STRIP"
for s in strips:
    head += "%-14s" % s[-16:-8]
print(head)
rows = []
for g in gifs:
    line = "%-20s" % g.replace(".gif", "")
    best = None
    for s in strips:
        figs = strips[s]
        c = float(np.mean([cost(gifs[g][i], figs[i]) for i in range(min(6, len(figs)))]))
        line += "%-14.4f" % c
        if best is None or c < best[1]:
            best = (s, c)
    print(line, " -> best:", best[0][-12:-4], "%.4f" % best[1])
    rows.append((g, best[0], best[1]))

# 导出配对大图：上排 GIF，下排对应条带
for g, s, c in rows:
    figs = strips[s]
    W, H = 240, 200
    sheet = Image.new("RGBA", (W * 6, H * 2), (32, 32, 36, 255))
    for i in range(6):
        sheet.paste(gifs[g][i], (i * W, 0), gifs[g][i])
        sheet.paste(figs[i], (i * W, H), figs[i])
    sheet.convert("RGB").save(os.path.join(OUT, "pair_" + g.replace(".gif", "") + ".png"))
print("pairs saved")
