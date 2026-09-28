"""popular.py: builds the built-in dashes for popular sim cars (assets/dashes/*.json).

Original layouts in the spirit of each car's real display, drawn with the wheel's own fonts (the firmware carries
car-specific ones: ir18, 992, 296, f3, bmw, w12...). Every text box gets a font picked here from the car's preferred
fonts: the tallest that holds every sample with room (2 px above and below the font's band, 2 px each side).

    python tools/dashgen/popular.py            # writes assets/dashes/<id>.json
Then gate each: fxdash check --pad 10,20 (0 errors, 0 warnings), fit-bands (no changes), verify (Ok).
"""
import json, os, re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'assets', 'dashes')
W, H = 790, 460  # what the wheel shows: 800x480 less the default padding (10 left, 20 top)

# ---------- fonts (from Usb/FontMetrics.cs: [height, widths of ' '..'~'], -1 = no glyph) ----------
_src = open(os.path.join(ROOT, 'Usb', 'FontMetrics.cs'), encoding='utf-8').read()
FONTS = [[int(n) for n in m.split(',')] for m in re.findall(r'new\[\] \{([^}]*)\}', _src)]


def width(font, text):
    w = 0
    for ch in text:
        o = ord(ch)
        if o < 32 or o > 126:
            return -1
        g = FONTS[font][o - 31]
        if g < 0:
            return -1
        w += g
    return w


def fits(font, texts, w, h, pad=2):
    fh = FONTS[font][0]
    if fh + 2 * pad > h:
        return False
    return all(width(font, t) >= 0 and width(font, t) + 4 <= w for t in texts)


def pick(prefs, texts, w, h, pad=2):
    """The tallest font of `prefs` that holds every text in a w x h box."""
    ok = [f for f in prefs if fits(f, texts, w, h, pad)]
    if not ok:
        raise ValueError(f'no font of {prefs} fits {texts} in {w}x{h}')
    return max(ok, key=lambda f: (FONTS[f][0], -max(width(f, t) for t in texts)))


# roles: font lists to pick from, per look
NUM963 = [102, 99, 100, 97, 96, 98, 101]          # narrow numbers (102 digits/gear only)
LABEL = [60, 5, 14, 12, 10]                        # small labels, least wide first at each height
GEAR963 = [102, 117]
# big gear fonts (digits, D N P R; 70 = 992b has everything), tallest first: 248..119 px
GEARBIG = [26, 42, 75, 113, 52, 45, 39, 30, 70, 118, 34, 56, 77, 65, 72, 84, 66, 102, 117]

# ---------- elements ----------


def box(x, y, w, h, color, fill=None, border=2, radius=6, **kw):
    e = dict(Type='box', X=x, Y=y, W=w, H=h, Color=color, Border=border, Radius=radius)
    if fill:
        e['Fill'] = fill
    e.update(kw)
    return e


def rect(x, y, w, h, color, **kw):
    e = dict(Type='rect', X=x, Y=y, W=w, H=h, Color=color)
    e.update(kw)
    return e


def label(x, y, w, h, text, color, fonts=LABEL, align='center', **kw):
    f = pick(fonts, [text], w, h, pad=0)
    e = dict(Type='label', Name=text, X=x, Y=y, W=w, H=h, Text=text, Font=f, Color=color, Align=align)
    e.update(kw)
    return e


def value(x, y, w, h, name, bind, fmt, samples, preview, color, fonts=NUM963, align='center', empty='-', **kw):
    f = pick(fonts, samples + [preview, empty], w, h)
    e = dict(Type='value', Name=name, X=x, Y=y, W=w, H=h, Bind=bind, Format=fmt, Font=f, Color=color, Align=align,
             Samples=samples, PreviewText=preview, Empty=empty)
    e.update(kw)
    return e


