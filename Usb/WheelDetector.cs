using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    /// <summary>How a wheel was found: its own USB device, or SimPro listing it on the base.</summary>
    public enum WheelLink { Usb, Base }

    /// <summary>A wheel the detector found.</summary>
    public sealed class DetectedWheel
    {
        public WheelModel Model;
        public WheelLink Link;
        /// <summary>Its HID path (USB only).</summary>
        public string Path;

        public string Describe() => Model.Name + (Link == WheelLink.Usb ? " · USB" : " · on the base");
    }

    /// <summary>
    /// Which wheels are connected, every 2 s: each model's own USB device (a HID path), and the wheels SimPro lists on
    /// the base (get_device_list, polled in the background so a slow or closed SimPro never holds anything up). A
    /// wheel seen both ways counts once, as USB. `Changed` fires (on a worker thread) when the list changes.
    /// </summary>
    internal sealed class WheelDetector : IDisposable
    {
        private readonly SimProClient simPro = new SimProClient();
        private readonly Timer timer;
        private readonly Func<WheelModel, string> findUsb;
        private volatile List<DetectedWheel> wheels = new List<DetectedWheel>();
        private volatile List<WheelModel> onBase = new List<WheelModel>();
        private DateTime nextSimPro;
        private int polling, running;
        private string lastKey = "";

        /// <summary>The wheels found at the last check.</summary>
        public IReadOnlyList<DetectedWheel> Wheels => wheels;

        /// <summary>The list changed (worker thread).</summary>
        public event Action Changed;

        public WheelDetector() : this(m => FxUsb.FindPath(m.UsbFilter), true) { }

        /// <summary>Tests: USB from `findUsb`, no SimPro, no timer (call Check).</summary>
        internal WheelDetector(Func<WheelModel, string> findUsb, bool start)
        {
            this.findUsb = findUsb;
            if (start) timer = new Timer(_ => Check(), null, 0, 2000);
        }

        /// <summary>The path of a model's USB device at the last check, or null.</summary>
        public string PathOf(WheelModel m) => wheels.FirstOrDefault(w => w.Model == m && w.Link == WheelLink.Usb)?.Path;

        /// <summary>Tests: what SimPro lists on the base.</summary>
        internal void SetBase(params WheelModel[] models) => onBase = models.ToList();

        public void Check()
        {
            if (Interlocked.Exchange(ref running, 1) == 1) return;
            try
            {
                if (timer != null) PollSimPro();
                var list = new List<DetectedWheel>();
                foreach (var m in WheelModel.All)
                {
                    string path = null;
                    try { path = findUsb(m); } catch { }
                    if (path != null) list.Add(new DetectedWheel { Model = m, Link = WheelLink.Usb, Path = path });
                }
                foreach (var m in onBase)
                    if (!list.Any(w => w.Model == m)) list.Add(new DetectedWheel { Model = m, Link = WheelLink.Base });
                wheels = list;
                string key = string.Join(",", list.Select(w => w.Model.Id + ":" + w.Link + ":" + w.Path));
                if (key != lastKey)
                {
                    lastKey = key;
                    try { Changed?.Invoke(); } catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] wheel detection: " + ex.Message); }
                }
            }
            finally { Interlocked.Exchange(ref running, 0); }
        }

        private void PollSimPro()
        {
            if (DateTime.UtcNow < nextSimPro || Interlocked.Exchange(ref polling, 1) == 1) return;
            nextSimPro = DateTime.UtcNow.AddSeconds(4);
            Task.Run(async () =>
            {
                try
                {
                    var found = await simPro.FindWheels().ConfigureAwait(false);
                    onBase = found.Select(w => WheelModel.BySimPro(w.ProductUuid)).Where(m => m != null).Distinct().ToList();
                }
                catch { onBase = new List<WheelModel>(); } // SimPro not running: nothing on the base as far as we know
                finally { Interlocked.Exchange(ref polling, 0); }
            });
        }

        /// <summary>
        /// Which wheel the pages should be for: the only one found; with several, the current one if it's among them,
        /// else the first on USB; with none, the current one (the pages keep their shape while the wheel is off).
        /// </summary>
        public static WheelModel Choose(IReadOnlyList<DetectedWheel> found, WheelModel current)
        {
            if (found == null || found.Count == 0) return current;
            var models = found.Select(w => w.Model).Distinct().ToList();
            if (models.Count == 1) return models[0];
            if (models.Contains(current)) return current;
            return (found.FirstOrDefault(w => w.Link == WheelLink.Usb) ?? found[0]).Model;
        }

        public void Dispose() => timer?.Dispose();
    }
}
