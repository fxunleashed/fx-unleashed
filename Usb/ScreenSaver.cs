using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The wheel's screen between sessions in USB mode: the plugin's logo (assets/logo-nobg.png) on black, with its 13
    /// rev-light dots running a slow rev sweep. The logo goes out as `fill` rectangles; its anti-aliased edges are
    /// sorted into crisp black / red / white first (~3,300 rectangles at 420 px instead of ~10,000+), and the commands
    /// are sent in slices between LED frames, so it draws in over ~3 s while the lights keep animating.
    /// </summary>
    internal sealed class ScreenSaver : IAnimatedSaver
    {
        private const int Size = 420, X = (DashRenderer.Width - Size) / 2, Y = (DashRenderer.Height - Size) / 2;
        private const double Scale = Size / 500.0;
        // Dot row in the 500 px logo: 13 dots, 13.33 px apart, centred on y 103 (measured from the image).
        private const double DotX0 = 169.5, DotPitch = 13.33, DotY = 103;
        private const int DotCount = 13;

        private static List<string> logoCmds;           // built once, shared
        private static readonly object buildLock = new object();

        private readonly Queue<string> pending = new Queue<string>();
        private readonly string[] dotSent = new string[DotCount];

        private static readonly int Dim = DashRenderer.Rgb565("#5A0A0A"), Lit = DashRenderer.Rgb565("#FFFFFF"),
                                    Flash = DashRenderer.Rgb565("#FF1414");

        /// <summary>Queues the whole screen: page 0, cleared, the logo.</summary>
        public void Start()
        {
            pending.Clear();
            pending.Enqueue("page 0");
            pending.Enqueue("vis 255,0");
            pending.Enqueue("cls 0");
            foreach (var c in Logo()) pending.Enqueue(c);
            for (int i = 0; i < DotCount; i++) dotSent[i] = null;
        }

        /// <summary>True while the logo is still going out.</summary>
        public bool Drawing => pending.Count > 0;

        /// <summary>Sends up to `max` queued commands, then (once drawn) the dots' animation.</summary>
        public void Step(IScreenSink screen, double now, int max = 60)
        {
            for (int n = 0; n < max && pending.Count > 0; n++) screen.Cmd(pending.Dequeue());
            if (pending.Count == 0) Dots(screen, now);
            screen.Flush();
        }

        /// <summary>
        /// A 4 s rev sweep: the dots fill white left to right (2 s), flash red (0.8 s), then rest dim.
        /// Only dots whose colour changed are redrawn (2 small fills each).
        /// </summary>
        private void Dots(IScreenSink screen, double now)
        {
            double t = now % 4;
            for (int i = 0; i < DotCount; i++)
            {
                int colour;
                if (t < 2) colour = t >= i * 2.0 / DotCount ? Lit : Dim;
                else if (t < 2.8) colour = (int)((t - 2) * 5) % 2 == 0 ? Flash : Dim;
                else colour = Dim;
                string key = colour.ToString(CultureInfo.InvariantCulture);
                if (dotSent[i] == key) continue;
                dotSent[i] = key;
                int cx = X + (int)Math.Round((DotX0 + i * DotPitch) * Scale), cy = Y + (int)Math.Round(DotY * Scale) - 1;
                // a round dot 7 px across (a 7x5 bar and a 5x7 bar), covering the logo's own dot
                screen.Cmd(string.Format(CultureInfo.InvariantCulture, "fill {0},{1},7,5,{2}", cx - 3, cy - 2, colour));
                screen.Cmd(string.Format(CultureInfo.InvariantCulture, "fill {0},{1},5,7,{2}", cx - 2, cy - 3, colour));
            }
        }

        private static List<string> Logo()
        {
            lock (buildLock)
            {
                if (logoCmds != null) return logoCmds;
                var px = new int[Size * Size];
                using (var stream = typeof(ScreenSaver).Assembly.GetManifestResourceStream("User.FXProRpmSync.logo-nobg.png"))
                using (var src = new Bitmap(stream))
                using (var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, Size, Size);
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                    bmp.UnlockBits(data);
                }
                int red = DashRenderer.Rgb565("#FF1414"), white = DashRenderer.Rgb565("#FFFFFF");
                for (int i = 0; i < px.Length; i++)
                {
                    int r = (px[i] >> 16) & 255, g = (px[i] >> 8) & 255, b = px[i] & 255;
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    px[i] = mx < 70 ? 0 : (mx - mn) / (double)mx < 0.35 ? white : red;
                }
                var cmds = new List<string>();
                foreach (var rc in DashRenderer.MergeRects(px, Size, new Rectangle(0, 0, Size, Size)))
                    if (rc[4] != 0)
                        cmds.Add(string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", rc[0] + X, rc[1] + Y, rc[2], rc[3], rc[4]));
                logoCmds = cmds;
                return cmds;
            }
        }
    }
}

