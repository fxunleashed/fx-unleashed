using System;
using System.Collections.Generic;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// The games' own car light data (CarLightsDatabase, docs/car-lights-format.md): reading a game file, finding cars by
/// SimHub's names, and turning a car into the wheel's rev lights and pit limiter pattern. The BMW M4 GT3 record is the
/// published AMS2 1.6.9.96 one; its rev lights must come out as the layout checked against the in-game dash
/// (2026-09-29).
/// </summary>
static class CarLightsTests
{
    const string G = "#35FF0B", Y = "#FFDD00", R = "#FF0000", P = "#FF429E";

    static string M4Json() => @"{
 ""schema"": 1, ""game"": ""ams2"", ""simhubGame"": ""Automobilista2"", ""gameVersion"": ""1.6.9.96"",
 ""cars"": [
  { ""carId"": ""BMW M4 GT3"", ""aliases"": [""BMW_M4_GT3""], ""class"": ""GT3"", ""dash"": ""shift-lights"",
    ""rev"": { ""range"": [6200, 7000], ""steps"": 6, ""leds"": [
      { ""pos"": 0.0,    ""stages"": [[6200, ""G""], [7000, ""R""]] },
      { ""pos"": 0.0952, ""stages"": [[6360, ""G""], [7000, ""R""]] },
      { ""pos"": 0.2619, ""stages"": [[6520, ""Y""], [7000, ""R""]] },
      { ""pos"": 0.3563, ""stages"": [[6680, ""Y""], [7000, ""R""]] },
      { ""pos"": 0.4506, ""stages"": [[6840, ""R""]] },
      { ""pos"": 0.5451, ""stages"": [[6840, ""R""]] },
      { ""pos"": 0.6395, ""stages"": [[6680, ""Y""], [7000, ""R""]] },
      { ""pos"": 0.7338, ""stages"": [[6520, ""Y""], [7000, ""R""]] },
      { ""pos"": 0.9048, ""stages"": [[6360, ""G""], [7000, ""R""]] },
      { ""pos"": 1.0,    ""stages"": [[6200, ""G""], [7000, ""R""]] } ] },
    ""limiter"": { ""onBar"": true, ""leds"": [
      { ""pos"": 0.0, ""colour"": ""P"" }, { ""pos"": 0.0952, ""colour"": ""P"" }, { ""pos"": 0.2619, ""colour"": ""G"" },
      { ""pos"": 0.3563, ""colour"": ""G"" }, { ""pos"": 0.4506, ""colour"": ""G"" }, { ""pos"": 0.5451, ""colour"": ""G"" },
      { ""pos"": 0.6395, ""colour"": ""G"" }, { ""pos"": 0.7338, ""colour"": ""G"" }, { ""pos"": 0.9048, ""colour"": ""P"" },
      { ""pos"": 1.0, ""colour"": ""P"" } ] } },
  { ""carId"": ""Ligier JS P217"", ""aliases"": [], ""dash"": ""shift-lights"",
    ""rev"": { ""range"": [7400, 8300], ""steps"": 1, ""leds"": [ { ""pos"": 0.5, ""stages"": [[8300, ""R""]] } ] },
    ""limiter"": { ""onBar"": false, ""colour"": ""#40FF00"", ""lights"": 6 } },
  { ""carId"": ""Gone Car"", ""missingSince"": ""1.6.9.96"", ""dash"": ""tacho"", ""rev"": null, ""limiter"": null }
 ] }".Replace("\"G\"", "\"" + G + "\"").Replace("\"Y\"", "\"" + Y + "\"").Replace("\"R\"", "\"" + R + "\"").Replace("\"P\"", "\"" + P + "\"");

