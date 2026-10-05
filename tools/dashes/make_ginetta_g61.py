"""
The "LMP3 Ginetta G61" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_ginetta_g61.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmp3-ginetta-g61.json)

Needs SimHub installed with the SimHub dash "LMP3 Ginetta G61" (DashTemplates), Windows' Lucida Sans and Arial (the
original's fonts, used to measure its texts) and fxdash built (dotnet build -c Release tools/fxdash/fxdash.csproj).
Re-running it re-imports from the SimHub dash, so every change to the conversion lives here, with its reason (the
/create-dash way; make_mustang.py is the bigger example). `ginetta_g61_parity.py` checks the result item by item.

What the import brings over by itself: the main screen, the tyre widget's three screens (tyre pressures, brake
temperatures, tyre temperatures: SimHub flips them with screen commands) as the dash's three pages, the pit limiter
banner, the ignition-off screen (with its blinking "PRESS IGNITION TO START") and the one-second ignition-on screen as
overlays, the 30-40 % see-through layers mixed into their colours. What this script changes, and why, is below.

`fxdash tune` is not used: it doesn't know that the overlays and pages exclude each other, and shrank the lap times and
the speed to clear the ignition logo they never show with.
"""
import base64, io, json, os, subprocess, sys, tempfile
from PIL import Image, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FX = os.path.join(REPO, 'tools', 'fxdash', 'bin', 'Release', 'net48', 'fxdash.exe')
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmp3-ginetta-g61.json'
SIMHUB_DASH = 'LMP3 Ginetta G61'
SIMHUB = os.environ.get('SIMHUB_INSTALL_PATH', r'C:\Program Files (x86)\SimHub')
SRC = os.path.join(SIMHUB, 'DashTemplates', SIMHUB_DASH)


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


SAMPLES = {}


def fonts(text):
    """Every screen font with the width of `text` in it (-1: a glyph missing)."""
    if text not in SAMPLES:
        SAMPLES[text] = {x['id']: x for x in fx('fonts', '--sample', text)}
    return SAMPLES[text]


def width(font, text): return fonts(text)[font]['sampleWidth']
def height(font): return fonts('0')[font]['height']


work = tempfile.mkdtemp()
raw = os.path.join(work, 'import.json')
report = fx('import', SIMHUB_DASH, raw, '--fit', '790,460')['report']
d = json.load(open(raw, encoding='utf-8'))
E = d['Elements']


def vis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def page(e):
    for c in vis(e):
        if c.startswith('page:'):
            return int(c[5:])
    return None


def el(typ, name=None, pg=None, bind=None, text=None, cond=None):
    """The one element matching all that's given (bind: a text in its binding; cond: a text in one of its conditions)."""
    hits = [e for e in E if e['Type'] == typ and (name is None or e.get('Name') == name) and page(e) == pg
            and (bind is None or bind in (e.get('Bind') or '')) and (text is None or e.get('Text') == text)
            and (cond is None or any(cond in c for c in vis(e)))]
    assert len(hits) == 1, (typ, name, pg, bind, text, cond, len(hits))
    return hits[0]


# ---------------------------------------------------------------------------------------------------------------------
# The original's texts, measured: each one rendered with its own font (Lucida Sans / Arial at the size it has once the
# 1200 x 720 dash is scaled to the wheel's 766 x 460) in its box with its alignment. Each converted text gets the screen
# font closest to that in height and width, centred where the original's ink is.
SC, OX = 766 / 1200, 12
TTF = {'Lucida Sans': r'C:\Windows\Fonts\LSANS.TTF', 'Arial': r'C:\Windows\Fonts\arial.ttf'}


def original_items():
    out = []

    def walk(items, ox, oy, screen):
        for it in items or []:
            if it.get('IsTextItem') or 'GearText' in it.get('$type', ''):
                out.append((screen, ox, oy, it))
            walk(it.get('Childrens'), ox, oy, screen)
    main = json.load(open(os.path.join(SRC, SIMHUB_DASH + '.djson'), encoding='utf-8-sig'))
    for s in main['Screens']:
        walk(s['Items'], 0, 0, s['Name'])
    widget = json.load(open(os.path.join(SRC, 'TyresBrakes.djson'), encoding='utf-8-sig'))
    for s in widget['Screens']:
        walk(s['Items'], 0, 250, s['Name'])   # the widget sits at (0, 250) in the main screen, at its own size
    return out


