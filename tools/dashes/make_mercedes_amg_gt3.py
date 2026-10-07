"""
The "LMGT3 Mercedes AMG GT3" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_mercedes_amg_gt3.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmgt3-mercedes-amg-gt3.json)

Needs SimHub installed with the SimHub dash "LMGT3 Mercedes AMG GT3" (DashTemplates; its SansSerif comes with it),
Windows' Arial, Segoe UI and Microsoft JhengHei (the original's fonts, used to measure and draw its texts) and fxdash
built (dotnet build -c Release tools/fxdash/fxdash.csproj). Re-running it re-imports from the SimHub dash, so every
change to the conversion lives here, with its reason. `mercedes_amg_gt3_parity.py` checks the result item by item;
`simhub_ref.py` draws the original for side-by-side looks.

What the import brings over by itself: the Bosch DDU-style main screen (the rev bar with its numbers, water and oil,
the delta / last lap box, speed, energy, the gear, ABS / TC / MAP, fuel last lap and fuel, the bottom bar with its
icons, the icons at the sides), three widgets the driver flips on their own SimHub commands as three sets of pages (the
lap time box: lap time, last lap, predicted, best, Next page 1; the tyres over the energy and lap time boxes: off,
pressures and temperatures, brakes and temperatures, Next page 2; the flag bars at the sides: off / on, Next page 3),
every overlay (setting changes, lap summaries per session in the bottom bar, the alert banners at the top: LIFT NOW,
water temperature, pit limiter, ABS warm-up, start / pit exit / pit stop GO, battery, release throttle; pit stop
requested / cancelled, flags), and the ignition-off and ignition-on screens (the Bosch splash, then the dash's
"SYSTEM READY" start-up picture).

One change from the original, the maintainer's choice: the wide layout. The original's side columns (the pit limiter's
arrows, the pit box, engine, tyre and fuel pump icons, FLAGS ON / OFF) are left out and the dash is stretched over the
screen's width; the flag bars are thin strips at its edges.

The import's own grouping of the ignition-on screen's copies of the lap time and tyre widgets (an older copy, on other
SimHub commands) took two sets of pages of their own and left the main screen's tyre widget with only its first screen:
the importer now puts a copy of a widget (same file, as many screens) in its set (Usb/SimHubImport.cs).
"""
import base64, io, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_kit as K  # noqa: E402

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-mercedes-amg-gt3.json'
SIMHUB_DASH = 'LMGT3 Mercedes AMG GT3'

work = os.path.join(os.path.dirname(OUT), '_build')
os.makedirs(work, exist_ok=True)
raw = os.path.join(work, 'lmgt3-mercedes-amg-gt3.import.json')
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
# 1. Identity, and names for the three sets of pages
d['Id'] = 'lmgt3-mercedes-amg-gt3'
d['Name'] = 'LMGT3 Mercedes AMG GT3'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "LMGT3 Mercedes AMG GT3" '
                    '(https://lmu-dashboards.com). Parts flip on their own, as in the original: the lap time box (lap time, '
                    'last lap, predicted, best: Next page 1), the tyres over the left boxes (pressures and temperatures, '
                    'brakes: Next page 2) and the flag bars at the sides (Next page 3).')
d['Source'] = 'Redadeg\'s SimHub dash "LMGT3 Mercedes AMG GT3", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions aren't used by any formula
assert d['Pages'] == ['CUR LAP', 'LAST LAP', 'PREDICTED', 'BEST'] and [p['Name'] for p in d['PageSets']] == ['TiresLeft', 'FLAGS'], (d['Pages'], d['PageSets'])
SETS = {'LAPTIMES_LEFT': 'page', 'TiresLeft': 'page2', 'FLAGS': 'page3'}
d['Pages'] = ['Lap time', 'Last lap', 'Predicted', 'Best lap']
d['PageSets'][0].update(Name='Tyres', Pages=['Off', 'Pressures', 'Brakes'])
d['PageSets'][1].update(Name='Flags', Pages=['Off', 'On'])

