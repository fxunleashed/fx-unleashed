"""
A reference picture of a SimHub dash screen, drawn the way SimHub draws it, at the size the FX Pro shows it: for
comparing a conversion with its original item by item (/create-dash, a 1:1 conversion).

    python tools/dashes/simhub_ref.py "DASH NAME" OUT.png [--screen NAME] [--true "formula" ...] [--page widget=N ...]
                                      [--fit 790,460] [--sample text|empty]

Draws the screen's items in order with the dash's own pictures (.ressources, SimHub's ImageLibrary) and fonts (Windows'
or the dash's _SHFonts), WPF's rules for what it can: text alignment in its box, a picture's Uniform stretch in its box,
opacity (layers multiply into their children), rotation, ellipses (fill, then the rim inside
the box), rectangles (background, border, corner radius), widgets (one screen each: --page widget=N, else the first).
Bound texts show their designer text (what SimHub's own preview shows). An item or layer with a Visible formula shows
only when that formula is given with --true (exactly as written in the dash, or a part of it); blinking items show.

Not drawn: gauges, graphs and maps; leaderboard items show a sample ("Lorem Ipsum" / "+0.00", as SimHub's preview).
Used as a module: render(dash_path, screen, true_formulas, pages, fit) -> PIL image.
"""
import io, json, os, re, sys, zipfile
from PIL import Image, ImageDraw, ImageFont

SIMHUB = os.environ.get('SIMHUB_INSTALL_PATH', r'C:\Program Files (x86)\SimHub')
WINFONTS = r'C:\Windows\Fonts'
FONT_FILES = {'arial': ('arial.ttf', 'arialbd.ttf'), 'bahnschrift': ('bahnschrift.ttf', 'bahnschrift.ttf'),
              'lucida sans': ('LSANS.TTF', 'LSANSD.TTF'), 'segoe ui': ('segoeui.ttf', 'segoeuib.ttf'),
              'microsoft jhenghei': ('msjh.ttc', 'msjhbd.ttc'), 'microsoft jhenghei ui': ('msjh.ttc#1', 'msjhbd.ttc#1')}
WEIGHTS = {'Thin': 'Light', 'ExtraLight': 'Light', 'Light': 'Light', 'Normal': 'Regular', 'Regular': 'Regular',
           'Medium': 'SemiBold', 'SemiBold': 'SemiBold', 'DemiBold': 'SemiBold', 'Bold': 'Bold', 'ExtraBold': 'Bold',
           'UltraBold': 'Bold', 'Black': 'Bold', 'Heavy': 'Bold'}


def colour(argb, opacity=1.0):
    """'#AARRGGBB' / '#RRGGBB' -> (r, g, b, a) with `opacity` applied, or None."""
    if not argb or not isinstance(argb, str) or not argb.startswith('#'): return None
    h = argb[1:]
    a = int(h[0:2], 16) if len(h) == 8 else 255
    r, g, b = (int(h[i:i + 2], 16) for i in ((2, 4, 6) if len(h) == 8 else (0, 2, 4)))
    return (r, g, b, int(round(a * opacity)))


