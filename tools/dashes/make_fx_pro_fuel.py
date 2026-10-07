"""
The "FX-Pro Fuel" dash: a community SimHub dash for Le Mans Ultimate (800x480, made for the FX Pro's screen size)
converted for the FX Pro.

    python tools/dashes/make_fx_pro_fuel.py [OUT.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\fx-pro-fuel.json)

Needs the SimHub dash "FX-Pro Fuel" installed (its folder in SimHub's DashTemplates, with its JavascriptExtensions and
_SHFonts) and fxdash built (dotnet build -c Release tools/fxdash/fxdash.csproj). Re-running it re-imports from the SimHub
dash, so every change to the conversion lives here, with its reason. `simhub_ref.py` draws the original for side-by-side
looks.

The original has three in-game screens the driver flips with SimHub's next / previous screen: Fuel (the doX fuel and
virtual energy table, laps / fuel time / energy time / time to go, lift & coast, the pit stop estimate), CAS (a radar of
the car behind: distance and gap gauges, spotter arrows) and Tyres (wear of the four tyres). Here they are the dash's
three pages, in that order. Over the Fuel screen: the pit limiter, the pit time after leaving the pits, penalties, the
lap summary after each lap (one per session type), ignition off, the CAS radar when a car closes in, the blue flag
(who's behind, how far) and the flags (a coloured frame).

Its data comes from other SimHub plugins, which the driver needs too: doX LMU Session Data (doX_LMU_SessionDataPlugin:
the fuel table, laps), LMU NeoRed (LMU_NeoRedPlugin: pit stop estimate, class position, tyres), Persistant Tracker
(PersistantTrackerPlugin: the car behind), Lovely (LovelyPlugin.TeamLINQ: flags), and the dash's own JavaScript helpers
(its JavascriptExtensions folder: haa_* functions, doXTiming*, ld_getSim). The converted dash points at that folder
where SimHub installs the dash (ScriptsFolder).
"""
import base64, io, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_kit as K  # noqa: E402

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
    r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\fx-pro-fuel.json'
SIMHUB_DASH = 'FX-Pro Fuel'
SCRIPTS = r'C:\Program Files (x86)\SimHub\DashTemplates\FX-Pro Fuel\JavascriptExtensions'
PAGES = [('Fuel', 'FUEL'), ('CAS', 'CAS'), ('Tyres', 'TYRES')]   # (SimHub screen, page name)

work = os.path.join(os.path.dirname(OUT), '_build')
os.makedirs(work, exist_ok=True)
vis, own = K.vis, K.own

# ---------------------------------------------------------------------------------------------------------------------
# 1. The three in-game screens, each imported on its own, as the dash's three pages. An element of screen N shows on
# page N (its "page:N" condition first); pictures keep their names (the same picture in two screens is one).
d, E, images, reports, origin = None, [], {}, {}, []
for n, (screen, _) in enumerate(PAGES):
    raw = os.path.join(work, f'fx-pro-fuel.{screen.lower()}.import.json')
    reports[screen] = K.fx('import', SIMHUB_DASH, raw, '--screen', screen, '--fit', '790,460')['report']
    s = json.load(open(raw, encoding='utf-8'))
    for k, v in (s.get('Images') or {}).items():
        assert images.get(k, v) == v, k
        images[k] = v
    for e in s['Elements']:
        e['Visible'] = [f'page:{n}'] + vis(e)
        E.append(e)
        origin.append(screen)
    if d is None: d = s
d['Elements'], d['Images'] = E, images
d['Pages'] = [p for _, p in PAGES]
orig = K.Original(SIMHUB_DASH)
SC = 460 / 480                   # SimHub's 800 x 480 on the wheel's 790 x 460
OX = (790 - 800 * SC) / 2


def wx(x): return OX + x * SC
def wy(y): return y * SC


SCREEN = {id(e): s for e, s in zip(E, origin)}


def screen_of(e): return SCREEN.get(id(e))


def match(e):
    # (without its page condition: K.match takes a page for a widget's screen; among items of the same name, those where
    # the importer put it first: the lap summary's driver names are named as the blue flag screen's)
    bare = dict(e, Visible=[c for c in vis(e) if not K.PAGE.match(c)])
    cands = [o for o in orig.items if o.screen == screen_of(e)]
    # the lap summary's three versions (the widget's screens 0-2) use the same names at other places: its own only
    if session(e) is not None:
        cands = [o for o in cands if o.widget == 'NewLap_PopUp' and o.page == session(e)] or cands
    box = POS.get(id(e), (e['X'], e['Y']))
    near = [o for o in cands if o.name == e.get('Name') and abs(o.x - box[0]) < 40 and abs(o.y - box[1]) < 40]
    return K.match(bare, orig, near or cands)


POS = {id(e): (e['X'], e['Y']) for e in E}   # where the importer put each element (the original's place)


def has(e, text): return any(text in c for c in vis(e))


def session(e):
    """Which of the lap summary's screens (0 practice, 1 qualifying, 2 race) `e` is on, or None."""
    for n in (0, 1, 2):
        if has(e, '})()) == %d;' % n): return n
    return None


# The Fuel screen's layers over the whole screen (the pit limiter's frame, ignition off, the blue flag, the flags) show
# over every page here: in the original they are part of the Fuel screen only, but they matter as much on the CAS and
# Tyres pages (on the Fuel page nothing changes). The pop-ups over part of it (the pit time after the pits, penalties,
# the lap summary, the CAS radar when a car closes in) stay on the Fuel page, as in the original: over the other pages
# they cut through the tyres and the radar (shapes half under a pop-up are drawn again around it), and the CAS radar is
# that page already.
OVERLAYS = ("$prop('EngineIgnitionOn')==1 && ($prop('IsInPit')", 'EngineIgnitionOn]<1', "$prop('Flag_Blue')", "blink('flaggen-")
PAGE0_POPUPS = ('isdecreasing(8000', 'haa_isDT() || haa_isSG()', 'changed(5000, [LastLapTime])', 'changed(6000')
for e in E:
    if screen_of(e) == 'Fuel' and any(has(e, t) for t in OVERLAYS):
        e['Visible'] = [c for c in vis(e) if c != 'page:0']
# ... and are drawn after all three pages (later on top): imported per screen, the CAS and Tyres pages came after them
everywhere = [e for e in E if screen_of(e) == 'Fuel' and not K.pages_of(e)]
ids = {id(e) for e in everywhere}
E[:] = [e for e in E if id(e) not in ids] + everywhere


# ---------------------------------------------------------------------------------------------------------------------
# 2. Identity
d['Id'] = 'fx-pro-fuel'
d['Name'] = 'FX-Pro Fuel'
d['Description'] = ('Le Mans Ultimate: fuel and virtual energy, pit stop estimate, CAS radar and tyre wear on three pages '
                    '(bind Next / Previous page to a wheel button). Needs the SimHub dash "FX-Pro Fuel" installed (its '
                    'JavaScript helpers) and the doX LMU Session Data, LMU NeoRed, Persistant Tracker and Lovely plugins.')
d['Source'] = 'SimHub dash "FX-Pro Fuel"'
d['ScriptsFolder'] = SCRIPTS



from PIL import Image   # noqa: E402


def picture(im):
    buf = io.BytesIO(); im.save(buf, 'PNG')
    return base64.b64encode(buf.getvalue()).decode('ascii')


def els(name, typ=None, cond=None, screen=None):
    return [e for e in E if e.get('Name') == name and (typ is None or e['Type'] == typ) and (cond is None or has(e, cond))
            and (screen is None or screen_of(e) == screen)]


def el(name, typ=None, cond=None, screen=None):
    h = els(name, typ, cond, screen)
    assert len(h) == 1, (name, typ, cond, screen, len(h))
    return h[0]


def replace(old, new):
    """`new` (a list) where `old` was, counted as part of the same screen."""
    i = E.index(old)
    E[i:i + 1] = new
    for e in new: SCREEN[id(e)] = SCREEN[id(old)]


