"""
Parity of the converted "LMGT3 Aston Martin Vantage AMR" dash (make_lmgt3_aston.py) with Redadeg's SimHub dash it is
converted from.

    python tools/dashes/lmgt3_aston_parity.py [DASH.json]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmgt3-aston-martin.json)

Walks the SimHub dash the way SimHub draws it (the main screen and its layers, the three widgets the driver flips on their
own commands as the three sets of pages, the ignition-off and ignition-on screens) and finds every item in the converted
dash: texts, gears, the speed, the leaderboard items, ovals, rectangles and pictures. For each: the same data (value
formula), the same conditions (every layer's and the item's own, as SimHub combines them; blinking), the same page of the
same set, the same format, the same colour formula and colour stops, the same alignment, the same text. Prints every
difference; the ones made on purpose (KNOWN, with why) are shown apart. Exit 1 if anything else differs, or the
conversion has an element that is no item's (or part of one).

Not compared: sizes and places (the screen has its own fonts: make_lmgt3_aston.py picks the closest and puts them where
the original's ink is; simhub_ref.py draws the original for comparing by eye), and the take-turns conditions the
conversion adds (an element under an overlay's box hides while the overlay shows: counted, listed with -v).
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simhub_ref  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith('-')]
VERBOSE = '-v' in sys.argv
DASH = args[0] if args else r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-aston-martin.json'
dash = json.load(open(DASH, encoding='utf-8-sig'))
E = dash['Elements']
orig = simhub_ref.Dash(simhub_ref.find('LMGT3 Aston Martin'))
SETS = {'Energy Fuel': 'page', 'Laptimes': 'page2', 'Tyre Widget': 'page3'}   # the widget -> its set of pages
PAGE = re.compile(r'^\s*page([2-4])?\s*:\s*(\d+)\s*$')


def norm(s): return re.sub(r'\s+', ' ', s or '').strip()


def rgb(argb): return None if not argb else ('#' + argb[-6:]).upper()


def unprefix(f):
    return None if f is None else norm(f[6:] if f.startswith('ncalc:') else f[3:] if f.startswith('js:') else f)


# ---------- the original, as SimHub draws it ----------
items = []
ALIGN = {0: 'left', 1: 'center', 2: 'right'}


def walk(lst, conds, where, opacity, page=None):
    for it in lst or []:
        t = simhub_ref.tname(it)
        v = simhub_ref.formula(it, 'Visible')
        if it.get('Visible') is False and v is None: continue
        c = conds + ([norm(v)] if v else [])
        blink = bool(it.get('BlinkEnabled'))
        op = opacity * (it.get('Opacity', 100) if it.get('Opacity') is not None else 100) / 100
        if t in ('Layer', 'GroupItem'):
            walk(it.get('Childrens') or it.get('Items'), c + (['(blink)'] if blink else []), where + '/' + str(it.get('Name')), op, page)
            continue
        if t == 'WidgetItem':
            w = json.load(open(os.path.join(orig.folder, it['FileName']), encoding='utf-8-sig'))
            for k, s in enumerate(w['Screens']):
                walk(s['Items'], c, where + '/' + it['Name'] + f'[page {k}]', op, (SETS[it['Name']], k))
            continue
        b = it.get('Bindings') or {}
        tb = b.get('Text') if isinstance(b.get('Text'), dict) else {}
        cb = b.get('TextColor') if isinstance(b.get('TextColor'), dict) else {}
        stops = None
        if cb.get('Mode') == 4:
            stops = [(cb['StartColorValue'], rgb(cb['StartColor']))] + \
                    ([(cb['MiddleColorValue'], rgb(cb['MiddleColor']))] if cb.get('EnableMiddleColor') else []) + \
                    [(cb['EndColorValue'], rgb(cb['EndColor']))]
        data = simhub_ref.formula(it, 'Text')
        if t == 'GearText': data = 'gearText'
        items.append(dict(kind=t, name=it.get('Name'), data=norm(data) if data else None, conds=c + (['(blink)'] if blink else []),
                          colour=norm(simhub_ref.formula(it, 'TextColor')) or None, stops=stops,
                          fill_formula=norm(simhub_ref.formula(it, 'BackgroundColor')) or None,
                          fmt=tb.get('FormatString'), text=it.get('Text') if data is None else None, where=where, page=page,
                          fixed=rgb(it.get('TextColor')) if t in ('TextItem', 'GearText', 'SpeedText') else rgb(it.get('BackgroundColor')),
                          align=ALIGN.get(it.get('HorizontalAlignment')) if t in ('TextItem', 'GearText', 'SpeedText') else None,
                          opacity=op, it=it))


for s in orig.json['Screens']:
    trig = ((s.get('OverlayTriggerExpression') or {}).get('Expression') or '').strip() if s.get('IsOverlayLayer') else ''
    walk(s.get('Items'), [norm(trig)] if trig else [], s['Name'], 1.0)


# ---------- the conversion ----------
def evis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def epages(e):
    """{set: page}: 'page' (set 1), 'page2'.. ."""
    out = {}
    for c in evis(e):
        m = PAGE.match(c)
        if m: out['page' + (m.group(1) or '')] = int(m.group(2))
    return out


def take_turn(c):
    return c.startswith('ncalc:!(') or (c.startswith('js:') and 'return !(' in c)


def own(e):
    """The conditions an element has from its item: not pages, not blinking, not take-turns."""
    return sorted(unprefix(c) for c in evis(e) if not PAGE.match(c) and 'blink(' not in c and not take_turn(c) and c != ALWAYS)


ALWAYS = 'ncalc:true'   # shown all the time, drawn by the screen itself (make_lmgt3_aston.py: the centre disc)


def blinks(e): return any('blink(' in c and not take_turn(c) for c in evis(e))


TYPES = {'TextItem': ('label', 'value', 'image'), 'GearText': ('value',), 'SpeedText': ('value',),
         'EllipseItem': ('ellipse',), 'RectangleItem': ('rect', 'box', 'dim'), 'ImageItem': ('image', 'rect')}


def types_of(kind): return TYPES.get(kind, ('value',) if kind.startswith('Leaderboard') else ())


def item_conds(it):
    """An item's conditions as the conversion writes them: the original's own, plus the ones the conversion keeps apart
    from them (blinking: a blink() condition)."""
    return sorted(c for c in it['conds'] if c != '(blink)')


def is_text_picture(e, it):
    return e['Type'] == 'image' and (e.get('Image') or '').startswith(f'text {it["name"]} ')


def pressure_formula(o):
    """make_lmgt3_aston.py 2.: the tyre pressures' formula with bar formatted in it too (format(x, '0.00'))."""
    m = re.search(r"\[(TyrePressure\w+)\]\)\)$", o)
    return o[:-len(f'[{m.group(1)}]))')] + f"format([{m.group(1)}], '0.00')))" if m and o.startswith("if ([TyrePressureUnit] = 'Psi'") else None


