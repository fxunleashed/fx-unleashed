using System;
using System.Collections.Generic;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// Lights per car state (docs/light-states-plan.md): the state detector on scripted sessions, state looks, the rev tint,
/// pit limiter lights (per preset and per car), start-up / shutdown, the new presets and the Prism rename.
/// </summary>
static class LightStateTests
{
    public static void Run()
    {
        CarData();
        Tracker();
        Gauges();
        Looks();
        Spotter();
        Extras();
        Limiter();
        Takeovers();
        Presets();
        PerCar();
    }

    static DashValues Car(bool engine, double rpm = 0, bool limiter = false, bool menu = false) =>
        new DashValues { Running = true, EngineOn = engine, Rpm = rpm, MaxRpm = 8000, Redline = 7600, PitLimiter = limiter, InMenu = menu, FuelPercent = 50 };

    static void Tracker()
    {
        var t = new CarStateTracker();
        double now = 0;
        CarState Step(DashValues v, double dt = 0.05) { now += dt; return t.Update(v, now); }

        Check("state: no game = idle", Step(new DashValues()) == CarState.Idle);
        Check("state: game in a menu", Step(Car(false, menu: true)) == CarState.Menu);
        Check("state: in the car, engine off", Step(Car(false)) == CarState.EngineOff);
        // a stumble shorter than the debounce doesn't start anything
        Step(Car(true, 900)); Step(Car(false));
        Check("state: a 0.05 s blip isn't an engine start", Step(Car(false)) == CarState.EngineOff);
        Step(Car(true, 900));
        for (int i = 0; i < 10; i++) Step(Car(true, 900));
        Check("state: engine start plays the start-up", t.State == CarState.Starting && t.Progress(now) > 0 && t.Progress(now) < 1);
        for (int i = 0; i < 40; i++) Step(Car(true, 3000));
        Check("state: then driving", t.State == CarState.Driving && t.Progress(now) == 0);
        Check("state: pit limiter", Step(Car(true, 3000, limiter: true)) == CarState.PitLimiter);
        Check("state: limiter off, driving", Step(Car(true, 3000)) == CarState.Driving);
        for (int i = 0; i < 10; i++) Step(Car(false));
        Check("state: engine off after driving plays the shutdown", t.State == CarState.Stopping);
        for (int i = 0; i < 40; i++) Step(Car(false));
        Check("state: then engine off", t.State == CarState.EngineOff);
        Check("state: pausing = menu", Step(Car(false, menu: true)) == CarState.Menu);
        Check("state: game closed = idle", Step(new DashValues()) == CarState.Idle);

        // joining a session with the engine already running: straight to driving, no start-up
        var j = new CarStateTracker();
        j.Update(new DashValues(), 0);
        Check("state: joining with the engine running skips the start-up", j.Update(Car(true, 2000), 0.1) == CarState.Driving);

        // a game that never reports ignition: revs up = engine on (DashValues.FromSimHub sets EngineOn from the revs)
        var g = new CarStateTracker();
        Check("state: from the menu into a running car", g.Update(Car(false, menu: true), 0) == CarState.Menu && g.Update(Car(true, 850), 0.1) == CarState.Driving);
    }

    /// <summary>The AMS2 BMW M4 GT3 from Lovely Car Data: 12 LEDs with a gap each side, outside in.</summary>
    static CarLedProfile M4()
    {
        var curve = new[] { 6800, 4800, 5200, 0, 5600, 6000, 6400, 6400, 6000, 5600, 0, 5200, 4800 };
        return new CarLedProfile
        {
            CarName = "BMW M4 GT3", LedNumber = 12,
            Colors = new[] { "#FFFF0000", "#FF00FF00", "#FF00FF00", "#00000000", "#FFFFFF00", "#FFFFFF00", "#FFFF0000", "#FFFF0000", "#FFFFFF00", "#FFFFFF00", "#00000000", "#FF00FF00", "#FF00FF00" },
            GearRpm = new Dictionary<string, int[]> { ["N"] = curve, ["1"] = curve, ["2"] = curve },
        };
    }

    static void CarData()
    {
        var usb = RpmLayout.FromProfile(M4(), includeGears: false, exactColours: true);
        Check("car data: 12 LEDs on 15 keep one-LED gaps (G G G . Y Y R R R Y Y . G G G)",
              usb.Rpm.SequenceEqual(new[] { 4800, 5200, 5200, 0, 5600, 6000, 6400, 6400, 6400, 6000, 5600, 0, 5200, 5200, 4800 }));
        Check("car data: USB mode keeps the car's own colours", usb.Colors[0] == "#00FF00" && usb.Colors[4] == "#FFFF00" && usb.Colors[7] == "#FF0000" && usb.FlashColor == "#FF0000" && usb.FlashRpm == 6800);
        var simpro = RpmLayout.FromProfile(M4(), includeGears: false);
        Check("car data: SimPro still gets its palette", simpro.Colors[7] == RpmLightsMapper.ToSimProColor("#FFFF0000") && simpro.Rpm.SequenceEqual(usb.Rpm));
        // a car with as many LEDs as the wheel, and one with more, are unchanged by the stretch
        var fifteen = new CarLedProfile { LedNumber = 15, Colors = Enumerable.Repeat("#FF00FF00", 16).ToArray(), GearRpm = new Dictionary<string, int[]> { ["N"] = Enumerable.Range(0, 16).Select(i => 4000 + i * 100).ToArray() } };
        Check("car data: 15 LEDs map one to one", RpmLayout.FromProfile(fifteen, false).Rpm.SequenceEqual(Enumerable.Range(1, 15).Select(i => 4000 + i * 100)));

        // mirrored cars stay mirrored on the wheel (the Ligier JS P320: 10 lights, 5 steps each side)
        CarLedProfile Mirror(int[] curve) => new CarLedProfile { LedNumber = curve.Length, Colors = Enumerable.Repeat("#FF00FF00", curve.Length + 1).ToArray(),
            GearRpm = new Dictionary<string, int[]> { ["1"] = new[] { 6700 }.Concat(curve).ToArray() } };
        var p320 = RpmLayout.FromProfile(Mirror(new[] { 6000, 6000, 6170, 6340, 6525, 6525, 6340, 6170, 6000, 6000 }), false).Rpm;
        Check("car data: 10 mirrored lights stay mirrored on 15", p320.SequenceEqual(new[] { 6000, 6000, 6000, 6170, 6340, 6340, 6525, 6525, 6525, 6340, 6340, 6170, 6000, 6000, 6000 }), string.Join(",", p320));
        foreach (var n in new[] { 4, 6, 8, 9, 11, 12, 13, 14 })
        {
            var half = Enumerable.Range(0, n / 2).Select(i => 4000 + i * 150).ToArray();
            var curve = half.Concat(n % 2 == 1 ? new[] { 4000 + (n / 2) * 150 } : new int[0]).Concat(half.Reverse()).ToArray();
            var rpm = RpmLayout.FromProfile(Mirror(curve), false).Rpm;
            Check($"car data: {n} mirrored lights are mirrored on the wheel", rpm.SequenceEqual(rpm.Reverse()) && rpm[0] == 4000 && rpm[7] == curve.Max(), string.Join(",", rpm));
        }
        var rising = RpmLayout.FromProfile(Mirror(Enumerable.Range(0, 10).Select(i => 4000 + i * 100).ToArray()), false).Rpm;
        Check("car data: a rising bar (not mirrored) is stretched as before", rising.SequenceEqual(new[] { 4000, 4100, 4100, 4200, 4300, 4300, 4400, 4500, 4500, 4600, 4700, 4700, 4800, 4900, 4900 }), string.Join(",", rising));
    }

