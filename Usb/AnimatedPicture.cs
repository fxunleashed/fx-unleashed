using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>One picture of an animation: a rectangle (relative to the animation's own corner) as a JPEG, or one colour.</summary>
    public class AnimTile
    {
        public int X, Y, W, H;
        /// <summary>Base64 JPEG; null = the whole rectangle is one colour (`Colour`, RGB565), drawn with a `fill`.</summary>
        public string Jpeg;
        public int Colour;
    }

    /// <summary>One step of an animation: the pictures drawn over the one before to get to it, and how long it stays (ms).</summary>
    public class AnimFrame
    {
        public int Ms;
        public List<AnimTile> Tiles = new List<AnimTile>();
    }

    /// <summary>What the person importing a GIF chose (the import window).</summary>
    public sealed class GifOptions
    {
        /// <summary>1 = fitted into the screen's usable area (at most 3x the GIF's own size); less is smaller (0.05 - 1).</summary>
        public double Size = 1;
        /// <summary>Most steps a second to keep; 0 = as many as fit in the screen's memory.</summary>
        public double MaxStepsPerSecond;
        /// <summary>1 (lowest) - 5 (high), see <see cref="AnimatedPicture.QualityName"/>: lower makes smaller files and has the
        /// screen draw less each step, at the cost of a softer picture.</summary>
        public int Quality = AnimatedPicture.DefaultQuality;
    }

    /// <summary>What an animated GIF is, for the import window.</summary>
    public sealed class GifInfo
    {
        public string Name;
        public int Width, Height, Frames;
        public double Seconds;
        /// <summary>The scale that fits it into the usable area (at most 3x), before the person's size choice.</summary>
        public double FitScale;
        /// <summary>Its first frame, as a PNG.</summary>
        public byte[] FirstFrame;
        public double StepsPerSecond => Seconds <= 0 ? 0 : Frames / Seconds;
        public int ShownWidth(double size) => Math.Max(8, (int)Math.Round(Width * FitScale * size));
        public int ShownHeight(double size) => Math.Max(8, (int)Math.Round(Height * FitScale * size));
    }

    /// <summary>
    /// An animated GIF made ready for the screen's RAM drive (FXProDashes docs/screen-images.md): the screen can't play a
    /// GIF, but it draws a JPEG from its RAM in a few ms, so the animation is the first frame as a picture and then, for
    /// each next frame, only the rectangles that changed (a GIF mostly changes a small part), drawn over what's there.
    /// Everything has to fit in <see cref="Budget"/> of the drive (the rest stays for the dashes): when the GIF is more than
    /// that, evenly spaced frames are kept (the loop keeps its whole length, just fewer steps) and the saver says so
    /// (<see cref="FramesTaken"/> of <see cref="FramesTotal"/>). Played by <see cref="GifSaver"/>.
    /// </summary>
    public class AnimatedPicture
    {
        /// <summary>What one animation may take of the screen's RAM drive (accounted bytes, files and their overhead): about
        /// 60% of it, so a rotation of dashes still fits beside it.</summary>
        public const int Budget = 200 * 1024;
        /// <summary>Most files one animation puts on the drive: each takes ~0.3 s to upload (the loading screen, once after a
        /// power-on), so 80 files is ~25 s.</summary>
        public const int MaxFiles = 80;

        public int Version = 1;
        public string Name;
        /// <summary>The animation's size on the screen.</summary>
        public int Width, Height;
        /// <summary>The GIF's frames, and how many of them are kept.</summary>
        public int FramesTotal, FramesTaken;
        /// <summary>What it takes of the RAM drive (accounted bytes).</summary>
        public int RamBytes;
        /// <summary>[0] is the whole picture, each next one what changes from the one before.</summary>
        public List<AnimFrame> Frames = new List<AnimFrame>();
        /// <summary>The changes from the last frame back to the first (null: none).</summary>
        public AnimFrame Wrap;
        /// <summary>The first frame as a picture dash: for a screen without the RAM drive, which can't play it.</summary>
        public DashDefinition Still;

        /// <summary>Fewer frames were kept because they didn't fit in the screen's memory (as opposed to the person's smoothness limit).</summary>
        public bool MemoryLimited;

        /// <summary>The quality level it was made at (<see cref="QualityName"/>; 0 in older files).</summary>
        public int Quality;

        [JsonIgnore] public bool Oversized => FramesTaken < FramesTotal;

        /// <summary>How long one trip through the animation takes, in seconds.</summary>
        [JsonIgnore] public double LoopSeconds => Frames.Sum(f => Math.Max(0.03, f.Ms / 1000.0));

        /// <summary>Steps the screen has to show a second (frames that change nothing are not steps).</summary>
        [JsonIgnore] public double StepsPerSecond => Frames.Count / Math.Max(0.03, LoopSeconds);

        /// <summary>
        /// The share of the time the animation allows that the screen needs to draw its steps, by the screen model
        /// (GifSaver.MicrosecondsPerPixel): over 1 and it can't keep up, so steps get skipped.
        /// </summary>
        [JsonIgnore]
        public double ScreenLoad
        {
            get
            {
                if (Frames.Count < 2) return 0;
                double ms = Frames.Skip(1).Sum(f => GifSaver.CostMs(f.Tiles)) + (Wrap == null ? 0 : GifSaver.CostMs(Wrap.Tiles));
                return ms / (Math.Max(0.03, LoopSeconds) * 1000.0);
            }
        }

        /// <summary>The steps a second the screen can draw of this animation (the model).</summary>
        [JsonIgnore] public double DrawableStepsPerSecond => StepsPerSecond / Math.Max(1.0, ScreenLoad);

        // ---------------------------------------------------------------- importing

        /// <summary>The quality choices: JPEG quality of each picture, and how far a colour must move (0-255) before the pixel is
        /// drawn again (the screen has 5/6/5 bits; GIFs are often dithered, so the smallest changes are mostly noise). Lower
        /// levels give smaller files (more frames fit the memory) and smaller patches (less for the screen to draw a step).</summary>
        private static readonly int[] JpegQualities = { 30, 45, 60, 72, 86 };
        private static readonly int[] Tolerances = { 26, 18, 11, 6, 4 };
        private static readonly string[] QualityNames = { "Lowest", "Low", "Medium", "Good", "High" };
        /// <summary>The level an import uses unless asked otherwise (what the first version always used).</summary>
        public const int DefaultQuality = 4;
        public const int QualityLevels = 5;

        private static int Level(int quality) => Math.Max(1, Math.Min(QualityLevels, quality));
        public static string QualityName(int quality) => QualityNames[Level(quality) - 1];
        /// <summary>The JPEG quality (1-100) a level encodes at.</summary>
        public static int JpegQuality(int quality) => JpegQualities[Level(quality) - 1];

        /// <summary>A pixel counts as changed when a colour differs by more than this (0-255; the screen has 5/6/5 bits).</summary>
        private const int Threshold = 6;
        private const int MaxCandidates = 120;
        private const int MaxPatchesPerFrame = 6;
        /// <summary>Changes covering this share (%) of the picture are drawn as the whole picture.</summary>
        private const int WholePercent = 80;
        /// <summary>A GIF made bigger than this many times its own size isn't sharper, only dearer.</summary>
        private const double MaxUpscale = 3;

        private sealed class Candidate { public int Id; public byte[] Bgr; public int Ms; }
        private struct Encoded { public byte[] Jpeg; public int Colour; }

        /// <summary>
        /// The GIF at `path` as an animation, or null when it isn't an animated GIF (one frame, another format). Slow (decodes
        /// and encodes every frame several times): not on the UI thread.
        /// </summary>
        public static AnimatedPicture FromGif(string path, string name, int budget = Budget, int maxFiles = MaxFiles, GifOptions options = null, bool needStill = true)
        {
            var o = options ?? new GifOptions();
            byte[] data = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(data))
            using (var gif = Image.FromStream(ms))
            {
                if (!gif.RawFormat.Equals(ImageFormat.Gif) || gif.FrameDimensionsList.Length == 0) return null;
                var dim = new FrameDimension(gif.FrameDimensionsList[0]);
                int total = gif.GetFrameCount(dim);
                if (total < 2) return null;
                var delays = Delays(gif, total);
                const double areaW = 790, areaH = 460;
                double fit = Math.Min(Math.Min(areaW / gif.Width, areaH / gif.Height), MaxUpscale) * Math.Max(0.05, Math.Min(1, o.Size));

                AnimatedPicture best = null;
                int level = Level(o.Quality), tolerance = Tolerances[level - 1], jpeg = JpegQualities[level - 1];
                // the picture's own size first; if even its first frame is too much for the budget, smaller and smaller
                foreach (double shrink in new[] { 1, 0.85, 0.7, 0.55, 0.4, 0.3 })
                {
                    int w = Math.Max(8, (int)Math.Round(gif.Width * fit * shrink)), h = Math.Max(8, (int)Math.Round(gif.Height * fit * shrink));
                    var cands = Decode(gif, dim, total, delays, w, h, o.MaxStepsPerSecond);
                    // the chosen quality, then (if the first picture alone is too much) coarser ones before a smaller size
                    foreach (int q in new[] { jpeg, Math.Max(20, jpeg - 12), Math.Max(15, jpeg - 24) }.Distinct())
                    {
                        best = Fit(cands, total, w, h, q, tolerance, budget, maxFiles, name);
                        if (best != null) { best.Quality = level; goto found; }
                    }
                }
                return null;
                found:
                if (needStill) using (var first = new Bitmap(new MemoryStream(data))) best.Still = IdleScreens.StillDash(first, name);
                return best;
            }
        }

        /// <summary>What the GIF at `path` is (size, frames, length, first frame), or null when it isn't an animated GIF.</summary>
        public static GifInfo Inspect(string path)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(data))
                using (var gif = Image.FromStream(ms))
                {
                    if (!gif.RawFormat.Equals(ImageFormat.Gif) || gif.FrameDimensionsList.Length == 0) return null;
                    var dim = new FrameDimension(gif.FrameDimensionsList[0]);
                    int total = gif.GetFrameCount(dim);
                    if (total < 2) return null;
                    const double areaW = 790, areaH = 460;
                    var info = new GifInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(path), Width = gif.Width, Height = gif.Height, Frames = total,
                        Seconds = Delays(gif, total).Sum() / 1000.0,
                        FitScale = Math.Min(Math.Min(areaW / gif.Width, areaH / gif.Height), MaxUpscale),
                    };
                    gif.SelectActiveFrame(dim, 0);
                    using (var bmp = new Bitmap(gif.Width, gif.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.DrawImage(gif, 0, 0, gif.Width, gif.Height); }
                        using (var png = new MemoryStream()) { bmp.Save(png, ImageFormat.Png); info.FirstFrame = png.ToArray(); }
                    }
                    return info;
                }
            }
            catch { return null; }
        }

        /// <summary>The steps a second a good animation has, however many frames the GIF has: smoother is dearer for the screen.</summary>
        public const double SmoothSteps = 10;

        /// <summary>
        /// The biggest picture that plays at the GIF's speed: at most SmoothSteps steps a second (or the GIF's own, if fewer),
        /// the screen able to draw them by its model, and not so many frames that the memory cuts them. Slow (a few trial
        /// imports): not on the UI thread. `picture` is the one made at that size (no still picture in it yet). `quality` is the
        /// level the person chose; the size is what is worked out for it.
        /// </summary>
        public static GifOptions BestFit(string path, GifInfo info, out AnimatedPicture picture, int budget = Budget, int quality = DefaultQuality)
        {
            double target = Math.Min(info.StepsPerSecond, SmoothSteps);
            // every step of the window's size slider (5% each): the search is a halving one, a handful of trial imports
            double[] sizes = Enumerable.Range(0, 16).Select(i => Math.Round(1.0 - 0.05 * i, 2)).ToArray();
            var tried = new Dictionary<int, AnimatedPicture>();
            GifOptions At(int i) => new GifOptions { Size = sizes[i], MaxStepsPerSecond = target >= info.StepsPerSecond - 0.01 ? 0 : target, Quality = Level(quality) };
            AnimatedPicture Try(int i)
            {
                if (!tried.TryGetValue(i, out var pic)) tried[i] = pic = FromGif(path, info.Name, budget, MaxFiles, At(i), needStill: false);
                return pic;
            }
            // smaller is cheaper for the screen and keeps more frames in the memory, so "good" only gets truer going down the list
            bool Good(int i)
            {
                var pic = Try(i);
                return pic != null && pic.ScreenLoad <= 0.95 && (!pic.MemoryLimited || pic.StepsPerSecond >= 0.8 * target);
            }
            int lo = 0, hi = sizes.Length - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Good(mid)) hi = mid; else lo = mid + 1; }
            picture = Try(lo);
            return At(lo);
        }

        /// <summary>The GIF's frame delays in ms (browsers' rule: 10 ms or less means 100).</summary>
        private static int[] Delays(Image gif, int n)
        {
            var d = Enumerable.Repeat(100, n).ToArray();
            try
            {
                var p = gif.GetPropertyItem(0x5100);
                for (int i = 0; i < n && i * 4 + 4 <= p.Value.Length; i++)
                {
                    int ms = BitConverter.ToInt32(p.Value, i * 4) * 10;
                    d[i] = ms <= 10 ? 100 : Math.Max(20, ms);
                }
            }
            catch { }
            return d;
        }

        /// <summary>
        /// The frames to work from, drawn complete (GDI+ hands out every frame already combined with the ones before it, as a
        /// browser would show it) at w x h on black: all of them, or at most MaxCandidates evenly spaced for a long GIF
        /// (and as many as fit in about 100 MB), each with the time it and the frames after it up to the next one last.
        /// </summary>
        private static List<Candidate> Decode(Image gif, FrameDimension dim, int total, int[] delays, int w, int h, double maxStepsPerSecond = 0)
        {
            int m = Math.Max(2, Math.Min(Math.Min(total, MaxCandidates), 100000000 / (w * h * 3)));
            // the person's smoothness limit: no more candidates than the loop has seconds times the steps a second wanted
            if (maxStepsPerSecond > 0) m = Math.Min(m, Math.Max(2, (int)Math.Ceiling(delays.Sum() / 1000.0 * maxStepsPerSecond)));
            m = Math.Min(m, total);
            var list = new List<Candidate>();
            using (var edges = new ImageAttributes())
            {
                edges.SetWrapMode(WrapMode.TileFlipXY);
                for (int k = 0; k < m; k++)
                {
                    int src = (int)Math.Round(k * (double)total / m), next = k + 1 < m ? (int)Math.Round((k + 1) * (double)total / m) : total;
                    int ms = 0;
                    for (int s = src; s < next; s++) ms += delays[s];
                    gif.SelectActiveFrame(dim, src);
                    using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Black);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(gif, new Rectangle(0, 0, w, h), 0, 0, gif.Width, gif.Height, GraphicsUnit.Pixel, edges);
                        }
                        list.Add(new Candidate { Id = k, Bgr = Bytes(bmp), Ms = ms });
                    }
                }
            }
            return list;
        }

        private static byte[] Bytes(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var bgr = new byte[w * h * 3];
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try { for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, bgr, y * w * 3, w * 3); }
            finally { bmp.UnlockBits(d); }
            return bgr;
        }

        /// <summary>The most frames that fit the budget at this quality (null: not even the first picture does).</summary>
        private static AnimatedPicture Fit(List<Candidate> c, int total, int w, int h, int q, int tolerance, int budget, int maxFiles, string name)
        {
            var cache = new Dictionary<string, Encoded>();
            bool Fits(AnimatedPicture p) => p.RamBytes <= budget && Files(p) <= maxFiles;
            var all = Build(c, c.Count, total, w, h, q, tolerance, cache, name);
            if (Fits(all)) return all;
            var best = Build(c, 1, total, w, h, q, tolerance, cache, name);
            if (!Fits(best)) return null;
            best.MemoryLimited = true;
            int lo = 1, hi = c.Count - 1; // c.Count frames don't fit, one does
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var p = Build(c, mid, total, w, h, q, tolerance, cache, name);
                if (Fits(p)) { lo = mid; best = p; best.MemoryLimited = true; } else hi = mid - 1;
            }
            return best;
        }

        private static int Files(AnimatedPicture p) =>
            p.Frames.Concat(p.Wrap == null ? new AnimFrame[0] : new[] { p.Wrap }).SelectMany(f => f.Tiles).Where(t => t.Jpeg != null).Select(t => t.Jpeg).Distinct().Count();

        /// <summary>
        /// n evenly spaced candidates as an animation: the first as a whole picture, each next as what changes from the one
        /// before (a frame that changes nothing just stays longer), and what changes from the last back to the first.
        /// </summary>
        private static AnimatedPicture Build(List<Candidate> c, int n, int total, int w, int h, int q, int tolerance, Dictionary<string, Encoded> cache, string name)
        {
            var sel = Enumerable.Range(0, n).Select(j => (int)Math.Round(j * (double)c.Count / n)).ToArray();
            // how long each kept frame shows: its own time and that of the frames left out after it
            var dwell = new int[n];
            for (int j = 0; j < n; j++)
                for (int k = sel[j]; k < (j + 1 < n ? sel[j + 1] : c.Count); k++) dwell[j] += c[k].Ms;

            var pic = new AnimatedPicture { Name = name, Width = w, Height = h, FramesTotal = total, FramesTaken = n };
            var shown = (byte[])c[sel[0]].Bgr.Clone(); // what the screen shows now, as far as the pictures go
            var first = new AnimFrame { Ms = dwell[0] };
            first.Tiles.Add(Tile(c[sel[0]], new Rectangle(0, 0, w, h), w, q, cache));
            pic.Frames.Add(first);
            for (int j = 1; j < n; j++)
            {
                var rects = Changes(shown, c[sel[j]].Bgr, w, h, tolerance);
                if (rects.Count == 0) { pic.Frames[pic.Frames.Count - 1].Ms += dwell[j]; continue; }
                var f = new AnimFrame { Ms = dwell[j] };
                foreach (var r in rects) { f.Tiles.Add(Tile(c[sel[j]], r, w, q, cache)); Copy(c[sel[j]].Bgr, shown, r, w); }
                pic.Frames.Add(f);
            }
            var back = Changes(shown, c[sel[0]].Bgr, w, h, tolerance);
            if (back.Count > 0)
            {
                pic.Wrap = new AnimFrame();
                foreach (var r in back) pic.Wrap.Tiles.Add(Tile(c[sel[0]], r, w, q, cache));
            }
            var names = new HashSet<string>();
            foreach (var t in pic.Frames.Concat(pic.Wrap == null ? new AnimFrame[0] : new[] { pic.Wrap }).SelectMany(f => f.Tiles).Where(t => t.Jpeg != null))
                if (names.Add(t.Jpeg)) pic.RamBytes += ScreenRam.Accounted(Convert.FromBase64String(t.Jpeg).Length);
            return pic;
        }

        private static void Copy(byte[] from, byte[] to, Rectangle r, int w)
        {
            for (int y = r.Top; y < r.Bottom; y++) Buffer.BlockCopy(from, (y * w + r.Left) * 3, to, (y * w + r.Left) * 3, r.Width * 3);
        }

        /// <summary>A rectangle of a frame as a tile: a JPEG, or one colour when it is (encodings are kept: the search tries
        /// many frame counts and the same rectangle of the same frame comes up again).</summary>
        private static AnimTile Tile(Candidate c, Rectangle r, int w, int q, Dictionary<string, Encoded> cache)
        {
            string key = $"{c.Id}|{r.X},{r.Y},{r.Width},{r.Height}|{q}";
            if (!cache.TryGetValue(key, out var e))
            {
                e = Encode(c.Bgr, r, w, q);
                cache[key] = e;
            }
            return new AnimTile { X = r.X, Y = r.Y, W = r.Width, H = r.Height, Jpeg = e.Jpeg == null ? null : Convert.ToBase64String(e.Jpeg), Colour = e.Colour };
        }

        private static Encoded Encode(byte[] bgr, Rectangle r, int w, int q)
        {
            int o0 = (r.Y * w + r.X) * 3;
            bool one = true;
            for (int y = r.Top; y < r.Bottom && one; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int o = (y * w + x) * 3;
                    if (bgr[o] != bgr[o0] || bgr[o + 1] != bgr[o0 + 1] || bgr[o + 2] != bgr[o0 + 2]) { one = false; break; }
                }
            if (one) return new Encoded { Colour = (bgr[o0 + 2] >> 3 << 11) | (bgr[o0 + 1] >> 2 << 5) | (bgr[o0] >> 3) };
            using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
            {
                var d = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try { for (int y = 0; y < r.Height; y++) System.Runtime.InteropServices.Marshal.Copy(bgr, ((r.Y + y) * w + r.X) * 3, d.Scan0 + y * d.Stride, r.Width * 3); }
                finally { bmp.UnlockBits(d); }
                return new Encoded { Jpeg = ScreenTiles.Jpeg(bmp, q) };
            }
        }

        // ---------------------------------------------------------------- what changed

        /// <summary>
        /// The rectangles that cover what differs between two frames: the box around it all, cut into a few boxes where a wide
        /// enough empty band runs through it (two blinking lights far apart are two small pictures, not one big one).
        /// </summary>
        internal static List<Rectangle> Changes(byte[] shown, byte[] next, int w, int h, int threshold = Threshold)
        {
            var mask = new bool[w * h];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 3;
                    if (Math.Abs(shown[o] - next[o]) > threshold || Math.Abs(shown[o + 1] - next[o + 1]) > threshold || Math.Abs(shown[o + 2] - next[o + 2]) > threshold)
                    {
                        mask[y * w + x] = true;
                        if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
                }
            var list = new List<Rectangle>();
            if (maxX < 0) return list;
            var box = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            Split(mask, w, box, list, 0);
            if (list.Count > MaxPatchesPerFrame) list = new List<Rectangle> { box };
            // changes over nearly the whole picture (a clip: every pixel moves): the whole picture. A little dearer, but the
            // step stands on its own, so the player can jump to it when the screen is behind (GifSaver)
            if (list.Sum(r => (long)r.Width * r.Height) * 100 >= (long)w * h * WholePercent) return new List<Rectangle> { new Rectangle(0, 0, w, h) };
            return list;
        }

        /// <summary>A cut is worth it when what it leaves out is more than a file costs (its header and the drive's entry
        /// are ~1 KB: about this many pixels of a JPEG).</summary>
        private const int WorthACut = 4000;

        private static void Split(bool[] mask, int w, Rectangle r, List<Rectangle> output, int depth)
        {
            r = Tighten(mask, w, r);
            // the empty rows and columns inside the box
            int bestGap = 0, at = 0; bool rows = true;
            var rowHas = new bool[r.Height]; var colHas = new bool[r.Width];
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                    if (mask[(r.Y + y) * w + r.X + x]) { rowHas[y] = true; colHas[x] = true; }
            for (int y = 1; y < r.Height - 1; y++)
            {
                if (rowHas[y]) continue;
                int e = y; while (e < r.Height && !rowHas[e]) e++;
                if ((e - y) * r.Width > bestGap) { bestGap = (e - y) * r.Width; at = y; rows = true; }
                y = e;
            }
            for (int x = 1; x < r.Width - 1; x++)
            {
                if (colHas[x]) continue;
                int e = x; while (e < r.Width && !colHas[e]) e++;
                if ((e - x) * r.Height > bestGap) { bestGap = (e - x) * r.Height; at = x; rows = false; }
                x = e;
            }
            if (bestGap < WorthACut || depth >= 4) { output.Add(Tighten(mask, w, r)); return; }
            Rectangle a, b;
            if (rows) { int e = at; while (!rowHas[e]) e++; a = new Rectangle(r.X, r.Y, r.Width, at); b = new Rectangle(r.X, r.Y + e, r.Width, r.Height - e); }
            else { int e = at; while (!colHas[e]) e++; a = new Rectangle(r.X, r.Y, at, r.Height); b = new Rectangle(r.X + e, r.Y, r.Width - e, r.Height); }
            Split(mask, w, a, output, depth + 1);
            Split(mask, w, b, output, depth + 1);
        }

        /// <summary>The box around what's changed inside `r`.</summary>
        private static Rectangle Tighten(bool[] mask, int w, Rectangle r)
        {
            int minX = r.Right, minY = r.Bottom, maxX = -1, maxY = -1;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (mask[y * w + x]) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            return maxX < 0 ? r : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
    }
}
