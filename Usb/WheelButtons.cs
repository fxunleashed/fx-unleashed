using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro's own buttons, read straight from its USB game controller (VID 0483 / PID 0529, 32 buttons) through
    /// Windows' joystick API, 50 times a second. Bound buttons run the plugin's actions (next / previous dash, sleep)
    /// without going through SimHub's Controls and events, which can lose this controller (seen 2026-09-27: its joystick
    /// manager reported "device lost" and didn't find it again until a restart). Learn() waits for the next press, for
    /// the settings page's "press a wheel button" binding.
    /// </summary>
    internal sealed class WheelButtons : IDisposable
    {
        private const ushort Vid = 0x0483, Pid = 0x0529;

        private readonly FXProRpmSyncPlugin plugin;
        private readonly Timer timer;
        private int joystick = -1;
        private DateTime nextScan;
        private uint last;
        private volatile Action<int> learner;
        private int busy;

        /// <summary>Action ids a wheel button can run, with their names on the settings page.</summary>
        public static readonly (string Id, string Name)[] Actions =
        {
            ("next", "Next dash"), ("prev", "Previous dash"), ("sleep", "Sleep / wake"),
        };

        public WheelButtons(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            timer = new Timer(_ => Poll(), null, 500, 20);
        }

        /// <summary>The wheel's controller is visible to Windows.</summary>
        public bool Found => joystick >= 0;

        /// <summary>Buttons held now (1-32), for the settings page.</summary>
        public uint Down => last;

        /// <summary>Calls `pressed` (on the timer's thread) with the next button pressed, instead of running its action.</summary>
        public void Learn(Action<int> pressed) => learner = pressed;

        public void CancelLearn() => learner = null;

        private void Poll()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                if (!plugin.Unlocked) { joystick = -1; return; }
                if (joystick < 0)
                {
                    if (DateTime.UtcNow < nextScan) return;
                    nextScan = DateTime.UtcNow.AddSeconds(3);
                    joystick = Find();
                    if (joystick < 0) return;
                    last = Read(joystick) ?? 0;
                }
                var now = Read(joystick);
                if (now == null) { joystick = -1; return; }
                uint pressed = now.Value & ~last;
                last = now.Value;
                if (pressed == 0) return;
                for (int b = 0; b < 32; b++)
                    if ((pressed >> b & 1) != 0) Pressed(b + 1);
            }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] wheel buttons: " + ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        private void Pressed(int button)
        {
            var l = learner;
            if (l != null) { learner = null; l(button); return; }
            var map = plugin.Settings.Usb.WheelButtons;
            if (map == null) return;
            foreach (var kv in map)
                if (kv.Value == button) Run(kv.Key);
        }

        private void Run(string action)
        {
            switch (action)
            {
                case "next": plugin.CycleUsbDash(+1); break;
                case "prev": plugin.CycleUsbDash(-1); break;
                case "sleep": if (plugin.Usb?.Sleeping == true) plugin.Usb.Wake(); else plugin.Usb?.SleepNow(); break;
            }
        }

        private static int Find()
        {
            var caps = new JOYCAPSW();
            for (int i = 0; i < 16; i++)
                if (joyGetDevCapsW(i, ref caps, Marshal.SizeOf(caps)) == 0 && caps.wMid == Vid && caps.wPid == Pid && Read(i) != null)
                    return i;
            return -1;
        }

        private static uint? Read(int i)
        {
            var j = new JOYINFOEX { dwSize = Marshal.SizeOf(typeof(JOYINFOEX)), dwFlags = 0x80 }; // JOY_RETURNBUTTONS
            return joyGetPosEx(i, ref j) == 0 ? j.dwButtons : (uint?)null;
        }

        public void Dispose() => timer.Dispose();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPSW
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax, wRmin, wRmax, wUmin, wUmax, wVmin, wVmax,
                        wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public int dwSize, dwFlags;
            public uint dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(int id, ref JOYCAPSW caps, int size);
        [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);
    }
}
