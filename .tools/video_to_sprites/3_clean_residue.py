import os, glob
import cv2
import numpy as np

D = r"D:\unity\GameJam_Ring\Assets\Art\Characters\Swordsman\Attack"
files = sorted(glob.glob(os.path.join(D, "*.png")))

for f in files:
    im = cv2.imread(f, cv2.IMREAD_UNCHANGED)
    a = im[:, :, 3]
    soft = (a > 20).astype(np.uint8)
    num, lab, stats, _ = cv2.connectedComponentsWithStats(soft, 8)
    if num <= 1:
        continue
    main = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    kill = np.zeros_like(soft)
    for i in range(1, num):
        if i == main:
            continue
        area = stats[i, cv2.CC_STAT_AREA]
        x, y, w, h = stats[i, :4]
        cx, cy = x + w // 2, y + h // 2
        core = int(((a > 100) & (lab == i)).sum())     # real character/fx has solid dark core
        if cx < 470 and cy > 380 and area < 900 and core < 40:
            kill[lab == i] = 1
    if kill.any():
        kill = cv2.dilate(kill, np.ones((3, 3), np.uint8)).astype(bool)
        im[:, :, 3][kill] = 0
        cv2.imwrite(f, im)
        print(os.path.basename(f), "removed", int(kill.sum()))
print("done")
