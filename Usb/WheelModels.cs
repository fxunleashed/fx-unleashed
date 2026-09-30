using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The wheels the plugin knows, and everything that differs between them: how they show up on USB and in SimPro,
    /// whether they have a screen, and their LEDs (count, groups, encoders). Everything else asks the model instead of
    /// assuming an FX Pro (docs/gt-neo-plan.md).
    /// </summary>
    public sealed class WheelModel
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        /// <summary>The HID path filter of its own USB device.</summary>
        public string UsbFilter { get; private set; }
        /// <summary>SimPro's product_uuid for it (get_device_list).</summary>
        public string SimProProduct { get; private set; }
        public bool HasScreen { get; private set; }
        /// <summary>USB mode needs the FXProDashes wheel firmware (FX Pro). The GT Neo's USB mode is stock.</summary>
        public bool NeedsFirmware { get; private set; }
        /// <summary>What the settings page calls USB mode on this wheel.</summary>
        public string ModeName { get; private set; }
        public int LedCount { get; private set; }
        /// <summary>The groups it has, in the order the settings page lists them.</summary>
        public LedGroup[] Groups { get; private set; }
        /// <summary>Its encoder lights sit on ABS, TC, BB, DIFF, MAP, so they can show those levels (LightEffect.Levels).</summary>
        public bool HasLevels { get; private set; }
        /// <summary>Button bits in its input report 01 (bytes 3-7).</summary>
        public int ButtonCount { get; private set; } = 40;

        private Dictionary<LedGroup, int[][]> segments = new Dictionary<LedGroup, int[][]>();
        private Dictionary<LedGroup, int[]> alertLeds = new Dictionary<LedGroup, int[]>();
        private Dictionary<LedGroup, string> groupNames = new Dictionary<LedGroup, string>();

        /// <summary>A group's LEDs in effect order (empty when the wheel doesn't have it).</summary>
        public int[] Leds(LedGroup g) => segments.TryGetValue(g, out var s) ? s.SelectMany(x => x).ToArray() : new int[0];

        /// <summary>
        /// A group's LEDs in pieces that each run an effect of their own: one piece on the FX Pro; on the GT Neo each
        /// ring of 12 around an encoder (a rainbow spins around each ring rather than across all four).
        /// </summary>
        public int[][] Segments(LedGroup g) => segments.TryGetValue(g, out var s) ? s : new int[0][];

        /// <summary>
        /// The LEDs an alert on this group lights: the group's own, or a stand-in when the wheel lacks it (the GT Neo has
        /// no side lights; its alerts for them use the ends of the rev bar).
        /// </summary>
        public int[] AlertLeds(LedGroup g) => alertLeds.TryGetValue(g, out var l) ? l : Leds(g);

        public bool Has(LedGroup g) => segments.ContainsKey(g);

        /// <summary>The groups an alert can use here: its own, then the stand-ins (GT Neo: the ends of the rev bar).</summary>
        public LedGroup[] AlertGroups => Groups.Concat(alertLeds.Keys.Where(g => !segments.ContainsKey(g))).ToArray();

        public string GroupName(LedGroup g) => groupNames.TryGetValue(g, out var n) ? n : LightPresets.GroupName(g);

        /// <summary>The group an LED belongs to (null = none).</summary>
        public LedGroup? GroupOf(int led)
        {
            foreach (var kv in segments)
                if (kv.Value.Any(s => s.Contains(led))) return kv.Key;
            return null;
        }

        public override string ToString() => Name;

        // ---------- The wheels ----------

        /// <summary>
        /// FX Pro, 38 LEDs as its firmware counts them: buttons 0-11, encoders 12-16 (ABS, TC, BB, DIFF, MAP), lights
        /// beside the rev bar 17-19 left / 20-22 right, rev lights 23-37 (left to right).
        /// </summary>
        public static readonly WheelModel FxPro = new WheelModel
        {
            Id = "fxpro", Name = "FX Pro", UsbFilter = FxUsb.DeviceFilter, SimProProduct = "0000000002030000",
            HasScreen = true, NeedsFirmware = true, ModeName = "Unleashed", LedCount = 38, HasLevels = true,
            Groups = new[] { LedGroup.Buttons, LedGroup.Encoders, LedGroup.SideLeft, LedGroup.SideRight, LedGroup.Rev },
            segments =
            {
                [LedGroup.Buttons] = new[] { Range(0, 12) },
                [LedGroup.Encoders] = new[] { Range(12, 5) },
                [LedGroup.SideLeft] = new[] { new[] { 17, 18, 19 } },
                [LedGroup.SideRight] = new[] { new[] { 20, 21, 22 } },
                [LedGroup.Rev] = new[] { Range(23, 15) },
            },
        };

        /// <summary>
        /// GT Neo on its own USB (powered up holding button 3), 73 LEDs as its host RGB command numbers them (the same
        /// order SimHub's own GT Neo device uses): 10 button lights 0-9 (0-4 the right grip from the bottom up, 5-9 the
        /// left grip from the top down), four rings of 12 around the encoders 10-21 upper left, 22-33 upper right, 34-45
        /// lower left, 46-57 lower right (each from 12 o'clock, clockwise), rev lights 58-72 (left to right). Checked on
        /// the user's wheel 2026-09-29. FXProDashes docs/gt-neo-firmware.md.
        /// </summary>
        public static readonly WheelModel GtNeo = new WheelModel
        {
            Id = "gtneo", Name = "GT Neo", UsbFilter = NeoUsb.DeviceFilter, SimProProduct = "0000000002060000",
            HasScreen = false, NeedsFirmware = false, ModeName = "USB", LedCount = 73, HasLevels = false,
            Groups = new[] { LedGroup.Buttons, LedGroup.Encoders, LedGroup.Rev },
            segments =
            {
                [LedGroup.Buttons] = new[] { Range(0, 10) },
                [LedGroup.Encoders] = new[] { Range(10, 12), Range(22, 12), Range(34, 12), Range(46, 12) },
                [LedGroup.Rev] = new[] { Range(58, 15) },
            },
            alertLeds =
            {
                [LedGroup.SideLeft] = Range(58, 4),
                [LedGroup.SideRight] = Range(69, 4),
            },
            groupNames =
            {
                [LedGroup.Encoders] = "Encoder rings",
                [LedGroup.SideLeft] = "Left end of the rev lights",
                [LedGroup.SideRight] = "Right end of the rev lights",
            },
        };

        public static readonly WheelModel[] All = { FxPro, GtNeo };

        /// <summary>A model by id; the FX Pro for anything unknown (settings from before GT Neo support).</summary>
        public static WheelModel Find(string id) => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? FxPro;

        /// <summary>The model SimPro reports by its product_uuid, or null.</summary>
        public static WheelModel BySimPro(string productUuid) => All.FirstOrDefault(m => m.SimProProduct == productUuid);

        private static int[] Range(int start, int count) => Enumerable.Range(start, count).ToArray();
    }
}
