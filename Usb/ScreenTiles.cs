using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A piece of a dash's static layer as a picture on the screen's RAM drive (FXProDashes docs/screen-images.md):
    /// drawn with one `sets "ramv: X, Y, ram/NAME"` instead of many `fill`s. A tile of one colour has no file and is
    /// drawn with one `fill`.
    /// </summary>
    internal sealed class ScreenTile
    {
        /// <summary>File name on the RAM drive (from the JPEG's hash, so equal tiles are shared); null = one colour.</summary>
        public string Name;
        /// <summary>Where it goes, in dash coordinates.</summary>
        public Rectangle R;
        /// <summary>RGB565, for a tile without a file.</summary>
        public int Colour;
        public byte[] Jpeg;
    }

    /// <summary>
    /// A dash's static layer cut into pictures for the screen's RAM drive: a grid (drawing the dash, and putting areas
    /// back) and, for values over a background that isn't one colour, a band tile each (the background under the value's
    /// text, so a new value is the band and the text: two commands that touch nothing else).
    /// </summary>
    internal sealed class ScreenTiles
    {
        /// <summary>Grid cell: 160 x 160 = 5 x 3 tiles. Bigger tiles cost less RAM (each JPEG carries its own tables,
        /// ~600 bytes) but put back more around a change; measured on a busy background: 160 px ~ one full picture.</summary>
        public const int Grid = 160;
        public const long Quality = 88;

        public readonly List<ScreenTile> GridTiles = new List<ScreenTile>();
        /// <summary>By element index.</summary>
        public readonly Dictionary<int, ScreenTile> Bands = new Dictionary<int, ScreenTile>();
        /// <summary>Pictures that come and go (an image element with a condition: tyre compound, pit or headlight icons),
        /// by element index: the picture over the static layer under it, drawn with one command when it shows (with fills
        /// a 59 px icon took ~840 fills, ~19 KB, ~0.8 s, every time the Ferrari 488 dash showed).</summary>
        public readonly Dictionary<int, ScreenTile> Pictures = new Dictionary<int, ScreenTile>();
        /// <summary>A picture bigger than this stays drawn with fills (a full-screen splash would take a big part of the drive).</summary>
        public const int MaxPicture = 24 * 1024;

        /// <summary>The files this dash needs on the screen (each once).</summary>
        public IEnumerable<ScreenTile> Files =>
            GridTiles.Concat(Bands.Values).Concat(Pictures.Values).Concat(Variants).Where(t => t.Name != null).GroupBy(t => t.Name).Select(g => g.First());

        /// <summary>More pictures of those shapes: with an overlay inside theirs under them (a flag inside the race start
        /// screen), or in another of their colours (a speed-coloured oval), so they're drawn from a file in that state too
        /// (DashRenderer picks the one that matches).</summary>
        public readonly List<ScreenTile> Variants = new List<ScreenTile>();

        /// <summary>RAM-drive bytes the files take (their sizes; the drive adds a small entry per file).</summary>
        public int Bytes => Files.Sum(t => t.Jpeg.Length);
        public int FileCount => Files.Count();

        /// <summary>The tiles of a grid cell that a dash-coordinate rectangle touches.</summary>
        public IEnumerable<ScreenTile> GridTilesIn(Rectangle area) => GridTiles.Where(t => t.R.IntersectsWith(area));

        /// <summary>A rectangle grown to whole grid cells.</summary>
        public static Rectangle Snap(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return r;
            int x0 = r.Left / Grid * Grid, y0 = r.Top / Grid * Grid;
            int x1 = (r.Right + Grid - 1) / Grid * Grid, y1 = (r.Bottom + Grid - 1) / Grid * Grid;
            return Rectangle.Intersect(Rectangle.FromLTRB(x0, y0, x1, y1), new Rectangle(0, 0, DashRenderer.Width, DashRenderer.Height));
        }

        /// <summary>A tile of `bmp` (full-colour static layer, 32 bpp) over `r`, `px` being its RGB565 pixels.</summary>
        public static ScreenTile Make(Bitmap bmp, int[] px, Rectangle r)
        {
            var t = new ScreenTile { R = r };
            int c0 = px[r.Y * DashRenderer.Width + r.X];
            bool one = true;
            for (int y = r.Top; y < r.Bottom && one; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (px[y * DashRenderer.Width + x] != c0) { one = false; break; }
            if (one) { t.Colour = c0; return t; }
            using (var part = bmp.Clone(r, PixelFormat.Format24bppRgb))
                t.Jpeg = Jpeg(part, Quality);
            t.Name = NameFor(t.Jpeg);
            Registry[t.Name] = t.Jpeg;
            return t;
        }

        /// <summary>A tile of a picture already cut to `r` (32 bpp, r's size).</summary>
        /// <summary>JPEG quality of the pictures of shapes that come and go (MakeFrom; overlays' ovals, frames, logos): 75 looks
        /// the same as 88 on them on the wheel's screen and takes a third less of the RAM drive. The dash itself uses Quality.</summary>
        public static long PictureQuality = 75;

        public static ScreenTile MakeFrom(Bitmap part, Rectangle r)
        {
            var t = new ScreenTile { R = r };
            using (var rgb = part.Clone(new Rectangle(0, 0, part.Width, part.Height), PixelFormat.Format24bppRgb))
                t.Jpeg = Jpeg(rgb, PictureQuality);
            t.Name = NameFor(t.Jpeg);
            Registry[t.Name] = t.Jpeg;
            return t;
        }

        public static string NameFor(byte[] jpeg)
        {
            using (var sha = SHA1.Create())
                return "t" + BitConverter.ToString(sha.ComputeHash(jpeg), 0, 5).Replace("-", "").ToLowerInvariant() + ".jpg";
        }

        public static byte[] Jpeg(Bitmap bmp, long quality)
        {
            var enc = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
            using (var ps = new EncoderParameters(1))
            using (var ms = new MemoryStream())
            {
                ps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bmp.Save(ms, enc, ps);
                return ms.ToArray();
            }
        }

        /// <summary>Every tile built in this process, by name: what previews and the screen mirror draw for `ramv`.</summary>
        public static readonly ConcurrentDictionary<string, byte[]> Registry = new ConcurrentDictionary<string, byte[]>();

        /// <summary>The `ramv` command that draws a tile at its place shifted by the dash's padding.</summary>
        public static string Ramv(ScreenTile t, int dx, int dy) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "sets \"ramv: {0}, {1}, ram/{2}\"", t.R.X + dx, t.R.Y + dy, t.Name);

        /// <summary>Parses a `ramv` command: (x, y, file name), or null.</summary>
        public static Tuple<int, int, string> ParseRamv(string cmd)
        {
            const string head = "sets \"ramv: ";
            if (!cmd.StartsWith(head, StringComparison.Ordinal) || !cmd.EndsWith("\"", StringComparison.Ordinal)) return null;
            var parts = cmd.Substring(head.Length, cmd.Length - head.Length - 1).Split(new[] { ", " }, StringSplitOptions.None);
            if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)) return null;
            string path = parts[2];
            return path.StartsWith("ram/", StringComparison.Ordinal) ? Tuple.Create(x, y, path.Substring(4)) : null;
        }
    }
}

