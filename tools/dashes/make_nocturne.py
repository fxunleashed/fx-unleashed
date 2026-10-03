"""NOCTURNE: an endurance-race dash for the FX Pro (790 x 460 drawable, padding 10/20), made for a screen with the RAM drive.

The look: a neon-green night cockpit, modelled in 3D and rendered. The background is a Cycles render (Blender, see
nocturne_scene.py) of real geometry lit from the top left: bevelled slabs for the panels, header and deck; a circular lens
with a machined bezel, a glowing neon tube and sloped walls stepping down into a deep pocket for the gear; a raised badge
across its top for the delta and its tape; glass cylinders for fuel and energy (their liquid drawn live as three bars side
by side: highlight, body, shade); raised pads for the timing tower (two cars ahead, you, two behind, each with its class
colour). Shadows, bounce light from the neon, reflections in the metal and the glow are all real light, not painted.

The render is shown from tiles on the RAM drive. The values are drawn by the screen on top, each in a flat well (the colour
of the surface there, sampled from the render) so a redraw is one command. Labels are painted into the picture in Chakra
Petch (OFL), not drawn by the screen's own wide fonts. Made for endurance: laps of fuel, burn per lap, stint laps, class
position, gaps to the cars around you, session time left, flags, traffic.

Needs Blender 4.x (BLENDER env var, default the standard install path) and a GPU for a quick render (Cycles falls back to
the CPU). The render is cached by a hash of the scene, so changing only the elements or labels doesn't re-render.
"""
import base64, hashlib, io, json, os, subprocess, sys, tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else 'nocturne.json'
W, H = 790, 460
FONT_DIR = r'C:\Windows\Fonts'
BLENDER = os.environ.get('BLENDER', r'C:\Program Files\Blender Foundation\Blender 4.3\blender.exe')
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.environ.get('NOC_BUILD', os.path.join(tempfile.gettempdir(), 'nocturne_build'))
os.makedirs(BUILD, exist_ok=True)


def rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


# ---------------------------------------------------------------- palette: near-black graphite with a tint, one neon accent
# NOC_THEME picks the colourway. The accent is the light: rings, rims, glows, the turbine's gaps; the hub, badge and the rest
# of the surfaces take its tint. Red is the plugin's own (Ui/Theme.cs). Data colours (fuel amber, delta red/green, purple best,
# flags, tyre heat) keep their meaning in every theme. `mult` scales every glowing part: red and blue are dimmer than green at
# the same strength, and the glow (compositor bloom) works on luminance.
THEMES = {
    'green': dict(
        id='fx-nocturne', name='NOCTURNE Endurance', neon=(57, 255, 120), neon_ui='#39FF78', mult=1.0, glare=2.5,
        text='#EEF7F0', dim='#8FA89A', faint='#56685E', accent2='#7FE0A6', pit_title='#C8FFDA',
        day=('#2BD99A', '#60E2A8', '#C8FFDA', '#6EE6A8'),
        hub=(13, 78, 46), hub_edge=(46, 190, 104), badge=(7, 34, 20), well=(3, 6, 4), tube='#040806',
        faces=dict(panel=[7, 10, 8], cell=[7, 9, 8], header=[8, 10, 9], pad=[6, 9, 7], pad_me=[6, 26, 14], chip=[7, 10, 8],
                   blade=[74, 82, 77], metal=[200, 210, 204], anod=[150, 170, 158]),
        floor=((4, 7, 5), (2, 4, 3)), floor_glow=(6, 30, 15), dome=[0.035, 0.05, 0.04], softbox=[150, 255, 190]),
    'red': dict(
        id='fx-nocturne-red', name='NOCTURNE Red', neon=(255, 31, 45), neon_ui='#FF1F2D', mult=2.2, glare=2.5,
        text='#F6EEEF', dim='#A58E90', faint='#68565A', accent2='#FF8E96', pit_title='#FFD4D6',
        day=('#FF3B4A', '#FF7A84', '#FFD4D6', '#FF8E96'),
        hub=(100, 12, 20), hub_edge=(205, 32, 44), badge=(38, 7, 11), well=(6, 3, 3), tube='#080404',
        faces=dict(panel=[10, 7, 7], cell=[10, 7, 7], header=[11, 8, 8], pad=[9, 6, 6], pad_me=[38, 8, 12], chip=[10, 7, 8],
                   blade=[88, 76, 76], metal=[208, 200, 200], anod=[170, 150, 152]),
        floor=((7, 4, 4), (4, 2, 3)), floor_glow=(34, 6, 8), dome=[0.045, 0.035, 0.035], softbox=[255, 150, 150]),
    'blue': dict(
        id='fx-nocturne-blue', name='NOCTURNE Blue', neon=(20, 60, 255), neon_ui='#2A6BFF', mult=1.5, glare=1.6,
        text='#EDF1FA', dim='#8E9BB8', faint='#566078', accent2='#8FB0FF', pit_title='#C6D6FF',
        day=('#3A70FF', '#6E98FF', '#C6D6FF', '#8FB0FF'),
        hub=(14, 36, 112), hub_edge=(40, 90, 255), badge=(7, 14, 42), well=(3, 4, 8), tube='#04060A',
        faces=dict(panel=[6, 8, 12], cell=[6, 8, 11], header=[7, 9, 13], pad=[5, 7, 10], pad_me=[8, 18, 52], chip=[6, 8, 12],
                   blade=[72, 78, 92], metal=[198, 205, 216], anod=[150, 162, 185]),
        floor=((4, 5, 9), (2, 3, 5)), floor_glow=(6, 12, 40), dome=[0.032, 0.038, 0.055], softbox=[150, 180, 255]),
}
THEME = os.environ.get('NOC_THEME', 'green')
T = THEMES[THEME]
TEXT, DIM, FAINT = T['text'], T['dim'], T['faint']
NEON = T['neon']                          # the light: rings, rims, glows
NEONH = T['neon_ui']
AMBER, ENERGYC = '#FFB020', '#4DFF8A'     # fuel amber; the energy gauge is green in every theme (good = green)
GREEN, RED, PURPLE = '#2EE6A0', '#FF4560', '#F24BFF'
CLASS = '#E4002B'                         # Hypercar red until the live class colour arrives
HUB = T['hub']                            # the edge-lit glass hub under the gear: emissive and flat, so exactly this colour
HUB_EDGE = T['hub_edge']                  # ...brightening toward its rim
BADGE_COL = T['badge']                    # the delta badge's face: emissive and flat
WELL = T['well']                          # the tube cores
TUBE = T['tube']