# ---------------------------------------------------------------------------------------------------------------------
# 2. Formulas.
# Tyre pressures: the formula returns text in psi and kPa (format(x, '0.0') / '0') and the raw number otherwise (bar),
# and SimHub's format applies to numbers only. The wheel formats numeric text too ("26.90"), so the formula formats
# every unit itself (bar as SimHub would: its 0.00), shown as it comes.
for e in E:
    b = e.get('Bind') or ''
    if e['Type'] == 'value' and b.startswith("ncalc:if ([TyrePressureUnit] = 'Psi'"):
        key = b[b.index('format([') + 8:b.index('],')]
        tail = f"[{key}]))"
        assert b.endswith(tail), b
        e['Bind'] = b[:-len(tail)] + f"format([{key}], '0.00')))"
        e['Format'] = 'text'
# "Headlight Blue": a blue rectangle at 15 % over the whole dash while the headlights are on (SimHub tints everything
# under it). The wheel draws opaque: a see-through rectangle over a screen of many colours would need every pixel of the
# dash drawn again, tinted, each time anything changes. Left out (the headlight icon in the bottom bar shows they're on).
E.remove(el('Headlight Blue', 'rect'))
# The side columns (the wide layout, the maintainer's choice; 7.): the pit limiter's arrows, the pit box, engine, tyre and
# fuel pump icons, FLAGS ON / OFF left out (everything outside 107..682 here); the flag bars as 8 px strips at the
# screen's edges.
L, R = 107, 682
for e in [e for e in E if e['W'] < 700 and (e['X'] + e['W'] <= L - 3 or e['X'] >= R + 2) and e.get('Name') not in ('LINKS', 'RECHTS')]:
    E.remove(e)
for e in E:
    if e.get('Name') == 'LINKS' and e['Type'] == 'rect': e.update(X=0, W=8)
    elif e.get('Name') == 'RECHTS' and e['Type'] == 'rect': e.update(X=782, W=8)