ORIG = original_items()


def measure(screen, name, text, bind=None):
    """The original item's em (px on the wheel), advance width of `text`, and ink box (x0, y0, x1, y1) on the wheel."""
    hits = [(ox, oy, it) for s, ox, oy, it in ORIG if s == screen and it.get('Name') == name
            and (bind is None or bind in json.dumps(it.get('Bindings') or {}))]
    assert len(hits) == 1, (screen, name, bind, len(hits))
    ox, oy, it = hits[0]
    family = it.get('Font', 'Lucida Sans') if it.get('IsTextItem') else it.get('Font', 'Lucida Sans')
    em = it['FontSize'] * SC
    f = ImageFont.truetype(TTF[family], em)
    asc, desc = f.getmetrics()
    x, y, w, h = (ox + it['Left']) * SC + OX, (oy + it['Top']) * SC, it['Width'] * SC, it['Height'] * SC
    l, t, r, b = f.getbbox(text, anchor='la')
    adv = f.getlength(text)
    ha, va = it.get('HorizontalAlignment', 0), it.get('VerticalAlignment', 0)
    tx = x if ha == 0 else x + (w - adv) / 2 if ha == 1 else x + w - adv
    ty = y if va == 0 else y + (h - asc - desc) / 2 if va == 1 else y + h - asc - desc
    return em, adv, (tx + l, ty + t, tx + r, ty + b)


def score(fid, texts, em, adv, area_w, area_h, chars_needed=None):
    """How far font `fid` is from the original's size (height ~ em, width of the first text ~ its advance), or None
    when the texts don't fit the area."""
    x = fonts(texts[0])[fid]
    if x['height'] > area_h: return None
    ws = [width(fid, t) for t in texts]
    if min(ws) < 0 or max(ws) > area_w - 4: return None
    if chars_needed and not all(c in x['chars'] for c in chars_needed): return None
    return abs(x['height'] - em) / em + abs(ws[0] - adv) / adv


def choose(texts, em, adv, area_w, area_h, chars_needed=None):
    """The screen font closest to the original's size that fits."""
    scored = [(sc, fid) for fid in fonts(texts[0]) if (sc := score(fid, texts, em, adv, area_w, area_h, chars_needed)) is not None]
    assert scored, (texts, em, adv, area_w, area_h)
    return min(scored)[1]


def place_group(specs):
    """Texts the original draws in one size (the lap times, a page's four corners...): one font for all of them, the
    closest to the original over the group that fits every area. specs: (element, place()'s arguments as a dict)."""
    meas = [(measure(a['screen'], a['name'], a['texts'][0], a.get('bind')), a) for _, a in specs]
    best = None
    for fid in fonts(specs[0][1]['texts'][0]):
        total = 0
        for (em, adv, _), a in meas:
            x0, y0, x1, y1 = a['area']
            sc = score(fid, a['texts'], em, adv, x1 - x0, y1 - y0, a.get('chars'))
            if sc is None: total = None; break
            total += sc
        if total is not None and (best is None or total < best[0]):
            best = (total, fid)
    assert best, [a['name'] for _, a in specs]
    for e, a in specs:
        place(e, font=best[1], **a)


def place(e, screen, name, texts, area, bind=None, align=None, chars=None, label=False, font=None):
    """Font and box for element `e` from the original item: area = (x0, y0, x1, y1) it may use (inside its frame's
    lines with 2 px to spare, clear of its neighbours). A label's box is its text; a value's is its font's band, as
    wide as the area allows around where the original's text is."""
    em, adv, ink = measure(screen, name, texts[0], bind)
    x0, y0, x1, y1 = area
    font = font or choose(texts, em, adv, x1 - x0, y1 - y0, chars)
    fh = height(font)
    # the band centred on the ink: a font's capitals and digits sit a little above the middle of its band
    cy = (ink[1] + ink[3]) / 2 + 0.06 * fh
    top = int(round(min(max(cy - fh / 2, y0), y1 - fh)))
    align = align or e.get('Align', 'left')
    tw = max(width(font, t) for t in texts)
    if label:
        w = tw + 4
        bx = ink[0] - 2 if align == 'left' else ink[2] + 2 - w if align == 'right' else (ink[0] + ink[2]) / 2 - w / 2
        bx = int(round(min(max(bx, x0), x1 - w)))
    elif align == 'center':
        cx = (ink[0] + ink[2]) / 2
        half = min(cx - x0, x1 - cx)
        bx, w = int(round(cx - half)), int(half * 2)
        if w < tw + 4:   # near the area's edge: as wide as the text needs, moved in as little as it takes
            w = tw + 4
            bx = int(round(min(max(cx - w / 2, x0), x1 - w)))
    elif align == 'right':
        right = min(int(round(ink[2])) + 2, x1)
        bx, w = x0, right - x0
    else:
        left = max(int(round(ink[0])) - 2, x0)
        bx, w = left, x1 - left
    e.update(Font=font, X=bx, Y=top, W=int(w), H=fh, Align=align)
    return e