    static void Spotter()
    {
        var fx = WheelModel.FxPro;
        var e = new LightEngine(fx);
        var p = LightPresets.Find("synthwave").Clone();
        var theirs = Enumerable.Repeat(new LedColor(10, 20, 30, 90), fx.LedCount).ToArray();
        var none = Car(true, 3000);
        Check("spotter: nothing alongside leaves another program's lights alone (same frame)", ReferenceEquals(e.OverlaySpotter(theirs, p, none, 1), theirs));
        var left = Car(true, 3000); left.SpotterLeft = true;
        var f = e.OverlaySpotter(theirs, p, left, 1);
        Check("spotter: a car on the left lights the left side lights over them", Enumerable.Range(17, 3).All(i => f[i].R > 100) && Enumerable.Range(20, 3).All(i => f[i].R == 10));
        Check("spotter: everything else is theirs", Enumerable.Range(0, 17).Concat(Enumerable.Range(20, 18)).All(i => f[i].R == 10 && f[i].G == 20 && f[i].B == 30) && theirs.All(c => c.R == 10));
        var right = Car(true, 3000); right.SpotterRight = true;
        var fr = e.OverlaySpotter(theirs, p, right, 1);
        Check("spotter: a car on the right lights the right side lights", Enumerable.Range(20, 3).All(i => fr[i].R > 100) && Enumerable.Range(17, 3).All(i => fr[i].R == 10));
        var both = Car(true, 3000); both.SpotterLeft = both.SpotterRight = true;
        Check("spotter: both sides at once", Enumerable.Range(17, 6).All(i => e.OverlaySpotter(theirs, p, both, 1)[i].R > 100));
        var abs = Car(true, 3000); abs.AbsActive = true; abs.BlueFlag = true;
        Check("spotter: only the spotter is drawn over them (no ABS or flag alerts)", ReferenceEquals(e.OverlaySpotter(theirs, p, abs, 1), theirs));
        var off = p.Clone(); foreach (var a in off.Alerts.Where(a => a.Trigger == AlertTrigger.SpotterLeft)) a.Enabled = false;
        Check("spotter: a preset with the alert switched off draws nothing", ReferenceEquals(e.OverlaySpotter(theirs, off, left, 1), theirs));
        var small = new LedColor[10];
        Check("spotter: a frame of another size is left alone", ReferenceEquals(e.OverlaySpotter(small, p, left, 1), small));
        var neoFrame = Enumerable.Repeat(new LedColor(10, 20, 30, 90), 73).ToArray();
        var ng = new LightEngine(WheelModel.GtNeo).OverlaySpotter(neoFrame, LightPresets.Find("neo-aurora").Clone(), left, 1);
        Check("spotter: the GT Neo shows it on the left end of its rev bar", Enumerable.Range(58, 4).All(i => ng[i].R > 100) && ng[70].R == 10);

        Check("session: time trial, hot lap and lone qualifying are solo", DashValues.SoloSession("Time Trial") && DashValues.SoloSession("TIME_TRIAL") && DashValues.SoloSession("HotLap")
              && DashValues.SoloSession("Lone Qualify") && !DashValues.SoloSession("RACE") && !DashValues.SoloSession("Practice") && !DashValues.SoloSession(null));
        Check("lap: flagged invalid on the first lap isn't shown, later laps are", !DashValues.LapInvalidShown(true, 0) && DashValues.LapInvalidShown(true, 1) && DashValues.LapInvalidShown(true, 5)
              && !DashValues.LapInvalidShown(false, 3) && !DashValues.LapInvalidShown(false, 0));
    }

