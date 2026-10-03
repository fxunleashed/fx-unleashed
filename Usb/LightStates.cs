using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// What the car is doing, for the lights (docs/light-states-plan.md). One detector for every preset; each preset only
    /// says how it looks in each state (StateLook) on top of its theme.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CarState
    {
        /// <summary>No game running (or no data from SimHub).</summary>
        Idle,
        /// <summary>Game running, but in a menu, paused, a replay or spectating.</summary>
        Menu,
        /// <summary>In the car, engine not running.</summary>
        EngineOff,
        /// <summary>The engine just started (CarStateTracker.StartSeconds): the start-up animation.</summary>
        Starting,
        Driving,
        /// <summary>Driving with the pit limiter on.</summary>
        PitLimiter,
        /// <summary>The engine just went off after driving (CarStateTracker.StopSeconds): the shutdown animation.</summary>
        Stopping,
    }

    /// <summary>How a preset looks in a state where the car isn't being driven (Idle, Menu, Engine off).</summary>
    public class StateLook
    {
        /// <summary>% of the theme's brightness (0 = dark).</summary>
        public int Brightness = 100;
        /// <summary>One effect for every group instead of each group's own (null = the theme as it is).</summary>
        public LightEffect? Effect;
        /// <summary>With Effect: its colours (null or empty = each group's own).</summary>
        public List<string> Colors;
        /// <summary>With Effect: seconds per cycle.</summary>
        public double Period = 6;
        /// <summary>The rev bar joins in (runs the look's effect, or the buttons' when there's none); off = dark.</summary>
        public bool RevBar;

        public StateLook Clone() { var c = (StateLook)MemberwiseClone(); c.Colors = Colors?.ToList(); return c; }

        /// <summary>A state's look when the preset doesn't set one: idle as the theme (as before), dimmer in menus, nearly
        /// dark with the engine off.</summary>
        public static StateLook Default(CarState s)
        {
            switch (s)
            {
                case CarState.Menu: return new StateLook { Brightness = 55 };
                case CarState.EngineOff: return new StateLook { Brightness = 18 };
                default: return new StateLook();
            }
        }
    }

    /// <summary>The engine-start animation (CarState.Starting), over everything but alerts.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum StartupStyle
    {
        None,
        /// <summary>A white wave runs out from the middle of the rev bar and through every group, then settles into the theme.</summary>
        Sweep,
        /// <summary>The rev bar fills left to right like a self-test, flashes, and the theme fades in.</summary>
        SelfTest,
        /// <summary>Everything flashes white twice, then fades into the theme.</summary>
        Ignite,
    }

    /// <summary>The engine-off animation (CarState.Stopping).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ShutdownStyle
    {
        None,
        /// <summary>The lights fade out.</summary>
        Fade,
        /// <summary>Every group and the rev bar close in to their middle and go out.</summary>
        Collapse,
    }

    /// <summary>What the pit limiter shows (per preset, or saved per car: UsbSettings.CarLimiters).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LimiterStyle
    {
        /// <summary>No pit limiter lights (the rev bar as usual).</summary>
        None,
        /// <summary>The two halves take turns.</summary>
        Alternate,
        /// <summary>Both ends of the bar flash, the middle stays dark.</summary>
        Ends,
        /// <summary>The whole bar flashes.</summary>
        Blink,
        /// <summary>A band of light runs across the bar.</summary>
        Sweep,
        /// <summary>A light bounces from end to end.</summary>
        Chase,
        /// <summary>The bar fills from the middle out and empties again.</summary>
        CentreFill,
        /// <summary>Every other light, swapping (chequered).</summary>
        Checker,
        /// <summary>The whole bar steady.</summary>
        Solid,
        /// <summary>The car's own pattern from the game's dash (Pattern: a colour per rev LED; CarLightsDatabase).</summary>
        CarPattern,
    }

    public class LimiterLook
    {
        public LimiterStyle Style = LimiterStyle.Alternate;
        /// <summary>First colour, and the second one styles that use two (Alternate, Checker; else unused). "#000000" = dark.</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)] // saved list replaces the default (not appended to it)
        public List<string> Colors = new List<string> { "#0040FF", "#000000" };
        /// <summary>Cycles per second.</summary>
        public double Hz = 3;
        /// <summary>Over every light on the wheel, not just the rev bar.</summary>
        public bool WholeWheel;
        /// <summary>CarPattern: the colour of each of the wheel's rev LEDs, left to right ("#000000" = dark).</summary>
        public List<string> Pattern;
        /// <summary>Taken from the game's data for the car (not saved by the user).</summary>
        public bool FromGame;

        public LimiterLook Clone()
        {
            var c = (LimiterLook)MemberwiseClone();
            c.Colors = (Colors ?? new List<string>()).ToList();
            c.Pattern = Pattern?.ToList();
            return c;
        }

        public static string StyleName(LimiterStyle s)
        {
            switch (s)
            {
                case LimiterStyle.None: return "No limiter lights";
                case LimiterStyle.Alternate: return "Halves take turns";
                case LimiterStyle.Ends: return "Both ends flash";
                case LimiterStyle.Blink: return "Whole bar flashes";
                case LimiterStyle.Sweep: return "Band sweeps across";
                case LimiterStyle.Chase: return "Light bounces end to end";
                case LimiterStyle.CentreFill: return "Fills from the middle";
                case LimiterStyle.Checker: return "Chequered, swapping";
                case LimiterStyle.CarPattern: return "The car's own (from the game)";
                default: return "Steady";
            }
        }

        /// <summary>
        /// How lit (0-1) LED `i` of `n` is at `now`, and whether it takes the second colour. Position runs left to right.
        /// </summary>
        public (double Level, bool Second) At(int i, int n, double now)
        {
            double hz = Math.Max(0.2, Hz);
            double cycle = now * hz;
            int phase = (int)Math.Floor(cycle * 2) & 1;
            double x = n <= 1 ? 0.5 : (double)i / (n - 1);
            switch (Style)
            {
                case LimiterStyle.Alternate: return (1, (x < 0.5) == (phase == 1));
                case LimiterStyle.Ends: return (phase == 0 && (x < 0.27 || x > 0.73) ? 1 : 0, false);
                case LimiterStyle.Blink: return (phase == 0 ? 1 : 0, false);
                case LimiterStyle.Sweep:
                {
                    double head = (cycle % 1) * 1.4 - 0.2;
                    return (Math.Max(0, 1 - Math.Abs(x - head) / 0.18), false);
                }
                case LimiterStyle.Chase:
                {
                    double t = cycle % 2, head = t < 1 ? t : 2 - t;
                    return (Math.Max(0, 1 - Math.Abs(x - head) * n / 1.6), false);
                }
                case LimiterStyle.CentreFill:
                {
                    double t = cycle % 1, reach = t < 0.5 ? t * 2 : 2 - t * 2;
                    return (Math.Abs(x - 0.5) * 2 <= reach + 0.001 ? 1 : 0, false);
                }
                case LimiterStyle.Checker: return (1, ((i + phase) & 1) == 1);
                case LimiterStyle.Solid:
                case LimiterStyle.CarPattern: return (1, false);
                default: return (0, false);
            }
        }
    }

    /// <summary>A moment for the lights engine: the car's state, how far into a start-up / shutdown, the car's own limiter.</summary>
    public sealed class LightMoment
    {
        public CarState State = CarState.Driving;
        /// <summary>Starting / Stopping: 0 at the start of the animation, 1 at its end.</summary>
        public double Progress;
        /// <summary>This car's pit limiter lights (UsbSettings.CarLimiters), or null = the preset's.</summary>
        public LimiterLook CarLimiter;
        /// <summary>The facts the rev bar extras need (RevExtrasState), or null = none.</summary>
        public RevExtrasInput Extras;

        public static LightMoment Of(CarState s, double progress = 0, LimiterLook car = null) => new LightMoment { State = s, Progress = progress, CarLimiter = car };
    }

    /// <summary>
    /// Works out the car's state every frame from DashValues (the only place with the rules; docs/light-states-plan.md).
    /// The engine flag is taken as it is on the first frame in the car, then must hold for Debounce before it counts, so
    /// a stumble at idle doesn't trigger the shutdown animation. Starting and Stopping only happen on a real change while
    /// in the car: joining a session with the engine already running goes straight to Driving.
    /// </summary>
    public sealed class CarStateTracker
    {
        public const double StartSeconds = 1.6, StopSeconds = 1.4, Debounce = 0.4;

        private CarState state = CarState.Idle;
        private double since;
        private bool engine, engineRaw;
        private double engineChanged = double.MinValue;

        public CarState State => state;

        /// <summary>Starting / Stopping: how far into the animation (0-1); else 0.</summary>
        public double Progress(double now) =>
            state == CarState.Starting ? Math.Min(1, (now - since) / StartSeconds)
          : state == CarState.Stopping ? Math.Min(1, (now - since) / StopSeconds) : 0;

        public CarState Update(DashValues v, double now)
        {
            CarState want;
            if (v == null || !v.Running) want = CarState.Idle;
            else if (v.InMenu) want = CarState.Menu;
            else
            {
                bool inCar = state != CarState.Idle && state != CarState.Menu;
                if (v.EngineOn != engineRaw) { engineRaw = v.EngineOn; engineChanged = now; }
                if (!inCar) engine = engineRaw;
                else if (engineRaw != engine && now - engineChanged >= Debounce) engine = engineRaw;

                if (engine)
                {
                    if (state == CarState.Starting && now - since < StartSeconds) want = CarState.Starting;
                    else if (inCar && (state == CarState.EngineOff || state == CarState.Stopping)) want = CarState.Starting;
                    else want = v.PitLimiter ? CarState.PitLimiter : CarState.Driving;
                }
                else
                {
                    if (state == CarState.Stopping && now - since < StopSeconds) want = CarState.Stopping;
                    else if (state == CarState.Driving || state == CarState.PitLimiter || state == CarState.Starting) want = CarState.Stopping;
                    else want = CarState.EngineOff;
                }
            }
            if (want != state) { state = want; since = now; }
            return state;
        }

        public static string Name(CarState s)
        {
            switch (s)
            {
                case CarState.Idle: return "No game";
                case CarState.Menu: return "In a menu";
                case CarState.EngineOff: return "Engine off";
                case CarState.Starting: return "Engine start";
                case CarState.PitLimiter: return "Pit limiter";
                case CarState.Stopping: return "Engine stop";
                default: return "Driving";
            }
        }
    }
}
