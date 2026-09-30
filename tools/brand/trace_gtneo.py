"""
Traces the GT Neo for the settings page's wheel drawing (Ui/WheelView.cs), like assets/fxpro-outline.svg for the FX Pro.

Reference: SimPro 3's front picture of the wheel (product 0000000002060000), served by SimPro's local web UI at
http://127.0.0.1:4010/simpro/assets/wheel_0000000002060000.<hash>.png (500 x 334, RGBA). The picture isn't kept in
this repo. Its LEDs are transparent cut-outs, so each LED is a hole in the alpha mask: the rev slats, the ring segments
and the round buttons are measured, not guessed.

Writes assets/gtneo-outline.svg (outline + the grip openings, viewBox 663 x 396) and prints the LED positions in that
coordinate system for WheelView (paste into NeoRevs / NeoButtons / NeoRings).

    python tools/brand/trace_gtneo.py <reference.png> [preview.png]
"""
import sys
import cv2
import numpy as np

W, H = 663, 396

img = cv2.imread(sys.argv[1], cv2.IMREAD_UNCHANGED)
solid = (img[:, :, 3] > 128).astype(np.uint8)
ys, xs = np.nonzero(solid)
x0, y0, x1, y1 = xs.min(), ys.min(), xs.max(), ys.max()
scale = min((W - 24) / (x1 - x0 + 1), (H - 24) / (y1 - y0 + 1))
ox = (W - (x1 - x0 + 1) * scale) / 2 - x0 * scale
oy = (H - (y1 - y0 + 1) * scale) / 2 - y0 * scale


def T(x, y):
    return (x * scale + ox, y * scale + oy)


# ---- outline: the outer edge and the big openings in the grips (not the LED cut-outs, drawn as LEDs)
# traced on a 4x smoothed mask, so the edge is a curve rather than the picture's pixel steps
UP = 4
smooth = cv2.GaussianBlur(cv2.resize(img[:, :, 3], None, fx=UP, fy=UP, interpolation=cv2.INTER_CUBIC), (0, 0), UP * 0.9)
big = (smooth > 128).astype(np.uint8)
contours, hier = cv2.findContours(big, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
paths = []
for i, c in enumerate(contours):
    outer = hier[0][i][3] < 0
    area = cv2.contourArea(c)
    if (outer and area > 5000 * UP * UP) or (not outer and area > 800 * UP * UP):
        c = cv2.approxPolyDP(c, 0.35 * UP, True)
        pts = [T((p[0][0] + 0.5) / UP, (p[0][1] + 0.5) / UP) for p in c]
        paths.append("M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts) + " Z")
svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 663 396">\n'
       '  <!-- traced from SimPro\'s GT Neo front picture by tools/brand/trace_gtneo.py -->\n'
       '  <g fill="none" stroke="currentColor" stroke-width="8" stroke-linejoin="round" fill-rule="evenodd">\n'
       f'    <path d="{" ".join(paths)}"/>\n  </g>\n</svg>\n')
open("assets/gtneo-outline.svg", "w", encoding="utf-8").write(svg)
print(f"outline: {len(paths)} paths, scale {scale:.4f}, offset {ox:.1f},{oy:.1f}")

# ---- LED cut-outs: holes in the mask that aren't the outside
inv = (1 - solid).astype(np.uint8)
ff = inv.copy()
cv2.floodFill(ff, np.zeros((inv.shape[0] + 2, inv.shape[1] + 2), np.uint8), (0, 0), 2)
holes = (ff == 1).astype(np.uint8)
n, lab, st, cen = cv2.connectedComponentsWithStats(holes, 8)
blobs = [(cen[i][0], cen[i][1], st[i][2], st[i][3], st[i][4]) for i in range(1, n) if st[i][4] >= 8]

revs = sorted([b for b in blobs if b[4] < 40 and abs(b[1] - 97.5) < 2], key=lambda b: b[0])
buttons = [b for b in blobs if 200 < b[4] < 400]
rings = [b for b in blobs if b[4] < 40 and abs(b[1] - 97.5) >= 2]
assert len(revs) == 15, len(revs)
assert len(buttons) == 10, len(buttons)
assert len(rings) == 48, len(rings)

f = lambda v: f"{v:.1f}"
print("rev slats (left to right), canvas x at y =", f(T(0, np.mean([b[1] for b in revs]))[1]),
      "size", f(np.mean([b[2] for b in revs]) * scale), "x", f(np.mean([b[3] for b in revs]) * scale))
print("  " + ", ".join(f(T(b[0], b[1])[0]) for b in revs))

# buttons: left side top to bottom, then right side top to bottom
left = sorted([b for b in buttons if b[0] < 250], key=lambda b: b[1])
right = sorted([b for b in buttons if b[0] >= 250], key=lambda b: b[1])
print("buttons (left top-bottom, right top-bottom), radius", f(np.mean([b[2] for b in buttons]) / 2 * scale))
print("  " + ", ".join("({}, {})".format(*map(f, T(b[0], b[1]))) for b in left + right))

# rings: four clusters (upper-left, upper-right, lower-left, lower-right); each LED's angle from its centre
cl = {}
for b in rings:
    key = (b[0] >= 250, b[1] >= 180)
    cl.setdefault(key, []).append(b)
print("rings (UL, UR, LL, LR): centre, radius, segment size")
for key in [(False, False), (True, False), (False, True), (True, True)]:
    pts = cl[key]
    assert len(pts) == 12, (key, len(pts))
    cx, cy = np.mean([p[0] for p in pts]), np.mean([p[1] for p in pts])
    r = np.mean([np.hypot(p[0] - cx, p[1] - cy) for p in pts])
    X, Y = T(cx, cy)
    print(f"  ({f(X)}, {f(Y)}), r {f(r * scale)}")

if len(sys.argv) > 2:
    prev = np.full((H, W, 3), 30, np.uint8)
    for p in paths:
        for sub in p.split(" Z")[:-1]:
            pts = np.array([[float(v) for v in q.strip("ML ").split(",")] for q in sub.strip().split(" L")], np.int32)
            cv2.polylines(prev, [pts], True, (255, 255, 255), 1)
    for b in blobs:
        X, Y = T(b[0], b[1])
        cv2.circle(prev, (int(X), int(Y)), 3, (0, 0, 255), -1)
    cv2.imwrite(sys.argv[2], prev)
