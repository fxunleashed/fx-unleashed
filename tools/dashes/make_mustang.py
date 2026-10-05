"""
The built-in "LMGT3 Ford Mustang GT3" dash: Redadeg's SimHub dash (lmu-dashboards.com) converted 1:1 for the FX Pro.

    python tools/dashes/make_mustang.py [OUT.json]        (default: Usb/BuiltIn/lmgt3-mustang.json)

Needs SimHub installed with the SimHub dash "LMGT3 Ford Mustang GT3" (DashTemplates) and fxdash built
(dotnet build -c Release tools/fxdash/fxdash.csproj). Re-running it re-imports and re-tunes from the SimHub dash, so
every change to the conversion lives here, with its reason (the /create-dash way, docs/examples/lmgt3-mclaren-tune.py).

What the import brings over by itself: the MAIN screen (fuel and energy columns, gear oval, bias row, settings boxes,
the side delta bars), every overlay in it (setting changes, lap summaries per session, pit limiter and pit stop
states, low energy, engine off, flags, pit request / cancel, lift), the "above the bar" widget's two screens as the
dash's two pages (brake temps / tyre pressures / tyre temps, and the delta bar with lap time and position: SimHub
flips them with its screen commands, the plugin with Next / Previous page), the two overlay screens (ignition off,
the ignition-on sweep), blinking items as blink() conditions, and the headlights' see-through black layer as a dim of
the backlight. What this script changes, and why, is below.
"""
import json, os, subprocess, sys, tempfile

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FX = os.path.join(REPO, 'tools', 'fxdash', 'bin', 'Release', 'net48', 'fxdash.exe')
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(REPO, 'Usb', 'BuiltIn', 'lmgt3-mustang.json')
SIMHUB_DASH = 'LMGT3 Ford Mustang GT3'


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


FONTS = {}


def width(font, text):
    if text not in FONTS:
        FONTS[text] = {x['id']: x for x in fx('fonts', '--sample', text)}
    return FONTS[text][font]['sampleWidth']


def height(font):
    return width(font, '0') and FONTS['0'][font]['height']


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


def els(name, typ=None, **kw):
    """Elements by name (and type); pg= page, cond= a text in one of its conditions, always=True: no condition."""
    hits = [e for e in E if e.get('Name') == name and (typ is None or e['Type'] == typ)]
    if 'pg' in kw: hits = [e for e in hits if page(e) == kw['pg']]
    if 'cond' in kw: hits = [e for e in hits if any(kw['cond'] in c for c in vis(e))]
    if kw.get('always'): hits = [e for e in hits if not vis(e)]
    assert hits, (name, typ, kw)
    return hits


def el(name, typ=None, **kw):
    h = els(name, typ, **kw)
    assert len(h) == 1, (name, typ, kw, len(h))
    return h[0]


def text_fits(e, font, texts, margin=2):
    return height(font) <= e['H'] and all(0 <= width(font, t) <= e['W'] - 2 * margin for t in texts)


def font_for(e, texts, prefer):
    """The first font in `prefer` the texts fit in (box height and width)."""
    for f in prefer:
        if text_fits(e, f, texts):
            return f
    raise SystemExit(f'{e.get("Name")}: none of {prefer} fits {texts} in {e["W"]}x{e["H"]}')


NARROW = [100, 97, 96, 98, 71, 101, 94]   # the 963 / 992 families: digits as narrow as the screen has, tallest first
LABEL = [60, 14, 12]                     # small labels (24, 20, 16 px)

# ---------------------------------------------------------------------------------------------------------------------
# 1. Identity
d['Id'] = 'lmgt3-mustang'
d['Name'] = 'LMGT3 Ford Mustang GT3'
d['Author'] = 'Redadeg (lmu-dashboards.com)'
d['Description'] = ('Converted for the FX Pro by FX Unleashed from Redadeg\'s SimHub dash "LMGT3 Ford Mustang GT3" '
                    '(https://lmu-dashboards.com). Two pages above the settings row: tyres and brakes, and the delta bar '
                    '(bind Next / Previous page to a wheel button).')
