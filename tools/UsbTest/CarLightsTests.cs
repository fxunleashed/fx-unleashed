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

        // lights that switch off again: the BMW M Hybrid V8's pair moving inward (one side of its mirrored row)
        db.Load(@"{ ""schema"": 1, ""game"": ""ams2x"", ""simhubGame"": ""Automobilista2x"", ""gameVersion"": ""1"", ""cars"": [
          { ""carId"": ""Mover"", ""dash"": ""shift-lights"", ""rev"": { ""range"": [7100, 7850], ""steps"": 6, ""leds"": [
            { ""pos"": 0.0, ""stages"": [[7100, ""#FFBE12""], [7400, null], [7850, ""#00A1FF""]] },
            { ""pos"": 0.5, ""stages"": [[7250, ""#FFBE12""], [7550, null], [7850, ""#00A1FF""]] },
            { ""pos"": 1.0, ""stages"": [[7400, ""#FFBE12""], [7850, ""#00A1FF""]] } ] } } ] }");
        var mover = CarLightsDatabase.ToProfile(db.Find("Automobilista2x", "Mover").Car.Rev, "Mover", out _);
        var ml = RpmLayout.FromProfile(mover, includeGears: false, exactColours: true);
        int a = Array.IndexOf(ml.Rpm, 7100), c = Array.IndexOf(ml.Rpm, 7400);
        Check("car lights: a light that switches off keeps its off RPM", mover.OffRpm != null && mover.OffRpm[1] == 7400 && mover.OffRpm[3] == 0 && ml.OffRpm[a] == 7400);
        Check("car lights: ...and is dark from there until the flash", ml.Lit(a, 7300) && !ml.Lit(a, 7450) && ml.Lit(c, 7450) && ml.FlashRpm == 7850,
              string.Join(",", ml.Rpm) + " / " + string.Join(",", ml.OffRpm));
        Check("car lights: an off RPM moves with an offset", ml.Offset(100).OffRpm[a] == 7500 && ml.Clone().OffRpm != ml.OffRpm);

        // lights stacked on one position can't be laid on the bar (the Ligier JS P320's published record): no rev data
        db.Load(@"{ ""schema"": 1, ""game"": ""ams2s"", ""simhubGame"": ""Automobilista2s"", ""gameVersion"": ""1"", ""cars"": [
          { ""carId"": ""Stacked"", ""dash"": ""shift-lights"", ""rev"": { ""range"": [6000, 6700], ""steps"": 5, ""leds"": [
            { ""pos"": 0.0, ""stages"": [[6000, ""#40FF00""]] }, { ""pos"": 0.0, ""stages"": [[6175, ""#FFD700""]] },
            { ""pos"": 0.0, ""stages"": [[6525, ""#0095FF""]] }, { ""pos"": 1.0, ""stages"": [[6000, ""#00FF00""]] } ] },
            ""limiter"": { ""onBar"": true, ""leds"": [ { ""pos"": 0.0, ""colour"": ""#0095FF"" } ] } },
          { ""carId"": ""Spread"", ""dash"": ""shift-lights"", ""rev"": { ""range"": [6000, 6700], ""steps"": 3, ""leds"": [
            { ""pos"": 0.0, ""stages"": [[6000, ""#40FF00""]] }, { ""pos"": 0.5, ""stages"": [[6350, ""#FFD700""]] }, { ""pos"": 1.0, ""stages"": [[6700, ""#FF0000""]] } ] } } ] }");
        var stacked = db.Find("Automobilista2s", "Stacked");
        Check("car lights: lights stacked on one position give no rev data (Lovely or the preset takes over)", stacked != null && stacked.Car.Rev == null);
        Check("car lights: ...but its pit limiter lights are kept", stacked?.Car.Limiter != null);
        Check("car lights: a record with every light on its own spot is kept", db.Find("Automobilista2s", "Spread")?.Car.Rev?.Lights.Count == 3);

        // Lovely Car Data: SimHub's "Ligier JS P320" against the database's "ligier_js_p320"
        Check("car data: ids compare as plain words", CarLedDatabase.Slug("Ligier JS P320") == "ligier-js-p320" && CarLedDatabase.Slug("ligier_js_p320") == "ligier-js-p320"
              && CarLedDatabase.Slug("Hyper_Porsche Penske 2026_6") == "hyper-porsche-penske-2026-6" && CarLedDatabase.Slug("Citroën  C4") == "citroen-c4" && CarLedDatabase.Slug("  ") == "");
        var manifest = Newtonsoft.Json.Linq.JArray.Parse(@"[
          { ""carName"": ""Ligier JS P320"", ""carId"": ""ligier_js_p320"", ""path"": ""automobilista2/ligier-js-p320.json"" },
          { ""carName"": ""Ligier JS P217"", ""carId"": ""ligier_js_p217"", ""path"": ""automobilista2/ligier-js-p217.json"" },
          { ""carName"": ""AF Corse"", ""carId"": ""AF Corse"", ""path"": ""lmu/af-corse.json"" },
          { ""carName"": ""AF Corse"", ""carId"": ""AF Corse"", ""path"": ""lmu/af-corse-gte.json"" } ]");
        Check("car data: SimHub's spelling finds the database's entry", (string)CarLedDatabase.FindByWords(manifest, "Ligier JS P320")?["path"] == "automobilista2/ligier-js-p320.json");
        Check("car data: another car isn't matched", CarLedDatabase.FindByWords(manifest, "Ligier JS P4") == null);
        Check("car data: two entries that read the same are never guessed between", CarLedDatabase.FindByWords(manifest, "af corse") == null);

        // iRacing: the lights per gear, a blink of their own colour, and a light one gear never lights
        db.Load(@"{ ""schema"": 1, ""game"": ""iracing"", ""simhubGame"": ""IRacing"", ""gameVersion"": ""2026.10.01.01"", ""cars"": [
          { ""carId"": ""testcar"", ""aliases"": [""Test Car""], ""dash"": ""shift-lights"", ""rev"": { ""range"": [7000, 7400], ""steps"": 3,
            ""leds"": [ { ""pos"": 0.0, ""stages"": [[7000, ""#00FF00""]] }, { ""pos"": 0.5, ""stages"": [[7200, ""#FFFF00""]] }, { ""pos"": 1.0, ""stages"": [[7400, ""#FF0000""]] } ],
            ""blink"": [ { ""rpm"": 7600, ""period"": 0.5, ""frames"": [ { ""s"": 0.25, ""leds"": [null, null, null] }, { ""s"": 0.25, ""leds"": [""#0000FF"", ""#0000FF"", ""#0000FF""] } ] } ],
            ""gears"": {
              ""N"": { ""range"": [3000, 5000], ""steps"": 3, ""leds"": [ { ""pos"": 0.0, ""stages"": [[3000, ""#00FF00""]] }, { ""pos"": 0.5, ""stages"": [[4000, ""#FFFF00""]] }, { ""pos"": 1.0, ""stages"": [[5000, ""#FF0000""]] } ] },
              ""R"": { ""range"": [3000, 5000], ""steps"": 3, ""leds"": [ { ""pos"": 0.0, ""stages"": [[3000, ""#00FF00""]] }, { ""pos"": 0.5, ""stages"": [[4000, ""#FFFF00""]] }, { ""pos"": 1.0, ""stages"": [[5000, ""#FF0000""]] } ] },
              ""1"": { ""range"": [6500, 6900], ""steps"": 2, ""leds"": [ { ""pos"": 0.0, ""stages"": [[6500, ""#00FF00""]] }, { ""pos"": 0.5, ""stages"": [[6900, ""#FFFF00""]] } ],
                       ""blink"": [ { ""rpm"": 7100, ""period"": 0.5, ""frames"": [ { ""s"": 0.25, ""leds"": [null, null, null] }, { ""s"": 0.25, ""leds"": [""#0000FF"", ""#0000FF"", ""#0000FF""] } ] } ] },
              ""2"": { ""range"": [7000, 7400], ""steps"": 3, ""leds"": [ { ""pos"": 0.0, ""stages"": [[7000, ""#00FF00""]] }, { ""pos"": 0.5, ""stages"": [[7200, ""#FFFF00""]] }, { ""pos"": 1.0, ""stages"": [[7400, ""#FF0000""]] } ],
                       ""blink"": [ { ""rpm"": 7600, ""period"": 0.5, ""frames"": [ { ""s"": 0.25, ""leds"": [null, null, null] }, { ""s"": 0.25, ""leds"": [""#0000FF"", ""#0000FF"", ""#0000FF""] } ] } ] } } },
            ""limiter"": { ""onBar"": true, ""leds"": [ { ""pos"": 0.0, ""colour"": ""#00FF00"" }, { ""pos"": 0.5, ""colour"": null }, { ""pos"": 1.0, ""colour"": ""#00FF00"" } ],
              ""blink"": { ""period"": 0.5, ""frames"": [] } } } ] }");
        var ir = db.Find("IRacing", "testcar");
        Check("car lights: iRacing car found, with its gears", ir?.Label == "iRacing 2026.10.01.01" && ir.Car.Rev?.Gears?.Count == 4 && db.Find("iracing", "Test Car") != null);
        var irp = CarLightsDatabase.ToProfile(ir.Car.Rev, "testcar", out _);
        Check("car lights: a curve per gear", irp.GearRpm.Count == 4 && irp.GearRpm["N"].Skip(1).SequenceEqual(new[] { 3000, 4000, 5000 }) && irp.GearRpm["2"].Skip(1).SequenceEqual(new[] { 7000, 7200, 7400 }),
              string.Join(" | ", irp.GearRpm.Select(g => g.Key + ":" + string.Join(",", g.Value))));
        Check("car lights: a light a gear never lights stays dark in it", irp.GearRpm["1"][3] == CarLightsDatabase.NeverRpm && irp.Colors[3] == "#FFFF0000");
        Check("car lights: the blink is the flash, in its own colour, at each gear's rpm", irp.Colors[0] == "#FF0000FF" && irp.GearRpm["2"][0] == 7600 && irp.GearRpm["1"][0] == 7100
              && irp.GearRpm["N"][0] == CarLightsDatabase.NeverRpm && irp.RedlineBlinkIntervalMs == 250, $"{irp.Colors[0]} {irp.RedlineBlinkIntervalMs}");
        var irl = RpmLayout.FromProfile(irp, includeGears: true, exactColours: true);
        Check("car lights: per-gear layouts on the wheel", irl.Gears != null && irl.ForGear("N").Rpm[0] == 3000 && irl.ForGear("2").Rpm[0] == 7000 && irl.ForGear("2").FlashRpm == 7600
              && irl.ForGear("1").FlashRpm == 7100 && irl.FlashBlinkUnits > 0, irl.Gears == null ? "no gears" : string.Join(",", irl.ForGear("1").Rpm) + " flash " + irl.ForGear("1").FlashRpm);
        Check("car lights: with no gear known, the car's own main curve, not reverse/neutral's (the most common)",
              RpmLayout.FromProfile(irp, includeGears: false, exactColours: true).Rpm[0] == 7000);
        // "Rev lights only" sends the USB-mode layout (the car's exact colours) to SimPro, which shows only its palette
        var tmpl = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""lights"": [ { ""mode"": ""X"", ""value"": [], ""color"": [], ""max_rpm"": 0,
            ""rpm_redlines"": [ { ""enabled"": true, ""telemtery_item"": { ""value"": { ""threshold"": 0, ""max_value"": 20000 } },
                ""light"": { ""active_effect"": ""mono"", ""configs"": { ""mono"": { ""foregroud_color"": [], ""select_lights"": [] }, ""breath"": { ""foregroud_color"": [], ""select_lights"": [], ""interval"": 1 } } } } ] } ] }");
        var sp = RpmLightsMapper.ToSimPro(tmpl, RpmLayout.FromProfile(irp, includeGears: false, exactColours: true), 8000, false);
        var spColours = ((Newtonsoft.Json.Linq.JArray)sp["lights"][0]["color"]).Select(c => (string)c).ToList();
        Check("car lights: exact colours reach SimPro as its palette (others show as off)",
              spColours.All(c => c == LedPalette.Off || RpmLightsMapper.ToSimProColor(c) == c) && spColours.Any(c => c != LedPalette.Off), string.Join(",", spColours));
        Check("car lights: SimPro's palette by colour name (a lime green stays green, amber stays orange)",
              RpmLightsMapper.ToSimProColor("#70FF01") == "#00ff84" && RpmLightsMapper.ToSimProColor("#45FF45") == "#00ff84" && RpmLightsMapper.ToSimProColor("#FFEA01") == "#fffd51"
              && RpmLightsMapper.ToSimProColor("#FFAD18") == "#ff6c00" && RpmLightsMapper.ToSimProColor("#FF3B10") == "#ff0054" && RpmLightsMapper.ToSimProColor("#07C2FF") == "#00fffc"
              && RpmLightsMapper.ToSimProColor("#268DFF") == "#0006ff" && RpmLightsMapper.ToSimProColor("#000000") == null);
        Check("car lights: a gear's missing light doesn't light on the wheel", !irl.ForGear("1").Lit(14, 9000) && irl.ForGear("1").Lit(0, 6600));
        Check("car lights: iRacing limiter lights on the row", CarLightsDatabase.ToLimiter(ir.Car)?.Pattern?.First() == "#00FF00");

        // the blink frame by frame (the AMG GT3's limiter swaps two patterns every 0.25 s; a flash that isn't 50/50)
        db.Load(@"{ ""schema"": 1, ""game"": ""iracingf"", ""simhubGame"": ""IRacingF"", ""gameVersion"": ""1"", ""cars"": [
          { ""carId"": ""frames"", ""dash"": ""shift-lights"", ""rev"": { ""range"": [7000, 7200], ""steps"": 2,
            ""leds"": [ { ""pos"": 0.0, ""stages"": [[7000, ""#00FF00""]] }, { ""pos"": 1.0, ""stages"": [[7200, ""#FF0000""]] } ],
            ""blink"": [ { ""rpm"": 7400, ""period"": 0.4, ""pos"": [0.0, 1.0], ""frames"": [ { ""s"": 0.1, ""leds"": [""#0000FF"", null] }, { ""s"": 0.3, ""leds"": [null, ""#0000FF""] } ] } ] },
            ""limiter"": { ""onBar"": true, ""leds"": [ { ""pos"": 0.0, ""colour"": ""#01C0FF"" }, { ""pos"": 1.0, ""colour"": ""#FFE901"" } ],
              ""blink"": { ""period"": 0.5, ""frames"": [ { ""s"": 0.25, ""leds"": [""#01C0FF"", ""#FFE901""] }, { ""s"": 0.25, ""leds"": [""#FFE901"", ""#01C0FF""] } ] },
              ""bySpeed"": [ { ""from"": null, ""leds"": [ { ""pos"": 0.0, ""colour"": ""#01C0FF"" }, { ""pos"": 1.0, ""colour"": ""#FFE901"" } ],
                               ""blink"": { ""period"": 0.5, ""frames"": [ { ""s"": 0.25, ""leds"": [""#01C0FF"", ""#FFE901""] }, { ""s"": 0.25, ""leds"": [""#FFE901"", ""#01C0FF""] } ] } },
                             { ""from"": 0.5, ""rel"": true, ""leds"": [ { ""pos"": 0.0, ""colour"": ""#FF0000"" }, { ""pos"": 1.0, ""colour"": ""#FF0000"" } ] } ] } } ] }");
        var fr = db.Find("IRacingF", "frames").Car;
        var frl = RpmLayout.FromProfile(CarLightsDatabase.ToProfile(fr.Rev, "frames", out _), includeGears: false, exactColours: true);
        Check("car lights: the flash's frames reach the wheel", frl.FlashRpm == 7400 && frl.FlashFrames?.Count == 2 && frl.FlashFrames[0].Seconds == 0.1
              && frl.FlashFrames[0].Colors[0] == "#0000FF" && frl.FlashFrames[0].Colors[14] == null && frl.FlashFrames[1].Colors[14] == "#0000FF",
              frl.FlashFrames == null ? "none" : string.Join(" | ", frl.FlashFrames.Select(f => f.Seconds + ":" + string.Join(",", f.Colors))));
        var frlim = CarLightsDatabase.ToLimiter(fr);
        Check("car lights: the limiter swaps its patterns on the clock", frlim.PatternAt(50, 60, 0.1)[6] == "#01C0FF" && frlim.PatternAt(50, 60, 0.3)[6] == "#FFE901"
              && frlim.PatternAt(50, 60, 10.1)[6] == "#01C0FF", string.Join(",", frlim.PatternAt(50, 60, 0.3)));
        Check("car lights: an animated limiter shows as many lights as the car's, centred (no light doubled)",
              frlim.PatternAt(50, 60, 0.1).Count(c => c != "#000000") == 2 && frlim.PatternAt(50, 60, 0.1)[5] == "#000000" && frlim.PatternAt(50, 60, 0.1)[7] == "#FFE901",
              string.Join(",", frlim.PatternAt(50, 60, 0.1)));
        Check("car lights: the limiter's speed step over the limit", frlim.PatternAt(61, 60, 0.3)[6] == "#FF0000" && frlim.PatternAt(60.2, 60, 0.3)[6] == "#FFE901"
              && frlim.PatternAt(70, 0, 0.1)[6] == "#01C0FF");
        var engine = new LightEngine(WheelModel.FxPro);
        var dv = new DashValues { Running = true, Rpm = 7500, GearKey = "3", SessionTime = 100.05 };
        var revLeds = WheelModel.FxPro.Leds(LedGroup.Rev);
        var profileU = LightPresets.For(WheelModel.FxPro)[0].Clone();
        profileU.Groups[LedGroup.Rev] = new GroupLighting { Effect = LightEffect.Rpm };
        profileU.Rev.UseCarData = true;
        profileU.RevTint = 0;
        dv.FuelPercent = 50; dv.MaxRpm = 8000;
        var f1 = engine.Render(profileU, dv, frl, 0, false);
        dv.SessionTime = 100.2;
        var f2 = engine.Render(profileU, dv, frl, 0, false);
        Check("car lights: the wheel plays the flash on the game's clock", f1[revLeds[0]].B > 200 && f1[revLeds[14]].B == 0 && f2[revLeds[0]].B == 0 && f2[revLeds[14]].B > 200,
              $"{f1[revLeds[0]].B}/{f1[revLeds[14]].B} then {f2[revLeds[0]].B}/{f2[revLeds[14]].B}");

        Check("car lights: slots keep an even bar as it is", CarLightsDatabase.Slots(new List<double> { 0, 0.25, 0.5, 0.75, 1 }).All(s => s != null));
        Check("car lights: a newer schema is ignored", Try(() => db.Load(M4Json().Replace("\"schema\": 1", "\"schema\": 99").Replace("\"ams2\"", "\"ams3\""))) &&
              db.Find("Automobilista2", "BMW M4 GT3")?.GameVersion == "1.6.9.96");
    }

    static bool Try(Action a) { try { a(); return true; } catch { return false; } }
}
