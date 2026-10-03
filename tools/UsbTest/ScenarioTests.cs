using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// The light scenarios ("Try the lights", Usb/LightScenarios.cs) played through the real pipeline (ScenarioRun: car state, rev bar
/// extras, light engine) and checked at the moments that matter, and drawn as pictures (OUT/scenarios/*.png: a row of the wheel's
/// LEDs every half second) so they can be looked at without a wheel. What this can't show is the wheel itself and the game's data.
/// </summary>
static class ScenarioTests
{
    const double Dt = 0.05;

    static List<LedColor[]> Play(LightScenario sc, LightProfile p, WheelModel m)
    {
        var run = new ScenarioRun(sc, p, m);
        var frames = new List<LedColor[]>();
        for (int i = 0; i * Dt <= sc.Seconds + 1e-9; i++) frames.Add(run.Frame(i * Dt, false));
        return frames;
    }

    static LedColor[] At(List<LedColor[]> f, double t) => f[Math.Min(f.Count - 1, (int)Math.Round(t / Dt))];
    static bool On(LedColor c) => c.R + c.G + c.B > 0;
    static LedColor[] Rev(LedColor[] f) => f.Skip(23).Take(15).ToArray();
    static LedColor[] Left(LedColor[] f) => f.Skip(17).Take(3).ToArray();
    static LedColor[] Right(LedColor[] f) => f.Skip(20).Take(3).ToArray();
    static LedColor[] LeftButtons(LedColor[] f) => f.Take(6).ToArray();
    static LedColor[] RightButtons(LedColor[] f) => f.Skip(6).Take(6).ToArray();
    static bool Same(LedColor a, LedColor b) => a.R == b.R && a.G == b.G && a.B == b.B && a.Brightness == b.Brightness;
    static int Brightest(LedColor[] f) => Enumerable.Range(0, f.Length).OrderByDescending(i => f[i].R + f[i].G + f[i].B).First();