namespace User.FXProRpmSync
{
    /// <summary>A screensaver that draws itself with screen commands (not a dash): the logo, the start lights.</summary>
    internal interface IAnimatedSaver
    {
        /// <summary>Queues the first full draw.</summary>
        void Start();
        /// <summary>True while the first draw is still going out.</summary>
        bool Drawing { get; }
        /// <summary>Sends up to `max` queued commands, then the animation's changes for `now`.</summary>
        void Step(IScreenSink screen, double now, int max = 60);
    }

    /// <summary>
    /// "Lights out": a start gantry of five red lights that come on one a second, hold for a random moment and go out,
    /// then "LIGHTS OUT" and the time. Each light is one native filled circle (`cirs`), so a change costs ~20 bytes.
    /// </summary>
    internal sealed class LightsOutSaver : IAnimatedSaver
    {
        private const int Cx0 = 140, Pitch = 130, Cy = 200, R = 50;
        private readonly Queue<string> pending = new Queue<string>();
        private readonly bool?[] lit = new bool?[5];
        private bool? go;
        private string clock;

        private static readonly int On = DashRenderer.Rgb565("#FF1A1A"), Off = DashRenderer.Rgb565("#2A0606"), Rim = DashRenderer.Rgb565("#3A3D44"),
                                    Frame = DashRenderer.Rgb565("#2A2D33"), Panel = DashRenderer.Rgb565("#0C0D10"), White = DashRenderer.Rgb565("#FFFFFF"),
                                    Grey = DashRenderer.Rgb565("#6A717C");

        public bool Drawing => pending.Count > 0;

        public void Start()
        {
            pending.Clear();
            foreach (var c in new[] { "page 0", "vis 255,0", "cls 0" }) pending.Enqueue(c);
            pending.Enqueue(F("fill {0},{1},{2},{3},{4}", 65, 120, 670, 160, Frame));
            pending.Enqueue(F("fill {0},{1},{2},{3},{4}", 69, 124, 662, 152, Panel));
            for (int k = 0; k < 5; k++) pending.Enqueue(F("cirs {0},{1},{2},{3}", Cx0 + k * Pitch, Cy, R + 4, Rim));
            for (int k = 0; k < 5; k++) lit[k] = null;
            go = null; clock = null;
        }

        public void Step(IScreenSink screen, double now, int max = 60)
        {
            for (int n = 0; n < max && pending.Count > 0; n++) screen.Cmd(pending.Dequeue());
            if (pending.Count == 0)
            {
                var on = IdleScreens.StartLights(now, out bool lightsOut);
                for (int k = 0; k < 5; k++)
                {
                    if (lit[k] == on[k]) continue;
                    lit[k] = on[k];
                    screen.Cmd(F("cirs {0},{1},{2},{3}", Cx0 + k * Pitch, Cy, R, on[k] ? On : Off));
                }
                if (go != lightsOut)
                {
                    go = lightsOut;
                    screen.Cmd(lightsOut ? F("xstr {0},{1},{2},{3},0,{4},0,1,1,1,\"LIGHTS OUT\"", 100, 305, 600, 56, White)
                                         : F("fill {0},{1},{2},{3},0", 100, 305, 600, 56));
                }
                var time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (clock != time)
                {
                    clock = time;
                    screen.Cmd(F("xstr {0},{1},{2},{3},4,{4},0,1,1,1,\"", 300, 405, 200, 32, Grey) + time + "\"");
                }
            }
            screen.Flush();
        }

        private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
