"""
Shared pieces of the 1:1 conversions of SimHub dashes (make_lmgt3_aston.py; make_ginetta_g61.py and make_mustang.py
predate it and carry their own copies).

- fx(...): runs fxdash and returns its JSON.
- Screen fonts: fonts(text) / width(font, text) / height(font) / font_chars(font).
- Original(dash): every text item of the SimHub dash (all screens, every screen of every widget), with its box and ink
  on the wheel, measured with its own font and weight (simhub_ref.py's fonts: Bahnschrift's weights, Arial, Lucida...).
- match(e, orig): the original item a converted element came from (name, text or binding, nearest box).
- place(e, item, area, ...) / place_group(...): the screen font closest to the original's size (height ~ its em,
  width ~ its advance) that fits `area`, the band centred on the original's ink. One font for a group the original
  draws at one size.
- Conditions: vis(e), pages_of(e), own(e) (not page or take-turns conditions).
- Fixes every 1:1 conversion needs (each with its reason in its docstring): take_turns, backgrounds_from_overlays,
  snap_into_panels, keep_inside_boxes, fit_in_ellipse, clear_of_shapes_after, shrink_to_text (value boxes trimmed to
  their text where they meet other texts or caption pictures), box_as_rects, split_label, flatten_picture,
  crop_to_screen, logo_on_plain (a logo screen as a rectangle + the logo's own area).
"""
import json, os, re, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
FX = os.environ.get('FXDASH_EXE') or os.path.join(REPO, 'tools', 'fxdash', 'bin', 'Release', 'net48', 'fxdash.exe')   # (another build: FXDASH_EXE)
sys.path.insert(0, HERE)
import simhub_ref  # noqa: E402


def fx(*args, ok=(0,)):
    # fxdash prints JSON; read it from a file (piping it in Git Bash can lose it)
    fd, tmp = tempfile.mkstemp(suffix='.json'); os.close(fd)
    with open(tmp, 'w', encoding='utf-8') as f:
        rc = subprocess.run([FX] + list(args), stdout=f, stderr=subprocess.PIPE).returncode
    try:
        out = json.load(open(tmp, encoding='utf-8'))
    finally:
        os.remove(tmp)
    if rc not in ok:
        raise SystemExit(f'fxdash {args[0]} failed ({rc}): {json.dumps(out)[:2000]}')
    return out


_SAMPLES = {}


def fonts(text):
    """Every screen font with the width of `text` in it (-1: a glyph missing)."""
    if text not in _SAMPLES:
        _SAMPLES[text] = {x['id']: x for x in fx('fonts', '--sample', text)}
    return _SAMPLES[text]


def width(font, text): return fonts(text)[font]['sampleWidth']


_TIGHT = None


def tight(font):
    """A font spaced like a normal font. The screen's S* fonts (S16-S56, s30, s36, S128, S150) and f123a advance their
    capitals ~1.06 x their height (a typewriter's spacing: "T C  S L I P"); a font like Bahnschrift ~0.55, and the
    screen's car fonts (963, c8r, 992, w12...) 0.45-0.7. Measured: average advance of A-Z over the height."""
    global _TIGHT
    if _TIGHT is None:
        caps = fonts('ABCDEFGHIJKLMNOPQRSTUVWXYZ')
        _TIGHT = {fid for fid, x in caps.items() if x['sampleWidth'] > 0 and x['sampleWidth'] / 26 / x['height'] < 0.75}
        # fonts without capitals (digits only, gear fonts) count by their digits
        digits = fonts('0123456789')
        _TIGHT |= {fid for fid, x in digits.items() if fid not in caps or caps[fid]['sampleWidth'] < 0
                   if x['sampleWidth'] > 0 and x['sampleWidth'] / 10 / x['height'] < 0.75}
    return font in _TIGHT
def height(font): return fonts('0')[font]['height']
def font_chars(font): return fonts('0')[font]['chars']


# ---------------------------------------------------------------------------------------------------------------------
# The original

def _norm(s): return re.sub(r'\s+', ' ', s or '').strip()


class Item:
    """A text item of the original: where it is on the wheel, what it shows, in which font."""
    def __init__(self, it, x, y, w, h, scale, screen, widget, page, conds):
        self.it, self.x, self.y, self.w, self.h, self.scale = it, x, y, w, h, scale
        self.screen, self.widget, self.page, self.conds = screen, widget, page, conds
        self.type = simhub_ref.tname(it)
        self.name = it.get('Name')
        self.bind = simhub_ref.formula(it, 'Text')
        self.family = it.get('Font') or ('Arial' if self.type == 'GearText' else 'Bahnschrift')
        self.em = (it.get('FontSize') or 20) * scale
        self.weight = it.get('FontWeight')
        self.halign = {0: 'left', 1: 'center', 2: 'right'}.get(it.get('HorizontalAlignment', 0), 'left')
        self.valign = it.get('VerticalAlignment', 0)

    def __repr__(self): return f'<{self.type} {self.name!r} {self.screen}/{self.widget or ""}{"" if self.page is None else "#" + str(self.page)} @{self.x:.0f},{self.y:.0f}>'


class Drawn:
    """Any item of the original as SimHub draws it: its frame (fx, fy, fs: where its parent puts it), box on the wheel,
    conditions (every layer's and its own, an overlay screen's trigger first) and widget page."""
    def __init__(self, it, fx, fy, fs, screen, widget, page, conds):
        self.it, self.fx, self.fy, self.fs = it, fx, fy, fs
        self.screen, self.widget, self.page, self.conds = screen, widget, page, conds
        self.type = simhub_ref.tname(it)
        self.name = it.get('Name')
        self.x, self.y = fx + (it.get('Left') or 0) * fs, fy + (it.get('Top') or 0) * fs
        self.w, self.h = (it.get('Width') or 0) * fs, (it.get('Height') or 0) * fs

    def __repr__(self): return f'<{self.type} {self.name!r} {self.screen}/{self.widget or ""}{"" if self.page is None else "#" + str(self.page)} @{self.x:.0f},{self.y:.0f}>'


