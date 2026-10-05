using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>Colours: "#RRGGBB", "#AARRGGBB", names, System.Drawing / WPF colours; the screen's RGB565.</summary>
    internal static class DashColors
    {
        public static Color Parse(string s, Color fallback) => TryParse(s, out var c) ? c : fallback;

        public static bool TryParse(object o, out Color c)
        {
            c = Color.Empty;
            switch (o)
            {
                case null: return false;
                case Color dc: c = dc; return true;
                case string s:
                    s = s.Trim();
                    if (s.Length == 0) return false;
                    if (s[0] == '#')
                    {
                        var h = s.Substring(1);
                        if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2)));
                        if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
                        if (h.Length == 6) v |= 0xFF000000;
                        else if (h.Length != 8) return false;
                        c = Color.FromArgb(unchecked((int)v));
                        return true;
                    }
                    var named = Color.FromName(s);
                    if (named.IsKnownColor) { c = named; return true; }
                    return false;
                default:
                    // WPF's System.Windows.Media.Color prints as #AARRGGBB
                    return TryParse(o.ToString(), out c);
            }
        }

        public static int To565(Color c) => ((c.R >> 3) << 11) | ((c.G >> 2) << 5) | (c.B >> 3);

        public static Color From565(int c565)
        {
            int r = (c565 >> 11) & 31, g = (c565 >> 5) & 63, b = c565 & 31;
            return Color.FromArgb((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
        }

        public static Color WithOpacity(Color c, int opacity) =>
            opacity >= 100 ? c : Color.FromArgb((int)Math.Round(c.A * Math.Max(0, opacity) / 100.0), c.R, c.G, c.B);

        public static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>A problem Check found: "error" (the screen will show it wrong) or "warning" (costly or doubtful).</summary>
    public class DashIssue
    {
        public string Level;
        public string Element;
        public string Message;
        public override string ToString() => (Element != null ? Element + ": " : "") + Message;
    }

    /// <summary>What drawing the dash costs on the wheel (screen traffic is paced at 25 KB/s).</summary>
    public class DashCost
    {
        public int StaticFills;
        public int StaticBytes;
        public double StaticSeconds;
        public int Elements, DynamicElements;
        /// <summary>Values whose area is redrawn from the shapes before each new text (busy background).</summary>
        public List<string> RedrawnValues = new List<string>();
        /// <summary>On a wheel with the screen's RAM drive: the dash's pictures (tiles) there, and the drive's budget.</summary>
        public int RamBytes, RamFiles;
        public int RamBudget = ScreenRam.Budget;
    }

    /// <summary>
    /// Draws a DashDefinition on the FX Pro screen with its own commands (ported from FXProDashes tools/dash, verified on
    /// the wheel). Shapes that never change go into a bitmap once and out as merged `fill` rectangles; plain labels as
    /// `xstr` with no background. Everything else is dynamic and redrawn only when it changes: values, bars and delta
    /// bars, and shapes/labels with a Visible condition or a ColorBind. Hiding something redraws its area from the static
    /// layer and the dynamic elements there ("repaint"). A value's background is automatic: the colour under it when
    /// that's one colour, else the area is repainted before the new text is drawn without a background.
    /// Offsets pad the whole dash away from the screen's edges.
    /// </summary>
    internal sealed class DashRenderer
    {
        public const int Width = 800, Height = 480;
        private const int PopupKey = 0xF81F; // magenta: the transparent colour of the pop-up's bitmap
        private const int Transparent = -1;
        public const double BytesPerSecond = 25000;

        private readonly IScreenSink screen;
        private readonly DashDefinition def;
        private readonly int dx, dy;
        /// <summary>The static layer as drawn now: colour-reduced (fills) or full colour (tiles, see UseTiles).</summary>
        private int[] staticPx;
        private int[] quantPx, richPx, shownPx;
        private Bitmap richBmp;
        private bool tilesOn;
        /// <summary>The static layer as pictures for the screen's RAM drive (EnableTiles), or null.</summary>
        public ScreenTiles Tiles { get; private set; }
        /// <summary>Drawing from tiles (all of them are on the screen) rather than fills.</summary>
        public bool TilesOn => tilesOn;
        private readonly List<Node> staticLabels = new List<Node>();
        private readonly List<Node> dynamic = new List<Node>();
        private readonly List<Node> popups = new List<Node>();
        private readonly List<Node> dims = new List<Node>();
        private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
        private DashValues current;

        private sealed class Node
        {
            public int Index;
            public DashElement E;
            public Rectangle R;
            public string Kind;            // shape, label, value, bar, deltabar, popup
            public int XCen;
            // runtime
            public bool Visible, Shown;
            public string Key, Sent;        // wanted / last drawn state
            public Color Colour;            // text / shape colour now
            public string Text;
            public Rectangle? TextAt;       // where the value's text was last drawn (its extent in the box)
            public bool SolidAt;            // ...with its background (all of TextAt painted), not just the glyphs
            public int SolidBg;             // that background
            public bool LinesAt;            // ...drawn on its main background colour, then the lines through it put back
            public bool BandAt;             // ...over its band tile (tiles: the background drawn as a picture first)
            public int? StaticBg;           // the one colour under the box in the static layer, if it is one
            public int[] Px;                // shape pixels for Key (Transparent = not drawn)
            public int[] DrawnPx;           // the pixels it was last drawn with (Px may be refreshed before it's drawn again)
            public string DrawnKey;         // the Key it was last drawn with
            public int DrawnRim = -1;       // drawn by the screen itself: the rim colour it had (RGB565)
            public bool DamageAll = true;   // since it was drawn, drawn over where unknown (else only in Damage, see MarkAbove)
            public Rectangle Damage;
            public string PxKey;
            public bool? PxOpaque;          // Px has no transparent pixel
            public bool[] PxSolid;          // which of Px don't show what's under them (null: all but Transparent)
            public List<Rectangle> SolidParts; // else its solid parts (a rounded box: all but its corners), screen coordinates
            public bool CrowdedKnown, IsCrowded;
            // bar
            public int FillEnd;
            public int? BarAt;              // fill end as last drawn in full (null: the whole bar must be drawn again)
            public Color BarColour;
            // deltabar
            public int[] X0, X1;
            public string[] SegSent;
            public int Pos, Neg, SegEmpty;
            // popup
            public double Until = -1;
            public Dictionary<string, string> Last = new Dictionary<string, string>();
        }

        public DashRenderer(IScreenSink screen, DashDefinition def, int left, int top)
        {
            this.screen = screen;
            this.def = def;
            dx = left; dy = top;
            LoadImages();
            int index = 0;
            foreach (var e in def.Elements)
            {
                var n = new Node { Index = index++, E = e, R = new Rectangle(e.X, e.Y, Math.Max(0, e.W), Math.Max(0, e.H)), XCen = Align(e.Align) };
                switch (e.Type)
                {
                    case "rect": case "ellipse": case "box": case "gradient": case "image": n.Kind = "shape"; break;
                    case "label": n.Kind = "label"; n.Text = e.Text ?? ""; break;
                    case "value": n.Kind = "value"; break;
                    case "bar": n.Kind = "bar"; break;
                    case "deltabar":
                        n.Kind = "deltabar";
                        var xs = e.SegmentLefts();
                        n.X0 = xs; n.X1 = xs.Select(x => x + e.SegmentWidth).ToArray(); n.SegSent = new string[xs.Length];
                        n.Pos = Rgb565(e.PositiveColor ?? "#FF0000"); n.Neg = Rgb565(e.NegativeColor ?? "#00FF00"); n.SegEmpty = Rgb565(e.SegmentColor);
                        n.R = Rectangle.FromLTRB(xs.Min(), e.Y, xs.Max() + e.SegmentWidth, e.Y + e.H);
                        break;
                    case "popup": n.Kind = "popup"; popups.Add(n); continue;
                    case "dim": n.Kind = "dim"; dims.Add(n); continue; // the backlight, not drawn (DimPercent)
                    default: continue; // unknown types are reported by Check
                }
                // a label over something that changes (a bar, a value, a shown/hidden shape) must be drawn again after it
                bool overDynamic = n.Kind == "label" && dynamic.Any(m => m.R.IntersectsWith(n.R));
                if (n.Kind == "label" && overDynamic) n.Colour = DashColors.Parse(e.Color, Color.White);
                if (n.Kind == "label" && !e.IsDynamic && !overDynamic) { n.Colour = DashColors.Parse(e.Color, Color.White); staticLabels.Add(n); }
                else if (e.IsDynamic || n.Kind != "shape" || overDynamic) dynamic.Add(n);
            }
            // a fixed label touching something that changes after it in the list (text, a bar, a pop-up) can be wiped by
            // it: it has to be drawn again after, so it's dynamic too (dynamic stays in element order)
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach (var l in staticLabels.ToList())
                    if (dynamic.Any(m => m.R.IntersectsWith(l.R)))
                    {
                        staticLabels.Remove(l); dynamic.Add(l); moved = true;
                    }
            }
            dynamic.Sort((a, b) => a.Index.CompareTo(b.Index));
            staticPx = quantPx = BuildStatic();
            foreach (var n in dynamic) n.StaticBg = Uniform(staticPx, Width, Clip(n.R));
        }

        // ---------- Tiles (the screen's RAM drive) ----------

        /// <summary>
        /// Builds the static layer in full colour (anti-aliased, pictures not colour-reduced) and cuts it into tiles: the
        /// grid, and a band per value whose text sits on a background of more than one colour with nothing dynamic under
        /// it. Returns them; the caller puts their files on the screen, then UseTiles(true) + DrawAll draws from them.
        /// </summary>
        public ScreenTiles EnableTiles()
        {
            if (Tiles != null) return Tiles;
            richBmp = BuildStaticBitmap(true);
            richPx = Pixels(richBmp);
            var t = new ScreenTiles();
            for (int y = 0; y < Height; y += ScreenTiles.Grid)
                for (int x = 0; x < Width; x += ScreenTiles.Grid)
                    t.GridTiles.Add(ScreenTiles.Make(richBmp, richPx, new Rectangle(x, y, Math.Min(ScreenTiles.Grid, Width - x), Math.Min(ScreenTiles.Grid, Height - y))));
            // what the screen shows from those tiles (their JPEGs decoded): a small area put back with fills uses these
            // colours, so it matches the tile around it (the exact colours left a faint seam along anti-aliased lines)
            shownPx = (int[])richPx.Clone();
            foreach (var tile in t.GridTiles.Where(g => g.Jpeg != null))
                try
                {
                    using (var ms = new MemoryStream(tile.Jpeg))
                    using (var dec = new Bitmap(ms))
                    {
                        var dpx = Pixels(dec);
                        for (int yy = 0; yy < Math.Min(dec.Height, tile.R.Height); yy++)
                            Array.Copy(dpx, yy * dec.Width, shownPx, (tile.R.Y + yy) * Width + tile.R.X, Math.Min(dec.Width, tile.R.Width));
                    }
                }
                catch { }
            foreach (var n in dynamic)
            {
                if (n.Kind != "value" || n.E.Background != null) continue;
                var band = Clip(BandArea(n, -1));
                if (band.Width <= 0 || band.Height <= 0 || Uniform(richPx, Width, band) != null) continue;
                if (DynamicUnder(band, n.Index)) continue; // what's under it changes: no fixed picture of it
                t.Bands[n.Index] = ScreenTiles.Make(richBmp, richPx, band);
            }
            // Shapes that come and go with a fixed look (pictures, ovals, frames, gradients; a plain rectangle is one fill
            // anyway; a colour formula landing on its stops counts, one look per colour), never over a bar: a picture of
            // them over the static layer and the shapes that always show with them (their own overlay's: conditions all
            // among theirs, drawn before), drawn with one command when exactly that is under them (PictureFits). An
            // overlay's logo over its own oval is then a single command.
            var cands = new List<(Node N, Rectangle R, List<Node> With, List<Node> Inner)>();
            foreach (var n in dynamic)
            {
                if (n.Kind != "shape" || n.E.Type == "rect" || !Steady(n) || n.E.Visible == null || n.E.Visible.Count == 0) continue;
                if (NativeCapable(n)) continue; // the screen draws it itself (ScreenShapes): no picture needed
                var r = Clip(n.R);
                if (r.Width <= 0 || r.Height <= 0 || r != n.R) continue;
                // its picture also holds the shapes under it that always show with it (the same conditions) and reach into
                // its box, whole (an oval a few px bigger than the ring on it): they need no picture of their own
                foreach (var m in dynamic.Where(m => m.Index < n.Index && m.Kind == "shape" && Steady(m) && m.R.IntersectsWith(n.R) && m.E.Visible != null
                                                     && m.E.Visible.Count == n.E.Visible.Count && m.E.Visible.All(n.E.Visible.Contains) && DrawsIn(m, n.R)))
                {
                    var u = Rectangle.Union(r, m.R);
                    if (Clip(u) == u && u.Width * u.Height <= 1.6 * n.R.Width * n.R.Height) r = u;
                }
                if (dynamic.Any(m => m.Index < n.Index && m.R.IntersectsWith(r) && (m.Kind == "bar" || m.Kind == "deltabar"))) continue;
                // a shape a few smooth rectangles draw (a frame, a small box, a little arrow) needs no picture: it shows just
                // as fast and exact, and takes no room on the drive (a file there is its bytes and an entry). (Only shapes
                // that could have one: anti-aliased, a shape that can't takes more rectangles than drawn plain.)
                if (SmoothFills(n) <= SmoothShapeFills) { smoothShapes.Add(n.Index); continue; }
                // (only shapes whose pixels reach into its area: an icon whose box grazes a corner changes nothing in it)
                var with = dynamic.Where(m => m.Index < n.Index && m.Kind == "shape" && m.R.IntersectsWith(r) && Steady(m)
                                              && m.E.Visible != null && m.E.Visible.All(n.E.Visible.Contains) && DrawsIn(m, r)).ToList();
                // overlays inside its own (shown only with it: their conditions take in all of its): a variant of the
                // picture for each mix of them that can be under it (a few at most)
                var inner = dynamic.Where(m => m.Index < n.Index && m.Kind == "shape" && m.R.IntersectsWith(r) && Steady(m)
                                               && m.E.Visible != null && m.E.Visible.Count > n.E.Visible.Count && n.E.Visible.All(m.E.Visible.Contains) && DrawsIn(m, r)).ToList();
                if (inner.Count > 3) inner.Clear();
                cands.Add((n, r, with, inner));
            }
            // A shape under another one's picture that always shows with it (its ring, its frame) and holds all of its box:
            // drawn by that picture (one command for both), so it needs none of its own (the pit oval's three colours
            // and the white pit stop oval live in the ring's pictures).
            foreach (var c in cands)
            {
                var top = cands.Where(t => t.N.Index > c.N.Index && t.R.Contains(c.R) && t.N.E.Visible.All(c.N.E.Visible.Contains)
                                           && (t.With.Contains(c.N) || t.Inner.Contains(c.N))
                                           && dynamic.All(m => m.Index <= c.N.Index || m.Index >= t.N.Index || m.Kind != "shape" || !DrawsIn(m, c.R) || t.With.Contains(m) || t.Inner.Contains(m)))
                               .Select(t => t.N).FirstOrDefault();
                if (top != null) coveredBy[c.N.Index] = top;
            }
            foreach (var (n, r, with, inner) in cands)
            {
                if (coveredBy.ContainsKey(n.Index)) continue;
                picArea[n.Index] = r;
                ScreenTile Picture(List<Node> under, Dictionary<int, Color> colours)
                {
                    Color C(Node x) => colours.TryGetValue(x.Index, out var c) ? c : DashColors.Parse(x.E.Color, Color.White);
                    using (var part = richBmp.Clone(r, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(part))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            foreach (var m in under.OrderBy(x => x.Index)) DrawShape(g, m.E, new Point(m.R.X - r.X, m.R.Y - r.Y), C(m));
                            DrawShape(g, n.E, new Point(n.R.X - r.X, n.R.Y - r.Y), C(n));
                        }
                        var tile = ScreenTiles.MakeFrom(part, r);
                        return tile.Jpeg.Length <= ScreenTiles.MaxPicture ? tile : null;
                    }
                }
                // every mix of colours the coloured ones among it and the shapes under it can have (their colour stops)
                List<Dictionary<int, Color>> Mixes(List<Node> under)
                {
                    var mixes = new List<Dictionary<int, Color>> { new Dictionary<int, Color>() };
                    foreach (var m in under.Concat(new[] { n }).Where(x => !string.IsNullOrEmpty(x.E.ColorBind)))
                        mixes = mixes.SelectMany(mx => StopColours(m).Select(c => new Dictionary<int, Color>(mx) { [m.Index] = c })).ToList();
                    return mixes;
                }
                try
                {
                    var variants = new List<PictureVariant>();
                    for (int mask = 0; mask < (1 << inner.Count) && variants.Count <= MaxVariants; mask++)
                    {
                        var under = with.Concat(inner.Where((m, i) => (mask >> i & 1) != 0)).ToList();
                        foreach (var mix in Mixes(under))
                        {
                            if (variants.Count > MaxVariants) break;
                            var v = Picture(under, mix);
                            if (v != null) variants.Add(new PictureVariant { With = new HashSet<int>(under.Select(m => m.Index)), Colours = mix.ToDictionary(kv => kv.Key, kv => DashColors.To565(kv.Value)), Tile = v });
                        }
                    }
                    if (variants.Count == 0 || variants.Count > MaxVariants) continue; // (too many: drawn with rectangles)
                    t.Pictures[n.Index] = variants[0].Tile;
                    foreach (var v in variants.Skip(1)) t.Variants.Add(v.Tile);
                    pictureWith[n.Index] = new HashSet<int>(with.Select(m => m.Index));
                    pictureInner[n.Index] = new HashSet<int>(inner.Select(m => m.Index));
                    pictureVariants[n.Index] = variants;
                }
                catch { } // drawn with fills
            }
            // What's under each box that comes and goes, exactly its area (grown by the static labels it touches, as a repaint
            // grows), as one picture: its going puts back that, not the whole 160 px grid tiles it touches and every element in
            // them (a setting pop-up in the Mustang redrew 25 elements, a hiccup). Boxes in one spot share it.
            var areas = new HashSet<Rectangle>();
            foreach (var n in dynamic)
            {
                if (n.Kind != "shape" || n.E.Visible == null || n.E.Visible.Count == 0) continue;
                var outline = Outline(n);
                if (outline.Count != 1) continue; // (a frame with nothing inside: put back along its border only)
                var a = Clip(outline[0]);
                for (int pass = 0; pass < 64; pass++)
                {
                    var grown = a;
                    foreach (var l in staticLabels) { var ink = LabelInk(l); if (ink.IntersectsWith(a)) grown = Rectangle.Union(grown, ink); }
                    grown = Clip(grown);
                    if (grown == a) break;
                    a = grown;
                }
                if (a.Width <= 0 || a.Height <= 0 || ScreenTiles.Snap(a) == a) continue;
                // only where it pays: a box up to a sixth of the screen that the grid would put back with twice its area or
                // more (a full-screen start-up screen or a big oval would just be another big file)
                var grid = ScreenTiles.Snap(a);
                if (a.Width * a.Height > AreaTileMaxPixels || grid.Width * grid.Height < 2 * a.Width * a.Height) continue;
                if (MergeRects(shownPx, Width, a).Count <= TileRepaintFills) continue; // put back with fills anyway
                areas.Add(a);
            }
            foreach (var a in areas) t.AreaTiles.Add(ScreenTiles.Make(richBmp, richPx, a));
            return Tiles = t;
        }

        /// <summary>The biggest box that gets an area tile (EnableTiles).</summary>
        public static int AreaTileMaxPixels = 50000;

        /// <summary>The smallest area tile holding `area`, or null.</summary>
        private ScreenTile AreaTileFor(Rectangle area) =>
            Tiles?.AreaTiles.Where(t => t.R.Contains(area)).OrderBy(t => t.R.Width * t.R.Height).FirstOrDefault();

        /// <summary>
        /// Draw from the tiles (true; their files must be on the screen) or with fills (false). Takes effect with the next
        /// DrawAll (call it after).
        /// </summary>
        public void UseTiles(bool on)
        {
            if (on && Tiles == null) EnableTiles();
            tilesOn = on;
            staticPx = on ? richPx : quantPx;
            foreach (var n in dynamic) { n.StaticBg = Uniform(staticPx, Width, Clip(n.R)); n.CrowdedKnown = false; n.Px = null; n.PxOpaque = null; }
        }

        /// <summary>For each shape picture (Tiles.Pictures): the shapes of its own overlay drawn into it under it.</summary>
        private readonly Dictionary<int, HashSet<int>> pictureWith = new Dictionary<int, HashSet<int>>();
        /// <summary>Overlays inside a shape picture's own that have variants of it (with them under it).</summary>
        private readonly Dictionary<int, HashSet<int>> pictureInner = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, List<PictureVariant>> pictureVariants = new Dictionary<int, List<PictureVariant>>();

        /// <summary>
        /// A shape that comes and goes, drawn with at most this many smooth rectangles, gets no picture: it appears as fast
        /// (~0.5 KB, 20 ms). 60 (~1.8 KB) was too many: the lap summary's boxes were seen drawing in on the wheel.
        /// </summary>
        public static int SmoothShapeFills = 16;

        /// <summary>Shapes with a fixed look drawn with smooth rectangles rather than a picture (cheap enough: EnableTiles).</summary>
        private readonly HashSet<int> smoothShapes = new HashSet<int>();

        /// <summary>The rectangles drawing `n` smoothly (anti-aliased, over the static layer) takes.</summary>
        private int SmoothFills(Node n)
        {
            int w = Math.Max(1, n.E.W), h = Math.Max(1, n.E.H);
            var px = new int[w * h];
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawShape(g, n.E, Point.Empty, DashColors.Parse(n.E.Color, Color.White));
                }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        int sx = n.R.X + x, sy = n.R.Y + y;
                        if (c.A < 8) { px[y * w + x] = Transparent; continue; }
                        var bg = sx >= 0 && sy >= 0 && sx < Width && sy < Height ? ToColor((richPx ?? staticPx)[sy * Width + sx]) : Color.Black;
                        int a = c.A;
                        px[y * w + x] = DashColors.To565(Color.FromArgb((c.R * a + bg.R * (255 - a)) / 255, (c.G * a + bg.G * (255 - a)) / 255, (c.B * a + bg.B * (255 - a)) / 255));
                    }
            }
            return MergeRects(px, w, new Rectangle(0, 0, w, h)).Count(r => r[4] != Transparent);
        }

        /// <summary>Where a shape's picture goes (its box, grown by the shapes it holds that always show with it).</summary>
        private readonly Dictionary<int, Rectangle> picArea = new Dictionary<int, Rectangle>();
        private Rectangle PicArea(Node n) => picArea.TryGetValue(n.Index, out var a) ? a : n.R;

        /// <summary>Shapes drawn by a later shape's picture that holds them (EnableTiles): element index -> that shape.</summary>
        private readonly Dictionary<int, Node> coveredBy = new Dictionary<int, Node>();

        /// <summary>`m` draws a pixel in `area` (its own look; the colour doesn't matter).</summary>
        private bool DrawsIn(Node m, Rectangle area)
        {
            var a = Rectangle.Intersect(area, m.R);
            if (a.Width <= 0 || a.Height <= 0) return false;
            // drawn as it is (a picture's own transparency: the fills' colour reduction would make it solid)
            using (var bmp = new Bitmap(Math.Max(1, m.E.W), Math.Max(1, m.E.H), PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawShape(g, m.E, Point.Empty, DashColors.Parse(m.E.Color, Color.White));
                }
                for (int y = a.Top; y < a.Bottom; y++)
                    for (int x = a.Left; x < a.Right; x++)
                        if (bmp.GetPixel(x - m.R.X, y - m.R.Y).A >= 8) return true;
            }
            return false;
        }

        /// <summary>
        /// `n` comes with a later shape's picture that holds it, and that picture can be drawn now: `n` is left to it (the
        /// later shape is marked to be drawn this pass), so both are one command.
        /// </summary>
        private bool LeftToPicture(Node n)
        {
            if (!tilesOn || !coveredBy.TryGetValue(n.Index, out var top) || !top.Visible || Covered(top.R)) return false;
            var tile = ChosenPicture(top, out var with);
            if (tile == null || with == null || !with.Contains(n.Index) || !PictureFits(top, with)) return false;
            // (and nothing solid already on the screen over it: it would draw with rectangles, and `n` be missing under it)
            var solid = SolidShapesAbove(top.Index);
            var at = PicArea(top);
            if (dynamic.Any(m => m.Index > top.Index && m.Kind == "shape" && m.Visible && m.Shown && m.Sent == m.Key && !Covered(m.R) && m.R.IntersectsWith(at) && solid.Contains(Clip(m.R))))
                return false;
            bool textOver = dynamic.Any(m => m.Index > top.Index && SolidText(m) && m.Sent == m.Key && !Covered(m.R) && m.TextAt.Value.IntersectsWith(at));
            if (textOver && !(CostlyWithFills(n) || CostlyWithFills(top))) return false;
            if (textOver) PictureOverTextEvents++;
            Invalidate(top);
            return true;
        }

        /// <summary>
        /// Drawing `n` with rectangles takes more than TileRepaintFills fills (a big oval): its picture is worth drawing even
        /// over text with its own background, which is then drawn again right after (gone for a moment, instead of the
        /// shape being drawn row by row for half a second). Small shapes keep the flicker-free way (fills around the text).
        /// </summary>
        private bool CostlyWithFills(Node n)
        {
            EnsurePx(n);
            return MergeRects(n.Px, Math.Max(1, n.E.W), new Rectangle(0, 0, Math.Max(1, n.E.W), Math.Max(1, n.E.H))).Count(r => r[4] != Transparent) > TileRepaintFills;
        }

        /// <summary>Diagnostics (verify): updates where a picture went over text that was then drawn again (on purpose).</summary>
        internal int PictureOverTextEvents;

        /// <summary>A picture of a shape: the shapes under it it holds, and the colours (RGB565) of the coloured ones.</summary>
        private sealed class PictureVariant { public HashSet<int> With; public Dictionary<int, int> Colours; public ScreenTile Tile; }

        /// <summary>Pictures a shape may have (every mix of what can be under it and the colours of the coloured ones).</summary>
        public static int MaxVariants = 12;

        /// <summary>A shape that always looks the same, or whose colour formula lands on its colour stops (a speed-coloured
        /// oval: blue / green / red): one picture per look.</summary>
        private static bool Steady(Node m) => string.IsNullOrEmpty(m.E.ColorBind) || (m.E.ColorStops != null && m.E.ColorStops.Count > 0 && m.E.ColorStops.Count <= 6);

        private static List<Color> StopColours(Node m) =>
            m.E.ColorStops.Select(c => DashColors.Parse(c.Color, Color.White)).GroupBy(c => DashColors.To565(c)).Select(g => g.First()).ToList();

        /// <summary>The picture of `n` for what's under it now (its own overlay, and those inside it that show) in the
        /// colours they have now, or null (a colour between two stops: drawn with rectangles).</summary>
        private ScreenTile ChosenPicture(Node n, out HashSet<int> with)
        {
            with = null;
            if (!pictureVariants.TryGetValue(n.Index, out var variants)) return null;
            var want = new HashSet<int>(pictureWith[n.Index]);
            foreach (var i in pictureInner[n.Index]) if (dynamic.First(x => x.Index == i).Visible) want.Add(i);
            foreach (var v in variants)
            {
                if (!v.With.SetEquals(want)) continue;
                bool same = true;
                foreach (var kv in v.Colours)
                {
                    var node = kv.Key == n.Index ? n : dynamic.First(x => x.Index == kv.Key);
                    if (DashColors.To565(node.Colour) != kv.Value) { same = false; break; }
                }
                if (same) { with = v.With; return v.Tile; }
            }
            return null;
        }

        /// <summary>Diagnostics (fxdash pictures): the shape pictures with their size and the overlay shapes in them.</summary>
        internal IEnumerable<object> PictureList()
        {
            var t = Tiles ?? EnableTiles();
            bool was = tilesOn; tilesOn = true;
            var seen = new HashSet<string>();
            var list = t.Pictures.Select(kv => (object)new
            {
                // bytes its files add (a file another picture already has counted once), and what drawing it smoothly with
                // fills would take instead (rectangles)
                uniqueBytes = (pictureVariants.TryGetValue(kv.Key, out var uv) ? uv.Select(v => v.Tile) : new[] { kv.Value }).Where(x => seen.Add(x.Name)).Sum(x => x.Jpeg.Length),
                fills = FillCount(dynamic.First(x => x.Index == kv.Key)),
                smoothFills = SmoothFills(dynamic.First(x => x.Index == kv.Key)),
                element = $"#{kv.Key} {def.Elements[kv.Key].Type} {def.Elements[kv.Key].Name}", bytes = kv.Value.Jpeg.Length,
                with = pictureWith.TryGetValue(kv.Key, out var w) ? w.Select(i => $"#{i} {def.Elements[i].Name}").ToList() : new List<string>(),
                inner = pictureInner.TryGetValue(kv.Key, out var inn) ? inn.Select(i => $"#{i} {def.Elements[i].Name}").ToList() : new List<string>(),
                area = picArea.TryGetValue(kv.Key, out var pa) ? $"{pa.X},{pa.Y} {pa.Width}x{pa.Height}" : null,
                variants = pictureVariants.TryGetValue(kv.Key, out var vs) ? vs.Count : 1,
                variantBytes = pictureVariants.TryGetValue(kv.Key, out var vb) ? vb.Sum(v => v.Tile.Jpeg.Length) : kv.Value.Jpeg.Length,
            }).ToList();
            tilesOn = was;
            foreach (var n in dynamic) { n.Px = null; n.PxKey = null; }
            return list;
        }

        private int FillCount(Node n)
        {
            EnsurePx(n);
            return MergeRects(n.Px, Math.Max(1, n.E.W), new Rectangle(0, 0, Math.Max(1, n.E.W), Math.Max(1, n.E.H))).Count(r => r[4] != Transparent);
        }

        /// <summary>A picture's file (made over the static layer and the shapes of its overlay) shows what fills would:
        /// exactly those shapes show under it, and text under it only where the picture covers it (its shape or those
        /// shapes' solid pixels: the file puts back the static layer everywhere else in its box).</summary>
        private bool PictureFits(Node n) => PictureFits(n, pictureWith.TryGetValue(n.Index, out var w) ? w : null);

        private bool PictureFits(Node n, HashSet<int> with)
        {
            var fitArea = PicArea(n);
            var cover = new List<Node> { n };
            var texts = new List<Rectangle>();
            foreach (var m in dynamic)
            {
                if (m.Index >= n.Index) break;
                if (!m.R.IntersectsWith(fitArea)) continue;
                bool inside = with != null && with.Contains(m.Index);
                if (!m.Visible) { if (inside) { whyNot = $"#{m.Index} isn't shown"; return false; } continue; } // a shape the file holds isn't there now
                // another state of the same icon (soft / medium / hard / wet in one spot): normally only one shows; when
                // several do (the demo can't tell, so all of them), the top one is what counts
                if (m.R == n.R && m.E.Type == "image" && Tiles.Pictures.ContainsKey(m.Index)) continue;
                if (inside) { cover.Add(m); continue; }
                if (m.Kind == "shape" || m.Kind == "bar" || m.Kind == "deltabar")
                {
                    // another overlay's shape under it is fine where the picture's own shapes drawn after it hide all of it
                    // (a setting pop-up under the lap summary that a summary arrow sits on)
                    if (m.Kind == "shape" && HiddenBy(m, Rectangle.Intersect(m.R, fitArea), cover.Where(c => c.Index > m.Index).Concat(with.Where(i => i > m.Index).Select(i => dynamic.First(x => x.Index == i))).Concat(new[] { n }).Distinct().ToList())) continue;
                    whyNot = $"#{m.Index} {m.Kind} {m.E.Name} under it"; return false;
                }
                if (m.TextAt.HasValue && !m.TextAt.Value.IntersectsWith(fitArea)) continue; // its box does, its text doesn't
                if (!m.Shown || (!m.TextAt.HasValue && m.Kind == "value")) continue; // not on the screen (covered): nothing to wipe
                texts.Add(Rectangle.Intersect(m.TextAt ?? m.R, fitArea));
            }
            // text under it: only where the picture's solid pixels (its own or its overlay's) cover all of it
            foreach (var area in texts)
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        bool covered = false;
                        foreach (var c in cover)
                        {
                            if (!c.R.Contains(x, y)) continue;
                            EnsurePx(c);
                            if (c.Px[(y - c.R.Y) * Math.Max(1, c.E.W) + (x - c.R.X)] != Transparent) { covered = true; break; }
                        }
                        if (!covered) { whyNot = $"text at {x},{y} not under it"; return false; }
                    }
            return true;
        }

        private string whyNot;

        /// <summary>Every pixel `m` draws in `area` lies under a solid pixel of one of `over` (shapes drawn after it).</summary>
        private bool HiddenBy(Node m, Rectangle area, List<Node> over)
        {
            EnsurePx(m);
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (m.Px[(y - m.R.Y) * Math.Max(1, m.E.W) + (x - m.R.X)] == Transparent) continue;
                    bool hidden = false;
                    foreach (var c in over)
                    {
                        if (!c.R.Contains(x, y)) continue;
                        EnsurePx(c);
                        if (c.Px[(y - c.R.Y) * Math.Max(1, c.E.W) + (x - c.R.X)] != Transparent) { hidden = true; break; }
                    }
                    if (!hidden) return false;
                }
            return true;
        }

        private bool DynamicUnder(Rectangle area, int index) =>
            dynamic.Any(m => m.Index < index && (m.Kind == "shape" || m.Kind == "bar" || m.Kind == "deltabar") && m.R.IntersectsWith(area));

        /// <summary>A repaint needing more fills than this (with tiles on) is drawn from tiles instead.</summary>
        public static int TileRepaintFills = 60;

        private void DrawTiles(Rectangle area)
        {
            var at = Tiles.AreaTiles.FirstOrDefault(t => t.R == area);
            if (at != null) { screen.Cmd(at.Name == null ? Fill(at.R.X, at.R.Y, at.R.Width, at.R.Height, at.Colour) : ScreenTiles.Ramv(at, dx, dy)); return; }
            foreach (var t in Tiles.GridTilesIn(area))
                screen.Cmd(t.Name == null ? Fill(t.R.X, t.R.Y, t.R.Width, t.R.Height, t.Colour) : ScreenTiles.Ramv(t, dx, dy));
        }

        // ---------- Shapes the screen draws itself (ScreenShapes) ----------

        /// <summary>Ovals and rounded boxes that come and go drawn with the screen's own smoothed commands (off: fills and
        /// pictures, as before).</summary>
        public static bool NativeShapes = true;

        /// <summary>`n` is an oval or a rounded box the screen can draw itself (its colours permitting, SendNative).</summary>
        private bool NativeCapable(Node n)
        {
            if (!NativeShapes || n.Kind != "shape") return false;
            var e = n.E;
            // only shapes that come and go, as with pictures: one always shown gets text redrawn on it all the time, and a
            // piece of its smoothed edge put back with fills is a fill a pixel (HALO's delta disk: 3x the bytes)
            if (e.Visible == null || e.Visible.Count == 0) return false;
            if (e.Type != "ellipse" && !(e.Type == "box" && e.Radius >= 2)) return false;
            if (e.W < 8 || e.H < 8 || Clip(n.R) != n.R) return false;
            if (e.Type == "ellipse") return e.Border <= 0 || (e.W - 2 * e.Border >= 8 && e.H - 2 * e.Border >= 8);
            if (e.Fill == null && e.Border <= 0) return false; // draws nothing
            return Math.Min(e.Radius, Math.Min(e.W, e.H) / 2) <= ScreenShapes.MaxCornerRadius;
        }

        /// <summary>A repaint of `area` draws all of `n` again (one smoothed shape) when it put back all of `n`'s box: drawn
        /// whole over pixels of it still there, its smoothed edge would blend over itself and darken.</summary>
        private static bool NativeRepaintWorthIt(Node n, Rectangle area) => area.Contains(n.R);

        /// <summary>Sends `n` drawn by the screen itself; false (nothing sent) when it can't be (see NativeCommands).</summary>
        /// <param name="clean">`n` is on the screen as drawn before (a colour change): its old smoothed edge is wiped first
        /// (see NativeCommands), else the new one would blend over it.</param>
        private bool SendNative(Node n, bool clean = false)
        {
            var cmds = NativeCommands(n, dx, dy, clean, out var rimNow);
            if (cmds == null) return false;
            n.DrawnRim = rimNow;
            NativeEvents++;
            Trace?.Invoke($"  #{n.Index} drawn by the screen ({cmds.Count} commands)");
            foreach (var c in cmds) screen.Cmd(c);
            return true;
        }

        /// <summary>The rectangles drawing `n`'s pixels in `area` takes.</summary>
        private int FillsIn(Node n, Rectangle area)
        {
            EnsurePx(n);
            var local = Rectangle.Intersect(area, n.R);
            if (local.Width <= 0 || local.Height <= 0) return 0;
            local.Offset(-n.R.X, -n.R.Y);
            return MergeRects(n.Px, Math.Max(1, n.E.W), local).Count(r => r[4] != Transparent);
        }

        /// <summary>Diagnostics (verify): shapes drawn by the screen itself.</summary>
        internal int NativeEvents;

        /// <summary>
        /// The commands that draw `n` as DrawShape draws it: the outer shape in its rim colour, then the inner one in its fill
        /// colour (the verified Mustang way). A ring with no fill gets the inside's colour from what's under it, and a
        /// see-through shape its colours mixed with it: null when that isn't one colour (drawn with fills then).
        /// </summary>
        private List<string> NativeCommands(Node n, int ox, int oy) => NativeCommands(n, ox, oy, false, out _);

        /// <summary>
        /// (`clean`: `n` was drawn before and is still on the screen, so its smoothed edges are wiped first, exactly: the
        /// same shape 2 px larger in the colour around that edge goes under the new one, its own edge falling where that
        /// colour is plain. Only the inside when only the inside's colour changed (a ring's fill: wiped with the rim's
        /// colour); else the whole shape, when what's around it is one colour.)
        /// </summary>
        private List<string> NativeCommands(Node n, int ox, int oy, bool clean, out int rimNow)
        {
            rimNow = -1;
            var e = n.E;
            Color Op(Color c) => DashColors.WithOpacity(c, e.Opacity);
            Color rim;
            Color? fill;
            int innerRadius = 0;
            if (e.Type == "ellipse")
            {
                fill = e.Fill == null ? (Color?)null : Op(DashColors.Parse(e.Fill, Color.Black));
                if (!string.IsNullOrEmpty(e.ColorBind) && e.Border > 0) fill = Op(n.Colour);
                rim = string.IsNullOrEmpty(e.ColorBind) || e.Border == 0 ? Op(n.Colour) : Op(DashColors.Parse(e.Color, Color.White));
            }
            else
            {
                rim = Op(string.IsNullOrEmpty(e.ColorBind) || e.Fill == null ? n.Colour : DashColors.Parse(e.Color, Color.White));
                fill = e.Fill == null ? (Color?)null : Op(string.IsNullOrEmpty(e.ColorBind) ? DashColors.Parse(e.Fill, Color.Black) : n.Colour);
                innerRadius = Math.Max(0, e.Radius - e.Border);
            }
            bool ring = e.Border > 0;
            if (e.Type == "box" && !ring) { if (!fill.HasValue) return null; rim = fill.Value; }
            var r = n.R;
            var inner = r; inner.Inflate(-e.Border, -e.Border);
            int? under = null;
            if (rim.A < 255 || (ring && fill.HasValue && fill.Value.A < 255))
                under = UnderUniform(n, r, Inside(e.Type, r, e.Radius, 0));
            else if (ring && !fill.HasValue)
                under = UnderUniform(n, inner, Inside(e.Type, inner, innerRadius, 1));
            if ((rim.A < 255 || (ring && (!fill.HasValue || fill.Value.A < 255))) && under == null) return null;
            int Solid(Color c)
            {
                if (c.A >= 255) return DashColors.To565(c);
                var u = ToColor(under.Value);
                int a = c.A;
                return DashColors.To565(Color.FromArgb((c.R * a + u.R * (255 - a)) / 255, (c.G * a + u.G * (255 - a)) / 255, (c.B * a + u.B * (255 - a)) / 255));
            }
            List<string> Shape(Rectangle b, int radius, int c) => e.Type == "ellipse"
                ? ScreenShapes.Ellipse(b.X + ox, b.Y + oy, b.Width, b.Height, c)
                : ScreenShapes.RoundedBox(b.X + ox, b.Y + oy, b.Width, b.Height, radius, c);
            int rimC = Solid(rim);
            rimNow = rimC;
            bool hasInner = ring && inner.Width > 0 && inner.Height > 0;
            int innerC = hasInner ? (fill.HasValue ? Solid(fill.Value) : under.Value) : 0;
            const int grow = 2;
            painted = (r, e.Radius);
            // (only when nothing else changed: nothing drawn over it since, nothing shown sits on it whose edge its wipe
            // would reach under)
            bool alone = !dynamic.Any(m => m.Index > n.Index && m.Kind == "shape" && m.Shown && m.Visible && m.R.IntersectsWith(n.R));
            if (clean && alone && !n.DamageAll && n.Damage.IsEmpty && hasInner && n.DrawnRim == rimC && e.Border >= 3)
            {
                // only the inside changed: it wiped with the rim's colour (grown into the rim), then drawn
                int g = Math.Min(grow, e.Border - 1);
                var wipe = Shape(Rectangle.Inflate(inner, g, g), innerRadius + g, rimC);
                var inside = Shape(inner, innerRadius, innerC);
                if (wipe != null && inside != null) { painted = (Rectangle.Inflate(inner, g, g), innerRadius + g); wipe.AddRange(inside); return wipe; }
            }
            var list = new List<string>();
            if (clean && e.Type == "box")
            {
                // a rounded box is smooth only in its corners: what's under each corner square put back first (exact fills)
                int cr = Math.Min(e.Radius, Math.Min(r.Width, r.Height) / 2);
                foreach (var c in new[] { new Rectangle(r.X, r.Y, cr, cr), new Rectangle(r.Right - cr, r.Y, cr, cr), new Rectangle(r.X, r.Bottom - cr, cr, cr), new Rectangle(r.Right - cr, r.Bottom - cr, cr, cr) })
                {
                    var cornerPx = Composite(c, n.Index);
                    if (cornerPx == null) continue;
                    foreach (var f in MergeRects(cornerPx, c.Width, new Rectangle(0, 0, c.Width, c.Height)))
                        list.Add(string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", c.X + f[0] + ox, c.Y + f[1] + oy, f[2], f[3], f[4]));
                }
            }
            else if (clean)
            {
                // the whole shape wiped with the one colour around its edge (when it is one)
                var big = Rectangle.Inflate(r, grow, grow);
                var inBig = Inside(e.Type, big, e.Radius + grow, 0);
                var inSmall = Inside(e.Type, r, e.Radius, 1); // (what's under its edge pixels matters; further in, the shape covers it)
                var around = UnderUniform(n, big, (x, y) => inBig(x, y) && !inSmall(x, y));
                var wipe = around.HasValue && Clip(big) == big ? Shape(big, e.Radius + grow, around.Value) : null;
                if (wipe != null) { list.AddRange(wipe); painted = (big, e.Radius + grow); }
            }
            var outer = Shape(r, e.Radius, rimC);
            if (outer == null) return null;
            list.AddRange(outer);
            if (hasInner)
            {
                var inside = Shape(inner, innerRadius, innerC);
                if (inside == null) return null;
                list.AddRange(inside);
            }
            return list;
        }

        private PreviewScreen nativeCanvas;

        /// <summary>The shape (box, corner radius) the last NativeCommands paints whole: what's in it is drawn afresh.</summary>
        private (Rectangle R, int Radius) painted;

        /// <summary>
        /// After `n` was drawn by the screen itself: what's over it is drawn again (MarkAbove), and a shape over it whose edge
        /// lies inside what `n` painted counts as drawn over all of it (its pixels are gone: drawn plainly); any other only
        /// partly (its old edge may be there still: wiped first).
        /// </summary>
        private void MarkAboveNative(Node n)
        {
            var (pr, prad) = painted;
            var inPainted = Inside(n.E.Type, pr, prad, 0);
            var area = Rectangle.Union(n.R, pr);
            foreach (var m in dynamic)
            {
                if (m.Index <= n.Index || !m.Shown || !m.Visible) continue;
                if (!(IsText(m) && m.TextAt.HasValue ? Ink(m) : m.R).IntersectsWith(area)) continue;
                Invalidate(m);
                if (m.Kind != "shape" || !NativeCapable(m)) continue;
                // its outline grown by a pixel (where its smoothed edge is), all inside what was painted
                var big = Rectangle.Inflate(m.R, 1, 1);
                var inBig = Inside(m.E.Type, big, m.E.Radius + 1, 0);
                var inSmall = Inside(m.E.Type, m.R, m.E.Radius, 2);
                bool covered = true;
                for (int y = big.Top; y < big.Bottom && covered; y++)
                    for (int x = big.Left; x < big.Right; x++)
                        if (inBig(x, y) && !inSmall(x, y) && !inPainted(x, y)) { covered = false; break; }
                if (covered) { m.DamageAll = false; m.Damage = m.R; }
            }
        }

        /// <summary>
        /// What the screen shows over `n`'s box when it draws `n` itself (its commands run on a preview screen over what's
        /// under it), and what was there before; null when it can't be drawn that way. ShapePixels takes its colours from
        /// it, so the model holds what the wheel was sent (a corner's `cirs`, not GDI's arc) and a part put back with fills
        /// matches.
        /// </summary>
        private (int[] Shown, int[] Under)? NativeRender(Node n)
        {
            var cmds = NativeCommands(n, 0, 0);
            if (cmds == null) return null;
            var area = n.R;
            if (nativeCanvas == null) nativeCanvas = new PreviewScreen();
            var under = Composite(area, n.Index);
            nativeCanvas.Put(under, area);
            foreach (var c in cmds) nativeCanvas.Cmd(c);
            return (nativeCanvas.Get(area), under);
        }

        /// <summary>Which pixels of `n`'s box its own commands paint whatever is under them (over black and over white
        /// alike): its smoothed edge isn't solid, it mixes with what's there. Null when it isn't drawn that way.</summary>
        private bool[] NativeSolid(Node n)
        {
            var cmds = NativeCommands(n, 0, 0);
            if (cmds == null || n.Px == null) return null;
            var area = n.R;
            if (nativeCanvas == null) nativeCanvas = new PreviewScreen();
            int len = area.Width * area.Height;
            int[] Render(int bg)
            {
                nativeCanvas.Put(Enumerable.Repeat(bg, len).ToArray(), area);
                foreach (var c in cmds) nativeCanvas.Cmd(c);
                return nativeCanvas.Get(area);
            }
            var overBlack = Render(0);
            var overWhite = Render(0xFFFF);
            var solid = new bool[len];
            for (int i = 0; i < len && i < n.Px.Length; i++) solid[i] = n.Px[i] != Transparent && overBlack[i] == overWhite[i];
            return solid;
        }

        /// <summary>Whether a pixel (screen coordinates) lies inside an oval or a rounded box, `shrink` px in from its edge.</summary>
        private static Func<int, int, bool> Inside(string type, Rectangle b, int radius, int shrink)
        {
            if (type == "ellipse")
            {
                double cx = b.X + b.Width / 2.0, cy = b.Y + b.Height / 2.0, ra = b.Width / 2.0 - shrink, rb = b.Height / 2.0 - shrink;
                return (x, y) => ra > 0 && rb > 0 && Math.Pow((x + 0.5 - cx) / ra, 2) + Math.Pow((y + 0.5 - cy) / rb, 2) <= 1;
            }
            var s = Rectangle.Inflate(b, -shrink, -shrink);
            int rr = Math.Max(0, Math.Min(radius, Math.Min(s.Width, s.Height) / 2));
            return (x, y) =>
            {
                if (!s.Contains(x, y)) return false;
                double qx = x + 0.5 < s.X + rr ? s.X + rr : x + 0.5 > s.Right - rr ? s.Right - rr : x + 0.5;
                double qy = y + 0.5 < s.Y + rr ? s.Y + rr : y + 0.5 > s.Bottom - rr ? s.Bottom - rr : y + 0.5;
                return (x + 0.5 - qx) * (x + 0.5 - qx) + (y + 0.5 - qy) * (y + 0.5 - qy) <= rr * rr;
            };
        }

        /// <summary>The one colour under `n` (static layer and what's shown before it) at the pixels of `area` that `inside`
        /// takes in, or null.</summary>
        private int? UnderUniform(Node n, Rectangle area, Func<int, int, bool> inside)
        {
            area = Clip(area);
            var px = Composite(area, n.Index);
            if (px == null) return null;
            int? c = null;
            for (int y = 0; y < area.Height; y++)
                for (int x = 0; x < area.Width; x++)
                {
                    if (!inside(area.X + x, area.Y + y)) continue;
                    int p = px[y * area.Width + x];
                    if (c == null) c = p;
                    else if (p != c) return null;
                }
            return c;
        }

        // ---------- Colours and fonts ----------

        public static int Rgb565(string hex) => DashColors.To565(DashColors.Parse(hex, Color.Black));

        public static Color ToColor(int c565) => DashColors.From565(c565);

        private static int Align(string a) => a == "center" ? 1 : a == "right" ? 2 : 0;

        public static int FontHeight(int font) => font >= 0 && font < FontMetrics.Fonts.Length ? FontMetrics.Fonts[font][0] : 0;

        /// <summary>Width the screen draws `text` at in `font`, or -1 if the font lacks a glyph.</summary>
        public static int TextWidth(int font, string text)
        {
            if (font < 0 || font >= FontMetrics.Fonts.Length) return -1;
            int w = 0;
            foreach (char ch in text)
            {
                int g = ch >= 32 && ch < 127 ? FontMetrics.Fonts[font][ch - 31] : -1;
                if (g < 0) return -1;
                w += g;
            }
            return w;
        }

        private static Rectangle Clip(Rectangle r) => Rectangle.Intersect(r, new Rectangle(0, 0, Width, Height));

        // ---------- Shapes ----------

        private void LoadImages()
        {
            if (def.Images == null) return;
            foreach (var kv in def.Images)
            {
                try
                {
                    var bytes = Convert.FromBase64String(kv.Value);
                    using (var ms = new MemoryStream(bytes))
                    using (var bmp = new Bitmap(ms))
                        images[kv.Key] = new Bitmap(bmp); // detached from the stream
                }
                catch { }
            }
        }

        /// <summary>Draws one shape into g, with its top-left at `origin`.</summary>
        private void DrawShape(Graphics g, DashElement e, Point origin, Color colour)
        {
            var r = new Rectangle(origin.X, origin.Y, e.W, e.H);
            if (r.Width <= 0 || r.Height <= 0) return;
            Color Op(Color c) => DashColors.WithOpacity(c, e.Opacity);
            switch (e.Type)
            {
                case "rect":
                    using (var br = new SolidBrush(Op(colour))) g.FillRectangle(br, r);
                    break;
                case "ellipse":
                {
                    var fill = e.Fill == null ? (Color?)null : Op(DashColors.Parse(e.Fill, Color.Black));
                    if (!string.IsNullOrEmpty(e.ColorBind) && e.Border > 0) fill = Op(colour); // data colour fills a ringed ellipse
                    var rim = string.IsNullOrEmpty(e.ColorBind) || e.Border == 0 ? Op(colour) : Op(DashColors.Parse(e.Color, Color.White));
                    if (e.Border <= 0) { using (var br = new SolidBrush(rim)) g.FillEllipse(br, r); break; }
                    var inner = r; inner.Inflate(-e.Border, -e.Border);
                    if (fill.HasValue && fill.Value.A == 255 && rim.A == 255)
                    {
                        // the verified Mustang way: rim colour over the whole ellipse, the inside on top
                        using (var br = new SolidBrush(rim)) g.FillEllipse(br, r);
                        using (var br = new SolidBrush(fill.Value)) g.FillEllipse(br, inner);
                    }
                    else
                    {
                        using (var ring = new GraphicsPath(FillMode.Alternate))
                        {
                            ring.AddEllipse(r); ring.AddEllipse(inner);
                            using (var br = new SolidBrush(rim)) g.FillPath(br, ring);
                        }
                        if (fill.HasValue) using (var br = new SolidBrush(fill.Value)) g.FillEllipse(br, inner);
                    }
                    break;
                }
                case "box":
                {
                    var border = Op(string.IsNullOrEmpty(e.ColorBind) || e.Fill == null ? colour : DashColors.Parse(e.Color, Color.White));
                    var fill = e.Fill == null ? (Color?)null : Op(string.IsNullOrEmpty(e.ColorBind) ? DashColors.Parse(e.Fill, Color.Black) : colour);
                    var inner = r; inner.Inflate(-e.Border, -e.Border);
                    int ir = Math.Max(0, e.Radius - e.Border);
                    if (e.Border > 0)
                    {
                        using (var ring = new GraphicsPath(FillMode.Alternate))
                        {
                            ring.AddPath(Rounded(r, e.Radius), false);
                            if (inner.Width > 0 && inner.Height > 0) ring.AddPath(Rounded(inner, ir), false);
                            using (var br = new SolidBrush(border)) g.FillPath(br, ring);
                        }
                    }
                    if (fill.HasValue && inner.Width > 0 && inner.Height > 0)
                        using (var p = Rounded(e.Border > 0 ? inner : r, e.Border > 0 ? ir : e.Radius)) using (var br = new SolidBrush(fill.Value)) g.FillPath(br, p);
                    break;
                }
                case "gradient":
                {
                    var stops = (e.Colors ?? new List<string>()).Select(c => Op(DashColors.Parse(c, Color.Black))).ToList();
                    if (stops.Count == 0) stops.Add(Op(colour));
                    if (stops.Count == 1) stops.Add(stops[0]);
                    using (var br = new LinearGradientBrush(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), stops[0], stops[stops.Count - 1], (float)e.Angle))
                    {
                        br.InterpolationColors = new ColorBlend(stops.Count)
                        {
                            Colors = stops.ToArray(),
                            Positions = Enumerable.Range(0, stops.Count).Select(i => (float)i / (stops.Count - 1)).ToArray(),
                        };
                        using (var p = Rounded(r, e.Radius)) g.FillPath(br, p);
                    }
                    if (e.Border > 0)
                        using (var ring = new GraphicsPath(FillMode.Alternate))
                        {
                            var inner = r; inner.Inflate(-e.Border, -e.Border);
                            ring.AddPath(Rounded(r, e.Radius), false);
                            if (inner.Width > 0 && inner.Height > 0) ring.AddPath(Rounded(inner, Math.Max(0, e.Radius - e.Border)), false);
                            using (var br = new SolidBrush(Op(DashColors.Parse(e.Color, Color.Gray)))) g.FillPath(br, ring);
                        }
                    break;
                }
                case "image":
                    if (e.Image != null && images.TryGetValue(e.Image, out var img))
                    {
                        var old = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        using (var attrs = new ImageAttributes())
                        {
                            if (e.Opacity < 100)
                                attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0, e.Opacity) / 100f });
                            g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
                        }
                        g.InterpolationMode = old;
                    }
                    break;
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>The shapes that never change, in order, on black; images reduced to their colour budget.</summary>
        private int[] BuildStatic()
        {
            using (var bmp = BuildStaticBitmap(false)) return Pixels(bmp);
        }

        /// <summary>
        /// The static layer as a bitmap. For fills (`full` false): hard edges, pictures reduced to MaxColors, so rectangles
        /// merge. For tiles (`full` true): anti-aliased shapes and pictures in full colour (drawn as pictures, size
        /// doesn't depend on the colours).
        /// </summary>
        private Bitmap BuildStaticBitmap(bool full)
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = full ? SmoothingMode.AntiAlias : SmoothingMode.None; // fills: exact colours, so rectangles merge
                if (full) g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Black);
                foreach (var e in def.Elements)
                    if (IsShape(e) && !e.IsDynamic)
                    {
                        DrawShape(g, e, new Point(e.X, e.Y), DashColors.Parse(e.Color, Color.White));
                        if (e.Type == "image" && !full) Quantize(bmp, Clip(new Rectangle(e.X, e.Y, e.W, e.H)), e.MaxColors);
                    }
            }
            return bmp;
        }

        private static bool IsShape(DashElement e) => e.Type == "rect" || e.Type == "ellipse" || e.Type == "box" || e.Type == "gradient" || e.Type == "image";

        /// <summary>Reduces an area of the bitmap to its `max` most common colours (fewer colours = fewer fills).</summary>
        private static void Quantize(Bitmap bmp, Rectangle area, int max)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            max = Math.Max(2, Math.Min(64, max));
            var data = bmp.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var px = new int[data.Width * data.Height];
                for (int y = 0; y < data.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * data.Width, data.Width);
                // popularity on a 4-bit-per-channel grid, then each pixel to the nearest chosen colour. Hard edges, as the
                // rest of the rectangle drawing: a pixel under half covered stays transparent, the others are opaque in a
                // chosen colour (a transparent background, black underneath, used to be picked as a colour and written back
                // opaque: a black square around a logo; partly covered edge pixels kept see-through blend into one-off
                // colours, many more rectangles).
                var counts = new Dictionary<int, int>();
                foreach (var p in px)
                {
                    if (((p >> 24) & 255) < 128) continue;
                    int k = ((p >> 20) & 0xF) << 8 | ((p >> 12) & 0xF) << 4 | ((p >> 4) & 0xF); counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
                }
                if (counts.Count == 0) return;
                var palette = counts.OrderByDescending(kv => kv.Value).Take(max)
                    .Select(kv => (R: ((kv.Key >> 8) & 0xF) * 17, G: ((kv.Key >> 4) & 0xF) * 17, B: (kv.Key & 0xF) * 17)).ToArray();
                for (int i = 0; i < px.Length; i++)
                {
                    if (((px[i] >> 24) & 255) < 128) { px[i] = 0; continue; }
                    int r = (px[i] >> 16) & 255, g = (px[i] >> 8) & 255, b = px[i] & 255, best = 0, bestD = int.MaxValue;
                    for (int k = 0; k < palette.Length; k++)
                    {
                        int d = (r - palette[k].R) * (r - palette[k].R) + (g - palette[k].G) * (g - palette[k].G) + (b - palette[k].B) * (b - palette[k].B);
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    px[i] = unchecked((int)0xFF000000) | palette[best].R << 16 | palette[best].G << 8 | palette[best].B;
                }
                for (int y = 0; y < data.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(px, y * data.Width, data.Scan0 + y * data.Stride, data.Width);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static int[] Pixels(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width, bmp.Width);
            bmp.UnlockBits(data);
            for (int i = 0; i < px.Length; i++)
                px[i] = (((px[i] >> 16) & 255) >> 3 << 11) | (((px[i] >> 8) & 255) >> 2 << 5) | ((px[i] & 255) >> 3);
            return px;
        }

        /// <summary>A dynamic shape's own pixels (its box) for a colour, blended over the static layer; Transparent where
        /// it draws nothing.</summary>
        private int[] ShapePixels(Node n, Color colour)
        {
            var e = n.E;
            int w = Math.Max(1, e.W), h = Math.Max(1, e.H);
            var native = colour == n.Colour && NativeCapable(n) ? NativeRender(n) : null;
            // with tiles, a shape that has pictures on the screen looks the same drawn with fills (part of it, or when no
            // picture fits): anti-aliased, its edge blended over what's under it now (the static layer and the shapes shown
            // there), exact colours inside so the rows merge into few fills
            // (a shape the screen draws itself is smoothed by the screen, over whatever is under it)
            bool smooth = tilesOn && (HasPictures(n) || smoothShapes.Contains(n.Index)) || NativeCapable(n);
            int[] underPx = smooth ? Composite(n.R, n.Index) : null;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var under = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = smooth ? SmoothingMode.AntiAlias : SmoothingMode.None;
                    g.Clear(Color.Transparent);
                    DrawShape(g, e, Point.Empty, colour);
                }
                if (e.Type == "image" && !smooth) Quantize(bmp, new Rectangle(0, 0, w, h), e.MaxColors);
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var argb = new int[w * h];
                for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * w, w);
                bmp.UnlockBits(data);
                var px = new int[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = argb[y * w + x], a = (p >> 24) & 255;
                        if (a < 8) { px[y * w + x] = Transparent; continue; }
                        if (underPx != null && a < 250 && x < n.R.Width && y < n.R.Height)
                        {
                            var bgc = ToColor(underPx[y * n.R.Width + x]);
                            int rr = (((p >> 16) & 255) * a + bgc.R * (255 - a)) / 255, gg2 = (((p >> 8) & 255) * a + bgc.G * (255 - a)) / 255, bb = ((p & 255) * a + bgc.B * (255 - a)) / 255;
                            px[y * w + x] = (rr >> 3 << 11) | (gg2 >> 2 << 5) | (bb >> 3);
                            continue;
                        }
                        int r = (p >> 16) & 255, gg = (p >> 8) & 255, b = p & 255;
                        if (a < 250)
                        {
                            int sx = e.X + x, sy = e.Y + y;
                            var bg = sx >= 0 && sy >= 0 && sx < Width && sy < Height ? ToColor(staticPx[sy * Width + sx]) : Color.Black;
                            r = (r * a + bg.R * (255 - a)) / 255; gg = (gg * a + bg.G * (255 - a)) / 255; b = (b * a + bg.B * (255 - a)) / 255;
                        }
                        px[y * w + x] = (r >> 3 << 11) | (gg >> 2 << 5) | (b >> 3);
                    }
                // drawn by the screen itself: its colours as the commands draw them; see-through where the shape is (a ring's
                // middle stays so, the screen just puts back what's there) unless the commands changed the pixel (their edge)
                if (native != null && native.Value.Shown.Length == px.Length)
                    for (int i = 0; i < px.Length; i++)
                        px[i] = px[i] == Transparent && native.Value.Shown[i] == native.Value.Under[i] ? Transparent : native.Value.Shown[i];
                return px;
            }
        }

        /// <summary>The colour most of an area of the static layer (everything always drawn) has, RGB565; null off screen.</summary>
        public int? CommonStaticColour(Rectangle area) => CommonStaticColour(area, out _);

        /// <summary>As above, with the share of the area (0-1) that colour has.</summary>
        public int? CommonStaticColour(Rectangle area, out double share)
        {
            share = 0;
            area = Clip(area);
            if (area.Width <= 0 || area.Height <= 0) return null;
            var n = new Dictionary<int, int>();
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int c = staticPx[y * Width + x];
                    n[c] = n.TryGetValue(c, out var k) ? k + 1 : 1;
                }
            var top = n.OrderByDescending(kv => kv.Value).First();
            share = top.Value / (double)(area.Width * area.Height);
            return top.Key;
        }

        /// <summary>How many fills redrawing an area of the static layer takes (what a value with no Background costs per change).</summary>
        public int StaticFillsIn(Rectangle area)
        {
            area = Clip(area);
            return area.Width > 0 && area.Height > 0 ? MergeRects(staticPx, Width, area).Count : 0;
        }

        /// <summary>The one colour of an area, or null if it has several.</summary>
        private static int? Uniform(int[] px, int stride, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            int c = px[area.Top * stride + area.Left];
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (px[y * stride + x] != c) return null;
            return c;
        }

        /// <summary>An RGB565 image area as rectangles {x, y, w, h, colour}: runs along each row, identical runs stacked.</summary>
        public static List<int[]> MergeRects(int[] px, int stride, Rectangle area)
        {
            var open = new Dictionary<long, int[]>();
            var done = new List<int[]>();
            for (int y = area.Top; y < area.Bottom; y++)
            {
                var next = new Dictionary<long, int[]>();
                int x = area.Left;
                while (x < area.Right)
                {
                    int c = px[y * stride + x], x0 = x;
                    while (x < area.Right && px[y * stride + x] == c) x++;
                    long key = ((long)x0 << 40) | ((long)(x - x0) << 20) | (uint)(c & 0xFFFFF);
                    if (c >= 0 && open.TryGetValue(key, out var rect)) { rect[3]++; open.Remove(key); }
                    else rect = new[] { x0, y, x - x0, 1, c };
                    next[key] = rect;
                }
                done.AddRange(open.Values);
                open = next;
            }
            done.AddRange(open.Values);
            return done;
        }

        private string Fill(int x, int y, int w, int h, int c) =>
            string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", x + dx, y + dy, w, h, c);

        /// <summary>Fills for an area of an RGB565 array whose (0,0) is at (ox, oy) on the screen; `skip` left out.</summary>
        private void SendFills(int[] px, int stride, Rectangle area, int skip, int ox, int oy)
        {
            foreach (var r in MergeRects(px, stride, area))
                if (r[4] != skip && r[4] != Transparent) screen.Cmd(Fill(r[0] + ox, r[1] + oy, r[2], r[3], r[4]));
        }

        // sta (TJC): 1 = solid background, 3 = none.
        private string Xstr(Rectangle r, int font, int colour, int bg, int xcen, int sta, string text)
        {
            string head = string.Format(CultureInfo.InvariantCulture, "xstr {0},{1},{2},{3},{4},{5},{6},{7},1,{8},",
                r.X + dx, r.Y + dy, r.Width, r.Height, font, colour, bg, xcen, sta);
            // a screen command is at most 58 characters: longer text is cut (`check` reports texts that would be)
            string t = Clean(text);
            int room = MaxCommand - head.Length - 2;
            if (t.Length > room) t = t.Substring(0, Math.Max(0, room));
            return head + "\"" + t + "\"";
        }

        public const int MaxCommand = 58;

        /// <summary>Quotes would end the command; anything outside ASCII has no glyph.</summary>
        internal static string Clean(string s) => new string((s ?? "").Where(c => c >= 32 && c < 127 && c != '"').ToArray());

        // ---------- Drawing ----------

        /// <summary>Page 0 (no screen timers), its objects hidden, cleared, then the static layer.</summary>
        public void DrawAll()
        {
            screen.Cmd("page 0");
            screen.Cmd("vis 255,0");
            screen.Cmd("cls 0");
            if (tilesOn) DrawTiles(new Rectangle(0, 0, Width, Height));
            else SendFills(staticPx, Width, new Rectangle(0, 0, Width, Height), 0, 0, 0);
            foreach (var l in staticLabels) DrawLabel(l, l.Colour);
            foreach (var n in dynamic) { n.Shown = false; n.Sent = null; n.TextAt = null; n.BandAt = false; if (n.SegSent != null) for (int k = 0; k < n.SegSent.Length; k++) n.SegSent[k] = null; }
            foreach (var p in popups) { p.Until = -1; p.Last.Clear(); }
            screen.Flush();
        }

        private void DrawLabel(Node n, Color colour)
        {
            var bg = n.E.Background != null ? Rgb565(n.E.Background) : 0;
            screen.Cmd(Xstr(n.R, n.E.Font, DashColors.To565(colour), bg, n.XCen, 3, n.Text));
        }

        /// <summary>Diagnostics (tools): when set, how often each element was drawn by Update.</summary>
        internal Dictionary<DashElement, int> DrawCounts;

        /// <summary>Diagnostics (tools): told what is drawn and why, before its commands.</summary>
        internal Action<string> Trace;

        /// <summary>Redraws what changed. `now` in seconds.</summary>
        public void Update(DashValues v, double now)
        {
            current = v;
            foreach (var p in popups) CheckPopup(p, v, now);

            // What each dynamic element wants to show now
            foreach (var n in dynamic) Evaluate(n, v);
            int dim = 0;
            foreach (var n in dims) { Evaluate(n, v); if (n.Visible) dim = Math.Max(dim, Math.Max(0, Math.Min(95, n.E.Opacity))); }
            DimPercent = dim;

            // Something over (nearly) the whole screen went (an ignition-off or start-up screen): the dash drawn again from
            // a cleared screen, as when it starts (cheaper than putting it back piece by piece: black isn't sent)
            if (!PopupShowing && dynamic.Any(n => n.Shown && !n.Visible && n.Kind == "shape" && Clip(n.R).Width * Clip(n.R).Height >= 0.9 * Width * Height && !CoveredAbove(n.Index, n.R)))
            {
                Trace?.Invoke("full redraw: a screen-sized overlay went");
                screen.Cmd("cls 0");
                if (tilesOn) DrawTiles(new Rectangle(0, 0, Width, Height));
                else SendFills(staticPx, Width, new Rectangle(0, 0, Width, Height), 0, 0, 0);
                foreach (var l in staticLabels) DrawLabel(l, l.Colour);
                foreach (var n in dynamic) { n.Shown = false; n.Sent = null; n.TextAt = null; n.BandAt = false; n.BarAt = null; if (n.SegSent != null) for (int k = 0; k < n.SegSent.Length; k++) n.SegSent[k] = null; }
            }

            // Hidden since last time: repaint their areas (this also marks what's under/over them for a redraw). An area
            // already put back by this pass (an overlay's box and the shapes and texts on it go together) isn't again.
            var repainted = new List<Rectangle>();
            foreach (var n in dynamic)
            {
                if (!n.Shown || n.Visible) continue;
                var was = IsText(n) && n.TextAt.HasValue ? Ink(n) : n.R;
                n.Shown = false; n.Sent = null; n.TextAt = null; n.BarAt = null;
                // under a solid shape drawn after it that stays: nothing on the screen changes
                if (CoveredAbove(n.Index, was)) continue;
                // text exactly over text with its own background (a pop-up copy of a value): that one is drawn again in
                // one step over it
                var under = IsText(n) ? dynamic.LastOrDefault(m => m.Index < n.Index && SolidText(m) && m.TextAt.Value.Contains(was)) : null;
                if (under != null) { Invalidate(under); continue; }
                // only what no solid shape above it covers (a pop-up going from under another one); a frame with
                // nothing inside (an alert border round the screen) only where its border was
                // (text: only where its ink was, not its whole box)
                foreach (var area in IsText(n) ? new List<Rectangle> { was } : Outline(n))
                    foreach (var part in Subtract(Clip(area), SolidShapesAbove(n.Index)))
                    {
                        if (repainted.Any(r => r.Contains(part))) continue;
                        var done = Repaint(part);
                        if (!done.IsEmpty) repainted.Add(done);
                    }
            }
            foreach (var p in popups)
                if (p.Until >= 0 && now >= p.Until)
                {
                    p.Until = -1; PopupEvents++; Repaint(p.R);
                    // what it only partly covered wasn't drawn at all while it showed (outside it too): drawn whole now
                    foreach (var n in dynamic)
                        if (n.Shown && n.R.IntersectsWith(p.R) && !p.R.Contains(n.R)) { Invalidate(n); if (n.Kind == "bar") n.BarAt = null; }
                }

            // Draw in element order
            foreach (var n in dynamic)
            {
                if (!n.Visible || Covered(n.R)) continue;
                if (DrawCounts != null && !(n.Shown && n.Sent == n.Key)) DrawCounts[n.E] = (DrawCounts.TryGetValue(n.E, out var dc) ? dc : 0) + 1;
                if (Trace != null && !(n.Shown && n.Sent == n.Key)) Trace($"#{n.Index} {n.Kind} {n.E.Name}");
                switch (n.Kind)
                {
                    case "shape":
                        if (n.Shown && n.Sent == n.Key) break;
                        // a pop-up under another one that shows: only what isn't covered is drawn (no flash of the
                        // covered part), and only what's over the drawn parts is drawn again
                        var oldPx = n.Shown ? n.DrawnPx : null;
                        EnsurePx(n); // known even when nothing of it shows (what's over it is drawn on it)
                        // changed to a look with see-through pixels where it had colour (a fill that goes transparent):
                        // what's under those is drawn back first, else the old colour would stay
                        if (oldPx != null && oldPx != n.Px && oldPx.Length == n.Px.Length)
                        {
                            bool uncovered = false;
                            for (int i = 0; i < n.Px.Length && !uncovered; i++) uncovered = n.Px[i] == Transparent && oldPx[i] != Transparent;
                            if (uncovered) Repaint(n.R, n.Index);
                        }
                        // a tile changing colour under text drawn with its own background (a temperature tile): the tile
                        // is filled around that text, and the text drawn again once on the new colour (filling over it
                        // first would wipe it for a moment: a flash)
                        var keep = new List<Node>();
                        if (oldPx != null)
                            foreach (var m in dynamic)
                                if (m.Index > n.Index && SolidText(m) && !Covered(m.R) && n.R.Contains(m.TextAt.Value)) keep.Add(m);
                        var skip = SolidShapesAbove(n.Index);
                        skip.AddRange(keep.Select(m => m.TextAt.Value));
                        // a picture with its own file on the screen, nothing over it: one command
                        // a picture is drawn whole: fine under solid shapes still to be drawn this update (they go on top
                        // right after, nothing shows in between), not under ones already on the screen (they'd be wiped)
                        var picAt = PicArea(n);
                        var textBlocker = keep.FirstOrDefault(m => m.Sent == m.Key && m.TextAt.Value.IntersectsWith(picAt)) ??
                                          dynamic.FirstOrDefault(m => m.Index > n.Index && SolidText(m) && m.Sent == m.Key && !Covered(m.R) && picAt != n.R && m.TextAt.Value.IntersectsWith(picAt) && !n.R.Contains(m.TextAt.Value));
                        // (text over a shape that's costly with rectangles: the picture anyway, the text drawn again after it)
                        bool overText = textBlocker != null && tilesOn && pictureVariants.ContainsKey(n.Index) && CostlyWithFills(n);
                        var blocker = (overText ? null : textBlocker) ??
                                      dynamic.FirstOrDefault(m => m.Index > n.Index && m.Kind == "shape" && m.Visible && m.Shown && m.Sent == m.Key && !Covered(m.R)
                                                                  && m.R.IntersectsWith(picAt) && skip.Contains(Clip(m.R)));
                        bool overDrawn = blocker != null;
                        // an oval or a rounded box the screen draws itself, whole, when nothing solid over it is on the
                        // screen (text with its own background on it is drawn again right after, only for costly shapes)
                        // (as a picture: not under a solid shape already on the screen; shapes and text over it are drawn
                        // again after it, all before the screen shows the frame)
                        var shapeBlocker = NativeCapable(n) ? dynamic.FirstOrDefault(m => m.Index > n.Index && m.Kind == "shape" && m.Visible && m.Shown && m.Sent == m.Key
                                                                                        && !Covered(m.R) && m.R.IntersectsWith(n.R) && skip.Contains(Clip(m.R))) : null;
                        bool textOn = keep.Count > 0 || textBlocker != null;
                        // still on the screen as drawn, only part of it drawn over since (a repaint under a corner): that part
                        // with fills of its pixels (drawn again whole, its smoothed edge would blend over itself and darken)
                        if (NativeCapable(n) && n.Shown && n.DrawnKey == n.Key && !n.DamageAll && !n.Damage.IsEmpty && !n.Damage.Contains(n.R)
                            && FillsIn(n, n.Damage) <= TileRepaintFills) // (else all of it again, below: fewer bytes)
                        {
                            Trace?.Invoke($"  #{n.Index} drawn again where it was drawn over: {n.Damage}");
                            foreach (var part in Subtract(Clip(n.Damage), skip)) { DrawShapeNode(n, part); MarkAbove(n, part); }
                            foreach (var m in keep) Invalidate(m);
                            n.Shown = true; n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                            break;
                        }
                        bool onScreen = n.Shown && !(!n.DamageAll && n.Damage.Contains(n.R));
                        if (NativeCapable(n) && shapeBlocker == null && (!textOn || CostlyWithFills(n)) && SendNative(n, clean: onScreen))
                        {
                            if (textOn) PictureOverTextEvents++; // (as a picture over text: the text drawn again after it)
                            MarkAboveNative(n);
                            foreach (var m in keep) Invalidate(m);
                            n.Shown = true; n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                            break;
                        }
                        if (LeftToPicture(n)) { EnsurePx(n); n.Shown = true; n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty; break; }
                        HashSet<int> picWith = null;
                        var picTile = tilesOn ? ChosenPicture(n, out picWith) : null;
                        if (Trace != null && tilesOn && Tiles.Pictures.ContainsKey(n.Index))
                            Trace($"  picture #{n.Index}: {(picTile == null ? "no picture for what's under it" : overDrawn ? $"#{blocker.Index} {blocker.E.Name} over it, on the screen" : PictureFits(n, picWith) ? "drawn from its file" : "what's under it differs: " + whyNot)}");
                        if (picTile != null && !overDrawn && PictureFits(n, picWith))
                        {
                            screen.Cmd(ScreenTiles.Ramv(picTile, dx, dy));
                            MarkAbove(n, picAt);
                            if (overText) { PictureOverTextEvents++; foreach (var m in keep) Invalidate(m); }
                            n.Shown = true; n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                            break;
                        }
                        foreach (var part in Subtract(Clip(n.R), skip))
                        {
                            DrawShapeNode(n, part);
                            MarkAbove(n, part);
                        }
                        foreach (var m in keep) Invalidate(m);
                        n.Shown = true; n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                        break;
                    case "label":
                        // a label that changes (colour, shown/hidden) or is drawn over something that changes: drawn like
                        // a value (its text band in one step where it can be)
                        if (n.Shown && n.Sent == n.Key) break;
                        if (Occluded(n, TextExtent(n, n.Text))) break;
                        DrawValue(n);
                        n.Shown = true; n.Sent = n.Key;
                        MarkAbove(n, n.TextAt ?? n.R);
                        break;
                    case "value":
                        if (n.Shown && n.Sent == n.Key) break;
                        // under a solid element drawn after it (a pop-up box): nothing would show; drawn once uncovered
                        if (Occluded(n, TextExtent(n, n.Text))) break;
                        DrawValue(n);
                        n.Shown = true; n.Sent = n.Key;
                        MarkAbove(n, n.TextAt ?? n.R);
                        break;
                    case "bar":
                        if (n.Shown && n.Sent == n.Key) break;
                    {
                        // Only the level changed: just the part between the old and the new level is drawn, and only
                        // what's over that part is drawn again (text on a bar would otherwise blink at every change).
                        var changed = n.R;
                        if (n.Shown && n.BarAt.HasValue && n.BarColour == n.Colour) changed = BarRange(n, n.BarAt.Value, n.FillEnd);
                        // only what no solid shape drawn after it covers (a pop-up over the bar): the rest shows once
                        // that goes (its repaint draws the bar as it is then)
                        var parts = changed.Width > 0 && changed.Height > 0 ? Subtract(Clip(changed), SolidShapesAbove(n.Index)) : new List<Rectangle>();
                        foreach (var part in parts) DrawBar(n, part);
                        n.Shown = true; n.Sent = n.Key;
                        n.BarAt = n.FillEnd; n.BarColour = n.Colour;
                        foreach (var part in parts) MarkAbove(n, part, keepSolidText: true); // painted over
                        foreach (var m in dynamic)
                            if (m.Index > n.Index && SolidText(m) && m.TextAt.Value.IntersectsWith(n.R) && BackgroundIn(m.TextAt.Value, m.Index) != m.SolidBg)
                                Invalidate(m); // the bar's edge crossed the text's centre: the text's background changes
                    }
                        break;
                    case "deltabar":
                        UpdateDeltaBar(n, v);
                        n.Shown = true;
                        break;
                }
            }
            screen.Flush();
        }

        /// <summary>
        /// What of a shape's box it painted: a box with no fill only its border (with its rounded corners), as four
        /// strips; anything else its whole box.
        /// </summary>
        private static List<Rectangle> Outline(Node n)
        {
            var r = n.R;
            if (n.Kind != "shape" || n.E.Type != "box" || n.E.Fill != null || !string.IsNullOrEmpty(n.E.ColorBind)) return new List<Rectangle> { r };
            int t = Math.Max(n.E.Border, 1) + Math.Max(0, n.E.Radius) + 1; // the corners curve inside the radius
            if (2 * t >= r.Width || 2 * t >= r.Height) return new List<Rectangle> { r };
            return new List<Rectangle>
            {
                new Rectangle(r.X, r.Y, r.Width, t), new Rectangle(r.X, r.Bottom - t, r.Width, t),
                new Rectangle(r.X, r.Y + t, t, r.Height - 2 * t), new Rectangle(r.Right - t, r.Y + t, t, r.Height - 2 * t),
            };
        }

        private bool Covered(Rectangle r) => popups.Any(p => p.Until >= 0 && p.R.IntersectsWith(r));

        /// <summary>How much darker the screen should be now (0-95 %): the "dim" elements shown (their Opacity). The
        /// controller lowers the backlight by it, so dimming the whole dash redraws nothing.</summary>
        public int DimPercent { get; private set; }

        /// <summary>Diagnostics (verify): a pop-up shows now; counts pop-ups shown or taken down.</summary>
        internal bool PopupShowing => popups.Any(p => p.Until >= 0);
        internal int PopupEvents;

        private void Evaluate(Node n, DashValues v)
        {
            var e = n.E;
            n.Visible = true;
            if (e.Visible != null)
                foreach (var cond in e.Visible)
                {
                    var t = v.Truthy(cond);
                    // in a preview, a condition that can't be evaluated here (SimHub formula) follows PreviewVisible
                    if (t == null && v.Preview && !DashValues.KnownKey(cond)) t = e.PreviewVisible ?? true;
                    if (t != true) { n.Visible = false; break; }
                }
            n.Colour = ElementColour(e, v);
            switch (n.Kind)
            {
                case "shape":
                case "label":
                    n.Key = DashColors.Hex(n.Colour);
                    break;
                case "value":
                    n.Text = Format(e, v, out var sign);
                    if (sign > 0 && e.PositiveColor != null) n.Colour = DashColors.Parse(e.PositiveColor, n.Colour);
                    else if (sign < 0 && e.NegativeColor != null) n.Colour = DashColors.Parse(e.NegativeColor, n.Colour);
                    n.Key = n.Text + "|" + DashColors.Hex(n.Colour);
                    break;
                case "bar":
                {
                    double x = v.Number(e.Bind) ?? (v.Preview ? (e.Min + e.Max) / 2 : e.Min);
                    // Max may be below Min (a gauge that fills as the value goes negative, like SimHub's 0 to -1)
                    double f = Math.Abs(e.Max - e.Min) > 1e-12 ? Math.Max(0, Math.Min(1, (x - e.Min) / (e.Max - e.Min))) : 0;
                    int len = e.Orientation == "vertical" ? n.R.Height : n.R.Width;
                    n.FillEnd = (int)Math.Round(f * len);
                    n.Key = n.FillEnd + "|" + DashColors.Hex(n.Colour);
                    break;
                }
            }
        }

        /// <summary>The element's colour now: ColorBind (a colour, or a number through ColorStops) or its Color.</summary>
        private static Color ElementColour(DashElement e, DashValues v)
        {
            var baseColour = DashColors.Parse(e.Color, Color.White);
            if (string.IsNullOrEmpty(e.ColorBind)) return baseColour;
            var raw = v.Raw(e.ColorBind);
            if (raw == null) return baseColour;
            if (e.ColorStops != null && e.ColorStops.Count > 0)
            {
                var x = DashValues.ToNumber(raw);
                if (x.HasValue) return Stops(e.ColorStops, x.Value, baseColour);
            }
            return DashColors.TryParse(raw, out var c) ? c : baseColour;
        }

        private static Color Stops(List<ColorStop> stops, double x, Color fallback)
        {
            var s = stops.OrderBy(k => k.Value).ToList();
            if (x <= s[0].Value) return DashColors.Parse(s[0].Color, fallback);
            for (int i = 1; i < s.Count; i++)
                if (x <= s[i].Value)
                {
                    var a = DashColors.Parse(s[i - 1].Color, fallback); var b = DashColors.Parse(s[i].Color, fallback);
                    double t = (x - s[i - 1].Value) / Math.Max(1e-9, s[i].Value - s[i - 1].Value);
                    return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
                }
            return DashColors.Parse(s[s.Count - 1].Color, fallback);
        }

        /// <summary>Draws a dynamic shape, clipped to `area`.</summary>
        private void DrawShapeNode(Node n, Rectangle area)
        {
            EnsurePx(n);
            var clip = Rectangle.Intersect(Rectangle.Intersect(n.R, area), new Rectangle(0, 0, Width, Height));
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var local = new Rectangle(clip.X - n.R.X, clip.Y - n.R.Y, clip.Width, clip.Height);
            SendFills(n.Px, Math.Max(1, n.E.W), local, Transparent, n.R.X, n.R.Y);
        }

        /// <summary>Later dynamic elements over `n` (or over `area` of it) must be drawn again.</summary>
        private void MarkAbove(Node n, Rectangle? area = null, bool keepSolidText = false)
        {
            var r = area ?? n.R;
            foreach (var m in dynamic)
                if (m.Index > n.Index && m.Shown && m.Visible && !(keepSolidText && SolidText(m)) &&
                    (IsText(m) && m.TextAt.HasValue ? Ink(m) : m.R).IntersectsWith(r))
                {
                    // a shape keeps where it was drawn over (the rest of it is still on the screen as it was)
                    bool partial = m.Kind == "shape" && (m.Sent == m.Key || !m.DamageAll);
                    var before = m.Sent == m.Key ? Rectangle.Empty : m.Damage;
                    Invalidate(m);
                    if (partial)
                    {
                        var hit = Rectangle.Intersect(r, m.R);
                        m.DamageAll = false;
                        m.Damage = before.IsEmpty ? hit : Rectangle.Union(before, hit);
                    }
                }
        }

        private static bool IsText(Node n) => n.Kind == "value" || n.Kind == "label";

        /// <summary>Text drawn with its own background band (bars don't paint under it).</summary>
        private static bool SolidText(Node m) => IsText(m) && m.Shown && m.Visible && m.SolidAt && m.TextAt.HasValue;

        /// <summary>`area` of `n` lies under one solid shape shown after it (nothing of `n` there would be seen).</summary>
        private bool Occluded(Node n, Rectangle area)
        {
            area = Rectangle.Intersect(Clip(area), n.R);
            // only the rows the font is drawn in matter (a box may reach past what covers it)
            int fh = FontHeight(n.E.Font);
            if (fh > 0 && fh <= n.R.Height) area = Rectangle.Intersect(area, new Rectangle(n.R.X, n.R.Y + (n.R.Height - fh) / 2, n.R.Width, fh));
            if (area.Width <= 0 || area.Height <= 0) return false;
            // covered all over by solid shapes after it (together: a pop-up background can come in pieces)
            if (Subtract(area, SolidShapesAbove(n.Index)).Count == 0) return true;
            foreach (var m in dynamic)
            {
                // text drawn later with its own background over all of this one (two values in the same place)
                if (m.Index > n.Index && m.Visible && SolidText(m) && m.TextAt.Value.Contains(area)) return true;
                if (m.Index <= n.Index || m.Kind != "shape" || !m.Visible || Covered(m.R) || !m.R.Contains(area)) continue;
                EnsurePx(m);
                var local = area; local.Offset(-m.R.X, -m.R.Y);
                var c = Uniform(m.Px, Math.Max(1, m.E.W), local);
                if (c.HasValue && c != Transparent && OpaqueIn(m, local)) return true;
                if (!c.HasValue && OpaqueIn(m, local)) return true;
            }
            return false;
        }

        private void EnsurePx(Node m)
        {
            string key = m.Key + UnderSignature(m);
            if (m.Px == null || m.PxKey != key) { m.Px = ShapePixels(m, m.Colour); m.PxSolid = NativeCapable(m) ? NativeSolid(m) : null; m.PxKey = key; m.PxOpaque = null; m.SolidParts = null; }
        }

        private bool HasPictures(Node n) => pictureVariants.ContainsKey(n.Index) || coveredBy.ContainsKey(n.Index);

        /// <summary>With tiles, a shape with pictures (or drawn smoothly in their place) blends its edge over what's under it:
        /// which shapes show there, in which colours (empty otherwise).</summary>
        private string UnderSignature(Node n)
        {
            if (!(tilesOn && (HasPictures(n) || smoothShapes.Contains(n.Index))) && !NativeCapable(n)) return "";
            var sb = new System.Text.StringBuilder("|");
            foreach (var m in dynamic)
            {
                if (m.Index >= n.Index) break;
                if (m.Kind == "shape" && m.Shown && m.R.IntersectsWith(n.R)) sb.Append(m.Index).Append(':').Append(m.Key).Append(',');
            }
            return sb.ToString();
        }

        /// <summary>Shapes after element `index` that show now and have no transparent pixel: nothing under them is seen.</summary>
        private List<Rectangle> SolidShapesAbove(int index)
        {
            var list = new List<Rectangle>();
            foreach (var m in dynamic)
            {
                if (m.Index <= index || m.Kind != "shape" || !m.Visible || Covered(m.R)) continue;
                EnsurePx(m);
                if (m.PxOpaque == null) m.PxOpaque = m.PxSolid == null ? !m.Px.Contains(Transparent) : m.PxSolid.All(x => x);
                if (m.PxOpaque == true) list.Add(Clip(m.R));
                else list.AddRange(m.SolidParts ?? (m.SolidParts = SolidParts(m)));
            }
            return list;
        }

        /// <summary>
        /// The solid parts of a shape with see-through pixels: the longest run of rows it fills from side to side and the
        /// longest run of columns it fills from top to bottom (a rounded box: everything but its corners; an outline or an
        /// oval: nothing much). Used where it hides what's under it.
        /// </summary>
        private List<Rectangle> SolidParts(Node m)
        {
            var list = new List<Rectangle>();
            int w = Math.Max(1, m.E.W), h = Math.Max(1, m.E.H);
            if (m.Px.Length < w * h) return list;
            Rectangle Longest(int count, Func<int, bool> full, Func<int, int, Rectangle> make)
            {
                int best = 0, bestAt = 0, run = 0;
                for (int i = 0; i < count; i++)
                {
                    run = full(i) ? run + 1 : 0;
                    if (run > best) { best = run; bestAt = i - run + 1; }
                }
                return best == 0 ? Rectangle.Empty : make(bestAt, best);
            }
            var rows = Longest(h, y => { for (int x = 0; x < w; x++) if (!SolidAt(m, y * w + x)) return false; return true; },
                               (at, n) => new Rectangle(m.R.X, m.R.Y + at, w, n));
            var cols = Longest(w, x => { for (int y = 0; y < h; y++) if (!SolidAt(m, y * w + x)) return false; return true; },
                               (at, n) => new Rectangle(m.R.X + at, m.R.Y, n, h));
            // only worth it when it holds most of the shape (a box's corners, not an oval's middle strip)
            foreach (var r in new[] { rows, cols })
                if (!r.IsEmpty && r.Width * r.Height >= 0.5 * w * h) list.Add(Clip(r));
            return list;
        }

        /// <summary>`area` is all under one solid shape after element `index` that shows now.</summary>
        private bool CoveredAbove(int index, Rectangle area)
        {
            area = Clip(area);
            return area.Width > 0 && area.Height > 0 && Subtract(area, SolidShapesAbove(index)).Count == 0;
        }

        /// <summary>A pixel of `m` (index into Px) hides what's under it (a smoothed edge mixes with it: not).</summary>
        private static bool SolidAt(Node m, int i) => m.PxSolid == null ? m.Px[i] != Transparent : m.PxSolid[i];

        /// <summary>All of `local` (in `m`'s box) hides what's under it.</summary>
        private static bool OpaqueIn(Node m, Rectangle local)
        {
            int stride = Math.Max(1, m.E.W);
            for (int y = local.Top; y < local.Bottom; y++)
                for (int x = local.Left; x < local.Right; x++)
                    if (!SolidAt(m, y * stride + x)) return false;
            return true;
        }


        private static void Invalidate(Node m)
        {
            m.Sent = null;
            m.DamageAll = true;
            m.BarAt = null;
            if (m.SegSent != null) for (int k = 0; k < m.SegSent.Length; k++) m.SegSent[k] = null;
        }

        /// <summary>
        /// An area back as it should look under the dynamic text: the static layer, static labels touching it, and the
        /// dynamic shapes shown there (clipped); dynamic text, bars and later shapes there are marked for a redraw.
        /// </summary>
        /// <param name="layer">
        /// The element being drawn over the area: bars and text below it that are up to date are drawn back straight away
        /// (not marked, which would make them draw again and mark this one: two overlapping elements redrawing each
        /// other every frame). 0 = mark everything (an element hidden, a pop-up gone).
        /// </param>
        private Rectangle Repaint(Rectangle area, int layer = 0)
        {
            area = Clip(area);
            if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
            Trace?.Invoke($"repaint {area} layer {layer}");
            // Tiles: only where the area is detailed (a picture under it). A tile is drawn whole, so it puts back what
            // else it covers too, which then has to be drawn again (a bar's emptied sliver would redraw every value in
            // its tile at every update); an area of a few colours is put back exactly, with fills, as without tiles.
            // A solid shape shown under all of it (a warning's box under its blinking label): nothing of the dash under that
            // can be seen, so neither tiles nor the static labels are put back, only the box and what's on it.
            var floor0 = FloorUnder(area, layer);
            bool fromTiles = floor0 == null && tilesOn && MergeRects(shownPx ?? staticPx, Width, area).Count > TileRepaintFills;
            // Text drawn back over the area must have all of it under the repaint: drawn again over its own old pixels
            // (no background), its anti-aliased edges would thicken. So the area grows to take in the text it touches.
            for (int pass = 0; pass < 64; pass++) // until nothing more joins (a row of labels can chain)
            {
                var grown = area;
                if (floor0 == null)
                    foreach (var l in staticLabels) { var ink = LabelInk(l); if (ink.IntersectsWith(area)) grown = Rectangle.Union(grown, ink); }
                if (layer > 0)
                    foreach (var n in dynamic)
                    {
                        if (n.Index >= layer || !n.Shown || !n.Visible || n.Sent != n.Key || (floor0 != null && n.Index < floor0.Index)) continue;
                        var ink = IsText(n) && n.TextAt.HasValue ? Ink(n) : n.Kind == "label" ? LabelInk(n) : Rectangle.Empty;
                        if (!ink.IsEmpty && ink.IntersectsWith(area)) grown = Rectangle.Union(grown, ink);
                    }
                grown = Clip(grown);
                // a tile is drawn whole: what else it covers is put back too (an overlay's own area tile when it holds it)
                if (fromTiles) grown = AreaTileFor(grown)?.R ?? ScreenTiles.Snap(grown);
                if (grown == area) break;
                area = grown;
            }
            // The topmost solid shape shown under all of the area (a pop-up box): the repaint starts from it, as nothing
            // under it can be seen (drawing that first would flash).
            var floor = FloorUnder(area, layer);
            if (floor == null)
            {
                if (fromTiles) DrawTiles(area);
                else SendFills(tilesOn && shownPx != null ? shownPx : staticPx, Width, area, -2, 0, 0);
                foreach (var l in staticLabels) if (LabelInk(l).IntersectsWith(area)) DrawLabel(l, l.Colour);
            }
            foreach (var n in dynamic)
            {
                if (floor != null && n.Index < floor.Index) continue; // under the floor: not seen, not touched
                // the bar this repaint is for (behind its emptied part): the area may have grown past that part, so its
                // own pixels there are put back too (without another repaint)
                if (n.Index == layer && n.Kind == "bar" && n.Shown && n.Visible) { BarPixelsIn(n, area); continue; }
                // a value only needs drawing again where its text is (its box may reach well past it)
                if (!(IsText(n) && n.TextAt.HasValue ? Ink(n) : n.R).IntersectsWith(area)) continue;
                if (n.Kind == "shape" && n.Shown && n.Visible)
                {
                    // with its picture on the screen: drawn whole from it (one command, the very pixels it showed), what's
                    // over it drawn again after; else its pixels in the area, with fills
                    if (coveredBy.TryGetValue(n.Index, out var over) && over.Shown && over.Visible && LeftToPicture(n)) continue;
                    // drawn whole by the screen when the area is a good part of it (a corner of it: fills, as before)
                    // (only for something that went, layer 0: under an element being drawn it would mark that one's
                    // neighbours, which repaint it in turn, every frame)
                    if (layer == 0 && NativeCapable(n) && NativeRepaintWorthIt(n, area) && SendNative(n))
                    {
                        // (now up to date: drawn whole again later, its smoothed edge would blend over itself)
                        MarkAboveNative(n);
                        n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                        continue;
                    }
                    HashSet<int> picWith = null;
                    var pic = tilesOn ? ChosenPicture(n, out picWith) : null;
                    if (pic != null && PictureFits(n, picWith))
                    {
                        screen.Cmd(ScreenTiles.Ramv(pic, dx, dy));
                        MarkAbove(n, PicArea(n));
                    }
                    // drawn by the screen itself, and its part here is many rectangles (a stretch of its smoothed edge under
                    // a blinking icon: a rectangle a pixel, seconds of traffic): all of it again, its old edge wiped
                    else if (NativeCapable(n) && FillsIn(n, area) > TileRepaintFills && SendNative(n, clean: true))
                    {
                        MarkAboveNative(n);
                        n.Sent = n.Key; n.DrawnPx = n.Px; n.DrawnKey = n.Key; n.DamageAll = false; n.Damage = Rectangle.Empty;
                    }
                    else DrawShapeNode(n, area);
                    continue;
                }
                bool below = n.Index < layer && n.Shown && n.Visible && n.Sent == n.Key && !Covered(n.R);
                if (Trace != null) Trace($"  {(n.Kind == "shape" && n.Shown && n.Visible ? "shape back" : below ? "back" : "marked")} #{n.Index} {n.Kind} {n.E.Name}");
                if (below && n.Kind == "bar") DrawBar(n, area);
                else if (below && n.Kind == "label" && !n.TextAt.HasValue) DrawLabel(n, n.Colour);
                else if (below && IsText(n) && n.TextAt.HasValue) RedrawText(n); // exactly as it was drawn
                else { Invalidate(n); if (n.Kind == "bar") n.BarAt = null; } // (a bar about to move: drawn whole, the repainted part included)
            }
            return area;
        }

        /// <summary>The topmost solid shape shown under all of `area` (below `layer` when it's given), or null.</summary>
        private Node FloorUnder(Rectangle area, int layer)
        {
            Node floor = null;
            foreach (var m in dynamic)
            {
                if (layer > 0 && m.Index >= layer) break;
                if (m.Kind != "shape" || !m.Shown || !m.Visible || Covered(m.R) || !m.R.Contains(area)) continue;
                EnsurePx(m);
                var local = area; local.Offset(-m.R.X, -m.R.Y);
                if (OpaqueIn(m, local)) floor = m;
            }
            return floor;
        }

        /// <summary>
        /// The one colour under `area` now (static layer and the dynamic shapes shown before element `index`), or null.
        /// </summary>
        private int? BackgroundIn(Rectangle area, int index)
        {
            var px = Composite(area, index);
            if (px == null || px.Length == 0) return null;
            int c = px[0];
            for (int k = 1; k < px.Length; k++) if (px[k] != c) return null;
            return c;
        }

        /// <summary>
        /// Where `text` in `font` is drawn in an element's box: the text's width (with the margin DrawValue gives it,
        /// aligned as the element is) by the font's height (centred as the screen centres it).
        /// </summary>
        internal static Rectangle TextBand(DashElement e, int font, string text, int margin = -1)
        {
            var r = new Rectangle(e.X, e.Y, Math.Max(0, e.W), Math.Max(0, e.H));
            int xcen = Align(e.Align), fh = FontHeight(font), tw = TextWidth(font, text ?? "");
            if (margin < 0) margin = 4 + fh / 4;
            int w = tw < 0 ? r.Width : Math.Min(r.Width, tw + 2 * margin);
            int x = xcen == 1 ? r.X + (r.Width - w) / 2 : xcen == 2 ? r.Right - w : r.X;
            return fh > 0 && fh <= r.Height ? new Rectangle(x, r.Y + (r.Height - fh) / 2, w, fh) : new Rectangle(x, r.Y, w, r.Height);
        }

        /// <summary>The one colour of the static layer (shapes that are always there) over `area`, or null.</summary>
        internal int? StaticColour(Rectangle area)
        {
            area = Clip(area);
            return area.Width <= 0 || area.Height <= 0 ? null : Uniform(staticPx, Width, area);
        }

        /// <summary>
        /// The one colour under `area` whenever element `e` shows (static layer, the shapes always shown, and the shapes
        /// shown whenever `e` is: their conditions all among its own, before it), or null.
        /// </summary>
        internal int? ColourUnder(DashElement e, Rectangle area)
        {
            area = Clip(area);
            if (area.Width <= 0 || area.Height <= 0) return null;
            var px = new int[area.Width * area.Height];
            for (int y = 0; y < area.Height; y++) Array.Copy(staticPx, (area.Y + y) * Width + area.X, px, y * area.Width, area.Width);
            int index = def.Elements.IndexOf(e);
            foreach (var m in dynamic)
            {
                if (m.Index >= index) break;
                if (m.Kind != "shape" || !m.R.IntersectsWith(area)) continue;
                bool always = m.E.Visible == null || m.E.Visible.Count == 0;
                // shown whenever `e` is: its conditions all among e's (e may have more: a page, a "take turns" condition)
                bool together = e.Visible != null && m.E.Visible != null && m.E.Visible.All(e.Visible.Contains);
                if (!always && !together) continue;
                var spx = ShapePixels(m, DashColors.Parse(m.E.Color, Color.White));
                var r = Rectangle.Intersect(area, m.R);
                int stride = Math.Max(1, m.E.W);
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int c = spx[(y - m.R.Y) * stride + (x - m.R.X)];
                        if (c != Transparent) px[(y - area.Y) * area.Width + (x - area.X)] = c;
                    }
            }
            int c0 = px[0];
            return px.All(c => c == c0) ? c0 : (int?)null;
        }

        private static string WidestText(Node n)
        {
            string w = n.Text ?? "";
            if (n.Kind == "value" && n.E.Samples != null)
                foreach (var s in n.E.Samples)
                    if (!string.IsNullOrEmpty(s) && TextWidth(n.E.Font, s) > TextWidth(n.E.Font, w)) w = s;
            return w;
        }

        /// <summary>Another text's glyphs (a label, or a value as last drawn) reach into `area`.</summary>
        private bool TouchesOtherText(Node n, Rectangle area)
        {
            foreach (var l in staticLabels) if (GlyphRuns(l, l.R).Any(r => r.IntersectsWith(area))) return true;
            foreach (var m in dynamic)
            {
                if (m == n || !IsText(m) || !m.Shown || !m.Visible) continue;
                var box = m.TextAt ?? (m.Kind == "label" ? m.R : Rectangle.Empty);
                if (!box.IsEmpty && GlyphRuns(m, box).Any(r => r.IntersectsWith(area))) return true;
            }
            return false;
        }

        /// <summary>Where a text's characters are drawn in `box` (runs of non-space characters, a pixel of room each side, the font's rows).</summary>
        private static List<Rectangle> GlyphRuns(Node n, Rectangle box)
        {
            var runs = new List<Rectangle>();
            string t = n.Text ?? "";
            int fh = FontHeight(n.E.Font);
            int tw = TextWidth(n.E.Font, t);
            int y = box.Y + Math.Max(0, (box.Height - fh) / 2), h = Math.Min(box.Height, fh);
            if (tw < 0) { runs.Add(new Rectangle(box.X, y, box.Width, h)); return runs; }
            int x = n.XCen == 1 ? box.X + (box.Width - tw) / 2 : n.XCen == 2 ? box.Right - tw : box.X;
            int start = -1;
            for (int i = 0; i <= t.Length; i++)
            {
                bool ink = i < t.Length && t[i] != ' ';
                if (ink && start < 0) start = x;
                if (!ink && start >= 0) { runs.Add(new Rectangle(start - 2, y, x - start + 4, h)); start = -1; }
                if (i < t.Length) x += Math.Max(0, TextWidth(n.E.Font, t[i].ToString()));
            }
            return runs;
        }

        /// <summary>A text drawn back exactly as it was last drawn: with its background, or on it with lines put back, or glyphs only.</summary>
        private void RedrawText(Node n)
        {
            var at = n.TextAt.Value;
            int colour = DashColors.To565(n.Colour);
            if (n.BandAt && tilesOn && Tiles.Bands.TryGetValue(n.Index, out var bandTile))
            {
                screen.Cmd(ScreenTiles.Ramv(bandTile, dx, dy));
                screen.Cmd(Xstr(at, n.E.Font, colour, 0, n.XCen, 3, n.Text));
                return;
            }
            if (n.LinesAt)
            {
                var under = Composite(at, n.Index);
                if (under != null)
                {
                    int major = under.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                    screen.Cmd(Xstr(at, n.E.Font, colour, major, n.XCen, 1, n.Text));
                    SendFills(under, at.Width, new Rectangle(0, 0, at.Width, at.Height), major, at.X, at.Y);
                    return;
                }
            }
            screen.Cmd(Xstr(at, n.E.Font, colour, n.SolidBg, n.XCen, n.SolidAt ? 1 : 3, n.Text));
        }

        /// <summary>The pixels a label's text can cover in its box.</summary>
        private static Rectangle LabelInk(Node n)
        {
            var r = n.R;
            int tw = TextWidth(n.E.Font, n.Text);
            int w = tw < 0 ? r.Width : Math.Min(r.Width, tw + 4); // unknown width (a character the font lacks): all of it
            int x = n.XCen == 1 ? r.X + (r.Width - w) / 2 : n.XCen == 2 ? r.Right - w : r.X;
            int h = Math.Min(r.Height, FontHeight(n.E.Font) + 2);
            return new Rectangle(x, r.Y + (r.Height - h) / 2, w, h);
        }

        /// <summary>The pixels a drawn value's text can cover: its strip, only the font's height (centred) of it.</summary>
        private static Rectangle Ink(Node n)
        {
            var r = n.TextAt.Value;
            if (n.SolidAt) return r;
            int h = Math.Min(r.Height, FontHeight(n.E.Font) + 2);
            return new Rectangle(r.X, r.Y + (r.Height - h) / 2, r.Width, h);
        }

        /// <summary>
        /// The band a value's text is (re)drawn in: the new text's extent with `margin` beside it (-1: the usual), joined
        /// with where the old text was, kept centred / anchored like the text, by the rows the font is drawn in (where the
        /// screen centres it in the box; a band exactly that tall centres it there too).
        /// </summary>
        private Rectangle BandArea(Node n, int margin)
        {
            // the widest text the value shows (its samples, or what it shows now if wider): a band that doesn't change
            // with the text, so nothing is left behind when it gets shorter, and a full redraw draws the same
            var area = TextExtent(n, WidestText(n), margin);
            area = Rectangle.Intersect(area, n.R);
            if (n.XCen == 1) { int half = Math.Max(n.R.Right - area.Right, 0); int left = Math.Max(area.X - n.R.X, 0); int m = Math.Min(half, left); area = new Rectangle(n.R.X + m, n.R.Y, n.R.Width - 2 * m, n.R.Height); }
            int fh = FontHeight(n.E.Font);
            if (fh > 0 && fh <= n.R.Height) area = new Rectangle(area.X, n.R.Y + (n.R.Height - fh) / 2, area.Width, fh);
            return area;
        }

        /// <summary>
        /// What's under `area` now (static layer, then the dynamic shapes shown before element `index`), as RGB565 pixels
        /// row by row; null if a shape's pixels aren't known yet.
        /// </summary>
        /// <param name="coarse">With tiles: the colour-reduced static layer instead of the full-colour one. For pixels put
        /// back with fills around text (a border line through a value's band): the anti-aliased layer made every pixel of
        /// such a line its own fill (the Ferrari 488's first values: ~1000 fills, 23 KB, ~1 s; seen on the wheel
        /// 2026-09-30), the reduced one a few straight runs.</param>
        private int[] Composite(Rectangle area, int index, bool coarse = false)
        {
            area = Clip(area);
            if (area.Width <= 0 || area.Height <= 0) return null;
            var px = new int[area.Width * area.Height];
            var src = coarse && quantPx != null ? quantPx : staticPx;
            for (int y = 0; y < area.Height; y++)
                Array.Copy(src, (area.Y + y) * Width + area.X, px, y * area.Width, area.Width);
            foreach (var m in dynamic)
            {
                if (m.Index >= index) break;
                if (m.Kind == "bar" && m.Shown && m.Visible && m.R.IntersectsWith(area))
                {
                    // text on a bar: the bar keeps out from under the text (DrawBar), which shows the bar's colour at its
                    // centre instead, so the text never has to be wiped and drawn again when the level moves
                    var rb = Rectangle.Intersect(area, m.R);
                    int cb = BarColourAt(m, new Point(Math.Max(m.R.X, Math.Min(m.R.Right - 1, area.X + area.Width / 2)), Math.Max(m.R.Y, Math.Min(m.R.Bottom - 1, area.Y + area.Height / 2))));
                    for (int y = rb.Top; y < rb.Bottom; y++)
                        for (int x = rb.Left; x < rb.Right; x++) px[(y - area.Y) * area.Width + (x - area.X)] = cb;
                    continue;
                }
                if (IsText(m) && SolidText(m) && m.TextAt.Value.IntersectsWith(area))
                {
                    // text drawn with its own background band: that colour, around its glyphs, is what's under things
                    var rt = Rectangle.Intersect(area, m.TextAt.Value);
                    for (int y = rt.Top; y < rt.Bottom; y++)
                        for (int x = rt.Left; x < rt.Right; x++) px[(y - area.Y) * area.Width + (x - area.X)] = m.SolidBg;
                    continue;
                }
                if (m.Kind != "shape" || !m.Shown || !m.R.IntersectsWith(area)) continue;
                EnsurePx(m);
                var r = Rectangle.Intersect(area, m.R);
                int stride = Math.Max(1, m.E.W);
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int c = m.Px[(y - m.R.Y) * stride + (x - m.R.X)];
                        if (c != Transparent) px[(y - area.Y) * area.Width + (x - area.X)] = c;
                    }
            }
            return px;
        }

        /// <summary>Where a value's text lands in its box (full box height, a little margin for glyph overhang).</summary>
        private static Rectangle TextExtent(Node n, string text, int margin = -1)
        {
            // glyphs can reach past their advance width, and the screen clips text to its box: room on both sides
            if (margin < 0) margin = 4 + FontHeight(n.E.Font) / 4;
            int tw = TextWidth(n.E.Font, text);
            int w = tw < 0 ? n.R.Width : Math.Min(n.R.Width, tw + 2 * margin);
            int x = n.XCen == 1 ? n.R.X + (n.R.Width - w) / 2 : n.XCen == 2 ? n.R.Right - w : n.R.X;
            return new Rectangle(x, n.R.Y, w, n.R.Height);
        }

        /// <summary>Another text, picture or gradient reaches into this element's box.</summary>
        private bool Crowded(Node n)
        {
            if (n.CrowdedKnown) return n.IsCrowded;
            n.CrowdedKnown = true;
            n.IsCrowded = def.Elements.Any(e => e != n.E && (e.Type == "value" || e.Type == "label" || e.Type == "image" || e.Type == "gradient")
                                                && new Rectangle(e.X, e.Y, e.W, e.H).IntersectsWith(n.R));
            return n.IsCrowded;
        }

        /// <summary>The background colour under a value/bar now, or null when it isn't one colour (then: repaint).</summary>
        private int? BackgroundUnder(Node n)
        {
            if (n.E.Background != null) return Rgb565(n.E.Background);
            int? under = n.StaticBg;
            foreach (var m in dynamic)
            {
                if (m.Index >= n.Index) break;
                if (m.Kind == "bar" && m.Shown && m.Visible && m.R.IntersectsWith(n.R)) return null; // text on a bar: its band
                if (m.Kind != "shape" || !m.Shown || !m.R.IntersectsWith(n.R)) continue;
                if (m.Px == null) return null;
                var local = Rectangle.Intersect(n.R, m.R);
                if (local != n.R) return null; // only partly under it
                local.Offset(-m.R.X, -m.R.Y);
                under = Uniform(m.Px, Math.Max(1, m.E.W), local);
                if (under == null || under == Transparent) return null;
            }
            return under;
        }

        private void DrawValue(Node n)
        {
            int colour = DashColors.To565(n.Colour);
            // the whole box painted in one go only when nothing else is in it (a box reaching over its neighbour's text
            // would wipe it, then it's drawn again: a flash); else its text band. A label's box: always its band.
            var bg = n.Kind == "value" && !Crowded(n) ? BackgroundUnder(n) : null;
            if (bg.HasValue) { screen.Cmd(Xstr(n.R, n.E.Font, colour, bg.Value, n.XCen, 1, n.Text)); n.TextAt = n.R; n.SolidAt = true; n.SolidBg = bg.Value; n.BandAt = false; return; }
            // Tiles: a value over a picture or several colours has its band (the background under its widest text) as
            // a tile: the band, then the text without a background. Two commands, nothing else touched (a grid tile
            // would also cover the neighbours, which would then be drawn again too). Only while the band is still the
            // one the tile was made for and nothing that changes, or other text, is in it.
            if (tilesOn && Tiles.Bands.TryGetValue(n.Index, out var bandTile) && Clip(BandArea(n, -1)) == bandTile.R
                && !DynamicUnder(bandTile.R, n.Index) && !TouchesOtherText(n, bandTile.R))
            {
                if (n.TextAt.HasValue && n.TextAt.Value != bandTile.R)
                    foreach (var part in Subtract(Rectangle.Intersect(n.TextAt.Value, n.R), new List<Rectangle> { bandTile.R }))
                        Repaint(part, n.Index);
                screen.Cmd(ScreenTiles.Ramv(bandTile, dx, dy));
                screen.Cmd(Xstr(bandTile.R, n.E.Font, colour, 0, n.XCen, 3, n.Text));
                n.TextAt = bandTile.R; n.SolidAt = false; n.SolidBg = 0; n.LinesAt = false; n.BandAt = true;
                return;
            }
            // Not one colour under the whole box (an image, a bar, a border line through it): only the band the text is
            // drawn in (the font's height, centred like the text; the old and the new text's width) is redrawn. Centred /
            // left / right text lands where it does in the box, as the band shares the box's centre / left / right edge
            // and (its height differing from the box's by an even number) its vertical centre.
            // One colour there: the new text with that background, one command, nothing cleared first (no flash). The
            // room left beside the text (for glyph overhang) shrinks if a line runs through it.
            // the widest room that keeps clear of other text's glyphs and, if possible, has one colour under it
            var area = BandArea(n, -1);
            var strip = BackgroundIn(area, n.Index);
            bool clear = !TouchesOtherText(n, area);
            for (int margin = (4 + FontHeight(n.E.Font) / 4) / 2; (!strip.HasValue || !clear) && margin >= 1; margin = margin > 1 ? margin / 2 : 0)
            {
                var a2 = BandArea(n, margin);
                var s2 = BackgroundIn(a2, n.Index);
                bool c2 = !TouchesOtherText(n, a2);
                if ((c2 && !clear) || (c2 == clear && s2.HasValue && !strip.HasValue)) { area = a2; strip = s2; clear = c2; }
            }
            // where the old text reached beyond the new one: back to what's under it (a neighbour's label included), as a
            // full redraw would show it; the new text's band is drawn over the rest
            if (n.TextAt.HasValue)
            {
                var covered = SolidShapesAbove(n.Index); covered.Add(area);
                foreach (var part in Subtract(Rectangle.Intersect(n.TextAt.Value, n.R), covered))
                {
                    if (TouchesOtherText(n, part)) { Repaint(part, n.Index); continue; } // a neighbour's glyphs to put back
                    var px = Composite(part, n.Index, coarse: true);                       // else just what's under it
                    if (px != null) SendFills(px, part.Width, new Rectangle(0, 0, part.Width, part.Height), Transparent, part.X, part.Y);
                    else Repaint(part, n.Index);
                }
            }
            var under = strip.HasValue ? null : Composite(area, n.Index, coarse: true);
            int major = 0, odd = int.MaxValue;
            if (under != null)
            {
                major = under.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                odd = under.Count(c => c != major);
            }
            if (strip.HasValue) screen.Cmd(Xstr(area, n.E.Font, colour, strip.Value, n.XCen, 1, n.Text));
            else if (under != null && odd <= under.Length / 10)
            {
                // Mostly one colour, a few lines through it (a box's border): the text on that colour, then the lines
                // put back. Still no clearing first.
                screen.Cmd(Xstr(area, n.E.Font, colour, major, n.XCen, 1, n.Text));
                SendFills(under, area.Width, new Rectangle(0, 0, area.Width, area.Height), major, area.X, area.Y);
                n.LinesAt = true;
            }
            else
            {
                // Pictures or bars under the text: cleared, then drawn (the wheel may show the gap for a moment).
                ClearedDraws++;
                if (ClearedBy != null) ClearedBy[n.E] = (ClearedBy.TryGetValue(n.E, out var cb) ? cb : 0) + 1;
                Repaint(area, n.Index);
                screen.Cmd(Xstr(area, n.E.Font, colour, 0, n.XCen, 3, n.Text));
            }
            n.TextAt = area; n.SolidAt = strip.HasValue; n.SolidBg = strip ?? 0; n.BandAt = false;
            if (strip.HasValue || under == null || odd > under.Length / 10) n.LinesAt = false;
        }

        /// <summary>Diagnostics: value redraws that had to clear their area first (may flash on the wheel).</summary>
        internal int ClearedDraws;
        internal Dictionary<DashElement, int> ClearedBy;

        /// <summary>The part of a bar between two fill levels (in pixels along it).</summary>
        private static Rectangle BarRange(Node n, int from, int to)
        {
            var r = n.R;
            bool vertical = n.E.Orientation == "vertical";
            int len = vertical ? r.Height : r.Width;
            int a = Math.Max(0, Math.Min(len, Math.Min(from, to))), b = Math.Max(0, Math.Min(len, Math.Max(from, to)));
            if (!vertical) return n.E.Reverse ? new Rectangle(r.Right - b, r.Y, b - a, r.Height) : new Rectangle(r.X + a, r.Y, b - a, r.Height);
            return n.E.Reverse ? new Rectangle(r.X, r.Y + a, r.Width, b - a) : new Rectangle(r.X, r.Bottom - b, r.Width, b - a);
        }

        /// <summary>Draws a bar; `clip`: only that part of it (drawing back what's under a value).</summary>
        private void DrawBar(Node n, Rectangle? clip = null)
        {
            var e = n.E; var r = n.R;
            bool vertical = e.Orientation == "vertical";
            int len = vertical ? r.Height : r.Width, f = Math.Max(0, Math.Min(len, n.FillEnd));
            Rectangle filled, empty;
            if (!vertical)
            {
                filled = e.Reverse ? new Rectangle(r.Right - f, r.Y, f, r.Height) : new Rectangle(r.X, r.Y, f, r.Height);
                empty = e.Reverse ? new Rectangle(r.X, r.Y, len - f, r.Height) : new Rectangle(r.X + f, r.Y, len - f, r.Height);
            }
            else
            {
                filled = e.Reverse ? new Rectangle(r.X, r.Y, r.Width, f) : new Rectangle(r.X, r.Bottom - f, r.Width, f);
                empty = e.Reverse ? new Rectangle(r.X, r.Y + f, r.Width, len - f) : new Rectangle(r.X, r.Y, r.Width, len - f);
            }
            if (clip.HasValue) { filled.Intersect(clip.Value); empty.Intersect(clip.Value); }
            // text drawn with its own background over the bar keeps its band: the bar never paints over it
            var holes = dynamic.Where(m => m.Index > n.Index && SolidText(m) && m.TextAt.Value.IntersectsWith(n.R)).Select(m => m.TextAt.Value).ToList();
            int? bg = null;
            foreach (var part in Subtract(empty, holes))
            {
                if (bg == null) bg = e.Fill != null ? Rgb565(e.Fill) : BackgroundUnder(n);
                if (bg.HasValue) screen.Cmd(Fill(part.X, part.Y, part.Width, part.Height, bg.Value));
                else Repaint(part, n.Index);
            }
            foreach (var part in Subtract(filled, holes))
                screen.Cmd(Fill(part.X, part.Y, part.Width, part.Height, DashColors.To565(n.Colour)));
        }

        /// <summary>A bar's filled part (and its empty part if it has a Fill colour) within `area`, as plain fills.</summary>
        private void BarPixelsIn(Node n, Rectangle area)
        {
            var e = n.E; var r = n.R;
            bool vertical = e.Orientation == "vertical";
            int len = vertical ? r.Height : r.Width, f = Math.Max(0, Math.Min(len, n.FillEnd));
            var filled = BarRange(n, 0, f); var empty = BarRange(n, f, len);
            filled.Intersect(area); empty.Intersect(area);
            var holes = dynamic.Where(m => m.Index > n.Index && SolidText(m) && m.TextAt.Value.IntersectsWith(n.R)).Select(m => m.TextAt.Value).ToList();
            foreach (var part in Subtract(filled, holes)) screen.Cmd(Fill(part.X, part.Y, part.Width, part.Height, DashColors.To565(n.Colour)));
            if (e.Fill != null) foreach (var part in Subtract(empty, holes)) screen.Cmd(Fill(part.X, part.Y, part.Width, part.Height, Rgb565(e.Fill)));
        }

        /// <summary>A bar's colour at a point now: its colour where filled, else its Fill or what's under it.</summary>
        private int BarColourAt(Node m, Point p)
        {
            var r = m.R;
            bool vertical = m.E.Orientation == "vertical";
            int t = !vertical ? (m.E.Reverse ? r.Right - 1 - p.X : p.X - r.X) : (m.E.Reverse ? p.Y - r.Y : r.Bottom - 1 - p.Y);
            if (t < m.FillEnd) return DashColors.To565(m.Colour);
            if (m.E.Fill != null) return Rgb565(m.E.Fill);
            var under = Composite(new Rectangle(p.X, p.Y, 1, 1), m.Index);
            return under != null && under.Length > 0 ? under[0] : 0;
        }

        /// <summary>A rectangle minus some holes, as rectangles.</summary>
        private static List<Rectangle> Subtract(Rectangle r, List<Rectangle> holes)
        {
            var parts = new List<Rectangle>();
            if (r.Width > 0 && r.Height > 0) parts.Add(r);
            foreach (var h in holes)
            {
                var next = new List<Rectangle>();
                foreach (var a in parts)
                {
                    if (!a.IntersectsWith(h)) { next.Add(a); continue; }
                    var c = Rectangle.Intersect(a, h);
                    if (c.Top > a.Top) next.Add(new Rectangle(a.X, a.Y, a.Width, c.Top - a.Top));
                    if (c.Bottom < a.Bottom) next.Add(new Rectangle(a.X, c.Bottom, a.Width, a.Bottom - c.Bottom));
                    if (c.Left > a.Left) next.Add(new Rectangle(a.X, c.Y, c.Left - a.Left, c.Height));
                    if (c.Right < a.Right) next.Add(new Rectangle(c.Right, c.Y, a.Right - c.Right, c.Height));
                }
                parts = next;
            }
            return parts;
        }

        private void UpdateDeltaBar(Node b, DashValues v)
        {
            var e = b.E;
            double d = Math.Max(-1, Math.Min(1, (v.Number(e.Bind) ?? 0) / (e.Range <= 0 ? 1 : e.Range)));
            int n = e.Segments;
            // Halves measured from the centre, in px: left half ends at segment n-1's right edge, right starts at segment n.
            double leftCentre = b.X1[n - 1], rightCentre = b.X0[n], half = leftCentre - b.X0[0];
            // opaque shapes drawn after it (an overlay's box over the bar): what they cover isn't drawn (it would land on
            // them); a segment all under them waits, and is drawn whole once they go (their repaint invalidates it)
            var solid = SolidShapesAbove(b.Index).Where(r => r.IntersectsWith(b.R)).ToList();
            void Seg(int x, int w, int c)
            {
                var r = new Rectangle(x, e.Y, w, e.H);
                foreach (var part in solid.Count == 0 ? new List<Rectangle> { r } : Subtract(r, solid))
                {
                    screen.Cmd(Fill(part.X, part.Y, part.Width, part.Height, c));
                    MarkAbove(b, part); // text or shapes drawn over the bar (not solid) go back on top
                }
            }
            for (int k = 0; k < b.X0.Length; k++)
            {
                int x0 = b.X0[k], x1 = b.X1[k], f0, f1, colour;
                if (k < n)
                {
                    f0 = Clamp((int)Math.Round(leftCentre - Math.Max(0, d) * half), x0, x1); f1 = x1; colour = b.Pos;
                    if (f0 >= x1) f0 = f1 = x0;
                }
                else
                {
                    f0 = x0; f1 = Clamp((int)Math.Round(rightCentre + Math.Max(0, -d) * half), x0, x1); colour = b.Neg;
                    if (f1 <= x0) f0 = f1 = x0;
                }
                string state = f0 + "," + f1 + "," + colour;
                if (state == b.SegSent[k]) continue;
                var segR = new Rectangle(x0, e.Y, x1 - x0, e.H);
                if (Covered(segR)) continue;
                if (solid.Count > 0 && Subtract(segR, solid).Count == 0) continue;
                if (f0 > x0) Seg(x0, f0 - x0, b.SegEmpty);
                if (f1 > f0) Seg(f0, f1 - f0, colour);
                if (f1 < x1) Seg(Math.Max(x0, f1), x1 - Math.Max(x0, f1), b.SegEmpty);
                b.SegSent[k] = state;
            }
        }

        private static int Clamp(int x, int lo, int hi) => x < lo ? lo : x > hi ? hi : x;

        private void CheckPopup(Node p, DashValues v, double now)
        {
            PopupWatch shown = null;
            string shownText = null;
            foreach (var w in p.E.Watch ?? new List<PopupWatch>())
            {
                var n = v.Number(w.Bind);
                if (n == null) continue;
                string text = FormatNumber(n.Value, w.Format);
                if (p.Last.TryGetValue(w.Bind, out var last) && last != text && shown == null) { shown = w; shownText = text; }
                p.Last[w.Bind] = text;
            }
            if (shown == null) return;
            p.Until = now + p.E.Duration;
            PopupEvents++;
            DrawPopup(p, shown, shownText);
        }

        private void DrawPopup(Node p, PopupWatch w, string value)
        {
            var r = p.R;
            int[] px;
            using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.Clear(ToColor(PopupKey));
                    using (var path = Rounded(new Rectangle(0, 0, r.Width, r.Height), p.E.Radius))
                    using (var br = new SolidBrush(DashColors.Parse(w.Color ?? "#D3D3D3", Color.LightGray))) g.FillPath(br, path);
                }
                px = Pixels(bmp);
            }
            SendFills(px, r.Width, new Rectangle(0, 0, r.Width, r.Height), PopupKey, r.X, r.Y);
            int text = Rgb565(p.E.Color ?? "#000000");
            int lh = FontHeight(p.E.Font), vh = FontHeight(p.E.ValueFont);
            screen.Cmd(Xstr(new Rectangle(r.X, r.Y + 8, r.Width, lh + 2), p.E.Font, text, 0, 1, 3, w.Label ?? ""));
            screen.Cmd(Xstr(new Rectangle(r.X, r.Y + 8 + lh + 8, r.Width, Math.Min(vh + 4, r.Height - lh - 16)), p.E.ValueFont, text, 0, 1, 3, value));
        }

        // ---------- Formatting ----------

        /// <summary>The text a value element shows; sign = the value's sign (for colour rules).</summary>
        public static string Format(DashElement e, DashValues v, out int sign)
        {
            sign = 0;
            var raw = v.Raw(e.Bind);
            if (raw == null)
            {
                if (v.Preview) return e.PreviewText ?? (e.Samples != null && e.Samples.Length > 0 ? e.Samples[0] : e.Empty ?? "");
                return e.Empty ?? "";
            }
            var fmt = e.Format ?? "0";
            if (fmt == "text")
            {
                // an unformatted decimal (SimHub items without a format) would print every digit: at most 2 places
                if (raw is double dd && !double.IsNaN(dd)) return dd.ToString("0.##", CultureInfo.InvariantCulture);
                if (raw is float ff) return ff.ToString("0.##", CultureInfo.InvariantCulture);
                if (raw is TimeSpan ts) return ts.TotalSeconds > 0 ? FormatNumber(ts.TotalSeconds, "laptime") : e.Empty ?? ""; // lap times
                return Convert.ToString(raw, CultureInfo.InvariantCulture);
            }
            if (fmt == "gear" && raw is string gs) return gs;
            var n = DashValues.ToNumber(raw);
            if (n == null) return Convert.ToString(raw, CultureInfo.InvariantCulture); // text data under a number format
            double x = n.Value * e.Scale;
            if ((fmt == "laptime" || fmt.StartsWith("time:")) && x <= 0) return e.Empty ?? "";
            sign = Math.Abs(x) < 0.005 ? 0 : Math.Sign(x);
            return FormatNumber(x, fmt);
        }

        public static string FormatNumber(double x, string format)
        {
            var ci = CultureInfo.InvariantCulture;
            format = format ?? "0";
            if (format.StartsWith("time:"))
            {
                try { return TimeSpan.FromSeconds(Math.Max(0, x)).ToString(format.Substring(5), ci); }
                catch { format = "laptime"; }
            }
            switch (format)
            {
                case "int": return Math.Round(x).ToString("0", ci);
                case "gear": return x < 0 ? "R" : Math.Round(x) == 0 ? "N" : Math.Round(x).ToString("0", ci);
                case "delta": return (x > 0.004 ? "+" : x < -0.004 ? "-" : "") + Math.Abs(x).ToString("0.00", ci);
                case "laptime":
                    int m = (int)(x / 60);
                    return m + ":" + (x - m * 60).ToString("00.000", ci);
                case "text": return x.ToString(ci);
                default:
                    try { return x.ToString(format, ci); }
                    catch { return x.ToString("0", ci); }
            }
        }

        // ---------- Checks ----------

        public List<string> Check() => CheckDetailed(out _).Select(i => i.ToString()).ToList();

        /// <summary>
        /// Problems the screen would show: text wider or taller than its box (wraps onto a line the screen doesn't show),
        /// glyphs a font lacks, a forced background cutting into shapes, overlapping always-shown text, anything off the
        /// screen once padded, unknown types/fonts/images/keys; plus what drawing it costs.
        /// </summary>
        public List<DashIssue> CheckDetailed(out DashCost cost)
        {
            var issues = new List<DashIssue>();
            void Add(string level, DashElement e, string msg) => issues.Add(new DashIssue { Level = level, Element = e == null ? null : Name(e), Message = msg });

            var known = new HashSet<string> { "rect", "ellipse", "box", "gradient", "image", "label", "value", "bar", "deltabar", "popup", "dim" };
            foreach (var e in def.Elements)
            {
                if (!known.Contains(e.Type ?? "")) { Add("error", e, $"unknown type \"{e.Type}\""); continue; }
                var r = new Rectangle(e.X, e.Y, e.W, e.H);
                if (e.Type != "deltabar" && e.Type != "popup" && e.Type != "dim" && (r.Left < 0 || r.Top < 0 || r.Right + dx > Width || r.Bottom + dy > Height))
                    Add(e.Type == "label" || e.Type == "value" ? "error" : "warning", e, "outside the screen" + (dx > 0 || dy > 0 ? " with the padding" : ""));
                if (e.Type == "image" && (e.Image == null || !images.ContainsKey(e.Image))) Add("error", e, $"image \"{e.Image}\" isn't in the dash's Images");
                foreach (var b in new[] { e.Bind, e.ColorBind }.Concat(e.Visible ?? new List<string>()))
                    if (!string.IsNullOrEmpty(b) && !DashValues.KnownKey(b) && !b.Contains(":"))
                        Add("warning", e, $"unknown data key \"{b}\" (see the bindings list; SimHub properties need \"prop:\")");
                var page = DashPages.PageOf(e);
                if (page.HasValue && def.Pages != null && def.Pages.Count > 0 && page.Value >= def.Pages.Count)
                    Add("warning", e, $"on page {page.Value + 1}, but the dash has {def.Pages.Count} pages (Pages)");
                if (e.Type == "popup" && page.HasValue) Add("warning", e, "a pop-up can't be on a page (it shows over every page)");
            }

            var texts = staticLabels.Concat(dynamic.Where(n => n.Kind == "label" || n.Kind == "value")).ToList();
            foreach (var t in texts)
            {
                var e = t.E;
                int fh = FontHeight(e.Font);
                if (fh == 0) { Add("error", e, $"font {e.Font} doesn't exist"); continue; }
                if (fh > t.R.Height) Add("error", e, $"font {e.Font} is {fh} px tall, box {t.R.Height}");
                var samples = t.Kind == "label" ? new[] { e.Text ?? "" }
                    : (e.Samples ?? new string[0]).Concat(new[] { e.Empty, e.PreviewText }).Where(s => !string.IsNullOrEmpty(s));
                // the text a screen command has room for after its head (the box, font, its colour and the one under it): a
                // long text in a wide box fits the box but not the command, and the wheel shows its start. Its colour: the
                // widest of its colour and colour stops; under it: its Background, else the plain colour under its text
                // (static labels are sent with 0 there), else the widest a colour gets.
                int fg = new[] { e.Color }.Concat((e.ColorStops ?? new List<ColorStop>()).Select(s => s.Color))
                    .Where(c => c != null).Select(Rgb565).DefaultIfEmpty(65535).OrderByDescending(c => c.ToString(CultureInfo.InvariantCulture).Length).First();
                int bgc = e.Background != null ? Rgb565(e.Background)
                    : t.Kind == "label" && staticLabels.Contains(t) ? 0
                    : ColourUnder(e, BandArea(t, -1)) ?? 65535;
                int room = MaxCommand - Xstr(t.R, e.Font, fg, bgc, 1, 1, "").Length;
                foreach (var sample in samples)
                {
                    int w = TextWidth(e.Font, Clean(sample));
                    if (w < 0) Add("error", e, $"font {e.Font} has no glyph for part of \"{sample}\"");
                    else if (w > t.R.Width) Add("error", e, $"\"{sample}\" is {w} px wide, box {t.R.Width}");
                    if (Clean(sample).Length > room)
                        Add("error", e, $"\"{sample}\" is cut to its first {room} characters on the wheel (a screen command holds {MaxCommand}, its box and colours take the rest): split it into two labels or shorten it");
                }
                if (t.Kind == "value" && (e.Samples == null || e.Samples.Length == 0)) Add("warning", e, "no Samples: can't check the widest text fits");
                // (what's under it whenever it shows: the static layer and the shapes that show with it, its overlay's box)
                var underIt = e.Background != null ? ColourUnder(e, BandArea(t, -1)) : null;
                if (e.Background != null && underIt.HasValue && underIt.Value != Rgb565(e.Background))
                    Add("warning", e, "its Background differs from what's drawn under it");
            }
            // Overlaps between text shown together: a label only covers its text (no background), a value its whole box
            // (its background is redrawn); touching by a pixel or two doesn't count.
            Rectangle Covers(Node t)
            {
                if (t.Kind != "label") return t.R;
                int w = Math.Max(0, TextWidth(t.E.Font, Clean(t.Text)));
                int x = t.XCen == 1 ? t.R.X + (t.R.Width - w) / 2 : t.XCen == 2 ? t.R.Right - w : t.R.X;
                int fh = FontHeight(t.E.Font);   // its text's rows (drawn centred in the box)
                return fh > 0 && fh <= t.R.Height ? new Rectangle(x, t.R.Y + (t.R.Height - fh) / 2, w, fh) : new Rectangle(x, t.R.Y, w, t.R.Height);
            }
            var always = texts.Where(t => (t.E.Visible == null || t.E.Visible.Count == 0) || t.E.PreviewVisible != false).ToList();
            for (int i = 0; i < always.Count; i++)
                for (int j = i + 1; j < always.Count; j++)
                {
                    if (always[i].Kind == "label" && always[j].Kind == "label") continue; // both transparent: harmless
                    if (DashPages.Apart(always[i].E, always[j].E)) continue; // on different pages: never shown together
                    var o = Rectangle.Intersect(Covers(always[i]), Covers(always[j]));
                    if (o.Width > 2 && o.Height > 2)
                        Add("warning", always[i].E, $"overlaps {Name(always[j].E)}");
                }

            var fills = MergeRects(staticPx, Width, new Rectangle(0, 0, Width, Height)).Where(r => r[4] != 0).ToList();
            cost = new DashCost
            {
                StaticFills = fills.Count,
                StaticBytes = fills.Sum(r => Fill(r[0], r[1], r[2], r[3], r[4]).Length + 3) + staticLabels.Count * 50,
                Elements = def.Elements.Count,
                DynamicElements = dynamic.Count + popups.Count,
            };
            cost.StaticSeconds = cost.StaticBytes / BytesPerSecond;
            var ramTiles = DashRam.TilesOf(def);
            if (ramTiles != null)
            {
                cost.RamBytes = ramTiles.Bytes; cost.RamFiles = ramTiles.FileCount;
                if (cost.RamBytes + cost.RamFiles * ScreenRam.FileOverhead > ScreenRam.Budget)
                    Add("warning", null, $"takes {DashRam.Text(cost.RamBytes)} of the screen's RAM drive ({ScreenRam.Budget / 1024} KB): on a wheel with it, it's drawn with rectangles instead; fewer or smaller pictures and gradients fit");
            }
            foreach (var n in dynamic.Where(n => n.Kind == "value"))
                if (n.E.Background == null && !n.StaticBg.HasValue)
                {
                    var r = Clip(n.R);
                    int f = r.Width > 0 && r.Height > 0 ? MergeRects(staticPx, Width, r).Count : 0;
                    cost.RedrawnValues.Add($"{Name(n.E)} ({f} fills per change)");
                    if (f > 80) Add("warning", n.E, $"busy background: {f} fills redrawn per change (slow; give it a plain background)");
                }
            if (cost.StaticSeconds > 8) Add("warning", null, $"takes ~{cost.StaticSeconds:0} s to draw (at 25 KB/s); fewer image colours or smaller images draw faster");
            return issues;
        }

        private static string Name(DashElement e) => e.Name ?? e.Text ?? e.Bind ?? e.Type;

        /// <summary>How far the dash can move right/down.</summary>
        public static (int Right, int Down) Room(DashDefinition d) =>
            d.Elements.Count == 0 ? (0, 0) :
            (Math.Max(0, Width - d.Elements.Max(e => e.X + e.W)), Math.Max(0, Height - d.Elements.Max(e => e.Y + e.H)));
    }

    /// <summary>
    /// Draws the commands the way the screen would, for previews: text in each glyph's real cell width (FontMetrics),
    /// cut where the screen would wrap; glyph shapes are Segoe UI stand-ins.
    /// </summary>
    internal sealed class PreviewScreen : IScreenSink, IDisposable
    {
        public readonly Bitmap Bitmap = new Bitmap(DashRenderer.Width, DashRenderer.Height, PixelFormat.Format32bppRgb);
        private readonly Graphics g;
        private readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();
        private readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>();
        public long Bytes;
        public int Commands;

        public PreviewScreen()
        {
            g = Graphics.FromImage(Bitmap);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Black);
        }

        public void Cmd(string cmd)
        {
            Bytes += cmd.Length + 3;
            Commands++;
            try
            {
                if (cmd.StartsWith("cls ")) g.Clear(DashRenderer.ToColor(int.Parse(cmd.Substring(4), CultureInfo.InvariantCulture)));
                else if (cmd.StartsWith("fill "))
                {
                    var a = cmd.Substring(5).Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    using (var br = new SolidBrush(Ink(a[4]))) g.FillRectangle(br, a[0], a[1], a[2], a[3]);
                }
                else if (cmd.StartsWith("aph="))
                {
                    // the screen's global alpha (0-127): fills, polygons and circles blend with it
                    if (int.TryParse(cmd.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var aph)) alpha = Math.Max(0, Math.Min(127, aph));
                }
                else if (cmd.StartsWith("draw_h "))
                {
                    // the gauge needle: emWin's anti-aliased polygon (ScreenShapes), plus its hub circle
                    var p = ScreenShapes.Polygon(cmd);
                    var a = cmd.Substring(7).Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    Smooth(() =>
                    {
                        if (p != null) using (var br = new SolidBrush(Ink(a[5]))) g.FillPolygon(br, p);
                        if (a.Length > 7 && a[7] > 0)
                            using (var br = new SolidBrush(Ink(a[6]))) g.FillEllipse(br, a[0] - a[7] / 2, a[1] - a[7] / 2, a[7] / 2 * 2 + 1, a[7] / 2 * 2 + 1);
                    });
                }
                else if (cmd.StartsWith("xstr ")) Xstr(cmd);
                else if (cmd.StartsWith("sets \"ramv: "))
                {
                    // a picture from the screen's RAM drive (a dash tile): drawn from the registry, as the screen does
                    var r = ScreenTiles.ParseRamv(cmd);
                    if (r != null && ScreenTiles.Registry.TryGetValue(r.Item3, out var jpeg))
                    {
                        if (!pictures.TryGetValue(r.Item3, out var pic))
                            using (var ms = new System.IO.MemoryStream(jpeg)) pictures[r.Item3] = pic = To565(new Bitmap(Image.FromStream(ms)));
                        var mode = g.InterpolationMode;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.DrawImage(pic, new Rectangle(r.Item1, r.Item2, pic.Width, pic.Height));
                        g.InterpolationMode = mode;
                    }
                }
                else if (cmd.StartsWith("cirs "))
                {
                    var a = cmd.Substring(5).Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    // emWin's anti-aliased filled circle: columns x-r .. x+r, a disc of radius r + 1/2 about the pixel centre
                    Smooth(() => { using (var br = new SolidBrush(Ink(a[3]))) g.FillEllipse(br, a[0] - a[2], a[1] - a[2], a[2] * 2 + 1, a[2] * 2 + 1); });
                }
            }
            catch { }
        }

        /// <summary>
        /// Draws anti-aliased as emWin does: whole coordinates are pixel corners (GDI+'s default puts them on pixel centres,
        /// so two polygons sharing an edge would each half-cover that column: a seam the wheel doesn't show).
        /// </summary>
        private void Smooth(Action draw)
        {
            var mode = g.SmoothingMode;
            var offset = g.PixelOffsetMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            try { draw(); }
            finally { g.SmoothingMode = mode; g.PixelOffsetMode = offset; }
        }

        /// <summary>The screen's global alpha (`aph`, 0-127; 127 = solid).</summary>
        private int alpha = 127;

        /// <summary>An RGB565 colour as drawn now (with the global alpha).</summary>
        private Color Ink(int c565)
        {
            var c = DashRenderer.ToColor(c565);
            return alpha >= 127 ? c : Color.FromArgb(alpha * 255 / 127, c);
        }

        /// <summary>A decoded picture rounded to RGB565 like the screen shows it (the full 24-bit decode showed a flat
        /// panel a few levels off the exact colour behind values, as boxes that aren't there on the wheel).</summary>
        private static Bitmap To565(Bitmap bmp)
        {
            var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            for (int i = 0; i < px.Length; i++) px[i] = DashRenderer.ToColor(DashColors.To565(Color.FromArgb(px[i]))).ToArgb();
            System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        private void Xstr(string cmd)
        {
            int q = cmd.IndexOf('"');
            var a = cmd.Substring(5, q - 6).Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            string text = cmd.Substring(q + 1, cmd.Length - q - 2);
            var r = new Rectangle(a[0], a[1], a[2], a[3]);
            if (a[9] == 1) using (var br = new SolidBrush(DashRenderer.ToColor(a[6]))) g.FillRectangle(br, r);
            int font = a[4], h = DashRenderer.FontHeight(font);
            var cells = new List<(char C, int W)>();
            int width = 0;
            foreach (char c in text)
            {
                int cw = Math.Max(0, DashRenderer.TextWidth(font, c.ToString()));
                if (width + cw > r.Width) break;
                cells.Add((c, cw));
                width += cw;
            }
            int x = a[7] == 0 ? r.X : a[7] == 1 ? r.X + (r.Width - width) / 2 : r.Right - width;
            int y = a[8] == 0 ? r.Y : a[8] == 1 ? r.Y + (r.Height - h) / 2 : r.Bottom - h;
            if (!fonts.TryGetValue(font, out var f))
                fonts[font] = f = new Font("Segoe UI", Math.Max(6, h * 0.78f), h >= 90 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            using (var br = new SolidBrush(DashRenderer.ToColor(a[5])))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip })
            {
                g.SetClip(r);
                foreach (var (c, w) in cells)
                {
                    g.DrawString(c.ToString(), f, br, new RectangleF(x, y, w, h), sf);
                    x += w;
                }
                g.ResetClip();
            }
        }

        public void Flush() { }

        /// <summary>Puts RGB565 pixels (area-sized, row by row) on the screen at `area`.</summary>
        public void Put(int[] px, Rectangle area)
        {
            var data = Bitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            var row = new int[area.Width];
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++) row[x] = DashRenderer.ToColor(px[y * area.Width + x]).ToArgb();
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, area.Width);
            }
            Bitmap.UnlockBits(data);
        }

        /// <summary>The screen's pixels in `area`, RGB565, row by row.</summary>
        public int[] Get(Rectangle area)
        {
            var data = Bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var px = new int[area.Width * area.Height];
            var row = new int[area.Width];
            for (int y = 0; y < area.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, area.Width);
                for (int x = 0; x < area.Width; x++) px[y * area.Width + x] = DashColors.To565(Color.FromArgb(row[x]));
            }
            Bitmap.UnlockBits(data);
            return px;
        }

        public byte[] Png()
        {
            using (var ms = new MemoryStream()) { Bitmap.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        public void Dispose()
        {
            foreach (var f in fonts.Values) f.Dispose();
            foreach (var p in pictures.Values) p.Dispose();
            g.Dispose();
            Bitmap.Dispose();
        }
    }
}