d['Source'] = 'Redadeg\'s SimHub dash "LMGT3 Ford Mustang GT3", https://lmu-dashboards.com'
d['ScriptsFolder'] = None   # its JavascriptExtensions aren't used by any formula; a built-in / library dash never has one
d['Pages'] = ['Tyres', 'Delta']
d['FormatVersion'] = 3   # pages   # SimHub's widget screens are both called "Screen": named by what they show

# ---------------------------------------------------------------------------------------------------------------------
# 2. Data that also works outside LMU. The dash reads LMU's own delta (mDeltaBest); elsewhere that property is missing
# and the delta bar, side bars and delta value would stay empty. SimHub's live delta stands in when it is.
LMU_DELTA = '[GameRawData.CurrentPlayerTelemetry.mDeltaBest]'
DELTA = 'isnull(' + LMU_DELTA + ', [PersistantTrackerPlugin.SessionBestLiveDeltaSeconds])'
for e in E:
    for k in ('Bind', 'ColorBind'):
        if e.get(k) and LMU_DELTA in e[k]:
            e[k] = e[k].replace(LMU_DELTA, DELTA)
    if e.get('Visible') and any(LMU_DELTA in c for c in vis(e)):
        e['Visible'] = [c.replace(LMU_DELTA, DELTA) for c in vis(e)]

# ---------------------------------------------------------------------------------------------------------------------
# 3. Page "Delta": SimHub draws the delta bar as 14 grey segments, two gauges filling from the centre and 12 black
# separators over them. On the wheel the separators would be redrawn over the gauges at every change (and flash); a
# deltabar element draws exactly that look, segment by segment. Segment pitch / width / height from the original
# (40 / 30 / 120 at 1200 px); the off segments are its grey at 20% over black.
green = els('Green bar', 'box', pg=1)
brown = els('Brown bar', 'box', pg=1)
segs = sorted(green + brown, key=lambda e: e['X'])
assert len(segs) == 14
gauge = el('Gauge Red', 'bar', pg=1)
pitch_w = segs[1]['X'] - segs[0]['X']
deltabar = {
    'Type': 'deltabar', 'Name': 'delta bar', 'Bind': 'ncalc:' + DELTA, 'X': segs[0]['X'], 'Y': gauge['Y'], 'H': gauge['H'],
    'W': segs[-1]['X'] + segs[-1]['W'] - segs[0]['X'],
    'Segments': 7, 'SegmentX': [e['X'] for e in segs], 'SegmentWidth': min(e['W'] for e in segs),
    'Range': 1, 'PositiveColor': '#FF0000', 'NegativeColor': '#00FF00', 'SegmentColor': '#1A1A1A',
    'Visible': ['page:1'],
}
gone = set(map(id, segs + els('Gauge Green', 'bar', pg=1) + [gauge] + els('Dark bar', 'box', pg=1)))
at = E.index(segs[0]) if segs[0] in E else 0
E[:] = [e for e in E if id(e) not in gone]
E.insert(min(at, len(E)), deltabar)

# lap time: as big as the original (80 px at 1200 = 51) in the narrow digits
lt = el('Laptime3', 'value', pg=1)
lt['Samples'] = ['8.88.888']; lt['PreviewText'] = '1.23.456'
lt['Font'] = font_for(lt, lt['Samples'], NARROW)
# position: the label ran into its value; label right-aligned against the value's box
pv = el('Pos.#', 'value', pg=1); pl = el('Pos.', 'label', pg=1)
pv['Samples'] = ['88']; pv['PreviewText'] = '12'; pv['Font'] = 14; pv['Empty'] = '-'
pl['Font'] = 14; pl['Align'] = 'right'; pl['W'] = width(14, 'Pos.') + 2; pl['X'] = pv['X'] - 4 - pl['W']

