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
        private readonly int[] staticPx;
        private readonly List<Node> staticLabels = new List<Node>();
        private readonly List<Node> dynamic = new List<Node>();
        private readonly List<Node> popups = new List<Node>();
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
            public int? StaticBg;           // the one colour under the box in the static layer, if it is one
            public int[] Px;                // shape pixels for Key (Transparent = not drawn)
            public string PxKey;
            // bar
            public int FillEnd;
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
                    default: continue; // unknown types are reported by Check
                }
                if (n.Kind == "label" && !e.IsDynamic) { n.Colour = DashColors.Parse(e.Color, Color.White); staticLabels.Add(n); }
                else if (e.IsDynamic || n.Kind != "shape") dynamic.Add(n);
            }
            staticPx = BuildStatic();
            foreach (var n in dynamic) n.StaticBg = Uniform(staticPx, Width, Clip(n.R));
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
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None; // exact colours, so rectangles merge
                    g.Clear(Color.Black);
                    foreach (var e in def.Elements)
                        if (IsShape(e) && !e.IsDynamic)
                        {
                            DrawShape(g, e, new Point(e.X, e.Y), DashColors.Parse(e.Color, Color.White));
                            if (e.Type == "image") Quantize(bmp, Clip(new Rectangle(e.X, e.Y, e.W, e.H)), e.MaxColors);
                        }
                }
                return Pixels(bmp);
            }
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
                // popularity on a 4-bit-per-channel grid, then each pixel to the nearest chosen colour
                var counts = new Dictionary<int, int>();
                foreach (var p in px) { int k = ((p >> 20) & 0xF) << 8 | ((p >> 12) & 0xF) << 4 | ((p >> 4) & 0xF); counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1; }
                var palette = counts.OrderByDescending(kv => kv.Value).Take(max)
                    .Select(kv => (R: ((kv.Key >> 8) & 0xF) * 17, G: ((kv.Key >> 4) & 0xF) * 17, B: (kv.Key & 0xF) * 17)).ToArray();
                for (int i = 0; i < px.Length; i++)
                {
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
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var under = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.Clear(Color.Transparent);
                    DrawShape(g, e, Point.Empty, colour);
                }
                if (e.Type == "image") Quantize(bmp, new Rectangle(0, 0, w, h), e.MaxColors);
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
                        int r = (p >> 16) & 255, gg = (p >> 8) & 255, b = p & 255;
                        if (a < 250)
                        {
                            int sx = e.X + x, sy = e.Y + y;
                            var bg = sx >= 0 && sy >= 0 && sx < Width && sy < Height ? ToColor(staticPx[sy * Width + sx]) : Color.Black;
                            r = (r * a + bg.R * (255 - a)) / 255; gg = (gg * a + bg.G * (255 - a)) / 255; b = (b * a + bg.B * (255 - a)) / 255;
                        }
                        px[y * w + x] = (r >> 3 << 11) | (gg >> 2 << 5) | (b >> 3);
                    }
                return px;
            }
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
        private string Xstr(Rectangle r, int font, int colour, int bg, int xcen, int sta, string text) =>
            string.Format(CultureInfo.InvariantCulture, "xstr {0},{1},{2},{3},{4},{5},{6},{7},1,{8},\"{9}\"",
                r.X + dx, r.Y + dy, r.Width, r.Height, font, colour, bg, xcen, sta, Clean(text));

        /// <summary>Quotes would end the command; anything outside ASCII has no glyph.</summary>
        private static string Clean(string s) => new string((s ?? "").Where(c => c >= 32 && c < 127 && c != '"').ToArray());

        // ---------- Drawing ----------

        /// <summary>Page 0 (no screen timers), its objects hidden, cleared, then the static layer.</summary>
        public void DrawAll()
        {
            screen.Cmd("page 0");
            screen.Cmd("vis 255,0");
            screen.Cmd("cls 0");
            SendFills(staticPx, Width, new Rectangle(0, 0, Width, Height), 0, 0, 0);
            foreach (var l in staticLabels) DrawLabel(l, l.Colour);
            foreach (var n in dynamic) { n.Shown = false; n.Sent = null; if (n.SegSent != null) for (int k = 0; k < n.SegSent.Length; k++) n.SegSent[k] = null; }
            foreach (var p in popups) { p.Until = -1; p.Last.Clear(); }
            screen.Flush();
        }

        private void DrawLabel(Node n, Color colour)
        {
            var bg = n.E.Background != null ? Rgb565(n.E.Background) : 0;
            screen.Cmd(Xstr(n.R, n.E.Font, DashColors.To565(colour), bg, n.XCen, 3, n.Text));
        }

        /// <summary>Redraws what changed. `now` in seconds.</summary>
        public void Update(DashValues v, double now)
        {
            current = v;
            foreach (var p in popups) CheckPopup(p, v, now);

            // What each dynamic element wants to show now
            foreach (var n in dynamic) Evaluate(n, v);

            // Hidden since last time: repaint their areas (this also marks what's under/over them for a redraw)
            foreach (var n in dynamic)
                if (n.Shown && !n.Visible) { n.Shown = false; n.Sent = null; Repaint(n.R); }
            foreach (var p in popups)
                if (p.Until >= 0 && now >= p.Until) { p.Until = -1; Repaint(p.R); }

            // Draw in element order
            foreach (var n in dynamic)
            {
                if (!n.Visible || Covered(n.R)) continue;
                switch (n.Kind)
                {
                    case "shape":
                        if (n.Shown && n.Sent == n.Key) break;
                        DrawShapeNode(n, n.R);
                        n.Shown = true; n.Sent = n.Key;
                        MarkAbove(n);
                        break;
                    case "label":
                        if (n.Shown && n.Sent == n.Key) break;
                        DrawLabel(n, n.Colour);
                        n.Shown = true; n.Sent = n.Key;
                        break;
                    case "value":
                        if (n.Shown && n.Sent == n.Key) break;
                        DrawValue(n);
                        n.Shown = true; n.Sent = n.Key;
                        break;
                    case "bar":
                        if (n.Shown && n.Sent == n.Key) break;
                        DrawBar(n);
                        n.Shown = true; n.Sent = n.Key;
                        break;
                    case "deltabar":
                        UpdateDeltaBar(n, v);
                        n.Shown = true;
                        break;
                }
            }
            screen.Flush();
        }

        private bool Covered(Rectangle r) => popups.Any(p => p.Until >= 0 && p.R.IntersectsWith(r));

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
            if (n.Px == null || n.PxKey != n.Key) { n.Px = ShapePixels(n, n.Colour); n.PxKey = n.Key; }
            var clip = Rectangle.Intersect(Rectangle.Intersect(n.R, area), new Rectangle(0, 0, Width, Height));
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var local = new Rectangle(clip.X - n.R.X, clip.Y - n.R.Y, clip.Width, clip.Height);
            SendFills(n.Px, Math.Max(1, n.E.W), local, Transparent, n.R.X, n.R.Y);
        }

        /// <summary>Later dynamic elements over `n` must be drawn again.</summary>
        private void MarkAbove(Node n)
        {
            foreach (var m in dynamic)
                if (m.Index > n.Index && m.R.IntersectsWith(n.R)) Invalidate(m);
        }

        private static void Invalidate(Node m)
        {
            m.Sent = null;
            if (m.SegSent != null) for (int k = 0; k < m.SegSent.Length; k++) m.SegSent[k] = null;
        }

        /// <summary>
        /// An area back as it should look under the dynamic text: the static layer, static labels touching it, and the
        /// dynamic shapes shown there (clipped); dynamic text, bars and later shapes there are marked for a redraw.
        /// </summary>
        private void Repaint(Rectangle area)
        {
            area = Clip(area);
            if (area.Width <= 0 || area.Height <= 0) return;
            SendFills(staticPx, Width, area, -2, 0, 0);
            foreach (var l in staticLabels) if (l.R.IntersectsWith(area)) DrawLabel(l, l.Colour);
            foreach (var n in dynamic)
            {
                if (!n.R.IntersectsWith(area)) continue;
                if (n.Kind == "shape" && n.Shown && n.Visible) DrawShapeNode(n, area);
                else Invalidate(n);
            }
        }

        /// <summary>The background colour under a value/bar now, or null when it isn't one colour (then: repaint).</summary>
        private int? BackgroundUnder(Node n)
        {
            if (n.E.Background != null) return Rgb565(n.E.Background);
            int? under = n.StaticBg;
            foreach (var m in dynamic)
            {
                if (m.Index >= n.Index) break;
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
            var bg = BackgroundUnder(n);
            if (bg.HasValue) { screen.Cmd(Xstr(n.R, n.E.Font, colour, bg.Value, n.XCen, 1, n.Text)); return; }
            Repaint(n.R);
            screen.Cmd(Xstr(n.R, n.E.Font, colour, 0, n.XCen, 3, n.Text));
        }

        private void DrawBar(Node n)
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
            if (empty.Width > 0 && empty.Height > 0)
            {
                int? bg = e.Fill != null ? Rgb565(e.Fill) : BackgroundUnder(n);
                if (bg.HasValue) screen.Cmd(Fill(empty.X, empty.Y, empty.Width, empty.Height, bg.Value));
                else Repaint(empty);
            }
            if (filled.Width > 0 && filled.Height > 0)
                screen.Cmd(Fill(filled.X, filled.Y, filled.Width, filled.Height, DashColors.To565(n.Colour)));
        }

        private void UpdateDeltaBar(Node b, DashValues v)
        {
            var e = b.E;
            double d = Math.Max(-1, Math.Min(1, (v.Number(e.Bind) ?? 0) / (e.Range <= 0 ? 1 : e.Range)));
            int n = e.Segments;
            // Halves measured from the centre, in px: left half ends at segment n-1's right edge, right starts at segment n.
            double leftCentre = b.X1[n - 1], rightCentre = b.X0[n], half = leftCentre - b.X0[0];
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
                if (Covered(new Rectangle(x0, e.Y, x1 - x0, e.H))) continue;
                if (f0 > x0) screen.Cmd(Fill(x0, e.Y, f0 - x0, e.H, b.SegEmpty));
                if (f1 > f0) screen.Cmd(Fill(f0, e.Y, f1 - f0, e.H, colour));
                if (f1 < x1) screen.Cmd(Fill(Math.Max(x0, f1), e.Y, x1 - Math.Max(x0, f1), e.H, b.SegEmpty));
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

            var known = new HashSet<string> { "rect", "ellipse", "box", "gradient", "image", "label", "value", "bar", "deltabar", "popup" };
            foreach (var e in def.Elements)
            {
                if (!known.Contains(e.Type ?? "")) { Add("error", e, $"unknown type \"{e.Type}\""); continue; }
                var r = new Rectangle(e.X, e.Y, e.W, e.H);
                if (e.Type != "deltabar" && e.Type != "popup" && (r.Left < 0 || r.Top < 0 || r.Right + dx > Width || r.Bottom + dy > Height))
                    Add(e.Type == "label" || e.Type == "value" ? "error" : "warning", e, "outside the screen" + (dx > 0 || dy > 0 ? " with the padding" : ""));
                if (e.Type == "image" && (e.Image == null || !images.ContainsKey(e.Image))) Add("error", e, $"image \"{e.Image}\" isn't in the dash's Images");
                foreach (var b in new[] { e.Bind, e.ColorBind }.Concat(e.Visible ?? new List<string>()))
                    if (!string.IsNullOrEmpty(b) && !DashValues.KnownKey(b) && !b.Contains(":"))
                        Add("warning", e, $"unknown data key \"{b}\" (see the bindings list; SimHub properties need \"prop:\")");
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
                foreach (var sample in samples)
                {
                    int w = TextWidth(e.Font, Clean(sample));
                    if (w < 0) Add("error", e, $"font {e.Font} has no glyph for part of \"{sample}\"");
                    else if (w > t.R.Width) Add("error", e, $"\"{sample}\" is {w} px wide, box {t.R.Width}");
                }
                if (t.Kind == "value" && (e.Samples == null || e.Samples.Length == 0)) Add("warning", e, "no Samples: can't check the widest text fits");
                if (e.Background != null && t.StaticBg.HasValue && t.StaticBg.Value != Rgb565(e.Background))
                    Add("warning", e, "its Background differs from what's drawn under it");
            }
            // Overlaps between text shown together: a label only covers its text (no background), a value its whole box
            // (its background is redrawn); touching by a pixel or two doesn't count.
            Rectangle Covers(Node t)
            {
                if (t.Kind != "label") return t.R;
                int w = Math.Max(0, TextWidth(t.E.Font, Clean(t.Text)));
                int x = t.XCen == 1 ? t.R.X + (t.R.Width - w) / 2 : t.XCen == 2 ? t.R.Right - w : t.R.X;
                return new Rectangle(x, t.R.Y, w, t.R.Height);
            }
            var always = texts.Where(t => (t.E.Visible == null || t.E.Visible.Count == 0) || t.E.PreviewVisible != false).ToList();
            for (int i = 0; i < always.Count; i++)
                for (int j = i + 1; j < always.Count; j++)
                {
                    if (always[i].Kind == "label" && always[j].Kind == "label") continue; // both transparent: harmless
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
                    using (var br = new SolidBrush(DashRenderer.ToColor(a[4]))) g.FillRectangle(br, a[0], a[1], a[2], a[3]);
                }
                else if (cmd.StartsWith("xstr ")) Xstr(cmd);
            }
            catch { }
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

        public byte[] Png()
        {
            using (var ms = new MemoryStream()) { Bitmap.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        public void Dispose()
        {
            foreach (var f in fonts.Values) f.Dispose();
            g.Dispose();
            Bitmap.Dispose();
        }
    }
}
