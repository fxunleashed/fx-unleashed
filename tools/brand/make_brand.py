"""FX Unleashed brand assets, drawn from scratch (NEXT.md O1): no Simagic marks, no traced product outline, no fonts.

    python tools/brand/make_brand.py            writes the files below (needs Pillow)

Letters are polygons: a squared, chamfered, italic "racing" alphabet built here, so the logo needs no font licence.
  assets/brand/mark.svg, mark-512.png       round badge: rev dots, FX monogram (the X breaks out of the ring), UNLEASHED
  assets/brand/lockup.svg, lockup.png       wide: badge + "FX UNLEASHED" (website header, README)
  assets/logo-nobg.png (500)                the badge on transparent (settings header, logo screensaver)
  assets/logo.png (600)                     the badge on black (README on light backgrounds)
  Resources/menu-icon.png (32)              SimHub menu icon: the monogram alone, readable at 32 px
The badge keeps 13 rev dots in a row at x = 169.5 + 13.33 i, y = 103 (500 px), where Usb/ScreenSaver.cs animates them.
"""
import math
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RED, RED_DEEP, WHITE, DIM = (255, 31, 45), (140, 10, 18), (238, 240, 243), (90, 10, 14)
SKEW = math.tan(math.radians(12))

# ---------- alphabet: unit letters, height 1, stroke S, polygons (x right, y down) ----------
S = 0.2
C = 0.07  # chamfer


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def letters():
    w = 0.72
    L = {}
    L["F"] = (w, [[(0, C), (C, 0), (w, 0), (w, S), (S, S), (S, 0.42), (w * 0.82, 0.42), (w * 0.82, 0.42 + S), (S, 0.42 + S), (S, 1), (0, 1)]])
    L["X"] = (0.86, [[(0, 0), (S * 1.15, 0), (0.86, 1), (0.86 - S * 1.15, 1)],
                     [(0.86 - S * 1.15, 0), (0.86, 0), (S * 1.15, 1), (0, 1)]])
    L["U"] = (w, [[(0, 0), (S, 0), (S, 1 - S), (w - S, 1 - S), (w - S, 0), (w, 0), (w, 1 - C), (w - C, 1), (C, 1), (0, 1 - C)]])
    L["N"] = (w, [rect(0, 0, S, 1), rect(w - S, 0, w, 1), [(S, 0), (S + 0.13, 0), (w - S, 0.78), (w - S, 1), (w - S - 0.13, 1), (S, 0.22)]])
    L["L"] = (w * 0.9, [[(0, 0), (S, 0), (S, 1 - S), (w * 0.9, 1 - S), (w * 0.9, 1), (C, 1), (0, 1 - C)]])
    L["E"] = (w, [[(0, C), (C, 0), (w, 0), (w, S), (S, S), (S, 0.4), (w * 0.85, 0.4), (w * 0.85, 0.4 + S), (S, 0.4 + S), (S, 1 - S), (w, 1 - S), (w, 1), (C, 1), (0, 1 - C)]])
    L["A"] = (w, [[(0, C), (C, 0), (w - C, 0), (w, C), (w, 1), (w - S, 1), (w - S, 0.58), (S, 0.58), (S, 1), (0, 1)],
                  ])
    L["S"] = (w, [[(C, 0), (w, 0), (w, S), (S, S), (S, 0.4), (w - C, 0.4), (w, 0.4 + C), (w, 1 - C), (w - C, 1), (0, 1), (0, 1 - S),
                   (w - S, 1 - S), (w - S, 0.4 + S), (C, 0.4 + S), (0, 0.4 + S - C), (0, C)]])
    L["H"] = (w, [rect(0, 0, S, 1), rect(w - S, 0, w, 1), rect(S, 0.4, w - S, 0.4 + S)])
    L["D"] = (w, [[(0, 0), (w - 0.18, 0), (w, 0.18), (w, 1 - 0.18), (w - 0.18, 1), (0, 1)]])
    # holes (drawn in the background colour on top): A, D
    holes = {"A": [rect(S, S, w - S, 0.58 - 0.001)][0:0] + [[(S, S), (w - S, S), (w - S, 0.38), (S, 0.38)]],
             "D": [[(S, S), (w - 0.18 - S * 0.4, S), (w - S, 0.18 + S * 0.6), (w - S, 1 - 0.18 - S * 0.6), (w - 0.18 - S * 0.4, 1 - S), (S, 1 - S)]]}
    return L, holes


LETTERS, HOLES = letters()


def word(text, x, y, h, gap=0.16):
    """Polygons for text at (x, y) top-left, height h, italic. Returns (fills, holes, width)."""
    fills, holes = [], []
    cx = x
    for ch in text:
        if ch == " ":
            cx += h * 0.45
            continue
        w, polys = LETTERS[ch]
        for p in polys:
            fills.append([(cx + (px + (1 - py) * SKEW) * h, y + py * h) for px, py in p])
        for p in HOLES.get(ch, []):
            holes.append([(cx + (px + (1 - py) * SKEW) * h, y + py * h) for px, py in p])
        cx += (w + gap) * h
    return fills, holes, cx - x - gap * h


