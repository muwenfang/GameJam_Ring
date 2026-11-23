import os, sys
import cv2
import numpy as np

SRC = r"C:\Users\Muwen\Downloads\素材其一.mp4"
TMP = r"D:\unity\GameJam_Ring\Temp"
OUT = os.path.join(TMP, "build")
os.makedirs(OUT, exist_ok=True)
CACHE = os.path.join(TMP, "bg_stats.npz")

cap = cv2.VideoCapture(SRC)
n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
W = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
H = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
FPS = cap.get(cv2.CAP_PROP_FPS)

d = np.load(CACHE)
bg, bgstd, darkr = d["bg"], d["std"], d["dark"]
bgf = bg.astype(np.float32)

# truly static background ink: always dark AND temporally flat
bg_ink = (darkr.astype(np.float32) > 228) & (bgstd.astype(np.float32) < 12) & (bgf < 230)
bg_ink = cv2.morphologyEx(bg_ink.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((11, 11), np.uint8))
bg_ink = cv2.dilate(bg_ink, np.ones((5, 5), np.uint8)).astype(bool)
print("bg_ink ratio %.3f" % bg_ink.mean())

# fixed decorative zones (bottom-left ink wash + ground strip) -> hard suppression
zone = np.zeros((H, W), np.float32)
zone[385:, :345] = 1.0
zone[600:, :] = 1.0
zone = cv2.GaussianBlur(zone, (0, 0), 14.0)


def fill_holes(a, a_src, thr=0.14, max_area=90000):
    m = (a > thr).astype(np.uint8)
    ff = m.copy()
    h, w = m.shape
    mask2 = np.zeros((h + 2, w + 2), np.uint8)
    for pt in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        cv2.floodFill(ff, mask2, pt, 1)
    holes = (ff == 0) & (m == 0)
    num, lab, stats, _ = cv2.connectedComponentsWithStats(holes.astype(np.uint8), 8)
    out = a.copy()
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] <= max_area:
            out[lab == i] = a_src[lab == i]
    return out


def matte(frame_bgr):
    luma = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY).astype(np.float32)
    a_base = np.power(np.clip((218.0 - luma) / (218.0 - 100.0), 0.0, 1.0), 0.85)

    diff = np.abs(luma - bgf)
    static = diff < 16.0
    supp = cv2.GaussianBlur((static & bg_ink).astype(np.float32), (0, 0), 1.6)
    a = a_base * (1.0 - np.clip(supp * 1.9, 0, 1))

    a = fill_holes(a, a_base)

    # hard suppression inside fixed decorative zones: keep only near-black pixels (character / fx)
    keep_w = np.clip((60.0 - luma) / 25.0, 0.0, 1.0)   # 1 = near-black (character/fx), 0 = grey wash
    a = a * (1.0 - np.clip(zone, 0, 1) * (1.0 - keep_w))

    a = cv2.GaussianBlur(a, (0, 0), 0.6)
    a[a < 0.05] = 0.0

    # keep the character + connected ink effects, drop stray background remnants
    m = (a > 0.28).astype(np.uint8)
    num, lab, stats, _ = cv2.connectedComponentsWithStats(m, 8)
    if num > 1:
        main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        mx, my, mw, mh = stats[main, :4]
        pad = 90
        keep = np.zeros_like(m)
        for i in range(1, num):
            area = stats[i, cv2.CC_STAT_AREA]
            if area < 200:
                continue
            x, y, w_, h_ = stats[i, :4]
            near = (x < mx + mw + pad and x + w_ > mx - pad and
                    y < my + mh + pad and y + h_ > my - pad)
            if near or area > 0.02 * H * W:
                keep[lab == i] = 1
        keep = cv2.dilate(keep, np.ones((5, 5), np.uint8))
        a = a * cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 1.0)

    # bottom strip of the frame holds only decorative ground ink, never the character's feet
    a[636:, :] = 0.0

    rgb = np.dstack([luma, luma, luma])
    solid = (a > 0.05).astype(np.uint8)
    filled = rgb.copy()
    for _ in range(14):
        dd = cv2.dilate(filled, np.ones((3, 3), np.uint8))
        dm = cv2.dilate(solid, np.ones((3, 3), np.uint8))
        filled[(dm > 0) & (solid == 0)] = dd[(dm > 0) & (solid == 0)]
        solid = dm
    return np.dstack([filled, a * 255.0]).astype(np.uint8), a


# key poses covering the whole attack
TIMES = [0.42, 1.29, 2.62, 3.46, 4.33, 4.79, 5.21, 6.08, 6.54, 6.96, 7.83, 9.58]


def checker(size, cell=24):
    h, w = size
    yy, xx = np.mgrid[0:h, 0:w]
    c = (((yy // cell) + (xx // cell)) % 2).astype(np.uint8)
    return np.where(c == 0, 58, 88).astype(np.uint8)


thumbs = []
for k, t in enumerate(TIMES):
    fi = min(n - 1, int(round(t * FPS)))
    cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
    ok, f = cap.read()
    if not ok:
        print("fail", fi)
        continue
    rgba, a = matte(f)
    cv2.imwrite(os.path.join(OUT, "rgba_%02d.png" % (k + 1)), rgba)
    bgr = cv2.cvtColor(f, cv2.COLOR_BGR2GRAY)
    chk = checker((H, W))[:, :, None].repeat(3, axis=2)
    al = a[:, :, None]
    comp = (np.dstack([bgr, bgr, bgr]) * al + chk * (1 - al)).astype(np.uint8)
    cv2.imwrite(os.path.join(OUT, "pv_%02d.png" % (k + 1)), comp)
    th = cv2.resize(comp, (426, 240), interpolation=cv2.INTER_AREA)
    cv2.putText(th, "%02d  %.2fs" % (k + 1, t), (6, 20), cv2.FONT_HERSHEY_SIMPLEX,
                0.5, (255, 255, 255), 1, cv2.LINE_AA)
    thumbs.append(th)
    print("ok %02d t=%.2f f=%d" % (k + 1, t, fi))
cap.release()

cols = 4
rows = (len(thumbs) + cols - 1) // cols
th_, tw_ = thumbs[0].shape[:2]
sheet = np.full((rows * th_, cols * tw_, 3), 20, np.uint8)
for i, t in enumerate(thumbs):
    r, c = divmod(i, cols)
    sheet[r * th_:(r + 1) * th_, c * tw_:(c + 1) * tw_] = t
cv2.imwrite(os.path.join(OUT, "sheet_final.png"), sheet)
print("sheet", sheet.shape)
