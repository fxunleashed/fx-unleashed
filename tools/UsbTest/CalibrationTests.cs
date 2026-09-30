using System;
using System.Collections.Generic;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// Auto calibration (ShiftCalibrator): a simulated car with a known torque curve and gearing is driven flat out
/// through its gears; the calibrator must find the physics answer (where the next gear, at the RPM it lands on, pulls
/// harder), worked out here by brute force. Plus applying it to a car's lights per gear.
/// </summary>
static class CalibrationTests
{
    public static void Run()
    {
        Peaky();
        Flat();
        Apply();
    }

    /// <summary>A simulated car: torque by RPM, RPM per km/h per gear, a limiter.</summary>
    sealed class SimCar
    {
        public Func<double, double> Torque;
        public double[] Ratio;       // index = gear
        public double Limiter;

        // scaled so first gear pulls about 0.7 g, like a GT3 car
        double Force(int gear, double rpm) => Torque(Math.Min(rpm, Limiter)) * Ratio[gear] * 0.25;

        /// <summary>Flat out from 25 km/h in first, each gear to the limiter, a 0.2 s shift; samples at 60 Hz (a little speed noise).</summary>
        public List<ShiftCalibrator.Sample> Drive(int topGear, int seed = 1)
        {
            var rnd = new Random(seed);
            var list = new List<ShiftCalibrator.Sample>();
            double t = 0, v = 25 / 3.6, dt = 1 / 60.0;
            int gear = 1;
            double shiftUntil = -1;
            while (t < 120)
            {
                double rpm = v * 3.6 * Ratio[gear];
                bool shifting = t < shiftUntil;
                double drag = 0.45 * v * v + 150;
                double a = shifting ? -drag / 1300 : (Force(gear, rpm) - drag) / 1300;
                if (rpm >= Limiter && !shifting) a = Math.Min(a, 0); // bouncing on the limiter
                v += a * dt;
                t += dt;
                list.Add(new ShiftCalibrator.Sample
                {
                    Time = t, Gear = gear, Rpm = v * 3.6 * Ratio[gear], SpeedKmh = v * 3.6 + (rnd.NextDouble() - 0.5) * 0.06,
                    Throttle = shifting ? 0 : 100, Brake = 0, Clutch = shifting ? 100 : 0,
                });
                if (!shifting && rpm >= Limiter - 1)
                {
                    if (gear == topGear) { if (t > shiftUntil + 3) break; continue; }
                    gear++; shiftUntil = t + 0.2;
                }
            }
            return list;
        }

        /// <summary>The physics answer: the lowest RPM where the next gear pulls at least as hard (null = never before the limiter).</summary>
        public double? Best(int gear)
        {
            for (double r = 2500; r <= Limiter; r += 5)
                if (Force(gear + 1, r * Ratio[gear + 1] / Ratio[gear]) >= Force(gear, r)) return r;
            return null;
        }
    }

    static void Peaky()
    {
        // torque peaks at 5200 and falls away hard: the best upshifts come before the limiter in the lower gears
        var car = new SimCar
        {
            Torque = r => r < 5200 ? 300 + 220 * (r - 2000) / 3200 : 520 - 250 * (r - 5200) / 2200,
            Ratio = new[] { 0, 72.0, 52, 42, 35.5, 31 }, Limiter = 7400,
        };
        // two flat-out runs through the gears, as in a lap or two (the clock keeps running between them)
        var c = new ShiftCalibrator();
        var second = car.Drive(5, seed: 2);
        foreach (var s in car.Drive(5)) c.Add(s);
        foreach (var s in second) { var x = s; x.Time += 200; c.Add(x); }
        var results = c.Results();
        Check("calibration: every gear with one above gets a result", Enumerable.Range(1, 4).All(g => results.Any(r => r.Gear == g && r.ShiftRpm.HasValue)),
              string.Join("; ", results.Select(r => $"{r.Gear}: {r.ShiftRpm?.ToString() ?? r.Need}")));
        Check("calibration: the limiter is found", Math.Abs(c.Limiter - 7400) < 30, c.Limiter.ToString("0"));
        foreach (var g in Enumerable.Range(1, 4))
        {
            var r = results.FirstOrDefault(x => x.Gear == g);
            var best = car.Best(g);
            bool ok = r?.ShiftRpm != null && (best.HasValue ? !r.AtLimiter && Math.Abs(r.ShiftRpm.Value - best.Value) <= 150 : r.AtLimiter);
            Check($"calibration: {g} → {g + 1} matches the physics", ok, $"measured {r?.ShiftRpm?.ToString("0") ?? r?.Need}, physics {best?.ToString("0") ?? "at the limiter"}");
        }
        var top = results.FirstOrDefault(r => r.Gear == 5);
        Check("calibration: the top gear has nothing to shift to", top == null || (!top.ShiftRpm.HasValue && top.Need.Contains("gear 6")));
        Check("calibration: shift points by gear", c.ShiftByGear().Keys.OrderBy(k => k).SequenceEqual(new[] { "1", "2", "3", "4" }));
    }