def svg_poly(p, fill):
    return f'<polygon fill="{fill}" points="{" ".join(f"{a:.1f},{b:.1f}" for a, b in p)}"/>'


def hexc(c):
    return "#%02X%02X%02X" % c


# ---------- the badge (500 x 500) ----------
def badge_shapes():
    """(kind, data, colour) in 500 px coordinates, drawn in order."""
    s = []
    s.append(("ring", (250, 250, 238, 12), RED))
    s.append(("ring", (250, 250, 219, 3), WHITE))
    for i in range(13):  # rev dots: white to red, where the screensaver animates them
        t = i / 12
        col = WHITE if i < 8 else RED if i < 11 else RED
        s.append(("dot", (169.5 + i * 13.33, 103, 4.6), col))
    s.append(("bar", (150, 124, 350, 127), DIM))
    # monogram: F red, X white, the X's lower right leg breaking out of the ring
    fF, _, wF = word("F", 0, 0, 170)
    fX, _, wX = word("X", 0, 0, 170)
    total = wF + 0.12 * 170 + wX
    x0, y0 = 250 - total / 2 - 10, 150
    for p in fF:
        s.append(("poly", [(a + x0, b + y0) for a, b in p], RED))
    ox = x0 + wF + 0.12 * 170
    for p in fX:
        s.append(("poly", [(a + ox, b + y0) for a, b in p], WHITE))
    # the break-out: the X's rising stroke (bottom left to top right) runs on past its top, out through the rings at
    # the upper right (clear of the rev dots and the wordmark); the rings are cut open around it
    xs = [(a + ox, b + y0) for a, b in fX[1]]   # [top left, top right, bottom right, bottom left] of that stroke
    top = ((xs[0][0] + xs[1][0]) / 2, (xs[0][1] + xs[1][1]) / 2)
    bot = ((xs[2][0] + xs[3][0]) / 2, (xs[2][1] + xs[3][1]) / 2)
    dx, dy = top[0] - bot[0], top[1] - bot[1]
    n = math.hypot(dx, dy); ux, uy = dx / n, dy / n
    k = 104  # how far past the top
    e0, e1 = (xs[0][0] + ux * k, xs[0][1] + uy * k), (xs[1][0] + ux * k, xs[1][1] + uy * k)
    px, py = -uy * 9, ux * 9  # the cut is 9 px wider on each side
    s.append(("gap", [(xs[0][0] + px, xs[0][1] + py), (xs[1][0] - px, xs[1][1] - py), (e1[0] - px + ux * 24, e1[1] - py + uy * 24),
                      (e0[0] + px + ux * 24, e0[1] + py + uy * 24)], None))
    s.append(("poly", [xs[0], xs[1], e1, e0], WHITE))
    # wordmark
    fills, holes, ww = word("UNLEASHED", 0, 0, 34, gap=0.2)
    wx, wy = 250 - ww / 2 - 4, 352
    for p in fills:
        s.append(("poly", [(a + wx, b + wy) for a, b in p], WHITE))
    for p in holes:
        s.append(("hole", [(a + wx, b + wy) for a, b in p], None))
    s.append(("bar", (wx - 60, wy + 15, wx - 16, wy + 19), RED))
    s.append(("bar", (wx + ww + 22, wy + 15, wx + ww + 66, wy + 19), RED))
    return s