# ---------------------------------------------------------------- geometry (dash px, y down)
CX, CY = 395, 224                         # the lens
R_OUT = 150
Z_HDR, Z_PANEL, Z_TYRE, Z_CELL = 12, 16, 18, 22
BADGE = (298, 65, 492, 136)
TUBES = (33, 91)                          # centres of the two cylinders
ROWS = [86 + k * 52 for k in range(5)]    # the timing tower's rows (48 high)
DECK = [(250, 358, 540, 440), (550, 358, 668, 396), (672, 358, 790, 396), (550, 400, 668, 438), (672, 400, 790, 438)]

# the lens, outside in as (radius, height, material of the strip ending here: 0 bezel metal, 1 dark, 2 anodised, 3 bright rim
# metal). Never outward, so the strips face the right way. A machined bezel, then a recess (its floor glows) holding two
# counter-rotating turbine stages, then a bright inner rim round the glass hub that carries the gear.
LENS_BEZEL = [
    (150.0, 0.0, 0), (150.0, 16.5, 0), (148.4, 20.0, 0), (147.0, 20.0, 0), (146.0, 18.6, 0), (145.0, 20.0, 0),
    (141.8, 20.0, 0), (140.6, 18.4, 0), (139.9, 14.0, 1), (139.4, 2.5, 1),
]
LENS_RIM = [(117.0, 2.0, 1), (116.0, 9.0, 3), (113.6, 10.6, 3), (112.4, 8.6, 3), (112.2, 6.0, 1)]
R_HUB, R_HUB_FLAT = 112, 105.5
TURBINE = [
    dict(name='stage A', r0=128.0, r1=138.6, n=36, sweep=24, width=11.0, pitch=40, thick=1.4, z=6.5),
    dict(name='stage B', r0=117.6, r1=127.0, n=30, sweep=-24, width=12.0, pitch=-40, thick=1.4, z=6.5),
]


# ---------------------------------------------------------------- textures for the 3D scene
def blur(a, s):
    r = max(1, int(s * 3))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / s) ** 2).astype(np.float32)
    k /= k.sum()
    a = np.apply_along_axis(lambda v: np.convolve(v, k, 'same'), 1, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, 'same'), 0, a)


