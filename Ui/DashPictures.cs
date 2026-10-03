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

        /// <summary>
        /// Whether the wheel draws this dash from the screen's RAM pictures: its pictures fit the drive (else it's drawn with
        /// rectangles). Previews draw the same way when the drive is on, so they show what the wheel shows.
        /// </summary>
        internal static bool WantsTiles(DashDefinition d)
        {
            if (d == null) return false;
            // per dash file version: working out the key of DashRam's cache serializes the whole dash (slow on the UI thread)
            string key = Key(d);
            lock (wants) if (wants.TryGetValue(key, out bool hit)) return hit;
            bool fits;
            try { fits = DashRam.Bytes(new[] { d }) <= ScreenRam.Budget; } catch { fits = false; }
            lock (wants) wants[key] = fits;
            return fits;
        }

        private static readonly Dictionary<string, bool> wants = new Dictionary<string, bool>();

        // The keys say what was asked for (`ram` = drawn from the RAM drive if it fits): what that gives is fixed by the dash
        // file and the plugin version, so a picture on disk needs no tiles worked out to be found.
        private static string StillKey(DashDefinition d, bool ram) => Key(d) + (ram ? "|ram" : "");

        private static string SaverKey(SaverItem item, bool ram) =>
            "saver|" + item.Id + "|" + (item.Kind == SaverKind.Clock ? DateTime.Now.ToString("HH:mm") : "") + (ram ? "|ram" : "");

        // ---- pictures for galleries, drawn off the UI thread (two at a time, below normal priority) ----

        private static readonly System.Collections.Concurrent.BlockingCollection<Action> queue = StartWorkers(2);

        private static System.Collections.Concurrent.BlockingCollection<Action> StartWorkers(int count)
        {
            var q = new System.Collections.Concurrent.BlockingCollection<Action>();
            for (int i = 0; i < count; i++)
                new System.Threading.Thread(() =>
                {
                    foreach (var job in q.GetConsumingEnumerable())
                        try { job(); } catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] picture: " + ex.Message); }
                }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "FXProRpmSync pictures " + i }.Start();
            return q;
        }

        /// <summary>Shows a dash's still in `img`: at once if it's in memory, else when a background thread has loaded or drawn it.</summary>
        public static void ShowStill(System.Windows.Controls.Image img, DashDefinition d, bool ram)
        {
            if (d == null) return;
            lock (cache) if (cache.TryGetValue(StillKey(d, ram), out var hit)) { img.Source = hit; return; }
            Later(img, () => Still(d, ram));
        }

        /// <summary>Shows a screensaver's picture in `img`, the same way.</summary>
        public static void ShowSaver(System.Windows.Controls.Image img, SaverItem item, bool ram)
        {
            if (item == null) return;
            lock (cache) if (cache.TryGetValue(SaverKey(item, ram), out var hit)) { img.Source = hit; return; }
            Later(img, () => Saver(item, ram));
        }

        private static void Later(System.Windows.Controls.Image img, Func<BitmapSource> make)
        {
            var ui = img.Dispatcher;
            queue.Add(() =>
            {
                var b = make();
                ui.BeginInvoke(new Action(() => img.Source = b));
            });
        }

        /// <summary>
        /// Puts the galleries' pictures on disk in the background (those not there yet), so the pages show them at once the
        /// first time they open. Called at start-up; costs nothing once the pictures are on disk.
        /// </summary>
        public static void WarmUp(UsbSettings s)
        {
            queue.Add(() =>
            {
                // what the Dashes page reads first: the dash files and the wheel dashes' thumbnails
                DashCache.All();
                foreach (var w in DashCatalog.All) DashSection.ThumbSource(w.Id);
                System.Threading.Thread.Sleep(8000); // the pictures after SimHub's own start-up
                foreach (var d in DashCache.All())
                {
                    string id = d.Id;
                    queue.Add(() => { var key = StillKey(d, s.RamFor(id)); if (!Disk.Has(key)) Disk.Save(key, Render(d, s.RamFor(id))); });
                }
                foreach (var item in IdleScreens.All(s))
                    if (Disk.Wants(item))
                        queue.Add(() => { var key = SaverKey(item, s.ScreenRamDrive); if (!Disk.Has(key)) Disk.Save(key, RenderSaver(item, s.ScreenRamDrive)); });
            });
        }

        /// <summary>The dash a little way into the demo lap (so its values are filled in); `ram`: as drawn from the screen's RAM drive.</summary>
        public static BitmapSource Still(DashDefinition d, bool ram = false)
        {
            if (d == null) return null;
            string key = StillKey(d, ram);
            lock (cache) if (cache.TryGetValue(key, out var hit)) return hit;
            var img = Disk.Load(key);
            if (img == null) { img = Render(d, ram); Disk.Save(key, img); }
            lock (cache) cache[key] = img;
            return img;
        }

        private static BitmapSource Render(DashDefinition d, bool ram)
        {
            ram = ram && WantsTiles(d);
            BitmapSource img = null;
            try
            {
                using (var screen = new PreviewScreen())
                {
                    var room = DashRenderer.Room(d);
                    var r = new DashRenderer(screen, d, Math.Min(10, room.Right), Math.Min(20, room.Down));
                    if (ram) { r.EnableTiles(); r.UseTiles(true); }
                    r.DrawAll();
                    var demo = new UsbDemo(d);
                    DashValues v = null;
                    for (int i = 0; i < 12; i++) v = demo.Step(2.5);
                    r.Update(v, 30);
                    img = Theme.ToImage(screen.Bitmap);
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] dash picture " + d.Id + ": " + ex.Message); }
            return img;
        }

        /// <summary>A screensaver as the wheel draws it (the logo fully drawn in; dashes with the time).</summary>
        public static BitmapSource Saver(SaverItem item, bool ram = false)
        {
            string key = SaverKey(item, ram);
            lock (cache) if (cache.TryGetValue(key, out var hit)) return hit;
            var img = Disk.Wants(item) ? Disk.Load(key) : null;
            if (img == null)
            {
                img = RenderSaver(item, ram);
                if (Disk.Wants(item)) Disk.Save(key, img);
            }
            lock (cache) cache[key] = img;
            return img;
        }

        private static BitmapSource RenderSaver(SaverItem item, bool ram)
        {
            BitmapSource img = null;
            try
            {
                using (var screen = new PreviewScreen())
                {
                    var d = IdleScreens.DashFor(item);
                    if (d == null)
                    {
                        var saver = IdleScreens.Animated(item, new LastSession { Car = "McLaren 720S GT3 Evo", BestLap = 107.832, Laps = 23, Position = 3 }) ?? new ScreenSaver();
                        if (ram && saver is ITiledSaver ts) { try { ts.UseTiles = ts.Tiles != null; } catch { } } // drawn from its pictures, as on the wheel
                        saver.Start();
                        for (int i = 0; i < 400 && saver.Drawing; i++) saver.Step(screen, 3.3, 500);
                        saver.Step(screen, 3.3, 500);
                    }
                    else
                    {
                        var room = DashRenderer.Room(d);
                        var r = new DashRenderer(screen, d, Math.Min(10, room.Right), Math.Min(20, room.Down));
                        if (ram && WantsTiles(d)) { r.EnableTiles(); r.UseTiles(true); }
                        r.DrawAll();
                        var v = new DashValues();
                        // a moment with things lit, and a sample session for the pit board
                        IdleScreens.IdleValues(v, 2.2, new LastSession { Car = "McLaren 720S GT3 Evo", BestLap = 107.832, Laps = 23, Position = 3 });
                        r.Update(v, 2.2);
                    }
                    img = Theme.ToImage(screen.Bitmap);
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] saver picture " + item.Id + ": " + ex.Message); }
            return img;
        }

        /// <summary>
        /// Pictures kept on disk between starts (PNG, ~5 ms to load vs 50-120 ms to draw), in a folder per plugin build so a
        /// new build draws them afresh; other builds' folders are deleted. Keys include the dash file's time.
        /// </summary>
        private static class Disk
        {
            private static readonly string folder = Folder();

            private static string Folder()
            {
                try
                {
                    string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync", "cache", "pictures");
                    var dll = typeof(DashPictures).Assembly.Location;
                    string build = string.IsNullOrEmpty(dll) ? "dev" : File.GetLastWriteTimeUtc(dll).Ticks.ToString();
                    if (Directory.Exists(root))
                        foreach (var old in Directory.GetDirectories(root))
                            if (Path.GetFileName(old) != build) try { Directory.Delete(old, true); } catch { }
                    string f = Path.Combine(root, build);
                    Directory.CreateDirectory(f);
                    return f;
                }
                catch { return null; }
            }

            /// <summary>Screensavers drawn from the plugin's own code only (a clock changes, pictures and dashes come from files).</summary>
            public static bool Wants(SaverItem item) => item != null && (item.Kind == SaverKind.Logo || item.Kind == SaverKind.Builtin);

            private static string PathOf(string key)
            {
                using (var sha = System.Security.Cryptography.SHA1.Create())
                    return Path.Combine(folder, BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "") + ".png");
            }

            public static bool Has(string key) => folder != null && File.Exists(PathOf(key));

            public static BitmapSource Load(string key)
            {
                if (folder == null) return null;
                try
                {
                    string p = PathOf(key);
                    if (!File.Exists(p)) return null;
                    var img = new BitmapImage();
                    using (var f = File.OpenRead(p))
                    {
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = f;
                        img.EndInit();
                    }
                    img.Freeze();
                    return img;
                }
                catch { return null; }
            }

            public static void Save(string key, BitmapSource img)
            {
                if (folder == null || img == null) return;
                try
                {
                    string p = PathOf(key), tmp = p + ".tmp";
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    using (var f = File.Create(tmp)) enc.Save(f);
                    if (File.Exists(p)) File.Delete(p);
                    File.Move(tmp, p);
                }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] picture cache: " + ex.Message); }
            }
        }
    }

    /// <summary>A dash drawn live into an Image, 10 times a second like the wheel: the wheel's values while it shows this
    /// dash, else the demo lap.</summary>
    internal sealed class LiveDashPreview : IDisposable
    {
        private readonly Image target;
        private readonly Func<UsbController> usb;
        private readonly Func<DashDefinition, bool> ramDrive;
        private PreviewScreen screen;
        private DashRenderer renderer;
        private UsbDemo demo;
        private string key;
        private double last;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public DashDefinition Dash { get; private set; }
        public List<string> Problems { get; private set; } = new List<string>();

        /// <summary>`ramDrive`: the screen's RAM drive is on and this dash uses it, so it is drawn from its pictures (full colour) as on the wheel.</summary>
        public LiveDashPreview(Image target, Func<UsbController> usb, Func<DashDefinition, bool> ramDrive = null) { this.target = target; this.usb = usb; this.ramDrive = ramDrive; }

        public void Show(DashDefinition d, int padLeft, int padTop)
        {
            bool ram = d != null && ramDrive?.Invoke(d) == true && DashPictures.WantsTiles(d);
            string k = d == null ? null : d.Id + "|" + padLeft + "|" + padTop + "|" + d.FilePath + (ram ? "|ram" : "");
            if (k == key) return;
            key = k;
            Dash = d;
            screen?.Dispose(); screen = null; renderer = null; demo = null;
            if (d == null) { target.Source = null; return; }
            screen = new PreviewScreen();
            var room = DashRenderer.Room(d);
            renderer = new DashRenderer(screen, d, Math.Min(padLeft, room.Right), Math.Min(padTop, room.Down));
            // The check is about drawing with rectangles (busy backgrounds, how long the static layer takes): run it before the
            // tiles are on, as for the wheel. With them on it reported false alarms ("busy background" on flat wells, "takes ~66 s").
            Problems = renderer.Check();
            if (ram) { renderer.EnableTiles(); renderer.UseTiles(true); }
            renderer.DrawAll();
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

        private static System.DateTime stamped;

        public static System.Collections.Generic.List<DashDefinition> All(bool force = false)
        {
            lock (gate)
            {
                // the folder is looked at no more than once a second (pages call Find for every tile)
                if (!force && list != null && (System.DateTime.UtcNow - stamped).TotalSeconds < 1) return list;
                stamped = System.DateTime.UtcNow;
                string now = Stamp();
                if (!force && list != null && now == stamp) return list;
                var errors = new System.Collections.Generic.List<string>();
                list = DashLibrary.Load(errors);
                Errors = errors;
                stamp = now;
                return list;
            }
        }

        private static readonly object gate = new object();

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