# ---------------------------------------------------------------------------------------------------------------------
# 3. The widest text each value really shows (the import copies SimHub's preview text: "XXXXX", "1.65", "6,3").
SAMPLES = [   # (a text in the binding, samples): the first that matches, so the specific ones first
    ('SessionBestLiveDeltaSeconds', ['-8.88']), ('driverbestlap(getopponent', ['8.888']),
    ('driverbestlap(1)', ['88:88.888', '-:---']), ('drivershortname', ['WWWWWWWW']), ('driverrelativegaptoplayer', ['+888.88', '-']),
    ('TyrePressure', ['88.8', '888', '8.88']), ('mTireInnerLayerTemperature', ['188']), ('BrakeTemperature', ['1888']),
    ('[CurrentLapTime]', ['88:88:88']), ('[LastLapTime]', ['8:88:888']), ('EstimatedLapTime', ['8:88:888']), ('[BestLapTime]', ['8:88:888']),
    ('mVirtualEnergy', ['100.0']), ('Fuel_LastLapConsumption', ['8.88']), ('Fuel_RemainingLaps', ['88.8']), ('[Fuel]', ['188']),
    ('[OilTemperature]', ['188']), ('[WaterTemperature]', ['188']), ('[SpeedLocal]', ['388']), ('[Speedkmh]', ['388']),
    ('[EngineMap]', ['88']), ('[TCLevel]', ['88']), ('[ABSLevel]', ['88']), ('mTCCut', ['88']), ('mTCSlip', ['88']),
    ('[BrakeBias]', ['88.8']), ('[Position]', ['88']), ('gearText', ['8', 'N', 'R']),
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
    # before a lap has a time SimHub formats the zero time ("00:00:00"); the wheel shows Empty for a time of 0
    if (e.get('Format') or '').startswith('time:'):
        e['Empty'] = e['Format'][5:].replace('\\', '').replace('m', '0').replace('ss', '00').replace('f', '0')
        if e['Empty'] not in e['Samples']: e['Samples'] = e['Samples'] + [e['Empty']]   # it must fit too

# ---------------------------------------------------------------------------------------------------------------------
# 4. Overlays over SimHub's whole width cover the wheel's (0..790, not the scaled dash's 12..778): the flag bars at the
# sides, the ignition screens.
for e in E:
    if e['Type'] not in ('rect', 'box') or not own(e) and not K.pages_of(e): continue
    if e['X'] <= 12: e['W'] += e['X']; e['X'] = 0
    if e['X'] + e['W'] >= 778: e['W'] = 790 - e['X']
    if e['Y'] + e['H'] > 460: e['H'] = 460 - e['Y']

# ---------------------------------------------------------------------------------------------------------------------
# 5. Every text in the original's size and place: each one measured from its original item with the original's font
# (Microsoft JhengHei, Segoe UI, Arial, SansSerif at its weight), the screen font closest to it that fits picked, and its
# band centred on where the original's ink is. Items the original draws in the same font, size and weight get one font
# together (convert_kit.place_group). Areas: the original's box on the wheel, unless AREAS says otherwise.
AREAS = {   # (name, a text in a condition or '') -> (x0, y0, x1, y1)
    # the lap time box's time: above its page dots (its text rows reached them: every change redrew the dots, a flash)
    ('Laptime#', ''): (117, 300, 303, 349),
}
# (name, a text in a condition or '') -> screen font, where the closest isn't the right one
# the lap summaries' driver names (JhengHei 14 px, 19 px rows over the gaps): 12, the screen's 16 px font with lower case
# (the closest normally spaced one, 24 px, ran into the gaps under them)
FONTS = {('LeaderboardOpponentNameText', ''): 12, ('LeaderboardOpponentNameText2', ''): 12}


def texts_of(e):
    return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def over_bar(e):
    return any(b['Type'] == 'bar' and b['X'] < e['X'] + e['W'] and e['X'] < b['X'] + b['W'] and b['Y'] < e['Y'] + e['H'] and e['Y'] < b['Y'] + b['H'] for b in E)


# Fixed texts smaller than the screen's normally spaced fonts (the smallest is 32 px; the boxes' captions are ~23 px)
# become pictures in 6. in the original's own font; an overlay's caption as a picture is a RAM-drive file of its own:
# only under 25 px. Larger fixed texts (the banners, the big "X" placeholders of the start-up screen) stay screen text.
def as_picture(e):
    if e['Type'] != 'label' or e.get('ColorBind') or not (e.get('Text') or '').strip() or over_bar(e): return False
    em = K.match(e, orig).em
    static = not [c for c in own(e) if 'blink(' not in c] and not K.pages_of(e)
    return em < 25 or (static and em < 30)


# The tyre widget's four rows (pressure, temperature, temperature, pressure: 43-51 px apart): each number in its own row,
# from halfway to the row above to halfway to the row below (the original's boxes overlap by a few px; the screen fonts'
# text rows overlapped the next row's: every change redrew the neighbour's text, a flash).
for e in [e for e in E if e['Type'] == 'value' and any(c.startswith('page2:') for c in K.pages_of(e))]:
    col = sorted((x for x in E if x['Type'] == 'value' and K.pages_of(x) == K.pages_of(e) and own(x) == own(e) and abs(x['X'] - e['X']) < 30),
                 key=lambda x: x['Y'])
    item = K.match(e, orig)
    i = col.index(e)
    y0 = (col[i - 1]['Y'] + col[i - 1]['H'] + e['Y']) // 2 + 1 if i else int(item.y)
    y1 = (e['Y'] + e['H'] + col[i + 1]['Y']) // 2 - 1 if i + 1 < len(col) else int(item.y + item.h)
    AREAS[(e['Name'], K.pages_of(e)[0] + '|' + str(e['X']))] = (int(item.x), y0, int(item.x + item.w), y1)


def lookup(table, e):
    for (name, cond), v in table.items():
        if '|' in cond:   # (a page and a left edge: one tyre number)
            pg, x = cond.split('|')
            if e.get('Name') == name and pg in K.pages_of(e) and str(e['X']) == x: return v
            continue
        if e.get('Name') == name and (cond == '' or any(cond in c for c in vis(e))): return v
    return None


groups = {}
for e in E:
    if e['Type'] not in ('label', 'value') or as_picture(e): continue
    if not texts_of(e)[0].strip(): continue
    item = K.match(e, orig)
    area = lookup(AREAS, e) or (max(0, int(item.x)), max(0, int(item.y)), min(790, int(item.x + item.w)), min(460, int(item.y + item.h)))
    kw = {'chars': '0123456789NR'} if e.get('Bind') == 'gearText' else {}
    if lookup(FONTS, e):
        K.place(e, orig, item, texts_of(e), area, font=lookup(FONTS, e))
        continue
    groups.setdefault((item.family, round(item.em, 1), item.weight, e.get('Bind') == 'gearText'), []).append((e, item, texts_of(e), area, kw))


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

# ---------------------------------------------------------------------------------------------------------------------
# 6. Fixed texts as pictures in the original's own font (under 32 px the screen has only its letter-spaced fonts).
pictures = 0
for i, e in enumerate(E):
    if not as_picture(e): continue
    item = K.match(e, orig)
    pic = K.text_picture(orig, item, e['Text'], e.get('Color', '#FFFFFF')[:7])
    if pic is None: continue
    im, x, y = pic
    # a caption on its own black background (cutting its box's top line): the picture within that background (its
    # 1 px margin past it was made over the static line's colour, and drew it over an overlay's line of another colour)
    bgs = [b for b in E[:i] if b['Type'] == 'rect' and b.get('Name') == e['Name'] + ' background' and b.get('Visible') == e.get('Visible')]
    if bgs:
        b = bgs[-1]
        x0, y0, x1, y1 = max(x, b['X']), max(y, b['Y']), min(x + im.width, b['X'] + b['W']), min(y + im.height, b['Y'] + b['H'])
        if x1 > x0 and y1 > y0 and (x0, y0, x1, y1) != (x, y, x + im.width, y + im.height):
            im = im.crop((x0 - x, y0 - y, x1 - x, y1 - y)); x, y = x0, y0
    key = f'text {e["Name"]} {i}@{im.width}x{im.height}'
    d.setdefault('Images', {})[key] = picture(im)
    E[i] = {k: v for k, v in e.items() if k in ('Name', 'Visible', 'PreviewVisible')}
    E[i].update(Type='image', Image=key, X=x, Y=y, W=im.width, H=im.height, MaxColors=2)
    pictures += 1

captions = [e for e in E if e['Type'] == 'image' and e['Image'].startswith('text ') and not own(e)]
shrunk, still = K.shrink_to_text(E, also=captions)


# The lap time box's page dots ("•" x 4 under the time, the current page's one bigger and yellow): the screen's fonts
# have no "•" (the import left them empty). Each page's four as one picture of the original's dots (one RAM-drive file
# a page, not four), as make_lmgt3_aston.py does.
from PIL import Image
dot_groups = {}
for e in E:
    if e['Type'] == 'label' and e.get('Name') in ('1', '2', '3', '4') and not (e.get('Text') or '').strip() and K.pages_of(e):
        dot_groups.setdefault(tuple(vis(e)), []).append(e)
for key, dots in dot_groups.items():
    assert len(dots) == 4, dots
    pics = []
    for e in dots:
        item = K.match(e, orig)
        # (the original stores "�" where "•" was meant: SimHub shows the replacement character; the intended dot here)
        pics.append(K.text_picture(orig, item, item.it['Text'].strip().replace('�', '•'), '#' + item.it.get('TextColor', '#FFFFFFFF')[-6:]))
    x0, y0 = min(p[1] for p in pics), min(p[2] for p in pics)
    x1, y1 = max(p[1] + p[0].width for p in pics), max(p[2] + p[0].height for p in pics)
    im = Image.new('RGBA', (x1 - x0, y1 - y0), (0, 0, 0, 0))
    for p, x, y in pics: im.alpha_composite(p, (x - x0, y - y0))
    k = f'text page dots {len(d["Images"])}@{im.width}x{im.height}'
    d['Images'][k] = picture(im)
    first = E.index(dots[0])
    E[first] = {'Type': 'image', 'Name': 'Punkte', 'Image': k, 'X': x0, 'Y': y0, 'W': im.width, 'H': im.height,
                'Visible': dots[0]['Visible'], 'PreviewVisible': dots[0].get('PreviewVisible'), 'MaxColors': 3}
    for e in dots[1:]: E.remove(e)
    pictures += 1

# The water temperature banner's unit: the original stores "�C" where "°C" was meant (SimHub shows the
# replacement character; the import dropped it, "C"); no screen font has "°": a picture of "°C" in the original's
# font (Arial bold).
for e in els('WATTEMP#2', 'label'):
    item = K.match(e, orig)
    im, x, y = K.text_picture(orig, item, item.it['Text'].replace('�', '°'), '#' + item.it.get('TextColor', '#FFFFFFFF')[-6:])
    k = f'text WATTEMP#2 unit@{im.width}x{im.height}'
    d['Images'][k] = picture(im)
    E[E.index(e)] = {'Type': 'image', 'Name': e['Name'], 'Image': k, 'X': x, 'Y': y, 'W': im.width, 'H': im.height,
                     'Visible': e.get('Visible'), 'PreviewVisible': e.get('PreviewVisible'), 'MaxColors': 2}
    pictures += 1

captions = [e for e in E if e['Type'] == 'image' and e['Image'].startswith('text ') and not own(e)]
shrunk, still = K.shrink_to_text(E, also=captions)

# ---------------------------------------------------------------------------------------------------------------------
# 7. Shapes. A bordered box without rounded corners that comes and goes (every banner at the top, the setting pop-ups'
# frames, the lap summaries' bottom bar) would be a picture on the RAM drive, one per look; two rectangles (the border
# colour, then the fill inset by the border) look the same, are two fills and take none.
for e in [e for e in E if e['Type'] == 'box' and not e.get('Radius') and own(e) and e.get('Fill')]:
    K.box_as_rects(E, e)
# An outline box (a border, no fill, square corners) that comes and goes (the start-up screen's copy of every frame) would
# be a picture on the RAM drive each: its four sides as rectangles instead (four fills, no file).
for e in [e for e in E if e['Type'] == 'box' and not e.get('Radius') and own(e) and not e.get('Fill') and e.get('Border')]:
    b, i = e['Border'], E.index(e)
    line = {k: v for k, v in e.items() if k in ('Visible', 'PreviewVisible')}
    n = e.get('Name')
    E[i:i + 1] = [dict(line, Type='rect', Name=n, X=e['X'], Y=e['Y'], W=e['W'], H=b, Color=e['Color']),
                  dict(line, Type='rect', Name=n + ' border', X=e['X'], Y=e['Y'] + e['H'] - b, W=e['W'], H=b, Color=e['Color']),
                  dict(line, Type='rect', Name=n + ' border', X=e['X'], Y=e['Y'] + b, W=b, H=e['H'] - 2 * b, Color=e['Color']),
                  dict(line, Type='rect', Name=n + ' border', X=e['X'] + e['W'] - b, Y=e['Y'] + b, W=b, H=e['H'] - 2 * b, Color=e['Color'])]
# The setting pop-ups (TC, ABS, MAP: 493,195 204x190 in SimHub; BIAS, TC CUT, TC SLIP: 492,194 206x192) and the ABS
# warm-up box (493,196 204x189) cover the same cell a pixel apart: each size kept the static picture under it as an area
# tile of its own (~8.5 KB each). Their outer boxes take the biggest one's place (a pixel more for some; their insides
# stay where they are).
pops = [e for e in E if e['Type'] == 'rect' and own(e) and 480 <= e['X'] <= 486 and 184 <= e['Y'] <= 190 and e['W'] >= 194 and e['H'] >= 179]
big = max(pops, key=lambda e: e['W'] * e['H'])
for e in pops: e.update(X=big['X'], Y=big['Y'], W=big['W'], H=big['H'])
# A text longer than one screen command takes ("RELEASE THROTTLE PEDAL" showed "RELEASE THROTTLE PED", "ABS WarmUp
# ACTIVE" and "UBATT LOW: 12.9 V" lost their ends): two labels that read as one line.
for e in [e for e in E if e['Type'] == 'label' and len(e.get('Text') or '') > 15 and ' ' in e['Text']]:
    K.split_label(E, e)

# The ignition screens: the Mercedes star at 32 % on near-black (ignition off) and the Bosch splash on black (the first
# half second of ignition on). A rectangle of the background colour with only the logo's own area as a picture over it
# (the 32 % mixed in: the wheel draws pictures opaque).
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') == 'ImageItem' and own(e) and any('EngineIgnitionOn' in c for c in vis(e))]:
    off = any(c.startswith('ncalc:![EngineIgnitionOn]') for c in vis(e))
    K.logo_on_plain(d, E, e, 'stern ignition off' if off else 'bosch ignition on', 3 if off else 4)
# The star reaches over the rev bar's rows, and the renderer makes no RAM-drive picture of a shape over a bar (drawn with
# rectangles: 178 KB each time the ignition went off): cut in strips around the bar's rows (convert_kit.strips_around_bars).
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') == 'ImageItem' and own(e) and any('EngineIgnitionOn' in c for c in vis(e))]:
    K.strips_around_bars(d, E, e)
for e in [e for e in E if e['Type'] == 'rect' and any('EngineIgnitionOn' in c for c in vis(e)) and e['W'] >= 760]:
    e.update(X=0, Y=0, W=790, H=460)
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]

