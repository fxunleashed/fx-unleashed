using System;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The active wheel's own buttons, read straight from its USB input report (report 01: two bytes, then the button
    /// bits from byte 3: 40 in bytes 3-7 on the GT Neo and stock FX Pro, 48 in bytes 3-8 with FX Pro wheel app build 9),
    /// on a thread of their own. Bound buttons run the plugin's actions (next / previous dash, sleep) without going
    /// through SimHub's Controls and events, which can lose this controller (seen 2026-09-27: its joystick manager
    /// reported "device lost" and didn't find it again until a restart). Windows' joystick API only shows the first 32
    /// buttons, so the report is read raw: with wheel app build 5 the dash button is button 40 (DashButton).
    /// Learn() waits for the next press, for the settings page's "press a wheel button" binding.
    /// </summary>
    internal sealed class WheelButtons : IDisposable
    {
        /// <summary>The dash button with wheel app builds 5-7 (while the plugin has set the button mode). It shares the
        /// button with a stock control (on the FX Pro an encoder direction), so build 8 moves it (DashSlot).</summary>
        public const int LegacyDashButton = 40;

        /// <summary>The dash button as the wheel reports it now: UsbSettings.DashSlot on build 8+, else button 40. Set by
        /// UsbController when it learns the wheel's build.</summary>
        public static int DashButton = LegacyDashButton;

        private readonly FXProRpmSyncPlugin plugin;
        private readonly Thread thread;
        private volatile bool stop;
        private volatile SafeFileHandle handle;
        private ulong last;
        private volatile Action<int> learner;

        /// <summary>Action ids a wheel button can run, with their names on the settings page.</summary>
        public static readonly (string Id, string Name)[] Actions =
        {
            ("next", "Next dash"), ("prev", "Previous dash"), ("sleep", "Sleep / wake"),
            ("screen", "Screen on / off"), ("wheeldash", "Custom / wheel's own dash"),
            ("ledup", "Lights brighter"), ("leddown", "Lights dimmer"),
            ("screenup", "Screen brighter"), ("screendown", "Screen dimmer"),
            ("night", "Night mode on / off"), ("lightnext", "Next light preset"), ("lightprev", "Previous light preset"),
        };

        public WheelButtons(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            thread = new Thread(Loop) { IsBackground = true, Name = "FXProRpmSync wheel buttons" };
            thread.Start();
        }

        /// <summary>The wheel's input reports are being read.</summary>
        public bool Found => handle != null;

        /// <summary>Buttons held now (bit n = button n+1).</summary>
        public ulong Down => last;

        /// <summary>A wheel button went down (its number; raised on the reader's thread). For the Wheel tab's drawing:
        /// raised for every press, bound or not, and while a binding is being learned.</summary>
        public event Action<int> ButtonDown;

        /// <summary>A button's name on the settings page (the dash button is the FX Pro's).</summary>
        public static string Name(int button) => Name(button, WheelModel.FxPro);

        public static string Name(int button, WheelModel m)
        {
            if (m != WheelModel.FxPro) return "Wheel button " + button;
            if (button == DashButton) return $"Dash button ({button})";
            return FxProControls.Describe(button, Layout) is string d ? $"{d} ({button})" : "Wheel button " + button;
        }

        /// <summary>Where the FX Pro's dash button and upper paddles report (the plugin's USB settings), for the names.</summary>
        public static UsbSettings Layout;

        /// <summary>Calls `pressed` (on the reader's thread) with the next button pressed, instead of running its action.</summary>
        public void Learn(Action<int> pressed) => learner = pressed;

        public void CancelLearn() => learner = null;

        private void Loop()
        {
            var buf = new byte[64];
            while (!stop)
            {
                try
                {
                    var model = plugin.ActiveModel;
                    string path = plugin.Unlocked ? FxUsb.FindPath(model.UsbFilter) : null;
                    if (path == null) { Thread.Sleep(2000); continue; }
                    using (var h = FxUsb.CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) // GENERIC_READ, shared
                    {
                        if (h.IsInvalid) { Thread.Sleep(2000); continue; }
                        handle = h;
                        last = 0;
                        while (!stop && plugin.Unlocked && plugin.ActiveModel == model)
                        {
                            if (!FxUsb.ReadFile(h, buf, buf.Length, out int n, IntPtr.Zero)) break; // unplugged, or closed by Dispose
                            if (n >= 8 && buf[0] == 1) Report(buf, n);
                        }
                    }
                }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] wheel buttons: " + ex.Message); }
                finally { handle = null; }
                if (!stop) Thread.Sleep(500);
            }
        }

        /// <summary>Report 01: [id, axis, axis, button bits from byte 3]: 40 buttons in an 8-byte report, 48 in a 9-byte
        /// one (FX Pro wheel app build 9).</summary>
        private void Report(byte[] r, int length)
        {
            int bytes = Math.Min(length, 9) - 3;
            ulong now = 0;
            for (int i = 0; i < bytes; i++) now |= (ulong)r[3 + i] << (8 * i);
            ulong pressed = now & ~last;
            last = now;
            for (int b = 0; b < bytes * 8 && pressed != 0; b++)
                if ((pressed >> b & 1) != 0)
                {
                    try { ButtonDown?.Invoke(b + 1); } catch { }
                    Pressed(b + 1);
                }
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
                case "screen": plugin.ToggleScreen(); break;
                case "wheeldash": plugin.ToggleWheelDash(); break;
                case "ledup": plugin.StepLedCeiling(+FXProRpmSyncPlugin.LedCeilingStep); break;
                case "leddown": plugin.StepLedCeiling(-FXProRpmSyncPlugin.LedCeilingStep); break;
                case "screenup": plugin.StepScreenBrightness(+FXProRpmSyncPlugin.ScreenBrightnessStep); break;
                case "screendown": plugin.StepScreenBrightness(-FXProRpmSyncPlugin.ScreenBrightnessStep); break;
                case "night": plugin.ToggleNightMode(); break;
                case "lightnext": plugin.CycleLightPreset(+1); break;
                case "lightprev": plugin.CycleLightPreset(-1); break;
            }
        }

        public void Dispose()
        {
            stop = true;
            try { handle?.Dispose(); } catch { } // ends the blocking read
            thread.Join(1000);
        }
    }
}