def draw_badge(size, bg=None, scale_up=4):
    k = size * scale_up / 500
    im = Image.new("RGBA", (size * scale_up, size * scale_up), bg + (255,) if bg else (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    back = bg + (255,) if bg else (0, 0, 0, 0)
    for kind, data, col in badge_shapes():
        c = (col + (255,)) if col else back
        if kind == "ring":
            x, y, r, w = data
            d.ellipse([(x - r) * k, (y - r) * k, (x + r) * k, (y + r) * k], outline=c, width=max(1, round(w * k)))
        elif kind == "dot":
            x, y, r = data
            d.ellipse([(x - r) * k, (y - r) * k, (x + r) * k, (y + r) * k], fill=c)
        elif kind == "bar":
            x0, y0, x1, y1 = data
            d.rectangle([x0 * k, y0 * k, x1 * k, y1 * k], fill=c)
        elif kind in ("poly", "hole", "gap"):
            d.polygon([(a * k, b * k) for a, b in data], fill=c)
    return im.resize((size, size), Image.LANCZOS)


def badge_svg():
    out = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500" role="img" aria-label="FX Unleashed">']
    for kind, data, col in badge_shapes():
        if kind == "ring":
            x, y, r, w = data
            out.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="none" stroke="{hexc(col)}" stroke-width="{w}"/>')
        elif kind == "dot":
            x, y, r = data
            out.append(f'<circle class="rev" cx="{x:.2f}" cy="{y}" r="{r}" fill="{hexc(col)}"/>')
        elif kind == "bar":
            x0, y0, x1, y1 = data
            out.append(f'<rect x="{x0:.1f}" y="{y0:.1f}" width="{x1 - x0:.1f}" height="{y1 - y0:.1f}" fill="{hexc(col)}"/>')
        elif kind == "poly":
            out.append(svg_poly(data, hexc(col)))
        elif kind in ("hole", "gap"):
            out.append(svg_poly(data, "var(--fxu-bg, #000)"))
    out.append("</svg>")
    return "\n".join(out)


def lockup(h=120):
    """Badge + FX UNLEASHED, for headers. Returns (svg, png)."""
    fx_f, _, wf = word("FX", 0, 0, h * 0.5)
    un_f, un_h, wu = word(" UNLEASHED", 0, 0, h * 0.5, gap=0.18)
    width = int(h + 24 + wf + wu + 20)
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {h}" role="img" aria-label="FX Unleashed">',
             f'<g transform="scale({h / 500})">' + badge_svg().split("\n", 1)[1].rsplit("</svg>", 1)[0] + "</g>"]
    ty = h * 0.25
    tx = h + 24
    for p in fx_f:
        parts.append(svg_poly([(a + tx, b + ty) for a, b in p], hexc(RED)))
    for p in un_f:
        parts.append(svg_poly([(a + tx + wf, b + ty) for a, b in p], hexc(WHITE)))
    for p in un_h:
        parts.append(svg_poly([(a + tx + wf, b + ty) for a, b in p], "var(--fxu-bg, #000)"))
    parts.append("</svg>")
    k = 4
    im = Image.new("RGBA", (width * k, h * k), (0, 0, 0, 0))
    im.paste(draw_badge(h * k, scale_up=1), (0, 0))
    d = ImageDraw.Draw(im)
    for polys, col in ((fx_f, RED), (un_f, WHITE)):
        dx = tx if polys is fx_f else tx + wf
        for p in polys:
            d.polygon([((a + dx) * k, (b + ty) * k) for a, b in p], fill=col + (255,))
    for p in un_h:
        d.polygon([((a + tx + wf) * k, (b + ty) * k) for a, b in p], fill=(0, 0, 0, 0))
    return "\n".join(parts), im.resize((width, h), Image.LANCZOS)


def menu_icon(size=32):
    """The monogram alone, heavy, for 32 px: F red, X white, on transparent."""
    k = 16
    im = Image.new("RGBA", (size * k, size * k), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    fF, _, wF = word("F", 0, 0, 1)
    fX, _, wX = word("X", 0, 0, 1)
    h = size * k * 0.94 / (wF + 0.1 + wX + SKEW)  # two letters are wider than tall: fit the width
    total = (wF + 0.1 + wX + SKEW) * h  # the italic leans right by SKEW * h at the top
    x0, y0 = (size * k - total) / 2, (size * k - h) / 2
    for polys, col, dx in ((fF, RED, 0), (fX, WHITE, (wF + 0.1) * h)):
        for p in polys:
            d.polygon([(x0 + dx + a * h, y0 + b * h) for a, b in p], fill=col + (255,))
    return im.resize((size, size), Image.LANCZOS)


# ---------- the wheel (663 x 396), our own silhouette around the measured LED positions ----------
def wheel_path():
    """A formula-style wheel: flat top over the screen, rounded shoulders, two grips and a lower bridge under the
    bottom buttons. Our own geometry (points joined by lines), symmetric about x = 331.5."""
    m = 331.5
    right = [
        (m, 14), (430, 14), (500, 20), (560, 36),              # top edge
        (606, 58), (632, 96), (646, 150),                      # shoulder
        (654, 214), (656, 262), (650, 306),                    # outer grip
        (636, 346), (612, 374), (582, 390), (548, 394),        # grip bottom
        (520, 384), (494, 374), (462, 368), (420, 364),        # lower bridge, under the bottom buttons
        (380, 362), (m, 361),
    ]
    left = [(2 * m - x, y) for x, y in reversed(right[1:-1])]
    pts = right + left
    return "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts) + " Z"


def main():
    brand = os.path.join(ROOT, "assets", "brand")
    os.makedirs(brand, exist_ok=True)
    svg = badge_svg()
    open(os.path.join(brand, "mark.svg"), "w", encoding="utf-8").write(svg.replace("var(--fxu-bg, #000)", "#000"))
    draw_badge(512).save(os.path.join(brand, "mark-512.png"))
    draw_badge(500).save(os.path.join(ROOT, "assets", "logo-nobg.png"))
    draw_badge(600, bg=(10, 11, 13)).convert("RGB").save(os.path.join(ROOT, "assets", "logo.png"))
    lsvg, lpng = lockup(120)
    open(os.path.join(brand, "lockup.svg"), "w", encoding="utf-8").write(lsvg.replace("var(--fxu-bg, #000)", "#000"))
    lpng.save(os.path.join(brand, "lockup.png"))
    menu_icon(32).save(os.path.join(ROOT, "Resources", "menu-icon.png"))
    menu_icon(256).save(os.path.join(brand, "icon-256.png"))
    print("written:", brand, "logo-nobg.png, logo.png, menu-icon.png")


if __name__ == "__main__":
    main()
