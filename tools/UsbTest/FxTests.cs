using System;
using System.Collections.Generic;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// FX (not Pro) support: the model, telling it from the FX Pro (same USB id), its status, its LED tables (stock palette
/// mode and the patch's RGB table), the 5-LED rev bar from a car's 15-LED layout, the engine with every preset,
/// detection with three wheels and its per-wheel settings. The FX Pro's frames stay as recorded (golden-fxpro.txt, in
/// GtNeoTests).
/// </summary>
static class FxTests
{
    public static void Run()
    {
        Model();
        StatusAndProduct();
        Palette();
        Tables();
        RevBar();
        Engine();
        Detection();
        Settings();
    }

    static void Model()
    {
        var fx = WheelModel.Fx;
        Check("FX: 20 LEDs, 12 buttons, 3 dial lights, 5 rev lights", fx.LedCount == 20 && fx.Leds(LedGroup.Buttons).SequenceEqual(Enumerable.Range(0, 12))
              && fx.Leds(LedGroup.Encoders).SequenceEqual(new[] { 12, 14, 13 }) && fx.Leds(LedGroup.Rev).SequenceEqual(Enumerable.Range(15, 5)));
        Check("FX: no screen, no firmware needed, called USB", !fx.HasScreen && !fx.NeedsFirmware && fx.ModeName == "USB" && !fx.HasLevels && !fx.GaugeRings);
        Check("FX: side alerts on the left and right dials, spotter on each side's six buttons",
              fx.AlertLeds(LedGroup.SideLeft).SequenceEqual(new[] { 12 }) && fx.AlertLeds(LedGroup.SideRight).SequenceEqual(new[] { 13 })
              && fx.AlertLeds(LedGroup.ButtonsLeft).SequenceEqual(Enumerable.Range(0, 6)) && fx.AlertLeds(LedGroup.ButtonsRight).SequenceEqual(Enumerable.Range(6, 6)));
        Check("FX: found by SimPro's product id and its own id", WheelModel.BySimPro("0000000002020000") == fx && WheelModel.Find("fx") == fx && WheelModel.All.Contains(fx));
        Check("FX: button names from the owner's wheel", WheelButtons.Name(20, fx) == "Left side, lowest button (20)" && WheelButtons.Name(18, fx) == "Middle dial + (18)"
              && WheelButtons.Name(23, fx) == "Wheel button 23" && WheelButtons.Name(20, WheelModel.FxPro) != WheelButtons.Name(20, fx));
    }

    static void StatusAndProduct()
    {
        Check("product strings: the FX is \"FX Wheel\", the FX Pro anything else (an unread string too)",
              FxUsb.IsFxProduct("FX Wheel") && FxUsb.IsFxProduct(" fx wheel ") && !FxUsb.IsFxProduct("FX Pro Wheel") && !FxUsb.IsFxProduct(null)
              && WheelModel.Fx.UsbProduct("FX Wheel") && !WheelModel.Fx.UsbProduct("FX Pro Wheel") && !WheelModel.Fx.UsbProduct(null)
              && WheelModel.FxPro.UsbProduct("FX Pro Wheel") && WheelModel.FxPro.UsbProduct(null) && !WheelModel.FxPro.UsbProduct("FX Wheel"));
        var fxApp = new FxUsb.Status { Product = 2, Version = 0x10305, RunMode = 0 };
        var proApp = new FxUsb.Status { Product = 3, Version = 0x1030B, RunMode = 0 };
        var fxOld = new FxUsb.Status { Product = 2, Version = 0x10301, RunMode = 0 };
        var fxAsPro = new FxUsb.Status { Product = 2, Version = 0x1030B, RunMode = 0 };
        Check("status: an FX on 1.3.5 is the FX app and never the FX Pro's", fxApp.IsFxApp && !fxApp.IsSupportedApp && fxApp.VersionText == "1.3.5");
        Check("status: the FX Pro's app is never the FX's", proApp.IsSupportedApp && !proApp.IsFxApp);
        Check("status: an FX on 1.3.1, or reporting the FX Pro's version, is neither", !fxOld.IsFxApp && !fxOld.IsSupportedApp && !fxAsPro.IsSupportedApp && !fxAsPro.IsFxApp);
    }

