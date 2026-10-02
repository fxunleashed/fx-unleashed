"""APEX: an original hot-lapping dash for the FX Pro (790 x 460 drawable, padding 10/20).

Design rules (the /create-dash skill): every value sits on a flat colour (one command per redraw, fast with or without
the screen's RAM drive); gradients, bevels and glows only under fixed labels and frames (drawn once; with the RAM drive
they come from pictures in full colour); 2+ px between a font's band and any line; no overlapping boxes.
"""
import json, sys

OUT = sys.argv[1] if len(sys.argv) > 1 else 'apex.json'

# palette
BG0, BG1 = '#05070A', '#0B0F15'          # page gradient
PANEL = '#0E1218'                         # flat panel fill: values sit on this
PANEL_HI = '#151B23'
EDGE = '#232B36'
CYAN = '#22D3EE'                          # the accent
CYAN_DK = '#0E6E80'
TEXT, DIM, FAINT = '#FFFFFF', '#8C97A6', '#4A5462'
GREEN, RED, AMBER, PURPLE, BLUE = '#2EE58A', '#FF4040', '#FFB020', '#F24BFF', '#3D6BFF'

# ---------------------------------------------------------------- data
ND = 'DataCorePlugin.GameData.NewData.'
WHEEL = {'FrontLeft': '01', 'FrontRight': '02', 'RearLeft': '03', 'RearRight': '04'}


def tyre_temp(corner):
    """LMU: what LMU's own dashes show (inner layer average and carcass, halfway, Kelvin to C); other games: SimHub's."""
    w = f'GameRawData.CurrentPlayerTelemetry.mWheels{WHEEL[corner]}.'
    inner = ' + '.join(f'isnull([{w}mTireInnerLayerTemperature0{k}], 0)' for k in (1, 2, 3))
    lmu = f'((({inner}) / 3 + isnull([{w}mTireCarcassTemperature], 0)) / 2) - 273.15'
    return f"ncalc:if([DataCorePlugin.CurrentGame] = 'LMU', {lmu}, [{ND}TyreTemperature{corner}])"


def sector_time(n):
    """This lap's time for the sector (none until it's done, so every lap starts empty). S3 ends with the lap: the one
    just finished (shown for S3_SHOW seconds into the next lap, see sector_shown)."""
    if n == 3:
        return f'timespantoseconds([{ND}Sector3LastLapTime])'
    return f'timespantoseconds([{ND}Sector{n}Time])'


S3_SHOW = 8


def sector_shown(n):
    """When a sector's result shows: there's a time for it and a best lap to compare with; S3 only early in the next
    lap. (Blank otherwise: no number on a grey block.)"""
    t = f'{ND}Sector3LastLapTime' if n == 3 else f'{ND}Sector{n}Time'
    conds = [f'ncalc:isnull([{t}], 0)', f'ncalc:isnull([{ND}Sector{n}BestLapTime], 0)']
    if n == 3:
        conds.append(f'ncalc:timespantoseconds([{ND}CurrentLapTime]) < {S3_SHOW}')
    return conds


def sector_delta(n):
    """Against the same sector on the best lap (+ = slower)."""
    return f'ncalc:{sector_time(n)} - timespantoseconds([{ND}Sector{n}BestLapTime])'


def sector_colour(n, purple, green, red):
    """Purple: the best single sector yet (or equal); green: faster than on the best lap; red: slower."""
    t = sector_time(n)
    return (f"ncalc:if({t} <= timespantoseconds([{ND}Sector{n}BestTime]) + 0.0005, '{purple}', "
            f"if({t} < timespantoseconds([{ND}Sector{n}BestLapTime]), '{green}', '{red}'))")


def sector_live(n):
    """Live delta in sector n: the lap's live delta less what this lap's earlier sectors gained or lost against the
    best lap's (so it starts near 0 at each split)."""
    d = '[PersistantTrackerPlugin.SessionBestLiveDeltaSeconds]'
    done = ''.join(f' - (timespantoseconds([{ND}Sector{k}Time]) - timespantoseconds([{ND}Sector{k}BestLapTime]))'
                   for k in range(1, n))
    return f'ncalc:{d}{done}'


def sector_live_shown(n):
    """While in sector n with a best lap to compare with and this lap's earlier sectors known."""
    conds = [f'ncalc:[{ND}CurrentSectorIndex] = {n}', f'ncalc:isnull([{ND}Sector{n}BestLapTime], 0)']
    conds += [f'ncalc:isnull([{ND}Sector{k}Time], 0)' for k in range(1, n)]
    return conds