def draw_items(orig, drawn, size=(790, 460), crop=True):
    """Items of the original (Drawn) drawn together as SimHub draws them (simhub_ref.Renderer: a picture on its panel's
    colour and border, gradients, texts in their own font), on transparent: (PIL RGBA cut to what was drawn, x, y), or
    None. One picture for what an overlay shows that never changes (its panel, line and caption): one RAM-drive file."""
    from PIL import Image
    r = simhub_ref.Renderer(orig.dash, [], {}, 'text', orig.sc, orig.ox, orig.oy, size)
    r.im = Image.new('RGBA', size, (0, 0, 0, 0))
    for o in drawn:
        getattr(r, 'd_' + o.type, r.d_other)(o.it, o.fx, o.fy, o.fs, (o.it.get('Opacity') if o.it.get('Opacity') is not None else 100) / 100)
    bb = r.im.getbbox()
    if not bb: return None
    if not crop: return r.im, 0, 0
    return r.im.crop(bb), bb[0], bb[1]


DRAWN_TYPES = {'image': ('ImageItem',), 'gradient': ('GradientItem',), 'label': ('TextItem',), 'rect': ('RectangleItem',),
               'box': ('RectangleItem',), 'ellipse': ('EllipseItem',), 'bar': ('LinearGaugeItem',)}


def drawn_of(e, orig, sets):
    """The original item (Drawn) an imported element came from: the same type and name, the same conditions (its own:
    no page, blink or take-turns ones) and widget page, then the nearest box. sets: widget name -> 'page' / 'page2'...
    (the dash's sets of pages, in the import's order)."""
    mine = sorted({_norm(c[6:] if c.startswith('ncalc:') else c[3:]) for c in own(e) if 'blink(' not in c})
    pg = {c.split(':')[0]: int(c.split(':')[1]) for c in pages_of(e)}
    def ok(o):
        if o.type not in DRAWN_TYPES.get(e['Type'], (o.type,)) or o.name != e.get('Name'): return False
        if sorted({_norm(c) for c in o.conds}) != mine: return False
        return (o.page is None and not pg) or (o.widget in sets and pg == {sets[o.widget]: o.page})
    hits = [o for o in orig.all if ok(o)]
    if not hits: return None
    return min(hits, key=lambda o: abs(o.x - e['X']) + abs(o.y - e['Y']) + abs(o.w - e['W']) + abs(o.h - e['H']))


def flatten_under(im, box, tolerance=60):
    """A picture's pixels in `box` (x0, y0, x1, y1, in the picture) close to the box's most common colour (within
    `tolerance` per channel: the panel's shading) made that colour: a value's text band drawn there is then on one
    colour (it redraws in one step; over shading every change repaints the picture under it). Lines and letters (far from
    it) stay. Returns the colour, or None."""
    from collections import Counter
    x0, y0, x1, y1 = (max(0, int(box[0])), max(0, int(box[1])), min(im.width, int(box[2])), min(im.height, int(box[3])))
    if x1 <= x0 or y1 <= y0: return None
    px = im.load()
    cnt = Counter(px[x, y] for y in range(y0, y1) for x in range(x0, x1) if px[x, y][3] >= 250)
    if not cnt: return None
    base = cnt.most_common(1)[0][0]
    for y in range(y0, y1):
        for x in range(x0, x1):
            p = px[x, y]
            if p[3] >= 250 and max(abs(a - b) for a, b in zip(p[:3], base[:3])) <= tolerance: px[x, y] = base
    return '#%02X%02X%02X' % base[:3]


class Original:
    def __init__(self, dash_name, fit=(790, 460)):
        self.dash = simhub_ref.Dash(simhub_ref.find(dash_name))
        j = self.dash.json
        bw, bh = j.get('BaseWidth') or 1280, j.get('BaseHeight') or 720
        self.sc = min(fit[0] / bw, fit[1] / bh)
        self.ox, self.oy = (fit[0] - bw * self.sc) / 2, (fit[1] - bh * self.sc) / 2
        self.items = []
        self.all = []   # every drawn item (not layers or widgets), in drawing order: Drawn
        for s in j['Screens']:
            # an overlay screen shows while its trigger holds: part of its items' conditions (as the importer does)
            trig = ((s.get('OverlayTriggerExpression') or {}).get('Expression') or '').strip() if s.get('IsOverlayLayer') else ''
            self._walk(s.get('Items'), self.ox, self.oy, self.sc, s.get('Name'), None, None, [trig] if trig else [])

    def _walk(self, items, fx_, fy, fs, screen, widget, page, conds, hidden=False):
        for it in items or []:
            t = simhub_ref.tname(it)
            v = simhub_ref.formula(it, 'Visible')
            c = conds + ([v] if v else [])
            # hidden for good (Visible off, no formula): SimHub never draws it or what's in it (`all` leaves it out;
            # `items` keeps it, as the earlier conversions were built with)
            gone = hidden or (it.get('Visible') is False and v is None)
            if t not in ('Layer', 'GroupItem', 'WidgetItem') and not gone:
                self.all.append(Drawn(it, fx_, fy, fs, screen, widget, page, c))
            if t in ('Layer', 'GroupItem'):
                # a group's children are placed from its corner (a layer's on the screen's own coordinates)
                gx, gy = ((it.get('Left') or 0) * fs, (it.get('Top') or 0) * fs) if t == 'GroupItem' else (0, 0)
                self._walk(it.get('Childrens') or it.get('Items'), fx_ + gx, fy + gy, fs, screen, widget, page, c, gone)
            elif t == 'WidgetItem':
                p = os.path.join(self.dash.folder, it.get('FileName') or '')
                if not os.path.exists(p): continue
                w = json.load(open(p, encoding='utf-8-sig'))
                bw = w.get('BaseWidth') or it.get('Width') or 1
                s = fs * (it.get('Width') or bw) / bw
                x, y = fx_ + (it.get('Left') or 0) * fs, fy + (it.get('Top') or 0) * fs
                for k, scr in enumerate(w.get('Screens') or []):
                    self._walk(scr.get('Items'), x, y, s, screen, it.get('Name'), k, c, gone)
            # (SimHub's built-in texts too: FuelText, TyreTemperatureText... with their own font and box)
            elif t in ('TextItem', 'GearText', 'SpeedText') or t.startswith('Leaderboard') or (t.endswith('Text') and 'Font' in it):
                x, y = fx_ + (it.get('Left') or 0) * fs, fy + (it.get('Top') or 0) * fs
                self.items.append(Item(it, x, y, (it.get('Width') or 0) * fs, (it.get('Height') or 0) * fs, fs, screen, widget, page, c))

    def font(self, item):
        return self.dash.font(item.family, item.em, item.weight)

    def measure(self, item, text):
        """The original's advance width of `text` and its ink box (x0, y0, x1, y1) on the wheel."""
        f = self.font(item)
        asc, desc = f.getmetrics()
        l, t, r, b = f.getbbox(text, anchor='la')
        # PIL counts leading spaces as ink (" %": the box began at the space); the ink starts after them
        lead = text[:len(text) - len(text.lstrip())]
        if lead and text.strip():
            l, t, r, b = f.getbbox(text.strip(), anchor='la')
            l, r = l + f.getlength(lead), r + f.getlength(lead)
        adv = f.getlength(text)
        ha, va = item.halign, item.valign
        tx = item.x if ha == 'left' else item.x + (item.w - adv) / 2 if ha == 'center' else item.x + item.w - adv
        ty = item.y if va == 0 else item.y + (item.h - asc - desc) / 2 if va == 1 else item.y + item.h - asc - desc
        return adv, (tx + l, ty + t, tx + r, ty + b)


