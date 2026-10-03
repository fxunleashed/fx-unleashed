using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Undoes the lights' growth in saved settings (fixed 2026-10-02). Two load bugs added to the user's lights on every
    /// SimHub start:
    /// - lists with default items (colours) got the saved items appended to the defaults: one more copy of the defaults in
    ///   front per start;
    /// - UsbSettings.ActiveLights was saved too, and on load Newtonsoft filled the profile it returns (one of UserLights) a
    ///   second time: every list became defaults + saved + saved, so it doubled per start.
    /// The settings file reached ~1 MB and the Lights page took 10+ s to open (one control per colour and alert). Undone
    /// newest first: halve while the list is defaults + X + X (or X + X), then drop leading copies of the defaults. Only
    /// lists far longer than anyone sets are touched, so real lists are left alone.
    /// </summary>
    internal static class LightsRepair
    {
        private const int MaxColours = 16, MaxAlerts = 40, MaxGauges = 8;

        /// <summary>Repairs every light profile in the settings; true if anything changed.</summary>
        public static bool Repair(UsbSettings s, List<string> log)
        {
            bool changed = false;
            void Profiles(IEnumerable<LightProfile> ps, string where)
            {
                foreach (var p in ps ?? Enumerable.Empty<LightProfile>())
                    if (p != null && Repair(p)) { changed = true; log.Add($"{where}: {p.Name ?? p.Id}"); }
            }
            Profiles(s.UserLights, "lights");
            Profiles(new[] { s.CustomLights }, "custom lights");
            foreach (var w in s.Wheels ?? new Dictionary<string, WheelSettings>())
            {
                Profiles(w.Value?.UserLights, w.Key + " lights");
                Profiles(new[] { w.Value?.CustomLights }, w.Key + " custom lights");
            }
            foreach (var kv in s.CarLimiters ?? new Dictionary<string, LimiterLook>())
                if (kv.Value != null && Colours(ref kv.Value.Colors, LimiterDefaults)) { changed = true; log.Add("pit limiter: " + kv.Key); }
            return changed;
        }

        /// <summary>
        /// Moves the spotter of every saved profile that still has it where it used to be (the three small lights beside the rev
        /// bar) to the six buttons on its side. True if anything moved.
        /// </summary>
        public static bool UpgradeSpotter(UsbSettings s, List<string> log)
        {
            bool changed = false;
            void Profiles(IEnumerable<LightProfile> ps, string where)
            {
                foreach (var p in ps ?? Enumerable.Empty<LightProfile>())
                    if (p != null && p.UpgradeSpotterGroups()) { changed = true; log.Add($"{where}: {p.Name ?? p.Id}"); }
            }
            Profiles(s.UserLights, "lights");
            Profiles(new[] { s.CustomLights }, "custom lights");
            foreach (var w in s.Wheels ?? new Dictionary<string, WheelSettings>())
            {
                Profiles(w.Value?.UserLights, w.Key + " lights");
                Profiles(new[] { w.Value?.CustomLights }, w.Key + " custom lights");
            }
            return changed;
        }

        private static readonly string[] GroupDefaults = new GroupLighting().Colors.ToArray();
        private static readonly string[] RevDefaults = new RevLighting().Colors.ToArray();
        private static readonly string[] LimiterDefaults = new LimiterLook().Colors.ToArray();

        private static bool Repair(LightProfile p)
        {
            bool changed = false;
            foreach (var g in p.Groups?.Values ?? Enumerable.Empty<GroupLighting>())
            {
                if (g == null) continue;
                changed |= Colours(ref g.Colors, GroupDefaults);
                changed |= Halve(ref g.Gauges, MaxGauges);
            }
            if (p.Rev != null) changed |= Colours(ref p.Rev.Colors, RevDefaults);
            if (p.Limiter != null) changed |= Colours(ref p.Limiter.Colors, LimiterDefaults);
            changed |= Halve(ref p.Alerts, MaxAlerts);
            return changed;
        }

        private static bool Colours(ref List<string> list, string[] defaults)
        {
            if (list == null || list.Count <= MaxColours) return false;
            var l = list;
            int d = defaults.Length;
            while (true)
            {
                bool lead = l.Count > d && l.Take(d).SequenceEqual(defaults);
                int rest = lead ? l.Count - d : l.Count;
                if (rest > 0 && rest % 2 == 0)
                {
                    var a = l.Skip(lead ? d : 0).Take(rest / 2).ToList();
                    if (a.SequenceEqual(l.Skip((lead ? d : 0) + rest / 2))) { l = a; continue; }
                }
                if (lead) { l = l.Skip(d).ToList(); continue; }
                break;
            }
            if (l.Count == list.Count) return false;
            list = l.Count > 0 ? l : defaults.ToList();
            return true;
        }

        /// <summary>X + X -> X while the list is far longer than anyone sets (items compared as JSON).</summary>
        private static bool Halve<T>(ref List<T> list, int max)
        {
            if (list == null || list.Count <= max) return false;
            var l = list;
            while (l.Count % 2 == 0 && l.Count > 1)
            {
                int h = l.Count / 2;
                bool same = true;
                for (int i = 0; i < h && same; i++) same = JsonConvert.SerializeObject(l[i]) == JsonConvert.SerializeObject(l[h + i]);
                if (!same) break;
                l = l.Take(h).ToList();
            }
            if (l.Count == list.Count) return false;
            list = l;
            return true;
        }
    }
}
