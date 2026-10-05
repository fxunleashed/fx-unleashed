using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// How a dash behaves on the wheel over a demo lap, measured on a simulated screen: USB traffic against the screen's
    /// 25 KB/s, flashes (a pixel that shows something else for a moment within one update: text wiped and drawn again),
    /// and whether the incremental drawing ends where a full redraw of the same values does. `fxdash verify`,
    /// `POST /api/verify`.
    /// </summary>
    public sealed class VerifyResult
    {
        public bool Ok;
        public List<string> Problems = new List<string>();
        public double Seconds;
        public int Updates;
        public const int BudgetBytesPerSecond = 25000;
        public int AvgBytesPerSecond, WorstSecondBytes;
        /// <summary>Updates in which some pixel flashed (pop-ups appearing or going not counted).</summary>
        public int FlashingUpdates;
        /// <summary>Updates in which a pop-up appeared or went (their box and text are drawn one after the other).</summary>
        public int PopupUpdates;
        public List<VerifyFlash> Flashes = new List<VerifyFlash>();
        /// <summary>The elements that send the most, with what they send per second (including what they make redraw).</summary>
        public List<VerifyTraffic> Traffic = new List<VerifyTraffic>();
        /// <summary>Every second of the run: the bytes sent and what sent the most in it.</summary>
        public List<VerifySecond> Timeline = new List<VerifySecond>();
        /// <summary>The busiest second: when, and what sent the most in it (bytes).</summary>
        public double WorstSecondAt;
        public Dictionary<string, long> WorstSecondBy = new Dictionary<string, long>();
        /// <summary>null when every checked update matched a full redraw; else when and where it first didn't.</summary>
        public string RedrawMismatch;
        /// <summary>Dashes with pages: how many flips the run made (every PageEvery seconds) and the biggest one's bytes.</summary>
        public int PageFlips, WorstPageFlipBytes;
        public double PageEvery;
        /// <summary>Elements with a condition that came up or went (overlays, warnings, pages), and what that update sent
        /// (the first 300): where a busy second comes from.</summary>
        public List<VerifyEvent> Events = new List<VerifyEvent>();
        /// <summary>With the overlay sweep (Run's `overlays`): each overlay (a set of conditions some elements share) shown
        /// in turn over the running demo, on every page: what showing and hiding it sends, and flashes while it shows.</summary>
        public List<VerifyOverlay> Overlays;
        /// <summary>With `demo` (as the wheel's demo: the lap with the overlays taking turns): updates where something came or
        /// went and pixels outside what changed flashed (drawn over, then put back), and the slowest such updates.</summary>
        public List<VerifyChange> ChangeFlashes, SlowChanges;
        /// <summary>What drawing the dash from scratch sends (static layer and first values): what loading it takes.</summary>
        public int FullDrawBytes;
        /// <summary>Overlays that take over a second of the screen to come or go, but no more than drawing the dash from
        /// scratch (an overlay over most of the screen can't cost less): worth knowing, not a fault.</summary>
        public List<string> Notes = new List<string>();
    }

    public sealed class VerifyOverlay
    {
        public string Overlay, Conditions; public int Page, Elements, ShowBytes, HideBytes, FlashingUpdates;
        /// <summary>What sent the most when it showed / went (bytes by element).</summary>
        public Dictionary<string, long> ShowBy, HideBy;
        public VerifyFlash Flash;
    }

    public sealed class VerifyEvent { public double Time; public int Bytes; public List<string> Shown = new List<string>(), Hidden = new List<string>(); }

    public sealed class VerifySecond { public double Second; public int Bytes; public Dictionary<string, long> By = new Dictionary<string, long>(); }

    public sealed class VerifyChange { public double Time; public int Bytes, Pixels; public string Box; public List<string> Changed = new List<string>(), At = new List<string>(); }

    public sealed class VerifyFlash { public double Time; public int Pixels; public string Box; public List<string> Elements = new List<string>(); }

    public sealed class VerifyTraffic { public string Element; public int BytesPerSecond; public double DrawsPerSecond; }

    public static class DashVerify
    {
        /// <summary>Runs the demo lap for `seconds` (the dash updated 10 times a second, as on the wheel).</summary>
        public static VerifyResult Run(DashDefinition d, int left = 10, int top = 20, double seconds = 120, int compareEvery = 10, bool tiles = false, bool overlays = false, bool demoShowcase = false)
        {
            var res = new VerifyResult { Seconds = seconds };
            var showcase = demoShowcase ? new OverlayShowcase(d) : null;
            if (demoShowcase) { res.ChangeFlashes = new List<VerifyChange>(); res.SlowChanges = new List<VerifyChange>(); }
            using (var screen = new WatchScreen())
            {
                var r = new DashRenderer(screen, d, left, top);
                string current = "(start)";
                r.Trace = t => { if (t.StartsWith("#")) current = Trim(t, d); };
                screen.Owner = () => current;
                if (tiles) { r.EnableTiles(); r.UseTiles(true); } // as on a wheel with the RAM drive
                r.DrawAll();
                var demo = new UsbDemo(d) { BudgetMs = null };
                using (var counter = new WatchScreen())
                {
                    // the dash from scratch with its first values, on a screen of its own
                    var pr = new DashRenderer(counter, d, left, top);
                    if (tiles) { pr.EnableTiles(); pr.UseTiles(true); }
                    pr.DrawAll(); pr.Update(new UsbDemo(d) { BudgetMs = null }.Step(0.1), 0.1);
                    res.FullDrawBytes = (int)counter.Bytes;
                }
                var bytesBy = new Dictionary<string, long>();
                var drawsBy = new Dictionary<string, int>();
                r.DrawCounts = new Dictionary<DashElement, int>();
                bool[] wasVis = null;
                long start = screen.Bytes, secStart = screen.Bytes;
                var secBy = new Dictionary<string, long>();
                int steps = (int)Math.Round(seconds * 30);
                // a dash with pages flips through them during the run (each page shown a few times), as a driver would
                int pages = d.PageCount, page = 0;
                if (pages > 1) res.PageEvery = Math.Max(2, Math.Min(15, Math.Floor(seconds / (pages * 3))));
                double nextFlip = res.PageEvery;
                for (int k = 1; k <= steps; k++)
                {
                    var v = demo.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    double now = k / 30.0;
                    showcase?.Apply(v, now); // the wheel's demo: the overlays in turn
                    bool flip = pages > 1 && now >= nextFlip;
                    if (flip) { page = (page + 1) % pages; nextFlip += res.PageEvery; res.PageFlips++; }
                    v.Page = page;
                    var vis = d.Elements.Select(e => e.Visible == null || e.Visible.All(c => v.Truthy(c) ?? (e.PreviewVisible ?? true))).ToArray();
                    bool popup = wasVis == null || vis.Where((x, i) => x != wasVis[i]).Any();
                    VerifyEvent ev = null;
                    if (wasVis != null && popup && res.Events.Count < 300)
                    {
                        ev = new VerifyEvent { Time = Math.Round(now, 2) };
                        for (int i = 0; i < vis.Length; i++)
                            if (vis[i] != wasVis[i]) (vis[i] ? ev.Shown : ev.Hidden).Add(Label(d, d.Elements[i]));
                        // a group (an overlay's box and its texts) is named by its first element and a count
                        ev.Shown = Group(ev.Shown); ev.Hidden = Group(ev.Hidden);
                        res.Events.Add(ev);
                    }
                    var wasPrev = wasVis;
                    wasVis = vis;
                    screen.Begin(bytesBy);
                    current = "(areas of elements that hid)";
                    int events = r.PopupEvents, overText = r.PictureOverTextEvents;
                    long before = screen.Bytes;
                    // diagnostics: FXDASH_TRACE_AT=<seconds> prints what the update at that time draws, and why
                    var traceAt = Environment.GetEnvironmentVariable("FXDASH_TRACE_AT");
                    // (or FROM-TO: every update between)
                    var span = traceAt?.Split('-');
                    bool tracing = traceAt != null && (span.Length == 2
                        ? now >= double.Parse(span[0], CultureInfo.InvariantCulture) && now <= double.Parse(span[1], CultureInfo.InvariantCulture)
                        : Math.Abs(now - double.Parse(traceAt, CultureInfo.InvariantCulture)) < 0.05);
                    if (tracing) Console.Error.WriteLine($"== {now:0.0}s");
                    var keepTrace = r.Trace;
                    if (tracing) { r.Trace = t => { keepTrace(t); Console.Error.WriteLine(t); }; screen.Echo = true; }
                    r.Update(v, now);
                    if (tracing) { r.Trace = keepTrace; screen.Echo = false; }
                    popup |= r.PopupEvents != events; // a pop-up element came up or went: drawn on purpose
                    popup |= r.PictureOverTextEvents != overText; // a big shape's picture over text, the text drawn again after it
                    if (flip) res.WorstPageFlipBytes = Math.Max(res.WorstPageFlipBytes, (int)(screen.Bytes - before));
                    if (ev != null) ev.Bytes = (int)(screen.Bytes - before);
                    var flash = screen.End();
                    if (showcase != null && popup && wasPrev != null)
                    {
                        // what changed: the boxes of the elements that came or went; a pixel outside them that flashed is a redraw
                        var changed = Enumerable.Range(0, vis.Length).Where(i => vis[i] != wasPrev[i]).ToList();
                        var boxes = changed.Select(i => new Rectangle(d.Elements[i].X + left, d.Elements[i].Y + top, d.Elements[i].W, d.Elements[i].H)).ToList();
                        var outside = flash.Points.Where(pt => !boxes.Any(b => b.Contains(pt))).ToList();
                        var names = Group(changed.Select(i => (vis[i] ? "+" : "-") + Label(d, d.Elements[i])).ToList());
                        int bytes = (int)(screen.Bytes - before);
                        if (outside.Count > 0 && res.ChangeFlashes.Count < 40)
                        {
                            var box = outside.Aggregate(Rectangle.Empty, (a, pt) => a.IsEmpty ? new Rectangle(pt, new Size(1, 1)) : Rectangle.Union(a, new Rectangle(pt, new Size(1, 1))));
                            res.ChangeFlashes.Add(new VerifyChange { Time = Math.Round(now, 2), Bytes = bytes, Pixels = outside.Count, Box = Rect(box, left, top), Changed = names,
                                At = outside.Select(pt => ElementAt(d, vis, pt.X - left, pt.Y - top)).Where(n => n != null).GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(4).ToList() });
                        }
                        if (bytes > 2500) res.SlowChanges.Add(new VerifyChange { Time = Math.Round(now, 2), Bytes = bytes, Changed = names });
                    }
                    res.Updates++;
                    if (popup) res.PopupUpdates++;
                    else if (flash.Pixels > 0)
                    {
                        res.FlashingUpdates++;
                        if (res.Flashes.Count < 10)
                        {
                            var names = flash.Points.Select(p => ElementAt(d, vis, p.X - left, p.Y - top)).Where(n => n != null).GroupBy(n => n)
                                .OrderByDescending(g => g.Count()).Select(g => g.Key).Take(4).ToList();
                            res.Flashes.Add(new VerifyFlash { Time = Math.Round(now, 2), Pixels = flash.Pixels, Box = Rect(flash.Box, left, top), Elements = names });
                        }
                    }
                    if (k % 30 == 0)
                    {
                        int sec = (int)(screen.Bytes - secStart);
                        res.Timeline.Add(new VerifySecond
                        {
                            Second = Math.Round(now, 1), Bytes = sec,
                            By = bytesBy.Select(kv => (kv.Key, V: kv.Value - (secBy.TryGetValue(kv.Key, out var b1) ? b1 : 0))).Where(x => x.V > 0)
                                .OrderByDescending(x => x.V).Take(4).ToDictionary(x => x.Key, x => x.V)
                        });
                        if (sec > res.WorstSecondBytes)
                        {
                            res.WorstSecondBytes = sec; res.WorstSecondAt = Math.Round(now, 1);
                            res.WorstSecondBy = bytesBy.Select(kv => (kv.Key, V: kv.Value - (secBy.TryGetValue(kv.Key, out var b0) ? b0 : 0)))
                                .Where(x => x.V > 0).OrderByDescending(x => x.V).Take(5).ToDictionary(x => x.Key, x => x.V);
                        }
                        secStart = screen.Bytes; secBy = new Dictionary<string, long>(bytesBy);
                    }
                    if (res.RedrawMismatch == null && !r.PopupShowing && (res.Updates % compareEvery == 0 || k + 3 > steps))
                    {
                        using (var full = new PreviewScreen())
                        {
                            var fr = new DashRenderer(full, d, left, top);
                            if (tiles) { fr.EnableTiles(); fr.UseTiles(true); }
                            fr.DrawAll(); fr.Update(v, now);
                            var diff = Diff(screen.P.Bitmap, full.Bitmap, tiles);
                            if (diff.Width > 0) res.RedrawMismatch = $"at {now:0.0}s, in {Rect(diff, left, top)} (dash coordinates): the screen no longer shows what a full redraw would";
                            var dumpTo = Environment.GetEnvironmentVariable("FXDASH_DUMP");
                            if (diff.Width > 0 && !string.IsNullOrEmpty(dumpTo))
                            {
                                screen.P.Bitmap.Save(System.IO.Path.Combine(dumpTo, "incremental.png"), ImageFormat.Png);
                                full.Bitmap.Save(System.IO.Path.Combine(dumpTo, "full.png"), ImageFormat.Png);
                            }
                        }
                    }
                }
                res.AvgBytesPerSecond = (int)((screen.Bytes - start) / Math.Max(0.1, seconds));
                if (overlays) Sweep(d, r, screen, demo, res, left, top, seconds, tiles);
                foreach (var kv in r.DrawCounts) drawsBy[Label(d, kv.Key)] = kv.Value;
                res.Traffic = bytesBy.Where(kv => kv.Key != "(start)").OrderByDescending(kv => kv.Value).Take(10)
                    .Select(kv => new VerifyTraffic
                    {
                        Element = kv.Key, BytesPerSecond = (int)(kv.Value / Math.Max(0.1, seconds)),
                        DrawsPerSecond = Math.Round((drawsBy.TryGetValue(kv.Key, out var c) ? c : 0) / Math.Max(0.1, seconds), 1),
                    }).ToList();
            }
            if (res.AvgBytesPerSecond > 12000) res.Problems.Add($"average traffic {res.AvgBytesPerSecond} B/s: aim under 12000 (the screen takes 25000; the lights share the time)");
            if (res.WorstSecondBytes > VerifyResult.BudgetBytesPerSecond) res.Problems.Add($"busiest second {res.WorstSecondBytes} B: over the screen's 25000 B/s, updates fall behind then");
            if (res.FlashingUpdates > 0) res.Problems.Add($"{res.FlashingUpdates} of {res.Updates} updates flash (see Flashes)");
            if (res.RedrawMismatch != null) res.Problems.Add("drawing error: " + res.RedrawMismatch);
            if (res.WorstPageFlipBytes > VerifyResult.BudgetBytesPerSecond / 2)
                res.Problems.Add($"a page flip sends {res.WorstPageFlipBytes} B (~{res.WorstPageFlipBytes / (double)VerifyResult.BudgetBytesPerSecond:0.0} s on the screen): keep pages small or plain");
            if (res.SlowChanges != null) res.SlowChanges = res.SlowChanges.OrderByDescending(c => c.Bytes).Take(25).ToList();
            res.Ok = res.Problems.Count == 0;
            return res;
        }

        /// <summary>
        /// The overlay sweep: every set of conditions that elements share (an overlay: a flag oval, a pit screen, a warning)
        /// is made true for 1.5 s and false again, on each page, while the demo keeps the values underneath moving. The
        /// "take turns" conditions (ncalc:!(...), written by tune and dash scripts for values half under an overlay) follow.
        /// What the demo lap never shows (pit limiter, ignition off, flags) gets measured too.
        /// </summary>
        private static void Sweep(DashDefinition d, DashRenderer r, WatchScreen screen, UsbDemo demo, VerifyResult res, int left, int top, double t0, bool tiles)
        {
            res.Overlays = new List<VerifyOverlay>();
            var show = new OverlayShowcase(d);
            var groups = show.Groups;
            List<string> Own(DashElement e) => OverlayShowcase.Own(e);
            double now = t0;
            int pages = d.PageCount;
            foreach (var g in groups.Take(120))
                for (int page = 0; page < pages; page++)
                {
                    var o = new VerifyOverlay
                    {
                        Page = page, Conditions = string.Join(" & ", g.Select(c => c.Length > 70 ? c.Substring(0, 67) + "..." : c)),
                        Elements = d.Elements.Count(e => Own(e).SequenceEqual(g)),
                    };
                    var first = d.Elements.First(e => Own(e).SequenceEqual(g));
                    o.Overlay = Label(d, first) + (o.Elements > 1 ? $" (+{o.Elements - 1})" : "");
                    // its conditions true, their "take turns" negations false, what waits after an event it starts not yet
                    var force = show.Force(g);
                    // inside another overlay (a pit stop state in the pit limiter screen, a staged item of a start-up sweep):
                    // that one comes first, as it would, and what this one adds is measured
                    var parent = show.Parent(g);
                    var parentForce = parent == null ? null : show.Force(parent);
                    bool[] wasVis = null;
                    int lead = parentForce == null ? 0 : 6;
                    for (int k = -lead; k < 23; k++)   // (the parent alone,) 15 updates on, 8 off (10 a second)
                    {
                        bool on = k < 15;
                        DashValues v = null;
                        for (int j = 0; j < 3; j++) v = demo.Step(1 / 30.0);
                        now += 0.1;
                        v.Page = page;
                        if (k < 0) { foreach (var kv in parentForce) v.Set(kv.Key, kv.Value); r.Update(v, now); wasVis = null; continue; }
                        if (on) foreach (var kv in force) v.Set(kv.Key, kv.Value);
                        var vis = d.Elements.Select(e => e.Visible == null || e.Visible.All(c => v.Truthy(c) ?? (e.PreviewVisible ?? true))).ToArray();
                        bool changed = wasVis == null || vis.Where((x, i) => x != wasVis[i]).Any();
                        wasVis = vis;
                        var by = k == 0 || k == 15 ? new Dictionary<string, long>() : null;
                        // diagnostics: FXDASH_TRACE=<text in an overlay's name> prints what its first update draws, and why
                        var traceFor = Environment.GetEnvironmentVariable("FXDASH_TRACE");
                        var keepTrace = r.Trace;
                        if (!string.IsNullOrEmpty(traceFor) && o.Overlay.Contains(traceFor) && (k == 0 || k == 15 || Environment.GetEnvironmentVariable("FXDASH_TRACE_ALL") == "1"))
                        {
                            int kk = k, pp = page;
                            Console.Error.WriteLine($"--- page {pp + 1} update {kk} ({(on ? "on" : "off")})");
                            r.Trace = t => { keepTrace?.Invoke(t); Console.Error.WriteLine(t); };
                            foreach (var kv in force) Console.Error.WriteLine($"forced {kv.Key} = {kv.Value}");
                            screen.Echo = Environment.GetEnvironmentVariable("FXDASH_TRACE_CMDS") == "1";
                        }
                        screen.Begin(by);
                        int events = r.PopupEvents, overText = r.PictureOverTextEvents;
                        long before = screen.Bytes;
                        r.Update(v, now);
                        changed |= r.PopupEvents != events || r.PictureOverTextEvents != overText;
                        r.Trace = keepTrace;
                        screen.Echo = false;
                        int bytes = (int)(screen.Bytes - before);
                        var flash = screen.End();
                        Dictionary<string, long> Top(Dictionary<string, long> x) => x.OrderByDescending(kv => kv.Value).Take(4).ToDictionary(kv => kv.Key, kv => kv.Value);
                        if (k == 0) { o.ShowBytes = bytes; o.ShowBy = Top(by); }
                        if (k == 15) { o.HideBytes = bytes; o.HideBy = Top(by); }
                        if (!changed && flash.Pixels > 0)
                        {
                            o.FlashingUpdates++;
                            if (o.Flash == null)
                                o.Flash = new VerifyFlash
                                {
                                    Time = Math.Round(now, 2), Pixels = flash.Pixels, Box = Rect(flash.Box, left, top),
                                    Elements = flash.Points.Select(p => ElementAt(d, vis, p.X - left, p.Y - top)).Where(n => n != null).GroupBy(n => n)
                                                   .OrderByDescending(x => x.Count()).Select(x => x.Key).Take(4).ToList(),
                                };
                        }
                        // the screen ends each phase as a full redraw of the same moment would draw it
                        if ((k == 14 || k == 22) && res.RedrawMismatch == null && !r.PopupShowing)
                            using (var full = new PreviewScreen())
                            {
                                var fr = new DashRenderer(full, d, left, top);
                                if (tiles) { fr.EnableTiles(); fr.UseTiles(true); }
                                fr.DrawAll(); fr.Update(v, now);
                                var diff = Diff(screen.P.Bitmap, full.Bitmap, tiles);
                                var dumpTo = Environment.GetEnvironmentVariable("FXDASH_DUMP");
                                if (diff.Width > 0 && !string.IsNullOrEmpty(dumpTo))
                                {
                                    screen.P.Bitmap.Save(System.IO.Path.Combine(dumpTo, "incremental.png"), ImageFormat.Png);
                                    full.Bitmap.Save(System.IO.Path.Combine(dumpTo, "full.png"), ImageFormat.Png);
                                }
                                if (diff.Width > 0) res.RedrawMismatch = $"overlay sweep, {o.Overlay} {(on ? "shown" : "gone")} on page {page + 1}: in {Rect(diff, left, top)} the screen no longer shows what a full redraw would";
                            }
                    }
                    res.Overlays.Add(o);
                }
            foreach (var o in res.Overlays)
            {
                int most = Math.Max(o.ShowBytes, o.HideBytes);
                if (most > VerifyResult.BudgetBytesPerSecond && most <= Math.Max(VerifyResult.BudgetBytesPerSecond, res.FullDrawBytes * 2))
                    res.Notes.Add($"overlay {o.Overlay} (page {o.Page + 1}) sends {most} B when it {(o.ShowBytes >= o.HideBytes ? "shows" : "goes")} (~{most / (double)VerifyResult.BudgetBytesPerSecond:0.0} s; drawing the whole dash takes {res.FullDrawBytes} B)");
                else if (most > VerifyResult.BudgetBytesPerSecond)
                    res.Problems.Add($"overlay {o.Overlay} (page {o.Page + 1}) sends {Math.Max(o.ShowBytes, o.HideBytes)} B when it {(o.ShowBytes >= o.HideBytes ? "shows" : "goes")}: over a second of the screen");
                if (o.FlashingUpdates > 0)
                    res.Problems.Add($"overlay {o.Overlay} (page {o.Page + 1}): {o.FlashingUpdates} updates flash while it shows (at {o.Flash?.Box}: {string.Join(", ", o.Flash?.Elements ?? new List<string>())})");
            }
        }

        private static string Label(DashDefinition d, DashElement e) => $"#{d.Elements.IndexOf(e)} {e.Type} {e.Name}";

        private static List<string> Group(List<string> names) =>
            names.Count <= 3 ? names : new List<string> { names[0], names[1], $"... {names.Count - 2} more" };

        // the trace names elements "#i kind name"; the draw counts are by element
        private static string Trim(string traced, DashDefinition d)
        {
            var parts = traced.Split(' ');
            if (parts.Length < 2 || !parts[0].StartsWith("#") || !int.TryParse(parts[0].Substring(1), out int i) || i < 0 || i >= d.Elements.Count) return traced;
            return Label(d, d.Elements[i]);
        }

        /// <summary>The smallest shown text element at a point, else the smallest shown element.</summary>
        private static string ElementAt(DashDefinition d, bool[] vis, int x, int y)
        {
            var hit = d.Elements.Where((e, i) => vis[i] && x >= e.X && x < e.X + e.W && y >= e.Y && y < e.Y + e.H)
                .OrderBy(e => e.Type == "value" || e.Type == "label" ? 0 : 1).ThenBy(e => e.W * e.H).FirstOrDefault();
            return hit == null ? null : Label(d, hit);
        }

        private static string Rect(Rectangle r, int left, int top) => $"{r.X - left},{r.Y - top} {r.Width}x{r.Height}";

        /// <summary>Per colour channel: what JPEG at the tiles' quality can move a pixel by (edges included).</summary>
        public const int JpegTolerance = 56;

        /// <summary>Per colour channel, without tiles: one RGB565 step. A smoothed shape's edge (drawn by the screen itself,
        /// ScreenShapes) wiped and drawn again rounds a level differently from one drawn once.</summary>
        public const int SmoothTolerance = 8;

        private static int[] Pixels(Bitmap bmp)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
            bmp.UnlockBits(bd);
            return px;
        }

        /// <summary>
        /// Where two screens differ. With tiles the same thing can be drawn two right ways, from a JPEG on the screen or
        /// with exact fills, a few levels apart: differences within JPEG's error (ScreenTiles.Quality) don't count, as
        /// drawing errors (a stale or missing element) are whole colours apart. Without, one RGB565 step doesn't either.
        /// </summary>
        private static Rectangle Diff(Bitmap a, Bitmap b, bool tiles = false)
        {
            int[] pa = Pixels(a), pb = Pixels(b);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            bool Apart(int p, int q)
            {
                if (p == q) return false;
                int dr = Math.Abs(((p >> 16) & 255) - ((q >> 16) & 255)), dg = Math.Abs(((p >> 8) & 255) - ((q >> 8) & 255)), db = Math.Abs((p & 255) - (q & 255));
                return Math.Max(dr, Math.Max(dg, db)) > (tiles ? JpegTolerance : SmoothTolerance);
            }
            for (int i = 0; i < pa.Length; i++)
                if (Apart(pa[i], pb[i])) { int x = i % DashRenderer.Width, y = i / DashRenderer.Width; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        /// <summary>A simulated screen that counts bytes and watches every pixel each command touches in an update.</summary>
        private sealed class WatchScreen : IScreenSink, IDisposable
        {
            public readonly PreviewScreen P = new PreviewScreen();
            public long Bytes;
            public Func<string> Owner;
            private Dictionary<string, long> bytesBy;
            private int[] before;
            private readonly Dictionary<int, List<int>> seen = new Dictionary<int, List<int>>();

            public sealed class Flash { public int Pixels; public Rectangle Box; public List<Point> Points = new List<Point>(); }

            public void Begin(Dictionary<string, long> by) { bytesBy = by; before = Pixels(P.Bitmap); seen.Clear(); }

            public Flash End()
            {
                var after = Pixels(P.Bitmap);
                var f = new Flash();
                foreach (var kv in seen)
                {
                    int p = kv.Key;
                    if (kv.Value.Any(c => c != before[p] && c != after[p]))
                    {
                        f.Pixels++;
                        var pt = new Point(p % DashRenderer.Width, p / DashRenderer.Width);
                        if (f.Points.Count < 4000) f.Points.Add(pt);
                        f.Box = f.Box.IsEmpty ? new Rectangle(pt, new Size(1, 1)) : Rectangle.Union(f.Box, new Rectangle(pt, new Size(1, 1)));
                    }
                }
                before = null;
                return f;
            }

            public bool Echo;

            public void Cmd(string c)
            {
                if (Echo) Console.Error.WriteLine("    > " + c);
                P.Cmd(c);
                int n = c.Length + 3;
                Bytes += n;
                if (bytesBy != null && Owner != null) { var o = Owner(); bytesBy[o] = (bytesBy.TryGetValue(o, out var b) ? b : 0) + n; }
                if (before == null) return;
                Rectangle area;
                if (c.StartsWith("fill ") || c.StartsWith("xstr "))
                {
                    var a = c.Substring(5).Split(',');
                    area = new Rectangle(int.Parse(a[0], CultureInfo.InvariantCulture), int.Parse(a[1], CultureInfo.InvariantCulture),
                                         int.Parse(a[2], CultureInfo.InvariantCulture), int.Parse(a[3], CultureInfo.InvariantCulture));
                }
                else if (c.StartsWith("cls")) area = new Rectangle(0, 0, DashRenderer.Width, DashRenderer.Height);
                else return;
                area.Intersect(new Rectangle(0, 0, DashRenderer.Width, DashRenderer.Height));
                if (area.Width <= 0 || area.Height <= 0) return;
                var bd = P.Bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var row = new int[area.Width];
                for (int y = 0; y < area.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, area.Width);
                    for (int x = 0; x < area.Width; x++)
                    {
                        int p = (area.Y + y) * DashRenderer.Width + area.X + x;
                        if (row[x] == before[p] && !seen.ContainsKey(p)) continue;
                        if (!seen.TryGetValue(p, out var l)) seen[p] = l = new List<int>();
                        if (l.Count == 0 || l[l.Count - 1] != row[x]) l.Add(row[x]);
                    }
                }
                P.Bitmap.UnlockBits(bd);
            }

            public void Flush() { }
            public void Dispose() => P.Dispose();
        }
    }
}
