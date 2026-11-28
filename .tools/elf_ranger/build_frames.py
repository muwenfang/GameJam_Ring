# -*- coding: utf-8 -*-
"""精灵游侠：把原图转成 Unity 可用的透明序列帧。

统一口径（与既有三套角色一致，便于同一组件与剪辑复用）：
- 角色帧：800x800 画布，脚底基线 y=799，脚底中点 x=400，人物身高 583px（PPU 292 → 2.0 世界单位）
- 荆棘：876x292 原画布（3x1 格），地面贴底边
- 光环：对齐角色画布（800x800），可与角色帧直接叠加
- 箭矢：644x210 原画布，箭尖固定在 x=626（代码按 pivot 处理）

抠底：以通道最小值做软阈值（215~235），既清掉白色背景，又保留弓弦等浅色细节。
"""
import os
import shutil
import numpy as np
from PIL import Image

SRC = r"D:\unity\GameJam_Ring\.tools\elf_ranger\src"
DST = r"D:\unity\GameJam_Ring\Assets\Resources\ElfRanger"
PROBE = r"D:\unity\GameJam_Ring\.tools\elf_ranger\probe"

CANVAS = (800, 800)
BASELINE = 799
CENTER_X = 400
CHAR_H = 583

os.makedirs(DST, exist_ok=True)
os.makedirs(PROBE, exist_ok=True)

LO, HI = 215.0, 235.0


def to_rgba(im, saturate=1.0):
    rgb = im.convert("RGB")
    arr = np.asarray(rgb).astype(np.float32)
    m = arr.min(axis=2)
    a = np.clip((HI - m) / (HI - LO), 0.0, 1.0)
    # 对白去污染：P = a*C + (1-a)*白  →  C = (P - (1-a)*255) / a
    # 否则抗锯齿边缘与淡绿光晕会保留大量白色，在暗色地图上呈白边 / 白块。
    alpha = np.clip(a, 0.0, 1.0)[:, :, None]
    safe = np.maximum(alpha, 1e-3)
    color = (arr - (1.0 - alpha) * 255.0) / safe
    color = np.where(alpha > 0.02, color, arr)
    color = np.clip(color, 0.0, 255.0)

    if abs(saturate - 1.0) > 1e-6:
        # 半透明像素多是叠在白底上的淡绿光晕，去白后仍偏白；
        # 这些效果层按需提饱和度，暗底上才读得出「绿色光环」。
        flat = color.reshape(-1, 3) / 255.0
        mx = flat.max(axis=1)
        mn = flat.min(axis=1)
        s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
        ns = np.clip(s * saturate, 0.0, 1.0)
        scale = np.where(s > 1e-4, ns / np.maximum(s, 1e-4), 1.0)
        mid = (mx + mn) / 2.0
        flat = mid[:, None] + (flat - mid[:, None]) * scale[:, None]
        color = np.clip(flat, 0, 1).reshape(color.shape) * 255.0
        color = np.where(alpha > 0.97, arr, color)

    rgba = np.dstack([color.astype(np.uint8), (alpha[:, :, 0] * 255.0).astype(np.uint8)])
    return bleed(Image.fromarray(rgba, "RGBA"))


