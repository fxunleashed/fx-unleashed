using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>
    /// One snapshot of everything USB mode shows: dash values by key, plus what the lights need. Immutable once built;
    /// DataUpdate (live) or the demo timer publishes a new one, the USB thread reads the latest.
    /// Missing = null (not provided by the game, or no lap/best yet), so the dash shows its "empty" text.
    /// </summary>
    public sealed class DashValues
    {
        /// <summary>Keys a dash can bind to (besides "prop:" + any SimHub property), with what they are.</summary>
        public static readonly (string Key, string Description)[] Keys =
        {
            ("speed", "Speed, km/h"), ("gear", "Gear (-1 = R, 0 = N)"), ("rpm", "RPM"), ("maxRpm", "Max RPM"),
            ("throttle", "Throttle %"), ("brake", "Brake %"), ("clutch", "Clutch %"),
            ("currentLapTime", "Current lap, s"), ("lastLapTime", "Last lap, s"), ("bestLapTime", "Best lap, s"),
            ("delta", "Delta to session best, s (+ = slower)"), ("predictedLap", "Predicted lap from session best, s"),
            ("position", "Position"), ("lap", "Current lap number"), ("completedLaps", "Completed laps"),
            ("fuel", "Fuel (SimHub's unit)"), ("fuelPercent", "Fuel %"), ("fuelLastLap", "Fuel used last lap"),
            ("fuelThisLap", "Fuel used this lap"), ("fuelRemainingLaps", "Laps of fuel left"),
            ("virtualEnergy", "Virtual energy % (LMU)"), ("brakeBias", "Brake bias %"),
            ("absLevel", "ABS level"), ("tcLevel", "TC level"), ("tcCut", "TC cut / TC2 (ACC, iRacing, LMU)"),
            ("tcSlip", "TC slip (LMU)"), ("engineMap", "Engine map"), ("throttleMap", "Throttle map (not provided yet)"),
            ("pas", "PAS (not provided yet)"), ("sessionTypeName", "Session type"),
            ("waterTemp", "Water temperature"), ("oilTemp", "Oil temperature"),
            ("gearText", "Gear as text (R, N, 1...)"), ("rpmPercent", "RPM as % of max"), ("gameRunning", "A game is running"),
            ("absActive", "ABS working now"), ("tcActive", "TC working now"), ("pitLimiter", "Pit limiter on"),
            ("clock", "Time of day, HH:mm (screensavers)"), ("date", "Date, e.g. SAT 27 SEP (screensavers)"),
        };

        private readonly Dictionary<string, object> v = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Light inputs
        public double Rpm, MaxRpm, Redline;
        public string GearKey = "N";
        public bool AbsActive, TcActive, PitLimiter, Drs, InPitLane;
        public bool BlueFlag, YellowFlag, GreenFlag, WhiteFlag, CheckeredFlag, BlackFlag;
        public double FuelPercent = 100;
        public bool Running;

        /// <summary>
        /// A preview (designer, offline tools): bindings that can't be evaluated here (SimHub formulas without SimHub)
        /// count as visible, and values show their preview text, so a dash looks like it does in its designer.
        /// </summary>
        public bool Preview;

        public void Set(string key, object value) { if (value != null) v[key] = value; }

        /// <summary>The raw value of a binding: its own entry, else a built-in key it's an alias of (see Alias).</summary>
        public object Raw(string key)
        {
            if (key == null) return null;
            if (v.TryGetValue(key, out var o)) return o;
            var alias = Alias(key);
            return alias != null && v.TryGetValue(alias, out o) ? o : null;
        }

        private static readonly HashSet<string> keySet = new HashSet<string>(Keys.Select(k => k.Key), StringComparer.OrdinalIgnoreCase);

        /// <summary>A built-in key, or a SimHub binding that's an alias of one: it has a value without SimHub.</summary>
        public static bool KnownKey(string bind) => !string.IsNullOrEmpty(bind) && (keySet.Contains(bind) || Alias(bind) != null || bind.StartsWith("saver.", StringComparison.OrdinalIgnoreCase));

        /// <summary>The binding has a value here (or is an alias of a key that has one).</summary>
        public bool Has(string key) => Raw(key) != null;

        public double? Number(string key) => ToNumber(Raw(key));

        public static double? ToNumber(object o)
        {
            var n = ToNumberRaw(o);
            return n.HasValue && (double.IsNaN(n.Value) || double.IsInfinity(n.Value)) ? null : n;
        }

        private static double? ToNumberRaw(object o)
        {
            switch (o)
            {
                case null: return null;
                case double d: return double.IsNaN(d) ? (double?)null : d;
                case bool b: return b ? 1 : 0;
                case TimeSpan ts: return ts.TotalSeconds;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : (double?)null;
                case IConvertible c: try { return c.ToDouble(CultureInfo.InvariantCulture); } catch { return null; }
                default: return null;
            }
        }

        public string Text(string key)
        {
            var o = Raw(key);
            return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        /// <summary>For conditions: true, a number other than 0, or a text other than "", "0", "false".</summary>
        public bool? Truthy(string key)
        {
            var o = Raw(key);
            switch (o)
            {
                case null: return null;
                case bool b: return b;
                case string s: s = s.Trim(); return s.Length > 0 && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase);
                default: var n = ToNumber(o); return n.HasValue ? Math.Abs(n.Value) > 1e-9 : (bool?)true;
            }
        }

        /// <summary>
        /// Common SimHub properties as built-in keys, so imported SimHub bindings like "ncalc:[SpeedKmh]" or
        /// "prop:DataCorePlugin.GameData.NewData.Gear" show demo values offline. Live, SimHub evaluates them itself.
        /// </summary>
        public static string Alias(string bind)
        {
            if (string.IsNullOrEmpty(bind)) return null;
            string name = bind;
            if (name.StartsWith("prop:", StringComparison.OrdinalIgnoreCase)) name = name.Substring(5);
            else if (name.StartsWith("ncalc:", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(6).Trim();
                if (!(name.StartsWith("[") && name.EndsWith("]") && name.IndexOf('[', 1) < 0)) return null; // only a bare [Property]
                name = name.Substring(1, name.Length - 2);
            }
            else return null;
            foreach (var prefix in new[] { "DataCorePlugin.GameData.NewData.", "DataCorePlugin.GameData.", "GameData.NewData.", "DataCorePlugin.Computed.", "DataCorePlugin.", "PersistantTrackerPlugin." })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(prefix.Length); break; }
            return AliasTable.TryGetValue(name, out var key) ? key : null;
        }

        private static readonly Dictionary<string, string> AliasTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SpeedKmh", "speed" }, { "SpeedLocal", "speed" }, { "FilteredSpeedLocal", "speed" }, { "FilteredSpeedKmh", "speed" },
            { "Gear", "gearText" }, { "Rpms", "rpm" }, { "FilteredRpms", "rpm" }, { "MaxRpm", "maxRpm" }, { "CarSettings_MaxRPM", "maxRpm" },
            { "CarSettings_CurrentDisplayedRPMPercent", "rpmPercent" }, { "Throttle", "throttle" }, { "Brake", "brake" }, { "Clutch", "clutch" },
            { "CurrentLapTime", "currentLapTime" }, { "LastLapTime", "lastLapTime" }, { "BestLapTime", "bestLapTime" },
            { "Position", "position" }, { "CurrentLap", "lap" }, { "CompletedLaps", "completedLaps" },
            { "Fuel", "fuel" }, { "FuelPercent", "fuelPercent" }, { "Fuel_LastLapConsumption", "fuelLastLap" },
            { "Fuel_CurrentLapConsumption", "fuelThisLap" }, { "Fuel_RemainingLaps", "fuelRemainingLaps" },
            { "SessionBestLiveDeltaSeconds", "delta" }, { "DeltaToSessionBest", "delta" }, { "EstimatedLapTime_SessionBestBased", "predictedLap" },
            { "BrakeBias", "brakeBias" }, { "TCLevel", "tcLevel" }, { "ABSLevel", "absLevel" }, { "EngineMap", "engineMap" },
            { "WaterTemperature", "waterTemp" }, { "OilTemperature", "oilTemp" }, { "SessionTypeName", "sessionTypeName" },
            { "ABSActive", "absActive" }, { "TCActive", "tcActive" }, { "PitLimiterOn", "pitLimiter" }, { "GameRunning", "gameRunning" },
        };

        // ---------- Live: SimHub ----------

        private const string RawData = "DataCorePlugin.GameRawData.";

        /// <summary>From SimHub's data; `props` = the "prop:" bindings of the active dash.</summary>
        /// <param name="binds">The active dash's "prop:" / "ncalc:" / "js:" bindings.</param>
        /// <param name="formula">Evaluates "ncalc:" / "js:" bindings (SimHubFormulas); null = leave them out.</param>
        public static DashValues FromSimHub(GameData data, PluginManager pm, IEnumerable<string> binds, Func<string, object> formula = null)
        {
            var r = new DashValues();
            var d = data.NewData;
            if (d == null || !data.GameRunning) return r;
            r.Running = true;
            Try(() =>
            {
                r.Set("speed", d.SpeedKmh);
                int gear = SimHubFeedMapper.ParseGear(d.Gear);
                r.Set("gear", (double)gear);
                r.GearKey = gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString(CultureInfo.InvariantCulture);
                r.Set("gearText", r.GearKey);
                r.Set("rpm", d.Rpms);
                r.MaxRpm = d.CarSettings_MaxRPM > 0 ? d.CarSettings_MaxRPM : d.MaxRpm;
                r.Redline = d.CarSettings_RedLineRPM > 0 ? d.CarSettings_RedLineRPM : d.Redline;
                r.Rpm = d.Rpms;
                r.Set("maxRpm", r.MaxRpm);
                if (r.MaxRpm > 0) r.Set("rpmPercent", d.Rpms / r.MaxRpm * 100);
                r.Set("gameRunning", true);
                r.Set("throttle", d.Throttle); r.Set("brake", d.Brake); r.Set("clutch", d.Clutch);
            });
            Try(() =>
            {
                r.Set("currentLapTime", Seconds(d.CurrentLapTime));
                r.Set("lastLapTime", Seconds(d.LastLapTime));
                r.Set("bestLapTime", Seconds(d.BestLapTime));
                r.Set("position", (double)d.Position);
                r.Set("lap", (double)d.CurrentLap);
                r.Set("completedLaps", (double)d.CompletedLaps);
                r.Set("sessionTypeName", (d.SessionTypeName ?? "").ToUpperInvariant());
                r.InPitLane = d.IsInPitLane != 0;
                // Delta and prediction from SimHub's persistent tracker (what the SimHub dash uses), only once there's a best.
                var delta = Prop(pm, "PersistantTrackerPlugin.SessionBestLiveDeltaSeconds") ?? d.DeltaToSessionBest;
                if (d.BestLapTime.TotalSeconds > 0 && delta.HasValue) r.Set("delta", delta.Value);
                var predicted = Prop(pm, "PersistantTrackerPlugin.EstimatedLapTime_SessionBestBased");
                if (predicted > 0 && !r.InPitLane) r.Set("predictedLap", predicted.Value);
            });
            Try(() =>
            {
                r.Set("fuel", d.Fuel);
                r.FuelPercent = d.FuelPercent;
                r.Set("fuelPercent", d.FuelPercent);
                r.Set("fuelLastLap", Positive(Prop(pm, "DataCorePlugin.Computed.Fuel_LastLapConsumption")));
                r.Set("fuelThisLap", Prop(pm, "DataCorePlugin.Computed.Fuel_CurrentLapConsumption"));
                r.Set("fuelRemainingLaps", Positive(Prop(pm, "DataCorePlugin.Computed.Fuel_RemainingLaps")));
                var ve = Prop(pm, RawData + "PlayerNativeTelemetry.mVirtualEnergy");
                if (ve.HasValue) r.Set("virtualEnergy", ve.Value * 100);
                r.Set("brakeBias", d.BrakeBias);
                r.Set("waterTemp", d.WaterTemperature);
                r.Set("oilTemp", d.OilTemperature);
            });
            Try(() =>
            {
                r.Set("absLevel", (double)d.ABSLevel);
                r.Set("tcLevel", (double)d.TCLevel);
                r.Set("engineMap", (double)d.EngineMap);
                foreach (var kv in SimHubFeedMapper.BuiltInRawFields(data.GameName))
                    if (kv.Key == "tcCut") r.Set("tcCut", Prop(pm, kv.Value));
                if (r.Number("tcCut") == null) r.Set("tcCut", Prop(pm, RawData + "PlayerNativeTelemetry.mTCCut"));
                r.Set("tcSlip", Prop(pm, RawData + "PlayerNativeTelemetry.mTCSlip"));
                r.AbsActive = d.ABSActive != 0;
                r.TcActive = d.TCActive != 0;
                r.PitLimiter = d.PitLimiterOn != 0;
                r.Set("absActive", r.AbsActive); r.Set("tcActive", r.TcActive); r.Set("pitLimiter", r.PitLimiter);
                r.Drs = d.DRSEnabled != 0;
                r.BlueFlag = d.Flag_Blue != 0; r.YellowFlag = d.Flag_Yellow != 0; r.GreenFlag = d.Flag_Green != 0;
                r.WhiteFlag = d.Flag_White != 0; r.CheckeredFlag = d.Flag_Checkered != 0; r.BlackFlag = d.Flag_Black != 0;
            });
            if (binds != null)
                foreach (var p in binds)
                    if (p.StartsWith("prop:", StringComparison.OrdinalIgnoreCase))
                        Try(() => r.Set(p, pm.GetPropertyValue(p.Substring(5))));
                    else if (formula != null && SimHubFormulas.IsFormula(p))
                        Try(() => r.Set(p, formula(p)));
            return r;
        }

        private static void Try(Action a) { try { a(); } catch { } }

        private static double? Seconds(TimeSpan t) => t.TotalSeconds > 0 ? t.TotalSeconds : (double?)null;
        private static double? Positive(double? x) => x > 0 ? x : null;

        private static double? Prop(PluginManager pm, string path)
        {
            object o;
            try { o = pm.GetPropertyValue(path); } catch { return null; }
            switch (o)
            {
                case null: return null;
                case TimeSpan ts: return ts.TotalSeconds;
                case bool b: return b ? 1 : 0;
                case IConvertible c: try { return c.ToDouble(CultureInfo.InvariantCulture); } catch { return null; }
                default: return null;
            }
        }

        // ---------- Demo ----------

        /// <summary>From the demo's simulated lap (the same DemoCar the SimGame feed demo uses) plus its extras.</summary>
        internal static DashValues FromDemo(SimProTelemetry t, UsbDemo extra)
        {
            var r = new DashValues { Running = true };
            int gear = (int)t.Get("gear");
            r.Set("speed", t.Get("speed"));
            r.Set("gear", (double)gear);
            r.GearKey = gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString(CultureInfo.InvariantCulture);
            r.Set("gearText", r.GearKey);
            r.Rpm = t.Get("rpm"); r.MaxRpm = t.Get("maxRpm"); r.Redline = r.MaxRpm * 0.96;
            r.Set("rpm", r.Rpm); r.Set("maxRpm", r.MaxRpm);
            if (r.MaxRpm > 0) r.Set("rpmPercent", r.Rpm / r.MaxRpm * 100);
            r.Set("gameRunning", true);
            r.Set("throttle", t.Get("throttle")); r.Set("brake", t.Get("brake"));
            double best = t.Get("bestLapTime") / 1000;
            r.Set("currentLapTime", t.Get("currentLapTime") / 1000);
            if (t.Get("lastLapTime") > 0) r.Set("lastLapTime", t.Get("lastLapTime") / 1000);
            if (best > 0)
            {
                r.Set("bestLapTime", best);
                r.Set("delta", t.Get("gainLoss"));
                r.Set("predictedLap", best + t.Get("gainLoss"));
            }
            r.Set("position", t.Get("position"));
            r.Set("lap", t.Get("completedLaps") + 1);
            r.Set("completedLaps", t.Get("completedLaps"));
            r.Set("sessionTypeName", "PRACTICE");
            r.Set("fuel", t.Get("fuel"));
            // the demo car has a tank, not a percentage
            r.FuelPercent = t.Get("fuelPercent") > 0 ? t.Get("fuelPercent") : (t.Get("maxFuel") > 0 ? Math.Min(100, t.Get("fuel") / t.Get("maxFuel") * 100) : 0);
            r.Set("fuelPercent", r.FuelPercent);
            if (extra.FuelLastLap > 0) r.Set("fuelLastLap", extra.FuelLastLap);
            r.Set("fuelThisLap", extra.FuelThisLap);
            if (extra.FuelLastLap > 0) r.Set("fuelRemainingLaps", t.Get("fuel") / extra.FuelLastLap);
            r.Set("virtualEnergy", extra.VirtualEnergy * 100);
            r.Set("brakeBias", extra.Bias);
            r.Set("waterTemp", t.Get("waterTemperature")); r.Set("oilTemp", t.Get("oilTemperature"));
            r.Set("absLevel", (double)extra.Abs); r.Set("tcLevel", (double)extra.Tc); r.Set("tcCut", (double)extra.TcCut);
            r.Set("tcSlip", (double)extra.TcSlip); r.Set("engineMap", (double)extra.Map);
            r.Set("throttleMap", 2.0); r.Set("pas", 3.0);
            r.AbsActive = t.Get("isAbsActive") > 0;
            r.TcActive = t.Get("isTcActive") > 0;
            r.PitLimiter = t.Get("isPitLimiterOn") > 0;
            r.Set("absActive", r.AbsActive); r.Set("tcActive", r.TcActive); r.Set("pitLimiter", r.PitLimiter);
            r.BlueFlag = t.Get("blueFlag") > 0; r.YellowFlag = t.Get("yellowFlag") > 0;
            return r;
        }
    }

    /// <summary>
    /// The USB demo: the plugin's DemoCar lap, plus what the Mustang dash shows on top: fuel per lap, virtual energy,
    /// and a driver changing a setting every 12-20 s (so the pop-up shows). Given a dash (UseDash), it also fills
    /// everything else the dash shows: SimHub properties and formulas from simulated data (DemoFormulas), and where
    /// even that can't work, the element's preview text, moving a little (FillDash).
    /// </summary>
    internal sealed class UsbDemo
    {
        private DashDefinition dash;
        private DemoFormulas formulas;
        private readonly Dictionary<string, object> results = new Dictionary<string, object>();
        private readonly HashSet<string> unresolved = new HashSet<string>();
        private double lastFormulas = double.NegativeInfinity;
        private string[] toEval = new string[0];
        private int cursor;
        private readonly System.Diagnostics.Stopwatch budget = new System.Diagnostics.Stopwatch();

        public UsbDemo(DashDefinition dash = null) { UseDash(dash); }

        /// <summary>
        /// Milliseconds of formula evaluation per frame (the wheel's lights share the thread); null = all of them every
        /// round (tools: verify and tune must give the same answer on a busy machine as on an idle one).
        /// </summary>
        public double? BudgetMs = 4;

        /// <summary>The dash the demo feeds (null: built-in keys only).</summary>
        public void UseDash(DashDefinition d)
        {
            if (ReferenceEquals(d, dash)) return;
            dash = d;
            formulas = d == null ? null : new DemoFormulas(d.ScriptsFolder);
            results.Clear(); unresolved.Clear();
            lastFormulas = double.NegativeInfinity;
            cursor = 0;
            toEval = d == null ? new string[0] : d.Bindings
                .Where(b => !DashValues.KnownKey(b) && (b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(b)))
                .ToArray();
        }

        private readonly DemoCar car = new DemoCar();
        public readonly SimProTelemetry Telemetry = new SimProTelemetry();
        private readonly Random rng = new Random(20260927); // fixed: the same demo every time (tests can repeat it)
        private double t, nextChange = 6, fuelAtLapStart = -1;
        private int laps = -1;
        public double FuelLastLap, FuelThisLap, VirtualEnergy = 1, Bias = 56.2;
        public int Map = 6, Tc = 6, TcCut = 6, TcSlip = 6, Abs = 6;

        public DashValues Step(double dt)
        {
            if (dt > 0)
            {
                t += dt;
                car.Step(dt, Telemetry);
                double fuel = Telemetry.Get("fuel");
                int done = (int)Telemetry.Get("completedLaps");
                if (laps < 0) { laps = done; fuelAtLapStart = fuel; }
                if (done != laps)
                {
                    laps = done;
                    FuelLastLap = fuelAtLapStart - fuel;
                    fuelAtLapStart = fuel;
                }
                FuelThisLap = Math.Max(0, fuelAtLapStart - fuel);
                VirtualEnergy = Math.Max(0, VirtualEnergy - Telemetry.Get("throttle") / 100 * 0.0009 * dt);
                if (VirtualEnergy <= 0.02) VirtualEnergy = 1;
                if (t >= nextChange)
                {
                    nextChange = t + 12 + rng.NextDouble() * 8;
                    int step = rng.Next(2) == 0 ? -1 : 1;
                    switch (rng.Next(6))
                    {
                        case 0: Tc = Clamp(Tc + step, 1, 11); break;
                        case 1: TcCut = Clamp(TcCut + step, 1, 11); break;
                        case 2: TcSlip = Clamp(TcSlip + step, 1, 11); break;
                        case 3: Abs = Clamp(Abs + step, 1, 11); break;
                        case 4: Map = Clamp(Map + step, 1, 8); break;
                        default: Bias = Math.Round(Math.Max(50, Math.Min(62, Bias + step * 0.2)), 1); break;
                    }
                }
            }
            var r = DashValues.FromDemo(Telemetry, this);
            if (dash != null) FillDash(r);
            return r;
        }

        /// <summary>
        /// Every binding of the dash the built-in keys don't cover, up to 10 times a second, but at most ~4 ms of it per
        /// frame (the wheel's lights run on the same thread): a dash with hundreds of formulas refreshes them in turns.
        /// </summary>
        private void FillDash(DashValues r)
        {
            if (t - lastFormulas >= 0.099 && toEval.Length > 0)
            {
                budget.Restart();
                for (int k = 0; k < toEval.Length && (k == 0 || BudgetMs == null || budget.Elapsed.TotalMilliseconds < BudgetMs); k++)
                {
                    var b = toEval[cursor];
                    cursor = (cursor + 1) % toEval.Length;
                    if (cursor == 0) lastFormulas = t; // all of them done: next round in 0.1 s
                    var o = formulas.Eval(b, r, t, out bool known);
                    if (known) { results[b] = o; unresolved.Remove(b); } else { results.Remove(b); unresolved.Add(b); }
                }
            }
            foreach (var kv in results) r.Set(kv.Key, kv.Value);
            if (unresolved.Count == 0) return;
            foreach (var e in dash.Elements)
            {
                if (e.Bind != null && unresolved.Contains(e.Bind) && (e.Type == "value" || e.Type == "bar") && !r.Has(e.Bind))
                    r.Set(e.Bind, Sample(e, t));
                // a condition the demo can't evaluate shows the element as its designer does
                if (e.Visible != null)
                    foreach (var c in e.Visible)
                        if (unresolved.Contains(c) && !r.Has(c)) r.Set(c, e.PreviewVisible ?? true);
            }
        }

        private static readonly Regex NumberToken = new Regex(@"[-+]?\d+(\.\d+)?");

        /// <summary>
        /// A stand-in for a value the demo can't compute: the element's preview text, its numbers moving a little every
        /// few seconds (same decimals), so the dash looks alive; a bar sweeps around its middle.
        /// </summary>
        internal static object Sample(DashElement e, double t)
        {
            var ci = CultureInfo.InvariantCulture;
            if (e.Type == "bar")
                return (e.Min + e.Max) / 2 + (e.Max - e.Min) * 0.35 * Math.Sin(t * 0.5 + DemoFormulas.Phase(e.Bind));
            string text = !string.IsNullOrEmpty(e.PreviewText) ? e.PreviewText : e.Samples?.FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (string.IsNullOrEmpty(text)) return null;
            double step = Math.Floor(t / 3);
            string fmt = e.Format ?? "0";
            double scale = Math.Abs(e.Scale) > 1e-12 ? e.Scale : 1;
            // a lap time "1:23.456"
            var lap = Regex.Match(text.Trim(), @"^(\d+):(\d{1,2}(\.\d+)?)$");
            if (lap.Success)
            {
                double s = int.Parse(lap.Groups[1].Value, ci) * 60 + double.Parse(lap.Groups[2].Value, ci);
                s += 0.4 * DemoFormulas.Noise(e.Bind, step);
                if (fmt == "text") { int m = (int)(s / 60); int dec = lap.Groups[3].Length > 0 ? lap.Groups[3].Length - 1 : 0; return m + ":" + (s - m * 60).ToString("00" + (dec > 0 ? "." + new string('0', dec) : ""), ci); }
                return s / scale;
            }
            int i = 0;
            string moved = NumberToken.Replace(text, m =>
            {
                string tok = m.Value;
                int dec = m.Groups[1].Success ? m.Groups[1].Value.Length - 1 : 0;
                double x = double.Parse(tok, ci);
                double unit = Math.Pow(10, -dec);
                double amp = Math.Max(unit * (dec > 0 ? 3 : 1), Math.Abs(x) * 0.03);
                double y = Math.Round((x + amp * DemoFormulas.Noise(e.Bind + "#" + i++, step)) / unit) * unit;
                if (x >= 0 && y < 0) y = 0;
                string s = y.ToString(dec > 0 ? "0." + new string('0', dec) : "0", ci);
                return tok.StartsWith("+") && y > 0 ? "+" + s : s;
            });
            if (fmt != "text" && fmt != "gear" && double.TryParse(moved, NumberStyles.Float, ci, out var n)) return n / scale;
            return moved;
        }

        private static int Clamp(int x, int lo, int hi) => Math.Max(lo, Math.Min(hi, x));
    }
}
