"""v9 = v8 + manual blade axis for frame 05 (the only frame whose blade edge has no
visible upper boundary at all) + tighter stray-component filter + softer face/hand."""
import os
import cv2
import numpy as np

SRC = r"C:\Users\Muwen\Downloads\素材其一.mp4"
TMP = r"D:\unity\GameJam_Ring\Temp"
OUT = os.path.join(TMP, "v10")
os.makedirs(OUT, exist_ok=True)

d = np.load(os.path.join(TMP, "bg_stats.npz"))
bg, bgstd, darkr = d["bg"], d["std"], d["dark"]
bgf = bg.astype(np.float32)
H, W = bg.shape

bg_ink = (darkr.astype(np.float32) > 228) & (bgstd.astype(np.float32) < 12) & (bgf < 230)
bg_ink = cv2.morphologyEx(bg_ink.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((11, 11), np.uint8))
bg_ink = cv2.dilate(bg_ink, np.ones((5, 5), np.uint8)).astype(bool)

zone = np.zeros((H, W), np.float32)
zone[385:, :345] = 1.0
zone[600:, :] = 1.0
zone = cv2.GaussianBlur(zone, (0, 0), 14.0)

# frame index (1-based) -> manual blade axis segments (tip ... root) in 1280x720 px
MANUAL_BLADE = {
    5: [((132, 268), (340, 318)), ((340, 318), (524, 346))],
}


def blade_band_fill(gray, idx):
    e = cv2.Canny(gray, 16, 52)
    lines = cv2.HoughLinesP(e, 1, np.pi / 180, threshold=50, minLineLength=150, maxLineGap=5)
    band = np.zeros(gray.shape, np.uint8)
    if lines is not None:
        for l in lines:
            x1, y1, x2, y2 = [int(v) for v in l[0]]
            if max(y1, y2) > 560:
                continue
            cv2.line(band, (x1, y1), (x2, y2), 255, 34)
    band = cv2.morphologyEx(band, cv2.MORPH_CLOSE,
                            cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (41, 41)))
    e_in = cv2.dilate(cv2.bitwise_and(e, band), np.ones((3, 3), np.uint8))
    filled = cv2.morphologyEx(e_in, cv2.MORPH_CLOSE,
                              cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (21, 21)))
    filled = cv2.bitwise_and(filled, band)
    filled = cv2.erode(filled, np.ones((3, 3), np.uint8))
    filled = cv2.bitwise_and(filled, band)

    if idx in MANUAL_BLADE:
        solid = np.zeros(gray.shape, np.uint8)
        for a, b in MANUAL_BLADE[idx]:
            cv2.line(solid, a, b, 255, 20)
        filled = cv2.bitwise_or(filled, solid)
    return band, filled


def hand_fill(gray, sleeve):
    paper = ((gray > 214) & (gray < 240)).astype(np.uint8)
    num, lab, stats, _ = cv2.connectedComponentsWithStats(paper, 8)
    sleeve_d = cv2.dilate(sleeve, np.ones((3, 3), np.uint8))
    out = np.zeros(gray.shape, np.uint8)
    for i in range(1, num):
        area = stats[i, cv2.CC_STAT_AREA]
        if area < 350 or area > 5200:
            continue
        x, y, w, h = stats[i, :4]
        if y + h > 570:
            continue
        region = (lab == i)
        if not (region & (sleeve_d > 0)).any():
            continue
        out[region] = 255
    return out


def matte(frame_bgr, idx):
    luma = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
    lf = luma.astype(np.float32)
    a_base = np.power(np.clip((218.0 - lf) / (218.0 - 100.0), 0.0, 1.0), 0.85)

    sleeve = ((a_base > 0.22) & (a_base < 0.80)).astype(np.uint8) * 255
    band, filled = blade_band_fill(luma, idx)
    hf = hand_fill(luma, sleeve)

    a = np.maximum(a_base, cv2.GaussianBlur((filled > 0).astype(np.float32), (0, 0), 0.9))
    a = np.maximum(a, cv2.GaussianBlur((hf > 0).astype(np.float32), (0, 0), 1.4) * 0.55)

    diff = np.abs(lf - bgf)
    supp = cv2.GaussianBlur(((diff < 16.0) & bg_ink).astype(np.float32), (0, 0), 1.6)
    a = a * (1.0 - np.clip(supp * 1.9, 0, 1))

    keep_w = np.clip((45.0 - lf) / 18.0, 0.0, 1.0)
    a = a * (1.0 - np.clip(zone, 0, 1) * (1.0 - keep_w))

    a = cv2.GaussianBlur(a, (0, 0), 0.6)
    a[a < 0.06] = 0.0

    m = (a > 0.30).astype(np.uint8)
    num, lab, stats, _ = cv2.connectedComponentsWithStats(m, 8)
    if num > 1:
        main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        mx, my, mw, mh = stats[main, :4]
        pad = 40
        keep = np.zeros_like(m)
        for i in range(1, num):
            area = stats[i, cv2.CC_STAT_AREA]
            if area < 320:
                continue
            x, y, w_, h_ = stats[i, :4]
            if (x < mx + mw + pad and x + w_ > mx - pad and
                    y < my + mh + pad and y + h_ > my - pad) or area > 0.02 * H * W:
                keep[lab == i] = 1
        keep = cv2.dilate(keep, np.ones((5, 5), np.uint8))
        a = a * cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 1.0)

    a[636:, :] = 0.0

    rgb = np.dstack([lf, lf, lf])
    solid = (a > 0.60).astype(np.uint8)
    res = rgb.copy()
    for _ in range(14):
        dd = cv2.dilate(res, np.ones((3, 3), np.uint8))
        dm = cv2.dilate(solid, np.ones((3, 3), np.uint8))
        res[(dm > 0) & (solid == 0)] = dd[(dm > 0) & (solid == 0)]
        solid = dm
    return np.dstack([res, a * 255.0]).astype(np.uint8), a


TIMES = [0.42, 1.29, 2.62, 3.46, 4.33, 4.79, 5.21, 6.08, 6.54, 6.96, 7.83, 9.58]
cap = cv2.VideoCapture(SRC)
n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
FPS = cap.get(cv2.CAP_PROP_FPS)

pairs = []
for k, t in enumerate(TIMES):
    cap.set(cv2.CAP_PROP_POS_FRAMES, min(n - 1, int(round(t * FPS))))
    _, f = cap.read()
    rgba, a = matte(f, k + 1)
    cv2.imwrite(os.path.join(OUT, "rgba_%02d.png" % (k + 1)), rgba)
    g = cv2.cvtColor(f, cv2.COLOR_BGR2GRAY)
    al = a[:, :, None]
    comp = np.clip(np.dstack([g] * 3) * al + 40.0 * (1 - al), 0, 255).astype(np.uint8)
    pair = np.hstack([cv2.cvtColor(g, cv2.COLOR_GRAY2BGR), comp])
    cv2.putText(pair, "%02d" % (k + 1), (8, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 0, 255), 2, cv2.LINE_AA)
    pairs.append(pair)
    print("ok %02d" % (k + 1))
cap.release()

for gi in range(0, 12, 3):
    sheet = cv2.resize(np.vstack(pairs[gi:gi + 3]), None, fx=0.62, fy=0.62, interpolation=cv2.INTER_AREA)
    cv2.imwrite(os.path.join(OUT, "cmp_%d.png" % (gi // 3 + 1)), sheet)
print("done")
