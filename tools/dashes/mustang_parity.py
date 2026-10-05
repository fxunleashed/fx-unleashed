"""
Parity of the built-in Mustang (Usb/BuiltIn/lmgt3-mustang.json) with Redadeg's SimHub dash it is converted from.

    python tools/dashes/mustang_parity.py [SimHub folder]

Walks the SimHub dash the way SimHub draws it (MAIN, its layers and widgets, the widget's two screens as the pages,
the two overlay screens) and finds each item in the converted dash: same data (value formula), same conditions (every
layer's and the item's own, as SimHub combines them), same colour formula, same format, same text. Prints every
difference; the ones made on purpose (listed in KNOWN, with why) are shown apart. Exit 1 if anything else differs.
"""
import json, os, re, sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SIMHUB = sys.argv[1] if len(sys.argv) > 1 else r'C:\Program Files (x86)\SimHub'
FOLDER = os.path.join(SIMHUB, 'DashTemplates', 'LMGT3 Ford Mustang GT3')
dash = json.load(open(os.path.join(REPO, 'Usb', 'BuiltIn', 'lmgt3-mustang.json'), encoding='utf-8'))
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


# ---------- the original, as SimHub draws it ----------
items = []   # (kind, name, data, conditions, colour, format, text, where)


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
            screens = w['Screens']
            flipped = (it.get('NextScreenCommand') or 0) or (it.get('PreviousScreenCommand') or 0)
            if len(screens) > 1 and flipped and it.get('NextScreenCommand') == 1:
                for k, s in enumerate(screens):
                    walk(s['Items'], c, where + '/' + it['Name'] + f'[page {k}]', k)
            else:
                walk(screens[it.get('InitialScreenIndex') or 0]['Items'], c, where + '/' + it['Name'], page)
            continue
        data = formula(binds, 'Text') or formula(binds, 'Value')
        colour = formula(binds, 'TextColor') or formula(binds, 'BackgroundColor') or formula(binds, 'GaugeColor') or formula(binds, 'FillColor')
        fmt = ((binds.get('Text') or {}).get('FormatString') if isinstance(binds.get('Text'), dict) else None)
        text = it.get('Text') if data is None else None
        items.append(dict(kind=t, name=it.get('Name'), data=data, conds=[x for x in c], colour=colour, fmt=fmt, text=text,
                          where=where, page=page, img=it.get('Image'), opacity=it.get('Opacity')))


main = load('LMGT3 Ford Mustang GT3.djson')
screens = {s['Name']: s for s in main['Screens']}
walk(screens['MAIN']['Items'], [], 'MAIN')
for name in ('IGN OFF', 'IGN ON'):
    s = screens[name]
    walk(s['Items'], ['ncalc:' + s['OverlayTriggerExpression']['Expression'].strip()], name)

# ---------- matching ----------
def evis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def own(e):
    return [c for c in evis(e) if not c.startswith('page:') and not c.startswith('ncalc:!(')]


def epage(e):
    for c in evis(e):
        if c.startswith('page:'): return int(c[5:])
    return None


DELTA_LMU = '[GameRawData.CurrentPlayerTelemetry.mDeltaBest]'
DELTA_ANY = 'isnull([GameRawData.CurrentPlayerTelemetry.mDeltaBest], [PersistantTrackerPlugin.SessionBestLiveDeltaSeconds])'


def same_conds(orig, conv):
    o = [norm(c).replace(DELTA_LMU, DELTA_ANY) for c in orig if c != '(blink)']
    v = [norm(c) for c in conv if 'blink(' not in c]
    return sorted(o) == sorted(v)


def same_data(o, v):
    o, v = norm(o), norm(v)
    return o == v or o.replace(DELTA_LMU, DELTA_ANY) == v


used = set()
problems, known = [], []
# SimHub items with no formula of their own: the speed in the user's unit, the gear; leaderboard items as formulas
# picking the same driver (SimHub's OpponentAtPosition; the importer's Leaderboard)
BUILTIN = {'SpeedText': 'ncalc:[SpeedLocal]', 'GearText': 'gearText'}

