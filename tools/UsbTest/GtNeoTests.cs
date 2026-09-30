using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// GT Neo support (docs/gt-neo-plan.md): wheel models, the lights engine on the GT Neo, its USB LED reports, wheel
/// detection and the switch, per-wheel settings. The FX Pro must render exactly as before (golden-fxpro.txt, from the
/// code before GT Neo support).
/// </summary>
static class GtNeoTests
{
    public static void Run()
    {
        Golden();
        Models();
        Engine();
        Link();
        Detection();
        PerWheelSettings();
        Plugin();
    }

    static void Golden()
    {
        var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "golden-fxpro.txt");
        var want = File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n") : null; // git may check it out with CRLF
        var got = global::Golden.Dump(() => new LightEngine(WheelModel.FxPro));
        Check("FX Pro: every preset renders exactly as before GT Neo support", want != null && got == want,
              want == null ? "golden-fxpro.txt missing" : FirstDiff(want, got));
    }

    static string FirstDiff(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) return "line " + (i + 1) + ": " + y[i].Substring(0, Math.Min(60, y[i].Length));
        return x.Length == y.Length ? "" : $"{y.Length} lines, want {x.Length}";
    }

    static void Models()
    {
        foreach (var m in WheelModel.All)
        {
            var all = m.Groups.SelectMany(m.Leds).ToList();
            Check($"{m.Name}: its groups cover every LED once", all.Count == m.LedCount && all.Distinct().Count() == m.LedCount && all.Min() == 0 && all.Max() == m.LedCount - 1);
        }
        var neo = WheelModel.GtNeo;
        Check("GT Neo: 73 LEDs, 10 buttons, 4 rings of 12, 15 rev lights",
              neo.LedCount == 73 && neo.Leds(LedGroup.Buttons).Length == 10 && neo.Segments(LedGroup.Encoders).Length == 4
              && neo.Segments(LedGroup.Encoders).All(r => r.Length == 12) && neo.Leds(LedGroup.Rev).SequenceEqual(Enumerable.Range(58, 15)));
        Check("GT Neo: no screen, no firmware, called USB", !neo.HasScreen && !neo.NeedsFirmware && neo.ModeName == "USB");
        Check("GT Neo: side-light alerts use the ends of the rev bar", neo.AlertLeds(LedGroup.SideLeft).SequenceEqual(new[] { 58, 59, 60, 61 })
              && neo.AlertLeds(LedGroup.SideRight).SequenceEqual(new[] { 69, 70, 71, 72 }) && neo.AlertGroups.Contains(LedGroup.SideLeft));
        Check("wheel ids: unknown falls back to the FX Pro", WheelModel.Find("nope") == WheelModel.FxPro && WheelModel.Find("GTNEO") == neo);
        Check("SimPro product ids", WheelModel.BySimPro("0000000002060000") == neo && WheelModel.BySimPro("0000000002030000") == WheelModel.FxPro && WheelModel.BySimPro("x") == null);
        Check("GT Neo version from its USB serial", NeoUsb.VersionText("936684880B3748540B373355-01010404-00010404") == "1.4.4" && NeoUsb.VersionText("junk") == null);
        Check("ATSR-Hub map sized for the GT Neo", AtsrBridge.ParseMap("", 73, out _).Length == 73 && AtsrBridge.ParseMap("1,2", 73, out var err).Length == 73 && err != null);
    }

    static void Engine()
    {
        var neo = WheelModel.GtNeo;
        var v = new DashValues { Running = true, Rpm = 7900, MaxRpm = 8400, Redline = 8000, GearKey = "3", FuelPercent = 50 };
        foreach (var p in LightPresets.GtNeo)
        {
            var f = new LightEngine(neo).Render(p, v, null, 1.3, false);
            Check($"GT Neo preset {p.Name}: a full frame, rev bar lit near the shift point", f.Length == 73 && f.Skip(58).Take(10).All(c => c.R + c.G + c.B > 0));
        }
        Check("presets per wheel", LightPresets.For(neo).All(p => p.Id.StartsWith("neo-")) && LightPresets.For(WheelModel.FxPro).All(p => !p.Id.StartsWith("neo-"))
              && LightPresets.All.Select(p => p.Id).Distinct().Count() == LightPresets.All.Length);

        // each ring runs its own effect: the same position on every ring shows the same colour
        var rainbow = LightPresets.Find("neo-rainbow");
        var fr = new LightEngine(neo).Render(rainbow, v, null, 0.7, false);
        bool same = Enumerable.Range(0, 12).All(j => Same(fr[10 + j], fr[22 + j]) && Same(fr[10 + j], fr[46 + j]));
        bool turns = !Same(fr[10], fr[16]);
        Check("GT Neo: a rainbow goes round each ring", same && turns);

        // an FX Pro profile on the GT Neo: side groups skipped, Levels shown steady
        var fx = LightPresets.Find("aurora").Clone();
        fx.Groups[LedGroup.Encoders] = new GroupLighting { Effect = LightEffect.Levels, Colors = new List<string> { "#00FF00", "#FF0000" } };
        var ff = new LightEngine(neo).Render(fx, v, null, 0.5, false);
        Check("an FX Pro profile renders on the GT Neo (Levels steady)", ff.Length == 73 && ff[10].G == 255 && ff[10].R == 0);

        // spotter on the left: the left end of the rev bar
        var stealth = LightPresets.Find("neo-stealth").Clone();
        var sv = new DashValues { Running = true, Rpm = 3000, MaxRpm = 8000, SpotterLeft = true, FuelPercent = 50 };
        var sf = new LightEngine(neo).Render(stealth, sv, null, 0.2, false);
        var spot = LightEngine.Rgb(stealth.Alerts.First(a => a.Trigger == AlertTrigger.SpotterLeft).Color);
        Check("GT Neo: car on the left lights the left end of the rev bar", Enumerable.Range(58, 4).All(i => sf[i].R == spot.Item1 && sf[i].G == spot.Item2) && sf[62].R + sf[62].G == 0);
    }

    static bool Same(LedColor a, LedColor b) => a.R == b.R && a.G == b.G && a.B == b.B;

    static void Link()
    {
        var reports = new List<byte[]>();
        var now = new DateTime(2026, 9, 29, 20, 0, 0);
        var link = new NeoLedLink(r => { reports.Add((byte[])r.Clone()); return true; }, () => now);
        link.Enable();
        Check("GT Neo link: on = F0 .. EC 02 01", reports.Count == 1 && reports[0].Length == 64 && reports[0][0] == 0xF0 && reports[0][6] == 0xEC && reports[0][7] == 2 && reports[0][8] == 1);
        reports.Clear();

        var frame = Enumerable.Range(0, 73).Select(i => new LedColor((byte)i, 0x40, 0x80, 90)).ToArray();
        link.Send(frame, 90);
        var ids = reports.SelectMany(r => Enumerable.Range(0, r[8]).Select(k => r[9 + 4 * k])).ToList();
        Check("GT Neo link: a first frame is every LED in 13s (6 reports)", reports.Count == 6 && reports.All(r => r[7] == 3 && r[8] <= 13) && ids.SequenceEqual(Enumerable.Range(0, 73).Select(i => (byte)i)));
        Check("GT Neo link: colours as R G B", reports[0][9] == 0 && reports[0][10] == 0 && reports[0][11] == 0x40 && reports[0][12] == 0x80 && reports[1][9] == 13 && reports[1][10] == 13);
        Check("GT Neo link: never the hang or update reports", reports.All(r => r[0] == 0xF0 && r[6] == 0xEC));

        reports.Clear();
        link.Send(frame, 90);
        Check("GT Neo link: nothing sent when nothing changed", reports.Count == 0);
        now = now.AddSeconds(1.1);
        link.Send(frame, 90);
        Check("GT Neo link: every second, host mode on again and the whole frame (SimHub's device switches host mode off when it stops)",
              reports.Count == 7 && reports[0][7] == 2 && reports[0][8] == 1 && reports.Skip(1).Sum(r => r[8]) == 73);

        reports.Clear();
        frame[40] = new LedColor(255, 0, 0, 90);
        link.Send(frame, 90);
        Check("GT Neo link: one LED changed, one short report", reports.Count == 1 && reports[0][8] == 1 && reports[0][9] == 40 && reports[0][10] == 255);

        reports.Clear();
        link.Send(frame, 45);
        Check("GT Neo link: the brightness limit scales colours", reports.Count > 0 && reports.SelectMany(r => Enumerable.Range(0, r[8]).Where(k => r[9 + 4 * k] == 40).Select(k => r[10 + 4 * k])).First() == 128);
        Check("GT Neo link: brightness scaling", NeoLedLink.Scale(new LedColor(200, 100, 0, 45), 90) == (100 << 16 | 50 << 8) && NeoLedLink.Scale(new LedColor(255, 255, 255, 1), 90) > 0);

        reports.Clear();
        link.Disable();
        Check("GT Neo link: off = EC 02 00", reports.Count == 1 && reports[0][7] == 2 && reports[0][8] == 0);
    }

    static void Detection()
    {
        var fx = WheelModel.FxPro; var neo = WheelModel.GtNeo;
        DetectedWheel U(WheelModel m) => new DetectedWheel { Model = m, Link = WheelLink.Usb, Path = m.Id };
        DetectedWheel B(WheelModel m) => new DetectedWheel { Model = m, Link = WheelLink.Base };
        Check("choose: nothing found keeps the current wheel", WheelDetector.Choose(new DetectedWheel[0], neo) == neo);
        Check("choose: the only wheel found", WheelDetector.Choose(new[] { U(neo) }, fx) == neo && WheelDetector.Choose(new[] { B(fx) }, neo) == fx);
        Check("choose: both found keeps the current one", WheelDetector.Choose(new[] { U(fx), B(neo) }, neo) == neo && WheelDetector.Choose(new[] { U(fx), B(neo) }, fx) == fx);

        var usb = new Dictionary<WheelModel, string>();
        var d = new WheelDetector(m => usb.TryGetValue(m, out var p) ? p : null, start: false);
        int changes = 0;
        d.Changed += () => changes++;
        d.Check();
        Check("detector: nothing connected", d.Wheels.Count == 0 && changes == 0);
        usb[neo] = "\\\\?\\hid#vid_3670&pid_0805";
        d.Check();
        Check("detector: GT Neo on USB", d.Wheels.Count == 1 && d.Wheels[0].Model == neo && d.PathOf(neo) != null && changes == 1);
        d.SetBase(neo, fx);
        d.Check();
        Check("detector: a wheel on USB and the base counts once, as USB", d.Wheels.Count == 2 && d.Wheels.Count(w => w.Model == neo) == 1
              && d.Wheels.First(w => w.Model == neo).Link == WheelLink.Usb && d.Wheels.First(w => w.Model == fx).Link == WheelLink.Base && changes == 2);
        d.Check();
        Check("detector: no change, no event", changes == 2);
    }

    static void PerWheelSettings()
    {
        var u = new UsbSettings { LightPreset = "aurora" };
        u.ButtonLeds[1] = 0;
        u.WheelButtons["sleep"] = 7;
        u.CarLights["ACC | car"] = "ember";
        u.SwapWheel(WheelModel.GtNeo);
        Check("switch: the GT Neo starts with its own first preset and nothing mapped", u.ActiveWheel == "gtneo" && u.LightPreset == "neo-prism"
              && u.ButtonLeds.Count == 0 && u.WheelButtons.Count == 0 && u.CarLights.Count == 0 && u.Wheels.ContainsKey("fxpro"));
        u.LightPreset = "neo-ember"; u.ButtonLeds[3] = 5;
        Check("switch: the GT Neo's presets only", u.AllLightIds().All(id => id.StartsWith("neo-")) && u.ActiveLights.Id == "neo-ember");
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(u);
        u = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>(json);
        u.SwapWheel(WheelModel.FxPro);
        Check("switch back: the FX Pro's own settings return (after a save and load)", u.ActiveWheel == "fxpro" && u.LightPreset == "aurora" && u.ButtonLeds[1] == 0
              && u.WheelButtons["sleep"] == 7 && u.CarLights["ACC | car"] == "ember" && u.Wheels["gtneo"].LightPreset == "neo-ember" && u.Wheels["gtneo"].ButtonLeds[3] == 5);
        u.SwapWheel(WheelModel.FxPro);
        Check("switch to the same wheel changes nothing", u.LightPreset == "aurora" && !u.Wheels.ContainsKey("fxpro"));

        var id = u.CopyLightsTo(WheelModel.GtNeo, LightPresets.Find("synthwave"));
        Check("copy lights to the other wheel", u.Wheels["gtneo"].UserLights.Any(p => p.Id == id && p.Name == "Synthwave (copy)") && !u.UserLights.Any(p => p.Id == id));
        u.SwapWheel(WheelModel.GtNeo);
        Check("the copy shows on that wheel", u.FindLights(id)?.Name == "Synthwave (copy)" && u.AllLightIds().Contains(id));

        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>("{\"LightPreset\":\"ice\"}");
        Check("settings from before GT Neo support: the FX Pro, as they were", old.Model == WheelModel.FxPro && old.LightPreset == "ice" && old.Wheels.Count == 0);
    }

    static void Plugin()
    {
        var p = NewPlugin();
        p.Settings.Usb.WheelButtons.Clear();
        p.SwitchWheel(WheelModel.GtNeo);
        Check("plugin: switch to the GT Neo", p.ActiveModel == WheelModel.GtNeo && p.ActiveLightsFor(null).Id == "neo-prism");
        Check("plugin: no FX Pro dash-button binding on the GT Neo", !p.Settings.Usb.WheelButtons.ContainsValue(WheelButtons.DashButton));
        p.SwitchWheel(WheelModel.FxPro);
        Check("plugin: back on the FX Pro, the dash button steps the dashes", p.ActiveModel == WheelModel.FxPro && p.Settings.Usb.WheelButtons.TryGetValue("next", out var b) && b == WheelButtons.DashButton);
        Check("button names per wheel", WheelButtons.Name(40, WheelModel.FxPro) == "Dash button" && WheelButtons.Name(40, WheelModel.GtNeo) == "Wheel button 40");
    }
}
