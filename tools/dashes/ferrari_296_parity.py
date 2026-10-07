"""
Parity of the converted "LMGT3 Ferrari 296" dash (make_ferrari_296.py) with Redadeg's SimHub dash it is converted from.

    python tools/dashes/ferrari_296_parity.py [DASH.json] [-v]
        (default: SimHub's PluginsData\\Common\\FXProRpmSync\\Dashes\\lmgt3-ferrari-296.json)

Walks the SimHub dash the way SimHub draws it (the main screen and its layers, the four widgets' screens as their sets of
pages, the ignition-off and ignition-on screens) and finds every item in the converted dash:
- values (texts with a formula, the gear, leaderboard items): the same data, conditions (every layer's and the item's
  own, as SimHub combines them; blinking), page, format, colour and colour formula, alignment;
- what never changes (panel pictures and their shading boxes, gradients, lines, fixed texts): drawn by
  make_ferrari_296.py into pictures from the original items themselves, so here: a picture that holds the item (its name
  in the picture's, over the item's box) with the same conditions and page;
- the cold / hot tiles (a panel 80 high behind a number): a rounded box in the panel's colour and border colour;
- rectangles (the rev bar's segment backgrounds, the yellow flag box, car up, the ignition screens) and the rev bar's
  segments (the same fill as SimHub's: value in percent of the segment's maximum formula).
Prints every difference; the ones made on purpose (KNOWN, with why) are shown apart. Exit 1 if anything else differs,
or the conversion has an element that is no item's (or part of one).

Not compared: sizes and places (make_ferrari_296.py picks the screen font closest to the original's and puts it where
the original's ink is; simhub_ref.py draws the original for comparing by eye), and the take-turns conditions the
conversion adds (an element under an overlay's box hides while the overlay shows: counted, listed with -v).
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simhub_ref  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith('-')]
VERBOSE = '-v' in sys.argv
DASH = args[0] if args else r'C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\lmgt3-ferrari-296.json'
dash = json.load(open(DASH, encoding='utf-8-sig'))
E = dash['Elements']
orig = simhub_ref.Dash(simhub_ref.find('LMGT3 Ferrari 296'))
SETS = {'Tyres': 'page', 'Laptimes': 'page2', 'Fuel Low 15': 'page3', 'Fuel Low 10': 'page4'}
PAGE = re.compile(r'^\s*page([2-4])?\s*:\s*(\d+)\s*$')
bw, bh = orig.json.get('BaseWidth') or 1280, orig.json.get('BaseHeight') or 720
SC = min(790 / bw, 460 / bh)
OX, OY = (790 - bw * SC) / 2, (460 - bh * SC) / 2


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


def walk(lst, conds, where, fx, fy, fs, page=None, hidden=False):
    for it in lst or []:
        t = simhub_ref.tname(it)
        v = simhub_ref.formula(it, 'Visible')
        gone = hidden or (it.get('Visible') is False and v is None)   # hidden for good: SimHub never draws it
        c = conds + ([norm(v)] if v else [])
        blink = bool(it.get('BlinkEnabled')) or simhub_ref.formula(it, 'BlinkEnabled') is not None
        if t in ('Layer', 'GroupItem'):
            walk(it.get('Childrens') or it.get('Items'), c + (['(blink)'] if blink else []), where + '/' + str(it.get('Name')), fx, fy, fs, page, gone)
            continue
        if t == 'WidgetItem':
            w = json.load(open(os.path.join(orig.folder, it['FileName']), encoding='utf-8-sig'))
            s = fs * (it.get('Width') or w.get('BaseWidth')) / (w.get('BaseWidth') or 1)
            for k, scr in enumerate(w['Screens']):
                walk(scr['Items'], c, where + '/' + it['Name'] + f'[page {k}]', fx + (it.get('Left') or 0) * fs, fy + (it.get('Top') or 0) * fs, s,
                     (SETS[it['Name']], k), gone)
            continue
        if gone: continue
        b = it.get('Bindings') or {}
        tb = b.get('Text') if isinstance(b.get('Text'), dict) else {}
        data = simhub_ref.formula(it, 'Value' if t == 'LinearGaugeItem' else 'Text')
        if t == 'GearText': data = 'gearText'
        x, y = fx + (it.get('Left') or 0) * fs, fy + (it.get('Top') or 0) * fs
        items.append(dict(kind=t, name=it.get('Name'), data=norm(data) if data else None, conds=c + (['(blink)'] if blink else []),
                          blink_formula=norm(simhub_ref.formula(it, 'BlinkEnabled')) or None,
                          colour=norm(simhub_ref.formula(it, 'TextColor')) or None, stops=stops_of(b.get('TextColor')),
                          fmt=tb.get('FormatString'), text=it.get('Text') if data is None else None, where=where, page=page,
                          fixed=rgb(it.get('GearTextColor') or it.get('TextColor')) if t in ('TextItem', 'GearText') or t.startswith('Leaderboard') else None,
                          align=ALIGN.get(it.get('HorizontalAlignment')) if t in ('TextItem', 'GearText') or t.startswith('Leaderboard') else None,
                          box=(x, y, (it.get('Width') or 0) * fs, (it.get('Height') or 0) * fs), it=it))


for s in orig.json['Screens']:
    trig = ((s.get('OverlayTriggerExpression') or {}).get('Expression') or '').strip() if s.get('IsOverlayLayer') else ''
    walk(s.get('Items'), [norm(trig)] if trig else [], s['Name'], OX, OY, SC)


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
    """The conditions an element has from its item: not pages, not blinking, not take-turns (as a set: SimHub's overlay
    trigger and its layer's condition are often the same formula)."""
    return sorted({unprefix(c) for c in evis(e) if not PAGE.match(c) and 'blink(' not in c and not take_turn(c)})


def blink_cond(e):
    b = [c for c in evis(e) if 'blink(' in c and not take_turn(c)]
    return b[0] if b else None


def item_conds(it): return sorted({c for c in it['conds'] if c != '(blink)'})


def same_page(it, e):
    return (it['page'] is None and not epages(e)) or (it['page'] is not None and epages(e) == {it['page'][0]: it['page'][1]})


def overlaps(it, e, slack=3):
    x, y, w, h = it['box']
    return x < e['X'] + e['W'] + slack and e['X'] - slack < x + w and y < e['Y'] + e['H'] + slack and e['Y'] - slack < y + h


def is_value(it):
    return it['kind'] == 'GearText' or it['kind'].startswith('Leaderboard') or (it['kind'] == 'TextItem' and it['data'])


def is_tile(it):
    return it['kind'] == 'ImageItem' and it['it'].get('Image') == 'shadow' and it['box'][3] < 50 and item_conds(it)


def same_text(a, b):
    return a['kind'] == b['kind'] == 'TextItem' and a['text'] == b['text'] and abs(a['box'][0] - b['box'][0]) < 1 and abs(a['box'][1] - b['box'][1]) < 1 \
        and a['it'].get('TextColor') == b['it'].get('TextColor') and a['it'].get('FontSize') == b['it'].get('FontSize') \
        and a['it'].get('Opacity', 100) == b['it'].get('Opacity', 100) and (b['it'].get('BackgroundColor') or '#00')[:3] == '#00'


def same_data(it, e):
    b = e.get('Bind') or ''
    if it['kind'] == 'GearText': return b == 'gearText'
    if it['kind'] == 'LinearGaugeItem':
        mx = norm(simhub_ref.formula(it['it'], 'Maximum'))
        return unprefix(b) == norm(f'if(({mx}) > 0, [Rpms] / ({mx}) * 100, 0)') and it['data'] == '[Rpms]'
    return unprefix(b) == it['data']


# leaderboard items: SimHub's own controls, imported as formulas picking the same drivers
LEADERBOARD = {'LeaderboardOpponentNameText', 'LeaderboardOpponentBestLap', 'LeaderboardOpponentGap'}
TYPES = {'TextItem': ('value',), 'GearText': ('value',), 'RectangleItem': ('rect', 'box'), 'LinearGaugeItem': ('bar',)}

used = set()
problems, known, turns = [], [], []
handled = set()   # items found as part of another's element (a segment's background box)
for it in sorted(items, key=lambda it: it['kind'] != 'LinearGaugeItem'):
    if id(it) in handled: continue
    tag = f"{it['where']}/{it['name']} ({it['kind']})"
    want = item_conds(it)
    # --- what never changes: inside a picture made from the original items ---
    if (it['kind'] in ('ImageItem', 'GradientItem') and not is_tile(it)) or (it['kind'] == 'TextItem' and not it['data']):
        if it['kind'] == 'TextItem' and want:
            twin = [o for o in items if o is not it and not item_conds(o) and o['page'] == it['page'] and same_text(o, it)]
            cover = [o for o in items if item_conds(o) == want and o['page'] == it['page'] and o['kind'] in ('ImageItem', 'GradientItem')
                     and items.index(o) < items.index(it) and o['where'] == it['where'] and overlaps(it, {'X': o['box'][0], 'Y': o['box'][1], 'W': o['box'][2], 'H': o['box'][3]}, -1.5)]
            if twin and not cover:
                known.append(f"{tag}: left out: the same caption, in the same place and colour, always drawn under it (make_ferrari_296.py 4.)")
                continue
        if it['kind'] == 'ImageItem' and it['name'] == 'LOGO':
            pic = [i for i, e in enumerate(E) if e['Type'] == 'image' and e.get('Name') == 'LOGO' and own(e) == want]
            bg = [i for i, e in enumerate(E) if e['Type'] == 'rect' and e.get('Name') == 'LOGO background' and own(e) == want]
            if not pic or not bg: problems.append(f'{tag}: the logo picture or its background not found'); continue
            used.update(pic + [bg[0]])
            known.append(f"{tag}: the logo's own area over a rectangle of the picture's background colour, in {len(pic)} strips (the rows "
                         "over the rev bar apart: no RAM picture over a bar) (make_ferrari_296.py 6.)")
            continue
        hits = [i for i, e in enumerate(E) if e['Type'] == 'image' and (e.get('Image') or '').startswith('panel ')
                and it['name'] in (e.get('Name') or '').split(' + ') and own(e) == want and same_page(it, e) and overlaps(it, e)]
        if not hits:
            problems.append(f'{tag}: not in any picture with the same conditions and page ({it["text"] or it["it"].get("Image") or ""} | {want} | {it["page"]})')
            continue
        e = E[hits[0]]
        used.update(hits)
        if ('(blink)' in it['conds']) != (blink_cond(e) is not None):
            problems.append(f'{tag}: blinks {"(blink)" in it["conds"]} -> {blink_cond(e) is not None}')
        continue
    if is_tile(it):
        bs = it['it'].get('BorderStyle') or {}
        hits = [i for i, e in enumerate(E) if i not in used and e['Type'] == 'box' and e.get('Name') == it['name'] and own(e) == want and same_page(it, e) and overlaps(it, e)]
        if not hits: problems.append(f'{tag}: its tile (a rounded box) not found'); continue
        e = E[hits[0]]; used.add(hits[0])
        if rgb(e.get('Fill')) != rgb(it['it']['BackgroundColor']) or rgb(e.get('Color')) != rgb(bs.get('BorderColor') or it['it']['BackgroundColor']):
            problems.append(f"{tag}: tile {rgb(it['it']['BackgroundColor'])} / border {rgb(bs.get('BorderColor'))} -> {e.get('Fill')} / {e.get('Color')}")
        known.append(f"{tag}: the cold / hot tile as a rounded box the screen draws, in its colour and border, without the shading "
                     "picture (the number's text covers nearly all of it; 24 such pictures would fill the RAM drive) (make_ferrari_296.py 4.)")
        continue
    kinds = ('value',) if it['kind'] in LEADERBOARD else TYPES.get(it['kind'], ())
    cands = []
    for i, e in enumerate(E):
        if i in used or e['Type'] not in kinds or e.get('Name') != it['name']: continue
        if it['kind'] in ('TextItem', 'GearText', 'LinearGaugeItem') and not same_data(it, e): continue
        if it['kind'] in LEADERBOARD and not (e.get('Bind') or '').startswith('ncalc:'): continue
        if own(e) != want or not same_page(it, e): continue
        if it['kind'] == 'RectangleItem' and not overlaps(it, e, 14): continue
        cands.append(i)
    if not cands:
        problems.append(f'{tag}: not found with the same data, conditions and page ({it["data"] or it["text"]} | {want} | {it["page"]})')
        continue
    i = cands[0]
    used.add(i)
    e = E[i]
    bc = blink_cond(e)
    if ('(blink)' in it['conds']) != (bc is not None):
        problems.append(f'{tag}: blinks {"(blink)" in it["conds"]} -> {bc is not None}')
    t = [c for c in evis(e) if take_turn(c)]
    if t: turns.append(f'{tag}: hides while {len(t)} overlay(s) over it show')
    if it['kind'] == 'LinearGaugeItem':
        if (e.get('Min'), e.get('Max')) != (0, 100) or rgb(e.get('Color')) != '#EDEDED':
            problems.append(f"{tag}: bar 0..100 in the gauge picture's grey -> {e.get('Min')}..{e.get('Max')} {e.get('Color')}")
        known.append(f"{tag}: its maximum ({norm(simhub_ref.formula(it['it'], 'Maximum'))}) folded into the value (the fill in percent "
                     "of it: the same fill), filled in the gauge picture's main grey (make_ferrari_296.py 2b.)")
        # its background box (Gauge Background/Item k): the bar's empty part, in its place
        n = it['name'].replace('Gauge Item ', 'Item ')
        bg = [o for o in items if o['kind'] == 'RectangleItem' and o['name'] == n and o['where'].endswith('Gauge Background')]
        c = bg[0]['it'].get('BackgroundColor') if bg else ''
        a = int(c[1:3], 16) / 255 if c else 0
        mixed = '#' + ''.join('%02X' % round(int(c[k:k + 2], 16) * a) for k in (3, 5, 7)) if c else None
        if not bg or rgb(e.get('Fill')) != mixed: problems.append(f'{tag}: its empty part is not its background box {n} ({mixed}): {e.get("Fill")}')
        else:
            handled.add(id(bg[0]))
            known.append(f"{tag}: in the place of its background box {n}, whose colour ({c} over black: {mixed}) is its empty part: what a "
                         "shrinking segment leaves is one colour (no tile repaints); full, it's 2 px narrower and shorter than SimHub's (make_ferrari_296.py 2b.)")
        continue
    if it['kind'] == 'RectangleItem':
        c = it['it'].get('BackgroundColor') or ''
        if len(c) == 9 and c[1:3].upper() not in ('FF', '00'):
            a = int(c[1:3], 16) / 255
            mixed = '#' + ''.join('%02X' % round(int(c[k:k + 2], 16) * a) for k in (3, 5, 7))
            if rgb(e.get('Fill') or e.get('Color')) != mixed: problems.append(f'{tag}: colour {c} over black -> {e.get("Color")}')
            else: known.append(f"{tag}: {c} ({a:.0%} see-through) mixed over the black under it ({mixed}) (make_ferrari_296.py 2b.)")
        elif c[1:3].upper() == 'FF' and rgb(e.get('Fill') or e.get('Color')) != rgb(c):
            problems.append(f'{tag}: colour {rgb(c)} -> {e.get("Color")}')
        continue
    # values: format
    fmt = e.get('Format')
    if it['fmt'] and fmt not in (it['fmt'], 'time:' + it['fmt']):
        problems.append(f'{tag}: format {it["fmt"]} -> {fmt}')
    elif not it['fmt'] and it['kind'] == 'TextItem':
        # no format in SimHub: a bare lap time shows as a time (m:ss.fff, "laptime"), anything else as it comes ("text")
        bare_lap = re.fullmatch(r'\[[^\]]*(LapTime|BestLap|LastLap|Laptime)[^\]]*\]', it['data'] or '', re.I)
        if fmt != ('laptime' if bare_lap else 'text'):
            problems.append(f'{tag}: no format in SimHub (shown as {"laptime" if bare_lap else "text"}), here {fmt}')
    if it['fmt'] and fmt == 'time:' + it['fmt']:
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
        problems.append(f'{tag}: colour {it["fixed"]} -> {e.get("Color")}')
    if it['align'] and e.get('Align') != it['align']:
        problems.append(f'{tag}: aligned {it["align"]} -> {e.get("Align")}')

# an overlay screen's own background (the import draws the screen's background colour under its items: the ignition
# screens' black)
screens = {s['Name']: ((s.get('OverlayTriggerExpression') or {}).get('Expression') or '').strip() for s in orig.json['Screens'] if s.get('IsOverlayLayer')}
for i, e in enumerate(E):
    n = e.get('Name') or ''
    if i not in used and e['Type'] == 'rect' and n.endswith(' background') and n[:-11] in screens and own(e) == [norm(screens[n[:-11]])]:
        used.add(i)
        known.append(f"{n[:-11]} (overlay screen): its background colour as a rectangle under its items (the import)")
extra = [f"#{i} {e['Type']} {e.get('Name')} {evis(e)[:2]}" for i, e in enumerate(E) if i not in used]
print(f'{len(items)} items in the SimHub dash, {len(used)} elements of the conversion hold them; {len(turns)} hide while an overlay over them shows')
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