# ---------------------------------------------------------------------------------------------------------------------
# 1. Identity
d['Id'] = 'lmp3-ginetta-g61'
d['Name'] = 'LMP3 Ginetta G61'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "LMP3 Ginetta G61" '
                    '(https://lmu-dashboards.com). Three pages in the tyre panel: tyre pressures, brake temperatures and '
                    'tyre temperatures (bind Next / Previous page to a wheel button).')
d['Source'] = 'Redadeg\'s SimHub dash "LMP3 Ginetta G61", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions aren't used by any formula
d['FormatVersion'] = 3      # pages
assert d['Pages'] == ['T PRESS', 'BRAKE TEMP', 'T TEMP'], d.get('Pages')

# ---------------------------------------------------------------------------------------------------------------------
# 2. Tyre pressures. The formula returns text in psi and kPa (format(x, '0.0') / '0') and the raw number otherwise
# (bar), and SimHub's '0.00' format then applies to the number only: "26.9" psi, "185" kPa, "1.85" bar. The wheel
# formats text that reads as a number too ("26.90"), so the formula formats every unit itself, shown as it comes.
for e in E:
    if e['Type'] == 'value' and page(e) == 0:
        b = e['Bind']
        key = b[b.index('format([') + 8:b.index('],')]
        tail = f"[{key}]))"
        assert b.endswith(tail), b
        e['Bind'] = b[:-len(tail)] + f"format([{key}], '0.00')))"
        e['Format'] = 'text'

# ---------------------------------------------------------------------------------------------------------------------
# 3. The tyre panel's frame is the same on all three pages: one frame, always shown (a page flip then redraws only
# the numbers, not the frame).
frames = [e for e in E if e['Type'] == 'box' and page(e) is not None]
assert len(frames) == 3 and all((f['X'], f['Y'], f['W'], f['H']) == (frames[0]['X'], frames[0]['Y'], frames[0]['W'], frames[0]['H']) for f in frames)
frames[0]['Visible'] = None
for f in frames[1:]:
    E.remove(f)

# ---------------------------------------------------------------------------------------------------------------------
# 4. Every text: the original's size and place (see measure / choose / place), with the widest text it really shows as
# its Samples (the import copies SimHub's preview text: "130", "2", "0:00.000"). Areas are inside the frame lines (the
# panels' 1 px lines at x 12 / 298-299 / 490-491 / 777 and y 0 / 95-96 / 159-160 / 239-240 / 318; the bottom row's
# 3-4 px frames), 2 px clear of them, so no text row crosses a line (fit-bands checks it after).
LAPTIME = ['8.88.888']
TEMP = ['888']

def spec(e, screen, name, texts, area, **kw):
    return (e, dict(screen=screen, name=name, texts=texts, area=area, **kw))


# fuel panel: last lap consumption over the fuel level, values right-aligned against their small labels (labels 25 px,
# values 100 px in the original: one font each)
lap, lvl = el('label', 'FUEL LAP'), el('label', 'FUEL LEVEL')
place_group([spec(lap, 'Main', 'FUEL LAP', ['L/LAP'], (15, 3, 296, 78), label=True),
             spec(lvl, 'Main', 'FUEL LEVEL', ['LEVEL'], (15, 81, 296, 156), label=True)])
stint, fuel = el('value', 'Stint'), el('value', 'FUEL LEVEL#')
stint['Samples'] = ['8.88']; fuel['Samples'] = ['888']
place_group([spec(stint, 'Main', 'Stint', ['8.88'], (15, 3, lap['X'] - 2, 79)),
             spec(fuel, 'Main', 'FUEL LEVEL#', ['888'], (15, 81, lvl['X'] - 2, 156))])

