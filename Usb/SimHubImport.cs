using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>Picking a screen font for a text box (real sizes from FontMetrics).</summary>
    public static class DashFonts
    {
        /// <summary>Font ids with a glyph for every printable ASCII character, tallest first.</summary>
        public static readonly int[] FullAscii = Enumerable.Range(0, FontMetrics.Fonts.Length)
            .Where(f => Enumerable.Range(32, 95).All(c => FontMetrics.Fonts[f][c - 31] >= 0))
            .OrderByDescending(f => FontMetrics.Fonts[f][0]).ToArray();

        /// <summary>
        /// The tallest font (not taller than `height` * 1.15 nor the box) whose `sample` fits the box width; among fonts of
        /// that height, the one closest to ordinary text proportions. Returns -1 when nothing fits.
        /// </summary>
        public static int Pick(double height, int boxW, int boxH, string sample)
        {
            sample = string.IsNullOrEmpty(sample) ? "0" : sample;
            var fits = FullAscii
                .Concat(Enumerable.Range(0, FontMetrics.Fonts.Length).Except(FullAscii)) // gear/number fonts, if the sample only uses their glyphs
                .Where(f => FontMetrics.Fonts[f][0] <= Math.Max(8, height * 1.15) && FontMetrics.Fonts[f][0] <= boxH)
                .Select(f => (Font: f, W: DashRenderer.TextWidth(f, sample), H: FontMetrics.Fonts[f][0]))
                .Where(x => x.W >= 0 && x.W <= boxW)
                .ToList();
            if (fits.Count == 0) return -1;
            int best = fits.Max(x => x.H);
            return fits.Where(x => x.H == best)
                       .OrderBy(x => Math.Abs(DashRenderer.TextWidth(x.Font, "a") / (double)x.H - 0.55))
                       .First().Font;
        }

        /// <summary>The smallest full-ASCII font (a last resort when nothing fits).</summary>
        public static int Smallest => FullAscii.Last();
    }

    public class ImportOptions
    {
        /// <summary>Screen name or index; null = the main in-game screen.</summary>
        public string Screen;
        public bool Images = true;
        /// <summary>Colours per image (fewer = faster to draw).</summary>
        public int ImageColors = 6;
        /// <summary>If drawing the static layer would take longer than this, image colours are cut, then the largest images dropped.</summary>
        public double MaxDrawSeconds = 8;
        /// <summary>The area the dash is scaled into (top left at 0,0): the screen minus the padding the wheel uses.</summary>
        public int FitWidth = DashRenderer.Width, FitHeight = DashRenderer.Height;
    }

    public class ImportReport
    {
        public string Dash, Screen;
        public int Converted, Approximated, Skipped;
        public int NcalcFormulas, JsFormulas;
        public List<string> Notes = new List<string>();
        public Dictionary<string, int> SkippedTypes = new Dictionary<string, int>();
        public List<string> Screens = new List<string>();

        /// <summary>Adds a note once; repeats are counted ("... (x3)").</summary>
        public void Note(string note)
        {
            int i = Notes.FindIndex(n => n == note || n.StartsWith(note + " (x"));
            if (i < 0) { Notes.Add(note); return; }
            var m = Regex.Match(Notes[i], @" \(x(\d+)\)$");
            int count = m.Success ? int.Parse(m.Groups[1].Value) + 1 : 2;
            Notes[i] = note + $" (x{count})";
        }

        public void Skip(string type, string why = null)
        {
            Skipped++;
            SkippedTypes[type] = SkippedTypes.TryGetValue(type, out var n) ? n + 1 : 1;
            if (why != null) Note(why);
        }

        public override string ToString() =>
            $"{Dash} / {Screen}: {Converted} converted, {Approximated} approximated, {Skipped} skipped" +
            (SkippedTypes.Count > 0 ? " (" + string.Join(", ", SkippedTypes.Select(k => k.Key + " x" + k.Value)) + ")" : "") +
            $"; formulas: {NcalcFormulas} NCalc, {JsFormulas} JavaScript" + (Notes.Count > 0 ? "\n- " + string.Join("\n- ", Notes) : "");
    }

    /// <summary>
    /// Converts a SimHub dash (.djson, SimHub's Dash Studio format) into a DashDefinition for the FX Pro: the layout scaled
    /// to 800x480, shapes, gradients and images (reduced to a few colours), texts with the closest screen font that
    /// fits, linear gauges as bars, layers and widgets flattened, and every binding kept as a SimHub formula
    /// ("ncalc:"/"js:"), which SimHub's own engine evaluates while SimHub runs. Visibility conditions of items, layers and
    /// widgets carry over; widget screens switched by a formula are imported as conditional groups. Not converted
    /// (reported): dial gauges, charts, maps, leaderboards, web pages, buttons, shift light images, overlay screens.
    /// The result is a starting point for the dash designer.
    /// </summary>
    public static class SimHubImport
    {
        public static string DashTemplatesFolder => Path.Combine(DashLibrary.SimHubFolder, "DashTemplates");

        /// <summary>Installed SimHub dashes: (name, .djson path). The main file is the one named like its folder.</summary>
        public static List<(string Name, string Path)> Installed(string folder = null)
        {
            folder = folder ?? DashTemplatesFolder;
            var list = new List<(string, string)>();
            if (!Directory.Exists(folder)) return list;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d))
            {
                var name = Path.GetFileName(dir);
                var main = Path.Combine(dir, name + ".djson");
                if (File.Exists(main)) list.Add((name, main));
            }
            return list;
        }

        /// <summary>Screen names of a dash (for choosing one).</summary>
        public static List<string> ScreenNames(string path)
        {
            var d = Load(path);
            return (d["Screens"] as JArray ?? new JArray()).Select((s, i) => (string)s["Name"] ?? ("Screen " + (i + 1))).ToList();
        }

        private static JObject Load(string path) => (JObject)Normalize(JObject.Parse(File.ReadAllText(path)));

        /// <summary>
        /// Older dashes are saved with Newtonsoft's reference handling: lists as {"$id", "$values": [...]} and repeated
        /// objects as {"$ref": id}. Unwraps both so every dash reads the same.
        /// </summary>
        private static JToken Normalize(JToken root)
        {
            var ids = new Dictionary<string, JToken>();
            void Collect(JToken t)
            {
                if (t is JObject o)
                {
                    if (o["$id"] is JValue id && id.Value != null) ids[id.ToString()] = o;
                    foreach (var p in o.Properties()) Collect(p.Value);
                }
                else if (t is JArray a) foreach (var x in a) Collect(x);
            }
            JToken Fix(JToken t, int depth)
            {
                if (depth > 200) return t;
                if (t is JObject o)
                {
                    if (o["$ref"] is JValue r && r.Value != null && ids.TryGetValue(r.ToString(), out var target) && target != o)
                        return Fix(target.DeepClone(), depth + 1);
                    if (o["$values"] is JArray values && o.Properties().All(p => p.Name == "$id" || p.Name == "$values" || p.Name == "$type"))
                        return Fix(values, depth + 1);
                    var copy = new JObject();
                    foreach (var p in o.Properties()) copy[p.Name] = Fix(p.Value, depth + 1);
                    return copy;
                }
                if (t is JArray a) return new JArray(a.Select(x => Fix(x, depth + 1)));
                return t;
            }
            Collect(root);
            return Fix(root, 0);
        }

        public static DashDefinition Import(string path, ImportOptions opt, out ImportReport report)
        {
            opt = opt ?? new ImportOptions();
            var ctx = new Context(path, opt);
            report = ctx.Report;
            var def = ctx.Run();
            if (opt.Images) FitDrawTime(def, opt, report);
            return def;
        }

        /// <summary>Keeps the static layer's draw time under the budget: fewer image colours, then drop the largest images.</summary>
        private static void FitDrawTime(DashDefinition def, ImportOptions opt, ImportReport report)
        {
            for (int round = 0; round < 12; round++)
            {
                double seconds;
                using (var p = new PreviewScreen())
                {
                    new DashRenderer(p, def, 0, 0).CheckDetailed(out var cost);
                    seconds = cost.StaticSeconds;
                }
                if (seconds <= opt.MaxDrawSeconds) return;
                var imgs = def.Elements.Where(e => e.Type == "image").OrderByDescending(e => e.W * e.H).ToList();
                if (imgs.Count == 0) return;
                var reducible = imgs.FirstOrDefault(e => e.MaxColors > 2);
                if (reducible != null && round < 6)
                {
                    foreach (var e in imgs) e.MaxColors = Math.Max(2, e.MaxColors - 2);
                    continue;
                }
                var drop = imgs[0];
                def.Elements.Remove(drop);
                report.Notes.Add($"image \"{drop.Name}\" left out: the dash would take ~{seconds:0} s to draw");
            }
        }

        private sealed class Context
        {
            private readonly string path, folder;
            private readonly ImportOptions opt;
            public readonly ImportReport Report = new ImportReport();
            private readonly DashDefinition def = new DashDefinition();
            private double s, ox, oy;
            private readonly Dictionary<string, ZipArchive> zips = new Dictionary<string, ZipArchive>(StringComparer.OrdinalIgnoreCase);
            private int counter;

            public Context(string path, ImportOptions opt) { this.path = path; this.opt = opt; folder = Path.GetDirectoryName(path); }

            public DashDefinition Run()
            {
                try
                {
                    var d = Load(path);
                    string name = Path.GetFileNameWithoutExtension(path);
                    double bw = (double?)d["BaseWidth"] ?? 1280, bh = (double?)d["BaseHeight"] ?? 720;
                    int fw = Math.Max(100, Math.Min(DashRenderer.Width, opt.FitWidth)), fh = Math.Max(100, Math.Min(DashRenderer.Height, opt.FitHeight));
                    s = Math.Min(fw / bw, fh / bh);
                    ox = (fw - bw * s) / 2; oy = (fh - bh * s) / 2;
                    var screens = (d["Screens"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                    Report.Dash = name;
                    Report.Screens = screens.Select((x, i) => (string)x["Name"] ?? ("Screen " + (i + 1))).ToList();
                    var main = PickScreen(screens);
                    if (main == null) { Report.Note("the dash has no screens"); return def; }
                    Report.Screen = (string)main["Name"] ?? "Screen " + (screens.IndexOf(main) + 1);
                    def.Id = "simhub-" + Slug(name) + (screens.Count > 1 ? "-" + Slug(Report.Screen) : "");
                    def.Name = name + (screens.Count(x => !(bool?)x["IsOverlayLayer"] ?? true) > 1 ? " - " + Report.Screen : "");
                    def.Source = "SimHub dash \"" + name + "\" / " + Report.Screen;
                    def.Description = $"Imported from SimHub ({bw:0}x{bh:0}, scaled {s:0.###}).";
                    var js = Path.Combine(folder, "JavascriptExtensions");
                    if (Directory.Exists(js)) def.ScriptsFolder = js;

                    // Dash background, then background-layer screens, the screen, foreground-layer screens
                    var bg = Color((string)d["BackgroundColor"]);
                    if (bg.HasValue && bg.Value.A > 0 && (bg.Value.R | bg.Value.G | bg.Value.B) != 0)
                        Add(new DashElement { Type = "rect", Name = "background", X = 0, Y = 0, W = DashRenderer.Width, H = DashRenderer.Height, Color = Hex(bg.Value) });
                    var frame = new Frame { Scale = 1 };
                    foreach (var layer in screens.Where(x => (bool?)x["IsBackgroundLayer"] == true && x != main)) Screen(layer, frame, true);
                    Screen(main, frame, false);
                    foreach (var layer in screens.Where(x => (bool?)x["IsForegroundLayer"] == true && (bool?)x["IsOverlayLayer"] != true && x != main)) Screen(layer, frame, true);
                    MarkOverlays();
                    int overlays = screens.Count(x => (bool?)x["IsOverlayLayer"] == true);
                    if (overlays > 0) Report.Note($"{overlays} overlay screen(s) not imported (pop-ups shown over the dash in SimHub)");
                }
                finally { foreach (var z in zips.Values) z.Dispose(); }
                return def;
            }

            /// <summary>
            /// Pop-ups inside a screen (flag banners, pit or engine warnings) are a condition shared by a filled shape and
            /// the text on it: every element with such a condition starts hidden in previews (live, SimHub decides).
            /// </summary>
            private void MarkOverlays()
            {
                bool Filled(DashElement e) =>
                    (e.Type == "rect" || e.Type == "image" || e.Type == "gradient" || (e.Type == "box" && e.Fill != null) || (e.Type == "ellipse" && (e.Border == 0 || e.Fill != null)))
                    && e.W * e.H >= 0.03 * DashRenderer.Width * DashRenderer.Height;
                var overlayConditions = new HashSet<string>(def.Elements.Where(e => e.Visible != null && Filled(e)).SelectMany(e => e.Visible));
                int hidden = 0;
                foreach (var e in def.Elements)
                    if (e.Visible != null && e.Visible.Any(overlayConditions.Contains) && e.PreviewVisible != false) { e.PreviewVisible = false; hidden++; }
                if (hidden > 0) Report.Note($"{hidden} elements of pop-ups/warnings hidden in previews (they show when SimHub's conditions say so)");
            }

            private JObject PickScreen(List<JObject> screens)
            {
                if (opt.Screen != null)
                {
                    if (int.TryParse(opt.Screen, out var i) && i >= 0 && i < screens.Count) return screens[i];
                    var byName = screens.FirstOrDefault(x => string.Equals((string)x["Name"], opt.Screen, StringComparison.OrdinalIgnoreCase));
                    if (byName != null) return byName;
                }
                bool Layer(JObject x) => (bool?)x["IsOverlayLayer"] == true || (bool?)x["IsBackgroundLayer"] == true || (bool?)x["IsForegroundLayer"] == true;
                int Size(JObject x) => (x["Items"] as JArray)?.Count ?? 0;
                var candidates = screens.Where(x => !Layer(x)).ToList();
                var inGame = candidates.Where(x => (bool?)x["InGameScreen"] == true).ToList();
                if (inGame.Count == 0) inGame = candidates;
                // a screen called Main/Race wins; else the in-game screen with the most on it (not a splash/logo screen)
                return inGame.FirstOrDefault(x => Regex.IsMatch((string)x["Name"] ?? "", @"^(main|race|dash)\b", RegexOptions.IgnoreCase))
                    ?? inGame.OrderByDescending(Size).FirstOrDefault() ?? screens.FirstOrDefault();
            }

            private void Screen(JObject screen, Frame frame, bool isLayer)
            {
                var bg = Color((string)screen["BackgroundColor"]);
                if (!isLayer && bg.HasValue && bg.Value.A > 0 && (bg.Value.R | bg.Value.G | bg.Value.B) != 0)
                    Add(new DashElement { Type = "rect", Name = "screen background", X = 0, Y = 0, W = DashRenderer.Width, H = DashRenderer.Height, Color = Hex(bg.Value) });
                Items(screen["Items"] as JArray, frame);
            }

            /// <summary>Where source coordinates land: (x, y) in the parent's space * Scale + (OX, OY), conditions to AND.</summary>
            private sealed class Frame
            {
                public double OX, OY, Scale = 1;
                public List<string> Visible = new List<string>();
                public string Folder;
                public Frame With(double ox, double oy, double scale, IEnumerable<string> cond, string folder = null) =>
                    new Frame { OX = ox, OY = oy, Scale = scale, Visible = Visible.Concat(cond).ToList(), Folder = folder ?? Folder };
            }

            private void Items(JArray items, Frame f)
            {
                if (items == null) return;
                foreach (var it in items.OfType<JObject>()) Item(it, f);
            }

            private void Item(JObject it, Frame f)
            {
                string type = TypeName(it);
                var binds = it["Bindings"] as JObject;
                string visBind = Formula(binds, "Visible");
                bool visible = (bool?)it["Visible"] ?? true;
                if (!visible && visBind == null) return; // hidden in the dash, no condition to show it
                var cond = visBind == null ? new List<string>() : new List<string> { visBind };

                switch (type)
                {
                    case "Layer":
                    case "GroupItem":
                        Background(it, f, cond);
                        Items((it["Childrens"] ?? it["Items"]) as JArray, f.With(f.OX, f.OY, f.Scale, cond));
                        return;
                    case "WidgetItem":
                        Widget(it, f, cond, binds);
                        return;
                    case "RectangleItem":
                        Rectangle(it, f, cond, binds);
                        return;
                    case "EllipseItem":
                        Ellipse(it, f, cond, binds);
                        return;
                    case "GradientItem":
                        Gradient(it, f, cond);
                        return;
                    case "ImageItem":
                        Image(it, f, cond);
                        return;
                    case "LinearGaugeItem":
                        Gauge(it, f, cond, binds);
                        return;
                }
                if ((bool?)it["IsTextItem"] == true || type.EndsWith("Text") || type.EndsWith("LapTime"))
                {
                    Text(it, type, f, cond, binds);
                    return;
                }
                Report.Skip(type);
            }

            // ---------- geometry and colours ----------

            private Rectangle Box(JObject it, Frame f)
            {
                double l = (double?)it["Left"] ?? 0, t = (double?)it["Top"] ?? 0, w = (double?)it["Width"] ?? 0, h = (double?)it["Height"] ?? 0;
                double x0 = ox + (f.OX + l * f.Scale) * s, y0 = oy + (f.OY + t * f.Scale) * s;
                double x1 = ox + (f.OX + (l + w) * f.Scale) * s, y1 = oy + (f.OY + (t + h) * f.Scale) * s;
                return System.Drawing.Rectangle.FromLTRB((int)Math.Round(x0), (int)Math.Round(y0), (int)Math.Round(x1), (int)Math.Round(y1));
            }

            private double Px(double v, Frame f) => v * f.Scale * s;

            private static Color? Color(string hex) => DashColors.TryParse(hex, out var c) ? c : (Color?)null;
            private static string Hex(Color c) => DashColors.Hex(c);

            private static int Opacity(JObject it) => (int)Math.Round((double?)it["Opacity"] ?? 100);

            /// <summary>
            /// Conditional elements that look like overlays (big, or flashing on a change) start hidden in previews, where
            /// SimHub formulas can't be evaluated; everything else shows. Only a preview default: live, SimHub decides.
            /// </summary>
            private DashElement Add(DashElement e)
            {
                if (e.Visible != null && e.Visible.Count > 0)
                {
                    bool big = e.W * e.H >= 0.4 * DashRenderer.Width * DashRenderer.Height;
                    bool flash = e.Visible.Any(c => c.IndexOf("changed(", StringComparison.OrdinalIgnoreCase) >= 0 || c.IndexOf("blink", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (big || flash) e.PreviewVisible = false;
                }
                if (e.Name == null) e.Name = e.Type + (++counter);
                def.Elements.Add(e);
                return e;
            }

            private static List<string> Cond(List<string> frame, List<string> own) => frame.Concat(own).ToList() is var l && l.Count > 0 ? l : null;

            /// <summary>A background colour on a layer/group: a rect under its children.</summary>
            private void Background(JObject it, Frame f, List<string> cond)
            {
                var c = Color((string)it["BackgroundColor"]);
                if (c == null || c.Value.A == 0) return;
                var r = Box(it, f);
                if (r.Width <= 0 || r.Height <= 0) return;
                Add(new DashElement { Type = "rect", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Color = Hex(c.Value), Opacity = Opacity(it), Visible = Cond(f.Visible, cond) });
                Report.Converted++;
            }

            // ---------- items ----------

            private void Rectangle(JObject it, Frame f, List<string> cond, JObject binds)
            {
                var r = Box(it, f);
                var fill = Color((string)it["BackgroundColor"]);
                var bs = it["BorderStyle"] as JObject;
                int border = bs == null ? 0 : (int)Math.Round(Px(new[] { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" }.Average(k => (double?)bs[k] ?? 0), f));
                int radius = bs == null ? 0 : (int)Math.Round(Px(new[] { "RadiusTopLeft", "RadiusTopRight", "RadiusBottomLeft", "RadiusBottomRight" }.Average(k => (double?)bs[k] ?? 0), f));
                var borderColour = bs == null ? null : Color((string)bs["BorderColor"]);
                string colourBind = Formula(binds, "BackgroundColor"); var stops = Stops(binds, "BackgroundColor");
                bool hasFill = (fill.HasValue && fill.Value.A > 0) || colourBind != null;
                bool hasBorder = border > 0 && borderColour.HasValue && borderColour.Value.A > 0;
                if (!hasFill && !hasBorder) { Report.Skip("RectangleItem (transparent)"); return; }
                var e = new DashElement
                {
                    Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Opacity = Opacity(it),
                    Visible = Cond(f.Visible, cond), ColorBind = colourBind, ColorStops = stops, Radius = radius,
                };
                if (hasBorder || radius > 0)
                {
                    e.Type = "box"; e.Border = hasBorder ? Math.Max(1, border) : 0;
                    e.Color = hasBorder ? Hex(borderColour.Value) : Hex(fill ?? System.Drawing.Color.Black);
                    e.Fill = hasFill ? Hex(fill ?? System.Drawing.Color.Black) : null;
                    if (!hasBorder) { e.Border = 0; }
                }
                else { e.Type = "rect"; e.Color = Hex(fill ?? System.Drawing.Color.Black); }
                Add(e);
                Report.Converted++;
            }

            private void Ellipse(JObject it, Frame f, List<string> cond, JObject binds)
            {
                var r = Box(it, f);
                var rim = Color((string)it["EllipseColor"]);
                var fill = Color((string)it["FillColor"]);
                int thickness = (int)Math.Round(Px((double?)it["EllipseThickness"] ?? 0, f));
                bool hasRim = thickness > 0 && rim.HasValue && rim.Value.A > 0, hasFill = fill.HasValue && fill.Value.A > 0;
                string colourBind = Formula(binds, "FillColor") ?? Formula(binds, "EllipseColor");
                if (!hasRim && !hasFill && colourBind == null) { Report.Skip("EllipseItem (transparent)"); return; }
                Add(new DashElement
                {
                    Type = "ellipse", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Opacity = Opacity(it),
                    Color = hasRim ? Hex(rim.Value) : Hex(fill ?? System.Drawing.Color.White), Border = hasRim ? Math.Max(1, thickness) : 0,
                    Fill = hasRim && hasFill ? Hex(fill.Value) : null, Visible = Cond(f.Visible, cond),
                    ColorBind = colourBind, ColorStops = Stops(binds, Formula(binds, "FillColor") != null ? "FillColor" : "EllipseColor"),
                });
                Report.Converted++;
            }

            private void Gradient(JObject it, Frame f, List<string> cond)
            {
                var r = Box(it, f);
                var brush = it["Color"]?["LinearGradientBrush"] as JObject;
                var colours = new List<string>();
                double angle = 90;
                if (brush != null)
                {
                    var stopsToken = brush.SelectTokens("..GradientStop")
                        .SelectMany(t => t is JArray a ? a.Children() : (IEnumerable<JToken>)new[] { t }).OfType<JObject>().ToList();
                    colours = stopsToken.OrderBy(t => (double?)t["@Offset"] ?? 0)
                        .Select(t => Color((string)t["@Color"])).Where(c => c.HasValue).Select(c => Hex(c.Value)).ToList();
                    var sp = ParsePoint((string)brush["@StartPoint"]) ?? new PointF(0.5f, 0); var ep = ParsePoint((string)brush["@EndPoint"]) ?? new PointF(0.5f, 1);
                    angle = Math.Atan2((ep.Y - sp.Y) * r.Height, (ep.X - sp.X) * r.Width) * 180 / Math.PI;
                }
                if (colours.Count == 0) { Report.Skip("GradientItem", "gradients without readable stops left out"); return; }
                var bs = it["BorderStyle"] as JObject;
                Add(new DashElement
                {
                    Type = "gradient", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Colors = colours, Angle = angle,
                    Opacity = Opacity(it), Visible = Cond(f.Visible, cond),
                    Border = bs == null ? 0 : (int)Math.Round(Px((double?)bs["BorderTop"] ?? 0, f)),
                    Radius = bs == null ? 0 : (int)Math.Round(Px((double?)bs["RadiusTopLeft"] ?? 0, f)),
                    Color = bs == null ? "#808080" : Hex(Color((string)bs["BorderColor"]) ?? System.Drawing.Color.Gray),
                });
                Report.Approximated++;
            }

            private static PointF? ParsePoint(string p)
            {
                var parts = (p ?? "").Split(',');
                if (parts.Length != 2) return null;
                return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                       float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? new PointF(x, y) : (PointF?)null;
            }

            private void Image(JObject it, Frame f, List<string> cond)
            {
                if (!opt.Images) { Report.Skip("ImageItem", "images left out (import option)"); return; }
                var r = Box(it, f);
                var name = (string)it["Image"];
                var clip = System.Drawing.Rectangle.Intersect(r, new System.Drawing.Rectangle(0, 0, DashRenderer.Width, DashRenderer.Height));
                if (string.IsNullOrEmpty(name) || name == "None" || clip.Width <= 0 || clip.Height <= 0) { Report.Skip("ImageItem (empty or off screen)"); return; }
                var png = ImageBytes(name, f.Folder);
                if (png == null) { Report.Skip("ImageItem", $"image \"{name}\" not found in the dash's resources"); return; }
                // Stored at the size it's drawn (keeps the dash file small)
                string key;
                try
                {
                    using (var ms = new MemoryStream(png))
                    using (var src = new Bitmap(ms))
                    using (var dst = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(dst))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, new System.Drawing.Rectangle(0, 0, dst.Width, dst.Height));
                        }
                        using (var o = new MemoryStream())
                        {
                            dst.Save(o, ImageFormat.Png);
                            key = name + "@" + dst.Width + "x" + dst.Height;
                            if (def.Images == null) def.Images = new Dictionary<string, string>();
                            def.Images[key] = Convert.ToBase64String(o.ToArray());
                        }
                    }
                }
                catch { Report.Skip("ImageItem", $"image \"{name}\" couldn't be read"); return; }
                Add(new DashElement
                {
                    Type = "image", Name = (string)it["Name"] ?? name, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Image = key,
                    MaxColors = opt.ImageColors, Opacity = Opacity(it), Visible = Cond(f.Visible, cond),
                });
                Report.Approximated++;
            }

            private byte[] ImageBytes(string name, string widgetFolderFile)
            {
                if (name.StartsWith("library:", StringComparison.OrdinalIgnoreCase))
                {
                    // SimHub's shared images: <SimHub>\ImageLibrary\<path>
                    var file = Path.Combine(DashLibrary.SimHubFolder, "ImageLibrary", name.Substring(8).TrimStart('\\', '/'));
                    try { return File.Exists(file) ? File.ReadAllBytes(file) : null; } catch { return null; }
                }
                foreach (var file in new[] { widgetFolderFile, path }.Where(x => x != null))
                {
                    var res = file + ".ressources";
                    if (!File.Exists(res)) continue;
                    try
                    {
                        if (!zips.TryGetValue(res, out var zip)) zips[res] = zip = ZipFile.OpenRead(res);
                        var entry = zip.Entries.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Name), name, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        using (var st = entry.Open()) using (var ms = new MemoryStream()) { st.CopyTo(ms); return ms.ToArray(); }
                    }
                    catch { }
                }
                return null;
            }

            private void Gauge(JObject it, Frame f, List<string> cond, JObject binds)
            {
                var r = Box(it, f);
                var value = Formula(binds, "Value");
                if (value == null) { Report.Skip("LinearGaugeItem (no value binding)"); return; }
                var colour = Color((string)it["GaugeColor"]) ?? System.Drawing.Color.Lime;
                var back = Color((string)it["BackgroundColor"]);
                int orientation = (int?)it["GaugeOrientation"] ?? 0, alignment = (int?)it["GaugeAlignment"] ?? 0;
                Add(new DashElement
                {
                    Type = "bar", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Bind = value,
                    Min = (double?)it["Minimum"] ?? 0, Max = (double?)it["Maximum"] ?? 100,
                    Orientation = orientation == 1 ? "vertical" : "horizontal", Reverse = alignment == 2,
                    Color = Hex(colour), Fill = back.HasValue && back.Value.A > 0 ? Hex(back.Value) : null,
                    ColorBind = Formula(binds, "GaugeColor"), ColorStops = Stops(binds, "GaugeColor"),
                    Opacity = Opacity(it), Visible = Cond(f.Visible, cond),
                });
                if (!string.IsNullOrEmpty((string)it["GaugeImage"]) && (string)it["GaugeImage"] != "None" || !string.IsNullOrEmpty((string)it["BackgroundImage"]) && (string)it["BackgroundImage"] != "None")
                    Report.Approximated++;
                else Report.Converted++;
            }

            private void Widget(JObject it, Frame f, List<string> cond, JObject binds)
            {
                var file = (string)it["FileName"];
                var wpath = file == null ? null : Path.Combine(folder, file);
                if (wpath == null || !File.Exists(wpath)) { Report.Skip("WidgetItem", $"widget \"{file}\" not found"); return; }
                JObject w;
                try { w = Load(wpath); } catch { Report.Skip("WidgetItem", $"widget \"{file}\" couldn't be read"); return; }
                double bw = (double?)w["BaseWidth"] ?? 1, width = (double?)it["Width"] ?? bw;
                double scale = f.Scale * (width / Math.Max(1, bw));
                double l = (double?)it["Left"] ?? 0, t = (double?)it["Top"] ?? 0;
                var inner = f.With(f.OX + l * f.Scale, f.OY + t * f.Scale, scale, cond, wpath);
                var screens = (w["Screens"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                if (screens.Count == 0) return;
                int initial = (int?)it["InitialScreenIndex"] ?? 0;
                var screenBind = Formula(binds, "InitialScreenIndex");
                if (screenBind != null && screens.Count > 1)
                {
                    // screen chosen by a formula: every screen, each shown while the formula gives its index
                    for (int i = 0; i < screens.Count; i++)
                        Items(screens[i]["Items"] as JArray, inner.With(inner.OX, inner.OY, inner.Scale, new[] { Equals(screenBind, i) }));
                    Report.Note($"widget \"{file}\": {screens.Count} screens, switched by its formula");
                }
                else Items(screens[Math.Max(0, Math.Min(screens.Count - 1, initial))]["Items"] as JArray, inner);
                Report.Converted++;
            }

            /// <summary>A condition "formula == index" in the formula's own language.</summary>
            private static string Equals(string formula, int index)
            {
                if (formula.StartsWith("js:")) return "js:return ((function(){ " + formula.Substring(3) + " })()) == " + index + ";";
                if (formula.StartsWith("ncalc:")) return "ncalc:(" + formula.Substring(6) + ") = " + index;
                return formula;
            }

            private void Text(JObject it, string type, Frame f, List<string> cond, JObject binds)
            {
                var r = Box(it, f);
                if (r.Width <= 0 || r.Height <= 0) { Report.Skip(type + " (empty box)"); return; }
                double size = (double?)it["FontSize"] ?? 20;
                var colour = Color((string)(it["TextColor"] ?? it["GearTextColor"])) ?? System.Drawing.Color.White;
                var back = Color((string)it["BackgroundColor"]);
                var bs = it["BorderStyle"] as JObject;
                int border = bs == null ? 0 : (int)Math.Round(Px(new[] { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" }.Average(k => (double?)bs[k] ?? 0), f));
                var borderColour = bs == null ? null : Color((string)bs["BorderColor"]);
                bool framed = border > 0 && borderColour.HasValue && borderColour.Value.A > 0;
                if (framed || (back.HasValue && back.Value.A > 0))
                {
                    int radius = bs == null ? 0 : (int)Math.Round(Px(new[] { "RadiusTopLeft", "RadiusTopRight", "RadiusBottomLeft", "RadiusBottomRight" }.Average(k => (double?)bs[k] ?? 0), f));
                    Add(new DashElement
                    {
                        Type = framed || radius > 0 ? "box" : "rect", Name = ((string)it["Name"] ?? type) + (framed ? " frame" : " background"),
                        X = r.X, Y = r.Y, W = r.Width, H = r.Height, Radius = radius, Border = framed ? Math.Max(1, border) : 0,
                        Color = framed ? Hex(borderColour.Value) : Hex(back.Value), Fill = back.HasValue && back.Value.A > 0 ? Hex(back.Value) : null,
                        Opacity = Opacity(it), Visible = Cond(f.Visible, cond),
                    });
                    Report.Converted++;
                }
                int align = (int?)it["HorizontalAlignment"] ?? 0;
                string sample = (string)it["Text"] ?? (string)it["DesignerText"] ?? (string)it["NoDataText"] ?? "";
                string textBind = Formula(binds, "Text");
                string format = "text";
                var fs = (string)binds?["Text"]?["FormatString"];
                if (textBind == null) BuiltIn(type, ref textBind, ref format, ref sample);
                else if (!string.IsNullOrEmpty(fs)) format = TimeSpanFormat(fs) ? "time:" + fs : fs;

                double em = Px(size, f);
                int font = DashFonts.Pick(em, r.Width, Math.Max(r.Height, (int)Math.Ceiling(em * 1.2)), Clean(sample).Length > 0 ? Clean(sample) : "0");
                if (font < 0 && Clean(sample).Length == 0) font = DashFonts.Smallest;
                if (font < 0) { font = DashFonts.Smallest; Report.Note($"text \"{Clean(sample)}\" doesn't fit its box in any screen font (fix its box in the designer)"); }
                int fh = DashRenderer.FontHeight(font);
                var box = r;
                if (box.Height < fh) { box.Y -= (fh - box.Height) / 2; box.Height = fh; } // the screen needs the whole line
                box.Y = Math.Max(0, Math.Min(DashRenderer.Height - box.Height, box.Y));
                box.X = Math.Max(0, Math.Min(DashRenderer.Width - box.Width, box.X));
                var e = new DashElement
                {
                    Name = (string)it["Name"] ?? type, X = box.X, Y = box.Y, W = box.Width, H = box.Height, Font = font,
                    Color = Hex(colour), Align = align == 1 ? "center" : align == 2 ? "right" : "left",
                    Visible = Cond(f.Visible, cond), ColorBind = Formula(binds, "TextColor"), ColorStops = Stops(binds, "TextColor"),
                };
                if (textBind == null)
                {
                    if (string.IsNullOrWhiteSpace(sample)) { if (!framed) Report.Skip(type + " (no text)"); return; }
                    e.Type = "label"; e.Text = Clean(sample);
                }
                else
                {
                    e.Type = "value"; e.Bind = textBind; e.Format = format; e.Empty = "";
                    e.PreviewText = Clean(sample);
                    e.Samples = string.IsNullOrEmpty(e.PreviewText) ? null : new[] { e.PreviewText };
                }
                Add(e);
                if (DashRenderer.TextWidth(font, Clean(sample)) >= 0) Report.Converted++; else Report.Approximated++;
            }

            /// <summary>SimHub's built-in text items (no formula of their own) as bindings.</summary>
            private void BuiltIn(string type, ref string bind, ref string format, ref string sample)
            {
                switch (type)
                {
                    case "GearText": bind = "gearText"; format = "text"; if (sample == "") sample = "N"; break;
                    case "SpeedText": bind = "speed"; format = "0"; if (sample == "") sample = "288"; break;
                    case "RPMText": bind = "rpm"; format = "0"; if (sample == "") sample = "8888"; break;
                    case "CurrentLapTime": bind = "currentLapTime"; format = "laptime"; if (sample == "") sample = "1:23.456"; break;
                    case "LastLapTime": bind = "lastLapTime"; format = "laptime"; if (sample == "") sample = "1:23.456"; break;
                    case "BestLapTime": bind = "bestLapTime"; format = "laptime"; if (sample == "") sample = "1:23.456"; break;
                    case "LiveDeltaToBestText": bind = "delta"; format = "delta"; if (sample == "") sample = "+0.00"; break;
                    case "FuelText": bind = "fuel"; format = "0.0"; if (sample == "") sample = "88.8"; break;
                    case "FuelLastLapText": bind = "fuelLastLap"; format = "0.00"; if (sample == "") sample = "2.50"; break;
                    case "FuelRemainingLapsText": bind = "fuelRemainingLaps"; format = "0.0"; if (sample == "") sample = "12.3"; break;
                    case "FuelLiterPerLapsText": bind = "prop:DataCorePlugin.Computed.Fuel_LitersPerLap"; format = "0.00"; if (sample == "") sample = "2.50"; break;
                    case "TimeText": bind = "ncalc:format([DataCorePlugin.CurrentDateTime], 'HH:mm')"; format = "text"; if (sample == "") sample = "12:34"; break;
                    default:
                        if (sample != "" && type != "TextItem") Report.Note($"{type}: no data mapping yet, imported as fixed text");
                        break;
                }
            }

            private static bool TimeSpanFormat(string fs) => fs.Contains(@"\:") || fs.Contains("mm") || fs.Contains("ss") || fs.Contains("fff");

            // ---------- bindings ----------

            /// <summary>A binding as "ncalc:..." / "js:...", or null.</summary>
            private string Formula(JObject binds, string prop)
            {
                var b = binds?[prop] as JObject;
                if (b == null) return null;
                var mode = (int?)b["Mode"] ?? 2;
                var fo = b["Formula"] as JObject;
                if (fo == null) return null;
                var expr = (string)fo["Expression"];
                if (string.IsNullOrWhiteSpace(expr)) return null;
                bool js = (int?)fo["Interpreter"] == 1;
                if (mode != 2 && mode != 4 && mode != 1) return null;
                if (js) Report.JsFormulas++; else Report.NcalcFormulas++;
                return (js ? "js:" : "ncalc:") + expr.Trim();
            }

            /// <summary>SimHub colour gradients (binding mode 4): the value's colour between start/middle/end colours.</summary>
            private static List<ColorStop> Stops(JObject binds, string prop)
            {
                var b = binds?[prop] as JObject;
                if (b == null || (int?)b["Mode"] != 4) return null;
                var list = new List<ColorStop>
                {
                    new ColorStop { Value = (double?)b["StartColorValue"] ?? 0, Color = (string)b["StartColor"] ?? "#FF00FF00" },
                    new ColorStop { Value = (double?)b["EndColorValue"] ?? 100, Color = (string)b["EndColor"] ?? "#FFFF0000" },
                };
                if ((bool?)b["EnableMiddleColor"] == true)
                    list.Insert(1, new ColorStop { Value = (double?)b["MiddleColorValue"] ?? 50, Color = (string)b["MiddleColor"] ?? "#FFFFFF00" });
                return list;
            }

            private static string TypeName(JObject it) => ((string)it["$type"] ?? "").Split(',')[0].Split('.').Last();

            private static string Clean(string s) => new string((s ?? "").Where(c => c >= 32 && c < 127 && c != '"').ToArray()).Trim();

            private static string Slug(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }
    }
}