class Dash:
    def __init__(self, path):
        self.path = path
        self.folder = os.path.dirname(path)
        self.json = json.load(open(path, encoding='utf-8-sig'))
        self.images = {}
        res = path + '.ressources'
        if os.path.exists(res):
            with zipfile.ZipFile(res) as z:
                for n in z.namelist():
                    if n.lower().endswith('.png') or n.lower().endswith('.jpg'):
                        self.images[os.path.splitext(os.path.basename(n))[0]] = Image.open(io.BytesIO(z.read(n))).convert('RGBA')
        self.fonts = {}

    def image(self, name):
        if not name: return None
        if name in self.images: return self.images[name]
        if name.startswith('library:'):
            p = os.path.join(SIMHUB, 'ImageLibrary', name[len('library:'):].replace('\\', os.sep))
            if os.path.exists(p):
                self.images[name] = Image.open(p).convert('RGBA')
                return self.images[name]
        return None

    def font(self, family, size, weight):
        key = (family, round(size, 2), weight)
        if key in self.fonts: return self.fonts[key]
        fam = (family or 'Arial').lower()
        bold = WEIGHTS.get(weight or 'Normal', 'Regular') in ('Bold', 'SemiBold')
        files = FONT_FILES.get(fam)
        path = None
        if files: path = os.path.join(WINFONTS, files[1 if bold else 0])
        if not path or not os.path.exists(path.split('#')[0]):
            # the dash's own fonts
            shf = os.path.join(self.folder, '_SHFonts')
            for f in (os.listdir(shf) if os.path.isdir(shf) else []):
                if fam.split()[0] in f.lower(): path = os.path.join(shf, f); break
            else:
                # by the family name inside the file (the Mercedes AMG's "SansSerif" is sanss___.ttf / sanssb__.ttf)
                for f in sorted(os.listdir(shf) if os.path.isdir(shf) else []):
                    try: n = ImageFont.truetype(os.path.join(shf, f), 10).getname()
                    except Exception: continue
                    if n[0].lower() == fam and (n[1].lower() == ('bold' if bold else 'regular')):
                        path = os.path.join(shf, f); break
        if not path or not os.path.exists(path.split('#')[0]): path = os.path.join(WINFONTS, 'arial.ttf')
        idx = 0
        if '#' in os.path.basename(path): path, idx = path.rsplit('#', 1)[0], int(path.rsplit('#', 1)[1])   # a face in a .ttc
        f = ImageFont.truetype(path, max(1, size), index=idx)
        if fam == 'bahnschrift':
            try: f.set_variation_by_name(WEIGHTS.get(weight or 'Normal', 'Regular'))
            except Exception: pass
        self.fonts[key] = f
        return f


def tname(it): return (it.get('$type') or '').split(',')[0].split('.')[-1]


def formula(it, prop):
    b = (it.get('Bindings') or {}).get(prop)
    if not isinstance(b, dict): return None
    f = b.get('Formula') or {}
    e = (f.get('Expression') or f.get('JSExt') or '').strip()
    return e or None


def norm(s): return re.sub(r'\s+', ' ', s or '').strip()


