using System;
using System.Collections.Generic;
using User.FXProRpmSync;

/// <summary>
/// Checks for the logic behind the NEXT.md workstreams that doesn't need the wheel (quick controls, lights per car /
/// game, ...). Each check prints one line; any failure makes the run return 1.
/// </summary>
static class FeatureTests
{
    static int failures;

    public static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) failures++;
    }

    public static int Run(string dir)
    {
        failures = 0;
        QuickControls();
        LightsPerCar();
        FeedWatchChecks();
        Alerts();
        UpdaterTests.Run(dir);
        WorkstreamTests.Run();
        GtNeoTests.Run();
        LightStateTests.Run();
        CalibrationTests.Run();
        CarLightsTests.Run();
        foreach (var extra in Extra) extra();
        Console.WriteLine(failures == 0 ? "features: OK" : $"features: {failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Later workstreams add their checks here (keeps Run short).</summary>
    public static readonly List<Action> Extra = new List<Action>();

    static void QuickControls()
    {
        Check("night window same day", UsbSettings.InWindow("08:00", "17:00", TimeSpan.FromHours(12)) && !UsbSettings.InWindow("08:00", "17:00", TimeSpan.FromHours(18)));
        Check("night window past midnight", UsbSettings.InWindow("22:00", "07:00", TimeSpan.FromHours(23)) && UsbSettings.InWindow("22:00", "07:00", TimeSpan.FromHours(3))
                                            && !UsbSettings.InWindow("22:00", "07:00", TimeSpan.FromHours(12)));
        Check("night window bad text = never", !UsbSettings.InWindow("late", "07:00", TimeSpan.FromHours(3)) && !UsbSettings.InWindow("07:00", "07:00", TimeSpan.FromHours(7)));
        var u = new UsbSettings { LedCeiling = 20, NightLedCeiling = 50, ScreenBrightness = 3, NightScreenBrightness = 140 };
        Check("ceiling day", u.LedCeilingNow(false) == 20);
        Check("night ceiling never brighter than day", u.LedCeilingNow(true) == 20);
        u.LedCeiling = 200; u.NightLedCeiling = 0;
        Check("ceiling clamped 1-90", u.LedCeilingNow(false) == 90 && u.LedCeilingNow(true) == 1);
        Check("screen brightness clamped 5-100", u.ScreenBrightnessNow(false) == 5 && u.ScreenBrightnessNow(true) == 100);

        var plugin = NewPlugin();
        var s = plugin.Settings.Usb;
        s.LedCeiling = 50;
        plugin.StepLedCeiling(+10); Check("LED ceiling up", s.LedCeiling == 60);
        for (int i = 0; i < 20; i++) plugin.StepLedCeiling(-10);
        Check("LED ceiling floor", s.LedCeiling == 1);
        plugin.ToggleScreen(); Check("screen toggle off", s.ScreenOff);
        plugin.ToggleScreen(); Check("screen toggle on", !s.ScreenOff);
        plugin.ToggleNightMode(); Check("night toggle on", plugin.NightActive && s.NightMode);
        plugin.StepScreenBrightness(-10); Check("screen step goes to the night value at night", s.NightScreenBrightness == 15 && s.ScreenBrightness == 100);
        plugin.ToggleNightMode(); Check("night toggle off", !plugin.NightActive && !s.NightMode);
        // a schedule covering all day: on without the switch, a toggle overrides it
        s.NightSchedule = true; s.NightFrom = "00:00"; s.NightTo = "23:59";
        bool inWindow = DateTime.Now.TimeOfDay < TimeSpan.FromMinutes(23 * 60 + 59);
        if (inWindow)
        {
            Check("schedule turns night on", plugin.NightActive && !s.NightMode);
            plugin.ToggleNightMode(); Check("toggle against the schedule", !plugin.NightActive && !s.NightMode);
            plugin.SetNightMode(true); Check("switch on clears the override", plugin.NightActive);
        }
    }

    static void LightsPerCar()
    {
        var plugin = NewPlugin();
        var s = plugin.Settings.Usb;
        s.LightPreset = "aurora";
        string car = "iRacing | porsche992cup", other = "iRacing | mx5";
        Check("global preset", plugin.ActiveLightsFor(car).Id == "aurora" && plugin.LightsIdFor(null, out var l0) == "aurora" && l0 == "global");
        plugin.SetGameLights("iracing", "ember"); // game keys ignore case (SimHub's GameName)
        Check("game preset", plugin.ActiveLightsFor(car).Id == "ember" && plugin.ActiveLightsFor(other).Id == "ember");
        plugin.SetCarLights(car, "ice");
        Check("car beats game", plugin.ActiveLightsFor(car).Id == "ice" && plugin.ActiveLightsFor(other).Id == "ember");
        Check("other game untouched", plugin.ActiveLightsFor("LMU | x").Id == "aurora");
        s.CarLights[car] = "user-deleted";
        Check("deleted car lights fall back to the game", plugin.ActiveLightsFor(car).Id == "ember");
        plugin.SetCarLights(car, null); plugin.SetGameLights("iRacing", null);
        Check("cleared", plugin.ActiveLightsFor(car).Id == "aurora");
        Check("game of key", FXProRpmSyncPlugin.GameOf("Assetto Corsa Competizione | bmw_m4_gt3") == "Assetto Corsa Competizione" && FXProRpmSyncPlugin.GameOf("nokey") == null);
        plugin.CycleLightPreset(+1);
        Check("cycle global", s.LightPreset == "synthwave", s.LightPreset);
        var ids = s.AllLightIds();
        s.LightPreset = ids[0]; plugin.CycleLightPreset(-1);
        Check("cycle wraps", s.LightPreset == ids[ids.Count - 1], s.LightPreset);
    }

    static void FeedWatchChecks()
    {
        var w = new FeedWatch();
        // SimPro on the game: raised on the second check, not the first
        Check("feed: first check doesn't raise", w.Update(true, true, true, true, true, "iRacing") == FeedWatch.Change.None && !w.Problem);
        Check("feed: second check raises", w.Update(true, true, true, true, true, "iRacing") == FeedWatch.Change.Raised && w.Problem);
        Check("feed: message names the game", w.Action == "Close iRacing and start it again." && !w.Detail.Contains("Still showing"), w.Action);
        Check("feed: in-between frames don't clear", w.Update(true, true, true, true, false, "iRacing") == FeedWatch.Change.None && w.Problem);
        // the user closes the game (stub running): cleared at once
        Check("feed: game closed clears", w.Update(true, false, true, true, false, null) == FeedWatch.Change.Cleared && !w.Problem);
        // it comes back after the restart: the SimPro restart advice is added
        w.Update(true, true, true, true, true, "iRacing");
        w.Update(true, true, true, true, true, "iRacing");
        Check("feed: still after restart", w.Problem && w.Detail.Contains("Still showing"));
        // SimPro back on SimGame: cleared, and a good game resets the 'still' memory
        Check("feed: SimGame clears", w.Update(true, true, true, false, true, "iRacing") == FeedWatch.Change.Cleared);
        w.Update(true, true, true, true, true, "ACC"); w.Update(true, true, true, true, true, "ACC");
        Check("feed: fresh occurrence after a good game", w.Problem && !w.Detail.Contains("Still showing") && w.Action.Contains("ACC"));
        // feed off / unlocked mode / no game / SimPro unknown: never
        var n = new FeedWatch();
        for (int i = 0; i < 3; i++) n.Update(false, true, false, true, true, "iRacing");
        Check("feed: not wanted never raises", !n.Problem);
        for (int i = 0; i < 3; i++) n.Update(true, false, true, true, true, null);
        Check("feed: no game never raises", !n.Problem);
        for (int i = 0; i < 3; i++) n.Update(true, true, true, null, true, "iRacing");
        Check("feed: SimPro unknown never raises", !n.Problem);
        for (int i = 0; i < 2; i++) n.Update(true, true, false, false, true, "LMU");
        Check("feed: stub down raises with its own advice", n.Problem && n.StubDown && n.Action.Contains("off and on"));
    }

    /// <summary>F: alerts (new triggers, custom conditions, priority, styles), encoder levels, profile upgrades.</summary>
    static void Alerts()
    {
        // condition text -> binding
        Check("alert bind: property path", AlertRule.Bind("DataCorePlugin.GameData.NewData.CarDamagesMax") == "prop:DataCorePlugin.GameData.NewData.CarDamagesMax");
        Check("alert bind: formula", AlertRule.Bind("[CarDamagesMax] > 5") == "ncalc:[CarDamagesMax] > 5");
        Check("alert bind: kept as typed", AlertRule.Bind("js:return 1") == "js:return 1" && AlertRule.Bind("prop:A.B") == "prop:A.B" && AlertRule.Bind(" absActive ") == "absActive");
        Check("alert bind: empty", AlertRule.Bind("  ") == null && AlertRule.Bind(null) == null);

        var engine = new LightEngine();
        var p = LightPresets.Find("stealth").Clone();
        var v = new DashValues { Running = true, MaxRpm = 8000, Rpm = 3000, FuelPercent = 50 };
        Func<int, LedColor> at = led => engine.Render(p, v, null, 0.01, false)[led];
        // spotter beats ABS on the left side lights (higher in the default list)
        v.AbsActive = true; v.SpotterLeft = true;
        var spot = LightEngine.Rgb(p.Alerts.Find(a => a.Trigger == AlertTrigger.SpotterLeft).Color);
        Check("spotter over ABS", at(17).R == spot.R && at(17).G == spot.G);
        // reorder: ABS first now wins
        var abs = p.Alerts.Find(a => a.Trigger == AlertTrigger.Abs);
        p.Alerts.Remove(abs); p.Alerts.Insert(0, abs);
        var absC = LightEngine.Rgb(abs.Color);
        Check("priority follows the list", at(17).R == absC.R && at(17).G == absC.G);
        v.AbsActive = false; v.SpotterLeft = false;

        // custom alert on a dash value and on an evaluated binding
        var custom = new AlertRule { Trigger = AlertTrigger.Custom, Condition = "[CarDamagesMax] > 5", Color = "#FF00C0", BlinkHz = 0, Groups = { LedGroup.Buttons } };
        p.Alerts.Insert(0, custom);
        Check("custom: off without a value", at(0).R != 0xFF || at(0).B != 0xC0);
        Check("custom: in the lights' bindings", new List<string>(p.Bindings()).Contains("ncalc:[CarDamagesMax] > 5"));
        v.Set("ncalc:[CarDamagesMax] > 5", true);
        Check("custom: on when true", at(0).R == 0xFF && at(0).B == 0xC0);
        v.Set("ncalc:[CarDamagesMax] > 5", 0.0);
        Check("custom: 0 is off", at(0).B != 0xC0);
        custom.Enabled = false;
        Check("custom: disabled isn't read", !new List<string>(p.Bindings()).Contains("ncalc:[CarDamagesMax] > 5"));

        // new triggers
        v.Rpm = 7950;
        Check("rev limiter", LightEngine.Active(AlertTrigger.RevLimiter, v) && !LightEngine.Active(AlertTrigger.RevLimiter, new DashValues { Running = true, MaxRpm = 8000, Rpm = 7700 }));
        v.OrangeFlag = true; v.Stalled = true; v.LapInvalid = true;
        Check("orange / stalled / invalid", LightEngine.Active(AlertTrigger.OrangeFlag, v) && LightEngine.Active(AlertTrigger.Stalled, v) && LightEngine.Active(AlertTrigger.InvalidLap, v));
        Check("nothing while no game", !LightEngine.Active(AlertTrigger.OrangeFlag, new DashValues { OrangeFlag = true }));

        // styles
        var chk = new AlertRule { Style = AlertStyle.Checker, BlinkHz = 2 };
        Check("checker alternates", AlertLevel(chk, 0, 0.0) == 1 && AlertLevel(chk, 1, 0.0) == 0 && AlertLevel(chk, 0, 0.3) == 0 && AlertLevel(chk, 1, 0.3) == 1);
        var flash = new AlertRule { Style = AlertStyle.Flash, BlinkHz = 0 };
        Check("steady flash always on", AlertLevel(flash, 3, 0.37) == 1);
        var sweep = new AlertRule { Style = AlertStyle.Sweep, BlinkHz = 1 };
        double lit = 0; for (int i = 0; i < 15; i++) lit += LightEngine.AlertLevel(sweep, i, 15, 0.5);
        Check("sweep lights a band, not all", lit > 0.5 && lit < 8, lit.ToString("0.00"));

        // an old saved profile gets the new alerts, switched off; none twice
        var old = new LightProfile { Alerts = new List<AlertRule> { new AlertRule { Trigger = AlertTrigger.Abs } } };
        Check("old profile upgraded", old.AddMissingAlerts() && old.Alerts.Count == LightPresets.DefaultAlerts().Count
                                      && old.Alerts.Find(a => a.Trigger == AlertTrigger.SpotterLeft).Enabled == false && old.Alerts[0].Enabled);
        Check("upgrade only once", !old.AddMissingAlerts());

        // encoder levels: TC 11 = last colour, a change blinks, no value = dim
        var lv = LightPresets.Find("stealth").Clone();
        lv.Groups[LedGroup.Encoders] = new GroupLighting { Effect = LightEffect.Levels, Colors = new List<string> { "#00FF00", "#FF0000" } };
        lv.Alerts.Clear();
        var e2 = new LightEngine();
        var d = new DashValues { Running = true };
        d.Set("tcLevel", 11.0); d.Set("absLevel", 1.0);
        var f = e2.Render(lv, d, null, 10, false);
        Check("levels: high = last colour", f[13].R > 200 && f[13].G < 40, $"{f[13].R},{f[13].G}");
        Check("levels: low = first colour", f[12].G > 200 && f[12].R < 40, $"{f[12].R},{f[12].G}");
        Check("levels: missing = dim", f[16].G < 40);
        var d2 = new DashValues { Running = true }; d2.Set("tcLevel", 5.0); d2.Set("absLevel", 1.0);
        bool blinked = false;
        for (double t = 10.02; t < 10.5; t += 0.02) { var g = e2.Render(lv, d2, null, t, false)[13]; if (g.R < 40 && g.G < 40) blinked = true; }
        var later = e2.Render(lv, d2, null, 12, false)[13];
        Check("levels: a change blinks, then steady", blinked && later.R + later.G > 150);
        lv.Groups[LedGroup.Encoders].DiffSource = "DataCorePlugin.GameRawData.Telemetry.dcDiffEntry";
        Check("levels: DIFF source read", new List<string>(lv.Bindings()).Contains("prop:DataCorePlugin.GameRawData.Telemetry.dcDiffEntry"));
    }

    static double AlertLevel(AlertRule a, int i, double t) => LightEngine.AlertLevel(a, i, 15, t);

    /// <summary>A plugin with default settings and no SimHub (SaveSettings is guarded in tests by a null PluginManager).</summary>
    public static FXProRpmSyncPlugin NewPlugin()
    {
        var p = new FXProRpmSyncPlugin { Settings = new FXProRpmSyncSettings() };
        p.Settings.Mode = WheelMode.Unlocked;
        p.Settings.Usb.Enabled = true;
        return p;
    }
}