    public static void Run()
    {
        var db = new CarLightsDatabase(() => null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fxu-carlights-test-" + Guid.NewGuid().ToString("N")));
        db.Load(M4Json());

        var m4 = db.Find("Automobilista2", "BMW M4 GT3");
        Check("car lights: found by SimHub's game and car id", m4?.Car.CarId == "BMW M4 GT3" && m4.Label == "AMS2 1.6.9.96");
        Check("car lights: found by an alias", db.Find("Automobilista2", "BMW_M4_GT3")?.Car.CarId == "BMW M4 GT3");
        Check("car lights: another game's names don't match", db.Find("IRacing", "BMW M4 GT3") == null);
        Check("car lights: cars no longer in the game are left out", db.Find("Automobilista2", "Gone Car") == null);
        var variant = db.Find("Automobilista2", "BMW M4 GT3 Model1");
        Check("car lights: a variant word after the name finds the car", variant?.Car.CarId == "BMW M4 GT3" && variant.ReportedAs == "BMW M4 GT3 Model1");
        Check("car lights: an exact name doesn't count as a variant", db.Find("Automobilista2", "BMW M4 GT3")?.ReportedAs == null);
        Check("car lights: more than one extra word isn't a variant", db.Find("Automobilista2", "BMW M4 GT3 Evo Special") == null);
        Check("car lights: a name that only starts the same isn't a variant", db.Find("Automobilista2", "BMW M4 GT34") == null);

        // gaps: two lights missing between the groups -> one unused slot each side, like Lovely's 12
        var profile = CarLightsDatabase.ToProfile(m4.Car.Rev, "BMW M4 GT3", out var slots);
        Check("car lights: gaps become unused slots", profile.LedNumber == 12 && slots[2] == null && slots[9] == null && slots.Count(s => s == null) == 2,
              string.Join(",", slots.Select(s => s?.ToString("0.00") ?? "·")));
        Check("car lights: the all-red last step is the flash", profile.GearRpm["1"][0] == 7000 && profile.Colors[0] == "#FFFF0000");

        var layout = RpmLayout.FromProfile(profile, includeGears: false, exactColours: true);
        var want = new[] { 6200, 6360, 6360, 0, 6520, 6680, 6840, 6840, 6840, 6680, 6520, 0, 6360, 6360, 6200 };
        Check("car lights: M4 GT3 on the wheel = the layout checked in the game", layout.Rpm.SequenceEqual(want), string.Join(",", layout.Rpm));
        Check("car lights: M4 GT3 colours are the dash's", layout.Colors[0] == G && layout.Colors[4] == Y && layout.Colors[7] == R && layout.Colors[3] == LedPalette.Off,
              string.Join(",", layout.Colors));
        Check("car lights: flash at 7000 in red, steady", layout.FlashRpm == 7000 && layout.FlashColor == R && layout.FlashBlinkUnits == 0);
        Check("car lights: no per-gear curves (AMS2 has one range per car)", layout.Gears == null);

        var lim = CarLightsDatabase.ToLimiter(m4.Car);
        var wantLim = new[] { P, P, P, "#000000", G, G, G, G, G, G, G, "#000000", P, P, P };
        Check("car lights: limiter pattern on the same lights as the rev bar", lim.Style == LimiterStyle.CarPattern && lim.Pattern.SequenceEqual(wantLim) && lim.FromGame,
              string.Join(",", lim.Pattern));
        Check("car lights: limiter pattern is steady", lim.At(3, 15, 0.1) == (1.0, false) && lim.At(3, 15, 0.37) == (1.0, false));
        var clone = lim.Clone();
        clone.Pattern[0] = "#123456";
        Check("car lights: a cloned limiter has its own pattern", lim.Pattern[0] == P);

        var ligier = CarLightsDatabase.ToLimiter(db.Find("Automobilista2", "Ligier JS P217").Car);
        Check("car lights: pit lamps off the bar = the whole bar in their colour", ligier.Style == LimiterStyle.Solid && ligier.Colors[0] == "#40FF00");

        Check("car lights: slots keep an even bar as it is", CarLightsDatabase.Slots(new List<double> { 0, 0.25, 0.5, 0.75, 1 }).All(s => s != null));
        Check("car lights: a newer schema is ignored", Try(() => db.Load(M4Json().Replace("\"schema\": 1", "\"schema\": 99").Replace("\"ams2\"", "\"ams3\""))) &&
              db.Find("Automobilista2", "BMW M4 GT3")?.GameVersion == "1.6.9.96");
    }

    static bool Try(Action a) { try { a(); return true; } catch { return false; } }
}