# ---------------------------------------------------------------------------------------------------------------------
# 3. What isn't drawn.
left_out = {}


def drop(why, pred):
    gone = [e for e in E if pred(e)]
    for e in gone: E.remove(e)
    left_out[why] = len(gone)


# The lap summary's fourth screen: shown when SessionTypeName maps to 3, and the map (Practice 0, Qualify 1, Race 2,
# else -1) never gives 3. SimHub never shows it.
# The other three (practice, qualifying, race) have no background in the original: SimHub draws them straight over the
# fuel table, the laps panel and the pit stop panel, the texts of both on top of each other. Only that fourth screen has
# the author's panel (dark, a grey edge, over the lower part of the screen): it goes under the other three here, so the
# summary reads on its own (and its changing texts don't land on the panels under them).
panel = next(e for e in E if has(e, '?? -1; })()) == 3') and e['Type'] == 'gradient')
panel['Visible'] = [c for c in vis(panel) if '?? -1; })()) == 3' not in c]
drop('lap summary screen 4 (never shown)', lambda e: has(e, '?? -1; })()) == 3'))
E.remove(panel)
# (on the wheel: from 194, so the summary's captions at the panel's top sit on it, to 773, so it covers the pit stop panel
# under it whole: a panel half under it is drawn again around it at every update, 130 KB in a second)
panel.update(Y=194, H=460 - 194, W=773 - panel['X'])
first = next(e for e in E if has(e, 'changed(5000, [LastLapTime])'))
E.insert(E.index(first), panel)
# The CAS radar's look while the engine is off (a perspective grid picture over the whole screen, the radar's title
# "CAS-M3 Motorsport Radar System", a logo, the gauges again): a screenful of picture for a radar with nothing to show
# while the engine is off; on the Fuel page the IGNITION OFF screen is under it anyway.
drop('CAS radar with the engine off', lambda e: has(e, 'if ([DataCorePlugin.GameData.EngineIgnitionOn], 0, 1)'))
# The fuel table's and laps panel's optional custom colour (a doX plugin setting, UseCustomBackgroundColor): a second
# background under the default one, coloured by a property; the default look (the panels' gradient, 5.) is kept.
drop('doX custom panel colour (a doX setting)', lambda e: e['Type'] in ('box', 'rect') and e.get('ColorBind', '').startswith('ncalc:[doX_LMU_SessionDataPlugin.CustomBackgroundColor]'))
# The red fuel and blue energy bars behind their rows are a doX option too (FuelCompanionShowLevelBars, on by default):
# always drawn here, so the numbers on them are drawn on the bar's colour (with the option off, on the panel's).
for e in E:
    if has(e, '[doX_LMU_SessionDataPlugin.FuelCompanionShowLevelBars]'):
        e['Visible'] = [c for c in vis(e) if c != 'ncalc:[doX_LMU_SessionDataPlugin.FuelCompanionShowLevelBars]']
for e in E:
    if has(e, 'not [doX_LMU_SessionDataPlugin.UseCustomBackgroundColor]'):
        e['Visible'] = [c for c in vis(e) if c != 'ncalc:not [doX_LMU_SessionDataPlugin.UseCustomBackgroundColor]']
# The Tyres page is drawn again in 6. (its widgets are turned a quarter, their tyre pictures chosen by name).
drop('Tyres page as imported (rebuilt in 6.)', lambda e: screen_of(e) == 'Tyres' and not (e['Type'] == 'label' or e.get('Name') == 'RectangleItem'))

# ---------------------------------------------------------------------------------------------------------------------
# 4. What the original sets from its scripts (SimHub binds a size or place to a formula; the importer takes the
# designer's value).
# The laps / fuel time / energy time / time to go panel: its background's height is doXTimingBackgroundHeight(), the
# rows shown (all four: 50 + 3 x 32) + 1 = 147; the designer's 60 covers only the first row.
# Its condition (a script: not under the garage's setup cover, rows to show) is the rows' own (an NCalc formula, the same
# test) plus the rows' existence: written as the rows', so the checks see they show together (a row's value is kept off
# the panel's border).
rows_cond = next(c for e in E if screen_of(e) == 'Fuel' for c in vis(e) if c.startswith('ncalc:([doX_LMU_SessionDataPlugin.SpectatorMode] = 0) and not ([doX_LMU_SessionDataPlugin.SetupCoverVisible]'))
for e in [e for e in E if has(e, 'doXSetupCoverActive')]:
    e['H'] = round(147 * SC)
    e['Visible'] = [c for c in vis(e) if 'doXSetupCoverActive' not in c] + ([rows_cond] if rows_cond not in vis(e) else [])
# Lift & Coast's stint bar: SimHub's gauge turned 270 degrees (filling from the bottom), so on the wheel it stands in its
# group's column (the group at 354, 239; 85 x 180, above the green box).
bar = el('LinearGaugeItem', 'bar', screen='Fuel')
bar.update(X=round(wx(354)), Y=round(wy(239)), W=round(85 * SC), H=round(180 * SC), Orientation='vertical', Reverse=False)


# The CAS radar's markers move with the data (SimHub binds their Top to the distance or gap of the cars behind: a square
# turned 45 degrees on the ruler, coloured by the value): the wheel can't move an element. Each becomes a slim bar beside
# its ruler, from the ruler's 0 up to where SimHub puts the marker, in the marker's colours (ColorStops): the car right
# behind next to the numbers, the one behind it beside that. (A marker per step of the ruler was tried: 220 shapes, each
# its own overlay.)
markers = 0
for m in [e for e in E if e.get('Name', '').startswith('behind 0') and e['Type'] == 'box']:
    n = m['Name'][7:9]
    if m['Name'].endswith('seconds'):
        prop, hi = f'PersistantTrackerPlugin.DriverBehind_{n}_Gap', 3
        top = lambda v, base=783 / 2 + (55 if n == '00' else 57): base - v / 3 * 783 / 2
        x = 709 if n == '00' else 701
    else:
        prop, hi = f'PersistantTrackerPlugin.DriverBehind_{n}_Distance', 50
        top = lambda v, base=783 / 2 + (59 if n == '00' else 58): base - v / 50 * 783 / 2
        x = 66 if n == '00' else 75
    y0, y1 = round(wy(top(hi) + 15)), round(wy(top(0) + 15))
    replace(m, [{'Type': 'bar', 'Name': m['Name'], 'Bind': f'ncalc:isnull([{prop}], 0)', 'Min': 0, 'Max': hi, 'Orientation': 'vertical',
                 'X': x, 'Y': y0, 'W': 6, 'H': y1 - y0, 'Color': '#' + m['ColorStops'][0]['Color'][3:], 'ColorBind': f'ncalc:[{prop}]',
                 'ColorStops': m['ColorStops'], 'Visible': vis(m), 'PreviewVisible': False}])
    markers += 1
# ("Dist [m]" under the left ruler moves right, "RTG [s]" under the right one left, clear of the bars; the right ruler's
# bars are left of its numbers: on the ruler's picture each change of the bar would draw the picture again)
# (done after 9.: they become pictures there, drawn where the original has them)


def steps(prop, lo, hi, step):
    v = lo
    while v < hi - 1e-9:
        b = min(hi, round(v + step, 6))
        yield v, b, f'ncalc:[{prop}] >= {v:g} and [{prop}] {"<=" if b >= hi else "<"} {b:g}'
        v = b


# The arrows of the car behind (close, red, 1-10 m; mid, orange, 10-35 m; far, green, 35-50 m) move up the screen with
# the distance too (Top bound to it): in steps of 5 m, each a copy of the arrow at its place for the step's middle (over
# black every copy is the same picture: one file on the RAM drive).
ARROWS = {'behind close': (1, 10, 92), 'behind mid': (10, 35, 45), 'behind far': (35, 50, 2)}
prop = 'PersistantTrackerPlugin.DriverBehind_00_Distance'
for a in [e for e in E if e.get('Name') in ARROWS and e['Type'] == 'image']:
    lo, hi, off = ARROWS[a['Name']]
    base = [c for c in vis(a) if prop not in c or 'changed(' in c]
    out = [dict(a, Name=f'{a["Name"]} {v:g}', Y=round(wy(783 / 2 - off - (v + b) / 2 / 50 * 783 / 2)), Visible=base + [cond], PreviewVisible=False)
           for v, b, cond in steps(prop, lo, hi, 5)]
    replace(a, out)
    markers += len(out)

