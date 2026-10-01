using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>Stock wheel over RF (SimPro does lights and dashes) or the flashed wheel over USB (the plugin does both).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum WheelMode { Standard, Unlocked }

    /// <summary>
    /// Unlocked mode, per car: the dashes it switches between (a button bound to the UsbNextDash action steps through
    /// them), each one of the plugin's dashes or one of the wheel's own (the screen is given back and the wheel switched
    /// to it through SimPro). See DashRef.
    /// </summary>
    public class UsbCarDash
    {
        public string CarKey;
        public string Game;
        public string CarId;
        public string CarName;
        public List<string> Dashes = new List<string>();
        /// <summary>The one shown now (index into Dashes).</summary>
        public int Current;
        public DateTime UpdatedUtc;
        /// <summary>Before dash lists: the one dash (read once, then moved into Dashes).</summary>
        public bool WheelDash;
        public string DashId;

        public UsbCarDash Clone() => (UsbCarDash)MemberwiseClone();
    }

    /// <summary>A dash in a car's list: "c:&lt;id&gt;" = one of the plugin's, "w:&lt;page&gt;" = one of the wheel's own
    /// ("w:" alone = whatever the wheel shows).</summary>
    public static class DashRef
    {
        public static string Custom(string id) => "c:" + id;
        public static string Wheel(string page) => "w:" + (page ?? "");
        public static bool IsWheel(string r) => r != null && r.StartsWith("w:");
        public static string Id(string r) { var id = r == null || r.Length < 2 ? null : r.Substring(2); return string.IsNullOrEmpty(id) ? null : id; }

        public static string Name(string r) =>
            IsWheel(r) ? (Id(r) == null ? "Wheel's own dash" : DashCatalog.NameOf(Id(r))) : DashLibrary.Load(null).FirstOrDefault(d => d.Id == Id(r))?.Name ?? Id(r);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SaverKind { Logo, Clock, Image, Dash, Builtin }

    /// <summary>A screensaver: the logo, a built-in one, a picture (stored as a one-image dash) or one of the dashes.</summary>
    public class SaverItem
    {
        public const string LogoId = "logo", ClockId = "clock";

        public string Id;
        public string Name;
        public SaverKind Kind;
        /// <summary>Dash: the dash's id. Image: the file of its generated dash (in IdleScreens.Folder).</summary>
        public string DashId;
        public string File;
        /// <summary>For the gallery.</summary>
        [JsonIgnore] public string Blurb;
    }

    /// <summary>The last session driven, for the pit board screensaver (kept in the settings).</summary>
    public class LastSession
    {
        public string Car;
        public string Game;
        public double BestLap;
        public int Laps;
        public int Position;
        public DateTime When;
    }

    /// <summary>What the screen shows when you're not racing, and the pictures turned into screensavers.</summary>
    public static class IdleScreens
    {
        public static string Folder => Path.Combine(DashLibrary.SimHubFolder, "PluginsData", "Common", "FXProRpmSync", "Savers");

        public static readonly SaverItem Logo = new SaverItem { Id = SaverItem.LogoId, Name = "FX Unleashed logo", Kind = SaverKind.Logo, Blurb = "The plugin's logo" };
        public static readonly SaverItem Clock = new SaverItem { Id = SaverItem.ClockId, Name = "Clock", Kind = SaverKind.Clock, Blurb = "Time and date" };

        /// <summary>The built-in screensavers made of dash elements, animated by IdleValues.</summary>
        private static readonly (SaverItem Item, Func<DashDefinition> Make)[] builtins =
        {
            // painted art + animation (ArtSavers.cs)
            (new SaverItem { Id = "lights-out", Name = "Lights out", Kind = SaverKind.Builtin, Blurb = "The start gantry, then go" }, null),
            (new SaverItem { Id = "rev-sweep", Name = "Rev sweep", Kind = SaverKind.Builtin, Blurb = "A tacho sweeping through the gears" }, null),
            (new SaverItem { Id = "pit-board", Name = "Pit board", Kind = SaverKind.Builtin, Blurb = "Your last session" }, null),
            (new SaverItem { Id = "chequered", Name = "Chequered", Kind = SaverKind.Builtin, Blurb = "The waving flag and the time" }, null),
        };

        /// <summary>The built-in screensavers, then the user's.</summary>
        public static List<SaverItem> All(UsbSettings s) =>
            new[] { Logo, Clock }.Concat(builtins.Select(b => b.Item)).Concat(s.Savers ?? new List<SaverItem>()).ToList();

        public static SaverItem Find(UsbSettings s, string id) => All(s).FirstOrDefault(x => x.Id == id) ?? Logo;

        /// <summary>The dash a screensaver draws (null for the logo, which has its own animation).</summary>
        public static DashDefinition DashFor(SaverItem item, List<DashDefinition> library = null)
        {
            switch (item?.Kind)
            {
                case SaverKind.Clock: return ClockDash();
                case SaverKind.Builtin: return builtins.FirstOrDefault(b => b.Item.Id == item.Id).Make?.Invoke();
                case SaverKind.Image:
                    try { return JsonConvert.DeserializeObject<DashDefinition>(System.IO.File.ReadAllText(item.File)); }
                    catch { return null; }
                case SaverKind.Dash:
                    return (library ?? DashLibrary.Load(null)).FirstOrDefault(d => d.Id == item.DashId);
                default: return null;
            }
        }

        /// <summary>The screensavers that draw themselves (the logo, the painted built-ins); null for dash ones.</summary>
        internal static IAnimatedSaver Animated(SaverItem item, LastSession last = null)
        {
            if (item == null || item.Kind == SaverKind.Logo) return new ScreenSaver();
            switch (item.Id)
            {
                case "lights-out": return new LightsOutSaver();
                case "rev-sweep": return new TachoSaver();
                case "pit-board": return new PitBoardSaver(last);
                case "chequered": return new ChequeredSaver();
                default: return null;
            }
        }

        /// <summary>The start lights at `t`: a 9 s cycle, one light a second, a random hold, then out (go = "LIGHTS OUT").</summary>
        public static bool[] StartLights(double t, out bool go) => StartLights(t, out go, out _);

        /// <summary>The same, with the seconds since the lights went out (for the reaction timer).</summary>
        public static bool[] StartLights(double t, out bool go, out double sinceOut)
        {
            int cycle = (int)(t / 9);
            double c = t - cycle * 9, hold = 4.6 + new Random(cycle).NextDouble() * 1.4;
            go = c >= hold && c < hold + 2.4;
            sinceOut = Math.Max(0, c - hold);
            var on = new bool[5];
            for (int k = 0; k < 5; k++) on[k] = c >= (k + 1) * 0.9 && c < hold;
            return on;
        }

        /// <summary>
        /// The rev sweep at `t`: 3.2 s per gear (1-6): the revs climb (`frac` 0-1), then the lights flash blue at the shift
        /// point (`flash` true/false, null before it).
        /// </summary>
        public static (int Gear, double Frac, bool? Flash) RevSweep(double t)
        {
            int cycle = (int)(t / 3.2);
            double g = (t - cycle * 3.2) / 3.2, f = g < 0.82 ? Math.Pow(g / 0.82, 1.25) : 1;
            bool? flash = g >= 0.82 ? (int)((g - 0.82) * 3.2 / 0.08) % 2 == 0 : (bool?)null;
            return (cycle % 6 + 1, f, flash);
        }

        /// <summary>Idle values for a screensaver dash: the time and date.</summary>
        public static void AddClock(DashValues v)
        {
            var now = DateTime.Now;
            v.Set("clock", now.ToString("HH:mm", CultureInfo.InvariantCulture));
            v.Set("date", now.ToString("ddd d MMM", CultureInfo.InvariantCulture).ToUpperInvariant());
        }

        /// <summary>Everything a screensaver dash shows (the clock and date). `t` and `last` are kept for the callers.</summary>
        public static void IdleValues(DashValues v, double t, LastSession last) => AddClock(v);

        private static DashElement Val(string name, string bind, int font, int x, int y, int w, int h, string colour, string align, params string[] samples) =>
            new DashElement { Type = "value", Name = name, Bind = bind, Format = "text", Font = font, Align = align, X = x, Y = y, W = w, H = h, Color = colour, Samples = samples, Empty = "" };

        private static DashElement Rect(string name, int x, int y, int w, int h, string colour) =>
            new DashElement { Type = "rect", Name = name, X = x, Y = y, W = w, H = h, Color = colour };

        /// <summary>A big clock over the date, with a red rule between (fonts 1 = 150 px and 3 = 48 px, monospaced digits).</summary>
        public static DashDefinition ClockDash() => new DashDefinition
        {
            Id = "saver:clock", Name = "Clock", Author = "FX Unleashed",
            Elements = new List<DashElement>
            {
                Val("clock", "clock", 1, 40, 110, 710, 150, "#FFFFFF", "center", "88:88"),
                Rect("rule", 335, 282, 120, 4, "#FF1414"),
                Val("date", "date", 3, 95, 306, 600, 48, "#9AA0A6", "center", "WED 28 SEP"),
            },
        };

        /// <summary>Most seconds a picture may take to draw in (25 KB/s): it's drawn once, while the lights keep running.</summary>
        public const double MaxDrawSeconds = 15;

        /// <summary>
        /// Turns a picture into a screensaver: fitted into the screen's usable 790x460, then reduced (fewer colours, then
        /// bigger pixels) until it draws in MaxDrawSeconds, since the screen only draws rectangles. Saved as a dash file.
        /// </summary>
        public static SaverItem ImportImage(string path)
        {
            const int areaW = 790, areaH = 460;
            using (var src = new Bitmap(path))
            {
                double k = Math.Min(1.0 * areaW / src.Width, 1.0 * areaH / src.Height);
                int w = Math.Max(8, (int)Math.Round(src.Width * k)), h = Math.Max(8, (int)Math.Round(src.Height * k));
                DashDefinition best = null;
                foreach (int block in new[] { 1, 2, 3, 4, 5, 6, 8, 10 })
                    foreach (int colours in new[] { 24, 16, 12, 8 })
                    {
                        var d = ImageDash(src, w, h, block, colours, Path.GetFileNameWithoutExtension(path));
                        best = d;
                        if (DrawSeconds(d) <= MaxDrawSeconds) goto done;
                    }
                done:
                Directory.CreateDirectory(Folder);
                string id = "img-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                best.Id = "saver:" + id;
                var file = Path.Combine(Folder, id + ".json");
                System.IO.File.WriteAllText(file, JsonConvert.SerializeObject(best, Formatting.Indented));
                return new SaverItem { Id = id, Name = best.Name, Kind = SaverKind.Image, File = file };
            }
        }

        public static double DrawSeconds(DashDefinition d)
        {
            using (var sink = new PreviewScreen())
            {
                new DashRenderer(sink, d, 0, 0).CheckDetailed(out var cost);
                return cost.StaticSeconds;
            }
        }

        private static DashDefinition ImageDash(Bitmap src, int w, int h, int block, int colours, string name)
        {
            using (var small = new Bitmap(Math.Max(1, w / block), Math.Max(1, h / block), PixelFormat.Format32bppArgb))
            using (var full = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, small.Width, small.Height);
                }
                using (var g = Graphics.FromImage(full))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, 0, 0, w, h);
                }
                string png;
                using (var ms = new MemoryStream()) { full.Save(ms, ImageFormat.Png); png = Convert.ToBase64String(ms.ToArray()); }
                return new DashDefinition
                {
                    Name = name, Author = "FX Unleashed",
                    Images = new Dictionary<string, string> { ["picture"] = png },
                    Elements = new List<DashElement>
                    {
                        new DashElement { Type = "image", Name = "picture", Image = "picture", MaxColors = colours, X = (790 - w) / 2, Y = (460 - h) / 2, W = w, H = h },
                    },
                };
            }
        }

        public static void Delete(SaverItem item)
        {
            if (item?.Kind != SaverKind.Image || string.IsNullOrEmpty(item.File)) return;
            try { if (System.IO.File.Exists(item.File)) System.IO.File.Delete(item.File); } catch { }
        }
    }
}