    static void Palette()
    {
        var want = new Dictionary<string, byte>
        {
            ["#ff0054"] = FxWheel.Red, ["#ff6c00"] = FxWheel.Orange, ["#fffd51"] = FxWheel.Yellow, ["#00ff84"] = FxWheel.Green, ["#00fffc"] = FxWheel.Cyan,
            ["#0006ff"] = FxWheel.Blue, ["#6000ff"] = FxWheel.Purple, ["#eeeeee"] = FxWheel.White,
            ["#FF0020"] = FxWheel.Red, ["#FFB000"] = FxWheel.Orange, ["#00FF40"] = FxWheel.Green, ["#0040FF"] = FxWheel.Blue,
        };
        var wrong = want.Where(kv => { var (r, g, b) = LightEngine.Rgb(kv.Key); return FxWheel.Snap(r, g, b) != kv.Value; }).Select(kv => kv.Key).ToList();
        Check("palette: SimPro's colours and the standard rev colours snap to the FX's own", wrong.Count == 0, string.Join(", ", wrong));
        // (the FX's own palette entries don't snap back to themselves: they're corrected for its LEDs, e.g. its orange is
        // stored as R 220 G 50, its cyan as B 220 G 139; the snap works on the colours the plugin means)
        Check("palette: black is off", FxWheel.Snap(0, 0, 0) == FxWheel.Off);
    }

    static LedColor[] Frame() => Enumerable.Range(0, 20).Select(_ => new LedColor(0, 0, 0, 1)).ToArray();

    static void Tables()
    {
        var f = Frame();
        var (t0, r0) = FxWheel.StockFrame(f, 90);
        Check("stock tables: all dark = every LED off", t0.Length == 16 && r0.Length == 44 && t0.All(b => b == 0) && r0.Skip(1).All(b => b == 0));
        f[0] = new LedColor(255, 0, 32, 90);   // red, full
        f[13] = new LedColor(0, 64, 255, 90);  // blue, full: the right dial
        f[15] = new LedColor(0, 255, 64, 90);  // green: rev light 1
        f[19] = new LedColor(0, 64, 255, 90);  // blue: rev light 5
        f[5] = new LedColor(255, 255, 255, 5); // far dimmer than the rest: off
        var (t, r) = FxWheel.StockFrame(f, 90);
        Check("stock tables: colours in the high nibble, trigger 0 (always)", t[0] == 0x10 && t[13] == 0x50 && t[5] == 0 && t[15] == 0);
        Check("stock tables: rev lights at pattern 0 (bytes 1, 11, 21, 31, 41), brightness from the brightest LED",
              r[0] == 100 && r[1] == 0x40 && r[41] == 0x50 && r[11] == 0 && r[2] == 0 && r[12] == 0);
        var (_, rc) = FxWheel.StockFrame(f, 45);
        Check("stock tables: the brightness limit caps the wheel's brightness", rc[0] == 50);
        var half = Frame();
        half[3] = new LedColor(128, 0, 0, 90);
        Check("stock tables: a half-bright colour dims the wheel, not the colour", FxWheel.StockFrame(half, 90).rev[0] == 50 && FxWheel.StockFrame(half, 90).table[3] == 0x10);

        var rgb = FxWheel.RgbFrame(f, 90);
        Check("patch table: 20 x [B, R, G, level], level from the app's curve", rgb.Length == 80 && rgb[0] == 32 && rgb[1] == 255 && rgb[2] == 0 && rgb[3] == FxWheel.Curve[100]
              && rgb[13 * 4] == 255 && rgb[13 * 4 + 1] == 0 && rgb[13 * 4 + 2] == 64);
        Check("patch table: dark LEDs are black with a level (0 would mean the wheel's own)", rgb[4] == 0 && rgb[5] == 0 && rgb[6] == 0 && rgb[7] > 0 && rgb.Where((b, i) => i % 4 == 3).All(b => b > 0));
        Check("patch table: the limit caps each LED's level", FxWheel.RgbFrame(f, 45)[3] == FxWheel.Curve[50]);
        Check("the curve and palettes are the app's (101 levels, 5-255; 2 x 10 entries)", FxWheel.Curve.Length == 101 && FxWheel.Curve[0] == 5 && FxWheel.Curve[100] == 255
              && FxWheel.StockPalettes.Length == 80 && FxWheel.DefaultLedTable.Length == 16);
    }