# tyre panel, three pages: four corners each, no lines between them in the original (only the panel's frame); all
# twelve numbers 100 px in the original: one font
corners = {'FL': (15, 163, 155, 239), 'FR': (156, 163, 296, 239), 'RL': (15, 240, 155, 315), 'RR': (156, 240, 296, 315)}
wheel_of = {'FrontLeft': 'FL', 'FrontRight': 'FR', 'RearLeft': 'RL', 'RearRight': 'RR', 'mWheels01': 'FL', 'mWheels02': 'FR', 'mWheels03': 'RL', 'mWheels04': 'RR'}
screens = ['T PRESS', 'BRAKE TEMP', 'T TEMP']
specs = []
for e in [e for e in E if e['Type'] == 'value' and page(e) is not None]:
    key = next(k for k in wheel_of if k in e['Bind'])
    e['Samples'] = ['88.8', '888', '8.88'] if page(e) == 0 else TEMP if page(e) == 1 else ['188']
    e['PreviewText'] = '26.9' if page(e) == 0 else '450' if page(e) == 1 else '85'
    # the original's items are named after their layer, not their wheel (rear right is "FR"): found by their binding
    specs.append(spec(e, screens[page(e)], e['Name'], e['Samples'], corners[wheel_of[key]], bind=key))
place_group(specs)

# speed and gear
v = el('value', 'SPEED')
v['Samples'] = ['388']; v['PreviewText'] = '60'
place(v, 'Main', 'SPEED', ['388'], (302, 3, 488, 93))
g = el('value', 'GearText')
g['Samples'] = ['8', 'N', 'R']; g['PreviewText'] = 'N'; g['Empty'] = 'N'
place(g, 'Main', 'GearText', ['N', '8', 'R'], (302, 99, 488, 316), chars='0123456789NR')

# lap times: label over value, one font for the three labels and one for the three values (as in the original)
rows = [('BEST', 'BEST#', 3, 90), ('LAST', 'LAST#', 82, 166), ('PRED', 'PRED#', 158, 237)]
place_group([spec(el('label', l), 'Main', l, [l], (494, y0, 775, y1), label=True) for l, _, y0, y1 in rows])
specs = []
for i, (l, vname, _, _) in enumerate(rows):
    lab, v = el('label', l), el('value', vname)
    bottom = el('label', rows[i + 1][0])['Y'] - 2 if i + 1 < len(rows) else 237
    v['Samples'] = LAPTIME; v['PreviewText'] = '1.23.456'
    # before a lap has a time SimHub formats the zero time ("0.00.000"); the wheel shows Empty for a time of 0
    v['Empty'] = '0.00.000'
    specs.append(spec(v, 'Main', vname, LAPTIME, (494, lab['Y'] + lab['H'] + 2, 775, bottom)))
place_group(specs)

# brake temperatures under the lap times (the original's: RL = rear left, "FR" = front left: kept, see the parity list)
specs = []
for name, bind, area in (('RL', 'BrakeTemperatureRearLeft', (494, 243, 634, 315)), ('FR', 'BrakeTemperatureFrontLeft', (635, 243, 775, 315))):
    v = el('value', name, bind=bind)
    v['Samples'] = TEMP; v['PreviewText'] = '450'
    specs.append(spec(v, 'Main', name, TEMP, area, bind=bind))
place_group(specs)

# headlights: MAIN LIGHT over OFF / BEAM, inside the cyan frame (315..475 x 321..410, 4 px). The screen's small fonts
# are too wide for "MAIN LIGHT" at the original's 19 px: the narrowest that fits is 32 px tall.
ml = place(el('label', 'Main Light'), 'Main', 'Main Light', ['MAIN LIGHT'], (321, 325, 469, 404), label=True)
place_group([spec(el('label', n), 'Main', n, [n], (321, ml['Y'] + ml['H'] + 2, 469, 404), label=True) for n in ('OFF', 'BEAM')])

