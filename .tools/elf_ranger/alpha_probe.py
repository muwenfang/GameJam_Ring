# -*- coding: utf-8 -*-
"""抠底预览：比较三种 alpha 方案在同一帧上的边缘质量与背景残留。"""
import os
import numpy as np
from PIL import Image

SRC = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
OUT = r"D:\unity\GameJam_Ring\.tools\elf_ranger\probe"

jp = os.path.join(SRC, "ref", "Gemini_Generated_Image_noraf7noraf7nora.jpg")
im = Image.open(jp).convert("RGB")
arr = np.asarray(im).astype(np.int16)
print("corners", arr[0, 0], arr[0, -1], arr[-1, 0], arr[-1, -1])
print("unique-ish sample", arr[500, 0:20:5].tolist())

# 背景统计：四边 30px 内的通道最小值分布
border = np.concatenate([
    arr[:30, :, :].reshape(-1, 3), arr[-30:, :, :].reshape(-1, 3),
    arr[:, :30, :].reshape(-1, 3), arr[:, -30:, :].reshape(-1, 3)])
mn = border.min(axis=1)
print("border min-channel percentiles", np.percentile(mn, [1, 5, 50, 95, 99]).round(1))
print("border max-channel percentiles", np.percentile(border.max(axis=1), [1, 5, 50, 95, 99]).round(1))

m = arr.min(axis=2).astype(np.float32)
for lo, hi in [(222, 242), (215, 235), (205, 228)]:
    a = np.clip((hi - m) / (hi - lo), 0, 1)
    print("ramp %d~%d -> transparent %.1f%%" % (lo, hi, 100.0 * (a < 0.02).mean()))

# 导出对比：原图 / 三种 ramp，都贴深灰底
def compose(alpha):
    rgba = np.dstack([np.asarray(im), (alpha * 255).astype(np.uint8)])
    fg = Image.fromarray(rgba, "RGBA")
    bg = Image.new("RGBA", fg.size, (34, 34, 38, 255))
    bg.alpha_composite(fg)
    return bg

cell = 688
tiles = [Image.open(jp).convert("RGBA").crop((0, 0, cell, im.height)).resize((344, 512), Image.LANCZOS)]
for lo, hi in [(222, 242), (215, 235), (205, 228)]:
    a = np.clip((hi - m) / (hi - lo), 0, 1)
    tiles.append(compose(a).crop((0, 0, cell, im.height)).resize((344, 512), Image.LANCZOS))

sheet = Image.new("RGBA", (344 * len(tiles), 512), (0, 0, 0, 255))
for i, t in enumerate(tiles):
    sheet.paste(t, (i * 344, 0))
sheet.convert("RGB").save(os.path.join(OUT, "alpha_compare.png"))
print("saved alpha_compare.png")