def cell(x, y, w, h, title, name, bind, fmt, samples, preview, t, color=None, fonts=None, title_h=24, fill=None,
         frame=None, align='center', empty='-', **kw):
    """A framed cell: its title on top, the value filling the rest (with room above and below its band)."""
    out = [box(x, y, w, h, frame or t['frame'], fill=fill or t.get('fill'), radius=t.get('radius', 6), Name=f'{name} frame')]
    out.append(label(x + 6, y + 4, w - 12, title_h, title, t['title'], align=align if align != 'left' else 'left'))
    out.append(value(x + 8, y + title_h + 8, w - 16, h - title_h - 14, name, bind, fmt, samples, preview,
                     color or t['text'], fonts=fonts or t['num'], align=align, empty=empty, **kw))
    return out


def gear(x, y, w, h, t, fonts=None, frame=True, color=None):
    out = []
    if frame:
        out.append(box(x, y, w, h, t['accent'], fill=t.get('fill'), border=3, radius=t.get('radius', 6), Name='Gear frame'))
    prefs = fonts or t['gear']
    own = [f for f in prefs[:2] if fits(f, ['8', 'N', 'R'], w - 20, h - 16)]
    out.append(value(x + 10, y + 8, w - 20, h - 16, 'Gear', 'gearText', 'gear', ['8', 'N', 'R'], '3', color or t['text'],
                     fonts=own[:1] or prefs, empty='N'))
    return out


def lap_cell(x, y, w, h, title, name, bind, t, color=None, **kw):
    return cell(x, y, w, h, title, name, bind, 'laptime', ['8:88.888'], '1:32.481', t, color=color, empty='-:--.---', **kw)


def delta_cell(x, y, w, h, t, title='DELTA', **kw):
    return cell(x, y, w, h, title, 'Delta', 'delta', 'delta', ['+88.88', '-88.88'], '-0.35', t,
                PositiveColor='#FF3B30', NegativeColor='#30E060', empty='-.--', **kw)


def tyre_temp(corner):
    names = {'FL': 'FrontLeft', 'FR': 'FrontRight', 'RL': 'RearLeft', 'RR': 'RearRight'}
    return 'prop:DataCorePlugin.GameData.TyreTemperature' + names[corner]


def tyre_press(corner):
    names = {'FL': 'FrontLeft', 'FR': 'FrontRight', 'RL': 'RearLeft', 'RR': 'RearRight'}
    return 'prop:DataCorePlugin.GameData.TyrePressure' + names[corner]


TEMP_STOPS = [dict(Value=50, Color='#3FA9FF'), dict(Value=75, Color='#30E060'), dict(Value=100, Color='#30E060'),
              dict(Value=115, Color='#FF3B30')]