def green_glow(im, tint=(0.52, 0.86, 0.60), alpha_scale=0.85,
               min_channel=165, max_sat=0.32):
    """把「近白的淡绿光晕」压成明确绿色。

    源素材是白底稿，光晕本身只有极淡的绿（实测约 209/249/216），
    叠到暗色地图上就是一坨白。这里只对「透明度介于 20~238 + 通道最小值够亮 +
    饱和度够低」的像素做乘色，角色实心区域（alpha≈255 且带色）不受影响。
    """
    arr = np.asarray(im).astype(np.float32)
    rgb = arr[:, :, :3].copy()
    alpha = arr[:, :, 3].copy()
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    mask = (alpha > 20) & (alpha < 238) & (mn > min_channel) & (sat < max_sat)
    rgb[mask] = rgb[mask] * np.array(tint, dtype=np.float32)
    alpha[mask] = alpha[mask] * alpha_scale

    # 光晕核心在源稿里是**不透明**的近白淡绿（实测 205/231/208，alpha≈255）：
    # 白底上看着"白得发亮"，压到暗底就是一圈白描边。用「绿通道为最大且亮度高」
    # 把它与角色自身的米色布料（R 最大）区分开，再做更重的乘色。
    g_max = (rgb[:, :, 1] >= rgb[:, :, 0]) & (rgb[:, :, 1] >= rgb[:, :, 2])
    bright = mx > 175
    core = (alpha >= 238) & g_max & bright & ((mx - mn) > 10)
    rgb[core] = rgb[core] * np.array((0.44, 0.80, 0.52), dtype=np.float32)
    out = np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def bleed(im, iterations=6):
    """把边缘颜色向透明区外扩若干像素。

    透明像素的 RGB 原本是白色，双线性采样时会被混进边缘，形成白边。
    这里用「不透明邻居的中位色」向外填充，采样时就不会再拉进白色。
    """
    arr = np.asarray(im).astype(np.float32)
    rgb, alpha = arr[:, :, :3], arr[:, :, 3]
    known = alpha > 8
    for _ in range(iterations):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(alpha.shape, dtype=np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            k = np.roll(known, (dy, dx), axis=(0, 1))
            c = np.roll(rgb, (dy, dx), axis=(0, 1))
            acc += c * k[:, :, None]
            cnt += k
        fill = (~known) & (cnt > 0)
        rgb[fill] = acc[fill] / cnt[fill][:, None]
        known = known | fill
    out = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), alpha.astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def bbox(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 8)
    if len(ys) == 0:
        return None
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def feet_center_x(im, box, band=0.12):
    a = np.asarray(im)[:, :, 3]
    x0, y0, x1, y1 = box
    top = int(y1 - max(2, (y1 - y0) * band))
    sub = a[top:y1, x0:x1] > 8
    xs = np.where(sub.any(axis=0))[0]
    if len(xs) == 0:
        return (x0 + x1) / 2.0
    return x0 + (xs.min() + xs.max()) / 2.0


def place(im, scale=1.0, anchor_feet=True):
    """按脚底中点与基线摆放到 800x800 画布。"""
    out = Image.new("RGBA", CANVAS, (255, 255, 255, 0))
    box = bbox(im)
    if box is None:
        return out
    x0, y0, x1, y1 = box
    crop = im.crop(box)
    if abs(scale - 1.0) > 1e-6:
        crop = crop.resize((max(1, int(round(crop.width * scale))),
                            max(1, int(round(crop.height * scale)))), Image.LANCZOS)
    if anchor_feet:
        cx = feet_center_x(im, box) - x0
        dx = CENTER_X - cx * scale
        dy = BASELINE + 1 - crop.height
    else:
        dx = (CANVAS[0] - crop.width) // 2
        dy = (CANVAS[1] - crop.height) // 2
    out.paste(crop, (int(round(dx)), int(round(dy))), crop)
    return out


def save(im, name):
    p = os.path.join(DST, name)
    im.save(p, "PNG", optimize=True)
    return p


report = []


def record(action, path):
    im = Image.open(path).convert("RGBA")
    b = bbox(im)
    h = b[3] - b[1]
    feet = b[3] - 1
    report.append((action, os.path.basename(path), im.width, im.height, h, BASELINE - feet,
                   round(feet_center_x(im, b), 1)))


# ---------- 1) 已有单帧（627 / 800 画布），仅抠底并搬到统一画布 ----------
SIMPLE = [
    ("Idle", "idle", "idle", 6, (627, 627), 92, 175, 1.0),
    ("Attack", "attack", "single", 10, (800, 800), 0, 0, 1.0),
    ("Heavy", "heavy", "heavy", 12, (800, 800), 0, 0, 1.0),
    ("Heal", "heal", "heal", 12, (627, 627), 92, 175, 2.4),
]
for action, folder, prefix, count, native, dx, dy, sat in SIMPLE:
    for i in range(1, count + 1):
        src = os.path.join(SRC, folder, "%s_%02d.png" % (prefix, i))
        if not os.path.exists(src):
            raise SystemExit("missing " + src)
        im = to_rgba(Image.open(src), sat)
        if action == "Heal":
            im = green_glow(im)
        if dx or dy:
            out = Image.new("RGBA", CANVAS, (255, 255, 255, 0))
            out.paste(im, (dx, dy), im)
        else:
            out = im
        name = "ElfRanger_%s_%02d.png" % (action, i - 1)
        record(action, save(out, name))

