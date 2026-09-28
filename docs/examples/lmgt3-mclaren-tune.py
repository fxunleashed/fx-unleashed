"""
Worked example for /create-dash (.claude/skills/create-dash): tuning an imported SimHub dash until it passes the gates.

    fxdash import "LMGT3 McLaren 720S" out.json --fit 790,460 --png out.png > report.json
    python docs/examples/lmgt3-mclaren-tune.py out.json
    fxdash check out.json --pad 10,20 ; fxdash fit-bands out.json ; fxdash verify out.json

Keep dash edits in a script like this: re-importing and re-tuning is then one command, and the reasons stay written
down. Before it: 16 KB/s, 64 KB worst second, 727 of 1200 updates flashing, RPM showing "573". After: 5 KB/s,
15 KB worst second, no flashes.
"""
import json, subprocess, os, sys
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FX = os.path.join(REPO, 'tools', 'fxdash', 'bin', 'Release', 'net48', 'fxdash.exe')
path = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else 'out.json')
d = json.load(open(path, encoding='utf-8'))
E = d['Elements']


def els(name, typ=None):
    hits = [e for e in E if e.get('Name') == name and (typ is None or e['Type'] == typ)]
    assert hits, name
    return hits


def suggest(e, text, height=0):
    args = [FX, 'suggest-font', str(e['W']), str(e['H']), text] + (['--height', str(height)] if height else [])
    return json.loads(subprocess.run(args, capture_output=True, encoding='utf-8').stdout)['font']


def fit(e, text, prefer=None):
    """The preferred font if the text fits the box in it, else the tallest that fits."""
    if prefer is not None:
        f = json.loads(subprocess.run([FX, 'fonts', '--sample', text], capture_output=True, encoding='utf-8').stdout)
        p = [x for x in f if x['id'] == prefer][0]
        if p['height'] <= e['H'] and 0 <= p['sampleWidth'] <= e['W'] - 6:
            e['Font'] = prefer
            return prefer
    e['Font'] = suggest(e, text)
    assert e['Font'] >= 0, (e['Name'], text)
    return e['Font']


# 0. what verify flagged on the raw import
# - ABS/TC icons are pictures shown while ABS/TC works (every braking zone): each toggle redraws the picture
#   (10 KB/s alone). Cheap coloured labels instead.
for n, col in (('ABS', '#FFA000'), ('TC', '#30C0FF')):
    for e in [x for x in E if x.get('Name') == n and x['Type'] == 'image']:
        e['Type'] = 'label'; e['Text'] = n; e['Color'] = col; e['Align'] = 'center'
        e.pop('Image', None); e.pop('MaxColors', None)
        e['Font'] = suggest(e, n)
# - the ARB values' boxes reached into the "FUEL LAST LAP" label below them: they flashed with it
tag = els('ARB F Tag2', 'label')[0]
for n in ('ARB F#', 'ARB R#'):
    v = els(n, 'value')[0]; v['H'] = tag['Y'] - 2 - v['Y']

# 1. every value's widest real text (the importer copies SimHub's preview text, often "0")
samples = {
    'RPM': ['8888'], 'SPEED': ['388'], 'GEAR': ['8'], 'BB': ['88.8'], 'ABS': ['88'], 'TC': ['88'], 'MAP': ['88'],
    'VE#': ['100'], 'SLIP': ['11'], 'TCCUT': ['11'], 'LAP': ['188'], 'PREDICTED LAPTIME': ['8:88.88'],
    'DELTA': ['+88.88'], 'FUEL LAP': ['188'], 'LAST LAP#': ['8:88.888'], 'ARB R#': ['88'], 'ARB F#': ['88'],
    'T_RR': ['888'], 'T_RL': ['888'], 'T_FR': ['888'], 'T_FL': ['888'],
    'P_RR': ['88.88'], 'P_RL': ['88.88'], 'P_FR': ['88.88'], 'P_FL': ['88.88'],
    'RR %': ['99'], 'RL %': ['99'], 'FR %': ['99'], 'FL %': ['99'], 'RACE 1 Tag': ['PRACTICE'],
}
for e in E:
    if e['Type'] == 'value' and e.get('Name') in samples:
        e['Samples'] = samples[e['Name']]

# 2. the centre column (324..466): RPM above, the gear big below it, speed at the bottom
col_x, col_w = 324, 142
rpm = els('RPM', 'value')[0]; rpm.update(X=col_x, W=col_w, Y=100, H=50); fit(rpm, '8888', 100)
gear = els('GEAR', 'value')[0]; gear.update(X=col_x, W=col_w, Y=152, H=172, Empty='N')
gear['Font'] = suggest(gear, '8', 170)  # a gear font (digits, N, R) as tall as fits

# 3. values in the compact 963 family where they fit (the S fonts are letter-spaced: "5 9", "2 . 3 8")
for name, prefer in (('SLIP', 98), ('TCCUT', 98), ('VE#', 98), ('LAP', 98), ('ARB R#', 101), ('ARB F#', 101),
                     ('FUEL LAP', 98), ('LAST LAP#', 98)):
    for e in els(name, 'value'):
        fit(e, e['Samples'][0], prefer)
for e in els('VE#', 'value'):
    if e['Y'] > 340:  # "FUEL LAST LAP" value (named VE# in the SimHub dash)
        e['Samples'] = ['8.88']; fit(e, '8.88', 98)
for name in ('BB', 'ABS', 'TC', 'MAP'):
    for e in els(name, 'value'):
        fit(e, e['Samples'][0], 101 if e['H'] >= 32 else None)

# 4. tyre wear in the 33 px tyre blocks: "100" can't fit, so show at most 99
for n in ('RR %', 'RL %', 'FR %', 'FL %'):
    for e in els(n, 'value'):
        e['Bind'] = 'ncalc:min(' + e['Bind'][len('ncalc:'):] + ', 99)'
        fit(e, '99')

# 5. labels: the tallest font their text fits (small screen fonts are wide)
for e in E:
    if e['Type'] == 'label' and e['H'] <= 40 and e.get('Name') not in ('720S GT3',):
        f = suggest(e, e['Text'])
        if f >= 0: e['Font'] = f
        else:
            # doesn't fit in any font (the screen's small fonts are wide): keep a 16 px one, widen the box around its centre
            e['Font'] = 12
            fonts = json.loads(subprocess.run([FX, 'fonts', '--sample', e['Text']], capture_output=True, encoding='utf-8').stdout)
            w = [x for x in fonts if x['id'] == 12][0]['sampleWidth'] + 6
            if w > e['W']: e['X'] -= (w - e['W']) // 2; e['W'] = w

json.dump(d, open(path, 'w', encoding='utf-8'), indent=2)
print('tuned')

# 6. last look fixes: the tallest gear font; top row values in the 24 px narrow font; fuel per lap compact
d = json.load(open(path, encoding='utf-8')); E = d['Elements']
els('GEAR', 'value')[0]['Font'] = 102                      # 128 px, digits/N/R only (Empty is "N")
for name in ('BB', 'ABS', 'TC', 'MAP'):
    for e in els(name, 'value'):
        if e['Y'] < 60: fit(e, e['Samples'][0], 60)
for e in els('VE#', 'value'):
    if e['Y'] > 340: fit(e, '8.88', 101)
json.dump(d, open(path, 'w', encoding='utf-8'), indent=2)
print('look fixes')
