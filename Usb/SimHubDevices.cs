using SimHub.Plugins;
using SimHub.Plugins.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace User.FXProRpmSync
{
    /// <summary>
    /// SimHub's own "Simagic GT Neo" LED device (Devices, brand Simagic). It drives the GT Neo's lights through the same
    /// USB mode as the plugin, so with both on the lights flicker between them. The user can have the plugin turn it
    /// off (and back on): through SimHub's Devices plugin while SimHub runs, then saved the way SimHub saves it. Never by
    /// editing its settings file (SimHub rewrites it on exit). Anything unexpected in SimHub's API only loses this
    /// feature: every call is wrapped, and the SimHub types are touched in methods of their own.
    /// </summary>
    internal static class SimHubGtNeoDevice
    {
        /// <summary>SimHub's device type id for the GT Neo (SimagicDevicesRegistry).</summary>
        public const string DeviceTypeId = "67A11361-13EE-45AE-A461-3E19E14B8F87";

        /// <summary>SimHub has the device added and switched on (null = couldn't tell).</summary>
        public static bool? Enabled(PluginManager pm)
        {
            if (pm == null) return null;
            try { return EnabledCore(pm); }
            catch (Exception ex) { Log("couldn't read SimHub's GT Neo device: " + ex.Message); return null; }
        }

        /// <summary>Switches SimHub's GT Neo device(s) on or off and saves SimHub's device settings. True if it did.</summary>
        public static bool SetEnabled(PluginManager pm, bool on)
        {
            if (pm == null) return false;
            try
            {
                bool done = SetEnabledCore(pm, on);
                if (done) Log("SimHub's GT Neo device switched " + (on ? "on" : "off"));
                return done;
            }
            catch (Exception ex) { Log("couldn't switch SimHub's GT Neo device: " + ex.Message); return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool? EnabledCore(PluginManager pm)
        {
            var devices = Find(pm);
            if (devices == null) return null;
            return devices.Any(d => d.Enabled);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SetEnabledCore(PluginManager pm, bool on)
        {
            var plugin = pm.GetPlugin<DevicesPlugin>();
            var devices = Find(pm);
            if (plugin == null || devices == null || devices.Count == 0) return false;
            foreach (var d in devices) d.Enabled = on;
            plugin.SaveSettings();
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<DeviceInstance> Find(PluginManager pm)
        {
            var plugin = pm.GetPlugin<DevicesPlugin>();
            return plugin?.GetDevices().Where(d => string.Equals(d.DeviceDescriptor?.DeviceTypeID, DeviceTypeId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void Log(string text) { try { SimHub.Logging.Current.Info("[FXProRpmSync] " + text); } catch { } }
    }
}
