r"""
Parity of the converted "LMGT3 Mercedes AMG GT3" dash (make_mercedes_amg_gt3.py) with Redadeg's SimHub dash it is
converted from.

    python tools/dashes/mercedes_amg_gt3_parity.py [DASH.json] [-v]
        (default: SimHub's PluginsData\Common\FXProRpmSync\Dashes\lmgt3-mercedes-amg-gt3.json)

Walks the SimHub dash the way SimHub draws it (the main screen and its layers, the three widgets' screens as their sets
of pages, the ignition-off and ignition-on screens) and finds every item in the converted dash: texts, the gear,
leaderboard items, gauges, rectangles and pictures. For each: the same data (value formula), the same conditions (every
layer's and the item's own, as SimHub combines them; blinking), the same page, the same format, the same colour formula
and colour stops, the same alignment, the same text. Prints every difference; the ones made on purpose (KNOWN, with why)
are shown apart. Exit 1 if anything else differs, or the conversion has an element that is no item's (or part of one).

Not compared: sizes and places (the screen has its own fonts: make_mercedes_amg_gt3.py picks the closest and puts them
where the original's ink is; simhub_ref.py draws the original for comparing by eye), and the take-turns conditions the
conversion adds (an element under an overlay's box hides while the overlay shows: counted, listed with -v).
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simhub_ref  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith('-')]
VERBOSE = '-v' in sys.argv
DASH = args[0] if args else r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-mercedes-amg-gt3.json'
dash = json.load(open(DASH, encoding='utf-8-sig'))
E = dash['Elements']
orig = simhub_ref.Dash(simhub_ref.find('LMGT3 Mercedes AMG GT3'))
SETS = {'LAPTIMES_LEFT': 'page', 'TiresLeft': 'page2', 'FLAGS': 'page3'}   # the widget -> its set of pages
PAGE = re.compile(r'^\s*page([2-4])?\s*:\s*(\d+)\s*$')


def norm(s): return re.sub(r'\s+', ' ', s or '').strip()


def rgb(argb): return None if not argb else ('#' + argb[-6:]).upper()


def unprefix(f):
    return None if f is None else norm(f[6:] if f.startswith('ncalc:') else f[3:] if f.startswith('js:') else f)


def stops_of(b):
    """A colour binding's stops as SimHub maps a number to a colour (Mode 4): start, middle (if on), end."""
    if not isinstance(b, dict) or b.get('Mode') != 4: return None
    return [(b['StartColorValue'], rgb(b['StartColor']))] + \
           ([(b['MiddleColorValue'], rgb(b['MiddleColor']))] if b.get('EnableMiddleColor') else []) + \
           [(b['EndColorValue'], rgb(b['EndColor']))]


# ---------- the original, as SimHub draws it ----------
items = []
ALIGN = {0: 'left', 1: 'center', 2: 'right'}
TEXTS = ('TextItem', 'GearText', 'SpeedText', 'FuelText')


def walk(lst, conds, where, opacity, page=None):
    for it in lst or []:
        t = simhub_ref.tname(it)
        v = simhub_ref.formula(it, 'Visible')
        if it.get('Visible') is False and v is None: continue   # hidden for good (the four TyreTemperatureText items)
        c = conds + ([norm(v)] if v else [])
        blink = bool(it.get('BlinkEnabled')) or simhub_ref.formula(it, 'BlinkEnabled') is not None
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
        data = simhub_ref.formula(it, 'Value' if t == 'LinearGaugeItem' else 'Text')
        if t == 'GearText': data = 'gearText'
        if t == 'SpeedText': data = '[SpeedLocal]'
        if t == 'FuelText': data = 'fuel'
        items.append(dict(kind=t, name=it.get('Name'), data=norm(data) if data else None, conds=c + (['(blink)'] if blink else []),
                          blink_formula=norm(simhub_ref.formula(it, 'BlinkEnabled')) or None,
                          colour=norm(simhub_ref.formula(it, 'TextColor')) or None, stops=stops_of(b.get('TextColor')),
                          fill_formula=norm(simhub_ref.formula(it, 'BackgroundColor')) or None, fill_stops=stops_of(b.get('BackgroundColor')),
                          fmt=tb.get('FormatString') if t != 'FuelText' else it.get('Format'), text=it.get('Text') if data is None else None,
                          where=where, page=page,
                          fixed=rgb(it.get('TextColor')) if t in TEXTS or t.startswith('Leaderboard') else rgb(it.get('BackgroundColor')),
                          align=ALIGN.get(it.get('HorizontalAlignment')) if t in TEXTS or t.startswith('Leaderboard') else None,
                          opacity=op, it=it))


