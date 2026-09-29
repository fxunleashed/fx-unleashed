using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode's quick controls (the Wheel tab's card, SimHub actions and wheel buttons): screen on/off, the LED
    /// ceiling, screen brightness, night mode, light presets (per car / game / global) and the custom/wheel dash swap.
    /// Every change applies live and is saved, except the dash swap and a night toggle against the schedule.
    /// </summary>
    public partial class FXProRpmSyncPlugin
    {
        public const int LedCeilingStep = 10, ScreenBrightnessStep = 10;

        private volatile bool atsrNight;
        private bool? nightOverride;   // a toggle against the schedule / ATSR-Hub, until they change their mind
        private bool lastAutoNight;
        private long lastAtsrNightTicks;

        private void RegisterQuickControls()
        {
            this.AddAction("UsbScreenToggle", (a, b) => ToggleScreen());
            this.AddAction("UsbLedBrightnessUp", (a, b) => StepLedCeiling(+LedCeilingStep));
            this.AddAction("UsbLedBrightnessDown", (a, b) => StepLedCeiling(-LedCeilingStep));
            this.AddAction("UsbScreenBrightnessUp", (a, b) => StepScreenBrightness(+ScreenBrightnessStep));
            this.AddAction("UsbScreenBrightnessDown", (a, b) => StepScreenBrightness(-ScreenBrightnessStep));
            this.AddAction("UsbNightModeToggle", (a, b) => ToggleNightMode());
            this.AddAction("UsbNextLightPreset", (a, b) => CycleLightPreset(+1));
            this.AddAction("UsbPreviousLightPreset", (a, b) => CycleLightPreset(-1));
            this.AddAction("UsbWheelDashToggle", (a, b) => ToggleWheelDash());
            this.AttachDelegate("Usb.ScreenOn", () => !Settings.Usb.ScreenOff && Usb?.Sleeping != true);
            this.AttachDelegate("Usb.LedCeiling", () => (int)Settings.Usb.LedCeilingNow(NightActive));
            this.AttachDelegate("Usb.ScreenBrightness", () => Settings.Usb.ScreenBrightnessNow(NightActive));
            this.AttachDelegate("Usb.NightMode", () => NightActive);
            this.AttachDelegate("Usb.LightPreset", () => ActiveLightsFor(DashCarKey)?.Name ?? "");
            this.AttachDelegate("Usb.WheelDashSwapped", () => Usb?.SwapDash == true);
        }

        private void QuickChanged() { SaveSettings(); Usb?.SettingsChanged(); }

        public void ToggleScreen() { Settings.Usb.ScreenOff = !Settings.Usb.ScreenOff; QuickChanged(); }

        public void SetScreenOff(bool off) { if (Settings.Usb.ScreenOff == off) return; Settings.Usb.ScreenOff = off; QuickChanged(); }

        /// <summary>Steps the LED ceiling of what shows now: the night ceiling at night, else the day one.</summary>
        public void StepLedCeiling(int delta)
        {
            var u = Settings.Usb;
            if (NightActive) u.NightLedCeiling = Clamp(Math.Min(u.NightLedCeiling, u.LedCeiling) + delta, 1, 90);
            else u.LedCeiling = Clamp(u.LedCeiling + delta, 1, 90);
            QuickChanged();
        }

        public void StepScreenBrightness(int delta)
        {
            var u = Settings.Usb;
            if (NightActive) u.NightScreenBrightness = Clamp(u.NightScreenBrightness + delta, 5, 100);
            else u.ScreenBrightness = Clamp(u.ScreenBrightness + delta, 5, 100);
            QuickChanged();
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        /// <summary>
        /// Night mode now: switched on by hand, or by the schedule / ATSR-Hub's night mode, unless toggled against them
        /// (that toggle lasts until the schedule or ATSR-Hub next changes).
        /// </summary>
        public bool NightActive
        {
            get
            {
                var u = Settings?.Usb;
                if (u == null) return false;
                bool auto = (u.NightSchedule && UsbSettings.InWindow(u.NightFrom, u.NightTo, DateTime.Now.TimeOfDay)) ||
                            (u.NightFollowAtsr && atsrNight && DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref lastAtsrNightTicks) < TimeSpan.FromSeconds(5).Ticks);
                if (auto != lastAutoNight) { lastAutoNight = auto; nightOverride = null; }
                return nightOverride ?? (u.NightMode || auto);
            }
        }

        public void ToggleNightMode()
        {
            var u = Settings.Usb;
            bool now = NightActive;
            if (lastAutoNight || (!u.NightMode && now)) nightOverride = !now; // against the schedule / ATSR-Hub: until it changes
            else { u.NightMode = !now; nightOverride = null; }
            QuickChanged();
        }

        /// <summary>The settings page's switch: night mode by hand (clears a toggle against the schedule).</summary>
        public void SetNightMode(bool on)
        {
            Settings.Usb.NightMode = on;
            nightOverride = null;
            if (NightActive != on) nightOverride = on; // off while the schedule / ATSR-Hub says night
            QuickChanged();
        }

        /// <summary>ATSR-Hub's night mode (its NM_Brightness below 100), read in DataUpdate once a second.</summary>
        private void ReadAtsrNight(PluginManager pm)
        {
            if (!Settings.Usb.NightFollowAtsr || DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref lastAtsrNightTicks) < TimeSpan.FromSeconds(1).Ticks) return;
            try
            {
                var v = pm.GetPropertyValue("ATSRHubMain.NM_Brightness");
                if (v == null) { atsrNight = false; return; }
                atsrNight = Convert.ToDouble(v, CultureInfo.InvariantCulture) < 100;
                System.Threading.Interlocked.Exchange(ref lastAtsrNightTicks, DateTime.UtcNow.Ticks);
            }
            catch { atsrNight = false; }
        }

        /// <summary>
        /// The lights a car uses: its own preset, else its game's, else the global one. `carKey` is "Game | CarId"
        /// (null = no car: the global preset).
        /// </summary>
        public LightProfile ActiveLightsFor(string carKey)
        {
            var u = Settings.Usb;
            string id = LightsIdFor(carKey, out _);
            return u.FindLights(id) ?? u.ActiveLights;
        }

        /// <summary>The preset id a car resolves to and where it comes from ("car", "game" or "global").</summary>
        public string LightsIdFor(string carKey, out string level)
        {
            var u = Settings.Usb;
            lock (sync)
            {
                if (carKey != null && u.CarLights != null && u.CarLights.TryGetValue(carKey, out var c) && u.FindLights(c) != null) { level = "car"; return c; }
                string game = GameOf(carKey);
                if (game != null && u.GameLights != null && u.GameLights.TryGetValue(game, out var g) && u.FindLights(g) != null) { level = "game"; return g; }
            }
            level = "global";
            return u.LightPreset;
        }

        /// <summary>The game part of a "Game | CarId" key.</summary>
        public static string GameOf(string carKey)
        {
            if (string.IsNullOrEmpty(carKey)) return null;
            int i = carKey.IndexOf(" | ", StringComparison.Ordinal);
            return i > 0 ? carKey.Substring(0, i) : null;
        }

        /// <summary>Sets (or with null clears) the preset of a car or a game.</summary>
        public void SetCarLights(string carKey, string id)
        {
            if (carKey == null) return;
            lock (sync) { if (id == null) Settings.Usb.CarLights.Remove(carKey); else Settings.Usb.CarLights[carKey] = id; }
            QuickChanged();
            if (carKey == DashCarKey) Reapply(); // the fallback rev lights come from the preset
        }

        public void SetGameLights(string game, string id)
        {
            if (game == null) return;
            lock (sync) { if (id == null) Settings.Usb.GameLights.Remove(game); else Settings.Usb.GameLights[game] = id; }
            QuickChanged();
            if (GameOf(DashCarKey) == game) Reapply();
        }

        /// <summary>
        /// Next / previous light preset for what's driving now: the car's own preset if it has one, else its game's, else
        /// the global one.
        /// </summary>
        public void CycleLightPreset(int step)
        {
            var u = Settings.Usb;
            var ids = u.AllLightIds();
            if (ids.Count == 0) return;
            string key = DashCarKey;
            string cur = LightsIdFor(key, out string level);
            int i = ids.IndexOf(cur);
            string next = ids[((i < 0 ? 0 : i + step) % ids.Count + ids.Count) % ids.Count];
            if (level == "car") SetCarLights(key, next);
            else if (level == "game") SetGameLights(GameOf(key), next);
            else { u.LightPreset = next; QuickChanged(); if (key != null) Reapply(); }
        }

        public void ToggleWheelDash()
        {
            if (Usb == null || !Unlocked) return;
            Usb.SwapDash = !Usb.SwapDash;
        }

        /// <summary>Older settings get the dictionaries added since.</summary>
        private static void EnsureQuickSettings(UsbSettings u)
        {
            if (u.CarLights == null) u.CarLights = new Dictionary<string, string>();
            if (u.GameLights == null) u.GameLights = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            else if (!Equals(u.GameLights.Comparer, StringComparer.OrdinalIgnoreCase))
                u.GameLights = new Dictionary<string, string>(u.GameLights, StringComparer.OrdinalIgnoreCase);
            u.LedCeiling = Clamp(u.LedCeiling, 1, 90);
            u.NightLedCeiling = Clamp(u.NightLedCeiling, 1, 90);
            u.NightScreenBrightness = Clamp(u.NightScreenBrightness, 5, 100);
        }
    }
}
