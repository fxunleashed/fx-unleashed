using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A screensaver with painted artwork: the background is painted once on the PC (gradients, bevels, glow, any font),
    /// reduced to a small palette so gradients become clean bands, and sent as merged `fill` rectangles (a few seconds at
    /// 25 KB/s, while the lights keep running). After that only the animation goes out: a few circles and short texts
    /// on plain panels, so a frame costs tens of bytes. The art is built once per process and shared.
    /// </summary>
    internal abstract class ArtSaver : IAnimatedSaver
    {
        protected const int W = DashRenderer.Width, H = DashRenderer.Height;

        private static readonly Dictionary<Type, List<string>> artCache = new Dictionary<Type, List<string>>();
        private readonly Queue<string> pending = new Queue<string>();
        private double lastAnim = double.NegativeInfinity;

        /// <summary>The colours the art is reduced to (the first should be black: it's the cleared screen, never sent).</summary>
        protected abstract string[] Palette { get; }

        /// <summary>Paints the background art (800 x 480, black already).</summary>
        protected abstract void Paint(Graphics g);

        /// <summary>Per-pixel painting after Paint (what GDI+ can't draw: the waving flag).</summary>
        internal virtual void PaintExtra(Bitmap bmp) { }

        /// <summary>The first state of the animated parts, drawn right after the art.</summary>
        protected abstract void Reset();

        /// <summary>The animation's changes for `now` (only what changed since the last call).</summary>
        protected abstract void Animate(IScreenSink screen, double now);

        /// <summary>
        /// Rows drawn in steps of this many pixels from `y` down (1 = every row). A curve or a slant costs a rectangle
        /// per row it changes on, so 2-3 px steps cut the first draw by 2-3x; on the wheel's 5" screen they still look smooth.
        /// </summary>
        protected virtual int RowStep(int y) => 2;

        /// <summary>Seconds between animation frames (the plugin steps savers ~50x a second).</summary>
        protected virtual double FrameSeconds => 0.05;

        public void Start()
        {
            pending.Clear();
            foreach (var c in new[] { "page 0", "vis 255,0", "cls 0" }) pending.Enqueue(c);
            foreach (var c in Art()) pending.Enqueue(c);
            lastAnim = double.NegativeInfinity;
            Reset();
        }

        public bool Drawing => pending.Count > 0;

        public void Step(IScreenSink screen, double now, int max = 60)
        {
            for (int n = 0; n < max && pending.Count > 0; n++) screen.Cmd(pending.Dequeue());
            if (pending.Count == 0 && now - lastAnim >= FrameSeconds)
            {
                lastAnim = now;
                Animate(screen, now);
            }
            screen.Flush();
        }

        /// <summary>The art as fill commands (cached per saver type).</summary>
        private List<string> Art()
        {
            lock (artCache)
            {
                if (artCache.TryGetValue(GetType(), out var hit)) return hit;
                var cmds = new List<string>();
                foreach (var r in DashRenderer.MergeRects(Render(), W, new Rectangle(0, 0, W, H)))
                    if (r[4] != 0) cmds.Add(F("fill {0},{1},{2},{3},{4}", r[0], r[1], r[2], r[3], r[4]));
                return artCache[GetType()] = cmds;
            }
        }

        /// <summary>The art in RGB565, each pixel the nearest palette colour.</summary>
        internal int[] Render()
        {
            var pal = new Color[Palette.Length];
            var pal565 = new int[Palette.Length];
            for (int i = 0; i < pal.Length; i++) { pal[i] = DashColors.Parse(Palette[i], Color.Black); pal565[i] = DashColors.To565(pal[i]); }
            var px = new int[W * H];
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Paint(g);
                }
                PaintExtra(bmp);
                var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var raw = new int[W * H];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                bmp.UnlockBits(data);
                var memo = new Dictionary<int, int>();
                for (int i = 0; i < raw.Length; i++)
                {
                    int c = raw[i] & 0xFFFFFF;
                    if (!memo.TryGetValue(c, out int q))
                    {
                        int r = c >> 16, gg = (c >> 8) & 255, b = c & 255, best = 0, bestD = int.MaxValue;
                        for (int k = 0; k < pal.Length; k++)
                        {
                            int dr = r - pal[k].R, dg = gg - pal[k].G, db = b - pal[k].B;
                            int d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
                            if (d < bestD) { bestD = d; best = k; }
                        }
                        memo[c] = q = pal565[best];
                    }
                    px[i] = q;
                }
            }
            for (int y = 1; y < H; y++)
            {
                int step = Math.Max(1, RowStep(y)), from = y - y % step;
                if (from != y) Array.Copy(px, from * W, px, y * W, W);
            }
            return px;
        }

        // ---------- helpers ----------

        protected static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
        protected static int C(string hex) => DashRenderer.Rgb565(hex);
        protected static Color Col(string hex) => DashColors.Parse(hex, Color.Black);

        /// <summary>A display font for the art (Bahnschrift, the dashes' look; Segoe UI where it's missing).</summary>
        protected static Font Display(float px, FontStyle style = FontStyle.Bold)
        {
            var f = new Font("Bahnschrift", px, style, GraphicsUnit.Pixel);
            return f.Name == "Bahnschrift" ? f : new Font("Segoe UI", px, style, GraphicsUnit.Pixel);
        }

        protected static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment align = StringAlignment.Center)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(s, f, b, r, sf);
        }

        /// <summary>A plain panel for live text: its colour must be in the palette, so text drawn on it with that
        /// background (xstr, background mode 1) matches the art.</summary>
        protected static void Panel(Graphics g, RectangleF r, string fill, string edge, float radius = 10, float edgeWidth = 2)
        {
            using (var path = Rounded(r, radius))
            using (var b = new SolidBrush(Col(fill)))
            using (var p = new Pen(Col(edge), edgeWidth))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }

        /// <summary>Text on a plain panel colour (xstr with the panel as background, so the old text is wiped).</summary>
        protected static string Xstr(int x, int y, int w, int h, int font, int colour, int back, int align, string text)
        {
            // a screen command is at most 58 characters: long texts are cut to fit
            string head = F("xstr {0},{1},{2},{3},{4},{5},{6},{7},1,1,\"", x, y, w, h, font, colour, back, align);
            int room = 58 - head.Length - 1;
            if (text.Length > room) text = text.Substring(0, Math.Max(0, room));
            return head + text + "\"";
        }

        protected static string Cirs(int x, int y, int r, int colour) => F("cirs {0},{1},{2},{3}", x, y, r, colour);
    }

    // =====================================================================================================================

    /// <summary>
    /// "Lights out": a start gantry of five pods over a starting grid. One pair of lights a second, a random hold, out;
    /// then "LIGHTS OUT" and a reaction timer on the timing panel, with the time below. A lens is 3-4 circles inside its
    /// rim, so a change costs ~80 bytes and never touches the art around it.
    /// At lights out the car launches: the track is drawn the way 80s arcade racers did it, as 4 px rows of asphalt
    /// strips, kerb blocks and a dashed centre line whose light/dark pattern depends on the distance along the track.
    /// Moving forward shifts the pattern, and only the rows whose pattern changed are redrawn (a few fills each), so the
    /// track rushes at you for ~10-15 KB/s while the car accelerates, and the start line slides away underneath.
    /// </summary>
    internal sealed class LightsOutSaver : ArtSaver
    {
        private const int Pods = 5, PodW = 96, PodH = 150, Pitch = 128, Top = 34, LensR = 27;
        private static int PodX(int k) => W / 2 - (Pods - 1) * Pitch / 2 + k * Pitch;
        private static readonly int[] LensY = { Top + 42, Top + 106 };
        private const int PanelX = 175, PanelY = 192, PanelW = 450, PanelH = 96;

        protected override int RowStep(int y) => y >= TrackTop ? 1 : 2; // the track's rows are 4 px bands already

        // ---- the track: rows below the horizon, each a slice of road at some distance ahead
        private const double Horizon = 214, Depth = 3, Stripe = 1.5, StartAt = 4;
        private const int TrackTop = 218, Band = 6, Vx = W / 2;
        private static readonly int RoadA = C("#1C2026"), RoadB = C("#14171B"), KerbRed = C("#C41212"), KerbWhite = C("#E8E8E8"),
                                    Line = C("#D9DDE2");
        private static readonly int Rows = (H - TrackTop + Band - 1) / Band;
        private readonly int[] rowState = new int[Rows];

        /// <summary>
        /// How far the car has gone (track units) `sinceOut` seconds after lights out: a 2.4 s launch, then a steady pace
        /// to the end of the drive. The top speed stays under half a stripe per frame, or the strips would seem to stand
        /// still or run backwards.
        /// </summary>
        private static double Travel(double sinceOut)
        {
            // a launch up to `top`, then easing down to a cruise that costs a third of the bytes (they follow the speed)
            const double launch = 2.4, accel = 1.5, top = 2 * accel * launch, cruise = 3.2, ease = 2.0;
            double s = Math.Min(sinceOut, IdleScreens.DriveSeconds);
            if (s <= launch) return accel * s * s;
            double d = accel * launch * launch, u = s - launch;
            if (u <= ease) return d + top * u - (top - cruise) * u * u / (2 * ease);
            return d + top * ease - (top - cruise) * ease / 2 + cruise * (u - ease);
        }

        /// <summary>A row's look for a travel `d`: bit 0 asphalt shade, bit 1 kerb colour, bit 2 centre dash, bit 3 start line,
        /// 16 far (plain).</summary>
        private static int RowState(int row, double d)
        {
            double t = (TrackTop + row * Band + Band / 2.0 - Horizon) / (H - Horizon);
            if (t < 0.15) return 16;                              // far away: plain (stripes there would only flicker)
            double w = Depth / t + d;
            int s = (int)Math.Floor(w / Stripe), a = (int)Math.Floor(w / (2 * Stripe));
            bool dash = (w / (2 * Stripe)) % 1 < 0.5;
            // the start line: a strip 0.25 long at StartAt, coming at us as the car moves (gone once it's behind)
            bool start = Math.Abs(Depth / t - (StartAt - d)) < 0.25;
            return (a & 1) | ((s & 1) << 1) | (dash ? 4 : 0) | (start ? 8 : 0);
        }

        /// <summary>
        /// The fills that draw a row in a state, cut around the timing panel (and a pixel round it). `dashOnly`: just the
        /// centre dash's spot (a line, or road where there's none), when only the dash changed.
        /// </summary>
        private static IEnumerable<(int X, int Y, int W, int H, int Colour, int Part)> RowFills(int row, int state)
        {
            int y = TrackTop + row * Band, h = Math.Min(Band, H - y);
            bool underPanel = y + h > PanelY - 1 && y < PanelY + PanelH + 1;
            foreach (var f in RowParts(row, state))
            {
                int x0 = Math.Max(0, f.X), x1 = Math.Min(W, f.X + f.W);
                if (x1 <= x0) continue;
                if (!underPanel) { yield return (x0, f.Y, x1 - x0, f.H, f.Colour, f.Part); continue; }
                int p0 = PanelX - 1, p1 = PanelX + PanelW + 1;
                if (x0 < p0) yield return (x0, f.Y, Math.Min(x1, p0) - x0, f.H, f.Colour, f.Part);
                if (x1 > p1) { int a = Math.Max(x0, p1); yield return (a, f.Y, x1 - a, f.H, f.Colour, f.Part); }
            }
        }

        /// <summary>A row's parts in a state: kerbs (part 0), road (1), centre dash (2, in the road's colour when off).</summary>
        private static IEnumerable<(int X, int Y, int W, int H, int Colour, int Part)> RowParts(int row, int state)
        {
            int y = TrackTop + row * Band, h = Math.Min(Band, H - y);
            double t = (y + Band / 2.0 - Horizon) / (H - Horizon);
            int half = (int)Math.Round(60 + 320 * t), kerb = (int)Math.Round(8 + 70 * t);
            bool far = (state & 16) != 0, start = (state & 8) != 0;
            int road = start ? Line : far ? RoadA : (state & 1) == 0 ? RoadA : RoadB;
            int kc = far ? KerbRed : (state & 2) == 0 ? KerbRed : KerbWhite;
            int l = Vx - half, r = Vx + half;
            yield return (l - kerb, y, kerb, h, kc, 0);
            yield return (r, y, kerb, h, kc, 0);
            yield return (l, y, r - l, h, road, 1);
            if (!far && !start)
            {
                int dw = Math.Max(2, (int)Math.Round(2 + 8 * t));
                yield return (Vx - dw / 2, y, dw, h, (state & 4) != 0 ? Line : road, 2);
            }
        }

        private static readonly string[] Pal =
        {
            "#000000", "#07080A", "#0D0F12", "#14171B", "#1C2026", "#262B32", "#333943", "#47505C", "#6B7481", "#9AA2AD",
            "#D9DDE2", "#FFFFFF", "#2A0505", "#4A0909", "#C41212", "#FF2020", "#FF7A7A", "#E8E8E8", "#B00F0F", "#0A0B0D",
        };
        protected override string[] Palette => Pal;

        private static readonly int LensOff = C("#2A0505"), LensOffIn = C("#1A0303"), LitRim = C("#B00F0F"), Lit = C("#FF2020"),
                                    Hot = C("#FF7A7A"), Spark = C("#FFFFFF"), PanelC = C("#0A0B0D"), White = C("#FFFFFF"),
                                    Red = C("#FF2020"), Grey = C("#9AA2AD");

        private readonly bool?[] lit = new bool?[Pods];
        private string headline, timer, clock;

        protected override void Reset()
        {
            for (int k = 0; k < Pods; k++) lit[k] = null;
            headline = null; timer = null; clock = null;
            for (int row = 0; row < Rows; row++) rowState[row] = RowState(row, 0); // as painted
        }

        protected override void Paint(Graphics g)
        {
            // the track, standing on the grid (the same rows the animation redraws)
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int row = 0; row < Rows; row++)
                foreach (var f in RowFills(row, RowState(row, 0)))
                    if (f.W > 0) using (var b = new SolidBrush(DashRenderer.ToColor(f.Colour))) g.FillRectangle(b, f.X, f.Y, f.W, f.H);
            g.SmoothingMode = mode;
            // the gantry: a beam, two struts and five pods
            using (var b = new LinearGradientBrush(new Rectangle(0, 8, W, 30), Col("#47505C"), Col("#14171B"), 90f)) g.FillRectangle(b, 30, 10, W - 60, 24);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 30, H), Col("#333943"), Col("#0D0F12"), 0f))
            {
                g.FillRectangle(b, 18, 10, 24, (int)Horizon - 8);
                g.FillRectangle(b, W - 42, 10, 24, (int)Horizon - 8);
            }
            for (int k = 0; k < Pods; k++)
            {
                var r = new RectangleF(PodX(k) - PodW / 2f, Top, PodW, PodH);
                using (var path = Rounded(r, 14))
                using (var b = new LinearGradientBrush(r, Col("#262B32"), Col("#07080A"), 0f))
                using (var edge = new Pen(Col("#47505C"), 3))
                {
                    g.FillPath(b, path);
                    g.DrawPath(edge, path);
                }
                foreach (int ly in LensY)
                    using (var rim = new SolidBrush(Col("#333943"))) g.FillEllipse(rim, PodX(k) - LensR - 5, ly - LensR - 5, (LensR + 5) * 2, (LensR + 5) * 2);
            }
            // the timing panel
            Panel(g, new RectangleF(PanelX, PanelY, PanelW, PanelH), "#0A0B0D", "#C41212", 12, 3);
            Text(g, "RACE START", Display(14, FontStyle.Bold), Col("#6B7481"), new RectangleF(PanelX, PanelY + 6, PanelW, 16));
        }

        protected override void Animate(IScreenSink screen, double now)
        {
            var on = IdleScreens.StartLights(now, out bool lightsOut, out double sinceOut);
            // the track: only rows whose pattern changed (back to the grid when the next start begins)
            double d = Travel(sinceOut);
            for (int row = 0; row < Rows; row++)
            {
                int st = RowState(row, d), changed = st ^ rowState[row];
                if (changed == 0) continue;
                rowState[row] = st;
                // only what changed: the dash spot, the kerbs, or (asphalt / start line) the whole row
                bool whole = (changed & (1 | 8 | 16)) != 0;
                foreach (var f in RowFills(row, st))
                    if (f.W > 0 && (whole || (f.Part == 0 && (changed & 2) != 0) || (f.Part == 2 && (changed & 4) != 0)))
                        screen.Cmd(F("fill {0},{1},{2},{3},{4}", f.X, f.Y, f.W, f.H, f.Colour));
            }
            for (int k = 0; k < Pods; k++)
            {
                if (lit[k] == on[k]) continue;
                lit[k] = on[k];
                foreach (int ly in LensY)
                {
                    int x = PodX(k);
                    if (on[k])
                    {
                        screen.Cmd(Cirs(x, ly, LensR, LitRim));
                        screen.Cmd(Cirs(x, ly, LensR - 4, Lit));
                        screen.Cmd(Cirs(x - 7, ly - 7, 9, Hot));
                        screen.Cmd(Cirs(x - 9, ly - 10, 3, Spark));
                    }
                    else
                    {
                        screen.Cmd(Cirs(x, ly, LensR, LensOff));
                        screen.Cmd(Cirs(x + 3, ly + 4, LensR - 8, LensOffIn));
                    }
                }
            }
            // the panel: LIGHTS OUT and the reaction time for 3 s, then the race clock; GET READY and the time on the grid
            bool racing = lightsOut && sinceOut >= 3;
            string head = racing ? RaceClock(sinceOut) : lightsOut ? "LIGHTS OUT" : "GET READY";
            if (head != headline)
            {
                headline = head;
                screen.Cmd(Xstr(PanelX + 10, PanelY + 24, PanelW - 20, 40, 8, lightsOut ? White : Grey, PanelC, 1, head));
            }
            string t = racing ? "RACE TIME" : lightsOut ? sinceOut.ToString("0.000", CultureInfo.InvariantCulture) + " s"
                     : DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (t != timer)
            {
                timer = t;
                screen.Cmd(Xstr(PanelX + 10, PanelY + 66, PanelW - 20, 26, 5, lightsOut ? Red : Grey, PanelC, 1, t));
            }
        }

        /// <summary>Time since lights out as a race clock, to the tenth (0:12.3): a few bytes ten times a second.</summary>
        private static string RaceClock(double s) =>
            ((int)(s / 60)).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00.0", CultureInfo.InvariantCulture);

        protected override double FrameSeconds => 0.08;
    }

    // =====================================================================================================================

    /// <summary>
    /// "Rev sweep": a tachometer. The dial, bezel, ticks, numerals and red zone are art; a ring of 41 LEDs inside the
    /// ticks sweeps green, amber, red up through the gears and flashes blue at the shift point; the gear and the revs
    /// sit in the middle. Only LEDs that change are redrawn (one circle each).
    /// </summary>
    internal sealed class TachoSaver : ArtSaver
    {
        private const int Cx = 400, Cy = 248, R = 216, LedR = 172, Leds = 33;
        private const int PillW = 176, PillH = 46, PillY = Cy + 112;

        protected override int RowStep(int y) => 3;
        private const double A0 = 140, Sweep = 260; // degrees, clockwise from the right
        private static readonly string[] Pal =
        {
            "#000000", "#07080A", "#0D0F12", "#14171B", "#1C2026", "#262B32", "#333943", "#47505C", "#6B7481", "#9AA2AD",
            "#D9DDE2", "#FFFFFF", "#8C0A12", "#FF1F2D", "#16181C", "#0B0C0F",
        };
        protected override string[] Palette => Pal;

        private static readonly int Off = C("#1C2026"), Face = C("#0B0C0F"), White = C("#FFFFFF"), Grey = C("#6B7481"),
                                    Green = C("#16D65A"), Amber = C("#FFB000"), RedC = C("#FF1F2D"), Blue = C("#2F6BFF");

        private readonly int[] shown = new int[Leds];
        private string gear, rpm;
        private double lastRpm;

        protected override void Reset()
        {
            for (int i = 0; i < Leds; i++) shown[i] = -1;
            gear = rpm = null;
            lastRpm = double.NegativeInfinity;
        }

        private static PointF At(double deg, double r) => new PointF((float)(Cx + r * Math.Cos(deg * Math.PI / 180)), (float)(Cy + r * Math.Sin(deg * Math.PI / 180)));
        private static double Angle(double frac) => A0 + Sweep * frac;

        protected override void Paint(Graphics g)
        {
            // bezel: a metal ring lit from the top
            using (var b = new LinearGradientBrush(new Rectangle(Cx - R - 18, Cy - R - 18, (R + 18) * 2, (R + 18) * 2), Col("#9AA2AD"), Col("#14171B"), 90f))
                g.FillEllipse(b, Cx - R - 18, Cy - R - 18, (R + 18) * 2, (R + 18) * 2);
            using (var b = new SolidBrush(Col("#07080A"))) g.FillEllipse(b, Cx - R - 4, Cy - R - 4, (R + 4) * 2, (R + 4) * 2);
            // the face
            using (var b = new SolidBrush(Col("#0D0F12"))) g.FillEllipse(b, Cx - R, Cy - R, R * 2, R * 2);
            // the red zone: 7.5-9 on the outer track
            using (var pen = new Pen(Col("#8C0A12"), 16)) g.DrawArc(pen, Cx - R + 14, Cy - R + 14, (R - 14) * 2, (R - 14) * 2, (float)Angle(7.5 / 9), (float)(Sweep * 1.5 / 9));
            // ticks: majors at each 1000, minors every 500
            for (int i = 0; i <= 18; i++)
            {
                double a = Angle(i / 18.0);
                bool major = i % 2 == 0;
                using (var pen = new Pen(Col(i >= 15 ? "#FF1F2D" : major ? "#D9DDE2" : "#6B7481"), major ? 5 : 3))
                    g.DrawLine(pen, At(a, R - 6), At(a, R - (major ? 30 : 20)));
                if (major)
                {
                    var p = At(a, R - 82);
                    Text(g, (i / 2).ToString(CultureInfo.InvariantCulture), Display(26), Col(i >= 15 ? "#FF1F2D" : "#D9DDE2"), new RectangleF(p.X - 26, p.Y - 18, 52, 36));
                }
            }
            // sockets for the LED ring
            for (int i = 0; i < Leds; i++)
            {
                var p = At(Angle(i / (Leds - 1.0)), LedR);
                using (var b = new SolidBrush(Col("#07080A"))) g.FillEllipse(b, p.X - 11, p.Y - 11, 22, 22);
            }
            // the centre: a plain disc for the gear and the revs
            using (var b = new SolidBrush(Col("#0B0C0F"))) g.FillEllipse(b, Cx - 108, Cy - 108, 216, 216);
            using (var pen = new Pen(Col("#262B32"), 3)) g.DrawEllipse(pen, Cx - 108, Cy - 108, 216, 216);
            Text(g, "RPM", Display(14, FontStyle.Regular), Col("#6B7481"), new RectangleF(Cx - 60, Cy + 74, 120, 20));
            Panel(g, new RectangleF(Cx - PillW / 2f, PillY, PillW, PillH), "#0B0C0F", "#262B32", 10, 2);
            Text(g, "FX UNLEASHED", Display(16), Col("#FF1F2D"), new RectangleF(Cx - 100, Cy + 172, 200, 22));
        }

        protected override void Animate(IScreenSink screen, double now)
        {
            var (g, f, flash) = IdleScreens.RevSweep(now);
            for (int i = 0; i < Leds; i++)
            {
                double at = i / (Leds - 1.0);
                // the shift flash blinks the red end only (fewer circles than the whole ring)
                int c = flash != null ? (at >= 0.8 ? (flash.Value ? Blue : Off) : at < 0.55 ? Green : Amber)
                      : f >= at ? (at < 0.55 ? Green : at < 0.8 ? Amber : RedC) : Off;
                if (shown[i] == c) continue;
                shown[i] = c;
                var p = At(Angle(at), LedR);
                screen.Cmd(Cirs((int)Math.Round(p.X), (int)Math.Round(p.Y), 7, c));
            }
            string gs = g.ToString(CultureInfo.InvariantCulture);
            if (gs != gear) { gear = gs; screen.Cmd(Xstr(Cx - 70, Cy - 80, 140, 128, 11, White, Face, 1, gs)); }
            // the revs, 5 times a second
            if (now - lastRpm >= 0.2 || flash != null)
            {
                lastRpm = now;
                string rs = ((int)(1000 + 7500 * f) / 100 * 100).ToString(CultureInfo.InvariantCulture);
                if (rs != rpm) { rpm = rs; screen.Cmd(Xstr(Cx - PillW / 2 + 6, PillY + 4, PillW - 12, PillH - 8, 6, Grey, Face, 1, rs)); }
            }
        }
    }

    // =====================================================================================================================

    /// <summary>
    /// "Pit board": the board on its pole over the pit wall: a riveted frame, the red header, and your last session in
    /// pit board letters (position, lap, best lap), with a footer that alternates the car and the time. Only text changes.
    /// </summary>
    internal sealed class PitBoardSaver : ArtSaver
    {
        private const int Bx = 236, By = 30, Bw = 470, Bh = 420;
        // rows on the board (from By): POS and LAP in 112 px pit board letters, the best lap, the footer
        private const int RowPos = 70, RowLap = 190, RowBest = 310, RowFoot = 362, TextX = Bx + 96, TextW = Bw - 126;
        private static readonly string[] Pal =
        {
            "#000000", "#07080A", "#0D0F12", "#14171B", "#1C2026", "#262B32", "#333943", "#47505C", "#6B7481", "#9AA2AD",
            "#D9DDE2", "#FFFFFF", "#8C0A12", "#C41212", "#FF1F2D", "#050505",
        };
        protected override string[] Palette => Pal;

        private static readonly int Board = C("#050505"), Yellow = C("#FFD000"), White = C("#FFFFFF"), Grey = C("#9AA2AD");
        private readonly LastSession last;
        private string footer;
        private bool drawn;

        public PitBoardSaver(LastSession last) { this.last = last; }

        protected override void Reset() { footer = null; drawn = false; }

        protected override void Paint(Graphics g)
        {
            // the pit wall and the garage behind
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Col("#0D0F12"), Col("#000000"), 90f)) g.FillRectangle(b, 0, 0, W, H);
            using (var b = new SolidBrush(Col("#14171B"))) g.FillRectangle(b, 0, 380, W, 100);
            using (var b = new SolidBrush(Col("#262B32"))) g.FillRectangle(b, 0, 380, W, 6);
            for (int x = 0; x < W; x += 80) using (var b = new SolidBrush(Col("#1C2026"))) g.FillRectangle(b, x, 392, 40, 88);
            // the pole, held from the left
            using (var b = new LinearGradientBrush(new Rectangle(0, 200, W, 24), Col("#9AA2AD"), Col("#262B32"), 90f)) g.FillRectangle(b, 0, 206, Bx + 20, 18);
            using (var b = new SolidBrush(Col("#6B7481"))) g.FillRectangle(b, Bx - 6, 196, 14, 38);
            // the board: frame, rivets, face, header
            var frame = new RectangleF(Bx, By, Bw, Bh);
            using (var path = Rounded(frame, 14))
            using (var b = new LinearGradientBrush(frame, Col("#9AA2AD"), Col("#262B32"), 60f))
                g.FillPath(b, path);
            var face = new RectangleF(Bx + 16, By + 16, Bw - 32, Bh - 32);
            using (var path = Rounded(face, 6)) using (var b = new SolidBrush(Col("#050505"))) g.FillPath(b, path);
            foreach (var (x, y) in new[] { (Bx + 8, By + 8), (Bx + Bw - 8, By + 8), (Bx + 8, By + Bh - 8), (Bx + Bw - 8, By + Bh - 8) })
            {
                using (var b = new SolidBrush(Col("#D9DDE2"))) g.FillEllipse(b, x - 5, y - 5, 10, 10);
                using (var b = new SolidBrush(Col("#333943"))) g.FillEllipse(b, x - 2, y - 1, 5, 5);
            }
            var head = new RectangleF(face.X, face.Y, face.Width, 54);
            using (var b = new LinearGradientBrush(head, Col("#FF1F2D"), Col("#8C0A12"), 90f)) g.FillRectangle(b, head);
            Text(g, "FX UNLEASHED", Display(30), Color.White, head);
            // row labels
            var lab = Display(16, FontStyle.Bold);
            foreach (var (t, y) in new[] { ("POS", RowPos), ("LAP", RowLap), ("BEST", RowBest) })
                Text(g, t, lab, Col("#6B7481"), new RectangleF(face.X + 14, By + y + 6, 70, 24), StringAlignment.Near);
            using (var pen = new Pen(Col("#1C2026"), 2))
                foreach (int y in new[] { RowLap - 4, RowBest - 4, RowFoot - 6 }) g.DrawLine(pen, face.X + 12, By + y, face.Right - 12, By + y);
        }

        protected override void Animate(IScreenSink screen, double now)
        {
            bool have = last != null && last.BestLap > 0;
            if (!drawn)
            {
                drawn = true;
                string pos = have ? (last.Position > 0 ? "P" + last.Position : "P-") : "BOX";
                string lap = have ? "L" + last.Laps : "BOX";
                string best = have ? LapTime(last.BestLap) : "--:--.---";
                // pit board letters (screen font 47: 112 px, narrow digits)
                screen.Cmd(Xstr(TextX, By + RowPos, TextW, 112, 47, Yellow, Board, 1, pos));
                screen.Cmd(Xstr(TextX, By + RowLap, TextW, 112, 47, White, Board, 1, lap));
                screen.Cmd(Xstr(TextX, By + RowBest, TextW, 44, 8, White, Board, 1, best));
            }
            // the footer: the car for 4 s, the time for 4 s
            string car = have ? (last.Car ?? "").ToUpperInvariant() : "NO SESSION YET";
            if (car.Length > 28) car = car.Substring(0, 28);
            string f = (int)(now / 4) % 2 == 0 ? car : DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (f != footer) { footer = f; screen.Cmd(Xstr(Bx + 24, By + RowFoot, Bw - 48, 24, 5, Grey, Board, 1, f)); }
        }

        private static string LapTime(double s) => TimeSpan.FromSeconds(s).ToString(s >= 60 ? @"m\:ss\.fff" : @"ss\.fff", CultureInfo.InvariantCulture);

        protected override double FrameSeconds => 0.25;
    }

    // =====================================================================================================================

    /// <summary>
    /// "Chequered": a chequered flag waving on its pole, shaded along its folds, with the time and date on a glass panel.
    /// The flag is painted per pixel (a travelling wave, light from the upper left); only the clock changes.
    /// </summary>
    internal sealed class ChequeredSaver : ArtSaver
    {
        private const int PanelX = 548, PanelY = 324, PanelW = 232, PanelH = 132;
        private static readonly string[] Pal =
        {
            "#000000", "#08090B", "#121418", "#1D2026", "#2B2F36", "#3E434C", "#5A606A", "#7E858F", "#A6ACB4", "#CCD1D6",
            "#EEF0F2", "#FFFFFF", "#C9A227", "#7A6118", "#FF1F2D", "#0A0B0D",
        };
        protected override string[] Palette => Pal;

        private static readonly int PanelC = C("#0A0B0D"), White = C("#FFFFFF"), Grey = C("#7E858F");
        private string clock, date;

        protected override void Reset() { clock = date = null; }

        protected override void Paint(Graphics g)
        {
            // night sky with a glow low down
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Col("#000000"), Col("#121418"), 90f)) g.FillRectangle(b, 0, 0, W, H);
            // the pole
            using (var b = new LinearGradientBrush(new Rectangle(54, 0, 16, H), Col("#A6ACB4"), Col("#2B2F36"), 0f)) g.FillRectangle(b, 56, 40, 14, H - 40);
            using (var b = new SolidBrush(Col("#C9A227"))) g.FillEllipse(b, 51, 24, 24, 24);
            using (var b = new SolidBrush(Col("#7A6118"))) g.FillEllipse(b, 58, 34, 10, 10);
            // the glass panel
            Panel(g, new RectangleF(PanelX, PanelY, PanelW, PanelH), "#0A0B0D", "#3E434C", 14, 2);
            using (var b = new SolidBrush(Col("#FF1F2D"))) g.FillRectangle(b, PanelX + 24, PanelY + 90, PanelW - 48, 3);
        }

        /// <summary>The flag, painted per pixel over the art after Paint (Render() calls Paint, so do it there).</summary>
        private static void Flag(Bitmap bmp)
        {
            const int fx = 70, fy = 48, fw = 500, fh = 290, cols = 8, rows = 5;
            for (int y = 0; y < H; y++)
                for (int x = fx; x < fx + fw + 10; x++)
                {
                    // invert the wave: a point (u, v) of the flag lands at x = fx + u*fw, y = fy + v*fh + wave(u)
                    double u = (x - fx) / (double)fw;
                    if (u < 0 || u > 1) continue;
                    double amp = 34 * u, phase = u * 2 * Math.PI * 1.4;
                    double wave = amp * Math.Sin(phase), slope = amp * Math.Cos(phase) * 2 * Math.PI * 1.4 / fw + 34.0 / fw * Math.Sin(phase);
                    double v = (y - fy - wave) / fh;
                    if (v < 0 || v > 1) continue;
                    bool white = ((int)(u * cols) + (int)(v * rows)) % 2 == 0;
                    // light: folds facing up-left are bright, the others darker
                    double light = 0.72 - 6.5 * slope + 0.12 * (1 - v);
                    light = Math.Max(0.18, Math.Min(1.08, light));
                    int lv = white ? (int)(240 * light) : (int)(46 * light);
                    lv = Math.Max(0, Math.Min(255, lv));
                    bmp.SetPixel(x, y, Color.FromArgb(lv, lv, lv));
                }
        }

        internal override void PaintExtra(Bitmap bmp) => Flag(bmp);

        protected override void Animate(IScreenSink screen, double now)
        {
            string c = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), d = DateTime.Now.ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            if (c != clock) { clock = c; screen.Cmd(Xstr(PanelX + 10, PanelY + 12, PanelW - 20, 74, 21, White, PanelC, 1, c)); }
            if (d != date) { date = d; screen.Cmd(Xstr(PanelX + 10, PanelY + 98, PanelW - 20, 26, 5, Grey, PanelC, 1, d)); }
        }

        protected override double FrameSeconds => 0.5;
    }
}
