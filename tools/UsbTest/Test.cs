using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using User.FXProRpmSync;

static class UsbTestMain
{
    sealed class Counter : IScreenSink
    {
        public PreviewScreen P = new PreviewScreen(); public long Bytes; public int Reports; int pending;
        public System.Collections.Generic.List<string> Log;
        public Action<string> OnCmd;
        public void Cmd(string c) { Log?.Add(c); OnCmd?.Invoke(c); int n = c.Length + 3; if (n > 61) throw new Exception("too long: " + c); if (pending + n > 61) Flush(); pending += n; Bytes += n; P.Cmd(c); }
        public void Flush() { if (pending > 0) { Reports++; pending = 0; } }
    }

    /// <summary>Screen that watches every pixel each command touches during an update.</summary>
    sealed class FlashSink : IScreenSink
    {
        public PreviewScreen P = new PreviewScreen();
        int[] before;
        readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(int C, string Cmd)>> seen = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(int, string)>>();
        public sealed class Result { public int Count, Pixels, Blinks; public Rectangle Box; public System.Collections.Generic.List<string> BlinkPoints = new System.Collections.Generic.List<string>(); public System.Collections.Generic.List<Point> Points = new System.Collections.Generic.List<Point>(); public System.Collections.Generic.List<string> Culprits = new System.Collections.Generic.List<string>(); }
        public System.Collections.Generic.List<string> FrameLog = new System.Collections.Generic.List<string>();
        public void Begin() { before = Pixels(P.Bitmap); seen.Clear(); FrameLog.Clear(); }
        public Result End()
        {
            var after = Pixels(P.Bitmap); var r = new Result(); var box = Rectangle.Empty;
            var culprits = new System.Collections.Generic.HashSet<string>();
            foreach (var kv in seen)
            {
                int p = kv.Key;
                foreach (var (c, cmd) in kv.Value)
                    if (c != before[p] && c != after[p])
                    {
                        r.Pixels++; culprits.Add(cmd); r.Points.Add(new Point(p % 800, p / 800));
                        if (before[p] == after[p]) { r.Blinks++; r.BlinkPoints.Add($"{p % 800},{p / 800} was {before[p] & 0xFFFFFF:X6} became {c & 0xFFFFFF:X6} by: {cmd}"); } // should have stayed as it was
                        var px = new Rectangle(p % 800, p / 800, 1, 1); box = box.IsEmpty ? px : Rectangle.Union(box, px);
                        break;
                    }
            }
            r.Count = r.Pixels; r.Box = box; r.Culprits = culprits.ToList(); r.Points = r.Points;
            return r;
        }
        public void Cmd(string c)
        {
            P.Cmd(c);
            FrameLog.Add(c);
            if (before == null) return;
            Rectangle area;
            if (c.StartsWith("fill ") || c.StartsWith("xstr "))
            {
                var a = c.Substring(5).Split(',');
                area = new Rectangle(int.Parse(a[0]), int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
            }
            else if (c.StartsWith("cls")) area = new Rectangle(0, 0, 800, 480);
            else return;
            area.Intersect(new Rectangle(0, 0, 800, 480));
            if (area.Width <= 0 || area.Height <= 0) return;
            var bd = P.Bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new int[area.Width];
            for (int y = 0; y < area.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, area.Width);
                for (int x = 0; x < area.Width; x++)
                {
                    int p = (area.Y + y) * 800 + area.X + x;
                    if (row[x] == before[p] && !seen.ContainsKey(p)) continue;
                    if (!seen.TryGetValue(p, out var l)) seen[p] = l = new System.Collections.Generic.List<(int, string)>();
                    if (l.Count == 0 || l[l.Count - 1].C != row[x]) l.Add((row[x], c));
                }
            }
            P.Bitmap.UnlockBits(bd);
        }
        public void Flush() { }
    }

