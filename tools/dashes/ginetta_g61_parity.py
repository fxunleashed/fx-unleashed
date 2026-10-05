"""
Parity of the converted "LMP3 Ginetta G61" dash (make_ginetta_g61.py) with Redadeg's SimHub dash it is converted from.

    python tools/dashes/ginetta_g61_parity.py [DASH.json] [SimHub folder]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmp3-ginetta-g61.json)

Walks the SimHub dash the way SimHub draws it (Main, its layers, the tyre widget's three screens as the pages, the two
ignition screens) and finds each item in the converted dash: same data (value formula), same conditions (every layer's
and the item's own, as SimHub combines them), same format, same colour (fixed, or the colour formula with its colour
stops), same alignment, same text. Prints every difference; the ones made on purpose (KNOWN, with why) are shown apart.
Exit 1 if anything else differs. (Sizes and places aren't compared: the screen has its own fonts; make_ginetta_g61.py
picks the closest and centres them where the original's text is.)
"""
import json, os, re, sys

SIMHUB = sys.argv[2] if len(sys.argv) > 2 else os.environ.get('SIMHUB_INSTALL_PATH', r'C:\Program Files (x86)\SimHub')
DASH = sys.argv[1] if len(sys.argv) > 1 else os.path.join(SIMHUB, 'PluginsData', 'Common', 'FXProRpmSync', 'Dashes', 'lmp3-ginetta-g61.json')
FOLDER = os.path.join(SIMHUB, 'DashTemplates', 'LMP3 Ginetta G61')
dash = json.load(open(DASH, encoding='utf-8-sig'))
E = dash['Elements']


def load(name):
    return json.load(open(os.path.join(FOLDER, name), encoding='utf-8-sig'))


def tname(it):
    return (it.get('$type') or '').split(',')[0].split('.')[-1]


def formula(binds, prop):
    b = (binds or {}).get(prop)
    if not isinstance(b, dict) or b.get('Mode') not in (1, 2, 4): return None
    f = b.get('Formula') or {}
    expr = (f.get('Expression') or '').strip()
    if not expr: return None
    return ('js:' if f.get('Interpreter') == 1 else 'ncalc:') + expr


def norm(c):
    return re.sub(r'\s+', ' ', c or '').strip()


def rgb(argb):
    """'#AARRGGBB' / '#RRGGBB' -> '#RRGGBB' (upper case)."""
    return None if not argb else ('#' + argb[-6:]).upper()


# ---------- the original, as SimHub draws it ----------
items = []
ALIGN = {0: 'left', 1: 'center', 2: 'right'}


def walk(lst, conds, where, page=None):
    for it in lst or []:
        t = tname(it)
        binds = it.get('Bindings') or {}
        vis = formula(binds, 'Visible')
        if it.get('Visible') is False and vis is None: continue
        c = conds + ([vis] if vis else [])
        if it.get('BlinkEnabled'): c = c + ['(blink)']
        if t in ('Layer', 'GroupItem'):
            walk(it.get('Childrens') or it.get('Items'), c, where + '/' + str(it.get('Name')), page)
            continue
        if t == 'WidgetItem':
            w = load(it['FileName'])
            for k, s in enumerate(w['Screens']):   # flipped by the driver: the dash's pages
                walk(s['Items'], c, where + '/' + it['Name'] + f'[page {k}]', k)
            continue
        tb = binds.get('Text') if isinstance(binds.get('Text'), dict) else {}
        cb = binds.get('TextColor') if isinstance(binds.get('TextColor'), dict) else {}
        stops = None
        if cb.get('Mode') == 4:   # SimHub's colour interpolation: start, (middle,) end
            stops = [(cb['StartColorValue'], rgb(cb['StartColor']))] + \
                    ([(cb['MiddleColorValue'], rgb(cb['MiddleColor']))] if cb.get('EnableMiddleColor') else []) + \
                    [(cb['EndColorValue'], rgb(cb['EndColor']))]
        data = formula(binds, 'Text')
        if t == 'GearText': data = 'gearText'
        items.append(dict(kind=t, name=it.get('Name'), data=data, conds=c, colour=formula(binds, 'TextColor'), stops=stops,
                          fmt=tb.get('FormatString'), text=it.get('Text') if data is None else None, where=where, page=page,
                          fixed=rgb(it.get('TextColor')) if t in ('TextItem', 'GearText') else rgb(it.get('BackgroundColor')),
                          align=ALIGN.get(it.get('HorizontalAlignment')) if t in ('TextItem', 'GearText') else None,
                          opacity=it.get('Opacity'), border=(it.get('BorderStyle') or {}), img=it.get('Image')))