used = set()
problems, known, turns = [], [], []
dots = {}   # the lap times' page dots: page -> the one picture of its four
for it in items:
    tag = f"{it['where']}/{it['name']} ({it['kind']})"
    want = item_conds(it)
    if it['kind'] == 'EllipseItem' and it['name'] == 'GELB' and not want and it['where'] == 'Screen 1/Grafiken':
        if any(e.get('Name') == 'Kreis Mitte2 disc' for e in E):
            known.append(f"{tag}: left out: an oval in the disc's own colour inside the disc, nothing of it seen (the disc is "
                         "drawn by the screen, make_lmgt3_aston.py)")
        else: problems.append(f'{tag}: not found (nor the disc it is part of)')
        continue
    if it['kind'] == 'TextItem' and (it['text'] or '').strip() == '•' and it['page'] and it['page'][0] == 'page2':
        k = it['page'][1]
        pic = [i for i, e in enumerate(E) if e['Type'] == 'image' and (e.get('Image') or '').startswith(f'text page dots {k}@') and epages(e) == {'page2': k} and own(e) == want]
        if len(pic) != 1: problems.append(f'{tag}: the page dots picture of page {k} not found'); continue
        if k not in dots:
            dots[k] = pic[0]; used.add(pic[0])
            known.append(f"{tag} (and its page's other three dots): one picture of the four dots in the original's font: the "
                         "screen's fonts have no • (make_lmgt3_aston.py 5.)")
        continue
    cands = []
    for i, e in enumerate(E):
        if i in used or e['Type'] not in types_of(it['kind']): continue
        if e.get('Name') != it['name']: continue
        if it['kind'] == 'TextItem' and e['Type'] == 'image' and not is_text_picture(e, it): continue
        if it['data']:
            b = e.get('Bind') or ''
            if it['kind'] == 'GearText':
                if b != 'gearText': continue
            elif unprefix(b) != it['data'] and unprefix(b) != pressure_formula(it['data']): continue
        elif it['kind'] == 'TextItem' and e['Type'] == 'label':
            text = norm(e.get('Text'))
            if text != norm(it['text']) and not norm(it['text']).startswith(text + ' '): continue
        if own(e) != want: continue
        if (it['page'] is None) != (not epages(e)): continue
        if it['page'] is not None and epages(e) != {it['page'][0]: it['page'][1]}: continue
        cands.append(i)
    if not cands:
        problems.append(f'{tag}: not found with the same data, conditions and page ({it["data"] or it["text"]} | {want} | {it["page"]})')
        continue
    i = cands[0]
    used.add(i)
    e = E[i]
    if ('(blink)' in it['conds']) != blinks(e):
        problems.append(f'{tag}: blinks {"(blink)" in it["conds"]} -> {blinks(e)}')
    t = [c for c in evis(e) if take_turn(c)]
    if t: turns.append(f'{tag}: hides while {len(t)} overlay(s) over it show')
    if is_text_picture(e, it):
        known.append(f"{tag}: a picture of its text in the original's font (Bahnschrift), not screen text: the screen's fonts "
                     "under 32 px are spaced like a typewriter (make_lmgt3_aston.py 5.)")
        continue
    # the second half of a text split in two (make_lmgt3_aston.py 6.)
    if e['Type'] == 'label' and norm(e.get('Text')) != norm(it['text']):
        rest = norm(it['text'])[len(norm(e.get('Text'))) + 1:]
        two = [k for k, x in enumerate(E) if k not in used and x.get('Name') == (it['name'] or '') + ' 2' and norm(x.get('Text')) == rest and own(x) == want]
        if not two:
            problems.append(f'{tag}: text "{it["text"]}" -> "{e.get("Text")}"')
        else:
            used.add(two[0])
            known.append(f'{tag}: two labels ("{e.get("Text")}", "{rest}") that read as one line: one screen command holds 58 '
                         'characters with its box and colours (make_lmgt3_aston.py 6.)')
    if it['data'] and it['data'].startswith("if ([TyrePressureUnit] = 'Psi'") and unprefix(e.get('Bind')) != it['data']:
        known.append(f"{tag}: bar formatted in the formula (format(x, '0.00')), shown as text: SimHub formats only numbers, the "
                     "wheel numeric text too (make_lmgt3_aston.py 2.)")
    elif it['data'] and it['fmt'] and e.get('Format') not in (it['fmt'], 'time:' + it['fmt']):
        problems.append(f'{tag}: format {it["fmt"]} -> {e.get("Format")}')
    if it['data'] and it['fmt'] and e.get('Format') == 'time:' + it['fmt']:
        zero = it['fmt'].replace('\\', '').replace('m', '0').replace('ss', '00').replace('fff', '000')
        if e.get('Empty') != zero:
            problems.append(f'{tag}: a zero time shows "{zero}" in SimHub, "{e.get("Empty")}" here (Empty)')
    # colours
    if e['Type'] in ('label', 'value'):
        if it['colour']:
            if it['colour'] != unprefix(e.get('ColorBind')):
                problems.append(f'{tag}: colour formula {it["colour"]} -> {e.get("ColorBind")}')
            conv = [(s['Value'], rgb(s['Color'])) for s in e.get('ColorStops') or []]
            if it['stops'] and conv != it['stops']:
                problems.append(f'{tag}: colour stops {it["stops"]} -> {conv}')
        elif e.get('ColorBind') and it['it'].get('PlayerStyleEnabled') and re.fullmatch(
                r"if\(driverisplayer\(\d+\), '%s', '%s'\)" % (rgb(it['it'].get('PlayerTextColor')), rgb(it['it'].get('OpponentTextColor'))), unprefix(e['ColorBind']) or ''):
            known.append(f"{tag}: the player's own line in its player colour ({rgb(it['it'].get('PlayerTextColor'))}): SimHub's "
                         "leaderboard player style, as a colour formula")
        elif e.get('ColorBind'):
            problems.append(f"{tag}: a colour formula the SimHub item doesn't have ({e['ColorBind']})")
        elif it['fixed'] and rgb(e.get('Color')) != it['fixed']:
            if it['opacity'] < 1: known.append(f"{tag}: {it['fixed']} at {it['opacity']:.0%} opacity mixed into one colour ({e.get('Color')})")
            else: problems.append(f'{tag}: colour {it["fixed"]} -> {e.get("Color")}')
        if it['align'] and e.get('Align') != it['align'] and not (e['Type'] == 'label' and (it['name'] or '') + ' 2' in [x.get('Name') for x in E]):
            problems.append(f'{tag}: aligned {it["align"]} -> {e.get("Align")}')
    elif it['fill_formula'] and unprefix(e.get('ColorBind')) != it['fill_formula']:
        problems.append(f'{tag}: colour formula {it["fill_formula"]} -> {e.get("ColorBind")}')

