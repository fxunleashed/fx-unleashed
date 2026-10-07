"""
The "LMGT3 Ferrari 296" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_ferrari_296.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmgt3-ferrari-296.json)

Needs SimHub installed with the SimHub dash "LMGT3 Ferrari 296" (DashTemplates; its fonts CPMono and Arial Edit come
with it, Segoe UI from Windows) and fxdash built (dotnet build -c Release tools/fxdash/fxdash.csproj). Re-running it
re-imports from the SimHub dash, so every change to the conversion lives here, with its reason.
`ferrari_296_parity.py` checks the result item by item; `simhub_ref.py` draws the original for side-by-side looks.

What the import brings over by itself: the main screen (session, speed, the brake bias / lap counter / energy panels,
the rev bar, tyre pressures or temperatures, the gear, the icons, brake temperatures, the bottom row of setting panels
and the lap time, delta and predicted lap), the four widgets the driver flips on their own SimHub commands as four sets
of pages (tyres: pressures / temperatures, Next page 1; the lap time panel's five screens, Next page 2; the two
energy-low warnings, whose second screen is the driver's acknowledgement, Next page 3 and 4), every overlay (setting
changes, pit limiter, car up, lap summary, engine off, launch, LIFT, the delta's per-session lap summaries, cold / hot
tyre and brake tiles, TC / ABS off), and the ignition-off and ignition-on screens.

This replaces the automatic conversion of 2026-09 (fxdash tune): its panels lost their colours and shading, its
captions were in the screen's letter-spaced fonts, and the delta read "00.0-03.60" (two values in one place).

How the original is drawn: every panel is a shading picture ("shadow": dark at the top, clear in the middle) stretched
over a coloured, rounded, bordered box, with a gradient line under its caption. The import keeps only the picture.
Here each panel, with its line and its fixed texts, is drawn as SimHub draws it (convert_kit.draw_items) into one
picture: always-shown ones into the static layer, an overlay's into one RAM-drive file per look. Values stay screen
text on top; a panel holding a value is flat, in the colour of its shading behind the value (one colour under a value:
it redraws in one step; a flat stripe only under the text looked like a box behind the number).
"""
import base64, copy, io, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_kit as K  # noqa: E402

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-ferrari-296.json'
SIMHUB_DASH = 'LMGT3 Ferrari 296'

work = os.path.join(os.path.dirname(OUT), '_build')
os.makedirs(work, exist_ok=True)
raw = os.path.join(work, 'lmgt3-ferrari-296.import.json')
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


def picture(im):
    buf = io.BytesIO(); im.save(buf, 'PNG')
    return base64.b64encode(buf.getvalue()).decode('ascii')


# ---------------------------------------------------------------------------------------------------------------------
# 1. Identity, and names for the four sets of pages
d['Id'] = 'lmgt3-ferrari-296'
d['Name'] = 'LMGT3 Ferrari 296'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "LMGT3 Ferrari 296" '
                    '(https://lmu-dashboards.com). Parts flip on their own, as in the original: the tyres, pressures or '
                    'temperatures (Next page 1); the lap time panel: last lap, lap time, predicted, overall best, personal '
                    'best (Next page 2); the two energy-low warnings are put away with Next page 3 and 4.')
d['Source'] = 'Redadeg\'s SimHub dash "LMGT3 Ferrari 296", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions aren't used by any formula
assert d['Pages'] == ['Pres', 'Temp'] and [p['Name'] for p in d['PageSets']] == ['Laptimes', 'Fuel Low 15', 'Fuel Low 10'], (d['Pages'], d['PageSets'])
SETS = {'Tyres': 'page', 'Laptimes': 'page2', 'Fuel Low 15': 'page3', 'Fuel Low 10': 'page4'}
d['Pages'] = ['Pressures', 'Temperatures']
d['PageSets'][0].update(Name='Lap times', Pages=['Last lap', 'Lap time', 'Predicted', 'Overall best', 'Personal best'])
d['PageSets'][1].update(Name='Energy low 15 %', Pages=['Warning', 'Put away', 'Put away 2'])
d['PageSets'][2].update(Name='Energy low 10 %', Pages=['Warning', 'Put away'])

