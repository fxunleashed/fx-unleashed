using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro's own USB HID interface (VID 0483, PID 0529), used by USB mode. Ported from FXProDashes
    /// (tools/usb/FxHid.cs); protocol and safety notes in FXProDashes docs/firmware-notes.md and docs/custom-dash.md.
    ///  - `F2 09 len data`: bytes straight to the screen's UART (TJC commands, each ending FF FF FF).
    ///  - `F2 0A len addr data`: the updater's flash-program command; its address check is compiled out, so a RAM
    ///    address is a plain store. Only RAM is ever written here: an address in flash would really program flash.
    ///  - `F1` feature report: status (0x483, 3, app version, run mode).
    /// The patched wheel app (FXProDashes build 4) reads a control block at 0x20007000 that the stock app never uses:
    /// LED colours per LED (mode "FXL1"+4) and a "host owns the screen" flag ("FXS1") gated by a USB screen packet in
    /// the last second. On the stock app the same writes land in unused RAM and do nothing.
    /// </summary>
    internal static class FxUsb
    {
        public const string DeviceFilter = "vid_0483&pid_0529";

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [StructLayout(LayoutKind.Sequential)] private struct DID { public int cb; public Guid g; public int f; public IntPtr r; }
        [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref DID di);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref DID di, IntPtr b, int sz, out int req, IntPtr d);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteFile(SafeFileHandle h, byte[] b, int n, out int w, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadFile(SafeFileHandle h, byte[] b, int n, out int r, IntPtr o);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
        [DllImport("hid.dll")] private static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int n);

        /// <summary>The FX Pro's HID path, or null when it isn't plugged in (never an FX, which shares its USB id).</summary>
        public static string FindPath() => WheelModel.FxPro.FindUsb();

        /// <summary>The FX's HID product string (the FX Pro's is "FX Pro Wheel").</summary>
        public static bool IsFxProduct(string product) => string.Equals(product?.Trim(), "FX Wheel", StringComparison.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> products = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A HID device's product string (read once per path: a path names one plugged-in device), or null.</summary>
        public static string ProductString(string path)
        {
            lock (products) if (products.TryGetValue(path, out var known)) return known;
            string s = null;
            try
            {
                using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    var b = new byte[256];
                    if (!h.IsInvalid && HidD_GetProductString(h, b, b.Length)) s = Encoding.Unicode.GetString(b).TrimEnd('\0');
                }
            }
            catch { }
            if (s != null) lock (products) products[path] = s; // unread: asked again next time
            return s;
        }

        /// <summary>Device path of the first HID interface whose path contains `filter` ("vid_xxxx&amp;pid_yyyy") and whose
        /// product string passes `product` (null = any), or null.</summary>
        public static string FindPath(string filter, Func<string, bool> product = null)
        {
            HidD_GetHidGuid(out var g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            try
            {
                for (int i = 0; ; i++)
                {
                    var di = new DID(); di.cb = Marshal.SizeOf(di);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref di)) return null;
                    SetupDiGetDeviceInterfaceDetail(set, ref di, IntPtr.Zero, 0, out int req, IntPtr.Zero);
                    IntPtr b = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(b, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref di, b, req, out req, IntPtr.Zero)) continue;
                        string p = Marshal.PtrToStringAuto(b + 4);
                        if (p != null && p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 && (product == null || product(ProductString(p)))) return p;
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        public sealed class Status
        {
            public uint Product;   // 3 = FX Pro, 2 = FX
            public uint Version;   // 0x1030B = 1.3.11
            public uint RunMode;   // 0 = app, otherwise bootloader
            /// <summary>Report byte 0x20 (low byte of 0x20000850): the patch build after a marker query (build 7+), else 0.</summary>
            public byte Marker;
            /// <summary>The report as read (33 bytes; status byte k = Raw[k]).</summary>
            public byte[] Raw;
            public string VersionText => $"{(Version >> 16) & 0xFF}.{(Version >> 8) & 0xFF}.{Version & 0xFF}";
            /// <summary>The wheel app the patch was built for, running normally.</summary>
            public bool IsSupportedApp => Product == 3 && Version == 0x1030B && RunMode == 0;
            /// <summary>An FX (not Pro) running stock app 1.3.5, the only FX app there is (Usb/FxTransport.cs).</summary>
            public bool IsFxApp => Product == 2 && Version == 0x10305 && RunMode == 0;
            /// <summary>The wheel's bootloader (update mode): it reports version bytes 0F 01 01 01 where the app reports
            /// 0B 03 01 00; the run mode stays 0 (FXProDashes, build 9 flashed through the escape hatch).</summary>
            public bool IsBootloader => Version == 0x0101010F || RunMode != 0;
        }

        /// <summary>
        /// The escape hatch into update mode (FXProDashes tools/usb/boot-mode.ps1, proven 2026-09-30): F1 03 erases the
        /// boot flag page, F1 01 restarts the wheel, which then stays in its bootloader (across power cycles too) until
        /// SimPro finishes an install. The app stays in flash. These are the same two commands SimPro sends first, so
        /// SimPro can then reinstall a wheel whose app misbehaves, as long as its USB answers. Returns null when sent.
        /// </summary>
        public static string EnterBootloader(string path)
        {
            foreach (byte cmd in new byte[] { 3, 1 })
            {
                using (var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return "can't open the wheel (error " + Marshal.GetLastWin32Error() + ")";
                    var r = new byte[33]; r[0] = 0xF1; r[1] = cmd;
                    if (!HidD_SetFeature(h, r, r.Length)) return $"F1 0{cmd} failed (error {Marshal.GetLastWin32Error()})";
                }
                Thread.Sleep(300);
            }
            return null;
        }

        /// <summary>F1 status (read-only). Null if it can't be read.</summary>
        public static Status ReadStatus(string path)
        {
            using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var b = new byte[33]; b[0] = 0xF1;
                if (!HidD_GetFeature(h, b, b.Length)) return null;
                // The report comes back without its id byte: 0x483, 3, version, run mode as u32 LE from byte 0.
                // The app fills 0x24 bytes but the descriptor declares 32 + id, so only bytes 0x00-0x20 arrive.
                return new Status { Product = BitConverter.ToUInt32(b, 4), Version = BitConverter.ToUInt32(b, 8), RunMode = BitConverter.ToUInt32(b, 12), Marker = b[32], Raw = b };
            }
        }

        /// <summary>
        /// App 1.3.11's "last input report collected" flag (byte 0x200002B8, bit 0; FXProDashes docs/firmware-notes.md
        /// "Buttons stop after a USB reconnect"). The main loop sends a report only while it is 1 and clears it on
        /// sending; only the EP2 IN "collected" callback sets it again, and nothing resets it on a USB bus reset. So when
        /// the link drops while the wheel stays powered (by the base) with a report pending, the wheel never sends
        /// buttons again. Its word: the flag, an unused byte, and a button scratch halfword the scan clears and rebuilds
        /// every pass, so writing 1 is safe. On a healthy link the flag is 1 already.
        /// </summary>
        public const uint InputReadyWord = 0x200002B8;

        /// <summary>Lets a wheel whose button reports are stuck send again (see InputReadyWord). App 1.3.11 only.</summary>
        public static void ReleaseInputReports(FxConnection c)
        {
            try { c.WriteRam(InputReadyWord, BitConverter.GetBytes(1u)); } catch { }
        }

        public static void ReleaseInputReports(string path)
        {
            try { using (var c = new FxConnection(path)) ReleaseInputReports(c); } catch { }
        }

        public const uint MarkerWord = 0x20000850;     // echoed by the status report at bytes 0x20-0x23
        public const uint MarkerQuery = 0x46585131;    // 'FXQ1' at Ctrl+0x178

        /// <summary>
        /// The patch build the wheel runs: build 7+ answers 'FXQ1' at Ctrl+0x178 by writing its build number (then 'FXU')
        /// to 0x20000850, which the stock status report shows at byte 0x20 (FXProDashes tools/fw/build_marker.py).
        /// 0 = no answer: stock, or builds 4-6, which can't be told apart. Null = couldn't ask.
        /// On older builds and stock both words are RAM nothing else reads (the status report aside, and 0x850 is
        /// cleared first and after). Only for a wheel past its boot grace, in the app (not the bootloader).
        /// </summary>
        public static int? QueryBuild(string path)
        {
            try
            {
                using (var c = new FxConnection(path))
                {
                    try
                    {
                        c.WriteRam(MarkerWord, BitConverter.GetBytes(0u));
                        c.WriteRam(FxConnection.Ctrl + 0x178, BitConverter.GetBytes(MarkerQuery));
                        // the main loop answers on its next pass (a few ms); allow for a slow pass
                        for (int i = 0; i < 3; i++)
                        {
                            Thread.Sleep(100);
                            var st = ReadStatus(path);
                            if (st == null) return null;
                            if (st.Marker != 0) return st.Marker;
                        }
                        return 0;
                    }
                    finally
                    {
                        try
                        {
                            c.WriteRam(FxConnection.Ctrl + 0x178, BitConverter.GetBytes(0u));
                            c.WriteRam(MarkerWord, BitConverter.GetBytes(0u));
                        }
                        catch { }
                    }
                }
            }
            catch { return null; }
        }
    }

    /// <summary>One open handle to the wheel, shared by the screen and the LEDs (writes are serialised).</summary>
    internal sealed class FxConnection : IDisposable
    {
        public const uint Ctrl = 0x20007000;
        private readonly SafeFileHandle h;
        private readonly object lk = new object();
        public long Reports;

        public FxConnection(string path)
        {
            h = FxUsb.CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) throw new Exception("can't open the wheel's USB interface (error " + Marshal.GetLastWin32Error() + ")");
        }

        private void Send(byte[] report)
        {
            lock (lk)
            {
                if (!FxUsb.WriteFile(h, report, report.Length, out _, IntPtr.Zero))
                    throw new Exception("USB write failed (error " + Marshal.GetLastWin32Error() + ")");
                Reports++;
            }
        }

        /// <summary>F2 09: up to 61 bytes to the screen, as they are (whole commands with terminators).</summary>
        public void ScreenBytes(byte[] data, int count)
        {
            if (count > 61) throw new ArgumentException("max 61 bytes");
            var r = new byte[65]; r[0] = 0xF2; r[1] = 0x09; r[2] = (byte)count;
            Array.Copy(data, 0, r, 3, count);
            Send(r);
        }

        /// <summary>F2 0A as a RAM store: RAM only (0x20000000-0x2000FFFF), word aligned, up to 56 bytes.</summary>
        public void WriteRam(uint address, byte[] data, int offset = 0, int count = -1)
        {
            if (count < 0) count = data.Length - offset;
            if (address < 0x20000000 || address + (uint)count > 0x20010000 || (address & 3) != 0)
                throw new ArgumentException("RAM, word aligned only");
            int len = (count + 3) & ~3;
            if (len > 56) throw new ArgumentException("max 56 bytes");
            uint rel = unchecked(address - 0x08020000u);
            var r = new byte[65]; r[0] = 0xF2; r[1] = 0x0A; r[2] = (byte)len;
            r[3] = (byte)rel; r[4] = (byte)(rel >> 8); r[5] = (byte)(rel >> 16); r[6] = (byte)(rel >> 24);
            Array.Copy(data, offset, r, 7, count);
            Send(r);
        }

        // Wheel MCU: APB2 72 MHz; USART1 BRR (0x40013808) for the screen's UART: 512000 -> 0x08C, 115200 -> 0x271, 9600 -> 0x1D4C.
        private const uint Usart1Brr = 0x40013808;

        /// <summary>
        /// Sets the speed of the wheel's UART to the screen (512000, 115200 or 9600) with one F2 0A store to USART1's BRR,
        /// the only non-RAM address this class writes: PG only affects stores into flash, so this is a plain register write.
        /// For reaching a screen left at another speed (screen recovery); a wheel power cycle sets 512000 again.
        /// </summary>
        public void SetScreenUart(int baud)
        {
            uint brr = baud == 512000 ? 0x08Cu : baud == 115200 ? 0x271u : baud == 9600 ? 0x1D4Cu : 0;
            if (brr == 0) throw new ArgumentException("512000, 115200 or 9600");
            uint rel = unchecked(Usart1Brr - 0x08020000u);
            var r = new byte[65]; r[0] = 0xF2; r[1] = 0x0A; r[2] = 4;
            r[3] = (byte)rel; r[4] = (byte)(rel >> 8); r[5] = (byte)(rel >> 16); r[6] = (byte)(rel >> 24);
            r[7] = (byte)brr; r[8] = (byte)(brr >> 8);
            Send(r);
        }

        /// <summary>Longer RAM blocks in 56-byte stores.</summary>
        public void WriteRamBlock(uint address, byte[] data)
        {
            for (int o = 0; o < data.Length; o += 56)
                WriteRam(address + (uint)o, data, o, Math.Min(56, data.Length - o));
        }

        public void Dispose() => h.Dispose();
    }

    /// <summary>Where screen commands go: the wheel, or the settings page's preview.</summary>
    internal interface IScreenSink
    {
        void Cmd(string cmd);
        void Flush();
    }

    /// <summary>
    /// "Host owns the screen" (build 3+): while active, the wheel's own screen output is dropped and only our commands
    /// reach the screen. Active = mode word set AND a USB screen packet in the last 1000 ms, so a keepalive runs every
    /// 250 ms; if SimHub dies, the wheel's dash comes back by itself within a second.
    /// Paced at 25 KB/s (what FXProDashes tools/dash used on the wheel): USB alone can push ~30 KB/s, faster than the screen
    /// draws small fills, and what overflows the screen's input is lost. After a `page` command the screen loads the page,
    /// so nothing is sent for 150 ms.
    /// </summary>
    internal sealed class FxHostScreen : IScreenSink, IDisposable
    {
        private const uint ModeAddress = FxConnection.Ctrl + 4;
        private const uint Magic = 0x46585331; // 'FXS1'
        private readonly FxConnection c;
        private readonly object lk = new object();
        private readonly byte[] pending = new byte[61];
        private int count;
        private Timer keepalive;
        private DateTime lastSend;
        public long Bytes;
        private bool onPage0, skipVis;

        private const double Rate = 25000, Burst = 1200;   // bytes/s, bytes
        private readonly System.Diagnostics.Stopwatch pace = System.Diagnostics.Stopwatch.StartNew();
        private double credit = Burst, creditAt;

        /// <summary>
        /// Called (on the sending thread) while a command waits for the pacing: a busy dash must not hold up the lights,
        /// so the controller sends LED frames from here. Must not send screen commands.
        /// </summary>
        public Action Waiting;

        public FxHostScreen(FxConnection c) { this.c = c; }

        /// <summary>
        /// Stops the wheel's own screen output. The wheel sends each command as two writes (text, then FF FF FF), so
        /// taking over can leave half a command in the screen's parser; a lone terminator 30 ms later clears it.
        /// </summary>
        public void Take()
        {
            lock (lk) SendNow(0);
            c.WriteRam(ModeAddress, BitConverter.GetBytes(Magic));
            ScreenMirror.Held(true);
            if (keepalive == null) keepalive = new Timer(_ => Tick(), null, 250, 250);
            Thread.Sleep(30);
            lock (lk)
            {
                pending[0] = pending[1] = pending[2] = 0xFF;
                SendNow(3);
            }
        }

        /// <summary>Gives the screen back; `page` = what to show first (the wheel only re-sends its page on a change).</summary>
        public void Release(string page = "page dp")
        {
            try { if (page != null) { Cmd(page); Flush(); } } catch { }
            keepalive?.Dispose();
            keepalive = null;
            ScreenMirror.Held(false);
            c.WriteRam(ModeAddress, BitConverter.GetBytes(0u));
        }

        private void Tick()
        {
            try { lock (lk) if ((DateTime.UtcNow - lastSend).TotalMilliseconds > 200 && count == 0) SendNow(0); }
            catch { }
        }

        public void Cmd(string cmd)
        {
            // Everything the plugin draws goes on page 0 with its widgets hidden. Once there, don't send it again:
            // "page 0" reloads the page (the wheel's Check1 dash) for a moment before the next drawing clears it,
            // which showed as a flash between two of the plugin's dashes.
            if (cmd == "page 0")
            {
                if (onPage0) { skipVis = true; return; }
                onPage0 = true;
            }
            else if (cmd == "vis 255,0" && skipVis) { skipVis = false; return; }
            else if (cmd.StartsWith("page ", StringComparison.Ordinal)) onPage0 = false;
            skipVis = false;
            ScreenMirror.Cmd(cmd);
            var b = Encoding.ASCII.GetBytes(cmd);
            int len = b.Length + 3;
            if (len > 61) throw new ArgumentException("screen command too long (max 58 characters): " + cmd);
            lock (lk)
            {
                if (count + len > 61) SendNow(count);
                Array.Copy(b, 0, pending, count, b.Length);
                pending[count + b.Length] = pending[count + b.Length + 1] = pending[count + b.Length + 2] = 0xFF;
                count += len;
                // smoothed shapes (draw_h, cirs) keep the screen busy for ~2 us a pixel: that time is paid as bytes, so
                // what follows waits (unpaced, a burst of ovals overflowed the screen's input: FXProDashes screen-commands.md)
                double smooth = ScreenShapes.SmoothPixels(cmd);
                if (smooth > 0) credit -= smooth * ScreenShapes.SecondsPerSmoothPixel * Rate;
                if (cmd.StartsWith("page ", StringComparison.Ordinal))
                {
                    SendNow(count);
                    Thread.Sleep(150); // the screen is loading the page; commands now could be dropped
                    credit = Burst; creditAt = pace.Elapsed.TotalSeconds;
                }
            }
        }

        public void Flush() { lock (lk) if (count > 0) SendNow(count); }

        /// <summary>
        /// Bytes to the screen as they are (no terminators): a file's data while the screen is in its upload mode
        /// (twfile). Not paced: the data goes into the screen's 4 KB packet buffer, not its command buffer (the caller
        /// waits after each packet), and USB (~30 KB/s) can't outrun the wheel's UART (~51 KB/s). Not mirrored.
        /// Pending commands go first (paced).
        /// </summary>
        public void Raw(byte[] data)
        {
            lock (lk)
            {
                if (count > 0) SendNow(count);
                for (int o = 0; o < data.Length; o += 61)
                {
                    int n = Math.Min(61, data.Length - o);
                    Array.Copy(data, o, pending, 0, n);
                    c.ScreenBytes(pending, n);
                    Bytes += n;
                }
                count = 0;
                lastSend = DateTime.UtcNow;
                credit = 0; creditAt = pace.Elapsed.TotalSeconds; // the next commands wait for the screen as after a burst
            }
        }

        /// <summary>Waits `ms` with nothing sent to the screen (it is busy, e.g. writing a file), the lights kept going.</summary>
        public void Pause(int ms)
        {
            Flush();
            var until = pace.Elapsed.TotalMilliseconds + ms;
            while (pace.Elapsed.TotalMilliseconds < until)
            {
                try { Waiting?.Invoke(); } catch { }
                Thread.Sleep(Math.Max(1, Math.Min(10, (int)(until - pace.Elapsed.TotalMilliseconds))));
            }
        }

        private void SendNow(int n)
        {
            if (n > 0)
            {
                double t = pace.Elapsed.TotalSeconds;
                credit = Math.Min(Burst, credit + (t - creditAt) * Rate);
                creditAt = t;
                while (credit < n)
                {
                    try { Waiting?.Invoke(); } catch { }
                    t = pace.Elapsed.TotalSeconds;
                    credit = Math.Min(Burst, credit + (t - creditAt) * Rate);
                    creditAt = t;
                    if (credit < n) Thread.Sleep(Math.Min(10, (int)Math.Ceiling((n - credit) * 1000 / Rate)));
                }
                credit -= n;
            }
            c.ScreenBytes(pending, n);
            Bytes += n;
            count = 0;
            lastSend = DateTime.UtcNow;
        }

        public void Dispose() { keepalive?.Dispose(); keepalive = null; }
    }

    /// <summary>Every LED's colour from the PC (build 4, mode "FXL1"+4). Black = off. A power cycle clears the mode.</summary>
    internal sealed class FxLedWriter
    {
        public const int LedCount = 48;       // table size; the wheel has 38 (0-11 buttons, 12-16 encoders, 17-22 side, 23-37 rev)
        private const uint Magic = 0x46584C31; // 'FXL1'
        private const uint ModeAll = Magic + 4;
        // Flash 0x08037274-0x080372F3 of stock 1.3.11 (palettes + LED chain map): the patch redirects reads of that
        // range to Ctrl+0x10, so the mirror must hold these bytes before any LED mode is switched on.
        private static readonly byte[] StockMirror = Hex(
            "000000000082000000dc320000d26e00001eaa00a0030000dc1d8b00ff4a0000949ba00000000000" +
            "000000000082000000a93c0000e6a000001eaa00a0030000dc1d8b00ff7200009494a00000000000" +
            "020205020401000207021e011d011c0119011a011b010a010b010c010d010e010f011001110112011301140115011601");

        private readonly FxConnection c;
        private readonly byte[] leds = new byte[LedCount * 4];

        public FxLedWriter(FxConnection c) { this.c = c; }

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>brightness: 1-90 (the patch caps it at 90, the most stock firmware drives an LED); 0 = the wheel's own.</summary>
        public void Set(int led, byte r, byte g, byte b, byte brightness)
        {
            leds[led * 4] = b; leds[led * 4 + 1] = r; leds[led * 4 + 2] = g; leds[led * 4 + 3] = brightness;
        }

        /// <summary>LEDs 0-37 (three stores).</summary>
        public void Send()
        {
            var part = new byte[38 * 4];
            Array.Copy(leds, part, part.Length);
            c.WriteRamBlock(FxConnection.Ctrl + 0x90, part);
        }

        /// <summary>Stock mirror and the current table first, then the mode, so no frame shows unseeded RAM.</summary>
        public void Enable()
        {
            c.WriteRam(FxConnection.Ctrl, BitConverter.GetBytes(0u));
            c.WriteRamBlock(FxConnection.Ctrl + 0x10, StockMirror);
            c.WriteRamBlock(FxConnection.Ctrl + 0x90, leds);
            c.WriteRam(FxConnection.Ctrl, BitConverter.GetBytes(ModeAll));
        }

        /// <summary>Back to the wheel's own colours (SimPro's settings).</summary>
        public void Disable() => c.WriteRam(FxConnection.Ctrl, BitConverter.GetBytes(0u));
    }
}
