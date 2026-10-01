using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Every control on the FX Pro and the controller buttons it sends over USB, with where it is on the Wheel tab's
    /// drawing (WheelView coordinates, 663 x 396). Mapped on the user's wheel with a guided button capture on 2026-09-30
    /// (FXProDashes firmware-notes.md "Buttons"), positions measured on SimPro's front picture of the wheel.
    /// Stock 1.3.11 uses 38 of the report's 40 buttons. The dash button (bottom of the right lower cluster) and the
    /// two upper paddles are sent by the patched firmware on buttons the plugin chooses (UsbSettings.DashSlot /
    /// UpperPaddleA / UpperPaddleB): 41-48 with wheel app build 9, which declares 48 buttons; on build 8 they have to
    /// share one of the 40.
    /// </summary>
    public static class FxProControls
    {
        public enum Kind { Button, Knob, RollerSideways, RollerUpDown, Funky, Paddle, Shift, Clutch }

        /// <summary>Which way a press went, for the Wheel tab's animation.</summary>
        public enum Dir { Press, Clockwise, Anticlockwise, Up, Down, Left, Right }

        public sealed class Control
        {
            public string Id, Name;
            public Kind Kind;
            public double X, Y;
            /// <summary>The LED on this control (buttons and knobs), or -1.</summary>
            public int Led = -1;
            public override string ToString() => Name;
        }

        private const double Mid = 331.5;

        private static Control C(string id, string name, Kind kind, double x, double y, int led = -1) =>
            new Control { Id = id, Name = name, Kind = kind, X = x, Y = y, Led = led };

        // Buttons sit on WheelView's button LEDs: left 0-5 = bottom, middle, inner-high, outer-high, top outer, top inner;
        // right 6-11 = top inner, top outer, outer-high, inner-high, middle, bottom (the dash button).
        public static readonly Control[] All =
        {
            C("l-top-outer", "Left top outer button", Kind.Button, 101.7, 61.6, 4),
            C("l-top-inner", "Left top inner button", Kind.Button, 167.4, 76.1, 5),
            C("l-outer-high", "Left outer-high button", Kind.Button, 151.8, 217.5, 3),
            C("l-inner-high", "Left inner-high button", Kind.Button, 192.9, 245.3, 2),
            C("l-middle", "Left middle button", Kind.Button, 174.0, 284.3, 1),
            C("l-bottom", "Left bottom button", Kind.Button, 203.0, 325.5, 0),
            C("r-top-inner", "Right top inner button", Kind.Button, 2 * Mid - 167.4, 76.1, 6),
            C("r-top-outer", "Right top outer button", Kind.Button, 2 * Mid - 101.7, 61.6, 7),
            C("r-outer-high", "Right outer-high button", Kind.Button, 2 * Mid - 151.8, 217.5, 8),
            C("r-inner-high", "Right inner-high button", Kind.Button, 2 * Mid - 192.9, 245.3, 9),
            C("r-middle", "Right middle button", Kind.Button, 2 * Mid - 174.0, 284.3, 10),
            C("dash", "Dash button", Kind.Button, 2 * Mid - 203.0, 325.5, 11),
            C("abs", "ABS knob", Kind.Knob, 260.8, 287.6, 12),
            C("tc", "TC knob", Kind.Knob, 402.2, 287.6, 13),
            C("bb", "BB knob", Kind.Knob, Mid, 239.7, 14),
            C("diff", "DIFF knob", Kind.Knob, 251.9, 217.5, 15),
            C("map", "MAP knob", Kind.Knob, 411.1, 217.5, 16),
            C("l-outer-roller", "Left outer roller", Kind.RollerSideways, 62.0, 119.0),
            C("l-inner-roller", "Left inner roller", Kind.RollerUpDown, 165.5, 154.5),
            C("r-inner-roller", "Right inner roller", Kind.RollerUpDown, 2 * Mid - 165.5, 154.5),
            C("r-outer-roller", "Right outer roller", Kind.RollerSideways, 2 * Mid - 62.0, 119.0),
            C("funky", "Funky switch", Kind.Funky, Mid, 318.0),
            // behind the wheel, top to bottom: the upper paddles and the shift paddles show through the thumb openings, the
            // clutch paddles below the grips
            C("l-paddle", "Left upper paddle", Kind.Paddle, 125.0, 115.0),
            C("r-paddle", "Right upper paddle", Kind.Paddle, 2 * Mid - 125.0, 115.0),
            C("l-shift", "Left shift paddle", Kind.Shift, 125.0, 163.0),
            C("r-shift", "Right shift paddle", Kind.Shift, 2 * Mid - 125.0, 163.0),
            C("l-clutch", "Left clutch paddle", Kind.Clutch, 114.0, 286.0),
            C("r-clutch", "Right clutch paddle", Kind.Clutch, 2 * Mid - 114.0, 286.0),
        };

        public static Control ById(string id) => All.First(c => c.Id == id);

        /// <summary>Stock 1.3.11: button -> control and direction (the user's capture, 2026-09-30).</summary>
        private static readonly Dictionary<int, (string Id, Dir Dir)> Stock = new Dictionary<int, (string, Dir)>
        {
            [7] = ("l-top-outer", Dir.Press), [8] = ("l-top-inner", Dir.Press), [6] = ("l-outer-high", Dir.Press),
            [5] = ("l-inner-high", Dir.Press), [19] = ("l-middle", Dir.Press), [20] = ("l-bottom", Dir.Press),
            [3] = ("r-top-inner", Dir.Press), [4] = ("r-top-outer", Dir.Press), [2] = ("r-outer-high", Dir.Press),
            [1] = ("r-inner-high", Dir.Press), [21] = ("r-middle", Dir.Press),
            [22] = ("diff", Dir.Clockwise), [23] = ("diff", Dir.Anticlockwise),
            [32] = ("map", Dir.Clockwise), [31] = ("map", Dir.Anticlockwise),
            [18] = ("bb", Dir.Clockwise), [17] = ("bb", Dir.Anticlockwise),
            [12] = ("abs", Dir.Clockwise), [11] = ("abs", Dir.Anticlockwise),
            [10] = ("tc", Dir.Clockwise), [9] = ("tc", Dir.Anticlockwise),
            [34] = ("l-outer-roller", Dir.Right), [33] = ("l-outer-roller", Dir.Left),
            [37] = ("l-inner-roller", Dir.Up), [38] = ("l-inner-roller", Dir.Down),
            // the right side checked live on the Wheel tab (2026-09-30): 3/4 and the two rollers were the other way round
            // in the capture; which way round each roller's pair goes is still to be confirmed
            [36] = ("r-outer-roller", Dir.Right), [35] = ("r-outer-roller", Dir.Left),
            [39] = ("r-inner-roller", Dir.Up), [40] = ("r-inner-roller", Dir.Down),
            [29] = ("funky", Dir.Up), [28] = ("funky", Dir.Down), [25] = ("funky", Dir.Left), [30] = ("funky", Dir.Right),
            [26] = ("funky", Dir.Press),
            // the funky switch also turns (seen in a capture between the TC knob and its directions; which way is which
            // isn't confirmed yet)
            [15] = ("funky", Dir.Clockwise), [16] = ("funky", Dir.Anticlockwise),
            // the shift paddles (checked live on the Wheel tab, 2026-09-30; an earlier note had these as the clutch paddles
            // "pulled fully": the clutch paddles only move the report's two axes)
            [14] = ("l-shift", Dir.Press), [13] = ("r-shift", Dir.Press),
        };

        /// <summary>The buttons the patched firmware sends for the dash button and the upper paddles.</summary>
        public static readonly string[] Movable = { "dash", "l-paddle", "r-paddle" };

        /// <summary>Stock controls that already use a button (1-40), and the buttons 24/27 that stock only uses with
        /// the clutch paddles in button mode.</summary>
        public static bool IsStockButton(int b) => Stock.ContainsKey(b);

        /// <summary>Buttons 24 and 27: the clutch paddles' button-mode buttons, free while they're axes.</summary>
        public static bool IsClutchButtonMode(int b) => b == 24 || b == 27;

        /// <summary>What a button means on this wheel now: a stock control, or one of the three the plugin places.</summary>
        public static (Control Control, Dir Dir)? Lookup(int button, UsbSettings s)
        {
            if (s != null)
            {
                if (button == s.DashSlot) return (ById("dash"), Dir.Press);
                if (s.UpperPaddles && button == s.UpperPaddleA) return (ById("l-paddle"), Dir.Press);
                if (s.UpperPaddles && button == s.UpperPaddleB) return (ById("r-paddle"), Dir.Press);
                if (s.ClutchMode == 2 && button == 24) return (ById("l-clutch"), Dir.Press);
                if (s.ClutchMode == 2 && button == 27) return (ById("r-clutch"), Dir.Press);
            }
            return Stock.TryGetValue(button, out var m) ? (ById(m.Id), m.Dir) : ((Control, Dir)?)null;
        }

        /// <summary>A short name for a button ("Left top outer button", "BB knob clockwise").</summary>
        public static string Describe(int button, UsbSettings s)
        {
            var m = Lookup(button, s);
            if (m == null) return null;
            var (c, d) = m.Value;
            switch (d)
            {
                case Dir.Clockwise: return c.Name + " clockwise";
                case Dir.Anticlockwise: return c.Name + " anticlockwise";
                case Dir.Up: return c.Name + " up";
                case Dir.Down: return c.Name + " down";
                case Dir.Left: return c.Name + (c.Kind == Kind.Funky ? " left" : " to the left");
                case Dir.Right: return c.Name + (c.Kind == Kind.Funky ? " right" : " to the right");
                default: return c.Kind == Kind.Funky ? "Funky switch push" : c.Name;
            }
        }

        /// <summary>The stock control using a button (1-40), for the slot picker; null if free. With the clutch paddles
        /// in button mode, 24/27 are theirs.</summary>
        public static string StockOwner(int button, UsbSettings s = null)
        {
            if (Stock.TryGetValue(button, out _)) return Describe(button, null);
            if (s?.ClutchMode == 2 && IsClutchButtonMode(button)) return button == 24 ? "Left clutch paddle (button mode)" : "Right clutch paddle (button mode)";
            return null;
        }

        /// <summary>Highest button the wheel's firmware reports: 48 on build 9+, else 40.</summary>
        public static int MaxButton(int build) => build >= 9 ? 48 : 40;

        /// <summary>The defaults for the dash button and the upper paddles (left, right) on a build.</summary>
        public static (int Dash, int A, int B) Defaults(int build) => build >= 9 ? (41, 42, 43) : (36, 24, 27);

        /// <summary>
        /// Makes the three placed buttons valid for the wheel's build: in range, and no two the same. Settings made for
        /// build 8 (36 / 24 / 27, the old defaults) move to build 9's own buttons once the wheel runs build 9.
        /// Returns true when something changed.
        /// </summary>
        public static bool Normalize(UsbSettings s, int build)
        {
            if (s == null || build < 8) return false;
            int max = MaxButton(build);
            var (dd, da, db) = Defaults(build);
            var before = (s.DashSlot, s.UpperPaddleA, s.UpperPaddleB);
            if (build >= 9 && s.DashSlot == 36 && s.UpperPaddleA == 24 && s.UpperPaddleB == 27)
            {
                s.DashSlot = dd; s.UpperPaddleA = da; s.UpperPaddleB = db;
            }
            if (s.DashSlot < 1 || s.DashSlot > max) s.DashSlot = dd;
            if (s.UpperPaddleA < 1 || s.UpperPaddleA > max) s.UpperPaddleA = da;
            if (s.UpperPaddleB < 1 || s.UpperPaddleB > max) s.UpperPaddleB = db;
            // the clutch paddles in button mode own 24 and 27
            if (s.ClutchMode == 2)
            {
                if (IsClutchButtonMode(s.DashSlot)) s.DashSlot = Free(s, build, dd);
                if (IsClutchButtonMode(s.UpperPaddleA)) s.UpperPaddleA = Free(s, build, da);
                if (IsClutchButtonMode(s.UpperPaddleB)) s.UpperPaddleB = Free(s, build, db);
            }
            if (s.UpperPaddleA == s.DashSlot) s.UpperPaddleA = Free(s, build, da);
            if (s.UpperPaddleB == s.DashSlot || s.UpperPaddleB == s.UpperPaddleA) s.UpperPaddleB = Free(s, build, db);
            return before != (s.DashSlot, s.UpperPaddleA, s.UpperPaddleB);
        }

        /// <summary>A button none of the three uses: the preferred one if free, else the first free one.</summary>
        private static int Free(UsbSettings s, int build, int preferred)
        {
            bool taken(int b) => b == s.DashSlot || b == s.UpperPaddleA || b == s.UpperPaddleB || (s.ClutchMode == 2 && IsClutchButtonMode(b));
            if (!taken(preferred)) return preferred;
            for (int b = MaxButton(build); b >= 1; b--) if (!taken(b) && !Stock.ContainsKey(b)) return b;
            for (int b = MaxButton(build); b >= 1; b--) if (!taken(b)) return b;
            return preferred;
        }
    }
}