# ---------------------------------------------------------------------------------------------------------------------
# 2. The widest text each value really shows (the import copies SimHub's preview text: "25.0", "77", "7").
SAMPLES = [   # (a text in the binding, samples): the first that matches, so the specific ones first
    ('SessionBestLiveDeltaSeconds', ['-88.88', 'N/A']), ('driverbestlap(getopponent', ['88:88.888', '--:--:---']),
    ('driverbestlap(1)', ['88:88.888', '-:---']), ('drivershortname', ['WWWWWWWW']),
    ('driverrelativegaptoplayer', ['+888.88', '-']), ('EstimatedLapTime', ['8:88.88']), ('Fuel_RemainingLaps', ['88.8']),
    ('Fuel_LitersPerLap', ['8.8']), ('[TCLevel]', ['88']), ('mTCCut', ['88']), ('mTCSlip', ['88']), ('AntiSway', ['88']),
    ('[ABSLevel]', ['88']), ('[EngineMap]', ['88']), ('mPressure', ['888']), ('mTireInnerLayerTemperature', ['188']),
    ('[LastLapTime]', ['88:88.88']), ('[CurrentLapTime]', ['88:88.88']), ('[BestLapTime]', ['88:88.88']),
    ('[BrakeBias]', ['88.8']), ('[CompletedLaps]', ['888']), ('mVirtualEnergy', ['100']), ('[SessionTypeName]', ['PRACTICE']),
    ('gearText', ['8', 'N', 'R']), ('BrakeTemperature', ['1888']), ('[SpeedLocal]', ['388']), ('mWiperState', ['8']),
    ('[Position]', ['88']), ('[SelfsplitDelta]', ['-88.888']),
]
for e in E:
    if e['Type'] != 'value': continue
    for key, s in SAMPLES:
        if key in (e.get('Bind') or ''):
            e['Samples'] = s
            break
    else:
        raise SystemExit(f'no samples for {e.get("Name")}: {e.get("Bind")}')
    if e.get('Bind') == 'gearText': e['Empty'] = 'N'
    e['PreviewText'] = e['Samples'][0]
    # before a lap has a time SimHub formats the zero time ("00.00.00"); the wheel shows Empty for a time of 0
    if (e.get('Format') or '').startswith('time:'):
        e['Empty'] = e['Format'][5:].replace('\\', '').replace('m', '0').replace('ss', '00').replace('f', '0')
        if e['Empty'] not in e['Samples']: e['Samples'] = e['Samples'] + [e['Empty']]   # it must fit too

# ---------------------------------------------------------------------------------------------------------------------
# 2b. The rev bar: 13 segments, each a SimHub gauge of [Rpms] from 0 to its own maximum, [CarSettings_MaxRPM] x k/14
# (a formula), filled with a light grey picture ("rpm"). The import kept neither: every segment ran to a fixed 50 rpm
# (always full) in a see-through colour. A bar's maximum here is a number, so each segment's value is its fill in
# percent of that maximum (the same fill as SimHub's, 0..100), in the picture's light grey.
rpm_pic = orig.dash.image('rpm').convert('RGB')
from collections import Counter as _C
rpm_grey = '#%02X%02X%02X' % _C(rpm_pic.getdata()).most_common(1)[0][0]
for o in [o for o in orig.all if o.type == 'LinearGaugeItem']:
    e = el(o.name, 'bar')
    mx = K.simhub_ref.formula(o.it, 'Maximum')
    val = K.simhub_ref.formula(o.it, 'Value')
    assert val == '[Rpms]' and mx and e['Bind'] == 'ncalc:[Rpms]', (o.name, val, mx, e['Bind'])
    e['Bind'] = f'ncalc:if(({mx}) > 0, [Rpms] / ({mx}) * 100, 0)'
    e.update(Min=0, Max=100, Color=rpm_grey)
# The segments' dark red backgrounds are see-through (#A08B0000: 63 % over the black under them): mixed over black.
for e in E:
    for k in ('Color', 'Fill'):
        c = e.get(k)
        if e['Type'] in ('rect', 'box') and isinstance(c, str) and len(c) == 9 and c[1:3].upper() != 'FF':
            a = int(c[1:3], 16) / 255
            e[k] = '#' + ''.join('%02X' % round(int(c[i:i + 2], 16) * a) for i in (3, 5, 7))