# conversion-only elements that are parts of matched items
matched = {E[i].get('Name') for i in used}


def part(e):
    n = e.get('Name') or ''
    if n.endswith(' border') and n[:-7] in matched: return True     # a box drawn as two rectangles (make_lmgt3_aston.py 6.)
    if n in ('Kreis Mitte2 disc', 'Kreis Mitte2 disc middle') and evis(e) == [ALWAYS]: return True   # the ring picture's disc
    if n.endswith(' background') or n.endswith(' frame'): return True   # an overlay screen's background, a text's frame
    return False


parts = [i for i, e in enumerate(E) if i not in used and part(e)]
extra = [f"#{i} {e['Type']} {e.get('Name')} {evis(e)[:2]}" for i, e in enumerate(E) if i not in used and i not in parts]
print(f'{len(items)} items in the SimHub dash, {len(used)} elements matched to them; {len(parts)} more are parts of matched '
      f'items (a box\'s border, an overlay screen\'s background); {len(turns)} hide while an overlay over them shows')
print(f'\nmade on purpose ({len(known)}):')
for k in known: print('  ' + k)
if VERBOSE:
    print(f'\ntake turns ({len(turns)}):')
    for k in turns: print('  ' + k)
print(f'\ndifferences ({len(problems)}):')
for p in problems: print('  ' + p)
print(f'\nin the conversion only ({len(extra)}):')
for x in extra: print('  ' + x)
sys.exit(1 if problems or extra else 0)
