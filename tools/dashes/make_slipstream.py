"""SLIPSTREAM: an original hot-lapping dash for the FX Pro (790 x 460 drawable, padding 10/20).

A livery look: one painted background picture (slanted graphite plates, a red edge with a glow, the strips), the gear
huge on its plate, a hero delta, the timing on slanted plates, sector splits and tyre temperatures as solid colour
blocks. Values only ever sit on flat colour (a plate's inside, a block): one command per redraw, with or without the
screen's RAM drive. The picture: with the RAM drive it's shown in full colour; without, as rectangles of MaxColors.
"""
import base64, io, json, sys
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else 'slipstream.json'
W, H, SS = 790, 460, 3

BLACK, PLATE, PLATE2, STRIP = (0, 0, 0), (13, 14, 17), (21, 23, 28), (13, 14, 17)
RED = (255, 42, 42)
TEXT, DIM, FAINT = '#FFFFFF', '#8A919C', '#4C525C'
GREEN, REDC, PURPLE, BLUE, AMBER = '#2EE58A', '#FF3B3B', '#F24BFF', '#3D7BFF', '#FFB020'
HEX = lambda c: '#%02X%02X%02X' % c

# ---------------------------------------------------------------- geometry (dash pixels)
GEAR_PLATE = [(50, 8), (300, 8), (258, 296), (8, 296)]  # a parallelogram fully on the screen (one off the edge left a notch and a fringe)
RX, RW = 320, 470
PW, PG, SLANT = 152, 7, 8                        # timing plates
PLATE_Y0, PLATE_Y1 = 156, 226
STRIP_Y = 404


def plate_poly(i):
    x0 = RX + i * (PW + PG)
    return [(x0 + SLANT, PLATE_Y0), (x0 + PW, PLATE_Y0), (x0 + PW - SLANT, PLATE_Y1), (x0, PLATE_Y1)]


# ---------------------------------------------------------------- the art
def paint():
    img = Image.new('RGB', (W * SS, H * SS), BLACK)
    S = lambda pts: [(x * SS, y * SS) for x, y in pts]
    # a red glow along the gear plate's slanted edge (drawn first, blurred, then the plate over it)
    glow = Image.new('RGB', img.size, BLACK)
    g = ImageDraw.Draw(glow)
    g.line(S([(300, 8), (258, 296)]), fill=(200, 20, 30), width=8 * SS)
    glow = glow.filter(ImageFilter.GaussianBlur(5 * SS))
    img = Image.blend(img, glow, 0.85)
    d = ImageDraw.Draw(img)
    # the gear plate: flat inside, a red edge on the slant, a thin light line along the top
    d.polygon(S(GEAR_PLATE), fill=PLATE)
    d.line(S([(300, 8), (258, 296)]), fill=RED, width=4 * SS)
    d.line(S([(50, 8), (300, 8)]), fill=(60, 64, 72), width=1 * SS)
    # timing plates: slanted, a red tick on each
    for i in range(3):
        d.polygon(S(plate_poly(i)), fill=PLATE2)
        x0 = RX + i * (PW + PG)
        d.polygon(S([(x0 + SLANT + 4, PLATE_Y0), (x0 + SLANT + 30, PLATE_Y0), (x0 + SLANT + 29, PLATE_Y0 + 3), (x0 + SLANT + 3, PLATE_Y0 + 3)]), fill=RED)
    # delta zone: a hairline under the tape
    d.line(S([(RX, 152), (RX + RW, 152)]), fill=(34, 37, 43), width=1 * SS)
    # the bottom strip: graphite with a red top line and a red chevron at the end
    d.polygon(S([(14, STRIP_Y), (W, STRIP_Y), (W, H), (0, H), (0, STRIP_Y + 14)]), fill=STRIP)
    d.line(S([(14, STRIP_Y), (W, STRIP_Y)]), fill=RED, width=2 * SS)
    d.line(S([(0, STRIP_Y + 14), (14, STRIP_Y)]), fill=RED, width=2 * SS)
    for k in range(3):
        x = 700 + k * 22
        d.polygon(S([(x, 300), (x + 12, 300), (x + 24, 312), (x + 12, 324), (x, 324), (x + 12, 312)]), fill=(70 + 60 * k, 10, 14)) if False else None
    img = img.resize((W, H), Image.LANCZOS)
    # flat insides stay exactly flat (the resize can leave a level off): repaint them unscaled, inset from the edges
    d = ImageDraw.Draw(img)
    d.polygon([(54, 12), (294, 12), (255, 292), (15, 292)], fill=PLATE)
    for i in range(3):
        x0 = RX + i * (PW + PG)
        d.polygon([(x0 + SLANT + 3, PLATE_Y0 + 5), (x0 + PW - 3, PLATE_Y0 + 5), (x0 + PW - SLANT - 3, PLATE_Y1 - 3), (x0 + 3, PLATE_Y1 - 3)], fill=PLATE2)
    d.rectangle([4, STRIP_Y + 18, W - 1, H - 1], fill=STRIP)
    d.rectangle([RX, 0, W - 1, 150], fill=BLACK)
    d.line([(RX, 152), (RX + RW, 152)], fill=(34, 37, 43), width=1)
    return img