# Each segment over its background box: SimHub's gauge (25 x 30) is a little bigger than the box (20 x 26) and starts 5
# px left of it, so what a shrinking segment leaves is black and dark red, not one colour: every step down repainted it
# from the RAM drive's tile, and that tile's area the segments beside it, in a chain (up to 56 KB a segment in a second,
# with flashes). Each segment takes its box's place and colour as its empty part (one box: the dark red when empty, the
# grey as far as it fills, 2 px narrower and shorter than SimHub's when full); the boxes go.
for k in range(1, 14):
    bar, box = el(f'Gauge Item {k}', 'bar'), el(f'Item {k}', 'box')
    bar.update(X=box['X'], Y=box['Y'], W=box['W'], H=box['H'], Fill=box.get('Fill') or box['Color'])
    E.remove(box)

# ---------------------------------------------------------------------------------------------------------------------
# 3. Overlays over SimHub's whole width cover the wheel's (0..790, not the scaled dash's 12..778): the ignition screens.
for e in E:
    if e['Type'] not in ('rect', 'box') or not own(e): continue
    if e['X'] <= 12: e['W'] += e['X']; e['X'] = 0
    if e['X'] + e['W'] >= 778: e['W'] = 790 - e['X']
    if e['Y'] + e['H'] > 460: e['H'] = 460 - e['Y']

# ---------------------------------------------------------------------------------------------------------------------
# 4. Panels, lines and fixed texts drawn as SimHub draws them.
# The small cold / hot tiles behind a tyre's or a brake's number (blue under 160 kPa / 230 deg, red over 190 / 800):
# the number's text covers almost all of the tile, so its shading would be under the text (flattened away anyway), and
# 24 of them as pictures would fill the RAM drive. Each is a rounded box the screen draws itself (no RAM, ~150 B), in
# the tile's colour with its border.
def tile(e, o):
    it = o.it
    bs = it.get('BorderStyle') or {}
    b = max(1, round(bs.get('BorderTop', 0) * o.fs))
    return {'Type': 'box', 'Name': e['Name'], 'X': e['X'], 'Y': e['Y'], 'W': e['W'], 'H': e['H'],
            'Color': '#' + (bs.get('BorderColor') or it['BackgroundColor'])[-6:], 'Fill': '#' + it['BackgroundColor'][-6:], 'Border': b,
            'Radius': max(2, round(bs.get('RadiusTopLeft', 0) * o.fs)), 'Visible': e.get('Visible'), 'PreviewVisible': e.get('PreviewVisible')}


def is_tile(e, o):
    return e['Type'] == 'image' and e.get('Name') == 'Background' and o is not None and o.it.get('Image') == 'shadow' and o.h < 50 and own(e)


# A caption the tile's overlay draws again where the same caption always shows (the tiles' "P_RR", "T_FL"...): the same
# pixels drawn again; left out (one RAM-drive picture less each).
def same_text(a, b):
    return a.type == b.type == 'TextItem' and a.it.get('Text') == b.it.get('Text') and abs(a.x - b.x) < 1 and abs(a.y - b.y) < 1 \
        and a.it.get('TextColor') == b.it.get('TextColor') and a.it.get('FontSize') == b.it.get('FontSize')         and a.it.get('Opacity', 100) == b.it.get('Opacity', 100) and (b.it.get('BackgroundColor') or '#00')[:3] == '#00'


def covered_before(e):
    """Something of e's own overlay drawn before it reaches into it (a panel the caption is drawn on): not a repeat."""
    # (by more than a pixel: the tiles end where their caption starts)
    return any(x['Type'] in ('image', 'gradient', 'rect', 'box') and vis(x) == vis(e) and x['X'] + 1 < e['X'] + e['W'] and e['X'] + 1 < x['X'] + x['W']
               and x['Y'] + 1 < e['Y'] + e['H'] and e['Y'] + 1 < x['Y'] + x['H'] for x in E[:E.index(e)])


