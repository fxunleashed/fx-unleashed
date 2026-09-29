using GameReaderCommon;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    public class FXProRpmSyncSettings
    {
        /// <summary>Standard: stock wheel, lights and dashes through SimPro. Unlocked: flashed wheel, everything over USB.</summary>
        public WheelMode Mode = WheelMode.Standard;

        /// <summary>Standard mode: keep the rev lights matched to the car through SimPro.</summary>
        public bool Enabled = true;

        /// <summary>Use each car's real shift lights (Lovely Car Data) when available; otherwise rescale the preset.</summary>
        public bool UseCarDatabase = true;
        /// <summary>
        /// For cars whose data has a different curve per gear, push the current gear's curve on every gear change
        /// (the FX Pro has no per-gear mode). Off: one curve for all gears.
        /// </summary>
        public bool LiveGearCurves = true;
        /// <summary>How cars without rev-light data are shown.</summary>
        public FallbackStyle Fallback = new FallbackStyle();

        public string SimProUrl = "http://127.0.0.1:4010/simpro/api/v3";

        /// <summary>Per-car adjustments, keyed by "Game | CarId".</summary>
        public Dictionary<string, CarOverride> Overrides = new Dictionary<string, CarOverride>();

        /// <summary>preset_uuid -> original rpm_lights part (JSON) as it was before this plugin touched it.</summary>
        public Dictionary<string, string> Originals = new Dictionary<string, string>();

        /// <summary>Show each car's dash on the wheel's screen (see DashSwitcher).</summary>
        public bool DashSwitching = true;
        /// <summary>Remember the dash picked with the wheel's dash button while driving a car.</summary>
        public bool LearnDashes = true;
        /// <summary>Dash per car, keyed by "Game | CarId".</summary>
        public Dictionary<string, CarDash> CarDashes = new Dictionary<string, CarDash>();
        /// <summary>preset_uuid -> original screens part (the dash rotation) as it was before this plugin touched it.</summary>
        public Dictionary<string, string> ScreensOriginals = new Dictionary<string, string>();
        /// <summary>Base rotation and force per car/game (BaseSwitcher). Off by default.</summary>
        public BaseSettings Base = new BaseSettings();

        /// <summary>Drive the wheel's dash values from SimHub instead of SimPro's own game telemetry.</summary>
        public FeedSettings Feed = new FeedSettings();

        /// <summary>USB mode: custom dashes and every LED over the wheel's own USB (patched wheel firmware).</summary>
        public UsbSettings Usb = new UsbSettings();

        /// <summary>In-plugin updates (Updater).</summary>
        public UpdateSettings Updates = new UpdateSettings();

        /// <summary>
        /// The settings' format. Raised whenever a migration is added (MigrateSettings); a file written by a newer
        /// plugin (after a roll back) is backed up before this one saves over it.
        /// </summary>
        public int SettingsVersion;
    }

    [PluginDescription("Custom dashes and lights for the Simagic FX Pro: rev lights and dashes per car through SimPro, or, on the flashed wheel, your own dashes and every light over USB. Independent project, not affiliated with Simagic.")]
    [PluginAuthor("ziadkadry99")]
    [PluginName("FX Unleashed")]
    public partial class FXProRpmSyncPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string RpmPart = "rpm_lights";
        private const int RpmPartId = 1;

        public FXProRpmSyncSettings Settings;
        public PluginManager PluginManager { get; set; }
        public System.Windows.Media.ImageSource PictureIcon => icon ?? (icon = LoadIcon());
        private System.Windows.Media.ImageSource icon;
        public string LeftMenuTitle => "FX Unleashed";

        private readonly SimProClient simPro = new SimProClient();
        private CarLedDatabase carDb;
        private DashSwitcher dashes;
        private readonly Dictionary<string, CarLedProfile> profileCache = new Dictionary<string, CarLedProfile>();
        private readonly object sync = new object();
        internal object SyncRoot => sync;
        private long lastDataTicks; // last DataUpdate with a car, for "driving" checks on the worker
        private long lastGameTicks; // last DataUpdate where SimHub reported a running game (plugin enabled or not)
        private volatile string runningGameName;

        // SimHub -> SimPro telemetry feed (SimGame source)
        private SimGameFeed feed;
        private readonly SimProTelemetry feedData = new SimProTelemetry();
        private readonly object feedLock = new object();
        private volatile bool feedOn;
        private DateTime nextSourceCheckUtc;
        public string FeedStatus { get; private set; } = "";
        public bool FeedOn => feedOn;
        /// <summary>The game SimPro is reading telemetry from (null = none, which is what SimPro reports while it reads SimGame).</summary>
        public string SimProSource { get; private set; }
        /// <summary>SimProSource has been read since the feed was turned on.</summary>
        public bool SimProSourceKnown { get; private set; }
        /// <summary>The last attempt to read SimProSource reached SimPro.</summary>
        public bool SimProReachable { get; private set; }
        /// <summary>simgame.exe is running, so SimPro can read SimGame.</summary>
        public bool FeedRunning => feed?.Running == true;
        /// <summary>SimHub reports a running game (seen in the last 3 s).</summary>
        public bool GameRunning => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastGameTicks) < TimeSpan.FromSeconds(3).Ticks;
        /// <summary>The game SimHub reads, while GameRunning.</summary>
        public string RunningGameName => GameRunning ? runningGameName : null;
        /// <summary>
        /// Turning the feed off now would hand SimPro to the running game for good: SimPro keeps a selected game until its
        /// process exits, so turning the feed back on does nothing until the game restarts.
        /// </summary>
        public bool TurningOffNeedsGameRestart => feedOn && FeedRunning && GameRunning && SimProSource == null;
        /// <summary>A copy of the last values sent, for the settings page's live readout.</summary>
        internal SimProTelemetry FeedSnapshot { get; } = new SimProTelemetry();

        // Demo: a simulated lap instead of SimHub's data (not saved; off after a restart).
        private DemoCar demoCar;
        private Timer demoTimer;
        private readonly System.Diagnostics.Stopwatch demoClock = new System.Diagnostics.Stopwatch();
        private double demoLast;
        public bool DemoOn { get; private set; }
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private CancellationTokenSource cts;
        private Task worker;

        // Latest wanted target, written by DataUpdate, consumed by the worker.
        private Target pending;
        private Target lastRequested;

        // Worker state
        private SimProClient.Wheel wheel;
        private BaseSwitcher baseSwitcher;
        /// <summary>The base's current rotation and force as the plugin set them (settings page), or null.</summary>
        internal string BaseNow => baseSwitcher?.Now;
        internal string BaseName => baseSwitcher?.BaseName;
        private string modifiedPresetUuid;
        private Target appliedTarget;
        private double appliedScaleMax;
        private DateTime nextMaxCheckUtc, nextWheelCheckUtc;
        // Recent fingerprints we pushed, per preset. SimPro's read-back lags writes, so a read can return any of
        // these; they must never be mistaken for the user editing the preset.
        private readonly Dictionary<string, List<string>> appliedFingerprints = new Dictionary<string, List<string>>();
        // Live per-gear curves (LiveGearCurves): the current car's rpm_lights per gear, pushed on gear changes.
        private Dictionary<string, JObject> gearParts;
        private string gearPresetUuid, pushedGear;
        private volatile bool gearLive;
        private volatile string wantedGear;
        private int gearPushes;
        private long gearPushMsTotal, gearPushMsMax;

        public string Status { get; private set; } = "Idle";
        public string CurrentCar { get; private set; } = "";
        public double AppliedMaxRpm { get; private set; }
        public double AppliedRedline { get; private set; }
        public string LightsSource { get; private set; } = "";

        // The current car, for the settings page's override editor.
        public string CurrentCarKey { get; private set; }
        public string CurrentGame { get; private set; }
        public string CurrentCarId { get; private set; }
        public string CurrentCarName { get; private set; }
        /// <summary>The current car's lights before any override (what the plugin would show without one).</summary>
        public RpmLayout CurrentBaseLayout { get; private set; }
        public string CurrentBaseSource { get; private set; }

        // The current car for dash switching (set on every car change, even before its RPM range is known).
        public string DashCarKey { get; private set; }
        private string dashCarGame, dashCarId, dashCarName;
        public string CurrentCarNameForDash => dashCarName;
        public string CurrentGameForDash => dashCarGame;
        public string WheelDash => dashes?.WheelDash;
        public string DashStatus => dashes?.Status ?? "";

        // USB mode (patched wheel firmware): custom dash + all LEDs over the wheel's own USB.
        internal UsbController Usb { get; private set; }
        /// <summary>The FX Pro's buttons read directly (bindings for next/previous dash, sleep).</summary>
        internal WheelButtons Buttons { get; private set; }
        private long lastUsbPublishTicks, lastAtsrTicks, lastFormulaTicks, lastWheelTeleTicks;
        private readonly SimProTelemetry wheelTele = new SimProTelemetry();
        private long wheelTeleTicks;

        /// <summary>What the wheel's own dash is being fed (USB mode, last 2 s), for the settings page; null otherwise.</summary>
        internal SimProTelemetry WheelTelemetryNow => DateTime.UtcNow.Ticks - Interlocked.Read(ref wheelTeleTicks) < TimeSpan.FromSeconds(2).Ticks ? wheelTele : null;
        private readonly SimHubFormulas formulas = new SimHubFormulas();
        private readonly Dictionary<string, object> formulaResults = new Dictionary<string, object>();
        private string atsrMapText;
        private DateTime nextAtsrPickUtc;
        private int[] atsrMap;
        /// <summary>The current car's rev lights in real RPM, after any per-car override (null = not known).</summary>
        public RpmLayout CurrentLightsLayout { get; private set; }

        /// <summary>The dash designer's local web server (docs/dash-designer.md); null when off.</summary>
        internal DesignerServer Designer { get; private set; }

        /// <summary>Starts the designer server if it isn't running; returns its URL (null + logged error if the port is taken).</summary>
        internal string StartDesigner()
        {
            if (Designer?.Running == true) return Designer.Url;
            try
            {
                Designer?.Dispose();
                Designer = new DesignerServer(Settings.Usb.DesignerPort, new DesignerHost(this));
                Designer.Start();
                SimHub.Logging.Current.Info("[FXProRpmSync] dash designer on " + Designer.Url);
                return Designer.Url;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[FXProRpmSync] dash designer couldn't start on port " + Settings.Usb.DesignerPort + ": " + ex.Message);
                return null;
            }
        }

        private class Target
        {
            public string CarKey;
            public string GameName;
            public string CarId;
            public string CarModel;
            public double MaxRpm;
            public double Redline;
            public bool Restore;

            public bool SameAs(Target o) =>
                o != null && o.Restore == Restore && o.CarKey == CarKey &&
                Math.Abs(o.MaxRpm - MaxRpm) < 100 && Math.Abs(o.Redline - Redline) < 50;
        }

        /// <summary>The settings format this version writes (see FXProRpmSyncSettings.SettingsVersion).</summary>
        public const int CurrentSettingsVersion = 2;

        /// <summary>Updates from GitHub releases, roll back, start-up marker.</summary>
        internal Updater Updates { get; private set; }
        /// <summary>Init failed: the settings page shows only this and Roll back.</summary>
        internal Exception InitError { get; private set; }

        public void Init(PluginManager pluginManager)
        {
            Updates = new Updater(this);
            Updates.StartupBegin();
            try
            {
                InitCore();
                Updates.StartupSucceeded();
                Updates.CheckInBackground();
            }
            catch (Exception ex)
            {
                // keep SimHub running and the settings page reachable, so the user can roll back an update
                InitError = ex;
                SimHub.Logging.Current.Error("[FXProRpmSync] start-up failed: " + ex);
            }
        }

        private void InitCore()
        {
            MigrateOldSettings();
            Settings = this.ReadCommonSettings("GeneralSettings", () => new FXProRpmSyncSettings { SettingsVersion = CurrentSettingsVersion });
            MigrateSettings(Settings);
            if (Settings.Originals == null) Settings.Originals = new Dictionary<string, string>();
            if (Settings.Fallback == null) Settings.Fallback = new FallbackStyle();
            if (Settings.Overrides == null) Settings.Overrides = new Dictionary<string, CarOverride>();
            if (Settings.CarDashes == null) Settings.CarDashes = new Dictionary<string, CarDash>();
            if (Settings.ScreensOriginals == null) Settings.ScreensOriginals = new Dictionary<string, string>();
            if (Settings.Base == null) Settings.Base = new BaseSettings();
            if (Settings.Base.Cars == null) Settings.Base.Cars = new Dictionary<string, BaseCarSetting>();
            if (Settings.Base.Games == null) Settings.Base.Games = new Dictionary<string, BaseCarSetting>(StringComparer.OrdinalIgnoreCase);
            else Settings.Base.Games = new Dictionary<string, BaseCarSetting>(Settings.Base.Games, StringComparer.OrdinalIgnoreCase);
            if (Settings.Base.Originals == null) Settings.Base.Originals = new Dictionary<string, string>();
            baseSwitcher = new BaseSwitcher(simPro, () => Settings.Base, SaveSettings);
            if (Settings.Feed == null) Settings.Feed = new FeedSettings();
            if (Settings.Feed.Overrides == null) Settings.Feed.Overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Settings.Usb == null) Settings.Usb = new UsbSettings();
            if (Settings.Usb.CarDashes == null) Settings.Usb.CarDashes = new Dictionary<string, UsbCarDash>();
            if (Settings.Usb.Savers == null) Settings.Usb.Savers = new List<SaverItem>();
            if (Settings.Usb.SaverRotation == null) Settings.Usb.SaverRotation = new List<string>();
            MigrateUsb(Settings.Usb);
            EnsureQuickSettings(Settings.Usb);
            SaveSettings(); // keeps what the migration did (ids of moved lights) stable
            // Before modes, USB mode was a tick box: carry it over (the two are kept in step from here on).
            if (Settings.Usb.Enabled) Settings.Mode = WheelMode.Unlocked;
            Settings.Usb.Enabled = Settings.Mode == WheelMode.Unlocked;
            simPro.BaseUrl = Settings.SimProUrl;
            dashes = new DashSwitcher(this, simPro);
            feed = new SimGameFeed(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync"));
            // The SimGame feed is for SimPro's dashes (standard mode); unlocked mode feeds the wheel over USB itself
            if (Settings.Feed.Enabled && Settings.Mode == WheelMode.Standard) SetFeedEnabled(true, save: false);
            carDb = new CarLedDatabase(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync"));

            this.AttachDelegate("Status", () => Status);
            this.AttachDelegate("CurrentCar", () => CurrentCar);
            this.AttachDelegate("AppliedMaxRpm", () => AppliedMaxRpm);
            this.AttachDelegate("AppliedRedline", () => AppliedRedline);
            this.AttachDelegate("LightsSource", () => LightsSource);

            this.AddAction("ReapplyNow", (a, b) => Reapply());
            this.AddAction("RestoreOriginal", (a, b) => RequestRestore());
            // Map these to wheel buttons to tune the current car while driving (saved as an offset override).
            this.AddAction("CurrentCarLightsLater", (a, b) => NudgeCurrentCar(+NudgeStepRpm));
            this.AddAction("CurrentCarLightsEarlier", (a, b) => NudgeCurrentCar(-NudgeStepRpm));
            this.AttachDelegate("CurrentCarOverride", () =>
                CurrentCarKey != null && Settings.Overrides.TryGetValue(CurrentCarKey, out var o) ? o.Summary : "");
            // Map to a wheel button: keep the dash the wheel shows now for this car (learning never overwrites it).
            this.AddAction("KeepWheelDashForCurrentCar", (a, b) => KeepWheelDashForCurrentCar());
            this.AttachDelegate("WheelDash", () => DashCatalog.NameOf(WheelDash));
            this.AttachDelegate("CurrentCarDash", () => DashCatalog.NameOf(GetCarDash(DashCarKey)?.DashId));
            this.AttachDelegate("UsbModeState", () => Usb?.State ?? "");
            this.AddAction("UsbModeToggleDemo", (a, b) => Usb?.SetDemo(!Usb.DemoOn));
            // Unlocked mode: step the current car's custom dash through the library (saved for the car), sleep / wake.
            this.AddAction("UsbNextDash", (a, b) => CycleUsbDash(+1));
            this.AddAction("UsbPreviousDash", (a, b) => CycleUsbDash(-1));
            this.AddAction("UsbSleepNow", (a, b) => Usb?.SleepNow());
            this.AddAction("UsbWake", (a, b) => Usb?.Wake());
            this.AttachDelegate("UsbDash", () => Usb?.ActiveDashName ?? "");
            RegisterQuickControls();
            RegisterFeedProblem();
            Usb = new UsbController(this);
            // the FX Pro as a SimHub LED device (Usb/SimHubLedDevice.cs): its frames come in here
            SimHubLedDevice.Sink = f => { if (Settings.Usb.LightsFrom == User.FXProRpmSync.LightsSource.SimHubDevice) Usb?.PublishDevice(f); };
            SimHubLedDevice.IsConnected = () => Settings.Usb.Enabled && Usb?.WheelFound == true;
            if (Settings.Usb.WheelButtons == null) Settings.Usb.WheelButtons = new Dictionary<string, int>();
            // The dash button (build 5) steps through the dashes unless the user bound it or "next" elsewhere
            if (!Settings.Usb.WheelButtons.ContainsKey("next") && !Settings.Usb.WheelButtons.ContainsValue(WheelButtons.DashButton))
                Settings.Usb.WheelButtons["next"] = WheelButtons.DashButton;
            Buttons = new WheelButtons(this);
            if (Settings.Usb.DesignerServer) StartDesigner();

            cts = new CancellationTokenSource();
            worker = Task.Run(() => WorkerLoop(cts.Token));
            SimHub.Logging.Current.Info("[FXProRpmSync] started");
        }

        private static System.Windows.Media.ImageSource LoadIcon()
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = typeof(FXProRpmSyncPlugin).Assembly.GetManifestResourceStream("User.FXProRpmSync.menu-icon.png");
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>The plugin was called "Simagic RPM Sync" before v0.1; carry its settings (overrides, captured originals) over.</summary>
        private static void MigrateOldSettings()
        {
            try
            {
                var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common");
                var oldFile = System.IO.Path.Combine(dir, "SimagicRpmSyncPlugin.GeneralSettings.json");
                var newFile = System.IO.Path.Combine(dir, "FXProRpmSyncPlugin.GeneralSettings.json");
                if (System.IO.File.Exists(oldFile) && !System.IO.File.Exists(newFile))
                {
                    System.IO.File.Copy(oldFile, newFile);
                    SimHub.Logging.Current.Info("[FXProRpmSync] migrated settings from Simagic RPM Sync");
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] settings migration failed: " + ex.Message); }
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (InitError != null) return;
            if (feedOn) WriteFeed(pluginManager, data);
            if (Settings.Usb.Enabled && Usb != null && DateTime.UtcNow.Ticks - lastUsbPublishTicks >= TimeSpan.FromMilliseconds(30).Ticks)
            {
                lastUsbPublishTicks = DateTime.UtcNow.Ticks;
                try
                {
                    // SimHub formulas of imported dashes: evaluated 10 times a second (the dash's rate), reused between
                    var binds = Usb.Props;
                    if (binds.Length > 0 && DateTime.UtcNow.Ticks - lastFormulaTicks >= TimeSpan.FromMilliseconds(100).Ticks)
                    {
                        lastFormulaTicks = DateTime.UtcNow.Ticks;
                        formulas.SetJavascriptDirectory(Usb.ScriptsFolder);
                        foreach (var b in binds)
                            if (SimHubFormulas.IsFormula(b)) formulaResults[b] = formulas.Eval(b);
                    }
                    Usb.Publish(DashValues.FromSimHub(data, pluginManager, binds, b => formulaResults.TryGetValue(b, out var r) ? r : null));
                }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] USB values: " + ex.Message); }
            }
            // A wheel dash on the screen: its data from SimHub, 10 times a second (see WheelTelemetry)
            if (Settings.Usb.Enabled && Usb?.WheelPage != null && data.GameRunning && DateTime.UtcNow.Ticks - lastWheelTeleTicks >= TimeSpan.FromMilliseconds(100).Ticks)
            {
                lastWheelTeleTicks = DateTime.UtcNow.Ticks;
                try
                {
                    SimHubFeedMapper.Fill(wheelTele, data, pluginManager, Settings.Feed, (section, ex) => SimHub.Logging.Current.Debug("[FXProRpmSync] wheel dash data " + section + ": " + ex.Message));
                    Usb.PublishWheelTelemetry(WheelTelemetry.Build(wheelTele));
                    Interlocked.Exchange(ref wheelTeleTicks, DateTime.UtcNow.Ticks);
                }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] wheel dash data: " + ex.Message); }
            }
            var usb = Settings.Usb;
            if (usb.Enabled) ReadAtsrNight(pluginManager);
            if (usb.Enabled && Usb != null && usb.LightsFrom == User.FXProRpmSync.LightsSource.AtsrHub && DateTime.UtcNow.Ticks - lastAtsrTicks >= TimeSpan.FromMilliseconds(30).Ticks)
            {
                lastAtsrTicks = DateTime.UtcNow.Ticks;
                try
                {
                    if (atsrMap == null || atsrMapText != usb.AtsrMap) { atsrMapText = usb.AtsrMap; atsrMap = AtsrBridge.ParseMap(usb.AtsrMap, out _); }
                    // No device picked yet: take ATSR-Hub's only published device (checked every 3 s).
                    if (string.IsNullOrEmpty(usb.AtsrDevice) && DateTime.UtcNow >= nextAtsrPickUtc)
                    {
                        nextAtsrPickUtc = DateTime.UtcNow.AddSeconds(3);
                        var devices = AtsrBridge.Devices(pluginManager);
                        if (devices.Count == 1)
                        {
                            usb.AtsrDevice = devices[0];
                            SaveSettings();
                            SimHub.Logging.Current.Info("[FXProRpmSync] USB mode lights from ATSR-Hub device " + devices[0]);
                        }
                    }
                    var frame = AtsrBridge.Read(pluginManager, usb.AtsrDevice, atsrMap, usb.AtsrBrightness);
                    if (frame != null) Usb.PublishExternal(frame);
                }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] ATSR-Hub lights: " + ex.Message); }
            }
            if (data.GameRunning && data.NewData != null)
            {
                Interlocked.Exchange(ref lastGameTicks, DateTime.UtcNow.Ticks);
                runningGameName = data.GameName;
            }
            if (!(Settings.Enabled || Settings.DashSwitching || Unlocked || Settings.Base.Enabled) || !data.GameRunning || data.NewData == null) return;
            var d = data.NewData;

            double max = d.CarSettings_MaxRPM > 0 ? d.CarSettings_MaxRPM : d.MaxRpm;
            double red = d.CarSettings_RedLineRPM > 0 ? d.CarSettings_RedLineRPM : d.Redline;
            if (string.IsNullOrEmpty(d.CarId)) return;
            Interlocked.Exchange(ref lastDataTicks, DateTime.UtcNow.Ticks);

            var gear = GearKey(d.Gear);
            if (gear != wantedGear)
            {
                wantedGear = gear;
                if (gearLive) wake.Set();
            }

            var t = new Target
            {
                CarKey = data.GameName + " | " + d.CarId,
                GameName = data.GameName,
                CarId = d.CarId,
                CarModel = d.CarModel,
                MaxRpm = max,
                Redline = red,
            };
            lock (sync)
            {
                if (t.SameAs(lastRequested)) return;
                lastRequested = t;
                pending = t;
            }
            wake.Set();
        }

        /// <summary>SimHub's gear ("R", "N", "1".."10") as a SimPro/car data gear key.</summary>
        private static string GearKey(string gear)
        {
            if (string.IsNullOrEmpty(gear) || gear == "0") return "N";
            gear = gear.Trim().ToUpperInvariant();
            return gear == "R" || gear == "-1" ? "R" : gear;
        }

        public void Reapply()
        {
            lock (sync) { lastRequested = null; }
        }

        /// <summary>Saves the settings (skipped outside SimHub, i.e. in the offline tests, where there's no PluginManager).</summary>
        public void SaveSettings() { if (PluginManager != null) this.SaveCommonSettings("GeneralSettings", Settings); }

        /// <summary>Older USB settings to the current ones: one dash per car -> dash lists, "Your own" lights -> a list.</summary>
        private void MigrateUsb(UsbSettings u)
        {
            if (u.UserLights == null) u.UserLights = new List<LightProfile>();
            if (u.CustomLights != null)
            {
                var c = u.CustomLights;
                c.Id = LightPresets.NewUserId();
                if (string.IsNullOrEmpty(c.Name) || c.Name == "Your own") c.Name = "My lights";
                if (u.LightPreset == LightPresets.CustomId) u.LightPreset = c.Id;
                u.UserLights.Add(c);
                u.CustomLights = null;
            }
            if (u.DefaultDashes == null || u.DefaultDashes.Count == 0)
                u.DefaultDashes = new List<string> { u.DashEnabled ? DashRef.Custom(u.DashId ?? BuiltInDashes.MustangId) : DashRef.Wheel(null) };
            foreach (var kv in u.CarDashes)
                if (kv.Value.Dashes == null || kv.Value.Dashes.Count == 0)
                    kv.Value.Dashes = new List<string>
                    {
                        kv.Value.WheelDash ? DashRef.Wheel(Settings.CarDashes.TryGetValue(kv.Key, out var w) ? w.DashId : null) : DashRef.Custom(kv.Value.DashId ?? u.DashId),
                    };
        }

        // ---------- Mode ----------

        public bool Unlocked => Settings.Mode == WheelMode.Unlocked;

        /// <summary>
        /// Switches between the stock wheel (SimPro drives lights and dashes) and the flashed wheel (USB). Going unlocked
        /// gives SimPro's preset back its own rev lights and dash order, since the plugin now drives the wheel directly.
        /// </summary>
        public void SetMode(WheelMode mode)
        {
            if (Settings.Mode == mode) return;
            Settings.Mode = mode;
            Settings.Usb.Enabled = mode == WheelMode.Unlocked;
            SaveSettings();
            if (mode == WheelMode.Unlocked) RequestRestore();
            // SimPro's SimGame feed only in standard mode (the user's choice is kept for it)
            bool feedWanted = mode == WheelMode.Standard && Settings.Feed.Enabled;
            if (feedWanted != FeedOn) SetFeedEnabled(feedWanted, save: false, remember: false);
            Reapply();
            Usb?.SettingsChanged();
            SimHub.Logging.Current.Info("[FXProRpmSync] mode: " + mode);
        }

        // ---------- Unlocked mode: dashes per car ----------

        /// <summary>Set by the settings page: open the Dashes tab on a car's dash list (from Car tuning).</summary>
        internal Action<string> OpenDashesFor;

        public UsbCarDash GetUsbCarDash(string carKey)
        {
            lock (sync) return carKey != null && Settings.Usb.CarDashes.TryGetValue(carKey, out var d) ? Copy(d) : null;
        }

        private static UsbCarDash Copy(UsbCarDash d) { var c = d.Clone(); c.Dashes = new List<string>(d.Dashes ?? new List<string>()); return c; }

        public List<UsbCarDash> AllUsbCarDashes()
        {
            lock (sync) return Settings.Usb.CarDashes.Values.Select(Copy).OrderBy(d => d.Game).ThenBy(d => d.CarName).ToList();
        }

        /// <summary>
        /// The dashes a car switches between (DashRef refs): its own list, else the default list. `own` says which.
        /// </summary>
        public List<string> UsbRotation(string carKey, out bool own, out int current)
        {
            lock (sync)
            {
                var u = Settings.Usb;
                if (carKey != null && u.CarDashes.TryGetValue(carKey, out var d) && d.Dashes?.Count > 0)
                {
                    own = true; current = d.Current;
                    return new List<string>(d.Dashes);
                }
                own = false; current = u.DefaultCurrent;
                return u.DefaultDashes?.Count > 0 ? new List<string>(u.DefaultDashes) : new List<string> { DashRef.Custom(BuiltInDashes.MustangId) };
            }
        }

        /// <summary>The dash a car shows now: one of the plugin's (Id) or the wheel's own (Wheel; Id null = leave the wheel's).</summary>
        public (bool WheelDash, string DashId) UsbDashFor(string carKey)
        {
            var list = UsbRotation(carKey, out _, out int current);
            var r = list[((current % list.Count) + list.Count) % list.Count];
            return (DashRef.IsWheel(r), DashRef.Id(r));
        }

        /// <summary>Sets a car's dashes (carKey null = the default ones); an empty list gives a car back to the default.</summary>
        public void SetUsbRotation(string carKey, List<string> refs, int current, KnownCar car = null)
        {
            lock (sync)
            {
                var u = Settings.Usb;
                current = refs.Count == 0 ? 0 : ((current % refs.Count) + refs.Count) % refs.Count;
                if (carKey == null) { u.DefaultDashes = new List<string>(refs); u.DefaultCurrent = current; }
                else if (refs.Count == 0) u.CarDashes.Remove(carKey);
                else
                {
                    if (!u.CarDashes.TryGetValue(carKey, out var d))
                        u.CarDashes[carKey] = d = carKey == DashCarKey
                            ? new UsbCarDash { CarKey = carKey, Game = dashCarGame, CarId = dashCarId, CarName = dashCarName }
                            : new UsbCarDash { CarKey = carKey, Game = car?.Game, CarId = car?.CarId, CarName = car?.Name ?? carKey };
                    d.Dashes = new List<string>(refs);
                    d.Current = current;
                    d.UpdatedUtc = DateTime.UtcNow;
                }
            }
            SaveSettings();
            UsbDashChanged(carKey);
        }

        public void DeleteUsbCarDash(string carKey) => SetUsbRotation(carKey, new List<string>(), 0);

        private void UsbDashChanged(string carKey)
        {
            Usb?.SettingsChanged();
            if (carKey == null || carKey == DashCarKey) Reapply(); // a wheel dash: switch the wheel to it
        }

        /// <summary>
        /// Next / previous dash of the current car (or of the default list without a car). A SimHub action, so any
        /// button can do it: a wheel button, a button box, a key.
        /// </summary>
        public void CycleUsbDash(int step)
        {
            if (!Unlocked) return;
            var key = DashCarKey;
            var list = UsbRotation(key, out bool own, out int current);
            if (list.Count < 2) return;
            SetUsbRotation(own ? key : null, list, current + step);
        }

        /// <summary>For the dash switcher: the wheel dash a car should show (unlocked: its current dash, when that's a wheel one).</summary>
        internal CarDash WheelDashTarget(string carKey)
        {
            // Unlocked: the wheel is on USB and ignores SimPro's RF dash switching; the plugin picks the page itself.
            return Unlocked ? null : GetCarDash(carKey);
        }

        // ---------- Per-car overrides ----------

        public const int NudgeStepRpm = 50;

        public CarOverride GetOverride(string carKey)
        {
            lock (sync) return carKey != null && Settings.Overrides.TryGetValue(carKey, out var o) ? o.Clone() : null;
        }

        public List<CarOverride> AllOverrides()
        {
            lock (sync) return Settings.Overrides.Values.Select(o => o.Clone()).OrderBy(o => o.Game).ThenBy(o => o.CarName).ToList();
        }

        public void SaveOverride(CarOverride o)
        {
            o.UpdatedUtc = DateTime.UtcNow;
            lock (sync) Settings.Overrides[o.CarKey] = o.Clone();
            SaveSettings();
            if (o.CarKey == CurrentCarKey) Reapply();
        }

        public void DeleteOverride(string carKey)
        {
            bool removed;
            lock (sync) removed = Settings.Overrides.Remove(carKey);
            if (!removed) return;
            SaveSettings();
            if (carKey == CurrentCarKey) Reapply();
        }

        /// <summary>A new override for the current car, starting from its current lights.</summary>
        public CarOverride NewOverrideForCurrentCar(OverrideKind kind) =>
            CurrentCarKey == null ? null : new CarOverride
            {
                CarKey = CurrentCarKey, Game = CurrentGame, CarId = CurrentCarId, CarName = CurrentCarName,
                Kind = kind, Custom = kind == OverrideKind.Custom ? CurrentBaseLayout?.Clone() : null,
                Style = kind == OverrideKind.Pattern ? Settings.Fallback.Clone() : null,
            };

        public void NudgeCurrentCar(int deltaRpm)
        {
            var o = GetOverride(CurrentCarKey) ?? NewOverrideForCurrentCar(OverrideKind.Offset);
            if (o == null) return;
            if (o.Kind == OverrideKind.Custom && o.Custom != null) o.Custom = o.Custom.Offset(deltaRpm);
            else o.OffsetRpm += deltaRpm;
            SaveOverride(o);
        }

        // ---------- Dash values from SimHub (SimGame feed) ----------

        /// <summary>Starts or stops feeding SimPro from SimHub. Stopping hands the wheel back to SimPro's game telemetry.</summary>
        /// <param name="remember">Store it as the user's choice (false: the mode turned it on/off, the choice stays).</param>
        public void SetFeedEnabled(bool on, bool save = true, bool remember = true)
        {
            lock (feedLock)
            {
                try
                {
                    if (on)
                    {
                        feedData.Clear();
                        feed.Start(feedData.Buffer);
                        SimProSourceKnown = false;
                        FeedStatus = "Running: SimPro reads SimHub's data while no game is selected in SimPro";
                    }
                    else
                    {
                        DemoOn = false;
                        demoTimer?.Dispose();
                        demoTimer = null;
                        feed.Stop();
                        FeedStatus = "Off: SimPro reads the game directly";
                    }
                    feedOn = on;
                }
                catch (Exception ex)
                {
                    feedOn = false;
                    try { feed.Stop(); } catch { }
                    FeedStatus = "Error: " + ex.Message;
                    SimHub.Logging.Current.Warn("[FXProRpmSync] SimGame feed: " + ex);
                }
            }
            if (remember) Settings.Feed.Enabled = on;
            nextSourceCheckUtc = DateTime.MinValue;
            if (save) SaveSettings();
            SimHub.Logging.Current.Info("[FXProRpmSync] SimGame feed " + (feedOn ? "on" : "off"));
        }

        /// <summary>
        /// Demo mode: animates every dash value with a simulated lap (needs the feed, so it turns it on). SimPro must
        /// be reading SimGame, i.e. no game selected in SimPro.
        /// </summary>
        public void SetDemo(bool on)
        {
            if (on && !feedOn) SetFeedEnabled(true);
            lock (feedLock)
            {
                demoTimer?.Dispose();
                demoTimer = null;
                DemoOn = on && feedOn;
                if (DemoOn)
                {
                    demoCar = new DemoCar();
                    demoClock.Restart();
                    demoLast = 0;
                    demoTimer = new Timer(_ => DemoTick(), null, 0, 20);
                }
            }
            SimHub.Logging.Current.Info("[FXProRpmSync] demo " + (DemoOn ? "on" : "off"));
        }

        private void DemoTick()
        {
            lock (feedLock)
            {
                if (!DemoOn || !feedOn) return;
                double now = demoClock.Elapsed.TotalSeconds;
                try
                {
                    demoCar.Step(now - demoLast, feedData);
                    feed.Write(feedData.Buffer);
                    Buffer.BlockCopy(feedData.Buffer, 0, FeedSnapshot.Buffer, 0, SimProTelemetry.Size);
                }
                catch (Exception ex) { FeedStatus = "Error: " + ex.Message; }
                demoLast = now;
            }
        }

        private void WriteFeed(PluginManager pm, GameData data)
        {
            lock (feedLock)
            {
                if (!feedOn || DemoOn) return;
                try
                {
                    SimHubFeedMapper.Fill(feedData, data, pm, Settings.Feed, FeedError);
                    feed.Write(feedData.Buffer);
                    Buffer.BlockCopy(feedData.Buffer, 0, FeedSnapshot.Buffer, 0, SimProTelemetry.Size);
                }
                catch (Exception ex)
                {
                    FeedError("write", ex);
                }
            }
        }

        private readonly HashSet<string> loggedFeedErrors = new HashSet<string>();

        /// <summary>Shows the error and logs each distinct one once (the mapper runs every frame).</summary>
        private void FeedError(string section, Exception ex)
        {
            FeedStatus = $"Error in {section}: {ex.Message}";
            if (loggedFeedErrors.Add(section + "|" + ex.GetType().Name + "|" + ex.Message))
                SimHub.Logging.Current.Warn($"[FXProRpmSync] feed {section} failed: {ex}");
        }

        /// <summary>Which game SimPro reads (SimGame, or a real game that was already running), every 5 s.</summary>
        private async Task CheckFeedSource()
        {
            if (!feedOn || DateTime.UtcNow < nextSourceCheckUtc) { EvaluateFeedProblem(fromSourceCheck: false); return; }
            nextSourceCheckUtc = DateTime.UtcNow.AddSeconds(5);
            try
            {
                SimProSource = await simPro.GetRunningGameName().ConfigureAwait(false);
                SimProReachable = true;
            }
            catch
            {
                SimProSource = null;
                SimProReachable = false;
            }
            SimProSourceKnown = true;
            EvaluateFeedProblem(fromSourceCheck: true);
        }

        // ---------- Dash per car ----------

        public CarDash GetCarDash(string carKey)
        {
            lock (sync) return carKey != null && Settings.CarDashes.TryGetValue(carKey, out var d) ? d.Clone() : null;
        }

        public List<CarDash> AllCarDashes()
        {
            lock (sync) return Settings.CarDashes.Values.Select(d => d.Clone()).OrderBy(d => d.Game).ThenBy(d => d.CarName).ToList();
        }

        /// <param name="apply">Switch the wheel now if it's the current car (false when the wheel already shows it).</param>
        public void SaveCarDash(CarDash d, bool apply = true)
        {
            d.UpdatedUtc = DateTime.UtcNow;
            lock (sync) Settings.CarDashes[d.CarKey] = d.Clone();
            SaveSettings();
            if (apply && d.CarKey == DashCarKey) Reapply();
        }

        public void DeleteCarDash(string carKey)
        {
            bool removed;
            lock (sync) removed = Settings.CarDashes.Remove(carKey);
            if (!removed) return;
            SaveSettings();
            if (carKey == DashCarKey) Reapply();
        }

        /// <summary>A new entry for the current car (no dash set yet), or null when not in a car.</summary>
        public CarDash NewCarDashForCurrentCar() =>
            DashCarKey == null ? null : new CarDash { CarKey = DashCarKey, Game = dashCarGame, CarId = dashCarId, CarName = dashCarName };

        /// <summary>Picks a dash for the current car on the settings page (learning won't overwrite it).</summary>
        public void PickDashForCurrentCar(string dashId)
        {
            var d = GetCarDash(DashCarKey) ?? NewCarDashForCurrentCar();
            if (d == null || dashId == null) return;
            d.DashId = dashId;
            d.Learned = false;
            SaveCarDash(d);
        }

        public void KeepWheelDashForCurrentCar() => PickDashForCurrentCar(WheelDash);

        private bool Driving => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastDataTicks) < TimeSpan.FromSeconds(2).Ticks;

        /// <summary>The selected SimPro preset's original rpm_lights (as captured), for the settings page preview.</summary>
        public JObject PresetTemplate
        {
            get
            {
                lock (sync)
                {
                    string json = null;
                    if (modifiedPresetUuid != null) Settings.Originals.TryGetValue(modifiedPresetUuid, out json);
                    json = json ?? Settings.Originals.Values.FirstOrDefault();
                    return json == null ? null : JObject.Parse(json);
                }
            }
        }

        public void RequestRestore()
        {
            lock (sync)
            {
                pending = new Target { Restore = true };
                lastRequested = pending;
            }
            wake.Set();
        }

        public void ForgetOriginals()
        {
            lock (sync) { Settings.Originals.Clear(); appliedFingerprints.Clear(); Settings.ScreensOriginals.Clear(); }
            dashes.ForgetOriginals();
            this.SaveCommonSettings("GeneralSettings", Settings);
            Status = "Forgot saved originals; the next car change captures the current preset as original";
        }

        public void End(PluginManager pluginManager)
        {
            if (InitError != null) return; // nothing started
            cts?.Cancel();
            wake.Set();
            try { worker?.Wait(2000); } catch { }
            try { Designer?.Dispose(); } catch { }
            try { Buttons?.Dispose(); } catch { }
            SimHubLedDevice.Sink = null; SimHubLedDevice.IsConnected = null;
            try { Usb?.Dispose(); } catch { } // gives the screen and LEDs back to the wheel

            // Leave SimPro as we found it.
            try { RestoreAsync().Wait(3000); } catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] restore on exit failed: " + ex.Message); }
            lock (feedLock) { DemoOn = false; demoTimer?.Dispose(); feedOn = false; feed?.Dispose(); }
            this.SaveCommonSettings("GeneralSettings", Settings);
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager) =>
            InitError != null ? (System.Windows.Controls.Control)new StartupFailedControl(this) : new SettingsControl(this);

        private async Task WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                wake.WaitOne(1000);
                if (ct.IsCancellationRequested) break;

                Target t;
                lock (sync) { t = pending; pending = null; }
                if (t == null && await PushGear().ConfigureAwait(false)) continue;
                if (t == null) t = await CheckGameMaxChanged().ConfigureAwait(false);
                if (t == null) { CheckGameEnded(); await CheckWheelChanged().ConfigureAwait(false); await PollDash().ConfigureAwait(false); await CheckFeedSource().ConfigureAwait(false); continue; }

                try
                {
                    if (t.Restore) await RestoreAsync().ConfigureAwait(false);
                    else
                    {
                        await ApplyDashAsync(t).ConfigureAwait(false);
                        await ApplyBaseAsync(t).ConfigureAwait(false);
                        await ApplyAsync(t).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    wheel = null; // rediscover next time (SimPro restarted / wheel swapped)
                    Status = "Error: " + ex.Message;
                    SimHub.Logging.Current.Warn("[FXProRpmSync] " + ex);
                    lock (sync) { if (pending == null) lastRequested = null; } // retry on next frame
                    await Task.Delay(3000, ct).ContinueWith(_ => { }).ConfigureAwait(false);
                }
            }
        }

        private async Task ApplyDashAsync(Target t)
        {
            DashCarKey = t.CarKey;
            dashCarGame = t.GameName;
            dashCarId = t.CarId;
            dashCarName = string.IsNullOrEmpty(t.CarModel) ? t.CarId : t.CarModel;
            try
            {
                if (Unlocked) return; // over USB the plugin switches the wheel's pages itself (UsbController)
                var w = await GetWheel().ConfigureAwait(false);
                await dashes.OnCarAsync(w, t.CarKey).ConfigureAwait(false);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] dash switch failed: " + ex.Message); }
        }

        /// <summary>The base's rotation and force for this car (BaseSwitcher); its own errors never stop the rev lights.</summary>
        private async Task ApplyBaseAsync(Target t)
        {
            if (baseSwitcher == null || (!Settings.Base.Enabled && !baseSwitcher.HasChanges)) return;
            try { await baseSwitcher.OnCarAsync(t.CarKey, t.GameName).ConfigureAwait(false); }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] base settings: " + ex.Message); }
        }

        private async Task PollDash()
        {
            if (wheel == null || !Settings.DashSwitching) return;
            if (Unlocked && Usb?.ScreenHeld == true) return; // the wheel's dash isn't what's on the screen
            try { await dashes.PollAsync(wheel, Driving, DashCarKey, NewCarDashForCurrentCar).ConfigureAwait(false); }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] dash poll failed: " + ex.Message); }
        }

        /// <summary>
        /// The game closed (SimHub has reported no running game for a few seconds): forget the current car, so the settings
        /// page stops showing it and the next car (even the same one) is applied afresh. The wheel keeps its lights and dash.
        /// </summary>
        private void CheckGameEnded()
        {
            if ((DashCarKey == null && CurrentCarKey == null) || GameRunning) return;
            SimHub.Logging.Current.Info("[FXProRpmSync] game ended: " + (DashCarKey ?? CurrentCarKey));
            lock (sync)
            {
                pending = null;
                lastRequested = null;
            }
            DashCarKey = null;
            dashCarGame = dashCarId = dashCarName = null;
            CurrentCarKey = CurrentGame = CurrentCarId = CurrentCarName = null;
            CurrentBaseLayout = null;
            CurrentBaseSource = null;
            CurrentLightsLayout = null;
            CurrentCar = "";
            LightsSource = "";
            appliedTarget = null; // stops CheckGameMaxChanged re-applying the old car
            gearLive = false;
            gearParts = null;
            pushedGear = null;
            dashes.GameEnded();
            Status = "No game running";
        }

        /// <summary>
        /// Every 5 s: if SimPro now lists a different wheel (swapped on the base), switch to it and re-apply the car.
        /// The old wheel's changes can't be restored any more; SimPro reloads its preset when it's attached again.
        /// </summary>
        private async Task CheckWheelChanged()
        {
            if (DateTime.UtcNow < nextWheelCheckUtc) return;
            nextWheelCheckUtc = DateTime.UtcNow.AddSeconds(5);
            SimProClient.Wheel found;
            try { found = await simPro.FindWheel().ConfigureAwait(false); }
            catch { return; }
            if (found == null || found.DeviceUuid == wheel?.DeviceUuid) return;

            if (wheel != null)
                SimHub.Logging.Current.Info($"[FXProRpmSync] wheel changed: {wheel.Name} -> {found.Name}");
            wheel = found;
            modifiedPresetUuid = null;
            appliedTarget = null;
            gearLive = false;
            gearParts = null;
            pushedGear = null;
            dashes.WheelChanged();
            lock (sync) lastRequested = null; // re-apply the current car on the next frame
        }

        /// <summary>Live per-gear curves: sends the current gear's lights when the gear changed. True if it pushed.</summary>
        private async Task<bool> PushGear()
        {
            var gear = wantedGear;
            if (!gearLive || gearParts == null || gear == null || gear == pushedGear || wheel == null) return false;
            if (!gearParts.TryGetValue(gear, out var part)) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await simPro.SetPart(wheel, gearPresetUuid, RpmPart, RpmPartId, part).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[FXProRpmSync] gear push failed: " + ex.Message);
                pushedGear = null;
                gearLive = false; // the next car apply rebuilds it
                lock (sync) lastRequested = null;
                return false;
            }
            pushedGear = gear;
            long ms = sw.ElapsedMilliseconds;
            gearPushes++;
            gearPushMsTotal += ms;
            gearPushMsMax = Math.Max(gearPushMsMax, ms);
            if (gearPushes == 1 || gearPushes % 50 == 0)
                SimHub.Logging.Current.Info($"[FXProRpmSync] gear pushes: {gearPushes}, avg {gearPushMsTotal / gearPushes} ms, max {gearPushMsMax} ms (last: gear {gear}, {ms} ms)");
            return true;
        }

        /// <summary>SimPro can pick up the new car's max RPM a moment after SimHub reports the car change.</summary>
        private async Task<Target> CheckGameMaxChanged()
        {
            var t = appliedTarget;
            if (t == null || DateTime.UtcNow < nextMaxCheckUtc) return null;
            nextMaxCheckUtc = DateTime.UtcNow.AddSeconds(3);
            try
            {
                var max = await simPro.GetGameMaxRpm().ConfigureAwait(false);
                return max > 0 && Math.Abs(max - appliedScaleMax) >= 1 ? t : null;
            }
            catch { return null; }
        }

        private async Task<SimProClient.Wheel> GetWheel()
        {
            if (wheel == null)
            {
                wheel = await simPro.FindWheel().ConfigureAwait(false);
                if (wheel == null) throw new Exception("No Simagic wheel found in SimPro Manager");
            }
            return wheel;
        }

        private async Task ApplyAsync(Target t)
        {
            bool unlocked = Unlocked;
            if (!unlocked && !Settings.Enabled) return;
            var w = await GetWheel().ConfigureAwait(false);
            var sel = await simPro.GetSelectedPreset(w).ConfigureAwait(false);
            var presetUuid = (string)sel["preset_uuid"];
            var current = sel.SelectToken("config.rpm_lights.1") as JObject
                          ?? throw new Exception("Selected preset has no rpm_lights");

            var original = GetOrCaptureOriginal(presetUuid, current);

            // The wheel scales % thresholds by the game max RPM *SimPro* reads, so use that when we can.
            var simProMax = await simPro.GetGameMaxRpm().ConfigureAwait(false);
            var scaleMax = simProMax > 0 ? simProMax : t.MaxRpm;

            CarLedProfile profile = null;
            if ((Settings.UseCarDatabase || unlocked) && !profileCache.TryGetValue(t.CarKey, out profile))
            {
                profile = await carDb.Find(t.GameName, t.CarId, t.CarModel).ConfigureAwait(false);
                profileCache[t.CarKey] = profile;
            }

            // 1) The car's lights in real RPM, from the best source available.
            RpmLayout layout;
            string source;
            if (profile != null)
            {
                layout = RpmLayout.FromProfile(profile, includeGears: !w.OldDevice || Settings.LiveGearCurves);
                source = $"car database, {profile.MatchedBy} ({profile.LedNumber} LEDs" + (layout.Gears != null ? ", per gear)" : ")");
            }
            else if (t.Redline > 0)
            {
                if (unlocked)
                {
                    // the light preset's own pattern and colours (set in the Lights tab)
                    var lights = ActiveLightsFor(t.CarKey);
                    var rev = lights.Rev;
                    layout = rev.ForShift(t.Redline);
                    source = $"not in car database: {lights.Name}'s rev lights ({LedPatterns.Catalog.First(c => c.Kind == rev.Pattern).Title}), shift point = SimHub redline";
                }
                else
                {
                    var style = Settings.Fallback;
                    layout = RpmLightsMapper.FromStyle(style, original, t.Redline);
                    source = $"not in car database: {LedPatterns.Catalog.First(c => c.Kind == style.Pattern).Title}, shift point = SimHub redline";
                }
            }
            else return; // SimHub hasn't learned this car's RPM range yet

            CurrentCarKey = t.CarKey;
            CurrentGame = t.GameName;
            CurrentCarId = t.CarId;
            CurrentCarName = string.IsNullOrEmpty(t.CarModel) ? t.CarId : t.CarModel;
            CurrentBaseLayout = layout;
            CurrentBaseSource = source;

            // 2) The user's per-car override, if any.
            var ov = GetOverride(t.CarKey);
            if (ov != null)
            {
                layout = ov.Apply(layout, original) ?? layout;
                source += " + override (" + ov.Summary + ")";
            }
            LightsSource = source;
            CurrentLightsLayout = layout;
            if (unlocked)
            {
                // The USB lights show it (LightEngine); SimPro's preset is left alone.
                CurrentCar = t.CarKey;
                AppliedRedline = layout.ShiftRpm;
                AppliedMaxRpm = t.MaxRpm;
                Status = $"{t.CarKey}: {LightsSource}, shift {AppliedRedline:0} rpm (lights over USB)";
                return;
            }

            // 3) To SimPro's percent-of-game-max settings.
            if (scaleMax <= 0) return;
            JObject mapped;
            if (w.OldDevice && Settings.LiveGearCurves && layout.Gears != null)
            {
                // The wheel can't switch curves by gear, so we do: one part per gear, the current one sent now.
                var parts = layout.Gears.Keys.ToDictionary(g => g, g => RpmLightsMapper.ToSimPro(original, layout.ForGear(g), scaleMax, false));
                var gear = wantedGear ?? "N";
                mapped = parts.TryGetValue(gear, out var p) ? p : RpmLightsMapper.ToSimPro(original, layout, scaleMax, false);
                foreach (var part in parts.Values) RememberApplied(presetUuid, part);
                gearParts = parts;
                gearPresetUuid = presetUuid;
                pushedGear = gear;
                gearLive = true;
                LightsSource += ", switched live by gear";
            }
            else
            {
                gearLive = false;
                gearParts = null;
                mapped = RpmLightsMapper.ToSimPro(original, layout, scaleMax, allowPerGear: !w.OldDevice);
            }

            await simPro.SetPart(w, presetUuid, RpmPart, RpmPartId, mapped).ConfigureAwait(false);
            RememberApplied(presetUuid, mapped);
            modifiedPresetUuid = presetUuid;
            appliedTarget = t;
            appliedScaleMax = scaleMax;

            CurrentCar = t.CarKey;
            AppliedMaxRpm = scaleMax;
            AppliedRedline = layout.ShiftRpm;
            Status = $"Applied {t.CarKey} from {LightsSource}: redline {AppliedRedline:0} / max {AppliedMaxRpm:0} rpm";
            SimHub.Logging.Current.Info("[FXProRpmSync] " + Status);
        }

        private void RememberApplied(string presetUuid, JObject part)
        {
            lock (sync)
            {
                if (!appliedFingerprints.TryGetValue(presetUuid, out var list))
                    appliedFingerprints[presetUuid] = list = new List<string>();
                var fp = RpmLightsMapper.Fingerprint(part);
                list.Remove(fp);
                list.Add(fp);
                if (list.Count > 40) list.RemoveAt(0); // room for a car's per-gear parts (12) plus history

            }
        }

        private JObject GetOrCaptureOriginal(string presetUuid, JObject current)
        {
            lock (sync)
            {
                var fp = RpmLightsMapper.Fingerprint(current);
                Settings.Originals.TryGetValue(presetUuid, out var saved);
                appliedFingerprints.TryGetValue(presetUuid, out var applied);

                bool isOurs = applied != null && applied.Contains(fp);
                bool isOriginal = saved != null && RpmLightsMapper.Fingerprint(JObject.Parse(saved)) == fp;

                // First time we see this preset, or the user changed it in SimPro since: current is the new baseline.
                // (After a SimHub restart 'applied' is empty, so a leftover modified preset is only
                //  re-captured if we never saved an original for it.)
                if (saved == null || (!isOurs && !isOriginal && applied != null))
                {
                    saved = current.ToString(Newtonsoft.Json.Formatting.None);
                    Settings.Originals[presetUuid] = saved;
                    this.SaveCommonSettings("GeneralSettings", Settings);
                    SimHub.Logging.Current.Info("[FXProRpmSync] captured original rpm_lights for preset " + presetUuid);
                }
                return JObject.Parse(saved);
            }
        }

        private async Task RestoreAsync()
        {
            if (baseSwitcher != null && baseSwitcher.HasChanges)
            {
                try { await baseSwitcher.RestoreAsync().ConfigureAwait(false); }
                catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] base restore failed: " + ex.Message); }
            }
            if (dashes != null && dashes.HasChanges)
            {
                try { await dashes.RestoreAsync(await GetWheel().ConfigureAwait(false)).ConfigureAwait(false); }
                catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] dash restore failed: " + ex.Message); }
            }

            gearLive = false;
            gearParts = null;
            pushedGear = null;
            var presetUuid = modifiedPresetUuid;
            if (presetUuid == null) return;
            string saved;
            lock (sync) Settings.Originals.TryGetValue(presetUuid, out saved);
            if (saved == null) return;

            var w = await GetWheel().ConfigureAwait(false);
            await simPro.SetPart(w, presetUuid, RpmPart, RpmPartId, JObject.Parse(saved)).ConfigureAwait(false);
            RememberApplied(presetUuid, JObject.Parse(saved));
            modifiedPresetUuid = null;
            appliedTarget = null;
            Status = "Restored original preset RPM lights";
            SimHub.Logging.Current.Info("[FXProRpmSync] " + Status);
        }
    }
}
