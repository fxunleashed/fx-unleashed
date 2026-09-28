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
        public void Cmd(string c) { int n = c.Length + 3; if (n > 61) throw new Exception("too long: " + c); if (pending + n > 61) Flush(); pending += n; Bytes += n; P.Cmd(c); }
        public void Flush() { if (pending > 0) { Reports++; pending = 0; } }
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