static_kinds = ('image', 'gradient', 'label')


def mergeable(e):
    """Never-changing parts of a panel: its picture, gradients (panels, lines), fixed texts (no colour formula). Not the
    ignition logos (7.), not a text that blinks on its own."""
    if e['Type'] not in static_kinds or e.get('ColorBind') or e.get('Name') == 'LOGO': return False
    if e['Type'] == 'label' and not (e.get('Text') or '').strip(): return False
    return True


drawn = {id(e): K.drawn_of(e, orig, SETS) for e in E if mergeable(e) or e['Type'] in ('rect', 'box')}
missing = [f"{e['Type']} {e.get('Name')} {vis(e)}" for e in E if mergeable(e) and drawn[id(e)] is None]
assert not missing, missing

dropped, tiles, groups = [], 0, {}
for e in list(E):
    o = drawn.get(id(e))
    if is_tile(e, o):
        E[E.index(e)] = tile(e, o); tiles += 1
        continue
    if not mergeable(e): continue
    if e['Type'] == 'label' and own(e) and not covered_before(e):
        always = [x for x in E if x is not e and mergeable(x) and drawn.get(id(x)) is not None and not own(x)
                  and K.pages_of(x) == K.pages_of(e) and same_text(drawn[id(x)], o)]
        if always:
            E.remove(e); dropped.append(e.get('Name')); continue
    groups.setdefault(tuple(vis(e)), []).append(e)

# The import's own extras for a text's background or frame (SimHub draws them as part of the text item): drawn with it.
for e in [e for e in E if e['Type'] in ('rect', 'box') and (e.get('Name') or '').endswith((' background', ' frame'))]:
    base = e['Name'].rsplit(' ', 1)[0]
    if any(m.get('Name') == base for m in groups.get(tuple(vis(e)), [])) and 'IGN' not in base:
        E.remove(e)

# How each group becomes pictures (RAM-drive files: each file costs its JPEG plus ~1 KB of tables and accounting, and
# files of the same pixels are one file, named by their hash):
# - always shown: each part its own picture (the static layer: tiles with the RAM patch, rectangles in their own
#   colours without);
# - an overlay or a page: its parts that touch each other as one picture (the pit limiter's PIT panel at the top and its
#   speed panel are apart: two, not one picture of everything between them);
# - a panel the original draws the same in the same place for several overlays (the four setting pop-ups, the lap-time
#   panel's five pages, the delta's lap summaries, the two energy warnings): the panel alone, and its captions as a
#   picture of their own, so all of them use one file of the panel.
def touching(a, b):
    return a.x < b.x + b.w + 2 and b.x < a.x + a.w + 2 and a.y < b.y + b.h + 2 and b.y < a.y + a.h + 2


def components(members):
    out = []
    for m in members:
        joined = [c for c in out if any(touching(drawn[id(m)], drawn[id(x)]) for x in c)]
        for c in joined: out.remove(c)
        out.append(sum(joined, []) + [m])
    return [sorted(c, key=members.index) for c in out]


def is_panel(o):
    """A panel's own drawing: its shading picture on its box, its gradients and lines (not icons, not texts)."""
    return (o.type == 'ImageItem' and o.it.get('Image') in ('shadow', 'shadow 2')) or o.type == 'GradientItem'


# (icons are pictures of their own: the energy warnings' triangle is still in one and blinks in the other, so the rest
# of the two warnings is the same picture)
chunks = []
for key, members in groups.items():
    icons = [m for m in members if drawn[id(m)].type == 'ImageItem' and not is_panel(drawn[id(m)])]
    rest = [m for m in members if m not in icons]
    chunks += [(key, [m]) for m in icons]
    if not key: chunks += [(key, [m]) for m in rest]
    else: chunks += [(key, c) for c in components(rest)]
# the panel part of each chunk (its pictures and gradients), drawn alone: the same pixels in the same place in two
# chunks -> both split
def panel_png(chunk):
    pic = K.draw_items(orig, [drawn[id(m)] for m in chunk if is_panel(drawn[id(m)])])
    return None if pic is None else (pic[1], pic[2], pic[0].tobytes())


