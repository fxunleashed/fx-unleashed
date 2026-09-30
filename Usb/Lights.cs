using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>A wheel's LEDs in groups; which LEDs each is on which wheel comes from its WheelModel.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LedGroup { Buttons, Encoders, SideLeft, SideRight, Rev }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum LightEffect
    {
        Off,
        /// <summary>Steady colours (several colours alternate along the group).</summary>
        Solid,
        /// <summary>Fades in and out; with several colours, each breath takes the next colour.</summary>
        Breathe,
        /// <summary>A gradient of the colours flowing along the group.</summary>
        Wave,
        /// <summary>Every hue flowing along the group.</summary>
        Rainbow,
        /// <summary>Rainbow flowing along the group while breathing.</summary>
        RainbowBreathe,
        /// <summary>A light sweeping back and forth with a fading trail.</summary>
        Scanner,
        /// <summary>Random LEDs glinting over a dim base.</summary>
        Sparkle,
        /// <summary>Rev lights: fill with RPM (the car's real lights when known), flash at the shift point.</summary>
        Rpm,
        /// <summary>Encoders: each shows its own setting (ABS, TC, BB, DIFF, MAP), coloured low to high.</summary>
        Levels,
        /// <summary>A bright head with a fading tail running along the group and round again (first colour head, last tail).</summary>
        Comet,
        /// <summary>A lub-dub double pulse, once per cycle.</summary>
        Heartbeat,
        /// <summary>Flickering flames: the colours from coolest to hottest (e.g. dark red, orange, yellow).</summary>
        Fire,
        /// <summary>Stars: a dim base (first colour) with lights glinting on and off in the others (white if there's one colour).</summary>
        Twinkle,
        /// <summary>Waves spreading out from the middle of the group.</summary>
        Ripple,
        /// <summary>Colours melting into each other like a plasma lamp.</summary>
        Plasma,
        /// <summary>Emergency-light strobes: the two halves double-flash in turn (first colour left, second right).</summary>
        Strobe,
    }

    /// <summary>Order doesn't matter (saved by name); new triggers go at the end.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AlertTrigger
    {
        Abs, Tc, PitLimiter, BlueFlag, YellowFlag, LowFuel, Drs,
        WhiteFlag, GreenFlag, CheckeredFlag, BlackFlag, OrangeFlag, SpotterLeft, SpotterRight, RevLimiter, InvalidLap, Stalled,
        /// <summary>Any SimHub property or formula (AlertRule.Condition).</summary>
        Custom,
    }

    /// <summary>How an alert shows on its lights.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AlertStyle
    {
        /// <summary>All its lights blink together (BlinkHz; 0 = steady).</summary>
        Flash,
        /// <summary>Fades in and out, BlinkHz times a second.</summary>
        Pulse,
        /// <summary>A band of light running along its lights.</summary>
        Sweep,
        /// <summary>Every other light, swapping BlinkHz times a second (a chequered flag).</summary>
        Checker,
    }

    /// <summary>How a ring gauge shows its value.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum GaugeStyle
    {
        /// <summary>Fills clockwise from 12 o'clock, low to high.</summary>
        Fill,
        /// <summary>One bright segment where the value is (brake bias, a dial).</summary>
        Pointer,
    }

    /// <summary>
    /// What one encoder ring shows as a gauge with the Levels effect (GT Neo; WheelModel.GaugeRings): a built-in value or
    /// any SimHub property / formula, over a range.
    /// </summary>
    public class RingGauge
    {
        /// <summary>A built-in key (see Sources) or, for Custom, a SimHub property / formula as AlertRule.Condition.</summary>
        public string Source = "tcLevel";
        public double Low = 1, High = 11;
        public GaugeStyle Style = GaugeStyle.Fill;

        public const string Custom = "custom";

        /// <summary>The built-in values: key, name, range, style, and whether 0 means the car hasn't got it (a dim ring).</summary>
        public static readonly (string Key, string Name, double Low, double High, GaugeStyle Style, bool ZeroIsOff)[] Sources =
        {
            ("tcLevel", "TC", 1, 11, GaugeStyle.Fill, true), ("absLevel", "ABS", 1, 11, GaugeStyle.Fill, true),
            ("brakeBias", "Brake bias", 50, 64, GaugeStyle.Pointer, false), ("engineMap", "Engine map", 1, 10, GaugeStyle.Fill, true),
            ("fuelPercent", "Fuel left", 0, 100, GaugeStyle.Fill, false), ("rpmPercent", "Revs", 0, 100, GaugeStyle.Fill, false),
            ("throttle", "Throttle", 0, 100, GaugeStyle.Fill, false), ("brake", "Brake pedal", 0, 100, GaugeStyle.Fill, false),
        };

        /// <summary>A built-in value as a gauge, with its usual range and style.</summary>
        public static RingGauge Of(string key)
        {
            foreach (var s in Sources) if (s.Key == key) return new RingGauge { Source = key, Low = s.Low, High = s.High, Style = s.Style };
            return new RingGauge { Source = key, Low = 0, High = 100 };
        }

        /// <summary>The four rings' defaults (upper left, upper right, lower left, lower right).</summary>
        public static List<RingGauge> Defaults() => new List<RingGauge> { Of("tcLevel"), Of("absLevel"), Of("brakeBias"), Of("engineMap") };

        [JsonIgnore]
        public bool IsCustom => !Sources.Any(s => s.Key == Source);

        /// <summary>What DashValues is asked for: the key, or the custom text as a binding.</summary>
        [JsonIgnore]
        public string Bind => IsCustom ? AlertRule.Bind(Source) : Source;

        [JsonIgnore]
        public bool ZeroIsOff => Sources.Any(s => s.Key == Source && s.ZeroIsOff);

        public RingGauge Clone() => (RingGauge)MemberwiseClone();
    }

    public class GroupLighting
    {
        public LightEffect Effect = LightEffect.Solid;
        public List<string> Colors = new List<string> { "#FFFFFF" };
        /// <summary>Seconds per breath / wave / sweep.</summary>
        public double Period = 4;
        /// <summary>% of the profile's brightness.</summary>
        public int Brightness = 100;
        /// <summary>Levels: blink an encoder's light for a second when its setting changes.</summary>
        public bool FlashOnChange = true;
        /// <summary>Levels: what the DIFF encoder shows (no standard SimHub value): a SimHub property or formula, as AlertRule.Condition.</summary>
        public string DiffSource;
        /// <summary>Groups in several segments (the GT Neo's rings): each one a step behind the last, so they chase each other.</summary>
        public bool Stagger;
        /// <summary>Levels on ring encoders (GT Neo): what each ring shows, in ring order (missing = RingGauge.Defaults).</summary>
        public List<RingGauge> Gauges;

        /// <summary>Ring `k`'s gauge (its own, else the default for that ring).</summary>
        public RingGauge GaugeFor(int k) => Gauges != null && k < Gauges.Count && Gauges[k] != null ? Gauges[k] : RingGauge.Defaults()[Math.Min(k, 3)];

        public GroupLighting Clone() { var c = (GroupLighting)MemberwiseClone(); c.Colors = new List<string>(Colors); c.Gauges = Gauges?.Select(g => g?.Clone()).ToList(); return c; }
    }

    public class AlertRule
    {
        public AlertTrigger Trigger;
        public bool Enabled = true;
        public string Color = "#FFB000";
        public List<LedGroup> Groups = new List<LedGroup>();
        /// <summary>Blinks per second (0 = steady).</summary>
        public double BlinkHz = 10;
        public AlertStyle Style = AlertStyle.Flash;
        /// <summary>Custom alerts: the name shown in the list.</summary>
        public string Name;
        /// <summary>
        /// Custom alerts: on while this is true (true, a number other than 0, a text other than "" / "0" / "false").
        /// A SimHub property ("DataCorePlugin.GameData.NewData.CarDamagesMax"), an NCalc formula ("[CarDamagesMax] > 5"),
        /// "js:..." or one of the dash keys ("absActive"). See Bind.
        /// </summary>
        public string Condition;

        public AlertRule Clone() { var c = (AlertRule)MemberwiseClone(); c.Groups = new List<LedGroup>(Groups); return c; }

        /// <summary>The DashValues key a custom condition is read from ("prop:"/"ncalc:"/"js:" or a dash key); null = none.</summary>
        [JsonIgnore]
        public string ConditionBind => Trigger == AlertTrigger.Custom ? Bind(Condition) : null;

        /// <summary>
        /// A condition as typed, as a binding: dash keys and "prop:"/"ncalc:"/"js:" as they are, a bare property path
        /// ("DataCorePlugin.X.Y", no spaces or operators) as "prop:", anything else as an NCalc formula.
        /// </summary>
        public static string Bind(string condition)
        {
            var c = condition?.Trim();
            if (string.IsNullOrEmpty(c)) return null;
            if (c.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(c) || DashValues.KnownKey(c)) return c;
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"^[A-Za-z_][\w]*(\.[\w]+)+$")) return "prop:" + c;
            return "ncalc:" + c;
        }
    }

    public class RevLighting
    {
        /// <summary>Use the car's real shift lights (rev light database / per-car override) when the plugin knows them.</summary>
        public bool UseCarData = true;
        /// <summary>Otherwise: first LED at this % of the shift point, colours by thirds.</summary>
        public double StartPercent = 75;
        public List<string> Colors = new List<string> { "#00FF40", "#FFB000", "#FF0020" };
        public string FlashColor = "#0040FF";
        /// <summary>Blinks per second at the shift point (0 = steady).</summary>
        public double FlashHz = 8;
        /// <summary>How the rev lights fill when the car's real lights aren't used (no data, or UseCarData off).</summary>
        public PatternKind Pattern = PatternKind.LeftToRight;

        public RevLighting Clone() { var c = (RevLighting)MemberwiseClone(); c.Colors = new List<string>(Colors); return c; }

        /// <summary>
        /// The 15 rev LEDs for this pattern: each lights at a fraction of the shift point (from StartPercent to 100% in
        /// the pattern's order), coloured by where it falls in that order (the colours split it evenly, low to high).
        /// </summary>
        public LedLayout Layout()
        {
            var ranks = LedPatterns.Ranks(Pattern == PatternKind.SimProPreset ? PatternKind.LeftToRight : Pattern);
            var cols = Colors != null && Colors.Count > 0 ? Colors : new List<string> { "#00FF40" };
            double start = Math.Max(0.1, Math.Min(0.99, StartPercent / 100));
            var layout = new LedLayout { FlashColor = FlashColor, FlashBlinks = FlashHz > 0 };
            for (int i = 0; i < ranks.Length; i++)
            {
                if (ranks[i] < 0) { layout.Fractions[i] = 0; layout.Colors[i] = "#000000"; continue; }
                layout.Fractions[i] = start + (1 - start) * ranks[i];
                layout.Colors[i] = cols[Math.Min(cols.Count - 1, (int)(ranks[i] * cols.Count))];
            }
            return layout;
        }

        /// <summary>The layout in real RPM for a shift point (for the plugin's per-car lights and overrides).</summary>
        public RpmLayout ForShift(double shiftRpm)
        {
            int units = FlashHz > 0 ? Math.Max(1, (int)Math.Round(1000 / (2 * FlashHz * RpmLightsMapper.BlinkMsPerUnit))) : 0;
            return RpmLayout.FromPattern(Layout(), shiftRpm, units);
        }
    }

    /// <summary>Everything the lights do in USB mode. Presets are profiles; "Customize" edits a copy.</summary>
    public class LightProfile
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>LED brightness 1-90 (90 = the most the firmware drives an LED).</summary>
        public int MaxBrightness = 90;
        public Dictionary<LedGroup, GroupLighting> Groups = new Dictionary<LedGroup, GroupLighting>();
        public RevLighting Rev = new RevLighting();
        /// <summary>First active alert wins for a LED.</summary>
        public List<AlertRule> Alerts = new List<AlertRule>();

        // ---------- per car state (docs/light-states-plan.md) ----------

        /// <summary>How it looks with no game, in a menu, with the engine off (missing = StateLook.Default).</summary>
        public Dictionary<CarState, StateLook> Looks = new Dictionary<CarState, StateLook>();
        public StartupStyle Startup = StartupStyle.Sweep;
        public ShutdownStyle Shutdown = ShutdownStyle.Fade;
        /// <summary>The pit limiter lights (a car's own, saved in UsbSettings.CarLimiters, come first).</summary>
        public LimiterLook Limiter = new LimiterLook();
        /// <summary>While driving, the rev bar's unlit LEDs glow in the theme's colour at this % (0 = dark, as before).</summary>
        public int RevTint;

        public StateLook LookFor(CarState s) => Looks != null && Looks.TryGetValue(s, out var l) && l != null ? l : StateLook.Default(s);

        public GroupLighting Group(LedGroup g) => Groups.TryGetValue(g, out var l) ? l : (Groups[g] = new GroupLighting { Effect = LightEffect.Off });

        /// <summary>SimHub properties / formulas these lights read (custom alerts, the DIFF encoder), for DataUpdate.</summary>
        public IEnumerable<string> Bindings()
        {
            foreach (var a in Alerts ?? new List<AlertRule>())
                if (a.Enabled && a.ConditionBind != null) yield return a.ConditionBind;
            if (Groups != null && Groups.TryGetValue(LedGroup.Encoders, out var enc) && enc.Effect == LightEffect.Levels)
            {
                var diff = AlertRule.Bind(enc.DiffSource);
                if (diff != null) yield return diff;
                foreach (var g in enc.Gauges ?? new List<RingGauge>())
                    if (g != null && g.IsCustom && g.Bind != null) yield return g.Bind;
            }
        }

        /// <summary>
        /// Adds the built-in alerts this profile doesn't have yet (saved before they existed), switched off, at the end,
        /// so the list always offers all of them. True if it added any.
        /// </summary>
        public bool AddMissingAlerts()
        {
            if (Alerts == null) Alerts = new List<AlertRule>();
            bool added = false;
            foreach (var d in LightPresets.DefaultAlerts())
                if (!Alerts.Any(a => a.Trigger == d.Trigger))
                {
                    d.Enabled = false;
                    Alerts.Add(d);
                    added = true;
                }
            return added;
        }

        public LightProfile Clone()
        {
            var c = (LightProfile)MemberwiseClone();
            c.Groups = Groups.ToDictionary(k => k.Key, k => k.Value.Clone());
            c.Rev = Rev.Clone();
            c.Alerts = Alerts.Select(a => a.Clone()).ToList();
            c.Looks = (Looks ?? new Dictionary<CarState, StateLook>()).ToDictionary(k => k.Key, k => k.Value?.Clone());
            c.Limiter = (Limiter ?? new LimiterLook()).Clone();
            return c;
        }
    }

    public static class LightPresets
    {
        public const string CustomId = "custom";

        public static string NewUserId() => "user-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        public static bool IsBuiltIn(string id) => All.Any(p => p.Id == id);

        /// <summary>The built-in presets for a wheel (the gallery, the first-time default), in order.</summary>
        public static LightProfile[] For(WheelModel m) => m == WheelModel.GtNeo ? GtNeo : FxPro;

        /// <summary>Every built-in preset, both wheels (ids are unique across them).</summary>
        public static readonly LightProfile[] All;

        static LightPresets() { All = FxPro.Concat(GtNeo).ToArray(); }

        // Order: the newest, most striking first (the first one is a new user's default), then the classics.
        public static readonly LightProfile[] FxPro =
        {
            Make("neon-tokyo", "Neon Tokyo", "Rain-soaked Shibuya at 2 a.m.: magenta and cyan plasma melting across the buttons, city lights twinkling in the encoders, neon signs buzzing on the sides.",
                (LedGroup.Buttons, LightEffect.Plasma, 7, 100, new[] { "#FF2E97", "#00F0FF", "#7B2FFF" }),
                (LedGroup.Encoders, LightEffect.Twinkle, 5, 100, new[] { "#1A0033", "#00F0FF", "#FF2E97" }),
                (LedGroup.SideLeft, LightEffect.Heartbeat, 2.2, 100, new[] { "#FF2E97" }),
                (LedGroup.SideRight, LightEffect.Heartbeat, 2.6, 100, new[] { "#00F0FF" }))
                .Parked(CarState.Idle, 80, LightEffect.Plasma, new[] { "#FF2E97", "#00F0FF", "#7B2FFF" }, 16, rev: true)
                .Parked(CarState.EngineOff, 22, LightEffect.Twinkle, new[] { "#1A0033", "#00F0FF", "#FF2E97" }, 6)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Collapse).Limit(LimiterStyle.Checker, "#FF2E97", "#00F0FF", 4).TintRev(6),
            Make("hyperspace", "Hyperspace", "Punch it: white star streaks racing out of every group into a violet warp. A starfield while you wait, and a hyperdrive spool-up every time the engine fires.",
                (LedGroup.Buttons, LightEffect.Comet, 0.9, 100, new[] { "#FFFFFF", "#3A1CFF" }),
                (LedGroup.Encoders, LightEffect.Twinkle, 3, 100, new[] { "#0A0030", "#FFFFFF", "#B0A0FF" }),
                (LedGroup.SideLeft, LightEffect.Comet, 0.6, 100, new[] { "#FFFFFF", "#6040FF" }),
                (LedGroup.SideRight, LightEffect.Comet, 0.6, 100, new[] { "#FFFFFF", "#6040FF" }))
                .Parked(CarState.Idle, 75, LightEffect.Twinkle, new[] { "#05001A", "#FFFFFF", "#B0A0FF" }, 7, rev: true)
                .Parked(CarState.EngineOff, 20, LightEffect.Twinkle, new[] { "#05001A", "#FFFFFF" }, 9)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Collapse).Limit(LimiterStyle.Chase, "#B0A0FF", hz: 1.5),
            Make("le-mans-night", "Le Mans Night", "Three in the morning on the Mulsanne: warm headlight white on the buttons, amber glowing in the encoders, red tail lights breathing at the sides. Parks with its sidelights on.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 55, new[] { "#FFE3A8" }),
                (LedGroup.Encoders, LightEffect.Breathe, 8, 70, new[] { "#FF9A1F" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 3, 85, new[] { "#FF1010" }),
                (LedGroup.SideRight, LightEffect.Breathe, 3, 85, new[] { "#FF1010" }))
                .Parked(CarState.Idle, 70, LightEffect.Comet, new[] { "#FFE3A8", "#3A2410" }, 5, rev: true)
                .Parked(CarState.EngineOff, 14, LightEffect.Solid, new[] { "#FF9A1F" })
                .Motion(StartupStyle.Ignite, ShutdownStyle.Fade).Limit(LimiterStyle.Ends, "#FFB000", hz: 2).TintRev(5),
            Make("inferno", "Inferno", "Every light a live flame: flickering reds and oranges on the buttons, coals glowing in the encoders, the sides beating like an engine. Starting up lights the fuse; switching off leaves embers.",
                (LedGroup.Buttons, LightEffect.Fire, 3, 100, new[] { "#3A0000", "#FF2000", "#FF8A00", "#FFD060" }),
                (LedGroup.Encoders, LightEffect.Fire, 5, 80, new[] { "#200000", "#C01000", "#FF6A00" }),
                (LedGroup.SideLeft, LightEffect.Heartbeat, 1.4, 100, new[] { "#FF2000" }),
                (LedGroup.SideRight, LightEffect.Heartbeat, 1.4, 100, new[] { "#FF2000" }))
                .Parked(CarState.Idle, 70, LightEffect.Fire, new[] { "#3A0000", "#FF2000", "#FF8A00", "#FFD060" }, 5, rev: true)
                .Parked(CarState.EngineOff, 12, LightEffect.Fire, new[] { "#200000", "#C01000", "#FF6A00" }, 7)
                .Motion(StartupStyle.Ignite, ShutdownStyle.Fade).Limit(LimiterStyle.Blink, "#FF6A00", hz: 3).TintRev(6),
            Make("abyss", "Abyss", "The deep ocean: slow teal waves rippling out from the middle, bioluminescent sparks drifting through the encoders, a jellyfish pulse at the sides.",
                (LedGroup.Buttons, LightEffect.Ripple, 9, 100, new[] { "#001A33", "#00C8FF", "#00FFC0" }),
                (LedGroup.Encoders, LightEffect.Twinkle, 7, 100, new[] { "#001018", "#00FFD0", "#66F0FF" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 5, 90, new[] { "#00B0FF", "#8040FF" }),
                (LedGroup.SideRight, LightEffect.Breathe, 5, 90, new[] { "#00B0FF", "#8040FF" }))
                .Parked(CarState.Idle, 80, LightEffect.Ripple, new[] { "#001A33", "#00C8FF", "#00FFC0" }, 14, rev: true)
                .Parked(CarState.EngineOff, 16, LightEffect.Twinkle, new[] { "#001018", "#00FFD0" }, 9)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Fade).Limit(LimiterStyle.Sweep, "#00FFD0", hz: 1.2),
            Make("heartbeat", "Heartbeat", "The wheel has a pulse. Resting at 40 beats a minute while you wait, racing at 80 once the engine runs; a defibrillator jolt on start-up and a flatline when you switch off.",
                (LedGroup.Buttons, LightEffect.Heartbeat, 0.75, 100, new[] { "#FF0030", "#FF4060" }),
                (LedGroup.Encoders, LightEffect.Heartbeat, 0.75, 70, new[] { "#FF0030" }),
                (LedGroup.SideLeft, LightEffect.Heartbeat, 0.75, 100, new[] { "#FF0030" }),
                (LedGroup.SideRight, LightEffect.Heartbeat, 0.75, 100, new[] { "#FF0030" }))
                .Parked(CarState.Idle, 60, LightEffect.Heartbeat, new[] { "#FF0030" }, 1.5, rev: true)
                .Parked(CarState.EngineOff, 20, LightEffect.Heartbeat, new[] { "#FF0030" }, 1.2)
                .Motion(StartupStyle.Ignite, ShutdownStyle.Collapse).Limit(LimiterStyle.Alternate, "#FF0030", hz: 2),
            Make("race-engineer", "Race Engineer", "All business: each encoder shows its setting (ABS, TC, brake bias, DIFF, map) from green to red, flashing when you change it; calm white everywhere else.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 30, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Levels, 4, 100, new[] { "#00FF40", "#FFB000", "#FF0020" }),
                (LedGroup.SideLeft, LightEffect.Solid, 4, 20, new[] { "#FFFFFF" }),
                (LedGroup.SideRight, LightEffect.Solid, 4, 20, new[] { "#FFFFFF" }))
                .Parked(CarState.Idle, 40, LightEffect.Breathe, new[] { "#FFFFFF" }, 8).Motion(StartupStyle.SelfTest, ShutdownStyle.Fade),
            Make("aurora", "Aurora", "Teal, blue and violet drifting like northern lights; the side lights breathe teal.",
                (LedGroup.Buttons, LightEffect.Wave, 9, 100, new[] { "#00FFA3", "#00B3FF", "#7B2FFF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 6, 100, new[] { "#7B2FFF", "#00FFA3" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 6, 90, new[] { "#00FFA3" }),
                (LedGroup.SideRight, LightEffect.Breathe, 6, 90, new[] { "#00FFA3" }))
                .Parked(CarState.Idle, 85, LightEffect.Wave, new[] { "#00FFA3", "#00B3FF", "#7B2FFF" }, 14, rev: true),
            Make("synthwave", "Synthwave", "Hot pink, purple and cyan flowing over the buttons, neon side lights.",
                (LedGroup.Buttons, LightEffect.Wave, 6, 100, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 4, 100, new[] { "#FF2E97", "#00F0FF" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 3, 100, new[] { "#FF2E97" }),
                (LedGroup.SideRight, LightEffect.Breathe, 3, 100, new[] { "#00F0FF" }))
                .Parked(CarState.Idle, 85, LightEffect.Wave, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }, 10, rev: true),
            Make("ember", "Ember", "Glowing embers: slow orange and red breathing with the odd spark.",
                (LedGroup.Buttons, LightEffect.Sparkle, 5, 100, new[] { "#FF3D00", "#FFB000" }),
                (LedGroup.Encoders, LightEffect.Breathe, 5, 100, new[] { "#FF5A00", "#FF1E00" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 7, 80, new[] { "#FF1E00" }),
                (LedGroup.SideRight, LightEffect.Breathe, 7, 80, new[] { "#FF1E00" }))
                .Parked(CarState.Idle, 60, LightEffect.Breathe, new[] { "#FF5A00", "#FF1E00" }, 8, rev: true),
            Make("ice", "Glacier", "Cold white and ice blue flowing slowly; calm and easy on the eyes at night.",
                (LedGroup.Buttons, LightEffect.Wave, 12, 70, new[] { "#FFFFFF", "#7FDBFF", "#0060FF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 8, 70, new[] { "#7FDBFF" }),
                (LedGroup.SideLeft, LightEffect.Solid, 4, 40, new[] { "#0060FF" }),
                (LedGroup.SideRight, LightEffect.Solid, 4, 40, new[] { "#0060FF" }))
                .Parked(CarState.Idle, 50, LightEffect.Breathe, new[] { "#7FDBFF" }, 12),
            Make("scanner", "Scanner", "A red light sweeping across the buttons, encoders breathing red. Knight Rider across the whole wheel while you wait.",
                (LedGroup.Buttons, LightEffect.Scanner, 1.6, 100, new[] { "#FF0010" }),
                (LedGroup.Encoders, LightEffect.Breathe, 3, 100, new[] { "#FF0010" }),
                (LedGroup.SideLeft, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideRight, LightEffect.Off, 4, 100, new[] { "#000000" }))
                .Parked(CarState.Idle, 100, LightEffect.Scanner, new[] { "#FF0010" }, 2, rev: true),
            Make("stealth", "Stealth", "Dim white buttons and nothing else, until something needs your attention. Completely dark with the engine off.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 18, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideLeft, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideRight, LightEffect.Off, 4, 100, new[] { "#000000" }))
                .Parked(CarState.EngineOff, 0).Motion(StartupStyle.None, ShutdownStyle.Fade).Limit(LimiterStyle.Ends, "#0040FF", hz: 2),
            Make("rainbow", "Full Rainbow", "Every light a flowing rainbow; the rev lights stay shift lights.",
                (LedGroup.Buttons, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideLeft, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideRight, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }))
                .Parked(CarState.Idle, 90, LightEffect.Rainbow, null, 8, rev: true),
        };

        /// <summary>
        /// The GT Neo's: its 10 button lights, four encoder rings of 12 (each ring runs its effect on its own, so waves
        /// and rainbows go round the rings; staggered rings chase each other) and the rev bar. Alerts for the side lights
        /// use the ends of the rev bar.
        /// </summary>
        public static readonly LightProfile[] GtNeo =
        {
            Make("neo-neon-tokyo", "Neon Tokyo", "Rain-soaked Shibuya at 2 a.m.: magenta and cyan plasma turning round the rings, one step apart, and city lights twinkling in the buttons.",
                (LedGroup.Buttons, LightEffect.Twinkle, 4, 100, new[] { "#1A0033", "#FF2E97", "#00F0FF" }),
                (LedGroup.Encoders, LightEffect.Plasma, 6, 100, new[] { "#FF2E97", "#00F0FF", "#7B2FFF" }))
                .Staggered(LedGroup.Encoders)
                .Parked(CarState.Idle, 80, LightEffect.Plasma, new[] { "#FF2E97", "#00F0FF", "#7B2FFF" }, 16, rev: true)
                .Parked(CarState.EngineOff, 22, LightEffect.Twinkle, new[] { "#1A0033", "#00F0FF", "#FF2E97" }, 6)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Collapse).Limit(LimiterStyle.Checker, "#FF2E97", "#00F0FF", 4).TintRev(6),
            Make("neo-hyperspace", "Hyperspace", "Punch it: four warp tunnels, white streaks orbiting each ring into violet, stars glinting on the buttons. A starfield while you wait, a hyperdrive spool-up on every start.",
                (LedGroup.Buttons, LightEffect.Twinkle, 3, 100, new[] { "#0A0030", "#FFFFFF", "#B0A0FF" }),
                (LedGroup.Encoders, LightEffect.Comet, 0.7, 100, new[] { "#FFFFFF", "#3A1CFF" }))
                .Staggered(LedGroup.Encoders)
                .Parked(CarState.Idle, 75, LightEffect.Twinkle, new[] { "#05001A", "#FFFFFF", "#B0A0FF" }, 7, rev: true)
                .Parked(CarState.EngineOff, 20, LightEffect.Twinkle, new[] { "#05001A", "#FFFFFF" }, 9)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Collapse).Limit(LimiterStyle.Chase, "#B0A0FF", hz: 1.5),
            Make("neo-le-mans-night", "Le Mans Night", "Three in the morning on the Mulsanne: warm headlight white on the buttons, red tail lights circling each ring like the cars ahead. Parks with its sidelights on.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 55, new[] { "#FFE3A8" }),
                (LedGroup.Encoders, LightEffect.Comet, 2.4, 90, new[] { "#FF1010", "#2A0000" }))
                .Staggered(LedGroup.Encoders)
                .Parked(CarState.Idle, 70, LightEffect.Comet, new[] { "#FFE3A8", "#3A2410" }, 5, rev: true)
                .Parked(CarState.EngineOff, 14, LightEffect.Solid, new[] { "#FF9A1F" })
                .Motion(StartupStyle.Ignite, ShutdownStyle.Fade).Limit(LimiterStyle.Ends, "#FFB000", hz: 2).TintRev(5),
            Make("neo-inferno", "Inferno", "Every light a live flame: the buttons flicker red and gold, four rings of fire burn round the encoders. Starting up lights the fuse; switching off leaves embers.",
                (LedGroup.Buttons, LightEffect.Fire, 3, 100, new[] { "#3A0000", "#FF2000", "#FF8A00", "#FFD060" }),
                (LedGroup.Encoders, LightEffect.Fire, 4, 100, new[] { "#200000", "#FF2000", "#FF8A00", "#FFD060" }))
                .Parked(CarState.Idle, 70, LightEffect.Fire, new[] { "#3A0000", "#FF2000", "#FF8A00", "#FFD060" }, 5, rev: true)
                .Parked(CarState.EngineOff, 12, LightEffect.Fire, new[] { "#200000", "#C01000", "#FF6A00" }, 7)
                .Motion(StartupStyle.Ignite, ShutdownStyle.Fade).Limit(LimiterStyle.Blink, "#FF6A00", hz: 3).TintRev(6),
            Make("neo-abyss", "Abyss", "The deep ocean: teal currents circling the rings one after another, bioluminescent sparks drifting across the buttons.",
                (LedGroup.Buttons, LightEffect.Twinkle, 7, 100, new[] { "#001018", "#00FFD0", "#66F0FF" }),
                (LedGroup.Encoders, LightEffect.Wave, 7, 100, new[] { "#001A33", "#00C8FF", "#00FFC0" }))
                .Staggered(LedGroup.Encoders)
                .Parked(CarState.Idle, 80, LightEffect.Wave, new[] { "#001A33", "#00C8FF", "#00FFC0" }, 14, rev: true)
                .Parked(CarState.EngineOff, 16, LightEffect.Twinkle, new[] { "#001018", "#00FFD0" }, 9)
                .Motion(StartupStyle.Sweep, ShutdownStyle.Fade).Limit(LimiterStyle.Sweep, "#00FFD0", hz: 1.2),
            Make("neo-heartbeat", "Heartbeat", "The wheel has a pulse. Resting at 40 beats a minute while you wait, racing at 80 once the engine runs; a defibrillator jolt on start-up and a flatline when you switch off.",
                (LedGroup.Buttons, LightEffect.Heartbeat, 0.75, 100, new[] { "#FF0030", "#FF4060" }),
                (LedGroup.Encoders, LightEffect.Heartbeat, 0.75, 100, new[] { "#FF0030" }))
                .Parked(CarState.Idle, 60, LightEffect.Heartbeat, new[] { "#FF0030" }, 1.5, rev: true)
                .Parked(CarState.EngineOff, 20, LightEffect.Heartbeat, new[] { "#FF0030" }, 1.2)
                .Motion(StartupStyle.Ignite, ShutdownStyle.Collapse).Limit(LimiterStyle.Alternate, "#FF0030", hz: 2),
            Make("neo-race-engineer", "Race Engineer", "All business: the four rings are gauges of TC, ABS, brake bias and engine map, green to red, flashing when you change them; calm white buttons.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 30, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Levels, 4, 100, new[] { "#00FF40", "#FFB000", "#FF0020" }))
                .Parked(CarState.Idle, 40, LightEffect.Breathe, new[] { "#FFFFFF" }, 8).Motion(StartupStyle.SelfTest, ShutdownStyle.Fade),
            Make("neo-aurora", "Aurora", "Teal, blue and violet drifting round the rings; the buttons breathe teal and violet.",
                (LedGroup.Buttons, LightEffect.Breathe, 6, 100, new[] { "#00FFA3", "#7B2FFF" }),
                (LedGroup.Encoders, LightEffect.Wave, 9, 100, new[] { "#00FFA3", "#00B3FF", "#7B2FFF" }))
                .Parked(CarState.Idle, 85, LightEffect.Wave, new[] { "#00FFA3", "#00B3FF", "#7B2FFF" }, 14, rev: true),
            Make("neo-synthwave", "Synthwave", "Hot pink, purple and cyan flowing round the rings, neon buttons.",
                (LedGroup.Buttons, LightEffect.Wave, 6, 100, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }),
                (LedGroup.Encoders, LightEffect.Wave, 5, 100, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }))
                .Parked(CarState.Idle, 85, LightEffect.Wave, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }, 10, rev: true),
            Make("neo-ember", "Ember", "Glowing embers: orange and red sparks on the buttons, the rings breathing red.",
                (LedGroup.Buttons, LightEffect.Sparkle, 5, 100, new[] { "#FF3D00", "#FFB000" }),
                (LedGroup.Encoders, LightEffect.Breathe, 5, 100, new[] { "#FF5A00", "#FF1E00" }))
                .Parked(CarState.Idle, 60, LightEffect.Breathe, new[] { "#FF5A00", "#FF1E00" }, 8, rev: true),
            Make("neo-ice", "Glacier", "Cold white and ice blue flowing slowly round the rings; calm at night.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 50, new[] { "#7FDBFF" }),
                (LedGroup.Encoders, LightEffect.Wave, 12, 70, new[] { "#FFFFFF", "#7FDBFF", "#0060FF" }))
                .Parked(CarState.Idle, 50, LightEffect.Breathe, new[] { "#7FDBFF" }, 12),
            Make("neo-chaser", "Chaser", "A red light chasing round each ring, the buttons dim red. Knight Rider across the whole wheel while you wait.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 25, new[] { "#FF0010" }),
                (LedGroup.Encoders, LightEffect.Scanner, 1.2, 100, new[] { "#FF0010" }))
                .Parked(CarState.Idle, 100, LightEffect.Scanner, new[] { "#FF0010" }, 2, rev: true),
            Make("neo-stealth", "Stealth", "Dim white buttons and nothing else, until something needs your attention. Completely dark with the engine off.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 18, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Off, 4, 100, new[] { "#000000" }))
                .Parked(CarState.EngineOff, 0).Motion(StartupStyle.None, ShutdownStyle.Fade).Limit(LimiterStyle.Ends, "#0040FF", hz: 2),
            Make("neo-rainbow", "Full Rainbow", "Every light a flowing rainbow; the rev lights stay shift lights.",
                (LedGroup.Buttons, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Rainbow, 3, 100, new[] { "#FFFFFF" }))
                .Parked(CarState.Idle, 90, LightEffect.Rainbow, null, 8, rev: true),
        };

        /// <summary>Old preset ids and what they became: Prism was replaced (too close to Full Rainbow).</summary>
        private static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>
        {
            ["mustang"] = "rainbow", ["neo-prism"] = "neo-rainbow",
        };

        /// <summary>A preset id as it is now (a replaced one's successor; anything else unchanged).</summary>
        public static string CurrentId(string id) => id != null && Renamed.TryGetValue(id, out var to) ? to : id;

        public static LightProfile Find(string id) =>
            id == null ? null : All.FirstOrDefault(p => p.Id == id) ?? (Renamed.TryGetValue(id, out var to) ? All.FirstOrDefault(p => p.Id == to) : null);

        // ---------- building the presets ----------

        /// <summary>How it looks in a parked state (no game, menu, engine off).</summary>
        private static LightProfile Parked(this LightProfile p, CarState s, int brightness, LightEffect? effect = null, string[] colours = null, double period = 6, bool rev = false)
        {
            p.Looks[s] = new StateLook { Brightness = brightness, Effect = effect, Colors = colours?.ToList(), Period = period, RevBar = rev };
            return p;
        }

        private static LightProfile Motion(this LightProfile p, StartupStyle start, ShutdownStyle stop) { p.Startup = start; p.Shutdown = stop; return p; }

        private static LightProfile Limit(this LightProfile p, LimiterStyle style, string colour, string second = "#000000", double hz = 3, bool wholeWheel = false)
        {
            p.Limiter = new LimiterLook { Style = style, Colors = new List<string> { colour, second }, Hz = hz, WholeWheel = wholeWheel };
            return p;
        }

        private static LightProfile TintRev(this LightProfile p, int percent) { p.RevTint = percent; return p; }

        private static LightProfile Staggered(this LightProfile p, LedGroup g) { p.Group(g).Stagger = true; return p; }

        private static LightProfile Make(string id, string name, string description,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) a,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) b,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) c,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) d) => Make(id, name, description, new[] { a, b, c, d });

        private static LightProfile Make(string id, string name, string description,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) a,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) b) => Make(id, name, description, new[] { a, b });

        /// <summary>
        /// A built-in preset. Its rev lights always keep the standard shift colours (RevLighting's green, amber, red and blue
        /// flash): drivers are used to them, so presets only style the other lights. A duplicate can change them.
        /// </summary>
        private static LightProfile Make(string id, string name, string description,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors)[] groups)
        {
            var p = new LightProfile { Id = id, Name = name, Description = description };
            foreach (var g in groups)
                p.Groups[g.G] = new GroupLighting { Effect = g.E, Period = g.Period, Brightness = g.Brightness, Colors = g.Colors.ToList() };
            p.Groups[LedGroup.Rev] = new GroupLighting { Effect = LightEffect.Rpm };
            p.Alerts = DefaultAlerts();
            return p;
        }

        /// <summary>
        /// Spotter on the side lights (a car beside you beats ABS/TC there), ABS on the left side lights, TC on the
        /// right, pit limiter on the rev lights, flags on the encoders. The first active alert wins a light.
        /// </summary>
        public static List<AlertRule> DefaultAlerts() => new List<AlertRule>
        {
            new AlertRule { Trigger = AlertTrigger.SpotterLeft, Color = "#FF5000", BlinkHz = 0, Groups = { LedGroup.SideLeft } },
            new AlertRule { Trigger = AlertTrigger.SpotterRight, Color = "#FF5000", BlinkHz = 0, Groups = { LedGroup.SideRight } },
            new AlertRule { Trigger = AlertTrigger.Abs, Color = "#FFB000", BlinkHz = 12, Groups = { LedGroup.SideLeft } },
            new AlertRule { Trigger = AlertTrigger.Tc, Color = "#00A0FF", BlinkHz = 12, Groups = { LedGroup.SideRight } },
            new AlertRule { Trigger = AlertTrigger.PitLimiter, Color = "#0040FF", BlinkHz = 3, Groups = { LedGroup.Rev } },
            new AlertRule { Trigger = AlertTrigger.BlackFlag, Color = "#FF0020", BlinkHz = 4, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.OrangeFlag, Color = "#FF6000", BlinkHz = 2, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.BlueFlag, Color = "#0040FF", BlinkHz = 2, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.YellowFlag, Color = "#FFD000", BlinkHz = 2, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.CheckeredFlag, Color = "#FFFFFF", BlinkHz = 3, Style = AlertStyle.Checker, Groups = { LedGroup.Encoders, LedGroup.Rev } },
            new AlertRule { Trigger = AlertTrigger.WhiteFlag, Color = "#FFFFFF", BlinkHz = 1, Style = AlertStyle.Pulse, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.GreenFlag, Color = "#00FF40", BlinkHz = 2, Style = AlertStyle.Sweep, Enabled = false, Groups = { LedGroup.Rev } },
            new AlertRule { Trigger = AlertTrigger.LowFuel, Color = "#FF5000", BlinkHz = 1, Enabled = false, Groups = { LedGroup.SideLeft, LedGroup.SideRight } },
            new AlertRule { Trigger = AlertTrigger.Drs, Color = "#00FF40", BlinkHz = 0, Enabled = false, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.RevLimiter, Color = "#FF0020", BlinkHz = 12, Enabled = false, Groups = { LedGroup.Rev } },
            new AlertRule { Trigger = AlertTrigger.InvalidLap, Color = "#FF0020", BlinkHz = 0, Enabled = false, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.Stalled, Color = "#FF0020", BlinkHz = 1, Style = AlertStyle.Pulse, Enabled = false, Groups = { LedGroup.Buttons } },
        };

        public static string AlertName(AlertTrigger t)
        {
            switch (t)
            {
                case AlertTrigger.Abs: return "ABS active";
                case AlertTrigger.Tc: return "TC active";
                case AlertTrigger.PitLimiter: return "Pit limiter on";
                case AlertTrigger.BlueFlag: return "Blue flag";
                case AlertTrigger.YellowFlag: return "Yellow flag";
                case AlertTrigger.LowFuel: return "Low fuel (under 10%)";
                case AlertTrigger.Drs: return "DRS open";
                case AlertTrigger.WhiteFlag: return "White flag";
                case AlertTrigger.GreenFlag: return "Green flag";
                case AlertTrigger.CheckeredFlag: return "Chequered flag";
                case AlertTrigger.BlackFlag: return "Black flag";
                case AlertTrigger.OrangeFlag: return "Meatball (orange) flag";
                case AlertTrigger.SpotterLeft: return "Car on your left";
                case AlertTrigger.SpotterRight: return "Car on your right";
                case AlertTrigger.RevLimiter: return "On the rev limiter";
                case AlertTrigger.InvalidLap: return "Lap invalidated";
                case AlertTrigger.Stalled: return "Engine stalled";
                case AlertTrigger.Custom: return "Custom";
                default: return t.ToString();
            }
        }

        /// <summary>A rule's name in the list: its own for custom alerts.</summary>
        public static string AlertName(AlertRule a) =>
            a.Trigger == AlertTrigger.Custom ? (string.IsNullOrWhiteSpace(a.Name) ? "Custom alert" : a.Name) : AlertName(a.Trigger);

        public static string StyleName(AlertStyle s)
        {
            switch (s)
            {
                case AlertStyle.Pulse: return "Pulse";
                case AlertStyle.Sweep: return "Sweep";
                case AlertStyle.Checker: return "Chequered";
                default: return "Flash";
            }
        }

        public static string GroupName(LedGroup g)
        {
            switch (g)
            {
                case LedGroup.Buttons: return "Buttons";
                case LedGroup.Encoders: return "Encoders";
                case LedGroup.SideLeft: return "Left of the rev lights";
                case LedGroup.SideRight: return "Right of the rev lights";
                default: return "Rev lights";
            }
        }
    }

    /// <summary>One frame of every LED: colour and brightness (1-90).</summary>
    public struct LedColor
    {
        public byte R, G, B, Brightness;
        public LedColor(byte r, byte g, byte b, byte brightness) { R = r; G = g; B = b; Brightness = brightness; }
    }

    /// <summary>Renders a LightProfile for a moment in time. Used for the wheel and the settings page's preview.</summary>
    public sealed class LightEngine
    {
        private readonly Random rng = new Random(); // per engine: the wheel and the preview render on different threads
        private readonly double[] sparkle;

        /// <summary>The wheel this engine renders for.</summary>
        public WheelModel Model { get; }

        /// <summary>LEDs in a frame (the wheel's count).</summary>
        public int Count => Model.LedCount;

        /// <summary>An engine for the FX Pro.</summary>
        public LightEngine() : this(WheelModel.FxPro) { }

        public LightEngine(WheelModel model)
        {
            Model = model ?? WheelModel.FxPro;
            sparkle = new double[Model.LedCount];
        }

        /// <param name="carLayout">The car's rev lights in real RPM (null = use the profile's colours).</param>
        /// <param name="reverseRev">The rev bar's first LED is the rightmost.</param>
        public LedColor[] Render(LightProfile p, DashValues v, RpmLayout carLayout, double now, bool reverseRev) => Render(p, v, carLayout, now, reverseRev, null);

        /// <summary>
        /// One frame, layer by layer (docs/light-states-plan.md): the theme with the state's look, the rev bar (shift lights
        /// with an optional theme tint while driving; dark or the look's effect when parked), the pit limiter, the start-up /
        /// shutdown animation, then alerts on top. `m` null = driving (as before there were states).
        /// </summary>
        public LedColor[] Render(LightProfile p, DashValues v, RpmLayout carLayout, double now, bool reverseRev, LightMoment m)
        {
            var state = m?.State ?? CarState.Driving;
            bool parked = state == CarState.Idle || state == CarState.Menu || state == CarState.EngineOff;
            var look = parked ? p.LookFor(state) : null;
            double dim = look != null ? Math.Max(0, Math.Min(100, look.Brightness)) / 100.0 : 1;
            var frame = new LedColor[Count];
            byte max = (byte)Math.Max(1, Math.Min(90, p.MaxBrightness));
            foreach (LedGroup g in Model.Groups)
            {
                var l = p.Group(g);
                double level = max * Math.Max(0, Math.Min(100, l.Brightness)) / 100.0 * dim;
                if (look != null && level < 0.5) { foreach (var led in Model.Leds(g)) frame[led] = new LedColor(0, 0, 0, 1); continue; }
                byte bright = (byte)Math.Max(1, Math.Round(level));
                if (l.Effect == LightEffect.Rpm)
                {
                    var leds = Model.Leds(g);
                    if (g == LedGroup.Rev && reverseRev) leds = leds.Reverse().ToArray();
                    if (look != null)
                    {
                        // parked: the rev bar joins the look (its effect, or the theme's own colours) or stays dark
                        var theme = ThemeLighting(p);
                        var revLook = look.Effect != null ? LookLighting(look, theme) : theme;
                        if (look.RevBar && revLook != null && revLook.Effect != LightEffect.Off)
                            for (int i = 0; i < leds.Length; i++) frame[leds[i]] = Ambient(revLook, i, leds.Length, leds[i], now, Bright(max, theme, dim));
                        else foreach (var led in leds) frame[led] = new LedColor(0, 0, 0, 1);
                    }
                    else
                    {
                        RenderRpm(frame, leds, p.Rev, v, carLayout, now, bright);
                        if (p.RevTint > 0) Tint(frame, leds, p, max);
                    }
                }
                else if (l.Effect == LightEffect.Levels && Model.HasLevels && look?.Effect == null) RenderLevels(frame, Model.Leds(g), l, v, now, bright);
                else if (l.Effect == LightEffect.Levels && Model.GaugeRings && look?.Effect == null) RenderGauges(frame, Model.Segments(g), l, v, now, bright);
                else
                {
                    // effects run along each segment (the GT Neo's encoder rings each get their own); Levels on a wheel
                    // without level lights shows its colours steady
                    var ambient = look?.Effect != null ? LookLighting(look, l)
                                : l.Effect == LightEffect.Levels ? new GroupLighting { Effect = LightEffect.Solid, Colors = l.Colors, Period = l.Period } : l;
                    var segs = Model.Segments(g);
                    for (int k = 0; k < segs.Length; k++)
                    {
                        var leds = segs[k];
                        double t = l.Stagger && segs.Length > 1 ? now + k * Math.Max(0.2, ambient.Period) / segs.Length : now;
                        for (int i = 0; i < leds.Length; i++)
                            frame[leds[i]] = Ambient(ambient, i, leds.Length, leds[i], t, bright);
                    }
                }
            }

            // The pit limiter: this car's own lights, else the preset's
            var limiter = m?.CarLimiter ?? p.Limiter;
            bool limiterOn = state == CarState.PitLimiter && limiter != null && limiter.Style != LimiterStyle.None;
            if (limiterOn) ApplyLimiter(frame, limiter, now);

            // Start-up / shutdown: short, over the theme, under the alerts
            if (state == CarState.Starting && p.Startup != StartupStyle.None) ApplyStartup(frame, p.Startup, m.Progress);
            if (state == CarState.Stopping && p.Shutdown != ShutdownStyle.None) ApplyShutdown(frame, p.Shutdown, m.Progress);

            // Alerts: first active rule wins for a LED. The pit limiter alert gives way to the limiter lights above.
            var taken = new bool[Count];
            foreach (var a in p.Alerts)
            {
                if (!a.Enabled || !Active(a, v)) continue;
                if (limiterOn && a.Trigger == AlertTrigger.PitLimiter) continue;
                var (r, g, b) = Rgb(a.Color);
                var leds = a.Groups.SelectMany(Model.AlertLeds).Distinct().ToArray();
                for (int i = 0; i < leds.Length; i++)
                {
                    int led = leds[i];
                    if (taken[led]) continue;
                    taken[led] = true;
                    double level = AlertLevel(a, i, leds.Length, now);
                    frame[led] = level <= 0 ? new LedColor(0, 0, 0, 1) : new LedColor((byte)(r * level), (byte)(g * level), (byte)(b * level), 90);
                }
            }
            return frame;
        }

        /// <summary>The theme's lighting for the rev bar when parked: the first group that isn't off (buttons, usually).</summary>
        private GroupLighting ThemeLighting(LightProfile p)
        {
            foreach (var g in Model.Groups)
            {
                if (g == LedGroup.Rev) continue;
                var l = p.Group(g);
                if (l.Effect != LightEffect.Off && l.Effect != LightEffect.Rpm)
                    return l.Effect == LightEffect.Levels ? new GroupLighting { Effect = LightEffect.Solid, Colors = l.Colors, Period = l.Period, Brightness = l.Brightness } : l;
            }
            return null;
        }

        private static byte Bright(byte max, GroupLighting l, double dim) => (byte)Math.Max(1, Math.Round(max * Math.Max(0, Math.Min(100, l?.Brightness ?? 100)) / 100.0 * dim));

        /// <summary>A look's one effect, in its own colours or the group's.</summary>
        private static GroupLighting LookLighting(StateLook look, GroupLighting group) => new GroupLighting
        {
            Effect = look.Effect ?? LightEffect.Solid,
            Colors = look.Colors != null && look.Colors.Count > 0 ? look.Colors : group?.Colors ?? new List<string> { "#FFFFFF" },
            Period = look.Period, Stagger = group?.Stagger ?? false,
        };

        /// <summary>Unlit rev LEDs glow faintly in the theme's colour (RevTint %); the shift lights stay as they are.</summary>
        private void Tint(LedColor[] frame, int[] leds, LightProfile p, byte max)
        {
            var theme = ThemeLighting(p);
            if (theme == null) return;
            var (r, g, b) = Rgb(theme.Colors != null && theme.Colors.Count > 0 ? theme.Colors[0] : "#FFFFFF");
            double k = Math.Min(40, p.RevTint) / 100.0;
            byte bright = (byte)Math.Max(1, Math.Round(max * k));
            foreach (var led in leds)
                if (frame[led].R + frame[led].G + frame[led].B == 0) frame[led] = new LedColor(r, g, b, bright);
        }

        private void ApplyLimiter(LedColor[] frame, LimiterLook lim, double now)
        {
            if (lim.Style == LimiterStyle.CarPattern && lim.Pattern != null && lim.Pattern.Count > 0)
            {
                // the car's own lights, steady: the pattern is laid out for the rev bar, left to right
                var rev = Model.Leds(LedGroup.Rev);
                for (int i = 0; i < rev.Length; i++)
                {
                    var c = Rgb(lim.Pattern[Math.Min(lim.Pattern.Count - 1, i * lim.Pattern.Count / rev.Length)]);
                    frame[rev[i]] = c.Item1 + c.Item2 + c.Item3 == 0 ? new LedColor(0, 0, 0, 1) : new LedColor(c.Item1, c.Item2, c.Item3, 90);
                }
                return;
            }
            var cols = lim.Colors != null && lim.Colors.Count > 0 ? lim.Colors : new List<string> { "#0040FF" };
            var a = Rgb(cols[0]);
            var b = cols.Count > 1 ? Rgb(cols[1]) : ((byte)0, (byte)0, (byte)0);
            void Paint(int[] leds)
            {
                for (int i = 0; i < leds.Length; i++)
                {
                    var (level, second) = lim.At(i, leds.Length, now);
                    var c = second ? b : a;
                    frame[leds[i]] = level <= 0 || c.Item1 + c.Item2 + c.Item3 == 0 ? new LedColor(0, 0, 0, 1)
                                   : new LedColor((byte)(c.Item1 * level), (byte)(c.Item2 * level), (byte)(c.Item3 * level), 90);
                }
            }
            if (lim.WholeWheel) foreach (var g in Model.Groups) foreach (var seg in Model.Segments(g)) Paint(seg);
            else Paint(Model.Leds(LedGroup.Rev));
        }

        /// <summary>Each LED's distance from the middle of its segment, 0 (middle) to 1 (an end): the takeovers' shape.</summary>
        private double[] middles;

        private double[] Middles()
        {
            if (middles != null) return middles;
            var d = new double[Count];
            foreach (var g in Model.Groups)
                foreach (var seg in Model.Segments(g))
                    for (int i = 0; i < seg.Length; i++) d[seg[i]] = seg.Length <= 1 ? 0 : Math.Abs((double)i / (seg.Length - 1) - 0.5) * 2;
            return middles = d;
        }

        private static LedColor Blend(LedColor under, (byte R, byte G, byte B) over, double k, byte bright = 90)
        {
            k = Math.Max(0, Math.Min(1, k));
            if (under.R + under.G + under.B == 0) under = new LedColor(0, 0, 0, bright);
            return new LedColor((byte)(under.R + (over.R - under.R) * k), (byte)(under.G + (over.G - under.G) * k), (byte)(under.B + (over.B - under.B) * k),
                                (byte)Math.Max(under.Brightness, (byte)(bright * k)));
        }

        private static LedColor Scaled(LedColor c, double k) { k = Math.Max(0, Math.Min(1, k)); return new LedColor((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.Brightness); }

        /// <summary>The engine-start animation over the theme, `t` from 0 to 1.</summary>
        private void ApplyStartup(LedColor[] frame, StartupStyle style, double t)
        {
            var mid = Middles();
            var white = ((byte)255, (byte)255, (byte)255);
            var rev = Model.Leds(LedGroup.Rev);
            for (int led = 0; led < frame.Length; led++)
            {
                var theme = frame[led];
                switch (style)
                {
                    case StartupStyle.Sweep:
                    {
                        // a white front runs out from the middle (0-0.6), the theme fills in behind it
                        double front = t / 0.6 * 1.25;
                        if (mid[led] > front) { frame[led] = new LedColor(0, 0, 0, 1); break; }
                        double glow = Math.Max(0, 1 - (front - mid[led]) / 0.35);
                        frame[led] = Blend(t < 0.6 ? Scaled(theme, 0.5) : theme, white, glow);
                        break;
                    }
                    case StartupStyle.Ignite:
                    {
                        if (t < 0.12 || (t >= 0.24 && t < 0.36)) frame[led] = new LedColor(255, 255, 255, 90);
                        else if (t < 0.36) frame[led] = new LedColor(0, 0, 0, 1);
                        else frame[led] = Blend(theme, white, 1 - (t - 0.36) / 0.64);
                        break;
                    }
                    case StartupStyle.SelfTest:
                    {
                        int at = Array.IndexOf(rev, led);
                        if (at >= 0)
                        {
                            // the bar fills in the standard shift colours, flashes, then hands over
                            var colours = new RevLighting().Colors;
                            var c = Rgb(colours[Math.Min(colours.Count - 1, at * colours.Count / Math.Max(1, rev.Length))]);
                            bool lit = t < 0.6 ? (double)at / rev.Length < t / 0.6 : t < 0.8 && (int)(t * 20) % 2 == 0;
                            frame[led] = lit ? new LedColor(c.Item1, c.Item2, c.Item3, 90) : t >= 0.8 ? Scaled(theme, (t - 0.8) / 0.2) : new LedColor(0, 0, 0, 1);
                        }
                        else frame[led] = t < 0.8 ? new LedColor(0, 0, 0, 1) : Scaled(theme, (t - 0.8) / 0.2);
                        break;
                    }
                }
            }
        }

        /// <summary>The engine-off animation over the theme, `t` from 0 to 1.</summary>
        private void ApplyShutdown(LedColor[] frame, ShutdownStyle style, double t)
        {
            var mid = Middles();
            for (int led = 0; led < frame.Length; led++)
            {
                if (style == ShutdownStyle.Fade) frame[led] = Scaled(frame[led], 1 - t);
                else if (style == ShutdownStyle.Collapse) frame[led] = mid[led] <= 1 - t * 1.1 ? frame[led] : new LedColor(0, 0, 0, 1);
            }
        }

        /// <summary>How lit (0-1) light `i` of an alert's `n` lights is at `now`, by the alert's style.</summary>
        public static double AlertLevel(AlertRule a, int i, int n, double now)
        {
            double hz = a.BlinkHz;
            switch (a.Style)
            {
                case AlertStyle.Pulse:
                    return hz <= 0 ? 1 : Breath(now * hz);
                case AlertStyle.Sweep:
                {
                    if (n <= 1) return 1;
                    // a band a third of the lights wide, crossing them `hz` times a second (0.5/s when steady)
                    double head = (now * (hz > 0 ? hz : 0.5) % 1) * (n + n / 3.0) - n / 6.0;
                    return Math.Max(0, 1 - Math.Abs(i - head) / Math.Max(1, n / 6.0));
                }
                case AlertStyle.Checker:
                {
                    int phase = hz <= 0 ? 0 : (int)(now * hz * 2) % 2;
                    return (i + phase) % 2 == 0 ? 1 : 0;
                }
                default:
                    return hz <= 0 || (int)(now * hz * 2) % 2 == 0 ? 1 : 0;
            }
        }

        public static bool Active(AlertRule a, DashValues v)
        {
            if (v == null || !v.Running) return false;
            if (a.Trigger == AlertTrigger.Custom)
            {
                var bind = a.ConditionBind;
                return bind != null && v.Truthy(bind) == true;
            }
            return Active(a.Trigger, v);
        }

        public static bool Active(AlertTrigger t, DashValues v)
        {
            if (v == null || !v.Running) return false;
            switch (t)
            {
                case AlertTrigger.Abs: return v.AbsActive;
                case AlertTrigger.Tc: return v.TcActive;
                case AlertTrigger.PitLimiter: return v.PitLimiter;
                case AlertTrigger.BlueFlag: return v.BlueFlag;
                case AlertTrigger.YellowFlag: return v.YellowFlag;
                case AlertTrigger.LowFuel: return v.FuelPercent > 0 && v.FuelPercent < 10;
                case AlertTrigger.Drs: return v.Drs;
                case AlertTrigger.WhiteFlag: return v.WhiteFlag;
                case AlertTrigger.GreenFlag: return v.GreenFlag;
                case AlertTrigger.CheckeredFlag: return v.CheckeredFlag;
                case AlertTrigger.BlackFlag: return v.BlackFlag;
                case AlertTrigger.OrangeFlag: return v.OrangeFlag;
                case AlertTrigger.SpotterLeft: return v.SpotterLeft;
                case AlertTrigger.SpotterRight: return v.SpotterRight;
                // the last 1.5% before the car's max: the limiter (SimHub's max RPM is the limiter in most games)
                case AlertTrigger.RevLimiter: return v.MaxRpm > 0 && v.Rpm >= v.MaxRpm * 0.985;
                case AlertTrigger.InvalidLap: return v.LapInvalid;
                case AlertTrigger.Stalled: return v.Stalled;
                default: return false;
            }
        }

        // ---------- Encoder levels ----------

        /// <summary>
        /// What each encoder light (FX Pro 12-16: ABS, TC, BB, DIFF, MAP) shows with the Levels effect: its value key and the
        /// range its colours span (levels outside it are clamped). Brake bias is a % around the middle of the car's range.
        /// </summary>
        public static readonly (string Name, string Key, double Low, double High)[] EncoderLevels =
        {
            ("ABS", "absLevel", 1, 11), ("TC", "tcLevel", 1, 11), ("BB", "brakeBias", 50, 64), ("DIFF", DiffKey, 1, 10), ("MAP", "engineMap", 1, 10),
        };

        /// <summary>The DIFF value's key while rendering (set from GroupLighting.DiffSource).</summary>
        private const string DiffKey = "\u0001diff";

        private readonly double?[] lastLevel = new double?[5];
        private readonly double[] levelChanged = { -99, -99, -99, -99, -99 };

        /// <summary>
        /// Each encoder's colour from its setting (low = first colour, high = last); a setting the game doesn't give
        /// (or 0 = off) is dim; a change blinks the light for a second.
        /// </summary>
        private void RenderLevels(LedColor[] frame, int[] leds, GroupLighting l, DashValues v, double now, byte bright)
        {
            var colours = l.Colors != null && l.Colors.Count > 0 ? l.Colors : new List<string> { "#00FF40", "#FFB000", "#FF0020" };
            for (int i = 0; i < leds.Length && i < EncoderLevels.Length; i++)
            {
                var (_, key, low, high) = EncoderLevels[i];
                double? x = v == null || !v.Running ? null : key == DiffKey ? v.Number(AlertRule.Bind(l.DiffSource) ?? "") : v.Number(key);
                if (x != lastLevel[i])
                {
                    if (lastLevel[i].HasValue && x.HasValue) levelChanged[i] = now;
                    lastLevel[i] = x;
                }
                if (!x.HasValue || (x.Value <= 0 && key != "brakeBias")) { frame[leds[i]] = Scale(Rgb(colours[0]), 0.08, bright); continue; }
                double t = Math.Max(0, Math.Min(1, (x.Value - low) / (high - low)));
                var c = colours.Count == 1 ? Rgb(colours[0]) : Ramp(colours, t);
                bool blink = l.FlashOnChange && now - levelChanged[i] < 1 && (int)((now - levelChanged[i]) * 16) % 2 == 1;
                frame[leds[i]] = Scale(c, blink ? 0.1 : 1, bright);
            }
        }

        private readonly Dictionary<int, double?> lastGauge = new Dictionary<int, double?>();
        private readonly Dictionary<int, double> gaugeChanged = new Dictionary<int, double>();

        /// <summary>
        /// Each ring as a gauge of its value (GT Neo): filled clockwise from 12 o'clock in the colours from low to high, or
        /// one bright segment where the value sits (Pointer). A value the game doesn't give (or 0 for a setting the car
        /// hasn't got) leaves the ring dim; a change makes the ring flash for a second, like the FX Pro's levels.
        /// </summary>
        private void RenderGauges(LedColor[] frame, int[][] rings, GroupLighting l, DashValues v, double now, byte bright)
        {
            var colours = l.Colors != null && l.Colors.Count > 0 ? l.Colors : new List<string> { "#00FF40", "#FFB000", "#FF0020" };
            for (int k = 0; k < rings.Length; k++)
            {
                var ring = rings[k];
                int n = ring.Length;
                var gauge = l.GaugeFor(k);
                double? x = v == null || !v.Running || gauge.Bind == null ? null : v.Number(gauge.Bind);
                lastGauge.TryGetValue(k, out var last);
                if (x != last)
                {
                    if (last.HasValue && x.HasValue) gaugeChanged[k] = now;
                    lastGauge[k] = x;
                }
                var dim = Scale(Rgb(colours[0]), 0.06, bright);
                if (!x.HasValue || (x.Value <= 0 && gauge.ZeroIsOff)) { foreach (var led in ring) frame[led] = dim; continue; }
                double span = gauge.High - gauge.Low;
                double t = span == 0 ? 1 : Math.Max(0, Math.Min(1, (x.Value - gauge.Low) / span));
                gaugeChanged.TryGetValue(k, out var changedAt);
                bool changed = l.FlashOnChange && gaugeChanged.ContainsKey(k) && now - changedAt < 1;
                bool blinkOff = changed && (int)((now - changedAt) * 16) % 2 == 1;
                if (gauge.Style == GaugeStyle.Pointer)
                {
                    int at = (int)Math.Round(t * (n - 1));
                    var c = colours.Count == 1 ? Rgb(colours[0]) : Ramp(colours, t);
                    for (int i = 0; i < n; i++)
                    {
                        int d = Math.Min(Math.Abs(i - at), n - Math.Abs(i - at));
                        frame[ring[i]] = d == 0 ? Scale(c, blinkOff ? 0.15 : 1, bright) : d == 1 ? Scale(c, 0.22, bright) : dim;
                    }
                }
                else
                {
                    // a setting at its lowest still shows one segment (it's on, just low); a pedal at 0 is empty
                    int lit = Math.Max(t > 0 || gauge.ZeroIsOff ? 1 : 0, (int)Math.Round(t * n));
                    for (int i = 0; i < n; i++)
                        frame[ring[i]] = i < lit ? Scale(colours.Count == 1 ? Rgb(colours[0]) : Ramp(colours, n <= 1 ? 1 : (double)i / (n - 1)), blinkOff ? 0.15 : 1, bright) : dim;
                }
            }
        }

        /// <summary>Colours spread evenly from first (0) to last (1), not wrapping.</summary>
        private static (byte, byte, byte) Ramp(List<string> colours, double t)
        {
            double pos = Math.Max(0, Math.Min(1, t)) * (colours.Count - 1);
            int a = Math.Min(colours.Count - 2, (int)Math.Floor(pos));
            return Mix(Rgb(colours[a]), Rgb(colours[a + 1]), pos - a);
        }

        private LedColor Ambient(GroupLighting l, int i, int n, int led, double now, byte bright)
        {
            var colours = l.Colors != null && l.Colors.Count > 0 ? l.Colors : new List<string> { "#FFFFFF" };
            double period = Math.Max(0.2, l.Period);
            double pos = n <= 1 ? 0 : (double)i / n;
            switch (l.Effect)
            {
                case LightEffect.Off: return new LedColor(0, 0, 0, 1);
                case LightEffect.Solid: return Scale(Rgb(colours[i % colours.Count]), 1, bright);
                case LightEffect.Breathe:
                {
                    double cycle = now / period;
                    var c = Rgb(colours[(int)Math.Floor(cycle) % colours.Count]);
                    return Scale(c, Breath(cycle), bright);
                }
                case LightEffect.Wave: return Scale(Gradient(colours, pos - now / period), 1, bright);
                case LightEffect.Rainbow: return Scale(Hue(pos * 0.6 + now / period), 1, bright);
                case LightEffect.RainbowBreathe: return Scale(Hue(pos + now / (period * 3)), Breath(now / period), bright);
                case LightEffect.Scanner:
                {
                    double t = now / period % 2;
                    double head = (t < 1 ? t : 2 - t) * (n - 1);
                    double level = Math.Max(0, 1 - Math.Abs(i - head) / 2.2);
                    return Scale(Rgb(colours[0]), 0.04 + 0.96 * level * level, bright);
                }
                case LightEffect.Comet:
                {
                    // head runs along and round again; the tail fades from the first colour to the last
                    double head = (now / period % 1) * n;
                    double tail = Math.Max(2, n * 0.45);
                    double back = ((head - i) % n + n) % n;
                    if (back > tail) return new LedColor(0, 0, 0, 1);
                    double k = 1 - back / tail;
                    return Scale(Mix(Rgb(colours[0]), Rgb(colours[colours.Count - 1]), 1 - k), k * k, bright);
                }
                case LightEffect.Heartbeat:
                {
                    double t = now / period % 1;
                    double beat = Math.Max(Pulse(t, 0, 0.11), 0.75 * Pulse(t, 0.18, 0.13));
                    var c = beat > 0 && t >= 0.18 && colours.Count > 1 ? Rgb(colours[1]) : Rgb(colours[0]);
                    return Scale(c, 0.05 + 0.95 * beat, bright);
                }
                case LightEffect.Fire:
                {
                    double speed = 8 / period;
                    double heat = 0.25 + 0.75 * Noise(led * 1.7, now * speed) * (0.55 + 0.45 * Noise(led * 0.31 + 9, now * speed * 0.37));
                    return Scale(Ramp(colours.Count > 1 ? colours : new List<string> { "#300000", colours[0] }, heat), 0.2 + 0.8 * heat, bright);
                }
                case LightEffect.Twinkle:
                {
                    var star = colours.Count > 1 ? colours[1 + (int)(Hash(led * 3.3) * (colours.Count - 1)) % (colours.Count - 1)] : "#FFFFFF";
                    double rate = 0.6 + Hash(led * 7.1) * 0.9;
                    double c = now / period * rate + Hash(led * 5.7 + 1);
                    double f = c - Math.Floor(c);
                    double k = Hash(led * 13.7 + Math.Floor(c) * 3.1) > 0.45 ? Math.Pow(Math.Sin(Math.PI * f), 3) : 0;
                    // the base glows softly (a night sky, not off), stars flare over it
                    return Scale(Mix(Rgb(colours[0]), Rgb(star), k), 0.3 + 0.7 * k, bright);
                }
                case LightEffect.Ripple:
                {
                    double d = n <= 1 ? 0 : Math.Abs((double)i / (n - 1) - 0.5) * 2;
                    double wave = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (d * 1.2 - now / period));
                    return Scale(Gradient(colours, d * 0.5 - now / period * 0.25), 0.12 + 0.88 * wave * wave, bright);
                }
                case LightEffect.Plasma:
                {
                    double w = 2 * Math.PI * now / period;
                    double x = Math.Sin(i * 0.9 + w) + Math.Sin(i * 0.37 - w * 1.7 + 1.3) + Math.Sin((i + now * 3 / period) * 0.21);
                    return Scale(Gradient(colours, (x / 3 + 1) / 2), 1, bright);
                }
                case LightEffect.Strobe:
                {
                    double t = now / period % 1;
                    bool left = (double)i / Math.Max(1, n) < 0.5;
                    double local = left ? t : (t + 0.5) % 1;
                    bool on = local < 0.08 || (local >= 0.14 && local < 0.22);
                    return on ? Scale(Rgb(colours[left || colours.Count < 2 ? 0 : 1]), 1, bright) : new LedColor(0, 0, 0, 1);
                }
                case LightEffect.Sparkle:
                {
                    if (led < sparkle.Length)
                    {
                        if (sparkle[led] < now && rng.NextDouble() < 0.012) sparkle[led] = now + 0.35;
                        double glint = Math.Max(0, (sparkle[led] - now) / 0.35);
                        var baseC = Rgb(colours[0]);
                        var glintC = Rgb(colours[Math.Min(1, colours.Count - 1)]);
                        double breath = 0.25 + 0.2 * Breath(now / period + pos);
                        return Scale(Mix(baseC, glintC, glint), Math.Min(1, breath + glint), bright);
                    }
                    return new LedColor(0, 0, 0, 1);
                }
                default: return new LedColor(0, 0, 0, 1);
            }
        }

        private static void RenderRpm(LedColor[] frame, int[] leds, RevLighting rev, DashValues v, RpmLayout car, double now, byte bright)
        {
            double rpm = v?.Rpm ?? 0;
            if (v == null || !v.Running) { foreach (var l in leds) frame[l] = new LedColor(0, 0, 0, 1); return; }
            if (rev.UseCarData && car != null)
            {
                var layout = car.Gears != null ? car.ForGear(v.GearKey) : car;
                if (layout.FlashRpm > 0 && rpm >= layout.FlashRpm)
                {
                    double hz = layout.FlashBlinkUnits > 0 ? 1000.0 / (2 * layout.FlashBlinkUnits * RpmLightsMapper.BlinkMsPerUnit) : 0;
                    bool on = hz <= 0 || (int)(now * hz * 2) % 2 == 0;
                    var (fr, fg, fb) = Rgb(layout.FlashColor);
                    foreach (var l in leds) frame[l] = on ? new LedColor(fr, fg, fb, bright) : new LedColor(0, 0, 0, 1);
                    return;
                }
                for (int i = 0; i < leds.Length && i < layout.Rpm.Length; i++)
                {
                    bool lit = layout.Rpm[i] > 0 && rpm >= layout.Rpm[i];
                    var (r, g, b) = Rgb(layout.Colors[i]);
                    frame[leds[i]] = lit ? new LedColor(r, g, b, bright) : new LedColor(0, 0, 0, 1);
                }
                return;
            }

            double shift = v.Redline > 0 ? v.Redline : v.MaxRpm * 0.95;
            if (shift <= 0) { foreach (var l in leds) frame[l] = new LedColor(0, 0, 0, 1); return; }
            if (rpm >= shift)
            {
                bool on = rev.FlashHz <= 0 || (int)(now * rev.FlashHz * 2) % 2 == 0;
                var (fr, fg, fb) = Rgb(rev.FlashColor);
                foreach (var l in leds) frame[l] = on ? new LedColor(fr, fg, fb, bright) : new LedColor(0, 0, 0, 1);
                return;
            }
            // the preset's own pattern
            var pattern = rev.Layout();
            for (int i = 0; i < leds.Length && i < pattern.Fractions.Length; i++)
            {
                bool lit = pattern.Fractions[i] > 0 && rpm >= pattern.Fractions[i] * shift;
                var (r, g, b) = Rgb(pattern.Colors[i]);
                frame[leds[i]] = lit ? new LedColor(r, g, b, bright) : new LedColor(0, 0, 0, 1);
            }
        }

        // ---------- Colour helpers ----------

        private static double Breath(double cycle) => 0.06 + 0.94 * (0.5 - 0.5 * Math.Cos(cycle * 2 * Math.PI));

        /// <summary>A soft pulse: 0 outside [start, start + width], a sine hump inside.</summary>
        private static double Pulse(double t, double start, double width) => t < start || t > start + width ? 0 : Math.Sin(Math.PI * (t - start) / width);

        /// <summary>0-1, the same for the same input (so effects look random but render the same every time).</summary>
        private static double Hash(double x) { double v = Math.Sin(x * 12.9898 + 78.233) * 43758.5453; return v - Math.Floor(v); }

        /// <summary>Smooth value noise over time for one light: 0-1, changing gently with t.</summary>
        private static double Noise(double x, double t)
        {
            double a = Math.Floor(t), f = t - a;
            f = f * f * (3 - 2 * f);
            double h0 = Hash(x * 17.13 + a * 3.71), h1 = Hash(x * 17.13 + (a + 1) * 3.71);
            return h0 + (h1 - h0) * f;
        }

        private static (byte, byte, byte) Scale01((byte R, byte G, byte B) c, double k) => ((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

        private static LedColor Scale((byte R, byte G, byte B) c, double level, byte bright)
        {
            level = Math.Max(0, Math.Min(1, level));
            return new LedColor((byte)(c.R * level), (byte)(c.G * level), (byte)(c.B * level), bright);
        }

        private static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) =>
            ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>Colours spread evenly around a loop, position 0-1 (wraps).</summary>
        private static (byte, byte, byte) Gradient(List<string> colours, double pos)
        {
            if (colours.Count == 1) return Rgb(colours[0]);
            pos = (pos % 1 + 1) % 1 * colours.Count;
            int a = (int)Math.Floor(pos) % colours.Count, b = (a + 1) % colours.Count;
            double t = pos - Math.Floor(pos);
            t = t * t * (3 - 2 * t); // ease
            return Mix(Rgb(colours[a]), Rgb(colours[b]), t);
        }

        public static (byte, byte, byte) Hue(double h)
        {
            h = (h % 1 + 1) % 1 * 6;
            byte x = (byte)(255 * (1 - Math.Abs(h % 2 - 1)));
            switch ((int)h)
            {
                case 0: return (255, x, 0);
                case 1: return (x, 255, 0);
                case 2: return (0, 255, x);
                case 3: return (0, x, 255);
                case 4: return (x, 0, 255);
                default: return (255, 0, x);
            }
        }

        public static (byte R, byte G, byte B) Rgb(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return (0, 0, 0);
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return (0, 0, 0);
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }
}