def make_floor(path):
    """The dark floor everything stands on: a tint, a glow round the lens, a few faint ripples spreading from it."""
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.hypot(xx - CX, yy - CY)
    t = (yy / H)[..., None]
    img = np.asarray(T['floor'][0], np.float32) * (1 - t) + np.asarray(T['floor'][1], np.float32) * t
    rg = np.clip(1 - np.hypot((xx - CX) / 1.25, yy - CY) / 330, 0, 1) ** 2
    img = img + rg[..., None] * np.asarray(T['floor_glow'], np.float32)
    fade = np.clip(1 - (d - R_OUT) / 110, 0, 1) ** 1.6
    for r in (R_OUT + 9, R_OUT + 24, R_OUT + 46):
        ring = np.clip(np.minimum(d - r, r + 1.4 - d) + 0.5, 0, 1)
        img = img * (1 - ring * fade * 0.5)[..., None] + np.asarray(NEON, np.float32) * (ring * fade * 0.5)[..., None]
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(path)


def make_sky(path):
    """The progress bar's track: one flat colour. (It used to be a painted sky: a gradient behind a bar that changes can't be
    restored with a few rectangles, so every change put back whole picture tiles and redrew everything along the bottom of
    the screen, ~3.5 KB/s and visible slowdowns. The bar now draws both its parts in flat colours, its colour following the day.)"""
    Image.fromarray(np.tile(np.asarray(rgb(TUBE), np.uint8), (40, 3104, 1))).save(path)


# ---------------------------------------------------------------- the 3D scene description
def scene_description(floor_png, sky_png):
    slabs, polys = [], []
    slabs.append(dict(name='panel L', rect=[-14, 64, 222, 348], r=12, z0=0, z1=Z_PANEL, mat='panel', bevel=2.4))
    slabs.append(dict(name='panel R', rect=[568, 64, 804, 348], r=12, z0=0, z1=Z_PANEL, mat='panel', bevel=2.4))
    slabs.append(dict(name='header', rect=[-14, -14, 804, 56], r=10, z0=0, z1=Z_HDR, mat='header', bevel=2.0))
    polys.append(dict(name='chip', pts=[[0, 9], [182, 9], [190, 17], [190, 40], [182, 48], [0, 48]], z0=Z_HDR, z1=Z_HDR + 3,
                      mat='chip', bevel=1.0))
    for k, y in enumerate(ROWS):                                    # the tower's rows: raised pads, yours higher
        me = k == 2
        slabs.append(dict(name=f'row {k}', rect=[576, y, 784, y + 48], r=3, z0=Z_PANEL, z1=Z_PANEL + (4.5 if me else 1.3),
                          mat='pad_me' if me else 'pad', bevel=0.7))
    for i, (x0, y0, x1, y1) in enumerate(DECK):
        slabs.append(dict(name=f'deck {i}', rect=[x0, y0, x1, y1], r=9, z0=0, z1=Z_CELL, mat='cell', bevel=2.0))
    for k in range(4):
        x = 4 + k * 60
        slabs.append(dict(name=f'tyre {k}', rect=[x - 3, 359, x + 59, 423], r=8, z0=0, z1=Z_TYRE, mat='cell', bevel=1.8))
    slabs.append(dict(name='progress frame', rect=[5, 444, 785, 462], r=6, z0=0, z1=8, mat='cell', bevel=1.2))
    ticks = []
    for i in range(72):                                             # inlaid tick marks on the bezel
        a = np.deg2rad(i * 5)
        L = 3.0 if i % 9 == 0 else 2.0
        c, s = np.cos(a), np.sin(a)
        r0 = 143.3
        ax, ay = CX + (r0 - L / 2) * c, CY + (r0 - L / 2) * s
        bx, by = CX + (r0 + L / 2) * c, CY + (r0 + L / 2) * s
        nx, ny = -s * 0.45, c * 0.45
        ticks.append([[ax + nx, ay + ny], [bx + nx, by + ny], [bx - nx, by - ny], [ax - nx, ay - ny]])
    return dict(
        W=W, H=H, ss=2, samples=int(os.environ.get('NOC_SAMPLES', '768')),
        floor_img=floor_png, floor_emission=0.22,
        materials=dict(T['faces'], neon=list(NEON), well=list(WELL), badge=list(BADGE_COL)),
        face_spec=0.0, coat=1.0,                 # the faces' own rough specular reflects the big light panels and lifts them: off
        neon_strength=9.0 * T['mult'], floor_glow_strength=2.4 * T['mult'],
        light=dict(az=135, el=38, key=1.6, soft=7, fill=0.35, dome=T['dome'],
                   softboxes=[dict(name='softbox TL', loc=[-300, 760, 480], size=[900, 460], col=[255, 250, 240], strength=9.0),
                              dict(name='softbox BR', loc=[1100, -260, 360], size=[900, 520], col=T['softbox'], strength=2.5)]),
        glare_threshold=T['glare'], glare_size=6, glare_mix=-0.5, strip_strength=2.2 * T['mult'],
        slabs=slabs, polys=polys, ticks=ticks, tick_z=20.06,
        strips=[dict(name='header light', rect=[0, 55.0, 790, 56.2], r=0.5, z0=Z_HDR - 0.6, z1=Z_HDR + 0.5)],
        lens=dict(cx=CX, cy=CY, bezel=LENS_BEZEL, rim=LENS_RIM, floor=dict(r0=117.0, r1=139.4, z=2.0),
                  hub=dict(r=R_HUB, r_flat=R_HUB_FLAT, z=6.0, col=list(HUB), edge_col=list(HUB_EDGE), edge_boost=0.8),
                  neon=dict(r=140.3, minor=1.1, z=18.2), turbine=TURBINE, screw_r=145.9, screw_z=20.7),
        badge=dict(rect=list(BADGE), r=12, z0=16, z1=30, edge=1.8),
        tubes=[dict(cx=cx, y0=94, y1=292, r=22, core_r=14, z=8, caps=[[86, 94], [292, 300]]) for cx in TUBES],
        sky=dict(rect=[7, 447, 783, 457], z=8.3, img=sky_png),
    )