def tyre_grid(x, y, w, h, t, kind='temp', title='TYRES'):
    """Four corners in a frame: temperatures coloured cold/ok/hot, or pressures."""
    out = [box(x, y, w, h, t['frame'], fill=t.get('fill'), radius=t.get('radius', 6), Name=f'{title} frame'),
           label(x + 6, y + 3, w - 12, 20, title, t['title'])]
    cw, ch = (w - 24) // 2, (h - 32) // 2
    for i, c in enumerate(['FL', 'FR', 'RL', 'RR']):
        cx, cy = x + 8 + (i % 2) * (cw + 8), y + 26 + (i // 2) * (ch + 2)
        if kind == 'temp':
            out.append(value(cx, cy, cw, ch, f'Tyre {c}', tyre_temp(c), '0', ['188'], '84', t['text'], fonts=t['num'],
                             ColorBind=tyre_temp(c), ColorStops=TEMP_STOPS))
        else:
            out.append(value(cx, cy, cw, ch, f'Tyre {c}', tyre_press(c), '0.0', ['88.8'], '27.6', t['text'], fonts=t['num']))
    return out


def settings_popup(x, y, w, h, t, watch):
    """The native pop-up: shows the setting that just changed for 2 s (over whatever is under it)."""
    lab_font = pick(LABEL + [2, 60], [wt[1] for wt in watch], w - 8, 30, pad=0)
    vfont = pick(t['num'], ['88.8'], w - 8, h - 50)
    return [dict(Type='popup', Name='Setting changed', X=x, Y=y, W=w, H=h, Color='#000000', Radius=10, Font=lab_font,
                 ValueFont=vfont, Duration=2.0,
                 Watch=[dict(Bind=b, Label=l, Color=c, Format=f) for b, l, c, f in watch])]


def pit_banner(t, y=0, h=None, x=0, w=W):
    """A full-width 'PIT LIMITER' band over the top row while the limiter is on (it covers whole cells)."""
    h = h or 60
    return [rect(x, y, w, h, '#1E5BFF', Name='Pit limiter band', Visible='pitLimiter', PreviewVisible=False),
            label(x + 10, y + 4, w - 20, h - 8, 'PIT LIMITER', '#FFFFFF', fonts=[0, 3, 15, 8, 2, 5, 14], Name='Pit limiter text',
                  Visible='pitLimiter', PreviewVisible=False)]


def dash(id, name, desc, elements, author='FXPro Unlocked'):
    return dict(FormatVersion=2, Id=id, Name=name, Author=author, Description=desc, Elements=elements)


GT_WATCH = [('tcLevel', 'TC', '#1E6BFF', 'int'), ('tcCut', 'TC CUT', '#12B8D8', 'int'), ('absLevel', 'ABS', '#FFC400', 'int'),
            ('engineMap', 'MAP', '#B04CFF', 'int'), ('brakeBias', 'BRAKE BIAS', '#D0D0D0', '0.0')]

# ---------- the dashes ----------


def mx5_cup():
    t = dict(frame='#3A3A3A', title='#FF8A1F', text='#FFFFFF', accent='#FF8A1F', num=NUM963, gear=[118, 34] + GEARBIG, radius=4)
    e = []
    # top: current lap (big) and delta
    e += lap_cell(0, 0, 390, 110, 'LAP TIME', 'Current lap', 'currentLapTime', t)
    e += delta_cell(400, 0, 390, 110, t)
    # middle: speed / rpm, gear, last / best
    e += cell(0, 120, 250, 110, 'SPEED', 'Speed', 'speed', '0', ['388'], '164', t)
    e += cell(0, 240, 250, 100, 'RPM', 'RPM', 'rpm', '0', ['8888'], '6450', t)
    e += gear(260, 120, 270, 220, t)
    e += lap_cell(540, 120, 250, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += lap_cell(540, 240, 250, 100, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    # bottom: fuel, laps left, water, oil, position
    bw = (W - 4 * 10) // 5
    for i, (ti, n, b, f, s, p) in enumerate([
        ('FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '31.4'),
        ('LAPS', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '12.6'),
        ('WATER', 'Water', 'waterTemp', '0', ['188'], '88'),
        ('OIL', 'Oil', 'oilTemp', '0', ['188'], '96'),
        ('POS', 'Position', 'position', '0', ['88'], '7')]):
        e += cell(i * (bw + 10), 350, bw, 110, ti, n, b, f, s, p, t)
    e += pit_banner(t, 0, 110)
    e += settings_popup(260, 120, 270, 220, dict(t, num=NUM963), [('brakeBias', 'BRAKE BIAS', '#FF8A1F', '0.0')])
    return dash('mx5-cup', 'Mazda MX-5 Cup',
                'Spec-series simple: lap time and delta on top, speed and RPM, a big gear, last and best laps, then '
                'fuel, laps of fuel, water, oil and position. iRacing, AC, ACC.', e)


def gr86():
    t = dict(frame='#4A0B12', title='#FF3048', text='#FFFFFF', accent='#E0102A', num=NUM963, gear=[30, 39] + GEARBIG, radius=10,
             fill='#12070A')
    e = []
    e += lap_cell(0, 0, 255, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += lap_cell(0, 120, 255, 110, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += cell(0, 240, 255, 100, 'LAP', 'Lap', 'lap', '0', ['888'], '6', t)
    e += gear(265, 0, 260, 250, t)
    e += cell(265, 260, 260, 80, 'KM/H', 'Speed', 'speed', '0', ['388'], '142', t, title_h=20)
    e += delta_cell(535, 0, 255, 110, t)
    e += cell(535, 120, 255, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '28.3', t)
    e += cell(535, 240, 255, 100, 'POSITION', 'Position', 'position', '0', ['88'], '4', t)
    e += tyre_grid(0, 350, 390, 110, dict(t, num=NUM963), title='TYRE TEMPS')
    e += cell(400, 350, 190, 110, 'WATER', 'Water', 'waterTemp', '0', ['188'], '91', t)
    e += cell(600, 350, 190, 110, 'OIL', 'Oil', 'oilTemp', '0', ['188'], '99', t)
    e += pit_banner(t, 0, 110, 265, 260)
    return dash('gr86', 'Toyota GR86',
                'Road-car red: laps on the left, a big gear and speed in the middle, delta, fuel and position on the '
                'right, tyre temperatures and engine temps below. iRacing, GT7-style.', e)


def porsche_992_cup():
    num = [72, 69, 71] + NUM963          # the 992's own fonts first
    t = dict(frame='#2E2E2E', title='#F5C400', text='#FFFFFF', accent='#F5C400', num=num, gear=[70, 72] + GEARBIG, radius=0,
             fill='#0B0B0B')
    e = []
    e += delta_cell(0, 0, 390, 100, t)
    e += lap_cell(400, 0, 390, 100, 'LAP TIME', 'Current lap', 'currentLapTime', t)
    e += cell(0, 110, 250, 110, 'BRAKE BAL', 'Brake bias', 'brakeBias', '0.0', ['88.8'], '53.5', t)
    e += cell(0, 230, 250, 110, 'MAP', 'Map', 'engineMap', '0', ['88'], '1', t)
    e += gear(260, 110, 270, 230, t)
    e += lap_cell(540, 110, 250, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += lap_cell(540, 230, 250, 110, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += tyre_grid(0, 350, 380, 110, t, kind='press', title='PRESSURES')
    e += cell(390, 350, 190, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '61.2', t)
    e += cell(590, 350, 200, 110, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '14.2', t)
    e += pit_banner(t, 0, 100)
    e += settings_popup(260, 110, 270, 230, dict(t, num=NUM963),
                        [('brakeBias', 'BRAKE BAL', '#F5C400', '0.0'), ('engineMap', 'MAP', '#F5C400', 'int')])
    return dash('porsche-992-cup', 'Porsche 911 GT3 Cup (992)',
                'One-make Cup style in the 992\'s own fonts: delta and lap time on top, brake balance and map, the gear, '
                'last and best, tyre pressures and fuel. iRacing, ACC, AC.', e)


def porsche_992_gt3r():
    num = [69, 71] + NUM963
    t = dict(frame='#20304A', title='#8FB4FF', text='#FFFFFF', accent='#3D7BFF', num=num, gear=[70, 72] + GEARBIG, radius=6,
             fill='#070B14')
    e = []
    e += lap_cell(0, 0, 255, 100, 'LAST', 'Last lap', 'lastLapTime', t)
    e += delta_cell(265, 0, 260, 100, t)
    e += lap_cell(535, 0, 255, 100, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += cell(0, 110, 255, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['188.8'], '84.6', t)
    e += cell(0, 230, 255, 110, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '17.3', t)
    e += gear(265, 110, 260, 230, t)
    e += tyre_grid(535, 110, 255, 230, t, kind='press', title='TYRE PSI')
    # settings row: coloured tiles
    tiles = [('TC', 'tcLevel', '#1E6BFF'), ('TC2', 'tcCut', '#12B8D8'), ('ABS', 'absLevel', '#FFC400'),
             ('MAP', 'engineMap', '#B04CFF')]
    bw = 150
    for i, (ti, b, c) in enumerate(tiles):
        e += cell(i * (bw + 10), 350, bw, 110, ti, ti, b, '0', ['88'], str([4, 3, 5, 1][i]), t, frame=c, color=c)
    e += cell(640, 350, 150, 110, 'BB', 'Brake bias', 'brakeBias', '0.0', ['88.8'], '54.2', t)
    e += pit_banner(t, 0, 100)
    e += settings_popup(265, 110, 260, 230, dict(t, num=NUM963), GT_WATCH)
    return dash('porsche-992-gt3r', 'Porsche 911 GT3 R (992)',
                'GT3 endurance: last, delta and best on top, fuel and laps left, the gear, tyre pressures, and TC, TC2, '
                'ABS, map and brake bias tiles with a pop-up when one changes. ACC, iRacing, LMU.', e)


def amg_gt3():
    num = [49, 50] + NUM963             # the w12 family (Mercedes) first
    t = dict(frame='#2A2A2A', title='#00D2BE', text='#FFFFFF', accent='#00D2BE', num=num, gear=[52] + GEARBIG, radius=4,
             fill='#060909')
    e = []
    tiles = [('TC', 'tcLevel', '#1E6BFF', '4'), ('TC2', 'tcCut', '#12B8D8', '3'), ('ABS', 'absLevel', '#FFC400', '5'),
             ('MAP', 'engineMap', '#B04CFF', '1'), ('BB', 'brakeBias', '#FFFFFF', '56.5')]
    bw = (W - 4 * 8) // 5
    for i, (ti, b, c, p) in enumerate(tiles):
        fmt, s = ('0.0', ['88.8']) if b == 'brakeBias' else ('0', ['88'])
        e += cell(i * (bw + 8), 0, bw, 96, ti, ti, b, fmt, s, p, t, frame=c, color=c, title_h=20)
    e += delta_cell(0, 106, 250, 110, t)
    e += lap_cell(0, 226, 250, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += gear(260, 106, 270, 230, t)
    e += cell(540, 106, 250, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['188.8'], '92.4', t)
    e += cell(540, 226, 250, 110, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '16.8', t)
    e += lap_cell(0, 346, 250, 114, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += cell(260, 346, 130, 114, 'POS', 'Position', 'position', '0', ['88'], '5', t)
    e += cell(400, 346, 130, 114, 'LAP', 'Lap', 'lap', '0', ['888'], '12', t)
    e += lap_cell(540, 346, 250, 114, 'PREDICTED', 'Predicted', 'predictedLap', t)
    e += pit_banner(t, 0, 96)
    e += settings_popup(260, 106, 270, 230, dict(t, num=NUM963), GT_WATCH)
    return dash('amg-gt3-evo', 'Mercedes-AMG GT3 Evo',
                'Bosch-style: TC, TC2, ABS, map and bias tiles across the top, delta and last lap, the gear, fuel and laps '
                'left, then best, position, lap and predicted lap. ACC, iRacing, AC.', e)


def ferrari_296_gt3():
    num = [68, 67] + NUM963
    t = dict(frame='#5A0A0A', title='#FFD200', text='#FFFFFF', accent='#FFD200', num=num, gear=[77] + GEARBIG, radius=8,
             fill='#0E0303')
    e = []
    e += delta_cell(0, 0, 390, 100, t)
    e += cell(400, 0, 390, 100, 'SPEED', 'Speed', 'speed', '0', ['388'], '231', t)
    e += cell(0, 110, 120, 110, 'TC', 'TC', 'tcLevel', '0', ['88'], '4', t, color='#3FA9FF')
    e += cell(130, 110, 120, 110, 'ABS', 'ABS', 'absLevel', '0', ['88'], '3', t, color='#FFC400')
    e += cell(0, 230, 120, 110, 'TC2', 'TC2', 'tcCut', '0', ['88'], '2', t, color='#12B8D8')
    e += cell(130, 230, 120, 110, 'MAP', 'Map', 'engineMap', '0', ['88'], '1', t, color='#B04CFF')
    e += gear(260, 110, 270, 230, t)
    e += lap_cell(540, 110, 250, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += lap_cell(540, 230, 250, 110, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += cell(0, 350, 250, 110, 'BRAKE BIAS', 'Brake bias', 'brakeBias', '0.0', ['88.8'], '53.8', t)
    e += cell(260, 350, 270, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['188.8'], '97.1', t)
    e += cell(540, 350, 250, 110, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '18.4', t)
    e += pit_banner(t, 0, 100)
    e += settings_popup(260, 110, 270, 230, dict(t, num=NUM963), GT_WATCH)
    return dash('ferrari-296-gt3', 'Ferrari 296 GT3',
                'Red and yellow, in the 296\'s own fonts: delta and speed, TC, ABS, TC2 and map, the gear, last and best, '
                'brake bias, fuel and laps left. ACC, iRacing, LMU.', e)


def indycar():
    num = [74, 73] + NUM963             # ir04 family, then the narrow set
    t = dict(frame='#1C2A3A', title='#00B2FF', text='#FFFFFF', accent='#00B2FF', num=num, gear=[42, 75] + GEARBIG, radius=4,
             fill='#050A10')
    e = []
    e += cell(0, 0, 390, 120, 'SPEED', 'Speed', 'speed', '0', ['388'], '342', t)
    e += delta_cell(400, 0, 390, 120, t)
    e += cell(0, 130, 250, 100, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '34.2', t)
    e += cell(0, 240, 250, 100, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0.0', ['88.8'], '21.5', t)
    e += gear(260, 130, 270, 210, t)
    e += cell(540, 130, 120, 100, 'POS', 'Position', 'position', '0', ['88'], '9', t)
    e += cell(670, 130, 120, 100, 'LAP', 'Lap', 'lap', '0', ['888'], '47', t)
    e += cell(540, 240, 250, 100, 'BRAKE BIAS', 'Brake bias', 'brakeBias', '0.0', ['88.8'], '56.0', t)
    e += lap_cell(0, 350, 390, 110, 'LAST LAP', 'Last lap', 'lastLapTime', t)
    e += lap_cell(400, 350, 390, 110, 'BEST LAP', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += pit_banner(t, 0, 120)
    e += settings_popup(260, 130, 270, 210, dict(t, num=NUM963), [('brakeBias', 'BRAKE BIAS', '#00B2FF', '0.0')])
    return dash('indycar-ir18', 'IndyCar Dallara IR-18',
                'Ovals and road courses: speed and delta big on top, fuel and laps of fuel, the gear, position and lap, '
                'brake bias, last and best laps. iRacing.', e)


def dallara_f3():
    num = [114, 115] + NUM963
    t = dict(frame='#303030', title='#B8B8B8', text='#FFFFFF', accent='#FFFFFF', num=num, gear=[113] + GEARBIG, radius=2,
             fill='#000000')
    e = []
    # delta bar across the top, delta number under it
    e.append(dict(Type='deltabar', Name='Delta bar', X=35, Y=6, W=720, H=26, Bind='delta', Segments=9, Pitch=40,
                  SegmentWidth=34, Range=1.0, PositiveColor='#FF3B30', NegativeColor='#30E060', SegmentColor='#262626'))
    e += delta_cell(0, 40, 250, 110, t)
    e += lap_cell(0, 160, 250, 110, 'LAST', 'Last lap', 'lastLapTime', t)
    e += gear(260, 40, 270, 302, t)
    e += lap_cell(540, 40, 250, 110, 'CURRENT', 'Current lap', 'currentLapTime', t)
    e += lap_cell(540, 160, 250, 110, 'BEST', 'Best lap', 'bestLapTime', t, color='#C07BFF')
    e += cell(0, 276, 120, 66, 'POS', 'Position', 'position', '0', ['88'], '3', t, title_h=16)
    e += cell(130, 276, 120, 66, 'LAP', 'Lap', 'lap', '0', ['88'], '8', t, title_h=16)
    e += cell(540, 276, 250, 66, 'SPEED', 'Speed', 'speed', '0', ['388'], '218', t, title_h=16)
    e += tyre_grid(0, 350, 390, 110, dict(t, num=NUM963), title='TYRE TEMPS')
    e += cell(400, 350, 190, 110, 'BIAS', 'Brake bias', 'brakeBias', '0.0', ['88.8'], '57.5', t)
    e += cell(600, 350, 190, 110, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '22.7', t)
    e += settings_popup(260, 40, 270, 300, dict(t, num=NUM963), [('brakeBias', 'BRAKE BIAS', '#FFFFFF', '0.0')])
    return dash('dallara-f3', 'Dallara F3',
                'Open-wheel, in the F3\'s own fonts: a delta bar across the top, delta and last lap, a tall gear, current '
                'and best laps, position, lap and speed, tyre temperatures, bias and fuel. iRacing, AC, rFactor 2.', e)


def nascar():
    num = [125, 124, 126] + NUM963      # c8r gauges family
    t = dict(frame='#1F3A1F', title='#7CFF6B', text='#FFFFFF', accent='#7CFF6B', num=num, gear=[84] + GEARBIG, radius=6,
             fill='#030803')
    e = []
    e += cell(0, 0, 520, 130, 'RPM', 'RPM', 'rpm', '0', ['8888'], '8950', t)
    e += gear(530, 0, 260, 240, t)
    gauges = [('WATER', 'Water', 'waterTemp', '0', ['288'], '228'),
              ('OIL', 'Oil temp', 'oilTemp', '0', ['288'], '251'),
              ('OIL P', 'Oil pressure', 'prop:DataCorePlugin.GameData.OilPressure', '0', ['188'], '78'),
              ('FUEL P', 'Fuel pressure', 'prop:DataCorePlugin.GameData.FuelPressure', '0', ['188'], '42')]
    bw = (520 - 3 * 8) // 4
    for i, g in enumerate(gauges):
        e += cell(i * (bw + 8), 140, bw, 100, *g, t)
    e += cell(0, 250, 190, 100, 'POS', 'Position', 'position', '0', ['88'], '12', t)
    e += cell(200, 250, 190, 100, 'LAP', 'Lap', 'lap', '0', ['888'], '143', t)
    e += delta_cell(400, 250, 390, 100, t, title='DELTA TO BEST')
    e += lap_cell(0, 360, 390, 100, 'LAST LAP', 'Last lap', 'lastLapTime', t)
    e += cell(400, 360, 190, 100, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '14.6', t)
    e += cell(600, 360, 190, 100, 'LAPS LEFT', 'Fuel laps', 'fuelRemainingLaps', '0', ['888'], '71', t)
    e += pit_banner(t, 0, 130)
    return dash('nascar-next-gen', 'NASCAR Next Gen',
                'Stock-car gauges as numbers: big RPM and gear, water, oil temperature, oil and fuel pressure, position, '
                'lap, delta, last lap, fuel and laps of fuel. iRacing.', e)


def bmw_e30():
    amber = '#FFB000'
    num = [57, 54, 53, 55] + NUM963     # bmw fonts
    t = dict(frame='#4A3300', title='#C98A00', text=amber, accent=amber, num=num, gear=[56] + GEARBIG, radius=0, fill='#000000')
    e = []
    e += cell(0, 0, 520, 150, 'RPM', 'RPM', 'rpm', '0', ['8888'], '7400', t)
    e += gear(530, 0, 260, 250, t)
    e += cell(0, 160, 255, 90, 'KM/H', 'Speed', 'speed', '0', ['288'], '186', t, title_h=20)
    e += cell(265, 160, 255, 90, 'OIL', 'Oil', 'oilTemp', '0', ['188'], '104', t, title_h=20)
    e += lap_cell(0, 260, 390, 95, 'LAST', 'Last lap', 'lastLapTime', t, title_h=20)
    e += lap_cell(400, 260, 390, 95, 'BEST', 'Best lap', 'bestLapTime', t, title_h=20)
    e += cell(0, 365, 190, 95, 'WATER', 'Water', 'waterTemp', '0', ['188'], '86', t, title_h=20)
    e += cell(200, 365, 190, 95, 'FUEL', 'Fuel', 'fuel', '0.0', ['88.8'], '48.0', t, title_h=20)
    e += delta_cell(400, 365, 390, 95, t, title_h=20)
    e += pit_banner(t, 0, 150, 0, 520)
    return dash('bmw-m3-e30', 'BMW M3 E30',
                'Eighties amber, in the wheel\'s BMW fonts: big RPM and gear, speed and oil, last and best laps, water, '
                'fuel and delta. Assetto Corsa, rFactor 2, iRacing.', e)


ALL = [mx5_cup, gr86, porsche_992_cup, porsche_992_gt3r, amg_gt3, ferrari_296_gt3, indycar, dallara_f3, nascar, bmw_e30]

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for make in ALL:
        d = make()
        for el in d['Elements']:
            assert el['X'] >= 0 and el['Y'] >= 0 and el['X'] + el['W'] <= W and el['Y'] + el['H'] <= H, (d['Id'], el.get('Name'))
        p = os.path.join(OUT, d['Id'] + '.json')
        with open(p, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(d, f, indent=2)
        print('wrote', p, len(d['Elements']), 'elements')
