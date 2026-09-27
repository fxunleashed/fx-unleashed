using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>USB mode settings (part of the plugin's settings).</summary>
    public class UsbSettings
    {
        public bool Enabled = false;
        /// <summary>
        /// The user flashed the FXProDashes wheel app (build 4). The patch can't be detected over USB (same version and
        /// status as stock), so USB mode only runs once this is confirmed. On a stock wheel nothing breaks: LED writes land
        /// in unused RAM, but the dash would flicker against the wheel's own.
        /// </summary>
        public bool FirmwareConfirmed = false;
        public bool DashEnabled = true;
        public string DashId = BuiltInDashes.MustangId;
        public int PadLeft = 10, PadTop = 20;
        public bool LightsEnabled = true;
        public string LightPreset = "mustang";
        /// <summary>The user's own lights ("Customize"), used when LightPreset is "custom".</summary>
        public LightProfile CustomLights;
        /// <summary>Rev LED 23 is the rightmost (flip the rev bar).</summary>
        public bool ReverseRev = false;

        public LightProfile ActiveLights =>
            (LightPreset == LightPresets.CustomId ? CustomLights : null) ?? LightPresets.Find(LightPreset) ?? LightPresets.All[0];
    }

    /// <summary>
    /// USB mode: drives the FX Pro's screen and every LED over the wheel's own USB while a game (or the demo) runs, and
    /// gives both back to the wheel/SimPro when it stops. Runs on its own thread: 30 LED frames/s, 10 dash updates/s
    /// (~1-2 KB/s, well under the wheel's 500 USB reports/s). Unplugging the wheel just drops back to "waiting".
    /// </summary>
    internal sealed class UsbController : IDisposable
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly Thread thread;
        private volatile bool stop;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);

        private DashValues latest;
        private long latestTicks;
        private volatile bool demoOn;
        private long testUntilTicks;
        private int settingsVersion, appliedVersion = -1;

        // Device
        private string path;
        private FxUsb.Status status;
        private DateTime nextProbe;

        // Active session
        private FxConnection conn;
        private FxHostScreen screen;
        private FxLedWriter leds;
        private DashRenderer renderer;
        private DashDefinition dash;
        private readonly LightEngine engine = new LightEngine();
        private LightProfile lights;          // this thread's copy (the settings page edits the original)
        private bool reverseRev;
        private string dashKey;               // what the drawn dash was built from
        private int dashReloads;
        private UsbDemo demo;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastDash, lastDemo, lastLed;
        private volatile string[] props = new string[0];

        public string State { get; private set; } = "Off";
        public string Detail { get; private set; } = "";
        public bool WheelFound => path != null;
        public string WheelVersion => status?.VersionText;
        public bool SupportedApp => status?.IsSupportedApp == true;
        public bool Active => conn != null;
        public bool DemoOn => demoOn;
        public bool Testing => DateTime.UtcNow.Ticks < Interlocked.Read(ref testUntilTicks);
        public string ActiveDashName => dash?.Name;
        public long ScreenBytes => screen?.Bytes ?? 0;
        /// <summary>The last values shown (for the settings page's preview).</summary>
        public DashValues Latest => Volatile.Read(ref latest);
        /// <summary>The last LED frame sent (for the settings page's preview).</summary>
        public LedColor[] LastFrame { get; private set; }
        public List<string> DashProblems { get; private set; } = new List<string>();
        /// <summary>SimHub properties the active dash binds to ("prop:" bindings), read in DataUpdate.</summary>
        public string[] Props => props;

        public UsbController(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            thread = new Thread(Loop) { IsBackground = true, Name = "FXProRpmSync USB" };
            thread.Start();
        }

        private UsbSettings S => plugin.Settings.Usb;

        /// <summary>Settings changed: re-read them (the dash is redrawn only if its own settings changed).</summary>
        public void SettingsChanged() { Interlocked.Increment(ref settingsVersion); wake.Set(); }

        /// <summary>Dash files changed on disk: redraw from them.</summary>
        public void ReloadDashes() { Interlocked.Increment(ref dashReloads); SettingsChanged(); }

        /// <summary>Live values from SimHub's DataUpdate.</summary>
        public void Publish(DashValues v)
        {
            if (demoOn || Testing) return;
            Volatile.Write(ref latest, v);
            Interlocked.Exchange(ref latestTicks, DateTime.UtcNow.Ticks);
        }

        public void SetDemo(bool on)
        {
            demoOn = on;
            demo = null;
            wake.Set();
        }

        /// <summary>Takes the wheel for 8 s with the demo, to check the firmware before confirming it.</summary>
        public void RunTest()
        {
            demo = null;
            Interlocked.Exchange(ref testUntilTicks, DateTime.UtcNow.AddSeconds(8).Ticks);
            wake.Set();
        }

        private bool LiveFresh => latest?.Running == true && DateTime.UtcNow.Ticks - Interlocked.Read(ref latestTicks) < TimeSpan.FromSeconds(2).Ticks;

        private void Loop()
        {
            while (!stop)
            {
                try
                {
                    var s = S;
                    bool testing = Testing;
                    if (!s.Enabled && !testing)
                    {
                        Deactivate();
                        State = "Off";
                        Detail = "";
                        wake.WaitOne(500);
                        continue;
                    }
                    Probe(force: false);
                    bool allowed = path != null && status?.IsSupportedApp == true && (s.FirmwareConfirmed || testing);
                    bool source = testing || demoOn || LiveFresh;
                    if (!allowed || !source)
                    {
                        Deactivate();
                        SetIdleState(s, allowed);
                        wake.WaitOne(250);
                        continue;
                    }
                    if (conn == null) Activate();
                    RunFrame(testing);
                    Thread.Sleep(15);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("[FXProRpmSync] USB mode: " + ex.Message);
                    State = "Error";
                    Detail = ex.Message;
                    Deactivate(quiet: true);
                    path = null; status = null; nextProbe = DateTime.UtcNow.AddSeconds(2);
                    wake.WaitOne(2000);
                }
            }
            Deactivate();
        }

        private void SetIdleState(UsbSettings s, bool allowed)
        {
            if (path == null) { State = "Waiting for the wheel"; Detail = "Plug the FX Pro's USB cable into the PC."; }
            else if (status == null) { State = "Wheel found"; Detail = "Couldn't read its status."; }
            else if (!status.IsSupportedApp) { State = "Unsupported wheel firmware"; Detail = $"The wheel runs app {status.VersionText}{(status.RunMode != 0 ? " (in its bootloader)" : "")}; USB mode needs the patched 1.3.11 app."; }
            else if (!allowed) { State = "Firmware not confirmed"; Detail = "Confirm that the wheel runs the patched firmware below."; }
            else { State = "Ready"; Detail = "Takes over the dash and lights when a game runs."; }
        }

        private void Probe(bool force)
        {
            if (!force && DateTime.UtcNow < nextProbe && (conn != null || path != null)) return;
            if (conn != null) return; // an open session finds out by failing writes
            nextProbe = DateTime.UtcNow.AddSeconds(2);
            var p = FxUsb.FindPath();
            if (p == null) { path = null; status = null; return; }
            if (p != path || status == null) status = FxUsb.ReadStatus(p);
            path = p;
        }

        private void Activate()
        {
            conn = new FxConnection(path);
            appliedVersion = -1;
            dashKey = null;
            ApplySettings();
            lastDash = lastDemo = clock.Elapsed.TotalSeconds;
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode active: " + (dash?.Name ?? "no dash") + ", lights " + S.ActiveLights.Name);
        }

        /// <summary>Copies the lights from the settings; rebuilds and redraws the dash only when its settings changed.</summary>
        private void ApplySettings()
        {
            int version = Volatile.Read(ref settingsVersion);
            if (version == appliedVersion) return;
            appliedVersion = version;
            var s = S;
            try { lights = s.ActiveLights.Clone(); } catch { lights = lights ?? LightPresets.All[0].Clone(); }
            reverseRev = s.ReverseRev;

            string key = $"{s.DashEnabled}|{s.DashId}|{s.PadLeft}|{s.PadTop}|{Volatile.Read(ref dashReloads)}";
            if (key == dashKey && (renderer != null || !s.DashEnabled)) { ApplyLightsOnOff(s); return; }
            dashKey = key;

            if (s.DashEnabled)
            {
                var errors = new List<string>();
                dash = DashLibrary.Load(errors).FirstOrDefault(d => d.Id == s.DashId) ?? BuiltInDashes.MustangGt3();
                if (screen == null) { screen = new FxHostScreen(conn); screen.Take(); }
                int left = Math.Max(0, s.PadLeft), top = Math.Max(0, s.PadTop);
                var room = DashRenderer.Room(dash);
                renderer = new DashRenderer(screen, dash, Math.Min(left, room.Right), Math.Min(top, room.Down));
                props = dash.Bindings.Where(b => b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase)).ToArray();
                DashProblems = renderer.Check();
                renderer.DrawAll();
            }
            else if (screen != null)
            {
                screen.Release();
                screen.Dispose();
                screen = null;
                renderer = null;
                dash = null;
            }
            ApplyLightsOnOff(s);
        }

        private void ApplyLightsOnOff(UsbSettings s)
        {
            if (s.LightsEnabled && leds == null) { leds = new FxLedWriter(conn); leds.Enable(); }
            else if (!s.LightsEnabled && leds != null) { leds.Disable(); leds = null; LastFrame = null; }
        }

        private void RunFrame(bool testing)
        {
            ApplySettings();
            double now = clock.Elapsed.TotalSeconds;
            DashValues v;
            if (testing || demoOn)
            {
                if (demo == null) { demo = new UsbDemo(); lastDemo = now; }
                v = demo.Step(now - lastDemo);
                lastDemo = now;
                Volatile.Write(ref latest, v);
                State = testing ? "Test" : "Demo";
                Detail = testing ? "Showing the demo for a few seconds: the dash should be steady, with no stock dash flickering through."
                                 : "Running a simulated lap on the wheel.";
            }
            else
            {
                v = latest;
                State = "Active";
                Detail = "Driving the dash and lights from SimHub.";
            }

            if (leds != null && now - lastLed >= 1.0 / 30)
            {
                lastLed = now;
                var frame = engine.Render(lights, v, testing || demoOn ? null : plugin.CurrentLightsLayout, now, reverseRev);
                for (int i = 0; i < frame.Length; i++) leds.Set(i, frame[i].R, frame[i].G, frame[i].B, Math.Max((byte)1, frame[i].Brightness));
                leds.Send();
                LastFrame = frame;
            }
            if (renderer != null && now - lastDash >= 0.1)
            {
                lastDash = now;
                renderer.Update(v, now);
            }
        }

        /// <summary>Gives the screen back (stock dash) and the LEDs (SimPro's colours).</summary>
        private void Deactivate(bool quiet = false)
        {
            if (conn == null) return;
            try { leds?.Disable(); } catch { }
            try { screen?.Release(); } catch { }
            screen?.Dispose();
            try { conn.Dispose(); } catch { }
            conn = null; screen = null; leds = null; renderer = null; dash = null; demo = null;
            LastFrame = null;
            if (!quiet) SimHub.Logging.Current.Info("[FXProRpmSync] USB mode released the wheel");
        }

        public void Dispose()
        {
            stop = true;
            wake.Set();
            thread.Join(3000);
        }
    }
}