def _conds(e):
    """An element's own SimHub conditions, as written in the original (no blink, page or take-turns conditions)."""
    return sorted(_norm(c[6:] if c.startswith('ncalc:') else c[3:]) for c in own(e) if c.startswith(('ncalc:', 'js:')) and 'blink(' not in c)


def text_picture(orig, item, text, colour):
    """`text` drawn as the original draws it (its font, size, weight, alignment in its box) in `colour` ('#RRGGBB'),
    anti-aliased on transparent: (PIL RGBA image cut to its ink + 1 px, x, y on the wheel), or None for no ink."""
    from PIL import Image, ImageDraw
    f = orig.font(item)
    adv, ink = orig.measure(item, text)
    asc, desc = f.getmetrics()
    l, t, r, b = f.getbbox(text, anchor='la')
    x0, y0 = int(ink[0]) - 2, int(ink[1]) - 2
    w, h = int(ink[2] - ink[0]) + 5, int(ink[3] - ink[1]) + 5
    if w <= 4 or h <= 4: return None
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    g = ImageDraw.Draw(im)
    rgb = tuple(int(colour[i:i + 2], 16) for i in (1, 3, 5))
    # the text's origin, relative to the picture: its ink starts at ink[0] - x0
    g.text((ink[0] - x0 - l, ink[1] - y0 - t), text, font=f, fill=rgb + (255,), anchor='la')
    bb = im.getbbox()
    if not bb: return None
    bb = (max(0, bb[0] - 1), max(0, bb[1] - 1), min(w, bb[2] + 1), min(h, bb[3] + 1))
    return im.crop(bb), x0 + bb[0], y0 + bb[1]


def match(e, orig, candidates=None):
    """The original item element `e` came from: same name, same text or binding, the same conditions and page, then
    the nearest box."""
    cands = candidates if candidates is not None else orig.items
    name = e.get('Name')
    hits = [o for o in cands if o.name == name]
    if e.get('Type') == 'label' and e.get('Text') is not None:
        same = [o for o in hits if _norm(o.it.get('Text')) == _norm(e.get('Text'))]
        hits = same or hits
    if e.get('Bind', '').startswith('ncalc:'):
        same = [o for o in hits if o.bind and _norm(o.bind) == _norm(e['Bind'][6:])]
        hits = same or hits
    if not hits: return None
    mine = _conds(e)
    pages = [int(c.split(':')[1]) for c in pages_of(e)]

    def cost(o):
        c = abs(o.x - e['X']) + abs(o.y - e['Y']) + abs(o.w - e['W']) + abs(o.h - e['H'])
        if sorted(_norm(x) for x in o.conds) != mine: c += 10000
        if (o.page is not None) != bool(pages) or (o.page is not None and o.page not in pages): c += 5000
        return c
    return min(hits, key=cost)


# ---------------------------------------------------------------------------------------------------------------------
# Fonts and places

def score(fid, texts, em, adv, area_w, area_h, chars=None):
    """How far screen font `fid` is from the original's size (height ~ em, width of the first text ~ its advance), or
    None when the texts don't fit the area (2 px to spare on each side)."""
    x = fonts(texts[0])[fid]
    if x['height'] > area_h: return None
    ws = [width(fid, t) for t in texts]
    if min(ws) < 0 or max(ws) > area_w - 4: return None
    if chars and not all(c in x['chars'] for c in chars): return None
    return abs(x['height'] - em) / em + abs(ws[0] - adv) / max(1, adv)


def choose(texts, em, adv, area_w, area_h, chars=None):
    # a font spaced like a normal one; only when none fits (small changing text: under 32 px the screen has only its
    # S fonts) any font
    scored = [(sc, fid) for fid in fonts(texts[0]) if tight(fid) and (sc := score(fid, texts, em, adv, area_w, area_h, chars)) is not None]         or [(sc, fid) for fid in fonts(texts[0]) if (sc := score(fid, texts, em, adv, area_w, area_h, chars)) is not None]
    if not scored: return None
    return min(scored)[1]