    static void Flat()
    {
        // torque that keeps rising: the next gear never pulls harder, so shift at the limiter
        var car = new SimCar { Torque = r => 300 + 0.04 * r, Ratio = new[] { 0, 70.0, 50, 40 }, Limiter = 8000 };
        var c = new ShiftCalibrator();
        foreach (var s in car.Drive(3)) c.Add(s);
        foreach (var s in car.Drive(3, seed: 2)) { var x = s; x.Time += 200; c.Add(x); }
        var r1 = c.Results().FirstOrDefault(r => r.Gear == 1);
        Check("calibration: a car that pulls to the limiter shifts at the limiter", r1?.AtLimiter == true && Math.Abs(r1.ShiftRpm.Value - 7940) <= 40,
              r1?.ShiftRpm?.ToString() ?? r1?.Need);

        // not enough yet: only part of first gear
        var partial = new ShiftCalibrator();
        foreach (var s in car.Drive(3).Where(s => s.Gear == 1 && s.Rpm < 6000)) partial.Add(s);
        Check("calibration: says what's missing instead of guessing", partial.Results().All(r => !r.ShiftRpm.HasValue && !string.IsNullOrEmpty(r.Need)));

        // part throttle and braking don't count
        var coast = new ShiftCalibrator();
        foreach (var s in car.Drive(2)) { var x = s; x.Throttle = 60; coast.Add(x); }
        Check("calibration: part throttle is ignored", coast.Samples == 0);
    }

    static void Apply()
    {
        var baseLayout = new RpmLayout { FlashRpm = 6800 };
        for (int i = 0; i < 15; i++) { baseLayout.Rpm[i] = 5000 + i * 100; baseLayout.Colors[i] = "#00FF00"; }
        var shifts = new Dictionary<string, int> { ["1"] = 7000, ["2"] = 6900, ["3"] = 7100 };
        var o = new CarOverride { CarKey = "AMS2 | x", Kind = OverrideKind.Calibrated, ShiftByGear = shifts };
        var l = o.Apply(baseLayout, null);
        Check("calibrated lights: each gear flashes at its measured point", l.ForGear("1").FlashRpm == 7000 && l.ForGear("2").FlashRpm == 6900 && l.ForGear("3").FlashRpm == 7100);
        Check("calibrated lights: the pattern's spacing stays the car's", l.ForGear("2").Rpm[14] - l.ForGear("2").Rpm[0] == 1400 && l.ForGear("2").Rpm[0] == 5100);
        Check("calibrated lights: higher gears take the top measured one, R and N first gear's", l.ForGear("6").FlashRpm == 7100 && l.ForGear("N").FlashRpm == 7000 && l.ForGear("R").FlashRpm == 7000);
        Check("calibrated lights: the summary names them", o.Summary.Contains("1→ 7000") && o.Summary.Contains("3→ 7100"));
        var rt = Newtonsoft.Json.JsonConvert.DeserializeObject<CarOverride>(Newtonsoft.Json.JsonConvert.SerializeObject(o));
        Check("calibrated lights: saved and loaded", rt.Kind == OverrideKind.Calibrated && rt.ShiftByGear["2"] == 6900);
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<CarOverride>("{\"Kind\":1,\"OffsetRpm\":0}");
        Check("calibrated lights: older overrides load as they were", old.Kind == OverrideKind.Custom);
    }
}
