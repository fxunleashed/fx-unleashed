using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A custom dash for the FX Pro screen in USB mode: an 800x480 page of elements drawn with the screen's own commands
    /// (no screen reflash). Built-in dashes are made in code (BuiltInDashes); user dashes are the same thing as JSON
    /// files in PluginsData\Common\FXProRpmSync\Dashes, written by the dash designer, the SimHub importer or by hand.
    /// Full reference: docs/dash-format.md.
    ///
    /// Elements, in drawing order:
    ///  - rect, ellipse, box, gradient, image: shapes. Drawn once, unless they have a Visible condition or a ColorBind,
    ///    then they're drawn and erased as those change.
    ///  - label: fixed text, no background.
    ///  - value: text bound to data, redrawn on change.
    ///  - bar: a fill from Min to Max (horizontal or vertical).
    ///  - deltabar: two rows of segments filling from the centre (positive = left half, negative = right).
    ///  - popup: a box shown for a few seconds when one of its watched values changes (TC, ABS, map...).
    /// Colours "#RRGGBB" or "#AARRGGBB" (alpha blends shapes over what's under them). Text uses the screen's fonts by id;
    /// their real sizes are in FontMetrics (text wider than its box wraps onto a line the screen doesn't show, so
    /// DashRenderer.Check measures it).
    /// </summary>
    public class DashDefinition
    {
        public const int CurrentFormat = 2;

        public int FormatVersion = CurrentFormat;
        public string Id;
        public string Name;
        public string Author;
        public string Description;
        public List<DashElement> Elements = new List<DashElement>();
        /// <summary>Pictures used by image elements, by name: base64 PNG. Keeps a dash one self-contained file.</summary>
        public Dictionary<string, string> Images;
        /// <summary>Where it came from (e.g. "SimHub: LMGT3 Ford Mustang GT3 / MAIN"), for imports.</summary>
        public string Source;
        /// <summary>Folder of JavaScript helpers its js: bindings call (an imported SimHub dash's JavascriptExtensions).</summary>
        public string ScriptsFolder;

        [JsonIgnore] public bool BuiltIn;
        [JsonIgnore] public string FilePath;

        /// <summary>Every data binding the dash uses (values, conditions, colours, pop-up watches).</summary>
        public IEnumerable<string> Bindings =>
            Elements.SelectMany(e => new[] { e.Bind, e.ColorBind }
                        .Concat(e.Visible ?? Enumerable.Empty<string>())
                        .Concat(e.Watch?.Select(w => w.Bind) ?? Enumerable.Empty<string>()))
                    .Where(b => !string.IsNullOrEmpty(b)).Distinct();

        public DashDefinition Clone() => JsonConvert.DeserializeObject<DashDefinition>(JsonConvert.SerializeObject(this));
    }

    public class DashElement
    {
        /// <summary>rect | ellipse | box | gradient | image | label | value | bar | deltabar | popup</summary>
        public string Type;
        public string Name;
        public int X, Y, W, H;

        /// <summary>Shape fill (rect), rim (ellipse), border (box), text colour (label, value), fill (bar).</summary>
        public string Color = "#FFFFFF";
        /// <summary>Ellipse / box inside, bar background (default: nothing drawn / black).</summary>
        public string Fill;
        /// <summary>Ellipse rim / box border width in px.</summary>
        public int Border;
        public int Radius;
        /// <summary>0-100: shapes blend over what's under them.</summary>
        public int Opacity = 100;

        /// <summary>Shown only while every condition is true (a number != 0, true, or a non-empty text). A single
        /// string or a list; bindings as for Bind.</summary>
        [JsonConverter(typeof(StringOrListConverter))] public List<string> Visible;
        /// <summary>
        /// In previews (designer, fxdash), where SimHub formulas can't be evaluated: show this element (null = yes).
        /// Imports set false for overlays (flashes, pit screens) so they don't hide the dash in the preview.
        /// </summary>
        public bool? PreviewVisible;
        /// <summary>Colour from data: a binding giving "#RRGGBB"/"#AARRGGBB"/a colour name, or a number mapped through
        /// ColorStops. Replaces Color (text, rect, bar) or Fill (ellipse, box).</summary>
        public string ColorBind;
        /// <summary>For a numeric ColorBind: colours at values, blended between (like SimHub's colour gradients).</summary>
        public List<ColorStop> ColorStops;

        public string Text;
        public int Font = 14;
        /// <summary>left | center | right</summary>
        public string Align = "left";
        /// <summary>
        /// Value background. Empty = automatic: the colour under the box when that's one colour, else the area is redrawn
        /// from the shapes before each new text (costs more). Set it only to force a colour.
        /// </summary>
        public string Background;

        /// <summary>
        /// Data: a key from DashValues.Keys ("speed", "gear"...), "prop:" + a SimHub property, or a SimHub formula:
        /// "ncalc:" + NCalc expression / "js:" + JavaScript (evaluated by SimHub's own engine while SimHub runs).
        /// </summary>
        public string Bind;
        /// <summary>
        /// "0", "0.0", "0.00"... (any .NET number format) | int | laptime | gear | delta | text | time:&lt;TimeSpan format&gt;
        /// </summary>
        public string Format = "0";
        /// <summary>Multiplier applied before formatting.</summary>
        public double Scale = 1;
        /// <summary>Shown when the value is missing (no data yet, game doesn't provide it).</summary>
        public string Empty = "-";
        /// <summary>Colour by sign (value, e.g. delta); fill colours (deltabar: positive = left half).</summary>
        public string PositiveColor, NegativeColor;
        /// <summary>The widest texts this value shows, checked against its box.</summary>
        public string[] Samples;
        /// <summary>What the designer shows for a value when there's no data (defaults to the first sample).</summary>
        public string PreviewText;

        // bar
        public double Min = 0, Max = 100;
        /// <summary>horizontal | vertical</summary>
        public string Orientation = "horizontal";
        /// <summary>Fill from the right (horizontal) or the top (vertical).</summary>
        public bool Reverse;

        // gradient: Colors spread from start to end; Angle 90 = top to bottom, 0 = left to right
        public List<string> Colors;
        public double Angle = 90;

        // image: a name in DashDefinition.Images; drawn with at most MaxColors colours (fewer = faster to draw)
        public string Image;
        public int MaxColors = 8;

        // deltabar
        public int Segments = 7;         // per side
        public int[] SegmentX;           // segment left edges (2 x Segments), or from Pitch
        public double Pitch = 40;
        public int SegmentWidth = 30;
        public double Range = 1;         // value that fills a half
        public string SegmentColor = "#808080";

        // popup
        public List<PopupWatch> Watch;
        public double Duration = 2;
        public int ValueFont = 35;

        public int[] SegmentLefts()
        {
            if (SegmentX != null && SegmentX.Length == Segments * 2) return SegmentX;
            return Enumerable.Range(0, Segments * 2).Select(k => X + (int)Math.Round(k * Pitch)).ToArray();
        }

        /// <summary>Drawn and erased at runtime rather than once (conditions, data colours, data itself).</summary>
        [JsonIgnore]
        public bool IsDynamic =>
            (Visible != null && Visible.Count > 0) || !string.IsNullOrEmpty(ColorBind) ||
            Type == "value" || Type == "bar" || Type == "deltabar" || Type == "popup";
    }

    public class ColorStop
    {
        public double Value;
        public string Color;
    }

    public class PopupWatch
    {
        public string Bind;
        public string Label;
        public string Color;
        public string Format = "int";
    }

    /// <summary>Reads "x" as ["x"], so a single condition can be written as a plain string.</summary>
    public class StringOrListConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(List<string>);

        public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType == JsonToken.String) return new List<string> { (string)reader.Value };
            return serializer.Deserialize<List<string>>(reader);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var list = (List<string>)value;
            if (list == null) writer.WriteNull();
            else if (list.Count == 1) writer.WriteValue(list[0]);
            else serializer.Serialize(writer, list);
        }
    }

    /// <summary>Built-in dashes plus JSON dashes from the user's folder.</summary>
    public static class DashLibrary
    {
        /// <summary>SimHub's folder (default: where this runs, i.e. SimHub itself; fxdash sets it).</summary>
        public static string Root;
        public static string SimHubFolder => Root ?? AppDomain.CurrentDomain.BaseDirectory;
        public static string Folder => Path.Combine(SimHubFolder, "PluginsData", "Common", "FXProRpmSync", "Dashes");

        /// <summary>Loaded dashes; files that fail to load come back with their error.</summary>
        public static List<DashDefinition> Load(List<string> errors)
        {
            var list = new List<DashDefinition>(BuiltInDashes.All());
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (var file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var d = JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(file));
                        if (d?.Elements == null) throw new Exception("no elements");
                        d.FilePath = file;
                        if (string.IsNullOrWhiteSpace(d.Id)) d.Id = "file:" + Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Path.GetFileNameWithoutExtension(file);
                        if (list.Any(x => x.Id == d.Id)) d.Id += " (" + Path.GetFileName(file) + ")";
                        list.Add(d);
                    }
                    catch (Exception ex) { errors?.Add(Path.GetFileName(file) + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { errors?.Add(ex.Message); }
            return list;
        }

        /// <summary>Writes a built-in dash to the folder as a starting point for editing.</summary>
        public static string Export(DashDefinition d)
        {
            Directory.CreateDirectory(Folder);
            var copy = JsonConvert.DeserializeObject<DashDefinition>(JsonConvert.SerializeObject(d));
            copy.Id = d.Id + "-copy";
            copy.Name = d.Name + " (copy)";
            var path = Path.Combine(Folder, copy.Id + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(copy, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Include }));
            return path;
        }
    }

    public static class BuiltInDashes
    {
        public const string MustangId = "lmgt3-mustang";

        public static IEnumerable<DashDefinition> All()
        {
            yield return MustangGt3();
        }

        /// <summary>SimHub 1200x720 coordinates to the screen's 800x480 (2/3).</summary>
        private static int S(double v) => (int)Math.Round(v * 2 / 3);

        /// <summary>A rectangle from SimHub coordinates, scaled (edges rounded like FXProDashes tools/dash).</summary>
        private static DashElement At(string type, double x, double y, double w, double h)
        {
            return new DashElement { Type = type, X = S(x), Y = S(y), W = S(x + w) - S(x), H = S(y + h) - S(y) };
        }

        private static DashElement E(string type, int x, int y, int w, int h) => new DashElement { Type = type, X = x, Y = y, W = w, H = h };

        /// <summary>
        /// The MAIN screen of SimHub's "LMGT3 Ford Mustang GT3" dash, scaled 2/3 and fitted to the FX Pro (FXProDashes
        /// docs/custom-dash.md; verified on the wheel 2026-09-27). Fonts chosen by their real widths: values in the
        /// narrow "963" family (101 = 32 px, 98 = 40, 100 = 50), labels S 20 px (14) / 16 px (12), gear font 117.
        /// Below the divider the layout is squeezed ~18 px so it fits with 20 px of top padding.
        /// </summary>
        public static DashDefinition MustangGt3()
        {
            const string grey = "#D3D3D3", blue = "#428AED", navy = "#1B1F3C";
            var d = new DashDefinition
            {
                Id = MustangId,
                Name = "LMGT3 Ford Mustang GT3",
                Author = "after SimHub's LMGT3 Ford Mustang GT3 dash",
                BuiltIn = true,
            };
            var el = d.Elements;

            // Shapes
            var oval = At("ellipse", 313, 54, 571, 230);
            oval.Color = "#FFFFFF"; oval.Fill = navy; oval.Border = 4; oval.Name = "oval";
            el.Add(oval);
            foreach (var y in new[] { 91, 183, 280 }) el.Add(Rect(0, S(y), S(220), 2, blue));
            foreach (var y in new[] { 108, 200, 297 }) el.Add(Rect(S(946), S(y), S(220), 2, blue));
            var divider = At("rect", 45, 402, 1120, 4); divider.Color = blue; el.Add(divider);
            foreach (var x in new[] { 496, 626, 764, 899, 1034 })
            {
                var mark = At("rect", x + 80, 416, 10, 30); mark.Color = "#FF0000"; el.Add(mark);
            }

            // Delta bar: 14 segments, 70 px tall (80 scaled, squeezed)
            el.Add(new DashElement
            {
                Type = "deltabar", Name = "delta bar", Bind = "delta", X = S(574), Y = 303, H = 70,
                Segments = 7, SegmentX = Enumerable.Range(0, 14).Select(k => S(574 + 40 * k)).ToArray(), SegmentWidth = S(604) - S(574),
                Range = 1, PositiveColor = "#FF0000", NegativeColor = "#00FF00", SegmentColor = "#808080",
            });

            // Bottom row: 8 boxes, 62 px tall, right under the bar
            string[] names = { "Map", "Throttle", "TC", "TC LON", "TC LAT", "ABS", "PAS", "Lap" };
            string[] shortNames = { "Map", "Throt", "TC", "TCLon", "TCLat", "ABS", "PAS", "Lap" };
            string[] colours = { "#3277AE", "#BA6234", "#28598B", "#0000FF", "#00AAF5", "#FFFF00", "#534A64", "#D3D3D3" };
            string[] binds = { "engineMap", "throttleMap", "tcLevel", "tcCut", "tcSlip", "absLevel", "pas", "lap" };
            int[] xs = { 40, 180, 320, 462, 608, 750, 890, 1030 };
            var boxes = new List<DashElement>();
            for (int i = 0; i < 8; i++)
            {
                int x = S(xs[i]), w = S(xs[i] + 130) - x, y = 379, h = 62;
                el.Add(new DashElement { Type = "box", Name = names[i], X = x, Y = y, W = w, H = h, Color = colours[i], Border = 3, Radius = 10 });
                boxes.Add(new DashElement { Type = "label", Text = shortNames[i], X = x + 3, Y = y + 3, W = w - 6, H = 16, Font = 12, Color = colours[i], Align = "center" });
                boxes.Add(new DashElement
                {
                    Type = "value", Name = names[i], Bind = binds[i], Format = "int", X = x + 8, Y = y + 19, W = w - 16, H = 40,
                    Font = 98, Color = grey, Align = "center", Samples = new[] { "88" },
                });
            }

            // Labels
            el.Add(Label("Fuel Last Lap", At("label", 0, 0, 346, 38), 14, grey, "left"));
            el.Add(Label("Fuel +/-", At("label", 0, 93, 346, 38), 14, grey, "left"));
            el.Add(Label("Fuel Lap", At("label", 0, 188, 346, 38), 14, grey, "left"));
            el.Add(Label("Fuel Remain", At("label", 0, 290, 346, 38), 14, grey, "left"));
            el.Add(Label("Last Lap", At("label", 896, 17, 270, 38), 14, grey, "right"));
            el.Add(Label("Delta", At("label", 906, 110, 260, 38), 14, grey, "right"));
            el.Add(Label("Predicted", At("label", 906, 205, 260, 38), 14, grey, "right"));
            el.Add(Label("VE Remain", At("label", 906, 307, 260, 38), 14, grey, "right"));
            el.Add(Label("Bias", E("label", 270, 231, 65, 33), 14, grey, "center"));
            el.Add(Label("Laptime", E("label", 57, 292, 173, 25), 14, grey, "left"));
            el.Add(Label("Pos.", E("label", 250, 273, 62, 26), 14, grey, "left"));
            el.AddRange(boxes.Where(b => b.Type == "label"));

            // Values
            el.Add(Value("fuel last lap", "fuelLastLap", "0.00", E("value", 0, 25, 125, 32), 101, "left", "-.--", "88.88"));
            el.Add(Value("fuel", "fuel", "0.0", At("value", 0, 131, 187, 50), 101, "left", "-", "88.8"));
            el.Add(Value("fuel this lap", "fuelThisLap", "0.00", At("value", 0, 227, 187, 50), 101, "left", "-.--", "8.88"));
            el.Add(Value("fuel remain laps", "fuelRemainingLaps", "0.0", At("value", 0, 332, 187, 50), 101, "left", "-.-", "888.8"));
            el.Add(Value("last lap", "lastLapTime", "laptime", E("value", 577, 37, 200, 32), 101, "right", "-:--.---", "8:88.888"));
            var delta = Value("delta", "delta", "delta", At("value", 906, 148, 260, 50), 101, "right", "-.--", "+8.88", "-8.88");
            delta.PositiveColor = "#FF0000"; delta.NegativeColor = "#00FF00";
            el.Add(delta);
            el.Add(Value("predicted", "predictedLap", "laptime", E("value", 577, 163, 200, 34), 101, "right", "-:--.---", "8:88.888"));
            el.Add(Value("ve", "virtualEnergy", "0.0", At("value", 906, 349, 260, 50), 101, "right", "-", "100.0"));
            el.Add(Value("speed", "speed", "0", E("value", 347, 0, 107, 36), 101, "center", "0", "388"));
            var gear = Value("gear", "gear", "gear", At("value", 520, 80, 160, 180), 117, "center", "N", "N", "R", "8");
            gear.Background = navy;
            el.Add(gear);
            var session = Value("session", "sessionTypeName", "text", E("value", 219, 100, 127, 20), 12, "left", "", "PRACTICE", "QUALIFY");
            session.Background = navy; session.Color = "#FFFFFF";
            el.Add(session);
            el.Add(Value("bias", "brakeBias", "0.00", E("value", 340, 229, 120, 37), 101, "center", "-", "88.88"));
            el.Add(Value("lap time", "currentLapTime", "laptime", E("value", 57, 318, 309, 50), 100, "left", "-:--.---", "8:88.888"));
            el.Add(Value("position", "position", "int", E("value", 314, 273, 59, 25), 14, "right", "-", "20"));
            el.AddRange(boxes.Where(b => b.Type == "value"));

            // Pop-up over the middle when a setting changes (SimHub shows it for 2 s)
            el.Add(new DashElement
            {
                Type = "popup", Name = "setting change", X = 280, Y = 229, W = 237, H = 167, Radius = 10, Font = 101, ValueFont = 35, Duration = 2,
                Color = "#000000",
                Watch = new List<PopupWatch>
                {
                    new PopupWatch { Bind = "tcLevel", Label = "TC", Color = "#28598B" },
                    new PopupWatch { Bind = "tcCut", Label = "TC LON", Color = "#0000FF" },
                    new PopupWatch { Bind = "tcSlip", Label = "TC LAT", Color = "#00AAF5" },
                    new PopupWatch { Bind = "absLevel", Label = "ABS", Color = "#FFFF00" },
                    new PopupWatch { Bind = "engineMap", Label = "Map", Color = "#3277AE" },
                    new PopupWatch { Bind = "brakeBias", Label = "Bias", Color = grey, Format = "0.0" },
                },
            });
            return d;
        }

        private static DashElement Rect(int x, int y, int w, int h, string colour) =>
            new DashElement { Type = "rect", X = x, Y = y, W = w, H = h, Color = colour };

        private static DashElement Label(string text, DashElement at, int font, string colour, string align)
        {
            at.Type = "label"; at.Text = text; at.Name = text; at.Font = font; at.Color = colour; at.Align = align;
            return at;
        }

        private static DashElement Value(string name, string bind, string format, DashElement at, int font, string align, string empty, params string[] samples)
        {
            at.Type = "value"; at.Name = name; at.Bind = bind; at.Format = format; at.Font = font; at.Align = align;
            at.Color = "#D3D3D3"; at.Empty = empty; at.Samples = samples;
            return at;
        }
    }
}