    public static void Run(string dir)
    {
        var fx = WheelModel.FxPro;
        var prof = LightPresets.Find("synthwave");
        var plays = new Dictionary<string, List<LedColor[]>>();
        foreach (var sc in LightScenarios.All)
        {
            plays[sc.Id] = Play(sc, prof, fx);
            Check($"scenario {sc.Id}: plays on the FX Pro ({sc.Seconds:0} s) with a title and what to look for", plays[sc.Id].All(f => f.Length == 38) && sc.Seconds > 1 && sc.Title.Length > 0 && sc.Expect.Length > 30);
            var neo = Play(sc, LightPresets.Find("neo-aurora"), WheelModel.GtNeo);
            Check($"scenario {sc.Id}: plays on the GT Neo too", neo.All(f => f.Length == 73));
        }
        Check("scenarios: ids are unique and found by id", LightScenarios.All.Select(s => s.Id).Distinct().Count() == LightScenarios.All.Count && LightScenarios.Find("SPOTTER")?.Id == "spotter" && LightScenarios.Find("nope") == null);

        // driving: still, sides dark, the rev bar moves
        var d = plays["driving-still"];
        Check("scenario driving-still: buttons and encoders hold still the whole time", d.Skip(1).All(f => Enumerable.Range(0, 17).All(i => Same(f[i], d[0][i]))));
        Check("scenario driving-still: the side lights stay dark", d.All(f => Left(f).Concat(Right(f)).All(c => !On(c))));
        Check("scenario driving-still: the rev bar moves", d.Select(f => Rev(f).Count(On)).Distinct().Count() > 5);

        // car states
        var cs = plays["car-states"];
        int Lit(LedColor[] f) => f.Count(On);
        Check("scenario car-states: engine off is much dimmer than driving", At(cs, 7.5).Max(c => c.Brightness) < At(cs, 14.5).Take(23).Max(c => c.Brightness) / 2);
        Check("scenario car-states: parked looks use the side lights, driving doesn't", Left(At(cs, 7.5)).Any(On) && Left(At(cs, 14.5)).All(c => !On(c)) && Right(At(cs, 14.5)).All(c => !On(c)));
        Check("scenario car-states: the rev bar is dark with the engine off", Rev(At(cs, 7.5)).All(c => !On(c)));
        Check("scenario car-states: the engine start and stop animate (frames change within them)", Enumerable.Range(0, 20).Select(i => Lit(At(cs, 9.2 + i * 0.05))).Distinct().Count() > 1 || !Same(At(cs, 9.2)[5], At(cs, 10.2)[5]));

        // spotter
        var sp = plays["spotter"];
        Check("scenario spotter: car on the left lights the six left buttons orange, the right ones stay as they were", LeftButtons(At(sp, 3.5)).All(c => c.R > 200 && c.G > 40 && c.B == 0)
              && RightButtons(At(sp, 3.5)).Zip(RightButtons(At(sp, 1)), Same).All(x => x));
        Check("scenario spotter: car on the right lights the six right buttons", RightButtons(At(sp, 8.5)).All(c => c.R > 200 && c.G > 40 && c.B == 0) && LeftButtons(At(sp, 8.5)).Zip(LeftButtons(At(sp, 1)), Same).All(x => x));
        Check("scenario spotter: both sides together", LeftButtons(At(sp, 13.5)).Concat(RightButtons(At(sp, 13.5))).All(c => c.R > 200 && c.G > 40 && c.B == 0));
        Check("scenario spotter: back to the theme when clear", At(sp, 5.5).Take(12).Zip(At(sp, 1).Take(12), Same).All(x => x));
        Check("scenario spotter: the three small lights beside the rev bar stay dark (they're for TC and ABS)", sp.All(f => Left(f).Concat(Right(f)).All(c => !On(c))));

        // indicators
        var ind = plays["indicators"];
        var leftOn = Enumerable.Range(0, 80).Select(i => Left(At(ind, i * 0.05)).All(On)).ToList();
        Check("scenario indicators: the left side lights blink with the indicator", leftOn.Contains(true) && leftOn.Contains(false) && Enumerable.Range(0, 80).All(i => Right(At(ind, i * 0.05)).All(c => !On(c))));
        Check("scenario indicators: then the right ones", Enumerable.Range(80, 80).Select(i => Right(At(ind, i * 0.05)).All(On)).Distinct().Count() == 2);

        // every alert shows something different from nothing
        var al = plays["alerts"];
        var quiet = new ScenarioRun(new LightScenario { Id = "q", Title = "q", Expect = "" }.Seg2(5, (v, u) => v.Rpm = 5000), prof, fx).Frame(1, false);
        string[] names = { "ABS", "TC", "blue flag", "yellow flag", "black flag", "meatball flag", "white flag", "green flag", "chequered flag", "low fuel", "DRS", "rev limiter", "lap invalidated", "stalled" };
        for (int i = 0; i < names.Length; i++)
        {
            // the flashing ones are checked over the second: at some moment the frame differs from the quiet one
            bool differs = Enumerable.Range(0, 20).Any(k => !At(al, i * 2 + 0.2 + k * 0.05).Zip(quiet, Same).All(x => x));
            Check($"scenario alerts: {names[i]} lights something", differs);
        }

        // pit speed
        var ps = plays["pit-speed"];
        var over = Rev(At(ps, 4.8));
        Check("scenario pit-speed: over the limit the bar is red, right of the middle, nothing green", over.Any(c => c.R > 200) && over.All(c => c.G == 0) && Brightest(over) > 7);
        var atLimit = Rev(At(ps, 10.5));
        Check("scenario pit-speed: at the limit the middle is green", atLimit[7].G > 200 && atLimit.All(c => c.R == 0));
        var under = Rev(At(ps, 13.5));
        Check("scenario pit-speed: under the limit a cyan pointer left of the middle", under.Any(c => c.B > 200) && Brightest(under.Select((c, i) => i == 7 ? new LedColor(0, 0, 0, 1) : c).ToArray()) < 7 && under.All(c => c.R == 0 || c.R == c.G));
        var stopped = Rev(At(ps, 16.9));
        Check("scenario pit-speed: stopped, the pit bar is gone (rev lights as the revs say: dark at 2500 rpm)", stopped.All(c => !On(c)));

        // pit speed learned
        var pl = plays["pit-speed-learned"];
        Check("scenario pit-speed-learned: in the first second (limit not learned yet) the limiter's own lights show, no pointer", Rev(At(pl, 0.4)).Count(On) > 3 && Rev(At(pl, 0.4)).All(c => c.G < 100) && Rev(At(pl, 0.4)).Any(c => c.B > 200));
        Check("scenario pit-speed-learned: once learned the pointer is green in the middle", Rev(At(pl, 4.5))[7].G > 200 && Brightest(Rev(At(pl, 4.5))) == 7);
        Check("scenario pit-speed-learned: faster moves it right, slower left", Brightest(Rev(At(pl, 8.5))) > 7 && Brightest(Rev(At(pl, 11.5)).Select((c, i) => i == 7 ? new LedColor(0, 0, 0, 1) : c).ToArray()) < 7);

        // launch
        var la = plays["launch"];
        Check("scenario launch: under the target an amber pointer left of the middle", Brightest(Rev(At(la, 0.5))) < 7 && Rev(At(la, 0.5))[Brightest(Rev(At(la, 0.5)))].R > 200 && Rev(At(la, 0.5))[Brightest(Rev(At(la, 0.5)))].G > 100);
        Check("scenario launch: through the target it goes right, red", Brightest(Rev(At(la, 4.8))) > 7 && Rev(At(la, 4.8))[Brightest(Rev(At(la, 4.8)))].G == 0);
        Check("scenario launch: holding the target it's a green pointer within a light of the middle", new[] { 6, 7, 8 }.Contains(Brightest(Rev(At(la, 7.5)))) && Rev(At(la, 7.5)).All(c => c.R == 0) && Rev(At(la, 7.5)).Max(c => c.G) > 150);
        Check("scenario launch: past 30 km/h the aid is gone (the rev lights, several lit, not one pointer)", Rev(At(la, 12.0)).Count(On) > 3);

        // lift and coast, refuel, bias
        var lc = plays["lift-coast"];
        var lcf = Rev(At(lc, 6.5));
        Check("scenario lift-coast: magenta from both ends, evenly, middle last", lcf[0].R > 200 && lcf[0].B > 200 && lcf[14].R > 200 && lcf.Select(c => (int)c.R).SequenceEqual(lcf.Select(c => (int)c.R).Reverse()) && Rev(At(lc, 2.0)).Count(On) < lcf.Count(On));
        var rf = plays["refuel"];
        Check("scenario refuel: the tank fills the bar left to right in cyan", Rev(At(rf, 7.5)).Count(On) > Rev(At(rf, 1.5)).Count(On) && Rev(At(rf, 7.5)).Where(On).All(c => c.R == 0 && c.B > 0) && Rev(At(rf, 7.5)).Any(c => c.B > 200) && On(Rev(At(rf, 7.5))[0]));
        Check("scenario refuel: when the fuel stops the bar goes (after a moment)", Rev(At(rf, 10.8)).Count(On) < Rev(At(rf, 7.5)).Count(On));
        var bb = plays["brake-bias"];
        Check("scenario brake-bias: nothing before a change", !Rev(At(bb, 1.0)).Any(c => c.R > 200 && c.G > 100 && c.B == 0));
        Check("scenario brake-bias: more front shows a pointer right of the middle", Brightest(Rev(At(bb, 3.0))) > 7);
        Check("scenario brake-bias: it goes after a couple of seconds", !Rev(At(bb, 5.5)).Any(c => c.R > 200 && c.G > 100 && c.B == 0));
        Check("scenario brake-bias: less front shows it left of the middle", Brightest(Rev(At(bb, 7.0)).Select((c, i) => i == 7 ? new LedColor(0, 0, 0, 1) : c).ToArray()) < 7);
        Check("scenario brake-bias: drifting while braking shows nothing", Enumerable.Range(0, 80).All(i => !Rev(At(bb, 12.5 + i * 0.05)).Any(c => c.R > 200 && c.G > 100 && c.B == 0)));

        // encoder levels flash on a change
        var lv = plays["levels"];
        Check("scenario levels: the encoders show colour", Enumerable.Range(12, 5).Count(i => On(At(lv, 1.5)[i])) >= 4);
        Check("scenario levels: an encoder blinks (dips) when its setting changes", Enumerable.Range(0, 20).Select(k => { var c = At(lv, 3.1 + k * 0.05)[12]; return c.R + c.G + c.B; }).Distinct().Count() >= 2
              && Enumerable.Range(0, 20).Select(k => { var c = At(lv, 1.0 + k * 0.05)[12]; return c.R + c.G + c.B; }).Distinct().Count() == 1);

        // car layouts
        var p3 = plays["car-p320"];
        var shapes = Enumerable.Range(0, 30).Select(k => Rev(At(p3, 1 + k * 0.2)).Select(c => On(c) ? 1 : 0).ToArray()).ToList();
        Check("scenario car-p320: the lit lights are always mirrored (left = right)", shapes.All(s => s.SequenceEqual(s.Reverse())), string.Join(" ", shapes.Take(3).Select(s => string.Join("", s))));
        Check("scenario car-p320: more lights as the revs rise", shapes[0].Sum() < shapes[25].Sum());
        var blinkFrames = Enumerable.Range(0, 80).Select(k => Rev(At(p3, 8.1 + k * 0.05)).Count(On)).ToList();
        Check("scenario car-p320: over the shift point the whole bar blinks out and back", blinkFrames.Contains(0) && blinkFrames.Max() >= 8 && blinkFrames.Count(x => x == 0) > 10 && blinkFrames.Count(x => x >= 8) > 10, $"{blinkFrames.Count(x => x == 0)} dark / {blinkFrames.Count(x => x >= 8)} lit of 80");
        var fc = plays["car-flash-colour"];
        var flashColours = Enumerable.Range(0, 60).Select(k => Rev(At(fc, 8.1 + k * 0.05))).ToList();
        Check("scenario car-flash-colour: over the shift point the whole bar is red", flashColours.All(r => r.All(c => c.R > 200 && c.G == 0 && c.B == 0)));
        var ia = plays["car-iracing"];
        Check("scenario car-iracing: nothing lit below the game's first light", Rev(At(ia, 0.1)).All(c => !On(c)));
        Check("scenario car-iracing: all lit by the game's last light (7000), just before the flash", Rev(At(ia, 7.5)).All(On));
        Check("scenario car-iracing: the bar flashes above the game's blink rpm", Enumerable.Range(0, 40).Select(k => Rev(At(ia, 9.5 + k * 0.05)).Count(On)).Distinct().Count() > 1);

        var inv = plays["invalid-lap"];
        Check("scenario invalid-lap: the encoders light red while the lap is invalid", Enumerable.Range(12, 5).All(i => At(inv, 5.0)[i].R > 200 && At(inv, 5.0)[i].G == 0) && !Enumerable.Range(12, 5).All(i => At(inv, 1.0)[i].R > 200 && At(inv, 1.0)[i].G == 0));

        Directory.CreateDirectory(Path.Combine(dir, "scenarios"));
        foreach (var sc in LightScenarios.All) Sheet(Path.Combine(dir, "scenarios", sc.Id + ".png"), sc, plays[sc.Id]);
        Check("scenarios: contact sheets written to OUT/scenarios", Directory.GetFiles(Path.Combine(dir, "scenarios"), "*.png").Length == LightScenarios.All.Count);
    }