def by_sign(expr, minus, plus, even):
    """Gaining / losing; even (grey) within 5 ms of 0, right after a split."""
    x = expr[6:]
    return f"ncalc:if({x} < -0.005, '{minus}', if({x} > 0.005, '{plus}', '{even}'))"


def sector_noref_shown(n):
    """A time for sector n but nothing to compare it with yet (first lap of a session). In SimHub's NCalc a missing
    value passes through as null (no error): isnull(timespantoseconds(null), -1) = -1 is true just then (checked live
    through /api/eval, 2026-10-01)."""
    t = f'{ND}Sector3LastLapTime' if n == 3 else f'{ND}Sector{n}Time'
    conds = [f'ncalc:isnull([{t}], 0)', f'ncalc:isnull(timespantoseconds([{ND}Sector{n}BestLapTime]), -1) = -1']
    if n == 3:
        conds.append(f'ncalc:timespantoseconds([{ND}CurrentLapTime]) < {S3_SHOW}')
    return conds

E = []


def add(**k):
    E.append(k)
    return k


def panel(name, x, y, w, h, title=None, accent=CYAN):
    """A panel: a soft outer glow line, a bevelled edge, a flat inside, a gradient title strip with a label."""
    add(Type='box', Name=name + ' edge', X=x, Y=y, W=w, H=h, Color=EDGE, Fill=PANEL, Border=2, Radius=10)
    if title:
        add(Type='gradient', Name=name + ' title', X=x + 2, Y=y + 2, W=w - 4, H=22, Colors=[PANEL_HI, PANEL], Angle=90, Radius=8)
        add(Type='rect', Name=name + ' accent', X=x + 12, Y=y + 2, W=26, H=3, Color=accent)
        add(Type='label', Name=name + ' label', X=x + 12, Y=y + 6, W=w - 24, H=16, Text=title, Font=10, Color=DIM)


def label(name, x, y, w, text, color=DIM, align='left', font=10, h=16):
    add(Type='label', Name=name, X=x, Y=y, W=w, H=h, Text=text, Font=font, Color=color, Align=align)


def value(name, bind, x, y, w, h, font, samples, preview, fmt='0', color=TEXT, align='left', empty='-', **more):
    e = dict(Type='value', Name=name, Bind=bind, X=x, Y=y, W=w, H=h, Font=font, Format=fmt, Color=color, Align=align,
             Samples=samples, PreviewText=preview, Empty=empty)
    e.update(more)
    return add(**e)


# ---------------------------------------------------------------- page
add(Type='gradient', Name='page', X=0, Y=0, W=790, H=460, Colors=[BG1, BG0], Angle=90)

# ---------------------------------------------------------------- left: timing
LX, LY, LW, LH = 0, 0, 262, 376
panel('timing', LX, LY, LW, LH, 'DELTA TO BEST')
value('delta', 'delta', LX + 10, LY + 34, LW - 20, 78, 92, ['+88.88', '-88.88'], '-0.214', fmt='delta', align='center',
      PositiveColor=RED, NegativeColor=GREEN, empty='-.--')
add(Type='deltabar', Name='delta bar', Bind='delta', X=LX + 14, Y=LY + 120, W=LW - 28, H=12, Segments=8,
    Pitch=(LW - 28 + 3) / 16, SegmentWidth=int((LW - 28 + 3) / 16) - 3, Range=1.0,
    PositiveColor=RED, NegativeColor=GREEN, SegmentColor='#1C232D')
add(Type='rect', Name='timing div 1', X=LX + 12, Y=LY + 146, W=LW - 24, H=1, Color=EDGE)
label('predicted label', LX + 14, LY + 156, 150, 'PREDICTED')
value('predicted', 'predictedLap', LX + 10, LY + 176, LW - 20, 54, 100, ['8:88.888'], '1:31.688', fmt='laptime',
      color=CYAN, align='center', empty='-:--.---')
add(Type='rect', Name='timing div 2', X=LX + 12, Y=LY + 238, W=LW - 24, H=1, Color=EDGE)
label('last label', LX + 14, LY + 256, 70, 'LAST')
value('last', 'lastLapTime', LX + 80, LY + 246, LW - 90, 36, 101, ['8:88.888'], '1:32.481', fmt='laptime', align='right',
      empty='-:--.---')
label('best label', LX + 14, LY + 300, 70, 'BEST', color=PURPLE)
value('best', 'bestLapTime', LX + 80, LY + 290, LW - 90, 36, 101, ['8:88.888'], '1:31.902', fmt='laptime', align='right',
      color=PURPLE, empty='-:--.---')