def place(e, orig, item, texts, area, font=None, chars=None, label=None, align=None, measure_text=None):
    """Font and box for `e` from original `item`. area = (x0, y0, x1, y1) it may use. A label's box is its text; a value's
    is its font's band, as wide as the area allows around the original's text. Returns e, or None when nothing fits."""
    label = e['Type'] == 'label' if label is None else label
    adv, ink = orig.measure(item, measure_text or texts[0])
    x0, y0, x1, y1 = area
    font = font or choose(texts, item.em, adv, x1 - x0, y1 - y0, chars)
    if font is None: return None
    fh = height(font)
    cy = (ink[1] + ink[3]) / 2 + 0.06 * fh   # capitals sit a little above the middle of a band
    top = int(round(min(max(cy - fh / 2, y0), y1 - fh)))
    align = align or item.halign
    tw = max(width(font, t) for t in texts)
    if label:
        w = tw + 4
        bx = ink[0] - 2 if align == 'left' else ink[2] + 2 - w if align == 'right' else (ink[0] + ink[2]) / 2 - w / 2
        bx = int(round(min(max(bx, x0), x1 - w)))
    elif align == 'center':
        cx = (ink[0] + ink[2]) / 2
        half = min(cx - x0, x1 - cx)
        bx, w = int(round(cx - half)), int(half * 2)
        if w < tw + 4:
            w = tw + 4
            bx = int(round(min(max(cx - w / 2, x0), x1 - w)))
    elif align == 'right':
        right = min(int(round(ink[2])) + 2, x1)
        bx, w = max(x0, right - max(tw + 4, right - x0)), max(tw + 4, right - x0)
        bx = right - w
    else:
        left = max(int(round(ink[0])) - 2, x0)
        bx, w = left, max(tw + 4, x1 - left)
    e.update(Font=font, X=int(bx), Y=top, W=int(w), H=fh, Align=align)
    return e


def place_group(specs, orig):
    """specs: [(e, item, texts, area, kwargs)]: one font for all (the original draws them at one size), the closest
    over the group that fits every area."""
    meas = [(orig.measure(item, kw.get('measure_text') or texts[0]), item, texts, area, kw) for _, item, texts, area, kw in specs]
    best = None
    for fid in fonts(specs[0][2][0]):
        if not tight(fid): continue
        total = 0
        for (adv, _), item, texts, area, kw in meas:
            x0, y0, x1, y1 = area
            sc = score(fid, texts, item.em, adv, x1 - x0, y1 - y0, kw.get('chars'))
            if sc is None: total = None; break
            total += sc
        if total is not None and (best is None or total < best[0]):
            best = (total, fid)
    if best is None: return None
    for e, item, texts, area, kw in specs:
        place(e, orig, item, texts, area, font=best[1], **{k: v for k, v in kw.items() if k != 'chars'})
    return best[1]


# ---------------------------------------------------------------------------------------------------------------------
# Conditions

PAGE = re.compile(r'^page([2-4])?:\d+$', re.I)


def vis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def pages_of(e): return [c for c in vis(e) if PAGE.match(c)]


def own(e):
    """An element's own conditions: not its pages, not the take-turns ones (!(...) of an overlay, a script's opposite)."""
    return [c for c in vis(e) if not PAGE.match(c) and not c.startswith('ncalc:!(') and not (c.startswith('js:') and 'return !(' in c)]


# ---------------------------------------------------------------------------------------------------------------------
# Shapes and long texts

def box_as_rects(E, e):
    """A bordered box without rounded corners that comes and goes is a picture on the RAM drive (a few KB); two plain
    rectangles (the border colour, then the fill inset by the border) look the same, are two fills and take none.
    Replaces `e` in E; returns the two."""
    i = E.index(e)
    b = e.get('Border', 0)
    outer = {'Type': 'rect', 'Name': (e.get('Name') or '') + ' border', 'X': e['X'], 'Y': e['Y'], 'W': e['W'], 'H': e['H'],
             'Color': e.get('Color', '#FFFFFF'), 'Visible': e.get('Visible'), 'PreviewVisible': e.get('PreviewVisible')}
    inner = {'Type': 'rect', 'Name': e.get('Name'), 'X': e['X'] + b, 'Y': e['Y'] + b, 'W': e['W'] - 2 * b, 'H': e['H'] - 2 * b,
             'Color': e.get('Fill'), 'Visible': e.get('Visible'), 'PreviewVisible': e.get('PreviewVisible')}
    # a box's colour formula colours its fill (a start screen's box by speed): the inner rectangle's
    if e.get('ColorBind'): inner.update(ColorBind=e['ColorBind'], ColorStops=e.get('ColorStops'))
    E[i:i + 1] = [outer, inner] if b > 0 else [inner]
    return outer, inner


def split_label(E, e):
    """A label longer than one screen command takes (58 characters, the box and colours ~43 of them): two labels at the
    space nearest its middle, left-aligned so the line reads as one, where it was (centred, left or right in its box).
    The screen's widths add up (the parts and the space make the whole)."""
    text, f = e['Text'], e['Font']
    spaces = [k for k, c in enumerate(text) if c == ' ']
    k = min(spaces, key=lambda k: abs(k - len(text) / 2))
    head, tail = text[:k], text[k + 1:]
    whole = width(f, text)
    start = e['X'] + 2 if e.get('Align') == 'left' else e['X'] + e['W'] - 2 - whole if e.get('Align') == 'right' else e['X'] + (e['W'] - whole) // 2
    second = dict(e, Name=(e.get('Name') or '') + ' 2', Text=tail, X=start + whole - width(f, tail) - 2, W=width(f, tail) + 4, Align='left')
    e.update(Text=head, X=start - 2, W=width(f, head) + 4, Align='left')
    E.insert(E.index(e) + 1, second)
    return e, second


