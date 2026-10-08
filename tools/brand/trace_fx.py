"""
Traces the Simagic FX for the settings page's wheel drawing (Ui/WheelView.cs), like trace_gtneo.py for the GT Neo.

Reference: SimPro 3's front picture of the FX (product 0000000002020000), served by SimPro's local web UI at
http://127.0.0.1:4010/simpro/assets/wheel_0000000002020000.<hash>.png (1500 x 833, RGBA). The picture isn't kept in
this repo. Unlike the GT Neo's it's a rendered photo with no LED cut-outs, so the outline comes from its alpha mask and
the controls are found as circles (Hough) and checked against the list below. The rev lights aren't in the picture:
they sit in a row of five under the SIMAGIC badge (the maintainer, 2026-10-07), placed by hand.

Which LED is where comes from an FX owner's wheel test (2026-10-07):
0 lowest left, 1 the one above it, 2 next one in, 3 mid left, 4 top left (outer), 5 top left second in, 6 top right
second in, 7 top right (outer), 8 mid right, 9 next one in on the right, 10 next one down, 11 bottom right; groups 12
left dial, 13 right dial, 14 middle dial; rev 15-19 left to right.

Writes assets/fx-outline.svg (the body with its finger windows, and the paddles behind them, viewBox 663 x 396) and prints the LED positions in that
coordinate system for WheelView (paste into FxButtons / FxDials / FxRevs).

    python tools/brand/trace_fx.py <reference.png> [preview.png]
"""
import sys
import cv2
import numpy as np

W, H = 663, 396

img = cv2.imread(sys.argv[1], cv2.IMREAD_UNCHANGED)
assert img is not None and img.shape[:2] == (833, 1500), "expected SimPro's 1500 x 833 FX picture"
solid = (img[:, :, 3] > 128).astype(np.uint8)
ys, xs = np.nonzero(solid)
x0, y0, x1, y1 = xs.min(), ys.min(), xs.max(), ys.max()
scale = min((W - 24) / (x1 - x0 + 1), (H - 24) / (y1 - y0 + 1))
ox = (W - (x1 - x0 + 1) * scale) / 2 - x0 * scale
oy = (H - (y1 - y0 + 1) * scale) / 2 - y0 * scale


def T(x, y):
    return (x * scale + ox, y * scale + oy)


