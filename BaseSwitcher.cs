using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    /// <summary>Rotation and force for a car or a game (null = the preset's own value).</summary>
    public class BaseCarSetting
    {
        /// <summary>Steering rotation, degrees lock to lock (servos max_wheel_angle and wheel_angle_limit).</summary>
        public int? Angle;
        /// <summary>Overall force, % (servos total_force).</summary>
        public int? Force;

        [JsonIgnore] public bool IsEmpty => Angle == null && Force == null;
    }

    /// <summary>Base settings per car (NEXT.md I). Off by default.</summary>
    public class BaseSettings
    {
        public bool Enabled = false;
        /// <summary>Car key ("Game | CarId") -> settings.</summary>
        public Dictionary<string, BaseCarSetting> Cars = new Dictionary<string, BaseCarSetting>();
        /// <summary>SimHub GameName -> settings (used for cars without their own).</summary>
        public Dictionary<string, BaseCarSetting> Games = new Dictionary<string, BaseCarSetting>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Base preset uuid -> its original servos part (JSON), restored on exit / off / Restore.</summary>
        public Dictionary<string, string> Originals = new Dictionary<string, string>();

        public BaseCarSetting For(string carKey, string game)
        {
            if (carKey != null && Cars.TryGetValue(carKey, out var c) && c != null && !c.IsEmpty) return c;
            if (game != null && Games.TryGetValue(game, out var g) && g != null && !g.IsEmpty) return g;
            return null;
        }
    }

    /// <summary>
    /// Pushes the base's rotation and force per car through SimPro, like the rev lights: SimPro's selected base preset,
    /// part "servos" (id 1: max_wheel_angle, wheel_angle_limit, total_force, dampers...), sent live with
    /// preset_set_dev_config (not saved into the preset). The preset's own servos part is captured the first time and
    /// restored on exit, when switched off, and on Restore. A car without settings gets the original back.
    /// Checked read-only on SimPro 3.2.2 (2026-09-29): every Simagic base preset has this part, whether the base is
    /// connected or not. Not yet tried on a base: only the user can (it changes the force feedback).
    /// </summary>
    internal sealed class BaseSwitcher
    {
        public const string Part = "servos";
        public const int PartId = 1;
        public const int MinAngle = 90, MaxAngle = 2520;

        private readonly SimProClient simPro;
        private readonly Func<BaseSettings> settings;
        private readonly Action save;
        private string modifiedPreset;
        private SimProClient.Wheel baseDevice;
        private readonly Queue<string> pushed = new Queue<string>(); // read-back lags writes: recent pushes aren't user edits
        private JObject lastPushed;
        private static readonly string[] Managed = { "max_wheel_angle", "wheel_angle_limit", "total_force" };

        /// <summary>What the base has now, for the settings page ("900° · 80%"), or null.</summary>
        public string Now { get; private set; }
        public string BaseName => baseDevice?.Name;

        public BaseSwitcher(SimProClient simPro, Func<BaseSettings> settings, Action save)
        {
            this.simPro = simPro; this.settings = settings; this.save = save;
        }

        public bool HasChanges => modifiedPreset != null;

        /// <summary>A car change (or settings change): the base gets the car's (or game's) rotation and force, or its own.</summary>
        public async Task OnCarAsync(string carKey, string game)
        {
            var s = settings();
            if (!s.Enabled) { await RestoreAsync().ConfigureAwait(false); return; }
            var b = await FindBase().ConfigureAwait(false);
            if (b == null) { Now = null; return; }
            var preset = await simPro.GetSelectedPreset(b).ConfigureAwait(false);
            string presetUuid = (string)preset?["preset_uuid"];
            var current = preset?["config"]?[Part]?[PartId.ToString()] as JObject ?? preset?[Part]?[PartId.ToString()] as JObject;
            if (presetUuid == null || current == null) { Now = null; return; }

            // the preset changed under us (SimPro switched it): the old one is SimPro's again
            if (modifiedPreset != null && modifiedPreset != presetUuid) modifiedPreset = null;

            string cur = Canon(current);
            s.Originals.TryGetValue(presetUuid, out var original);
            if (original == null || (cur != original && !pushed.Contains(cur)))
            {
                // first sight of this preset, or the user changed it in SimPro: that's the original now, except the
                // fields that still hold what we pushed (the user's edit went on top of a car's values)
                s.Originals[presetUuid] = original = Canon(Recapture(current, original, lastPushed));
                save();
            }

            var want = Apply(JObject.Parse(original), s.For(carKey, game));
            string wantText = Canon(want);
            Now = Describe(want);
            if (wantText == cur && (modifiedPreset != null || wantText == original)) return;
            await simPro.SetPart(b, presetUuid, Part, PartId, want).ConfigureAwait(false);
            Remember(wantText);
            lastPushed = want;
            modifiedPreset = wantText == original ? null : presetUuid;
            SimHub.Logging.Current.Info($"[FXProRpmSync] base: {Now} for {carKey ?? game ?? "no car"}");
        }

        /// <summary>Puts the preset's own rotation and force back if we changed them.</summary>
        public async Task RestoreAsync()
        {
            var presetUuid = modifiedPreset;
            if (presetUuid == null) return;
            modifiedPreset = null;
            if (!settings().Originals.TryGetValue(presetUuid, out var original)) return;
            var b = baseDevice ?? await FindBase().ConfigureAwait(false);
            if (b == null) return;
            await simPro.SetPart(b, presetUuid, Part, PartId, JObject.Parse(original)).ConfigureAwait(false);
            Remember(original);
            lastPushed = JObject.Parse(original);
            Now = Describe(JObject.Parse(original));
            SimHub.Logging.Current.Info("[FXProRpmSync] base: restored the preset's own rotation and force");
        }

        public void ForgetOriginals() { settings().Originals.Clear(); modifiedPreset = null; }

        /// <summary>The original servos part with this car's values in it (a copy).</summary>
        internal static JObject Apply(JObject original, BaseCarSetting c)
        {
            var o = (JObject)original.DeepClone();
            if (c?.Angle != null)
            {
                int a = Math.Max(MinAngle, Math.Min(MaxAngle, c.Angle.Value));
                o["max_wheel_angle"] = a;
                o["wheel_angle_limit"] = a;
            }
            if (c?.Force != null) o["total_force"] = Math.Max(0, Math.Min(100, c.Force.Value));
            return o;
        }

        /// <summary>
        /// The new original after a user edit: the current part, but a field we manage that still shows our last push
        /// (and differs from the old original) gets the old original's value back.
        /// </summary>
        internal static JObject Recapture(JObject current, string oldOriginal, JObject lastPushed)
        {
            var o = (JObject)current.DeepClone();
            if (oldOriginal == null || lastPushed == null) return o;
            var old = JObject.Parse(oldOriginal);
            foreach (var f in Managed)
                if (JToken.DeepEquals(o[f], lastPushed[f]) && !JToken.DeepEquals(lastPushed[f], old[f]) && old[f] != null) o[f] = old[f].DeepClone();
            return o;
        }

        internal static string Describe(JObject servos) =>
            $"{(int?)servos["max_wheel_angle"] ?? 0}° · {(int?)servos["total_force"] ?? 0}% force";

        private static string Canon(JToken t) => t.ToString(Formatting.None);

        private void Remember(string s)
        {
            pushed.Enqueue(s);
            while (pushed.Count > 8) pushed.Dequeue();
        }

        private async Task<SimProClient.Wheel> FindBase()
        {
            var list = await simPro.Call("get_device_list", new { }).ConfigureAwait(false) as JArray;
            var d = list?.FirstOrDefault(x => (string)x["product_type"] == "base");
            baseDevice = d == null ? null : new SimProClient.Wheel
            {
                DeviceUuid = (string)d["device_uuid"],
                ProductUuid = (string)d["product_uuid"],
                Name = (string)d["product_short_name"] ?? (string)d["product_name"],
            };
            return baseDevice;
        }
    }
}
