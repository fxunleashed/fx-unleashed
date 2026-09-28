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
    /// USB mode, per car: one of the plugin's dashes, or the wheel's own dashes (the screen is given back and the wheel is
    /// switched to the car's wheel dash, which lives in the plugin's CarDashes like in standard mode).
    /// </summary>
    public class UsbCarDash
    {
        public string CarKey;
        public string Game;
        public string CarId;
        public string CarName;
        /// <summary>True: the wheel's own dash; false: the custom dash DashId.</summary>
        public bool WheelDash;
        public string DashId;
        public DateTime UpdatedUtc;

        public UsbCarDash Clone() => (UsbCarDash)MemberwiseClone();
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SaverKind { Logo, Clock, Image, Dash }

    /// <summary>A screensaver: the logo, the clock, a picture (stored as a one-image dash) or one of the dashes.</summary>
    public class SaverItem
    {
        public const string LogoId = "logo", ClockId = "clock";

        public string Id;
        public string Name;
        public SaverKind Kind;
        /// <summary>Dash: the dash's id. Image: the file of its generated dash (in IdleScreens.Folder).</summary>
        public string DashId;
        public string File;
    }

    /// <summary>What the screen shows when you're not racing, and the pictures turned into screensavers.</summary>
    public static class IdleScreens
    {
        public static string Folder => Path.Combine(DashLibrary.SimHubFolder, "PluginsData", "Common", "FXProRpmSync", "Savers");

        public static readonly SaverItem Logo = new SaverItem { Id = SaverItem.LogoId, Name = "FX Pro logo", Kind = SaverKind.Logo };
        public static readonly SaverItem Clock = new SaverItem { Id = SaverItem.ClockId, Name = "Clock", Kind = SaverKind.Clock };

        /// <summary>The built-in screensavers, then the user's.</summary>
        public static List<SaverItem> All(UsbSettings s) =>
            new[] { Logo, Clock }.Concat(s.Savers ?? new List<SaverItem>()).ToList();

        public static SaverItem Find(UsbSettings s, string id) => All(s).FirstOrDefault(x => x.Id == id) ?? Logo;

        /// <summary>The dash a screensaver draws (null for the logo, which has its own animation).</summary>
        public static DashDefinition DashFor(SaverItem item, List<DashDefinition> library = null)
        {
            switch (item?.Kind)
            {
                case SaverKind.Clock: return ClockDash();
                case SaverKind.Image:
                    try { return JsonConvert.DeserializeObject<DashDefinition>(System.IO.File.ReadAllText(item.File)); }
                    catch { return null; }
                case SaverKind.Dash:
                    return (library ?? DashLibrary.Load(null)).FirstOrDefault(d => d.Id == item.DashId);
                default: return null;
            }
        }

        /// <summary>Idle values for a screensaver dash: the time and date.</summary>
        public static void AddClock(DashValues v)
        {
            var now = DateTime.Now;
            v.Set("clock", now.ToString("HH:mm", CultureInfo.InvariantCulture));
            v.Set("date", now.ToString("ddd d MMM", CultureInfo.InvariantCulture).ToUpperInvariant());
        }

        /// <summary>A big clock over the date, with a red rule between (fonts 1 = 150 px and 3 = 48 px, monospaced digits).</summary>
        public static DashDefinition ClockDash() => new DashDefinition
        {
            Id = "saver:clock", Name = "Clock", Author = "FXPro RPM Sync",
            Elements = new List<DashElement>
            {
                new DashElement { Type = "value", Name = "clock", Bind = "clock", Format = "text", Font = 1, Align = "center", X = 40, Y = 110, W = 710, H = 150, Color = "#FFFFFF", Samples = new[] { "88:88" }, Empty = "" },
                new DashElement { Type = "rect", Name = "rule", X = 335, Y = 282, W = 120, H = 4, Color = "#FF1414" },
                new DashElement { Type = "value", Name = "date", Bind = "date", Format = "text", Font = 3, Align = "center", X = 95, Y = 306, W = 600, H = 48, Color = "#9AA0A6", Samples = new[] { "WED 28 SEP" }, Empty = "" },
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
                    Name = name, Author = "FXPro RPM Sync",
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