# ---- outline: the body with its finger windows, and the paddles seen through them.
# The picture's inner holes are of three kinds: the see-through parts of the windows between each grip and the plate (on
# the sides, not round), the button caps (12 round ones) and, in the middle, the dial plates and the badge's letters
# (WheelView draws the buttons, dials and badge itself, so those are filled). The carbon paddles behind the wheel fill
# most of each window and are opaque in the picture, so a window is the see-through part closed over the paddle
# (a 60 px closing, together with the outside where the lower window opens downwards, on the two side bands only).
solid = (img[:, :, 3] > 128).astype(np.uint8)
cs, hr = cv2.findContours(solid, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
see = (1 - solid).astype(np.uint8)
for i, c in enumerate(cs):
    if hr[0][i][3] < 0:
        continue
    area, per, m = cv2.contourArea(c), cv2.arcLength(c, True), cv2.moments(c)
    cx = m["m10"] / m["m00"] if m["m00"] else 750
    window_part = (cx < 450 or cx > 1050) and area >= 800 and 4 * np.pi * area / max(1, per) ** 2 < 0.7
    if not window_part:
        cv2.drawContours(see, [c], -1, 0, -1)  # a button cap, dial plate or letter: part of the body here
# upper windows: the see-through part around each upper paddle, closed over the paddle (they're closed all round)
upper = np.zeros_like(solid)
upper[150:440, :450] = 1
upper[150:440, 1050:] = 1
# (the window's own see-through holes only, never the outside, so the closing can't eat into the grips)
outside = np.zeros((solid.shape[0] + 2, solid.shape[1] + 2), np.uint8)
inner = (1 - solid).copy()
cv2.floodFill(inner, outside, (0, 0), 0)
closed = cv2.morphologyEx(see * inner * upper, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (161, 161)))
wc, _ = cv2.findContours(((closed > 0) & (upper > 0)).astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
windows = np.zeros_like(solid)
cv2.drawContours(windows, [c for c in wc if cv2.contourArea(c) > 1500], -1, 1, -1)
# lower windows: open downwards to the outside, between the grip and the plate's edge, with the lower paddle and its
# bracket in them; traced by hand on the picture (picture pixels): the slit above the paddle, round the paddle and
# bracket, down to where the gap meets the outside
LOWER = [
    [(160, 512), (168, 497), (185, 492), (205, 493), (225, 496), (240, 503), (250, 500), (253, 488), (262, 492), (268, 506),
     (305, 566), (305, 583), (290, 592), (272, 610), (262, 640), (260, 665), (250, 676), (212, 676), (200, 662), (185, 632),
     (168, 605), (160, 575)],
    [(1336, 512), (1328, 498), (1310, 493), (1290, 493), (1270, 497), (1255, 503), (1245, 500), (1240, 488), (1232, 492),
     (1228, 506), (1186, 512), (1182, 578), (1196, 590), (1220, 612), (1233, 640), (1238, 665), (1250, 676), (1292, 676),
     (1300, 660), (1315, 630), (1328, 605), (1336, 575)],
]
for poly in LOWER:
    cv2.fillPoly(windows, [np.array(poly, np.int32)], 1)
body = ((1 - see) & (1 - windows)).astype(np.uint8)          # the picture's body, holes filled, windows cut out
paddles = (solid & windows).astype(np.uint8)                   # what of the paddles shows through the windows

UP = 2


def trace(mask, min_area, holes):
    up = cv2.GaussianBlur(cv2.resize(mask * 255, None, fx=UP, fy=UP, interpolation=cv2.INTER_CUBIC), (0, 0), UP * 1.2)
    cont, hier = cv2.findContours((up > 128).astype(np.uint8), cv2.RETR_CCOMP if holes else cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    out = []
    for c in cont:
        if cv2.contourArea(c) < min_area * UP * UP:
            continue
        c = cv2.approxPolyDP(c, 0.8 * UP, True)
        pts = [T((p[0][0] + 0.5) / UP, (p[0][1] + 0.5) / UP) for p in c]
        out.append("M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts) + " Z")
    return out


paths = trace(body, 1500, holes=True)
paddle_paths = trace(paddles, 400, holes=False)
svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 663 396">\n'
       '  <!-- traced from SimPro\'s FX front picture by tools/brand/trace_fx.py: the body with its finger windows, then the\n'
       '       paddles seen through them (WheelView draws them behind the body) -->\n'
       '  <g fill="none" stroke="currentColor" stroke-width="8" stroke-linejoin="round" fill-rule="evenodd">\n'
       f'    <path id="body" d="{" ".join(paths)}"/>\n'
       f'    <path id="paddles" d="{" ".join(paddle_paths)}"/>\n  </g>\n</svg>\n')
open("assets/fx-outline.svg", "w", encoding="utf-8").write(svg)
print(f"outline: {len(paths)} body paths (outer edge + {len(paths) - 1} windows), {len(paddle_paths)} paddles, scale {scale:.4f}, offset {ox:.1f},{oy:.1f}")

# ---- controls: circles in the picture, matched to where each one was expected (picture pixels)
EXPECTED = {
    # button LEDs 0-11 (see the docstring)
    0: (437, 697), 1: (397, 607), 2: (417, 511), 3: (329, 449), 4: (207, 79), 5: (356, 110),
    6: (1141, 110), 7: (1289, 79), 8: (1167, 449), 9: (1077, 512), 10: (1095, 605), 11: (1055, 701),
    # dials 12 left, 13 right, 14 middle
    12: (584, 608), 13: (908, 607), 14: (748, 499),
}
gray = cv2.medianBlur(cv2.cvtColor(img[:, :, :3], cv2.COLOR_BGR2GRAY), 5)
found = cv2.HoughCircles(gray, cv2.HOUGH_GRADIENT, dp=1.2, minDist=45, param1=90, param2=40, minRadius=22, maxRadius=60)[0]
pos = {}
for led, (ex, ey) in EXPECTED.items():
    d = [np.hypot(c[0] - ex, c[1] - ey) for c in found]
    k = int(np.argmin(d))
    assert d[k] < 25, f"LED {led}: no circle near {ex},{ey} (closest {d[k]:.0f} px)"
    pos[led] = (found[k][0], found[k][1])

f = lambda v: f"{v:.1f}"
btn = [T(*pos[i]) for i in range(12)]
print("FxButtons (LEDs 0-11):")
print("  " + ", ".join(f"({f(x)}, {f(y)})" for x, y in btn))
print("FxDials (LEDs 12 left, 13 right, 14 middle):")
print("  " + ", ".join(f"({f(x)}, {f(y)})" for x, y in (T(*pos[i]) for i in (12, 13, 14))))

# ---- rev lights: five under the badge, centred on the badge (its bright letters, picture rows 255-320)
badge = (cv2.cvtColor(img[:, :, :3], cv2.COLOR_BGR2GRAY)[255:320, 560:940] > 150).astype(np.uint8)
bys, bxs = np.nonzero(badge)
bl, br, bb = 560 + bxs.min(), 560 + bxs.max(), 255 + bys.max()
cx, w = (bl + br) / 2, (br - bl) * 0.62
revs = [T(cx - w / 2 + i * w / 4, bb + 26) for i in range(5)]
print(f"FxRevs (LEDs 15-19, badge x {bl}-{br}, bottom {bb}): y = {f(revs[0][1])}")
print("  " + ", ".join(f(x) for x, _ in revs))

if len(sys.argv) > 2:
    prev = np.full((H * 2, W * 2, 3), 30, np.uint8)
    for p in paths + paddle_paths:
        for sub in p.split(" Z")[:-1]:
            pts = np.array([[float(v) * 2 for v in q.strip("ML ").split(",")] for q in sub.strip().split(" L")], np.int32)
            cv2.polylines(prev, [pts], True, (255, 255, 255), 1)
    for i, (x, y) in enumerate(btn + [T(*pos[i]) for i in (12, 13, 14)] + revs):
        cv2.circle(prev, (int(x * 2), int(y * 2)), 6, (0, 0, 255), -1)
        cv2.putText(prev, str(i), (int(x * 2) + 8, int(y * 2) + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1)
    cv2.imwrite(sys.argv[2], prev)
