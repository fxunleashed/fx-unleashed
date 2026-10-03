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
    }

    /// <summary>What the car's dash shows with the pit limiter on.</summary>
    public sealed class CarLimiterLights
    {
        /// <summary>On the rev bar (Lights), or pit lamps of their own elsewhere on the dash (Colour).</summary>
        public bool OnBar;
        public string Colour;
        public List<(double Pos, string Colour)> Lights = new List<(double, string)>();
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
            if (c["rev"] is JObject r && r["range"] is JArray range && range.Count == 2)
            {
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
                if (rev.Lights.Count > 0 && !stacked) rec.Rev = rev;
            }
            if (c["limiter"] is JObject lim)
            {
                var l = new CarLimiterLights { OnBar = (bool?)lim["onBar"] ?? false, Colour = (string)lim["colour"] };
                foreach (JObject x in (lim["leds"] as JArray ?? new JArray()).OfType<JObject>())
                    l.Lights.Add(((double)x["pos"], (string)x["colour"]));
                if (l.OnBar ? l.Lights.Any(x => x.Colour != null) : l.Colour != null) rec.Limiter = l;
            }
            return rec;
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
            slots = Slots(rev.Lights.Select(l => l.Pos).ToList());
            var ordered = rev.Lights.OrderBy(l => l.Pos).ToList();
            // The flash: every light ends in one colour, and the last step turns the others to it (lights already in
            // that colour don't list the step again, like the M4 GT3's centre reds).
            var last = ordered.Select(l => l.Stages[l.Stages.Count - 1]).ToList();
            int flashRpm = last.Max(s => s.Rpm);
            string flashColour = last.First(s => s.Rpm == flashRpm).Colour;
            bool flash = ordered.Count > 1 && flashColour != null
                         && last.All(s => string.Equals(s.Colour, flashColour, StringComparison.OrdinalIgnoreCase))
                         && ordered.Any(l => l.Stages.Count > 1 && l.Stages[l.Stages.Count - 1].Rpm == flashRpm);
            if (flash) last = last.Select(_ => (flashRpm, flashColour)).ToList();
            int n = slots.Count;
            var colours = new string[n + 1];
            var curve = new int[n + 1];
            var offAt = new int[n + 1];
            colours[0] = flash ? Argb(last[0].Colour) : "#00000000";
            curve[0] = flash ? last[0].Rpm : 0;
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (slots[i] == null) { colours[i + 1] = "#00000000"; continue; }
                var light = ordered[k++];
                var first = light.Stages[0];
                colours[i + 1] = Argb(first.Colour);
                curve[i + 1] = first.Rpm;
                // a later stage with no colour switches it off (before the flash, which lights everything)
                var dark = light.Stages.Skip(1).FirstOrDefault(st => st.Colour == null);
                if (dark.Rpm > 0 && (!flash || dark.Rpm < last[0].Rpm)) offAt[i + 1] = dark.Rpm;
            }
            return new CarLedProfile
            {
                CarName = name, LedNumber = n, Colors = colours,
                GearRpm = new Dictionary<string, int[]> { ["1"] = curve },
                OffRpm = offAt.Any(v => v > 0) ? offAt : null,
                MatchedBy = "the game's own dash",
            };
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
            var pattern = new List<string>();
            if (car.Rev != null)
            {
                var profile = ToProfile(car.Rev, car.CarId, out var slots);
                var map = RpmLayout.SourceMap(profile, profile.GearRpm["1"]);
                double tol = Tolerance(slots.Where(s => s != null).Select(s => s.Value).ToList());
                for (int j = 0; j < w; j++)
                {
                    var at = slots[map[j] - 1];
                    pattern.Add(at == null ? "#000000" : Nearest(lim.Lights, at.Value, tol));
                }
            }
            else
            {
                double tol = Tolerance(lim.Lights.Select(l => l.Pos).ToList());
                for (int j = 0; j < w; j++) pattern.Add(Nearest(lim.Lights, w == 1 ? 0.5 : (double)j / (w - 1), tol));
            }
            return new LimiterLook { Style = LimiterStyle.CarPattern, Pattern = pattern, Colors = new List<string> { "#0040FF", "#000000" }, FromGame = true };
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
