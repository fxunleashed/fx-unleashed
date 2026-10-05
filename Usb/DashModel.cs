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
    ///  - dim: while shown (Visible), the whole screen is darker by its Opacity %, with the backlight (nothing redrawn):
    ///    SimHub's see-through black layer over a dash (headlights on) without redrawing every value under it.
    /// Colours "#RRGGBB" or "#AARRGGBB" (alpha blends shapes over what's under them). Text uses the screen's fonts by id;
    /// their real sizes are in FontMetrics (text wider than its box wraps onto a line the screen doesn't show, so
    /// DashRenderer.Check measures it).
    /// </summary>
    public class DashDefinition
    {
        /// <summary>The newest format this plugin reads. 3 = pages (Pages / "page:N" conditions), 4 = more page sets (PageSets /
        /// "page2:N"...); a dash is saved with the lowest format that holds what it uses (RequiredFormat), so dashes without
        /// them still load in older plugins.</summary>
        public const int CurrentFormat = 4;

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
        /// <summary>
        /// Pages the driver flips through (Next / Previous page actions, a wheel button): their names, in order. Elements
        /// on one page carry the condition "page:N" (0 = the first) in Visible; elements without one show on every page.
        /// Null = no pages. See DashPages.
        /// </summary>
        public List<string> Pages;
        /// <summary>
        /// More sets of pages, each flipped on its own (Next / Previous page 2, 3, 4: SimHub widgets on their own screen
        /// commands); Next / Previous page flips every set. PageSets[0] is set 2: its elements carry "page2:N", and so on.
        /// Null = only the one set (Pages). See DashPages.
        /// </summary>
        public List<DashPageSet> PageSets;

        [JsonIgnore] public bool BuiltIn;
        /// <summary>Comes inside the plugin (BundledDashes): read-only like a built-in, and replaced by a file with the same Id in the dashes folder.</summary>
        [JsonIgnore] public bool Bundled;
        [JsonIgnore] public string FilePath;

        /// <summary>Every data binding the dash uses (values, conditions, colours, pop-up watches).</summary>
        public IEnumerable<string> Bindings =>
            Elements.SelectMany(e => new[] { e.Bind, e.ColorBind }
                        .Concat(e.Visible ?? Enumerable.Empty<string>())
                        .Concat(e.Watch?.Select(w => w.Bind) ?? Enumerable.Empty<string>()))
                    .Where(b => !string.IsNullOrEmpty(b)).Distinct();

        public DashDefinition Clone() => JsonConvert.DeserializeObject<DashDefinition>(JsonConvert.SerializeObject(this));

        /// <summary>How many pages it flips through: its Pages, or the highest "page:N" any element uses (1 = no pages).</summary>
        [JsonIgnore]
        public int PageCount => PageCountOf(0);

        /// <summary>How many pages set `set` (0 = the first, Pages) has: its names, or the highest page its elements use.</summary>
        public int PageCountOf(int set) => Math.Max(Math.Max(1, NamesOf(set)?.Count ?? 0),
            Elements.Select(e => DashPages.PageOf(e, set)).Where(p => p.HasValue).Select(p => p.Value + 1).DefaultIfEmpty(1).Max());

        /// <summary>Flips that show every page of every set (DashPages.ShowFlip): its biggest set's pages (1 = no pages).</summary>
        [JsonIgnore]
        public int FlipCount => Enumerable.Range(0, DashPages.MaxSets).Max(PageCountOf);

        /// <summary>How many sets of pages it has (1 = one set or none; up to DashPages.MaxSets): the last set that flips.</summary>
        [JsonIgnore]
        public int SetCount
        {
            get
            {
                int n = 1;
                for (int set = 1; set < DashPages.MaxSets; set++)
                    if (PageCountOf(set) > 1) n = set + 1;
                return n;
            }
        }

        /// <summary>The lowest format that holds what this dash uses (what it's saved as): 4 with more page sets, 3 with
        /// pages, else 2.</summary>
        [JsonIgnore]
        public int RequiredFormat =>
            (PageSets != null && PageSets.Any(p => p?.Pages != null && p.Pages.Count > 0)) || Elements.Any(e => DashPages.SetsOf(e).Any(x => x > 0)) ? 4
            : (Pages != null && Pages.Count > 0) || Elements.Any(e => DashPages.PageOf(e).HasValue) ? 3 : 2;

        /// <summary>A page's name ("Page 2" when it has none).</summary>
        public string PageName(int page) => PageName(0, page);

        /// <summary>A page's name in a set ("Page 2" when it has none).</summary>
        public string PageName(int set, int page)
        {
            var names = NamesOf(set);
            return names != null && page >= 0 && page < names.Count && !string.IsNullOrWhiteSpace(names[page]) ? names[page] : "Page " + (page + 1);
        }

        /// <summary>A set's name: its PageSets name, else "Pages" / "Pages 2"...</summary>
        public string SetName(int set) =>
            set > 0 && PageSets != null && set - 1 < PageSets.Count && !string.IsNullOrWhiteSpace(PageSets[set - 1]?.Name) ? PageSets[set - 1].Name
            : set == 0 ? "Pages" : "Pages " + (set + 1);

        private List<string> NamesOf(int set) => set == 0 ? Pages : PageSets != null && set - 1 < PageSets.Count ? PageSets[set - 1]?.Pages : null;
    }

    /// <summary>A set of pages flipped on its own (DashDefinition.PageSets): what it shows ("Laptimes") and its pages' names.</summary>
    public class DashPageSet
    {
        public string Name;
        public List<string> Pages;
    }

    /// <summary>
    /// Pages of a dash: an element is on page N when its Visible holds "page:N"; the values snapshot carries the page shown
    /// now (DashValues.Page), so "page:N" is a condition like any other and the renderer shows/hides a page's elements with
    /// the same repaint as any condition (only the page's area is drawn on a flip). A dash can have more sets of pages,
    /// each flipped on its own: "page2:N" is page N of set 2 (DashDefinition.PageSets), up to "page4:N".
    /// </summary>
    public static class DashPages
    {
        public const string Prefix = "page:";
        /// <summary>Sets of pages a dash can have (set 1 = Pages, sets 2-4 = PageSets).</summary>
        public const int MaxSets = 4;

        public static string Condition(int page) => Condition(0, page);

        /// <summary>The condition for page `page` of set `set` (0 = the first: "page:N"; 1 = "page2:N"...).</summary>
        public static string Condition(int set, int page) =>
            (set == 0 ? "page" : "page" + (set + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) + ":" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static readonly System.Text.RegularExpressions.Regex rx =
            new System.Text.RegularExpressions.Regex(@"^\s*page([2-4])?\s*:\s*(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>A page condition's set (0-3) and page ("page:2" -> 0, 2; "page3:1" -> 2, 1); false for any other.</summary>
        public static bool TryParse(string cond, out int set, out int page)
        {
            set = page = 0;
            var m = cond == null ? null : rx.Match(cond);
            if (m == null || !m.Success || !int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out page)) return false;
            set = m.Groups[1].Success ? m.Groups[1].Value[0] - '1' : 0;
            return true;
        }

        /// <summary>The page in a first-set condition ("page:2" -> 2), or null (also for another set's).</summary>
        public static int? Parse(string cond) => TryParse(cond, out var set, out var p) && set == 0 ? p : (int?)null;

        /// <summary>A page condition of any set ("page:N", "page2:N"...).</summary>
        public static bool IsPage(string cond) => TryParse(cond, out _, out _);

        /// <summary>The page an element is on in the first set, or null (every page).</summary>
        public static int? PageOf(DashElement e) => PageOf(e, 0);

        /// <summary>The page an element is on in set `set`, or null (every page of that set).</summary>
        public static int? PageOf(DashElement e, int set)
        {
            if (e?.Visible == null) return null;
            foreach (var c in e.Visible) if (TryParse(c, out var s, out var p) && s == set) return p;
            return null;
        }

        /// <summary>The sets an element is on a page of.</summary>
        public static IEnumerable<int> SetsOf(DashElement e)
        {
            if (e?.Visible == null) yield break;
            foreach (var c in e.Visible) if (TryParse(c, out var s, out _)) yield return s;
        }

        /// <summary>Never shown together: on two different pages of the same set.</summary>
        public static bool Apart(DashElement a, DashElement b)
        {
            for (int set = 0; set < MaxSets; set++)
            {
                var pa = PageOf(a, set); var pb = PageOf(b, set);
                if (pa.HasValue && pb.HasValue && pa.Value != pb.Value) return true;
            }
            return false;
        }

        /// <summary>Flip `k` of a dash in `v`: page k of every set, each wrapping round its own pages (k from 0 to
        /// FlipCount - 1 shows every page of every set: what Next page does, from the first pages).</summary>
        public static void ShowFlip(DashValues v, DashDefinition d, int k)
        {
            for (int set = 0; set < MaxSets; set++)
            {
                int n = d.PageCountOf(set);
                v.SetPage(set, n > 1 ? ((k % n) + n) % n : 0);
            }
        }

        /// <summary>`page` moved by `step`, wrapping round `count`.</summary>
        public static int Step(int page, int step, int count) => count <= 1 ? 0 : (((page + step) % count) + count) % count;
    }

    public class DashElement
    {
        /// <summary>rect | ellipse | box | gradient | image | label | value | bar | deltabar | popup | dim</summary>
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
            Type == "value" || Type == "bar" || Type == "deltabar" || Type == "popup" || Type == "dim";
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
            list.AddRange(BundledDashes.All(errors));
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (var file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var d = JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(file));
                        if (d?.Elements == null) throw new Exception("no elements");
                        // never half-load a newer format: its new fields would be dropped or misread
                        if (d.FormatVersion > DashDefinition.CurrentFormat)
                            throw new Exception($"made for a newer version of the plugin (dash format {d.FormatVersion}, this plugin reads up to {DashDefinition.CurrentFormat}): update the plugin");
                        d.FilePath = file;
                        if (string.IsNullOrWhiteSpace(d.Id)) d.Id = "file:" + Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Path.GetFileNameWithoutExtension(file);
                        // a file with a bundled dash's Id (installed or updated from the library, or the user's own copy) takes its place
                        list.RemoveAll(x => x.Bundled && x.Id == d.Id);
                        if (list.Any(x => x.Id == d.Id)) d.Id += " (" + Path.GetFileName(file) + ")";
                        list.Add(d);
                    }
                    catch (Exception ex) { errors?.Add(Path.GetFileName(file) + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { errors?.Add(ex.Message); }
            return list;
        }

        private static readonly Dictionary<string, (long Length, DateTime Written, string Id)> idCache = new Dictionary<string, (long, DateTime, string)>();

        /// <summary>
        /// The ids of the dash files in the user's folder, for "already installed" checks. Read from each file's own Id without
        /// loading the dash (a dash with pictures can be 400 KB) and cached by size and time.
        /// </summary>
        public static List<string> LocalIds()
        {
            var ids = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return ids;
                lock (idCache)
                    foreach (var file in Directory.GetFiles(Folder, "*.json"))
                    {
                        var info = new FileInfo(file);
                        if (!idCache.TryGetValue(file, out var c) || c.Length != info.Length || c.Written != info.LastWriteTimeUtc)
                            idCache[file] = c = (info.Length, info.LastWriteTimeUtc, ReadId(file));
                        ids.Add(c.Id);
                    }
            }
            catch { }
            return ids;
        }

        /// <summary>A file's Id the way Load names it (a file without one is "file:name").</summary>
        private static string ReadId(string file)
        {
            try
            {
                using (var r = new JsonTextReader(new StreamReader(file)))
                {
                    if (r.Read() && r.TokenType == JsonToken.StartObject)
                        while (r.Read() && r.TokenType == JsonToken.PropertyName)
                        {
                            bool isId = string.Equals((string)r.Value, "Id", StringComparison.OrdinalIgnoreCase);
                            if (!r.Read()) break;
                            if (isId) { if (r.TokenType == JsonToken.String && !string.IsNullOrWhiteSpace((string)r.Value)) return (string)r.Value; break; }
                            if (r.TokenType == JsonToken.StartObject || r.TokenType == JsonToken.StartArray) r.Skip();
                        }
                }
            }
            catch { }
            return "file:" + Path.GetFileNameWithoutExtension(file);
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
        /// <summary>The version the library's item for the Mustang (same id) carries; the plugin has it built in, so the library shows it as installed.</summary>
        public const string MustangLibraryVersion = "1.1.0";

        public static IEnumerable<DashDefinition> All()
        {
            yield return MustangGt3();
        }

        private static string mustangJson;

        /// <summary>
        /// Redadeg's SimHub dash "LMGT3 Ford Mustang GT3" converted 1:1 (tools/dashes/make_mustang.py writes
        /// Usb/BuiltIn/lmgt3-mustang.json, embedded in the plugin): MAIN with all its overlays, the tyres / delta strip as
        /// two pages, the ignition screens, the headlights' dim. A fresh copy each call (callers may change it).
        /// </summary>
        public static DashDefinition MustangGt3()
        {
            if (mustangJson == null)
                using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("User.FXProRpmSync.BuiltIn." + MustangId + ".json"))
                    mustangJson = st == null ? "" : new StreamReader(st).ReadToEnd();
            var d = mustangJson.Length > 0 ? JsonConvert.DeserializeObject<DashDefinition>(mustangJson) : null;
            if (d?.Elements == null) d = new DashDefinition { Name = "LMGT3 Ford Mustang GT3", Description = "missing from this build of the plugin" };
            d.Id = MustangId;
            d.BuiltIn = true;
            d.ScriptsFolder = null;
            return d;
        }
    }
}