    /// <summary>One row of the wheel's LEDs every half second, as the driver sees them (buttons, encoders, side lights around the rev bar).</summary>
    static void Sheet(string path, LightScenario sc, List<LedColor[]> frames)
    {
        int[][] groups = { Enumerable.Range(0, 12).ToArray(), Enumerable.Range(12, 5).ToArray(), new[] { 17, 18, 19 }, Enumerable.Range(23, 15).ToArray(), new[] { 20, 21, 22 } };
        const int r = 8, pitch = 20, gap = 14, left = 330, rowH = 24;
        int rows = (int)Math.Ceiling(sc.Seconds / 0.5) + 1;
        int width = left + groups.Sum(g => g.Length * pitch + gap) + 10;
        using (var bmp = new Bitmap(width, rows * rowH + 30))
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe UI", 8.5f))
        {
            g.Clear(Color.FromArgb(18, 18, 22));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString($"{sc.Title}  ({sc.Seconds:0} s)", font, Brushes.White, 6, 6);
            for (int row = 0; row < rows; row++)
            {
                double t = row * 0.5;
                var f = frames[Math.Min(frames.Count - 1, (int)Math.Round(t / Dt))];
                sc.ValuesAt(t, out var note);
                int y = 26 + row * rowH;
                g.DrawString($"{t,4:0.0}s  {note}", font, Brushes.Gainsboro, 6, y + 3);
                int x = left;
                foreach (var grp in groups)
                {
                    foreach (var i in grp)
                    {
                        var c = f[i];
                        double k = Math.Max(0.05, Math.Min(1, c.Brightness / 90.0));
                        var col = On(c) ? Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k)) : Color.FromArgb(34, 34, 40);
                        using (var b = new SolidBrush(col)) g.FillEllipse(b, x, y + 2, r * 2 - 2, r * 2 - 2);
                        using (var pen = new Pen(Color.FromArgb(70, 70, 80))) g.DrawEllipse(pen, x, y + 2, r * 2 - 2, r * 2 - 2);
                        x += pitch;
                    }
                    x += gap;
                }
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}

static class ScenarioTestExtensions
{
    /// <summary>A one-segment scenario for the tests.</summary>
    public static LightScenario Seg2(this LightScenario s, double seconds, Action<DashValues, double> apply) => s.Seg(seconds, "test", apply);
}