    static int[] Pixels(System.Drawing.Bitmap bmp)
    {
        var d = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);
        return px;
    }

    static int Main(string[] args)
    {
        string dir = args[0];
        Directory.CreateDirectory(dir);
        if (args.Length > 1 && args[1] == "atsr")
        {
            var bg = Enumerable.Repeat("#FFFF0000", 40).ToList();                 // opaque red
            var l1 = Enumerable.Repeat("#00000000", 40).ToList();                 // transparent
            var l2 = Enumerable.Repeat("#00000000", 40).ToList(); l2[3] = "#80FFFFFF"; // half white over red
            var l3 = Enumerable.Repeat("#00000000", 40).ToList(); l3[5] = "#FF00FF00"; // opaque green
            var map = AtsrBridge.ParseMap("", out var e0);
            var f = AtsrBridge.Compose(new System.Collections.Generic.List<System.Collections.IList> { bg, l1, l2, l3 }, map, 90);
            Console.WriteLine($"identity: 0={f[0].R},{f[0].G},{f[0].B}  3={f[3].R},{f[3].G},{f[3].B}  5={f[5].R},{f[5].G},{f[5].B}  bright={f[0].Brightness}");
            var m2 = AtsrBridge.ParseMap(string.Join(",", Enumerable.Range(0, 38).Select(i => i == 0 ? 5 : i == 1 ? -1 : i)), out var e1);
            var g = AtsrBridge.Compose(new System.Collections.Generic.List<System.Collections.IList> { bg, l1, l2, l3 }, m2, 45);
            Console.WriteLine($"mapped: 0<-5={g[0].R},{g[0].G},{g[0].B}  1 off={g[1].R},{g[1].G},{g[1].B}  err={e1 ?? "none"} bright={g[0].Brightness}");
            AtsrBridge.ParseMap("1,2,3", out var e2); Console.WriteLine("bad map: " + e2);
            var shortList = new System.Collections.Generic.List<string> { "#FF0000FF" };
            var h = AtsrBridge.Compose(new System.Collections.Generic.List<System.Collections.IList> { shortList }, map, 90);
            Console.WriteLine($"short layer: 0={h[0].R},{h[0].G},{h[0].B} 37={h[37].R},{h[37].G},{h[37].B}");
            return 0;
        }
        if (args.Length > 1 && args[1] == "saver")
        {
            var sc = new Counter(); var sv = new ScreenSaver(); sv.Start(); int frames = 0;
            while (sv.Drawing) { sv.Step(sc, 0.5, 24); frames++; }
            Console.WriteLine($"saver: {sc.Bytes / 1024.0:0.0} KB, {sc.Reports} reports (~{sc.Reports / 500.0:0.0} s at 500/s), {frames} frames of 24 commands");
            sc.P.Bitmap.Save(Path.Combine(dir, "saver_a.png"), ImageFormat.Png);
            long b1 = sc.Bytes; for (int i = 0; i < 400; i++) sv.Step(sc, 0.5 + i * 0.01, 24);
            Console.WriteLine($"dots: {(sc.Bytes - b1) / 4.0:0} B/s");
            sv.Step(sc, 1.3, 24); sc.P.Bitmap.Save(Path.Combine(dir, "saver_b.png"), ImageFormat.Png);
            return 0;
        }
        if (args.Length > 1 && args[1] == "savers")
        {
            // the built-in screensavers: layout check, first-draw size, traffic while animating, pictures at a few moments
            var last = new LastSession { Car = "McLaren 720S GT3 Evo", BestLap = 107.832, Laps = 23, Position = 3 };
            foreach (var item in IdleScreens.All(new UsbSettings()).Where(x => x.Kind == SaverKind.Builtin || x.Kind == SaverKind.Clock))
            {
                var d = IdleScreens.DashFor(item);
                if (d == null)
                {
                    var ac = new Counter(); var an = IdleScreens.Animated(item); an.Start();
                    while (an.Drawing) an.Step(ac, 0, 60);
                    long f0 = ac.Bytes, pk = 0, pv = ac.Bytes;
                    for (int k = 1; k <= 300; k++)
                    {
                        an.Step(ac, k / 10.0, 60);
                        if (k % 10 == 0) { pk = Math.Max(pk, ac.Bytes - pv); pv = ac.Bytes; }
                        if (k == 25 || k == 57 || k == 100) ac.P.Bitmap.Save(Path.Combine(dir, $"saver-{item.Id}-{k}.png"), ImageFormat.Png);
                    }
                    Console.WriteLine($"{item.Id}: first draw {f0 / 1024.0:0.0} KB, then avg {(ac.Bytes - f0) / 30.0:0} B/s, worst {pk} B/s");
                    continue;
                }
                var c = new Counter(); var rn = new DashRenderer(c, d, 10, 20);
                var issues = rn.CheckDetailed(out var cost);
                rn.DrawAll(); c.Flush(); long first = c.Bytes;
                double peak = 0; long prev = c.Bytes;
                for (int k = 1; k <= 300; k++)
                {
                    var v = new DashValues(); IdleScreens.IdleValues(v, k / 10.0, last);
                    rn.Update(v, k / 10.0);
                    if (k % 10 == 0) { peak = Math.Max(peak, c.Bytes - prev); prev = c.Bytes; }
                    if (k == 25 || k == 57 || k == 100) c.P.Bitmap.Save(Path.Combine(dir, $"saver-{item.Id}-{k}.png"), ImageFormat.Png);
                }
                Console.WriteLine($"{item.Id}: first draw {first / 1024.0:0.0} KB ({first / 25000.0:0.0} s), then avg {(c.Bytes - first) / 30.0:0} B/s, worst {peak:0} B/s");
                foreach (var i in issues) Console.WriteLine("   " + i);
            }
            return 0;
        }
        if (args.Length > 1 && args[1] == "features") return FeatureTests.Run(dir);
        if (args.Length > 3 && args[1] == "release")
        {
            // a release made by release.ps1, checked exactly as the updater checks a download: UsbTest OUT release ZIP MANIFEST
            var m = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateManifest>(File.ReadAllText(args[3]));
            var dll = Updater.Verify(m, SemVer.Parse(m.version), File.ReadAllBytes(args[2]), null);
            Console.WriteLine($"release v{m.version}: OK ({dll.Length} byte DLL, firmware min {m.firmware?.min})");
            return 0;
        }
        if (args.Length > 1 && (args[1] == "ui" || args[1] == "uifull")) { UiTest.RunFull(dir, args.Length > 2 ? args[2] : null); return 0; }
        if (args.Length > 2 && args[1] == "traffic")
        {
            // screen traffic and demo CPU for a dash: fxdash-style JSON file, or "mustang"
            var d = args[2] == "mustang" ? BuiltInDashes.MustangGt3() : Newtonsoft.Json.JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(args[2]));
            if (args.Length > 3 && args[3] == "blame")
            {
                long Run(DashDefinition dd)
                {
                    var c = new Counter(); var rn = new DashRenderer(c, dd, 10, 20); rn.DrawAll(); c.Flush();
                    var de = new UsbDemo(dd); long s0 = c.Bytes;
                    for (int k = 1; k <= 600; k++) rn.Update(de.Step(1 / 30.0), k / 30.0);
                    return (c.Bytes - s0) / 20;
                }
                long all = Run(d);
                Console.WriteLine($"all: {all} B/s");
                var rows = new System.Collections.Generic.List<(long, string)>();
                for (int i = 0; i < d.Elements.Count; i++)
                {
                    var dd = d.Clone(); dd.Elements.RemoveAt(i);
                    var e = d.Elements[i];
                    rows.Add((all - Run(dd), $"#{i} {e.Type} {e.Name} ({e.X},{e.Y} {e.W}x{e.H}) bind={e.Bind} color={e.ColorBind} vis={(e.Visible == null ? "" : string.Join(" & ", e.Visible))}"));
                }
                foreach (var r1 in rows.OrderByDescending(x => x.Item1).Take(12)) Console.WriteLine($"  {r1.Item1,7} B/s  {r1.Item2.Replace((char)13, (char)32).Replace((char)10, (char)32)}");
                return 0;
            }
            var sc = new Counter(); var rr = new DashRenderer(sc, d, 10, 20); rr.DrawAll(); sc.Flush();
            rr.DrawCounts = new System.Collections.Generic.Dictionary<DashElement, int>();
            rr.ClearedBy = new System.Collections.Generic.Dictionary<DashElement, int>();
            if (args.Length > 3 && args[3] == "flash")
            {
                // A flash: during one update, a pixel shows a colour that is neither what it was before nor what it is
                // after (text wiped, then drawn again). Replays every command and reports each update that has any.
                int secs = args.Length > 4 ? int.Parse(args[4]) : 300;
                var fsink = new FlashSink();
                var fr2 = new DashRenderer(fsink, d, 10, 20); fr2.DrawAll();
                fr2.Trace = t0 => fsink.FrameLog.Add("// " + t0);
                var fdm = new UsbDemo(d);
                int frames = 0, flashFrames = 0, transitions = 0; long flashPx = 0;
                var byArea = new System.Collections.Generic.Dictionary<string, int>();
                var byElem = new System.Collections.Generic.Dictionary<string, int>();
                var firstFor = new System.Collections.Generic.Dictionary<string, string>();
                bool[] wasVis = null; string lastChange = ""; bool dumped = false, dumpedT = false; long transitionBlinks = 0;
                bool Vis(DashElement e, DashValues vv) => e.Visible == null || e.Visible.All(cnd => vv.Truthy(cnd) ?? (e.PreviewVisible ?? true));
                for (int k = 1; k <= secs * 30; k++)
                {
                    var fv = fdm.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    var vis = d.Elements.Select(e => Vis(e, fv)).ToArray();
                    bool transition = wasVis == null || vis.Where((x, i) => x != wasVis[i]).Any();
                    var prevVis = wasVis;
                    if (transition && wasVis != null) lastChange = $"{k / 30.0:0.00}s: " + string.Join(",", d.Elements.Select((e, i) => (e, i)).Where(t => wasVis[t.i] != vis[t.i]).Select(t => "#" + t.i + (vis[t.i] ? "+" : "-")));
                    wasVis = vis;
                    fsink.Begin();
                    fr2.Update(fv, k / 30.0);
                    var res = fsink.End();
                    frames++;
                    if (res.Count == 0) continue;
                    if (transition)
                    {
                        transitions++; transitionBlinks += res.Blinks;
                        if (Environment.GetEnvironmentVariable("FLASH_TRANSITIONS") == "1")
                        {
                            Console.WriteLine($"  transition t={k / 30.0:0.00}s {res.Pixels}px ({res.Blinks} blinked) box={res.Box} {lastChange}");
                            var dumpAt = Environment.GetEnvironmentVariable("FLASH_DUMP_AT");
                            if (dumpAt != null ? Math.Abs(k / 30.0 - double.Parse(dumpAt, System.Globalization.CultureInfo.InvariantCulture)) < 0.05 && !dumpedT : res.Blinks > 0 && res.Blinks < 100 && k / 30.0 > 20 && !dumpedT)
                            {
                                dumpedT = true;
                                foreach (var c in fsink.FrameLog) Console.WriteLine("      " + c);
                                foreach (var bp in res.BlinkPoints.Take(8)) Console.WriteLine("      blink at " + bp);
                            }
                        }
                        continue; // something appeared or went: its area is redrawn once
                    }
                    if (Environment.GetEnvironmentVariable("FLASH_TIMES") == "1")
                    {
                        var changedVis = string.Join(",", d.Elements.Select((e, i) => (e, i)).Where(t => prevVis != null && prevVis[t.i] != vis[t.i]).Select(t => "#" + t.i));
                        Console.WriteLine($"  t={k / 30.0:0.00}s {res.Pixels}px box={res.Box} prevFrameVisChanges=[{lastChange}]");
                        if (k / 30.0 > 5 && !dumped && (dumped = true)) foreach (var c in fsink.FrameLog) Console.WriteLine("      " + c);
                    }
                    foreach (var pt in res.Points)
                    {
                        // the smallest visible text element there, else the smallest element
                        var hit = d.Elements.Where((e, i) => vis[i] && pt.X - 10 >= e.X && pt.X - 10 < e.X + e.W && pt.Y - 20 >= e.Y && pt.Y - 20 < e.Y + e.H)
                            .OrderBy(e => e.Type == "value" || e.Type == "label" ? 0 : 1).ThenBy(e => e.W * e.H).FirstOrDefault();
                        string nm = hit == null ? "?" : $"#{d.Elements.IndexOf(hit)} {hit.Type} {hit.Name}";
                        byElem[nm] = (byElem.TryGetValue(nm, out var c1) ? c1 : 0) + 1;
                        firstFor[nm] = $"{k / 30.0:0.00}s " + string.Join(" | ", res.Culprits.Where(cc => { var aa = cc.Substring(5).Split(','); var rr0 = new Rectangle(int.Parse(aa[0]), int.Parse(aa[1]), int.Parse(aa[2]), int.Parse(aa[3])); return rr0.Contains(pt); }).Take(6));
                    }
                    flashFrames++; flashPx += res.Pixels;
                    string key = res.Box.ToString();
                    byArea[key] = (byArea.TryGetValue(key, out var c0) ? c0 : 0) + 1;
                    if (flashFrames - transitions <= 4)
                    {
                        Console.WriteLine($"flash at {k / 30.0:0.00}s: {res.Pixels} px in {res.Box} (screen coords)");
                        foreach (var c in res.Culprits.Take(12)) Console.WriteLine("   " + c);
                    }
                }
                Console.WriteLine($"{flashFrames} of {frames} updates flashed; {transitions} pop-up appear/go updates had drawing in between too, {transitionBlinks} px of which blinked (should have stayed)");
                foreach (var kv in byElem.OrderByDescending(x => x.Value).Take(15)) Console.WriteLine($"  {kv.Value,7} px  {kv.Key}" + Environment.NewLine + "            first: " + firstFor[kv.Key]);
                foreach (var kv in byArea.OrderByDescending(x => x.Value).Take(10)) Console.WriteLine($"  {kv.Value,4}x  {kv.Key}");
                return 0;
            }
            if (args.Length > 3 && args[3] == "diverge")
            {
                // first update after which the incremental screen differs from a full redraw; then, for the spot that
                // differs, every command each side drew there and for which element (why)
                var ds = new Counter(); var dr = new DashRenderer(ds, d, 10, 20);
                var hist = new System.Collections.Generic.List<(double T, string Why, string Cmd)>();
                string why = "(DrawAll)"; double tnow = 0;
                dr.Trace = t0 => why = t0.Trim();
                ds.Log = new System.Collections.Generic.List<string>();
                dr.DrawAll();
                foreach (var c in ds.Log) hist.Add((0, "(DrawAll)", c));
                var ddm = new UsbDemo(d);
                for (int k = 1; k <= 3000; k++)
                {
                    var dv = ddm.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    tnow = k / 30.0;
                    var log = new System.Collections.Generic.List<string>();
                    ds.Log = log; why = "(hide/popups)";
                    var cmdWhy = new System.Collections.Generic.List<string>();
                    dr.Trace = t0 => why = t0.Trim();
                    ds.OnCmd = c => cmdWhy.Add(why);
                    dr.Update(dv, tnow);
                    for (int i = 0; i < log.Count; i++) hist.Add((tnow, i < cmdWhy.Count ? cmdWhy[i] : "?", log[i]));
                    var fs = new Counter(); var fwhy = new System.Collections.Generic.List<(string, string)>(); string fw = "(DrawAll)";
                    var frr = new DashRenderer(fs, d, 10, 20); frr.Trace = t0 => fw = t0.Trim();
                    fs.OnCmd = c => fwhy.Add((fw, c));
                    frr.DrawAll(); frr.Update(dv, tnow);
                    int[] A2 = Pixels(ds.P.Bitmap), B2 = Pixels(fs.P.Bitmap); int nd = 0;
                    int x0 = 9999, y0 = 9999, x1 = -1, y1 = -1;
                    for (int i = 0; i < A2.Length; i++) if (A2[i] != B2[i]) { nd++; int x = i % 800, y = i / 800; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                    if (nd > 0)
                    {
                        var box = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
                        Console.WriteLine($"first difference after update at {tnow:0.00}s: {nd} px in {box} (screen)");
                        bool Hits(string c)
                        {
                            if (!(c.StartsWith("fill ") || c.StartsWith("xstr "))) return false;
                            var a = c.Substring(5).Split(',');
                            return new Rectangle(int.Parse(a[0]), int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])).IntersectsWith(box);
                        }
                        Console.WriteLine("  incremental, commands over that spot (latest 25):");
                        foreach (var h in hist.Where(h => Hits(h.Cmd)).Reverse().Take(25).Reverse()) Console.WriteLine($"    {h.T,6:0.00}s  [{h.Why}]  {h.Cmd}");
                        Console.WriteLine("  full redraw, commands over that spot:");
                        foreach (var (w, c) in fwhy.Where(x => Hits(x.Item2))) Console.WriteLine($"    [{w}]  {c}");
                        ds.P.Bitmap.Save(Path.Combine(dir, "incremental.png"), ImageFormat.Png); fs.P.Bitmap.Save(Path.Combine(dir, "full.png"), ImageFormat.Png);
                        return 0;
                    }
                }
                Console.WriteLine("no difference");
                return 0;
            }
            if (args.Length > 4 && args[3] == "trace")
            {
                // what the renderer does for one element over the first updates
                int idx = int.Parse(args[4]);
                var ts = new Counter(); var tr = new DashRenderer(ts, d, 10, 20);
                var log = new System.Collections.Generic.List<string>(); ts.Log = log;
                tr.Trace = t0 => log.Add("// " + t0);
                tr.DrawAll();
                var td = new UsbDemo(d);
                var el = d.Elements[idx];
                string sx = (el.X + 10).ToString() + ",";
                for (int k = 1; k <= 30; k++)
                {
                    var tv = td.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    log.Clear(); log.Add($"-- update {k / 30.0:0.00}s text={DashRenderer.Format(el, tv, out _)}");
                    tr.Update(tv, k / 30.0);
                    bool all = Environment.GetEnvironmentVariable("TRACE_ALL") == "1" && k == 6;
                    foreach (var c in log) if (all || c.StartsWith("--") || c.Contains("#" + idx + " ") || c.Contains(" " + sx) || c.Contains("xstr " + sx) || c.Contains("fill " + sx)) Console.WriteLine(c);
                }
                return 0;
            }
            if (args.Length > 4 && args[3] == "watch")
            {
                var we = d.Elements[int.Parse(args[4])]; var wd = new UsbDemo(d);
                for (int k = 1; k <= 60; k++) { var wv = wd.Step(1 / 30.0); Console.WriteLine($"{k / 30.0:0.00}s raw={wv.Raw(we.Bind)} text={DashRenderer.Format(we, wv, out _)}"); }
                return 0;
            }
            var dm = new UsbDemo(d); long start = sc.Bytes; DashValues lastV = null; int worstB = 0; long secB = sc.Bytes;
            var sw = System.Diagnostics.Stopwatch.StartNew(); double stepMs = 0, updMs = 0;
            var byKey = new System.Collections.Generic.Dictionary<string, int>();
            var lastVal = new System.Collections.Generic.Dictionary<string, string>();
            for (int tick = 1; tick <= 3000; tick++) // 100 s at 30 frames/s, like the USB loop
            {
                double now = tick / 30.0;
                var t0 = sw.Elapsed.TotalMilliseconds; var vv = dm.Step(1 / 30.0); var t1 = sw.Elapsed.TotalMilliseconds; lastV = vv;
                long before = sc.Bytes; if (tick % 3 == 0) rr.Update(vv, now); // the USB loop updates the dash at 10 Hz updMs += sw.Elapsed.TotalMilliseconds - t1; stepMs += t1 - t0;
                if (tick % 30 == 0) { worstB = Math.Max(worstB, (int)(sc.Bytes - secB)); secB = sc.Bytes; }
                foreach (var bnd in d.Bindings)
                {
                    string cur = Convert.ToString(vv.Raw(bnd), System.Globalization.CultureInfo.InvariantCulture);
                    if (!lastVal.TryGetValue(bnd, out var pv) || pv != cur) { byKey[bnd] = (byKey.TryGetValue(bnd, out var c) ? c : 0) + 1; lastVal[bnd] = cur; }
                }
            }
            foreach (var kv in byKey.OrderByDescending(k => k.Value).Take(15)) Console.WriteLine($"  {kv.Value,5} changes  {kv.Key.Replace((char)13, (char)32).Replace((char)10, (char)32).Substring(0, Math.Min(110, kv.Key.Length))}");
            foreach (var kv in rr.DrawCounts.OrderByDescending(k => k.Value).Take(14)) Console.WriteLine($"  {kv.Value / 100.0,6:0.0}/s drawn  #{d.Elements.IndexOf(kv.Key)} {kv.Key.Type} {kv.Key.Name} ({kv.Key.X},{kv.Key.Y} {kv.Key.W}x{kv.Key.H})");
            // incremental drawing must end up where a full redraw of the same values does
            var fresh = new Counter(); var fr = new DashRenderer(fresh, d, 10, 20); fr.DrawAll(); fr.Update(lastV, 3000 / 30.0); rr.Update(lastV, 3000 / 30.0);
            int diff = 0; var bb = System.Drawing.Rectangle.Empty;
            int[] A = Pixels(sc.P.Bitmap), B = Pixels(fresh.P.Bitmap);
            for (int i = 0; i < A.Length; i++)
                if (A[i] != B[i]) { diff++; var px = new System.Drawing.Rectangle(i % 800, i / 800, 1, 1); bb = bb.IsEmpty ? px : System.Drawing.Rectangle.Union(bb, px); }
            Console.WriteLine($"incremental vs full redraw: {diff} pixels differ {(diff > 0 ? bb.ToString() : "")}");
            if (diff > 0) { sc.P.Bitmap.Save(Path.Combine(dir, "incremental.png"), ImageFormat.Png); fresh.P.Bitmap.Save(Path.Combine(dir, "full.png"), ImageFormat.Png); }
            Console.WriteLine($"cleared-then-drawn value redraws: {rr.ClearedDraws / 100.0:0.0}/s");
            foreach (var kv in rr.ClearedBy.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value / 100.0,5:0.0}/s cleared  #{d.Elements.IndexOf(kv.Key)} {kv.Key.Name} ({kv.Key.X},{kv.Key.Y} {kv.Key.W}x{kv.Key.H})");
            Console.WriteLine($"{d.Name}: {(sc.Bytes - start) / 100.0:0} B/s avg, worst second {worstB} B (budget 25000), demo step {stepMs / 3000:0.00} ms, update {updMs / 3000:0.00} ms per frame");
            return 0;
        }
        var def = BuiltInDashes.MustangGt3();
        var scr = new Counter();
        var r = new DashRenderer(scr, def, 10, 20);
        var problems = r.Check();
        Console.WriteLine("checks: " + (problems.Count == 0 ? "OK" : string.Join("\n  ", problems)));
        Console.WriteLine("room: " + DashRenderer.Room(def));
        r.DrawAll(); scr.Flush();
        Console.WriteLine($"static: {scr.Bytes / 1024.0:0.0} KB, {scr.Reports} reports");
        scr.P.Bitmap.Save(Path.Combine(dir, "static.png"), ImageFormat.Png);
        var demo = new UsbDemo(); long b0 = scr.Bytes; int r0 = scr.Reports, worst = 0, sr = scr.Reports; bool popSaved = false, runSaved = false;
        for (int tick = 1; tick <= 2000; tick++)
        {
            double now = tick / 10.0;
            var v = demo.Step(0.1);
            r.Update(v, now);
            if (tick % 10 == 0) { worst = Math.Max(worst, scr.Reports - sr); sr = scr.Reports; }
            if (now > 100 && !runSaved) { scr.P.Bitmap.Save(Path.Combine(dir, "running.png"), ImageFormat.Png); runSaved = true; }
        }
        Console.WriteLine($"updates: {(scr.Bytes - b0) / 200.0:0} B/s, worst second {worst} reports");
        // pixel compare static shapes with tools/dash's static at left=10 top=20
        var eng = new LightEngine();
        foreach (var p in LightPresets.All)
        {
            var frame = eng.Render(p, demo.Step(0.05), null, 12.3, false);
            Console.WriteLine($"{p.Id,-10} " + string.Join(" ", frame.Take(23).Select(f => $"{f.R:X2}{f.G:X2}{f.B:X2}/{f.Brightness}")).Substring(0, 60));
        }
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(def, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(Path.Combine(dir, "mustang.json"), json);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<DashDefinition>(json);
        var scr2 = new Counter(); var r2 = new DashRenderer(scr2, back, 10, 20); r2.DrawAll();
        Console.WriteLine("json round trip: " + (Newtonsoft.Json.JsonConvert.SerializeObject(back) == Newtonsoft.Json.JsonConvert.SerializeObject(def) ? "same" : "DIFFERENT") + $", {json.Length / 1024} KB");
        return FeatureTests.Run(dir);
    }
}