# The start-up screen's "XXXXX" placeholders cover the copy's values under them with a black box (SimHub draws the label's
# background over the lap time): the box as wide as what it covers, or the lap time's last digits ("90") showed past it
# (the screen's digits are wider than JhengHei's).
for bg in [e for e in E if e['Type'] == 'rect' and (e.get('Name') or '').endswith(' background') and own(e) and e.get('Color') == '#000000']:
    for v in E[:E.index(bg)]:
        if v['Type'] != 'value' or not set(own(bg)) <= set(own(v)) or not K.pages_of(v) and own(v) != own(bg): continue
        ix = min(bg['X'] + bg['W'], v['X'] + v['W']) - max(bg['X'], v['X']); iy = min(bg['Y'] + bg['H'], v['Y'] + v['H']) - max(bg['Y'], v['Y'])
        if ix > 0 and iy > 0 and ix * iy >= 0.6 * v['W'] * v['H']:
            x0, x1 = min(bg['X'], v['X']), max(bg['X'] + bg['W'], v['X'] + v['W'])
            bg.update(X=x0, W=x1 - x0)

# The start-up screen draws its "X" placeholders (TCC, TCS) over its copy of the tyre widget: with the tyre page on, SimHub
# draws the tyre numbers and the X's over them, one on the other for the 1.5 s it shows. On the wheel every change of a
# number under an X redraws the X (a flash): the copy's numbers the X's draw over are left out.
xs = [e for e in E if e['Type'] == 'label' and (e.get('Text') or '').strip() == 'X' and own(e)]
for v in [e for e in E if e['Type'] == 'value' and any(c.startswith('page2:') for c in K.pages_of(e)) and own(e)]:
    if any(set(own(x)) <= set(own(v)) and E.index(x) > E.index(v) and x['X'] < v['X'] + v['W'] and v['X'] < x['X'] + x['W']
           and x['Y'] < v['Y'] + v['H'] and v['Y'] < x['Y'] + x['H'] for x in xs):
        E.remove(v)

