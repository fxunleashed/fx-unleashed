using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using NCalc;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>
    /// SimHub formulas ("ncalc:" / "js:" bindings of imported dashes) for the demo, without a game or SimHub: evaluates
    /// them with the formula engines SimHub ships (NCalc, Jint), SimHub's formula functions re-implemented here, and
    /// SimHub properties simulated from the demo lap (DemoProps). Live, SimHub's own engine evaluates them instead
    /// (SimHubFormulas). A formula that needs something the demo can't make up says so (Known = false), and the demo
    /// then shows the element's preview text instead (UsbDemo).
    /// </summary>
    internal sealed class DemoFormulas
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private readonly DemoProps props = new DemoProps();
        private readonly Dictionary<string, Expression> ncalc = new Dictionary<string, Expression>();
        private readonly HashSet<string> broken = new HashSet<string>();
        private readonly Dictionary<string, (double At, object Value)> changes = new Dictionary<string, (double, object)>();
        private readonly Dictionary<string, object> last = new Dictionary<string, object>();
        private readonly string scriptsFolder;
        private Engine js;
        private bool jsFailed;
        private DashValues v = new DashValues();
        private double t;
        private bool known;
        private string current;

        public DemoFormulas(string scriptsFolder) { this.scriptsFolder = scriptsFolder; }

        /// <summary>
        /// A "prop:" / "ncalc:" / "js:" binding's value in the demo now. known = false when it used a property or
        /// function the demo can't simulate, or failed: the value then means nothing.
        /// </summary>
        public object Eval(string bind, DashValues values, double time, out bool isKnown)
        {
            v = values; t = time; known = true; current = bind;
            object r = null;
            try
            {
                if (bind.StartsWith("prop:", StringComparison.OrdinalIgnoreCase)) r = Prop(bind.Substring(5));
                else if (broken.Contains(bind)) known = false;
                else if (bind.StartsWith("ncalc:", StringComparison.OrdinalIgnoreCase)) r = Ncalc(bind);
                else if (bind.StartsWith("js:", StringComparison.OrdinalIgnoreCase)) r = Js(bind);
                else known = false;
            }
            catch { known = false; r = null; }
            isKnown = known;
            return r;
        }

        private object Prop(string name)
        {
            var o = props.Get(name, v, t, out bool k);
            if (!k) known = false;
            return o;
        }

        // ---------- NCalc ----------

        private object Ncalc(string bind)
        {
            if (!ncalc.TryGetValue(bind, out var e))
            {
                e = new Expression(bind.Substring(6), EvaluateOptions.IgnoreCase, Ci);
                if (e.HasErrors()) { broken.Add(bind); known = false; return null; }
                e.EvaluateParameter += (name, a) => a.Result = Prop(name);
                e.EvaluateFunction += (name, a) =>
                {
                    string n = name.ToLowerInvariant();
                    if (!Functions.Contains(n)) return; // NCalc's own (if, in, abs, round...)
                    var args = a.Parameters.Select(p => p.Evaluate()).ToArray();
                    string site = n + "(" + string.Join(",", a.Parameters.Select(p => p.ParsedExpression?.ToString())) + ")";
                    a.Result = Call(n, args, site);
                };
                ncalc[bind] = e;
            }
            return e.Evaluate();
        }

        // ---------- Javascript ----------

        private object Js(string bind)
        {
            var engine = JsEngine();
            if (engine == null) { known = false; return null; }
            // SimHub gives each formula its own `root`, an object kept between its runs (scripts remember a trigger in it:
            // Redadeg's LIFT); without it they failed here, and their condition counted as false
            if (!roots.TryGetValue(bind, out var root)) roots[bind] = root = engine.Evaluate("({})");
            engine.SetValue("root", root);
            string code = bind.Substring(3);
            // SimHub runs a JS formula as a function body; a bare expression works too
            if (Regex.IsMatch(code, @"\breturn\b")) code = "(function(){\n" + code + "\n})()";
            var r = engine.Evaluate(code);
            return r.IsUndefined() || r.IsNull() ? null : r.ToObject();
        }

        /// <summary>Each js: formula's own `root` object (SimHub's per-formula state), by its text.</summary>
        private readonly Dictionary<string, JsValue> roots = new Dictionary<string, JsValue>();

        private Engine JsEngine()
        {
            if (js != null || jsFailed) return js;
            try
            {
                var e = new Engine(o =>
                {
                    o.TimeoutInterval(TimeSpan.FromMilliseconds(250)).LimitRecursion(64);
                    o.TimeSystem = new DemoClock(() => t); // scripts' Date follows the demo's time: runs repeat exactly
                });
                e.SetValue("$prop", new Func<string, object>(Prop));
                // a seeded Math.random: scripts that pick at random (blinking, effects) do the same every run
                e.Execute("Math.random = (function () { var s = 20260927; return function () { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; }; })();");
                foreach (var f in Functions)
                {
                    string n = f;
                    e.SetValue(n, new ClrFunction(e, n, (self, a) =>
                        JsValue.FromObject(e, Call(n, a.Select(x => x.IsUndefined() ? null : x.ToObject()).ToArray(), current + "|" + n + "|" + (a.Length > 1 ? a[1].ToString() : "")))));
                }
                // the helper functions dashes call: SimHub's own extensions, then the dash's
                foreach (var dir in new[] { Path.Combine(DashLibrary.SimHubFolder, "JavascriptExtensions"), scriptsFolder })
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        foreach (var file in Directory.GetFiles(dir, "*.js").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                            try { e.Execute(File.ReadAllText(file)); } catch { }
                js = e;
            }
            catch { jsFailed = true; }
            return js;
        }

        // ---------- SimHub's formula functions ----------

        /// <summary>SimHub's own formula functions, made up for the demo (NCalc's built-ins like if/round are NCalc's).</summary>
        private static readonly HashSet<string> Functions = new HashSet<string>
        {
            "isnull", "format", "left", "right", "padleft", "replace", "lcase", "ucase", "tcase",
            "secondstotimespan", "timespantoseconds", "toshorttime", "changed", "isincreasing", "isdecreasing",
            "maximum", "minimum", "blink", "rand", "loopingtimer", "prop", "getrpmtickvalue", "inertia", "scroll",
            "timesincelastevent", "dashboardwidth", "dashboardheight", "repeatindex", "isbuttonpressed",
            "currentlapgetsectortime", "lastlapgetsectortime", "bestsectortime", "sessionbestlapgetsectortime",
            "getbestsplittime", "getbestsplittime_playerclassonly",
            "getplayerleaderboardposition", "getopponentleaderboardposition_aheadbehind",
            "getopponentleaderboardposition_aheadbehind_playerclassonly", "getopponentleaderboardposition_playerclassonly",
            "getbestlapopponentleaderboardposition", "getbestlapopponentleaderboardposition_playerclassonly",
            "driveravailable", "driverisplayer", "drivername", "driverinitials", "drivershortname", "drivercarclass",
            "driverbestlap", "driverlastlap", "drivercurrentlaptime", "driverdeltatoplayer", "driverdeltatobest",
            "drivercurrentlap", "drivercarnumber", "driveriscarinpit", "driveriscarinpitlane", "driveriscaringarage",
            "driverisconnected", "driverisoutlap", "driverposition", "driverclassposition", "drivergaptoplayer",
            "driverrelativegaptoplayer", "drivergaptoleader", "drivergaptoclassleader", "driverlapstoplayer",
            "driverlapstoleader", "driverlapstoclassleader", "drivertrackpositionpercent", "driverlapsdonesincelastpitout",
            "driverpitcount", "drivercurrentsector", "driverspeed", "drivercarname", "driverteamname",
            "driveriracingirating", "driverlicencestring", "drivercarclasscolor", "drivercarclasstextcolor",
            "driverfronttyrecompound", "driverreartyrecompound", "driverpositiongain", "driverstartposition",
            "maptwocolors", "mapthreecolors", "log", "getformulainstanceid",
        };

        private static readonly string[] Names = { "M. Verstappen", "S. Buemi", "K. Kobayashi", "B. Hartley", "N. de Vries", "A. Fuoco", "J. Calado", "E. Bamber" };

        private object Call(string n, object[] a, string site)
        {
            object A(int i) => i < a.Length ? a[i] : null;
            double N(int i) => DashValues.ToNumber(Seconds(A(i))) ?? 0;
            string S(int i) => A(i) == null ? null : Convert.ToString(A(i), Ci);
            bool B(int i) => A(i) is bool b ? b : N(i) != 0;
            double best = v.Number("bestLapTime") ?? 92.4;
            double pos = v.Number("position") ?? 3;
            switch (n)
            {
                case "isnull": return a.Length < 2 ? (object)(A(0) == null) : A(0) ?? A(1);
                case "format":
                {
                    if (A(0) == null) return null;
                    string s = A(0) is TimeSpan ts ? ts.ToString(S(1) ?? "c", Ci) : DashValues.ToNumber(A(0)) is double d ? d.ToString(S(1) ?? "0", Ci) : S(0);
                    return a.Length > 2 && B(2) && N(0) > 0 ? "+" + s : s;
                }
                case "left": { var s = S(0) ?? ""; int k = Math.Max(0, Math.Min(s.Length, (int)N(1))); return s.Substring(0, k); }
                case "right": { var s = S(0) ?? ""; int k = Math.Max(0, Math.Min(s.Length, (int)N(1))); return s.Substring(s.Length - k); }
                case "padleft": return (S(0) ?? "").PadLeft((int)N(1));
                case "replace": return S(0)?.Replace(S(1) ?? "", S(2) ?? "");
                case "lcase": return S(0)?.ToLowerInvariant();
                case "ucase": return S(0)?.ToUpperInvariant();
                case "tcase": return S(0) == null ? null : Ci.TextInfo.ToTitleCase(S(0).ToLowerInvariant());
                case "secondstotimespan": return A(0) == null ? null : (object)TimeSpan.FromSeconds(N(0));
                case "timespantoseconds": return A(0) == null ? null : (object)N(0);
                case "toshorttime": return A(0) == null ? null : ShortTime(N(0), a.Length > 1 ? (int)N(1) : 3, a.Length > 2 && B(2), a.Length > 3 && B(3));
                case "changed":
                case "isincreasing":
                case "isdecreasing":
                {
                    object x = A(1);
                    bool had = last.TryGetValue(site, out var prev);
                    last[site] = x;
                    double? nx = DashValues.ToNumber(Seconds(x)), np = DashValues.ToNumber(Seconds(prev));
                    bool hit = had && (n == "changed" ? !Equals(Seconds(x), Seconds(prev)) : nx.HasValue && np.HasValue && (n == "isincreasing" ? nx > np : nx < np));
                    if (hit) changes[site] = (t, x);
                    return changes.TryGetValue(site, out var c) && t - c.At <= N(0) / 1000;
                }
                case "maximum": case "minimum": case "inertia": return A(1) ?? A(0);
                case "blink": return B(2) && (int)(t * 1000 / Math.Max(50, N(1))) % 2 == 0;
                case "rand": return Math.Floor(Noise(site, Math.Floor(t)) * 0.5 + 0.5) * N(0);
                case "loopingtimer": return N(0) > 0 ? t % N(0) / N(0) : 0.0;
                case "prop": return Prop(S(0));
                case "getrpmtickvalue": return (v.Number("maxRpm") ?? 9000) * N(0) / 100;
                case "scroll": { var s = S(2) ?? ""; int k = (int)N(1); return k > 0 && s.Length > k ? s.Substring(0, k) : s; }
                case "timesincelastevent": return 999.0;
                case "dashboardwidth": return (double)DashRenderer.Width;
                case "dashboardheight": return (double)DashRenderer.Height;
                case "repeatindex": return 1.0;
                case "isbuttonpressed": return false;
                case "currentlapgetsectortime":
                case "lastlapgetsectortime":
                case "bestsectortime":
                case "sessionbestlapgetsectortime":
                case "getbestsplittime":
                case "getbestsplittime_playerclassonly":
                {
                    int sec = Math.Max(1, Math.Min(3, (int)N(0)));
                    double share = new[] { 0.31, 0.37, 0.32 }[sec - 1];
                    double time = (a.Length > 1 && B(1) ? new[] { 0.31, 0.68, 1.0 }[sec - 1] : share) * best * (n.StartsWith("current") || n.StartsWith("last") ? 1.004 : 1);
                    return TimeSpan.FromSeconds(time);
                }
                case "getplayerleaderboardposition": return pos;
                case "getopponentleaderboardposition_aheadbehind":
                case "getopponentleaderboardposition_aheadbehind_playerclassonly": return Math.Max(1, pos + N(0));
                case "getopponentleaderboardposition_playerclassonly": return N(0);
                case "getbestlapopponentleaderboardposition":
                case "getbestlapopponentleaderboardposition_playerclassonly": return 1.0;
                case "log": return null;
                case "getformulainstanceid": return current;
            }
            if (n.StartsWith("driver")) return Driver(n, (int)N(0), a.Length > 1 ? A(1) : null, pos, best);
            if (n == "maptwocolors" || n == "mapthreecolors") return MapColors(n, a);
            known = false;
            return null;
        }

        private object Driver(string n, int p, object arg2, double pos, double best)
        {
            if (p < 1) p = 1;
            bool me = p == (int)pos;
            double rel = p - pos;
            switch (n)
            {
                case "driveravailable": case "driverisconnected": return p <= 24;
                case "driverisplayer": return me;
                case "drivername": case "drivershortname": return me ? "Demo Driver" : Names[(p - 1) % Names.Length];
                case "driverinitials": return me ? "DDR" : Names[(p - 1) % Names.Length].Split(' ').Last().Substring(0, 3).ToUpperInvariant();
                case "drivercarclass": return "HYPERCAR";
                case "drivercarname": return me ? "Toyota GR010 Hybrid" : "Ferrari 499P";
                case "driverteamname": return me ? "Demo Racing" : "Team " + p;
                case "drivercarnumber": return (7 + p * 3).ToString(Ci);
                case "driverbestlap": return TimeSpan.FromSeconds(best + rel * 0.18);
                case "driverlastlap": return TimeSpan.FromSeconds(best + 0.4 + rel * 0.2);
                case "drivercurrentlaptime": return TimeSpan.FromSeconds(v.Number("currentLapTime") ?? 30);
                case "driverdeltatoplayer": case "driverdeltatobest": return rel * 0.18;
                case "drivercurrentlap": return v.Number("lap") ?? 4;
                case "driverlapsdonesincelastpitout": return (v.Number("completedLaps") ?? 3) + 0.5;
                case "driverpitcount": return 1.0;
                case "driverposition": case "driverclassposition": case "driverstartposition": return (double)p;
                case "driverpositiongain": return 0.0;
                case "drivergaptoplayer": case "driverrelativegaptoplayer": return rel * 1.4;
                case "drivergaptoleader": case "drivergaptoclassleader": return (p - 1) * 1.6;
                case "driverlapstoplayer": case "driverlapstoleader": case "driverlapstoclassleader": return 0.0;
                case "drivertrackpositionpercent": return ((v.Number("currentLapTime") ?? 0) / best - rel * 0.02 + 1) % 1;
                case "drivercurrentsector": return (double)Sector(1);
                case "driverspeed": return v.Number("speed") ?? 180;
                case "driveriracingirating": return 2500.0 + p * 37;
                case "driverlicencestring": return "A 4.99";
                case "drivercarclasscolor": return "#FFDA59";
                case "drivercarclasstextcolor": return "#000000";
                case "driverfronttyrecompound": case "driverreartyrecompound": return "M";
                case "driveriscarinpit": case "driveriscarinpitlane": case "driveriscaringarage": case "driverisoutlap": return false;
            }
            known = false;
            return null;
        }

        private object MapColors(string n, object[] a)
        {
            double x = DashValues.ToNumber(a.ElementAtOrDefault(0)) ?? 0;
            var nums = a.Skip(1).Take(n == "maptwocolors" ? 2 : 3).Select(o => DashValues.ToNumber(o) ?? 0).ToArray();
            var cols = a.Skip(1 + nums.Length).Select(o => DashColors.Parse(Convert.ToString(o, Ci), System.Drawing.Color.White)).ToArray();
            if (cols.Length < nums.Length) { known = false; return null; }
            int i = 0;
            while (i < nums.Length - 2 && x > nums[i + 1]) i++;
            double f = Math.Abs(nums[i + 1] - nums[i]) < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (x - nums[i]) / (nums[i + 1] - nums[i])));
            int Mix(int c0, int c1) => (int)Math.Round(c0 + (c1 - c0) * f);
            var c = System.Drawing.Color.FromArgb(Mix(cols[i].R, cols[i + 1].R), Mix(cols[i].G, cols[i + 1].G), Mix(cols[i].B, cols[i + 1].B));
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>SimHub's toshorttime: "1:23.456", "23.456" under a minute unless forceMinutes, optional sign.</summary>
        private static string ShortTime(double s, int precision, bool sign, bool forceMinutes)
        {
            precision = Math.Max(0, Math.Min(3, precision));
            string head = s < 0 ? "-" : sign && s > 0 ? "+" : "";
            s = Math.Abs(s);
            int m = (int)(s / 60);
            string frac = precision > 0 ? "." + new string('0', precision) : "";
            return m > 0 || forceMinutes ? head + m + ":" + (s - m * 60).ToString("00" + frac, Ci) : head + s.ToString("0" + frac, Ci);
        }

        private static object Seconds(object o) => o is TimeSpan ts ? (object)ts.TotalSeconds : o;

        private int Sector(int first)
        {
            double f = (v.Number("currentLapTime") ?? 0) / (v.Number("bestLapTime") ?? 92.4);
            return first + (f < 0.31 ? 0 : f < 0.68 ? 1 : 2);
        }

        /// <summary>A steady pseudo-random number in [-1, 1] for a name and a step.</summary>
        internal static double Noise(string name, double step)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in name ?? "") h = h * 31 + c;
                h = h * 31 + (int)step;
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                return (h & 0xFFFF) / 32767.5 - 1;
            }
        }

        internal static double Phase(string name) => (Noise(name, 0) + 1) * Math.PI;
    }

    /// <summary>
    /// SimHub properties made up from the demo lap: the ones the demo drives (speed, laps, fuel...), then common SimHub
    /// and game properties by name (tyre temps, pressures, settings...), moving plausibly over time.
    /// Unknown names give known = false.
    /// </summary>
    internal sealed class DemoProps
    {
        private static readonly HashSet<string> TimeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "currentLapTime", "lastLapTime", "bestLapTime", "predictedLap" };

        private static readonly Dictionary<string, string> RawKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "mTCCut", "tcCut" }, { "mTCSlip", "tcSlip" }, { "mTC", "tcLevel" }, { "mTractionControl", "tcLevel" }, { "mABS", "absLevel" },
            { "mMotorMap", "engineMap" }, { "mEngineMap", "engineMap" }, { "mFuel", "fuel" }, { "mEngineRPM", "rpm" }, { "mEngineMaxRPM", "maxRpm" },
            { "mEngineWaterTemp", "waterTemp" }, { "mEngineOilTemp", "oilTemp" }, { "mDeltaBest", "delta" }, { "mPlace", "position" },
            { "mTotalLaps", "completedLaps" }, { "mUnfilteredThrottle", "throttle" }, { "mUnfilteredBrake", "brake" }, { "mUnfilteredClutch", "clutch" },
            { "dcTractionControl", "tcLevel" }, { "dcABS", "absLevel" }, { "dcBrakeBias", "brakeBias" }, { "dcEngineMap", "engineMap" },
        };

        public object Get(string name, DashValues v, double t, out bool known)
        {
            known = true;
            if (string.IsNullOrEmpty(name)) { known = false; return null; }
            string full = name.Trim();
            string shortName = full.Substring(full.LastIndexOf('.') + 1);
            bool raw = full.IndexOf("GameRawData", StringComparison.OrdinalIgnoreCase) >= 0 || full.IndexOf("Plugin.", StringComparison.OrdinalIgnoreCase) > 0 && !full.StartsWith("DataCorePlugin", StringComparison.OrdinalIgnoreCase) && !full.StartsWith("PersistantTrackerPlugin", StringComparison.OrdinalIgnoreCase);
            var special = Special(shortName, v, t);
            if (special is DBNull) return null; // known, no value (yet)
            if (special != null) return special;
            string key = DashValues.Alias("prop:" + full) ?? DashValues.Alias("prop:" + shortName);
            if (key == null && RawKeys.TryGetValue(shortName, out var rk)) key = rk;
            if (key != null)
            {
                var o = v.Raw(key);
                if (o != null && TimeKeys.Contains(key)) { var s = DashValues.ToNumber(o) ?? 0; return s > 0 ? (object)TimeSpan.FromSeconds(s) : null; }
                return o;
            }
            var guess = Guess(shortName, full, raw, v, t);
            if (guess == null) known = false;
            return guess;
        }

        private static double Wave(string name, double t, double period) => Math.Sin(t * 2 * Math.PI / period + DemoFormulas.Phase(name));

        /// <summary>Common SimHub properties with a fixed meaning; DBNull = known, but no value now.</summary>
        private static object Special(string n, DashValues v, double t)
        {
            double best = v.Number("bestLapTime") ?? 0, last = v.Number("lastLapTime") ?? 0, cur = v.Number("currentLapTime") ?? 0;
            // SimHub's sector times (NewData.Sector1Time: this lap's, once done; ...LastLapTime; ...BestLapTime: on the
            // best lap; ...BestTime: the best single sector), a few hundredths apart from lap to lap
            var sm = System.Text.RegularExpressions.Regex.Match(n, @"^Sector([123])(Time|LastLapTime|BestLapTime|BestTime)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sm.Success)
            {
                int s = sm.Groups[1].Value[0] - '1';
                double b = best > 0 ? best : 92.4, share = new[] { 0.31, 0.37, 0.32 }[s] * b, end = new[] { 0.31, 0.68, 1.0 }[s];
                int lap = (int)(v.Number("lap") ?? 1);
                double Run(int k) => share + 0.12 * DemoFormulas.Noise("sector" + s, k);
                switch (sm.Groups[2].Value.ToLowerInvariant())
                {
                    case "time": return s == 2 || cur / b < end ? DBNull.Value : (object)TimeSpan.FromSeconds(Run(lap));
                    case "lastlaptime": return last > 0 ? (object)TimeSpan.FromSeconds(Run(lap - 1)) : DBNull.Value;
                    case "bestlaptime": return best > 0 ? (object)TimeSpan.FromSeconds(share) : DBNull.Value;
                    default: return best > 0 ? (object)TimeSpan.FromSeconds(share - 0.05) : DBNull.Value;
                }
            }
            switch (n.ToLowerInvariant())
            {
                case "sessiontypename": return "Race";
                case "mvirtualenergy": return (v.Number("virtualEnergy") ?? 70) / 100; // LMU gives a fraction
                // LMU's lift-and-coast progress (0-255): the demo car doesn't lift on its own (the overlay demo brings up a
                // LIFT screen in its turn); unknown, every condition reading it (a LIFT trigger script and its negations)
                // fell back to its preview look: the AMR's blue flag oval stayed hidden while the gear hid for it
                case "mliftandcoastprogress": return v.LiftCoast * 2.55;
                case "speedlocalunit": return "KMH";
                case "tyrepressureunit": return "Psi";
                case "temperatureunit": return "C";
                case "fuelunit": return "Liters";
                case "currentgame": case "gamename": return "LMU";
                case "carmodel": return "Toyota GR010 Hybrid";
                case "carclass": return "Hypercar";
                case "carid": return "toyota_gr010";
                case "trackname": case "trackid": return "Le Mans";
                case "playername": return "Demo Driver";
                case "dashname": return "FX Pro";
                case "speedmph": return (v.Number("speed") ?? 0) * 0.621371;
                case "isinpit": case "isinpitlane": case "gamepaused": case "gameinmenu": case "spectating": case "gamereplay":
                case "enginewarnings": case "pitsvflags": case "drs_status": case "drsavailable": case "drsenabled": return 0;
                case "engineignitionon": case "enginestarted": return 1;
                case "gamerunning": return true;
                case "flag_yellow": return v.YellowFlag ? 1 : 0;
                case "flag_blue": return v.BlueFlag ? 1 : 0;
                case "flag_green": return t < 8 ? 1 : 0;
                case "currentsector": case "currentsectorindex": return 1 + (cur / Math.Max(1, best > 0 ? best : 92.4) < 0.31 ? 0 : cur / Math.Max(1, best > 0 ? best : 92.4) < 0.68 ? 1 : 2);
                case "sessiontimeleft": case "timeleft": case "sessiontimeremain": return TimeSpan.FromSeconds(Math.Max(0, 2700 - t));
                case "opponentscount": case "playerclassopponentscount": return 23;
                case "totallaps": return 0;
                case "alltimebest": return best > 0 ? (object)TimeSpan.FromSeconds(best - 0.412) : null;
                case "bestsplitdelta": case "selfsplitdelta": case "sessionbestsplitdelta": return (v.Number("delta") ?? 0) * 0.6 + 0.05 * Wave(n, t, 9);
                case "sessionbestlastlapdelta": case "alltimebestlastlapdelta": case "previouslap_00_deltatosessionbest": return last > 0 && best > 0 ? last - best : (object)null;
                case "fuel_litersperlap": return v.Raw("fuelLastLap");
                case "airtemp": return 24.0 + 0.2 * Wave(n, t, 300);
                case "tracktemp": return 33.0 + 0.3 * Wave(n, t, 300);
                case "voltage": return 13.8 + 0.1 * Wave(n, t, 40);
                case "currentdatetime": return new DateTime(2026, 6, 13, 15, 0, 0).AddSeconds(t); // fixed: runs repeat exactly
                case "carsettings_redlinerpm": return (v.Number("maxRpm") ?? 9000) * 0.96;
                case "carsettings_rpmredlinereached": return (v.Number("rpmPercent") ?? 0) >= 96 ? 1 : 0;
                case "carsettings_redlinedisplayedpercent": return 96.0;
                case "drivercaridlerpm": return 1200.0;
                case "trackpitspeedlimit": return 60.0;
            }
            return null;
        }

        /// <summary>A plausible value from a property's name alone (tyre temps, pressures, wear, settings, flags...).</summary>
        private static object Guess(string n, string full, bool raw, DashValues v, double t)
        {
            string l = n.ToLowerInvariant();
            // rFactor-style raw names: mTireCarcassTemperature -> tirecarcasstemperature
            if (n.Length > 1 && n[0] == 'm' && char.IsUpper(n[1])) l = l.Substring(1);
            string fl = full.ToLowerInvariant();
            double best = v.Number("bestLapTime") ?? 92.4;
            double W(double period) => Wave(full, t, period);

            if (l.Contains("temp"))
            {
                bool kelvin = raw && (l.Contains("tire") || l.Contains("tyre")) && fl.Contains("gameraw");
                if (l.Contains("brake")) return 480 + 330 * W(60);
                if (l.Contains("tire") || l.Contains("tyre") || l.Contains("layer") || l.Contains("carcass") || l.Contains("surface"))
                    return (kelvin ? 273.15 : 0) + 86 + 5 * W(40);
                if (l.Contains("oil")) return 104 + 2 * W(60);
                if (l.Contains("water")) return 88 + 1.5 * W(60);
                if (l.Contains("motor") || l.Contains("battery") || l.Contains("electric")) return 56 + 6 * W(50);
                if (l.Contains("air")) return 24.0;
                if (l.Contains("track")) return 33.0;
                return 80 + 4 * W(50);
            }
            if (l.Contains("pressure"))
            {
                if (l.Contains("oil")) return 4.6 + 0.3 * W(20);
                if (l.Contains("fuel")) return 3.5;
                return raw ? 178 + 3 * W(50) : 26.5 + 0.4 * W(50);
            }
            if (l.Contains("wear")) return raw ? 0.93 - 0.02 * W(200) : 93 - 2 * W(200);
            if (l.Contains("fraction") || l.Contains("charge")) return 0.5 + 0.42 * W(120); // a battery drains and charges slowly
            if (l.Contains("percent") || l.Contains("pct") || l == "batterylevel" || l.EndsWith("_battery_level")) return 55 + 40 * W(30);
            // brake bias: rFactor's raw one is the rear share as a fraction (0.445), the others a front % (55.5)
            if (l.Contains("bias")) return raw ? (l.Contains("rear") ? 0.445 : 0.555) : v.Number("brakeBias") ?? 55.5;
            if (l.Contains("delta") || l.Contains("gap")) return 0.35 * W(11) + (l.Contains("ahead") ? -0.9 : l.Contains("behind") ? 1.1 : 0);
            if (l.Contains("laptime") || (l.Contains("lap") && l.Contains("time")) || l.Contains("estimatedlap")) return TimeSpan.FromSeconds(best + 0.3 + 0.2 * W(90));
            if (l.Contains("timeleft") || l.Contains("timeremain")) return TimeSpan.FromSeconds(Math.Max(0, 2700 - t));
            if (l.Contains("sector")) return l.Contains("flag") ? 0 : raw ? (object)(double)SectorOf(v, 0) : (double)SectorOf(v, 1);
            if (l.Contains("energy")) return l.Contains("lap") ? 3.9 + 0.2 * W(90) : 62 + 25 * W(120);
            // on/off states first: "FuelAlertActive" is a flag, not the fuel
            if (l.EndsWith("active") || l.EndsWith("enabled") || l.EndsWith("available") || l.EndsWith("reached") || l.EndsWith("warning") || l.EndsWith("alert"))
                return 0;
            if (l.Contains("rpm")) return v.Number("rpm");
            if (l.Contains("speed")) return v.Number("speed");
            if (l.Contains("fuel")) return v.Number("fuel");
            if (l.Contains("voltage")) return 13.8;
            if (l.Contains("torque")) return 180 + 60 * W(7);
            if (l.Contains("power")) return 300 + 80 * W(9);
            if (l.Contains("name")) return l.Contains("track") ? "Le Mans" : l.Contains("car") ? "Toyota GR010 Hybrid" : "A. Driver";
            // settings the driver changes now and then: a level between 1 and 9, stepping every so often
            if (new[] { "antisway", "antiroll", "migration", "regen", "map", "mode", "mixture", "diff", "wing", "boost", "tractioncontrol", "throttleshape", "enginebraking", "enginepower", "level", "setting", "deploy", "arb" }.Any(l.Contains) || l.StartsWith("dc"))
            {
                double baseLevel = 2 + Math.Floor((DemoFormulas.Noise(full, 0) + 1) * 2.5);
                return baseLevel + Math.Round(DemoFormulas.Noise(full, Math.Floor(t / 23)));
            }
            if (l.Contains("laps")) return v.Number("completedLaps");
            if (l.Contains("position") || l == "place") return v.Number("position");
            if (l.StartsWith("is") || l.StartsWith("flag") || l.EndsWith("on") || l.EndsWith("active") || l.EndsWith("enabled") ||
                l.EndsWith("available") || l.EndsWith("reached") || l.EndsWith("flags") || l.EndsWith("warnings") || l.EndsWith("state") || l.EndsWith("status"))
                return 0;
            if (l.Contains("count")) return 1.0;
            return null;
        }

        private static int SectorOf(DashValues v, int first)
        {
            double f = (v.Number("currentLapTime") ?? 0) / (v.Number("bestLapTime") ?? 92.4);
            return first + (f < 0.31 ? 0 : f < 0.68 ? 1 : 2);
        }
    }

    /// <summary>The time JavaScript sees in the demo: a fixed day, moving with the demo's own clock.</summary>
    internal sealed class DemoClock : Jint.Runtime.ITimeSystem
    {
        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 6, 13, 15, 0, 0, TimeSpan.Zero);
        private readonly Func<double> now;
        private readonly Jint.Runtime.DefaultTimeSystem sys = new Jint.Runtime.DefaultTimeSystem(TimeZoneInfo.Utc, System.Globalization.CultureInfo.InvariantCulture);
        public DemoClock(Func<double> now) { this.now = now; }
        public DateTimeOffset GetUtcNow() => Start.AddSeconds(now());
        public TimeZoneInfo DefaultTimeZone => TimeZoneInfo.Utc;
        public bool TryParse(string date, out long epochMilliseconds) => sys.TryParse(date, out epochMilliseconds);
        public TimeSpan GetUtcOffset(long epochMilliseconds) => TimeSpan.Zero;
    }
}