art = paint()
buf = io.BytesIO()
art.save(buf, 'PNG', optimize=True)
art.save(OUT.replace('.json', '_art.png'))
IMG = f'slipstream-art@{W}x{H}'

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


def label(name, x, y, w, text, color=DIM, align='left', font=10, h=16, **more):
    return add(Type='label', Name=name, X=x, Y=y, W=w, H=h, Text=text, Font=font, Color=color, Align=align, **more)


def value(name, bind, x, y, w, h, font, samples, preview, fmt='0', color=TEXT, align='left', empty='-', **more):
    e = dict(Type='value', Name=name, Bind=bind, X=x, Y=y, W=w, H=h, Font=font, Format=fmt, Color=color, Align=align,
             Samples=samples, PreviewText=preview, Empty=empty)
    e.update(more)
    return add(**e)


add(Type='image', Name='art', X=0, Y=0, W=W, H=H, Image=IMG, MaxColors=16)

# ---------------------------------------------------------------- left: gear, lap, speed, lap time
label('lap label', 66, 18, 52, 'LAP', color=FAINT)
value('lap', 'lap', 122, 16, 60, 20, 14, ['888'], '12', color=DIM)
value('gear', 'gearText', 58, 42, 190, 220, 18, ['8', 'N', 'R'], '4', fmt='gear', align='center', empty='N')
value('speed', 'speed', 14, 300, 150, 54, 100, ['388'], '214', align='right')
label('speed unit', 172, 326, 80, 'KM/H', color=DIM)
value('current lap', 'currentLapTime', 14, 356, 280, 44, 98, ['8:88.8'], '0:47.1', fmt=r'time:m\:ss\.f', align='left',
      color=DIM, empty='-:--.-')

# ---------------------------------------------------------------- right: delta hero + tape
label('delta label', RX + 6, 6, 120, 'DELTA', color=FAINT)
value('delta', 'delta', RX + 10, 20, RW - 10, 116, 47, ['+88.88', '-88.88'], '-0.214', fmt='delta', align='right',
      PositiveColor=REDC, NegativeColor=GREEN, empty='-.--', Background='#000000')
seg, pitch = 12, RW / 24
add(Type='deltabar', Name='delta tape', Bind='delta', X=RX, Y=136, W=RW, H=10, Segments=seg, Pitch=pitch,
    SegmentWidth=int(pitch) - 4, Range=1.0, PositiveColor=REDC, NegativeColor=GREEN, SegmentColor='#1A1D22')

