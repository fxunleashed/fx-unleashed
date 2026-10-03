using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using User.FXProRpmSync;

/// <summary>
/// `UsbTest.exe OUT audit [lovely-data-dir] [ams2.json]`: every car record we can read, through the same mapping the wheel
/// gets (RpmLayout.FromProfile), checked for the things that look wrong on the wheel: a mirrored car that isn't mirrored,
/// lights coming on out of order, a threshold the car has that the wheel loses, a flash before the last light. Prints a summary and
/// writes every finding to OUT/audit.txt. Not part of the regular run (it needs the data on disk).
/// </summary>
static class CarAudit
{
    sealed class Finding { public string Source, Car, Kind, Detail; }

    public static int Run(string outDir, string lovelyDir, string ams2Json)
    {
        var findings = new List<Finding>();
        int cars = 0, mirroredCars = 0, risingCars = 0, irregular = 0, unreadable = 0, darkFlash = 0;

        void Check(string source, string name, CarLedProfile car)
        {
            cars++;
            int n = car.LedNumber, w = RpmLightsMapper.WheelLeds;
            var curve = car.GearRpm.Values.GroupBy(v => string.Join(",", v)).OrderByDescending(g => g.Count()).First().First();
            bool Lit(int i) => curve[i] > 0 && RpmLightsMapper.ToSimProColor(car.Colors[i]) != null;
            var lit = Enumerable.Range(1, n).Where(Lit).ToList();
            void Add(string kind, string detail) => findings.Add(new Finding { Source = source, Car = name, Kind = kind, Detail = detail });

            // data quality (not ours to fix, but worth knowing)
            if (lit.Count == 0) { Add("no-lit-leds", "every LED is off or has no colour"); return; }
            int top = lit.Max(i => curve[i]);
            if (curve[0] > 0 && curve[0] < top) Add("redline-below-last-led", $"flash {curve[0]} < last LED {top}");
            if (n > 40) Add("many-leds", n + " LEDs");
            foreach (var g in car.GearRpm) if (g.Value.Length != n + 1) Add("short-gear-array", $"gear {g.Key}: {g.Value.Length} values for {n} LEDs");

            var layout = RpmLayout.FromProfile(car, includeGears: true, exactColours: true);
            var wheel = layout.Rpm;
            if (wheel.Length != w) { Add("wheel-length", wheel.Length + " LEDs"); return; }

            // shape of the car
            bool mirrored = n >= 3 && Enumerable.Range(1, n / 2).All(i => Lit(i) == Lit(n + 1 - i) && (!Lit(i) || curve[i] == curve[n + 1 - i]));
            var litCurve = lit.Select(i => curve[i]).ToList();
            bool rising = litCurve.Zip(litCurve.Skip(1), (a, b) => b >= a).All(x => x);
            bool falling = litCurve.Zip(litCurve.Skip(1), (a, b) => b <= a).All(x => x);
            var onWheel = wheel.Where(r => r > 0).ToList();

            if (mirrored && lit.Count > 2)
            {
                mirroredCars++;
                if (!wheel.SequenceEqual(wheel.Reverse())) Add("mirrored-car-not-mirrored", string.Join(",", wheel));
                var halfUp = wheel.Take(w / 2 + 1).Where(r => r > 0).ToList();
                if (!halfUp.Zip(halfUp.Skip(1), (a, b) => b >= a).All(x => x) && !halfUp.Zip(halfUp.Skip(1), (a, b) => b <= a).All(x => x))
                    Add("mirrored-car-out-of-order", string.Join(",", wheel));
            }
            else if (rising)
            {
                risingCars++;
                if (!onWheel.Zip(onWheel.Skip(1), (a, b) => b >= a).All(x => x)) Add("rising-car-out-of-order", string.Join(",", wheel));
            }
            else if (falling)
            {
                if (!onWheel.Zip(onWheel.Skip(1), (a, b) => b <= a).All(x => x)) Add("falling-car-out-of-order", string.Join(",", wheel));
            }
            else irregular++;

            // every distinct threshold the car has should still be on the wheel when the wheel has room
            if (lit.Count <= w)
            {
                var lost = litCurve.Distinct().Except(wheel).ToList();
                if (lost.Count > 0) Add("threshold-lost", "car " + string.Join("/", lost) + " not on the wheel: " + string.Join(",", wheel));
            }
            // all lit LEDs lit by the top threshold
            if (onWheel.Count > 0 && onWheel.Max() != top) Add("top-threshold-changed", $"car {top}, wheel {onWheel.Max()}");
            // flash colour present
            bool hasFlashColour = RpmLightsMapper.ToSimProColor(car.Colors[0]) != null;
            if (curve[0] > 0 && layout.FlashRpm == 0 && (hasFlashColour || car.RedlineBlinkIntervalMs > 0)) Add("flash-dropped", $"redline {curve[0]} but no flash");
            if (layout.FlashDark) darkFlash++;
            // per-gear curves line up with the same LEDs
            if (layout.Gears != null)
                foreach (var g in layout.Gears)
                    for (int j = 0; j < w; j++)
                        if ((layout.Rpm[j] > 0) != (g.Value.Rpm[j] > 0)) { Add("gear-lights-differ", $"gear {g.Key} LED {j}"); break; }
        }

        // Lovely Car Data
        if (Directory.Exists(lovelyDir))
        {
            foreach (var f in Directory.GetFiles(lovelyDir, "*.json", SearchOption.AllDirectories))
            {
                var game = Path.GetFileName(Path.GetDirectoryName(f));
                if (game == "data" || Path.GetFileName(f) == "manifest.json" || Path.GetFileName(f) == "car-classes.json") continue;
                try
                {
                    var car = CarLedDatabase.Parse(JObject.Parse(File.ReadAllText(f)));
                    if (car == null) { unreadable++; findings.Add(new Finding { Source = "lovely/" + game, Car = Path.GetFileName(f), Kind = "unreadable", Detail = "Parse returned null" }); continue; }
                    Check("lovely/" + game, Path.GetFileNameWithoutExtension(f), car);
                }
                catch (Exception ex) { unreadable++; findings.Add(new Finding { Source = "lovely/" + game, Car = Path.GetFileName(f), Kind = "unreadable", Detail = ex.Message }); }
            }
        }
        // the games' own data
        if (File.Exists(ams2Json))
        {
            foreach (JObject c in (JObject.Parse(File.ReadAllText(ams2Json))["cars"] as JArray).OfType<JObject>())
            {
                var rec = CarLightsDatabase.ParseCar(c);
                if (rec.Rev == null) continue;
                var car = CarLightsDatabase.ToProfile(rec.Rev, rec.CarId, out _);
                Check("ams2-own", rec.CarId, car);
            }
        }

        Console.WriteLine($"audited {cars} cars ({mirroredCars} mirrored, {risingCars} rising, {irregular} irregular), {unreadable} unreadable; {darkFlash} blink their lights out at the redline");
        foreach (var g in findings.GroupBy(f => f.Kind).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-28} {g.Count(),4}   e.g. {g.First().Source}: {g.First().Car}");
        File.WriteAllLines(Path.Combine(outDir, "audit.txt"), findings.OrderBy(f => f.Kind).ThenBy(f => f.Source).ThenBy(f => f.Car).Select(f => $"{f.Kind}\t{f.Source}\t{f.Car}\t{f.Detail}"));
        Console.WriteLine("details: " + Path.Combine(outDir, "audit.txt"));
        return 0;
    }
}