def render_scene():
    floor_png, sky_png = os.path.join(BUILD, 'floor.png'), os.path.join(BUILD, 'sky.png')
    make_floor(floor_png)
    make_sky(sky_png)
    scene = scene_description(floor_png, sky_png)
    blob = json.dumps(scene, sort_keys=True)
    blob += open(os.path.join(HERE, 'nocturne_scene.py'), encoding='utf-8').read()
    blob += hashlib.sha1(open(floor_png, 'rb').read() + open(sky_png, 'rb').read()).hexdigest()
    key = hashlib.sha1(blob.encode()).hexdigest()[:12]
    out = os.path.join(BUILD, f'render_{key}.png')
    if not os.path.exists(out):
        sj = os.path.join(BUILD, 'scene.json')
        json.dump(scene, open(sj, 'w'))
        print('rendering the 3D scene with Blender ...')
        r = subprocess.run([BLENDER, '-b', '-P', os.path.join(HERE, 'nocturne_scene.py'), '--', sj, out],
                           capture_output=True, text=True)
        if r.returncode != 0 or not os.path.exists(out):
            print(r.stdout[-3000:], r.stderr[-3000:])
            raise SystemExit('Blender failed')
    return Image.open(out).convert('RGB').resize((W, H), Image.LANCZOS)


img = render_scene()

# ---------------------------------------------------------------- 2D touches on the render: the scale between the tubes,
# the wear bars' tracks
dr = ImageDraw.Draw(img)
for i in range(11):                                                  # ticks: every 10 %, long at 50 %
    y = 288 - i * 19
    wd = 9 if i in (0, 5, 10) else 5
    dr.rectangle([int(62 - wd / 2 + 1), y - 1, int(62 + wd / 2 + 1) - 1, y - 1 + (1 if wd == 9 else 0)], fill=(190, 214, 198))
for k in range(4):
    x = 4 + k * 60
    dr.rounded_rectangle([x, 424, x + 55, 431], radius=3, fill=rgb(TUBE))

# ================================================================= painted labels
LABELS = []


def lab(x, y, s, size=11, col=DIM, anchor='l', track=1.6, weight='SemiBold'):
    LABELS.append((x, y, s, size, col, anchor, track, weight))


# header
lab(210, 4, 'OVERALL', 10); lab(296, 4, 'IN CLASS', 10); lab(390, 4, 'TIME REMAINING', 10)
lab(632, 4, 'LAP', 10); lab(716, 4, 'TRACK \u00b0C', 10)
# left panel
lab(33, 70, 'FUEL', 11, AMBER, 'm'); lab(91, 70, 'ENERGY', 11, ENERGYC, 'm')
lab(130, 96, 'FUEL LAPS', 10); lab(130, 176, 'BURN / LAP', 10); lab(130, 256, 'STINT LAPS', 10)
# right panel
lab(580, 69, 'TIMING', 10); lab(784, 69, 'GAP  s', 10, DIM, 'r')
lab(702, 86 + 2 * 52 + 17, 'STOPS', 9, T['accent2'])
# deck
for k, s in enumerate(('FL', 'FR', 'RL', 'RR')):
    lab(32 + k * 60, 434, s, 9, FAINT, 'm', 1.2)
