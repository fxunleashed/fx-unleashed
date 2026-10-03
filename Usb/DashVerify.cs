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
    }

    public sealed class VerifySecond { public double Second; public int Bytes; public Dictionary<string, long> By = new Dictionary<string, long>(); }

    public sealed class VerifyFlash { public double Time; public int Pixels; public string Box; public List<string> Elements = new List<string>(); }

    public sealed class VerifyTraffic { public string Element; public int BytesPerSecond; public double DrawsPerSecond; }

    public static class DashVerify
    {
        /// <summary>Runs the demo lap for `seconds` (the dash updated 10 times a second, as on the wheel).</summary>
        public static VerifyResult Run(DashDefinition d, int left = 10, int top = 20, double seconds = 120, int compareEvery = 10, bool tiles = false)
        {
            var res = new VerifyResult { Seconds = seconds };
            using (var screen = new WatchScreen())
            {
                var r = new DashRenderer(screen, d, left, top);
                string current = "(start)";
                r.Trace = t => { if (t.StartsWith("#")) current = Trim(t, d); };
                screen.Owner = () => current;
                if (tiles) { r.EnableTiles(); r.UseTiles(true); } // as on a wheel with the RAM drive
                r.DrawAll();
                var demo = new UsbDemo(d) { BudgetMs = null };
                var bytesBy = new Dictionary<string, long>();
                var drawsBy = new Dictionary<string, int>();
                r.DrawCounts = new Dictionary<DashElement, int>();
                bool[] wasVis = null;
                long start = screen.Bytes, secStart = screen.Bytes;
                var secBy = new Dictionary<string, long>();
                int steps = (int)Math.Round(seconds * 30);
                for (int k = 1; k <= steps; k++)
                {
                    var v = demo.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    double now = k / 30.0;
                    var vis = d.Elements.Select(e => e.Visible == null || e.Visible.All(c => v.Truthy(c) ?? (e.PreviewVisible ?? true))).ToArray();
                    bool popup = wasVis == null || vis.Where((x, i) => x != wasVis[i]).Any();
                    wasVis = vis;
                    screen.Begin(bytesBy);
                    current = "(areas of elements that hid)";
                    int events = r.PopupEvents;
                    r.Update(v, now);
                    popup |= r.PopupEvents != events; // a pop-up element came up or went: drawn on purpose
                    var flash = screen.End();
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
                            var diff = Diff(screen.P.Bitmap, full.Bitmap);
                            if (diff.Width > 0) res.RedrawMismatch = $"at {now:0.0}s, in {Rect(diff, left, top)} (dash coordinates): the screen no longer shows what a full redraw would";
                        }
                    }
                }
                res.AvgBytesPerSecond = (int)((screen.Bytes - start) / Math.Max(0.1, seconds));
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
            res.Ok = res.Problems.Count == 0;
            return res;
        }

        private static string Label(DashDefinition d, DashElement e) => $"#{d.Elements.IndexOf(e)} {e.Type} {e.Name}";

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

        private static int[] Pixels(Bitmap bmp)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
            bmp.UnlockBits(bd);
            return px;
        }

        private static Rectangle Diff(Bitmap a, Bitmap b)
        {
            int[] pa = Pixels(a), pb = Pixels(b);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int i = 0; i < pa.Length; i++)
                if (pa[i] != pb[i]) { int x = i % DashRenderer.Width, y = i / DashRenderer.Width; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
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

            public void Cmd(string c)
            {
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