class Renderer:
    def __init__(self, dash, true_formulas, pages, sample, sc, ox, oy, size):
        self.d, self.true, self.pages, self.sample = dash, [norm(t) for t in true_formulas], pages or {}, sample
        self.sc, self.ox, self.oy = sc, ox, oy
        self.im = Image.new('RGBA', size, (0, 0, 0, 255))

    def shown(self, it):
        if it.get('Visible') is False and not formula(it, 'Visible'): return False
        f = formula(it, 'Visible')
        if f is None: return True
        nf = norm(f)
        return any(t == nf or (t and t in nf) for t in self.true)

    def box(self, it, fx, fy, fs):
        x = fx + (it.get('Left') or 0) * fs
        y = fy + (it.get('Top') or 0) * fs
        return x, y, (it.get('Width') or 0) * fs, (it.get('Height') or 0) * fs

    def over(self, layer):
        self.im.alpha_composite(layer)

    def draw_items(self, items, fx, fy, fs, op):
        for it in items or []:
            if not self.shown(it): continue
            t = tname(it)
            o = op * ((it.get('Opacity') if it.get('Opacity') is not None else 100) / 100)
            if t in ('Layer', 'GroupItem'):
                # a group's children are placed from its corner (a layer's on the screen's own coordinates)
                gx, gy = ((it.get('Left') or 0) * fs, (it.get('Top') or 0) * fs) if t == 'GroupItem' else (0, 0)
                self.draw_items(it.get('Childrens') or it.get('Items'), fx + gx, fy + gy, fs, o)
            elif t == 'WidgetItem':
                self.widget(it, fx, fy, fs, o)
            else:
                getattr(self, 'd_' + t, self.d_other)(it, fx, fy, fs, o)

    def widget(self, it, fx, fy, fs, o):
        p = os.path.join(self.d.folder, it.get('FileName') or '')
        if not os.path.exists(p): return
        w = json.load(open(p, encoding='utf-8-sig'))
        screens = w.get('Screens') or []
        if not screens: return
        n = self.pages.get(it.get('Name'), self.pages.get(it.get('FileName'), it.get('InitialScreenIndex') or 0))
        bw = w.get('BaseWidth') or it.get('Width') or 1
        s = fs * (it.get('Width') or bw) / bw
        x, y, _, _ = self.box(it, fx, fy, fs)
        self.draw_items(screens[n % len(screens)].get('Items'), x, y, s, o)

    # ---- items ----
    def layer(self):
        return Image.new('RGBA', self.im.size, (0, 0, 0, 0))

    def rect(self, it, fx, fy, fs, o, fill=None):
        x, y, w, h = self.box(it, fx, fy, fs)
        bs = it.get('BorderStyle') or {}
        r = max(bs.get('RadiusTopLeft', 0), bs.get('RadiusTopRight', 0), bs.get('RadiusBottomLeft', 0), bs.get('RadiusBottomRight', 0)) * fs
        bw = max(bs.get('BorderTop', 0), bs.get('BorderLeft', 0), bs.get('BorderRight', 0), bs.get('BorderBottom', 0)) * fs
        bg = fill or colour(it.get('BackgroundColor'), o)
        bc = colour(bs.get('BorderColor') or '#FFFFFFFF', o)   # SimHub's border without a colour: white (its own previews)
        if (bg and bg[3] > 0) or bw > 0:
            L = self.layer(); g = ImageDraw.Draw(L)
            box = [x, y, x + w - 1, y + h - 1]
            if bw > 0 and bc[3] > 0:
                g.rounded_rectangle(box, radius=r, fill=bc)
                inner = [x + bw, y + bw, x + w - 1 - bw, y + h - 1 - bw]
                if inner[2] > inner[0] and inner[3] > inner[1]:
                    g.rounded_rectangle(inner, radius=max(0, r - bw), fill=bg if bg and bg[3] > 0 else (0, 0, 0, 0))
            elif bg:
                g.rounded_rectangle(box, radius=r, fill=bg)
            self.over(L)

    def d_RectangleItem(self, it, fx, fy, fs, o):
        self.rect(it, fx, fy, fs, o)

    def d_EllipseItem(self, it, fx, fy, fs, o):
        x, y, w, h = self.box(it, fx, fy, fs)
        fill = colour(it.get('FillColor'), o)
        rim = colour(it.get('EllipseColor'), o)
        th = (it.get('EllipseThickness') or 0) * fs
        L = self.layer(); g = ImageDraw.Draw(L)
        if rim and rim[3] > 0 and th > 0:
            g.ellipse([x, y, x + w - 1, y + h - 1], fill=rim)
            g.ellipse([x + th, y + th, x + w - 1 - th, y + h - 1 - th], fill=fill if fill and fill[3] > 0 else (0, 0, 0, 0))
        elif fill and fill[3] > 0:
            g.ellipse([x, y, x + w - 1, y + h - 1], fill=fill)
        self.over(L)

    def d_ImageItem(self, it, fx, fy, fs, o):
        # a picture's own background colour and border (a panel: a shading picture on a coloured rounded box), then
        # the picture inside the border
        bs = it.get('BorderStyle') or {}
        if (colour(it.get('BackgroundColor')) or (0, 0, 0, 0))[3] > 0 or any(bs.get(k) for k in ('BorderTop', 'BorderLeft')):
            self.rect(it, fx, fy, fs, o)
        src = self.d.image(it.get('Image'))
        if src is None or o <= 0: return
        x, y, w, h = self.box(it, fx, fy, fs)
        if not it.get('AutoSize') and (bs.get('BorderTop') or (colour(it.get('BackgroundColor')) or (0, 0, 0, 0))[3] > 0):
            # a picture on a panel fills it (as SimHub shows the Ferrari 296's "shadow" panels): stretched inside the border
            b = max(bs.get('BorderTop', 0), bs.get('BorderLeft', 0)) * fs
            pic = src.resize((max(1, round(w - 2 * b)), max(1, round(h - 2 * b))), Image.LANCZOS)
            if o < 1: pic.putalpha(pic.getchannel('A').point(lambda a: int(a * o)))
            r = max(bs.get('RadiusTopLeft', 0), bs.get('RadiusTopRight', 0)) * fs
            m = Image.new('L', pic.size, 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, pic.width - 1, pic.height - 1], radius=max(0, r - b), fill=255)
            pic.putalpha(Image.composite(pic.getchannel('A'), Image.new('L', pic.size, 0), m))
            L = self.layer(); L.alpha_composite(pic, (int(round(x + b)), int(round(y + b)))); self.over(L)
            return
        # Uniform in its box (an AutoSize picture's stored size is what the designer sized it to)
        k = min(w / src.width, h / src.height)
        w2, h2 = src.width * k, src.height * k
        if w2 < 1 or h2 < 1: return
        pic = src.resize((max(1, round(w2)), max(1, round(h2))), Image.LANCZOS)
        if it.get('Rotation'): pic = pic.rotate(-it['Rotation'], resample=Image.BICUBIC, expand=False)
        if o < 1: pic.putalpha(pic.getchannel('A').point(lambda a: int(a * o)))
        L = self.layer()
        L.alpha_composite(pic, (int(round(x + (w - w2) / 2)), int(round(y + (h - h2) / 2))))
        self.over(L)

    def d_GradientItem(self, it, fx, fy, fs, o):
        br = ((it.get('Color') or {}).get('LinearGradientBrush')) or {}
        st = br.get('LinearGradientBrush.GradientStops', {}).get('GradientStop')
        if isinstance(st, dict): st = [st]
        stops = sorted(((float(g.get('@Offset', 0)), colour(g.get('@Color'), o)) for g in st or [] if colour(g.get('@Color'))), key=lambda t: t[0])
        if not stops: return
        x, y, w, h = self.box(it, fx, fy, fs)
        W, H = max(1, round(w)), max(1, round(h))
        sx, sy = (float(v) for v in br.get('@StartPoint', '0.5,0').split(','))
        ex, ey = (float(v) for v in br.get('@EndPoint', '0.5,1').split(','))
        g = Image.new('RGBA', (W, H))
        px = g.load()
        dx, dy = ex - sx, ey - sy
        n2 = dx * dx + dy * dy or 1
        for yy in range(H):
            for xx in range(W):
                t = (((xx + 0.5) / W - sx) * dx + ((yy + 0.5) / H - sy) * dy) / n2
                if t <= stops[0][0]: c = stops[0][1]
                elif t >= stops[-1][0]: c = stops[-1][1]
                else:
                    for (a, ca), (b, cb) in zip(stops, stops[1:]):
                        if a <= t <= b:
                            k = (t - a) / (b - a) if b > a else 0
                            c = tuple(int(round(ca[i] + (cb[i] - ca[i]) * k)) for i in range(4)); break
                px[xx, yy] = c
        bs = it.get('BorderStyle') or {}
        r = max(bs.get('RadiusTopLeft', 0), bs.get('RadiusTopRight', 0)) * fs
        r = min(r, W / 2, H / 2)
        m = Image.new('L', (W, H), 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, W - 1, H - 1], radius=r, fill=255)
        g.putalpha(Image.composite(g.getchannel('A'), Image.new('L', (W, H), 0), m))
        L = self.layer(); L.alpha_composite(g, (int(round(x)), int(round(y)))); self.over(L)

    def text(self, it, fx, fy, fs, o, text, family=None, size=None, colour_=None):
        if text is None or text == '': return
        x, y, w, h = self.box(it, fx, fy, fs)
        bg = colour(it.get('BackgroundColor'), o)
        if bg and bg[3] > 0 or it.get('BorderStyle'): self.rect(it, fx, fy, fs, o)
        f = self.d.font(family or it.get('Font'), (size or it.get('FontSize') or 20) * fs, it.get('FontWeight'))
        asc, desc = f.getmetrics()
        lines = str(text).split('\n')
        L = self.layer(); g = ImageDraw.Draw(L)
        lh = asc + desc
        total = lh * len(lines)
        va = it.get('VerticalAlignment', 0)
        ty = y if va == 0 else y + (h - total) / 2 if va == 1 else y + h - total
        c = colour_ or colour(it.get('TextColor'), o) or (255, 255, 255, int(255 * o))
        for k, line in enumerate(lines):
            adv = f.getlength(line)
            ha = it.get('HorizontalAlignment', 0)
            tx = x if ha == 0 else x + (w - adv) / 2 if ha == 1 else x + w - adv
            g.text((tx, ty + k * lh), line, font=f, fill=c, anchor='la')
        self.over(L)

    def d_TextItem(self, it, fx, fy, fs, o):
        t = it.get('Text')
        if formula(it, 'Text') and self.sample == 'empty': t = ''
        self.text(it, fx, fy, fs, o, t)

    def d_GearText(self, it, fx, fy, fs, o):
        self.text(it, fx, fy, fs, o, it.get('DesignerText') or 'N', colour_=colour(it.get('GearTextColor') or it.get('TextColor'), o))

    def d_SpeedText(self, it, fx, fy, fs, o):
        self.text(it, fx, fy, fs, o, '101')

    def d_other(self, it, fx, fy, fs, o):
        t = tname(it)
        if t.startswith('Leaderboard'):
            self.text(it, fx, fy, fs, o, 'Lorem Ipsum' if 'Name' in t else '+0.00' if 'Gap' in t else '1:23.456')