lab(362, 372, 'LAST', 10); lab(362, 410, 'BEST', 10, PURPLE)
lab(305, 428, 'KM/H', 9, FAINT, 'm', 1.4)
for (x, y, s) in ((558, 370, 'TC'), (680, 370, 'ABS'), (558, 412, 'MAP'), (680, 412, 'BB')):
    lab(x, y, s, 11)
lab(784, 435, 'RACE PROGRESS', 8, FAINT, 'r', 1.4, 'Medium')

# ================================================================= the elements
ND = 'DataCorePlugin.GameData.NewData.'
SC = 'DataCorePlugin.GameRawData.LMUNativeTelemetry.scoring.scoringInfo.'
PLAYER = 'getplayerleaderboardposition(0)'
E = []
WELLS = []      # (x0, y0, x1, y1, colour or 'auto'): flat rectangles repainted last, under every value's text


def add(**k):
    E.append(k)
    return k


def label(name, x, y, w, text, color=DIM, align='left', font=10, h=16, **more):
    return add(Type='label', Name=name, X=x, Y=y, W=w, H=h, Text=text, Font=font, Color=color, Align=align, **more)


def value(name, bind, x, y, w, h, font, samples, preview, fmt='0', color=TEXT, align='left', empty='-', well='auto', **more):
    e = dict(Type='value', Name=name, Bind=bind, X=x, Y=y, W=w, H=h, Font=font, Format=fmt, Color=color, Align=align,
             Samples=samples, PreviewText=preview, Empty=empty)
    e.update(more)
    if well is not None and 'Visible' not in more:
        WELLS.append((x - 2, y - 1, x + w + 2, y + h + 1, well))
    return add(**e)


def bands(*spec):
    stops = []
    for lo, hi, c in spec:
        stops += [{'Value': lo, 'Color': c}, {'Value': hi, 'Color': c}]
    return stops


CLASS_COLOUR = f'ncalc:drivercarclasscolor({PLAYER})'
IMG = f'nocturne-art@{W}x{H}'
MAXCOLORS = int(os.environ.get('NOC_COLORS', '2'))   # only for a screen without the RAM drive (rectangles, colour-reduced)
add(Type='image', Name='art', X=0, Y=0, W=W, H=H, Image=IMG, MaxColors=MAXCOLORS)

# ---- header: the blade, the class chip, position, class position, race clock, lap, track temperature
add(Type='rect', Name='blade', X=0, Y=0, W=W, H=3, Color=CLASS, ColorBind=CLASS_COLOUR)
add(Type='rect', Name='class tab', X=0, Y=9, W=7, H=39, Color=CLASS, ColorBind=CLASS_COLOUR)
value('class', f'prop:{ND}CarClass', 12, 12, 164, 32, 101, ['HYPERCAR', 'LMGT3'], 'HYPERCAR', fmt='text', color=TEXT, empty='')
value('overall', 'position', 208, 14, 68, 40, 98, ['P88'], 'P7', fmt="'P'0")
value('class pos', f'ncalc:driverclassposition({PLAYER})', 294, 14, 68, 40, 98, ['P88'], 'P2', fmt="'P'0")
value('race clock', f'ncalc:timespantoseconds([{ND}SessionTimeLeft])', 390, 14, 170, 40, 98, ['88:88:88'], '04:32:10',
      fmt=r'time:hh\:mm\:ss', empty='--:--:--')
value('lap', 'lap', 630, 14, 68, 40, 98, ['888'], '87')
value('track temp', f'prop:{ND}RoadTemperature', 714, 14, 70, 40, 98, ['88'], '31', empty='--')

# ---- left panel: fuel and energy cylinders (the liquid: highlight, body, shade side by side), readouts
FUEL = ('#FFD98A', '#FFB020', '#C27800')
ENERGY = ('#B8FFD0', '#4DFF8A', '#12A850')
LOW = ('#FF9BA8', '#FF4560', '#B3162E')


def cylinder(name, bind, cx, tones):
    for part, (dx, w), n, low in (('highlight', (-11, 6), tones[0], LOW[0]), ('body', (-5, 10), tones[1], LOW[1]),
                                  ('shade', (5, 6), tones[2], LOW[2])):
        add(Type='bar', Name=f'{name} {part}', Bind=bind, Min=0, Max=100, Orientation='vertical', X=cx + dx, Y=98, W=w, H=190,
            Color=n, Fill=TUBE, ColorBind=bind, ColorStops=bands((-1, 12.99, low), (13, 101, n)))


