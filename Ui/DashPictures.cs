using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Pictures of dashes drawn by the wheel's own renderer (text in stand-in letters at the screen fonts' real sizes):
    /// stills for galleries (cached), and a live preview that runs the demo lap (or shows the wheel's live values).
    /// </summary>
    internal static class DashPictures
    {
        private static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        private static string Key(DashDefinition d) =>
            d.Id + "|" + (d.FilePath != null && File.Exists(d.FilePath) ? File.GetLastWriteTimeUtc(d.FilePath).Ticks : 0);

        /// <summary>The dash a little way into the demo lap (so its values are filled in).</summary>
        public static BitmapSource Still(DashDefinition d)
        {
            if (d == null) return null;
            string key = Key(d);
            lock (cache) if (cache.TryGetValue(key, out var hit)) return hit;
            BitmapSource img = null;
            try
            {
                using (var screen = new PreviewScreen())
                {
                    var room = DashRenderer.Room(d);
                    var r = new DashRenderer(screen, d, Math.Min(10, room.Right), Math.Min(20, room.Down));
                    r.DrawAll();
                    var demo = new UsbDemo(d);
                    DashValues v = null;
                    for (int i = 0; i < 12; i++) v = demo.Step(2.5);
                    r.Update(v, 30);
                    img = Theme.ToImage(screen.Bitmap);
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] dash picture " + d.Id + ": " + ex.Message); }
            lock (cache) cache[key] = img;
            return img;
        }

        /// <summary>A screensaver as the wheel draws it (the logo fully drawn in; dashes with the time).</summary>
        public static BitmapSource Saver(SaverItem item)
        {
            string key = "saver|" + item.Id + "|" + (item.Kind == SaverKind.Clock ? DateTime.Now.ToString("HH:mm") : "");
            lock (cache) if (cache.TryGetValue(key, out var hit)) return hit;
            BitmapSource img = null;
            try
            {
                using (var screen = new PreviewScreen())
                {
                    var d = IdleScreens.DashFor(item);
                    if (d == null)
                    {
                        var saver = new ScreenSaver();
                        saver.Start();
                        for (int i = 0; i < 400 && saver.Drawing; i++) saver.Step(screen, 1.2, 500);
                    }
                    else
                    {
                        var room = DashRenderer.Room(d);
                        var r = new DashRenderer(screen, d, Math.Min(10, room.Right), Math.Min(20, room.Down));
                        r.DrawAll();
                        var v = new DashValues();
                        IdleScreens.AddClock(v);
                        r.Update(v, 1);
                    }
                    img = Theme.ToImage(screen.Bitmap);
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] saver picture " + item.Id + ": " + ex.Message); }
            lock (cache) cache[key] = img;
            return img;
        }
    }

    /// <summary>A dash drawn live into an Image, 10 times a second like the wheel: the wheel's values while it shows this
    /// dash, else the demo lap.</summary>
    internal sealed class LiveDashPreview : IDisposable
    {
        private readonly Image target;
        private readonly Func<UsbController> usb;
        private PreviewScreen screen;
        private DashRenderer renderer;
        private UsbDemo demo;
        private string key;
        private double last;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public DashDefinition Dash { get; private set; }
        public List<string> Problems { get; private set; } = new List<string>();

        public LiveDashPreview(Image target, Func<UsbController> usb) { this.target = target; this.usb = usb; }

        public void Show(DashDefinition d, int padLeft, int padTop)
        {
            string k = d == null ? null : d.Id + "|" + padLeft + "|" + padTop + "|" + d.FilePath;
            if (k == key) return;
            key = k;
            Dash = d;
            screen?.Dispose(); screen = null; renderer = null; demo = null;
            if (d == null) { target.Source = null; return; }
            screen = new PreviewScreen();
            var room = DashRenderer.Room(d);
            renderer = new DashRenderer(screen, d, Math.Min(padLeft, room.Right), Math.Min(padTop, room.Down));
            renderer.DrawAll();
            Problems = renderer.Check();
        }

        public void Tick()
        {
            if (renderer == null) return;
            double now = clock.Elapsed.TotalSeconds;
            var u = usb();
            DashValues v = u?.DashActive == true && u.ActiveDashName == Dash.Name ? u.Latest : null;
            if (v == null)
            {
                if (demo == null) { demo = new UsbDemo(); last = now; }
                demo.UseDash(Dash);
                v = demo.Step(Math.Min(1, now - last));
                last = now;
            }
            renderer.Update(v, now);
            target.Source = Theme.ToImage(screen.Bitmap);
        }

        public void Dispose() { screen?.Dispose(); screen = null; renderer = null; key = null; }
    }
}

namespace User.FXProRpmSync
{
    /// <summary>The dash library for the settings page, re-read only when the dashes folder changes (or on Reload).</summary>
    internal static class DashCache
    {
        private static System.Collections.Generic.List<DashDefinition> list;
        private static string stamp;
        public static System.Collections.Generic.List<string> Errors { get; private set; } = new System.Collections.Generic.List<string>();

        public static System.Collections.Generic.List<DashDefinition> All(bool force = false)
        {
            string now = Stamp();
            if (!force && list != null && now == stamp) return list;
            var errors = new System.Collections.Generic.List<string>();
            list = DashLibrary.Load(errors);
            Errors = errors;
            stamp = now;
            return list;
        }

        public static DashDefinition Find(string id) => id == null ? null : System.Linq.Enumerable.FirstOrDefault(All(), d => d.Id == id);

        public static string NameOf(string id) => Find(id)?.Name ?? id ?? "none";

        private static string Stamp()
        {
            try
            {
                if (!System.IO.Directory.Exists(DashLibrary.Folder)) return "none";
                var files = System.IO.Directory.GetFiles(DashLibrary.Folder, "*.json");
                long max = 0;
                foreach (var f in files) max = System.Math.Max(max, System.IO.File.GetLastWriteTimeUtc(f).Ticks);
                return files.Length + "|" + max;
            }
            catch { return "error"; }
        }
    }
}
