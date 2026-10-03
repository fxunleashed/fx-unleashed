using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A scripted situation for the wheel's lights ("Try the lights", Lights tab): what the car and game would be doing,
    /// second by second, fed through the same pipeline as a real session (car state, rev bar extras, the light engine), so what
    /// the wheel shows is what driving would show. Each says what to look for. Also what the offline tests play.
    /// </summary>
    public sealed class LightScenario
    {
        public string Id, Title, Expect;
        public double Seconds;
        internal List<(double Seconds, string Note, Action<DashValues, double> Apply)> Segments = new List<(double, string, Action<DashValues, double>)>();
        /// <summary>Switches on whatever the scenario shows (alerts, extras) in a copy of the player's preset.</summary>
        internal Action<LightProfile> Prepare = p => { };
        /// <summary>The car's rev lights for scenarios about them (null = the preset's own).</summary>
        internal Func<LightProfile, RpmLayout> MakeLayout;
        public LaunchTarget Launch;

        internal LightScenario Seg(double seconds, string note, Action<DashValues, double> apply)
        {
            Segments.Add((seconds, note, apply));
            Seconds = Segments.Sum(s => s.Seconds);
            return this;
        }

        /// <summary>The values at `t` seconds (the last segment's end once it's over).</summary>
        public DashValues ValuesAt(double t, out string note)
        {
            double start = 0;
            var seg = Segments.Count > 0 ? Segments[Segments.Count - 1] : default;
            double u = 1;
            foreach (var s in Segments)
            {
                if (t < start + s.Seconds) { seg = s; u = s.Seconds <= 0 ? 1 : (t - start) / s.Seconds; break; }
                start += s.Seconds;
            }
            var v = LightScenarios.Base();
            note = seg.Note;
            seg.Apply?.Invoke(v, Math.Max(0, Math.Min(1, u)));
            v.Set("rpm", v.Rpm); v.Set("maxRpm", v.MaxRpm); v.Set("speed", v.SpeedKmh);
            return v;
        }
    }

    /// <summary>One play of a scenario on a wheel: its own car state, extras history and engine, so it never touches the live ones.</summary>
    public sealed class ScenarioRun
    {
        public LightScenario Scenario { get; }
        private readonly LightProfile profile;
        private readonly LightEngine engine;
        private readonly CarStateTracker tracker = new CarStateTracker();
        private readonly Dictionary<string, double> pitSpeeds = new Dictionary<string, double>();
        private readonly RevExtrasState extras;
        private readonly RpmLayout layout;
        private double t0 = double.NaN;

        public ScenarioRun(LightScenario scenario, LightProfile baseProfile, WheelModel model)
        {
            Scenario = scenario;
            profile = (baseProfile ?? LightPresets.For(model)[0]).Clone();
            scenario.Prepare(profile);
            layout = scenario.MakeLayout?.Invoke(profile);
            engine = new LightEngine(model);
            extras = new RevExtrasState(() => pitSpeeds, d => { foreach (var kv in d) pitSpeeds[kv.Key] = kv.Value; });
        }

        public double Elapsed(double now) { if (double.IsNaN(t0)) t0 = now; return now - t0; }
        public bool Finished(double now) => Elapsed(now) > Scenario.Seconds + 0.6;
        public string NoteAt(double now) { Scenario.ValuesAt(Elapsed(now), out var n); return n; }

        /// <summary>The scenario's values now (also what the wheel's dash shows).</summary>
        public DashValues Values(double now) => Scenario.ValuesAt(Elapsed(now), out _);

        /// <summary>One frame of the wheel's lights, the way a live frame is made.</summary>
        public LedColor[] Frame(double now, bool reverseRev)
        {
            var v = Values(now);
            var state = tracker.Update(v, now);
            var moment = LightMoment.Of(state, tracker.Progress(now));
            moment.Extras = extras.Update(v, now, "scenario", Scenario.Launch);
            return engine.Render(profile, v, layout, now, reverseRev, moment);
        }
    }

    public static class LightScenarios
    {
        public static LightScenario Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>A car driving at speed, engine on, nothing happening.</summary>
        internal static DashValues Base() => new DashValues
        {
            Running = true, EngineOn = true, Rpm = 4500, MaxRpm = 8000, Redline = 7600, GearKey = "3", SpeedKmh = 120, FuelPercent = 60,
            Game = "Scenario", Track = "Test",
        }.With("gear", 3.0).With("gearText", "3").With("throttle", 60.0).With("brake", 0.0).With("clutch", 0.0).With("fuel", 48.0).With("fuelPercent", 60.0)
         .With("brakeBias", 56.0).With("absLevel", 6.0).With("tcLevel", 5.0).With("engineMap", 3.0).With("completedLaps", 3.0).With("lap", 4.0);

        private static DashValues With(this DashValues v, string key, object value) { v.Set(key, value); return v; }

        private static double Lerp(double a, double b, double u) => a + (b - a) * u;
        private static double Tri(double u, double lo, double hi) => lo + (hi - lo) * (u < 0.5 ? u * 2 : 2 - u * 2);

        private static void Enable(LightProfile p, params AlertTrigger[] triggers)
        {
            p.AddMissingAlerts();
            foreach (var a in p.Alerts) if (triggers.Length == 0 || triggers.Contains(a.Trigger)) a.Enabled = true;
        }

        private static CarLedProfile Car(string redColour, int blinkMs, int redline, params int[] leds) => new CarLedProfile
        {
            LedNumber = leds.Length, RedlineBlinkIntervalMs = blinkMs,
            Colors = new[] { redColour }.Concat(leds.Select((r, i) => i < leds.Length / 3 ? "#FF00FF00" : i < leds.Length * 2 / 3 ? "#FFFFFF00" : "#FF0000FF")).ToArray(),
            GearRpm = new Dictionary<string, int[]> { ["1"] = new[] { redline }.Concat(leds).ToArray() },
        };

        public static readonly IReadOnlyList<LightScenario> All = new List<LightScenario>
        {
            new LightScenario
            {
                Id = "driving-still", Title = "Driving: still lights, dark sides",
                Expect = "While the rev bar moves, the buttons and encoders hold one steady look and the three lights each side of the rev bar stay OFF.",
            }.Seg(14, "driving", (v, u) => { v.Rpm = Tri(u * 2 % 1, 3000, 7400); v.SpeedKmh = 140; v.GearKey = "4"; }),

            new LightScenario
            {
                Id = "car-states", Title = "Engine and menu states",
                Expect = "No game: idle look. Menu: dimmer. Engine off: nearly dark. Engine start: the start-up animation. Driving. Pit limiter: the limiter lights. Engine stop: the shutdown animation.",
            }.Seg(3, "no game", (v, u) => { v.Running = false; })
             .Seg(3, "in a menu", (v, u) => { v.InMenu = true; v.EngineOn = false; v.Rpm = 0; v.SpeedKmh = 0; })
             .Seg(3, "engine off", (v, u) => { v.EngineOn = false; v.Rpm = 0; v.SpeedKmh = 0; })
             .Seg(3, "engine starts", (v, u) => { v.Rpm = 900; v.SpeedKmh = 0; v.GearKey = "N"; })
             .Seg(4, "driving", (v, u) => { v.Rpm = Lerp(3000, 6500, u); })
             .Seg(4, "pit limiter on", (v, u) => { v.PitLimiter = true; v.SpeedKmh = 60; v.Rpm = 4500; })
             .Seg(3, "engine stops", (v, u) => { v.EngineOn = false; v.Rpm = 0; v.SpeedKmh = 0; }),

            new LightScenario
            {
                Id = "spotter", Title = "Spotter: a car on each side",
                Expect = "Car on the LEFT: the six buttons on the left side of the wheel light orange. Car on the RIGHT: the six on the right. Both: both sides. Nothing otherwise. (The three small lights beside the rev bar stay for TC, ABS and the like.)",
                Prepare = p => Enable(p, AlertTrigger.SpotterLeft, AlertTrigger.SpotterRight),
            }.Seg(2, "clear", (v, u) => { v.Rpm = 5000; })
             .Seg(3, "car on your LEFT", (v, u) => { v.Rpm = 5000; v.SpotterLeft = true; })
             .Seg(2, "clear", (v, u) => { v.Rpm = 5000; })
             .Seg(3, "car on your RIGHT", (v, u) => { v.Rpm = 5000; v.SpotterRight = true; })
             .Seg(2, "clear", (v, u) => { v.Rpm = 5000; })
             .Seg(3, "cars on BOTH sides", (v, u) => { v.Rpm = 5000; v.SpotterLeft = v.SpotterRight = true; }),

            new LightScenario
            {
                Id = "indicators", Title = "Turn indicators",
                Expect = "Left indicator: the left side lights blink amber. Right indicator: the right side lights blink amber.",
                Prepare = p => Enable(p, AlertTrigger.IndicatorLeft, AlertTrigger.IndicatorRight),
            }.Seg(4, "LEFT indicator blinking", (v, u) => { v.Rpm = 3000; v.SpeedKmh = 50; v.IndicatorLeft = (int)(u * 4 * 2) % 2 == 0; })
             .Seg(4, "RIGHT indicator blinking", (v, u) => { v.Rpm = 3000; v.SpeedKmh = 50; v.IndicatorRight = (int)(u * 4 * 2) % 2 == 0; }),

            new LightScenario
            {
                Id = "alerts", Title = "Every alert in turn",
                Expect = "Two seconds each, named on screen in the Lights tab: ABS, TC, blue/yellow/black/orange/white/green/chequered flags, low fuel, DRS, on the rev limiter, invalid lap, stalled. Each lights the part of the wheel its rule says.",
                Prepare = p => Enable(p, AlertTrigger.Abs, AlertTrigger.Tc, AlertTrigger.BlueFlag, AlertTrigger.YellowFlag, AlertTrigger.BlackFlag, AlertTrigger.OrangeFlag,
                                         AlertTrigger.WhiteFlag, AlertTrigger.GreenFlag, AlertTrigger.CheckeredFlag, AlertTrigger.LowFuel, AlertTrigger.Drs,
                                         AlertTrigger.RevLimiter, AlertTrigger.InvalidLap, AlertTrigger.Stalled),
            }.Seg(2, "ABS active", (v, u) => { v.AbsActive = true; v.Rpm = 5000; })
             .Seg(2, "TC active", (v, u) => { v.TcActive = true; v.Rpm = 5000; })
             .Seg(2, "blue flag", (v, u) => { v.BlueFlag = true; v.Rpm = 5000; })
             .Seg(2, "yellow flag", (v, u) => { v.YellowFlag = true; v.Rpm = 5000; })
             .Seg(2, "black flag", (v, u) => { v.BlackFlag = true; v.Rpm = 5000; })
             .Seg(2, "meatball (orange) flag", (v, u) => { v.OrangeFlag = true; v.Rpm = 5000; })
             .Seg(2, "white flag", (v, u) => { v.WhiteFlag = true; v.Rpm = 5000; })
             .Seg(2, "green flag", (v, u) => { v.GreenFlag = true; v.Rpm = 5000; })
             .Seg(2, "chequered flag", (v, u) => { v.CheckeredFlag = true; v.Rpm = 5000; })
             .Seg(2, "low fuel", (v, u) => { v.FuelPercent = 6; v.Rpm = 5000; })
             .Seg(2, "DRS", (v, u) => { v.Drs = true; v.Rpm = 5000; })
             .Seg(2, "on the rev limiter", (v, u) => { v.Rpm = 7950; })
             .Seg(2, "lap invalidated", (v, u) => { v.LapInvalid = true; v.Rpm = 5000; })
             .Seg(2, "engine stalled", (v, u) => { v.Stalled = true; v.EngineOn = true; v.Rpm = 600; v.SpeedKmh = 0; v.GearKey = "N"; }),

            new LightScenario
            {
                Id = "pit-speed", Title = "Pit speed bar (limit known)",
                Expect = "In the pit lane the rev bar becomes a pointer: well under 60 km/h it sits left of the middle in CYAN, at the limit it's GREEN in the middle, over it it turns RED and fills from the middle to the right. Stopped, the rev lights come back.",
                Prepare = p => p.Extras.PitSpeed = true,
            }.Seg(5, "speeding up in the pit lane: over the limit at the end", (v, u) => PitLane(v, Lerp(0, 75, u), 60))
             .Seg(3, "easing back to the limit", (v, u) => PitLane(v, Lerp(75, 60, u), 60))
             .Seg(3, "holding the limit (green)", (v, u) => PitLane(v, 60 + Math.Sin(u * 12) * 0.6, 60))
             .Seg(3, "slowing right down (cyan, left)", (v, u) => PitLane(v, Lerp(60, 30, u), 60))
             .Seg(3, "stopping: the bar gives the rev lights back", (v, u) => PitLane(v, Lerp(30, 0, u), 60)),

            new LightScenario
            {
                Id = "pit-speed-learned", Title = "Pit speed bar learns the limit",
                Expect = "The game gives no limit here (like AMS2 and LMU). The first second shows the limiter's own blue lights; once the speed has been steady for a second the plugin has learned the limit and the pointer appears: green in the middle at the held speed, moving right (red) when faster and left (cyan) when slower.",
                Prepare = p => p.Extras.PitSpeed = true,
            }.Seg(1, "limiter holding 59.8 km/h: the limiter's own lights, no pointer yet", (v, u) => { PitLane(v, 59.8, 0); v.PitLimiter = true; })
             .Seg(5, "learned: pointer in the middle (green)", (v, u) => { PitLane(v, 59.8, 0); v.PitLimiter = true; })
             .Seg(3, "faster: pointer moves right", (v, u) => { PitLane(v, Lerp(59.8, 72, u), 0); v.PitLimiter = false; })
             .Seg(3, "slower: pointer moves left", (v, u) => { PitLane(v, Lerp(72, 40, u), 0); v.PitLimiter = false; }),

            new LightScenario
            {
                Id = "launch", Title = "Launch aid (target 5000 rpm)",
                Expect = "Standing, first gear: a pointer on the rev bar. Under 5000 rpm it's AMBER and left of the middle, on 5000 it's GREEN in the middle, over it RED to the right. Once moving past 30 km/h the rev lights are back.",
                Prepare = p => p.Extras.Launch = true,
                Launch = new LaunchTarget { Rpm = 5000 },
            }.Seg(6, "revving up at the start: through the target", (v, u) => { v.GearKey = "1"; v.SpeedKmh = 0; v.Rpm = Lerp(2500, 6500, u); })
             .Seg(3, "holding 5000 (green)", (v, u) => { v.GearKey = "1"; v.SpeedKmh = 0; v.Rpm = 5000 + Math.Sin(u * 9) * 40; })
             .Seg(4, "away: past 30 km/h the aid ends", (v, u) => { v.GearKey = "1"; v.SpeedKmh = Lerp(0, 45, u); v.Rpm = Lerp(5000, 7000, u); }),

            new LightScenario
            {
                Id = "lift-coast", Title = "Lift and coast (LMU)",
                Expect = "The rev bar fills from both ends towards the middle in MAGENTA as the progress rises, empties as it falls.",
                Prepare = p => p.Extras.LiftCoast = true,
            }.Seg(7, "progress rising", (v, u) => { v.Rpm = 4200; v.LiftCoast = Lerp(0, 100, u); })
             .Seg(3, "progress falling", (v, u) => { v.Rpm = 4200; v.LiftCoast = Lerp(100, 0, u); }),

            new LightScenario
            {
                Id = "refuel", Title = "Refuelling",
                Expect = "Stopped in the pit box with fuel going in, the rev bar fills left to right in CYAN with the tank level. When the fuel stops going in the rev lights come back.",
                Prepare = p => p.Extras.Refuel = true,
            }.Seg(8, "fuel going in (20% to 80%)", (v, u) => { v.SpeedKmh = 0; v.InPitLane = true; v.Rpm = 900; v.GearKey = "N"; double f = Lerp(20, 80, u); v.FuelPercent = f; v.Set("fuel", f * 0.8); v.Set("fuelPercent", f); })
             .Seg(3, "full: the rev lights return", (v, u) => { v.SpeedKmh = 0; v.InPitLane = true; v.Rpm = 900; v.GearKey = "N"; v.FuelPercent = 80; v.Set("fuel", 64.0); v.Set("fuelPercent", 80.0); }),

            new LightScenario
            {
                Id = "brake-bias", Title = "Brake bias change",
                Expect = "When the bias moves by hand a pointer shows on the rev bar for a couple of seconds: right of the middle for more front, left for less, amber. While BRAKING, the bias drifting (brake migration) shows nothing.",
                Prepare = p => p.Extras.BrakeBias = true,
            }.Seg(2, "56.0%: nothing", (v, u) => { v.Rpm = 4500; v.Set("brakeBias", 56.0); })
             .Seg(4, "bias to 57.5%: pointer right", (v, u) => { v.Rpm = 4500; v.Set("brakeBias", 57.5); })
             .Seg(4, "bias to 54.0%: pointer left", (v, u) => { v.Rpm = 4500; v.Set("brakeBias", 54.0); })
             .Seg(4, "braking, bias drifting: nothing", (v, u) => { v.Rpm = 4500; v.Set("brake", 80.0); v.Set("brakeBias", Lerp(54, 51, u)); }),

            new LightScenario
            {
                Id = "levels", Title = "Encoder levels",
                Expect = "The five encoder lights show ABS, TC, brake bias, DIFF and engine map as a colour from your preset's low to its high colour (the Race Engineer preset: green to red); DIFF stays dark without a value. An encoder whose setting changes dips and recovers for a second.",
                Prepare = p => p.Group(LedGroup.Encoders).Effect = LightEffect.Levels,
            }.Seg(3, "settings steady", (v, u) => { v.Rpm = 4500; })
             .Seg(3, "ABS to 9", (v, u) => { v.Rpm = 4500; v.Set("absLevel", 9.0); })
             .Seg(3, "TC to 2", (v, u) => { v.Rpm = 4500; v.Set("absLevel", 9.0); v.Set("tcLevel", 2.0); })
             .Seg(3, "map to 8", (v, u) => { v.Rpm = 4500; v.Set("absLevel", 9.0); v.Set("tcLevel", 2.0); v.Set("engineMap", 8.0); }),

            new LightScenario
            {
                Id = "car-p320", Title = "Ligier P320: 10 mirrored lights, blinking out",
                Expect = "The lights build up from BOTH outer ends towards the middle, evenly (left and right the same). Over 6700 rpm the whole bar blinks OUT and back (not a colour).",
                Prepare = p => p.Rev.UseCarData = true,
                MakeLayout = p =>
                {
                    // the Ligier JS P320 as Lovely Car Data has it: green, yellow, blue in the middle (cyan beside it), no redline colour but a 120 ms blink
                    var car = Car("#00000000", 120, 6700, 6000, 6000, 6170, 6340, 6525, 6525, 6340, 6170, 6000, 6000);
                    car.Colors = new[] { "#00000000", "#FF00FF00", "#FF00FF00", "#FFFFFF00", "#FFFFFF00", "#FF0000FF", "#FF00BFFF", "#FFFFFF00", "#FFFFFF00", "#FF00FF00", "#FF00FF00" };
                    return RpmLayout.FromProfile(car, false, exactColours: true);
                },
            }.Seg(8, "revving up from 5800 to 6900", (v, u) => { v.GearKey = "2"; v.Rpm = Lerp(5800, 6900, u); })
             .Seg(4, "above the shift point: blinking out", (v, u) => { v.GearKey = "2"; v.Rpm = 6900; })
             .Seg(2, "off the throttle", (v, u) => { v.GearKey = "3"; v.Rpm = Lerp(6900, 5500, u); }),

            new LightScenario
            {
                Id = "car-flash-colour", Title = "Car with a red flash",
                Expect = "The lights build up left to right (green, yellow, blue); at the shift point the whole bar flashes RED, steady. For comparison with the blinking-out car.",
                Prepare = p => p.Rev.UseCarData = true,
                MakeLayout = p => RpmLayout.FromProfile(Car("#FFFF0000", 0, 7000, 5000, 5200, 5400, 5600, 5800, 6000, 6200, 6400, 6600, 6800), false, exactColours: true),
            }.Seg(8, "revving up", (v, u) => { v.Rpm = Lerp(4800, 7200, u); })
             .Seg(3, "above the shift point", (v, u) => { v.Rpm = 7200; }),

            new LightScenario
            {
                Id = "car-iracing", Title = "iRacing car from the game's own numbers",
                Expect = "The first rev light comes on at 6000 rpm and the last at 7000 (even steps between, in your preset's pattern); the bar flashes at 7500. This is what an iRacing car without Lovely data now uses.",
                Prepare = p => p.Rev.UseCarData = true,
                MakeLayout = p => p.Rev.ForAnchors(new ShiftAnchors { First = 6000, Shift = 6500, Last = 7000, Blink = 7500 }),
            }.Seg(9, "revving 5600 to 7800", (v, u) => { v.GearKey = "4"; v.Rpm = Lerp(5600, 7800, u); })
             .Seg(3, "above the blink rpm", (v, u) => { v.GearKey = "4"; v.Rpm = 7800; }),

            new LightScenario
            {
                Id = "invalid-lap", Title = "Lap invalidated",
                Expect = "The dash shows its LAP INVALID overlay (SLIPSTREAM, APEX, NOCTURNE) and, if the alert is on, the encoders light red. (Whether the GAME flags a first lap isn't something this can test: that's checked in a real session.)",
                Prepare = p => Enable(p, AlertTrigger.InvalidLap),
            }.Seg(3, "valid lap", (v, u) => { v.Rpm = 5000; })
             .Seg(4, "lap invalidated", (v, u) => { v.Rpm = 5000; v.LapInvalid = true; v.Set("lapInvalid", true); })
             .Seg(2, "new lap", (v, u) => { v.Rpm = 5000; }),
        };

        private static void PitLane(DashValues v, double speed, double limit)
        {
            v.InPitLane = true; v.SpeedKmh = speed; v.PitSpeedLimit = limit; v.PitLimiter = speed > 5 && limit > 0 && speed < 62;
            v.GearKey = "2"; v.Rpm = 2500 + speed * 25;
        }
    }
}