# The green Lift & Coast box: its colour formula gives transparent ('#00000000') when not coasting, showing the black
# page through it; black itself looks the same and the screen can draw the box (it draws rounded boxes in solid colours
# only).
lc = el('Lift&Cost', 'box')
assert "'#00000000'" in lc['ColorBind'], lc['ColorBind']
lc['ColorBind'] = lc['ColorBind'].replace("'#00000000'", "'#FF000000'")

# ---------------------------------------------------------------------------------------------------------------------
# 5. Shapes that come and go.
# Overlays over SimHub's whole screen cover the wheel's (0..790, not the scaled dash's 12..778): ignition off, the CAS
# radar, the lap summary's panel edges... (not the coloured frames: they are the dash's frame, below)
for e in E:
    if e['Type'] not in ('rect', 'box') or not own(e) or (e['Type'] == 'box' and not e.get('Fill')): continue
    if e['W'] < 700: continue
    if e['X'] <= 12: e['W'] += e['X']; e['X'] = 0
    if e['X'] + e['W'] >= 778: e['W'] = 790 - e['X']
    if e['Y'] < 0: e['H'] += e['Y']; e['Y'] = 0
    if e['Y'] + e['H'] > 460: e['H'] = 460 - e['Y']
# The flags and the pit limiter draw a 10 px coloured frame over the dash's grey one (43 px corners), blinking: a ring
# over everything is hundreds of rectangles at each blink (corners over 24 px can't be the screen's own, and nothing of
# one colour is inside it). Here: four straight strips along the frame, the corners square.
# (the pit limiter's badge and frame are 90 % opaque: mixed over the black page here, solid. See-through over a bar, its
# colour came back wrong where the bar went: 239 for 214 red)
for e in [e for e in E if e['Type'] in ('box', 'rect') and own(e) and e.get('Opacity', 100) < 100]:
    a_ = e['Opacity'] / 100
    mixc = lambda c: '#%02X%02X%02X' % tuple(round(int(c.lstrip('#')[-6:][i:i + 2], 16) * a_) for i in (0, 2, 4))
    e['Color'] = mixc(e['Color'])
    if e.get('Fill'): e['Fill'] = mixc(e['Fill'])
    e['Opacity'] = 100
strips = 0
for e in [e for e in E if e['Type'] == 'box' and not e.get('Fill') and e['W'] >= 700 and own(e)]:
    b = round(e.get('Border', 10) * SC)
    x0, y0, x1, y1 = e['X'], max(0, e['Y']), e['X'] + e['W'], min(460, e['Y'] + e['H'])
    keep = {k: v for k, v in e.items() if k in ('Visible', 'PreviewVisible')}
    parts = [('top', x0, y0, x1 - x0, b), ('bottom', x0, y1 - b, x1 - x0, b), ('left', x0, y0 + b, b, y1 - y0 - 2 * b),
             ('right', x1 - b, y0 + b, b, y1 - y0 - 2 * b)]
    replace(e, [dict(keep, Type='rect', Name=f'{e["Name"]} {n}', X=x, Y=y, W=w, H=h, Color=e['Color']) for n, x, y, w, h in parts])
    strips += 1
# The blue flag's screen (the car behind: position, name, distance, class): a blue box with the frame's 43 px corners;
# the screen draws rounded boxes itself up to 24 px corners (~150 bytes; bigger ones are hundreds of rectangles).
for e in [e for e in E if e['Type'] == 'box' and e.get('Fill') and e['W'] >= 700 and (e.get('Radius') or 0) > 24 and own(e)]:
    e['Radius'] = 24
# A bordered box without rounded corners that comes and goes (the CAS radar's black screen with its white edge) is a
# picture on the RAM drive; two rectangles (border colour, then the fill inset) look the same and take none.
for e in [e for e in E if e['Type'] == 'box' and not e.get('Radius') and e.get('Fill') and own(e)]:
    K.box_as_rects(E, e)
    for r in E:
        if id(r) not in SCREEN: SCREEN[id(r)] = 'Fuel'
# The dash's grey frame (Fuel and Tyres pages): its 43 px corners are more than the screen draws itself (24), so it was
# a 766 x 460 picture on the RAM drive (11 KB, the biggest file); with 24 px corners it's a few commands and no file.
for e in [e for e in E if e['Type'] == 'box' and e['W'] >= 700 and (e.get('Radius') or 0) > 24 and not own(e)]:
    e['Radius'] = 24
# Gradients under changing text (the fuel table's panel and its red fuel / blue energy bars, the pit stop panel, the laps
# panel, the lap summary): a value over a gradient has many colours under its text, so every change wipes and redraws
# the gradient there first (slow, and it flashes). Each is one colour here, the average of its stops (seen over black),
# with its border and corners (24 px at most: the screen draws a rounded box that comes and goes itself up to that).
def over_black(c):
    h = c.lstrip('#')
    a = int(h[:2], 16) / 255 if len(h) == 8 else 1
    return [int(h[i:i + 2], 16) * a for i in ((2, 4, 6) if len(h) == 8 else (0, 2, 4))]


flattened = 0
for e in [e for e in E if e['Type'] == 'gradient']:
    cs = [over_black(c) for c in e['Colors']]
    fill = '#%02X%02X%02X' % tuple(round(sum(c[k] for c in cs) / len(cs)) for k in range(3))
    # (opaque: the lap summary's panel is 98 %; a value on a see-through panel has the dash's colours under its text)
    e.update(Type='box', Fill=fill, Radius=min(24, e.get('Radius') or 0), Opacity=100)
    # (a see-through border, the pit panel's #C0808080: mixed over the black around the panel. The screen draws a rounded
    # box that comes and goes itself only in solid colours; see-through, it's ~8 KB of rectangles at every page flip)
    e['Color'] = fill if not e.get('Border') else '#%02X%02X%02X' % tuple(round(c) for c in over_black(e['Color']))
    e.pop('Colors', None); e.pop('Angle', None)
    flattened += 1
# The CAS radar's arrows are one colour on black, with an anti-aliased edge: 3 colours without the RAM patch (6 took
# 17-23 KB of rectangles each time one shows).
for e in [e for e in E if e['Type'] == 'image' and e.get('Image', '').startswith(('arrowbehind', 'OTarrow'))]:
    e['MaxColors'] = 3