# the bottom row's cells: TC and FPS (LMU's TC cut) in their 3 px frames, oil and water temperatures without one. Label
# at the top (25 px in the original), the number under it (90 px, bottom-aligned over the whole 180 x 140 cell, centred):
# one font for the four labels and one for the four numbers. The screen's smallest fonts are wide (16 px a letter:
# "WATER T" is 112 px, the original's 69), so the water cell runs to the wheel's edge (790; the original's scaled
# width ends at 778) and its label sits a few px right of the original's centre.
cells = []
for lname, vname, bind, fr in (('TC LEVEL', 'TC#', None, el('box', 'TC# frame')), ('FPS', 'TCC#', None, el('box', 'TCC# frame')),
                               ('OIL T', 'Water#', 'OilTemperature', None), ('WATER T', 'Water#', 'WaterTemperature', None)):
    area = (fr['X'] + 5, fr['Y'] + 5, fr['X'] + fr['W'] - 5, fr['Y'] + fr['H'] - 5) if fr else \
        (2, 324, 123, 410) if lname == 'OIL T' else (670, 324, 788, 410)
    cells.append((el('label', lname), lname, el('value', vname, bind=bind), vname, bind, area))
place_group([spec(l, 'Main', ln, [l['Text']], a, label=True) for l, ln, _, _, _, a in cells])
specs = []
for l, ln, v, vn, bind, a in cells:
    v['Samples'] = ['88'] if bind is None else TEMP
    v['PreviewText'] = '4' if bind is None else '85'
    if bind is None: v['Format'] = 'text'
    specs.append(spec(v, 'Main', vn, v['Samples'], (a[0], l['Y'] + l['H'] + 2, a[2], a[3]), bind=bind))
place_group(specs)
# the red boxes shown while TC works: SimHub draws each under its cell's frame (the purple frame over the red), so
# they go inside the frame's border here, plain rectangles (the frame stays on top, and they need no picture)
for frame in (el('box', 'TC# frame'), el('box', 'TCC# frame')):
    red = [e for e in E if e['Type'] == 'box' and vis(e) == ['ncalc:[TCActive]'] and (e['X'], e['Y'], e['W'], e['H']) == (frame['X'], frame['Y'], frame['W'], frame['H'])]
    assert len(red) == 1, frame['Name']
    b = frame['Border']
    red[0].update(Type='rect', X=frame['X'] + b, Y=frame['Y'] + b, W=frame['W'] - 2 * b, H=frame['H'] - 2 * b, Color=red[0]['Fill'], Border=0, Radius=0)
    red[0].pop('Fill')

# ---------------------------------------------------------------------------------------------------------------------
# 5. Pit limiter banner: SimHub's blue text box with a 4 px white border over the whole bottom row. A `box` without a
# radius that comes and goes became a 7 KB picture on the screen's RAM drive; two plain rectangles (white, then blue
# inside it) look the same, are two fills, and take no RAM.
pb = el('box', 'TextItem frame', cond='PitLimiterOn')
i = E.index(pb)
bw = pb['Border']
# SimHub's banner spans its whole screen: here the wheel's whole visible width (0..790, as the ignition screens), so it
# also covers the oil and water cells, which use the 12 px the dash's scaled width leaves at each side (half under it,
# they would be redrawn through it at every change).
pb['X'], pb['W'] = 0, 790
white = {'Type': 'rect', 'Name': 'pit limiter border', 'X': pb['X'], 'Y': pb['Y'], 'W': pb['W'], 'H': pb['H'], 'Color': pb['Color'],
         'Visible': pb['Visible'], 'PreviewVisible': False}
blue = {'Type': 'rect', 'Name': 'pit limiter', 'X': pb['X'] + bw, 'Y': pb['Y'] + bw, 'W': pb['W'] - 2 * bw, 'H': pb['H'] - 2 * bw,
        'Color': pb['Fill'], 'Visible': pb['Visible'], 'PreviewVisible': False}
E[i:i + 1] = [white, blue]
pl = el('label', 'TextItem', cond='PitLimiterOn')
place(pl, 'Main', 'TextItem', ['PIT LIMITER'], (blue['X'] + 2, blue['Y'] + 2, blue['X'] + blue['W'] - 2, blue['Y'] + blue['H'] - 2), label=True)
pl['Background'] = blue['Color']

# ---------------------------------------------------------------------------------------------------------------------
# 6. Ignition screens. SimHub draws each one as a whole screen: opaque black (the screen's background), then on the
# ignition-off screen a light grey layer at 40 % and the logo at 30 %, on the ignition-on screen opaque light grey and
# the logo at 30 %. The wheel gets exactly those pixels, flat:
# - each screen's background is the colour it ends up (black + 40 % grey = #545454; the light grey) over the wheel's
#   whole visible area (790 x 460: SimHub fills its whole screen; the dash's scaled width leaves 12 px at the sides),
#   one opaque rectangle (a see-through one was mixed with the dash under it, and the dash showed through);
# - the logo is mixed at 30 % over that colour into its own picture (the wheel draws pictures opaque: it came out at
#   full strength on a black square), one per screen, kept on the RAM drive with the background under it;
# - "PRESS IGNITION TO START" (blinking, 500 ms) is drawn on the grey, across the screen's width.

