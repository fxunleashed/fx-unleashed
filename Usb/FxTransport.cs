using System;
using System.Linq;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The Simagic FX (not Pro) over its own USB cable: stock app 1.3.5, the only FX app SimPro has
    /// (FX_App-V1.3.5.0-00000135.sfu). Same USB id and the same `F2 0A` RAM store as the FX Pro (FxConnection), but every
    /// address differs. Everything here was worked out from the 1.3.5 app and tried on an owner's wheel with an FX wheel
    /// test (2026-10-07).
    ///
    /// The stock app's LED renderer (every 10 ms, USB mode too) reads its colours from RAM, so the plugin
    /// drives all 20 LEDs with no firmware change: one palette colour per LED (8 colours + off) and one brightness for
    /// all. The FX Unleashed patch (build 1, F1 "lights") adds any RGB and a brightness per LED; it answers a query with
    /// its build in status bytes 0x11-0x13 (F4).
    /// </summary>
    internal static class FxWheel
    {
        // ---- stock app 1.3.5 RAM (read by its LED renderer)
        /// <summary>15 bytes: logical LEDs 0-11 (buttons), 12-14 (dials). High nibble palette colour, low nibble trigger
        /// (0 = always; else steady unless that alert is on).</summary>
        public const uint LedTable = 0x200005D0;
        /// <summary>Brightness (0-100, default 60), then the rev table: byte [1 + pattern + led * 10] for rev LEDs 0-4,
        /// patterns 0-9. High nibble colour, bit 3 blink, bits 0-2 trigger. Only pattern 0 is used over USB.</summary>
        public const uint BrightnessAndRev = 0x200005F8;
        /// <summary>Byte 2 = the rev pattern shown (10+ = off). Written by the radio only, so 0 over USB (RAM is zeroed at
        /// every start); bytes 0-1 and 3 are radio stores nothing reads.</summary>
        public const uint RevPatternWord = 0x200002BC;
        /// <summary>Byte 1: alert bits (ABS, TC, pit limiter, DRS); bytes 0 and 2 radio stores, 3 unused.</summary>
        public const uint AlertWord = 0x200002E4;
        /// <summary>Byte 3: race flag; bytes 0-2 radio stores.</summary>
        public const uint FlagWord = 0x200002C8;
        /// <summary>
        /// Byte 0 bit 0: "last input report collected". The main loop sends a report, arms the endpoint and only then
        /// clears the flag; when the "collected" interrupt (the only place that sets it) lands between the last two, its
        /// set is wiped and no report is ever sent again: seen on
        /// the owner's wheel with no reconnect at all (2026-10-07, 19 minutes of dead buttons). Byte 1 unused, bytes 2-3
        /// the shift-register buttons, rebuilt every scan, so writing 1 is harmless (tool test 3).
        /// </summary>
        public const uint InputReadyWord = 0x20000280;

        /// <summary>The app's defaults for the LED table: what the wheel shows with no saved setup.
        /// Written back when the plugin lets go of the LEDs (the wheel's own saved colours come back at its next start).</summary>
        public static readonly byte[] DefaultLedTable = Hex("70401050304044333010204031628010");

        // ---- FX Unleashed patch (build 1): its control block (RAM there checked on the owner's wheel)
        public const uint Ctrl = 0x20002400;
        private const uint LedMode = Ctrl, PalMirror = Ctrl + 0x10, PerLed = Ctrl + 0x60, QueryAt = Ctrl + 0xC8;
        public const uint LedMagic = 0x46584C31;  // 'FXL1'; +4 = every LED from PerLed
        private const uint QueryMagic = 0x46585131; // 'FXQ1'

        /// <summary>Both stock palettes as the app has them (2 x 10 entries [B, R, G, 0]): seeded into the patch's
        /// mirror before any LED mode, so no frame ever shows unseeded RAM.</summary>
        public static readonly byte[] StockPalettes = Hex(
            "000000000082000000dc320000d26e00001eaa00a0030000dc1d8b00ff4a0000949ba00000000000" +
            "000000000082000000a93c0000e6a000001eaa00a0030000dc1d8b00ff7200009494a00000000000");

        /// <summary>The app's brightness curve: percent 0-100 -> LED level 5-255.</summary>
        public static readonly byte[] Curve = Hex(
            "05060607070809090a0b0b0c0d0e0f101011121415161718191a1c1d1e202123242628292b2d2e30323436383a3c3e404345474a4c4f5154" +
            "56595c5e6164676a6d7073767a7d8084878a8e9295999da1a5a9adb1b5b9bdc1c6cacfd3d8dde1e6ebf0f5faff");

        public const int Leds = 20, ButtonAndDialLeds = 15, RevLeds = 5;

        /// <summary>Palette colours (both tables): 0 and 9 off.</summary>
        public const byte Off = 0, Red = 1, Orange = 2, Yellow = 3, Green = 4, Blue = 5, Cyan = 6, Purple = 7, White = 8;

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>Lets a wheel whose button reports stopped send again (see InputReadyWord).</summary>
        public static void ReleaseInputReports(FxConnection c)
        {
            try { c.WriteRam(InputReadyWord, BitConverter.GetBytes(1u)); } catch { }
        }

        /// <summary>
        /// The patch build the FX runs: the patch answers 'FXQ1' at Ctrl+0xC8 by writing build, 'F', 'X' into status bytes
        /// 0x11-0x13 and clearing the query. 0 = stock (the word is
        /// unused RAM there, cleared again afterwards). Null = couldn't ask. Only for an FX in its app (IsFxApp).
        /// </summary>
        public static int? QueryBuild(string path)
        {
            try
            {
                using (var c = new FxConnection(path))
                {
                    try
                    {
                        c.WriteRam(QueryAt, BitConverter.GetBytes(QueryMagic));
                        // the main loop answers on its next pass
                        for (int i = 0; i < 3; i++)
                        {
                            Thread.Sleep(100);
                            var st = FxUsb.ReadStatus(path);
                            if (st?.Raw == null) return null;
                            if (st.Raw[0x12] == 'F' && st.Raw[0x13] == 'X' && st.Raw[0x11] > 0) return st.Raw[0x11];
                        }
                        return 0;
                    }
                    finally { try { c.WriteRam(QueryAt, BitConverter.GetBytes(0u)); } catch { } }
                }
            }
            catch { return null; }
        }

        /// <summary>The nearest palette colour (by hue; grey and white = white). Black is the caller's call.</summary>
        public static byte Snap(byte r, byte g, byte b)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max <= 0) return Off;
            double sat = (max - min) / max;
            if (sat < 0.28) return White;
            double h;
            if (max == r) h = 60 * ((g - b) / (max - min));
            else if (max == g) h = 60 * ((b - r) / (max - min) + 2);
            else h = 60 * ((r - g) / (max - min) + 4);
            if (h < 0) h += 360;
            if (h < 15 || h >= 335) return Red;
            if (h < 42) return Orange;
            if (h < 75) return Yellow;
            if (h < 165) return Green;
            if (h < 195) return Cyan;
            if (h < 255) return Blue;
            return Purple;
        }

        /// <summary>What an LED shows, 0-90 (its colour's strongest channel x its brightness).</summary>
        public static double Level(LedColor c) => Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0 * c.Brightness;

        /// <summary>The wheel's brightness percent (0-100) for a plugin level (0-90, the FX Pro's scale).</summary>
        public static int Percent(double level) => (int)Math.Round(Math.Max(0, Math.Min(90, level)) * 100 / 90);

        /// <summary>The stock table bytes for a frame: 15 LED bytes (+1 unused) and the brightness + rev block (44 bytes,
        /// pattern 0 of rev LEDs 0-4). One brightness for all: the brightest LED's; LEDs much dimmer than it are off.</summary>
        public static (byte[] table, byte[] rev) StockFrame(LedColor[] frame, byte ceiling)
        {
            double top = 0;
            for (int i = 0; i < Leds && i < frame.Length; i++) top = Math.Max(top, Level(frame[i]));
            top = Math.Min(top, ceiling);
            double cut = Math.Max(1.5, top * 0.12);
            byte Colour(int i) => i < frame.Length && Level(frame[i]) >= cut ? Snap(frame[i].R, frame[i].G, frame[i].B) : Off;
            var table = new byte[16];
            for (int i = 0; i < ButtonAndDialLeds; i++) table[i] = (byte)(Colour(i) << 4);
            var rev = new byte[44];
            rev[0] = (byte)Percent(top);
            for (int k = 0; k < RevLeds; k++) rev[1 + k * 10] = (byte)(Colour(ButtonAndDialLeds + k) << 4);
            return (table, rev);
        }

        /// <summary>The patch's per-LED table (Ctrl+0x60): 20 x [B, R, G, level], level from the app's curve (never 0, which
        /// would mean "the wheel's own").</summary>
        public static byte[] RgbFrame(LedColor[] frame, byte ceiling)
        {
            var b = new byte[Leds * 4];
            for (int i = 0; i < Leds && i < frame.Length; i++)
            {
                var c = frame[i];
                b[i * 4] = c.B; b[i * 4 + 1] = c.R; b[i * 4 + 2] = c.G;
                b[i * 4 + 3] = Curve[Percent(Math.Min(ceiling, Math.Max((byte)1, c.Brightness)))];
            }
            return b;
        }

        /// <summary>Gives the LEDs back: the firmware's default colours, rev lights off, its default brightness.</summary>
        public static void RestoreDefaults(FxConnection c)
        {
            c.WriteRam(LedTable, DefaultLedTable);
            var rev = new byte[44];
            rev[0] = 60;
            c.WriteRam(BrightnessAndRev, rev);
        }

        /// <summary>No alert or flag overlays from the firmware while the plugin draws, and rev pattern 0 shown.</summary>
        public static void ClearOverlays(FxConnection c)
        {
            c.WriteRam(AlertWord, new byte[4]);
            c.WriteRam(FlagWord, new byte[4]);
            c.WriteRam(RevPatternWord, new byte[4]);
        }
    }

    /// <summary>
    /// The stock FX's LEDs (no patch): palette colours and one brightness, written into the app's own tables (2 stores
    /// per changed frame, everything again every 2 s). Effects that only dim (breathing) dim the whole wheel; colours snap
    /// to the nearest of the 8.
    /// </summary>
    internal sealed class FxPaletteLedLink : ILedLink
    {
        private readonly FxConnection c;
        private byte[] lastTable, lastRev;
        private DateTime nextFull;

        public FxPaletteLedLink(FxConnection c) { this.c = c; }

        public void Enable()
        {
            FxWheel.ClearOverlays(c);
            lastTable = lastRev = null;
        }

        public void Send(LedColor[] frame, byte ceiling)
        {
            var (table, rev) = FxWheel.StockFrame(frame, ceiling);
            bool full = DateTime.UtcNow >= nextFull;
            if (full) nextFull = DateTime.UtcNow.AddSeconds(2);
            if (full || lastTable == null || !table.SequenceEqual(lastTable)) c.WriteRam(FxWheel.LedTable, table);
            if (full || lastRev == null || !rev.SequenceEqual(lastRev)) c.WriteRam(FxWheel.BrightnessAndRev, rev);
            lastTable = table; lastRev = rev;
        }

        public void Disable() => FxWheel.RestoreDefaults(c);
    }

    /// <summary>
    /// The FX with the FX Unleashed patch (build 1+): any RGB and a level per LED (patch mode 'FXL1'+4, all LEDs from
    /// Ctrl+0x60). The app's tables are set to white so its renderer draws every LED through the patched colour lookup.
    /// A power cycle clears the mode (the wheel's own lights again).
    /// </summary>
    internal sealed class FxRgbLedLink : ILedLink
    {
        private readonly FxConnection c;
        private byte[] last;
        private DateTime nextFull;

        public FxRgbLedLink(FxConnection c) { this.c = c; }

        /// <summary>Mirror and table first, then the mode, so no frame shows unseeded RAM.</summary>
        public void Enable()
        {
            c.WriteRam(Ctrl(0), new byte[4]);
            c.WriteRamBlock(FxWheel.Ctrl + 0x10, FxWheel.StockPalettes);
            c.WriteRamBlock(FxWheel.Ctrl + 0x60, new byte[FxWheel.Leds * 4]);
            FxWheel.ClearOverlays(c);
            var table = Enumerable.Repeat((byte)(FxWheel.White << 4), 16).ToArray();
            table[15] = 0;
            c.WriteRam(FxWheel.LedTable, table);
            var rev = new byte[44];
            rev[0] = 100;
            for (int k = 0; k < FxWheel.RevLeds; k++) rev[1 + k * 10] = FxWheel.White << 4;
            c.WriteRam(FxWheel.BrightnessAndRev, rev);
            c.WriteRam(Ctrl(0), BitConverter.GetBytes(FxWheel.LedMagic + 4));
            last = null;
        }

        private static uint Ctrl(uint offset) => FxWheel.Ctrl + offset;

        public void Send(LedColor[] frame, byte ceiling)
        {
            var b = FxWheel.RgbFrame(frame, ceiling);
            bool full = DateTime.UtcNow >= nextFull;
            if (full) nextFull = DateTime.UtcNow.AddSeconds(2);
            if (!full && last != null && b.SequenceEqual(last)) return;
            c.WriteRamBlock(FxWheel.Ctrl + 0x60, b);
            last = b;
        }

        public void Disable()
        {
            c.WriteRam(Ctrl(0), new byte[4]);
            FxWheel.RestoreDefaults(c);
        }
    }
}