for it in items:
    if it['kind'] == 'TextItem' and it['data'] is None and not (it['text'] or '').strip() and not it['colour']:
        continue  # an empty text (a frame drawn as a text item: its frame is a box)
    cands = [i for i, e in enumerate(E) if i not in used and e.get('Name') in (it['name'], (it['name'] or '') + ' frame', (it['name'] or '') + ' background')]
    if it['kind'] in BUILTIN: it['data'] = BUILTIN[it['kind']]
    if it['kind'].startswith('Leaderboard'):
        cands = [i for i in cands if (E[i].get('Bind') or '').startswith('ncalc:') and 'driver' in E[i]['Bind']]
        if cands: it['data'] = E[cands[0]]['Bind']
    best = None
    for i in cands:
        e = E[i]
        if it['data'] and not (e.get('Bind') and same_data(it['data'], e['Bind'])): continue
        if not same_conds(it['conds'], own(e)): continue
        if it['page'] is not None and epage(e) != it['page']: continue
        best = i; break
    tag = f"{it['where']}/{it['name']} ({it['kind']})"
    if best is None:
        # made on purpose: see KNOWN below
        if it['where'].endswith('[page 1]/Delta') and it['kind'] in ('RectangleItem', 'LinearGaugeItem'):
            known.append(f'{tag}: drawn by the one deltabar element (same segments, colours, value)'); continue
        if it['name'] == 'HEADLIGHT':
            known.append(f'{tag}: the 50% black layer over the dash is a dim of the backlight (same darkening, no redraws)'); continue
        if it['name'] == 'Rahmen_sw':
            known.append(f"{tag}: the bar's black inner frame shows only with its bar (black on black otherwise: same look)"); continue
        problems.append(f'{tag}: not found with the same data and conditions ({it["data"]} | {it["conds"]})')
        continue
    used.add(best)
    e = E[best]
    if not it['colour'] and e.get('ColorBind') and not it['kind'].startswith('Leaderboard'):
        problems.append(f'{tag}: a colour formula the SimHub item doesn\'t have ({e["ColorBind"]})')
    if it['colour'] and norm(it['colour']).replace(DELTA_LMU, DELTA_ANY) != norm(e.get('ColorBind')):
        problems.append(f'{tag}: colour formula differs ({it["colour"]} -> {e.get("ColorBind")})')
    if it['data'] and it['fmt'] and e.get('Format') not in (it['fmt'], 'time:' + it['fmt']):
        problems.append(f'{tag}: format {it["fmt"]} -> {e.get("Format")}')
    if it['text'] is not None and e['Type'] == 'label' and norm(e.get('Text')) != norm(it['text']):
        if {'Throttle': 'Throt', 'TC LON': 'TCLon', 'TC LAT': 'TCLat'}.get(it['text']) == e.get('Text'):
            known.append(f'{tag}: caption "{it["text"]}" -> "{e["Text"]}" (fits no screen font in its 83 px frame)')
        else:
            problems.append(f'{tag}: text "{it["text"]}" -> "{e.get("Text")}"')
    if it['opacity'] is not None and it['opacity'] < 100 and e.get('Opacity', 100) == 100 and e['Type'] not in ('label', 'value'):
        problems.append(f'{tag}: opacity {it["opacity"]}% lost')

# the other half of a SimHub text with a border or a background (a frame or a box, and the text), and the overlay
# screens' own backgrounds: parts of items already matched
names = {it['name'] for it in items}
def part(e):
    n = e.get('Name') or ''
    return any(n == b + ' frame' or n == b + ' background' for b in names) or n in ('IGN OFF background', 'IGN ON background')         or (e['Type'] in ('label', 'value') and n in names)
parts = [i for i, e in enumerate(E) if i not in used and part(e)]
extra = [f"#{i} {e['Type']} {e.get('Name')}" for i, e in enumerate(E) if i not in used and i not in parts and e['Type'] not in ('deltabar', 'dim')]
print(f"{len(parts)} more elements are the second half of a matched item (a text's frame or background) or an overlay screen's background")
print(f'{len(items)} items in the SimHub dash, {len(used)} matched one to one')
print(f'\nmade on purpose ({len(known)}):')
for k in known: print('  ' + k)
print(f'\ndifferences ({len(problems)}):')
for p in problems: print('  ' + p)
print(f'\nin the conversion only ({len(extra)}):')
for x in extra: print('  ' + x)
sys.exit(1 if problems else 0)
