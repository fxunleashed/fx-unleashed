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
    internal sealed class ScreenSaver : ITiledSaver
    {
        /// <summary>With the screen's RAM drive: the logo in full colour (anti-aliased) as tiles, drawn in one go.</summary>
        public bool UseTiles { get; set; }

        private static ScreenTiles tiles;

        public ScreenTiles Tiles
        {
            get
            {
                lock (buildLock)
                {
                    if (tiles != null) return tiles;
                    using (var stream = typeof(ScreenSaver).Assembly.GetManifestResourceStream("User.FXProRpmSync.logo-nobg.png"))
                    using (var src = new Bitmap(stream))
                    using (var bmp = new Bitmap(DashRenderer.Width, DashRenderer.Height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Black);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, X, Y, Size, Size);
                        }
                        return tiles = SaverTiles.FromBitmap(bmp);
                    }
                }
            }
        }

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
            foreach (var c in UseTiles ? SaverTiles.Commands(Tiles) : Logo()) pending.Enqueue(c);
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
    /// <summary>
    /// A screensaver whose background can be drawn from the screen's RAM drive (FXProDashes docs/screen-images.md): its
    /// art as tiles (pictures), a few commands instead of thousands of rectangles (Lights out took ~10 s to draw in).
    /// Set UseTiles (the files on the screen) before Start.
    /// </summary>
    internal interface ITiledSaver : IAnimatedSaver
    {
        ScreenTiles Tiles { get; }
        bool UseTiles { get; set; }
    }

    /// <summary>Tiles of a whole-screen picture, and the commands that draw them.</summary>
    internal static class SaverTiles
    {
        public static ScreenTiles FromBitmap(Bitmap bmp)
        {
            int w = DashRenderer.Width, h = DashRenderer.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            for (int i = 0; i < px.Length; i++)
                px[i] = (((px[i] >> 16) & 255) >> 3 << 11) | (((px[i] >> 8) & 255) >> 2 << 5) | ((px[i] & 255) >> 3);
            var t = new ScreenTiles();
            for (int y = 0; y < h; y += ScreenTiles.Grid)
                for (int x = 0; x < w; x += ScreenTiles.Grid)
                    t.GridTiles.Add(ScreenTiles.Make(bmp, px, new Rectangle(x, y, Math.Min(ScreenTiles.Grid, w - x), Math.Min(ScreenTiles.Grid, h - y))));
            return t;
        }

        /// <summary>The tiles' commands (the screen already cleared to black: black tiles are left out).</summary>
        public static IEnumerable<string> Commands(ScreenTiles t)
        {
            foreach (var tile in t.GridTiles)
            {
                if (tile.Name != null) yield return ScreenTiles.Ramv(tile, 0, 0);
                else if (tile.Colour != 0)
                    yield return string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", tile.R.X, tile.R.Y, tile.R.Width, tile.R.Height, tile.Colour);
            }
        }
    }

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
}