def mix(top, under, opacity):
    t = [int(top[i:i + 2], 16) for i in (1, 3, 5)]; u = [int(under[i:i + 2], 16) for i in (1, 3, 5)]
    return '#' + ''.join(f'{round(a * opacity / 100 + b * (1 - opacity / 100)):02X}' for a, b in zip(t, u))


def logo_over(image_key, colour, opacity, name):
    """The logo mixed at `opacity` over a plain colour (PNG, base64, added to the dash's images): opaque where the logo
    is, still transparent around it (so the screen's background stays exactly its colour there, also when the picture
    is drawn with rectangles in a few colours)."""
    src = Image.open(io.BytesIO(base64.b64decode(d['Images'][image_key]))).convert('RGBA')
    a = src.copy(); a.putalpha(src.getchannel('A').point(lambda v: v * opacity // 100))
    bg = Image.new('RGBA', src.size, tuple(int(colour[i:i + 2], 16) for i in (1, 3, 5)) + (255,))
    out = Image.alpha_composite(bg, a)
    out.putalpha(src.getchannel('A').point(lambda v: 255 if v >= 8 else 0))
    buf = io.BytesIO(); out.save(buf, 'PNG')
    key = f'{name}@{src.width}x{src.height}'
    d['Images'][key] = base64.b64encode(buf.getvalue()).decode('ascii')
    return key


for cond, bg_name in (('![EngineIgnitionOn]', 'IGN OFF background'), ('changed(1000, [EngineIgnitionOn])', 'IGN ON background')):
    bg = el('rect', bg_name, cond=cond)
    layers = [e for e in E if e['Type'] == 'rect' and e is not bg and vis(e) == vis(bg)]
    colour = '#000000'
    for r in layers:   # the screen's own rectangles, in order, over its black
        colour = mix(r['Color'], colour, r.get('Opacity', 100))
        E.remove(r)
    bg.update(X=0, Y=0, W=790, H=460, Color=colour)
    logo = el('image', cond=cond)
    old = logo['Image']
    logo['Image'] = logo_over(old, colour, logo.get('Opacity', 100), 'Ginetta_Logo_' + ('off' if cond.startswith('!') else 'on'))
    logo['Opacity'] = 100
    # drawn with rectangles (a wheel without the screen's picture memory) the faint logo in 6 colours took 107-119 KB,
    # over 4 s, each time a screen came up; in 3 60-71 KB, in 2 (the ring and the disc) 38 KB, ~1.5 s. With the
    # picture memory it's a full-colour picture either way.
    logo['MaxColors'] = 2
    if cond.startswith('!'):
        # one screen command holds 58 characters and a label's box and colours take ~43 of them: the wheel would show
        # "PRESS IGNITION". Two labels, the same conditions (they blink together), set so the line reads as one text
        # centred across the screen, as in the original.
        press = el('label', 'TextItem', cond=cond)
        place(press, 'IGN OFF', 'TextItem', ['PRESS IGNITION TO START'], (14, 336, 776, 456), label=True)
        full, head, tail = 'PRESS IGNITION TO START', 'PRESS IGNITION', 'TO START'
        f = press['Font']
        start = 395 - width(f, full) // 2
        second = dict(press, Name='TextItem 2', Text=tail, X=start + width(f, full) - width(f, tail) - 2, W=width(f, tail) + 4, Align='left')
        press.update(Text=head, X=start - 2, W=width(f, head) + 4, Align='left', Background=colour)
        second['Background'] = colour
        E.insert(E.index(press) + 1, second)
# the logo as imported (with its transparency) is used by neither screen now
for k in [k for k in d['Images'] if k.startswith('Ginetta_Logo@')]:
    del d['Images'][k]

d['Elements'] = E
json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); a second
# run must change nothing (the /create-dash gate)
bands = fx('fit-bands', OUT)['changes']
again = fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'fit_bands': bands, 'import_notes': report['Notes'],
                  'skipped': report.get('SkippedTypes')}, indent=1))