def find(name):
    if os.path.exists(name): return name
    folder = os.path.join(SIMHUB, 'DashTemplates', name)
    return os.path.join(folder, name + '.djson')


def render(dash, screen=None, true_formulas=(), pages=None, fit=(790, 460), sample='text'):
    d = Dash(find(dash))
    j = d.json
    bw, bh = j.get('BaseWidth') or 1280, j.get('BaseHeight') or 720
    sc = min(fit[0] / bw, fit[1] / bh)
    ox, oy = (fit[0] - bw * sc) / 2, (fit[1] - bh * sc) / 2
    scr = [s for s in j['Screens'] if screen is None and (s.get('InGameScreen') and not s.get('IsOverlayLayer')) or s.get('Name') == screen][0]
    r = Renderer(d, true_formulas, pages, sample, sc, ox, oy, fit)
    r.draw_items(scr.get('Items'), ox, oy, sc, 1.0)
    return r.im.convert('RGB')


if __name__ == '__main__':
    args = sys.argv[1:]
    name, out = args[0], args[1]
    screen, true, pages, fit, sample = None, [], {}, (790, 460), 'text'
    k = 2
    while k < len(args):
        a = args[k]
        if a == '--screen': screen = args[k + 1]; k += 2
        elif a == '--true': true.append(args[k + 1]); k += 2
        elif a == '--page': w, n = args[k + 1].rsplit('=', 1); pages[w] = int(n); k += 2
        elif a == '--fit': fit = tuple(int(v) for v in args[k + 1].split(',')); k += 2
        elif a == '--sample': sample = args[k + 1]; k += 2
        else: raise SystemExit('unknown option ' + a)
    render(name, screen, true, pages, fit, sample).save(out)
    print(out)