# timing plates
for i, (name, bind, col, prev) in enumerate([('PRED', 'predictedLap', TEXT, '1:31.688'), ('LAST', 'lastLapTime', TEXT, '1:32.481'),
                                              ('BEST', 'bestLapTime', PURPLE, '1:31.902')]):
    x0 = RX + i * (PW + PG)
    label(f'{name} label', x0 + 40, PLATE_Y0 + 6, 80, name, color=DIM if name != 'BEST' else PURPLE)
    value(name.lower(), bind, x0 + 9, PLATE_Y0 + 26, PW - 20, 36, 101, ['8:88.888'], prev, fmt='laptime', align='center',
          color=col, empty='-:--.---')

# sector blocks
SY = 238
for i in range(3):
    x0 = RX + i * (PW + PG)
    label(f'S{i + 1} label', x0, SY, 60, f'S{i + 1}', color=DIM)
    add(Type='rect', Name=f'S{i + 1} now', X=x0, Y=SY + 70, W=PW, H=3, Color='#FFFFFF',
        Visible=f'ncalc:[DataCorePlugin.GameData.NewData.CurrentSectorIndex] = {i + 1}')
    expr, shown = sector_delta(i + 1), sector_shown(i + 1)
    colour = sector_colour(i + 1, PURPLE, GREEN, REDC)
    if i == 2:  # S3: grey again once its result is no longer shown
        colour = f"ncalc:if({shown[-1][6:]}, {colour[6:]}, '#22252B')"
    add(Type='rect', Name=f'S{i + 1} block', X=x0, Y=SY + 16, W=PW, H=52, Color='#22252B', ColorBind=colour)
    value(f'S{i + 1} delta', expr, x0 + 6, SY + 20, PW - 12, 44, 98, ['+88.888'], ['-0.084', '+0.131', '-0.022'][i],
          fmt='+0.000;-0.000;0.000', align='center', color='#000000', empty='', **({'Visible': shown} if shown else {}))
    value(f'S{i + 1} time', f'ncalc:{sector_time(i + 1)}', x0 + 6, SY + 20, PW - 12, 44, 98, ['188.888'], '38.653', fmt='0.000',
          align='center', color='#E6E9EE', empty='', Visible=sector_noref_shown(i + 1), PreviewVisible=False)

# tyre heat map
TY = 314
corners = [('FL', 'FrontLeft'), ('FR', 'FrontRight'), ('RL', 'RearLeft'), ('RR', 'RearRight')]
tw, tg = 110, 10
for k, (short, prop) in enumerate(corners):
    x = RX + k * (tw + tg)
    label(f'{short} label', x, TY, 40, short, color=DIM)
    t = tyre_temp(prop)
    # bands, not a blend: a block repaints only when its tyre crosses into another band (a blend repainted it with
    # every degree: 21 KB in the worst second)
    def band(lo, hi, c): return [{'Value': lo, 'Color': c}, {'Value': hi, 'Color': c}]
    stops = band(0, 69.9, BLUE) + band(70, 79.9, '#22D3EE') + band(80, 99.9, GREEN) + band(100, 109.9, AMBER) + band(110, 300, REDC)
    add(Type='rect', Name=f'{short} block', X=x, Y=TY + 18, W=tw, H=68, Color='#22252B', ColorBind=t, ColorStops=stops)
    value(f'{short} temp', t, x + 4, TY + 20, tw - 8, 42, 98, ['188'], '86', align='center', color='#000000', empty='--')
    value(f'{short} psi', f'prop:DataCorePlugin.GameData.NewData.TyrePressure{prop}', x + 4, TY + 64, tw - 8, 18, 10,
          ['88.8'], '26.5', fmt='0.0', align='center', color='#000000', empty='')

# ---------------------------------------------------------------- bottom strip: settings
items = [('TC', 'tcLevel', '0', ['88'], '4', '#22D3EE'), ('ABS', 'absLevel', '0', ['88'], '3', AMBER),
         ('BB', 'brakeBias', '0.0', ['88.8'], '55.5', TEXT), ('MAP', 'engineMap', '0', ['88'], '2', TEXT),
         ('FUEL', 'fuelRemainingLaps', '0.0', ['88.8'], '14.2', GREEN)]
