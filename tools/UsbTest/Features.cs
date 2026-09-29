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
        UpdaterTests.Run(dir);
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

    /// <summary>A plugin with default settings and no SimHub (SaveSettings is guarded in tests by a null PluginManager).</summary>
    public static FXProRpmSyncPlugin NewPlugin()
    {
        var p = new FXProRpmSyncPlugin { Settings = new FXProRpmSyncSettings() };
        p.Settings.Mode = WheelMode.Unlocked;
        p.Settings.Usb.Enabled = true;
        return p;
    }
}