# ---------- 2) 条带切分的四个移动动作 ----------
# (动作, 源条带, 帧率无关的缩放基准：条带内最高帧 = 583px)
STRIPS = [
    ("Run", "Gemini_Generated_Image_ehudlyehudlyehud.jpg"),
    ("Jump", "Gemini_Generated_Image_ehudlyehudlyehud (1).jpg"),
    ("DodgeFwd", "Gemini_Generated_Image_b13b0jb13b0jb13b.jpg"),
    ("DodgeBack", "Gemini_Generated_Image_noraf7noraf7nora.jpg"),
]


def split6(im):
    arr = np.asarray(im)
    ink = (arr[:, :, 3] > 8).sum(axis=0).astype(np.float64)
    cell = im.width / 6.0
    cuts = [0]
    for i in range(1, 6):
        expect = int(round(i * cell))
        lo, hi = max(1, expect - 200), min(im.width - 1, expect + 200)
        smooth = np.convolve(ink[lo:hi], np.ones(15) / 15, mode="same")
        cuts.append(lo + int(np.argmin(smooth)))
    cuts.append(im.width)
    return cuts


for action, strip in STRIPS:
    im = to_rgba(Image.open(os.path.join(SRC, "ref", strip)))
    cuts = split6(im)
    figs = []
    for i in range(6):
        f = im.crop((cuts[i], 0, cuts[i + 1], im.height))
        b = bbox(f)
        figs.append(f.crop(b))
    tallest = max(f.height for f in figs)
    scale = CHAR_H / float(tallest)
    print("%-9s strip=%s tallest=%d scale=%.4f" % (action, strip[-12:-4], tallest, scale))
    for i, f in enumerate(figs):
        name = "ElfRanger_%s_%02d.png" % (action, i)
        record(action, save(place(f, scale), name))

# ---------- 3) 荆棘（876x292 原画布，地面贴底边） ----------
for group, count in [("BrambleGrow", 8), ("BrambleIdle", 6), ("BrambleFade", 3)]:
    key = {"BrambleGrow": "bush_grow", "BrambleIdle": "bush_idle", "BrambleFade": "bush_fade"}[group]
    for i in range(1, count + 1):
        im = to_rgba(Image.open(os.path.join(SRC, "bramble", "%s_%02d.png" % (key, i))), 2.0)
        save(im, "ElfRanger_%s_%02d.png" % (group, i - 1))

# ---------- 4) 光环：对齐角色画布 ----------
for i in range(1, 9):
    im = to_rgba(Image.open(os.path.join(SRC, "halo", "halo_%02d.png" % i)), 2.4)
    im = green_glow(im)
    out = Image.new("RGBA", CANVAS, (255, 255, 255, 0))
    out.paste(im, (92, 175), im)
    record("Halo", save(out, "ElfRanger_Halo_%02d.png" % (i - 1)))

# ---------- 5) 箭矢（644x210 原画布，箭尖固定 x=626） ----------
for i in range(1, 13):
    im = to_rgba(Image.open(os.path.join(SRC, "arrow", "pflow_%02d.png" % i)))
    save(im, "ElfRanger_Arrow_%02d.png" % (i - 1))

# ---------- 报告 ----------
print()
print("%-12s %-28s %5s %5s %5s %5s %6s" % ("action", "file", "w", "h", "charH", "base", "feetX"))
for r in report:
    print("%-12s %-28s %5d %5d %5d %5d %6s" % r)
print()
print("输出文件数:", len(os.listdir(DST)))