# The CAS radar's spotter arrows (a car on the left / right; blinking 5 times a second) reach over the rulers, their
# numbers and the bars beside them: a picture over anything else is drawn with rectangles (on the wheel each blink took
# 0.9-1.5 s, 2026-10-06), so the page never caught up. Each is scaled down about its inner end to fit between the left
# bars (to x 82) and the right bars (from x 699), keeping its height in proportion.
for e in [e for e in E if e['Type'] == 'image' and e.get('Name') in ('left', 'right')]:
    x0, x1 = (84, e['X'] + e['W']) if e['Name'] == 'left' else (e['X'], 697)
    h = round(e['H'] * (x1 - x0) / e['W'])
    e.update(X=x0, W=x1 - x0, Y=e['Y'] + (e['H'] - h) // 2, H=h)
    im = Image.open(io.BytesIO(base64.b64decode(d['Images'][e['Image']]))).convert('RGBA').resize((e['W'], h), Image.LANCZOS)
    key = e['Image'].rsplit('@', 1)[0] + f'@{e["W"]}x{h}'
    d['Images'][key] = picture(im)
    e['Image'] = key
# Pictures reaching past the screen (the CAS gauges' ruler at x -5): cut to it.
for e in [e for e in E if e['Type'] == 'image']:
    K.crop_to_screen(d, e)

# ---------------------------------------------------------------------------------------------------------------------
# 6. The Tyres page. The original's four tyre widgets are turned a quarter (SimHub's Rotation on the widget, the wear
# text turned back inside), each told its tyre by a widget variable (TyreSide), with a photo of the tyre chosen by its
# compound's name (Soft / Medium / Hard / Wet) now and after the planned stop. Turned, they make four columns under
# HL HR VL VR (rear left, rear right, front left, front right): the tyre now with its wear, and when the stop changes it,
# an arrow and the new one. The wheel can't turn or choose pictures, so it's built here the same way up: a column per
# tyre under its letters. The tyre is a simple drawing (a dark tyre, a ring in the compound's colour where the photos
# have their coloured band, the grey rim, a dark pill for the wear), one picture per compound shown by the compound's
# name: the same picture in the four columns is one file on the RAM drive, four files in all. (The photos took ~590 KB
# of rectangles without the RAM patch; drawn as ovals by the screen, the page took ~1 s on the wheel, 2026-10-06.) The
# compound's name under it in the original's compound colours (LMU-Superdash's COMPOUND_COLOR_MAP), and while a change is
# planned an arrow down and the compound it gets.
# (The original's wear calls haa_tyrewear() without a side and shows nothing; here SimHub's TyreWear of each tyre, what
# that helper returns for a side.)
from PIL import Image   # noqa: E402
import zipfile   # noqa: E402
res = zipfile.ZipFile(os.path.join(orig.dash.folder, 'TyresRight.djson.ressources'))
arrow = Image.open(io.BytesIO(res.read('Arrow.png'))).convert('RGBA').rotate(-90, expand=True)
aw, ah = 28, 44
d['Images'][f'arrow down@{aw}x{ah}'] = picture(arrow.resize((aw, ah), Image.LANCZOS))
COMPOUND = "if({0}='Medium','#FFFF00',if({0}='Hard','#FF0000',if({0}='Wet','#1E90FF','#E6E6E6')))"
P, TY = 150, 70   # tyre size and top
COLOURS = {'Soft': (230, 230, 230), 'Medium': (255, 255, 0), 'Hard': (255, 0, 0), 'Wet': (30, 144, 255)}


def tyre_picture(rgb, k=4):
    from PIL import ImageDraw
    big = Image.new('RGBA', (P * k, P * k), (0, 0, 0, 0))
    g = ImageDraw.Draw(big)
    c = P * k / 2
    def disc(r, fill): g.ellipse([c - r, c - r, c + r, c + r], fill=fill)
    disc(P * k / 2, (30, 30, 30, 255))                 # the tyre
    disc((P - 16) * k / 2, rgb + (255,))              # the compound's band
    disc((P - 26) * k / 2, (30, 30, 30, 255))
    disc(96 * k / 2, (74, 74, 74, 255))                # the rim
    g.rounded_rectangle([c - 44 * k, c - 20 * k, c + 44 * k, c + 20 * k], radius=10 * k, fill=(0, 0, 0, 255))   # the wear's pill
    return big.resize((P, P), Image.LANCZOS)


for comp, rgb in COLOURS.items():
    d['Images'][f'tyre {comp}@{P}x{P}'] = picture(tyre_picture(rgb))
frame = el('RectangleItem', 'box', screen='Tyres')
after = frame
for letters, side, prefix in (('HL', 'rl', 'RearLeft'), ('HR', 'rr', 'RearRight'), ('VL', 'fl', 'FrontLeft'), ('VR', 'fr', 'FrontRight')):
    lab = el(letters, 'label', screen='Tyres')
    cx, cy = round(lab['X'] + lab['W'] / 2), TY + P // 2
    name = f'[LMU_NeoRedPlugin.Tyre.{side}_TyreCompound_Name]'
    planned = f"js:return haa_pittyrevisibility('{side}');"

    others = ' and '.join(f"{name} != '{c}'" for c in COLOURS if c != 'Soft')
    new = [{'Type': 'image', 'Name': f'tyre {letters} {comp}', 'Image': f'tyre {comp}@{P}x{P}', 'X': cx - P // 2, 'Y': TY, 'W': P, 'H': P,
            'MaxColors': 8, 'Visible': ['ncalc:' + (others if comp == 'Soft' else f"{name} = '{comp}'")], 'PreviewVisible': comp == 'Soft'}
           for comp in COLOURS] + [
        {'Type': 'value', 'Name': f'wear {letters}', 'Bind': f'ncalc:[TyreWear{prefix}]', 'Format': r'0\%', 'Samples': ['100%'], 'PreviewText': '74%',
         'Empty': '-', 'X': cx - 42, 'Y': cy - 18, 'W': 84, 'H': 36, 'Color': '#FFFFFF', 'Background': '#000000', 'Align': 'center', 'Font': 101},
        {'Type': 'value', 'Name': f'compound {letters}', 'Bind': 'ncalc:' + name, 'Format': 'text', 'Samples': ['Medium'], 'PreviewText': 'Soft',
         'Empty': '', 'ColorBind': 'ncalc:' + COMPOUND.format(name), 'X': cx - 85, 'Y': 232, 'W': 170, 'H': 40, 'Color': '#E6E6E6', 'Align': 'center', 'Font': 101},
        {'Type': 'image', 'Name': f'arrow {letters}', 'Image': f'arrow down@{aw}x{ah}', 'X': cx - aw // 2, 'Y': 290, 'W': aw, 'H': ah,
         'MaxColors': 2, 'Visible': [planned], 'PreviewVisible': False},
        {'Type': 'value', 'Name': f'compound after pit {letters}', 'Bind': f"js:return haa_compoundafterpit('{side}');", 'Format': 'text',
         'Samples': ['Medium'], 'PreviewText': 'Medium', 'Empty': '',
         'ColorBind': f"js:var c = haa_compoundafterpit('{side}'); return c == 'Medium' ? '#FFFF00' : c == 'Hard' ? '#FF0000' : c == 'Wet' ? '#1E90FF' : '#E6E6E6';",
         'X': cx - 85, 'Y': 364, 'W': 170, 'H': 40, 'Color': '#E6E6E6', 'Align': 'center', 'Font': 101, 'Visible': [planned], 'PreviewVisible': False},
    ]
    for e in new:
        e['Visible'] = ['page:2'] + vis(e)
        E.insert(E.index(after) + 1, e)
        SCREEN[id(e)] = 'Tyres'
        after = e
# (the letters stay last: drawn after the frame, as in the original)

# ---------------------------------------------------------------------------------------------------------------------
# 7. The widest text each value really shows (the import copies SimHub's designer text: "Text", "1", "Berechne...").
SAMPLES = {   # element name -> samples (the widest first)
    'Lift&Coast': ['88.88 s'], 'Lift&Coast Stint': ['88:88.88'],
    'Total Time': ['+188.8s'], 'Time Driver Swap': ['+88.8s'], 'Time Tires': ['+88.8s'], 'Time Refuel': ['+88.8s'], 'Time Damage': ['+88.8s'],
    'Fuel Ratio Avg': ['8.88', '-'], 'Fuel Ratio Last': ['8.88', '-'], 'Fuel Ratio': ['8.88', '-'],
    'Refuel VE': ['100%'], 'Remain VE': ['(88.8 laps)'], 'Current VE': ['100.0%'], 'VE per lap': ['8.88%'], 'VE Avg': ['8.88%'],
    'VE Gap': ['+88.88%'], 'Refuel fuel': ['188L'], 'Remain Fuel': ['(88.8 laps)'], 'Current Fuel': ['188.8L'],
    'Fuel per lap': ['8.88L', '-'], 'Fuel Avg': ['8.88L'], 'Fuel Gap': ['+188.88L'],
    'Current Lap': ['888'], 'Estimated Laps': ['/~888.8'], 'Fuel time value': ['88:88:88'], 'VE time value': ['88:88:88'],
    'TextItem': None,   # by binding below
    'PitStop Planned Value': ['88'], 'TotalPitStopTime Value': ['88:88:888'], 'PitStopTime Value': ['88:88:888'],
    'StopGo Time value': ['/ 88:88'], 'StopGo Value': ['8 Tours'], 'DT Value': ['88:88'], 'Penalty time Value': ['88:88'],
    'Sector1 Value': ['8:88.888'], 'Sector2 Value': ['8:88.888'], 'Sector3 Value': ['8:88.888'],
    'BestLapTime': ['8:88.888'], 'Last Laptime Value': ['8:88.888'], 'Last lap time': ['8:88.888'], 'Last vs AllTimeBest': ['+88.888'], 'Last vs BestLapTime': ['+88.888'],
    'TextItem2': ['Qualification', 'Practice'], 'LAP_VALUE': ['LAP 888'], 'Class position Value': ['88'],
    'CarClassText': ['LMGT3', 'Hyper', 'LMP2'], 'driverdeltatobestinclass': ['+88.888'], 'driverdeltatobest': ['+88.888'],
    'Penalty Value': ['8'], 'LeaderboardOpponentLastLap': ['8:88.888'], 'LeaderboardOpponentGap': ['+888.88'],
    'LeaderboardOpponentCarClassText': ['HYPERCAR'], 'distqnce behind': ['48m', '40m', '30m', '20m', '10m', '9m'], 'LeaderboardOpponentPositionText': ['88'],
}
BY_BIND = [   # (a text in the binding, samples) for names used twice
    ("if($prop('PitLimiterOn')", ['Pit Limiter OFF']), ('SessionTimeLeft', ['88:88:88', '--']),
    ('safetyFuelReserveLiters', ['Box in Runde: 888', 'BOX DIESE RUNDE!']),
    ('drivershortname', ['J. Doeson']), ('drivername(getopponentleaderboardposition_playerclassonly', ['Jonathan Doeson']),
    ('drivername(getopponentleaderboardposition_aheadbehind', ['Jonathan Doeson']), ('[LMU_NeoRedPlugin.Pit.StopAndGo]', ['Yes (3 Tours)']),
]
# The pit window's "BOX DIESE RUNDE!" is wrapped in two siren emoji the screen has no glyph for: left out.
for e in E:
    if 'safetyFuelReserveLiters' in (e.get('Bind') or ''):
        assert '"🚨 BOX DIESE RUNDE! 🚨"' in e['Bind'], e['Bind'][-200:]
        e['Bind'] = e['Bind'].replace('"🚨 BOX DIESE RUNDE! 🚨"', '"BOX DIESE RUNDE!"')
for e in E:
    if e['Type'] != 'value' or screen_of(e) == 'Tyres': continue
    s = next((s for k, s in BY_BIND if k in (e.get('Bind') or '')), None) or SAMPLES.get(e.get('Name'))
    if not s: raise SystemExit(f'no samples for {e.get("Name")}: {e.get("Bind")}')
    e['Samples'] = s
    # SimHub's designer text where it's a placeholder ("Text", "Car Class", "Berechne...") or wider than the data: the first sample
    if e.get('PreviewText') in (None, '', 'Text', 'Car Class', 'Berechne...') or len(e['PreviewText']) > len(s[0]):
        e['PreviewText'] = s[0]

# ---------------------------------------------------------------------------------------------------------------------
# 8. Every text in the original's size and place: each one measured from its original item with the original's font
# (Roboto, Segoe UI, the dash's own fonts, at its weight), the screen font closest to it that fits picked, and its band
# centred on where the original's ink is. Items the original draws in the same font, size and weight get one font
# together (convert_kit.place_group). Area: the original's box on the wheel, grown sideways where the screen's fonts need
# more room. (The Tyres page's own texts were placed in 6.)
def texts_of(e):
    return e.get('Samples') or [e.get('Text') or ''] if e['Type'] == 'value' else [e.get('Text') or '']


def together(a, b):
    # shown at the same time: on the same page (or one on every page) and one's own conditions within the other's
    pa, pb = K.pages_of(a), K.pages_of(b)
    if pa and pb and pa != pb: return False
    oa, ob = set(own(a)), set(own(b))
    return oa <= ob or ob <= oa


def over_bar(e):
    # (a bar on the caption's layer or under it; a pop-up's bar over it comes with the pop-up's panel, which covers it)
    return any(b['Type'] == 'bar' and together(b, e) and set(own(b)) <= set(own(e)) and b['X'] < e['X'] + e['W'] and e['X'] < b['X'] + b['W'] and b['Y'] < e['Y'] + e['H'] and e['Y'] < b['Y'] + b['H'] for b in E)


# Every fixed text becomes a picture of its text in the original's font (9.), as SimHub draws it: the screen draws text
# only in its own fonts, and those look nothing like the original's (Roboto bold, the dash's TT Lakes and Segoe UI): under
# 32 px it has only its S fonts, spaced like a typewriter ("T i m e  t o  G o"), and its bigger ones are thin. A caption
# never changes, so a picture of it costs only the RAM drive (one file per look; ~110 of its 350 KB are used) and, without
# the RAM patch, rectangles in 2 colours. Not over a bar (a picture there is drawn again with rectangles at every change
# of the bar). (Values change, so they stay in the screen's fonts: the closest normally spaced one, 8.)
def as_picture(e):
    # ("Lift & Coast" is on the stint bar, which moves only while coasting, and shows only then: a picture all the same)
    if e['Type'] != 'label' or e.get('ColorBind') or not (e.get('Text') or '').strip(): return False
    if over_bar(e) and e.get('Name') != 'Lift&Coast text': return False
    return match(e) is not None


# Areas other than the original's box (on the wheel: x0, y0, x1, y1)
AREAS = {
    # the lap number: the original's box (45 px) holds two digits; from the end of "Lap" to the total laps it holds three
    # (an endurance race has them), right-aligned as the original
    'Current Lap': (round(wx(100)), round(wy(232)), round(wx(189)), round(wy(282))),
}
# The pit limiter's text is a formula giving one of two fixed texts ("Pit Limiter   ON" / "Pit Limiter OFF"): two
# labels shown by the limiter's state, so each becomes a picture in the original's bold Segoe UI (9.).
lim = next(e for e in E if e['Type'] == 'value' and (e.get('Bind') or '').startswith("js:if($prop('PitLimiterOn')==1)"))
POS_LIM = POS.get(id(lim), (lim['X'], lim['Y']))
i = E.index(lim)
on = {k: v for k, v in lim.items() if k in ('Name', 'X', 'Y', 'W', 'H', 'Color', 'Align', 'Font', 'PreviewVisible')}
E[i:i + 1] = [dict(on, Type='label', Text='Pit Limiter   ON', Visible=vis(lim) + ['ncalc:[PitLimiterOn] = 1']),
              dict(on, Type='label', Text='Pit Limiter OFF', Visible=vis(lim) + ['ncalc:[PitLimiterOn] != 1'])]   # (the formula: ON when 1, else OFF)
for x in E[i:i + 2]: SCREEN[id(x)] = SCREEN[id(lim)]; POS[id(x)] = POS_LIM
# The practice and qualifying summaries show the session's name ([SessionTypeName]); each shows only in its session
# (sessionMap: Practice 0, Qualify 1), so the name is fixed: a label, a picture in the original's font (9.).
for n, name in ((0, 'Practice'), (1, 'Qualify')):
    for v in [v for v in E if v['Type'] == 'value' and v.get('Bind') == 'ncalc:[SessionTypeName]' and session(v) == n]:
        for k in ('Bind', 'Format', 'Samples', 'PreviewText', 'Empty', 'Background'): v.pop(k, None)
        v.update(Type='label', Text=name)
# IGNITION OFF is coloured by the Lovely plugin's theme (ld_theme('ld_uiLabel'): the dark grey it has by default): drawn
# in that grey, as a picture in the original's font (a picture can't follow another Lovely theme).
for e in E:
    if e['Type'] == 'label' and e.get('ColorBind') == "js:return ld_theme('ld_uiLabel')":
        e.pop('ColorBind'); e.pop('ColorStops', None)
groups, unmatched = {}, []
for e in E:
    if e['Type'] not in ('label', 'value') or as_picture(e) or screen_of(e) == 'Tyres' and e['Type'] == 'value': continue
    if not texts_of(e)[0].strip(): continue
    item = match(e)
    if item is None: unmatched.append(e.get('Name')); continue
    area = AREAS.get(e.get('Name')) or (max(0, int(item.x)), max(0, int(item.y)), min(790, int(item.x + item.w)), min(460, int(item.y + item.h)))
    groups.setdefault((item.family, round(item.em, 1), item.weight), []).append((e, item, texts_of(e), area, {}))


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

# The lap number and the total laps after it: the number's box ends 2 px before the total's
cur, tot = el('Current Lap', 'value'), el('Estimated Laps', 'value')
if cur['X'] + cur['W'] > tot['X'] - 2: cur['W'] = tot['X'] - 2 - cur['X']
# "Lift & Coast" reaches 3 px into the coast time's box under it in the original (only their ink is apart): just above it
lab, val = el('Lift&Coast text', 'label'), el('Lift&Coast', 'value')
if lab['Y'] + lab['H'] > val['Y'] - 2: lab['Y'] = val['Y'] - 2 - lab['H']

# Texts with a line under them in the original (BorderStyle with only its bottom border, 2 px: the lap summary's
# captions and session name in grey, the pit time pop-up's rows in SimHub's default white; the importer takes the four
# sides' average and draws none): the line under each, at the original's scale, drawn before its text.
underlines = 0
for e in list(E):
    if e['Type'] not in ('label', 'value'): continue
    item = match(e)
    bs = (item.it.get('BorderStyle') or {}) if item else {}
    if bs.get('BorderBottom') and not any(bs.get(k) for k in ('BorderTop', 'BorderLeft', 'BorderRight')):
        h = max(1, round(bs['BorderBottom'] * item.scale))
        line = {'Type': 'rect', 'Name': f'{e["Name"]} underline', 'X': round(item.x), 'Y': round(item.y + item.h) - h, 'W': round(item.w),
                'H': h, 'Color': '#' + (bs.get('BorderColor') or '#FFFFFFFF')[3:], 'Visible': list(vis(e)), 'PreviewVisible': False}
        E.insert(E.index(e), line)
        SCREEN[id(line)] = SCREEN[id(e)]
        underlines += 1
# A value right under one of those lines (the last lap time, the sector times: their boxes start where the caption's box
# ends) begins 2 px under it: the screen's band has no inner margin like SimHub's text, and a band on a line redraws the
# line at every change.
for v in [e for e in E if e['Type'] == 'value' and session(e) is not None]:
    for u in [u for u in E if u['Type'] == 'rect' and u.get('Name', '').endswith(' underline') and session(u) == session(v)]:
        if u['X'] < v['X'] + v['W'] and v['X'] < u['X'] + u['W'] and v['Y'] - 2 <= u['Y'] <= v['Y'] + 2:
            v['Y'] = u['Y'] + u['H'] + 2

# The session name's line: under the original's box (x 425, 410 wide, top 5, 55 tall in the summary's 1260 px, placed at
# 22, 209 at 0.6; several items share its name, so it's placed from those numbers)
sx, sy = wx(22 + 425 * 0.6), wy(209 + 5 * 0.6)
sw, sh = 410 * 0.6 * SC, 55 * 0.6 * SC
for v in [v for v in els('TextItem2', 'label') if session(v) in (0, 1)]:
    for u in [u for u in E if u.get('Name') == 'TextItem2 underline' and session(u) == session(v)]:
        u.update(X=round(sx), W=round(sw), Y=round(sy + sh) - u['H'])
# The practice and qualifying summaries' "All Time Best" lap time is SimHub's PersonalBestLapTime item, which the
# importer skips (no data mapping): SimHub's AllTimeBest, in the place mirroring the session best's (the original's
# boxes: 36 and 888 in the summary's 1260 px, at its 0.6 scale).
for best in [v for v in els('BestLapTime', 'value') if session(v) in (0, 1)]:
    pb = dict(best, Name='PersonalBestLapTime', Bind='ncalc:[AllTimeBest]', Format='laptime', Samples=['8:88.888'], Empty='')
    pb.pop('ColorBind', None); pb.pop('ColorStops', None)
    pb['X'] = best['X'] + best['W'] / 2 - (888 - 36) * 0.6 * SC - best['W'] / 2
    pb['X'] = round(pb['X'])
    E.insert(E.index(best) + 1, pb)
    SCREEN[id(pb)] = SCREEN[id(best)]

# ---------------------------------------------------------------------------------------------------------------------
# 9. Fixed texts as pictures in the original's own font (see as_picture): drawn as the original draws them, where it has
# them (part of the static layer; without the RAM patch rectangles of their colour and black, MaxColors 2).
pictures = 0
for i, e in enumerate(E):
    if not as_picture(e): continue
    item = match(e)
    pic = K.text_picture(orig, item, e['Text'], e.get('Color', '#FFFFFF')[:7])
    if pic is None: continue
    im, x, y = pic
    key = f'text {e["Name"]} {i}@{im.width}x{im.height}'
    d['Images'][key] = picture(im)
    new = {k: v for k, v in e.items() if k in ('Name', 'Visible', 'PreviewVisible')}
    new.update(Type='image', Image=key, X=x, Y=y, W=im.width, H=im.height, MaxColors=2)
    E[i] = new
    SCREEN[id(new)] = SCREEN[id(e)]
    pictures += 1

# The lap summary's badges. The light grey one (class position, "LAP n") is 90 % opaque: mixed over the panel's black
# (#C7C7C7), and its texts drawn on that colour. In the practice and qualifying versions it's too small for any screen
# font to hold "LAP 888" (SimHub draws it at 10 px): practice gets one row (position, then LAP n) in a badge widened to
# hold it, qualifying the lap row under the position in a badge 4 px wider each side.
for e in els('BACKGROUND', 'box'):
    e.update(Fill='#C7C7C7', Color='#C7C7C7', Opacity=100)
    s_ = session(e)
    pos, lap = [x for x in els('Class position Value', 'value') if session(x) == s_][0], [x for x in els('LAP_VALUE', 'value') if session(x) == s_][0]
    if s_ == 0:
        e.update(X=326, W=140)
        pos.update(Font=107, X=332, Y=e['Y'] + (e['H'] - 24) // 2, W=28, H=24, Align='center')
        lap.update(Font=94, X=362, Y=e['Y'] + (e['H'] - 32) // 2, W=100, H=32, Align='left')
    else:
        # qualifying: 4 px wider each side, "LAP n" (16 px) at its bottom; race: "LAP n" (32 px) at its bottom. The position
        # above it, in the biggest of its own fonts that fits.
        if s_ == 1: e.update(X=e['X'] - 4, W=e['W'] + 8)
        lf = 10 if s_ == 1 else 94
        lap.update(Font=lf, X=e['X'] + 3, Y=e['Y'] + e['H'] - 2 - K.height(lf), W=e['W'] - 6, H=K.height(lf), Align='center')
        room = lap['Y'] - 2 - (e['Y'] + 2)
        f = max((f for f in (69, 99, 100, 97, 96, 98, 101) if K.height(f) <= room and K.width(f, '88') + 4 <= e['W'] - 6), key=K.height)
        pos.update(Font=f, X=e['X'] + 3, W=e['W'] - 6, Y=e['Y'] + 2 + (room - K.height(f)) // 2, H=K.height(f), Align='center')
    for v in (pos, lap):
        v.update(Background='#C7C7C7', Color='#000000')
# The red car-class badges (qualifying: the best car in class, the best of all classes) are 52 px, and a class name
# ("LMGT3", "Hyper") needs 82 px in the smallest normally spaced font: the badges grow to 90 px to the right, the gap
# beside each moving with it. Their captions stay: in the original they are wider than their boxes and run over the
# badges' tops; the class name sits under the caption's lettering, in the badge's lower part.
for b in els('CarClassText background', 'box'):
    v = [x for x in els('CarClassText', 'value') if abs(x['X'] + x['W'] / 2 - (b['X'] + b['W'] / 2)) < 40][0]
    grow = 90 - b['W']
    b['W'] = 90
    v.update(Font=94, X=b['X'] + 3, Y=b['Y'] + b['H'] - 2 - 32, W=84, H=32, Align='center', Background=b['Fill'])
    if True:
        for o in E:
            if o['Type'] == 'value' and o is not v and session(o) == 1 and b['X'] < o['X'] < b['X'] + b['W'] + 20                     and o['Y'] >= b['Y'] - 2 and o['Y'] + o['H'] <= b['Y'] + b['H'] + 2:
                o['X'] += grow

# The race badge: the position's box ends 2 px above "LAP n" (it ran into it); "Last Laptime" under the badge (a picture
# now, where the original's ink is: 7 px into the badge's bottom)
for b in els('BACKGROUND', 'box'):
    s_ = session(b)
    pos = [x for x in els('Class position Value', 'value') if session(x) == s_][0]
    lap = [x for x in els('LAP_VALUE', 'value') if session(x) == s_][0]
    if pos['Y'] < lap['Y'] < pos['Y'] + pos['H'] + 2:
        pos['H'] = lap['Y'] - 2 - pos['Y']
        # the biggest tight font that still fits it (the 963 family, the position's own)
        f = max((f for f in (69, 99, 100, 97, 96, 98, 101) if K.height(f) <= pos['H'] and K.width(f, '88') + 4 <= pos['W']), key=K.height)
        pos.update(Font=f, Y=pos['Y'] + (pos['H'] - K.height(f)) // 2, H=K.height(f))
    for c in E:
        if c['Type'] == 'image' and c['Image'].startswith('text ') and session(c) == s_ and 'Laptime' in c['Image']                 and c['Y'] < b['Y'] + b['H'] + 2 and c['Y'] + c['H'] > b['Y'] and c['X'] < b['X'] + b['W'] and b['X'] < c['X'] + c['W']:
            c['Y'] = b['Y'] + b['H'] + 2

# The race summary's penalty box: two rows of small captions (the original's 10 px lettering, pictures here) and their
# values ("Penalty : 1", "Stop and Go : Yes (3 Tours)", which runs past the box in the original). The values in fonts that
# fit their rows (32 px ones overlapped each other), the box widened between the two last-lap times beside it (their
# boxes trimmed to their text's room) to hold the longer one.
for b in els('RectangleItem2', 'box'):
    b.update(X=220, W=372)
    for lt in [x for x in els('LeaderboardOpponentLastLap', 'value') if session(x) == 2]:
        if lt['X'] < b['X']: lt['W'] = b['X'] - 4 - lt['X']
        else: lt['W'] -= b['X'] + b['W'] + 4 - lt['X']; lt['X'] = b['X'] + b['W'] + 4
    rows = [c for c in E if c['Type'] == 'image' and c['Image'].startswith('text ') and session(c) == 2 and has(c, 'mNumPenalties')]
    for c in rows:
        c['X'] = b['X'] + 8
        v = next(x for x in E if x['Type'] == 'value' and session(x) == 2 and has(x, 'mNumPenalties') and abs((x['Y'] + x['H'] / 2) - (c['Y'] + c['H'] / 2)) < 14)
        f = 107 if v.get('Name') == 'Penalty Value' else 10   # digits (24 px); text (16 px)
        cy = c['Y'] + c['H'] / 2
        v.update(Font=f, X=c['X'] + c['W'] + 4, Y=round(cy - K.height(f) / 2), H=K.height(f), Align='left')
        v['W'] = b['X'] + b['W'] - 4 - v['X']

# The blue flag screen's distance: shown under 50 m (its frame's condition), across its frame's inside
dist, fr = el('distqnce behind', 'value'), el('distqnce behind frame', 'box')
dist.update(X=fr['X'] + 4, W=fr['W'] - 8, PreviewText='48m')

# "Dist [m]" and "RTG [s]" clear of the CAS radar's bars (4.)
for e in els('distance') + els('RTG'):
    if e.get('Name') == 'distance': e['X'] = max(e['X'], 84)
    else: e['X'] = min(e['X'], 697 - e['W'])

# The race summary's penalty box is an outline (SimHub draws the panel through it): filled with the panel's colour, or
# what's under it comes through and every change under it flashes it.
pbox = [e for e in E if e.get('Name') == 'RectangleItem2' and e['Type'] == 'box' and not e.get('Fill')]
for e in pbox: e['Fill'] = panel['Fill']

# A value's box reaching into another text shown with it (the original's boxes overlap, only their ink doesn't) or into
# a caption's picture: every change would redraw the other one. Such boxes shrink to their widest text.
captions = [e for e in E if e['Type'] == 'image' and e['Image'].startswith('text ')]
shrunk, still = K.shrink_to_text(E, also=captions)
# (a caption of an overlay meeting a value: its overlay's box covers the value while it shows)
still = [(v, c) for v, c in still if not any(own(x) for x in captions if x.get('Name') == c)]

# A value whose text runs into a caption picture shown with it (the screen's fonts are wider than the original's: the
# pit time's "00:00:000" into its "TOTAL", "Ratio Avg:" 0.84, the qualifying delta into "LAST LAPTIME"): its box ends
# 3 px from the caption on that side, and when its widest text no longer fits there, the biggest normally spaced font
# that does.
def ink(v, f=None):
    f = f or v['Font']
    w = max(K.width(f, t) for t in v.get('Samples') or [v.get('PreviewText') or '8'])
    x = v['X'] + (v['W'] - w) / 2 if v.get('Align') == 'center' else v['X'] + v['W'] - w if v.get('Align') == 'right' else v['X']
    y = v['Y'] + (v['H'] - K.height(f)) / 2
    return x, y, x + w, y + K.height(f)


def meets(a, c): return min(a[2], c['X'] + c['W']) - max(a[0], c['X']) > 0 and min(a[3], c['Y'] + c['H']) - max(a[1], c['Y']) > 0


refit = []


def clear_captions():
    for v in [e for e in E if e['Type'] == 'value']:
        for c in captions:
            if not together(v, c) or not meets(ink(v), c): continue
            # (not when one is under an opaque panel of the other's pop-up: the lap summary's captions over the pit panel)
            lo, hi = sorted((E.index(v), E.index(c)))
            top, low = E[hi], E[lo]
            lr = ink(low) if low is v else (low['X'], low['Y'], low['X'] + low['W'], low['Y'] + low['H'])
            if any(s_['Type'] in ('rect', 'box') and (s_['Type'] == 'rect' or s_.get('Fill')) and s_.get('Opacity', 100) >= 100
                   and own(s_) <= own(top) and not own(s_) <= own(low) and s_['X'] <= lr[0] and s_['Y'] <= lr[1]
                   and s_['X'] + s_['W'] >= lr[2] and s_['Y'] + s_['H'] >= lr[3] for s_ in E[lo + 1:hi]): continue
            texts = v.get('Samples') or [v.get('PreviewText')]
            x0, y0, x1, y1 = ink(v)
            cy = (y0 + y1) / 2
            if c['Y'] >= cy or c['Y'] + c['H'] <= cy:
                # above or below it: the band gets shorter on that side (the biggest normally spaced font that fits, centred
                # where the text was)
                top, bottom = (y0, c['Y'] - 2) if c['Y'] >= cy else (c['Y'] + c['H'] + 2, y1)
                ok = [f for f in K.fonts(texts[0]) if K.tight(f) and K.height(f) <= bottom - top and all(0 <= K.width(f, t) <= v['W'] - 4 for t in texts)]
                if ok:
                    f = max(ok, key=K.height)
                    mid = min(max(cy, top + K.height(f) / 2), bottom - K.height(f) / 2)
                    v.update(Font=f, Y=round(mid - K.height(f) / 2), H=K.height(f))
            else:
                if c['X'] + c['W'] / 2 < v['X'] + v['W'] / 2:
                    right = v['X'] + v['W']; v['X'] = c['X'] + c['W'] + 3; v['W'] = right - v['X']
                else:
                    v['W'] = c['X'] - 3 - v['X']
                if max(K.width(v['Font'], t) for t in texts) + 4 > v['W']:
                    ok = [f for f in K.fonts(texts[0]) if K.tight(f) and K.height(f) <= v['H'] and all(0 <= K.width(f, t) <= v['W'] - 4 for t in texts)]
                    if ok:
                        f = max(ok, key=K.height)
                        v.update(Y=v['Y'] + (v['H'] - K.height(f)) // 2, H=K.height(f), Font=f)
            refit.append(v.get('Name'))


clear_captions()

# ---------------------------------------------------------------------------------------------------------------------
# 10. Values on their own overlay's box are drawn on its colour; values over a panel's edge by a pixel moved inside it;
# values half under an overlay's box hide while it shows (convert_kit.take_turns).
K.snap_into_panels(E)
K.backgrounds_from_overlays(E)
# (keep_inside_boxes takes any box with a condition, also another pop-up's: the session name went 124 px left into the
# pit time pop-up's frame. Each value against the boxes shown with it only.)
for v in [e for e in E if e['Type'] == 'value']:
    K.keep_inside_boxes([b for b in E[:E.index(v)] if b['Type'] in ('box', 'rect') and together(b, v)] + [v])
# (take_turns leaves out overlays with a page: the Fuel page's pop-ups are on page 0. Run without their page, and keep
# what it adds only on the Fuel page and what's on every page: they never show on the other two.)
pop = [e for e in E if own(e) and vis(e) and vis(e)[0] == 'page:0' and any(has(e, t) for t in PAGE0_POPUPS)]
for e in pop: e['Visible'] = vis(e)[1:]
before = {id(e): list(vis(e)) for e in E}
turns = K.take_turns(E, d['Images'])
# the lap summary's values have the panel under them (backgrounds_from_overlays stops at a frame of the original's on it)
for v in E:
    if v['Type'] == 'value' and has(v, 'changed(5000, [LastLapTime])') and not v.get('Background') and session(v) is not None:
        v['Background'] = panel['Fill']
for e in E:
    if any(c in ('page:1', 'page:2') for c in vis(e)) and vis(e) != before[id(e)]:
        e['Visible'] = before[id(e)]; turns -= 1
for e in pop: e['Visible'] = ['page:0'] + vis(e)
clear_captions()   # (again: keep_inside_boxes and snap_into_panels move values back by a pixel or two)

# ---------------------------------------------------------------------------------------------------------------------
# 11. Fewer files on the RAM drive. Every picture is a file of its own there (~600 B of JPEG tables + 512 B counted on top
# of its data), the drive's 350 KB are shared with the other dashes and the screensaver, and what doesn't fit is evicted
# and loaded again (13 s after the screensaver on the wheel, 2026-10-06, with parts missing meanwhile). Pictures that
# show together and sit near each other (a row of captions, a ruler with its numbers) become one picture, when nothing
# that changes (a value, a bar, a picture or shape of something else that comes and goes) is inside the area they'd
# cover.
def rect_of(e): return (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H'])


def meet(a, b, pad=0): return a[0] - pad < b[2] and b[0] - pad < a[2] and a[1] - pad < b[3] and b[1] - pad < a[3]


def union(rs): return (min(r[0] for r in rs), min(r[1] for r in rs), max(r[2] for r in rs), max(r[3] for r in rs))


def static_picture(e):
    return e['Type'] == 'image' and not any('blink(' in c for c in vis(e)) and e.get('Opacity', 100) >= 100


def blocked(members, box):
    key = set(vis(members[0]))
    first = min(E.index(m) for m in members)
    for x in E:
        # (a value by where its text goes: its box is often wider)
        r = ink(x) if x['Type'] == 'value' else rect_of(x)
        if x in members or not meet(r, box) or not any(together(x, m) for m in members): continue
        if x['Type'] in ('value', 'label', 'bar', 'deltabar'): return True
        # what's under them and shows whenever they do is baked into the picture; something under them that comes and
        # goes on its own, or a picture of something else over them, isn't
        if E.index(x) < first and set(vis(x)) <= key: continue
        if E.index(x) > first and x['Type'] in ('rect', 'box', 'ellipse') and not (set(vis(x)) <= key): continue
        return True
    return False


merged_pictures = 0
groups_ = {}
for e in E:
    if static_picture(e): groups_.setdefault(tuple(sorted(vis(e))), []).append(e)
for key, members in groups_.items():
    clusters = [[m] for m in members]
    changed = True
    while changed:
        changed = False
        for i in range(len(clusters)):
            for j in range(i + 1, len(clusters)):
                a, b = clusters[i], clusters[j]
                ra, rb = union([rect_of(m) for m in a]), union([rect_of(m) for m in b])
                if not meet(ra, rb, 80): continue
                box = union([ra, rb])
                if (box[2] - box[0]) * (box[3] - box[1]) > 140000 or blocked(a + b, box): continue
                clusters[i] = a + b; del clusters[j]; changed = True
                break
            if changed: break
    for c in [c for c in clusters if len(c) > 1]:
        box = union([rect_of(m) for m in c])
        canvas = Image.new('RGBA', (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
        for m in sorted(c, key=E.index):
            im = Image.open(io.BytesIO(base64.b64decode(d['Images'][m['Image']]))).convert('RGBA')
            if im.size != (m['W'], m['H']): im = im.resize((m['W'], m['H']), Image.LANCZOS)
            canvas.alpha_composite(im, (m['X'] - box[0], m['Y'] - box[1]))
        first = min(c, key=E.index)
        name = 'pictures ' + ', '.join(sorted({m.get('Name') or '' for m in c}))[:60]
        k = f'{name} {E.index(first)}@{canvas.width}x{canvas.height}'
        d['Images'][k] = picture(canvas)
        new = {kk: v for kk, v in first.items() if kk in ('Visible', 'PreviewVisible')}
        new.update(Type='image', Name=name, Image=k, X=box[0], Y=box[1], W=canvas.width, H=canvas.height,
                   MaxColors=max(m.get('MaxColors', 8) for m in c))
        E[E.index(first)] = new
        SCREEN[id(new)] = SCREEN.get(id(first))
        for m in c:
            if m is not first: E.remove(m)
        merged_pictures += len(c) - 1
for k in [k for k in d['Images'] if not any(e.get('Image') == k for e in E)]:
    del d['Images'][k]   # pictures no element uses any more

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); a second
# run must change nothing (the /create-dash gate)
bands = K.fx('fit-bands', OUT)['changes']
# fit-bands sees the page under a pop-up's value (black), not the pop-up's panel: the lap summary's values it gave a
# background get the panel's colour back
d2 = json.load(open(OUT, encoding='utf-8'))
for v in d2['Elements']:
    if v['Type'] == 'value' and v.get('Background') == '#000000' and has(v, 'changed(5000, [LastLapTime])'):
        v['Background'] = panel['Fill']
json.dump(d2, open(OUT, 'w', encoding='utf-8'), indent=1)
again = K.fx('fit-bands', OUT)['changes']
assert not again, f'fit-bands still changes things: {again}'
print(json.dumps({'written': OUT, 'elements': len(E), 'left_out': left_out, 'markers': markers, 'strips': strips, 'flattened': flattened,
                  'unmatched': unmatched, 'unplaced': unplaced, 'underlines': underlines, 'widened': widened, 'text_pictures': pictures,
                  'take_turns': turns, 'cleared_captions': refit, 'merged_pictures': merged_pictures, 'shrunk': sorted(set(shrunk)), 'still_touching': still, 'fit_bands': bands}, indent=1))
