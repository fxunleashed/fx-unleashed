using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace User.FXProRpmSync
{
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum LightsSource { BuiltIn, AtsrHub }

    /// <summary>USB mode settings (part of the plugin's settings).</summary>
    public class UsbSettings
    {
        public bool Enabled = false;
        /// <summary>
        /// The user flashed the FXProDashes wheel app (build 4-6). Those builds can't be detected over USB (same version and
        /// status as stock), so USB mode only runs once this is confirmed; build 7+ reports itself (`FxUsb.QueryBuild`) and
        /// needs no confirmation. On a stock wheel nothing breaks: LED writes land in unused RAM, but the dash would
        /// flicker against the wheel's own.
        /// </summary>
        public bool FirmwareConfirmed = false;
        public bool DashEnabled = true;
        public string DashId = BuiltInDashes.MustangId;
        public int PadLeft = 10, PadTop = 20;
        public bool LightsEnabled = true;
        /// <summary>Keep the lights on with no game running (ambient effects; rev lights dark). Off = SimPro's lights until a game starts.</summary>
        public bool IdleLights = true;
        /// <summary>Between sessions, show the plugin's logo on the wheel's screen instead of the wheel's own dash.</summary>
        public bool ScreenSaver = true;
        public string LightPreset = "mustang";
        /// <summary>The user's own lights ("Customize"), used when LightPreset is "custom".</summary>
        public LightProfile CustomLights;
        /// <summary>Rev LED 23 is the rightmost (flip the rev bar).</summary>
        /// <summary>No longer used (the rev LED order is mapped); kept so old settings still load.</summary>
        public bool ReverseRev = false;

        /// <summary>Run the dash designer's local web server (and API for agents) while SimHub runs.</summary>
        public bool DesignerServer = true;
        public int DesignerPort = User.FXProRpmSync.DesignerServer.DefaultPort;

        /// <summary>Where the lights come from: the built-in effects, or ATSR-Hub (see AtsrBridge).</summary>
        public LightsSource LightsFrom = LightsSource.BuiltIn;
        /// <summary>The device name ATSR-Hub publishes (its wheel setup for the FX Pro).</summary>
        public string AtsrDevice;
        /// <summary>"" = ATSR-Hub LED N -> FX Pro LED N; else 38 ATSR-Hub indexes (see AtsrBridge.ParseMap).</summary>
        public string AtsrMap = "";
        /// <summary>Follow ATSR-Hub's brightness (night mode).</summary>
        public bool AtsrBrightness = true;

        /// <summary>Per car (keyed by "Game | CarId"): the dashes it switches between. Cars not here use DefaultDashes.</summary>
        public Dictionary<string, UsbCarDash> CarDashes = new Dictionary<string, UsbCarDash>();
        /// <summary>The dashes of cars without their own list (DashRef refs), and the one shown now.</summary>
        public List<string> DefaultDashes = new List<string>();
        public int DefaultCurrent;
        /// <summary>The user's lights (duplicated from presets and edited); LightPreset can name one.</summary>
        public List<LightProfile> UserLights = new List<LightProfile>();

        /// <summary>The user's screensavers (pictures, dashes); the logo and the clock are built in.</summary>
        public List<SaverItem> Savers = new List<SaverItem>();
        /// <summary>The screensaver shown (ScreenSaver on); with a rotation, the first one.</summary>
        public string SaverId = SaverItem.LogoId;
        /// <summary>Screensavers to take turns with the default (ids), every SaverSwitchMinutes (0 = just the default).</summary>
        public List<string> SaverRotation = new List<string>();
        public int SaverSwitchMinutes = 0;

        /// <summary>The screensavers taking turns, the default first (just the default without a rotation).</summary>
        public List<string> SaverCycle()
        {
            var ids = new List<string> { SaverId ?? SaverItem.LogoId };
            if (SaverSwitchMinutes > 0 && SaverRotation != null)
                foreach (var id in SaverRotation) if (!ids.Contains(id) && IdleScreens.All(this).Any(x => x.Id == id)) ids.Add(id);
            return ids;
        }

        /// <summary>After SleepMinutes without a game: every light off and the screen's backlight off, until a game starts.</summary>
        /// <summary>The last session driven (pit board screensaver).</summary>
        public LastSession LastSession;

        /// <summary>FX Pro buttons bound to actions: "next" / "prev" / "sleep" -> button number (1-32). See WheelButtons.</summary>
        public Dictionary<string, int> WheelButtons = new Dictionary<string, int>();

        /// <summary>The screen's backlight, 5-100 (the plugin sends it on connect, on change and after sleep).</summary>
        public int ScreenBrightness = 100;

        public bool SleepEnabled = false;
        public int SleepMinutes = 10;

        /// <summary>The screen dark (backlight off) while the lights keep running. Takes the screen even for wheel dashes:
        /// just sending dim=0 isn't enough, the wheel's page task can send its own dim=.</summary>
        public bool ScreenOff = false;
        /// <summary>No LED brighter than this, 1-90 (90 = the firmware's cap). A clamp on every frame the plugin sends,
        /// on top of each light profile's own brightness; SimPro's lights (plugin not driving the LEDs) aren't affected.</summary>
        public int LedCeiling = 90;

        /// <summary>Night mode switched on by hand (the schedule and ATSR-Hub can turn it on too; see FXProRpmSyncPlugin.NightActive).</summary>
        public bool NightMode = false;
        public int NightScreenBrightness = 25;
        public int NightLedCeiling = 20;
        /// <summary>Night mode between NightFrom and NightTo (local time, "HH:mm"; may run past midnight).</summary>
        public bool NightSchedule = false;
        public string NightFrom = "22:00", NightTo = "07:00";
        /// <summary>Night mode while ATSR-Hub's night mode is on (its NM_Brightness below 100).</summary>
        public bool NightFollowAtsr = false;

        /// <summary>The screen's backlight now (night or day), 5-100.</summary>
        public int ScreenBrightnessNow(bool night) => Math.Max(5, Math.Min(100, night ? NightScreenBrightness : ScreenBrightness));

        /// <summary>The LED ceiling now (night or day), 1-90. Night never makes it brighter than the day ceiling.</summary>
        public byte LedCeilingNow(bool night) => (byte)Math.Max(1, Math.Min(90, night ? Math.Min(LedCeiling, NightLedCeiling) : LedCeiling));

        /// <summary>`now` falls between from and to ("HH:mm"); a window like 22:00-07:00 runs past midnight. Unparsable = never.</summary>
        public static bool InWindow(string from, string to, TimeSpan now)
        {
            if (!TimeSpan.TryParse(from, System.Globalization.CultureInfo.InvariantCulture, out var a) ||
                !TimeSpan.TryParse(to, System.Globalization.CultureInfo.InvariantCulture, out var b) || a == b) return false;
            return a < b ? now >= a && now < b : now >= a || now < b;
        }

        /// <summary>Light presets per car ("Game | CarId" -> preset id) and per game (SimHub's GameName -> preset id).
        /// Resolution: car, then game, then LightPreset (FXProRpmSyncPlugin.ActiveLightsFor).</summary>
        public Dictionary<string, string> CarLights = new Dictionary<string, string>();
        public Dictionary<string, string> GameLights = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Light a button's LED while it's held (any lights source; needs ButtonLeds).</summary>
        public bool PressLights = false;
        public string PressColor = "#FFFFFF";
        /// <summary>Which LED (0-11) sits under each wheel button (1-40), from the guided "press each button" step.</summary>
        public Dictionary<int, int> ButtonLeds = new Dictionary<int, int>();

        /// <summary>The global preset (what cars and games without their own use).</summary>
        public LightProfile ActiveLights => FindLights(LightPreset) ?? LightPresets.All[0];

        /// <summary>A preset or the user's own lights by id; null if there's none.</summary>
        public LightProfile FindLights(string id) =>
            id == null ? null :
            UserLights?.FirstOrDefault(p => p.Id == id) ?? (id == LightPresets.CustomId ? CustomLights : null) ?? LightPresets.Find(id);

        /// <summary>Every preset the user can pick, built-in first (ids).</summary>
        public List<string> AllLightIds() => LightPresets.All.Select(p => p.Id).Concat((UserLights ?? new List<LightProfile>()).Select(p => p.Id)).ToList();
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

        // Lights from ATSR-Hub (read in DataUpdate, where SimHub's properties live)
        private LedColor[] external;
        private long externalTicks;

        // Active session
        private FxConnection conn;
        private FxHostScreen screen;
        private FxLedWriter leds;
        private DashRenderer renderer;
        private DashDefinition dash;
        private IAnimatedSaver saver;
        private readonly LightEngine engine = new LightEngine();
        private LightProfile lights;          // this thread's copy (the settings page edits the original)
        private bool reverseRev;
        private int dashReloads;
        private UsbDemo demo;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastDash, lastDemo, lastLed;
        private volatile string[] props = new string[0];
        private string[] dashProps = new string[0], lightProps = new string[0];

        /// <summary>What DataUpdate reads for us: the dash's bindings and the lights' (custom alerts, DIFF encoder).</summary>
        private void UpdateProps() => props = dashProps.Concat(lightProps).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public string State { get; private set; } = "Off";
        public string Detail { get; private set; } = "";
        public bool WheelFound => path != null;
        public string WheelVersion => status?.VersionText;
        public bool SupportedApp => status?.IsSupportedApp == true;
        public bool Active => conn != null;
        /// <summary>The custom dash is on the wheel's screen now.</summary>
        public bool DashActive => screen != null && renderer != null;
        /// <summary>The logo is on the wheel's screen now.</summary>
        public bool SaverActive => screen != null && (saver != null || screenKey?.StartsWith("saver|") == true);
        /// <summary>The plugin holds the wheel's screen (a dash, a screensaver, or dark for sleep).</summary>
        public bool ScreenHeld => screen != null;
        /// <summary>Sleeping: every light off, the screen's backlight off.</summary>
        public bool Sleeping { get; private set; }
        /// <summary>Seconds until sleep (null = sleep off or a game running).</summary>
        public double? SleepIn { get; private set; }
        public bool DemoOn => demoOn;
        public bool Testing => DateTime.UtcNow.Ticks < Interlocked.Read(ref testUntilTicks);
        public string ActiveDashName => dash?.Name;
        public long ScreenBytes => screen?.Bytes ?? 0;
        /// <summary>The last values shown (for the settings page's preview).</summary>
        public DashValues Latest => Volatile.Read(ref latest);
        /// <summary>The last LED frame sent (for the settings page's preview).</summary>
        public LedColor[] LastFrame { get; private set; }
        public List<string> DashProblems { get; private set; } = new List<string>();
        /// <summary>SimHub properties / formulas the active dash and lights bind to, read in DataUpdate.</summary>
        public string[] Props => props;
        /// <summary>The active dash's JavascriptExtensions folder (imported SimHub dashes), for js: bindings.</summary>
        public string ScriptsFolder { get; private set; }

        public UsbController(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            thread = new Thread(Loop) { IsBackground = true, Name = "FXProRpmSync USB" };
            thread.Start();
        }

        private UsbSettings S => plugin.Settings.Usb;

        /// <summary>Settings changed: re-read them (the dash is redrawn only if its own settings changed). Wakes the wheel:
        /// someone is at the PC.</summary>
        public void SettingsChanged() { Interlocked.Increment(ref settingsVersion); Wake(); }

        // Sleep: counted from the last time something drove the wheel (a game, the demo, the designer) or the user acted
        private double lastActive;
        private volatile bool sleepNow;

        // A test frame (API / LED layout check) shown over everything until testLedsUntil
        private LedColor[] testLeds;
        private long testLedsUntil;

        /// <summary>Shows this frame on the LEDs for `seconds` (the LED layout check, the designer API).</summary>
        public void TestLeds(LedColor[] frame, double seconds)
        {
            Volatile.Write(ref testLeds, frame);
            Interlocked.Exchange(ref testLedsUntil, DateTime.UtcNow.AddSeconds(Math.Max(0.1, seconds)).Ticks);
            Wake();
        }

        public bool TestingLeds => DateTime.UtcNow.Ticks < Interlocked.Read(ref testLedsUntil);

        /// <summary>Sleep now, until a game starts or Wake.</summary>
        public void SleepNow() { sleepNow = true; wake.Set(); }

        public void Wake() { sleepNow = false; lastActive = clock.Elapsed.TotalSeconds; wake.Set(); }

        /// <summary>Dash files changed on disk: redraw from them.</summary>
        public void ReloadDashes() { Interlocked.Increment(ref dashReloads); SettingsChanged(); }

        /// <summary>Live values from SimHub's DataUpdate.</summary>
        public void Publish(DashValues v)
        {
            if (demoOn || Testing) return;
            Volatile.Write(ref latest, v);
            Interlocked.Exchange(ref latestTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>A frame of lights from ATSR-Hub (DataUpdate, ~30/s).</summary>
        public void PublishExternal(LedColor[] frame)
        {
            Volatile.Write(ref external, frame);
            Interlocked.Exchange(ref externalTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>ATSR-Hub sent a frame in the last second.</summary>
        public bool ExternalFresh => Volatile.Read(ref external) != null && DateTime.UtcNow.Ticks - Interlocked.Read(ref externalTicks) < TimeSpan.FromSeconds(1).Ticks;

        /// <summary>What the lights show now: "built-in", "ATSR-Hub", or why ATSR-Hub isn't used.</summary>
        public string LightsState { get; private set; } = "";

        // Designer preview: a dash being edited, shown until StopPreview or a minute without updates
        private volatile DashDefinition previewDash;
        private int previewLeft, previewTop;
        private string previewKey;
        private long previewUntilTicks;

        public bool PreviewActive => previewDash != null && DateTime.UtcNow.Ticks < Interlocked.Read(ref previewUntilTicks);

        /// <summary>Shows a dash from the designer on the wheel (live data while a game runs, else the demo lap).</summary>
        public void SetPreviewDash(DashDefinition d, int left, int top)
        {
            previewLeft = left; previewTop = top;
            previewKey = Newtonsoft.Json.JsonConvert.SerializeObject(d).GetHashCode() + "|" + left + "|" + top;
            previewDash = d;
            Interlocked.Exchange(ref previewUntilTicks, DateTime.UtcNow.AddSeconds(60).Ticks);
            wake.Set();
        }

        public void StopPreview() { previewDash = null; wake.Set(); }

        /// <summary>SimHub's current values while a game runs (else null), for the designer's live render.</summary>
        public DashValues LiveNow => LiveFresh ? latest : null;

        public void SetDemo(bool on) => SetDemo(on, null);

        /// <summary>The demo lap on the wheel: with `dashId`, that dash (from the dashes page), else the car's / default one.</summary>
        public void SetDemo(bool on, string dashId)
        {
            demoDashId = on ? dashId : null;
            demoOn = on;
            demo = null;
            Wake();
        }

        private volatile string demoDashId;

        /// <summary>The dash the demo runs on the wheel (null = the car's or the default).</summary>
        public string DemoDashId => demoOn ? demoDashId : null;

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
                    bool allowed = path != null && status?.IsSupportedApp == true && (Patched(s) || testing);
                    bool preview = PreviewActive;
                    if (!preview && previewDash != null) previewDash = null; // timed out
                    bool source = testing || demoOn || preview || LiveFresh;
                    double now = clock.Elapsed.TotalSeconds;
                    if (source) { lastActive = now; sleepNow = false; }
                    bool sleeping = !source && (sleepNow || (s.SleepEnabled && now - lastActive >= Math.Max(1, s.SleepMinutes) * 60));
                    SleepIn = s.SleepEnabled && !source && !sleeping ? Math.Max(1, s.SleepMinutes) * 60 - (now - lastActive) : (double?)null;
                    Sleeping = sleeping && allowed;
                    bool idle = (s.LightsEnabled && s.IdleLights) || s.ScreenSaver || s.ScreenOff || sleeping;
                    if (!allowed || (!source && !idle))
                    {
                        Deactivate();
                        SetIdleState(s, allowed);
                        wake.WaitOne(250);
                        continue;
                    }
                    if (conn == null) Open();
                    RunFrame(s, source, testing, sleeping);
                    wake.WaitOne(15); // a settings change wakes it early, so the wheel shows it at once
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
            else if (status == null && DateTime.UtcNow - appearedAt < BootGrace) { State = "Wheel found"; Detail = "Letting it finish starting up."; }
            else if (status == null) { State = "Wheel found"; Detail = "Couldn't read its status."; }
            else if (!status.IsSupportedApp) { State = "Unsupported wheel firmware"; Detail = $"The wheel runs app {status.VersionText}{(status.RunMode != 0 ? " (in its bootloader)" : "")}; USB mode needs the patched 1.3.11 app."; }
            else if (!allowed) { State = "Firmware not confirmed"; Detail = build == 0 ? "The wheel doesn't report a patch build (stock, or builds 4-6). Confirm that it runs the patched firmware below." : "Confirm that the wheel runs the patched firmware below."; }
            else { State = "Ready"; Detail = "Takes over the dash and lights when a game runs."; }
        }

        private void Probe(bool force)
        {
            if (!force && DateTime.UtcNow < nextProbe && (conn != null || path != null)) return;
            if (conn != null) return; // an open session finds out by failing writes
            nextProbe = DateTime.UtcNow.AddSeconds(2);
            var p = FxUsb.FindPath();
            if (p == null) { path = null; status = null; return; }
            // A wheel that just appeared may still be booting: talking to it then (even reading its status) can
            // freeze its screen, so leave it alone for BootGrace first.
            if (p != path) { path = p; status = null; appearedAt = DateTime.UtcNow; nextProbe = appearedAt + BootGrace; return; }
            if (DateTime.UtcNow - appearedAt < BootGrace) return;
            if (status == null)
            {
                status = FxUsb.ReadStatus(p);
                build = (status?.IsSupportedApp == true ? FxUsb.QueryBuild(p) : null) ?? -1;
                if (build > 0) SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: the wheel runs patch build {build}");
            }
        }

        private volatile int build = -1; // -1 = unknown (read by the UI thread)

        /// <summary>The patch build the wheel reported (7+), 0 if it doesn't report one (stock or builds 4-6), null = unknown.</summary>
        public int? FirmwareBuild => status == null || build < 0 ? (int?)null : build;

        /// <summary>The wheel runs the patch: it said so (build 7+), or the user confirmed it.</summary>
        private bool Patched(UsbSettings s) => s.FirmwareConfirmed || (status != null && build > 0);

        /// <summary>The wheel runs the patch (reported by the wheel or confirmed by the user).</summary>
        public bool FirmwarePatched => Patched(S);

        private static readonly TimeSpan BootGrace = TimeSpan.FromSeconds(6);
        private DateTime appearedAt;

        private void Open()
        {
            conn = new FxConnection(path);
            sentBrightness = -1; // sent with the first settings pass
            // Wheel app build 5: the dash button becomes controller button 40 and stops switching the wheel's own
            // pages (no flash save). Harmless on older builds (unused RAM). Cleared again in Deactivate.
            try { conn.WriteRam(FxConnection.Ctrl + 0x160, BitConverter.GetBytes(ButtonMagic)); } catch { }
            appliedVersion = -1;
            screenKey = null;
            lastDash = lastDemo = clock.Elapsed.TotalSeconds;
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode connected to the wheel");
        }

        /// <summary>
        /// Copies the lights from the settings when they changed, then puts on the screen what it should show now:
        /// asleep: dark (backlight off); while `source` (a game, the demo, the test, the designer): the car's custom dash,
        /// or the wheel's own dash for cars set to it; otherwise the screensaver if on, else the wheel's own screen.
        /// Something is redrawn only when what it's built from changed.
        /// </summary>
        private void ApplySettings(UsbSettings s, bool source, bool testing, bool sleeping)
        {
            int version = Volatile.Read(ref settingsVersion);
            string car = plugin.DashCarKey;
            if (version != appliedVersion || car != lightsCar) // lights can be set per car and per game
            {
                appliedVersion = version;
                lightsCar = car;
                try { lights = plugin.ActiveLightsFor(plugin.DashCarKey).Clone(); } catch { lights = lights ?? LightPresets.All[0].Clone(); }
                reverseRev = false; // the LED order is mapped (WheelView); the old "fill from the right" is gone
                lightProps = lights.Bindings().Where(b => b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(b)).ToArray();
                UpdateProps();
            }
            // checked every frame: night mode can start or end on its schedule without a settings change
            if (!sleeping && !dimmed && Brightness(s) != sentBrightness) SendBrightness(s);
            // Idle with "keep the lights on between sessions" off: SimPro's lights, even while a screensaver holds the screen
            bool wantLeds = sleeping || (s.LightsEnabled && (source || s.IdleLights));
            if (wantLeds && leds == null) { leds = new FxLedWriter(conn); leds.Enable(); }
            else if (!wantLeds && leds != null) { leds.Disable(); leds = null; LastFrame = null; }

            // What the screen should show
            string key = null;
            DashDefinition pd = previewDash, want = null;
            string wantPage = null; // a wheel dash: its page (null = leave the wheel's)
            SaverItem item = null;
            int reloads = Volatile.Read(ref dashReloads);
            if (source || sleeping || !s.ScreenSaver) saverSince = -1;
            if (sleeping) key = "sleep";
            else if (s.ScreenOff && !testing && pd == null) key = "screenoff";
            else if (source)
            {
                if (pd != null) key = "preview|" + previewKey;
                else
                {
                    var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
                    if (SwapDash && !demoOn && !testing)
                    {
                        // the quick toggle: a custom dash <-> the wheel's own
                        if (!wheelDash) { wheelDash = true; id = null; }
                        else
                        {
                            wheelDash = false;
                            id = plugin.UsbRotation(plugin.DashCarKey, out _, out _).Concat(s.DefaultDashes ?? new List<string>())
                                       .Where(r => !DashRef.IsWheel(r)).Select(DashRef.Id).FirstOrDefault() ?? BuiltInDashes.MustangId;
                        }
                    }
                    if (demoOn && demoDashId != null)
                    {
                        // the demo of one dash from the dashes page: one of the plugin's, or a wheel dash ("w:N")
                        wheelDash = DashRef.IsWheel(demoDashId);
                        id = wheelDash ? DashRef.Id(demoDashId) : demoDashId;
                    }
                    else if (testing || demoOn)
                    {
                        // the test and the demo are about the plugin's dashes
                        if (wheelDash) id = plugin.Settings.Usb.DefaultDashes.Where(r => !DashRef.IsWheel(r)).Select(DashRef.Id).FirstOrDefault();
                        wheelDash = false;
                    }
                    if (!wheelDash) key = $"dash|{id ?? BuiltInDashes.MustangId}|{s.PadLeft}|{s.PadTop}|{reloads}";
                    else wantPage = id;
                }
            }
            else if (s.ScreenSaver)
            {
                // the default, or the one whose turn it is (counted from when the screensaver came on)
                var cycle = s.SaverCycle();
                if (saverSince < 0) saverSince = clock.Elapsed.TotalSeconds;
                int turn = cycle.Count < 2 ? 0 : (int)((clock.Elapsed.TotalSeconds - saverSince) / (s.SaverSwitchMinutes * 60.0)) % cycle.Count;
                item = IdleScreens.Find(s, cycle[turn]);
                SaverShown = item.Id;
                key = $"saver|{item.Id}|{s.PadLeft}|{s.PadTop}|{reloads}";
            }

            WheelPage = source && key == null ? (wantPage ?? "") : null;
            if (key == null) { ReleaseScreen(wantPage); ShowPage(wantPage); return; }
            if (screen != null && key == screenKey) return;
            screenKey = key;
            renderer = null; dash = null; saver = null;
            if (screen == null)
            {
                // always onto page 0, even from one of the wheel's own dashes: drawing on that dash's page kept its
                // screen timers, which brought its widgets back over the plugin's dash
                screen = new FxHostScreen(conn) { Waiting = () => SendLeds(clock.Elapsed.TotalSeconds) };
                screen.Take();
                shownPage = null;
            }

            if (key == "sleep" || key == "screenoff")
            {
                foreach (var c in new[] { "page 0", "vis 255,0", "cls 0", "dim=0" }) screen.Cmd(c);
                screen.Flush();
                dimmed = true;
                SimHub.Logging.Current.Info(key == "sleep" ? "[FXProRpmSync] USB mode asleep" : "[FXProRpmSync] USB mode screen off");
                return;
            }
            Undim();

            var errors = new List<string>();
            if (item != null)
            {
                want = item.Kind == SaverKind.Logo ? null : IdleScreens.DashFor(item, DashLibrary.Load(errors));
                if (want == null)
                {
                    saver = IdleScreens.Animated(item) ?? new ScreenSaver();
                    saver.Start();
                    SimHub.Logging.Current.Info("[FXProRpmSync] USB mode screensaver: " + item.Name);
                    return;
                }
            }
            else
            {
                string id = key.Split('|')[1];
                want = pd ?? DashLibrary.Load(errors).FirstOrDefault(d => d.Id == id) ?? BuiltInDashes.MustangGt3();
            }
            dash = want;
            var room = DashRenderer.Room(dash);
            int padL = pd != null ? previewLeft : s.PadLeft, padT = pd != null ? previewTop : s.PadTop;
            renderer = new DashRenderer(screen, dash, Math.Min(Math.Max(0, padL), room.Right), Math.Min(Math.Max(0, padT), room.Down));
            dashProps = dash.Bindings.Where(b => b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(b)).ToArray();
            UpdateProps();
            ScriptsFolder = dash.ScriptsFolder;
            DashProblems = renderer.Check();
            renderer.DrawAll();
            lastDash = clock.Elapsed.TotalSeconds;
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode " + (item != null ? "screensaver: " : "dash on: ") + dash.Name);
        }

        private double lastSessionSave;

        /// <summary>Keeps the session's car, best lap, laps and position for the pit board screensaver (every 2 s).</summary>
        private void RememberSession(UsbSettings s, DashValues v, double now)
        {
            if (v == null || !v.Running || now - lastSessionSave < 2) return;
            lastSessionSave = now;
            double best = v.Number("bestLapTime") ?? 0;
            if (best <= 0) return; // nothing worth showing yet
            var ls = s.LastSession ?? new LastSession();
            ls.Car = plugin.CurrentCarNameForDash ?? ls.Car;
            ls.Game = plugin.CurrentGameForDash ?? ls.Game;
            ls.BestLap = best;
            ls.Laps = (int)(v.Number("completedLaps") ?? ls.Laps);
            ls.Position = (int)(v.Number("position") ?? 0);
            ls.When = DateTime.Now;
            s.LastSession = ls;
        }

        private int sentBrightness = -1;
        private double saverSince = -1; // when the screensaver came on (its rotation counts from there)

        /// <summary>The screensaver whose turn it is (id), while one shows.</summary>
        public string SaverShown { get; private set; }

        private int Brightness(UsbSettings s) => s.ScreenBrightnessNow(plugin.NightActive);

        /// <summary>The quick toggle (UsbWheelDashToggle): show the wheel's own dash instead of the car's custom one, or the
        /// other way round, until toggled back. Not saved.</summary>
        public bool SwapDash { get => swapDash; set { swapDash = value; wake.Set(); } }
        private volatile bool swapDash;
        private string lightsCar;

        /// <summary>The backlight setting straight to the screen (works whether the plugin owns the screen or not).</summary>
        private void SendBrightness(UsbSettings s)
        {
            sentBrightness = Brightness(s);
            var cmd = System.Text.Encoding.ASCII.GetBytes("dim=" + sentBrightness);
            var bytes = new byte[cmd.Length + 3];
            Array.Copy(cmd, bytes, cmd.Length);
            bytes[cmd.Length] = bytes[cmd.Length + 1] = bytes[cmd.Length + 2] = 0xFF;
            try { conn.ScreenBytes(bytes, bytes.Length); } catch { }
        }

        private string screenKey;             // what the screen shows now (see ApplySettings)
        private bool dimmed;

        /// <summary>The backlight back on after sleep.</summary>
        private void Undim()
        {
            if (!dimmed || screen == null) return;
            dimmed = false;
            screen.Cmd("dim=" + Brightness(plugin.Settings.Usb));
            screen.Flush();
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode awake");
        }

        /// <summary>The wheel's own dash back (`page dp`, or `page N` for one of its dashes); the lights stay.</summary>
        private void ReleaseScreen(string page = null)
        {
            if (screen == null) return;
            try { Undim(); screen.Release(page != null ? "page " + page : "page dp"); } catch { }
            screen.Dispose();
            screen = null; renderer = null; dash = null; screenKey = null; saver = null;
            shownPage = page;
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode released the screen (" + (page != null ? "wheel dash " + page : "wheel's own dash") + ")");
        }

        /// <summary>
        /// The wheel's own dash `page` (a screen page number): sent straight to the screen, which the wheel then keeps
        /// until its dash button (its page task only resends a page when its index changes, and without the flash save).
        /// </summary>
        private void ShowPage(string page)
        {
            if (page == null || page == shownPage || screen != null) return;
            shownPage = page;
            var cmd = System.Text.Encoding.ASCII.GetBytes("page " + page);
            var bytes = new byte[cmd.Length + 3];
            Array.Copy(cmd, bytes, cmd.Length);
            bytes[cmd.Length] = bytes[cmd.Length + 1] = bytes[cmd.Length + 2] = 0xFF;
            conn.ScreenBytes(bytes, bytes.Length);
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode wheel dash " + page);
        }

        private string shownPage;

        /// <summary>'FXB1' at CTRL+0x160: wheel app build 5 reports the dash button (FXProDashes build_btnpatch.py).</summary>
        private const uint ButtonMagic = 0x46584231;

        /// <summary>A wheel dash is on the screen with the plugin feeding it: its page ("" = the wheel's choice), else null.</summary>
        public string WheelPage { get; private set; }

        private volatile byte[] wheelRam;
        private long wheelRamTicks;
        private double lastWheelRam;
        private DemoCar wheelDemo;
        private SimProTelemetry wheelDemoData;
        private double wheelDemoLast;

        /// <summary>The wheel dashes' data from SimHub (WheelTelemetry layout), from DataUpdate.</summary>
        public void PublishWheelTelemetry(byte[] ram)
        {
            wheelRam = ram;
            Interlocked.Exchange(ref wheelRamTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>10 times a second while a wheel dash shows: its data into the wheel's RAM (live, or the demo lap).</summary>
        private void FeedWheelDash(double now, bool demoLap)
        {
            if (WheelPage == null || screen != null || now - lastWheelRam < 0.1) return;
            lastWheelRam = now;
            byte[] ram;
            if (demoLap)
            {
                if (wheelDemo == null) { wheelDemo = new DemoCar(); wheelDemoData = new SimProTelemetry(); wheelDemoLast = now; }
                wheelDemo.Step(now - wheelDemoLast, wheelDemoData);
                wheelDemoLast = now;
                ram = WheelTelemetry.Build(wheelDemoData);
            }
            else
            {
                wheelDemo = null;
                if (DateTime.UtcNow.Ticks - Interlocked.Read(ref wheelRamTicks) > TimeSpan.FromSeconds(2).Ticks) return;
                ram = wheelRam;
            }
            if (ram != null) conn.WriteRamBlock(WheelTelemetry.Address, ram);
        }

        private void RunFrame(UsbSettings s, bool source, bool testing, bool sleeping)
        {
            ApplySettings(s, source, testing, sleeping);
            double now = clock.Elapsed.TotalSeconds;
            DashValues v;
            bool previewDemo = previewDash != null && !LiveFresh;
            if (testing || demoOn || previewDemo)
            {
                if (demo == null) { demo = new UsbDemo(); lastDemo = now; }
                demo.UseDash(dash);
                v = demo.Step(now - lastDemo);
                lastDemo = now;
                Volatile.Write(ref latest, v);
                State = testing ? "Test" : previewDemo ? "Designer preview" : "Demo";
                Detail = testing ? "Showing the demo for a few seconds: the dash should be steady, with no stock dash flickering through."
                       : previewDemo ? "Showing the dash from the designer with the simulated lap." : "Running a simulated lap on the wheel.";
            }
            else if (source)
            {
                v = latest;
                RememberSession(s, v, now);
                State = previewDash != null ? "Designer preview" : "Active";
                Detail = previewDash != null ? "Showing the dash from the designer with live data." : "Driving the dash and lights from SimHub.";
            }
            else if (sleeping)
            {
                v = new DashValues();
                State = "Sleeping";
                Detail = "Lights and screen off. Starting a game wakes the wheel.";
            }
            else
            {
                v = new DashValues(); // no game: ambient lights only, rev lights dark, no alerts
                IdleScreens.IdleValues(v, now, s.LastSession);
                bool onScreen = screen != null;
                State = onScreen ? "Standing by" : "Lights on";
                Detail = (onScreen ? "Showing the screensaver" + (leds != null ? " and your lights" : "") : "Showing your lights") +
                         ". The dash takes over the screen when a game runs.";
            }
            if (source && screen == null) Detail += " This car uses the wheel's own dash.";
            if (s.ScreenOff && !sleeping) Detail += " Screen off (the lights keep running).";

            frameS = s; frameV = v; frameSource = source; frameTesting = testing; frameSleeping = sleeping;
            FeedWheelDash(now, demoOn || testing);
            SendLeds(now);
            if (renderer != null && now - lastDash >= 0.1)
            {
                lastDash = now;
                renderer.Update(v, now);
            }
            // The logo goes out in slices (~24 commands per frame) so the lights keep animating while it draws in.
            if (saver != null && screen != null) saver.Step(screen, now, 24);
        }

        // what the current frame's lights are made from (SendLeds also runs while the screen waits for its pacing)
        private UsbSettings frameS;
        private DashValues frameV;
        private bool frameSource, frameTesting, frameSleeping;

        private void SendLeds(double now)
        {
            var s = frameS; var v = frameV; bool source = frameSource, testing = frameTesting;
            if (s == null || v == null) return;
            if (leds != null && now - lastLed >= 1.0 / 30)
            {
                lastLed = now;
                LedColor[] frame = null;
                if (TestingLeds) { frame = Volatile.Read(ref testLeds); LightsState = "test frame"; }
                else if (frameSleeping) { frame = new LedColor[LightEngine.Count]; LightsState = "off (sleeping)"; }
                else if (s.LightsFrom == LightsSource.AtsrHub && !testing)
                {
                    if (ExternalFresh) { frame = Volatile.Read(ref external); LightsState = "ATSR-Hub"; }
                    else LightsState = "no data from ATSR-Hub" + (string.IsNullOrEmpty(s.AtsrDevice) ? " (no device picked)" : " for \"" + s.AtsrDevice + "\"") + ", showing the built-in lights";
                }
                else LightsState = testing && s.LightsFrom == LightsSource.AtsrHub ? "built-in (test)" : "built-in";
                if (frame == null) frame = engine.Render(lights, v, source && !testing && !demoOn ? plugin.CurrentLightsLayout : null, now, reverseRev);
                if (s.PressLights && !frameSleeping && !TestingLeds) frame = PressOverlay(frame, s);
                // every frame passes here (presets, ATSR-Hub, alerts, idle, tests, the API), so the ceiling holds for all
                byte ceiling = s.LedCeilingNow(plugin.NightActive);
                for (int i = 0; i < frame.Length; i++) leds.Set(i, frame[i].R, frame[i].G, frame[i].B, Math.Min(ceiling, Math.Max((byte)1, frame[i].Brightness)));
                leds.Send();
                LastFrame = frame;
            }
        }

        /// <summary>The buttons held now lit in the press colour, over whatever the frame shows (a copy).</summary>
        private LedColor[] PressOverlay(LedColor[] frame, UsbSettings s)
        {
            ulong down = plugin.Buttons?.Down ?? 0;
            var map = s.ButtonLeds;
            if (down == 0 || map == null || map.Count == 0) return frame;
            var (r, g, b) = LightEngine.Rgb(s.PressColor);
            var copy = (LedColor[])frame.Clone();
            foreach (var kv in map)
                if (kv.Key >= 1 && kv.Key <= 64 && (down >> (kv.Key - 1) & 1) != 0 && kv.Value >= 0 && kv.Value < copy.Length)
                    copy[kv.Value] = new LedColor(r, g, b, 90);
            return copy;
        }

        /// <summary>Gives the screen back (stock dash) and the LEDs (SimPro's colours).</summary>
        private void Deactivate(bool quiet = false)
        {
            if (conn == null) return;
            try { leds?.Disable(); } catch { }
            try { screen?.Release(); } catch { }
            try { conn.WriteRam(FxConnection.Ctrl + 0x160, BitConverter.GetBytes(0u)); } catch { }
            screen?.Dispose();
            try { conn.Dispose(); } catch { }
            conn = null; screen = null; leds = null; renderer = null; dash = null; demo = null; saver = null; screenKey = null; dimmed = false;
            shownPage = null; WheelPage = null;
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