# The wide layout (the maintainer's choice, a change from the original): the original keeps a column at each side (the
# pit limiter's arrows; the pit box, engine, tyre and fuel pump icons; FLAGS ON / OFF), which on the wheel left the dash a
# narrow block in the middle. The side columns are left out (2.) and the dash (107..682) stretched over the screen's width:
# boxes, bars, lines and numbers' boxes to their new edges, pictures (captions, icons, logos) at their own size where
# their middle goes (pictures that touch, a logo's strips and the bottom bar's "DDU S2 Motorsport Display" move as one; a
# caption's black box under it with it). The flag bars are 8 px strips at the screen's edges.
NL, NR = 12, 778
def wx(x): return NL + (x - L) * (NR - NL) / (R - L)
moved = set()
# (with them the two halves of a text split in two, split_label: their places make one line)
def pair(a, b):   # b is the second half of a's text
    return a['Type'] == b['Type'] == 'label' and vis(a) == vis(b) and (a.get('Name') or '') + ' 2' == b.get('Name')
halves = [e for e in E if any(pair(e, x) or pair(x, e) for x in E)]
def plain(e):   # a picture of one colour (the red MAX RPM block over the rev bar's area): stretched like a box
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA')
    return len(im.getcolors(4) or [None] * 5) == 1
