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
            /// <summary>
            /// Widgets whose screens the driver flips (SimHub's next / previous screen commands), each a set of the dash's
            /// pages; widgets on the same commands with as many screens share one (SimHub flips them together). Their
            /// elements carry a placeholder condition until NumberPageSets numbers the sets by their commands.
            /// </summary>
            private readonly List<Pager> pagers = new List<Pager>();
            private sealed class Pager { public int Next, Prev, Count; public string Name; public List<string> Names; }
            private const string PagerMark = "\u0001pager:";

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
                    // overlay screens: SimHub shows them over the dash while their trigger holds (ignition off, a start-up
                    // splash): drawn last, each shown while its trigger is true, hidden in previews
                    int overlays = 0, skippedOverlays = 0;
                    foreach (var layer in screens.Where(x => (bool?)x["IsOverlayLayer"] == true && x != main))
                    {
                        var trigger = ((string)layer["OverlayTriggerExpression"]?["Expression"] ?? "").Trim();
                        if (trigger.Length == 0) { skippedOverlays++; continue; }
                        var cond = "ncalc:" + trigger;
                        int from = def.Elements.Count;
                        Screen(layer, frame.With(0, 0, 1, new[] { cond }), true);
                        // a full-screen overlay hides what's under it: give it the screen's background so values under it
                        // stop drawing (they're covered) and come back in one repaint when it goes
                        var lbg = Color((string)layer["BackgroundColor"]) ?? Color((string)d["BackgroundColor"]) ?? System.Drawing.Color.Black;
                        if (lbg.A > 0 && def.Elements.Count > from)
                        {
                            var back = new DashElement { Type = "rect", Name = ((string)layer["Name"] ?? "overlay") + " background", X = 0, Y = 0, W = DashRenderer.Width, H = DashRenderer.Height, Color = Hex(System.Drawing.Color.FromArgb(255, lbg)), Visible = new List<string> { cond }, PreviewVisible = false };
                            def.Elements.Insert(from, back);
                        }
                        for (int k = from; k < def.Elements.Count; k++) def.Elements[k].PreviewVisible = false;
                        overlays++;
                    }
                    NumberPageSets();
                    Dims();
                    MarkOverlays();
                    Declutter();
                    if (overlays > 0) Report.Note($"{overlays} overlay screen(s) imported, each shown while its SimHub trigger holds (hidden in previews)");
                    if (skippedOverlays > 0) Report.Note($"{skippedOverlays} overlay screen(s) without a trigger not imported");
                }
                finally { foreach (var z in zips.Values) z.Dispose(); }
                return def;
            }

            /// <summary>
            /// A see-through black layer over (nearly) the whole dash, shown on a condition (SimHub dashes darken themselves
            /// with headlights on this way): a "dim" element instead, which lowers the screen's backlight. Drawn as a shape,
            /// every value under it would have to draw it again at each change (flashing, and many times the traffic).
            /// </summary>
            private void Dims()
            {
                for (int i = 0; i < def.Elements.Count; i++)
                {
                    var e = def.Elements[i];
                    if (e.Type != "rect" || e.Visible == null || e.Visible.Count == 0 || e.Opacity <= 0 || e.Opacity >= 100 || !string.IsNullOrEmpty(e.ColorBind)) continue;
                    var c = DashColors.Parse(e.Color, System.Drawing.Color.White);
                    if (c.R > 16 || c.G > 16 || c.B > 16) continue;
                    if (e.W * e.H < 0.85 * opt.FitWidth * opt.FitHeight) continue;
                    def.Elements[i] = new DashElement { Type = "dim", Name = e.Name, Opacity = e.Opacity, Visible = e.Visible, PreviewVisible = false };
                    Report.Note($"\"{e.Name}\" (a see-through black layer over the dash, {e.Opacity}%) imported as a dim of the screen's backlight while it shows");
                }
            }

            /// <summary>
            /// Pop-ups inside a screen (flag banners, pit or engine warnings) are a condition shared by a filled shape and
            /// the text on it: every element with such a condition starts hidden in previews (live, SimHub decides).
            /// </summary>
            private void MarkOverlays()
            {
                bool Filled(DashElement e) =>
                    (e.Type == "rect" || e.Type == "image" || e.Type == "gradient" || (e.Type == "box" && e.Fill != null) || (e.Type == "ellipse" && (e.Border == 0 || e.Fill != null)))
                    && e.W * e.H >= 0.015 * DashRenderer.Width * DashRenderer.Height;
                var overlayConditions = new HashSet<string>(def.Elements.Where(e => e.Visible != null && Filled(e)).SelectMany(e => e.Visible));
                int hidden = 0;
                foreach (var e in def.Elements)
                    if (e.Visible != null && e.Visible.Any(overlayConditions.Contains) && e.PreviewVisible != false) { e.PreviewVisible = false; hidden++; }
                if (hidden > 0) Report.Note($"{hidden} elements of pop-ups/warnings hidden in previews (they show when SimHub's conditions say so)");
            }

            /// <summary>
            /// SimHub's fonts are narrower than the screen's, so a label and its value that share a row in SimHub can collide
            /// here, and a value's background would erase the label on the wheel. For text shown together on one row whose
            /// texts overlap: step the bigger font down until they don't, then trim the value's box off the other text.
            /// </summary>
            private void Declutter()
            {
                var texts = def.Elements.Where(e => (e.Type == "label" || e.Type == "value") && e.PreviewVisible != false).ToList();
                int fixedPairs = 0;
                for (int i = 0; i < texts.Count; i++)
                    for (int j = i + 1; j < texts.Count; j++)
                    {
                        var a = texts[i]; var b = texts[j];
                        if (!SameRow(a, b)) continue;
                        bool changed = false;
                        for (int step = 0; step < 8 && Collide(a, b); step++)
                        {
                            var big = DashRenderer.FontHeight(a.Font) >= DashRenderer.FontHeight(b.Font) ? a : b;
                            if (!Smaller(big) && !Smaller(big == a ? b : a)) break;
                            changed = true;
                        }
                        foreach (var (v, other) in new[] { (a, b), (b, a) })
                            if (v.Type == "value" && Extent(other) is var o && o.Width > 0) changed |= Trim(v, o);
                        if (changed) fixedPairs++;
                    }
                if (fixedPairs > 0) Report.Note($"{fixedPairs} label/value pairs sharing a row made smaller or narrower so they don't collide");
            }

            private static bool SameRow(DashElement a, DashElement b)
            {
                int top = Math.Max(a.Y, b.Y), bottom = Math.Min(a.Y + a.H, b.Y + b.H);
                return bottom - top >= 0.5 * Math.Min(a.H, b.H) && a.X < b.X + b.W && b.X < a.X + a.W;
            }

            private static string Widest(DashElement e) =>
                e.Type == "label" ? e.Text ?? "" :
                new[] { e.PreviewText }.Concat(e.Samples ?? new string[0]).Where(x => !string.IsNullOrEmpty(x))
                    .OrderByDescending(x => DashRenderer.TextWidth(e.Font, x)).FirstOrDefault() ?? "";

            /// <summary>Where the text itself is drawn (x range; y of the box).</summary>
            private static System.Drawing.Rectangle Extent(DashElement e)
            {
                int w = Math.Max(0, DashRenderer.TextWidth(e.Font, Widest(e)));
                int x = e.Align == "center" ? e.X + (e.W - w) / 2 : e.Align == "right" ? e.X + e.W - w : e.X;
                return new System.Drawing.Rectangle(x, e.Y, w, e.H);
            }

            private static bool Collide(DashElement a, DashElement b)
            {
                var ea = Extent(a); var eb = Extent(b);
                ea.Inflate(2, 0);
                return ea.IntersectsWith(eb);
            }

            /// <summary>The next smaller font that still fits the element's box and text; false if there's none.</summary>
            private static bool Smaller(DashElement e)
            {
                int h = DashRenderer.FontHeight(e.Font);
                int f = DashFonts.Pick(h - 1, e.W, h - 1, Widest(e));
                if (f < 0 || DashRenderer.FontHeight(f) >= h) return false;
                e.Font = f;
                return true;
            }

            /// <summary>Shrinks a value's box so it doesn't cover `other` (a text's extent), keeping its own text inside.</summary>
            private static bool Trim(DashElement v, System.Drawing.Rectangle other)
            {
                var own = Extent(v);
                if (!new System.Drawing.Rectangle(v.X, v.Y, v.W, v.H).IntersectsWith(other)) return false;
                int need = DashRenderer.TextWidth(v.Font, Widest(v));
                if (other.X + other.Width / 2 < own.X + own.Width / 2)
                {
                    int left = other.Right + 4, right = v.X + v.W;
                    if (left <= v.X || right - left < need) return false;
                    v.W = right - left; v.X = left;
                }
                else
                {
                    int right = other.X - 4;
                    if (right >= v.X + v.W || right - v.X < need) return false;
                    v.W = right - v.X;
                }
                return true;
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
                /// <summary>The layers' opacity above (0-1): SimHub draws a layer's children through it.</summary>
                public double Opacity = 1;
                public Frame With(double ox, double oy, double scale, IEnumerable<string> cond, string folder = null, double opacity = 1) =>
                    new Frame { OX = ox, OY = oy, Scale = scale, Visible = Visible.Concat(cond).ToList(), Folder = folder ?? Folder, Opacity = Opacity * opacity };
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
                // SimHub's blinking (an item shown and hidden every BlinkDelay ms while it shows): the same, as a condition
                if ((bool?)it["BlinkEnabled"] == true)
                {
                    double ms = (double?)it["BlinkDelay"] ?? 250; // SimHub's default (it saves the delay only when it differs)
                    string blink = $"blink('{Slug((string)it["Name"] ?? type)}-{++counter}', {ms.ToString("0", CultureInfo.InvariantCulture)}, true)";
                    cond.Add("ncalc:" + ((bool?)it["BlinkPhasisInverted"] == true ? "!" + blink : blink));
                }

                switch (type)
                {
                    case "Layer":
                    case "GroupItem":
                        Background(it, f, cond);
                        Items((it["Childrens"] ?? it["Items"]) as JArray, f.With(f.OX, f.OY, f.Scale, cond, null, Math.Max(0, Math.Min(100, (double?)it["Opacity"] ?? 100)) / 100));
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
                if (type.StartsWith("LeaderboardOpponent") && Leaderboard(it, type) is var lb && lb != null)
                {
                    Text(it, type, f, cond, binds, lb);
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

            private static int Opacity(JObject it, Frame f) => (int)Math.Round(((double?)it["Opacity"] ?? 100) * (f?.Opacity ?? 1));

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
                Add(new DashElement { Type = "rect", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Color = Hex(c.Value), Opacity = Opacity(it, f), Visible = Cond(f.Visible, cond) });
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
                var borderColour = bs == null ? (Color?)null : Color((string)bs["BorderColor"]) ?? System.Drawing.Color.White; // SimHub draws borders white by default
                string colourBind = Formula(binds, "BackgroundColor"); var stops = Stops(binds, "BackgroundColor");
                bool hasFill = (fill.HasValue && fill.Value.A > 0) || colourBind != null;
                bool hasBorder = border > 0 && borderColour.HasValue && borderColour.Value.A > 0;
                if (!hasFill && !hasBorder) { Report.Skip("RectangleItem (transparent)"); return; }
                var e = new DashElement
                {
                    Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Opacity = Opacity(it, f),
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
                    Type = "ellipse", Name = (string)it["Name"], X = r.X, Y = r.Y, W = r.Width, H = r.Height, Opacity = Opacity(it, f),
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
                    Opacity = Opacity(it, f), Visible = Cond(f.Visible, cond),
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
                        using (var attrs = new ImageAttributes())
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            // edges sampled from the picture itself, not from transparency around it: an opaque picture
                            // stays opaque (its border pixels came out semi-transparent, so it no longer counted as
                            // covering what's under it: a full-screen art picture let the values under it update through it)
                            attrs.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(src, new System.Drawing.Rectangle(0, 0, dst.Width, dst.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
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
                    MaxColors = opt.ImageColors, Opacity = Opacity(it, f), Visible = Cond(f.Visible, cond),
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
                    Opacity = Opacity(it, f), Visible = Cond(f.Visible, cond),
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
                // flipped by the driver (SimHub's next / previous screen commands on the widget): the dash's pages
                int next = (int?)it["NextScreenCommand"] ?? 0, prev = (int?)it["PreviousScreenCommand"] ?? 0;
                bool flipped = next != 0 || prev != 0;
                if (screenBind != null && screens.Count > 1)
                {
                    // screen chosen by a formula: every screen, each shown while the formula gives its index
                    for (int i = 0; i < screens.Count; i++)
                        Items(screens[i]["Items"] as JArray, inner.With(inner.OX, inner.OY, inner.Scale, new[] { Equals(screenBind, i) }));
                    Report.Note($"widget \"{file}\": {screens.Count} screens, switched by its formula");
                }
                else if (flipped && screens.Count > 1 && (pagers.Any(x => x.Next == next && x.Prev == prev && x.Count == screens.Count) || pagers.Count < DashPages.MaxSets))
                {
                    // a set of pages per pair of SimHub screen commands; another widget on the same commands with as many
                    // screens follows the same set, as SimHub would flip both
                    var pager = pagers.FirstOrDefault(x => x.Next == next && x.Prev == prev && x.Count == screens.Count);
                    bool first = pager == null;
                    if (first)
                    {
                        pager = new Pager { Next = next, Prev = prev, Count = screens.Count, Name = ((string)it["Name"] ?? Path.GetFileNameWithoutExtension(file)).Trim(), Names = new List<string>() };
                        pagers.Add(pager);
                    }
                    int id = pagers.IndexOf(pager);
                    for (int k = 0; k < screens.Count; k++)
                    {
                        // SimHub's widget screens start at InitialScreenIndex: page 0 is that one, the rest follow in order
                        int i = (initial + k) % screens.Count;
                        if (first) pager.Names.Add(ScreenTitle(screens[i], i));
                        Items(screens[i]["Items"] as JArray, inner.With(inner.OX, inner.OY, inner.Scale, new[] { PagerMark + id + ":" + k }));
                    }
                }
                else
                {
                    if (flipped && screens.Count > 1) Report.Note($"widget \"{file}\": {screens.Count} screens on screen commands past the dash's {DashPages.MaxSets} sets of pages: only its first screen imported");
                    Items(screens[Math.Max(0, Math.Min(screens.Count - 1, initial))]["Items"] as JArray, inner);
                }
                Report.Converted++;
            }

            /// <summary>
            /// The flipped widgets as the dash's sets of pages, numbered by their SimHub screen commands (the lowest first:
            /// set 1 = Next / Previous page, then Next / Previous page 2...): the placeholder conditions become "page:N",
            /// "page2:N"...; Pages and PageSets get the screens' names.
            /// </summary>
            private void NumberPageSets()
            {
                if (pagers.Count == 0) return;
                int Key(Pager x) => new[] { x.Next, x.Prev }.Where(c => c > 0).DefaultIfEmpty(int.MaxValue).Min();
                var order = pagers.OrderBy(Key).ToList();
                foreach (var e in def.Elements)
                    if (e.Visible != null)
                        for (int c = 0; c < e.Visible.Count; c++)
                        {
                            var v = e.Visible[c];
                            if (!v.StartsWith(PagerMark, StringComparison.Ordinal)) continue;
                            var parts = v.Substring(PagerMark.Length).Split(':');
                            int set = order.IndexOf(pagers[int.Parse(parts[0], CultureInfo.InvariantCulture)]);
                            e.Visible[c] = DashPages.Condition(set, int.Parse(parts[1], CultureInfo.InvariantCulture));
                        }
                def.Pages = order[0].Names;
                if (order.Count > 1)
                    def.PageSets = order.Skip(1).Select(x => new DashPageSet { Name = x.Name, Pages = x.Names }).ToList();
                if (order.Count == 1)
                    Report.Note($"widget \"{order[0].Name}\": {order[0].Count} screens flipped by the driver, imported as pages ({string.Join(", ", def.Pages)}): bind Next / Previous page to a wheel button");
                else
                    Report.Note($"{order.Count} widgets flipped by the driver on their own, imported as sets of pages: " +
                                string.Join("; ", order.Select((x, i) => $"{x.Name} ({string.Join(", ", x.Names)}): Next / Previous page{(i == 0 ? "" : " " + (i + 1))}")) +
                                ". Next / Previous page flip them all");
            }

            /// <summary>
            /// A page's name: the widget screen's own name, or (SimHub's default "Screen") what it shows: its top-level
            /// layers' names ("TyreTemp / TyrePres / Braketemps"), shortened.
            /// </summary>
            private static string ScreenTitle(JObject screen, int index)
            {
                var name = ((string)screen["Name"] ?? "").Trim();
                if (name.Length > 0 && !Regex.IsMatch(name, @"^screen\s*\d*$", RegexOptions.IgnoreCase)) return name;
                var layers = (screen["Items"] as JArray ?? new JArray()).OfType<JObject>()
                    .Where(x => TypeName(x) == "Layer" || TypeName(x) == "GroupItem").Select(x => ((string)x["Name"] ?? "").Trim()).Where(x => x.Length > 0).ToList();
                var title = string.Join(" / ", layers);
                if (title.Length == 0) return "Page " + (index + 1);
                return title.Length <= 32 ? title : title.Substring(0, 31) + "…";
            }

            /// <summary>A condition "formula == index" in the formula's own language.</summary>
            private static string Equals(string formula, int index)
            {
                if (formula.StartsWith("js:")) return "js:return ((function(){ " + formula.Substring(3) + " })()) == " + index + ";";
                if (formula.StartsWith("ncalc:")) return "ncalc:(" + formula.Substring(6) + ") = " + index;
                return formula;
            }

            /// <summary>What a SimHub leaderboard item shows, as a formula picking the same driver (SimHub's
            /// OpponentAtPosition: on track = the ahead/behind functions, relative to the player, or an absolute position;
            /// player class only when the item says so).</summary>
            private sealed class LeaderboardText { public string Bind, Format = "text", Empty = "", ColorBind, Sample; }

            private LeaderboardText Leaderboard(JObject it, string type)
            {
                int p = (int?)it["LeaderboardPosition"] ?? 1;
                bool onTrack = (bool?)it["LeaderboardPositionRelativeToPlayerOnTrack"] == true;
                bool relative = (bool?)it["LeaderboardPositionRelativeToPlayer"] == true;
                // LeaderBoardMode: 0 = the user's SimHub setting (overall by default), 1 = full, 2 = player class only
                bool cls = ((int?)it["LeaderboardMode"] ?? 0) == 2;
                string pos = onTrack ? $"getopponentleaderboardposition_aheadbehind{(cls ? "_playerclassonly" : "")}({p - 1})"
                           : relative ? (cls ? $"getopponentleaderboardposition_playerclassonly(driverclassposition(getplayerleaderboardposition()) + {p - 1})" : $"(getplayerleaderboardposition() + {p - 1})")
                           : cls ? $"getopponentleaderboardposition_playerclassonly({p})" : p.ToString(CultureInfo.InvariantCulture);
                var r = new LeaderboardText();
                switch (type)
                {
                    case "LeaderboardOpponentNameText":
                        int style = (int?)it["NameStyle"] ?? 0; // NameMode: Full, Initials, ShortName
                        r.Bind = $"ncalc:{(style == 1 ? "driverinitials" : style == 2 ? "drivershortname" : "drivername")}({pos})";
                        r.Sample = style == 1 ? "ABC" : style == 2 ? "J. Doeson" : "Jonathan Doeson";
                        break;
                    case "LeaderboardOpponentBestLap":
                    case "LeaderboardOpponentLastLap":
                    {
                        string fn = type.EndsWith("BestLap") ? "driverbestlap" : "driverlastlap";
                        string fmt = ((string)it["TimeFormat"] ?? @"m\:ss\.fff").Replace("'", "");
                        r.Empty = (string)it["EmptyTimeText"] ?? "-:---";
                        // (a time of 0, no lap yet: the item's empty text)
                        // (in an NCalc text a backslash escapes the next character: the format's own backslashes doubled)
                        string fmtText = fmt.Replace("\\", "\\\\");
                        r.Bind = $"ncalc:if(timespantoseconds({fn}({pos})) > 0, format({fn}({pos}), '{fmtText}'), '{r.Empty.Replace("'", "")}')";
                        r.Sample = "88:88.888";
                        break;
                    }
                    case "LeaderboardOpponentGap":
                    {
                        int mode = (int?)it["GapMode"] ?? 0; // GapMode: the user's setting (from the leader by default), from leader, from player
                        string fn = onTrack ? "driverrelativegaptoplayer" : mode == 2 ? "drivergaptoplayer" : cls ? "drivergaptoclassleader" : "drivergaptoleader";
                        string num = (string)it["Format"] ?? "0.00";
                        r.Format = (bool?)it["AlwaysAppendSign"] != false ? $"+{num};-{num};{num}" : num; // SimHub signs a gap always
                        r.Empty = (string)it["EmptyValueText"] ?? "-";
                        // (the player's own row has no gap: the item's empty text)
                        r.Bind = $"ncalc:if(driverisplayer({pos}), '{r.Empty.Replace("'", "")}', {fn}({pos}))";
                        r.Sample = "+888.88";
                        break;
                    }
                    case "LeaderboardOpponentPositionText": r.Bind = $"ncalc:driverposition({pos})"; r.Sample = "88"; break;
                    case "LeaderboardOpponentCarNumberText": r.Bind = $"ncalc:drivercarnumber({pos})"; r.Sample = "888"; break;
                    case "LeaderboardOpponentCarClassText": r.Bind = $"ncalc:drivercarclass({pos})"; r.Sample = "HYPERCAR"; break;
                    case "LeaderboardOpponentCarModelText": r.Bind = $"ncalc:drivercarname({pos})"; r.Sample = "Ford Mustang"; break;
                    case "LeaderboardOpponentCurrentLapText": r.Bind = $"ncalc:drivercurrentlap({pos})"; r.Sample = "88"; break;
                    default: return null;
                }
                // the player shown in another colour (PlayerStyle), when the item does
                if ((bool?)it["PlayerStyleEnabled"] == true)
                {
                    var pc = Color((string)it["PlayerTextColor"] ?? (string)it["PlayerStyle"]?["TextColor"]);
                    var oc = Color((string)it["OpponentTextColor"] ?? (string)it["OpponentStyle"]?["TextColor"] ?? (string)it["TextColor"]);
                    if (pc.HasValue && oc.HasValue && pc.Value != oc.Value)
                        r.ColorBind = $"ncalc:if(driverisplayer({pos}), '{Hex(pc.Value)}', '{Hex(oc.Value)}')";
                }
                Report.Note("leaderboard items imported as SimHub formulas picking the same drivers");
                return r;
            }

            private void Text(JObject it, string type, Frame f, List<string> cond, JObject binds, LeaderboardText lb = null)
            {
                var r = Box(it, f);
                if (r.Width <= 0 || r.Height <= 0) { Report.Skip(type + " (empty box)"); return; }
                // text turned on its side (a faint watermark along a panel's edge): the screen can't draw it turned, and
                // laid flat it lands on the panel's own text; fixed text like that is left out
                double rot = ((((double?)it["Rotation"] ?? 0) % 360) + 360) % 360;
                bool bound = binds?["Text"] != null || binds?["Value"] != null;
                if (!bound && Math.Abs(rot - 90) < 20 || !bound && Math.Abs(rot - 270) < 20) { Report.Skip(type + " (turned text)", "fixed text drawn on its side left out"); return; }
                double size = (double?)it["FontSize"] ?? 20;
                var colour = Color((string)(it["TextColor"] ?? it["GearTextColor"])) ?? System.Drawing.Color.White;
                // text in a see-through layer: the screen draws text solid, so its colour is blended toward the black under it
                int textOpacity = Opacity(it, f);
                if (textOpacity < 100) colour = System.Drawing.Color.FromArgb(colour.A, colour.R * textOpacity / 100, colour.G * textOpacity / 100, colour.B * textOpacity / 100);
                var back = Color((string)it["BackgroundColor"]);
                var bs = it["BorderStyle"] as JObject;
                int border = bs == null ? 0 : (int)Math.Round(Px(new[] { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" }.Average(k => (double?)bs[k] ?? 0), f));
                var borderColour = bs == null ? (Color?)null : Color((string)bs["BorderColor"]) ?? System.Drawing.Color.White; // SimHub draws borders white by default
                bool framed = border > 0 && borderColour.HasValue && borderColour.Value.A > 0;
                if (framed || (back.HasValue && back.Value.A > 0))
                {
                    int radius = bs == null ? 0 : (int)Math.Round(Px(new[] { "RadiusTopLeft", "RadiusTopRight", "RadiusBottomLeft", "RadiusBottomRight" }.Average(k => (double?)bs[k] ?? 0), f));
                    Add(new DashElement
                    {
                        Type = framed || radius > 0 ? "box" : "rect", Name = ((string)it["Name"] ?? type) + (framed ? " frame" : " background"),
                        X = r.X, Y = r.Y, W = r.Width, H = r.Height, Radius = radius, Border = framed ? Math.Max(1, border) : 0,
                        Color = framed ? Hex(borderColour.Value) : Hex(back.Value), Fill = back.HasValue && back.Value.A > 0 ? Hex(back.Value) : null,
                        Opacity = Opacity(it, f), Visible = Cond(f.Visible, cond),
                    });
                    Report.Converted++;
                }
                int align = (int?)it["HorizontalAlignment"] ?? 0;
                string sample = lb?.Sample ?? (string)it["Text"] ?? (string)it["DesignerText"] ?? (string)it["NoDataText"] ?? "";
                string textBind = lb?.Bind ?? Formula(binds, "Text");
                string format = lb?.Format ?? "text";
                var fs = lb != null ? null : (string)binds?["Text"]?["FormatString"];
                if (textBind == null) BuiltIn(type, ref textBind, ref format, ref sample);
                else if (!string.IsNullOrEmpty(fs)) format = TimeSpanFormat(fs) ? "time:" + fs : fs;
                else if (Regex.IsMatch(textBind, @"^ncalc:\[[^\]]*(LapTime|BestLap|LastLap|Laptime)[^\]]*\]$", RegexOptions.IgnoreCase)) format = "laptime"; // a bare lap time, shown m:ss.fff

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
                    e.Type = "value"; e.Bind = textBind; e.Format = format; e.Empty = lb?.Empty ?? "";
                    if (lb?.ColorBind != null) e.ColorBind = lb.ColorBind;
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
                    case "SpeedText": bind = "ncalc:[SpeedLocal]"; format = "0"; if (sample == "") sample = "288"; break; // the user's unit, as SimHub shows it
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