label('lap label', LX + 14, LY + 344, 70, 'LAP')
value('lap', 'lap', LX + 80, LY + 340, LW - 90, 24, 60, ['888'], '12', fmt='0', align='right', color=DIM)

# ---------------------------------------------------------------- centre: gear, speed, lap time
CX, CY, CW, CH = 270, 0, 250, 376
add(Type='gradient', Name='gear rim', X=CX, Y=CY, W=CW, H=238, Colors=[CYAN, CYAN_DK, '#06303A'], Angle=90, Radius=14)
add(Type='box', Name='gear well', X=CX + 4, Y=CY + 4, W=CW - 8, H=230, Color='#0A0D12', Fill='#0A0D12', Border=0, Radius=11)
add(Type='gradient', Name='gear sheen', X=CX + 4, Y=CY + 4, W=CW - 8, H=26, Colors=['#16202A', '#0A0D12'], Angle=90, Radius=11)
value('gear', 'gearText', CX + 40, CY + 40, CW - 80, 150, 102, ['8', 'N', 'R'], '4', fmt='gear', align='center', empty='N')
label('gear tag', CX + 4, CY + 204, CW - 8, 'GEAR', color=FAINT, align='center')
panel('speed', CX, CY + 246, CW, CH - 246)
value('speed', 'speed', CX + 10, CY + 254, 150, 54, 100, ['388'], '214', fmt='0', align='right')
label('speed unit', CX + 164, CY + 282, 76, 'KM/H', color=DIM)
add(Type='rect', Name='speed div', X=CX + 12, Y=CY + 314, W=CW - 24, H=1, Color=EDGE)
value('current lap', 'currentLapTime', CX + 10, CY + 320, CW - 20, 52, 100, ['8:88.8'], '0:47.1', fmt=r'time:m\:ss\.f',
      align='center', empty='-:--.-')

# ---------------------------------------------------------------- right: sectors + tyres
RX, RY, RW = 528, 0, 262
panel('sectors', RX, RY, RW, 180, 'SECTORS VS BEST')
for i in range(3):
    y = RY + 32 + i * 48
    add(Type='rect', Name=f'S{i + 1} now', X=RX + 8, Y=y + 4, W=4, H=30, Color=CYAN,
        Visible=f'ncalc:[DataCorePlugin.GameData.NewData.CurrentSectorIndex] = {i + 1}')
    label(f'S{i + 1} label', RX + 20, y + 10, 48, f'S{i + 1}', color=TEXT, font=14, h=20)
    expr, shown = sector_delta(i + 1), sector_shown(i + 1)
    value(f'S{i + 1} delta', expr, RX + 70, y + 2, RW - 82, 36, 101, ['+88.888', '-88.888'], ['-0.084', '+0.131', '-0.022'][i],
          fmt='+0.000;-0.000;0.000', align='right', empty='-.---',
          ColorBind=sector_colour(i + 1, PURPLE, GREEN, RED), **({'Visible': shown} if shown else {}))
    value(f'S{i + 1} time', f'ncalc:{sector_time(i + 1)}', RX + 70, y + 2, RW - 82, 36, 101, ['188.888'], '38.653', fmt='0.000',
          align='right', color=TEXT, empty='', Visible=sector_noref_shown(i + 1), PreviewVisible=False)

