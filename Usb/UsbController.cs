using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace User.FXProRpmSync
{
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum LightsSource { BuiltIn, AtsrHub, SimHubDevice }

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
        /// <summary>Header-only screen uploads from this PC (ScreenFlasher), oldest first.</summary>
        public List<ScreenFlashRecord> ScreenFlashes = new List<ScreenFlashRecord>();
        /// <summary>The last "Check my screen" (ScreenCheckPanel): "on", "off" or "no-reply", and when.</summary>
        public string ScreenCheck;
        public DateTime? ScreenCheckWhen;
        /// <summary>Hash of the firmware warning the user accepted for screen uploads (asked again when the text changes).</summary>
        public string ScreenFirmwareAck;
        public bool DashEnabled = true;
        /// <summary>
        /// The screen runs the RAM-drive image (FXProDashes docs/screen-images.md): dashes are drawn from pictures kept in
        /// the screen's RAM (tiles) instead of thousands of rectangles. Can't be detected (the screen's replies don't reach
        /// the PC), so the user turns it on, after the Test showed the test card. Also how a screen flashed outside the plugin
        /// (the PC tool, an earlier version) is told to it: no upload records, but this tick. The firmware card clears it when
        /// it puts Simagic's header back or an upload fails, and leaves it to the user after turning picture memory on (the
        /// screen needs a power cycle first, then Test).
        /// </summary>
        public bool ScreenRamDrive = false;
        /// <summary>With the RAM drive: load the whole rotation (the car's dashes) with the first dash, so switching is
        /// instant afterwards (as many as fit, in rotation order).</summary>
        public bool PreloadRotation = true;
        /// <summary>
        /// Dashes (by id) that are drawn with rectangles even with the RAM drive on: nothing of them goes up to the screen, so
        /// they take no screen RAM and no loading time. For easy-to-draw dashes whose pictures aren't worth the memory.
        /// </summary>
        public List<string> NoRamDashes = new List<string>();
        /// <summary>Whether this dash is drawn from the screen's RAM when the drive is on (the default; the dash's own pictures may still not fit).</summary>
        public bool DashUsesRam(string id) => NoRamDashes == null || id == null || !NoRamDashes.Contains(id);
        public void SetDashUsesRam(string id, bool on)
        {
            if (id == null) return;
            if (NoRamDashes == null) NoRamDashes = new List<string>();
            if (on) NoRamDashes.RemoveAll(x => x == id);
            else if (!NoRamDashes.Contains(id)) NoRamDashes.Add(id);
        }
        /// <summary>This dash is drawn from the screen's RAM now: the drive is on and the dash hasn't been set to skip it.</summary>
        public bool RamFor(string id) => ScreenRamDrive && DashUsesRam(id);
        /// <summary>What the plugin put on the screen's RAM drive (ScreenRam).</summary>
        public ScreenRamState ScreenRam = new ScreenRamState();
        public string DashId = BuiltInDashes.MustangId;
        /// <summary>The page each dash with pages was left on (by dash id), so it comes back on that page.</summary>
        public Dictionary<string, int> DashPages = new Dictionary<string, int>();
        public int PadLeft = 10, PadTop = 20;
        public bool LightsEnabled = true;
        /// <summary>Keep the lights on with no game running (ambient effects; rev lights dark). Off = SimPro's lights until a game starts.</summary>
        public bool IdleLights = true;
        /// <summary>Between sessions, show the plugin's logo on the wheel's screen instead of the wheel's own dash.</summary>
        public bool ScreenSaver = true;
        public string LightPreset = "neon-tokyo";
        /// <summary>The user's own lights ("Customize"), used when LightPreset is "custom".</summary>
        public LightProfile CustomLights;
        /// <summary>Rev LED 23 is the rightmost (flip the rev bar).</summary>
        /// <summary>No longer used (the rev LED order is mapped); kept so old settings still load.</summary>
        public bool ReverseRev = false;

        /// <summary>Run the dash designer's local web server (and API for agents) while SimHub runs.</summary>
        public bool DesignerServer = true;
        /// <summary>The online library (LibraryClient): base URL (empty = the public one), what's installed from it, and
        /// the library terms' hash the user accepted (asked again when the text changes).</summary>
        public string LibraryUrl = "";
        public List<LibraryInstall> LibraryInstalled = new List<LibraryInstall>();
        public string LibraryTermsAccepted;
        public int DesignerPort = User.FXProRpmSync.DesignerServer.DefaultPort;
        /// <summary>How the stream page (OBS browser source) looks.</summary>
        public MirrorSettings Mirror = new MirrorSettings();

        /// <summary>Where the lights come from: the built-in effects, or ATSR-Hub (see AtsrBridge).</summary>
        public LightsSource LightsFrom = LightsSource.BuiltIn;
        /// <summary>The device name ATSR-Hub publishes (its wheel setup for the FX Pro).</summary>
        public string AtsrDevice;
        /// <summary>"" = ATSR-Hub LED N -> FX Pro LED N; else 38 ATSR-Hub indexes (see AtsrBridge.ParseMap).</summary>
        public string AtsrMap = "";
        /// <summary>Follow ATSR-Hub's brightness (night mode).</summary>
        public bool AtsrBrightness = true;
        /// <summary>
        /// A car alongside (spotter) shows over ATSR-Hub's or SimHub's lights too, as this preset's spotter alerts (side lights).
        /// Nothing else of the preset's alerts is drawn over them.
        /// </summary>
        public bool SpotterOverExternal = true;

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

        /// <summary>Wheel buttons bound to actions: "next" / "prev" / "sleep" -> button number (1-40). See WheelButtons.</summary>
        public Dictionary<string, int> WheelButtons = new Dictionary<string, int>();

        /// <summary>Wheel app build 8+: the controller button the dash button reports as. Build 9 declares 48 buttons and
        /// 41-48 are free (default 41); on build 8 every one of the 40 is a stock control's (FxProControls), so it shares
        /// (default 36, with the right outer roller). Builds 5-7 always use 40. Kept valid by FxProControls.Normalize.</summary>
        public int DashSlot = 36;

        /// <summary>Wheel app build 8+: report the two upper paddles (analogue channels 4/5, which stock never sends over
        /// USB) as buttons UpperPaddleA / UpperPaddleB.</summary>
        public bool UpperPaddles = true;

        /// <summary>
        /// FX Pro: the clutch paddles' mode while the plugin drives the wheel: 0 = as set in SimPro (left alone), 1 = two
        /// axes, 2 = buttons (24 left / 27 right, as SimPro's button mode). The wheel keeps the mode in its config block
        /// (0x20001DA8, set by SimPro over the base's radio only, so never in USB mode); the plugin writes it in RAM: lost
        /// at power-off, written again on every connect. The same word holds the bite point (used only by SimPro's
        /// combined-axis mode, which this doesn't offer) and two unused bytes.
        /// </summary>
        public int ClutchMode = 0;

        /// <summary>Wheel app build 8+: the controller buttons for the two upper paddles (left, right). Build 9: 42/43 by
        /// default (41-48 are free). Build 8: 24/27, the clutch paddles' button-mode buttons, free while those are axes.</summary>
        public int UpperPaddleA = 24, UpperPaddleB = 27;

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
        /// <summary>Which LED (one of the wheel's button lights) sits under each wheel button (1-40), from the guided "press each button" step.</summary>
        public Dictionary<int, int> ButtonLeds = new Dictionary<int, int>();

        /// <summary>The global preset (what cars and games without their own use).</summary>
        /// <remarks>Never saved: on load, Newtonsoft filled the profile this returns (one of UserLights) a second time,
        /// appending every list again, so the user's lights doubled on each SimHub start (LightsRepair undoes it).</remarks>
        [Newtonsoft.Json.JsonIgnore]
        public LightProfile ActiveLights => FindLights(LightPreset) ?? LightPresets.For(Model)[0];

        /// <summary>A preset or the user's own lights by id; null if there's none.</summary>
        public LightProfile FindLights(string id) =>
            id == null ? null :
            UserLights?.FirstOrDefault(p => p.Id == id) ?? (id == LightPresets.CustomId ? CustomLights : null) ?? LightPresets.Find(id);

        /// <summary>Every preset the user can pick on this wheel, built-in first (ids).</summary>
        public List<string> AllLightIds() => LightPresets.For(Model).Select(p => p.Id).Concat((UserLights ?? new List<LightProfile>()).Select(p => p.Id)).ToList();

        // ---------- Per wheel ----------

        /// <summary>
        /// The wheel the settings pages (and USB mode) are for (WheelModel id). Set by FXProRpmSyncPlugin.SwitchWheel,
        /// which also swaps the settings tied to a wheel's LEDs and buttons (WheelSettings).
        /// </summary>
        public string ActiveWheel = WheelModel.FxPro.Id;

        [Newtonsoft.Json.JsonIgnore]
        public WheelModel Model => WheelModel.Find(ActiveWheel);

        /// <summary>
        /// The other wheels' settings that belong to a wheel's LEDs and buttons, by wheel id. The active wheel's are the
        /// fields above (so everything reading them works unchanged); SwapWheel trades them when the wheel changes.
        /// </summary>
        public Dictionary<string, WheelSettings> Wheels = new Dictionary<string, WheelSettings>();

        /// <summary>Saved preset ids that were replaced point at their successor (LightPresets.CurrentId), on every wheel.</summary>
        public void UpdateRenamedPresets()
        {
            void Fix(ref string id) => id = LightPresets.CurrentId(id);
            void FixMap(Dictionary<string, string> m) { if (m != null) foreach (var k in m.Keys.ToList()) m[k] = LightPresets.CurrentId(m[k]); }
            Fix(ref LightPreset); FixMap(CarLights); FixMap(GameLights);
            if (Wheels != null) foreach (var w in Wheels.Values) if (w != null) { w.LightPreset = LightPresets.CurrentId(w.LightPreset); FixMap(w.CarLights); FixMap(w.GameLights); }
        }

        /// <summary>Makes `to` the active wheel: the current wheel's LED/button settings go into Wheels, `to`'s come out.</summary>
        public void SwapWheel(WheelModel to)
        {
            if (to == null || to.Id == Model.Id) return;
            if (Wheels == null) Wheels = new Dictionary<string, WheelSettings>();
            Wheels[Model.Id] = Take();
            Put(Wheels.TryGetValue(to.Id, out var w) && w != null ? w : WheelSettings.Defaults(to));
            Wheels.Remove(to.Id);
            ActiveWheel = to.Id;
        }

        /// <summary>Adds a copy of a light profile to another wheel's own lights (the "Copy to" button); the copy's id.</summary>
        public string CopyLightsTo(WheelModel wheel, LightProfile p)
        {
            var copy = p.Clone();
            copy.Id = LightPresets.NewUserId();
            copy.Name = p.Name + (LightPresets.IsBuiltIn(p.Id) ? " (copy)" : "");
            if (wheel.Id == Model.Id) { (UserLights ?? (UserLights = new List<LightProfile>())).Add(copy); return copy.Id; }
            if (Wheels == null) Wheels = new Dictionary<string, WheelSettings>();
            if (!Wheels.TryGetValue(wheel.Id, out var w) || w == null) Wheels[wheel.Id] = w = WheelSettings.Defaults(wheel);
            (w.UserLights ?? (w.UserLights = new List<LightProfile>())).Add(copy);
            return copy.Id;
        }

        private WheelSettings Take() => new WheelSettings
        {
            LightPreset = LightPreset, CustomLights = CustomLights, UserLights = UserLights, CarLights = CarLights, GameLights = GameLights,
            ButtonLeds = ButtonLeds, WheelButtons = WheelButtons, PressLights = PressLights, PressColor = PressColor,
            LightsFrom = LightsFrom, AtsrDevice = AtsrDevice, AtsrMap = AtsrMap, AtsrBrightness = AtsrBrightness,
            TurnedOffSimHubDevice = TurnedOffSimHubDevice,
        };

        private void Put(WheelSettings w)
        {
            LightPreset = w.LightPreset; CustomLights = w.CustomLights; UserLights = w.UserLights ?? new List<LightProfile>();
            CarLights = w.CarLights ?? new Dictionary<string, string>();
            GameLights = new Dictionary<string, string>(w.GameLights ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            ButtonLeds = w.ButtonLeds ?? new Dictionary<int, int>(); WheelButtons = w.WheelButtons ?? new Dictionary<string, int>();
            PressLights = w.PressLights; PressColor = w.PressColor ?? "#FFFFFF";
            LightsFrom = w.LightsFrom; AtsrDevice = w.AtsrDevice; AtsrMap = w.AtsrMap ?? ""; AtsrBrightness = w.AtsrBrightness;
            TurnedOffSimHubDevice = w.TurnedOffSimHubDevice;
        }

        /// <summary>
        /// Pit limiter lights saved per car ("Game | CarId"): what that car's own dash does, set up once by someone who
        /// knows it (no sim publishes it). They come before the preset's own limiter lights, on any wheel.
        /// </summary>
        public Dictionary<string, LimiterLook> CarLimiters = new Dictionary<string, LimiterLook>();

        /// <summary>Launch aid targets per car ("Game | CarId"): what to hold at a standing start (RevExtrasOptions.Launch).</summary>
        public Dictionary<string, LaunchTarget> CarLaunch = new Dictionary<string, LaunchTarget>();

        /// <summary>One-time upgrades of saved alerts done (bit 1: a steady orange spotter on the buttons flashes red), so a player's later choice sticks.</summary>
        public int AlertUpgrades;

        /// <summary>Pit speed limits in km/h per "Game | Track", learned from the speed the pit limiter holds (RevExtrasState).</summary>
        public Dictionary<string, double> PitSpeeds = new Dictionary<string, double>();

        /// <summary>The plugin turned SimHub's own device for this wheel off (GT Neo), so it can offer to turn it back on.</summary>
        public bool TurnedOffSimHubDevice;
    }

    /// <summary>A wheel's own LED and button settings while another wheel is active (UsbSettings.Wheels).</summary>
    public class WheelSettings
    {
        public string LightPreset;
        public LightProfile CustomLights;
        public List<LightProfile> UserLights = new List<LightProfile>();
        public Dictionary<string, string> CarLights = new Dictionary<string, string>();
        public Dictionary<string, string> GameLights = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, int> ButtonLeds = new Dictionary<int, int>();
        public Dictionary<string, int> WheelButtons = new Dictionary<string, int>();
        public bool PressLights;
        public string PressColor = "#FFFFFF";
        public LightsSource LightsFrom = LightsSource.BuiltIn;
        public string AtsrDevice;
        public string AtsrMap = "";
        public bool AtsrBrightness = true;
        public bool TurnedOffSimHubDevice;

        /// <summary>A wheel's settings the first time it's used: its first preset, nothing bound or mapped yet.</summary>
        public static WheelSettings Defaults(WheelModel m) => new WheelSettings { LightPreset = LightPresets.For(m)[0].Id };
    }

    /// <summary>
    /// USB mode: drives the FX Pro's screen and every LED over the wheel's own USB while a game (or the demo) runs, and
    /// gives both back to the wheel/SimPro when it stops. Runs on its own thread: 30 LED frames/s, 10 dash updates/s
    /// (~1-2 KB/s, well under the wheel's 500 USB reports/s). Unplugging the wheel just drops back to "waiting".
    /// </summary>
    internal sealed class UsbController : IDisposable
    {
        private readonly FXProRpmSyncPlugin plugin;
        /// <summary>Why the wheel isn't on USB (base, restarting, unknown device).</summary>
        private readonly WheelSetup Setup = new WheelSetup();
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
        private FxConnection conn;            // FX Pro session (screen, RAM, LEDs)
        private NeoLedLink neo;               // GT Neo session (LEDs only)
        private FxConnection fx;              // FX session (LEDs and the input-report re-arm; Usb/FxTransport.cs)
        private FxHostScreen screen;
        private ILedLink leds;
        private DashRenderer renderer;
        private DashDefinition dash;
        private ScreenRam ram;
        /// <summary>Tiles of the shown dash still to go onto the screen (one per frame), then it's drawn from them.</summary>
        private List<ScreenTile> pendingTiles;
        /// <summary>The rest of the rotation's tiles, loaded in the background while the dash shows (one file at a time).</summary>
        private List<ScreenTile> backgroundTiles;
        /// <summary>The files being loaded (the shown dash's and the preloaded ones): never evicted to make room for each other.</summary>
        private HashSet<string> loadingKeep;
        private double loadStart, nextBackground;
        private int backgroundTotal;
        private volatile bool ramClear;
        private IAnimatedSaver saver;
        /// <summary>The screensaver, when it's drawn from tiles on the screen's RAM drive (or loading them).</summary>
        private ITiledSaver tiledSaver;
        private LightEngine engine = new LightEngine();
        /// <summary>The wheel this thread is set up for (probing, session, engine); follows UsbSettings.Model.</summary>
        private WheelModel model = WheelModel.FxPro;
        private string neoSerial;
        private LightProfile lights;          // this thread's copy (the settings page edits the original)
        private readonly CarStateTracker carState = new CarStateTracker();

        /// <summary>What the car is doing, as the lights see it (docs/light-states-plan.md).</summary>
        public CarState CarState => carState.State;
        private bool reverseRev;
        private int dashReloads;
        private UsbDemo demo;
        private OverlayShowcase showcase;
        private DashDefinition showcaseFor;
        private double demoStart;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastDash, lastDemo, lastLed, lastSlowLog = -10;
        private volatile string[] props = new string[0];
        private string[] dashProps = new string[0], lightProps = new string[0];

        /// <summary>What DataUpdate reads for us: the dash's bindings and the lights' (custom alerts, DIFF encoder).</summary>
        private void UpdateProps() => props = dashProps.Concat(lightProps).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public string State { get; private set; } = "Off";
        public string Detail { get; private set; } = "";
        public bool WheelFound => path != null;
        public string WheelVersion => model == WheelModel.GtNeo ? NeoUsb.VersionText(neoSerial) : status?.VersionText;
        public bool SupportedApp => status?.IsSupportedApp == true || (model == WheelModel.Fx && status?.IsFxApp == true);
        /// <summary>The wheel is in its updater (update mode), waiting for SimPro to install an app.</summary>
        public bool InUpdateMode => status?.IsBootloader == true;
        public bool Active => conn != null || neo != null || fx != null;
        /// <summary>The wheel USB mode is set up for now.</summary>
        public WheelModel Model => model;
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
            ram = new ScreenRam(() => S.ScreenRam ?? (S.ScreenRam = new ScreenRamState()));
            thread = new Thread(Loop) { IsBackground = true, Name = "FXProRpmSync USB" };
            thread.Start();
        }

        private UsbSettings S => plugin.Settings.Usb;

        /// <summary>Settings changed: re-read them (the dash is redrawn only if its own settings changed). Wakes the wheel:
        /// someone is at the PC.</summary>
        public void SettingsChanged() { Interlocked.Increment(ref settingsVersion); Wake(); }

        private volatile bool slotsChanged;

        /// <summary>The dash button / upper paddle buttons changed (the Wheel tab): sent to the wheel on the USB thread.</summary>
        public void ButtonSlotsChanged() { slotsChanged = true; SettingsChanged(); }

        /// <summary>
        /// Wheel app build 8+: where the dash button reports (CTRL+0x168), whether the upper paddles are sent (CTRL+0x169
        /// bit 0) and as which buttons (CTRL+0x16A/0x16B), as outputs 0-39 (build 9: 0-47, buttons 41-48 being its own).
        /// Unused RAM on older builds. Returns the dash button.
        /// </summary>
        private int WriteButtonSlots()
        {
            int max = FxProControls.MaxButton(build);
            int slot = Math.Max(1, Math.Min(max, S.DashSlot));
            byte Out(int button) => (byte)(Math.Max(1, Math.Min(max, button)) - 1);
            try { conn?.WriteRam(FxConnection.Ctrl + 0x168, new[] { Out(slot), (byte)(S.UpperPaddles ? 1 : 0), Out(S.UpperPaddleA), Out(S.UpperPaddleB) }); }
            catch { }
            WriteClutchMode();
            return slot;
        }

        /// <summary>The wheel's clutch mode byte (FX Pro app 1.3.11; see UsbSettings.ClutchMode).</summary>
        private const uint ClutchModeWord = 0x20001DA8;

        /// <summary>SimPro's button table (logical input i+1 -> output table[i]), the words holding logical 21-24 and 25-28.</summary>
        private const uint ButtonTable2124 = 0x20001C0C, ButtonTable2528 = 0x20001C10;

        /// <summary>
        /// Two axes or buttons, if the user picked one: [mode, bite point 50, 0, 0] (only the mode matters in these two). For
        /// buttons, the clutch paddles (logical inputs 24 and 27) also need their table entries: SimPro only sends them
        /// over the base when it is set to button mode itself, so on a wheel set up with axes they're 0 (button 1 for
        /// both). The table is written a word at a time, so logical 21-28 are written as SimPro's default (logical n ->
        /// button n), which the FX Pro uses (checked on the user's wheel: 22, 23, 25, 26, 28 unchanged, the clutch
        /// paddles 24 and 27). A custom SimPro mapping of those six comes back with a power-off.
        /// </summary>
        private void WriteClutchMode()
        {
            if (model != WheelModel.FxPro || (S.ClutchMode != 1 && S.ClutchMode != 2)) return;
            try
            {
                if (S.ClutchMode == 2)
                {
                    conn?.WriteRam(ButtonTable2124, new byte[] { 20, 21, 22, 23 });
                    conn?.WriteRam(ButtonTable2528, new byte[] { 24, 25, 26, 27 });
                }
                conn?.WriteRam(ClutchModeWord, new byte[] { (byte)S.ClutchMode, 50, 0, 0 });
            }
            catch { }
        }

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
        private RevExtrasState extras;
        private bool spotL, spotR;
        private int spotLogged = Environment.TickCount - 10000;

        /// <summary>One log line when a car appears or leaves alongside (at most twice a second): what the game's spotter said, for when the lights don't show it.</summary>
        private void NoteSpotter(DashValues v)
        {
            bool l = v != null && v.Running && v.SpotterLeft, r = v != null && v.Running && v.SpotterRight;
            if ((l == spotL && r == spotR) || Environment.TickCount - spotLogged < 500) return;
            spotL = l; spotR = r; spotLogged = Environment.TickCount;
            SimHub.Logging.Current.Info($"[FXProRpmSync] spotter: car on the left {(l ? "yes" : "no")}, on the right {(r ? "yes" : "no")}");
        }

        public void Publish(DashValues v)
        {
            NoteSpotter(v);
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

        private LedColor[] device;
        private long deviceTicks;

        /// <summary>A frame from SimHub's LED pipeline (the FX Pro as a SimHub device, SimHubLedDevice).</summary>
        public void PublishDevice(LedColor[] frame)
        {
            Volatile.Write(ref device, frame);
            Interlocked.Exchange(ref deviceTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>SimHub's device sent a frame in the last second.</summary>
        public bool DeviceFresh => Volatile.Read(ref device) != null && DateTime.UtcNow.Ticks - Interlocked.Read(ref deviceTicks) < TimeSpan.FromSeconds(1).Ticks;

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
        /// <param name="pages">The page shown in each set (the designer picks them one by one); null = flip `page` of every set.</param>
        public void SetPreviewDash(DashDefinition d, int left, int top, int page = -1, int overlay = -1, int[] pages = null)
        {
            previewForce = overlay >= 0 ? new OverlayShowcase(d).ForceFor(overlay) : null;
            previewLeft = left; previewTop = top;
            string key = Newtonsoft.Json.JsonConvert.SerializeObject(d).GetHashCode() + "|" + left + "|" + top;
            // a different page of the same dash: just flipped, not drawn again from scratch
            if (previewDash != null && key == previewKey)
            {
                if (pages != null)
                    for (int set = 0; set < DashPages.MaxSets; set++)
                    {
                        int want = set < pages.Length ? pages[set] : 0;
                        if (want >= 0 && want != DashPageIn(set)) Interlocked.Exchange(ref setSteps[set], want - DashPageIn(set));
                    }
                else if (page >= 0 && page != this.page) Interlocked.Exchange(ref pageSteps, page - this.page);
            }
            previewPage = page;
            previewPages = pages;
            previewKey = key;
            previewDash = d;
            Interlocked.Exchange(ref previewUntilTicks, DateTime.UtcNow.AddSeconds(60).Ticks);
            wake.Set();
        }

        public void StopPreview() { previewDash = null; wake.Set(); }

        // Pages of the shown dash (DashDefinition.Pages, and PageSets: sets 2-4). Flips come from SimHub actions and wheel
        // buttons on their own threads; the loop applies them before the next dash update, so only it touches the renderer.
        // pageSteps flips every set (Next / Previous page), setSteps[k] set k alone (Next / Previous page 2-4).
        private int pageSteps;
        private readonly int[] setSteps = new int[DashPages.MaxSets];
        private volatile int page;
        /// <summary>The page shown in sets 2-4 (index 0 = set 2); the loop alone writes it.</summary>
        private readonly int[] setPages = new int[DashPages.MaxSets - 1];
        private volatile int previewPage = -1;
        /// <summary>The designer's page per set, or null (previewPage flips every set).</summary>
        private volatile int[] previewPages;
        /// <summary>The designer's overlay picker: the values that bring that overlay up on the wheel, or null.</summary>
        private volatile Dictionary<string, bool> previewForce;

        /// <summary>Flip the shown dash's pages by `step` (wraps round), every set of them. Nothing happens on a dash without pages.</summary>
        public void StepPage(int step) { Interlocked.Add(ref pageSteps, step); wake.Set(); }

        /// <summary>Flip one set of the shown dash's pages (0 = the first, Pages; 1-3 = PageSets) by `step`.</summary>
        public void StepPage(int set, int step)
        {
            if (set < 0 || set >= DashPages.MaxSets) return;
            Interlocked.Add(ref setSteps[set], step); wake.Set();
        }

        /// <summary>The shown dash's page now (0 = the first) and how many it has (1 = no pages).</summary>
        public int DashPage => page;
        public int DashPageCount => dash?.PageCount ?? 1;
        /// <summary>The shown page's name, or null when the dash has no pages.</summary>
        public string DashPageName { get { var d = dash; return d != null && d.PageCount > 1 ? d.PageName(Math.Min(page, d.PageCount - 1)) : null; } }
        /// <summary>The shown dash's sets of pages (1 = one set or none).</summary>
        public int DashSetCount => dash?.SetCount ?? 1;
        /// <summary>The page shown now in a set of the shown dash.</summary>
        public int DashPageIn(int set) => set == 0 ? page : set - 1 < setPages.Length ? setPages[set - 1] : 0;

        /// <summary>The page a dash was left on (0 when it never was), within its pages.</summary>
        internal static int SavedPage(UsbSettings s, DashDefinition d) => SavedPage(s, d, 0);

        /// <summary>The page a set of a dash was left on: set 1 under the dash's id, sets 2-4 under "id#2"...</summary>
        internal static int SavedPage(UsbSettings s, DashDefinition d, int set)
        {
            if (d == null || d.PageCountOf(set) < 2 || s.DashPages == null || d.Id == null) return 0;
            lock (s.DashPages) return s.DashPages.TryGetValue(PageKey(d, set), out var p) && p >= 0 && p < d.PageCountOf(set) ? p : 0;
        }

        private static string PageKey(DashDefinition d, int set) => set == 0 ? d.Id : d.Id + "#" + (set + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void ApplyPageSteps(UsbSettings s)
        {
            int all = Interlocked.Exchange(ref pageSteps, 0);
            var d = dash;
            for (int set = 0; set < DashPages.MaxSets; set++)
            {
                int steps = all + Interlocked.Exchange(ref setSteps[set], 0);
                int count = d?.PageCountOf(set) ?? 1;
                if (steps == 0 || d == null || count < 2) continue;
                int now = DashPages.Step(DashPageIn(set), steps, count);
                if (set == 0) page = now; else setPages[set - 1] = now;
                lastDash = -1; // drawn at once, not at the next 10 Hz tick
                if (previewDash != null || d.Id == null) continue; // a designer preview isn't remembered
                if (s.DashPages == null) s.DashPages = new Dictionary<string, int>();
                lock (s.DashPages) s.DashPages[PageKey(d, set)] = now;
            }
        }

        /// <summary>SimHub's current values while a game runs (else null), for the designer's live render.</summary>
        public DashValues LiveNow => LiveFresh ? latest : null;

        public void SetDemo(bool on) => SetDemo(on, null);

        private volatile ScenarioRun scenarioRun;

        /// <summary>The scenario playing now (Lights tab "Try the lights"), or null.</summary>
        public LightScenario ScenarioNow => scenarioRun?.Scenario;

        /// <summary>What's happening in the scenario now ("car on your LEFT"), or null.</summary>
        public string ScenarioNote => scenarioRun?.NoteAt(clock.Elapsed.TotalSeconds);

        /// <summary>Seconds into the scenario, or 0.</summary>
        public double ScenarioElapsed => scenarioRun?.Elapsed(clock.Elapsed.TotalSeconds) ?? 0;

        /// <summary>
        /// Plays a scripted situation on the wheel (LightScenarios): its values go through the same car state, rev bar extras and
        /// light engine as a real session, with the player's own preset (alerts and extras the scenario shows are switched on in a copy).
        /// Ends by itself, or with StopScenario.
        /// </summary>
        public bool PlayScenario(string id)
        {
            var sc = LightScenarios.Find(id);
            if (sc == null) return false;
            scenarioRun = new ScenarioRun(sc, S.ActiveLights, model);
            demoDashId = null;
            demoOn = true;
            demo = null;
            Wake();
            return true;
        }

        public void StopScenario()
        {
            if (scenarioRun == null) return;
            scenarioRun = null;
            demoOn = false;
            demo = null;
            Wake();
        }

        /// <summary>The demo lap on the wheel: with `dashId`, that dash (from the dashes page), else the car's / default one.</summary>
        public void SetDemo(bool on, string dashId)
        {
            scenarioRun = null;
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

        private volatile string lentFor;
        private volatile bool lentIdle, reprobe;

        /// <summary>
        /// Hands the FX Pro to a firmware tool (screen flash, recovery, update mode): USB mode lets go of the wheel and
        /// leaves it alone until the returned handle is disposed, then looks at it afresh (it may be in another state).
        /// Returns null if USB mode didn't let go within 5 s.
        /// </summary>
        // ---------- After a screen upload (FirmwareCard) ----------

        private volatile bool forgetScreenFiles;
        private volatile int powerToken, powerResult;
        private volatile bool powerSawGone;

        /// <summary>The screen restarted (an upload): its RAM drive is empty whatever the wheel's marker says.</summary>
        public void ForgetScreenFiles() { forgetScreenFiles = true; Wake(); }

        /// <summary>
        /// Watch for a power cycle: `token` was just written to the wheel's marker word (FxUsb.MarkerWord), which only a
        /// power loss clears (the build query on connect also clears it, so it's compared at the first status read after the
        /// wheel reappears, before that query). The wheel must really leave USB first: a re-probe after a lend doesn't count.
        /// </summary>
        public void ExpectPowerCycle(int token) { powerResult = 0; powerSawGone = false; powerToken = token; }

        /// <summary>0 = waiting (or not asked), 1 = the wheel lost power and is back, 2 = it came back but kept power
        /// (USB unplugged while the base still powered it).</summary>
        public int PowerCycleResult => powerResult;

        public void CancelPowerCycle() { powerToken = 0; powerResult = 0; }

        public IDisposable Lend(string reason)
        {
            if (ScreenFlasher.InProgress != null)
                throw new InvalidOperationException("The screen is being updated (" + ScreenFlasher.InProgress + "). Wait until it's done.");
            if (lentFor != null) throw new InvalidOperationException("The wheel is busy (" + lentFor + "). Try again when that's done.");
            lentIdle = false;
            lentFor = reason;
            wake.Set();
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!lentIdle && DateTime.UtcNow < until) Thread.Sleep(50);
            if (!lentIdle) { lentFor = null; return null; }
            return new Lent(this);
        }

        public bool IsLent => lentFor != null;

        private sealed class Lent : IDisposable
        {
            private UsbController c;
            public Lent(UsbController c) { this.c = c; }
            public void Dispose()
            {
                if (c == null) return;
                c.reprobe = true;
                c.lentFor = null;
                c.wake.Set();
                c = null;
            }
        }

        private void Loop()
        {
            while (!stop)
            {
                try
                {
                    // ScreenFlasher.InProgress: a screen upload started by this plugin, or by the one SimHub replaced on a game
                    // change (plugins are recreated; the upload's thread keeps going), must never get our screen output mixed in
                    var lent = lentFor ?? ScreenFlasher.InProgress;
                    if (lent != null)
                    {
                        Deactivate();
                        State = "Busy";
                        Detail = lent;
                        lentIdle = true;
                        wake.WaitOne(500);
                        continue;
                    }
                    if (reprobe) { reprobe = false; path = null; status = null; build = -1; nextProbe = DateTime.MinValue; }
                    if (forgetScreenFiles) { forgetScreenFiles = false; ram.Forget(); screenKey = null; plugin.SaveSettings(); }
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
                    if (s.Model != model) SwitchModel(s.Model);
                    Probe(force: false);
                    bool allowed = Allowed(s, testing);
                    bool preview = PreviewActive;
                    if (!preview && previewDash != null) previewDash = null; // timed out
                    bool source = testing || demoOn || preview || LiveFresh;
                    double now = clock.Elapsed.TotalSeconds;
                    if (source) { lastActive = now; sleepNow = false; }
                    bool sleeping = !source && (sleepNow || (s.SleepEnabled && now - lastActive >= Math.Max(1, s.SleepMinutes) * 60));
                    SleepIn = s.SleepEnabled && !source && !sleeping ? Math.Max(1, s.SleepMinutes) * 60 - (now - lastActive) : (double?)null;
                    Sleeping = sleeping && allowed;
                    bool idle = (s.LightsEnabled && s.IdleLights) || (model.HasScreen && (s.ScreenSaver || s.ScreenOff)) || sleeping;
                    if (!allowed || (!source && !idle))
                    {
                        Deactivate();
                        SetIdleState(s, allowed);
                        wake.WaitOne(250);
                        continue;
                    }
                    if (conn == null && neo == null && fx == null) Open();
                    RunFrame(s, source, testing, sleeping);
                    wake.WaitOne(15); // a settings change wakes it early, so the wheel shows it at once
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("[FXProRpmSync] USB mode: " + ex.Message);
                    if (powerToken != 0) powerSawGone = true; // the wheel dropped off while a session was open (a power cycle shows up here)
                    State = "Error";
                    Detail = ex.Message;
                    Deactivate(quiet: true);
                    path = null; status = null; nextProbe = DateTime.UtcNow.AddSeconds(2);
                    wake.WaitOne(2000);
                }
            }
            Deactivate();
        }

        /// <summary>USB mode may take the wheel: FX Pro with the patched app (confirmed or reported); GT Neo when it's on USB
        /// and the plugin drives its lights (with "SimHub device" as the source, SimHub's own GT Neo device does).</summary>
        private bool Allowed(UsbSettings s, bool testing)
        {
            if (path == null) return false;
            if (model == WheelModel.GtNeo) return testing || s.LightsFrom != LightsSource.SimHubDevice;
            if (model == WheelModel.Fx) return status?.IsFxApp == true; // stock 1.3.5 takes its lights over USB as it is
            return status?.IsSupportedApp == true && (Patched(s) || testing);
        }

        /// <summary>The active wheel changed (the settings page's switch, or detection): let go of the old one and start
        /// over with the new one.</summary>
        private void SwitchModel(WheelModel to)
        {
            Deactivate();
            model = to;
            engine = new LightEngine(to);
            path = null; status = null; neoSerial = null; build = -1;
            nextProbe = DateTime.MinValue;
            Setup.Reset();
            appliedVersion = -1;
            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: now for the " + to.Name);
        }

        private void SetIdleState(UsbSettings s, bool allowed)
        {
            if (path == null)
            {
                var (state, detail) = Setup.Describe(model);
                State = state; Detail = detail;
            }
            else if (model == WheelModel.GtNeo)
            {
                if (!allowed) { State = "SimHub drives the lights"; Detail = "Lights come from SimHub's own GT Neo device (Lights tab)."; }
                else { State = "Ready"; Detail = "Takes over the lights when a game runs."; }
            }
            else if (model == WheelModel.Fx)
            {
                if (status == null) { State = "Wheel found"; Detail = "Couldn't read its status."; }
                else if (!status.IsFxApp) { State = "Unsupported wheel firmware"; Detail = $"The FX runs app {status.VersionText}{(status.IsBootloader || status.RunMode != 0 ? " (in update mode: install 1.3.5 in SimPro)" : "")}; USB mode needs its app 1.3.5 (install it in SimPro)."; }
                else { State = "Ready"; Detail = "Takes over the lights when a game runs" + (build > 0 ? " (full colour: FX Unleashed patch)." : " (its 8 colours: stock firmware)."); }
            }
            else if (status == null && (DateTime.UtcNow - appearedAt < BootGrace || buildTries > 0)) { State = "Wheel found"; Detail = "Letting it finish starting up."; }
            else if (status == null) { State = "Wheel found"; Detail = "Couldn't read its status."; }
            else if (!status.IsSupportedApp) { State = "Unsupported wheel firmware"; Detail = $"The wheel runs app {status.VersionText}{(status.IsBootloader ? " (in update mode: reinstall it in SimPro, see Recovery below)" : "")}; USB mode needs the patched 1.3.11 app."; }
            else if (!allowed) { State = "Firmware not confirmed"; Detail = build == 0 ? "The wheel doesn't report a custom firmware build (stock, or builds 4-6). Confirm that it runs the custom firmware below." : "Confirm that the wheel runs the custom firmware below."; }
            else { State = "Ready"; Detail = "Takes over the dash and lights when a game runs."; }
        }

        private void Probe(bool force)
        {
            if (!force && DateTime.UtcNow < nextProbe && (conn != null || path != null)) return;
            if (conn != null || neo != null || fx != null) return; // an open session finds out by failing writes
            nextProbe = DateTime.UtcNow.AddSeconds(2);
            var p = plugin.Detector?.PathOf(model) ?? model.FindUsb();
            if (model == WheelModel.GtNeo)
            {
                // stock firmware: nothing to check but that it's there
                if (p == null) { path = null; neoSerial = null; Setup.Poll(); return; }
                Setup.Reset();
                if (p != path) { path = p; neoSerial = NeoUsb.Serial(p); SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: GT Neo found (" + (neoSerial ?? "no serial") + ")"); }
                return;
            }
            if (model == WheelModel.Fx)
            {
                // read-only first: the status says which wheel and app; only an FX in app 1.3.5 is ever written to
                if (p == null) { path = null; status = null; Setup.Poll(); return; }
                Setup.Reset();
                if (p != path) { path = p; status = null; build = -1; }
                if (status != null) return;
                status = FxUsb.ReadStatus(p);
                if (status?.IsFxApp != true) { build = -1; return; }
                build = FxWheel.QueryBuild(p) ?? 0;
                SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: FX found, app 1.3.5, " + (build > 0 ? "FX Unleashed patch build " + build : "stock firmware"));
                return;
            }
            if (p == null) { path = null; status = null; if (powerToken != 0) powerSawGone = true; Setup.Poll(); return; }
            Setup.Reset();
            // A wheel that just appeared may still be booting: talking to it then (even reading its status) can
            // freeze its screen, so leave it alone for BootGrace first.
            if (p != path) { path = p; status = null; buildTries = 0; appearedAt = DateTime.UtcNow; nextProbe = appearedAt + BootGrace; return; }
            if (DateTime.UtcNow - appearedAt < BootGrace) return;
            if (status == null)
            {
                status = FxUsb.ReadStatus(p);
                // power-cycle check: the token gone = the wheel lost power (any status read: a re-probe reads the marker before
                // the build query clears it, and the token is put back after every query). The token still there only counts
                // as "kept power" once the wheel really left (USB unplugged with the base on); else keep waiting.
                if (powerToken != 0 && status != null && (status.Marker != powerToken || powerSawGone))
                {
                    powerResult = status.Marker == powerToken ? 2 : 1;
                    SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: the wheel is back " + (powerResult == 1 ? "after a power cycle" : "but kept power (the base still powered it)"));
                    powerToken = 0; powerSawGone = false;
                }
                // the screen's RAM files are only still there if the wheel kept power: our token in the marker word says
                // so (read before the build query, which clears that word)
                if (ram.Count > 0 && !(status?.IsSupportedApp == true && ram.TokenMatches(status.Marker)))
                {
                    ram.Forget();
                    SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: the wheel lost power, the screen's RAM files are gone");
                }
                build = (status?.IsSupportedApp == true ? FxUsb.QueryBuild(p) : null) ?? -1;
                // a power-cycle check is still waiting (ExpectPowerCycle; e.g. this was a re-probe after a lend, or a retry):
                // the build query just cleared the marker word, so put the token back before anything reads it again
                if (powerToken != 0 && status?.IsSupportedApp == true)
                    try { using (var c = new FxConnection(p)) c.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)powerToken)); } catch { }
                // Just after a power cycle the wheel app doesn't answer the build query yet (seen 3 times 2026-09-30/10-01:
                // no build, so the dash button stayed in its old slot and did nothing): ask again for a few seconds before
                // taking it for a build that doesn't answer (stock, 4-6). The session opens once the answer is in.
                if (build <= 0 && status?.IsSupportedApp == true && buildTries < 6)
                {
                    buildTries++;
                    status = null;
                    nextProbe = DateTime.UtcNow.AddSeconds(1);
                    return;
                }
                if (build > 0 && buildTries > 0) SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: the wheel answered the build query after {buildTries} retries");
                buildTries = 0;
                if (build > 0) SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: the wheel runs patch build {build}");
                if (status?.IsSupportedApp == true) FxUsb.ReleaseInputReports(p);
            }
        }

        private volatile int build = -1; // -1 = unknown (read by the UI thread)
        private int buildTries;

        /// <summary>The patch build the wheel reported (7+), 0 if it doesn't report one (stock or builds 4-6), null = unknown.</summary>
        public int? FirmwareBuild => status == null || build < 0 ? (int?)null : build;

        /// <summary>The wheel runs the patch: it said so (build 7+), or the user confirmed it.</summary>
        private bool Patched(UsbSettings s) => model == WheelModel.Fx ? status != null && build > 0 : s.FirmwareConfirmed || (status != null && build > 0);

        /// <summary>The wheel runs the patch (reported by the wheel or confirmed by the user).</summary>
        public bool FirmwarePatched => Patched(S);

        // Was 6 s: a wheel talked to while still booting froze its screen once (build 4 era). Zero while the user tests
        // connecting straight away (2026-09-29); put it back if the screen freezes after a restart.
        private static readonly TimeSpan BootGrace = TimeSpan.Zero;
        private DateTime appearedAt;

        private void Open()
        {
            if (model == WheelModel.GtNeo)
            {
                neo = new NeoLedLink(path);
                appliedVersion = -1;
                screenKey = null;
                lastDash = lastDemo = clock.Elapsed.TotalSeconds;
                SimHub.Logging.Current.Info("[FXProRpmSync] USB mode connected to the GT Neo");
                return;
            }
            if (model == WheelModel.Fx)
            {
                fx = new FxConnection(path);
                FxWheel.ReleaseInputReports(fx); // the stock app can stop its button reports by itself (FxWheel.InputReadyWord)
                appliedVersion = -1;
                screenKey = null;
                lastDash = lastDemo = clock.Elapsed.TotalSeconds;
                SimHub.Logging.Current.Info("[FXProRpmSync] USB mode connected to the FX (" + (build > 0 ? "full colour" : "8 colours") + ")");
                return;
            }
            conn = new FxConnection(path);
            sentBrightness = -1; // sent with the first settings pass
            // our token in the marker word (echoed by the status report): still there next time = the wheel kept power
            if (S.ScreenRamDrive) try { conn.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)ram.Token())); } catch { }
            // Wheel app build 8: where the dash button reports (CTRL+0x168), whether the upper paddles are sent
            // (CTRL+0x169 bit 0) and as which buttons (CTRL+0x16A/0x16B), as outputs 0-39 (build 9: 0-47, buttons
            // 41-48 being its own); written before the button mode that enables them. Unused RAM on older builds.
            int oldDash = S.DashSlot;
            if (FxProControls.Normalize(S, build)) plugin.SaveSettings();
            int slot = WriteButtonSlots();
            WheelButtons.DashButton = build >= 8 ? slot : WheelButtons.LegacyDashButton;
            // the default binding follows the dash button (40 on builds 5-7; on build 8+ 40 is a stock control again)
            if (build >= 8 && S.WheelButtons != null && S.WheelButtons.TryGetValue("next", out var next)
                && (next == WheelButtons.LegacyDashButton || next == oldDash) && next != slot)
                S.WheelButtons["next"] = slot;
            // Wheel app build 5: the dash button becomes a controller button (40 on builds 5-7, DashSlot on 8+) and stops
            // switching the wheel's own pages (no flash save). Harmless on older builds (unused RAM). Cleared again in Deactivate.
            try { conn.WriteRam(FxConnection.Ctrl + 0x160, BitConverter.GetBytes(ButtonMagic)); } catch { }
            // A reconnect after the link dropped (wheel kept powered by the base) can leave its button reports stuck.
            if (status?.IsSupportedApp == true) FxUsb.ReleaseInputReports(conn);
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
            if (slotsChanged && conn != null)
            {
                slotsChanged = false;
                if (FxProControls.Normalize(S, build)) plugin.SaveSettings();
                int dash = WriteButtonSlots();
                if (build >= 8) WheelButtons.DashButton = dash;
            }
            int version = Volatile.Read(ref settingsVersion);
            string car = plugin.DashCarKey;
            if (version != appliedVersion || car != lightsCar) // lights can be set per car and per game
            {
                appliedVersion = version;
                lightsCar = car;
                try { lights = plugin.ActiveLightsFor(plugin.DashCarKey).Clone(); } catch { lights = lights ?? LightPresets.For(model)[0].Clone(); }
                reverseRev = false; // the LED order is mapped (WheelView); the old "fill from the right" is gone
                lightProps = lights.Bindings().Where(b => b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(b)).ToArray();
                UpdateProps();
            }
            // checked every frame: night mode can start or end on its schedule without a settings change
            if (model.HasScreen && !sleeping && !dimmed && Brightness(s) != sentBrightness) SendBrightness(s);
            // Idle with "keep the lights on between sessions" off: SimPro's lights, even while a screensaver holds the screen
            bool wantLeds = sleeping || (s.LightsEnabled && (source || s.IdleLights));
            if (wantLeds && leds == null)
            {
                leds = neo ?? (fx != null ? (build > 0 ? new FxRgbLedLink(fx) : (ILedLink)new FxPaletteLedLink(fx)) : new FxLedLink(conn));
                leds.Enable();
            }
            else if (!wantLeds && leds != null) { leds.Disable(); leds = null; LastFrame = null; ScreenMirror.Leds(null); }
            if (!model.HasScreen) return; // the rest is the screen
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
                    else if (testing)
                    {
                        // the firmware test is about the plugin's dashes. (The demo used to be too: a rotation with wheel dashes in it
                        // showed the first custom one whenever the dash button reached a wheel dash, i.e. nothing happened. The demo
                        // now shows the wheel's own dash page, fed the simulated lap by FeedWheelDash.)
                        if (wheelDash) id = plugin.Settings.Usb.DefaultDashes.Where(r => !DashRef.IsWheel(r)).Select(DashRef.Id).FirstOrDefault();
                        wheelDash = false;
                    }
                    if (!wheelDash) key = $"dash|{id ?? BuiltInDashes.MustangId}|{s.PadLeft}|{s.PadTop}|{reloads}|{(s.RamFor(id ?? BuiltInDashes.MustangId) ? "ram" : "")}";
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
                // with the RAM drive flag, like a dash's key: switching picture memory on (the screen check) starts the saver
                // again through PrepareSaverTiles, which loads its pictures and the whole rotation behind the loading screen
                key = $"saver|{item.Id}|{s.PadLeft}|{s.PadTop}|{reloads}|{(s.ScreenRamDrive ? "ram" : "")}";
            }

            WheelPage = source && key == null ? (wantPage ?? "") : null;
            ScreenMirror.WheelDash(string.IsNullOrEmpty(WheelPage) ? null : WheelPage);
            if (key == null) { ReleaseScreen(wantPage); ShowPage(wantPage); return; }
            if (screen != null && key == screenKey) return;
            screenKey = key;
            renderer = null; dash = null; saver = null; tiledSaver = null;
            pendingTiles = null; backgroundTiles = null; loadingTotal = 0; loadingKeep = null;
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
                // an animated GIF plays when the screen has the RAM drive (a saver that draws itself, like the painted ones);
                // without, it's its first frame as a picture dash
                want = item.Kind == SaverKind.Logo ? null : IdleScreens.DashFor(item, DashLibrary.Load(errors), s.ScreenRamDrive && model == WheelModel.FxPro);
                if (want == null)
                {
                    saver = IdleScreens.Animated(item, s.LastSession, s.PadLeft, s.PadTop) ?? new ScreenSaver();
                    loadingTitle = item.Name;
                    PrepareSaverTiles();
                    if (pendingTiles != null) DrawLoading();
                    else saver.Start();
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
            var pp = pd != null ? previewPages : null;
            int Wanted(int set) => set < pp.Length && pp[set] >= 0 && pp[set] < dash.PageCountOf(set) ? pp[set] : 0;
            page = pp != null ? Wanted(0) : pd != null && previewPage >= 0 ? Math.Min(previewPage, dash.PageCount - 1) : SavedPage(s, dash);
            for (int set = 1; set < DashPages.MaxSets; set++)
                setPages[set - 1] = pp != null ? Wanted(set) : pd != null && previewPage >= 0 ? previewPage % dash.PageCountOf(set) : SavedPage(s, dash, set);
            Interlocked.Exchange(ref pageSteps, 0);
            for (int set = 0; set < DashPages.MaxSets; set++) Interlocked.Exchange(ref setSteps[set], 0);
            var room = DashRenderer.Room(dash);
            int padL = pd != null ? previewLeft : s.PadLeft, padT = pd != null ? previewTop : s.PadTop;
            renderer = new DashRenderer(screen, dash, Math.Min(Math.Max(0, padL), room.Right), Math.Min(Math.Max(0, padT), room.Down));
            dashProps = dash.Bindings.Where(b => b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) || SimHubFormulas.IsFormula(b)).ToArray();
            UpdateProps();
            ScriptsFolder = dash.ScriptsFolder;
            DashProblems = renderer.Check();
            loadingTitle = dash.Name;
            // a picture screensaver is drawn from the RAM like a dash (all its colours: with rectangles it was a few), the
            // clock and library savers of plain shapes are not
            PrepareTiles(item == null || IdleScreens.DrawnFromRam(dash));
            if (pendingTiles != null) DrawLoading();
            else renderer.DrawAll();
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

        /// <summary>The backlight now: the setting (day / night), lowered while the dash dims itself ("dim" elements, e.g.
        /// headlights on).</summary>
        private int Brightness(UsbSettings s)
        {
            int b = s.ScreenBrightnessNow(plugin.NightActive), dim = renderer?.DimPercent ?? 0;
            return dim <= 0 ? b : Math.Max(5, (int)Math.Round(b * (100 - dim) / 100.0));
        }

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
            var run = scenarioRun;
            if (run != null && run.Finished(now)) { scenarioRun = null; demoOn = false; run = null; }
            if (run != null)
            {
                v = run.Values(now);
                Volatile.Write(ref latest, v);
                State = "Scenario";
                Detail = $"Playing \"{run.Scenario.Title}\": {run.NoteAt(now)}.";
            }
            else if (testing || demoOn || previewDemo)
            {
                if (demo == null) { demo = new UsbDemo(); lastDemo = demoStart = now; showcase = null; }
                demo.UseDash(dash);
                v = demo.Step(now - lastDemo);
                lastDemo = now;
                // the demo on the wheel takes the dash's overlays in turn (flags, pit screens, warnings...): the whole dash
                // shows, not just what the simulated lap reaches
                if (demoOn && !testing && dash != null)
                {
                    if (showcase == null || showcaseFor != dash) { showcase = new OverlayShowcase(dash); showcaseFor = dash; }
                    showcase.Apply(v, now - demoStart);
                }
                Volatile.Write(ref latest, v);
                State = testing ? "Test" : previewDemo ? "Designer preview" : "Demo";
                Detail = testing ? (model.HasScreen ? "Showing the demo for a few seconds: the dash should be steady, with no stock dash flickering through." : "Showing the lights on a simulated lap for a few seconds.")
                       : previewDemo ? "Showing the dash from the designer with the simulated lap." : "Running a simulated lap on the wheel.";
            }
            else if (source)
            {
                v = latest;
                RememberSession(s, v, now);
                State = previewDash != null ? "Designer preview" : "Active";
                Detail = previewDash != null ? "Showing the dash from the designer with live data." : model.HasScreen ? "Driving the dash and lights from SimHub." : "Driving the lights from SimHub.";
            }
            else if (sleeping)
            {
                v = new DashValues();
                State = "Sleeping";
                Detail = (model.HasScreen ? "Lights and screen off." : "Lights off.") + " Starting a game wakes the wheel.";
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
            if (model.HasScreen && source && screen == null) Detail += " This car uses the wheel's own dash.";
            if (model.HasScreen && s.ScreenOff && !sleeping) Detail += " Screen off (the lights keep running).";

            frameS = s; frameV = v; frameSource = source; frameTesting = testing; frameSleeping = sleeping;
            FeedWheelDash(now, demoOn || testing);
            SendLeds(now);
            ApplyPageSteps(s);
            if (renderer != null && pendingTiles == null && now - lastDash >= 0.1)
            {
                lastDash = now;
                v.Page = page;
                for (int set = 1; set < DashPages.MaxSets; set++) v.SetPage(set, setPages[set - 1]);
                var force = previewDash != null ? previewForce : null;
                if (force != null) foreach (var kv in force) v.Set(kv.Key, kv.Value);
                long bytes0 = screen?.Bytes ?? 0;
                int native0 = renderer.NativeEvents;
                var took = System.Diagnostics.Stopwatch.StartNew();
                var drew = new Dictionary<DashElement, int>();
                renderer.DrawCounts = drew;
                renderer.Update(v, now);
                renderer.DrawCounts = null;
                // a slow update (what the screen takes over half a second for) in the log, with what it drew: the wheel's
                // hiccups are hard to see anywhere else
                long sent = (screen?.Bytes ?? 0) - bytes0;
                if ((sent > 6000 || took.Elapsed.TotalSeconds > 0.5) && now - lastSlowLog > 2)
                {
                    lastSlowLog = now;
                    SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: a slow dash update: {sent / 1024.0:0.0} KB in {took.Elapsed.TotalSeconds:0.00} s, " +
                        $"{renderer.NativeEvents - native0} shapes drawn by the screen; drew {drew.Count}: " +
                        string.Join(", ", drew.Keys.Take(12).Select(e => e.Name ?? e.Type)));
                }
            }
            // The logo goes out in slices (~24 commands per frame) so the lights keep animating while it draws in.
            if (saver != null && screen != null && pendingTiles == null) saver.Step(screen, now, 24);
            KeepInputReports();
            if (screen != null) UploadTiles(now);
        }

        // ---------- The screen's RAM drive (tiles) ----------

        /// <summary>Used bytes and files on the screen's RAM drive, for the settings page.</summary>
        public int RamUsed => ram?.Used ?? 0;
        public int RamFiles => ram?.Count ?? 0;
        /// <summary>Just the pictures' bytes (RamUsed also counts each file's overhead).</summary>
        public int RamData => ram?.DataBytes ?? 0;
        /// <summary>Pictures are going up to the screen now (a dash's, or the rotation in the background).</summary>
        public bool RamLoading => pendingTiles != null || backgroundTiles != null;
        /// <summary>What the RAM drive is doing (settings page), or null.</summary>
        public string RamStatus { get; private set; }

        /// <summary>Shows a test picture from the screen's RAM drive for a few seconds (settings page Test).</summary>

        /// <summary>
        /// With the RAM drive on: the dash's tiles. Drawn from them at once if they're all on the screen; else the missing
        /// ones go up first (UploadTiles) behind a loading screen, then the dash is drawn from them. Not drawn with fills
        /// meanwhile: the screen repaints its page (page 0's own "Check1" picture) after every file it receives or
        /// deletes, which wiped the dash (seen on the wheel 2026-09-30).
        /// </summary>
        private void PrepareTiles(bool realDash)
        {
            pendingTiles = null; loadingTotal = 0; loadingKeep = null; backgroundTiles = null; backgroundTotal = 0;
            if (!S.ScreenRamDrive || model != WheelModel.FxPro || renderer == null || !realDash) { RamStatus = null; return; }
            // a dash whose pictures can't all be on the drive at once (a big photo): the upload would push everything else
            // out and still fail, so it's drawn with rectangles from the start
            bool fits = DashRam.Bytes(new[] { dash }) <= ScreenRam.Budget;
            if (!S.DashUsesRam(dash?.Id) || !fits)
            {
                // this dash is set to skip the screen's RAM: drawn with rectangles, nothing of it uploaded. The rest of the
                // rotation still goes up in the background, for the dashes that do use the RAM.
                loadingKeep = new HashSet<string>();
                var others = RotationFiles(loadingKeep, 0, clock.Elapsed.TotalSeconds, 1);
                if (others.Count > 0) { backgroundTiles = others; backgroundTotal = others.Count; }
                nextBackground = clock.Elapsed.TotalSeconds + 1;
                RamStatus = fits ? "This dash is drawn without the screen's RAM (set on the Dashes page)."
                                 : "This dash's pictures are too big for the screen's RAM: drawn with rectangles.";
                return;
            }
            // our token in the marker word (also when the drive was just switched on): it tells a later reconnect that
            // the wheel kept power
            try { conn?.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)ram.Token())); } catch { }
            var tiles = renderer.EnableTiles();
            var files = tiles.Files.ToList();
            double now = clock.Elapsed.TotalSeconds;
            ram.Touch(files.Select(f => f.Name), now);
            var missing = files.Where(f => !ram.Has(f.Name)).OrderBy(f => f.Jpeg.Length).ToList();
            loadingKeep = new HashSet<string>(files.Select(f => f.Name));
            // the rest of the rotation (in rotation order, as long as it fits) goes up in the background once this dash
            // shows; switching to those is then instant
            var background = RotationFiles(loadingKeep, files.Sum(f => ScreenRam.Accounted(f.Jpeg.Length)), now, 1);
            if (background.Count > 0) { backgroundTiles = background; backgroundTotal = background.Count; }
            nextBackground = now + 1; // a second of the dash first
            if (missing.Count == 0)
            {
                renderer.UseTiles(true);
                RamStatus = $"Dash drawn from the screen's RAM ({tiles.Bytes / 1024} KB).";
                return;
            }
            // first time this dash shows (since the wheel was powered on): its own files behind a loading screen
            pendingTiles = missing; loadingTotal = missing.Count; loadStart = now;
            RamStatus = $"Loading pictures into the screen ({missing.Count} to go)...";
        }

        /// <summary>
        /// The rotation's files not on the screen yet, from its `from`th dash on (1 = the one after the current, 0 = the
        /// current too), in rotation order, as long as they fit with `total` bytes already taken. Added to `keep` (not
        /// evicted for each other) and touched just after the shown one's in the eviction order.
        /// </summary>
        private List<ScreenTile> RotationFiles(HashSet<string> keep, int total, double now, int from)
        {
            var list = new List<ScreenTile>();
            if (!S.PreloadRotation) return list;
            var rot = plugin.UsbRotation(plugin.DashCarKey, out _, out int cur);
            for (int k = from; k < rot.Count; k++)
            {
                var r = rot[(((cur + k) % rot.Count) + rot.Count) % rot.Count];
                if (DashRef.IsWheel(r) || !S.DashUsesRam(DashRef.Id(r))) continue;
                var other = DashRam.TilesOf(DashCache.Find(DashRef.Id(r)));
                if (other == null) continue;
                var extra = other.Files.Where(f => !keep.Contains(f.Name)).ToList();
                int add = extra.Sum(f => ScreenRam.Accounted(f.Jpeg.Length));
                if (total + add > ScreenRam.Budget) break; // the rest stays for later (loaded when it shows)
                total += add;
                foreach (var f in extra) keep.Add(f.Name);
                ram.Touch(other.Files.Select(f => f.Name), now - 1);
                list.AddRange(extra.Where(f => !ram.Has(f.Name)));
            }
            return list;
        }

        /// <summary>
        /// With the RAM drive on: the screensaver's art as tiles, and with it the whole rotation (as far as it fits),
        /// all behind the loading screen when any of it isn't on the screen (after the wheel is switched on: ~15 s). The
        /// screensaver then runs smoothly and the first dash shows at once.
        /// </summary>
        private void PrepareSaverTiles()
        {
            pendingTiles = null; loadingTotal = 0; loadingKeep = null; backgroundTiles = null; backgroundTotal = 0; tiledSaver = null;
            if (!S.ScreenRamDrive || model != WheelModel.FxPro || !(saver is ITiledSaver ts)) return;
            ScreenTiles tiles;
            try { tiles = ts.Tiles; } catch { return; }
            if (tiles == null) return;
            tiledSaver = ts;
            try { conn?.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)ram.Token())); } catch { }
            var files = tiles.Files.ToList();
            double now = clock.Elapsed.TotalSeconds;
            ram.Touch(files.Select(f => f.Name), now);
            loadingKeep = new HashSet<string>(files.Select(f => f.Name));
            // the rotation goes up with it behind the loading screen, not behind the running screensaver: each file
            // freezes the screen for ~0.3 s, which made the screensaver choppy for half a minute (2026-10-01)
            var rotation = RotationFiles(loadingKeep, files.Sum(f => ScreenRam.Accounted(f.Jpeg.Length)), now, 0);
            var missing = files.Where(f => !ram.Has(f.Name)).Concat(rotation).ToList();
            if (missing.Count == 0) { ts.UseTiles = true; return; }
            if (rotation.Count > 0) loadingTitle = "Your dashes";
            pendingTiles = missing; loadingTotal = missing.Count; loadStart = now;
        }

        /// <summary>Tiles of what shows now: the dash's or the screensaver's.</summary>
        private ScreenTiles ShownTiles => renderer?.Tiles ?? tiledSaver?.Tiles;

        /// <summary>Draws what shows now in full: from its tiles, or (they don't fit) as before.</summary>
        private void DrawShown(bool fromTiles, double now)
        {
            if (renderer != null) { renderer.UseTiles(fromTiles && renderer.Tiles != null); renderer.DrawAll(); return; } // no tiles: a dash set to skip the RAM
            if (saver == null) return;
            if (tiledSaver != null) tiledSaver.UseTiles = fromTiles;
            saver.Start();
            if (fromTiles) saver.Step(screen, now, 10000); // a few commands: all of it in this frame
        }

        private int loadingTotal;

        /// <summary>A plain loading screen with a progress bar, drawn after each file (the screen has just repainted its page).</summary>
        /// <summary>What's loading, for the loading screen (the dash's or the screensaver's name).</summary>
        private string loadingTitle;

        private static readonly LedColor[] LedLit =
        {
            new LedColor(0, 224, 112, 80), new LedColor(255, 176, 0, 80), new LedColor(255, 32, 32, 80),
        };

        /// <summary>Loading progress 0-1, or null when nothing loads behind the loading screen.</summary>
        private double? LoadingProgress =>
            pendingTiles == null || loadingTotal <= 0 ? (double?)null : Math.Max(0, Math.Min(1, (loadingTotal - pendingTiles.Count) / (double)loadingTotal));

        /// <summary>
        /// A plain loading screen with a progress bar, drawn after each file (the screen has just repainted its page); the
        /// wheel's rev lights fill along (SendLeds). Kept tiny on purpose: a painted one (a rev counter of big filled
        /// circles, ~1 Mpx) kept the screen busy past the next file's start, which then went astray (2026-10-01).
        /// </summary>
        private void DrawLoading()
        {
            if (pendingTiles == null || screen == null) return;
            if (loadingTotal < pendingTiles.Count) loadingTotal = pendingTiles.Count;
            const int x = 250, y = 250, w = 300, h = 12;
            int fill = (int)Math.Round(w * (LoadingProgress ?? 0));
            screen.Cmd("page 0"); screen.Cmd("vis 255,0"); screen.Cmd("cls 0");
            screen.Cmd(renderer != null ? "xstr 200,200,400,32,5,50712,0,1,1,3,\"LOADING DASH\"" : "xstr 200,200,400,32,5,50712,0,1,1,3,\"LOADING\"");
            screen.Cmd($"fill {x},{y},{w},{h},12678");
            if (fill > 0) screen.Cmd($"fill {x},{y},{fill},{h},63488");
            screen.Flush();
        }

        /// <summary>While the loading screen shows: the wheel's rev lights fill like its bar (a copy of the frame).</summary>
        private LedColor[] LoadingOverlay(LedColor[] frame)
        {
            var p = LoadingProgress;
            if (p == null || frame == null || !model.Has(LedGroup.Rev)) return frame;
            var rev = model.Leds(LedGroup.Rev);
            var f = (LedColor[])frame.Clone();
            int lit = (int)Math.Floor(p.Value * rev.Length + 1e-9);
            for (int i = 0; i < rev.Length; i++)
                if (rev[i] < f.Length) f[rev[i]] = i < lit ? LedLit[i * 3 / rev.Length] : new LedColor(0, 0, 0, 0);
            return f;
        }

        /// <summary>
        /// The screen draws into a frame it shows only when told: `ref_stop` holds what the panel shows (the screen's
        /// present step checks 0x7A104C+0xF2), `ref_star` shows the frame drawn since. Used around anything that makes the
        /// screen repaint its page (a file received or deleted repaints page 0's own "Check1" picture), so only the frame
        /// drawn after it is ever seen. Always paired.
        /// </summary>
        private void Held(Action draw)
        {
            screen.Cmd("ref_stop");
            screen.Flush();
            try { draw(); }
            finally { try { screen.Cmd("ref_star"); screen.Flush(); } catch { } }
        }

        private DateTime nextRearmCheck, lastRearm;

        /// <summary>
        /// The stock bug behind dead buttons after a USB hiccup (FXProDashes firmware-notes.md "stuck buttons"): a report
        /// lost with a dropped link leaves the wheel's send flag cleared for good. Re-armed on every connect, but a drop
        /// too brief for the plugin to see (2026-10-01: buttons dead while connected) needs this: no report for over a
        /// second while the reader is open = stuck, so the flag is set again (one RAM write, harmless when not stuck).
        /// </summary>
        private void KeepInputReports()
        {
            var now = DateTime.UtcNow;
            if ((conn == null || model != WheelModel.FxPro) && (fx == null || model != WheelModel.Fx) || now < nextRearmCheck) return;
            nextRearmCheck = now.AddSeconds(1);
            var b = plugin.Buttons;
            if (b == null || !b.Found || now.Ticks - b.LastReportTicks < TimeSpan.FromSeconds(1).Ticks) return;
            if (fx != null) FxWheel.ReleaseInputReports(fx); else FxUsb.ReleaseInputReports(conn);
            if (now - lastRearm > TimeSpan.FromSeconds(30))
                SimHub.Logging.Current.Info("[FXProRpmSync] USB mode: the wheel's button reports had stopped; started them again");
            lastRearm = now;
        }

        private void UploadTiles(double now)
        {
            if (ramClear) { ramClear = false; ClearRam(); return; }
            var shown = ShownTiles;
            if (pendingTiles == null) { UploadBackground(now); return; } // also while the dash shown is drawn without the RAM
            if (shown == null) return;
            // several files a frame (up to ~0.5 s; the lights keep going through the screen's pacing), the loading
            // screen redrawn after each (the screen repaints its page when a file arrives)
            var budget = System.Diagnostics.Stopwatch.StartNew();
            var keep = loadingKeep ?? new HashSet<string>(shown.Files.Select(f => f.Name));
            while (pendingTiles.Count > 0 && budget.ElapsedMilliseconds < 500)
            {
                var t = pendingTiles[0];
                pendingTiles.RemoveAt(0);
                bool ok = true;
                // One file per hold, then the loading screen drawn again. Several files back to back in one hold (to
                // redraw less often) lost files and left the screen stuck on the wheel (2026-10-01): the screen repaints
                // its page after each file, and what arrives meanwhile isn't reliably taken.
                Held(() =>
                {
                    ok = ram.Has(t.Name) || ram.Upload(screen, t, keep, now);
                    // the batch's end (or a failure): make sure the screen is back in command mode before drawing
                    if (!ok || pendingTiles.Count == 0) ram.Unstick(screen, t.Name);
                    if (!ok) DrawShown(false, now);
                    else if (pendingTiles.Count > 0) DrawLoading();
                    else DrawShown(true, now);
                });
                if (!ok)
                {
                    pendingTiles = null; loadingTotal = 0; loadingKeep = null;
                    RamStatus = "The dash's pictures don't fit in the screen's RAM: drawn with rectangles.";
                    return;
                }
            }
            if (pendingTiles.Count > 0) { RamStatus = $"Loading pictures into the screen ({pendingTiles.Count} to go)..."; return; }
            int loaded = loadingTotal;
            loadingTotal = 0; pendingTiles = null;
            nextBackground = clock.Elapsed.TotalSeconds + 1;
            plugin.SaveSettings(); // the file list, so a SimHub restart knows what the screen has
            string what = renderer != null ? "dash" : "screensaver";
            RamStatus = $"The {what} drawn from the screen's RAM ({shown.Bytes / 1024} KB; {ram.Used / 1024} KB in use).";
            SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: {what} tiles on the screen ({shown.FileCount} files, {shown.Bytes / 1024} KB); " +
                $"loaded {loaded} files in {clock.Elapsed.TotalSeconds - loadStart:0.0} s (waits {ScreenRam.ArmMs}/{ScreenRam.PacketMs}/{ScreenRam.DoneMs} ms); " +
                $"{ram.Evicted} files evicted to make room; drive {ram.Used / 1024} KB in {ram.Count} files");
            ram.Evicted = 0;
        }

        /// <summary>
        /// The rest of the rotation, one file at a time while the dash shows, only while the car stands still, is in the
        /// pit lane or in a menu: each file pauses the dash's values for ~0.3 s, which made it choppy on track (seen on
        /// the wheel 2026-09-30). Each goes up with the panel held (ref_stop) and the dash redrawn from its tiles in the
        /// same frame (the screen repaints its page after a file). Waits doubled: there's no hurry, and a lost file here
        /// would freeze the dash until the batch's end. Back to command mode (Unstick) after the last one.
        /// </summary>
        private void UploadBackground(double now)
        {
            if (backgroundTiles == null || now < nextBackground) return;
            if (saver != null && (tiledSaver == null || !tiledSaver.UseTiles || saver.Drawing)) return; // a saver drawn with fills would be wiped
            var v = frameV;
            bool quiet = saver != null || v == null || !v.Running || v.InMenu || v.InPitLane || v.SpeedKmh < 5;
            if (!quiet)
            {
                if (backgroundTiles.Count > 0) RamStatus = $"Dash drawn from the screen's RAM; the rest of the rotation ({backgroundTiles.Count} files) loads when you stop or pit.";
                return;
            }
            var t = backgroundTiles[0];
            backgroundTiles.RemoveAt(0);
            bool last = backgroundTiles.Count == 0, ok = true;
            var keep = loadingKeep ?? new HashSet<string>((ShownTiles?.Files ?? Enumerable.Empty<ScreenTile>()).Select(f => f.Name));
            int a = ScreenRam.ArmMs, pk = ScreenRam.PacketMs, d = ScreenRam.DoneMs;
            Held(() =>
            {
                ScreenRam.ArmMs = a * 2; ScreenRam.PacketMs = pk * 2; ScreenRam.DoneMs = d * 2;
                try { ok = ram.Has(t.Name) || ram.Upload(screen, t, keep, now); }
                finally { ScreenRam.ArmMs = a; ScreenRam.PacketMs = pk; ScreenRam.DoneMs = d; }
                if (!ok || last) ram.Unstick(screen, t.Name);
                DrawShown(true, now);
            });
            nextBackground = clock.Elapsed.TotalSeconds + 0.1; // values update in between
            if (!ok) { backgroundTiles = null; return; } // no room: the rest loads when it shows
            if (!last) { RamStatus = $"Dash drawn from the screen's RAM; loading the rest of the rotation ({backgroundTiles.Count} to go)..."; return; }
            backgroundTiles = null;
            plugin.SaveSettings();
            RamStatus = $"Dash drawn from the screen's RAM; rotation loaded ({ram.Used / 1024} KB in use).";
            SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: rotation preloaded in the background ({backgroundTotal} files); {ram.Evicted} files evicted; drive {ram.Used / 1024} KB in {ram.Count} files");
            ram.Evicted = 0;
        }

        // ---------- Developer hooks (designer API /api/wheel/ram...) ----------

        /// <summary>The RAM drive as the plugin sees it, and the upload waits.</summary>
        public object RamInfo() => new
        {
            on = S.ScreenRamDrive, preload = S.PreloadRotation, used = ram.Used, data = ram.DataBytes, files = ram.Count,
            budget = ScreenRam.Budget, overhead = ScreenRam.FileOverhead,
            waits = new { arm = ScreenRam.ArmMs, packet = ScreenRam.PacketMs, done = ScreenRam.DoneMs }, status = RamStatus,
        };

        /// <summary>Upload waits for this session (tuning on the wheel).</summary>
        public void RamWaits(int? arm, int? packet, int? done)
        {
            if (arm.HasValue) ScreenRam.ArmMs = Math.Max(0, arm.Value);
            if (packet.HasValue) ScreenRam.PacketMs = Math.Max(0, packet.Value);
            if (done.HasValue) ScreenRam.DoneMs = Math.Max(0, done.Value);
        }

        /// <summary>The budget (KB, accounted) and the per-file overhead (bytes) for this session (tuning on the wheel).</summary>
        public void RamBudget(int? budgetKb, int? overheadBytes)
        {
            if (overheadBytes.HasValue) ScreenRam.FileOverhead = Math.Max(0, overheadBytes.Value);
            if (budgetKb.HasValue) ScreenRam.Budget = Math.Max(16, Math.Min(budgetKb.Value, 1024)) * 1024;
            SimHub.Logging.Current.Info($"[FXProRpmSync] USB mode: screen RAM budget {ScreenRam.Budget / 1024} KB, {ScreenRam.FileOverhead} B per file (this session)");
        }

        /// <summary>Deletes everything the plugin put on the screen; the dash is shown (and loaded) again.</summary>
        public void RamClear() { ramClear = true; Wake(); }

        private void ClearRam()
        {
            var names = (S.ScreenRam?.Files.Keys ?? Enumerable.Empty<string>()).ToList();
            // first back to command mode, whatever state an upload left the screen in (a screen swallowing commands would take none
            // of the deletes), then each file deleted with the screen's wait
            Held(() => { ram.Unstick(screen, null); foreach (var n in names) ram.Delete(screen, n); });
            ram.Forget();
            plugin.SaveSettings();
            screenKey = null;
            RamStatus = $"Deleted {names.Count} files from the screen's RAM.";
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
                else if (frameSleeping) { frame = new LedColor[engine.Count]; LightsState = "off (sleeping)"; }
                else if (scenarioRun != null) { var run = scenarioRun; frame = run.Frame(now, reverseRev); LightsState = "scenario: " + run.Scenario.Title; }
                else if (s.LightsFrom == LightsSource.AtsrHub && !testing)
                {
                    if (ExternalFresh) { frame = Volatile.Read(ref external); LightsState = "ATSR-Hub"; }
                    else LightsState = "no data from ATSR-Hub" + (string.IsNullOrEmpty(s.AtsrDevice) ? " (no device picked)" : " for \"" + s.AtsrDevice + "\"") + ", showing the built-in lights";
                }
                else if (s.LightsFrom == LightsSource.SimHubDevice && !testing)
                {
                    if (DeviceFresh) { frame = Volatile.Read(ref device); LightsState = "SimHub device"; }
                    else LightsState = "no data from SimHub's device \"FX Pro wheel (USB mode)\" (add it in SimHub > Devices), showing the built-in lights";
                }
                else LightsState = testing && s.LightsFrom != LightsSource.BuiltIn ? "built-in (test)" : "built-in";
                // lights drawn by another program: a car alongside still shows (the preset's spotter alerts only)
                if (frame != null && frame.Length == engine.Count && !testing && !TestingLeds && !frameSleeping && scenarioRun == null && s.SpotterOverExternal && s.LightsFrom != LightsSource.BuiltIn)
                {
                    var over = engine.OverlaySpotter(frame, lights, v, now);
                    if (!ReferenceEquals(over, frame)) { frame = over; LightsState += " + spotter"; }
                }
                if (frame == null || frame.Length != engine.Count)
                {
                    var st = carState.Update(v, now);
                    var moment = LightMoment.Of(st, carState.Progress(now), plugin.CarLimiterFor(plugin.DashCarKey));
                    if (extras == null)
                    {
                        extras = new RevExtrasState(() => plugin.Settings.Usb.PitSpeeds ?? (plugin.Settings.Usb.PitSpeeds = new Dictionary<string, double>()),
                                                    learned => plugin.Settings.Usb.PitSpeeds = learned);
                        extras.PitSpeedLearned += () => plugin.SaveSettings();
                    }
                    moment.Extras = extras.Update(v, now, plugin.DashCarKey, plugin.LaunchFor(plugin.DashCarKey));
                    frame = engine.Render(lights, v, source && !testing && !demoOn ? plugin.CurrentLightsLayout : null, now, reverseRev, moment);
                    LightsState += " · " + CarStateTracker.Name(moment.State).ToLowerInvariant();
                }
                if (!frameSleeping && !TestingLeds) frame = LoadingOverlay(frame);
                if (s.PressLights && !frameSleeping && !TestingLeds) frame = PressOverlay(frame, s);
                // every frame passes here (presets, ATSR-Hub, alerts, idle, tests, the API), so the ceiling holds for all
                byte ceiling = s.LedCeilingNow(plugin.NightActive);
                leds.Send(frame, ceiling);
                LastFrame = frame;
                ScreenMirror.Leds(model.HasScreen ? frame : null);
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
            if (fx != null)
            {
                try { leds?.Disable(); } catch { }
                try { fx.Dispose(); } catch { }
                fx = null; leds = null; demo = null; LastFrame = null;
                if (!quiet) SimHub.Logging.Current.Info("[FXProRpmSync] USB mode released the FX");
                return;
            }
            if (neo != null)
            {
                try { leds?.Disable(); } catch { }
                neo.Dispose();
                neo = null; leds = null; demo = null; LastFrame = null;
                if (!quiet) SimHub.Logging.Current.Info("[FXProRpmSync] USB mode released the GT Neo");
                return;
            }
            if (conn == null) return;
            try { leds?.Disable(); } catch { }
            try { screen?.Release(); } catch { }
            try { conn.WriteRam(FxConnection.Ctrl + 0x160, BitConverter.GetBytes(0u)); } catch { }
            screen?.Dispose();
            try { conn.Dispose(); } catch { }
            conn = null; screen = null; leds = null; renderer = null; dash = null; demo = null; saver = null; screenKey = null; dimmed = false;
            shownPage = null; WheelPage = null;
            LastFrame = null;
            ScreenMirror.Held(false); ScreenMirror.Leds(null);
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
