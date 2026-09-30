using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Finds a car's ideal upshift point per gear from a lap or two of telemetry (docs/usb-mode.md "Auto calibration").
    /// Not from when the driver shifts (that learns habits, or the lights' own point back), but from the car: in the
    /// full-throttle stretches each gear's ratio (RPM per km/h) and its pull at each RPM are measured; the best upshift is
    /// where the next gear, at the RPM it lands on, pulls harder than this one. If it never does before the limiter, the
    /// best point is just short of the limiter (also measured: where the revs stop rising at full throttle).
    /// Samples come from DataUpdate; everything here is plain maths, so it's tested against a simulated car.
    /// </summary>
    public sealed class ShiftCalibrator
    {
        /// <summary>One telemetry sample. Throttle, brake and clutch in % (clutch = pressed).</summary>
        public struct Sample
        {
            public double Time, Rpm, SpeedKmh, Throttle, Brake, Clutch;
            public int Gear;
        }

        /// <summary>A gear's result: the upshift from it, or why there isn't one yet.</summary>
        public sealed class GearResult
        {
            public int Gear;
            /// <summary>The best upshift RPM, or null while it can't be told yet.</summary>
            public double? ShiftRpm;
            /// <summary>The next gear never pulls harder: shift at the limiter.</summary>
            public bool AtLimiter;
            /// <summary>What's missing, for the settings page ("hold it flat out to the limiter in 3rd").</summary>
            public string Need;
            /// <summary>0-1: how much of this gear's useful rev range has full-throttle data.</summary>
            public double Coverage;
            /// <summary>
            /// The next gear's pull where it lands was worked out from this gear lower in its pull (same engine torque at the
            /// same revs, through the next gear's ratio), because flat-out pulls only reach the next gear high in its revs.
            /// Close, but it leaves out that air resistance is higher at the shift than lower in the pull.
            /// </summary>
            public bool Estimated;
        }

        public const int Bin = 100;              // rpm per bucket
        public const double MinThrottle = 95;    // % counted as flat out
        public const double Settle = 0.3;        // s after a gear change before samples count (the shift itself)
        public const double Window = 0.25;       // s over which acceleration is measured
        public const int MinPerBin = 2;

        private readonly Dictionary<int, List<double>> ratios = new Dictionary<int, List<double>>();
        private readonly Dictionary<int, Dictionary<int, List<double>>> pull = new Dictionary<int, Dictionary<int, List<double>>>();
        private readonly List<Sample> run = new List<Sample>();
        private int lastGear = int.MinValue;
        private double gearSince;
        private double topRpm;

        /// <summary>Full-throttle samples taken so far.</summary>
        public int Samples { get; private set; }

        /// <summary>The highest RPM seen at full throttle: the limiter, once the car has been held against it.</summary>
        public double Limiter => topRpm;

        public void Add(Sample s)
        {
            if (s.Gear != lastGear) { lastGear = s.Gear; gearSince = s.Time; run.Clear(); }
            bool flatOut = s.Gear >= 1 && s.Throttle >= MinThrottle && s.Brake < 2 && s.Clutch < 5 && s.SpeedKmh > 15 && s.Rpm > 500;
            if (!flatOut || s.Time - gearSince < Settle) { run.Clear(); return; }
            if (run.Count > 0 && s.Time - run[run.Count - 1].Time > 0.15) run.Clear(); // a gap in the data
            run.Add(s);
            while (run.Count > 2 && run[0].Time < s.Time - Window) run.RemoveAt(0);
            Samples++;
            topRpm = Math.Max(topRpm, s.Rpm);
            List(ratios, s.Gear).Add(s.Rpm / s.SpeedKmh);
            if (run.Count < 3 || s.Time - run[0].Time < Window * 0.8) return;
            // acceleration: the slope of speed over the window (least squares), m/s²
            double mt = run.Average(r => r.Time), mv = run.Average(r => r.SpeedKmh / 3.6);
            double num = run.Sum(r => (r.Time - mt) * (r.SpeedKmh / 3.6 - mv)), den = run.Sum(r => (r.Time - mt) * (r.Time - mt));
            if (den <= 0) return;
            double rpm = run.Average(r => r.Rpm);
            if (!pull.TryGetValue(s.Gear, out var bins)) pull[s.Gear] = bins = new Dictionary<int, List<double>>();
            List(bins, (int)Math.Round(rpm / Bin)).Add(num / den);
        }

        private static List<double> List<TKey>(Dictionary<TKey, List<double>> d, TKey k)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<double>();
            return l;
        }

        private static double Median(List<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            return s.Count == 0 ? double.NaN : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        /// <summary>The gears seen at full throttle, lowest first.</summary>
        public IEnumerable<int> Gears => ratios.Keys.OrderBy(g => g);

        /// <summary>RPM per km/h in a gear (null = not seen).</summary>
        public double? Ratio(int gear) => ratios.TryGetValue(gear, out var r) && r.Count >= 5 ? Median(r) : (double?)null;

        /// <summary>A gear's pull (m/s²) by RPM, smoothed over neighbouring buckets; only buckets with enough samples.</summary>
        public SortedDictionary<double, double> Curve(int gear)
        {
            var result = new SortedDictionary<double, double>();
            if (!pull.TryGetValue(gear, out var bins)) return result;
            var ok = bins.Where(b => b.Value.Count >= MinPerBin).ToDictionary(b => b.Key, b => Median(b.Value));
            foreach (var k in ok.Keys)
            {
                var near = new[] { k - 1, k, k + 1 }.Where(ok.ContainsKey).Select(x => ok[x]).ToList();
                result[k * Bin] = near.Average();
            }
            return result;
        }

        private static double? At(SortedDictionary<double, double> curve, double rpm)
        {
            if (curve.Count == 0) return null;
            var keys = curve.Keys.ToList();
            if (rpm < keys[0] - Bin || rpm > keys[keys.Count - 1] + Bin) return null;
            if (rpm <= keys[0]) return curve[keys[0]];
            if (rpm >= keys[keys.Count - 1]) return curve[keys[keys.Count - 1]];
            int i = keys.FindLastIndex(k => k <= rpm);
            double a = keys[i], b = keys[i + 1];
            if (b - a > Bin * 3) return null; // a hole in the data
            return curve[a] + (curve[b] - curve[a]) * (rpm - a) / (b - a);
        }

        /// <summary>
        /// The engine's torque curve (relative units) from every gear: pull ÷ gear ratio (the same engine torque shows as
        /// more pull in a lower gear). Each RPM from the lowest gear that covered it, where air resistance takes least off.
        /// Flat-out pulls enter every gear above first partway up its revs, so this is how the low end of those gears is known.
        /// </summary>
        public SortedDictionary<double, double> TorqueCurve()
        {
            var t = new SortedDictionary<double, double>();
            foreach (var g in Gears)
            {
                var ratio = Ratio(g);
                if (ratio == null) continue;
                foreach (var kv in Curve(g))
                    if (!t.ContainsKey(kv.Key)) t[kv.Key] = kv.Value / ratio.Value;
            }
            return t;
        }

        /// <summary>
        /// The upshift from every gear seen that has a gear above it. Measured directly where the next gear has data at the
        /// revs it lands on; otherwise from the torque curve (Estimated): the two gears' force at the same road speed, where
        /// air resistance is the same for both and drops out.
        /// </summary>
        public List<GearResult> Results()
        {
            var list = new List<GearResult>();
            var gears = Gears.ToList();
            var torque = TorqueCurve();
            double limiter = topRpm;
            foreach (var g in gears)
            {
                int next = g + 1;
                var r = new GearResult { Gear = g };
                list.Add(r);
                var here = Curve(g);
                double? rg = Ratio(g), rn = Ratio(next);
                double from = limiter * 0.55;
                r.Coverage = here.Count == 0 ? 0 : Math.Min(1, here.Keys.Count(k => k >= from) * Bin / Math.Max(Bin, limiter - from));
                if (here.Count == 0 || rg == null) { r.Need = $"Full throttle in gear {g}."; continue; }
                if (!gears.Contains(next) || rn == null) { r.Need = $"Full throttle in gear {next} too."; continue; }
                double factor = rn.Value / rg.Value; // RPM after the shift = RPM before x this

                // 1) measured: this gear's pull against the next gear's where it lands
                var there = Curve(next);
                var direct = Crossing(here.Keys, rpm => here[rpm], rpm => At(there, rpm * factor), out bool directAbove);
                if (direct.HasValue) { r.ShiftRpm = Math.Round(direct.Value / 10) * 10; }
                else
                {
                    // 2) from the torque curve: force in this gear at rpm vs the next gear at rpm x factor (same speed)
                    var keys = torque.Keys.Where(k => k >= limiter * 0.5 && k <= limiter).ToList();
                    var est = Crossing(keys, rpm => At(torque, rpm) * rg.Value, rpm => At(torque, rpm * factor) * rn.Value, out bool estAbove);
                    double top = keys.Count > 0 ? keys.Max() : 0;
                    if (est.HasValue) { r.ShiftRpm = Math.Round(est.Value / 10) * 10; r.Estimated = true; }
                    else if ((directAbove || estAbove) && Math.Max(here.Keys.Max(), top) >= limiter - 3 * Bin) { r.ShiftRpm = Math.Round((limiter - 60) / 10) * 10; r.AtLimiter = true; }
                    else if (here.Keys.Max() < limiter - 3 * Bin) r.Need = $"Hold gear {g} flat out closer to the limiter.";
                    else r.Need = $"More full-throttle driving at lower revs (out of slower corners) to finish {g} → {next}.";
                }
                // guard against a noisy crossing low in the range
                if (r.ShiftRpm.HasValue && r.ShiftRpm < limiter * 0.6) { r.ShiftRpm = null; r.AtLimiter = r.Estimated = false; r.Need = $"More full-throttle laps in gears {g} and {next} (the reading isn't steady yet)."; }
            }
            return list;
        }

        /// <summary>
        /// Where `stay` (this gear's pull) drops to `shift` (the next gear's) going up the revs; null if it doesn't.
        /// `stillAbove`: this gear was still pulling harder at the highest RPM compared (so: shift at the limiter).
        /// </summary>
        private static double? Crossing(IEnumerable<double> rpms, Func<double, double?> stay, Func<double, double?> shift, out bool stillAbove)
        {
            double? lastDiff = null, lastRpm = null;
            stillAbove = false;
            foreach (var rpm in rpms)
            {
                var a1 = stay(rpm); var a2 = shift(rpm);
                if (a1 == null || a2 == null) { lastDiff = null; continue; }
                double diff = a1.Value - a2.Value;
                if (diff <= 0 && lastDiff.HasValue && lastDiff.Value > 0)
                    return lastRpm.Value + (rpm - lastRpm.Value) * lastDiff.Value / (lastDiff.Value - diff);
                lastDiff = diff; lastRpm = rpm;
            }
            stillAbove = lastDiff.HasValue && lastDiff.Value > 0;
            return null;
        }

        /// <summary>Upshift RPM by gear ("1", "2", ...) for the gears with a result.</summary>
        public Dictionary<string, int> ShiftByGear() =>
            Results().Where(r => r.ShiftRpm.HasValue).ToDictionary(r => r.Gear.ToString(System.Globalization.CultureInfo.InvariantCulture), r => (int)r.ShiftRpm.Value);
    }
}
