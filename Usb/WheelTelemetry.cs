using System;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Feeds the wheel's own dashes over USB. With its USB cable in, the FX Pro runs in USB mode and skips its RF
    /// receive path, so SimPro's telemetry never reaches it and its built-in dashes stand still. But its page task still
    /// draws every widget from a block of RAM variables (0x20000300-0x2000036B) that the RF handler normally fills
    /// (FXPro_App 1.3.11 `FUN_0802ed14`, RF ids 0x3D-0x4D), and RAM can be written over USB (F2 0A). So this builds
    /// that block from SimPro's telemetry layout (filled by SimHubFeedMapper or DemoCar) and the plugin writes it
    /// 10 times a second: the wheel then draws its dashes natively, in the units set on the wheel.
    /// Layout from the widget senders (FXProDashes docs/firmware-notes.md "Wheel dashes over USB").
    /// </summary>
    internal static class WheelTelemetry
    {
        public const uint Address = 0x20000300;
        public const int Length = 0x6C; // up to 0x2000036B, whole words

        /// <summary>Forces the flag number (designer API, for checking the codes on the wheel).</summary>
        public static int? TestFlag;

        public static byte[] Build(SimProTelemetry t)
        {
            var b = new byte[Length];
            void U8(int a, double v) => b[a - 0x300] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
            void U16(int a, double v)
            {
                int x = (int)Math.Max(0, Math.Min(65535, Math.Round(v)));
                b[a - 0x300] = (byte)x; b[a - 0x300 + 1] = (byte)(x >> 8);
            }
            void I16(int a, double v)
            {
                int x = (int)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v)));
                b[a - 0x300] = (byte)x; b[a - 0x300 + 1] = (byte)(x >> 8);
            }
            void Time(int a, double ms)
            {
                if (ms < 0) ms = 0;
                int total = (int)ms;
                U16(a, total / 60000); U8(a + 2, total / 1000 % 60); U16(a + 4, total % 1000);
            }
            double G(string f) { try { return t.Get(f); } catch { return 0; } }

            // 0x300 steering angle (settings page only), 0x306 rev LED stage: left 0
            U16(0x302, Math.Min(511, G("speed")));                      // spd, km/h (the wheel converts to mph)
            U16(0x304, Math.Min(32767, G("rpm")));                      // rpm, rpmbar (= rpm / 100)
            int gear = (int)G("gear");
            U8(0x308, gear < 0 ? 0 : Math.Min(15, gear + 1));           // 0 = R, 1 = N, 2 = 1st...
            U8(0x309, Math.Min(15, G("absLevel")));                     // abs
            U8(0x30A, G("tcLevel"));                                    // tc
            U8(0x30C, G("throttle"));                                   // th, %
            U8(0x30D, G("brake"));                                      // brk, %
            U8(0x30E, 100 - G("clutch"));                               // cl (shown as 100 - value)
            I16(0x310, G("gainLoss") * 100);                            // gl / gain / loss, 1/100 s
            U8(0x312, G("completedLaps"));                              // lap
            U8(0x313, TestFlag ?? FlagCode(G));                         // flag
            U16(0x316, G("turbo"));                                     // tb
            U8(0x318, G("engineMap"));                                  // map (0 = "NA")
            U16(0x31A, Math.Min(1023, G("brakeBias") * 10));            // bias, 1/10 %
            U8(0x31C, Math.Min(15, G("ersMode")));                      // ersm
            U8(0x31D, G("ersPercent"));                                 // soc, batt
            U8(0x31E, G("frontAntiRollBar"));                           // farb
            U8(0x31F, G("rearAntiRollBar"));                            // rarb
            U8(0x320, G("pushToPass"));                                 // p2p
            U8(0x321, G("fuelPerLap"));                                 // fxl, 1/10 l (the mapper sends x10)
            U8(0x322, G("diffAdjOnThrottle"));                          // diff
            U8(0x323, G("throttleShape"));                              // tps
            U16(0x324, G("gapAhead"));                                  // gapa, 1/100 s (the mapper sends x100)
            U16(0x326, G("gapBehind"));                                 // gapb
            U8(0x328, G("diffEntry"));                                  // entr
            U8(0x329, G("diffMiddle"));                                 // mid
            U8(0x32A, G("diffExit"));                                   // hspd
            U8(0x32B, G("engineBraking"));                              // eb
            U8(0x32C, G("tcCut"));                                      // tc2
            U8(0x32D, (G("isDrsEnabled") != 0 ? 8 : 0) | (G("isPitLimiterOn") != 0 ? 4 : 0)); // drs, pit icons
            U8(0x32E, G("oilTemperature"));                             // ot, deg C
            U8(0x32F, G("oilPressure"));                                // op, psi
            U8(0x330, G("waterTemperature"));                           // wt, deg C
            U8(0x331, G("position"));                                   // pos
            U16(0x332, Math.Min(1023, G("fuel") * 10));                 // fuel, 1/10 l
            Time(0x334, G("currentLapTime"));                           // trun
            Time(0x33A, G("lastLapTime"));                              // tlast
            Time(0x340, G("bestLapTime"));                              // tbest
            for (int c = 0; c < 4; c++)
            {
                U8(0x346 + c, G("tyreTemperature" + c));                // tt_*, deg C
                U8(0x34A + c, G("tyreTemperatureInner" + c));           // tti_*
                U8(0x34E + c, G("tyreTemperatureMiddle" + c));          // ttm_*
                U8(0x352 + c, G("tyreTemperatureOuter" + c));           // tto_*
                U16(0x356 + c * 2, Math.Min(1023, G("brakeTemperature" + c))); // bt_*
                U16(0x35E + c * 2, Math.Min(1023, G("tyrePressure" + c) * 10)); // tp_*, psi x10
                U8(0x366 + c, G("tyreWear" + c));                       // tw_*, twp_*, %
            }
            return b;
        }

        /// <summary>The wheel's flag number (the screen's `flag=` variable); the most urgent flag wins.</summary>
        public static int FlagCode(Func<string, double> g)
        {
            if (g("blackFlag") != 0) return FlagCodes.Black;
            if (g("yellowFlag") != 0) return FlagCodes.Yellow;
            if (g("blueFlag") != 0) return FlagCodes.Blue;
            if (g("whiteFlag") != 0) return FlagCodes.White;
            if (g("greenFlag") != 0) return FlagCodes.Green;
            return 0; // the wheel has no chequered flag panel
        }
    }

    /// <summary>Flag numbers the wheel's screen shows (checked with a camera on the wheel, 2026-09-27; 0 and 6+ = none).</summary>
    internal static class FlagCodes
    {
        public const int Green = 1, Blue = 2, Yellow = 3, Black = 4, White = 5;
    }
}