widths = [130, 150, 160, 150, 200]
x = 0
for (name, bind, fmt, samples, preview, col), iw in zip(items, widths):
    lw = 20 * len(name) + 4
    label(f'{name} label', x + 16, STRIP_Y + 22, lw, name, color=DIM)
    value(name.lower() + ' value', bind, x + 16 + lw + 6, STRIP_Y + 8, iw - lw - 34, 46, 97, samples, preview, fmt=fmt,
          align='right', color=col)
    x += iw

# ---------------------------------------------------------------- overlays (last: on top)
for i in range(3):
    x0 = RX + i * (PW + PG)
    live, shown = sector_live(i + 1), sector_live_shown(i + 1)
    add(Type='rect', Name=f'S{i + 1} live block', X=x0, Y=SY + 16, W=PW, H=52, Color='#14171C',
        ColorBind=by_sign(live, '#0B2A1B', '#2E0B0E', '#1A1D22'), Visible=shown, PreviewVisible=False)
    value(f'S{i + 1} live', live, x0 + 6, SY + 20, PW - 12, 44, 98, ['+88.88'], '-0.12', fmt='+0.00;-0.00;0.00',
          align='center', color=GREEN, empty='', ColorBind=by_sign(live, GREEN, REDC, '#C8CDD4'), Visible=shown, PreviewVisible=False)

inv = 'lapInvalid'
add(Type='box', Name='invalid', X=RX, Y=0, W=RW, H=150, Color=REDC, Fill='#3A0A0E', Border=2, Radius=6, Visible=inv, PreviewVisible=False)
label('invalid text', RX + 10, 34, RW - 20, 'LAP INVALID', color=REDC, align='center', font=99, h=68, Visible=inv, PreviewVisible=False)
label('invalid sub', RX + 10, 108, RW - 20, 'THIS LAP WON\'T COUNT', color='#FF9A9A', align='center', Visible=inv, PreviewVisible=False)

pb = 'ncalc:changed(5000, [BestLapTime])'
add(Type='box', Name='new best', X=RX, Y=PLATE_Y0 - 2, W=RW, H=PLATE_Y1 - PLATE_Y0 + 4, Color=PURPLE, Fill='#3A0C3E', Border=2,
    Radius=6, Visible=pb, PreviewVisible=False)
label('new best text', RX + 14, PLATE_Y0 + 26, 200, 'NEW BEST', color=PURPLE, font=98, h=44, Visible=pb, PreviewVisible=False)
value('new best time', 'bestLapTime', RX + 220, PLATE_Y0 + 14, RW - 234, 54, 100, ['8:88.888'], '1:31.902', fmt='laptime',
      align='right', empty='-:--.---', Visible=pb, PreviewVisible=False, Background='#3A0C3E')

pit = 'pitLimiter'
add(Type='box', Name='pit', X=0, Y=0, W=310, H=STRIP_Y - 4, Color=BLUE, Fill='#0A1640', Border=3, Radius=10, Visible=pit, PreviewVisible=False)
label('pit text', 10, 70, 290, 'PIT LIMITER', color=TEXT, align='center', font=98, h=44, Visible=pit, PreviewVisible=False)
value('pit speed', 'speed', 10, 140, 290, 104, 35, ['388'], '60', align='center', Visible=pit, PreviewVisible=False,
      Background='#0A1640')
label('pit unit', 10, 250, 290, 'KM/H', color='#9DB4FF', align='center', Visible=pit, PreviewVisible=False)

dash = dict(FormatVersion=2, Id='fx-slipstream', Name='SLIPSTREAM', Author='FX Unleashed',
            Description='Original hot-lapping dash in a livery look: huge gear, hero delta with tape, predicted/last/best, '
                        'sector splits and tyre temperatures as colour blocks, settings strip.',
            Elements=E, Images={IMG: base64.b64encode(buf.getvalue()).decode('ascii')})
json.dump(dash, open(OUT, 'w', encoding='utf-8'), indent=1)
print(f'{len(E)} elements, art {len(buf.getvalue()) // 1024} KB -> {OUT}')
