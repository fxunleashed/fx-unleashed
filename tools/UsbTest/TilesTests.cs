using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using User.FXProRpmSync;

/// <summary>
/// UsbTest OUT tiles [dash name filter]: every dash drawn with fills and with tiles (the screen's RAM drive, FXProDashes
/// docs/screen-images.md), through the same demo session: the two must look alike; prints what each costs.
/// </summary>
static class TilesTests
{
    sealed class Sink : IScreenSink
    {
        public PreviewScreen P = new PreviewScreen();
        public long Bytes; public int Commands, Ramv; int pending; public int Reports;
        public void Cmd(string c)
        {
            int n = c.Length + 3;
            if (n > 61) throw new Exception("command too long: " + c);
            if (pending + n > 61) Flush();
            pending += n; Bytes += n; Commands++;
            if (c.StartsWith("sets \"ramv: ")) Ramv++;
            P.Cmd(c);
        }
        public void Flush() { if (pending > 0) { Reports++; pending = 0; } }
    }

    static int[] Px(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    /// <summary>Mean channel difference (0-255) and the share of pixels off by more than 40 in any channel.</summary>
    static (double Mean, double Off) Diff(Bitmap a, Bitmap b)
    {
        var pa = Px(a); var pb = Px(b); long sum = 0; int off = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            int dr = Math.Abs(((pa[i] >> 16) & 255) - ((pb[i] >> 16) & 255)), dg = Math.Abs(((pa[i] >> 8) & 255) - ((pb[i] >> 8) & 255)), db = Math.Abs((pa[i] & 255) - (pb[i] & 255));
            sum += dr + dg + db;
            if (dr > 40 || dg > 40 || db > 40) off++;
        }
        return (sum / (3.0 * pa.Length), off * 100.0 / pa.Length);
    }

    public static int Run(string dir, string[] args)
    {
        var errors = new List<string>();
        // the installed dashes too (read only): SimHub's folder, as the plugin sees it
        DashLibrary.Root = Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub\";
        var dashes = DashLibrary.Load(errors).ToList();
        if (!dashes.Any(d => d.Id == BuiltInDashes.MustangId)) dashes.Insert(0, BuiltInDashes.MustangGt3());
        string filter = args.Length > 2 && args[2] != "-" ? args[2] : null;
        if (args.Length > 3) DashRenderer.TileRepaintFills = int.Parse(args[3]); // repaint threshold to try
        if (filter != null) dashes = dashes.Where(d => (d.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        int bad = 0;
        Console.WriteLine("dash | files KB (grid + bands) | draw: fills KB -> tiles KB (ramv) | demo 20 s: fills KB -> tiles KB | vs fills: start / end (mean, % px off) | drift vs a fresh tile drawing");
        foreach (var d in dashes)
        {
            try
            {
                var a = new Sink(); var ra = new DashRenderer(a, d, 10, 20); ra.DrawAll(); a.Flush();
                var b = new Sink(); var rb = new DashRenderer(b, d, 10, 20); var t = rb.EnableTiles(); rb.UseTiles(true); rb.DrawAll(); b.Flush();
                long drawA = a.Bytes, drawB = b.Bytes; int ramvDraw = b.Ramv;
                var start = Diff(a.P.Bitmap, b.P.Bitmap);
                string safe = new string((d.Name ?? d.Id ?? "dash").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                a.P.Bitmap.Save(Path.Combine(dir, $"tiles_{safe}_fills.png"), ImageFormat.Png);
                b.P.Bitmap.Save(Path.Combine(dir, $"tiles_{safe}_tiles.png"), ImageFormat.Png);

                // the same session for both (one demo, the same values to each), dash updated at 10 Hz like the USB loop
                var demo = new UsbDemo(d);
                DashValues last = null; double lastT = 0;
                // with a name filter: what draws how often (fills vs tiles) and the first repaints with tiles
                var trace = new List<string>();
                if (filter != null)
                {
                    ra.DrawCounts = new Dictionary<DashElement, int>(); rb.DrawCounts = new Dictionary<DashElement, int>();
                    rb.Trace = m => { if (trace.Count < 60 && (m.StartsWith("repaint") || m.StartsWith("  "))) trace.Add(m); };
                }
                for (int k = 1; k <= 600; k++)
                {
                    var v = demo.Step(1 / 30.0);
                    if (k % 3 != 0) continue;
                    ra.Update(v, k / 30.0); a.Flush();
                    rb.Update(v, k / 30.0); b.Flush();
                    last = v; lastT = k / 30.0;
                }
                // what the session left on the screen vs a fresh full drawing (tiles) of the same moment: anything left
                // behind or missed by incremental drawing shows here
                var c = new Sink(); var rc = new DashRenderer(c, d, 10, 20); rc.EnableTiles(); rc.UseTiles(true); rc.DrawAll(); rc.Update(last, lastT);
                var drift = Diff(b.P.Bitmap, c.P.Bitmap);
                c.P.Bitmap.Save(Path.Combine(dir, $"tiles_{safe}_tiles_fresh.png"), ImageFormat.Png);
                var end = Diff(a.P.Bitmap, b.P.Bitmap);
                a.P.Bitmap.Save(Path.Combine(dir, $"tiles_{safe}_fills_end.png"), ImageFormat.Png);
                b.P.Bitmap.Save(Path.Combine(dir, $"tiles_{safe}_tiles_end.png"), ImageFormat.Png);

                // vs the fills: information only (tiles are anti-aliased JPEG in full colour, the fills colour-reduced
                // and hard-edged). Correct = the session's screen matches a fresh drawing, and traffic didn't grow much.
                if (filter != null)
                {
                    Console.WriteLine("  most drawn (fills -> tiles):");
                    foreach (var kv in rb.DrawCounts.OrderByDescending(x => x.Value).Take(12))
                        Console.WriteLine($"    {kv.Key.Type} {kv.Key.Name}: {(ra.DrawCounts.TryGetValue(kv.Key, out var fa) ? fa : 0)} -> {kv.Value}");
                    Console.WriteLine("  first repaints with tiles:");
                    foreach (var l in trace.Take(40)) Console.WriteLine("    " + l);
                }
                bool ok = drift.Off < 0.5 && (b.Bytes - drawB) <= 1.5 * (a.Bytes - drawA) + 4096;
                if (!ok) bad++;
                Console.WriteLine($"{(ok ? "  " : "!!")} {d.Name} | {t.Bytes / 1024.0:0.0} KB in {t.FileCount} files ({t.GridTiles.Count(x => x.Name != null)} grid + {t.Bands.Count} bands, {t.GridTiles.Count(x => x.Name == null)} one-colour) " +
                                  $"| {drawA / 1024.0:0.0} -> {drawB / 1024.0:0.0} KB ({ramvDraw} ramv) | {(a.Bytes - drawA) / 1024.0:0.0} -> {(b.Bytes - drawB) / 1024.0:0.0} KB " +
                                  $"| {start.Mean:0.0}/{start.Off:0.00}% -> {end.Mean:0.0}/{end.Off:0.00}% | drift {drift.Off:0.00}%");
            }
            catch (Exception ex) { bad++; Console.WriteLine($"!! {d.Name}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Console.WriteLine(bad == 0 ? "tiles: all dashes draw correctly, traffic in line" : $"tiles: {bad} dash(es) to look at");
        return bad == 0 ? 0 : 1;
    }
}