pics = [e for e in E if e['Type'] == 'image' and e['W'] < 700 and not plain(e)] + halves
under = {}   # a caption's black box -> its picture
for bg in [e for e in E if e['Type'] == 'rect' and (e.get('Name') or '').endswith(' background') and e['W'] < 700]:
    for p in pics:
        if p.get('Name') == bg['Name'][:-11] and vis(p) == vis(bg) and p['X'] < bg['X'] + bg['W'] and bg['X'] < p['X'] + p['W'] \
                and p['Y'] < bg['Y'] + bg['H'] and bg['Y'] < p['Y'] + p['H']:
            under[id(bg)] = p
ONE = ({'DDU9', 'Motorsport', 'Display'},)   # the bottom bar's "DDU S2 Motorsport Display": one logo in three texts
def together(p, q):
    if (p.get('Image') or '').split(' rows ')[0] == (q.get('Image') or '').split(' rows ')[0]: return True   # a logo's strips
    if any({p.get('Name'), q.get('Name')} <= g for g in ONE): return True
    if p['Type'] == q['Type'] == 'label': return pair(p, q) or pair(q, p)
    return q['X'] - 2 <= p['X'] + p['W'] and p['X'] - 2 <= q['X'] + q['W'] and q['Y'] - 2 <= p['Y'] + p['H'] and p['Y'] - 2 <= q['Y'] + q['H']