main = load('LMP3 Ginetta G61.djson')
screens = {s['Name']: s for s in main['Screens']}
walk(screens['Main']['Items'], [], 'Main')
for name in ('IGN OFF', 'IGN ON'):
    s = screens[name]
    walk(s['Items'], ['ncalc:' + s['OverlayTriggerExpression']['Expression'].strip()], name)


# ---------- matching ----------
def evis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def epage(e):
    for c in evis(e):
        if c.startswith('page:'): return int(c[5:])
    return None


def same_conds(orig, conv):
    o = [norm(c) for c in orig if c != '(blink)']
    v = [norm(c) for c in conv if 'blink(' not in c and not c.startswith('page:')]
    return sorted(o) == sorted(v) and (('(blink)' in orig) == any('blink(' in c for c in conv))


# make_ginetta_g61.py 2.: the tyre pressures format every unit in the formula (bar: format(x, '0.00')), shown as text
def pressure_formula(o):
    m = re.search(r"\[(TyrePressure\w+)\]\)\)$", norm(o))
    return norm(o)[:-len(f'[{m.group(1)}]))')] + f"format([{m.group(1)}], '0.00')))" if m else None


used = set()
problems, known = [], []
for it in items:
    tag = f"{it['where']}/{it['name']} ({it['kind']})"
    if it['kind'] == 'RectangleItem' and '[page' in it['where']:
        frame = [i for i, e in enumerate(E) if e['Type'] == 'box' and e.get('Name') == 'RectangleItem' and not evis(e) and (e['X'], e['Y']) == (12, 160)]
        if len(frame) != 1:
            problems.append(f"{tag}: the tyre panel's one frame (a box at 12,160, always shown) not found"); continue
        used.add(frame[0])
        known.append(f"{tag}: the tyre panel's frame, the same on all three pages: one frame, always shown (make_ginetta_g61.py 3.)")
        continue
    if it['kind'] == 'RectangleItem' and it['where'].startswith('IGN'):
        known.append(f"{tag}: the screen's rectangles over its black are mixed into one opaque background colour (make_ginetta_g61.py 6.)")
        continue
    names = (it['name'], (it['name'] or '') + ' frame', (it['name'] or '') + ' background')
    cands = [i for i, e in enumerate(E) if i not in used and e.get('Name') in names]
    best = None
    for i in cands:
        e = E[i]
        if it['data']:
            bind = e.get('Bind') or ''
            if norm(bind) != norm(it['data']) and norm(bind) != pressure_formula(it['data'] or ''): continue
        elif it['text'] is not None and e['Type'] == 'label' and norm(e.get('Text')) != norm(it['text']):
            if not (it['text'] == 'PRESS IGNITION TO START' and e.get('Text') == 'PRESS IGNITION'): continue
        if not same_conds(it['conds'], evis(e)): continue
        if it['page'] is not None and epage(e) != it['page']: continue
        if it['page'] is None and epage(e) is not None: continue
        best = i; break
    if best is None:
        problems.append(f'{tag}: not found with the same data and conditions ({it["data"]} | {it["conds"]})')
        continue
    used.add(best)
    e = E[best]
    if it['data'] and pressure_formula(it['data']) == norm(e.get('Bind')):
        known.append(f"{tag}: bar formatted in the formula (format(x, '0.00')), the value shown as text: SimHub formats only "
                     f"numbers, the wheel numeric text too (\"26.9\" would read \"26.90\") (make_ginetta_g61.py 2.)")
    elif it['data'] and it['fmt'] and e.get('Format') not in (it['fmt'], 'time:' + it['fmt']):
        problems.append(f'{tag}: format {it["fmt"]} -> {e.get("Format")}')
    # a time: SimHub shows a zero time formatted ("0.00.000"), the wheel shows Empty for a time of 0
    if it['data'] and it['fmt'] and e.get('Format') == 'time:' + it['fmt']:
        zero = it['fmt'].replace('\\', '').replace('m', '0').replace('ss', '00').replace('fff', '000')
        if e.get('Empty') != zero:
            problems.append(f'{tag}: a zero time shows "{zero}" in SimHub, "{e.get("Empty")}" here (Empty)')
    if it['data'] and not it['fmt'] and e.get('Format') not in ('text', 'gear', None) and it['kind'] != 'GearText':
        problems.append(f'{tag}: no format in SimHub, "{e.get("Format")}" here')
    # colours
    if it['colour']:
        if norm(it['colour']) != norm(e.get('ColorBind')):
            problems.append(f'{tag}: colour formula differs ({it["colour"]} -> {e.get("ColorBind")})')
        conv = [(s['Value'], rgb(s['Color'])) for s in e.get('ColorStops') or []]
        if it['stops'] and conv != it['stops']:
            problems.append(f'{tag}: colour stops {it["stops"]} -> {conv}')
    elif e.get('ColorBind'):
        problems.append(f"{tag}: a colour formula the SimHub item doesn't have ({e['ColorBind']})")
    if e['Type'] in ('label', 'value') and it['fixed'] and rgb(e.get('Color')) != it['fixed'] and not it['colour']:
        problems.append(f'{tag}: colour {it["fixed"]} -> {e.get("Color")}')
    if e['Type'] in ('label', 'value') and it['align'] and e.get('Align') != it['align']:
        if it['text'] == 'PRESS IGNITION TO START':
            known.append(f'{tag}: two labels ("PRESS IGNITION", "TO START"), left-aligned so the line reads as one centred text: one '
                         'screen command holds 58 characters and the wheel would show "PRESS IGNITION" (make_ginetta_g61.py 6.)')
        else:
            problems.append(f'{tag}: aligned {it["align"]} -> {e.get("Align")}')
    if e['Type'] == 'image' and it['opacity'] is not None and it['opacity'] < 100:
        if e.get('Opacity', 100) == 100 and e.get('Image', '').startswith('Ginetta_Logo_'):
            known.append(f"{tag}: its {it['opacity']:.0f}% opacity is mixed into the picture over the screen's background (make_ginetta_g61.py 6.)")
        elif e.get('Opacity', 100) != it['opacity']:
            problems.append(f'{tag}: opacity {it["opacity"]}% -> {e.get("Opacity", 100)}%')
    if it['kind'] == 'RectangleItem' and it['fixed'] and it['fixed'] != '#FFFFFF' and it['fixed'] not in (rgb(e.get('Fill')), rgb(e.get('Color'))):
        problems.append(f'{tag}: colour {it["fixed"]} -> {e.get("Fill") or e.get("Color")}')
    if it['kind'] == 'RectangleItem' and e['Type'] == 'rect' and evis(e) == ['ncalc:[TCActive]']:
        known.append(f"{tag}: the red box inside its cell's frame (SimHub draws the frame over it) (make_ginetta_g61.py 4.)")

# conversion-only elements that are parts of matched items
names = {it['name'] for it in items}
def part(e):
    n = e.get('Name') or ''
    if n == 'TextItem 2' and e.get('Text') == 'TO START':
        return True   # the second half of PRESS IGNITION TO START
    if n in ('pit limiter border', 'pit limiter'):
        return True   # the PIT LIMITER text's border and blue background (make_ginetta_g61.py 5.)
    return any(n == b + ' frame' or n == b + ' background' for b in names) or n in ('IGN OFF background', 'IGN ON background')
parts = [i for i, e in enumerate(E) if i not in used and part(e)]
extra = [f"#{i} {e['Type']} {e.get('Name')}" for i, e in enumerate(E) if i not in used and i not in parts]
print(f'{len(items)} items in the SimHub dash, {len(used)} matched one to one; {len(parts)} more elements are parts of '
      f'matched items (a text\'s frame or background, an overlay screen\'s background, the second half of a split text)')
print(f'\nmade on purpose ({len(known)}):')
for k in known: print('  ' + k)
print(f'\ndifferences ({len(problems)}):')
for p in problems: print('  ' + p)
print(f'\nin the conversion only ({len(extra)}):')
for x in extra: print('  ' + x)
sys.exit(1 if problems or extra else 0)