panel_of = {i: panel_png(c) for i, (k, c) in enumerate(chunks) if k}
shared = {i for i in panel_of if panel_of[i] and sum(1 for j in panel_of if panel_of[j] == panel_of[i]) > 1
          and any(not is_panel(drawn[id(m)]) for m in chunks[i][1])}
pieces = []
for i, (key, chunk) in enumerate(chunks):
    if i in shared:
        pieces.append([m for m in chunk if is_panel(drawn[id(m)])])
        pieces.append([m for m in chunk if not is_panel(drawn[id(m)])])
    else:
        pieces.append(chunk)

pictures = 0
panel_items = {}   # picture -> the original items drawn into it
for chunk in pieces:
    pic = K.draw_items(orig, [drawn[id(m)] for m in chunk])
    first = chunk[0]
    if pic is None:
        for m in chunk: E.remove(m)
        continue
    im, x, y = pic
    name = first.get('Name') if len(chunk) == 1 else ' + '.join(dict.fromkeys(m.get('Name') for m in chunk))
    k = f'panel {name} {pictures}@{im.width}x{im.height}'
    panel_items[k] = [drawn[id(m)] for m in chunk]
    d.setdefault('Images', {})[k] = picture(im)
    E[E.index(first)] = {'Type': 'image', 'Name': name, 'Image': k, 'X': x, 'Y': y, 'W': im.width, 'H': im.height,
                         'Visible': first.get('Visible'), 'PreviewVisible': first.get('PreviewVisible'), 'MaxColors': 4}
    for m in chunk[1:]: E.remove(m)
    pictures += 1

# ---------------------------------------------------------------------------------------------------------------------
# 5. Every value in the original's size and place: each one measured from its original item with the original's font
# (CPMono), the screen font closest to it that fits picked, and its band centred on where the original's ink is. Items
# the original draws in the same font and size get one font together (convert_kit.place_group). Areas: the original's
# box on the wheel, unless AREAS says otherwise.
AREAS = {   # (name, a text in a condition or '') -> (x0, y0, x1, y1)
    # the gear: from under its GEAR caption to the bottom row of panels, between the PIT text and the icons
    ('GearText', ''): (320, 164, 500, 288),
    # the delta panel's lap-summary driver name: between the panel's top border (375) and its line (405)
    ('LeaderboardOpponentNameText', ''): (269, 377, 521, 403),
    # the front anti-roll bar, right of its "F": between the ARB panel's top border (293) and its line (327)
    ('F#', ''): (299, 295, 346, 325),
    # the session's name: its panel (33 px) is a gradient for its top 9 px, then flat; the name below it, clear of the
    # rounded corners (on the gradient every change repaints it; flattened, the panel would be gone)
    ('RACE 1 Tag', ''): (26, 9, 182, 33),
}
# (name, a text in a condition or '') -> screen font, where the closest isn't the right one: those two rows are 26 and
# 30 px, and 24 px (107) is the screen's smallest font spaced like a normal one
# The tyre and brake numbers: their cold / hot tiles are 44 px with a 3 px border, so 32 px (101, ink ~24 px, the
# original's ~26) is the most that fits; all of them in it, as the original draws them all at one size (the red tiles'
# numbers got 32 px from fit-bands, the others 40: a corner's number changed size with its colour)
# (driver names need lower case: 60, the narrowest small font with all of ASCII)
FONTS = {('LeaderboardOpponentNameText', ''): 60, ('F#', ''): 107, ('RACE 1 Tag', ''): 60}
for n in ('P_FL', 'P_FL2', 'P_FR', 'P_RL', 'P_RR', 'T_FL', 'T_FL2', 'T_FR', 'T_RL', 'T_RR', 'Brake Temperature Front Left',
          'Brake Temperature Front Right', 'Brake Temperature Rear Left', 'Brake Temperature Rear Right'):
    FONTS[(n, '')] = 101   # (name, a text in a condition or '') -> screen font, where the closest isn't the right one


def texts_of(e):
    return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def lookup(table, e):
    for (name, cond), v in table.items():
        if e.get('Name') == name and (cond == '' or any(cond in c for c in vis(e))): return v
    return None