for s in orig.json['Screens']:
    trig = ((s.get('OverlayTriggerExpression') or {}).get('Expression') or '').strip() if s.get('IsOverlayLayer') else ''
    walk(s.get('Items'), [norm(trig)] if trig else [], s['Name'], 1.0)


# ---------- the conversion ----------
def evis(e):
    v = e.get('Visible')
    return [] if v is None else [v] if isinstance(v, str) else v


def epages(e):
    out = {}
    for c in evis(e):
        m = PAGE.match(c)
        if m: out['page' + (m.group(1) or '')] = int(m.group(2))
    return out


def take_turn(c):
    return c.startswith('ncalc:!(') or (c.startswith('js:') and 'return !(' in c)


def own(e):
    """The conditions an element has from its item: not pages, not blinking, not take-turns (as a set: an overlay
    screen's trigger and its layer's condition are often the same formula)."""
    # ("true": always shown, as a shape that comes and goes so it's drawn again over what changes under it: the pump)
    return sorted({unprefix(c) for c in evis(e) if not PAGE.match(c) and 'blink(' not in c and not take_turn(c) and unprefix(c) != 'true'})


def blink_cond(e):
    b = [c for c in evis(e) if 'blink(' in c and not take_turn(c)]
    return b[0] if b else None


TYPES = {'TextItem': ('label', 'value', 'image'), 'GearText': ('value',), 'SpeedText': ('value',), 'FuelText': ('value',),
         'EllipseItem': ('ellipse',), 'RectangleItem': ('rect', 'box', 'dim'), 'ImageItem': ('image', 'rect'),
         'LinearGaugeItem': ('bar',)}


def types_of(kind): return TYPES.get(kind, ('value',) if kind.startswith('Leaderboard') else ())


def item_conds(it): return sorted({c for c in it['conds'] if c != '(blink)'})


def is_text_picture(e, it):
    return e['Type'] == 'image' and (e.get('Image') or '').startswith(f'text {it["name"]} ')


def pressure_formula(o):
    """make_mercedes_amg_gt3.py 2.: the tyre pressures' formula with bar formatted in it too (format(x, '0.00'))."""
    m = re.search(r"\[(TyrePressure\w+)\]\)\)$", o)
    return o[:-len(f'[{m.group(1)}]))')] + f"format([{m.group(1)}], '0.00')))" if m and o.startswith("if ([TyrePressureUnit] = 'Psi'") else None


def same_data(it, e):
    b = e.get('Bind') or ''
    if it['kind'] in ('GearText', 'FuelText'): return b == it['data']
    if it['kind'] == 'SpeedText': return unprefix(b) == it['data']
    return unprefix(b) == it['data'] or unprefix(b) == pressure_formula(it['data'])


