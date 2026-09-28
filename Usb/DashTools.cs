using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Everything the dash designer, its HTTP API and the fxdash command line do with a dash, in one place, so people,
    /// agents and the wheel all get the same answers: the format's schema, fonts and bindings, layout checks and draw
    /// cost, rendering to PNG (preview or a simulated lap), and importing SimHub dashes. Reference: docs/dash-format.md.
    /// </summary>
    public static class DashTools
    {
        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        public static DashDefinition Parse(string json)
        {
            var d = JsonConvert.DeserializeObject<DashDefinition>(json) ?? throw new Exception("empty dash");
            if (d.Elements == null) d.Elements = new List<DashElement>();
            return d;
        }

        public static string Serialize(DashDefinition d) => JsonConvert.SerializeObject(d, Json);

        // ---------- Reference data ----------

        /// <summary>The format, machine-readable: element types and fields with what they do.</summary>
        public static JObject Schema() => JObject.FromObject(new
        {
            screen = new { width = DashRenderer.Width, height = DashRenderer.Height, colours = "#RRGGBB or #AARRGGBB (alpha blends shapes); the screen shows 16-bit colour (RGB565)" },
            formatVersion = DashDefinition.CurrentFormat,
            dash = new Dictionary<string, string>
            {
                ["Id"] = "unique id (file name when saved)", ["Name"] = "shown in the dash list", ["Author"] = "", ["Description"] = "",
                ["Elements"] = "list, drawn in order (later on top)", ["Images"] = "name -> base64 PNG, for image elements",
                ["Source"] = "where an import came from", ["ScriptsFolder"] = "JavaScript helpers for js: bindings",
            },
            elementTypes = new Dictionary<string, string>
            {
                ["rect"] = "filled rectangle: Color",
                ["ellipse"] = "ellipse: Color (whole, or the rim when Border > 0), Fill (inside, optional), Border",
                ["box"] = "rounded frame: Color (border), Fill (inside, optional), Border, Radius",
                ["gradient"] = "linear gradient: Colors (2+ stops), Angle (90 = top to bottom), Radius, Border + Color",
                ["image"] = "picture from Images: Image (name), MaxColors (2-64; fewer draws faster)",
                ["label"] = "fixed text: Text, Font, Color, Align",
                ["value"] = "text from data: Bind, Format, Scale, Empty, Samples (widest texts, checked), PreviewText, Font, Color, Align, PositiveColor/NegativeColor, Background (optional)",
                ["bar"] = "gauge fill: Bind, Min, Max (may be below Min), Orientation horizontal|vertical, Reverse, Color (fill), Fill (empty part, optional)",
                ["deltabar"] = "segments filling from the centre: Bind, Segments (per side), SegmentX or Pitch, SegmentWidth, Range, PositiveColor (left), NegativeColor (right), SegmentColor",
                ["popup"] = "box shown for Duration s when a Watch value changes: Watch [{Bind, Label, Color, Format}], Font, ValueFont, Color (text), Radius",
            },
            commonFields = new Dictionary<string, string>
            {
                ["Type"] = "see elementTypes", ["Name"] = "for messages and the designer",
                ["X,Y,W,H"] = "box in screen pixels (0,0 = top left of 800x480)",
                ["Visible"] = "condition(s): a binding or list of bindings, all must be true (number != 0, true, non-empty text)",
                ["ColorBind"] = "binding giving a colour (#RRGGBB, #AARRGGBB, name) or a number mapped through ColorStops",
                ["ColorStops"] = "[{Value, Color}] blended between, for a numeric ColorBind",
                ["Opacity"] = "0-100 for shapes",
            },
            bindings = "a key from `bindings`, \"prop:<SimHub property>\", \"ncalc:<NCalc formula>\" or \"js:<JavaScript>\" (formulas are evaluated by SimHub while it runs)",
            formats = new Dictionary<string, string>
            {
                ["0, 0.0, 0.00 ..."] = "any .NET number format", ["int"] = "rounded", ["laptime"] = "m:ss.fff from seconds",
                ["time:<fmt>"] = "TimeSpan format from seconds, e.g. time:mm\\:ss\\.fff", ["gear"] = "R / N / number",
                ["delta"] = "+0.00 / -0.00", ["text"] = "as is",
            },
            fonts = "screen font ids (see fonts): a font's height must fit the box, and every sample must fit its width",
            limits = new[]
            {
                "No new pictures at runtime except as fill rectangles: images cost draw time (see check's cost).",
                "Text wider than its box wraps onto a line the screen doesn't show: give values Samples.",
                "The static layer draws at 25 KB/s when the dash starts (check's cost.StaticSeconds).",
                "A value on a busy background (image, gradient) redraws that area on every change; plain backgrounds are fastest.",
            },
        });

        public static IEnumerable<object> Bindings() => DashValues.Keys.Select(k => new { key = k.Key, description = k.Description });

        /// <summary>Every screen font: id, height, whether it has all of ASCII, and (with a sample) how wide the sample is.</summary>
        public static IEnumerable<object> Fonts(string sample = null) =>
            Enumerable.Range(0, FontMetrics.Fonts.Length).Select(f => new
            {
                id = f,
                height = DashRenderer.FontHeight(f),
                fullAscii = DashFonts.FullAscii.Contains(f),
                chars = new string(Enumerable.Range(32, 95).Where(c => FontMetrics.Fonts[f][c - 31] >= 0).Select(c => (char)c).ToArray()),
                digitWidth = DashRenderer.TextWidth(f, "0"),
                sampleWidth = sample == null ? (int?)null : DashRenderer.TextWidth(f, sample),
            });

        /// <summary>The best font for a box and text (same choice the SimHub importer makes).</summary>
        public static object SuggestFont(int boxW, int boxH, string sample, double height = 0)
        {
            int f = DashFonts.Pick(height > 0 ? height : boxH, boxW, boxH, sample);
            return new { font = f, height = f < 0 ? 0 : DashRenderer.FontHeight(f), width = f < 0 ? -1 : DashRenderer.TextWidth(f, sample) };
        }

        // ---------- Check and render ----------

        public class CheckResult
        {
            [JsonProperty("ok")] public bool Ok;
            [JsonProperty("errors")] public int Errors;
            [JsonProperty("warnings")] public int Warnings;
            [JsonProperty("issues")] public List<DashIssue> Issues;
            [JsonProperty("cost")] public DashCost Cost;
        }

        /// <summary>
        /// Values whose text rows cross a line of the dash (a box's border): redrawing such text means wiping the line and
        /// drawing it again, which flashes on the wheel. Each gets the tallest font that still fits its widest sample and
        /// whose rows fit between the lines (the text gets a little smaller). Returns what changed.
        /// </summary>
        public static List<string> FitTextBands(DashDefinition d)
        {
            var changes = new List<string>();
            using (var screen = new PreviewScreen())
            {
                var r = new DashRenderer(screen, d, 0, 0);
                for (int i = 0; i < d.Elements.Count; i++)
                {
                    var e = d.Elements[i];
                    if (e.Type != "value") continue;
                    var texts = new[] { e.PreviewText, e.Empty }.Concat(e.Samples ?? new string[0]).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                    if (texts.Count == 0) continue;
                    // clean: some margin (as the renderer shrinks it) leaves every text's band one colour
                    bool Clean(DashElement x, int f)
                    {
                        int full = 4 + DashRenderer.FontHeight(f) / 4;
                        for (int m = full; m >= 1; m = m > 1 ? (m == full ? full / 2 : m / 2) : 0)
                            if (texts.All(t => r.ColourUnder(e, DashRenderer.TextBand(x, f, t, m)).HasValue)) return true;
                        return false;
                    }
                    bool Fits(DashElement x, int f) => texts.All(t => { int tw = DashRenderer.TextWidth(f, t); return tw >= 0 && tw + 2 <= x.W; }) && DashRenderer.FontHeight(f) <= x.H;
                    if (Clean(e, e.Font)) continue;
                    int h0 = DashRenderer.FontHeight(e.Font);
                    // 1. the same font, the box nudged a few pixels (up/down, or its anchored side in from a line)
                    DashElement found = null;
                    foreach (var dy in new[] { 0, -1, 1, -2, 2, -3, 3, -4, 4 })
                    {
                        if (e.Y + dy < 0 || e.Y + e.H + dy > DashRenderer.Height - 20) continue; // stays on the screen with the padding
                        foreach (var inset in new[] { 0, 1, 2, 3 })
                        {
                            var x = Nudged(e, dy, inset);
                            if (Fits(x, e.Font) && Clean(x, e.Font)) { found = x; break; }
                        }
                        if (found != null) break;
                    }
                    if (found != null)
                    {
                        changes.Add($"#{i} {e.Name}: moved {found.X - e.X},{found.Y - e.Y} and {found.W - e.W},{found.H - e.H} px so its text sits between the lines");
                        e.X = found.X; e.Y = found.Y; e.W = found.W; e.H = found.H;
                        continue;
                    }
                    // 2. a smaller font (tallest first, then the proportions closest to the one it had), nudged if needed
                    string w0 = texts.OrderByDescending(t => DashRenderer.TextWidth(e.Font, t)).First();
                    double ratio0 = h0 > 0 ? (double)DashRenderer.TextWidth(e.Font, w0) / h0 : 1;
                    int best = -1; DashElement bestBox = null; double bestScore = double.MaxValue;
                    for (int f = 0; f < FontMetrics.Fonts.Length; f++)
                    {
                        int h = DashRenderer.FontHeight(f);
                        if (h <= 0 || h >= h0 || h < h0 * 0.6 || texts.Any(t => DashRenderer.TextWidth(f, t) < 0)) continue;
                        double score = (h0 - h) * 10 + Math.Abs((double)texts.Max(t => DashRenderer.TextWidth(f, t)) / h - ratio0);
                        if (score >= bestScore) continue;
                        foreach (var dy in new[] { 0, -1, 1, -2, 2, -3, 3 })
                        {
                            if (e.Y + dy < 0 || e.Y + e.H + dy > DashRenderer.Height - 20) continue;
                            var x = Nudged(e, dy, 0);
                            if (Fits(x, f) && Clean(x, f)) { best = f; bestBox = x; bestScore = score; break; }
                        }
                    }
                    if (best < 0)
                    {
                        // 2b. a value taller than the shape it sits on (a pill, a tile): the box brought inside that
                        //     shape, clear of its border and rounded corners, with the tallest font that's clean there
                        var cx = e.X + e.W / 2; var cy = e.Y + e.H / 2;
                        var under = d.Elements.Take(i).LastOrDefault(sh => (sh.Type == "box" || sh.Type == "rect") && (sh.Visible == null || sh.Visible.Count == 0)
                                                                          && sh.X <= cx && cx < sh.X + sh.W && sh.Y <= cy && cy < sh.Y + sh.H
                                                                          && (sh.Y > e.Y || sh.Y + sh.H < e.Y + e.H));
                        if (under != null)
                        {
                            int inset = Math.Max(under.Border, 0) + 1; // the corners only matter at the ends: Clean checks them
                            int top = Math.Max(e.Y, under.Y + inset), bottom = Math.Min(e.Y + e.H, under.Y + under.H - inset);
                            DashElement inside = null; int insideFont = -1;
                            var byHeight = Enumerable.Range(0, FontMetrics.Fonts.Length)
                                .Where(f => DashRenderer.FontHeight(f) > 0 && DashRenderer.FontHeight(f) <= h0 && DashRenderer.FontHeight(f) >= h0 * 0.5 && texts.All(t => DashRenderer.TextWidth(f, t) >= 0))
                                .OrderByDescending(f => DashRenderer.FontHeight(f))
                                .ThenBy(f => Math.Abs((double)texts.Max(t => DashRenderer.TextWidth(f, t)) / DashRenderer.FontHeight(f) - ratio0));
                            if (bottom - top >= 8)
                                foreach (var f in byHeight)
                                {
                                    var x = new DashElement { Type = e.Type, X = e.X, Y = top, W = e.W, H = bottom - top, Align = e.Align, Font = f };
                                    if (Fits(x, f) && Clean(x, f)) { inside = x; insideFont = f; break; }
                                }
                            if (inside != null)
                            {
                                changes.Add($"#{i} {e.Name}: box {e.Y}+{e.H} -> {inside.Y}+{inside.H} inside {under.Name ?? under.Type}, font {e.Font} ({h0} px) -> {insideFont} ({DashRenderer.FontHeight(insideFont)} px), so its text sits between the lines");
                                e.Y = inside.Y; e.H = inside.H; e.Font = insideFont;
                                continue;
                            }
                        }
                        // 3. nothing clears it: the value gets a solid background, the colour under most of its text (it
                        //    then covers that bit of the line, drawn in one step, instead of redrawing the line each change).
                        //    Only when that colour is nearly all of it: a band sticking out of its shape would show as a block.
                        var band = DashRenderer.TextBand(e, e.Font, w0);
                        var c = r.CommonStaticColour(band, out double share);
                        if (c == null || share < 0.9) { changes.Add($"#{i} {e.Name}: its text crosses a line, and no nearby position or font (down to 60% of its size) avoids it: move or resize it"); continue; }
                        var col = DashRenderer.ToColor(c.Value);
                        var bg = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
                        if (string.Equals(e.Background, bg, StringComparison.OrdinalIgnoreCase)) continue; // done before
                        e.Background = bg;
                        changes.Add($"#{i} {e.Name}: its text crosses a line and no position or font avoids it: Background {e.Background} (the colour under most of it)");
                        continue;
                    }
                    changes.Add($"#{i} {e.Name}: font {e.Font} ({h0} px) -> {best} ({DashRenderer.FontHeight(best)} px)" + (bestBox.Y != e.Y ? $", moved {bestBox.Y - e.Y} px" : "") + ", so its text sits between the lines");
                    e.Font = best; e.Y = bestBox.Y;
                }
            }
            return changes;
        }

        /// <summary>A copy of an element's box moved `dy` down, and `inset` px in from the side its text is anchored to.</summary>
        private static DashElement Nudged(DashElement e, int dy, int inset)
        {
            var x = new DashElement { Type = e.Type, X = e.X, Y = e.Y + dy, W = e.W, H = e.H, Align = e.Align, Font = e.Font };
            if (e.Align == "center") { x.X += inset; x.W -= 2 * inset; }
            else if (e.Align == "right") x.W -= inset;
            else { x.X += inset; x.W -= inset; }
            return x;
        }

        public static CheckResult Check(DashDefinition d, int left = 0, int top = 0)
        {
            using (var p = new PreviewScreen())
            {
                var issues = new DashRenderer(p, d, left, top).CheckDetailed(out var cost);
                return new CheckResult
                {
                    Ok = issues.All(i => i.Level != "error"),
                    Errors = issues.Count(i => i.Level == "error"),
                    Warnings = issues.Count(i => i.Level == "warning"),
                    Issues = issues,
                    Cost = cost,
                };
            }
        }

        /// <summary>
        /// A PNG of the dash as the wheel would show it. mode "preview": values show their PreviewText/samples and
        /// SimHub-only conditions count as met (like a designer); "demo": `seconds` of the simulated lap.
        /// </summary>
        public static byte[] Render(DashDefinition d, string mode = "preview", double seconds = 20, int left = 0, int top = 0, DashValues values = null)
        {
            using (var p = new PreviewScreen())
            {
                var r = new DashRenderer(p, d, left, top);
                r.DrawAll();
                if (values != null) r.Update(values, 0);
                else if (mode == "demo")
                {
                    var demo = new UsbDemo(d);
                    for (double t = 0.1; t <= seconds; t += 0.1) r.Update(demo.Step(0.1), t);
                }
                else r.Update(new DashValues { Preview = true, Running = true }, 0);
                return p.Png();
            }
        }

        // ---------- Library and import ----------

        public static IEnumerable<object> Dashes(List<string> errors = null) =>
            DashLibrary.Load(errors).Select(d => new { id = d.Id, name = d.Name, builtIn = d.BuiltIn, file = d.FilePath, source = d.Source, elements = d.Elements.Count });

        public static DashDefinition Find(string id) => DashLibrary.Load(null).FirstOrDefault(d => d.Id == id);

        /// <summary>Saves into the dashes folder (as &lt;Id&gt;.json); returns the path.</summary>
        public static string Save(DashDefinition d)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) throw new Exception("the dash needs an Id");
            if (BuiltInDashes.All().Any(b => b.Id == d.Id)) throw new Exception($"\"{d.Id}\" is a built-in dash: save it under another Id");
            Directory.CreateDirectory(DashLibrary.Folder);
            var file = Path.Combine(DashLibrary.Folder, string.Concat(d.Id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");
            d.FormatVersion = DashDefinition.CurrentFormat;
            File.WriteAllText(file, Serialize(d));
            return file;
        }

        public static IEnumerable<object> SimHubDashes(string folder = null) =>
            SimHubImport.Installed(folder).Select(x => new { name = x.Name, path = x.Path });

        /// <summary>A SimHub dash by name (installed) or .djson path.</summary>
        public static string ResolveSimHub(string nameOrPath, string folder = null)
        {
            if (File.Exists(nameOrPath)) return nameOrPath;
            var hit = SimHubImport.Installed(folder).FirstOrDefault(x => string.Equals(x.Name, nameOrPath, StringComparison.OrdinalIgnoreCase));
            return hit.Path ?? throw new Exception($"no SimHub dash \"{nameOrPath}\"");
        }

        public static (DashDefinition Dash, ImportReport Report) Import(string nameOrPath, ImportOptions opt = null, string folder = null)
        {
            var def = SimHubImport.Import(ResolveSimHub(nameOrPath, folder), opt, out var report);
            return (def, report);
        }
    }
}
