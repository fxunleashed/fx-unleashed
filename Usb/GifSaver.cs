using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A screensaver that plays an animated GIF (<see cref="AnimatedPicture"/>) from the screen's RAM drive: the first
    /// picture goes up whole, then each frame is just the few `ramv` commands that draw what changed over the one before
    /// (a few dozen bytes), at the GIF's own timing. With the RAM drive off (UseTiles false) it shows the first frame as a
    /// picture, drawn with rectangles. Files go up through the controller's PrepareSaverTiles like any tiled saver's.
    ///
    /// The screen takes time to draw a picture, and nothing tells the PC how much: its command buffer is 4 KB, drops what
    /// doesn't fit, and only shows a frame once it runs dry (FXProDashes docs/screen-speed.md). So the player keeps a model
    /// of the screen's cost (<see cref="MicrosecondsPerPixel"/>) and sends the next step only when the last one should be
    /// drawn; when that puts it behind the GIF's own clock it jumps to the step that should be showing now, if that one is a
    /// whole picture, so the animation keeps its speed with fewer steps instead of running slow.
    /// </summary>
    internal sealed class GifSaver : ITiledSaver
    {
        // ---- tuning, this session (the designer API /api/wheel/gif) ----

        /// <summary>A step drawing more pixels than this is wrapped in ref_stop ... ref_star (as around a file upload), so it
        /// appears whole. Off by default: it was added without trying it on the wheel, and the animation on the user's wheel ran
        /// at about half speed (2026-10-07).</summary>
        public static int HoldArea = int.MaxValue;

        /// <summary>What the screen takes to draw one pixel of a picture (JPEG decode and copy), in microseconds. The first value
        /// is what the first animation on the wheel implied: 460 x 460 steps every 200 ms ran at half speed, 1.9 us a pixel.
        /// Lower it while the animation keeps its speed (/api/wheel/gif?cost=...), raise it if the screen still falls behind.</summary>
        public static double MicrosecondsPerPixel = 1.9;

        /// <summary>The cost of a draw whatever its size (commands, the screen's refresh), in ms.</summary>
        public static double FixedMs = 5;

        /// <summary>Steps drawn, steps skipped to catch up, since the plugin started (the API shows them).</summary>
        public static long StepsDrawn, StepsSkipped;

        /// <summary>A step is sent when the screen should be done with the last one within this many seconds.</summary>
        private const double QueueSlack = 0.008;

        private sealed class Built
        {
            public ScreenTiles Tiles = new ScreenTiles();
            /// <summary>By AnimatedPicture.Frames: what to draw to show that frame (0: the whole picture).</summary>
            public List<List<ScreenTile>> Steps = new List<List<ScreenTile>>();
            public List<ScreenTile> Wrap;
            /// <summary>By step: it draws the whole picture, so it can be drawn without the steps before it.</summary>
            public List<bool> Full = new List<bool>();
            /// <summary>The names this one added to <see cref="ScreenTiles.Registry"/> (others already had them): what Forget takes out.</summary>
            public List<string> Owned = new List<string>();
        }

        private static readonly Dictionary<string, Tuple<DateTime, AnimatedPicture>> files = new Dictionary<string, Tuple<DateTime, AnimatedPicture>>();
        private static readonly Dictionary<AnimatedPicture, Built> built = new Dictionary<AnimatedPicture, Built>();

        /// <summary>The animation a screensaver item's file holds (kept until the file changes), or null.</summary>
        public static AnimatedPicture Load(SaverItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.File)) return null;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(item.File);
                lock (files)
                {
                    if (files.TryGetValue(item.File, out var hit) && hit.Item1 == stamp) return hit.Item2;
                    var pic = JsonConvert.DeserializeObject<AnimatedPicture>(File.ReadAllText(item.File));
                    if (pic == null || pic.Frames == null || pic.Frames.Count == 0) return null;
                    files[item.File] = Tuple.Create(stamp, pic);
                    return pic;
                }
            }
            catch { return null; }
        }

        public static GifSaver Create(SaverItem item, int padLeft = 10, int padTop = 20)
        {
            var pic = Load(item);
            return pic == null ? null : new GifSaver(pic, padLeft, padTop);
        }

        private readonly AnimatedPicture pic;
        private readonly int padLeft, padTop;
        /// <summary>Where the animation's corner is on the screen: centred in the usable 790 x 460 inside the padding.</summary>
        private readonly int ox, oy;
        private readonly Queue<string> pending = new Queue<string>();
        private int current;
        private double nextAt = -1, busyUntil;
        private bool stillDone;
        // timing report (the log shows how the player did, the one thing the PC can see of the screen's speed)
        private double reportAt = -1;
        private int reports, drawnSince, skippedSince;

        public GifSaver(AnimatedPicture pic, int padLeft = 10, int padTop = 20)
        {
            this.pic = pic;
            this.padLeft = Math.Max(0, Math.Min(padLeft, DashRenderer.Width - 790));
            this.padTop = Math.Max(0, Math.Min(padTop, DashRenderer.Height - 460));
            ox = this.padLeft + (790 - pic.Width) / 2;
            oy = this.padTop + (460 - pic.Height) / 2;
            loopSeconds = Math.Max(0.03, pic.Frames.Sum(f => Math.Max(0.03, f.Ms / 1000.0)));
        }

        /// <summary>With the screen's RAM drive: play from its files; else the first frame as a picture.</summary>
        public bool UseTiles { get; set; }

        public ScreenTiles Tiles => BuiltFor(pic).Tiles;

        /// <summary>The frame shown now (index into the animation's frames).</summary>
        internal int Current => current;

        private static Built BuiltFor(AnimatedPicture p)
        {
            lock (built)
            {
                if (built.TryGetValue(p, out var hit)) return hit;
                var b = new Built();
                ScreenTile Make(AnimTile t)
                {
                    var tile = new ScreenTile { R = new Rectangle(t.X, t.Y, t.W, t.H) };
                    if (t.Jpeg == null) { tile.Colour = t.Colour; return tile; }
                    tile.Jpeg = Convert.FromBase64String(t.Jpeg);
                    tile.Name = ScreenTiles.NameFor(tile.Jpeg);
                    if (ScreenTiles.Registry.TryAdd(tile.Name, tile.Jpeg)) b.Owned.Add(tile.Name); // previews and the mirror draw from it
                    b.Tiles.FrameTiles.Add(tile);
                    return tile;
                }
                foreach (var f in p.Frames) b.Steps.Add(f.Tiles.Select(Make).ToList());
                if (p.Wrap != null) b.Wrap = p.Wrap.Tiles.Select(Make).ToList();
                foreach (var step in b.Steps)
                    b.Full.Add(step.Count > 0 && step.Any(t => t.R.X <= 0 && t.R.Y <= 0 && t.R.Right >= p.Width && t.R.Bottom >= p.Height));
                return built[p] = b;
            }
        }

        /// <summary>
        /// Lets go of what was built for an animation that is no longer played (the import window's trial pictures: there are
        /// many, and the cache and the registry would keep every one for as long as SimHub runs). Pictures another saver
        /// registered first stay.
        /// </summary>
        internal static void Forget(AnimatedPicture p)
        {
            lock (built)
            {
                if (!built.TryGetValue(p, out var b)) return;
                built.Remove(p);
                // a picture another animation still plays (same bytes, so the same name) stays in the registry, and that
                // animation lets it go when its turn comes
                var users = new Dictionary<string, Built>();
                foreach (var o in built.Values) foreach (var t in o.Tiles.FrameTiles) users[t.Name] = o;
                foreach (var name in b.Owned)
                {
                    if (users.TryGetValue(name, out var heir)) heir.Owned.Add(name);
                    else ScreenTiles.Registry.TryRemove(name, out _);
                }
            }
        }

        /// <summary>A preview, not the wheel: nothing is counted or logged.</summary>
        internal bool Quiet { get; set; }

        public void Start()
        {
            pending.Clear();
            pending.Enqueue("page 0");
            pending.Enqueue("vis 255,0");
            pending.Enqueue("cls 0");
            if (UseTiles) foreach (var c in Commands(BuiltFor(pic).Steps[0])) pending.Enqueue(c);
            current = 0; nextAt = -1; busyUntil = 0; stillDone = false;
            reportAt = -1; drawnSince = skippedSince = 0;
        }

        public bool Drawing => pending.Count > 0 || (!UseTiles && !stillDone);

        public void Step(IScreenSink screen, double now, int max = 60)
        {
            for (int n = 0; n < max && pending.Count > 0; n++) screen.Cmd(pending.Dequeue());
            if (pending.Count == 0)
            {
                if (UseTiles) Play(screen, now);
                else DrawStill(screen);
            }
            screen.Flush();
        }

        /// <summary>
        /// Shows what's due by `now`. One step at a time while the screen is still drawing the last one (the model says when
        /// it's done); behind the GIF's clock, a whole-picture step jumps straight to the step that should show now; patches
        /// (cheap) catch up one after the other.
        /// </summary>
        private void Play(IScreenSink screen, double now)
        {
            var b = BuiltFor(pic);
            int count = b.Steps.Count;
            if (count < 2) return; // a still
            // the loop starts when the first picture is sent (the screen is busy drawing it for what it costs)
            if (nextAt < 0) { nextAt = now + Dwell(current); busyUntil = now + Cost(b.Steps[0]) / 1000.0; reportAt = now + 15; return; }
            for (int draws = 0; draws < 4 && now >= nextAt && busyUntil <= now + QueueSlack; draws++)
            {
                // the step that should be showing now, by the GIF's clock
                int target = current, behind = 0;
                double end = nextAt;
                if (now - end >= loopSeconds) // whole loops late: no need to walk through them
                {
                    double whole = Math.Floor((now - end) / loopSeconds);
                    end += whole * loopSeconds; behind += (int)whole * count;
                }
                while (end <= now) { target = (target + 1) % count; end += Dwell(target); behind++; }
                List<ScreenTile> tiles;
                if (behind >= 2 && b.Full[target])
                {
                    tiles = b.Steps[target]; // whole picture: nothing before it is needed
                    if (!Quiet) { skippedSince += behind - 1; StepsSkipped += behind - 1; }
                    current = target; nextAt = end;
                }
                else
                {
                    int next = (current + 1) % count;
                    tiles = next == 0 ? b.Wrap : b.Steps[next];
                    current = next; nextAt += Dwell(current);
                }
                busyUntil = Math.Max(busyUntil, now) + Draw(screen, tiles) / 1000.0;
                if (!Quiet) { drawnSince++; StepsDrawn++; }
            }
            // way behind on cheap patches (more than a loop): start the clock again from here rather than racing through them
            if (now - nextAt > Math.Max(1.0, loopSeconds)) nextAt = now + Dwell(current);
            if (!Quiet) Report(now);
        }

        /// <summary>How long one trip through the animation takes (its steps' times).</summary>
        private double loopSeconds;

        private double Dwell(int frame) => Math.Max(0.03, pic.Frames[frame].Ms / 1000.0);

        /// <summary>Sends the tiles; returns what the screen should take to draw them, in ms (the model).</summary>
        private double Draw(IScreenSink screen, List<ScreenTile> tiles)
        {
            if (tiles == null || tiles.Count == 0) return 0;
            bool hold = tiles.Sum(t => (long)t.R.Width * t.R.Height) > HoldArea;
            if (hold) screen.Cmd("ref_stop");
            foreach (var c in Commands(tiles)) screen.Cmd(c);
            if (hold) screen.Cmd("ref_star");
            return Cost(tiles);
        }

        /// <summary>The same for an animation's stored pictures (the import window's estimate).</summary>
        internal static double CostMs(IEnumerable<AnimTile> tiles)
        {
            long pixels = 0; int n = 0;
            foreach (var t in tiles) { n++; if (t.Jpeg != null) pixels += (long)t.W * t.H; }
            return n == 0 ? 0 : FixedMs + pixels * MicrosecondsPerPixel / 1000.0;
        }

        /// <summary>What the screen is assumed to take to draw these tiles, in ms (plain fills are cheap: not counted).</summary>
        private static double Cost(List<ScreenTile> tiles)
        {
            if (tiles == null || tiles.Count == 0) return 0;
            long pixels = tiles.Where(t => t.Name != null).Sum(t => (long)t.R.Width * t.R.Height);
            return FixedMs + pixels * MicrosecondsPerPixel / 1000.0;
        }

        /// <summary>A line in the log every 15 s for the first minute of playing, then every 5 minutes: how many steps went to
        /// the screen against the animation's own pace, and how many were skipped to keep its speed.</summary>
        private void Report(double now)
        {
            if (reportAt < 0 || now < reportAt) return;
            double span = reports < 4 ? 15 : 300;
            double loop = pic.Frames.Sum(f => f.Ms) / 1000.0;
            try
            {
                SimHub.Logging.Current.Info($"[FXProRpmSync] GIF screensaver: {drawnSince} steps drawn and {skippedSince} skipped in {span:0} s " +
                    $"(the animation has {pic.Frames.Count / loop:0.0} steps/s of its own; screen model {MicrosecondsPerPixel:0.0} us/px + {FixedMs:0} ms, hold {(HoldArea == int.MaxValue ? "off" : HoldArea + " px")})");
            }
            catch { }
            reports++; drawnSince = skippedSince = 0;
            reportAt = now + (reports < 4 ? 15 : 300);
        }

        private IEnumerable<string> Commands(List<ScreenTile> tiles)
        {
            foreach (var t in tiles)
                yield return t.Name != null
                    ? ScreenTiles.Ramv(t, ox, oy)
                    : string.Format(CultureInfo.InvariantCulture, "fill {0},{1},{2},{3},{4}", t.R.X + ox, t.R.Y + oy, t.R.Width, t.R.Height, t.Colour);
        }

        /// <summary>No RAM drive: the first frame as the picture dash (rectangles, reduced to draw in time).</summary>
        private void DrawStill(IScreenSink screen)
        {
            if (stillDone) return;
            stillDone = true;
            var d = pic.Still;
            if (d == null) return;
            var room = DashRenderer.Room(d);
            new DashRenderer(screen, d, Math.Min(padLeft, room.Right), Math.Min(padTop, room.Down)).DrawAll();
        }
    }
}