def grown(area, dx, dy):
    x0, y0, x1, y1 = area
    return (max(0, x0 - dx), max(0, y0 - dy), min(790, x1 + dx), min(460, y1 + dy))


groups = {}
for e in E:
    if e['Type'] not in ('label', 'value') or not texts_of(e)[0].strip(): continue
    item = K.match(e, orig)
    area = lookup(AREAS, e) or (max(0, int(item.x)), max(0, int(item.y)), min(790, int(item.x + item.w)), min(460, int(item.y + item.h)))
    kw = {'chars': '0123456789NR'} if e.get('Bind') == 'gearText' else {}
    if lookup(FONTS, e):
        K.place(e, orig, item, texts_of(e), area, font=lookup(FONTS, e))
        continue
    groups.setdefault((item.family, round(item.em, 1), item.weight, e.get('Bind') == 'gearText'), []).append((e, item, texts_of(e), area, kw))

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

# A value's box reaching into another text shown with it (the original's boxes overlap, only their ink doesn't): every
# change would redraw the other one (a flash). Such boxes shrink to their widest text.
shrunk, _ = K.shrink_to_text(E)

# ---------------------------------------------------------------------------------------------------------------------
# 6. The ignition screens: the Ferrari logo on black (ignition off; SimHub's black rectangle, then the logo) and the same
# for 1.5 s when the ignition goes on. A black rectangle with only the logo's own area as a picture over it (the RAM
# drive holds the logo, not a screenful of black); the import's own full-screen background under it is the same black.
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') == 'LOGO']:
    on = any('changed(' in c for c in vis(e))
    K.logo_on_plain(d, E, e, f'ferrari {"ignition on" if on else "ignition off"}', 6)
for e in [e for e in E if e['Type'] == 'rect' and e.get('Name') in ('IGN OFF background', 'IGN ON background', 'RectangleItem', 'LOGO background')
          and any('EngineIgnitionOn' in c for c in vis(e))]:
    e.update(X=0, Y=0, W=790, H=460)
# The renderer makes no RAM-drive picture of a shape over a bar's box (the logo reaches over the rev bar): drawn with
# rectangles, the logo took 161 KB (6.5 s) each time the ignition went on, with the RAM patch too. Each logo is cut in
# strips (convert_kit.strips_around_bars): the rows level with the rev bar (rectangles, a few KB), and the rest in
# pictures of at most ~170 rows (one file stays under the drive's 24 KB a picture: the whole logo is 25 KB as one).
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') == 'LOGO']:
    K.strips_around_bars(d, E, e)
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]

# ---------------------------------------------------------------------------------------------------------------------
# 7. Values on panels: their text drawn on one colour. Over the panel's shading every change would repaint the picture
# under it; the shading under each value's text rows is flattened to the panel's colour there (the panels are clear in
# the middle, where the values are: little changes), and the value is drawn on that colour (Background).
def band(v):
    fh = K.height(v['Font'])
    y = v['Y'] + (v['H'] - fh) // 2
    return (v['X'], y, v['X'] + v['W'], y + fh)


def no_border(o):
    """The original item with its box's border in another colour (not without it: the picture is stretched inside the
    border, so it would move)."""
    bs = o.it.get('BorderStyle')
    if not bs: return o
    c = copy.copy(o)
    c.it = dict(o.it, BorderStyle=dict(bs, BorderColor='#FF01FE03'))
    return c


_lines = {}


def screen_lines(screen):
    """Every caption line of a screen of the original, drawn alone."""
    if screen not in _lines:
        _lines[screen] = K.draw_items(orig, [o for o in orig.all if o.name == 'Line' and o.screen == screen], crop=False)[0]
    return _lines[screen]


