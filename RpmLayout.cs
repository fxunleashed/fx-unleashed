using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The wheel's 15 rev LEDs in real RPM: where each LED lights (0 = unused), its color, and the shift flash.
    /// Every source (car database, generated pattern, SimPro preset) produces one of these; per-car overrides edit
    /// it; RpmLightsMapper.ToSimPro turns it into SimPro's percent-of-max settings.
    /// </summary>
    public class RpmLayout
    {
        public int[] Rpm = new int[RpmLightsMapper.WheelLeds];
        public string[] Colors = Enumerable.Repeat(LedPalette.Off, RpmLightsMapper.WheelLeds).ToArray();

        /// <summary>RPM where all LEDs switch to FlashColor; 0 = no flash.</summary>
        public int FlashRpm;
        public string FlashColor = LedPalette.Blue;
        /// <summary>0 = solid, otherwise SimPro blink units.</summary>
        public int FlashBlinkUnits;

        /// <summary>Optional per-gear LED/flash RPMs ("R","N","1"..) for wheels with SimPro's Advanced mode.</summary>
        public Dictionary<string, GearCurve> Gears;

        public RpmLayout Clone()
        {
            var c = (RpmLayout)MemberwiseClone();
            c.Rpm = (int[])Rpm.Clone();
            c.Colors = (string[])Colors.Clone();
            c.Gears = Gears?.ToDictionary(g => g.Key, g => g.Value.Clone());
            return c;
        }

        /// <summary>The RPM where the sequence completes: the flash, or else the highest LED.</summary>
        public int ShiftRpm => FlashRpm > 0 ? FlashRpm : Rpm.DefaultIfEmpty(0).Max();

        /// <summary>Moves every LED, the flash and any per-gear curves by offset RPM (unused LEDs stay unused).</summary>
        public RpmLayout Offset(int offset)
        {
            var c = Clone();
            int Move(int v) => v <= 0 ? 0 : Math.Max(1, v + offset);
            for (int i = 0; i < c.Rpm.Length; i++) c.Rpm[i] = Move(c.Rpm[i]);
            c.FlashRpm = Move(c.FlashRpm);
            if (c.Gears != null)
                foreach (var g in c.Gears.Values)
                {
                    for (int i = 0; i < g.Rpm.Length; i++) g.Rpm[i] = Move(g.Rpm[i]);
                    g.FlashRpm = Move(g.FlashRpm);
                }
            return c;
        }

        /// <summary>One gear's curve as a plain layout (for wheels that can't do per-gear curves themselves).</summary>
        public RpmLayout ForGear(string gear)
        {
            var c = Clone();
            c.Gears = null;
            if (Gears != null && Gears.TryGetValue(gear, out var g))
            {
                c.Rpm = (int[])g.Rpm.Clone();
                c.FlashRpm = g.FlashRpm;
            }
            return c;
        }

        /// <summary>As fractions of the shift point, for the animated previews.</summary>
        public LedLayout ToPreview()
        {
            double shift = Math.Max(1, ShiftRpm);
            var p = new LedLayout
            {
                FlashColor = FlashRpm > 0 ? FlashColor : null,
                FlashBlinks = FlashBlinkUnits > 0,
            };
            for (int i = 0; i < Rpm.Length; i++)
            {
                p.Fractions[i] = Rpm[i] <= 0 ? 0 : Rpm[i] / shift;
                p.Colors[i] = Colors[i];
            }
            return p;
        }

        // ---------- Builders ----------

        /// <summary>From a car's real shift lights: its N LEDs stretched over the wheel's 15, colors snapped to the palette.</summary>
        /// <param name="exactColours">The car's own colours (USB mode drives any RGB); else snapped to SimPro's palette.</param>
        public static RpmLayout FromProfile(CarLedProfile car, bool includeGears, bool exactColours = false)
        {
            // Most common curve = the "default" curve (also used for gears the car data doesn't list).
            var defaultCurve = car.GearRpm.Values
                .GroupBy(v => string.Join(",", v)).OrderByDescending(g => g.Count()).First().First();

            string Colour(string c) => exactColours ? ExactColour(c) : RpmLightsMapper.ToSimProColor(c);
            var map = SourceMap(car, defaultCurve);
            var layout = new RpmLayout();
            var redColor = Colour(car.Colors[0]);
            for (int j = 0; j < RpmLightsMapper.WheelLeds; j++)
            {
                int src = map[j];
                var color = Colour(car.Colors[src]);
                bool off = color == null || defaultCurve[src] <= 0;
                layout.Rpm[j] = off ? 0 : defaultCurve[src];
                layout.Colors[j] = off ? LedPalette.Off : color;
            }
            layout.FlashRpm = redColor != null && defaultCurve[0] > 0 ? defaultCurve[0] : 0;
            layout.FlashColor = redColor ?? LedPalette.Blue;
            layout.FlashBlinkUnits = car.RedlineBlinkIntervalMs > 0
                ? Math.Max(1, (int)Math.Round(car.RedlineBlinkIntervalMs / RpmLightsMapper.BlinkMsPerUnit)) : 0;

            if (includeGears && car.GearRpm.Values.Any(v => !v.SequenceEqual(defaultCurve)))
            {
                layout.Gears = new Dictionary<string, GearCurve>();
                foreach (var gear in RpmLightsMapper.SimProGears)
                {
                    var curve = car.GearRpm.TryGetValue(gear, out var c) ? c : defaultCurve;
                    var gc = new GearCurve { FlashRpm = layout.FlashRpm > 0 ? curve[0] : 0 };
                    for (int j = 0; j < RpmLightsMapper.WheelLeds; j++)
                        // 0 in a gear's curve = lit from idle (e.g. the Porsche Cup's 1st gear), not unused.
                        gc.Rpm[j] = layout.Rpm[j] > 0 ? Math.Max(1, curve[map[j]]) : 0;
                    layout.Gears[gear] = gc;
                }
            }
            return layout;
        }

        /// <summary>
        /// Which of the car's LEDs (1..N in its data) each of the wheel's 15 shows, keeping order so symmetric patterns
        /// stay symmetric. A car with fewer LEDs is stretched by giving the extra slots to lit LEDs only: an unused LED
        /// (a gap in the pattern) stays one slot wide, as on the real car (the BMW M4 GT3's 12, with a gap each side,
        /// becomes G G G · Y Y R R R Y Y · G G G, not G G · · Y Y R R R Y Y · · G G). More LEDs than 15 are sampled evenly.
        /// </summary>
        internal static int[] SourceMap(CarLedProfile car, int[] curve)
        {
            int n = car.LedNumber, w = RpmLightsMapper.WheelLeds;
            var map = new int[w];
            if (n <= 1) { for (int j = 0; j < w; j++) map[j] = 1; return map; }
            bool Off(int src) => src >= curve.Length || curve[src] <= 0 || RpmLightsMapper.ToSimProColor(car.Colors[src]) == null;
            int offCount = Enumerable.Range(1, n).Count(Off), lit = n - offCount;
            if (n >= w || lit == 0 || offCount >= w)
            {
                for (int j = 0; j < w; j++) map[j] = 1 + (int)Math.Round(j * (n - 1) / (double)(w - 1));
                return map;
            }
            // each unused LED takes one slot, the lit ones share the rest; slot j shows the LED whose span holds its middle
            double litWidth = (w - offCount) / (double)lit;
            var ends = new double[n];
            double at = 0;
            for (int i = 0; i < n; i++) { at += Off(i + 1) ? 1 : litWidth; ends[i] = at; }
            for (int j = 0; j < w; j++)
            {
                double mid = j + 0.5;
                int i = 0;
                while (i < n - 1 && mid >= ends[i] - 1e-9) i++;
                map[j] = i + 1;
            }
            return map;
        }

        /// <summary>A car data colour ("#AARRGGBB", "#RRGGBB" or a name) as "#RRGGBB"; null when transparent or near black (off).</summary>
        internal static string ExactColour(string s)
        {
            if (RpmLightsMapper.ToSimProColor(s) == null) return null;
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s.Trim());
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>From a pattern (fractions of the shift point), with the shift point at shiftRpm.</summary>
        public static RpmLayout FromPattern(LedLayout pattern, double shiftRpm, int flashBlinkUnits)
        {
            var layout = new RpmLayout();
            for (int i = 0; i < RpmLightsMapper.WheelLeds; i++)
            {
                bool off = pattern.Fractions[i] <= 0;
                layout.Rpm[i] = off ? 0 : Math.Max(1, (int)Math.Round(pattern.Fractions[i] * shiftRpm));
                layout.Colors[i] = off ? LedPalette.Off : pattern.Colors[i];
            }
            if (pattern.FlashColor != null)
            {
                layout.FlashRpm = (int)Math.Round(shiftRpm);
                layout.FlashColor = pattern.FlashColor;
                layout.FlashBlinkUnits = pattern.FlashBlinks ? flashBlinkUnits : 0;
            }
            return layout;
        }
    }

    public class GearCurve
    {
        public int[] Rpm = new int[RpmLightsMapper.WheelLeds];
        public int FlashRpm;
        public GearCurve Clone() => new GearCurve { Rpm = (int[])Rpm.Clone(), FlashRpm = FlashRpm };
    }

    public enum OverrideKind { Offset, Custom, Pattern }

    /// <summary>A saved per-car adjustment, keyed by "Game | CarId".</summary>
    public class CarOverride
    {
        public string CarKey;
        public string Game;
        public string CarId;
        public string CarName;
        public OverrideKind Kind = OverrideKind.Offset;
        public int OffsetRpm;
        public RpmLayout Custom;
        /// <summary>For Pattern overrides: the pattern/colors to use instead of the car's own lights.</summary>
        public FallbackStyle Style;
        public DateTime UpdatedUtc = DateTime.UtcNow;

        /// <param name="baseLayout">The car's own lights (database or fallback); null when unknown.</param>
        /// <param name="presetTemplate">The SimPro preset's original rpm_lights, for the "your preset" pattern.</param>
        public RpmLayout Apply(RpmLayout baseLayout, Newtonsoft.Json.Linq.JObject presetTemplate)
        {
            switch (Kind)
            {
                case OverrideKind.Custom when Custom != null:
                    return Custom.Clone();
                case OverrideKind.Pattern when Style != null && baseLayout != null:
                    // Same shift point as the car's own lights, different look.
                    return RpmLightsMapper.FromStyle(Style, presetTemplate, baseLayout.ShiftRpm);
                default:
                    return baseLayout?.Offset(OffsetRpm);
            }
        }

        public string Summary
        {
            get
            {
                switch (Kind)
                {
                    case OverrideKind.Custom when Custom != null:
                        return $"Custom lights, shift at {Custom.ShiftRpm} rpm";
                    case OverrideKind.Pattern when Style != null:
                        return "Pattern: " + LedPatterns.Catalog.First(c => c.Kind == Style.Pattern).Title;
                    default:
                        return OffsetRpm == 0 ? "Offset 0 rpm (no change)" : $"Offset {OffsetRpm:+0;-0} rpm";
                }
            }
        }

        public CarOverride Clone()
        {
            var c = (CarOverride)MemberwiseClone();
            c.Custom = Custom?.Clone();
            c.Style = Style?.Clone();
            return c;
        }
    }
}