cylinder('fuel', 'fuelPercent', 33, FUEL)
cylinder('energy', 'virtualEnergy', 91, ENERGY)
value('fuel %', 'fuelPercent', 7, 306, 52, 34, 101, ['100'], '62', align='center', color=AMBER)
value('energy %', 'virtualEnergy', 65, 306, 52, 34, 101, ['100'], '71', align='center', color=ENERGYC)
value('fuel laps', 'fuelRemainingLaps', 130, 110, 74, 40, 98, ['88.8'], '18.4', fmt='0.0',
      ColorBind='fuelRemainingLaps', ColorStops=bands((-1, 2.99, RED), (3, 6.99, AMBER), (7, 999, TEXT)))
value('burn', 'fuelLastLap', 130, 190, 74, 40, 98, ['8.88'], '3.12', fmt='0.00', color=TEXT)
value('stint', f'ncalc:driverlapsdonesincelastpitout({PLAYER})', 130, 270, 74, 40, 98, ['888'], '23', color=TEXT)

# ---- the lens: the delta badge across its top (number and tape), the gear in the pocket
value('delta', 'delta', 302, 69, 186, 46, 97, ['+88.88', '-88.88'], '-0.214', fmt='delta', align='center',
      PositiveColor=RED, NegativeColor=GREEN, empty='-.--', well=BADGE_COL)
add(Type='deltabar', Name='delta tape', Bind='delta', X=299, Y=119, W=192, H=12, Segments=12, Pitch=8, SegmentWidth=6,
    Range=1.0, PositiveColor=RED, NegativeColor=GREEN, SegmentColor='#13211A')
value('gear', 'gearText', 339, 139, 112, 170, 103, ['8', 'N', 'R', 'D'], '4', fmt='gear', align='center', color=TEXT, empty='N', well=HUB)

# ---- right panel: the timing tower (two ahead, you, two behind)
for k in range(5):
    o = k - 2
    y = ROWS[k]
    idx = f'{PLAYER}+({o})' if o else PLAYER
    me = o == 0
    avail = f'ncalc:driveravailable({idx})'
    vis = {} if me else {'Visible': avail}
    add(Type='rect', Name=f'tower {k} tick', X=576, Y=y + 6, W=4, H=36, Color=CLASS,
        ColorBind=f'ncalc:drivercarclasscolor({idx})', **vis)
    pos_prev = ['5', '6', '7', '8', '9'][k]
    value(f'tower {k} pos', 'position' if me else f'ncalc:driverposition({idx})', 582, y + 8, 34, 32, 101,
          ['88'], pos_prev, align='right', color=TEXT if me else DIM, **vis)
    name_prev = ['RAV', 'MOR', 'ZIA', 'TEK', 'DUR'][k]
    value(f'tower {k} name', f'ncalc:driverinitials({idx})', 620, y + 8, 82, 32, 101, ['WWW'], name_prev, fmt='text',
          color=TEXT, **vis)
    if me:
        # your own row: the pit stops made so far (the gaps are for the others)
        value('tower me stops', f'ncalc:driverpitcount({idx})', 744, y + 8, 36, 32, 101, ['88'], '3', align='right', color=T['accent2'],
              empty='0')
    else:
        gap_prev = ['-14.2', '-3.8', '', '+2.6', '+9.1'][k]
        value(f'tower {k} gap', f'ncalc:drivergaptoplayer({idx})', 704, y + 8, 76, 32, 101, ['+88.8', '-88.8'], gap_prev,
              fmt='+0.0;-0.0;0.0', align='right', color=AMBER if o < 0 else GREEN, **vis)

# ---- bottom deck: tyres, speed and times, settings, race progress
TSTOP = bands((-300, 69.9, '#3E86FF'), (70, 79.9, '#28D7FF'), (80, 99.9, '#2EE6A0'), (100, 109.9, '#FFB020'), (110, 400, '#FF4560'))
WSTOP = bands((-1, 24.9, '#FF4560'), (25, 49.9, '#FFB020'), (50, 101, '#2EE6A0'))
LMU_W = {'FrontLeft': '01', 'FrontRight': '02', 'RearLeft': '03', 'RearRight': '04'}