# ---------------------------------------------------------------------------------------------------------------------
# 4. Page "Tyres": twelve values in three 2x2 grids with a cross of thin lines each. Value boxes kept inside their
# cell (a box running over a grid line would redraw the line with the text: a flash), the digits in the narrow 32 px
# font the original's size scales to.
for name, sample, prev in (('T FL', '88', '78'), ('T FR', '88', '79'), ('T RL', '88', '74'), ('T RR', '88', '75'),
                           ('PRES VL', '88.8', '26.4'), ('PRES VR', '88.8', '26.4'), ('PRES HL', '88.8', '26.1'), ('PRES HR', '88.8', '26.0'),
                           ('LF_BrakeTemp', '888', '412'), ('RF_BrakeTemp', '888', '408'), ('RL_BrakeTemp', '888', '322'), ('RR_BrakeTemp', '888', '318')):
    v = el(name, 'value', pg=0)
    v['Samples'] = [sample, '888'] if name.startswith('PRES') else [sample]   # kPa shows 3 digits
    v['PreviewText'] = prev
    v['Empty'] = '-'
    v['Font'] = 101
for grid in (('T FL', 'T FR', 'T RL', 'T RR'), ('PRES VL', 'PRES VR', 'PRES HL', 'PRES HR'),
             ('LF_BrakeTemp', 'RF_BrakeTemp', 'RL_BrakeTemp', 'RR_BrakeTemp')):
    fl, fr, rl, rr = (el(n, 'value', pg=0) for n in grid)
    # the grid's lines: the vertical one between the columns, the horizontal one between the rows
    lines = [e for e in E if e['Type'] == 'rect' and page(e) == 0 and fl['X'] - 8 <= e['X'] <= rr['X'] + rr['W'] + 8 and fl['Y'] - 4 <= e['Y'] <= rr['Y'] + rr['H']]
    vline = [e for e in lines if e['H'] > e['W']][0]
    hline = [e for e in lines if e['W'] > e['H']][0]
    for v in (fl, rl):    # left column: up to 3 px before the vertical line
        v['W'] = vline['X'] - 3 - v['X']
    for v in (fr, rr):    # right column: from 3 px after it
        right = v['X'] + v['W']; v['X'] = vline['X'] + vline['W'] + 3; v['W'] = right - v['X']
    fh = height(101)
    for v in (fl, fr):    # top row: ends 3 px above the horizontal line
        v['H'] = fh + 2; v['Y'] = hline['Y'] - 3 - v['H']
    for v in (rl, rr):    # bottom row: starts 3 px below it
        v['H'] = fh + 2; v['Y'] = hline['Y'] + hline['H'] + 3
for n in ('t_Tyre', 'p_Tyre', 't_Brake'):   # the original's own captions (Sui Generis), as they read
    lab = el(n, 'label', pg=0)
    lab['Font'] = font_for(lab, [lab['Text']], [14, 12])

# ---------------------------------------------------------------------------------------------------------------------
# 5. The settings row: eight frames 63 px tall with a caption and a value. The value boxes reached through the frames'
# bottom border (a flash at every change): caption and value now share the inside of the frame. Three captions don't
# fit 83 px in any screen font (there is no narrow font under 32 px), shortened as on the first conversion.
SHORT = {'Throttle': 'Throt', 'TC LON': 'TCLon', 'TC LAT': 'TCLat'}
for frame in [e for e in E if e['Type'] == 'box' and e.get('Name', '').endswith(' frame') and not vis(e)]:
    inside_y, inside_h = frame['Y'] + frame['Border'], frame['H'] - 2 * frame['Border']
    texts = [e for e in E if e['Type'] in ('label', 'value') and not vis(e) and e['X'] == frame['X'] and frame['Y'] <= e['Y'] < frame['Y'] + frame['H']]
    cap = min(texts, key=lambda e: e['Y']); val = max(texts, key=lambda e: e['Y'])
    cap['Text'] = SHORT.get(cap['Text'], cap['Text'])
    for e in (cap, val):
        e['X'] = frame['X'] + frame['Border'] + 1; e['W'] = frame['W'] - 2 * (frame['Border'] + 1)
    cap['Font'] = 10; cap['Align'] = 'center'
    cap['Y'] = inside_y + 1; cap['H'] = height(10) + 2
    val['Y'] = cap['Y'] + cap['H']; val['H'] = inside_y + inside_h - 1 - val['Y']
    val['Font'] = font_for(val, ['88'], [98, 71, 101])
    if val['Type'] == 'value':
        val['Samples'] = ['88']; val['Empty'] = '-'
