using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;

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
        };

        private readonly Dictionary<string, object> v = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Light inputs
        public double Rpm, MaxRpm, Redline;
        public string GearKey = "N";
        public bool AbsActive, TcActive, PitLimiter, Drs, InPitLane;
        public bool BlueFlag, YellowFlag, GreenFlag, WhiteFlag, CheckeredFlag, BlackFlag;
        public double FuelPercent = 100;
        public bool Running;

        public void Set(string key, object value) { if (value != null) v[key] = value; }

        public double? Number(string key)
        {
            if (key == null || !v.TryGetValue(key, out var o) || o == null) return null;
            switch (o)
            {
                case double d: return double.IsNaN(d) ? (double?)null : d;
                case bool b: return b ? 1 : 0;
                case TimeSpan ts: return ts.TotalSeconds;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : (double?)null;
                case IConvertible c: try { return c.ToDouble(CultureInfo.InvariantCulture); } catch { return null; }
                default: return null;
            }
        }

        public string Text(string key) => key != null && v.TryGetValue(key, out var o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : null;

        // ---------- Live: SimHub ----------

        private const string Raw = "DataCorePlugin.GameRawData.";

        /// <summary>From SimHub's data; `props` = the "prop:" bindings of the active dash.</summary>
        public static DashValues FromSimHub(GameData data, PluginManager pm, IEnumerable<string> props)
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
                r.Set("rpm", d.Rpms);
                r.MaxRpm = d.CarSettings_MaxRPM > 0 ? d.CarSettings_MaxRPM : d.MaxRpm;
                r.Redline = d.CarSettings_RedLineRPM > 0 ? d.CarSettings_RedLineRPM : d.Redline;
                r.Rpm = d.Rpms;
                r.Set("maxRpm", r.MaxRpm);
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
                var ve = Prop(pm, Raw + "PlayerNativeTelemetry.mVirtualEnergy");
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
                if (r.Number("tcCut") == null) r.Set("tcCut", Prop(pm, Raw + "PlayerNativeTelemetry.mTCCut"));
                r.Set("tcSlip", Prop(pm, Raw + "PlayerNativeTelemetry.mTCSlip"));
                r.AbsActive = d.ABSActive != 0;
                r.TcActive = d.TCActive != 0;
                r.PitLimiter = d.PitLimiterOn != 0;
                r.Drs = d.DRSEnabled != 0;
                r.BlueFlag = d.Flag_Blue != 0; r.YellowFlag = d.Flag_Yellow != 0; r.GreenFlag = d.Flag_Green != 0;
                r.WhiteFlag = d.Flag_White != 0; r.CheckeredFlag = d.Flag_Checkered != 0; r.BlackFlag = d.Flag_Black != 0;
            });
            if (props != null)
                foreach (var p in props)
                    if (p.StartsWith("prop:", StringComparison.OrdinalIgnoreCase))
                        Try(() => r.Set(p, pm.GetPropertyValue(p.Substring(5))));
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
            r.Rpm = t.Get("rpm"); r.MaxRpm = t.Get("maxRpm"); r.Redline = r.MaxRpm * 0.96;
            r.Set("rpm", r.Rpm); r.Set("maxRpm", r.MaxRpm);
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
            r.FuelPercent = t.Get("fuelPercent");
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
            r.BlueFlag = t.Get("blueFlag") > 0; r.YellowFlag = t.Get("yellowFlag") > 0;
            return r;
        }
    }

    /// <summary>
    /// The USB demo: the plugin's DemoCar lap, plus what the Mustang dash shows on top: fuel per lap, virtual energy,
    /// and a driver changing a setting every 12-20 s (so the pop-up shows).
    /// </summary>
    internal sealed class UsbDemo
    {
        private readonly DemoCar car = new DemoCar();
        public readonly SimProTelemetry Telemetry = new SimProTelemetry();
        private readonly Random rng = new Random();
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
            return DashValues.FromDemo(Telemetry, this);
        }

        private static int Clamp(int x, int lo, int hi) => Math.Max(lo, Math.Min(hi, x));
    }
}