def same_place(p, q):   # the same picture in another overlay (a caption every lap summary has)
    return abs(p['X'] - q['X']) <= 8 and abs(p['W'] - q['W']) <= 12 and abs(p['Y'] - q['Y']) <= 8 and abs(p['H'] - q['H']) <= 8
clusters = []   # pictures shown together that touch, as one
for p in pics:
    near = [c for c in clusters if vis(c[0]) == vis(p) and any(together(p, q) for q in c)]
    merged = [p] + [q for c in near for q in c]
    clusters = [c for c in clusters if c not in near] + [merged]
shift = {}
for c in clusters:
    x0, x1 = min(q['X'] for q in c), max(q['X'] + q['W'] for q in c)
    for q in c: shift[id(q)] = round(wx((x0 + x1) / 2) - (x0 + x1) / 2)
# (copies of one picture in other overlays a pixel or two apart: the first one's shift, or they rounded 1 px apart and
# the caption's black box under one cut into the other)
for i, p in enumerate(pics):
    for q in pics[:i]:
        if vis(q) != vis(p) and same_place(p, q) and abs(shift[id(p)] - shift[id(q)]) <= 2: shift[id(p)] = shift[id(q)]; break
was = {id(e): (e['X'], e['W']) for e in E}
for e in E:
    if e['W'] >= 700 or e['X'] == 0 and e['W'] == 8 or e['X'] == 782: continue
    if id(e) in shift: e['X'] += shift[id(e)]; continue
    if id(e) in under: e['X'] += shift[id(under[id(e)])]; continue
    x0, x1 = round(wx(e['X'])), round(wx(e['X'] + e['W']))
    if e['Type'] == 'rect' and e['W'] <= 4 and e['H'] > e['W']:
        # (a box's side keeps its width: at the box's new left or right edge, or where its middle goes)
        sib = [was[id(s)] for s in E if s is not e and vis(s) == vis(e) and was[id(s)][1] > 4]
        if any(sx + sw == e['X'] + e['W'] for sx, sw in sib): e['X'] = x1 - e['W']
        elif any(sx == e['X'] for sx, sw in sib): e['X'] = x0
        else: e['X'] = round(wx(e['X'] + e['W'] / 2) - e['W'] / 2)
        continue
    e['X'], e['W'] = x0, x1 - x0
    if e['Type'] == 'image':
        im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA')
        d['Images'][e['Image']] = picture(im.resize((e['W'], im.height)))
# A box inside another (a fill inset in its frame's border, a lit cell's fill in its border) keeps its insets: rounded
# separately, a 2 px inset came out 2 on one side and 3 on the other, and the frame's line showed through or got cut.
def inset(c, e):
    (cx, cw), (ex, ew) = was[id(c)], was[id(e)]
    li, ri = ex - cx, cx + cw - ex - ew
    a, b = set(vis(c)), set(vis(e))
    nested = a == b or a and b and (a <= b or b <= a)   # (the last lap box's line has one condition more)
    return (li, ri) if 0 <= li <= 4 and 0 <= ri <= 4 and cw > ew and nested \
        and c['Y'] - 4 <= e['Y'] + e['H'] and e['Y'] - 4 <= c['Y'] + c['H'] else None
shapes = sorted([e for e in E if e['Type'] in ('rect', 'box') and 4 < was[id(e)][1] < 700 and id(e) not in under], key=lambda e: -was[id(e)][1])
for e in shapes:
    outer = [c for c in shapes if c is not e and inset(c, e)]
    if not outer: continue
    c = min(outer, key=lambda c: was[id(c)][1])
    li, ri = inset(c, e)
    e['X'], e['W'] = c['X'] + li, c['W'] - li - ri
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]

# The designer's preview shows the plain dash: every overlay (an element with a condition of its own) hidden in it (the
# import hid most; the pit stop banner over the gear's box, the icons' "on" pictures and the delta box's colours it left).
for e in E:
    if own(e): e['PreviewVisible'] = False

