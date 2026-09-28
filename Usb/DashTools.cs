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
                    var demo = new UsbDemo();
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