# the same captions in the ignition-on sweep
for e in E:
    if e['Type'] == 'label' and e.get('Text') in SHORT:
        e['Text'] = SHORT[e['Text']]

# ---------------------------------------------------------------------------------------------------------------------
# 6. The columns left and right of the oval: captions in the 24 px label font, values in the narrow 32 px digits, the
# value boxes kept off their caption and off the blue lines between the blocks.
for name, samples, prev in (('Fuel Last Lap', ['8.88'], '2.09'), ('Fuel +/-', ['888'], '32'), ('Fuel This Lap', ['8.88'], '0.78'),
                            ('Fuel Used#', ['888.8'], '14.2'), ('Last Laptime', ['8.88.888'], '1.23.456'), ('#Delta', ['-8.888', '+8.888'], '-0.123'),
                            ('PREDICTED#', ['8:88.888'], '1:23.456'), ('VE#', ['888'], '62')):
    v = el(name, 'value', always=False) if name in ('#Delta', 'PREDICTED#') else el(name, 'value', always=True)
    v['Samples'] = samples; v['PreviewText'] = prev; v['Empty'] = '-'
    v['Font'] = 101
for name in ('Fuel Last Lap', 'Fuel +/-', 'Fuel Lap', 'Fuel RemLaps', 'Last Lap', 'Delta', 'Predicted', 'VE Remain'):
    lab = el(name, 'label', always=True)
    lab['Font'] = 60
