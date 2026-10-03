using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// What the rev bar shows instead of the shift lights in some situations (a preset's option: LightProfile.Extras).
    /// Each one only takes the bar while its situation lasts, and the shift lights win again near the shift point (except the
    /// pit speed bar and the launch aid, which are the point of the moment).
    /// </summary>
    public class RevExtrasOptions
    {
        /// <summary>In the pit lane, moving: a pointer for how far over or under the pit speed limit you are.</summary>
        public bool PitSpeed = true;
        /// <summary>LMU: the lift-and-coast progress, filling from both ends.</summary>
        public bool LiftCoast = true;
        /// <summary>Stopped while fuel goes in: the fuel level fills the bar.</summary>
        public bool Refuel = true;
        /// <summary>For a moment after the brake bias changes: a pointer for how far it moved from where it was.</summary>
        public bool BrakeBias = true;
        /// <summary>Standing start, first gear: a pointer for the revs (or throttle, or clutch) against the car's launch target. Off until a car has its target.</summary>
        public bool Launch;

        public RevExtrasOptions Clone() => (RevExtrasOptions)MemberwiseClone();
    }

    /// <summary>What the launch aid watches.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LaunchMode { Rpm, Throttle, Clutch }

    /// <summary>A car's launch aid (UsbSettings.CarLaunch, keyed "Game | CarId"): what to hold at a standing start.</summary>
    public class LaunchTarget
    {
        public LaunchMode Mode = LaunchMode.Rpm;
        /// <summary>Rpm mode: the revs to hold (0 = not set: 70% of the car's max).</summary>
        public int Rpm;
        /// <summary>Throttle mode: the throttle to hold, %.</summary>
        public int ThrottlePercent = 80;
        /// <summary>Clutch mode: the clutch's bite point, %.</summary>
        public int BitePercent = 50;

        public LaunchTarget Clone() => (LaunchTarget)MemberwiseClone();

        /// <summary>The target in the units of the mode, and how far either side the pointer's bar reaches.</summary>
        public (double Target, double Range) Window(double maxRpm)
        {
            switch (Mode)
            {
                case LaunchMode.Throttle: return (ThrottlePercent, 25);
                case LaunchMode.Clutch: return (BitePercent, 25);
                default:
                    double t = Rpm > 0 ? Rpm : Math.Round(Math.Max(0, maxRpm) * 0.7 / 100) * 100;
                    return (t, Math.Max(300, Math.Round(Math.Max(0, maxRpm) * 0.06 / 50) * 50));
            }
        }
    }

    /// <summary>What the lights need to know that takes memory (RevExtrasState works it out), for one frame.</summary>
    public sealed class RevExtrasInput
    {
        /// <summary>The pit lane's speed limit, km/h; 0 = unknown.</summary>
        public double PitSpeedKmh;
        /// <summary>Fuel is going in while the car is stopped.</summary>
        public bool Refuelling;
        /// <summary>% front brake bias against what it was when the car loaded.</summary>
        public double BiasOffset;
        /// <summary>The bias changed a moment ago.</summary>
        public bool BiasShown;
        /// <summary>This car's launch aid.</summary>
        public LaunchTarget Launch = new LaunchTarget();
    }

    /// <summary>
    /// The facts behind the rev bar extras that need a history: the pit speed limit (given by the game, else learned from the
    /// speed the pit limiter holds, and remembered per game and track), whether fuel is going in, and the brake bias against
    /// where it started. Called every frame with the frame's time.
    /// </summary>
    public sealed class RevExtrasState
    {
        public const double RefuelHoldSeconds = 1.5, BiasShowSeconds = 2.5, BiasStep = 0.04, StableSeconds = 1.0;

        private readonly Func<Dictionary<string, double>> pitSpeeds;
        private readonly Action<Dictionary<string, double>> setPitSpeeds;
        private string carKey;
        private double lastFuel = -1, fuelRiseAt = double.MinValue;
        private double? biasRef, lastBias;
        private double biasChangedAt = double.MinValue;
        private double stableSpeed, stableSince = double.MaxValue;

        /// <summary>A pit speed was learned or corrected (so it can be saved).</summary>
        public event Action PitSpeedLearned;

        /// <param name="pitSpeeds">The remembered pit speeds ("Game | Track" to km/h); read each time, so a reloaded settings file is followed.</param>
        /// <param name="setPitSpeeds">Stores a changed copy (so settings being saved on another thread never see one half way through a change); without it the dictionary is changed in place.</param>
        public RevExtrasState(Func<Dictionary<string, double>> pitSpeeds, Action<Dictionary<string, double>> setPitSpeeds = null)
        {
            this.pitSpeeds = pitSpeeds;
            this.setPitSpeeds = setPitSpeeds;
        }

        public static string PitKey(DashValues v) => (v.Game ?? "") + " | " + (v.Track ?? "");

        public RevExtrasInput Update(DashValues v, double now, string car, LaunchTarget launch)
        {
            if (car != carKey) { carKey = car; lastFuel = -1; fuelRiseAt = double.MinValue; biasRef = lastBias = null; biasChangedAt = double.MinValue; }
            var r = new RevExtrasInput { Launch = launch ?? new LaunchTarget() };
            if (v == null || !v.Running) return r;

            // pit speed: the game's, else what the limiter holds
            var map = pitSpeeds?.Invoke();
            var key = PitKey(v);
            double known = v.PitSpeedLimit > 0 ? v.PitSpeedLimit : map != null && map.TryGetValue(key, out var m) ? m : 0;
            if (v.PitLimiter && v.InPitLane && v.SpeedKmh >= 20 && v.SpeedKmh <= 150)
            {
                if (Math.Abs(v.SpeedKmh - stableSpeed) > 0.3) { stableSpeed = v.SpeedKmh; stableSince = now; }
                if (now - stableSince >= StableSeconds && v.PitSpeedLimit <= 0 && map != null)
                {
                    double learned = Math.Round(stableSpeed, 1);
                    if (!map.TryGetValue(key, out var old) || Math.Abs(old - learned) >= 0.5)
                    {
                        if (setPitSpeeds != null) setPitSpeeds(new Dictionary<string, double>(map) { [key] = learned });
                        else map[key] = learned;
                        known = learned;
                        PitSpeedLearned?.Invoke();
                    }
                }
            }
            else stableSince = double.MaxValue;
            r.PitSpeedKmh = known;

            // fuel going in: it rose while stopped, and holds a moment after the last rise
            var fuel = v.Number("fuel");
            if (fuel.HasValue)
            {
                if (lastFuel >= 0 && fuel.Value > lastFuel + 0.01 && v.SpeedKmh < 5) fuelRiseAt = now;
                lastFuel = fuel.Value;
            }
            r.Refuelling = now - fuelRiseAt < RefuelHoldSeconds;

            // brake bias against where the car started, shown for a moment after it moves
            // (not while braking: cars with brake migration move it themselves then, and a change made by hand is made on a straight)
            var bias = v.Number("brakeBias");
            if (bias.HasValue && bias.Value > 0)
            {
                bool braking = (v.Number("brake") ?? 0) > 3;
                if (biasRef == null) { biasRef = bias.Value; lastBias = bias.Value; }
                else if (Math.Abs(bias.Value - lastBias.Value) >= BiasStep) { if (!braking) biasChangedAt = now; lastBias = bias.Value; }
                r.BiasOffset = bias.Value - biasRef.Value;
            }
            r.BiasShown = now - biasChangedAt < BiasShowSeconds;
            return r;
        }
    }

    /// <summary>The rev bar's pictures for the extras: n LEDs, left to right. Pure, so each can be checked on its own.</summary>
    public static class RevBars
    {
        private static LedColor Off => new LedColor(0, 0, 0, 1);

        private static LedColor Lit((byte R, byte G, byte B) c, double level, byte bright) =>
            level <= 0.02 ? Off : new LedColor((byte)Math.Min(255, c.R * level), (byte)Math.Min(255, c.G * level), (byte)Math.Min(255, c.B * level), bright);

        public static readonly (byte, byte, byte) Green = (0, 255, 64), Red = (255, 0, 24), Cyan = (0, 176, 255), Amber = (255, 176, 0), Magenta = (255, 0, 255), Blue = (0, 100, 255);

        /// <summary>
        /// A soft pointer (about two LEDs wide) at `pos` (0 = the left end, 1 = the right) with a dim tick at the middle for
        /// the target. `fill` lights the LEDs from the middle to the pointer in that colour (overspeed).
        /// </summary>
        public static LedColor[] Pointer(int n, double pos, (byte R, byte G, byte B) colour, byte bright, (byte R, byte G, byte B)? fill = null)
        {
            var f = new LedColor[n];
            if (n <= 0) return f;
            pos = Math.Max(0, Math.Min(1, pos));
            double at = pos * (n - 1), mid = (n - 1) / 2.0;
            for (int i = 0; i < n; i++)
            {
                double level = Math.Max(0, Math.Min(1, 1.25 - Math.Abs(i - at)));
                var c = Lit(colour, level, bright);
                if (level <= 0.02 && fill.HasValue && i >= Math.Min(mid, at) - 1e-9 && i <= Math.Max(mid, at) + 1e-9) c = Lit(fill.Value, 0.7, bright);
                else if (level <= 0.02 && Math.Abs(i - mid) <= 0.5) c = Lit((255, 255, 255), 0.18, bright);
                f[i] = c;
            }
            return f;
        }

        /// <summary>Filled from the left end by `fraction` (0-1), the last LED partly lit.</summary>
        public static LedColor[] Fill(int n, double fraction, (byte R, byte G, byte B) colour, byte bright)
        {
            var f = new LedColor[n];
            double lit = Math.Max(0, Math.Min(1, fraction)) * n;
            for (int i = 0; i < n; i++) f[i] = Lit(colour, Math.Max(0, Math.Min(1, lit - i)), bright);
            return f;
        }

        /// <summary>Filled from both ends towards the middle by `fraction` (0-1).</summary>
        public static LedColor[] MirroredFill(int n, double fraction, (byte R, byte G, byte B) colour, byte bright)
        {
            var f = new LedColor[n];
            double reach = Math.Max(0, Math.Min(1, fraction)) * Math.Ceiling(n / 2.0);
            for (int i = 0; i < n; i++) f[i] = Lit(colour, Math.Max(0, Math.Min(1, reach - Math.Min(i, n - 1 - i))), bright);
            return f;
        }

        /// <summary>Where `value` sits against `target`: 0.5 on it, 0 / 1 at `range` below / above.</summary>
        public static double Position(double value, double target, double range) =>
            range <= 0 ? 0.5 : 0.5 + Math.Max(-1, Math.Min(1, (value - target) / range)) * 0.5;

        /// <summary>
        /// The pit speed bar: the pointer sits in the middle at the limit, to the left below it (cyan: room to go faster), green
        /// within 2 km/h of it, to the right above it (red, with the bar red from the middle to the pointer).
        /// </summary>
        public static LedColor[] PitSpeed(int n, double speedKmh, double limitKmh, byte bright)
        {
            double delta = speedKmh - limitKmh, range = Math.Max(8, limitKmh * 0.25);
            double pos = Position(speedKmh, limitKmh, range);
            if (delta > 2) return Pointer(n, pos, Red, bright, Red);
            if (delta >= -2)
            {
                var f = Pointer(n, pos, Green, bright);
                for (int i = 0; i < n; i++) if (Math.Abs(i - (n - 1) / 2.0) <= 1.01 && f[i].R + f[i].G + f[i].B == 0) f[i] = Lit(Green, 0.35, bright);
                return f;
            }
            return Pointer(n, pos, Cyan, bright);
        }

        /// <summary>The launch pointer: amber below the target, green on it, red above.</summary>
        public static LedColor[] Launch(int n, double value, double target, double range, byte bright)
        {
            double delta = value - target;
            var colour = Math.Abs(delta) <= range * 0.15 ? Green : delta < 0 ? Amber : Red;
            return Pointer(n, Position(value, target, range), colour, bright);
        }
    }
}