namespace User.FXProRpmSync
{
    /// <summary>
    /// What dashes take on the screen's RAM drive (their tiles), for the dashes page and the designer. Cached per dash
    /// version; building a dash's tiles takes ~0.1 s, so call it off the UI thread.
    /// </summary>
    internal static class DashRam
    {
        private static readonly ConcurrentDictionary<string, ScreenTiles> cache = new ConcurrentDictionary<string, ScreenTiles>();

        private static string Key(DashDefinition d) =>
            (d.Id ?? "") + "|" + Newtonsoft.Json.JsonConvert.SerializeObject(d.Elements).GetHashCode() + "|" + (d.Images?.Count ?? 0);

        /// <summary>The dash's tiles (as the wheel gets them), or null if it can't be drawn.</summary>
        public static ScreenTiles TilesOf(DashDefinition d)
        {
            if (d == null) return null;
            string key = Key(d);
            if (cache.TryGetValue(key, out var t)) return t;
            try
            {
                using (var p = new PreviewScreen()) t = new DashRenderer(p, d, 0, 0).EnableTiles();
            }
            catch { t = null; }
            if (t != null) cache[key] = t;
            return t;
        }

        /// <summary>Bytes on the drive for one dash.</summary>
        public static int Bytes(DashDefinition d) => TilesOf(d)?.Bytes ?? 0;

        /// <summary>What several dashes take on the drive together, in accounted bytes (files they share counted once).</summary>
        public static int Bytes(IEnumerable<DashDefinition> dashes) =>
            dashes.Where(d => d != null).Select(TilesOf).Where(t => t != null).SelectMany(t => t.Files)
                  .GroupBy(f => f.Name).Sum(g => ScreenRam.Accounted(g.First().Jpeg.Length));

        /// <summary>"36 KB".</summary>
        public static string Text(int bytes) => bytes <= 0 ? "0 KB" : $"{Math.Max(1, (bytes + 512) / 1024)} KB";
    }
}