# On a wheel without the RAM patch pictures are drawn with rectangles in MaxColors colours (of everything there): the
# bottom bar's faint "off" icons, the Bosch logo (and the side icons, since left out) in 8 took ~2,500 rectangles (61 KB) each time a lap
# summary left the bar. The always-shown ones are one grey on black (the "off" icons at 20 %, the Bosch logo): 2 colours
# (~800 rectangles); the coloured "on" icons and the logos 4.
# The ignition screens without the patch: the star at 32 % is one grey on near-black (2: was 167 KB, 6.7 s in 4); the
# start-up screen's copy of the dash takes its pictures in the dash's own colours (its icons' "off" pictures 2).
for e in E:
    if e['Type'] != 'image' or e['Image'].startswith('text '): continue
    startup = any('changed(2000, [EngineIgnitionOn])' in c for c in own(e))
    if e.get('Name') == 'ImageItem' and any(c.startswith('ncalc:![EngineIgnitionOn]') for c in own(e)): e['MaxColors'] = 2
    elif e.get('Name') == 'ImageItem': e['MaxColors'] = 4
    elif startup: e['MaxColors'] = 2 if e.get('Name', '').endswith(('off', 'inactive')) or e.get('Name') in ('Logo Bosch',) else 3
    else: e['MaxColors'] = 4 if own(e) else 2

# ---------------------------------------------------------------------------------------------------------------------
# 8. Values on their own overlay's box (a setting pop-up's number on its panel): drawn on its colour; a value over a
# panel's edge by a pixel or two moved inside it.
K.snap_into_panels(E)
K.backgrounds_from_overlays(E)
# (not the numbers on pages: the tyre widget draws a black panel over the lap time and energy boxes, so their frames under
# it are never seen with them; pulled into the lap time frame, the tyres' rows ran into each other; the start-up screen's
# lap time was pulled 59 px right into a box of another page)
tyres = {id(e): (e['X'], e['Y']) for e in E if e['Type'] == 'value' and K.pages_of(e)}
K.keep_inside_boxes(E)
for e in E:
    if id(e) in tyres: e['X'], e['Y'] = tyres[id(e)]
# Values half under an overlay's box hide while it shows (convert_kit.take_turns).
turns = K.take_turns(E, d['Images'])
# The delta box (green or red) under the last lap box (and under the start-up screen's copy of it): in the original's
# width its number's text ran past the box over it, so take_turns hid the number and the box's parts went with it; wide,
# the number is inside, nothing hid, and the box was drawn again under the last lap box when that went (a flash of the
# whole box in the overlay sweep). It hides while either shows, as before.
last = [e for e in E if e.get('Name') == 'LastLap#' and own(e) == ['ncalc:changed(5000, [LastLapTime])']][0]
startup = ['ncalc:changed(2000, [EngineIgnitionOn])', 'ncalc:!changed(500, [EngineIgnitionOn])']
for e in E:
    if any('SessionBestLiveDeltaSeconds]<5' in c for c in own(e)) and not K.pages_of(e) \
            and e['X'] < last['X'] + last['W'] and last['X'] < e['X'] + e['W'] and e['Y'] < 180:
        for c in (K.negation(own(last)), K.negation(startup)):
            if c not in vis(e): e['Visible'] = vis(e) + [c]; turns += 1
# The parts of one overlay or page (the same conditions) take turns together: a caption's picture with the background
# box it's drawn on (with a take-turns condition only the box had, the picture wasn't made over it: 13 KB of rectangles
# at each flip to the best lap page).
groups_tt = {}
for e in E:
    groups_tt.setdefault((tuple(sorted(own(e))), tuple(K.pages_of(e))), []).append(e)
for (o, pg), members in groups_tt.items():
    if not o and not pg: continue
    negs = [c for m in members for c in vis(m) if c not in own(m) and not K.PAGE.match(c)]
    for m in members:
        for c in dict.fromkeys(negs):
            if c not in vis(m): m['Visible'] = vis(m) + [c]; turns += 1
# ... and the parts that come and go inside an overlay (the last lap box's outer line, shown while the delta is under
# 10 s) take turns as their overlay does.
for x in E:
    negs = [c for c in vis(x) if c not in own(x) and not K.PAGE.match(c)]
    if not negs or not own(x): continue
    for y in E:
        if y is not x and set(own(x)) < set(own(y)) and K.pages_of(y) == K.pages_of(x):
            for c in negs:
                if c not in vis(y): y['Visible'] = vis(y) + [c]; turns += 1

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
bands = K.fx('fit-bands', OUT)['changes']
again = K.fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'unplaced': unplaced, 'widened': widened, 'text_pictures': pictures,
                  'shrunk': sorted(set(shrunk)), 'still': still, 'take_turns': turns, 'fit_bands': bands, 'import_notes': report['Notes']}, indent=1))
