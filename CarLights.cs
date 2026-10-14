using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    /// <summary>One light on a car's dash rev bar: where along the bar (0-1) and when it lights in what colour.</summary>
    public sealed class CarRevLight
    {
        public double Pos;
        /// <summary>(rpm, colour) from the first to the last change, e.g. green at 6200, red at 7000 (the flash).</summary>
        public List<(int Rpm, string Colour)> Stages = new List<(int, string)>();
    }

    public sealed class CarRev
    {
        public int Low, High, Steps;
        public List<CarRevLight> Lights = new List<CarRevLight>();
        /// <summary>Some colours couldn't be read from the game and follow the usual green, yellow, red.</summary>
        public bool ColourGuessed;
        /// <summary>Optional: the lights per gear ("R", "N", "1"..) when they differ (iRacing); this object is then the
        /// curve for wheels that show one.</summary>
        public Dictionary<string, CarRev> Gears;
        /// <summary>Optional: from this rpm the row blinks (0 = it doesn't), a whole on/off cycle taking BlinkPeriodMs.</summary>
        public int BlinkRpm, BlinkPeriodMs;
        /// <summary>The colour every light shows in the blink's lit frame when it is one colour (the IR18's blue), else null:
        /// the lights blink as they are.</summary>
        public string BlinkColour;
        /// <summary>The blink as the dash plays it: positions of the row's lights and one cycle of frames over them.</summary>
        public List<double> BlinkPos;
        public List<BlinkFrame> BlinkFrames;
    }

    /// <summary>One frame of a blink: how long it shows (seconds) and the colour of each light (null = dark).</summary>
    public sealed class BlinkFrame
    {
        public double Seconds;
        public string[] Colours;

        /// <summary>The frame showing `t` seconds into a cycle of these frames.</summary>
        public static BlinkFrame At(IList<BlinkFrame> frames, double t)
        {
            double period = frames.Sum(f => f.Seconds);
            if (period <= 0) return frames[0];
            double x = t % period;
            if (x < 0) x += period;
            foreach (var f in frames)
            {
                if (x < f.Seconds) return f;
                x -= f.Seconds;
            }
            return frames[frames.Count - 1];
        }

        internal static List<BlinkFrame> Parse(JToken frames)
        {
            var list = (frames as JArray)?.OfType<JObject>()
                .Select(f => new BlinkFrame { Seconds = (double?)f["s"] ?? 0, Colours = (f["leds"] as JArray ?? new JArray()).Select(x => (string)x).ToArray() })
                .Where(f => f.Seconds > 0).ToList();
            return list != null && list.Count > 1 ? list : null;
        }
    }

    /// <summary>What the car's dash shows with the pit limiter on.</summary>
    public sealed class CarLimiterLights
    {
        /// <summary>On the rev bar (Lights), or pit lamps of their own elsewhere on the dash (Colour).</summary>
        public bool OnBar;
        public string Colour;
        public List<(double Pos, string Colour)> Lights = new List<(double, string)>();
        /// <summary>Optional (iRacing): the lights blink, one cycle of frames over the same positions as Lights.</summary>
        public List<BlinkFrame> Frames;
        /// <summary>Optional (iRacing): what the row shows at each speed step, slowest first.</summary>
        public List<CarLimiterStep> BySpeed;
    }

    /// <summary>One of the limiter's speed steps: from what speed (null = any slower; Rel = km/h over the pit speed limit,
    /// else an absolute km/h) the row shows these lights (and blinks with these frames).</summary>
    public sealed class CarLimiterStep
    {
        public double? From;
        public bool Rel;
        public List<(double Pos, string Colour)> Lights = new List<(double, string)>();
        public List<BlinkFrame> Frames;
    }

    public sealed class CarLightsRecord
    {
        public string CarId, Class, Manufacturer, Dash;
        public List<string> Aliases = new List<string>();
        public CarRev Rev;
        public CarLimiterLights Limiter;
    }

    /// <summary>A car found in the game data, with where it came from.</summary>
    public sealed class CarLightsMatch
    {
        public CarLightsRecord Car;
        public string Game, GameVersion;
        /// <summary>The name the game reported when it isn't the car's own (a variant suffix, e.g. "... Model1"), else null.</summary>
        public string ReportedAs;
        public string Label => $"{GameNames.Short(Game)} {GameVersion}".Trim();
    }

    public static class GameNames
    {
        public static string Short(string game) => game == "ams2" ? "AMS2" : game == "iracing" ? "iRacing" : game;
    }

    /// <summary>
    /// Per-car rev lights and pit limiter lights taken from the games' own files (docs/car-lights-format.md): the
    /// library repo's cars/index.json and cars/&lt;game&gt;.json, made by the extraction tools for every game version.
    /// Downloaded in the background, checked against the index's sha256, cached on disk; a newer file is picked up
    /// on the next refresh (Changed fires, the plugin re-applies the car). Preferred over Lovely Car Data.
    /// </summary>
    public sealed class CarLightsDatabase
    {
        public const int CurrentSchema = 1;
        public const int MaxGameFileBytes = 8 * 1024 * 1024;
        public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);

        private readonly Func<string> libraryUrl;
        private readonly string folder;
        private readonly object sync = new object();
        private Dictionary<string, GameData> games = new Dictionary<string, GameData>();
        private DateTime lastRefreshUtc = DateTime.MinValue;
        private Task refreshing;

        /// <summary>New data arrived (a first download or a new game version).</summary>
        public event Action Changed;
        public string LastError { get; private set; }

        private sealed class GameData
        {
            public string Game, SimHubGame, GameVersion, Sha256;
            public Dictionary<string, CarLightsRecord> ByKey = new Dictionary<string, CarLightsRecord>(StringComparer.OrdinalIgnoreCase);
        }

        public CarLightsDatabase(Func<string> libraryUrl, string cacheDir)
        {
            this.libraryUrl = libraryUrl;
            folder = Path.Combine(cacheDir, "CarLights");
            LoadCached();
        }

        /// <summary>The car, looked up by SimHub's game name and car id (or model, or one of its aliases); null if unknown.
        /// Starts a background refresh when the data is older than RefreshEvery.</summary>
        public CarLightsMatch Find(string simHubGame, string carId, string carModel = null)
        {
            RefreshIfDue();
            lock (sync)
            {
                var g = games.Values.FirstOrDefault(x => string.Equals(x.SimHubGame, simHubGame, StringComparison.OrdinalIgnoreCase));
                if (g == null) return null;
                foreach (var key in new[] { carId, carModel })
                    if (!string.IsNullOrEmpty(key) && g.ByKey.TryGetValue(key.Trim(), out var car))
                        return new CarLightsMatch { Car = car, Game = g.Game, GameVersion = g.GameVersion };
                foreach (var key in new[] { carId, carModel })
                {
                    var prefix = ByPrefix(g.ByKey, key);
                    if (prefix == null) continue;
                    if (loggedPrefix.Add(key))
                        SimHub.Logging.Current.Info($"[FXProRpmSync] car light data: {g.Game} reports \"{key.Trim()}\", matched to \"{prefix.CarId}\" (add it as an alias)");
                    return new CarLightsMatch { Car = prefix, Game = g.Game, GameVersion = g.GameVersion, ReportedAs = key.Trim() };
                }
                return null;
            }
        }

        private readonly HashSet<string> loggedPrefix = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A name the game reports with a variant word after the car's own name (AMS2's "Formula V8 Gen2 Model1" for
        /// "Formula V8 Gen2"): the longest known name it starts with, when what follows is one short word. Exact names
        /// always win before this is tried.
        /// </summary>
        internal static CarLightsRecord ByPrefix(Dictionary<string, CarLightsRecord> byKey, string reported)
        {
            var name = reported?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            string best = null;
            foreach (var k in byKey.Keys)
            {
                if (k.Length >= name.Length || !name.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase)) continue;
                var rest = name.Substring(k.Length + 1).Trim();
                if (rest.Length == 0 || rest.Length > 12 || rest.Contains(" ")) continue;
                if (best == null || k.Length > best.Length) best = k;
            }
            return best == null ? null : byKey[best];
        }

        public void RefreshIfDue()
        {
            lock (sync)
            {
                if (refreshing != null && !refreshing.IsCompleted) return;
                if (DateTime.UtcNow - lastRefreshUtc < RefreshEvery) return;
                lastRefreshUtc = DateTime.UtcNow;
                refreshing = Task.Run(() => Refresh());
            }
        }

        /// <summary>Fetches the index and every game file whose sha256 changed. True when new data was loaded.</summary>
        public bool Refresh()
        {
            try
            {
                var client = new LibraryClient(libraryUrl?.Invoke());
                var index = JObject.Parse(Encoding.UTF8.GetString(client.GetFile("cars/index.json", 256 * 1024)));
                if ((int?)index["schema"] > CurrentSchema) { LastError = "the car data needs a newer plugin"; return false; }
                bool any = false;
                foreach (var p in ((JObject)index["games"])?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    string game = p.Name, file = (string)p.Value["file"], sha = (string)p.Value["sha256"];
                    if (!System.Text.RegularExpressions.Regex.IsMatch(game ?? "", "^[a-z0-9]{2,20}$") || file != $"cars/{game}.json" || sha == null) continue;
                    lock (sync) if (games.TryGetValue(game, out var have) && string.Equals(have.Sha256, sha, StringComparison.OrdinalIgnoreCase)) continue;
                    var data = client.GetFile(file, MaxGameFileBytes);
                    if (!string.Equals(Sha256(data), sha, StringComparison.OrdinalIgnoreCase)) throw new Exception(file + " doesn't match the index's checksum");
                    var parsed = Parse(Encoding.UTF8.GetString(data), sha);
                    if (parsed == null) continue;
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, game + ".json"), data);
                    lock (sync) games[game] = parsed;
                    any = true;
                }
                LastError = null;
                if (any) Changed?.Invoke();
                return any;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SimHub.Logging.Current.Warn($"[FXProRpmSync] car light data: {ex.Message}");
                return false;
            }
        }

        private void LoadCached()
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var data = File.ReadAllBytes(f);
                    var g = Parse(Encoding.UTF8.GetString(data), Sha256(data));
                    if (g != null) games[g.Game] = g;
                }
                catch (Exception ex) { SimHub.Logging.Current.Warn($"[FXProRpmSync] cached car light data {Path.GetFileName(f)}: {ex.Message}"); }
            }
        }

        /// <summary>For tests: loads a game file directly.</summary>
        public void Load(string json)
        {
            var g = Parse(json, Sha256(Encoding.UTF8.GetBytes(json)));
            if (g != null) lock (sync) games[g.Game] = g;
        }

        private static GameData Parse(string json, string sha)
        {
            var d = JObject.Parse(json);
            if ((int?)d["schema"] > CurrentSchema) return null;
            var g = new GameData { Game = (string)d["game"], SimHubGame = (string)d["simhubGame"], GameVersion = (string)d["gameVersion"], Sha256 = sha };
            if (string.IsNullOrEmpty(g.Game) || string.IsNullOrEmpty(g.SimHubGame)) return null;
            foreach (JObject c in (d["cars"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (c["missingSince"] != null) continue; // no longer in the game
                var rec = ParseCar(c);
                if (rec?.CarId == null) continue;
                g.ByKey[rec.CarId] = rec;
                foreach (var a in rec.Aliases) if (!g.ByKey.ContainsKey(a)) g.ByKey[a] = rec;
            }
            return g;
        }

        internal static CarLightsRecord ParseCar(JObject c)
        {
            var rec = new CarLightsRecord
            {
                CarId = (string)c["carId"], Class = (string)c["class"], Manufacturer = (string)c["manufacturer"], Dash = (string)c["dash"],
                Aliases = (c["aliases"] as JArray)?.Select(a => (string)a).Where(a => !string.IsNullOrEmpty(a)).ToList() ?? new List<string>(),
            };
            if (c["rev"] is JObject r)
            {
                var rev = ParseRev(r);
                if (rev != null && r["gears"] is JObject gears)
                {
                    foreach (var g in gears.Properties())
                        if (g.Value is JObject go && ParseRev(go) is CarRev gr)
                            (rev.Gears ?? (rev.Gears = new Dictionary<string, CarRev>()))[g.Name] = gr;
                }
                rec.Rev = rev;
            }
            if (c["limiter"] is JObject lim)
            {
                var l = new CarLimiterLights { OnBar = (bool?)lim["onBar"] ?? false, Colour = (string)lim["colour"] };
                foreach (JObject x in (lim["leds"] as JArray ?? new JArray()).OfType<JObject>())
                    l.Lights.Add(((double)x["pos"], (string)x["colour"]));
                l.Frames = BlinkFrame.Parse(lim["blink"]?["frames"]);
                foreach (JObject st in (lim["bySpeed"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var step = new CarLimiterStep { From = (double?)st["from"], Rel = (bool?)st["rel"] ?? false, Frames = BlinkFrame.Parse(st["blink"]?["frames"]) };
                    foreach (JObject x in (st["leds"] as JArray ?? new JArray()).OfType<JObject>())
                        step.Lights.Add(((double)x["pos"], (string)x["colour"]));
                    (l.BySpeed ?? (l.BySpeed = new List<CarLimiterStep>())).Add(step);
                }
                if (l.OnBar ? l.Lights.Any(x => x.Colour != null) || l.Frames != null : l.Colour != null) rec.Limiter = l;
            }
            return rec;
        }

        /// <summary>One rev curve (the car's, or one gear's): range, lights, blink. Null when it has no usable lights.</summary>
        private static CarRev ParseRev(JObject r)
        {
            if (!(r["range"] is JArray range) || range.Count != 2) return null;
            var rev = new CarRev { Low = (int)range[0], High = (int)range[1], Steps = (int?)r["steps"] ?? 0, ColourGuessed = (bool?)r["colourGuessed"] ?? false };
            foreach (JObject l in (r["leds"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var light = new CarRevLight { Pos = (double)l["pos"] };
                foreach (JArray st in (l["stages"] as JArray ?? new JArray()).OfType<JArray>())
                    if (st.Count == 2) light.Stages.Add(((int)Math.Round((double)st[0]), (string)st[1]));
                if (light.Stages.Count > 0) rev.Lights.Add(light);
            }
            // Lights sharing a position can't be laid out along the bar (the Ligier JS P320 came out as five lights stacked
            // at one end and one at the other, which lit the wheel lopsided): no rev data, so Lovely Car Data or the
            // preset's pattern is used for the car instead.
            bool stacked = rev.Lights.Count > 1 && rev.Lights.Select(l => Math.Round(l.Pos, 3)).Distinct().Count() < rev.Lights.Count;
            if (rev.Lights.Count == 0 || stacked) return null;
            // the first blink: from its rpm the row goes on and off (or between two pictures); the lit frame is the fuller one
            if ((r["blink"] as JArray)?.OfType<JObject>().FirstOrDefault() is JObject b && b["frames"] is JArray frames && frames.Count > 1)
            {
                rev.BlinkRpm = (int)Math.Round((double?)b["rpm"] ?? 0);
                rev.BlinkPeriodMs = (int)Math.Round(((double?)b["period"] ?? 0) * 1000);
                var lit = frames.OfType<JObject>().Select(f => (f["leds"] as JArray ?? new JArray()).Select(x => (string)x).Where(x => x != null).ToList())
                                .OrderByDescending(f => f.Count).First();
                if (lit.Count > 0 && lit.All(x => string.Equals(x, lit[0], StringComparison.OrdinalIgnoreCase))) rev.BlinkColour = lit[0];
                rev.BlinkPos = (b["pos"] as JArray)?.Select(x => (double)x).ToList();
                rev.BlinkFrames = BlinkFrame.Parse(frames);
                if (rev.BlinkPos == null || rev.BlinkFrames == null || rev.BlinkFrames.Any(f => f.Colours.Length != rev.BlinkPos.Count))
                    { rev.BlinkPos = null; rev.BlinkFrames = null; }
                if (rev.BlinkRpm <= 0 || rev.BlinkPeriodMs <= 0) { rev.BlinkRpm = 0; rev.BlinkPeriodMs = 0; rev.BlinkColour = null; rev.BlinkFrames = null; }
            }
            return rev;
        }

        private static string Sha256(byte[] data)
        {
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        // ---------- to the plugin's own models ----------

        /// <summary>
        /// The car's lights as a car profile (the same model Lovely Car Data gives, so RpmLayout.FromProfile maps it onto
        /// the wheel the same way): one entry per light in bar order, with an unused entry for each gap between groups
        /// (so a gap stays one LED wide on the wheel), and the flash when every light ends in the same colour at the
        /// same rpm (the M4 GT3's all-red step).
        /// </summary>
        public static CarLedProfile ToProfile(CarRev rev, string name, out List<double?> slots)
        {
            // every gear's lights share the row: the slots come from all the positions any gear lights
            var all = new List<CarRev> { rev };
            if (rev.Gears != null) all.AddRange(rev.Gears.Values);
            var positions = new List<double>();
            foreach (var p in all.SelectMany(r => r.Lights).Select(l => l.Pos).OrderBy(x => x))
                if (positions.Count == 0 || p - positions[positions.Count - 1] > 1e-3) positions.Add(p);
            slots = Slots(positions);
            int n = slots.Count;

            var main = Curve(rev, slots, out var colours, out var offAt, out int blinkMs);
            var gearRpm = new Dictionary<string, int[]>();
            if (rev.Gears != null && rev.Gears.Count > 0)
            {
                foreach (var g in rev.Gears)
                {
                    var curve = Curve(g.Value, slots, out var gc, out _, out int gb);
                    // a light this gear never lights stays dark (0 would mean "lit from idle" further on)
                    for (int i = 1; i <= n; i++)
                    {
                        if (colours[i] == Transparent && gc[i] != Transparent) colours[i] = gc[i];
                        if (curve[i] == 0 && gc[i] == Transparent && slots[i - 1] != null) curve[i] = NeverRpm;
                    }
                    if (curve[0] == 0 && main[0] > 0) curve[0] = NeverRpm;
                    blinkMs = Math.Max(blinkMs, gb);
                    gearRpm[g.Key == "all" ? "1" : g.Key] = curve;
                }
            }
            else gearRpm["1"] = main;
            // the blink frame by frame, over the slots (the main curve's; every gear's blink looks the same)
            List<BlinkFrame> frames = null;
            if (rev.BlinkFrames != null && main[0] > 0)
            {
                var at = slots.Select(s => s == null ? -1 : rev.BlinkPos.FindIndex(p => Math.Abs(p - s.Value) <= 1e-3)).ToList();
                frames = rev.BlinkFrames.Select(f => new BlinkFrame
                {
                    Seconds = f.Seconds,
                    Colours = new string[] { null }.Concat(at.Select(k => k < 0 ? null : f.Colours[k])).ToArray(),
                }).ToList();
            }
            return new CarLedProfile
            {
                CarName = name, LedNumber = n, Colors = colours,
                GearRpm = gearRpm,
                MainCurve = rev.Gears != null && rev.Gears.Count > 0 ? main : null,
                FlashFrames = frames,
                RedlineBlinkIntervalMs = blinkMs,
                OffRpm = offAt.Any(v => v > 0) ? offAt : null,
                MatchedBy = "the game's own dash",
            };
        }

        /// <summary>An rpm no engine reaches: a light that a gear never lights.</summary>
        public const int NeverRpm = 99999;
        private const string Transparent = "#00000000";

        /// <summary>
        /// One curve over the row's slots: [0] = the flash rpm (0 = none), [1..] = when each slot's light first comes on
        /// (0 = no light there). colours[0] = the flash colour (transparent: the lights blink as they are, or no flash).
        /// </summary>
        private static int[] Curve(CarRev rev, List<double?> slots, out string[] colours, out int[] offAt, out int blinkMs)
        {
            var ordered = rev.Lights.OrderBy(l => l.Pos).ToList();
            // The flash: every light ends in one colour, and the last step turns the others to it (lights already in
            // that colour don't list the step again, like the M4 GT3's centre reds).
            var last = ordered.Select(l => l.Stages[l.Stages.Count - 1]).ToList();
            int flashRpm = last.Max(s => s.Rpm);
            string flashColour = last.First(s => s.Rpm == flashRpm).Colour;
            bool flash = ordered.Count > 1 && flashColour != null
                         && last.All(s => string.Equals(s.Colour, flashColour, StringComparison.OrdinalIgnoreCase))
                         && ordered.Any(l => l.Stages.Count > 1 && l.Stages[l.Stages.Count - 1].Rpm == flashRpm);
            int n = slots.Count;
            colours = new string[n + 1];
            var curve = new int[n + 1];
            offAt = new int[n + 1];
            colours[0] = flash ? Argb(flashColour) : Transparent;
            curve[0] = flash ? flashRpm : 0;
            blinkMs = 0;
            if (rev.BlinkRpm > 0)
            {
                // half the period: the time between the lights going off and coming back, as Lovely's redlineBlinkInterval
                blinkMs = Math.Max(1, rev.BlinkPeriodMs / 2);
                if (!flash)
                {
                    // blinks in one colour of its own (the IR18's blue), or as it is (the colour stays transparent)
                    curve[0] = rev.BlinkRpm;
                    bool own = rev.BlinkColour != null && ordered.Any(l => !string.Equals(l.Stages[0].Colour, rev.BlinkColour, StringComparison.OrdinalIgnoreCase));
                    colours[0] = own ? Argb(rev.BlinkColour) : Transparent;
                }
            }
            for (int i = 0; i < n; i++)
            {
                colours[i + 1] = Transparent;
                if (slots[i] == null) continue;
                var light = ordered.FirstOrDefault(l => Math.Abs(l.Pos - slots[i].Value) <= 1e-3);
                if (light == null) continue;
                var first = light.Stages[0];
                colours[i + 1] = Argb(first.Colour);
                curve[i + 1] = first.Rpm;
                // a later stage with no colour switches it off (before the flash, which lights everything)
                var dark = light.Stages.Skip(1).FirstOrDefault(st => st.Colour == null);
                if (dark.Rpm > 0 && (curve[0] == 0 || dark.Rpm < curve[0])) offAt[i + 1] = dark.Rpm;
            }
            return curve;
        }

        /// <summary>Positions in bar order with a null (an unused LED) for each missing light in a gap: a gap of about
        /// two spacings is one unused LED, three is two, and so on.</summary>
        internal static List<double?> Slots(List<double> positions)
        {
            var p = positions.OrderBy(x => x).ToList();
            var slots = new List<double?>();
            if (p.Count == 0) return slots;
            var gaps = p.Zip(p.Skip(1), (a, b) => b - a).Where(d => d > 1e-4).OrderBy(d => d).ToList();
            double step = gaps.Count > 0 ? gaps[gaps.Count / 2] : 1;
            slots.Add(p[0]);
            for (int i = 1; i < p.Count; i++)
            {
                int missing = (int)Math.Round((p[i] - p[i - 1]) / step) - 1;
                for (int m = 0; m < Math.Min(missing, 4); m++) slots.Add(null);
                slots.Add(p[i]);
            }
            return slots;
        }

        /// <summary>
        /// The car's own pit limiter lights as a limiter look: pit lamps of their own = the whole bar in their colour;
        /// on the rev bar = its pattern, laid on the wheel's rev LEDs through the same mapping as the rev lights.
        /// </summary>
        public static LimiterLook ToLimiter(CarLightsRecord car)
        {
            var lim = car?.Limiter;
            if (lim == null) return null;
            if (!lim.OnBar) return new LimiterLook { Style = LimiterStyle.Solid, Colors = new List<string> { Hex(lim.Colour), "#000000" }, FromGame = true };
            int w = RpmLightsMapper.WheelLeds;
            // the wheel LED each of its 15 rev LEDs takes its colour from: a position along the car's row
            var spots = new double?[w];
            double tol;
            if (car.Rev != null)
            {
                var profile = ToProfile(car.Rev, car.CarId, out var slots);
                tol = Tolerance(slots.Where(s => s != null).Select(s => s.Value).ToList());
                if (slots.Count <= w && (lim.Frames != null || lim.BySpeed != null))
                {
                    // an animated pattern: one wheel LED per light of the car's row, the row centred on the wheel, so it shows as many lights
                    // as the car's (the AMG GT3's six swapping lights stay six, not stretched to seven with a pair doubled); a steady
                    // one keeps the rev bar's stretch (checked in AMS2: the M4 GT3's limiter sits on its rev lights)
                    int offset = (w - slots.Count) / 2;
                    for (int j = 0; j < w; j++) spots[j] = j - offset >= 0 && j - offset < slots.Count ? slots[j - offset] : null;
                }
                else
                {
                    var map = RpmLayout.SourceMap(profile, profile.GearRpm.TryGetValue("1", out var first) ? first : profile.GearRpm.Values.First());
                    for (int j = 0; j < w; j++) spots[j] = slots[map[j] - 1];
                }
            }
            else
            {
                tol = Tolerance(lim.Lights.Select(l => l.Pos).ToList());
                for (int j = 0; j < w; j++) spots[j] = w == 1 ? 0.5 : (double)j / (w - 1);
            }
            List<string> Lay(List<(double Pos, string Colour)> lights) =>
                spots.Select(at => at == null ? "#000000" : Nearest(lights, at.Value, tol)).ToList();
            List<LimiterFrame> Frames(List<BlinkFrame> frames, List<(double Pos, string Colour)> lights) =>
                frames?.Select(f => new LimiterFrame
                {
                    Seconds = f.Seconds,
                    Pattern = Lay(lights.Select((l, i) => (l.Pos, i < f.Colours.Length ? f.Colours[i] : null)).ToList()),
                }).ToList();
            return new LimiterLook
            {
                Style = LimiterStyle.CarPattern, Pattern = Lay(lim.Lights), Colors = new List<string> { "#0040FF", "#000000" }, FromGame = true,
                Frames = Frames(lim.Frames, lim.Lights),
                BySpeed = lim.BySpeed?.Select(st => new LimiterSpeedLook { From = st.From, Rel = st.Rel, Pattern = Lay(st.Lights), Frames = Frames(st.Frames, st.Lights) }).ToList(),
            };
        }

        private static double Tolerance(List<double> positions)
        {
            var p = positions.OrderBy(x => x).ToList();
            var gaps = p.Zip(p.Skip(1), (a, b) => b - a).Where(d => d > 1e-4).OrderBy(d => d).ToList();
            return gaps.Count > 0 ? gaps[gaps.Count / 2] * 0.55 : 1;
        }

        private static string Nearest(List<(double Pos, string Colour)> lights, double pos, double tol)
        {
            var best = lights.OrderBy(l => Math.Abs(l.Pos - pos)).FirstOrDefault();
            return lights.Count > 0 && Math.Abs(best.Pos - pos) <= tol && best.Colour != null ? Hex(best.Colour) : "#000000";
        }

        private static string Hex(string c) => string.IsNullOrEmpty(c) ? "#000000" : c.ToUpperInvariant();
        private static string Argb(string c) => c != null && c.Length == 7 ? "#FF" + c.Substring(1).ToUpperInvariant() : "#00000000";
    }
}
