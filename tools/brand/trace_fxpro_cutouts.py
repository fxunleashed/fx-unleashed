"""
Adds the FX Pro's see-through cutouts to assets/fxpro-outline.svg for the settings page's wheel drawing (Ui/WheelView.cs):
the windows round the five centre knobs, and the thumb openings by the inner rollers (with the slots under them).
The thin gaps along the grips are left out: they sit on the outline's own notch and only drew as noise. The outline
itself (traced from Simagic's front photo) stays exactly as it is.

Reference: SimPro 3's front picture of the wheel (product 0000000002030000), served by SimPro's local web UI at
http://127.0.0.1:4010/simpro/assets/wheel_0000000002030000.<hash>.png (500 x 301, RGBA). The picture isn't kept in
this repo. The see-through parts are transparent, so each cutout is a hole in its alpha mask. Holes that are LED or
button openings, the screen, the rev and side lights or screw holes are skipped: WheelView draws those itself.

The picture is fitted onto the outline's coordinate system (663 x 396) by their bounding boxes (checked by eye: the
existing LED, knob, screen and rev light positions land on the picture's parts).

    python tools/brand/trace_fxpro_cutouts.py <reference.png> [preview.png]
"""
import re
import sys

import cv2
import numpy as np

SVG = "assets/fxpro-outline.svg"
MID = 331.5
# where WheelView already draws something: the 12 buttons, rev lights, side lights (holes here are skipped)
BUTTONS = [(101.7, 61.6), (167.4, 76.1), (151.8, 217.5), (192.9, 245.3), (174.0, 284.3), (203.0, 325.5)]
BUTTONS += [(2 * MID - x, y) for x, y in BUTTONS]

svg = open(SVG, encoding="utf-8").read()
outline_d = re.search(r'\sd="([^"]+)"', svg).group(1).split("M")[1]   # the first subpath: the outline
outline_d = "M" + outline_d.strip()
pts = np.array([[float(a), float(b)] for a, b in re.findall(r"([\d.]+),([\d.]+)", outline_d)])

img = cv2.imread(sys.argv[1], cv2.IMREAD_UNCHANGED)
solid = (img[:, :, 3] > 128).astype(np.uint8)
ys, xs = np.nonzero(solid)
(ox0, oy0), (ox1, oy1) = pts.min(0), pts.max(0)
sx = (ox1 - ox0) / (xs.max() - xs.min())
sy = (oy1 - oy0) / (ys.max() - ys.min())
tx, ty = ox0 - xs.min() * sx, oy0 - ys.min() * sy


def T(p):
    return p[0] * sx + tx, p[1] * sy + ty


contours, hier = cv2.findContours(solid, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
holes = []
for k, c in enumerate(contours):
    if hier[0][k][3] < 0:
        continue                                    # an outer boundary, not a hole
    area = cv2.contourArea(c) * sx * sy
    x, y, w, h = cv2.boundingRect(c)
    cx, cy = T((x + w / 2, y + h / 2))
    if area < 300 or area > 15000:                  # screws, the slivers along the grips / the screen
        continue
    if min(np.hypot(cx - bx, cy - by) for bx, by in BUTTONS) < 16:
        continue                                    # a button opening
    if cy < 50 or (abs(abs(cx - MID) - 118.5) < 6 and cy < 100):
        continue                                    # rev lights / side lights
    holes.append(c)

# The thumb openings: the carbon shifter paddle behind each one is opaque in the picture, so only crescents round it
# are see-through. The opening in the plate is their convex hull together with the slot just below.
def centre(c):
    x, y, w, h = cv2.boundingRect(c)
    return T((x + w / 2, y + h / 2))


big = [c for c in holes if cv2.contourArea(c) * sx * sy > 600 and abs(centre(c)[0] - MID) > 150]
merged = []
for c in big:
    cx, cy = centre(c)
    near = [d for d in holes if d is not c and abs(centre(d)[0] - MID) > 150 and abs(centre(d)[0] - cx) < 40
            and 0 < centre(d)[1] - cy < 50]
    hull = cv2.convexHull(np.vstack([c] + near))
    merged.append((c, near, hull))
for c, near, hull in merged:
    holes = [h for h in holes if h is not c and not any(h is d for d in near)]
    holes.append(hull)

subpaths = []
for c in holes:
    eps = 0.6 / sx                                  # ~0.6 px in the drawing
    poly = cv2.approxPolyDP(c, eps, True)[:, 0, :]
    p = [T((float(a) + 0.5, float(b) + 0.5)) for a, b in poly]
    subpaths.append("M" + " L".join(f"{a:.1f},{b:.1f}" for a, b in p) + " Z")
    x, y, w, h = cv2.boundingRect(c)
    print(f"cutout at ({T((x + w / 2, y + h / 2))[0]:.1f}, {T((x + w / 2, y + h / 2))[1]:.1f}), "
          f"{w * sx:.0f} x {h * sy:.0f}, {len(p)} points")

d = outline_d.rstrip(" Z") + " Z " + " ".join(subpaths)
new = re.sub(r'(\sd=")[^"]+(")', lambda m: m.group(1) + d + m.group(2), svg, count=1)
if "fill-rule" not in new:
    new = new.replace('<path ', '<path fill-rule="evenodd" ', 1)
if "cutouts traced" not in new:
    new = new.replace("<path ", "<!-- cutouts traced from SimPro's FX Pro front picture by tools/brand/trace_fxpro_cutouts.py -->\n    <path ", 1)
open(SVG, "w", encoding="utf-8").write(new)
print(f"{len(holes)} cutouts written to {SVG}")

if len(sys.argv) > 2:
    K = 2
    out = np.zeros((396 * K, 663 * K, 3), np.uint8)
    cv2.fillPoly(out, [(pts * K).astype(np.int32)], (60, 60, 60))
    for c in holes:
        q = np.array([T((float(a), float(b))) for a, b in c[:, 0, :]]) * K
        cv2.fillPoly(out, [q.astype(np.int32)], (0, 0, 0))
    cv2.imwrite(sys.argv[2], out)
