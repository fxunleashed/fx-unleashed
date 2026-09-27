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
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);

        /// <summary>Device path of the wheel's HID interface, or null when it isn't plugged in.</summary>
        public static string FindPath()
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
                        if (p != null && p.IndexOf(DeviceFilter, StringComparison.OrdinalIgnoreCase) >= 0) return p;
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        public sealed class Status
        {
            public uint Version;   // 0x1030B = 1.3.11
            public uint RunMode;   // 0 = app, otherwise bootloader
            public string VersionText => $"{(Version >> 16) & 0xFF}.{(Version >> 8) & 0xFF}.{Version & 0xFF}";
            /// <summary>The wheel app the patch was built for, running normally.</summary>
            public bool IsSupportedApp => Version == 0x1030B && RunMode == 0;
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
                return new Status { Version = BitConverter.ToUInt32(b, 8), RunMode = BitConverter.ToUInt32(b, 12) };
            }
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

        public FxHostScreen(FxConnection c) { this.c = c; }

        /// <summary>
        /// Stops the wheel's own screen output. The wheel sends each command as two writes (text, then FF FF FF), so
        /// taking over can leave half a command in the screen's parser; a lone terminator 30 ms later clears it.
        /// </summary>
        public void Take()
        {
            lock (lk) SendNow(0);
            c.WriteRam(ModeAddress, BitConverter.GetBytes(Magic));
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
            c.WriteRam(ModeAddress, BitConverter.GetBytes(0u));
        }

        private void Tick()
        {
            try { lock (lk) if ((DateTime.UtcNow - lastSend).TotalMilliseconds > 200 && count == 0) SendNow(0); }
            catch { }
        }

        public void Cmd(string cmd)
        {
            var b = Encoding.ASCII.GetBytes(cmd);
            int len = b.Length + 3;
            if (len > 61) throw new ArgumentException("screen command too long (max 58 characters): " + cmd);
            lock (lk)
            {
                if (count + len > 61) SendNow(count);
                Array.Copy(b, 0, pending, count, b.Length);
                pending[count + b.Length] = pending[count + b.Length + 1] = pending[count + b.Length + 2] = 0xFF;
                count += len;
            }
        }

        public void Flush() { lock (lk) if (count > 0) SendNow(count); }

        private void SendNow(int n)
        {
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