def flatten_picture(d, key, tolerance=12):
    """A picture's near-identical shades (noise of +-1-3 in the original art: 67 greens inside one dial's disc) folded into
    one: colours taken by how common they are, each within `tolerance` (per channel) of a colour already kept becomes
    that colour. Distinct colours (the dials, anti-aliased edges further apart) stay; transparency is kept. The look is
    the same, a disc is then one colour (a number on it redraws in one step, not hundreds of rectangles) and the JPEG
    tiles get smaller."""
    import base64, io
    from collections import Counter
    from PIL import Image
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][key]))).convert('RGBA')
    px = list(im.getdata())
    kept, to = [], {}
    for c, _ in Counter(p[:3] for p in px if p[3] >= 8).most_common():
        for k in kept:
            if max(abs(a - b) for a, b in zip(c, k)) <= tolerance:
                to[c] = k
                break
        else:
            kept.append(c); to[c] = c
    im.putdata([to[p[:3]] + (p[3],) if p[3] >= 8 else p for p in px])
    buf = io.BytesIO(); im.save(buf, 'PNG')
    d['Images'][key] = base64.b64encode(buf.getvalue()).decode('ascii')
    return len(kept)


def backgrounds_from_overlays(E):
    """Values on their own overlay's box (shown with it: their conditions take in all of the box's) are drawn on its
    colour: the checks only see the static layer under them (the box comes and goes) and would pick that instead
    (make_mustang.py 8.). The last shape drawn under a value that shows with it and holds all of its box: an opaque
    rect, a box or ellipse with a Fill (an ellipse: the value's four corners inside it, within its rim). Returns how many."""
    def holds(b, v):
        if not (b['X'] <= v['X'] and b['Y'] <= v['Y'] and b['X'] + b['W'] >= v['X'] + v['W'] and b['Y'] + b['H'] >= v['Y'] + v['H']): return False
        if b['Type'] != 'ellipse': return True
        cx, cy = b['X'] + b['W'] / 2, b['Y'] + b['H'] / 2
        rx, ry = b['W'] / 2 - b.get('Border', 0), b['H'] / 2 - b.get('Border', 0)
        return all(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1 for x in (v['X'], v['X'] + v['W']) for y in (v['Y'], v['Y'] + v['H']))
    def touches(b, v): return b['X'] < v['X'] + v['W'] and v['X'] < b['X'] + b['W'] and b['Y'] < v['Y'] + v['H'] and v['Y'] < b['Y'] + b['H']
    n = 0
    for v in [e for e in E if e['Type'] == 'value' and vis(e)]:
        under = [b for b in E[:E.index(v)] if b['Type'] in ('rect', 'box', 'ellipse', 'image', 'gradient') and vis(b)
                 and set(vis(b)) <= set(vis(v)) and touches(b, v)]
        top = under[-1] if under else None
        if top and top['Type'] in ('rect', 'box', 'ellipse') and top.get('Opacity', 100) >= 100 and not top.get('ColorBind') \
                and (top['Type'] == 'rect' or top.get('Fill')) and holds(top, v):
            v['Background'] = top['Color'] if top['Type'] == 'rect' else top['Fill']
            n += 1
    return n