    static void RevBar()
    {
        Check("rev bar: 15 LEDs show the layout as it is", Enumerable.Range(0, 15).All(i => LightEngine.RevSource(i, 15, 15) == i));
        Check("rev bar: 5 LEDs show the layout's 3rd, 6th, 9th, 12th and 15th", Enumerable.Range(0, 5).Select(i => LightEngine.RevSource(i, 5, 15)).SequenceEqual(new[] { 2, 5, 8, 11, 14 }));

        var car = new RpmLayout();
        for (int i = 0; i < 15; i++) { car.Rpm[i] = 1000 * (i + 1); car.Colors[i] = i < 5 ? "#00FF00" : i < 10 ? "#FFB000" : "#FF0000"; }
        var p = LightPresets.For(WheelModel.Fx)[0].Clone();
        p.Groups[LedGroup.Rev] = new GroupLighting { Effect = LightEffect.Rpm };
        p.Rev.UseCarData = true;
        p.RevTint = 0; // no faint glow on unlit rev lights for this check
        LedColor[] At(double rpm) => new LightEngine(WheelModel.Fx).Render(p, new DashValues { Running = true, Rpm = rpm, MaxRpm = 16000, Redline = 15000, GearKey = "3", FuelPercent = 50 }, car, 0, false);
        bool Lit(LedColor c) => c.R + c.G + c.B > 0;
        var a = At(12500);
        Check("rev bar (FX): at 12,500 rpm the car's 3rd/6th/9th/12th are lit, the 15th not: four of five", Lit(a[15]) && Lit(a[16]) && Lit(a[17]) && Lit(a[18]) && !Lit(a[19]));
        var b = At(2500);
        Check("rev bar (FX): below the car's 3rd light, nothing", Enumerable.Range(15, 5).All(i => !Lit(b[i])));
        var c = At(15000);
        Check("rev bar (FX): full exactly where the car's bar is full, in the car's colours", Enumerable.Range(15, 5).All(i => Lit(c[i])) && c[15].G > 200 && c[15].R == 0 && c[19].R > 200 && c[19].G == 0);
    }

    static void Engine()
    {
        var v = new DashValues { Running = true, Rpm = 7900, MaxRpm = 8400, Redline = 8000, GearKey = "3", FuelPercent = 50 };
        var bad = new List<string>();
        foreach (var p in LightPresets.For(WheelModel.Fx))
        {
            var f = new LightEngine(WheelModel.Fx).Render(p, v, null, 1.3, false);
            if (f.Length != 20 || !f.Skip(15).Take(3).All(x => x.R + x.G + x.B > 0)) bad.Add(p.Name);
        }
        Check("FX: every preset renders 20 LEDs with the rev bar lit near the shift point", bad.Count == 0, string.Join(", ", bad));
        Check("FX: the FX Pro's presets", LightPresets.For(WheelModel.Fx).SequenceEqual(LightPresets.For(WheelModel.FxPro)));
        // the three dial rings (12 left, 13 right, 14 middle) carry each preset's encoder lights: lit at some point over a few seconds of
        // animation unless the preset switches them off (moving, as in the pits: on the track "still while driving" holds
        // each effect on one frame, on the FX as on the FX Pro)
        var darkDials = new List<string>();
        foreach (var p in LightPresets.For(WheelModel.Fx))
        {
            bool off = p.Group(LedGroup.Encoders).Effect == LightEffect.Off;
            var moving = p.Clone();
            moving.StillWhileDriving = false;
            var e = new LightEngine(WheelModel.Fx);
            var lit = new HashSet<int>();
            for (double t = 0; t < 30; t += 0.1) // Abyss sparks slowly
            {
                var f = e.Render(moving, new DashValues { Running = true, Rpm = 4000, MaxRpm = 8400, Redline = 8000, GearKey = "3", FuelPercent = 50 }, null, t, false);
                foreach (int i in new[] { 12, 13, 14 }) if (FxWheel.Level(f[i]) > 3) lit.Add(i);
            }
            if (off ? lit.Count > 0 : lit.Count < 3) darkDials.Add($"{p.Name} ({lit.Count} of 3 lit)");
        }
        Check("FX: every preset's encoder lights show on all three dials (off only where the preset turns them off)", darkDials.Count == 0, string.Join(", ", darkDials));
        // a flag on the encoder lights: all three dials
        var flagged = LightPresets.For(WheelModel.Fx).First(x => x.Alerts.Any(a => a.Trigger == AlertTrigger.YellowFlag)).Clone();
        var ff = new LightEngine(WheelModel.Fx).Render(flagged, new DashValues { Running = true, Rpm = 3000, MaxRpm = 8000, YellowFlag = true, FuelPercent = 50 }, null, 0.05, false);
        Check("FX: a yellow flag lights all three dials yellow", new[] { 12, 13, 14 }.All(i => ff[i].R > 150 && ff[i].G > 120 && ff[i].B < 60));
        // spotter on the left: the left side's buttons and the left dial
        var p2 = LightPresets.For(WheelModel.Fx).First(x => x.Alerts.Any(a => a.Trigger == AlertTrigger.SpotterLeft)).Clone();
        var sf = new LightEngine(WheelModel.Fx).Render(p2, new DashValues { Running = true, Rpm = 3000, MaxRpm = 8000, SpotterLeft = true, FuelPercent = 50 }, null, 0.2, false);
        var quiet = new LightEngine(WheelModel.Fx).Render(p2, new DashValues { Running = true, Rpm = 3000, MaxRpm = 8000, FuelPercent = 50 }, null, 0.2, false);
        bool changed = Enumerable.Range(0, 6).Concat(new[] { 12 }).Any(i => sf[i].R != quiet[i].R || sf[i].G != quiet[i].G || sf[i].B != quiet[i].B);
        bool rightSame = Enumerable.Range(6, 6).All(i => sf[i].R == quiet[i].R && sf[i].G == quiet[i].G && sf[i].B == quiet[i].B);
        Check("FX: a car on the left lights the left side, the right side stays as it was", changed && rightSame);
    }

