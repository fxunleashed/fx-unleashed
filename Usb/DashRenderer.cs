using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Draws a DashDefinition on the FX Pro screen with its own commands (ported from FXProDashes tools/dash, verified on
    /// the wheel). Shapes go into a bitmap once and out as merged `fill` rectangles; labels as `xstr` with no
    /// background; values as solid-background `xstr`, only when their text or colour changes. A pop-up is drawn over
    /// the page and its area restored from the bitmap afterwards. Offsets pad the whole dash away from the screen's
    /// edges.
    /// </summary>
    internal sealed class DashRenderer
    {
        public const int Width = 800, Height = 480;
        private const int PopupKey = 0xF81F; // magenta: the transparent colour of the pop-up's bitmap

        private readonly IScreenSink screen;
        private readonly DashDefinition def;
        private readonly int dx, dy;
        private readonly int[] staticPx;
        private readonly List<Text> labels = new List<Text>();
        private readonly List<Text> values = new List<Text>();
        private readonly List<Bar> bars = new List<Bar>();
        private readonly List<Popup> popups = new List<Popup>();

        private sealed class Text
        {
            public DashElement E;
            public Rectangle R;
            public int Colour, Bg, XCen;
            public string Value, Sent;
            public int ValueColour, SentColour;
        }

        private sealed class Bar
        {
            public DashElement E;
            public int[] X0, X1;
            public string[] Sent;
            public int Pos, Neg, Empty;
            public Rectangle Bounds;
        }

        private sealed class Popup
        {
            public DashElement E;
            public Rectangle R;
            public double Until = -1;
            public Dictionary<string, string> Last = new Dictionary<string, string>();
        }

        public DashRenderer(IScreenSink screen, DashDefinition def, int left, int top)
        {
            this.screen = screen;
            this.def = def;
            dx = left; dy = top;
            foreach (var e in def.Elements)
            {
                var r = new Rectangle(e.X, e.Y, e.W, e.H);
                switch (e.Type)
                {
                    case "label":
                        labels.Add(new Text { E = e, R = r, Colour = Rgb565(e.Color), Bg = Rgb565(e.Background ?? "#000000"), XCen = Align(e.Align), Value = e.Text ?? "" });
                        break;
                    case "value":
                        values.Add(new Text { E = e, R = r, Colour = Rgb565(e.Color), Bg = Rgb565(e.Background ?? "#000000"), XCen = Align(e.Align) });
                        break;
                    case "deltabar":
                        var xs = e.SegmentLefts();
                        bars.Add(new Bar
                        {
                            E = e, X0 = xs, X1 = xs.Select(x => x + e.SegmentWidth).ToArray(), Sent = new string[xs.Length],
                            Pos = Rgb565(e.PositiveColor ?? "#FF0000"), Neg = Rgb565(e.NegativeColor ?? "#00FF00"), Empty = Rgb565(e.SegmentColor),
                            Bounds = Rectangle.FromLTRB(xs.Min(), e.Y, xs.Max() + e.SegmentWidth, e.Y + e.H),
                        });
                        break;
                    case "popup":
                        popups.Add(new Popup { E = e, R = r });
                        break;
                }
            }
            staticPx = Pixels(DrawStatic());
        }

        // ---------- Colours and fonts ----------

        public static int Rgb565(string hex)
        {
            int v = ParseHex(hex);
            return (((v >> 16) & 255) >> 3 << 11) | (((v >> 8) & 255) >> 2 << 5) | ((v & 255) >> 3);
        }

        private static int ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2); // #AARRGGBB
            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        public static Color ToColor(int c565)
        {
            int r = (c565 >> 11) & 31, g = (c565 >> 5) & 63, b = c565 & 31;
            return Color.FromArgb((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
        }

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

        // ---------- Static layer ----------

        private Bitmap DrawStatic()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None; // exact colours only, so rectangles merge
                g.Clear(Color.Black);
                foreach (var e in def.Elements)
                {
                    var r = new Rectangle(e.X, e.Y, e.W, e.H);
                    switch (e.Type)
                    {
                        case "rect":
                            using (var br = Brush(e.Color)) g.FillRectangle(br, r);
                            break;
                        case "ellipse":
                            using (var br = Brush(e.Color)) g.FillEllipse(br, r);
                            if (e.Border > 0)
                            {
                                r.Inflate(-e.Border, -e.Border);
                                using (var br = Brush(e.Fill ?? "#000000")) g.FillEllipse(br, r);
                            }
                            break;
                        case "box":
                            using (var p = Rounded(r, e.Radius)) using (var br = Brush(e.Color)) g.FillPath(br, p);
                            var inner = r; inner.Inflate(-e.Border, -e.Border);
                            using (var p = Rounded(inner, Math.Max(0, e.Radius - e.Border))) using (var br = Brush(e.Fill ?? "#000000")) g.FillPath(br, p);
                            break;
                    }
                }
                foreach (var b in bars)
                    using (var br = new SolidBrush(ToColor(b.Empty)))
                        for (int k = 0; k < b.X0.Length; k++) g.FillRectangle(br, b.X0[k], b.E.Y, b.X1[k] - b.X0[k], b.E.H);
            }
            return bmp;
        }

        private static SolidBrush Brush(string hex) => new SolidBrush(ToColor(Rgb565(hex)));

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

        private static int[] Pixels(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var px = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            bmp.Dispose();
            for (int i = 0; i < px.Length; i++)
                px[i] = (((px[i] >> 16) & 255) >> 3 << 11) | (((px[i] >> 8) & 255) >> 2 << 5) | ((px[i] & 255) >> 3);
            return px;
        }

        /// <summary>`fill` commands for an area: runs along each row, identical runs stacked down the rows.</summary>
        private List<string> Fills(int[] px, int stride, Rectangle area, int skip, int ox = 0, int oy = 0)
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
                    long key = ((long)x0 << 40) | ((long)(x - x0) << 20) | (uint)c;
                    if (open.TryGetValue(key, out var rect)) { rect[3]++; open.Remove(key); }
                    else rect = new[] { x0, y, x - x0, 1, c };
                    next[key] = rect;
                }
                done.AddRange(open.Values);
                open = next;
            }
            done.AddRange(open.Values);
            var cmds = new List<string>(done.Count);
            foreach (var r in done)
                if (r[4] != skip) cmds.Add(Fill(r[0] + ox, r[1] + oy, r[2], r[3], r[4]));
            return cmds;
        }

        private string Fill(int x, int y, int w, int h, int c) =>
            string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", x + dx, y + dy, w, h, c);

        // sta (TJC): 1 = solid background, 3 = none.
        private string Xstr(Rectangle r, int font, int colour, int bg, int xcen, int sta, string text) =>
            string.Format(CultureInfo.InvariantCulture, "xstr {0},{1},{2},{3},{4},{5},{6},{7},1,{8},\"{9}\"",
                r.X + dx, r.Y + dy, r.Width, r.Height, font, colour, bg, xcen, sta, Clean(text));

        /// <summary>Quotes would end the command; anything outside ASCII has no glyph.</summary>
        private static string Clean(string s)
        {
            var chars = s.Where(c => c >= 32 && c < 127 && c != '"').ToArray();
            return new string(chars);
        }

        // ---------- Drawing ----------

        /// <summary>Page 0 (no screen timers), its objects hidden, cleared, then the static layer.</summary>
        public void DrawAll()
        {
            screen.Cmd("page 0");
            screen.Cmd("vis 255,0");
            screen.Cmd("cls 0");
            foreach (var c in Fills(staticPx, Width, new Rectangle(0, 0, Width, Height), 0)) screen.Cmd(c);
            foreach (var l in labels) screen.Cmd(Xstr(l.R, l.E.Font, l.Colour, l.Bg, l.XCen, 3, l.Value));
            foreach (var t in values) t.Sent = null;
            foreach (var b in bars) for (int k = 0; k < b.Sent.Length; k++) b.Sent[k] = null;
            foreach (var p in popups) { p.Until = -1; p.Last.Clear(); }
            screen.Flush();
        }

        /// <summary>Redraws what changed. `now` in seconds.</summary>
        public void Update(DashValues v, double now)
        {
            foreach (var p in popups) CheckPopup(p, v, now);

            foreach (var t in values)
            {
                var e = t.E;
                t.Value = Format(e, v, out var sign);
                t.ValueColour = sign > 0 && e.PositiveColor != null ? Rgb565(e.PositiveColor)
                              : sign < 0 && e.NegativeColor != null ? Rgb565(e.NegativeColor) : t.Colour;
            }

            foreach (var p in popups)
                if (p.Until >= 0 && now >= p.Until) { p.Until = -1; Restore(p.R); }

            foreach (var t in values)
            {
                if (t.Value == t.Sent && t.ValueColour == t.SentColour) continue;
                if (Covered(t.R)) continue;
                screen.Cmd(Xstr(t.R, t.E.Font, t.ValueColour, t.Bg, t.XCen, 1, t.Value));
                t.Sent = t.Value;
                t.SentColour = t.ValueColour;
            }

            foreach (var b in bars) UpdateBar(b, v);
            screen.Flush();
        }

        private bool Covered(Rectangle r) => popups.Any(p => p.Until >= 0 && p.R.IntersectsWith(r));

        private void UpdateBar(Bar b, DashValues v)
        {
            var e = b.E;
            double d = Math.Max(-1, Math.Min(1, (v.Number(e.Bind) ?? 0) / (e.Range <= 0 ? 1 : e.Range)));
            int n = e.Segments;
            // Halves measured from the centre, in px: left half ends at segment n-1's right edge, right starts at segment n.
            double leftCentre = b.X1[n - 1], rightCentre = b.X0[n], half = leftCentre - b.X0[0];
            for (int k = 0; k < b.X0.Length; k++)
            {
                int x0 = b.X0[k], x1 = b.X1[k], f0 = x0, f1 = x0, colour;
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
                if (state == b.Sent[k]) continue;
                if (Covered(new Rectangle(x0, e.Y, x1 - x0, e.H))) continue;
                if (f0 > x0) screen.Cmd(Fill(x0, e.Y, f0 - x0, e.H, b.Empty));
                if (f1 > f0) screen.Cmd(Fill(f0, e.Y, f1 - f0, e.H, colour));
                if (f1 < x1) screen.Cmd(Fill(Math.Max(x0, f1), e.Y, x1 - Math.Max(x0, f1), e.H, b.Empty));
                b.Sent[k] = state;
            }
        }

        private static int Clamp(int x, int lo, int hi) => x < lo ? lo : x > hi ? hi : x;

        private void CheckPopup(Popup p, DashValues v, double now)
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

        private void DrawPopup(Popup p, PopupWatch w, string value)
        {
            var r = p.R;
            using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.Clear(ToColor(PopupKey));
                    using (var path = Rounded(new Rectangle(0, 0, r.Width, r.Height), p.E.Radius))
                    using (var br = Brush(w.Color ?? "#D3D3D3")) g.FillPath(br, path);
                }
                foreach (var c in Fills(Pixels(bmp.Clone(new Rectangle(0, 0, r.Width, r.Height), PixelFormat.Format32bppRgb)), r.Width,
                                        new Rectangle(0, 0, r.Width, r.Height), PopupKey, r.X, r.Y))
                    screen.Cmd(c);
            }
            int text = Rgb565(p.E.Color ?? "#000000");
            int lh = FontHeight(p.E.Font), vh = FontHeight(p.E.ValueFont);
            screen.Cmd(Xstr(new Rectangle(r.X, r.Y + 8, r.Width, lh + 2), p.E.Font, text, 0, 1, 3, w.Label ?? ""));
            screen.Cmd(Xstr(new Rectangle(r.X, r.Y + 8 + lh + 8, r.Width, Math.Min(vh + 4, r.Height - lh - 16)), p.E.ValueFont, text, 0, 1, 3, value));
        }

        /// <summary>Static layer back under an area, labels touching it redrawn, values and bar segments there resent.</summary>
        private void Restore(Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, Width, Height));
            foreach (var c in Fills(staticPx, Width, area, -1)) screen.Cmd(c);
            foreach (var l in labels)
                if (l.R.IntersectsWith(area)) screen.Cmd(Xstr(l.R, l.E.Font, l.Colour, l.Bg, l.XCen, 3, l.Value));
            foreach (var t in values) if (t.R.IntersectsWith(area)) t.Sent = null;
            foreach (var b in bars)
                for (int k = 0; k < b.X0.Length; k++)
                    if (new Rectangle(b.X0[k], b.E.Y, b.X1[k] - b.X0[k], b.E.H).IntersectsWith(area)) b.Sent[k] = null;
        }

        // ---------- Formatting ----------

        /// <summary>The text a value element shows; sign = the value's sign (for colour rules).</summary>
        public static string Format(DashElement e, DashValues v, out int sign)
        {
            sign = 0;
            if (e.Format == "text") return v.Text(e.Bind) ?? e.Empty ?? "";
            var n = v.Number(e.Bind);
            if (n == null) return e.Empty ?? "";
            double x = n.Value * e.Scale;
            if (e.Format == "laptime" && x <= 0) return e.Empty ?? "";
            sign = Math.Abs(x) < 0.005 ? 0 : Math.Sign(x);
            return FormatNumber(x, e.Format);
        }

        public static string FormatNumber(double x, string format)
        {
            var ci = CultureInfo.InvariantCulture;
            switch (format)
            {
                case "int": return Math.Round(x).ToString("0", ci);
                case "gear": return x < 0 ? "R" : Math.Round(x) == 0 ? "N" : Math.Round(x).ToString("0", ci);
                case "delta": return (x > 0.004 ? "+" : x < -0.004 ? "-" : "") + Math.Abs(x).ToString("0.00", ci);
                case "laptime":
                    int m = (int)(x / 60);
                    return m + ":" + (x - m * 60).ToString("00.000", ci);
                default:
                    try { return x.ToString(string.IsNullOrEmpty(format) ? "0" : format, ci); }
                    catch { return x.ToString("0", ci); }
            }
        }

        // ---------- Checks ----------

        /// <summary>
        /// Layout problems the screen would show: text wider or taller than its box (wraps onto a clipped line), a
        /// glyph the font lacks, a value's solid background cutting into a shape, overlapping text boxes, anything off
        /// the screen once padded.
        /// </summary>
        public List<string> Check()
        {
            var problems = new List<string>();
            var texts = labels.Concat(values).ToList();
            foreach (var t in texts)
            {
                var r = t.R;
                string name = t.E.Name ?? t.E.Text ?? t.E.Bind;
                if (r.Left < 0 || r.Top < 0 || r.Right + dx > Width || r.Bottom + dy > Height) { problems.Add($"{name}: off the screen"); continue; }
                int fh = FontHeight(t.E.Font);
                if (fh == 0) problems.Add($"{name}: font {t.E.Font} doesn't exist");
                else if (fh > r.Height) problems.Add($"{name}: font {t.E.Font} is {fh} px tall, box {r.Height}");
                foreach (var sample in (t.E.Type == "value" ? t.E.Samples ?? new string[0] : new[] { t.Value }).Concat(t.E.Type == "value" && !string.IsNullOrEmpty(t.E.Empty) ? new[] { t.E.Empty } : new string[0]))
                {
                    int w = TextWidth(t.E.Font, sample);
                    if (w < 0) problems.Add($"{name}: font {t.E.Font} has no glyph for part of \"{sample}\"");
                    else if (w > r.Width) problems.Add($"{name}: \"{sample}\" is {w} px wide, box {r.Width}");
                }
            }
            foreach (var t in values)
            {
                var r = Rectangle.Intersect(t.R, new Rectangle(0, 0, Width, Height));
                int bad = 0;
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                        if (staticPx[y * Width + x] != t.Bg) bad++;
                if (bad > 0) problems.Add($"{t.E.Name ?? t.E.Bind}: its background covers {bad} px of the shapes under it");
            }
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                    if (texts[i].R.IntersectsWith(texts[j].R))
                        problems.Add($"overlap: {texts[i].E.Name ?? texts[i].E.Text} / {texts[j].E.Name ?? texts[j].E.Text}");
            int maxRight = def.Elements.Max(e => e.X + e.W), maxBottom = def.Elements.Max(e => e.Y + e.H);
            if (maxRight + dx > Width || maxBottom + dy > Height) problems.Add($"padding pushes the dash off the screen (dash ends at {maxRight},{maxBottom})");
            return problems;
        }

        /// <summary>How far the dash can move right/down.</summary>
        public static (int Right, int Down) Room(DashDefinition d) =>
            (Math.Max(0, Width - d.Elements.Max(e => e.X + e.W)), Math.Max(0, Height - d.Elements.Max(e => e.Y + e.H)));
    }

    /// <summary>
    /// Draws the commands the way the screen would, for the settings page's preview: text in each glyph's real cell
    /// width (FontMetrics), cut where the screen would wrap; glyph shapes are Segoe UI stand-ins.
    /// </summary>
    internal sealed class PreviewScreen : IScreenSink, IDisposable
    {
        public readonly Bitmap Bitmap = new Bitmap(DashRenderer.Width, DashRenderer.Height, PixelFormat.Format32bppRgb);
        private readonly Graphics g;
        private readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();

        public PreviewScreen()
        {
            g = Graphics.FromImage(Bitmap);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Black);
        }

        public void Cmd(string cmd)
        {
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

        public void Dispose()
        {
            foreach (var f in fonts.Values) f.Dispose();
            g.Dispose();
            Bitmap.Dispose();
        }
    }
}