def flatten_values():
    """A panel that holds a value is made flat: its shading (what the panel's own picture and gradients draw) becomes the
    colour at the middle of the value's text rows, over the inside of the panel around the value as far as the shading
    runs smoothly (a border or a caption stops it: those stay, found by drawing the panel without them) and up to the
    line under the caption (the caption's part keeps its shading: the step sits on the line).
    Text that changes needs one colour under it to redraw in one step; flattening only the rows under the text left a
    flat stripe across the shading, which read as a box behind the number (seen on the wheel)."""
    from PIL import Image
    from collections import Counter
    n = 0
    for v in [e for e in E if e['Type'] == 'value']:
        # (the session's name changes once a session: it stays on its panel's gradient, which its text fills top to
        # bottom; flattened, the panel would be gone)
        if '[SessionTypeName]' in (v.get('Bind') or ''): continue
        b = band(v)
        under = [p for p in E[:E.index(v)] if p['Type'] == 'image' and p['Image'] in panel_items and set(vis(p)) <= set(vis(v))
                 and p['X'] < b[2] and b[0] < p['X'] + p['W'] and p['Y'] < b[3] and b[1] < p['Y'] + p['H']]
        if not under: continue
        p = under[-1]
        items = panel_items[p['Image']]
        back = K.draw_items(orig, [o for o in items if is_panel(o) and o.name != 'Line'], crop=False)
        if back is None: continue   # a caption's picture, no panel
        back = back[0].crop((p['X'], p['Y'], p['X'] + p['W'], p['Y'] + p['H']))
        full = K.draw_items(orig, items, crop=False)[0].crop((p['X'], p['Y'], p['X'] + p['W'], p['Y'] + p['H']))
        # (the panel's border: what drawing it in another colour changes; it stays)
        nob = K.draw_items(orig, [no_border(o) for o in items if is_panel(o) and o.name != 'Line'], crop=False)[0]             .crop((p['X'], p['Y'], p['X'] + p['W'], p['Y'] + p['H'])).load()
        # (the line under the caption, in this picture or the caption's own: the flat part stops at it, the caption's
        # part of the panel keeps its shading)
        withl = screen_lines(items[0].screen).crop((p['X'], p['Y'], p['X'] + p['W'], p['Y'] + p['H'])).load()
        im = Image.open(io.BytesIO(base64.b64decode(d['Images'][p['Image']]))).convert('RGBA')
        x0, x1 = max(0, b[0] - p['X']), min(im.width, b[2] - p['X'])
        y0, y1 = max(0, b[1] - p['Y']), min(im.height, b[3] - p['Y'])
        if x1 <= x0 or y1 <= y0: continue
        fp, bp, px = full.load(), back.load(), im.load()
        lines = [y for y in range(im.height) if any(withl[x, y][3] for x in range(x0, x1))]
        top = max([y + 1 for y in lines if y < y0], default=0)
        bottom = min([y for y in lines if y >= y1], default=im.height)
        mine = lambda x, y: top <= y < bottom and fp[x, y] == bp[x, y] == nob[x, y] and fp[x, y][3] >= 250   # the panel's own pixel (no line, caption or border)
        mid = (y0 + y1) // 2
        cnt = Counter(fp[x, mid] for x in range(x0, x1) if mine(x, mid))
        if not cnt: continue
        base = cnt.most_common(1)[0][0]
        # the inside: from the pixels under the text, every panel pixel reached through smooth shading (neighbours
        # within 24 per channel: the shading's steps are a few levels, a border's or line's edge many)
        seen = set((x, y) for y in range(y0, y1) for x in range(x0, x1) if mine(x, y))
        todo = list(seen)
        while todo:
            x, y = todo.pop()
            c = fp[x, y]
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < im.width and 0 <= ny < im.height and (nx, ny) not in seen and mine(nx, ny)                         and max(abs(fp[nx, ny][i] - c[i]) for i in range(3)) <= 24:
                    seen.add((nx, ny)); todo.append((nx, ny))
        for x, y in seen: px[x, y] = base
        d['Images'][p['Image']] = picture(im)
        v['Background'] = '#%02X%02X%02X' % base[:3]
        n += 1
    return n


flat = flatten_values()