used = set()
problems, known, turns = [], [], []
for it in items:
    tag = f"{it['where']}/{it['name']} ({it['kind']})"
    want = item_conds(it)
    if it['name'] == 'Headlight Blue':
        if [e for e in E if e.get('Name') == 'Headlight Blue']: problems.append(f'{tag}: expected left out')
        else: known.append(f"{tag}: left out: a blue rectangle at 15 % over the whole dash while the headlights are on; the wheel "
                           "draws opaque, so every pixel of the dash would be drawn again tinted at each change (make_mercedes_amg_gt3.py 2.)")
        continue
    if '/Icons/' in it['where'] or it['name'] in ('FLAGS OFF', 'FLAGS ON'):
        # (an element left in the side column would match no item: "in the conversion only" below)
        known.append(f"{tag}: left out with the side column: the wide layout stretches the dash over the screen's width "
                     "(the maintainer's choice; make_mercedes_amg_gt3.py 7.)")
        continue
    if it['kind'] == 'TextItem' and it['name'] in ('1', '2', '3', '4') and it['page'] is not None and (it['text'] or '').strip() in ('•', '�', ''):
        # the lap time box's page dots: each page's four as one picture (make_mercedes_amg_gt3.py 6.)
        hits = [i for i, e in enumerate(E) if e['Type'] == 'image' and e.get('Name') == 'Punkte' and own(e) == want and epages(e) == {it['page'][0]: it['page'][1]}]
        if not hits: problems.append(f'{tag}: its page dots picture not found'); continue
        used.add(hits[0])
        if it['name'] == '1': known.append(f"{tag} (+2, 3, 4): the page dots (•, a character the screen's fonts lack) as one picture of the original's dots (make_mercedes_amg_gt3.py 6.)")
        continue
    cands = []
    for i, e in enumerate(E):
        if i in used or e['Type'] not in types_of(it['kind']): continue
        if e.get('Name') != it['name']: continue
        if it['kind'] == 'TextItem' and e['Type'] == 'image' and not is_text_picture(e, it): continue
        if it['kind'] == 'ImageItem' and e['Type'] == 'rect': continue
        if it['data']:
            if not same_data(it, e): continue
        elif it['kind'] == 'TextItem' and e['Type'] == 'label':
            # (a text longer than one screen command: two labels, the second named "<name> 2")
            two = [x for x in E if x.get('Name') == (e.get('Name') or '') + ' 2' and x['Type'] == 'label' and own(x) == own(e)]
            if norm(e.get('Text')) != norm(it['text']) and not (two and norm((e.get('Text') or '') + ' ' + (two[0].get('Text') or '')) == norm(it['text'])): continue
        if own(e) != want: continue
        if (it['page'] is None) != (not epages(e)): continue
        if it['page'] is not None and epages(e) != {it['page'][0]: it['page'][1]}: continue
        cands.append(i)
    if not cands and it['where'].startswith('IGN ON1/') and '/TiresLeft' in it['where'] and it['data']:
        known.append(f"{tag}: left out: the start-up screen's X placeholders are drawn over it (SimHub shows both, one on the "
                     "other, for 1.5 s; on the wheel each change under an X would redraw it) (make_mercedes_amg_gt3.py 7.)")
        continue
    if not cands:
        problems.append(f'{tag}: not found with the same data, conditions and page ({it["data"] or it["text"]} | {want} | {it["page"]})')
        continue
    i = cands[0]
    used.add(i)
    e = E[i]
    # blinking, and what turns it on
    bc = blink_cond(e)
    if ('(blink)' in it['conds']) != (bc is not None):
        problems.append(f'{tag}: blinks {"(blink)" in it["conds"]} -> {bc is not None}')
    elif it['blink_formula'] and not (bc or '').endswith(f" or !({it['blink_formula']})"):
        problems.append(f"{tag}: blinks while {it['blink_formula']} in SimHub, here {bc}")
    t = [c for c in evis(e) if take_turn(c)]
    if t: turns.append(f'{tag}: hides while {len(t)} overlay(s) over it show')
    if it['name'] == 'Zapfsaule_trans' and 'ncalc:true' in evis(e):
        known.append(f"{tag}: always shown, as a shape that comes and goes (\"true\"): drawn again over the energy gauge under its "
                     "pump-shaped hole each time the gauge changes (in the static layer the gauge was drawn over it) (make_mercedes_amg_gt3.py 7.)")
    if is_text_picture(e, it) and '�' in (it['text'] or ''):
        known.append(f"{tag}: the original stores \"\ufffd\" where \"°\" / \"•\" was meant (SimHub shows the replacement "
                     "character): the intended character, as a picture in the original's font (make_mercedes_amg_gt3.py 6.)")
        continue
    if is_text_picture(e, it):
        known.append(f"{tag}: a picture of its text in the original's font, not screen text: the screen has no normally "
                     "spaced font under 32 px (make_mercedes_amg_gt3.py 6.)")
        continue
    if it['kind'] == 'ImageItem' and it['where'].startswith('IGN ') and it['name'] == 'ImageItem':
        bg = [x for k, x in enumerate(E) if k not in used and x.get('Name') == 'ImageItem background' and own(x) == want and x['Type'] == 'rect']
        if not bg: problems.append(f'{tag}: its background rectangle not found'); continue
        used.add(E.index(bg[0]))
        # (cut in strips around the rev bar's rows: no RAM-drive picture over a bar; make_mercedes_amg_gt3.py 7.)
        used.update(k for k, x in enumerate(E) if x['Type'] == 'image' and x.get('Name') == it['name'] and own(x) == want
                    and (x.get('Image') or '').split(' rows ')[0] == (e.get('Image') or '').split(' rows ')[0])
        known.append(f"{tag}: the logo's own area as a picture over a rectangle of the picture's background colour"
                     + (f", {it['opacity']:.0%} opacity mixed in" if it['opacity'] < 1 else '') + " (make_mercedes_amg_gt3.py 7.)")
        continue
    if it['kind'] == 'FuelText' and e.get('Empty') != it['it'].get('NoDataText'):
        problems.append(f"{tag}: shows \"{it['it'].get('NoDataText')}\" without data in SimHub, \"{e.get('Empty')}\" here")
    if it['data'] and it['data'].startswith("if ([TyrePressureUnit] = 'Psi'") and unprefix(e.get('Bind')) != it['data']:
        known.append(f"{tag}: bar formatted in the formula (format(x, '0.00')), shown as text: SimHub formats only numbers, the "
                     "wheel numeric text too (make_mercedes_amg_gt3.py 2.)")
    elif it['data'] and it['fmt'] and e.get('Format') not in (it['fmt'], 'time:' + it['fmt']):
        problems.append(f'{tag}: format {it["fmt"]} -> {e.get("Format")}')
    elif it['data'] and not it['fmt'] and e['Type'] == 'value' and it['kind'] in ('TextItem', 'SpeedText'):
        # no format in SimHub: a bare lap time shows as a time (m:ss.fff, "laptime"), anything else as it comes ("text")
        bare_lap = re.fullmatch(r'\[[^\]]*(LapTime|BestLap|LastLap|Laptime)[^\]]*\]', it['data'], re.I)
        want_fmt = 'laptime' if bare_lap else 'text'
        if it['kind'] == 'SpeedText': want_fmt = '0'   # SimHub's speed item: whole numbers
        if e.get('Format') != want_fmt:
            problems.append(f'{tag}: no format in SimHub (shown as {want_fmt}), here {e.get("Format")}')
    if it['data'] and it['fmt'] and e.get('Format') == 'time:' + it['fmt']:
        zero = it['fmt'].replace('\\', '').replace('m', '0').replace('ss', '00').replace('f', '0')
        if e.get('Empty') != zero:
            problems.append(f'{tag}: a zero time shows "{zero}" in SimHub, "{e.get("Empty")}" here (Empty)')
    # leaderboard items (no format string in SimHub: each control's own format): names as text, best laps formatted in
    # their formula with the control's TimeFormat, gaps in its Format, signed unless AlwaysAppendSign is off
    if it['kind'].startswith('Leaderboard') and e['Type'] == 'value':
        if it['kind'] == 'LeaderboardOpponentGap':
            num = it['it'].get('Format') or '0.00'
            lf = f'+{num};-{num};{num}' if it['it'].get('AlwaysAppendSign') is not False else num
        else:
            lf = 'text'
        if e.get('Format') != lf: problems.append(f'{tag}: format {lf} (as SimHub draws it) -> {e.get("Format")}')
        tf = (it['it'].get('TimeFormat') or r'm\:ss\.fff').replace("'", '')
        if it['kind'] == 'LeaderboardOpponentBestLap' and tf.replace('\\', '\\\\') not in (e.get('Bind') or ''):
            problems.append(f'{tag}: its time not formatted {tf} in its formula')
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
        two = [x for x in E if x.get('Name') == (e.get('Name') or '') + ' 2' and x['Type'] == 'label' and own(x) == own(e)]
        if it['align'] and e.get('Align') != it['align'] and two and e.get('Align') == 'left':
            used.add(E.index(two[0]))
            known.append(f"{tag}: longer than one screen command takes: two labels, left-aligned where the {it['align']}ed line reads as one "
                         "(make_mercedes_amg_gt3.py 7.)")
        elif it['align'] and e.get('Align') != it['align']:
            problems.append(f'{tag}: aligned {it["align"]} -> {e.get("Align")}')
    elif it['fill_formula']:
        if unprefix(e.get('ColorBind')) != it['fill_formula']:
            problems.append(f'{tag}: colour formula {it["fill_formula"]} -> {e.get("ColorBind")}')
        conv = [(s['Value'], rgb(s['Color'])) for s in e.get('ColorStops') or []]
        if it['fill_stops'] and conv != it['fill_stops']:
            problems.append(f'{tag}: colour stops {it["fill_stops"]} -> {conv}')
    elif e['Type'] == 'bar':
        gf = norm(simhub_ref.formula(it['it'], 'GaugeColor')) or None
        if rgb(it['it'].get('GaugeColor')) != rgb(e.get('Color')):
            problems.append(f"{tag}: gauge colour {rgb(it['it'].get('GaugeColor'))} -> {e.get('Color')}")
        if gf != unprefix(e.get('ColorBind')):
            problems.append(f"{tag}: gauge colour formula {gf} -> {e.get('ColorBind')}")
        gs = stops_of((it['it'].get('Bindings') or {}).get('GaugeColor'))
        if gs and gs != [(x['Value'], rgb(x['Color'])) for x in e.get('ColorStops') or []]:
            problems.append(f"{tag}: gauge colour stops {gs} -> {e.get('ColorStops')}")
    elif e['Type'] == 'rect' and it['fixed'] and rgb(it['it'].get('BackgroundColor')) and (it['it'].get('BackgroundColor') or '').upper()[:3] != '#00' \
            and rgb(e.get('Color')) != it['fixed']:
        problems.append(f'{tag}: colour {it["fixed"]} -> {e.get("Color")}')
    if it['kind'] == 'RectangleItem' and e['Type'] == 'rect' and not (it['it'].get('BackgroundColor') or '#00').upper().startswith('#FF')             and (it['it'].get('BorderStyle') or {}).get('BorderTop'):
        # an outline box that comes and goes: its four sides as rectangles (make_mercedes_amg_gt3.py 7.)
        sides = [k for k, x in enumerate(E) if k not in used and x.get('Name') == it['name'] + ' border' and own(x) == want and epages(x) == epages(e)
                 and x['X'] >= e['X'] - 1 and x['X'] <= e['X'] + e['W'] and x['Y'] >= e['Y'] - 1 and x['Y'] <= e['Y'] + 400]
        if len(sides) < 3: problems.append(f'{tag}: its sides not found')
        else:
            used.update(sides[:3])
            known.append(f"{tag}: an outline that comes and goes, as its four sides (rectangles: no RAM-drive picture) (make_mercedes_amg_gt3.py 7.)")

# conversion-only elements that are parts of matched items
matched = {E[i].get('Name') for i in used}


def part(e):
    n = e.get('Name') or ''
    if n.endswith(' border') and n[:-7] in matched: return True     # a box drawn as two rectangles (make_mercedes_amg_gt3.py 7.)
    if n.endswith(' background') or n.endswith(' frame') or n.endswith(' frame border'): return True   # an overlay screen's background, a text's frame
    return False


parts = [i for i, e in enumerate(E) if i not in used and part(e)]
extra = [f"#{i} {e['Type']} {e.get('Name')} {evis(e)[:2]}" for i, e in enumerate(E) if i not in used and i not in parts]
print(f'{len(items)} items in the SimHub dash, {len(used)} elements matched to them; {len(parts)} more are parts of matched '
      f'items (a box\'s border, an overlay screen\'s background, a text\'s frame); {len(turns)} hide while an overlay over them shows')
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