TY = RY + 188
panel('tyres', RX, TY, RW, 188, 'TYRES  C / PSI')
corners = [('FL', 'FrontLeft'), ('FR', 'FrontRight'), ('RL', 'RearLeft'), ('RR', 'RearRight')]
cw, ch = (RW - 30) // 2, 76
for k, (short, prop) in enumerate(corners):
    x = RX + 10 + (k % 2) * (cw + 10)
    y = TY + 28 + (k // 2) * (ch + 6)
    add(Type='box', Name=f'{short} cell', X=x, Y=y, W=cw, H=ch, Color=EDGE, Fill=PANEL_HI, Border=1, Radius=6)
    t = tyre_temp(prop)
    value(f'{short} temp', t, x + 6, y + 6, cw - 12, 38, 101, ['188'], '86', fmt='0', align='center', empty='--',
          ColorBind=t, ColorStops=[{'Value': 60, 'Color': BLUE}, {'Value': 75, 'Color': CYAN}, {'Value': 85, 'Color': GREEN},
                                   {'Value': 98, 'Color': GREEN}, {'Value': 108, 'Color': RED}])
    value(f'{short} psi', f'prop:DataCorePlugin.GameData.NewData.TyrePressure{prop}', x + 6, y + 50, cw - 12, 18, 12,
          ['88.8'], '26.5', fmt='0.0', align='center', color=DIM, empty='--')

# ---------------------------------------------------------------- bottom: settings
BY, BH = 384, 76
items = [('TC', 'tcLevel', '0', ['88'], '4', CYAN), ('ABS', 'absLevel', '0', ['88'], '3', AMBER),
         ('BIAS', 'brakeBias', '0.0', ['88.8'], '55.5', TEXT), ('MAP', 'engineMap', '0', ['88'], '2', TEXT),
         ('FUEL LAPS', 'fuelRemainingLaps', '0.0', ['888.8'], '14.2', GREEN)]
bw = (790 - 4 * 8) // 5
for i, (name, bind, fmt, samples, preview, col) in enumerate(items):
    x = i * (bw + 8)
    panel(name.lower(), x, BY, bw, BH)
    add(Type='rect', Name=f'{name} accent', X=x + 12, Y=BY + 2, W=26, H=3, Color=col)
    label(f'{name} label', x + 8, BY + 8, bw - 12, name)
    value(name.lower() + ' value', bind, x + 10, BY + 26, bw - 20, 46, 97, samples, preview, fmt=fmt, align='right', color=col)

# ---------------------------------------------------------------- overlays (last: on top)
for i in range(3):
    y = RY + 32 + i * 48
    live, shown = sector_live(i + 1), sector_live_shown(i + 1)
    value(f'S{i + 1} live', live, RX + 70, y + 2, RW - 82, 36, 101, ['+88.88'], '-0.12', fmt='+0.00;-0.00;0.00',
          align='right', color=GREEN, empty='', ColorBind=by_sign(live, GREEN, RED, '#C8CDD4'), Visible=shown, PreviewVisible=False)

inv = 'lapInvalid'
add(Type='box', Name='invalid', X=CX, Y=CY + 246, W=CW, H=CH - 246, Color=RED, Fill='#3A0A0E', Border=2, Radius=10,
    Visible=inv, PreviewVisible=False)
label('invalid text', CX + 10, CY + 270, CW - 20, 'LAP INVALID', color=RED, align='center', font=98, h=44)
E[-1].update(Visible=inv, PreviewVisible=False)
label('invalid sub', CX + 10, CY + 324, CW - 20, 'NOT COUNTED', color='#FF9A9A', align='center')
E[-1].update(Visible=inv, PreviewVisible=False)

# a new best lap: purple over the last / best / lap rows for 5 s
pb = 'ncalc:changed(5000, [BestLapTime])'
add(Type='box', Name='new best', X=LX + 6, Y=LY + 244, W=LW - 12, H=LH - 250, Color=PURPLE, Fill='#3A0C3E', Border=2, Radius=8,
    Visible=pb, PreviewVisible=False)
label('new best text', LX + 12, LY + 258, LW - 24, 'NEW BEST LAP', color=PURPLE, align='center', font=14, h=22)
E[-1].update(Visible=pb, PreviewVisible=False)
value('new best time', 'bestLapTime', LX + 12, LY + 290, LW - 24, 54, 100, ['8:88.888'], '1:31.902', fmt='laptime', align='center',
      color=TEXT, empty='-:--.---', Visible=pb, PreviewVisible=False)

pit = 'pitLimiter'
add(Type='box', Name='pit', X=CX, Y=CY, W=CW, H=CH, Color=BLUE, Fill='#0A1640', Border=3, Radius=14, Visible=pit, PreviewVisible=False)
label('pit text', CX + 10, CY + 60, CW - 20, 'PIT LIMITER', color=TEXT, align='center', font=98, h=44)
E[-1].update(Visible=pit, PreviewVisible=False)
value('pit speed', 'speed', CX + 10, CY + 140, CW - 20, 72, 99, ['388'], '60', fmt='0', align='center', color=TEXT,
      Visible=pit, PreviewVisible=False)
label('pit unit', CX + 10, CY + 220, CW - 20, 'KM/H', color='#9DB4FF', align='center')
E[-1].update(Visible=pit, PreviewVisible=False)

dash = dict(FormatVersion=2, Id='fx-apex', Name='APEX Hot Lap', Author='FX Unleashed',
            Description='Original hot-lapping dash: delta to best with delta bar, predicted lap, live sector splits '
                        'against your best lap, tyre temperatures and pressures, settings at a glance.',
            Elements=E)
json.dump(dash, open(OUT, 'w', encoding='utf-8'), indent=1)
print(f'{len(E)} elements -> {OUT}')