    static void Detection()
    {
        var fx = WheelModel.Fx; var pro = WheelModel.FxPro; var neo = WheelModel.GtNeo;
        DetectedWheel U(WheelModel m) => new DetectedWheel { Model = m, Link = WheelLink.Usb, Path = m.Id };
        Check("choose: the FX alone", WheelDetector.Choose(new[] { U(fx) }, pro) == fx);
        Check("choose: three wheels keep the current one", WheelDetector.Choose(new[] { U(pro), U(neo), U(fx) }, fx) == fx);
        var usb = new Dictionary<WheelModel, string> { [fx] = "\\\\?\\hid#vid_0483&pid_0529#fx" };
        var d = new WheelDetector(m => usb.TryGetValue(m, out var p) ? p : null, start: false);
        d.Check();
        Check("detector: the FX on USB is the FX, not an FX Pro", d.Wheels.Count == 1 && d.Wheels[0].Model == fx && d.PathOf(pro) == null);
        d.SetBase(fx);
        d.Check();
        Check("detector: the FX on USB and the base counts once", d.Wheels.Count == 1 && d.Wheels[0].Link == WheelLink.Usb);
    }

    static void Settings()
    {
        var u = new UsbSettings { LightPreset = "aurora" };
        u.ButtonLeds[1] = 0;
        u.SwapWheel(WheelModel.Fx);
        Check("switch: the FX starts with the first preset and nothing mapped", u.ActiveWheel == "fx" && u.Model == WheelModel.Fx
              && u.LightPreset == LightPresets.For(WheelModel.Fx)[0].Id && u.ButtonLeds.Count == 0 && u.Wheels.ContainsKey("fxpro"));
        u.LightPreset = "ember"; u.ButtonLeds[20] = 0;
        u.SwapWheel(WheelModel.GtNeo);
        u.SwapWheel(WheelModel.FxPro);
        Check("switch through all three: each keeps its own", u.LightPreset == "aurora" && u.ButtonLeds[1] == 0 && u.Wheels["fx"].LightPreset == "ember"
              && u.Wheels["fx"].ButtonLeds[20] == 0 && u.Wheels.ContainsKey("gtneo"));
        var id = u.CopyLightsTo(WheelModel.Fx, LightPresets.Find("synthwave"));
        Check("copy lights to the FX", u.Wheels["fx"].UserLights.Any(p => p.Id == id));
    }
}
