"""
The "Toyota GR010 Hybrid" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_toyota_gr010.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\toyota-gr010-hybrid.json)

Needs SimHub installed with the SimHub dash "Toyota GR010 Hybrid" (DashTemplates), Windows' Arial and Segoe UI (the
original's fonts, used to measure and draw its texts; Arial Edit and Edge Racer come with the dash) and fxdash built
(dotnet build -c Release tools/fxdash/fxdash.csproj). Re-running it re-imports from the SimHub dash, so every change to
the conversion lives here, with its reason. `toyota_gr010_parity.py` checks the result item by item; `simhub_ref.py`
draws the original for side-by-side looks.

What the import brings over by itself: the main screen (stint, fuel, hybrid map, temperatures and predicted lap on the
left; speed, gear, tyre pressures and temperatures and the delta in the middle; lap time, state of charge, brake bias,
TC and brake temperatures and the best lap on the right; energy and the bottom widget under them), the bottom widget's
two screens as pages (ARB front / rear; migration / regen: Next / Previous page), every overlay of the main screen
(setting changes, engine off, lap summaries per session, flags, engine stall, pit request, LIFT, the pit limiter and the
race start's STEADY 60 / GREEN), and the ignition-off and ignition-on screens.

This replaces the first conversion (hand-tuned in the designer, 2026-09): it had no migration / regen page, the
original's captions in the screen's letter-spaced fonts, sizes that differed inside groups the original draws alike, and
114 KB on the screen's RAM drive.
"""
import base64, io, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_kit as K  # noqa: E402

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\toyota-gr010-hybrid.json'
SIMHUB_DASH = 'Toyota GR010 Hybrid'

work = os.path.join(os.path.dirname(OUT), '_build')
os.makedirs(work, exist_ok=True)
raw = os.path.join(work, 'toyota-gr010-hybrid.import.json')
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
# 1. Identity, and names for the bottom widget's two pages
d['Id'] = 'toyota-gr010-hybrid'
d['Name'] = 'Toyota GR010 Hybrid'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "Toyota GR010 Hybrid" '
                    '(https://lmu-dashboards.com). Two pages in the bottom bar, as in the original: the anti-roll bars, '
                    'and migration / regen (bind Next / Previous page to a wheel button).')
d['Source'] = 'Redadeg\'s SimHub dash "Toyota GR010 Hybrid", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions (sample.js) aren't used by any formula
assert len(d['Pages']) == 2 and not d.get('PageSets'), (d['Pages'], d.get('PageSets'))
d['Pages'] = ['ARB', 'MIG / REGEN']

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
# REGEN: the original's script calls replace(), which a library dash's checked scripts can't (only $prop and Math); the
# same in NCalc, with SimHub's replace().
regen = el('REGEN#', 'value')
assert regen['Bind'] == "js:return replace($prop('lmuDataPlugin.Redadeg.lmu.Extended.VM_REGEN_LEVEL'), 'kW', '');", regen['Bind']
regen['Bind'] = "ncalc:replace([lmuDataPlugin.Redadeg.lmu.Extended.VM_REGEN_LEVEL], 'kW', '')"
# A blink that SimHub turns on with a formula (BlinkEnabled bound to [SpeedKmh]>60: PIT LIMITER and STEADY 60 blink
# only over 60 km/h): the import made them blink all the time. Shown steady when the formula is false.
for name in ('TextItem', 'STEADY 60'):
    for e in [e for e in E if e.get('Name') == name and e['Type'] == 'label' and any('blink(' in c for c in vis(e))
              and e.get('Text') in ('PIT LIMITER', 'STEADY 60')]:
        # (the blink first: its timer runs all the time; a condition starting "!(" would read as a take-turns one)
        e['Visible'] = [c if 'blink(' not in c else c + ' or !([SpeedKmh]>60)' for c in vis(e)]

