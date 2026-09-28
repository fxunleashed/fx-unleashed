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
        public void Cmd(string c) { Log?.Add(c); int n = c.Length + 3; if (n > 61) throw new Exception("too long: " + c); if (pending + n > 61) Flush(); pending += n; Bytes += n; P.Cmd(c); }
        public void Flush() { if (pending > 0) { Reports++; pending = 0; } }
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
        if (args.Length > 1 && args[1] == "ui") { UiTest.Run(Path.Combine(dir, "ui.png"), false); UiTest.Run(Path.Combine(dir, "ui-custom.png"), true); return 0; }
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
            if (args.Length > 3 && args[3] == "diverge")
            {
                // first update after which the incremental screen differs from a full redraw, and what drew then
                var ds = new Counter(); var dr = new DashRenderer(ds, d, 10, 20); dr.DrawAll();
                var ddm = new UsbDemo(d);
                for (int k = 1; k <= 3000; k++)
                {
                    var dv = ddm.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    dr.DrawCounts = new System.Collections.Generic.Dictionary<DashElement, int>();
                    var log = new System.Collections.Generic.List<string>(); ds.Log = log;
                    dr.Update(dv, k / 30.0);
                    var fs = new Counter(); var frr = new DashRenderer(fs, d, 10, 20); frr.DrawAll(); frr.Update(dv, k / 30.0);
                    int[] A2 = Pixels(ds.P.Bitmap), B2 = Pixels(fs.P.Bitmap); int nd = 0;
                    for (int i = 0; i < A2.Length; i++) if (A2[i] != B2[i]) nd++;
                    if (nd > 0)
                    {
                        Console.WriteLine($"first difference after update at {k / 30.0:0.00}s: {nd} px; drawn: " + string.Join(", ", dr.DrawCounts.Keys.Select(x => "#" + d.Elements.IndexOf(x) + " " + x.Name)));
                        foreach (var c in log) Console.WriteLine("   " + c);
                        ds.P.Bitmap.Save(Path.Combine(dir, "incremental.png"), ImageFormat.Png); fs.P.Bitmap.Save(Path.Combine(dir, "full.png"), ImageFormat.Png);
                        return 0;
                    }
                }
                Console.WriteLine("no difference");
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
        return 0;
    }
}