def fit_in_ellipse(v, orig, item, texts, ell, chars=None, margin=3, font=None, avoid=()):
    """A value on an oval (a flag's gear on its coloured oval, a number on a dial's disc): the font closest to the
    original whose text band (the box: it clips the band) fits inside the oval, `margin` px clear of its rim, centred
    where the original's ink is (moved in as little as it takes). Over the rim every change would redraw the rim's
    pixels. ell: (cx, cy, rx, ry) of the oval's inside; avoid: rects (x0, y0, x1, y1) on the oval the box must stay
    `margin` px clear of (a logo printed on it). Returns the font, or None when none fits."""
    cx, cy, rx, ry = ell
    rx, ry = rx - margin, ry - margin
    adv, ink = orig.measure(item, texts[0])
    icx, icy = (ink[0] + ink[2]) / 2, (ink[1] + ink[3]) / 2
    best = None
    for fid in ([font] if font else fonts(texts[0])):
        if not font and not tight(fid): continue
        ws = [width(fid, t) for t in texts]
        if min(ws) < 0 or (chars and not all(c in fonts('0')[fid]['chars'] for c in chars)): continue
        w, h = max(ws) + 4, height(fid)
        # the box's centre as near the original's ink as the oval allows (its four corners inside it)
        def ok(xc, y):
            if not all(((x - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1 for x in (xc - w / 2, xc + w / 2) for yy in (y - h / 2, y + h / 2)):
                return False
            return not any(xc - w / 2 < a[2] + margin and a[0] - margin < xc + w / 2 and y - h / 2 < a[3] + margin and a[1] - margin < y + h / 2 for a in avoid)
        for dy in sorted(range(-int(ry), int(ry) + 1), key=abs):
            y = icy + 0.06 * h + dy
            if ok(icx, y) or ok(cx, y):   # where the original's ink is, else centred sideways on the oval
                break
        else:
            continue
        sc = abs(h - item.em) / item.em + abs(ws[0] - adv) / max(1, adv) + abs(dy) / 200
        if best is None or sc < best[0]: best = (sc, fid, w, h, y)
    if best is None: return None
    _, fid, w, h, y = best
    x = icx - w / 2 if ok(icx, y) else cx - w / 2
    v.update(Font=fid, X=int(round(x)), Y=int(round(y - h / 2)), W=int(w), H=int(h), Align='center')
    return fid


def snap_into_panels(E, slack=4):
    """A value that shows with a box or rect of its own overlay and sits in it but for a few px (the original's boxes
    overlap a panel's edge by a pixel): moved inside it, 2 px clear of its edges. Returns how many."""
    n = 0
    for v in [e for e in E if e['Type'] == 'value' and vis(e)]:
        for b in reversed(E[:E.index(v)]):
            if b['Type'] not in ('rect', 'box') or not vis(b) or not set(vis(b)) <= set(vis(v)): continue
            x0, y0, x1, y1 = b['X'] + 2, b['Y'] + 2, b['X'] + b['W'] - 2, b['Y'] + b['H'] - 2
            vx0, vy0, vx1, vy1 = v['X'], v['Y'], v['X'] + v['W'], v['Y'] + v['H']
            if vx0 >= x0 and vy0 >= y0 and vx1 <= x1 and vy1 <= y1: break
            if vx0 >= x0 - slack and vy0 >= y0 - slack and vx1 <= x1 + slack and vy1 <= y1 + slack and v['W'] <= x1 - x0 and v['H'] <= y1 - y0:
                v['X'] = min(max(vx0, x0), x1 - v['W']); v['Y'] = min(max(vy0, y0), y1 - v['H']); n += 1
                break
    return n


def crop_to_screen(d, e, w=790, h=460):
    """A picture reaching past the wheel's visible area (790 x 460): cut to it (what's past it is never seen; `check`
    calls it off the screen). Its picture is cut the same way, at its scale. Returns True when it changed."""
    import base64, io
    from PIL import Image
    x0, y0, x1, y1 = max(0, e['X']), max(0, e['Y']), min(w, e['X'] + e['W']), min(h, e['Y'] + e['H'])
    if (x0, y0, x1, y1) == (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H']) or x1 <= x0 or y1 <= y0: return False
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA')
    sx, sy = im.width / e['W'], im.height / e['H']
    part = im.crop((round((x0 - e['X']) * sx), round((y0 - e['Y']) * sy), round((x1 - e['X']) * sx), round((y1 - e['Y']) * sy)))
    key = e['Image'].split('@')[0] + f' cut@{x1 - x0}x{y1 - y0}'
    buf = io.BytesIO(); part.save(buf, 'PNG')
    d['Images'][key] = base64.b64encode(buf.getvalue()).decode('ascii')
    e.update(Image=key, X=x0, Y=y0, W=x1 - x0, H=y1 - y0)
    return True


def keep_inside_boxes(E, margin=2):
    """A value mostly inside a box or rect that can show under it (on its page; its own condition may come and go: a
    tyre corner's blue / red brake box) but over its edge: moved inside it, `margin` px clear of its border. On the
    border line, every change of the value would redraw the line (a flash) while the box shows. Returns how many."""
    def pages(e): return {c.split(':')[0]: c for c in pages_of(e)}
    n = 0
    for v in [e for e in E if e['Type'] == 'value']:
        pv = pages(v)
        for b in E[:E.index(v)]:
            if b['Type'] not in ('rect', 'box') or not vis(b): continue
            pb = pages(b)
            if any(k in pv and pv[k] != c for k, c in pb.items()): continue   # on another page of a set
            bx0, by0, bx1, by1 = b['X'], b['Y'], b['X'] + b['W'], b['Y'] + b['H']
            vx0, vy0, vx1, vy1 = v['X'], v['Y'], v['X'] + v['W'], v['Y'] + v['H']
            ix = max(0, min(bx1, vx1) - max(bx0, vx0)); iy = max(0, min(by1, vy1) - max(by0, vy0))
            if ix * iy < 0.5 * v['W'] * v['H']: continue
            m = margin + max(1, b.get('Border', 0))
            x0, y0, x1, y1 = bx0 + m, by0 + m, bx1 - m, by1 - m
            if vx0 >= x0 and vy0 >= y0 and vx1 <= x1 and vy1 <= y1: continue
            if v['W'] > x1 - x0 or v['H'] > y1 - y0: continue
            v['X'] = min(max(vx0, x0), x1 - v['W']); v['Y'] = min(max(vy0, y0), y1 - v['H']); n += 1
    return n


def negation(conds):
    """A condition true whenever an overlay is off: !(all of its ncalc conditions); for a script (a LIFT trigger) the same
    script returning the opposite (scripts can't be wrapped: the checked-script rules allow no functions), its blink left
    out. None when it can't be written."""
    if all(c.startswith('ncalc:') for c in conds):
        cond = conds[0][6:] if len(conds) == 1 else ' and '.join('(' + c[6:] + ')' for c in conds)
        return 'ncalc:!(' + cond + ')'
    scripts = [c for c in conds if c.startswith('js:')]
    if len(scripts) == 1 and all(c.startswith('js:') or 'blink(' in c for c in conds):
        body = scripts[0].rstrip()
        last = body.rfind('return ')
        if last > 0 and body.endswith(';') and body.count('return ') == 1:
            return body[:last] + 'return !(' + body[last + 7:-1].strip() + ');'
    return None


def take_turns(E, images=None, rounded=False):
    """Values and bars half under an overlay's opaque box or picture (a lap summary's panel over the delta; the race
    start screen's art over the side panels; `images`: the dash's pictures, to tell opaque ones): while the overlay shows,
    every change of the value would draw it and the box over it again (a flash). They hide while that overlay shows and
    come back with it gone (make_mustang.py 9.). Over all of a value's text needs nothing (the renderer skips covered
    values); elements on another page of a set never show together. Returns how many conditions were added."""
    def rect(e): return (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H'])
    def inter(a, b): return min(a[2], b[2]) - max(a[0], b[0]) > 2 and min(a[3], b[3]) - max(a[1], b[1]) > 2
    def contains(a, b): return a[0] <= b[0] and a[1] <= b[1] and a[2] >= b[2] and a[3] >= b[3]
    def band(v):
        if v['Type'] != 'value': return rect(v)
        fh = height(v['Font']); w = max(width(v['Font'], t) for t in v.get('Samples') or ['8']) + 2 * (4 + fh // 4)
        w = min(w, v['W'])
        x = v['X'] + (v['W'] - w) // 2 if v.get('Align') == 'center' else v['X'] + v['W'] - w if v.get('Align') == 'right' else v['X']
        y = v['Y'] + (v['H'] - fh) // 2
        return (x, y, x + w, y + fh)
    import base64, io
    from PIL import Image
    solid = {}
    def opaque_picture(key):
        # a picture with no see-through pixel (an art picture an overlay draws over the dash) hides all of its box;
        # rounded=True: a panel with rounded corners too (see-through only in its corners: what's under it there is
        # never a value's text)
        if key not in solid:
            im = Image.open(io.BytesIO(base64.b64decode(images[key]))).convert('RGBA')
            a = im.getchannel('A')
            solid[key] = a.getextrema()[0] >= 250
            if not solid[key] and rounded and im.width > 24 and im.height > 24:
                r = 12
                px = a.load()
                solid[key] = all(px[x, y] >= 250 for y in range(im.height) for x in range(im.width)
                                 if not ((x < r or x >= im.width - r) and (y < r or y >= im.height - r)))
        return solid[key]
    def opaque(e):
        if e.get('Opacity', 100) < 100: return False
        if e['Type'] == 'image': return bool(images) and e.get('Image') in images and opaque_picture(e['Image'])
        return e['Type'] == 'rect' or (e['Type'] in ('box', 'ellipse') and e.get('Fill'))
    def apart(a, b):
        pa = {c.split(':')[0]: c for c in pages_of(a)}; pb = {c.split(':')[0]: c for c in pages_of(b)}
        return any(k in pa and pa[k] != c for k, c in pb.items())
    def in_oval(o, b):
        bw = o.get('Border', 0) if not o.get('Fill') else 0
        cx, cy, rx, ry = o['X'] + o['W'] / 2, o['Y'] + o['H'] / 2, o['W'] / 2 - bw, o['H'] / 2 - bw
        return all(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1 for x in (b[0], b[2]) for y in (b[1], b[3]))
    turns = 0
    for ti, top in enumerate(E):
        conds = own(top)
        if not conds or not opaque(top) or pages_of(top): continue
        no = negation(conds)
        if no is None: continue
        for under in E[:ti]:
            if set(conds) <= set(own(under)) or apart(under, top) or no in vis(under): continue
            b = band(under)
            if top['Type'] == 'ellipse':
                # an oval covers what's inside it, but the renderer can't tell (it has no solid rows side to side), so
                # anything of another overlay or the dash inside a later overlay's oval kept updating under it (a setting
                # pop-up over the start screen's speed): it hides while the oval shows (no visible change: covered)
                # (an oval under it, inside it: the same size and place, or smaller: covered as well)
                oval_in_oval = under['Type'] == 'ellipse' and abs((under['X'] + under['W'] / 2) - (top['X'] + top['W'] / 2)) <= 1                     and abs((under['Y'] + under['H'] / 2) - (top['Y'] + top['H'] / 2)) <= 1 and under['W'] <= top['W'] + 1 and under['H'] <= top['H'] + 1
                if (in_oval(top, b) or oval_in_oval) and under['Type'] != 'rect' and vis(under) or under['Type'] in ('value', 'bar', 'deltabar') and in_oval(top, b):
                    under['Visible'] = vis(under) + [no]; turns += 1; continue
            if under['Type'] not in ('value', 'bar', 'deltabar'): continue
            if not inter(rect(top), b) or contains(rect(top), b) and top['Type'] != 'ellipse': continue
            under['Visible'] = vis(under) + [no]
            turns += 1
    return turns


def clear_of_shapes_after(E, orig, gap=2):
    """A value whose font band reaches into a shape drawn after it in its own overlay (the start screen's big speed over
    its four start lights): every change would draw the shape again over the new text. Its area ends `gap` px short of
    the shape (on the side the shape is), and it's placed again in that area (convert_kit.place: the original's size as
    near as fits). Returns the names moved."""
    def pos(e): return {c for c in vis(e) if not c.startswith('ncalc:!')}
    moved = []
    for v in [e for e in E if e['Type'] == 'value' and vis(e)]:
        x0, y0, x1, y1 = v['X'], v['Y'], v['X'] + v['W'], v['Y'] + v['H']
        hit = False
        for s in E[E.index(v) + 1:]:
            # of its overlay: shown only with it (the value's own conditions, not counting take-turns negations, are all
            # among the shape's)
            if s['Type'] not in ('ellipse', 'box', 'rect', 'image') or not pos(v) or not pos(v) <= pos(s): continue
            sx0, sy0, sx1, sy1 = s['X'], s['Y'], s['X'] + s['W'], s['Y'] + s['H']
            if not (sx0 < x1 and x0 < sx1 and sy0 < y1 and y0 < sy1): continue
            if sy0 > (y0 + y1) / 2: y1 = min(y1, sy0 - gap)
            elif sy1 < (y0 + y1) / 2: y0 = max(y0, sy1 + gap)
            else: continue
            hit = True
        if hit:
            item = match(v, orig)
            if place(v, orig, item, v.get('Samples') or [v.get('PreviewText') or '0'], (x0, y0, x1, y1)) is not None:
                moved.append(v.get('Name'))
    return moved


def _rect(e): return (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H'])


def _meet(a, b): return min(a[2], b[2]) - max(a[0], b[0]) > 0 and min(a[3], b[3]) - max(a[1], b[1]) > 0


def _texts(e): return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def shrink_to_text(E, also=()):
    """A value's box reaching into another text shown with it (the original's boxes overlap, only their ink doesn't: a
    tyre corner's pressure and its temperature, two numbers in one bar) or into one of `also` (static caption pictures:
    a number right-aligned up to its caption): every change would redraw the other one (a flash). Such boxes shrink to
    their widest text (+2 px each side), kept where their alignment holds them; a value meeting a value shrinks both.
    Shown together: not on different pages of a set, and the same overlay or one of them always shown. A value of an
    overlay over a static caption is covered by its overlay's box with it: left alone. Returns (names shrunk, what
    still meets a caption: (value, caption) pairs)."""
    def together(a, b):
        if b in also and own(a): return False
        for k in range(4):
            key = 'page' if k == 0 else f'page{k + 1}'
            pa = [c for c in pages_of(a) if c.split(':')[0] == key]
            pb = [c for c in pages_of(b) if c.split(':')[0] == key]
            if pa and pb and pa != pb: return False
        return sorted(own(a)) == sorted(own(b)) or not own(a) or not own(b)

    def shrink(v):
        tw = max(width(v['Font'], t) for t in _texts(v)) + 4
        if v['W'] <= tw: return
        if v.get('Align') == 'right': v['X'] += v['W'] - tw
        elif v.get('Align') == 'center': v['X'] += (v['W'] - tw) // 2
        v['W'] = tw

    shrunk = []
    for v in [e for e in E if e['Type'] == 'value']:
        for o in E:
            if o is v or (o['Type'] not in ('value', 'label') and o not in also) or not _meet(_rect(v), _rect(o)) or not together(v, o): continue
            shrink(v); shrunk.append(v.get('Name'))
            if o['Type'] == 'value': shrink(o)
    still = [(v.get('Name'), c.get('Name')) for v in E if v['Type'] == 'value' for c in also if _meet(_rect(v), _rect(c)) and together(v, c)]
    return shrunk, still


def strips_around_bars(d, E, e, rows=170):
    """The renderer makes no RAM-drive picture of a shape over a bar's box: a logo reaching over a rev bar is drawn with
    rectangles (the Ferrari 296's ignition logo: 161 KB, 6.5 s, each time, with the RAM patch too). The picture `e` is cut
    in strips: the rows level with the bars it reaches over (rectangles, a few KB), and the rest in pictures of at most
    `rows` rows (one file stays under the drive's 24 KB a picture). Replaces `e` in E; returns the strips."""
    import base64, io
    from PIL import Image
    bars = [b for b in E if b['Type'] in ('bar', 'deltabar') and b['X'] < e['X'] + e['W'] and e['X'] < b['X'] + b['W']
            and b['Y'] < e['Y'] + e['H'] and e['Y'] < b['Y'] + b['H']]
    if not bars: return [e]
    bar_y0, bar_y1 = min(b['Y'] for b in bars), max(b['Y'] + b['H'] for b in bars)
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA')
    cuts = [e['Y'], bar_y0, bar_y1]
    y = bar_y1
    while e['Y'] + e['H'] - y > rows:
        y += (e['Y'] + e['H'] - bar_y1) // 2 if e['Y'] + e['H'] - bar_y1 <= 2 * rows else rows
        cuts.append(y)
    cuts.append(e['Y'] + e['H'])
    cuts = sorted(c for c in set(cuts) if e['Y'] <= c <= e['Y'] + e['H'])
    strips = []
    for a, b in zip(cuts, cuts[1:]):
        if b <= a: continue
        k = e['Image'].split('@')[0] + f' rows {a}-{b}@{e["W"]}x{b - a}'
        buf = io.BytesIO(); im.crop((0, a - e['Y'], e['W'], b - e['Y'])).save(buf, 'PNG')
        d['Images'][k] = base64.b64encode(buf.getvalue()).decode('ascii')
        strips.append(dict(e, Image=k, Y=a, H=b - a))
    i = E.index(e)
    E[i:i + 1] = strips
    return strips


def logo_on_plain(d, E, e, key, max_colors, tolerance=3):
    """A full-screen picture that is a logo on one plain colour (a start-up or ignition screen): a rectangle of that colour
    (`<name> background`, a fill: nothing on the RAM drive) and only the logo's own area as a picture over it, cut to
    where it differs from the colour in its corner by more than `tolerance`. The picture's Opacity is mixed in over black
    first (the wheel draws pictures opaque: a logo at 8 % on a black screen). `max_colors`: the colours it really has
    (without the RAM patch it's rectangles in that many: a black and red logo on white, 3; a faint one, 2). The same
    look; the Toyota's ignition-on logo went from a screenful to its 727 x 192 area. Returns the rectangle."""
    import base64, io
    from PIL import Image
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA').resize((e['W'], e['H']), Image.LANCZOS).convert('RGB')
    a = e.get('Opacity', 100) / 100
    if a < 1:
        im = Image.eval(im, lambda v: int(round(v * a)))
        e['Opacity'] = 100
    bg = im.getpixel((2, 2))
    mask = Image.new('L', im.size, 0)
    px, mp = im.load(), mask.load()
    for yy in range(im.height):
        for xx in range(im.width):
            if max(abs(c - b) for c, b in zip(px[xx, yy], bg)) > tolerance: mp[xx, yy] = 255
    x0, y0, x1, y1 = mask.getbbox()
    x0, y0, x1, y1 = max(0, x0 - 2), max(0, y0 - 2), min(im.width, x1 + 2), min(im.height, y1 + 2)
    rect = {'Type': 'rect', 'Name': (e.get('Name') or '') + ' background', 'X': e['X'], 'Y': e['Y'], 'W': e['W'], 'H': e['H'],
            'Color': '#%02X%02X%02X' % bg, 'Visible': e.get('Visible'), 'PreviewVisible': e.get('PreviewVisible')}
    key = f'{key}@{x1 - x0}x{y1 - y0}'
    buf = io.BytesIO(); im.crop((x0, y0, x1, y1)).save(buf, 'PNG')
    d['Images'][key] = base64.b64encode(buf.getvalue()).decode('ascii')
    e.update(Image=key, X=e['X'] + x0, Y=e['Y'] + y0, W=x1 - x0, H=y1 - y0, MaxColors=max_colors)
    E.insert(E.index(e), rect)
    return rect