# The lap time shown again for 5 s after a lap ("LastLaptime#5sec") binds the same [CurrentLapTime] as the lap time
# under it, in the same box, font and colour: drawn over it, it shows exactly the same. Left out (one value, not two in
# one place redrawing each other).
# Oil and water temperatures: the original binds them with no format, so SimHub prints every digit of the game's
# number ("98.4316528320313"), right-aligned over its own "T OIL" / "T WATER" caption (the electric motor's temperature
# above them has "0"). Shown as T ELEC is: "0".
for n in ('Oil Temperature', 'Water Temperature'):
    v = el(n, 'value')
    assert v['Format'] == 'text', v
    v['Format'] = '0'
# The fuel: SimHub's FuelText item with its format "0" and "N/A" without data (the import used "0.0").
fuel = el('FuelText', 'value')
fuel.update(Format='0', Empty='N/A')
dup = el('LastLaptime#5sec', 'value')
assert dup['Bind'] == el('Laptime#', 'value')['Bind'] and (dup['X'], dup['Y'], dup['W'], dup['H']) == (el('Laptime#')['X'], el('Laptime#')['Y'], el('Laptime#')['W'], el('Laptime#')['H'])
E.remove(dup)

# ---------------------------------------------------------------------------------------------------------------------
# 3. The widest text each value really shows (the import copies SimHub's preview text: "Text", "FL Temp", "1").
SAMPLES = [   # (a text in the binding, samples): the first that matches, so the specific ones first
    ('driverlapsdonesincelastpitout', ['88', '-']), ('Fuel_CurrentLapConsumption', ['8.88']), ('Fuel_LastLapConsumption', ['8.88']),
    ('[EngineMap]*20', ['100']), ('[EngineMap]', ['88']), ('mElectricBoostMotorTemperature', ['188']), ('[OilTemperature]', ['188']),
    ('[WaterTemperature]', ['188']), ('EstimatedLapTime', ['8:88.888']), ('mVirtualEnergy', ['100']),
    ('energyPerLastLap', ['8.88']), ('mDeltaBest', ['-8.88']), ('[BestLapTime]', ['8:88.888']),
    ('TyrePressure', ['88.8', '888', '8.88']), ('mTireInnerLayerTemperature', ['188']), ('BrakeTemperature', ['1888']),
    ('[TCLevel]', ['88']), ('mTCSlip', ['88']), ('mTCCut', ['88']), ('[CurrentLapTime]', ['8.88.888']),
    ('[SpeedLocalUnit]', ['KMH', 'MPH']), ('[SpeedLocal]', ['388']), ('[BrakeBias]', ['88.8']),
    ('mBatteryChargeFraction', ['100']), ('mFrontAntiSway', ['88']), ('mRearAntiSway', ['88']),
    ('mMigration', ['2.5%', 'OFF']), ('VM_REGEN_LEVEL', ['888']), ('[Position]', ['88']), ('[SelfsplitDelta]', ['-8.888']),
    ('[LastLapTime]', ['8:88.888']), ('[BestSplitDelta]', ['-8.888']), ('driverbestlap', ['88:88.888', '-:---']),
    ('drivershortname', ['WWWWWWWW']), ('getopponentleaderboardposition', ['+888.88']), ('[Clutch]', ['100']),
    ('fuel', ['188', 'N/A']), ('gearText', ['8', 'N', 'R']),
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
    # the preview text SimHub's designer showed ("Text", "FL Temp"): the widest real text instead
    e['PreviewText'] = e['Samples'][0]
    # before a lap has a time SimHub formats the zero time ("0.00.000"); the wheel shows Empty for a time of 0
    if (e.get('Format') or '').startswith('time:'):
        e['Empty'] = e['Format'][5:].replace('\\', '').replace('m', '0').replace('ss', '00').replace('fff', '000')
        if e['Empty'] not in e['Samples']: e['Samples'] = e['Samples'] + [e['Empty']]   # it must fit too
for e in els('TextItem', 'value'):
    if 'SpeedLocalUnit' in e.get('Bind', ''): e['PreviewText'] = 'KMH'

# ---------------------------------------------------------------------------------------------------------------------
# 4. Overlays over SimHub's whole width cover the wheel's (0..790, not the scaled dash's 12..778): the engine stall band,
# the qualifying and practice lap summaries, the ignition screens. Their shapes reaching SimHub's edges reach the
# wheel's.
for e in E:
    if e['Type'] not in ('rect', 'box') or not own(e): continue
    if e['X'] <= 12: e['W'] += e['X']; e['X'] = 0
    if e['X'] + e['W'] >= 778: e['W'] = 790 - e['X']
    if e['Y'] + e['H'] > 460: e['H'] = 460 - e['Y']

# ---------------------------------------------------------------------------------------------------------------------
# 5. Every text in the original's size and place: each one measured from its original item with the original's font
# (Arial, Segoe UI, Arial Edit, Edge Racer at its weight), the screen font closest to it that fits picked, and its band
# centred on where the original's ink is. Items the original draws in the same font, size and weight get one font
# together (convert_kit.place_group). Areas: the original's box on the wheel (its text is inside it), unless AREAS says
# otherwise.
AREAS = {   # (name, a text in a condition or '') -> (x0, y0, x1, y1)
    # the speed, centred in the top of the gear panel: clear of the headlight icon (left) and the speed unit (right);
    # over the icon every speed change redrew it (the first conversion: 250 KB/s)
    ('SpeedText', ''): (278, 2, 489, 64),
    # the gear, under the speed: its 128 px band ended on the panel's bottom line (a flash when an overlay below went)
    ('GearText', ''): (236, 64, 553, 201),
    # the tyre temperatures in the corners of the pressures panel: the original's boxes (27 px) are shorter than the
    # nearest font (32 px); the room they have inside the panel's lines, not wider (grown with their group, they ran
    # into the pressures and the panels beside)
    ('FLTemp', ''): (237, 206, 302, 240), ('FRTemp', ''): (490, 206, 554, 240),
    ('HLTemp', ''): (237, 287, 302, 321), ('HRTemp', ''): (490, 287, 554, 321),
    # TC level / slip / cut: three rows 26 px apart in the TC panel (the original's boxes overlap each other)
    ('TCLevel#', ''): (660, 123, 776, 149), ('TCSLIP#', ''): (660, 149, 776, 176), ('TCCUT#', ''): (660, 176, 776, 202),
}
# (name, a text in a condition or '') -> screen font, where the closest isn't the right one
# TC rows: no normally spaced screen font is under 32 px; 60 (24 px) is the least wide of the small ones
FONTS = {('TCLevel#', ''): 60, ('TCSLIP#', ''): 60, ('TCCUT#', ''): 60}


def texts_of(e):
    return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def lookup(table, e):
    for (name, cond), v in table.items():
        if e.get('Name') == name and (cond == '' or any(cond in c for c in vis(e))): return v
    return None


def over_bar(e):
    return any(b['Type'] == 'bar' and b['X'] < e['X'] + e['W'] and e['X'] < b['X'] + b['W'] and b['Y'] < e['Y'] + e['H'] and e['Y'] < b['Y'] + b['H'] for b in E)


# Fixed texts smaller than the screen's normally spaced fonts (the smallest is 32 px; TC LEVEL, BIAS F, NRG LVL are
# ~26, under 25 even in the overlays) become pictures in 6.; at 32 px (STINT, FUEL, F LAST, HY MAP, T ELEC...) a
# normally spaced screen font fits them (convert_kit.tight: the 963 family), so they stay screen text. Pictures of
# them cost the RAM drive: drawn into the static layer, their anti-aliased letters took the left column's tiles from
# ~2 to 6-9 KB each (the first try: all captions as pictures, 61 KB of grid tiles). The bottom bar's captions are on
# pages (a picture there is a RAM-drive file per page) and 32 px: text. The overlays' captions stay text above 25 px. Texts over the state-of-charge bar stay text
# too: a picture over a bar is drawn with rectangles at every change of the bar.
def as_picture(e):
    if e['Type'] != 'label' or e.get('ColorBind') or not (e.get('Text') or '').strip() or over_bar(e): return False
    # (an overlay's caption as a picture is a RAM-drive file of its own: only under 25 px, where the screen has nothing
    # but its letter-spaced fonts; POLESITTER, BEHIND, AHEAD are 26-29 px)
    em = K.match(e, orig).em
    static = not [c for c in own(e) if 'blink(' not in c] and not K.pages_of(e)
    return em < 25 or (static and em < 30)


groups = {}
for e in E:
    if e['Type'] not in ('label', 'value') or as_picture(e): continue
    if not texts_of(e)[0].strip(): continue
    item = K.match(e, orig)
    area = lookup(AREAS, e) or (max(0, int(item.x)), max(0, int(item.y)), min(790, int(item.x + item.w)), min(460, int(item.y + item.h)))
    kw = {'chars': '0123456789NR'} if e.get('Bind') == 'gearText' else {}
    # the "%" after the state of charge is " %" in the original (the import strips the space): measured with it, where
    # its ink is, clear of the number before it
    if e['Type'] == 'label' and e.get('Name') == '%': kw = {'measure_text': item.it.get('Text')}
    if lookup(FONTS, e):
        K.place(e, orig, item, texts_of(e), area, font=lookup(FONTS, e))
        continue
    groups.setdefault((item.family, round(item.em, 1), item.weight, e.get('Bind') == 'gearText'), []).append((e, item, texts_of(e), area, kw))


# The screen's small fonts are wide and Arial is narrow: a text can need more room than its box in the original. The
# group's areas then grow sideways around their centres, as little as it takes (and a few px taller for a font that's a
# little taller), until one font fits them all.
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
# 6. Fixed texts as pictures in the original's own font. The screen draws text only in its own fonts, and below 32 px it
# has only the S fonts, spaced like a typewriter ("T C  L E V E L"): the first conversion's captions looked nothing like
# the original. A caption is the same text all the time, so it's drawn here as the original draws it (Arial or Segoe UI
# at its size and weight, its colour, anti-aliased) and placed where the original has it: part of the static layer (the
# RAM drive's tiles: no cost; without it, rectangles of its colour and black: MaxColors 2).
pictures = 0
for i, e in enumerate(E):
    if not as_picture(e): continue
    item = K.match(e, orig)
    pic = K.text_picture(orig, item, e['Text'], e.get('Color', '#FFFFFF')[:7])
    if pic is None: continue
    im, x, y = pic
    key = f'text {e["Name"]} {i}@{im.width}x{im.height}'
    d.setdefault('Images', {})[key] = picture(im)
    E[i] = {k: v for k, v in e.items() if k in ('Name', 'Visible', 'PreviewVisible')}
    E[i].update(Type='image', Image=key, X=x, Y=y, W=im.width, H=im.height, MaxColors=2)
    pictures += 1

# A value's box reaching into another text shown with it (the bottom bar's two ARB numbers, the SoC number and its "%",
# a tyre's pressure and its temperature: the original's boxes overlap, only their ink doesn't) or into a caption's
# picture (the oil and water temperatures, right-aligned up to the caption): every change would redraw the other one
# (a flash). Such boxes shrink to their widest text (+2 px each side), kept where their alignment holds them.
captions = [e for e in E if e['Type'] == 'image' and e['Image'].startswith('text ') and not own(e)]
shrunk, still = K.shrink_to_text(E, also=captions)
assert not still, still

# ---------------------------------------------------------------------------------------------------------------------
# 7. Shapes. A bordered box without rounded corners that comes and goes (every overlay's panel: setting changes, flags,
# lap summaries, the pit limiter) would be a picture on the RAM drive, one per look; two rectangles (the border colour,
# then the fill inset by the border) look the same, are two fills and take none. A box that's only an outline over an
# overlay's own background (the practice summary's "RectangleItem2": SimHub draws only its left and right borders) is
# those two lines.
for e in [e for e in E if e['Type'] == 'box' and not e.get('Radius') and own(e)]:
    if e.get('Fill'):
        K.box_as_rects(E, e)
    else:
        assert e.get('Name') == 'RectangleItem2', e
        i = E.index(e)
        b = max(1, e.get('Border', 1))
        line = {k: v for k, v in e.items() if k in ('Visible', 'PreviewVisible')}
        E[i:i + 1] = [dict(line, Type='rect', Name='RectangleItem2', X=e['X'], Y=e['Y'], W=b, H=e['H'], Color=e['Color']),
                      dict(line, Type='rect', Name='RectangleItem2 border', X=e['X'] + e['W'] - b, Y=e['Y'], W=b, H=e['H'], Color=e['Color'])]

# The bottom bar's frame is the same on both pages (the widget draws it on each screen): one frame, always shown, so a
# page flip redraws only the texts and an overlay going from over it doesn't wipe the page's box with it (the demo
# showed that at 85 s). Page 2's own box (SimHub's "RectangleItem2", around migration) adds only its right border, the
# divider: a 1 px line on that page.
frames = [e for e in E if e['Type'] == 'box' and e.get('Name') == 'RectangleItem' and K.pages_of(e)]
assert len(frames) == 2 and len({(e['X'], e['Y'], e['W'], e['H'], e['Color'], e.get('Border')) for e in frames}) == 1, frames
for e in frames:
    if K.pages_of(e) == ['page:1']: E.remove(e)
    else: e['Visible'] = None
mig = el('RectangleItem2', 'box', cond='page:1')
assert not mig.get('Fill') and mig['Border'] == 1, mig
E[E.index(mig)] = {'Type': 'rect', 'Name': 'RectangleItem2', 'X': mig['X'] + mig['W'] - 1, 'Y': mig['Y'], 'W': 1, 'H': mig['H'],
                   'Color': mig['Color'], 'Visible': mig['Visible']}

# The ignition screens: the Gazoo logo on white (ignition on, 1.2 s) and the same at 8 % on black (ignition off). The
# wheel draws pictures opaque, so the 8 % is mixed in here (white -> #141414). Each picture is a white (or #141414)
# rectangle with only the logo's own area as a picture over it: the same look, the RAM drive holds the logo, not a
# screenful of one colour.
# (without the RAM patch: rectangles in as few colours as it has: black and red on white, 3; at 8 % on near-black, 2:
# 93 / 70 KB at 4, longer than the 1.2 s the ignition-on screen shows)
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') == 'ImageItem']:
    off = e.get('Opacity', 100) < 100
    K.logo_on_plain(d, E, e, f'gazoo {"ignition off" if off else "ignition on"}', 2 if off else 3)
for n in ('IGN OFF background', 'IGN ON background'):
    for e in els(n, 'rect'):
        e.update(X=0, Y=0, W=790, H=460)
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]   # pictures no element uses any more

# ---------------------------------------------------------------------------------------------------------------------
# 8. Values on their own overlay's box (the lap summaries' panels, a flag's text on its colour): drawn on its colour
# (convert_kit.backgrounds_from_overlays); a value over its panel's edge by a pixel or two moved inside it.
K.snap_into_panels(E)
K.backgrounds_from_overlays(E)
K.keep_inside_boxes(E)
# Values half under an overlay's box hide while it shows (convert_kit.take_turns): else each change drew them, then
# the box over them.
turns = K.take_turns(E, d['Images'])

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); a second
# run must change nothing (the /create-dash gate)
bands = K.fx('fit-bands', OUT)['changes']
again = K.fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'unplaced': unplaced, 'widened': widened, 'text_pictures': pictures,
                  'take_turns': turns, 'shrunk': sorted(set(shrunk)), 'fit_bands': bands, 'import_notes': report['Notes']}, indent=1))