# 8. Values on their own overlay's box (the tyre and brake tiles): drawn on its colour; a value over a panel's edge by a
# pixel or two moved inside it, 2 px clear of a tile's border.
K.snap_into_panels(E)
K.backgrounds_from_overlays(E)
K.keep_inside_boxes(E)
# A number the original draws again on an overlay where it's always drawn (a tyre's pressure on its cold tile, a brake's
# temperature on its hot tile: the same formula in the same place): the one under hides while the overlay shows. Both
# shown, each change of one would redraw the other.
twins = 0
for v in [e for e in E if e['Type'] == 'value' and own(e)]:
    for u in E[:E.index(v)]:
        if u['Type'] != 'value' or u.get('Bind') != v.get('Bind') or K.pages_of(u) != K.pages_of(v): continue
        if not set(own(u)) < set(own(v)) or not (u['X'] < v['X'] + v['W'] and v['X'] < u['X'] + u['W'] and u['Y'] < v['Y'] + v['H'] and v['Y'] < u['Y'] + u['H']): continue
        no = K.negation([c for c in own(v) if c not in own(u)])
        if no and no not in vis(u):
            u['Visible'] = vis(u) + [no]; twins += 1
            # (the overlay's preview off, with all of it: the designer shows the plain dash, its numbers in white)
            for x in E:
                if own(x) == own(v) and K.pages_of(x) == K.pages_of(v): x['PreviewVisible'] = False
# Values half under an overlay's box hide while it shows (convert_kit.take_turns): else each change drew them, then
# the box over them.
# (the panels are pictures with rounded corners: they count as covering, corners aside)
turns = K.take_turns(E, d['Images'], rounded=True)
# An overlay's panel picture is drawn from the RAM drive only over exactly what it was made over (the static layer and
# its own overlay's shapes); with a value of the dash under it (the TC and ABS numbers under their OFF panels, the speed
# and tyres under the lap summary, the bottom row under CLUTCH + START) the picture is drawn with rectangles instead
# (25-70 KB each time it shows). Values under an overlay's panel, even wholly, hide while it shows; so do another
# overlay's shapes and pictures there (a cold tyre's tile, the wiper's icon under the lap summary: 55 KB of rectangles
# when both showed).
for p in [e for e in E if e['Type'] == 'image' and own(e) and not K.pages_of(e)]:
    no = K.negation(own(p))
    if no is None: continue
    for v in E[:E.index(p)]:
        if v['Type'] not in ('value', 'bar', 'box', 'rect', 'image', 'label') or set(own(p)) <= set(own(v)) or no in vis(v): continue
        if v['Type'] not in ('value', 'bar') and not own(v) and not K.pages_of(v): continue   # the static layer: what it's drawn over
        b = band(v) if v['Type'] == 'value' else (v['X'], v['Y'], v['X'] + v['W'], v['Y'] + v['H'])
        if p['X'] < b[2] and b[0] < p['X'] + p['W'] and p['Y'] < b[3] and b[1] < p['Y'] + p['H']:
            # (a shape of another overlay: all of that overlay takes turns with it, its numbers too)
            for x in ([v] if v['Type'] in ('value', 'bar') else [x for x in E if own(x) == own(v) and K.pages_of(x) == K.pages_of(v)]):
                if no not in vis(x): x['Visible'] = vis(x) + [no]; turns += 1
# The parts of an overlay that come and go inside it (the energy warning's blinking triangle on its red panel) take turns
# as the overlay does: the renderer bakes the panel into the triangle's picture only when the panel shows whenever the
# triangle does; with a take-turns condition the triangle lacked, the triangle was drawn with rectangles (33 KB a blink).
for x in E:
    negs = [c for c in vis(x) if c not in own(x) and not K.PAGE.match(c)]
    if not negs or not own(x): continue
    for y in E:
        if y is not x and set(own(x)) < set(own(y)) and K.pages_of(y) == K.pages_of(x):
            for c in negs:
                if c not in vis(y): y['Visible'] = vis(y) + [c]; turns += 1

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); then the
# shading under the moved ones flattened again; a second run must change nothing (the /create-dash gate)
bands = K.fx('fit-bands', OUT)['changes']
d = json.load(open(OUT, encoding='utf-8')); E = d['Elements']
flatten_values()
json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
again = K.fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'pictures': pictures, 'tiles': tiles, 'dropped': dropped,
                  'unplaced': unplaced, 'widened': widened, 'shrunk': sorted(set(shrunk)), 'flattened': flat, 'twins': twins, 'take_turns': turns,
                  'fit_bands': bands, 'import_notes': report['Notes']}, indent=1))