# each value between its caption (above) and the blue line under its block (or the oval's row)
blue = [e for e in E if e['Type'] == 'rect' and not vis(e) and e['H'] <= 2 and e['W'] < 300]
for name in ('Fuel Last Lap', 'Fuel +/-', 'Fuel This Lap', 'Fuel Used#', 'Last Laptime', '#Delta', 'PREDICTED#', 'VE#'):
    v = el(name, 'value')
    caps = [l for l in E if l['Type'] == 'label' and not vis(l) and l['Y'] < v['Y'] + v['H'] // 2 and l['X'] < v['X'] + v['W'] and v['X'] < l['X'] + l['W']]
    cap = max(caps, key=lambda l: l['Y'])
    cap_bottom = cap['Y'] + (cap['H'] + height(cap['Font'])) // 2   # where its text ends
    lines = [b for b in blue if b['Y'] > cap_bottom and b['X'] < v['X'] + v['W'] and v['X'] < b['X'] + b['W']]
    bottom = (min(b['Y'] for b in lines) if lines else v['Y'] + v['H'] + 2) - 1
    v['Y'] = cap_bottom; v['H'] = bottom - v['Y']
    assert v['H'] >= height(v['Font']), (name, v['H'])

# ---------------------------------------------------------------------------------------------------------------------
# 7. The oval: gear as big as the oval allows (the gear font: digits, N, R), speed above it clear of the oval's rim,
# session type in the small font it fits (PRACTICE is the widest).
GEAR_FONT = 117   # 119 px: the copies in the flag and pit ovals (inner black rim) don't take 102's 128 px, and the gear
                  # shouldn't change size when a flag shows
for g in els('GearText', 'value'):
    g['Format'] = 'text'; g['Samples'] = ['8', 'N', 'R']; g['Empty'] = 'N'; g['PreviewText'] = 'N'
    g['Font'] = GEAR_FONT if text_fits(g, GEAR_FONT, ['8', 'N', 'R']) else 117
oval = el('FORD_Elipse', 'ellipse', always=True)
sp = el('SpeedText', 'value')
# (32 px tall from the top: all under the LIFT / pit limiter bars, 0-32, so they cover it whole while they show)
# The original's speed is Sui Generis 60 (a wide face, ~28 px digits here); the narrow 963 digits at 32 px came out
# ~19 px and looked small on the wheel (user, 2026-10-04). The screen's wide 32 px font (4) is the closest that fits: a
# taller one (36 px) would cross the oval's rim at 35 and redraw it with every change.
sp['Y'] = 0; sp['H'] = 32; sp['Font'] = font_for(sp, ['388'], [4, 101, 94]); sp['Samples'] = ['388']; sp['PreviewText'] = '0'
gear = el('GearText', 'value', always=True)
for s in els('Session', 'value'):
    # from just inside the oval's rim to just before the gear (PRACTICE needs 124 px)
    s['Font'] = 12; s['Samples'] = ['PRACTICE', 'QUALIFY', 'RACE']; s['PreviewText'] = 'PRACTICE'
    s['X'] = oval['X'] + oval['Border'] + 3; s['W'] = gear['X'] - 1 - s['X']
    # the race start screen (a race only) has an inner black ring as well: its session text, always RACE, inside that
    rings = [r for r in E if r['Type'] == 'ellipse' and r.get('Name') == 'BLK_Rahmen_Elipse2' and vis(r) == vis(s)]
    if rings:
        s['Samples'] = ['RACE']; s['PreviewText'] = 'RACE'
        s['X'] = rings[0]['X'] + rings[0]['Border'] + 3; s['W'] = gear['X'] - 1 - s['X']
# The wiper icon: the original shows it when mWiperState > 0, but LMU reports 1 with the wipers off (seen live,
# 2026-10-04: 1 for a whole dry stint), so it never went away. LMU's header doesn't say what the values mean; off = 0
# or 1, so the icon shows above 1. (The ignition-on sweep's copy has its own timing, left as it is.)
for w in els('Wiperimg', 'image', cond='mWiperState'):
    w['Visible'] = [c.replace('mWiperState]>0', 'mWiperState]>1') for c in vis(w)]
    w['Visible'] = w['Visible'][0] if len(w['Visible']) == 1 else w['Visible']
# the headlight-switch flash ("FLASH" on white, blinking): 2 px wider so the word fits the small font
for e in els('FLASH background', 'rect') + els('FLASH', 'label'):
    e['X'] -= 2; e['W'] += 4
b = el('BIAS#', 'value', always=True)
b['Samples'] = ['88.88']; b['PreviewText'] = '54.20'; b['Font'] = 101

# The oval group 5 px lower (user, 2026-10-04: it sat tight under the top edge): every oval at its spot and what's in it
# (gear, session, flag and pit texts, the Ford script), the indicator arrows at its top corners, the speed above it,
# and the pit screens' boxes under the pit ring with what's on them (else the ring's bottom rim would run under them;
# they still end above the settings row at 385).
OVAL_DOWN = 5
def in_oval(e): return e['X'] >= 205 and e['X'] + e['W'] <= 590 and e['Y'] >= 30 and e['Y'] + e['H'] <= 185
def pit_screen(e): return any('[PitLimiterOn]' in c for c in vis(e)) and e['X'] >= 205 and e['X'] + e['W'] <= 590 and e['Y'] >= 185 and e['Y'] + e['H'] <= 385
pit = [e for e in E if pit_screen(e)]
moved = [e for e in E if in_oval(e) or e.get('Name') == 'Links'] + pit + [sp]
for e in moved:
    bottom = e['Y'] + e['H']
    e['Y'] += OVAL_DOWN
    if e in pit and bottom > 380:
        e['H'] = bottom - e['Y']   # (a pit box down to the settings row: its top moves, its bottom stays clear of 385)

# ---------------------------------------------------------------------------------------------------------------------
# 8. Overlays. The ignition screens' black backgrounds cover the wheel's visible area (790 x 460), not the screen's
# corners the padding pushes off. Values drawn on their overlay's black box say so (the box only shows with them, so
# the checks can't see it under them).
for n in ('IGN OFF background', 'IGN ON background'):
    e = el(n, 'rect'); e['X'] = e['Y'] = 0; e['W'] = 790; e['H'] = 460
# Values on their overlay's own box (shown with it: their conditions take in all of the box's) are drawn on its colour.
# The checks only see the static layer under them (the box comes and goes), and would pick that colour instead.
def holds(b, v):
    # the value's box all on the shape: inside its box (rect, box), or its four corners inside its oval (ellipse)
    if not (b['X'] <= v['X'] and b['Y'] <= v['Y'] and b['X'] + b['W'] >= v['X'] + v['W'] and b['Y'] + b['H'] >= v['Y'] + v['H']): return False
    if b['Type'] != 'ellipse': return True
    cx, cy, rx, ry = b['X'] + b['W'] / 2, b['Y'] + b['H'] / 2, b['W'] / 2 - b.get('Border', 0), b['H'] / 2 - b.get('Border', 0)
    return all(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1 for x in (v['X'], v['X'] + v['W']) for y in (v['Y'], v['Y'] + v['H']))
def touches(b, v): return b['X'] < v['X'] + v['W'] and v['X'] < b['X'] + b['W'] and b['Y'] < v['Y'] + v['H'] and v['Y'] < b['Y'] + b['H']
for v in [e for e in E if e['Type'] == 'value' and vis(e)]:
    # the last shape drawn under it that shows with it: when that one holds all of its box, the value sits on its colour
    # (else, an oval only partly under a tall gear, it's left to the renderer)
    under = [b for b in E[:E.index(v)] if b['Type'] in ('rect', 'box', 'ellipse', 'image', 'gradient') and vis(b) and set(vis(b)) <= set(vis(v)) and touches(b, v)]
    top = under[-1] if under else None
    if top and top['Type'] in ('rect', 'box', 'ellipse') and top.get('Opacity', 100) >= 100 and (top['Type'] == 'rect' or top.get('Fill')) and holds(top, v):
        v['Background'] = top['Color'] if top['Type'] == 'rect' else top['Fill']
# the race start's big speed: inside its own black box, clear of the blue line above it
for v in els('Speed', 'value'):
    bg = [b for b in els('Speed background', 'rect') if b.get('Visible') == v.get('Visible')]
    if not bg: continue
    bg = bg[0]
    v['X'], v['Y'], v['W'], v['H'] = bg['X'], bg['Y'] + 2, bg['W'], bg['H'] - 4
    v['Font'] = font_for(v, ['88.8'], [35, 90, 92, 99, 100])
# the black frames inside the LIFT / pit limiter bars show only with their bar (black on black otherwise, so no
# change in look): drawn while the bar is off, they would cross the speed under them at every change
for fr in els('Rahmen_sw', 'box'):
    bar = [b for b in E if b['Type'] == 'rect' and b['Name'] in ('LIFT background', 'PitSpeedBalken') and b['Y'] == fr['Y'] - 2 and all(c in vis(b) for c in vis(fr))]
    if bar: fr['Visible'] = list(vis(bar[0]))

# ---------------------------------------------------------------------------------------------------------------------
# 9. Values and bars half under an overlay's box (a setting-change box over the lap time, the pit screen over the
# energy value): while the overlay shows, every change of the value would draw it and then the box over it again (a
# flash, and the box's whole traffic each time). They hide while that overlay shows and come back with it gone, the
# way /create-dash's tune does it. An overlay over all of a value's text needs nothing (the renderer skips covered
# values), and elements on another page never show together.
def rect(e): return (e['X'], e['Y'], e['X'] + e['W'], e['Y'] + e['H'])
def inter(a, b): return max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(0, min(a[3], b[3]) - max(a[1], b[1])) > 0 and min(a[2], b[2]) - max(a[0], b[0]) > 2 and min(a[3], b[3]) - max(a[1], b[1]) > 2
def contains(a, b): return a[0] <= b[0] and a[1] <= b[1] and a[2] >= b[2] and a[3] >= b[3]
def band(v):
    if v['Type'] != 'value': return rect(v)
    fh = height(v['Font']); w = max(width(v['Font'], t) for t in v.get('Samples') or ['8']) + 2 * (4 + fh // 4)
    w = min(w, v['W'])
    x = v['X'] + (v['W'] - w) // 2 if v.get('Align') == 'center' else v['X'] + v['W'] - w if v.get('Align') == 'right' else v['X']
    y = v['Y'] + (v['H'] - fh) // 2
    return (x, y, x + w, y + fh)
def own(e): return [c for c in vis(e) if not c.startswith('page:') and not c.startswith('ncalc:!(')]
def opaque(e): return e.get('Opacity', 100) >= 100 and (e['Type'] == 'rect' or (e['Type'] in ('box', 'ellipse') and e.get('Fill')))
turns = 0
def negation(conds):
    """A condition true whenever the overlay is off: !(all of its ncalc conditions); for a script (the LIFT bar's lift and
    coast trigger) the same script returning the opposite (scripts can't be wrapped: the checked-script rules allow no
    functions), its blink left out (the value stays hidden for the whole phase, not just the bar's on half)."""
    if all(c.startswith('ncalc:') for c in conds):
        cond = conds[0][6:] if len(conds) == 1 else ' and '.join('(' + c[6:] + ')' for c in conds)
        return 'ncalc:!(' + cond + ')'
    scripts = [c for c in conds if c.startswith('js:')]
    if len(scripts) == 1 and all(c.startswith('js:') or "blink(" in c for c in conds):
        body = scripts[0].rstrip()
        last = body.rfind('return ')
        if last > 0 and body.endswith(';') and body.count('return ') == 1:
            return body[:last] + 'return !(' + body[last + 7:-1].strip() + ');'
    return None
for ti, top in enumerate(E):
    conds = own(top)
    if not conds or not opaque(top) or page(top) is not None: continue
    no = negation(conds)
    if no is None: continue
    for under in E[:ti]:
        # values shown on their own terms: always, on a page, or under conditions that aren't this overlay's (an
        # element whose conditions hold all of the overlay's is part of it, or of an overlay inside it)
        if under['Type'] not in ('value', 'bar', 'deltabar') or set(conds) <= set(own(under)): continue
        if page(under) is not None and page(top) is not None and page(under) != page(top): continue
        b = band(under)
        if not inter(rect(top), b) or contains(rect(top), b) or no in vis(under): continue
        under['Visible'] = vis(under) + [no]
        turns += 1

# ---------------------------------------------------------------------------------------------------------------------
# 9b. The setting-change pop-ups: their big value's box reached into the range labels under it ("1" ... "11"), so each
# change redrew a corner of a label (a flash). The value ends 2 px above them, in the tallest digit font that fits.
for v in [e for e in E if e['Type'] == 'value' and e.get('Name') in ('TC3 #', 'TC2 #', 'TC #', 'ABS#', 'Map #') and vis(e)]:
    marks = [l for l in E if l['Type'] == 'label' and vis(l) and set(vis(l)) <= set(vis(v)) and l['Y'] > v['Y'] and l['Y'] < v['Y'] + v['H']]
    if not marks: continue
    v['H'] = min(l['Y'] for l in marks) - 2 - v['Y']
    v['Samples'] = ['88']
    v['Font'] = font_for(v, ['88'], [38, 35, 108, 90, 92, 99, 100])

# ---------------------------------------------------------------------------------------------------------------------
# 10. The Ford script on the engine-off and ignition-on ovals: white on the oval's navy. Drawn with rectangles (a wheel
# without the screen's picture memory) two colours are all it has; 6 tripled its cost (59 KB, over 2 s). With the
# picture memory it's a full-colour picture either way.
for e in els('ImageItem2', 'image'):
    e['MaxColors'] = 2

json.dump(d, open(OUT, 'w', encoding='utf-8'), indent=1)
# text rows clear of every line (fit-bands moves a value a pixel or picks the next font down where one isn't); a second
# run must change nothing (the /create-dash gate)
bands = fx('fit-bands', OUT)['changes']
assert not fx('fit-bands', OUT)['changes'], 'fit-bands still changes things'
print(json.dumps({'written': OUT, 'elements': len(E), 'take_turns': turns, 'fit_bands': bands, 'import_notes': report['Notes']}, indent=1))
