using BA63Driver.Interfaces;
using BA63Driver.Mapper;
using SerialDash;
using SimHub.Plugins.Devices;
using SimHub.Plugins.OutputPlugins.GraphicalDash.LedModules;
using SimHub.Plugins.OutputPlugins.GraphicalDash.PSE;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro as a SimHub LED device (NEXT.md D): it shows up in SimHub's Devices ("FX Unleashed" brand), so SimHub's
    /// own LED editor and any SimHub LED profile can drive its 38 lights; the frames go through USB mode like ATSR-Hub's
    /// (Lights tab: "Lights come from: SimHub device").
    /// Built like SimHub's own registries (e.g. Delta EVO:XN): a LedModuleDevice whose LedModuleSettings get our driver.
    ///  - Telemetry LEDs: 21, split 3 + 15 + 3 = left side lights (17-19, top to bottom), rev bar (23-37), right side (20-22).
    ///  - Buttons: 12 (LEDs 0-11); encoders: 5 (12-16, ABS TC BB DIFF MAP).
    ///  - Individual LEDs: 38 in the firmware's order (any non-transparent colour overrides that LED).
    /// SimHub calls GetDevices without a try/catch (an exception there breaks its whole device list), so everything here
    /// fails soft, and the driver is created in its own method so a future SimHub with another driver interface only loses
    /// this device.
    /// </summary>
    public class FXProLedDeviceRegistry : IDeviceDescriptorsRegistry
    {
        public const string DeviceTypeId = "6B7F1E62-3C0A-4D59-9F3E-FA1E0D5E7A38";
        public const int Vid = 0x0483, Pid = 0x0529;

        public IEnumerable<DeviceDescriptor> GetDevices()
        {
            var list = new List<DeviceDescriptor>();
            try
            {
                list.Add(new DeviceDescriptor
                {
                    DeviceTypeID = DeviceTypeId,
                    MaximumInstances = 1,
                    Brand = "FX Unleashed",
                    Name = "FX Pro wheel (USB mode)",
                    AllowParentDeviceSettingsInheritence = false,
                    Factory = SafeCreate,
                    DetectionDescriptor = new USBRequest(Vid, Pid),
                });
            }
            catch (Exception ex) { try { SimHub.Logging.Current.Warn("[FXProRpmSync] SimHub LED device not registered: " + ex.Message); } catch { } }
            return list;
        }

        private static DeviceInstance SafeCreate()
        {
            try { return Create(); }
            catch (Exception ex)
            {
                try { SimHub.Logging.Current.Warn("[FXProRpmSync] SimHub LED device couldn't be created: " + ex.Message); } catch { }
                return null;
            }
        }

        private static DeviceInstance Create() => new LedModuleDevice(new LedModuleSettings<FXProLedDriver>(new LedModuleOptions
        {
            DeviceName = "FX Pro wheel (USB mode)",
            LedCount = 21,
            LedSplit = new LedSplit(3, 15, 3),
            ButtonsCount = 12,
            DefaultButtonsColor = Enumerable.Repeat(Color.White, 12).ToArray(),
            EncodersCount = 5,
            DefaultEncodersColor = Enumerable.Repeat(Color.White, 5).ToArray(),
            RawLedCount = LightEngine.Count,
            TelemetryLedsLabel = "Side lights + rev lights",
            ButtonsColorLabel = "Buttons",
            EncodersColorLabel = "Encoders (ABS, TC, BB, DIFF, MAP)",
            VID = Vid,
            PID = Pid,
            LedDriver = new FXProLedDriver(),
        }));
    }

    /// <summary>
    /// SimHub's LED pipeline hands every frame to Display; it's turned into the wheel's 38 LEDs and passed to the plugin
    /// (SimHubLedDevice.Frame). Connected = the plugin runs and USB mode is on with the wheel found.
    /// </summary>
    public class FXProLedDriver : ILedDeviceManager
    {
        public LedModuleSettings LedModuleSettings { get; set; }
        public LedDeviceState LastState { get; private set; }

#pragma warning disable 67 // SimHub subscribes; this driver has no connection of its own to report
        public event EventHandler BeforeDisplay, AfterDisplay, OnConnect, OnError, OnDisconnect;
#pragma warning restore 67

        public void Display(Func<Color[]> leds, Func<Color[]> buttons, Func<Color[]> encoders, Func<Color[]> matrix, Func<Color[]> rawState,
                            Func<Color[]> overrideState, bool forceRefresh, Func<object> extraData = null, double rpmBrightness = 1.0,
                            double buttonsBrightness = 1.0, double encodersBrightness = 1.0, double matrixBrightness = 1.0)
        {
            try
            {
                BeforeDisplay?.Invoke(this, EventArgs.Empty);
                Color[] l = leds?.Invoke() ?? new Color[0], b = buttons?.Invoke() ?? new Color[0], e = encoders?.Invoke() ?? new Color[0];
                Color[] raw = rawState?.Invoke() ?? new Color[0], ov = overrideState?.Invoke() ?? new Color[0];
                LastState = new LedDeviceState(l, b, e, new Color[0], raw, ov, rpmBrightness, buttonsBrightness, encodersBrightness, matrixBrightness);
                SimHubLedDevice.Publish(SimHubLedDevice.ToFrame(l, b, e, raw, ov, rpmBrightness, buttonsBrightness, encodersBrightness));
                AfterDisplay?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        public bool IsConnected() => SimHubLedDevice.Connected;
        public string GetSerialNumber() => null;
        public string GetFirmwareVersion() => null;
        public object GetDriverInstance() => this;
        public void Close() { }
        public void ResetDetection() { }
        public void SerialPortCanBeScanned(object sender, SerialDashController.ScanArgs e) { }
        public IPhysicalMapper GetPhysicalMapper() => null;
        public ILedDriverBase GetLedDriver() => null;
    }

    /// <summary>Between SimHub's LED pipeline and the plugin (no SimHub types, so the plugin never loads the driver itself).</summary>
    public static class SimHubLedDevice
    {
        /// <summary>Set by the plugin: where frames go (UsbController.PublishDevice).</summary>
        public static Action<LedColor[]> Sink;
        /// <summary>Set by the plugin: USB mode on and the wheel found.</summary>
        public static Func<bool> IsConnected;
        public static bool Connected { get { try { return IsConnected?.Invoke() == true; } catch { return false; } } }

        public static void Publish(LedColor[] frame) { try { Sink?.Invoke(frame); } catch { } }

        // Strip index -> LED: left side top to bottom, rev bar left to right, right side top to bottom.
        private static readonly int[] StripMap = { 17, 18, 19, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 20, 21, 22 };

        /// <summary>SimHub's groups -> the wheel's 38 LEDs (colour, brightness 0-90 as the firmware caps it).</summary>
        public static LedColor[] ToFrame(Color[] leds, Color[] buttons, Color[] encoders, Color[] raw, Color[] over,
                                         double rpmBrightness, double buttonsBrightness, double encodersBrightness)
        {
            var f = new LedColor[LightEngine.Count];
            void Set(int i, Color c, double brightness)
            {
                if (i < 0 || i >= f.Length || c.A == 0 || (c.R | c.G | c.B) == 0) return;
                double k = Math.Max(0, Math.Min(1, brightness)) * c.A / 255.0;
                f[i] = new LedColor(c.R, c.G, c.B, (byte)Math.Max(1, Math.Round(90 * k)));
            }
            for (int i = 0; i < leds.Length && i < StripMap.Length; i++) Set(StripMap[i], leds[i], rpmBrightness);
            for (int i = 0; i < buttons.Length && i < 12; i++) Set(i, buttons[i], buttonsBrightness);
            for (int i = 0; i < encoders.Length && i < 5; i++) Set(12 + i, encoders[i], encodersBrightness);
            for (int i = 0; i < raw.Length && i < f.Length; i++) if (raw[i].A > 0) { f[i] = default; Set(i, raw[i], 1); }
            for (int i = 0; i < over.Length && i < f.Length; i++) if (over[i].A > 0) { f[i] = default; Set(i, over[i], 1); }
            return f;
        }
    }
}
