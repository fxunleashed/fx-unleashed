using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The GT Neo's own USB HID device (VID 3670, PID 0805). It appears when the wheel powers up with button 3 held
    /// (SimHub's GT Neo mode), through the quick release. Stock firmware; FXProDashes docs/gt-neo-firmware.md.
    ///  - Feature report `F0`, 64 bytes with the id, byte 6 = `EC`: host RGB. `EC 02 01/00` takes/releases the LEDs,
    ///    `EC 03 n` + n x (LED id, R, G, B) sets up to 13 LEDs. Without an `EC` packet for 5 s the wheel takes its LEDs
    ///    back by itself.
    ///  - Never sent: `F0 [6]=00 [7]=CA` (hangs the wheel on purpose) and any `F1` report (firmware update).
    /// </summary>
    internal static class NeoUsb
    {
        public const string DeviceFilter = "vid_3670&pid_0805";

        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
        [DllImport("hid.dll")] private static extern bool HidD_GetSerialNumberString(SafeFileHandle h, byte[] b, int n);

        /// <summary>Its USB serial ("…-01010404-00010404"), or null. Read-only.</summary>
        public static string Serial(string path)
        {
            try
            {
                using (var h = FxUsb.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    var b = new byte[256];
                    return HidD_GetSerialNumberString(h, b, b.Length) ? Encoding.Unicode.GetString(b).TrimEnd('\0') : null;
                }
            }
            catch { return null; }
        }

        /// <summary>The firmware part of the serial ("01010404" for 1.4.4), shown on the settings page; null if unknown.</summary>
        public static string VersionText(string serial)
        {
            var parts = serial?.Split('-');
            if (parts == null || parts.Length < 3 || parts[1].Length != 8) return null;
            try
            {
                // the middle part's last three bytes: major, minor, patch (01 01 04 04 = 1.4.4)
                int major = Convert.ToInt32(parts[1].Substring(2, 2), 16), minor = Convert.ToInt32(parts[1].Substring(4, 2), 16), patch = Convert.ToInt32(parts[1].Substring(6, 2), 16);
                return $"{major}.{minor}.{patch}";
            }
            catch { return null; }
        }

        internal static bool SetFeature(SafeFileHandle h, byte[] report) => HidD_SetFeature(h, report, report.Length);
    }

    /// <summary>What a wheel's LEDs are driven through (FxLedLink, NeoLedLink).</summary>
    internal interface ILedLink
    {
        /// <summary>Takes the LEDs from the wheel's own lights.</summary>
        void Enable();
        /// <summary>One frame (the model's LED count); each LED's brightness is capped at `ceiling` (1-90).</summary>
        void Send(LedColor[] frame, byte ceiling);
        /// <summary>Gives the LEDs back to the wheel's own lights.</summary>
        void Disable();
    }

    /// <summary>The FX Pro's LEDs: the patched firmware's all-LEDs table (FxLedWriter).</summary>
    internal sealed class FxLedLink : ILedLink
    {
        private readonly FxLedWriter w;
        public FxLedLink(FxConnection c) { w = new FxLedWriter(c); }
        public void Enable() => w.Enable();
        public void Disable() => w.Disable();

        public void Send(LedColor[] frame, byte ceiling)
        {
            for (int i = 0; i < frame.Length && i < FxLedWriter.LedCount; i++)
                w.Set(i, frame[i].R, frame[i].G, frame[i].B, Math.Min(ceiling, Math.Max((byte)1, frame[i].Brightness)));
            w.Send();
        }
    }

    /// <summary>
    /// The GT Neo's LEDs through its stock host RGB command. Colours are scaled by brightness on the PC (the wheel has
    /// one brightness for all, set in SimPro). Only LEDs that changed are sent, 13 per report. Every second the link
    /// sends "host mode on" again and the whole frame: anything else that talks to the wheel (SimHub's own GT Neo device
    /// sends `EC 02 00` when it's switched off, after which the wheel ignores every `EC 03`) or leaves its colours behind
    /// is overruled within a second. That refresh also keeps the wheel from taking its LEDs back (it does after 5 s).
    /// </summary>
    internal sealed class NeoLedLink : ILedLink, IDisposable
    {
        public const int LedCount = 73;
        public const int PerReport = 13;
        /// <summary>How often "host mode on" and the whole frame go out again.</summary>
        public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);

        /// <summary>Sends one feature report (64 bytes, id F0 first). Swapped for a fake in the offline tests.</summary>
        private readonly Func<byte[], bool> write;
        private readonly IDisposable handle;
        private readonly int[] sent = new int[LedCount];
        private DateTime lastRefresh = DateTime.MinValue;
        private readonly Func<DateTime> clock;

        public NeoLedLink(string path)
        {
            var h = FxUsb.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // read/write, shared
            if (h.IsInvalid) throw new Exception("couldn't open the GT Neo (" + Marshal.GetLastWin32Error() + ")");
            handle = h;
            write = r => NeoUsb.SetFeature(h, r);
            clock = () => DateTime.UtcNow;
            Forget();
        }

        /// <summary>Tests: reports go to `write`, time comes from `clock`.</summary>
        internal NeoLedLink(Func<byte[], bool> write, Func<DateTime> clock)
        {
            this.write = write;
            this.clock = clock;
            Forget();
        }

        private void Forget() { for (int i = 0; i < sent.Length; i++) sent[i] = -1; }

        private static byte[] Report(byte cmd)
        {
            var r = new byte[64];
            r[0] = 0xF0; r[6] = 0xEC; r[7] = cmd;
            return r;
        }

        private void Write(byte[] r)
        {
            if (!write(r)) throw new Exception("the GT Neo didn't take a report (" + Marshal.GetLastWin32Error() + ")");
        }

        public void Enable()
        {
            var r = Report(2); r[8] = 1;
            Write(r);
            lastRefresh = clock();
            Forget(); // the first frame goes out whole
        }

        public void Disable()
        {
            var r = Report(2); r[8] = 0;
            try { Write(r); } catch { } // unplugged: it takes its lights back by itself
            Forget();
        }

        public void Send(LedColor[] frame, byte ceiling)
        {
            if (clock() - lastRefresh >= Refresh)
            {
                // host mode on again (something may have switched it off) and every LED again (something may have changed them)
                var on = Report(2); on[8] = 1;
                Write(on);
                lastRefresh = clock();
                Forget();
            }
            var changed = new List<(int Id, int Rgb)>();
            for (int i = 0; i < LedCount; i++)
            {
                var c = frame != null && i < frame.Length ? frame[i] : default(LedColor);
                int rgb = Scale(c, ceiling);
                if (rgb != sent[i]) changed.Add((i, rgb));
            }
            for (int start = 0; start < changed.Count; start += PerReport)
            {
                var r = Report(3);
                int n = Math.Min(PerReport, changed.Count - start);
                r[8] = (byte)n;
                for (int k = 0; k < n; k++)
                {
                    var (id, rgb) = changed[start + k];
                    int o = 9 + 4 * k;
                    r[o] = (byte)id; r[o + 1] = (byte)(rgb >> 16); r[o + 2] = (byte)(rgb >> 8); r[o + 3] = (byte)rgb;
                }
                Write(r);
                for (int k = 0; k < n; k++) sent[changed[start + k].Id] = changed[start + k].Rgb;
            }
        }

        /// <summary>Colour x brightness (1-90, capped at the ceiling), as 0xRRGGBB; 90 = the colour as it is.</summary>
        internal static int Scale(LedColor c, byte ceiling)
        {
            double k = Math.Min(Math.Min(90, (int)ceiling), (int)c.Brightness) / 90.0;
            if (k <= 0) return 0;
            return (int)Math.Round(c.R * k) << 16 | (int)Math.Round(c.G * k) << 8 | (int)Math.Round(c.B * k);
        }

        public void Dispose() { try { handle?.Dispose(); } catch { } }
    }
}
