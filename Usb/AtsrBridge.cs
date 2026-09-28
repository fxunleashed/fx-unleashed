using SimHub.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Lights from ATSR-Hub EVO in USB mode. ATSR-Hub publishes each device it drives as SimHub properties
    /// "ATSRHubMain.Device_&lt;name&gt;_Background / _Layer1 / _Layer2 / _Layer3": one "#AARRGGBB" string per LED of the
    /// device's layout (as set up in ATSR-Hub's wheel setup), and "ATSRHubMain.NM_Brightness" (0-100, night mode).
    /// Its own SimHub LED profiles draw those four layers in that order, each on top of the one before where it isn't
    /// transparent; this does the same and sends the result to the FX Pro, whose LEDs SimHub can't drive itself.
    /// </summary>
    internal static class AtsrBridge
    {
        private const string Prefix = "ATSRHubMain.Device_";
        private static readonly string[] Layers = { "_Background", "_Layer1", "_Layer2", "_Layer3" };
        private static readonly Regex DeviceProperty = new Regex(@"^ATSRHubMain\.Device_(.+)_Layer1$", RegexOptions.Compiled);

        /// <summary>Devices ATSR-Hub publishes right now (its matrix devices excluded).</summary>
        public static List<string> Devices(PluginManager pm)
        {
            try
            {
                return pm.GetAllPropertiesNames()
                    .Select(n => DeviceProperty.Match(n))
                    .Where(m => m.Success && !m.Groups[1].Value.Contains("_Matrix"))
                    .Select(m => m.Groups[1].Value)
                    .Distinct().OrderBy(n => n).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// "" = LED N of ATSR-Hub's layout goes to FX Pro LED N. Otherwise 38 comma-separated ATSR-Hub indexes, one per
        /// FX Pro LED (0-11 buttons, 12-16 encoders, 17-22 side lights, 23-37 rev lights); -1 = off.
        /// </summary>
        public static int[] ParseMap(string map, out string error)
        {
            error = null;
            var result = Enumerable.Range(0, LightEngine.Count).ToArray();
            if (string.IsNullOrWhiteSpace(map)) return result;
            var parts = map.Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != LightEngine.Count) { error = $"needs {LightEngine.Count} numbers, has {parts.Length}"; return result; }
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]) || result[i] < -1)
                {
                    error = $"\"{parts[i]}\" isn't an LED index";
                    return Enumerable.Range(0, LightEngine.Count).ToArray();
                }
            return result;
        }

        /// <summary>One frame for the FX Pro, or null when ATSR-Hub isn't publishing this device.</summary>
        public static LedColor[] Read(PluginManager pm, string device, int[] map, bool useBrightness)
        {
            if (string.IsNullOrEmpty(device)) return null;
            var layers = new List<IList>();
            foreach (var l in Layers)
            {
                object v;
                try { v = pm.GetPropertyValue(Prefix + device + l); } catch { v = null; }
                if (v is IList list) layers.Add(list);
            }
            if (layers.Count == 0) return null;

            byte brightness = 90;
            if (useBrightness)
            {
                double nm = 100;
                try { nm = Convert.ToDouble(pm.GetPropertyValue("ATSRHubMain.NM_Brightness") ?? 100.0, CultureInfo.InvariantCulture); } catch { }
                brightness = (byte)Math.Max(1, Math.Min(90, Math.Round(90 * nm / 100)));
            }

            return Compose(layers, map, brightness);
        }

        /// <summary>The layers drawn in order, each over the last by its alpha, onto black; mapped to the FX Pro's LEDs.</summary>
        public static LedColor[] Compose(List<IList> layers, int[] map, byte brightness)
        {
            var frame = new LedColor[LightEngine.Count];
            for (int led = 0; led < frame.Length; led++)
            {
                int src = map[led];
                double r = 0, g = 0, b = 0;
                if (src >= 0)
                    foreach (var layer in layers)
                    {
                        if (src >= layer.Count || !(layer[src] is string hex) || !TryParse(hex, out var a, out var lr, out var lg, out var lb) || a == 0) continue;
                        double k = a / 255.0;
                        r += (lr - r) * k; g += (lg - g) * k; b += (lb - b) * k;
                    }
                frame[led] = new LedColor((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b), brightness);
            }
            return frame;
        }

        /// <summary>"#AARRGGBB" or "#RRGGBB" (opaque).</summary>
        private static bool TryParse(string hex, out int a, out int r, out int g, out int b)
        {
            a = r = g = b = 0;
            if (hex == null) return false;
            hex = hex.Trim().TrimStart('#');
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            if (hex.Length == 6) v |= 0xFF000000;
            else if (hex.Length != 8) return false;
            a = (int)(v >> 24); r = (int)(v >> 16) & 255; g = (int)(v >> 8) & 255; b = (int)v & 255;
            return true;
        }
    }
}
