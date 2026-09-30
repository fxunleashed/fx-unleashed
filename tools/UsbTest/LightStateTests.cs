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
        Tracker();
        Gauges();
        Looks();
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
