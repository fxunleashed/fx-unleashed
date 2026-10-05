"""
The "LMGT3 Aston Martin Vantage AMR GT3 Evo" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_lmgt3_aston.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmgt3-aston-martin.json)

Needs SimHub installed with the SimHub dash "LMGT3 Aston Martin" (DashTemplates), Windows' Bahnschrift and Arial (the
original's fonts, used to measure its texts) and fxdash built (dotnet build -c Release tools/fxdash/fxdash.csproj).
Re-running it re-imports from the SimHub dash, so every change to the conversion lives here, with its reason.
`lmgt3_aston_parity.py` checks the result item by item; `simhub_ref.py` draws the original for side-by-side looks.

What the import brings over by itself: the main screen (the centre ring and the side lines, the gear, the seven dials of
the bottom bar, speed / laps / brake bias / delta, water and oil temperatures, the flag and pit lights at the top), the
three widgets the driver flips on their own SimHub commands as three sets of pages (format 4: fuel / energy on Next page,
the lap times on Next page 2, tyre pressures / temperatures on Next page 3), every overlay in the main screen (setting
changes in the centre oval, flags, LIFT, overboost, the pit limiter's speed and pit stop states, the start lights, lap
summaries per session, the engine-off checklist), and the ignition-off and ignition-on screens.
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_kit as K  # noqa: E402

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-aston-martin.json'
SIMHUB_DASH = 'LMGT3 Aston Martin'

work = os.path.join(os.path.dirname(OUT), '_build')
os.makedirs(work, exist_ok=True)
raw = os.path.join(work, 'lmgt3-aston-martin.import.json')
report = K.fx('import', SIMHUB_DASH, raw, '--fit', '790,460')['report']
d = json.load(open(raw, encoding='utf-8'))
E = d['Elements']
orig = K.Original(SIMHUB_DASH)
vis, own = K.vis, K.own


def els(name, typ=None, cond=None):
    return [e for e in E if e.get('Name') == name and (typ is None or e['Type'] == typ) and (cond is None or any(cond in c for c in vis(e)))]


def el(name, typ=None, cond=None):
    h = els(name, typ, cond)
    assert len(h) == 1, (name, typ, cond, len(h))
    return h[0]


# ---------------------------------------------------------------------------------------------------------------------
# 1. Identity, and names for the three sets of pages (SimHub calls the fuel widget's screens by their layers)
d['Id'] = 'lmgt3-aston-martin'
d['Name'] = 'LMGT3 Aston Martin Vantage AMR'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "LMGT3 Aston Martin Vantage AMR GT3 Evo" '
                    '(https://lmu-dashboards.com). Three parts flip on their own, as in the original: energy / fuel '
                    '(Next page), the lap times (Next page 2) and the tyres (Next page 3).')
d['Source'] = 'Redadeg\'s SimHub dash "LMGT3 Aston Martin Vantage AMR GT3 Evo", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions aren't used by any formula
assert len(d['Pages']) == 2 and [p['Name'] for p in d['PageSets']] == ['Laptimes', 'Tyre Widget'], (d['Pages'], d['PageSets'])
d['Pages'] = ['Energy', 'Fuel']
d['PageSets'][0].update(Name='Lap times', Pages=['Last lap', 'Lap time', 'Predicted', 'Best lap'])
d['PageSets'][1].update(Name='Tyres', Pages=['Pressures', 'Temperatures'])

# ---------------------------------------------------------------------------------------------------------------------
# 2. Tyre pressures. The formula returns text in psi and kPa (format(x, '0.0') / '0') and the raw number otherwise
# (bar), and SimHub's format applies to numbers only: "26.9" psi, "185" kPa. The wheel formats numeric text too
# ("26.90"), so the formula formats every unit itself (bar as SimHub would: its 0.00), shown as it comes.
for e in E:
    b = e.get('Bind') or ''
    if e['Type'] == 'value' and b.startswith("ncalc:if ([TyrePressureUnit] = 'Psi'"):
        key = b[b.index('format([') + 8:b.index('],')]
        tail = f"[{key}]))"
        assert b.endswith(tail), b
        e['Bind'] = b[:-len(tail)] + f"format([{key}], '0.00')))"
        e['Format'] = 'text'

# ---------------------------------------------------------------------------------------------------------------------
# 3. The widest text each value really shows (the import copies SimHub's preview text: "52.5", "1", "1:25.345").
SAMPLES = [   # (a text in the binding, samples): the first that matches, so the specific ones first
    ('driverbestlap(getopponent', ['8.888']), ('SelfsplitDelta', ['-8.888']),
    ('TyrePressure', ['88.8', '888', '8.88']), ('mTireInnerLayerTemperature', ['188']), ('BrakeTemperature', ['888']),
    ('LastLapTime]', ['8.88.888']), ('CurrentLapTime', ['8.88.888']), ('EstimatedLapTime', ['8.88.888']), ('BestLapTime]', ['8.88.888']),
    ('mVirtualEnergy', ['100']), ('Fuel_CurrentLapConsumption', ['8.88']), ('Fuel_LitersPerLap', ['8.88']), ('[Fuel]', ['188']),
    ('WaterTemperature', ['188']), ('OilTemperature', ['188']), ('[SpeedLocal]', ['388']), ('CompletedLaps', ['888']),
    ('[BrakeBias]', ['88.8']), ('mDeltaBest', ['-8.888']), ('[Position]', ['88']), ('[TCLevel]', ['88']), ('[ABSLevel]', ['88']),
    ('mTCCut', ['88']), ('mTCSlip', ['88']), ('[EngineMap]', ['88']), ('AntiSway', ['88']), ('mWiperState', ['8']),
    ('SessionTypeName],', ['PRACTICE']),
    ("format([DataCorePlugin.GameData.BrakeBias]", ['58.5:41.5']), ('[TrackName]', ['Circuit de la Sarthe']), ('[Gear]', ['8', 'N', 'R']), ('gearText', ['8', 'N', 'R']),
]
for e in E:
    if e['Type'] != 'value': continue
    for key, s in SAMPLES:
        if key in (e.get('Bind') or ''):
            e['Samples'] = s
            break
    if e.get('Bind') in ('gearText', 'ncalc:[Gear]'): e['Empty'] = 'N'
    # the preview text SimHub's designer showed ("58.5 : 43,5", "0:00.000" for a delta): the widest real text instead
    if e.get('Samples'): e['PreviewText'] = e['Samples'][0]
    # before a lap has a time SimHub formats the zero time ("0.00.000"); the wheel shows Empty for a time of 0
    if (e.get('Format') or '').startswith('time:'):
        e['Empty'] = e['Format'][5:].replace('\\', '').replace('m', '0').replace('ss', '00').replace('fff', '000')
        if e.get('Samples') and e['Empty'] not in e['Samples']: e['Samples'] = e['Samples'] + [e['Empty']]   # it must fit too

# ---------------------------------------------------------------------------------------------------------------------
# 4. Every value in the original's size and place: each one measured from its original item with the original's font
# (Bahnschrift in its weight, Arial for the gear), the screen font closest to it that fits picked, and its band centred
# on where the original's ink is. Items the original draws in the same font, size and weight get one font together
# (the dials' numbers, their captions, the side values...): convert_kit.place_group. Areas: the original's box on
# the wheel (its text is inside it), unless AREAS says otherwise.
AREAS = {}      # (name, a text in a condition or '') -> (x0, y0, x1, y1)
FONTS = {}      # (name, a text in a condition or '') -> screen font, where the closest isn't the right one


def texts_of(e):
    return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def lookup(table, e):
    for (name, cond), v in table.items():
        if e.get('Name') == name and (cond == '' or any(cond in c for c in vis(e))): return v
    return None


# Fixed texts that are always shown or too small for the screen's normally spaced fonts (under 25 px; the smallest is
# 32) become pictures in 5.; the rest (values, an overlay's big titles) stay screen text, in fonts spaced like a normal
# font (convert_kit.tight: the screen's S fonts space their letters like a typewriter, "T C  S L I P").
def as_picture(e):
    if e['Type'] != 'label' or e.get('ColorBind') or not (e.get('Text') or '').strip(): return False
    always = not [c for c in own(e) if 'blink(' not in c]
    return always or K.match(e, orig).em < 25


groups = {}
for e in E:
    if e['Type'] not in ('label', 'value') or as_picture(e): continue
    if not texts_of(e)[0].strip(): continue
    item = K.match(e, orig)
    area = lookup(AREAS, e) or (max(0, int(item.x)), max(0, int(item.y)), min(790, int(item.x + item.w)), min(460, int(item.y + item.h)))
    kw = {'chars': '0123456789NR'} if e.get('Bind') in ('gearText', 'ncalc:[Gear]') else {}
    if lookup(FONTS, e):
        K.place(e, orig, item, texts_of(e), area, font=lookup(FONTS, e))
        continue
    groups.setdefault((item.family, round(item.em, 1), item.weight, e.get('Bind') in ('gearText', 'ncalc:[Gear]')), []).append((e, item, texts_of(e), area, kw))
# The screen's small fonts are wide (16 px a letter) and Bahnschrift is narrow: a caption can need more room than its
# box in the original. The group's areas then grow sideways around their centres, as little as it takes (and a few px
# taller for a font that's a little taller), until one font fits them all.
def grown(area, dx, dy):
    x0, y0, x1, y1 = area
    return (max(0, x0 - dx), max(0, y0 - dy), min(790, x1 + dx), min(460, y1 + dy))


unplaced, widened = [], {}
for key, specs in groups.items():
    for dx in range(0, 161, 8):
        dy = min(6, dx // 8)
        tried = [(e, item, texts, grown(area, dx, dy), kw) for e, item, texts, area, kw in specs]
        if K.place_group(tried, orig) is not None:
            if dx: widened[specs[0][0].get('Name')] = dx
            break
    else:
        for e, item, texts, area, kw in specs:
            if K.place(e, orig, item, texts, grown(area, 160, 6), **kw) is None: unplaced.append(e.get('Name'))

# The art's near-identical shades made one colour each (the bar's discs held 67 greens within +-1 of each other: noise
# in the original picture, invisible, but a number on it redrew hundreds of rectangles at every change).
for key in {e['Image'] for e in E if e['Type'] == 'image' and e.get('Name') in ('Leiste unten NEU', 'Kreis Mitte2', 'Schriftzug in Kreis')}:
    K.flatten_picture(d, key)

# The seven dials of the bottom bar: each number on its coloured disc. Its text band must stay inside the disc (one
# colour): over the ring around it every change would redraw the ring's pixels (686 rectangles for TC SLIP). The discs
# are measured in the bar's own picture; each number gets the font closest to the original that fits, band and all,
# inside the disc with 3 px to spare, centred on the disc (the box clips the band).
from PIL import Image
import base64 as _b64, io as _io
bar = el('Leiste unten NEU', 'image', cond=None) if len(els('Leiste unten NEU', 'image')) == 1 else [e for e in els('Leiste unten NEU', 'image') if not own(e)][0]
bar_im = Image.open(_io.BytesIO(_b64.b64decode(d['Images'][bar['Image']]))).convert('RGBA').resize((bar['W'], bar['H']), Image.LANCZOS)
bg = Image.new('RGBA', bar_im.size, (0, 0, 0, 255)); bg.alpha_composite(bar_im); bar_im = bg.convert('RGB')
DIALS = ['TC3#', 'Map#', 'ARBF#', 'TCC#', 'ARBR#', 'ABS#', 'TC2#']
for name in DIALS:
    v = [e for e in E if e.get('Name') == name and e['Type'] == 'value' and not own(e)][0]
    cx, cy = v['X'] + v['W'] // 2 - bar['X'], v['Y'] + v['H'] // 2 - bar['Y']
    c0 = bar_im.getpixel((cx, cy))
    def run(dx):
        n = 0
        while 0 <= cx + dx * (n + 1) < bar_im.width and max(abs(a - b) for a, b in zip(bar_im.getpixel((cx + dx * (n + 1), cy)), c0)) <= 12: n += 1
        return n
    l, r = run(-1), run(1)
    ccx = bar['X'] + cx + (r - l) / 2
    rad = (l + r) / 2 - 3   # 3 px clear of the disc's anti-aliased edge
    # the disc's vertical centre: where its column is widest
    rows = []
    for yy in range(max(0, cy - 40), min(bar_im.height, cy + 40)):
        if max(abs(a - b) for a, b in zip(bar_im.getpixel((int(ccx - bar['X']), yy)), c0)) <= 12: rows.append(yy)
    ccy = bar['Y'] + (min(rows) + max(rows)) / 2
    item = K.match(v, orig)
    adv, _ = orig.measure(item, '88')
    best = None
    for fid in K.fonts('88'):
        if not K.tight(fid) or K.width(fid, '88') < 0: continue
        w, h = K.width(fid, '88') + 4, K.height(fid)
        if (w / 2) ** 2 + (h / 2) ** 2 > rad ** 2: continue
        sc = abs(h - item.em) / item.em + abs(K.width(fid, '88') - adv) / adv
        if best is None or sc < best[0]: best = (sc, fid, w, h)
    _, fid, w, h = best
    # every dial number in one font, the one that fits the smallest disc (the original: one size)
    v.update(Font=fid, X=int(round(ccx - w / 2)), Y=int(round(ccy - h / 2)), W=w, H=h, Align='center')
dial_font = min((e['Font'] for e in E if e.get('Name') in DIALS and e['Type'] == 'value' and not own(e)), key=K.height)
for v in [e for e in E if e.get('Name') in DIALS and e['Type'] == 'value' and not own(e)]:
    if v['Font'] != dial_font:
        w, h = K.width(dial_font, '88') + 4, K.height(dial_font)
        v.update(Font=dial_font, X=v['X'] + (v['W'] - w) // 2, Y=v['Y'] + (v['H'] - h) // 2, W=w, H=h)

# A value's box reaching into another text shown with it (the tyre widget: a corner's brake temperature and its
# pressure share the corner, the original's boxes overlap and only their ink doesn't): every change would redraw the
# other one (a flash). Such boxes shrink to their widest text (+2 px each side), kept where their alignment holds them.
def rect(e): return (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H'])
def meet(a, b): return min(a[2], b[2]) - max(a[0], b[0]) > 0 and min(a[3], b[3]) - max(a[1], b[1]) > 0
def together(a, b):
    """Can show at the same time: not on different pages of a set (overlays are left to take-turns / the checks)."""
    for k in range(4):
        pa = [c for c in K.pages_of(a) if c.split(':')[0] == ('page' if k == 0 else f'page{k + 1}')]
        pb = [c for c in K.pages_of(b) if c.split(':')[0] == ('page' if k == 0 else f'page{k + 1}')]
        if pa and pb and pa != pb: return False
    return sorted(own(a)) == sorted(own(b)) or not own(a) or not own(b)
def shrink(v):
    tw = max(K.width(v['Font'], t) for t in texts_of(v)) + 4
    if v['W'] <= tw: return
    if v.get('Align') == 'right': v['X'] += v['W'] - tw
    elif v.get('Align') == 'center': v['X'] += (v['W'] - tw) // 2
    v['W'] = tw
shrunk = 0
for v in [e for e in E if e['Type'] == 'value']:
    for o in E:
        if o is v or o['Type'] not in ('value', 'label') or not meet(rect(v), rect(o)) or not together(v, o): continue
        shrink(v); shrunk += 1
        if o['Type'] == 'value': shrink(o)

# ---------------------------------------------------------------------------------------------------------------------
# 5. Fixed texts as pictures in the original's own font. The screen draws text only in its own fonts, and below 32 px
# it has only the S fonts, spaced like a typewriter ("T C  S L I P", "L A S T  L A P"): the original's Bahnschrift
# captions looked nothing like it. A caption is the same text all the time, so it's drawn here as the original draws
# it (Bahnschrift at its size and weight, its colour, anti-aliased) and placed where the original has it. Always shown:
# part of the static layer (the RAM drive's tiles: no cost; without it, rectangles of its one colour). Shown with an
# overlay: part of that overlay's picture. A label with a colour formula stays text (a picture has one colour).
import base64, io
pictures = 0
for i, e in enumerate(E):
    if not as_picture(e): continue
    item = K.match(e, orig)
    pic = K.text_picture(orig, item, e['Text'], e.get('Color', '#FFFFFF')[:7])
    if pic is None: continue
    im, x, y = pic
    buf = io.BytesIO(); im.save(buf, 'PNG')
    key = f'text {e["Name"]} {i}@{im.width}x{im.height}'
    d.setdefault('Images', {})[key] = base64.b64encode(buf.getvalue()).decode('ascii')
    E[i] = {k: v for k, v in e.items() if k in ('Name', 'Visible', 'PreviewVisible')}
    E[i].update(Type='image', Image=key, X=x, Y=y, W=im.width, H=im.height, MaxColors=4, Text=None)
    E[i].pop('Text')
    pictures += 1

# The lap-times widget's page dots ("•" x 4 under the time, the current page's one bigger): the screen's fonts have no
# "•" (the import left them empty). Each page's four as one picture of the original's dots (one RAM-drive file a page,
# not four).
for k in range(4):
    dots = [e for e in E if e['Type'] == 'label' and e.get('Name') in ('1', '2', '3', '4') and K.pages_of(e) == [f'page2:{k}']]
    assert len(dots) == 4 and not any(e.get('Text') for e in dots), dots
    pics = []
    for e in dots:
        item = K.match(e, orig)
        pics.append(K.text_picture(orig, item, item.it['Text'].strip(), '#FFFFFF'))
    x0, y0 = min(p[1] for p in pics), min(p[2] for p in pics)
    x1, y1 = max(p[1] + p[0].width for p in pics), max(p[2] + p[0].height for p in pics)
    im = Image.new('RGBA', (x1 - x0, y1 - y0), (0, 0, 0, 0))
    for p, x, y in pics: im.alpha_composite(p, (x - x0, y - y0))
    buf = io.BytesIO(); im.save(buf, 'PNG')
    key = f'text page dots {k}@{im.width}x{im.height}'
    d['Images'][key] = base64.b64encode(buf.getvalue()).decode('ascii')
    first = E.index(dots[0])
    E[first] = {'Type': 'image', 'Name': 'Punkte', 'Image': key, 'X': x0, 'Y': y0, 'W': im.width, 'H': im.height,
                'Visible': dots[0]['Visible'], 'MaxColors': 2}
    for e in dots[1:]: E.remove(e)
    pictures += 1

# ---------------------------------------------------------------------------------------------------------------------
# 6. Shapes and long texts. A bordered box without rounded corners that comes and goes (the oil pressure banner): two
# rectangles instead of a picture. A text longer than one screen command takes ("oil pressure low" showed "oil pressure
# l"): two labels that read as one line.
for e in [e for e in E if e['Type'] == 'box' and not e.get('Radius') and e.get('Fill') and own(e)]:
    K.box_as_rects(E, e)
for e in [e for e in E if e['Type'] == 'label' and len(e.get('Text') or '') > 14 and ' ' in e['Text']]:
    K.split_label(E, e)

# The gears on ovals (the centre's light blue oval; a flag's, LIFT's or a lap summary's coloured one): the font closest
# to the original whose band fits inside the oval, 3 px clear of its edge (over the edge, every gear change redrew the
# ring's pixels). Gears the original draws at one size get one font: the one that fits them all.
def oval_inside(o):
    b = o.get('Border', 0)
    return (o['X'] + o['W'] / 2, o['Y'] + o['H'] / 2, o['W'] / 2 - b, o['H'] / 2 - b)


gears = {}
for g in [e for e in E if e['Type'] == 'value' and e.get('Bind') == 'gearText']:
    ovals = [o for o in E[:E.index(g)] if o['Type'] == 'ellipse' and (o.get('Fill') or not o.get('Border')) and set(vis(o)) <= set(vis(g))
             and o['X'] < g['X'] + g['W'] / 2 < o['X'] + o['W'] and o['Y'] < g['Y'] + g['H'] / 2 < o['Y'] + o['H']]
    if not ovals: continue
    item = K.match(g, orig)
    # what's printed on the oval under the gear (the centre's VANTAGE GT3): its box stays clear of it
    on = [(o['X'], o['Y'], o['X'] + o['W'], o['Y'] + o['H']) for o in E[E.index(ovals[-1]) + 1:E.index(g)]
          if o['Type'] in ('image', 'label') and set(vis(o)) <= set(vis(g)) and ovals[-1]['X'] <= o['X'] and o['X'] + o['W'] <= ovals[-1]['X'] + ovals[-1]['W']]
    gears.setdefault(round(item.em, 1), []).append((g, item, oval_inside(ovals[-1]), on))
for em, gs in gears.items():
    fid = None
    for g, item, ell, on in gs:   # the font each one would get; then the smallest of them for all
        f = K.fit_in_ellipse(dict(g), orig, item, g['Samples'], ell, chars='0123456789NR', avoid=on)
        if f is not None and (fid is None or K.height(f) < K.height(fid)): fid = f
    for g, item, ell, on in gs:
        K.fit_in_ellipse(g, orig, item, g['Samples'], ell, chars='0123456789NR', font=fid, avoid=on)

# The wheel shows 790 x 460: the faint winged logo reaches past its bottom (cut there, nothing seen changes); the
# ignition screens' backgrounds cover that area, not the screen's corners the padding pushes off.
for e in [e for e in E if e['Type'] == 'image']:
    K.crop_to_screen(d, e)
for n in ('IGN OFF background', 'IGN ON background'):
    for e in els(n, 'rect'):
        e.update(X=0, Y=0, W=790, H=460)
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]   # pictures no element uses any more (the uncut logos)

# Values on their own overlay's box (the lap summaries on their blue panels, a flag's gear on its oval): drawn on its
# colour (convert_kit.backgrounds_from_overlays, as make_mustang.py 8.); a value over its panel's edge by a pixel or two
# (the original's boxes overlap it) moved inside it.
K.snap_into_panels(E)
K.backgrounds_from_overlays(E)
# A tyre corner's numbers inside its blue / red brake box (shown when the brakes are cold / hot), 2 px clear of its
# border line: the front-left brake temperature sat on the line and flashed at every change while the box showed.
K.keep_inside_boxes(E)
# The start screen's speed (LIMITER START: race, pit limiter on, no lap done) over its four start lights: its font band
# reached 13 px into them, so every speed change would draw the lights again over it. Its area ends 2 px above them;
# the original's digits end above them too.
clear = K.clear_of_shapes_after(E, orig)
# Values half under an overlay's box (the race lap summary's blue panels over the delta and the lap time): they hide
# while it shows (convert_kit.take_turns, as make_mustang.py 9.); else each change drew them, then the panel over them.
turns = K.take_turns(E, d['Images'])
# The race lap summary (blue panels on both sides, its oval in the centre) over the start screen (LIMITER START: race,
# pit limiter on, no lap done, with its own centre ring picture). They show together only for a moment when a race
# session starts (the last lap time resets on the grid); the summary's panels lie over the start screen's picture, so
# the picture came first and the panels again over it (a flash over both panels). The summary waits for the start
# screen to go.
start = own([e for e in E if e.get('Name') == 'Kreis Mitte2' and e['Type'] == 'image' and any('CompletedLaps]<1' in c for c in own(e))][0])
summary = own(el('blau links', 'rect', cond="'Race'"))
for e in [e for e in E if own(e) and set(summary) <= set(own(e))]:
    e['Visible'] = vis(e) + [K.negation(start)]

# The wiper level is a small number at the wiper icon's corner: its box ends where the icon starts (the icon is a
# picture: a number running onto it would redraw the picture's pixels at each change).
for lv in els('WIPER Level', 'value'):
    icon = min((i for i in els('WIPER_OFF', 'image') + els('WIPER', 'image')), key=lambda i: i['X'])
    if lv['X'] + lv['W'] > icon['X'] - 1:
        lv['W'] = max(K.width(lv['Font'], '8') + 4, icon['X'] - 1 - lv['X'])
        if lv['X'] + lv['W'] > icon['X'] - 1: lv['X'] = icon['X'] - 1 - lv['W']

# The centre disc (light blue, a thin light line and a green line round it) is in the ring picture, and every overlay
# in the centre (setting changes, flags, lap summaries) is an oval over exactly that disc. Without the RAM drive, putting
# the disc back when one goes took ~1,900 rectangles (38 KB, 1.5 s of the screen: each of its thin lines two a row). So
# the disc is also two ovals drawn by the screen itself, shown all the time ("true": the screen draws only shapes that
# come and go itself), with VANTAGE GT3 on it after them: when an oval over it goes, the renderer draws the disc again
# over it (wiped first in the one colour around it) instead of putting the picture back (~4 KB). The picture is dark
# under the disc (one colour round it for that wipe; kept in the picture, the RAM drive's tiles took 229 KB, 175 KB
# without). The original's light blue oval inside the disc (EllipseItem "GELB", the disc's own
# colour, so nothing of it is seen) is left out.
ring = [e for e in E if e.get('Name') == 'Kreis Mitte2' and e['Type'] == 'image' and not own(e)][0]
ring_im = Image.open(_io.BytesIO(_b64.b64decode(d['Images'][ring['Image']]))).convert('RGBA').resize((ring['W'], ring['H']), Image.LANCZOS)
DISC = (287, 48, 219, 226)   # measured in the picture: the light line's outside edge; the green line 2 px, 1 px in
dark = ring_im.getpixel((DISC[0] - 3 - ring['X'], DISC[1] + DISC[3] // 2 - ring['Y']))
dcx, dcy = DISC[0] + DISC[2] / 2, DISC[1] + DISC[3] / 2
def in_disc(xx, yy, grow):
    return ((xx + 0.5 - dcx) / (DISC[2] / 2 + grow)) ** 2 + ((yy + 0.5 - dcy) / (DISC[3] / 2 + grow)) ** 2 <= 1
for yy in range(DISC[1] - 3, DISC[1] + DISC[3] + 3):
    for xx in range(DISC[0] - 3, DISC[0] + DISC[2] + 3):
        if in_disc(xx, yy, 1):
            ring_im.putpixel((xx - ring['X'], yy - ring['Y']), dark)
buf = _io.BytesIO(); ring_im.save(buf, 'PNG')
ring['Image'] = 'Kreis Mitte2 without its disc'
d['Images'][ring['Image']] = _b64.b64encode(buf.getvalue()).decode('ascii')
always = ['ncalc:true']
# (outside in: the light line, the green line, the light blue: an oval in the light line's colour filled green, then the
# light blue oval on the green. Each lies on one colour, so either can be wiped clean and drawn again over itself.)
disc = [{'Type': 'ellipse', 'Name': 'Kreis Mitte2 disc', 'X': DISC[0], 'Y': DISC[1], 'W': DISC[2], 'H': DISC[3],
         'Color': '#B1C0E0', 'Border': 2, 'Fill': '#83A124', 'Visible': always},
        {'Type': 'ellipse', 'Name': 'Kreis Mitte2 disc middle', 'X': DISC[0] + 5, 'Y': DISC[1] + 5, 'W': DISC[2] - 10, 'H': DISC[3] - 11,
         'Color': '#B1C0E0', 'Fill': '#B1C0E0', 'Visible': always}]
inner = [e for e in E if e.get('Name') == 'GELB' and e['Type'] == 'ellipse' and not vis(e)]
assert len(inner) == 1
E[E.index(inner[0]):E.index(inner[0]) + 1] = disc
[e for e in els('Schriftzug in Kreis', 'image') if not own(e)][0]['Visible'] = always

# On a wheel without the RAM drive the static layer is drawn with rectangles, each picture's area reduced to MaxColors
# colours (of everything there, not just the picture), and drawing it all took ~11 s. Measured by leaving each out: the
# ring art ~3,800 rectangles, the dial bar ~3,800, the captions ~1,800, the faint logo ~600. In as few colours as each
# needs it's ~7 s: the bar 10 (seven dials, their rings and black: enough once its noise is flattened), the ring 4, the
# logo 3, a caption 2 (its colour and black; 3 over the faint logo, or it took the logo's grey; over the ring's lines 2
# is enough, and 3 there cost a race summary's end 3 KB more). With the RAM drive they're
# full-colour tiles either way.
for e in E:
    if e['Type'] != 'image': continue
    if e.get('Name') == 'Leiste unten NEU': e['MaxColors'] = 10
    elif e.get('Name') == 'Kreis Mitte2': e['MaxColors'] = 3
    elif e.get('Name') in ('ImageItem', 'Schriftzug in Kreis'): e['MaxColors'] = 3
    elif e['Image'].startswith('text '):
        over = [o for o in E[:E.index(e)] if o['Type'] == 'image' and not own(o) and o.get('Name') == 'ImageItem'
                and o['X'] < e['X'] + e['W'] and e['X'] < o['X'] + o['W'] and o['Y'] < e['Y'] + e['H'] and e['Y'] < o['Y'] + o['H']]
        e['MaxColors'] = 3 if over else 2

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); a second
# run must change nothing (the /create-dash gate)
bands = K.fx('fit-bands', OUT)['changes']
again = K.fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'unplaced': unplaced, 'widened': widened, 'text_pictures': pictures, 'take_turns': turns, 'clear_of_shapes': clear, 'fit_bands': bands, 'import_notes': report['Notes']}, indent=1))