def tyre_temp(corner):
    w = f'GameRawData.CurrentPlayerTelemetry.mWheels{LMU_W[corner]}.'
    inner = ' + '.join(f'isnull([{w}mTireInnerLayerTemperature0{k}], 0)' for k in (1, 2, 3))
    lmu = f'((({inner}) / 3 + isnull([{w}mTireCarcassTemperature], 0)) / 2) - 273.15'
    return f"ncalc:if([DataCorePlugin.CurrentGame] = 'LMU', {lmu}, [{ND}TyreTemperature{corner}])"


for k, corner in enumerate(('FrontLeft', 'FrontRight', 'RearLeft', 'RearRight')):
    x = 4 + k * 60
    t = tyre_temp(corner)
    add(Type='rect', Name=f'{corner} block', X=x, Y=362, W=56, H=58, Color='#2EE6A0', ColorBind=t, ColorStops=TSTOP)
    value(f'{corner} temp', t, x, 364, 56, 36, 101, ['188'], ['88', '86', '91', '89'][k], align='center', color='#000000',
          empty='--')
    value(f'{corner} psi', f'prop:{ND}TyrePressure{corner}', x, 402, 56, 16, 10, ['88.8'], ['26.5', '26.7', '26.2', '26.4'][k],
          fmt='0.0', align='center', color='#000000', empty='')
    add(Type='bar', Name=f'{corner} wear', Bind=f'prop:{ND}TyreWear{corner}', Min=0, Max=100, Orientation='horizontal',
        X=x + 1, Y=425, W=54, H=6, Color='#2EE6A0', Fill=TUBE, ColorBind=f'prop:{ND}TyreWear{corner}', ColorStops=WSTOP)

value('speed', 'speed', 256, 362, 98, 64, 99, ['388'], '214', align='right')
value('last lap', 'lastLapTime', 398, 364, 138, 32, 101, ['8:88.888'], '1:42.318', fmt='laptime', align='right', empty='-:--.---')
value('best lap', 'bestLapTime', 398, 402, 138, 32, 101, ['8:88.888'], '1:41.907', fmt='laptime', align='right', color=PURPLE,
      empty='-:--.---')
for (x, y, name, bind, fmt, samples, prev) in ((550, 358, 'tc', 'tcLevel', '0', ['88'], '4'), (672, 358, 'abs', 'absLevel', '0', ['88'], '3'),
                                               (550, 400, 'map', 'engineMap', '0', ['88'], '2'), (672, 400, 'bb', 'brakeBias', '0.0', ['88.8'], '55.5')):
    value(f'{name} value', bind, x + 40, y + 3, 72, 32, 101, samples, prev, fmt=fmt, align='right')

# race progress, 0..1: LMU's own session clock (elapsed time over the time the session ends), else laps done over the laps
# in the race, else nothing.
PROGRESS = (f'ncalc:if(isnull([{SC}mEndET], 0) > 0, [{SC}mCurrentET] / [{SC}mEndET], '
            f'if(isnull([{ND}TotalLaps], 0) > 0, 1 - [{ND}RemainingLaps] / [{ND}TotalLaps], 0))')
_m, _c1, _c2, _c3 = T['day']
DAY = [(0.00, _m), (0.16, '#E0803C'), (0.30, _c1), (0.50, _c2), (0.70, _c3), (0.82, '#F59638'),
       (0.92, '#9A4AA0'), (1.00, '#6A46B4')]                  # morning, dawn, day, dusk, night: the bar's colour as the race goes
add(Type='bar', Name='race progress', Bind=PROGRESS, Min=0, Max=1, Orientation='horizontal', X=7, Y=447, W=776, H=10,
    Color=_m, Fill=TUBE, ColorBind=PROGRESS, ColorStops=[{'Value': v, 'Color': c} for v, c in DAY])

# ---- traffic side lights (a car alongside): only while it is there
add(Type='rect', Name='car left', X=0, Y=64, W=6, H=284, Color='#FFB020', Visible='spotterLeft', PreviewVisible=False)
add(Type='rect', Name='car right', X=784, Y=64, W=6, H=284, Color='#FFB020', Visible='spotterRight', PreviewVisible=False)

# ---- overlays (last, opaque, each covering whole cells)
pit = 'pitLimiter'
HUBH = '#%02X%02X%02X' % HUB
add(Type='box', Name='pit box', X=296, Y=62, W=198, H=252, Color=NEONH, Fill=HUBH, Border=3, Radius=14, Visible=pit,
    PreviewVisible=False)
