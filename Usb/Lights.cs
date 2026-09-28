using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>The FX Pro's LEDs in groups (renderer indices, FXProDashes docs/firmware-notes.md).</summary>
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
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AlertTrigger { Abs, Tc, PitLimiter, BlueFlag, YellowFlag, LowFuel, Drs }

    public class GroupLighting
    {
        public LightEffect Effect = LightEffect.Solid;
        public List<string> Colors = new List<string> { "#FFFFFF" };
        /// <summary>Seconds per breath / wave / sweep.</summary>
        public double Period = 4;
        /// <summary>% of the profile's brightness.</summary>
        public int Brightness = 100;

        public GroupLighting Clone() { var c = (GroupLighting)MemberwiseClone(); c.Colors = new List<string>(Colors); return c; }
    }

    public class AlertRule
    {
        public AlertTrigger Trigger;
        public bool Enabled = true;
        public string Color = "#FFB000";
        public List<LedGroup> Groups = new List<LedGroup>();
        /// <summary>Blinks per second (0 = steady).</summary>
        public double BlinkHz = 10;

        public AlertRule Clone() { var c = (AlertRule)MemberwiseClone(); c.Groups = new List<LedGroup>(Groups); return c; }
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

        public GroupLighting Group(LedGroup g) => Groups.TryGetValue(g, out var l) ? l : (Groups[g] = new GroupLighting { Effect = LightEffect.Off });

        public LightProfile Clone()
        {
            var c = (LightProfile)MemberwiseClone();
            c.Groups = Groups.ToDictionary(k => k.Key, k => k.Value.Clone());
            c.Rev = Rev.Clone();
            c.Alerts = Alerts.Select(a => a.Clone()).ToList();
            return c;
        }
    }

    public static class LightPresets
    {
        public const string CustomId = "custom";

        public static string NewUserId() => "user-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        public static bool IsBuiltIn(string id) => All.Any(p => p.Id == id);

        public static readonly LightProfile[] All =
        {
            Make("mustang", "Prism", "A rainbow drifting over the buttons and encoders, breathing slowly.",
                (LedGroup.Buttons, LightEffect.RainbowBreathe, 4, 100, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.RainbowBreathe, 4, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideLeft, LightEffect.RainbowBreathe, 4, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideRight, LightEffect.RainbowBreathe, 4, 100, new[] { "#FFFFFF" })),
            Make("aurora", "Aurora", "Teal, blue and violet drifting like northern lights; the side lights breathe teal.",
                (LedGroup.Buttons, LightEffect.Wave, 9, 100, new[] { "#00FFA3", "#00B3FF", "#7B2FFF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 6, 100, new[] { "#7B2FFF", "#00FFA3" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 6, 90, new[] { "#00FFA3" }),
                (LedGroup.SideRight, LightEffect.Breathe, 6, 90, new[] { "#00FFA3" }),
                rev: ("#00FFA3", "#00B3FF", "#7B2FFF", "#FFFFFF")),
            Make("synthwave", "Synthwave", "Hot pink, purple and cyan flowing over the buttons, neon side lights.",
                (LedGroup.Buttons, LightEffect.Wave, 6, 100, new[] { "#FF2E97", "#9D4EDD", "#00F0FF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 4, 100, new[] { "#FF2E97", "#00F0FF" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 3, 100, new[] { "#FF2E97" }),
                (LedGroup.SideRight, LightEffect.Breathe, 3, 100, new[] { "#00F0FF" }),
                rev: ("#00F0FF", "#9D4EDD", "#FF2E97", "#FFFFFF")),
            Make("ember", "Ember", "Glowing embers: slow orange and red breathing with the odd spark.",
                (LedGroup.Buttons, LightEffect.Sparkle, 5, 100, new[] { "#FF3D00", "#FFB000" }),
                (LedGroup.Encoders, LightEffect.Breathe, 5, 100, new[] { "#FF5A00", "#FF1E00" }),
                (LedGroup.SideLeft, LightEffect.Breathe, 7, 80, new[] { "#FF1E00" }),
                (LedGroup.SideRight, LightEffect.Breathe, 7, 80, new[] { "#FF1E00" }),
                rev: ("#FFD000", "#FF7A00", "#FF1E00", "#FFFFFF")),
            Make("ice", "Glacier", "Cold white and ice blue flowing slowly; calm and easy on the eyes at night.",
                (LedGroup.Buttons, LightEffect.Wave, 12, 70, new[] { "#FFFFFF", "#7FDBFF", "#0060FF" }),
                (LedGroup.Encoders, LightEffect.Breathe, 8, 70, new[] { "#7FDBFF" }),
                (LedGroup.SideLeft, LightEffect.Solid, 4, 40, new[] { "#0060FF" }),
                (LedGroup.SideRight, LightEffect.Solid, 4, 40, new[] { "#0060FF" }),
                rev: ("#FFFFFF", "#7FDBFF", "#0060FF", "#FF0020")),
            Make("scanner", "Scanner", "A red light sweeping across the buttons, encoders breathing red.",
                (LedGroup.Buttons, LightEffect.Scanner, 1.6, 100, new[] { "#FF0010" }),
                (LedGroup.Encoders, LightEffect.Breathe, 3, 100, new[] { "#FF0010" }),
                (LedGroup.SideLeft, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideRight, LightEffect.Off, 4, 100, new[] { "#000000" }),
                rev: ("#FF0010", "#FF0010", "#FF0010", "#FFFFFF")),
            Make("stealth", "Stealth", "Dim white buttons and nothing else, until something needs your attention.",
                (LedGroup.Buttons, LightEffect.Solid, 4, 18, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideLeft, LightEffect.Off, 4, 100, new[] { "#000000" }),
                (LedGroup.SideRight, LightEffect.Off, 4, 100, new[] { "#000000" })),
            Make("rainbow", "Full Rainbow", "Every light a flowing rainbow; the rev lights stay shift lights.",
                (LedGroup.Buttons, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.Encoders, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideLeft, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" }),
                (LedGroup.SideRight, LightEffect.Rainbow, 5, 100, new[] { "#FFFFFF" })),
        };

        public static LightProfile Find(string id) => All.FirstOrDefault(p => p.Id == id);

        private static LightProfile Make(string id, string name, string description,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) a,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) b,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) c,
            (LedGroup G, LightEffect E, double Period, int Brightness, string[] Colors) d,
            (string Low, string Mid, string High, string Flash)? rev = null)
        {
            var p = new LightProfile { Id = id, Name = name, Description = description };
            foreach (var g in new[] { a, b, c, d })
                p.Groups[g.G] = new GroupLighting { Effect = g.E, Period = g.Period, Brightness = g.Brightness, Colors = g.Colors.ToList() };
            p.Groups[LedGroup.Rev] = new GroupLighting { Effect = LightEffect.Rpm };
            if (rev.HasValue)
            {
                p.Rev.Colors = new List<string> { rev.Value.Low, rev.Value.Mid, rev.Value.High };
                p.Rev.FlashColor = rev.Value.Flash;
            }
            p.Alerts = DefaultAlerts();
            return p;
        }

        /// <summary>ABS on the left side lights, TC on the right, pit limiter on the rev lights, flags on the encoders.</summary>
        public static List<AlertRule> DefaultAlerts() => new List<AlertRule>
        {
            new AlertRule { Trigger = AlertTrigger.Abs, Color = "#FFB000", BlinkHz = 12, Groups = { LedGroup.SideLeft } },
            new AlertRule { Trigger = AlertTrigger.Tc, Color = "#00A0FF", BlinkHz = 12, Groups = { LedGroup.SideRight } },
            new AlertRule { Trigger = AlertTrigger.PitLimiter, Color = "#0040FF", BlinkHz = 3, Groups = { LedGroup.Rev } },
            new AlertRule { Trigger = AlertTrigger.BlueFlag, Color = "#0040FF", BlinkHz = 2, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.YellowFlag, Color = "#FFD000", BlinkHz = 2, Groups = { LedGroup.Encoders } },
            new AlertRule { Trigger = AlertTrigger.LowFuel, Color = "#FF5000", BlinkHz = 1, Enabled = false, Groups = { LedGroup.SideLeft, LedGroup.SideRight } },
            new AlertRule { Trigger = AlertTrigger.Drs, Color = "#00FF40", BlinkHz = 0, Enabled = false, Groups = { LedGroup.Encoders } },
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
                default: return t.ToString();
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
        public const int Count = 38;
        private readonly Random rng = new Random(); // per engine: the wheel and the preview render on different threads
        private readonly double[] sparkle = new double[Count];

        public static int[] Leds(LedGroup g)
        {
            switch (g)
            {
                case LedGroup.Buttons: return Enumerable.Range(0, 12).ToArray();
                case LedGroup.Encoders: return Enumerable.Range(12, 5).ToArray();
                case LedGroup.SideLeft: return new[] { 17, 18, 19 };
                case LedGroup.SideRight: return new[] { 20, 21, 22 };
                default: return Enumerable.Range(23, 15).ToArray();
            }
        }

        /// <param name="carLayout">The car's rev lights in real RPM (null = use the profile's colours).</param>
        /// <param name="reverseRev">Rev LED 23 is the rightmost.</param>
        public LedColor[] Render(LightProfile p, DashValues v, RpmLayout carLayout, double now, bool reverseRev)
        {
            var frame = new LedColor[Count];
            byte max = (byte)Math.Max(1, Math.Min(90, p.MaxBrightness));
            foreach (LedGroup g in Enum.GetValues(typeof(LedGroup)))
            {
                var l = p.Group(g);
                var leds = Leds(g);
                if (g == LedGroup.Rev && reverseRev) leds = leds.Reverse().ToArray();
                byte bright = (byte)Math.Max(1, Math.Round(max * Math.Max(0, Math.Min(100, l.Brightness)) / 100.0));
                if (l.Effect == LightEffect.Rpm) RenderRpm(frame, leds, p.Rev, v, carLayout, now, bright);
                else
                    for (int i = 0; i < leds.Length; i++)
                        frame[leds[i]] = Ambient(l, i, leds.Length, leds[i], now, bright);
            }

            // Alerts: first active rule wins for a LED.
            var taken = new bool[Count];
            foreach (var a in p.Alerts)
            {
                if (!a.Enabled || !Active(a.Trigger, v)) continue;
                bool on = a.BlinkHz <= 0 || (int)(now * a.BlinkHz * 2) % 2 == 0;
                var (r, g, b) = Rgb(a.Color);
                foreach (var grp in a.Groups)
                    foreach (var led in Leds(grp))
                    {
                        if (taken[led]) continue;
                        taken[led] = true;
                        frame[led] = on ? new LedColor(r, g, b, 90) : new LedColor(0, 0, 0, 1);
                    }
            }
            return frame;
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
                default: return false;
            }
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