    static void Gauges()
    {
        var neo = WheelModel.GtNeo;
        var p = LightPresets.Find("neo-race-engineer").Clone();
        var e = new LightEngine(neo);
        var v = Car(true, 5000);
        v.Set("tcLevel", 6.0); v.Set("absLevel", 0.0); v.Set("brakeBias", 57.0); v.Set("engineMap", 10.0);
        var f = e.Render(p, v, null, 1, false, LightMoment.Of(CarState.Driving));
        bool Lit(int i) => f[i].Brightness > 20 && f[i].R + f[i].G + f[i].B > 60;
        Check("gauge: TC 6 of 1-11 fills half the upper left ring, clockwise from 12", Enumerable.Range(10, 6).All(Lit) && Enumerable.Range(16, 6).All(i => !Lit(i)));
        Check("gauge: ABS 0 (not on this car) leaves the ring dim", Enumerable.Range(22, 12).All(i => !Lit(i)));
        var brightest = Enumerable.Range(34, 12).OrderByDescending(i => f[i].R + f[i].G + f[i].B).First();
        Check("gauge: brake bias 57 of 50-64 is a pointer at 6 o'clock (dim neighbours, the rest dark)", brightest == 40
              && Enumerable.Range(34, 12).Count(i => f[i].R + f[i].G + f[i].B > 200) == 1 && f[39].R + f[39].G + f[39].B > 30 && f[34].R + f[34].G + f[34].B < 30);
        Check("gauge: map 10 of 1-10 fills the ring, ending red", Enumerable.Range(46, 12).All(Lit) && f[57].R > 200 && f[57].G < 40);
        var low = Car(true, 5000); low.Set("engineMap", 1.0); low.Set("throttle", 0.0);
        var lp = LightPresets.Find("neo-race-engineer").Clone();
        lp.Group(LedGroup.Encoders).Gauges = new List<RingGauge> { RingGauge.Of("throttle"), RingGauge.Of("absLevel"), RingGauge.Of("brakeBias"), RingGauge.Of("engineMap") };
        var lf = new LightEngine(neo).Render(lp, low, null, 1, false, LightMoment.Of(CarState.Driving));
        Check("gauge: a setting at its lowest shows one segment, a pedal at 0 none",
              Enumerable.Range(46, 12).Count(i => lf[i].R + lf[i].G + lf[i].B > 60) == 1 && Enumerable.Range(10, 12).All(i => lf[i].R + lf[i].G + lf[i].B < 60));
        // a change flashes the ring for a second
        v.Set("tcLevel", 7.0);
        var flashes = Enumerable.Range(0, 16).Select(k => { var c = e.Render(p, v, null, 1.01 + k * 0.03, false, LightMoment.Of(CarState.Driving))[10]; return c.R + c.G + c.B; }).Distinct().Count();
        Check("gauge: a change flashes the ring", flashes > 1);
        // custom value
        var enc = p.Group(LedGroup.Encoders);
        enc.Gauges = RingGauge.Defaults();
        enc.Gauges[0] = new RingGauge { Source = "DataCorePlugin.GameData.NewData.TyreWearFrontLeft", Low = 0, High = 100 };
        Check("gauge: a custom SimHub value is read in DataUpdate", p.Bindings().Contains("prop:DataCorePlugin.GameData.NewData.TyreWearFrontLeft"));
        var v2 = Car(true, 5000); v2.Set("prop:DataCorePlugin.GameData.NewData.TyreWearFrontLeft", 25.0);
        var f2 = new LightEngine(neo).Render(p, v2, null, 1, false, LightMoment.Of(CarState.Driving));
        Check("gauge: custom value 25 of 0-100 fills 3 segments", Enumerable.Range(10, 12).Count(i => f2[i].Brightness > 20 && f2[i].R + f2[i].G + f2[i].B > 60) == 3);
        var rt = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(p));
        Check("gauge: saved and loaded", rt.Group(LedGroup.Encoders).Gauges[0].IsCustom && rt.Group(LedGroup.Encoders).GaugeFor(2).Style == GaugeStyle.Pointer);
        Check("gauge: no values while parked (the look takes over)", new LightEngine(neo).Render(p, new DashValues(), null, 1, false, LightMoment.Of(CarState.Idle)).Length == 73);
    }

    static void Looks()
    {
        var fx = WheelModel.FxPro;
        var p = LightPresets.Find("aurora").Clone();
        var e = new LightEngine(fx);
        var drive = e.Render(p, Car(true, 7700), null, 1, false, LightMoment.Of(CarState.Driving));
        var legacy = new LightEngine(fx).Render(p, Car(true, 7700), null, 1, false);
        Check("looks: driving is the old render", drive.Zip(legacy, (a, b) => a.R == b.R && a.G == b.G && a.B == b.B && a.Brightness == b.Brightness).All(x => x));

        var off = e.Render(p, Car(false), null, 1, false, LightMoment.Of(CarState.EngineOff));
        Check("looks: engine off is the default 18% (dimmer than driving)", off.Take(23).Max(c => c.Brightness) < drive.Take(23).Max(c => c.Brightness) / 3);
        Check("looks: engine off leaves the rev bar dark", off.Skip(23).All(c => c.R + c.G + c.B == 0));

        var idle = e.Render(p, new DashValues(), null, 1, false, LightMoment.Of(CarState.Idle));
        Check("looks: aurora's idle look brings the rev bar in", idle.Skip(23).Any(c => c.R + c.G + c.B > 0));

        var stealth = LightPresets.Find("stealth");
        var dark = e.Render(stealth, Car(false), null, 1, false, LightMoment.Of(CarState.EngineOff));
        Check("looks: Stealth goes dark with the engine off", dark.All(c => c.R + c.G + c.B == 0));

        // Heartbeat: resting pulse while idle is slower than while driving
        var hb = LightPresets.Find("heartbeat");
        Check("looks: Heartbeat rests at 40 bpm, races at 80", hb.LookFor(CarState.Idle).Period == 1.5 && hb.Group(LedGroup.Buttons).Period == 0.75);

        // rev tint: unlit shift lights glow faintly in the theme colour; lit ones keep the standard colours
        var tok = LightPresets.Find("neon-tokyo");
        var low = e.Render(tok, Car(true, 3000), null, 1, false, LightMoment.Of(CarState.Driving));
        var high = e.Render(tok, Car(true, 7500), null, 1, false, LightMoment.Of(CarState.Driving));
        var green = LightEngine.Rgb("#00FF40");
        Check("looks: rev tint on unlit shift lights, faint", low.Skip(23).All(c => c.R + c.G + c.B > 0 && c.Brightness <= 10));
        Check("looks: lit shift lights keep the standard colours", high[23].R == green.Item1 && high[23].G == green.Item2 && high[23].B == green.Item3);

        // driving: held still, side lights dark (alerts still use them)
        bool Same(LedColor a, LedColor b) => a.R == b.R && a.G == b.G && a.B == b.B && a.Brightness == b.Brightness;
        bool Dark(LedColor c) => c.R + c.G + c.B == 0;
        foreach (var id in new[] { "neon-tokyo", "hyperspace", "inferno", "abyss", "heartbeat", "aurora", "scanner", "rainbow", "le-mans-night" })
        {
            var calm = LightPresets.Find(id).Clone();
            var d1 = e.Render(calm, Car(true, 3000), null, 1.0, false, LightMoment.Of(CarState.Driving));
            var d2 = e.Render(calm, Car(true, 3000), null, 2.7, false, LightMoment.Of(CarState.Driving));
            Check($"driving: {id} holds still (buttons and encoders)", Enumerable.Range(0, 17).All(i => Same(d1[i], d2[i])));
            Check($"driving: {id} keeps its colours (not dark)", Enumerable.Range(0, 12).Any(i => !Dark(d1[i])));
            Check($"driving: {id} side lights are dark", Enumerable.Range(17, 6).All(i => Dark(d1[i]) && Dark(d2[i])));
            var limiter = e.Render(calm, Car(true, 3000, limiter: true), null, 1.0, false, LightMoment.Of(CarState.PitLimiter));
            var limiter2 = e.Render(calm, Car(true, 3000, limiter: true), null, 2.7, false, LightMoment.Of(CarState.PitLimiter));
            Check($"driving: {id} also holds still on the pit limiter", Enumerable.Range(0, 17).All(i => Same(limiter[i], limiter2[i])));
        }
        var anim = LightPresets.Find("neon-tokyo").Clone();
        anim.StillWhileDriving = false; anim.SidesDarkWhileDriving = false;
        var a1 = e.Render(anim, Car(true, 3000), null, 1.0, false, LightMoment.Of(CarState.Driving));
        var a2 = e.Render(anim, Car(true, 3000), null, 2.7, false, LightMoment.Of(CarState.Driving));
        Check("driving: with the options off the buttons animate and the side lights glow",
              Enumerable.Range(0, 12).Any(i => !Same(a1[i], a2[i])) && Enumerable.Range(17, 6).Any(i => !Dark(a1[i]) || !Dark(a2[i])));
        var spot = LightPresets.Find("neon-tokyo").Clone();
        var withSpotter = Car(true, 3000); withSpotter.SpotterLeft = true;
        var sp = e.Render(spot, withSpotter, null, 1.0, false, LightMoment.Of(CarState.Driving));
        Check("driving: a car on the left still lights the left side lights, not the right", Enumerable.Range(17, 3).All(i => !Dark(sp[i])) && Enumerable.Range(20, 3).All(i => Dark(sp[i])));
        var parked = e.Render(LightPresets.Find("neon-tokyo").Clone(), new DashValues(), null, 1.0, false, LightMoment.Of(CarState.Idle));
        var parked2 = e.Render(LightPresets.Find("neon-tokyo").Clone(), new DashValues(), null, 2.7, false, LightMoment.Of(CarState.Idle));
        Check("driving: parked looks still animate", Enumerable.Range(0, 38).Any(i => !Same(parked[i], parked2[i])));
        var oldSaved = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>("{\"Name\":\"old\"}");
        Check("driving: a profile saved before has both on", oldSaved.StillWhileDriving && oldSaved.SidesDarkWhileDriving);
        var roundTrip = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(anim));
        Check("driving: the options are saved", !roundTrip.StillWhileDriving && !roundTrip.SidesDarkWhileDriving);
        // every built-in preset on both wheels, in every state where you're in the car: still, sides dark
        foreach (var model in WheelModel.All)
        {
            var eng = new LightEngine(model);
            foreach (var preset in LightPresets.For(model))
            {
                var q = preset.Clone();
                foreach (var a in q.Alerts) a.Enabled = false;   // the theme on its own: alerts flash by design
                foreach (var st in new[] { CarState.Driving, CarState.PitLimiter })
                {
                    var times = new[] { 0.0, 0.31, 1.7, 4.2, 9.9, 23.5 };
                    var frames = times.Select(t => eng.Render(q, Car(true, 3000, limiter: st == CarState.PitLimiter), null, t, false, LightMoment.Of(st))).ToList();
                    var nonRev = Enumerable.Range(0, model.LedCount).Where(i => model.GroupOf(i) != LedGroup.Rev).ToList();
                    if (st == CarState.PitLimiter) nonRev = nonRev.Where(i => model.GroupOf(i) != LedGroup.Rev).ToList();
                    bool still = frames.Skip(1).All(f => nonRev.All(i => Same(f[i], frames[0][i])));
                    Check($"still: {model.Name} {preset.Name} holds still while {st}", still,
                          still ? "" : "LEDs " + string.Join(",", nonRev.Where(i => frames.Any(f => !Same(f[i], frames[0][i]))).Take(8)));
                    if (model.Has(LedGroup.SideLeft))
                        Check($"still: {model.Name} {preset.Name} side lights dark while {st}", frames.All(f => model.Leds(LedGroup.SideLeft).Concat(model.Leds(LedGroup.SideRight)).All(i => Dark(f[i]))));
                    Check($"still: {model.Name} {preset.Name} still has colour while {st}", nonRev.Any(i => !Dark(frames[0][i])) || preset.Id.Contains("stealth"));
                }
            }
        }
        var gt = LightPresets.Find("neo-neon-tokyo").Clone();
        var g1 = new LightEngine(WheelModel.GtNeo).Render(gt, Car(true, 3000), null, 1.0, false, LightMoment.Of(CarState.Driving));
        var g2 = new LightEngine(WheelModel.GtNeo).Render(gt, Car(true, 3000), null, 2.7, false, LightMoment.Of(CarState.Driving));
        Check("driving: the GT Neo's rings and buttons hold still too", Enumerable.Range(0, 58).All(i => Same(g1[i], g2[i])));
    }

    static void Limiter()
    {
        var fx = WheelModel.FxPro;
        var p = LightPresets.Find("aurora").Clone();
        p.Limiter = new LimiterLook { Style = LimiterStyle.Alternate, Colors = new List<string> { "#0040FF", "#000000" }, Hz = 2 };
        var e = new LightEngine(fx);
        var v = Car(true, 2500, limiter: true);
        var f1 = e.Render(p, v, null, 0.01, false, LightMoment.Of(CarState.PitLimiter));
        var f2 = e.Render(p, v, null, 0.26, false, LightMoment.Of(CarState.PitLimiter));
        bool leftFirst = f1[23].B > 0 && f1[37].B == 0, swapped = f2[23].B == 0 && f2[37].B > 0;
        Check("limiter: halves take turns on the rev bar", (leftFirst && swapped) || (!leftFirst && f1[23].B == 0 && f1[37].B > 0 && f2[23].B > 0));
        Check("limiter: the other lights keep the theme", f1[0].R + f1[0].G + f1[0].B > 0);

        // the old pit limiter alert gives way to the limiter lights
        var withAlert = LightPresets.Find("aurora").Clone();
        withAlert.Alerts.First(a => a.Trigger == AlertTrigger.PitLimiter).Enabled = true;
        withAlert.Limiter = new LimiterLook { Style = LimiterStyle.Solid, Colors = new List<string> { "#FF00FF" } };
        var fa = e.Render(withAlert, v, null, 0.3, false, LightMoment.Of(CarState.PitLimiter));
        Check("limiter: the pit limiter alert doesn't cover the limiter lights", fa.Skip(23).All(c => c.R == 255 && c.B == 255 && c.G == 0));

        // a car's own limiter lights win over the preset's
        var car = new LimiterLook { Style = LimiterStyle.Solid, Colors = new List<string> { "#00FF00" } };
        var fc = e.Render(withAlert, v, null, 0.3, false, LightMoment.Of(CarState.PitLimiter, 0, car));
        Check("limiter: a car's own lights come first", fc.Skip(23).All(c => c.G == 255 && c.R == 0));

        // whole wheel
        car.WholeWheel = true;
        var fw = e.Render(withAlert, v, null, 0.3, false, LightMoment.Of(CarState.PitLimiter, 0, car));
        Check("limiter: whole wheel", fw.All(c => c.G == 255));

        // every style lights something at some point, and None lights nothing extra
        foreach (LimiterStyle st in Enum.GetValues(typeof(LimiterStyle)))
        {
            var l = new LimiterLook { Style = st, Hz = 2 };
            bool any = Enumerable.Range(0, 40).Any(k => Enumerable.Range(0, 15).Any(i => l.At(i, 15, k * 0.037).Level > 0));
            Check($"limiter style {st}: {(st == LimiterStyle.None ? "never lights" : "lights up")}", st == LimiterStyle.None ? !any : any);
        }

        // the GT Neo's rev bar is 58-72
        var neo = new LightEngine(WheelModel.GtNeo).Render(LightPresets.Find("neo-aurora"), v, null, 0.3, false, LightMoment.Of(CarState.PitLimiter, 0, new LimiterLook { Style = LimiterStyle.Solid, Colors = new List<string> { "#00FF00" } }));
        Check("limiter: on the GT Neo's rev bar", Enumerable.Range(58, 15).All(i => neo[i].G == 255) && neo[10].G != 255 || neo[10].R + neo[10].B > 0);
    }

    static void Takeovers()
    {
        var e = new LightEngine(WheelModel.FxPro);
        var p = LightPresets.Find("aurora").Clone();
        var v = Car(true, 900);
        foreach (StartupStyle st in Enum.GetValues(typeof(StartupStyle)))
        {
            if (st == StartupStyle.None) continue;
            p.Startup = st;
            var start = e.Render(p, v, null, 1, false, LightMoment.Of(CarState.Starting, 0.02));
            var end = e.Render(p, v, null, 1, false, LightMoment.Of(CarState.Starting, 1));
            var drive = e.Render(p, v, null, 1, false, LightMoment.Of(CarState.Driving));
            bool endsOnTheme = end.Zip(drive, (a, b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 3).Count(x => x) >= 34;
            Check($"start-up {st}: starts differently, ends on the theme", !start.Zip(drive, (a, b) => a.R == b.R && a.G == b.G && a.B == b.B).All(x => x) && endsOnTheme);
        }
        p.Shutdown = ShutdownStyle.Fade;
        var gone = e.Render(p, Car(false), null, 1, false, LightMoment.Of(CarState.Stopping, 1));
        Check("shutdown Fade: out at the end", gone.Take(23).All(c => c.R + c.G + c.B == 0));
        p.Shutdown = ShutdownStyle.Collapse;
        var half = e.Render(p, Car(false), null, 1, false, LightMoment.Of(CarState.Stopping, 0.6));
        Check("shutdown Collapse: the ends go first", half[23].R + half[23].G + half[23].B == 0);

        // flags stay on top of a start-up
        p.Startup = StartupStyle.Ignite;
        var flag = Car(true, 900); flag.BlueFlag = true;
        var f = e.Render(p, flag, null, 1, false, LightMoment.Of(CarState.Starting, 0.05));
        var blue = LightEngine.Rgb(p.Alerts.First(a => a.Trigger == AlertTrigger.BlueFlag).Color);
        Check("start-up: a flag still shows over it", Enumerable.Range(12, 5).Any(i => f[i].B == blue.Item3 && f[i].R == blue.Item1));
    }

    static void Presets()
    {
        string[] fresh = { "neon-tokyo", "hyperspace", "le-mans-night", "inferno", "abyss", "heartbeat" };
        Check("six new presets on the FX Pro, first in the list", LightPresets.FxPro.Take(6).Select(p => p.Id).SequenceEqual(fresh));
        Check("Race Engineer on both wheels, right after them", LightPresets.FxPro[6].Id == "race-engineer" && LightPresets.GtNeo[6].Id == "neo-race-engineer");
        Check("six new presets on the GT Neo, first in the list", LightPresets.GtNeo.Take(6).Select(p => p.Id).SequenceEqual(fresh.Select(x => "neo-" + x)));
        Check("Prism is gone from both wheels", LightPresets.All.All(p => p.Name != "Prism"));
        Check("Prism ids lead to Full Rainbow", LightPresets.Find("mustang")?.Id == "rainbow" && LightPresets.Find("neo-prism")?.Id == "neo-rainbow");
        var u = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>("{\"LightPreset\":\"mustang\",\"CarLights\":{\"ACC | x\":\"mustang\"},\"Wheels\":{\"gtneo\":{\"LightPreset\":\"neo-prism\"}}}");
        u.UpdateRenamedPresets();
        Check("saved Prism choices move to Full Rainbow (global, per car, other wheel)", u.LightPreset == "rainbow" && u.CarLights["ACC | x"] == "rainbow" && u.Wheels["gtneo"].LightPreset == "neo-rainbow");
        Check("a new user starts on Neon Tokyo", new UsbSettings().LightPreset == "neon-tokyo" && new UsbSettings().ActiveLights.Id == "neon-tokyo");

        // every preset renders in every state on both wheels, and parked looks are the preset's
        foreach (var m in WheelModel.All)
            foreach (var p in LightPresets.For(m))
            {
                var e = new LightEngine(m);
                bool ok = true;
                foreach (CarState s in Enum.GetValues(typeof(CarState)))
                {
                    var f = e.Render(p, s == CarState.Idle ? new DashValues() : Car(s != CarState.EngineOff && s != CarState.Menu, 5000, s == CarState.PitLimiter, s == CarState.Menu), null, 2.3, false, LightMoment.Of(s, 0.5));
                    ok &= f.Length == m.LedCount;
                }
                Check($"{m.Name} {p.Name}: renders in every state", ok);
            }
        var rt = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(LightPresets.Find("inferno")));
        Check("a profile's looks, limiter and motion survive a save and load", rt.LookFor(CarState.Idle).Effect == LightEffect.Fire && rt.Limiter.Style == LimiterStyle.Blink
              && rt.Startup == StartupStyle.Ignite && rt.RevTint == 6);
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>("{\"Id\":\"user-x\",\"Name\":\"Mine\"}");
        Check("a profile saved before states gets the defaults", old.LookFor(CarState.Idle).Brightness == 100 && old.Limiter.Style == LimiterStyle.Alternate && old.Startup == StartupStyle.Sweep);
    }

    static LedColor[] Rev(LedColor[] f) => f.Skip(23).Take(15).ToArray();
    static bool IsDark(LedColor c) => c.R + c.G + c.B == 0;

    static void Extras()
    {
        const byte B = 90;

        // ---- the pictures
        var ptr = RevBars.Pointer(15, 0.5, RevBars.Green, B);
        Check("extras: a pointer in the middle lights the middle LED fully and its neighbours softly", ptr[7].G == 255 && ptr[6].G > 0 && ptr[6].G < 255 && ptr[5].G == 0 && ptr[8].G > 0 && ptr[9].G == 0);
        var left = RevBars.Pointer(15, 0, RevBars.Green, B);
        Check("extras: a pointer at 0 sits on the first LED, at 1 on the last", left[0].G == 255 && RevBars.Pointer(15, 1, RevBars.Green, B)[14].G == 255 && left[14].G == 0);
        Check("extras: the middle shows a dim white tick when the pointer is elsewhere", IsDark(left[7]) == false && left[7].R == left[7].G && left[7].G == left[7].B && left[7].R < 60);
        var fill = RevBars.Pointer(15, 0.9, RevBars.Red, B, RevBars.Red);
        Check("extras: a pointer with a fill lights from the middle to the pointer", Enumerable.Range(7, 6).All(i => fill[i].R > 0) && Enumerable.Range(0, 6).All(i => IsDark(fill[i])));
        Check("extras: position is 0.5 on target and clamps at the ends", RevBars.Position(100, 100, 10) == 0.5 && RevBars.Position(0, 100, 10) == 0 && RevBars.Position(500, 100, 10) == 1);
        var half = RevBars.Fill(15, 0.5, RevBars.Cyan, B);
        Check("extras: a half fill lights the left half (the middle partly)", Enumerable.Range(0, 7).All(i => half[i].B == 255) && half[7].B > 0 && half[7].B < 255 && Enumerable.Range(8, 7).All(i => IsDark(half[i])));
        Check("extras: an empty fill is dark and a full one lit", RevBars.Fill(15, 0, RevBars.Cyan, B).All(IsDark) && RevBars.Fill(15, 1, RevBars.Cyan, B).All(c => c.B == 255));
        var mir = RevBars.MirroredFill(15, 0.4, RevBars.Magenta, B);
        Check("extras: a mirrored fill closes in from both ends, evenly", mir[0].R == 255 && mir[14].R == 255 && mir[0].R == mir[14].R && IsDark(mir[7]) && Enumerable.Range(0, 15).All(i => mir[i].R == mir[14 - i].R));
        Check("extras: a full mirrored fill lights everything", RevBars.MirroredFill(15, 1, RevBars.Magenta, B).All(c => c.R == 255));
        var under = RevBars.PitSpeed(15, 40, 60, B); var at = RevBars.PitSpeed(15, 60, 60, B); var over = RevBars.PitSpeed(15, 72, 60, B);
        Check("extras: pit speed below the limit is a cyan pointer left of the middle", under.Select((c, i) => (c, i)).OrderByDescending(x => x.c.B).First().i < 7 && under.Where((c, i) => i != 7).All(c => c.R == 0));
        Check("extras: pit speed at the limit is green in the middle", at[7].G == 255 && at.All(c => c.R == 0) && at[6].G > 0 && at[8].G > 0);
        Check("extras: pit speed over the limit is red, filled from the middle to the pointer", over[13].R > 200 && Enumerable.Range(7, 7).All(i => over[i].R > 0) && Enumerable.Range(0, 7).All(i => IsDark(over[i])) && over[14].R == 0 && over.All(c => c.G == 0));
        var l = new LaunchTarget { Rpm = 5000 };
        var (tgt, rng) = l.Window(8000);
        Check("extras: launch target and window (revs: the saved rpm, a range from the car's max)", tgt == 5000 && rng == 500 && new LaunchTarget().Window(8000).Target == 5600 && new LaunchTarget { Mode = LaunchMode.Throttle }.Window(8000) == (80.0, 25.0));
        Check("extras: launch pointer is amber below, green on, red above the target",
              RevBars.Launch(15, 4000, 5000, 500, B)[0].R == 255 && RevBars.Launch(15, 4000, 5000, 500, B)[0].G == 176 && RevBars.Launch(15, 4000, 5000, 500, B).All(c => c.B == 0 || c.R == c.B)
              && RevBars.Launch(15, 5020, 5000, 500, B)[7].G > 240 && RevBars.Launch(15, 5020, 5000, 500, B).All(c => c.R == 0)
              && RevBars.Launch(15, 6000, 5000, 500, B)[14].R == 255 && RevBars.Launch(15, 6000, 5000, 500, B)[14].G == 0);

        // ---- the history behind them
        var learned = new Dictionary<string, double>();
        int saves = 0;
        var st = new RevExtrasState(() => learned);
        st.PitSpeedLearned += () => saves++;
        DashValues V(double speed = 0, bool limiter = false, bool pit = false, double fuel = 50, double bias = 55) { var d = Car(true, 3000, limiter: limiter); d.SpeedKmh = speed; d.InPitLane = pit; d.Game = "AMS2"; d.Track = "Nurburgring"; d.Set("fuel", fuel); d.Set("brakeBias", bias); return d; }
        var x0 = st.Update(V(), 0, "car", null);
        Check("extras state: nothing at first", x0.PitSpeedKmh == 0 && !x0.Refuelling && !x0.BiasShown && x0.BiasOffset == 0 && x0.Launch != null);
        var x1 = st.Update(V(bias: 56.0), 1.0, "car", null);
        Check("extras state: a bias change shows, as the offset from where it started", x1.BiasShown && Math.Abs(x1.BiasOffset - 1.0) < 1e-9);
        Check("extras state: ...for 2.5 s, then it's gone (the offset stays)", st.Update(V(bias: 56.0), 3.0, "car", null).BiasShown && !st.Update(V(bias: 56.0), 3.6, "car", null).BiasShown && Math.Abs(st.Update(V(bias: 56.0), 4, "car", null).BiasOffset - 1.0) < 1e-9);
        Check("extras state: a tiny wobble isn't a change", !st.Update(V(bias: 56.02), 10, "car", null).BiasShown);
        var again = st.Update(V(bias: 52), 11, "other car", null);
        Check("extras state: a new car starts its own reference (no change shown, offset 0)", !again.BiasShown && again.BiasOffset == 0);
        st.Update(V(fuel: 40), 20, "car", null);
        Check("extras state: fuel rising while stopped is refuelling", st.Update(V(fuel: 41), 20.1, "car", null).Refuelling);
        Check("extras state: ...held for 1.5 s after the last rise, then not", st.Update(V(fuel: 41), 21.4, "car", null).Refuelling && !st.Update(V(fuel: 41), 21.7, "car", null).Refuelling);
        Check("extras state: fuel falling or rising while moving isn't", !st.Update(V(50, fuel: 39), 30, "car", null).Refuelling && !st.Update(V(50, fuel: 45), 30.1, "car", null).Refuelling);
        var game = new DashValues { Running = true, PitSpeedLimit = 80, Game = "iRacing", Track = "x" };
        Check("extras state: a pit speed from the game is used", st.Update(game, 40, "car", null).PitSpeedKmh == 80);
        // learning: the limiter holding 59.8 km/h in the pit lane for a second
        double t = 100;
        RevExtrasInput last = null;
        for (int i = 0; i < 15; i++) { t += 0.05; last = st.Update(V(59.8 + (i % 2) * 0.1, limiter: true, pit: true), t, "car", null); }
        Check("extras state: before a second of steady speed nothing is learned", saves == 0 && learned.Count == 0 && last.PitSpeedKmh == 0);
        for (int i = 0; i < 25; i++) { t += 0.05; last = st.Update(V(59.8 + (i % 2) * 0.1, limiter: true, pit: true), t, "car", null); }
        double got = learned.TryGetValue("AMS2 | Nurburgring", out var gv0) ? gv0 : 0;
        Check("extras state: the speed the limiter holds is learned for the track and saved", saves == 1 && Math.Abs(got - 59.85) < 0.1 && last.PitSpeedKmh == got, string.Join(";", learned.Select(k => k.Key + "=" + k.Value)));
        Check("extras state: ...and known from then on, without the limiter", st.Update(V(40, pit: true), t + 5, "car", null).PitSpeedKmh == got);
        var hunt = new Dictionary<string, double>(); var st2 = new RevExtrasState(() => hunt); double t2 = 0;
        for (int i = 0; i < 80; i++) { t2 += 0.05; st2.Update(V(30 + i * 0.7, limiter: true, pit: true), t2, "c", null); }
        Check("extras state: a speed still changing isn't learned", hunt.Count == 0);
        for (int i = 0; i < 60; i++) { t2 += 0.05; st2.Update(V(60, limiter: true, pit: false), t2, "c", null); }
        Check("extras state: nothing is learned outside the pit lane", hunt.Count == 0);
        var gameLimit = new Dictionary<string, double>(); var st3 = new RevExtrasState(() => gameLimit); double t3 = 0;
        for (int i = 0; i < 60; i++) { t3 += 0.05; var gv = V(70, limiter: true, pit: true); gv.PitSpeedLimit = 72; st3.Update(gv, t3, "c", null); }
        Check("extras state: nothing is learned when the game gives the limit", gameLimit.Count == 0);

        Check("extras: pit speeds are read from the game's text", DashValues.ParsePitSpeed("60.00 kph") == 60 && Math.Abs(DashValues.ParsePitSpeed("45.00 mph") - 72.42) < 0.01
              && DashValues.ParsePitSpeed("80") == 80 && DashValues.ParsePitSpeed("fast") == 0 && DashValues.ParsePitSpeed(null) == 0 && DashValues.ParsePitSpeed("") == 0);

        // ---- in the engine
        var fx = WheelModel.FxPro; var e = new LightEngine(fx);
        LightProfile Prof() { var q = LightPresets.Find("stealth").Clone(); return q; }
        LedColor[] Draw(LightProfile q, DashValues d, RevExtrasInput x, CarState s = CarState.Driving, double now = 1) => e.Render(q, d, null, now, false, new LightMoment { State = s, Extras = x });
        var shiftOnly = Rev(e.Render(Prof(), Car(true, 3000), null, 1, false, LightMoment.Of(CarState.Driving)));

        var pitV = Car(true, 3000, limiter: true); pitV.InPitLane = true; pitV.SpeedKmh = 72;
        var pitX = new RevExtrasInput { PitSpeedKmh = 60 };
        var pf = Rev(Draw(Prof(), pitV, pitX, CarState.PitLimiter));
        Check("engine: in the pit lane over the limit the rev bar is the red pit speed bar", pf[13].R > 200 && pf.All(c => c.G == 0));
        pitV.SpeedKmh = 40;
        var slow = Rev(Draw(Prof(), pitV, pitX, CarState.PitLimiter));
        Check("engine: ...under it a cyan pointer left of the middle", slow.Select((c, i) => (c, i)).OrderByDescending(a => a.c.B).First().i < 7);
        pitV.SpeedKmh = 3;
        Check("engine: ...stopped (under 5 km/h) it gives the bar back (the limiter's own lights)", !Rev(Draw(Prof(), pitV, pitX, CarState.PitLimiter)).SequenceEqual(slow));
        pitV.SpeedKmh = 72;
        var offProf = Prof(); offProf.Extras.PitSpeed = false;
        Check("engine: with the option off the pit speed bar is not drawn", !Rev(Draw(offProf, pitV, pitX, CarState.PitLimiter)).SequenceEqual(pf));
        Check("engine: with no known limit it isn't drawn either", !Rev(Draw(Prof(), pitV, new RevExtrasInput(), CarState.PitLimiter)).SequenceEqual(pf));
        pitV.InPitLane = false;
        Check("engine: outside the pit lane it isn't drawn", !Rev(Draw(Prof(), pitV, pitX, CarState.PitLimiter)).SequenceEqual(pf));
        Check("engine: not while the engine starts or stops, or parked", !Rev(Draw(Prof(), pitV, pitX, CarState.Starting)).SequenceEqual(pf));

        var lc = Car(true, 3000); lc.LiftCoast = 60;
        var lf = Rev(Draw(Prof(), lc, new RevExtrasInput()));
        Check("engine: lift and coast fills the bar from both ends in magenta", lf[0].R == 255 && lf[0].B == 255 && lf[14].R == 255 && IsDark(lf[7]) && Enumerable.Range(0, 15).All(i => lf[i].R == lf[14 - i].R));
        lc.Rpm = 7600;
        Check("engine: ...but near the shift point the shift lights win", Rev(Draw(Prof(), lc, new RevExtrasInput())).All(c => c.B == 0 || c.R != 255 || c.G != 0) && !Rev(Draw(Prof(), lc, new RevExtrasInput())).SequenceEqual(lf));
        var lcLow = Car(true, 3000); lcLow.LiftCoast = 2;
        Check("engine: a little progress (2.5% or less) isn't shown", Rev(Draw(Prof(), lcLow, new RevExtrasInput())).SequenceEqual(shiftOnly));
        var noLc = Prof(); noLc.Extras.LiftCoast = false; lc.Rpm = 3000;
        Check("engine: lift and coast off", Rev(Draw(noLc, lc, new RevExtrasInput())).SequenceEqual(shiftOnly));

        var rf = Car(true, 0); rf.FuelPercent = 50; rf.SpeedKmh = 0;
        var refuel = Rev(Draw(Prof(), rf, new RevExtrasInput { Refuelling = true }));
        Check("engine: refuelling shows the tank filling (half full: the left half lit, cyan)", Enumerable.Range(0, 7).All(i => refuel[i].B == 255) && Enumerable.Range(8, 7).All(i => IsDark(refuel[i])));
        rf.SpeedKmh = 30;
        Check("engine: ...not while moving", Rev(Draw(Prof(), rf, new RevExtrasInput { Refuelling = true })).SequenceEqual(Rev(Draw(Prof(), rf, new RevExtrasInput()))));

        var bv = Car(true, 3000);
        var bias = Rev(Draw(Prof(), bv, new RevExtrasInput { BiasShown = true, BiasOffset = 2 }));
        Check("engine: a bias change shows a pointer right of the middle for +2% (amber)", bias.Select((c, i) => (c, i)).OrderByDescending(a => a.c.R).First().i > 7 && bias.Where((c, i) => i != 7).All(c => c.B == 0));
        Check("engine: ...a negative one left of it", Rev(Draw(Prof(), bv, new RevExtrasInput { BiasShown = true, BiasOffset = -3 })).Select((c, i) => (c, i)).OrderByDescending(a => a.c.R).First().i < 7);
        Check("engine: ...not once it has been shown", Rev(Draw(Prof(), bv, new RevExtrasInput { BiasShown = false, BiasOffset = 2 })).SequenceEqual(shiftOnly));

        var lv = Car(true, 5000); lv.GearKey = "1"; lv.SpeedKmh = 0;
        var lx = new RevExtrasInput { Launch = new LaunchTarget { Rpm = 5000 } };
        var launchOff = Rev(Draw(Prof(), lv, lx));
        Check("engine: the launch aid is off unless switched on", launchOff.SequenceEqual(Rev(e.Render(Prof(), lv, null, 1, false, LightMoment.Of(CarState.Driving)))));
        var lp = Prof(); lp.Extras.Launch = true;
        var onTarget = Rev(Draw(lp, lv, lx));
        Check("engine: on the target revs the middle is green", onTarget[7].G == 255 && onTarget.All(c => c.R == 0));
        lv.Rpm = 4200;
        Check("engine: under the target the pointer is amber and left of the middle", Rev(Draw(lp, lv, lx)).Select((c, i) => (c, i)).OrderByDescending(a => a.c.R).First().i < 7);
        lv.Rpm = 5000; lv.SpeedKmh = 40;
        Check("engine: not once you're moving (40 km/h)", Rev(Draw(lp, lv, lx)).SequenceEqual(Rev(e.Render(lp, lv, null, 1, false, LightMoment.Of(CarState.Driving)))));
        lv.SpeedKmh = 0; lv.GearKey = "2";
        Check("engine: not in second gear", Rev(Draw(lp, lv, lx)).SequenceEqual(Rev(e.Render(lp, lv, null, 1, false, LightMoment.Of(CarState.Driving)))));
        lv.GearKey = "1"; lv.InPitLane = true;
        Check("engine: not in the pit lane", Rev(Draw(lp, lv, lx)).SequenceEqual(Rev(e.Render(lp, lv, null, 1, false, LightMoment.Of(CarState.Driving)))));
        lv.InPitLane = false;
        var throttleX = new RevExtrasInput { Launch = new LaunchTarget { Mode = LaunchMode.Throttle, ThrottlePercent = 80 } };
        lv.Set("throttle", 80.0);
        Check("engine: throttle mode watches the throttle", Rev(Draw(lp, lv, throttleX))[7].G == 255);
        lv.Set("throttle", 20.0);
        Check("engine: ...amber below the target", Rev(Draw(lp, lv, throttleX)).Max(c => c.R) == 255 && Rev(Draw(lp, lv, throttleX)).Where((c, i) => i != 7).All(c => c.B == 0));

        var reverse = Car(true, 3000); reverse.LiftCoast = 30;
        var normalFrame = e.Render(Prof(), reverse, null, 1, false, new LightMoment { State = CarState.Driving, Extras = new RevExtrasInput() });
        var reversedFrame = e.Render(Prof(), reverse, null, 1, true, new LightMoment { State = CarState.Driving, Extras = new RevExtrasInput() });
        Check("engine: a mirrored bar is the same on a reversed rev bar", Rev(normalFrame).SequenceEqual(Rev(reversedFrame)));
        var bRev = Car(true, 3000);
        Check("engine: the extras follow a reversed rev bar (bias +2 points the other way)",
              Rev(e.Render(Prof(), bRev, null, 1, true, new LightMoment { State = CarState.Driving, Extras = new RevExtrasInput { BiasShown = true, BiasOffset = 2 } })).Select((c, i) => (c, i)).OrderByDescending(a => a.c.R).First().i < 7);

        // ---- settings and presets
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>(@"{""Name"":""old""}");
        Check("extras: a profile saved before has pit speed, lift and coast, refuel and bias on, launch off", old.Extras.PitSpeed && old.Extras.LiftCoast && old.Extras.Refuel && old.Extras.BrakeBias && !old.Extras.Launch);
        var prof = Prof(); prof.Extras.Launch = true; prof.Extras.PitSpeed = false;
        var round = Newtonsoft.Json.JsonConvert.DeserializeObject<LightProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(prof));
        Check("extras: the options are saved", round.Extras.Launch && !round.Extras.PitSpeed && prof.Clone().Extras != prof.Extras && prof.Clone().Extras.Launch);
        Check("extras: every built-in preset has them", LightPresets.All.All(q => q.Extras != null && q.Extras.PitSpeed));
        var u = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>(@"{""CarLaunch"":{""AMS2 | x"":{""Mode"":""Throttle"",""ThrottlePercent"":70}},""PitSpeeds"":{""AMS2 | t"":59.9}}");
        Check("extras: launch targets and pit speeds are saved", u.CarLaunch["AMS2 | x"].Mode == LaunchMode.Throttle && u.CarLaunch["AMS2 | x"].ThrottlePercent == 70 && u.PitSpeeds["AMS2 | t"] == 59.9 && new UsbSettings().CarLaunch.Count == 0);
        var plug = NewPlugin();
        Check("extras: no launch target by default", plug.LaunchFor("AMS2 | x") == null && plug.LaunchFor(null) == null);
        var tgtSave = new LaunchTarget { Rpm = 5200 };
        plug.SetCarLaunch("AMS2 | x", tgtSave); tgtSave.Rpm = 1;
        bool savedAsCopy = plug.LaunchFor("AMS2 | x")?.Rpm == 5200;
        plug.SetCarLaunch("AMS2 | x", null);
        Check("extras: a launch target is saved as a copy, and removed", savedAsCopy && plug.LaunchFor("AMS2 | x") == null);

        // ---- brake bias moved by the car (migration) isn't a change by hand; pit speeds are stored as a copy
        var mig = new RevExtrasState(() => new Dictionary<string, double>());
        DashValues Brake(double bias, double brake) { var d = V(bias: bias); d.Set("brake", brake); return d; }
        mig.Update(Brake(55, 0), 0, "c", null);
        Check("extras state: bias moving while braking (brake migration) isn't shown", !mig.Update(Brake(56, 60), 1, "c", null).BiasShown && !mig.Update(Brake(57, 70), 1.2, "c", null).BiasShown);
        Check("extras state: ...but a change by hand off the brakes is", mig.Update(Brake(58, 0), 2, "c", null).BiasShown);
        var original = new Dictionary<string, double> { ["AMS2 | Old"] = 50 };
        Dictionary<string, double> stored = null;
        var cow = new RevExtrasState(() => stored ?? original, d => stored = d);
        double tc = 0;
        for (int i = 0; i < 40; i++) { tc += 0.05; cow.Update(V(60, limiter: true, pit: true), tc, "c", null); }
        Check("extras state: a learned pit speed is stored as a copy, the old dictionary untouched", stored != null && stored["AMS2 | Nurburgring"] == 60 && stored["AMS2 | Old"] == 50 && original.Count == 1);

        // ---- the game's own shift light numbers (iRacing), for cars Lovely doesn't have
        var anchors = new ShiftAnchors { First = 6000, Shift = 6500, Last = 7000, Blink = 7500 };
        var ar = new RevLighting().ForAnchors(anchors);
        Check("anchors: the first LED lights at the game's first light and the last at its last", ar.Rpm[0] == 6000 && ar.Rpm[14] == 7000);
        Check("anchors: the LEDs in between rise evenly", Enumerable.Range(1, 14).All(i => ar.Rpm[i] >= ar.Rpm[i - 1]) && ar.Rpm[7] > 6400 && ar.Rpm[7] < 6600);
        Check("anchors: the flash is at the game's blink rpm", ar.FlashRpm == 7500 && !ar.FlashDark);
        Check("anchors: a blink at or below the last light flashes at the last light", new RevLighting().ForAnchors(new ShiftAnchors { First = 6000, Last = 7000, Blink = 6800 }).FlashRpm == 7000
              && new RevLighting().ForAnchors(new ShiftAnchors { First = 6000, Last = 7000 }).FlashRpm == 7000);
        var sideToCentre = new RevLighting { Pattern = PatternKind.EdgesToCenter }.ForAnchors(anchors);
        Check("anchors: a side-to-centre pattern is mirrored, first light at both ends, last in the middle", sideToCentre.Rpm[0] == 6000 && sideToCentre.Rpm[14] == 6000 && sideToCentre.Rpm[7] == 7000 && sideToCentre.Rpm.SequenceEqual(sideToCentre.Rpm.Reverse()),
              string.Join(",", sideToCentre.Rpm));
        Check("anchors: valid only with a first light below a last one", anchors.Valid && !new ShiftAnchors { First = 6000, Last = 6000 }.Valid && !new ShiftAnchors().Valid && !new ShiftAnchors { First = 0, Last = 7000 }.Valid);
        Check("anchors: a change in them counts as a new car state, a wobble doesn't", !anchors.SameAs(new ShiftAnchors { First = 6000, Shift = 6500, Last = 7100, Blink = 7500 }) && anchors.SameAs(new ShiftAnchors { First = 6000.4, Shift = 6500, Last = 7000.2, Blink = 7500 }) && !anchors.SameAs(default(ShiftAnchors)));
        var style = new FallbackStyle { Pattern = PatternKind.LeftToRight };
        var sp = RpmLightsMapper.FromStyleAnchors(style, null, anchors);
        Check("anchors: SimPro's fallback styles use them too", sp.Rpm[0] == 6000 && sp.Rpm[14] == 7000);

        // ---- indicators, and the flash that blinks the bar out
        var ind = LightPresets.Find("synthwave").Clone();
        var iv = Car(true, 3000); iv.IndicatorLeft = true;
        Check("indicator alerts: off by default", ind.Alerts.Where(a => a.Trigger == AlertTrigger.IndicatorLeft || a.Trigger == AlertTrigger.IndicatorRight).All(a => !a.Enabled) && ind.Alerts.Any(a => a.Trigger == AlertTrigger.IndicatorRight));
        var side = Enumerable.Range(17, 3).Select(i => e.Render(ind, iv, null, 1, false, LightMoment.Of(CarState.Driving))[i]).ToArray();
        Check("indicator alerts: nothing until switched on", side.All(IsDark));
        foreach (var a in ind.Alerts.Where(a => a.Trigger == AlertTrigger.IndicatorLeft)) a.Enabled = true;
        var lit = e.Render(ind, iv, null, 1, false, LightMoment.Of(CarState.Driving));
        Check("indicator alerts: the left indicator lights the left side lights (amber), the right ones stay dark", Enumerable.Range(17, 3).All(i => lit[i].R > 200 && lit[i].G > 100 && lit[i].B == 0) && Enumerable.Range(20, 3).All(i => IsDark(lit[i])));

        CarLedProfile Flashy(string redColour, int blink, int redline, params int[] leds) => new CarLedProfile
        {
            LedNumber = leds.Length, RedlineBlinkIntervalMs = blink,
            Colors = new[] { redColour }.Concat(Enumerable.Repeat("#FF00FF00", leds.Length)).ToArray(),
            GearRpm = new Dictionary<string, int[]> { ["1"] = new[] { redline }.Concat(leds).ToArray() },
        };
        var dark = RpmLayout.FromProfile(Flashy("#00000000", 120, 6700, 6000, 6100, 6200, 6300, 6400), false, exactColours: true);
        Check("flash: no redline colour but a blink interval blinks the lights out", dark.FlashDark && dark.FlashRpm == 6700 && dark.FlashBlinkUnits > 0);
        Check("flash: no redline colour and no interval is no flash", RpmLayout.FromProfile(Flashy("#00000000", 0, 6700, 6000, 6100), false, exactColours: true).FlashRpm == 0);
        var coloured = RpmLayout.FromProfile(Flashy("#FFFF0000", 120, 6700, 6000, 6100), false, exactColours: true);
        Check("flash: a redline colour is a colour flash as before", !coloured.FlashDark && coloured.FlashRpm == 6700 && coloured.FlashColor == "#FF0000");
        Check("flash: it can't come before the last light", RpmLayout.FromProfile(Flashy("#FFFF0000", 0, 5050, 6000, 9778), false, exactColours: true).FlashRpm == 9778);
        var layoutDark = dark;
        var rpmHigh = Car(true, 6800); rpmHigh.GearKey = "1";
        int litFrames = 0, darkFrames = 0;
        for (double now = 0; now < 1; now += 0.01)
        {
            var f = Rev(new LightEngine(fx).Render(LightPresets.Find("stealth").Clone(), rpmHigh, layoutDark, now, false, LightMoment.Of(CarState.Driving)));
            if (f.All(IsDark)) darkFrames++; else if (f.All(c => c.G == 255)) litFrames++;
        }
        Check("flash: over the redline the lights are all lit and all dark by turns, about half and half", litFrames > 30 && darkFrames > 30 && litFrames + darkFrames == 100, $"lit {litFrames} dark {darkFrames}");
        Check("flash: below the redline they're lit as the revs say", Rev(new LightEngine(fx).Render(LightPresets.Find("stealth").Clone(), Car(true, 6150), layoutDark, 0.3, false, LightMoment.Of(CarState.Driving))).Count(c => !IsDark(c)) is int n2 && n2 > 0 && n2 < 15);
        var simPro = RpmLightsMapper.ToSimPro(Newtonsoft.Json.Linq.JObject.Parse(@"{""lights"":[{""mode"":""X"",""value"":[],""color"":[],""max_rpm"":8000,""redline"":{},""rpm_redlines"":[{""enabled"":true,""light"":{},""telemtery_item"":{}}]}],""rpm_mode"":0}"), dark, 8000, false);
        Check("flash: SimPro (which can only flash a colour) gets no flash for it", ((Newtonsoft.Json.Linq.JArray)simPro["lights"][0]["rpm_redlines"]).Count == 0);
    }

    static void PerCar()
    {
        var p = NewPlugin();
        Check("per car limiter: none by default", p.LimiterFor("ACC | porsche_992_gt3_r") == null && p.LimiterFor(null) == null);
        var look = new LimiterLook { Style = LimiterStyle.Ends, Colors = new List<string> { "#FFB000" }, Hz = 2.5 };
        p.SetCarLimiter("ACC | porsche_992_gt3_r", look);
        look.Hz = 9; // the saved one is a copy
        var saved = p.LimiterFor("ACC | porsche_992_gt3_r");
        Check("per car limiter: saved as a copy", saved?.Style == LimiterStyle.Ends && saved.Hz == 2.5 && saved.Colors[0] == "#FFB000");
        p.SwitchWheel(WheelModel.GtNeo);
        Check("per car limiter: the same on the other wheel", p.LimiterFor("ACC | porsche_992_gt3_r")?.Style == LimiterStyle.Ends);
        p.SetCarLimiter("ACC | porsche_992_gt3_r", null);
        Check("per car limiter: removed", p.LimiterFor("ACC | porsche_992_gt3_r") == null);
    }
}