label('pit title', 302, 90, 186, 'PIT LIMITER', color=T['pit_title'], align='center', font=101, h=32, Visible=pit, PreviewVisible=False)
value('pit speed', 'speed', 302, 138, 186, 100, 35, ['388'], '60', align='center', color=TEXT, Visible=pit, PreviewVisible=False,
      well=None)
label('pit unit', 302, 252, 186, 'KM/H', color=T['pit_title'], align='center', font=101, h=32, Visible=pit, PreviewVisible=False)

blue = f'ncalc:[{ND}Flag_Blue] > 0'
yellow = f'ncalc:[{ND}Flag_Yellow] > 0'
for nm, cond, fill, edge, txt, tcol in (('blue', blue, '#06255A', '#4DA3FF', 'BLUE FLAG', '#CFE4FF'),
                                        ('yellow', yellow, '#4A3A00', '#FFD21F', 'YELLOW FLAG', '#FFE680')):
    add(Type='box', Name=f'{nm} flag', X=382, Y=4, W=236, H=51, Color=edge, Fill=fill, Border=2, Radius=8, Visible=cond,
        PreviewVisible=False)
    label(f'{nm} flag text', 392, 14, 216, txt, color=tcol, align='center', font=101, h=32, Visible=cond, PreviewVisible=False)

inv = 'lapInvalid'
add(Type='box', Name='invalid', X=296, Y=64, W=198, H=74, Color=RED, Fill='#3A0A12', Border=2, Radius=12, Visible=inv,
    PreviewVisible=False)
label('invalid text', 302, 84, 186, 'LAP INVALID', color='#FF9AA8', align='center', font=101, h=34, Visible=inv, PreviewVisible=False)

# ================================================================= finish the picture and write the dash
FEATHER = 5                                                       # a flat well fades into the render over this many px
arr = np.asarray(img, np.float32).copy()
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
flat = []
for (x0, y0, x1, y1, col) in WELLS:                               # the colour of each well: the mean of the render under it
    x0, y0, x1, y1 = int(x0), int(y0), int(x1), int(y1)
    flat.append((x0, y0, x1, y1, tuple(arr[y0:y1, x0:x1].reshape(-1, 3).mean(0)) if col == 'auto' else col))
for (x0, y0, x1, y1, col) in flat:                                # pass 1: feather every well into its surroundings
    dx = np.maximum(np.maximum(x0 - xx, xx - (x1 - 1)), 0)
    dy = np.maximum(np.maximum(y0 - yy, yy - (y1 - 1)), 0)
    m = np.clip(1 - np.hypot(dx, dy) / FEATHER, 0, 1)
    m = (m * m * (3 - 2 * m))[..., None]
    arr = arr * (1 - m) + np.asarray(col, np.float32) * m
img = Image.fromarray(np.clip(arr + 0.5, 0, 255).astype(np.uint8))
dr = ImageDraw.Draw(img)
for (x0, y0, x1, y1, col) in flat:                                # pass 2: every well exactly flat, whatever its neighbours did
    dr.rectangle([x0, y0, x1 - 1, y1 - 1], fill=tuple(int(round(c)) for c in col))


def draw_labels():
    for x, y, s, size, col, anchor, track, weight in LABELS:
        f = ImageFont.truetype(os.path.join(FONT_DIR, f'ChakraPetch-{weight}.ttf'), size)
        adv = [f.getlength(c) + track for c in s]
        total = sum(adv) - track
        cx = x if anchor == 'l' else x - total / 2 if anchor == 'm' else x - total
        for c, a in zip(s, adv):
            dr.text((cx, y), c, font=f, fill=rgb(col))
            cx += a


draw_labels()
buf = io.BytesIO()
img.save(buf, 'PNG', optimize=True)
img.save(OUT.replace('.json', '_art.png'))

dash = dict(FormatVersion=2, Id=T['id'], Name=T['name'], Author='FX Unleashed',
            Description='Night-endurance dash for a wheel with the RAM drive, modelled and rendered in 3D: a neon lens for the '
                        'gear and delta, glass fuel and energy cylinders, laps of fuel and stint, a timing tower with class '
                        'colours, race clock, flags and a race-progress bar whose colour follows the day.',
            Elements=E, Images={IMG: base64.b64encode(buf.getvalue()).decode('ascii')})
json.dump(dash, open(OUT, 'w', encoding='utf-8'), indent=1)
print(f'{len(E)} elements, art {len(buf.getvalue()) // 1024} KB -> {OUT}')
