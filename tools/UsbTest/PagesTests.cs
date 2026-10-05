using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// Dash pages (DashDefinition.Pages, "page:N" conditions), the dim element, the importer's pages / overlay screens /
/// blinking / layer opacity, the overlay sweep of verify, the shape pictures of the RAM drive, and the built-in Mustang
/// (tools/dashes/make_mustang.py) passing every gate.
/// </summary>
static class PagesTests
{
    public static void Run()
    {
        Pages();
        Format();
        Dim();
        Import();
        Mustang();
    }

    static DashDefinition TwoPages() => new DashDefinition
    {
        Id = "pages-test", Name = "Pages", Pages = new List<string> { "One", "Two" },
        Elements =
        {
            new DashElement { Type = "rect", Name = "always", X = 0, Y = 0, W = 100, H = 20, Color = "#428AED" },
            new DashElement { Type = "value", Name = "on one", Bind = "speed", X = 100, Y = 100, W = 200, H = 50, Font = 100, Samples = new[] { "388" }, Visible = new List<string> { "page:0" } },
            new DashElement { Type = "value", Name = "on two", Bind = "rpm", X = 100, Y = 100, W = 200, H = 50, Font = 100, Samples = new[] { "8888" }, Visible = new List<string> { "page:1" } },
            new DashElement { Type = "rect", Name = "two's line", X = 100, Y = 160, W = 200, H = 2, Color = "#FFFFFF", Visible = new List<string> { "page:1" } },
        },
    };

    static void Pages()
    {
        var v = new DashValues { Page = 1 };
        Check("pages: page:1 is true on page 1 only", v.Truthy("page:1") == true && v.Truthy("page:0") == false && v.Number("page") == 1);
        Check("pages: page:N is a known key (no unknown-key warning)", DashValues.KnownKey("page:3") && DashValues.KnownKey("page") && !DashValues.KnownKey("page:x"));
        Check("pages: steps wrap round", DashPages.Step(1, 1, 2) == 0 && DashPages.Step(0, -1, 3) == 2 && DashPages.Step(0, 5, 1) == 0);

        var d = TwoPages();
        Check("pages: count from Pages, or the highest page:N used", d.PageCount == 2 && new DashDefinition { Elements = { new DashElement { Type = "rect", Visible = new List<string> { "page:4" } } } }.PageCount == 5);
        Check("pages: names (and a default for a page without one)", d.PageName(1) == "Two" && d.PageName(3) == "Page 4");
        Check("pages: elements on different pages never overlap in check", DashTools.Check(d).Issues.All(i => !i.Message.Contains("overlaps")), string.Join("; ", DashTools.Check(d).Issues.Select(i => i.ToString())));
        Check("pages: two elements on the same page in one spot still overlap",
              DashTools.Check(new DashDefinition { Elements = { d.Elements[1], JsonConvert.DeserializeObject<DashElement>(JsonConvert.SerializeObject(d.Elements[1])) } }).Issues.Any(i => i.Message.Contains("overlaps")));

        // a flip on the screen draws only the page's area, and ends where a full redraw of that page does
        using (var screen = new PreviewScreen())
        {
            var r = new DashRenderer(screen, d, 0, 0);
            r.DrawAll();
            var demo = new UsbDemo(d) { BudgetMs = null };
            var v0 = demo.Step(0.1); v0.Page = 0; r.Update(v0, 0.1);
            var v1 = demo.Step(0.1); v1.Page = 1; r.Update(v1, 0.2);
            using (var full = new PreviewScreen())
            {
                var fr = new DashRenderer(full, d, 0, 0); fr.DrawAll(); fr.Update(v1, 0.2);
                Check("pages: after a flip the screen is what a full redraw of the page draws", Same(screen, full));
            }
        }
        var vr = DashVerify.Run(d, 0, 0, 30);
        Check("pages: verify flips the pages and measures the flips", vr.PageFlips > 0 && vr.WorstPageFlipBytes > 0 && vr.Ok, $"{vr.PageFlips} flips, worst {vr.WorstPageFlipBytes} B, {string.Join("; ", vr.Problems)}");
        // the plugin's state: the page a dash was left on
        var s = new UsbSettings();
        s.DashPages["pages-test"] = 1;
        Check("pages: a dash comes back on the page it was left on", UsbController.SavedPage(s, d) == 1);
        s.DashPages["pages-test"] = 7;
        Check("pages: a saved page past the dash's pages starts it on the first", UsbController.SavedPage(s, d) == 0);
        Check("pages: wheel buttons can flip pages", WheelButtons.Actions.Any(a => a.Id == "pagenext") && WheelButtons.Actions.Any(a => a.Id == "pageprev"));
    }

    static bool Same(PreviewScreen a, PreviewScreen b)
    {
        using (var ma = new MemoryStream(a.Png())) using (var mb = new MemoryStream(b.Png())) return ma.ToArray().SequenceEqual(mb.ToArray());
    }

    static void Format()
    {
        var plain = new DashDefinition { Elements = { new DashElement { Type = "rect", Visible = new List<string> { "pitLimiter" } } } };
        Check("format: a dash without pages needs format 2 (older plugins still load it)", plain.RequiredFormat == 2);
        Check("format: a dash with pages needs format 3", TwoPages().RequiredFormat == 3 && DashDefinition.CurrentFormat >= 3);
        var dir = Path.Combine(Path.GetTempPath(), "fxdash-format-" + Guid.NewGuid().ToString("N"));
        var keep = DashLibrary.Root;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "PluginsData", "Common", "FXProRpmSync", "Dashes"));
            DashLibrary.Root = dir;
            var d = TwoPages(); d.FormatVersion = 1;
            var file = DashTools.Save(d);
            Check("format: saved with the format it needs", JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(file)).FormatVersion == 3);
            var p = plain.Clone(); p.Id = "plain-test"; DashTools.Save(p);
            Check("format: a dash without pages is saved as format 2", JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(Path.Combine(DashLibrary.Folder, "plain-test.json"))).FormatVersion == 2);
            var errors = new List<string>();
            Check("format: a format 3 dash loads", DashLibrary.Load(errors).Any(x => x.Id == "pages-test" && x.Pages?.Count == 2), string.Join("; ", errors));
        }
        finally { DashLibrary.Root = keep; try { Directory.Delete(dir, true); } catch { } }
    }

    static void Dim()
    {
        var d = new DashDefinition
        {
            Elements =
            {
                new DashElement { Type = "value", Bind = "speed", X = 10, Y = 10, W = 200, H = 50, Font = 100, Samples = new[] { "388" } },
                new DashElement { Type = "dim", Name = "headlights", Opacity = 50, Visible = new List<string> { "pitLimiter" } },
            },
        };
        using (var screen = new PreviewScreen())
        {
            var r = new DashRenderer(screen, d, 0, 0);
            r.DrawAll();
            var v = new DashValues(); v.Set("speed", 100.0); v.Set("pitLimiter", true);
            r.Update(v, 0.1);
            Check("dim: shown, the screen is darker by its Opacity", r.DimPercent == 50);
            var v2 = new DashValues(); v2.Set("speed", 100.0); v2.Set("pitLimiter", false);
            r.Update(v2, 0.2);
            Check("dim: hidden, full brightness again", r.DimPercent == 0);
        }
        Check("dim: a known type, no box needed (no check issues)", DashTools.Check(d).Issues.Count == 0, string.Join("; ", DashTools.Check(d).Issues.Select(i => i.ToString())));
    }

    static void Import()
    {
        var simhub = Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub";
        var path = SimHubImport.Installed(Path.Combine(simhub, "DashTemplates")).FirstOrDefault(x => x.Name == "LMGT3 Ford Mustang GT3").Path;
        if (path == null) { Console.WriteLine("skip  import: SimHub dash \"LMGT3 Ford Mustang GT3\" isn't installed"); return; }
        var keep = DashLibrary.Root;
        DashLibrary.Root = simhub; // SimHub's ImageLibrary (library: pictures)
        DashDefinition d; ImportReport report;
        try { d = SimHubImport.Import(path, new ImportOptions { FitWidth = 790, FitHeight = 460 }, out report); }
        finally { DashLibrary.Root = keep; }
        Check("import: the widget's two flipped screens become two pages", d.Pages?.Count == 2 && d.Elements.Any(e => DashPages.PageOf(e) == 0) && d.Elements.Any(e => DashPages.PageOf(e) == 1));
        Check("import: a widget on other screen commands (a dismissable warning) isn't paged", report.Notes.Any(n => n.Contains("Low NRG") && n.Contains("other screen commands")));
        Check("import: overlay screens come in, shown while their trigger holds", d.Elements.Any(e => e.Visible != null && e.Visible.Contains("ncalc:![EngineIgnitionOn]")) && d.Elements.Any(e => e.Visible != null && e.Visible.Contains("ncalc:changed(1800, [EngineIgnitionOn])")));
        Check("import: blinking items blink (a blink() condition)", d.Elements.Any(e => e.Name == "ENG off" && e.Visible.Any(c => c.Contains("blink("))));
        Check("import: a layer's opacity reaches its children (dim indicator arrows)", d.Elements.Any(e => e.Name == "Links" && e.Type == "image" && e.Opacity == 20 && (e.Visible == null || e.Visible.Count == 0)));
        Check("import: the headlights' see-through black layer becomes a dim", d.Elements.Any(e => e.Type == "dim" && e.Name == "HEADLIGHT" && e.Opacity == 50));
        Check("import: the result needs format 3", d.RequiredFormat == 3);
    }

    /// <summary>Formulas broken in the SimHub dash a dash was converted from, kept as they are (SimHub fails on them the
    /// same way, so the dash behaves as the original): Redadeg's Mustang has one more ")" in its pit stop "Tyres" colour.</summary>
    static readonly HashSet<string> Inherited = new HashSet<string>
    {
        "ncalc:if([TyresWearAvg]<95,'-1', if([TyresWearAvg])>96,'1', 0))",
    };

    static void Mustang()
    {
        var m = BuiltInDashes.MustangGt3();
        Check("mustang: built in from the generated file, with its two pages", m.Elements.Count > 200 && m.PageCount == 2 && m.BuiltIn && m.Id == BuiltInDashes.MustangId && m.FormatVersion == 3, $"{m.Elements.Count} elements");
        Check("mustang: a fresh copy each time (callers may change it)", !ReferenceEquals(m, BuiltInDashes.MustangGt3()) && !ReferenceEquals(m.Elements, BuiltInDashes.MustangGt3().Elements));
        var c = DashTools.Check(m, 10, 20);
        Check("mustang: check has no errors and no warnings", c.Issues.Count == 0, string.Join("; ", c.Issues.Take(4).Select(i => i.ToString())));
        var copy = m.Clone();
        Check("mustang: fit-bands changes nothing", DashTools.FitTextBands(copy).Count == 0);
        var plain = DashVerify.Run(m, 10, 20, 120);
        Check("mustang: verify ok (no flashes, under budget)", plain.Ok && plain.FlashingUpdates == 0 && plain.RedrawMismatch == null, $"{plain.AvgBytesPerSecond} B/s, worst {plain.WorstSecondBytes}; {string.Join("; ", plain.Problems)}");
        var sweep = DashVerify.Run(m, 10, 20, 60, tiles: true, overlays: true);
        Check("mustang: every overlay shown and gone on every page, with the RAM drive: ok", sweep.Ok && sweep.Overlays.Count > 50 && sweep.Overlays.All(o => o.FlashingUpdates == 0 && Math.Max(o.ShowBytes, o.HideBytes) <= VerifyResult.BudgetBytesPerSecond),
              $"{sweep.Overlays.Count} overlays, worst {sweep.Overlays.Max(o => Math.Max(o.ShowBytes, o.HideBytes))} B; {string.Join("; ", sweep.Problems.Take(3))}");
        // every SimHub formula parses with SimHub's own NCalc (the demo shows a value whose formula fails as empty, so a
        // formula the importer wrote wrong would only show up live: a time format's backslashes did)
        foreach (var dash in new[] { m }.Concat(BundledDashes.All()))
        {
            var bad = dash.Bindings.Concat(dash.Elements.Where(e => e.Visible != null).SelectMany(e => e.Visible)).Distinct()
                .Where(b => b.StartsWith("ncalc:") && !Inherited.Contains(b)).Where(b => new NCalc.Expression(b.Substring(6)).HasErrors()).ToList();
            Check($"formulas: every NCalc formula of {dash.Name} parses", bad.Count == 0, string.Join(" | ", bad.Take(3)));
        }
        // the demo on the wheel takes the overlays in turn: the pit limiter screen and the flags come up, nothing flashes
        var show = new OverlayShowcase(m);
        int turns = show.TurnCount;
        bool pit = false, flag = false;
        for (double t = 0; t < (show.ShowSeconds + show.GapSeconds) * turns + 3; t += 0.5)
        {
            var v = new DashValues();
            show.Apply(v, t);
            pit |= v.Truthy("ncalc:[PitLimiterOn]") == true;
            flag |= v.Truthy("ncalc:[Flag_Blue]") == true;
        }
        Check("showcase: the demo brings up the pit limiter screen and the blue flag in its turns", turns > 15 && pit && flag, $"{turns} turns");
        // the pit limiter oval changes colour with the speed (blue / green / red): with the RAM drive each colour is a
        // picture of its own, so a change is a couple of commands, not the oval drawn again with rectangles
        {
            var sc = new UsbTestMain.Counter();
            var r = new DashRenderer(sc, m, 10, 20);
            r.EnableTiles(); r.UseTiles(true); r.DrawAll();
            var showPit = new OverlayShowcase(m);
            var pitGroup = showPit.Groups.First(g => g.Count == 1 && g[0] == "ncalc:[PitLimiterOn]");
            var colour = m.Elements.First(e => e.Name == "BLU+GRN+RED_Elipse").ColorBind;
            var demo = new UsbDemo(m) { BudgetMs = null };
            DashValues V(double speedState) { var v = demo.Step(0.1); foreach (var kv in showPit.Force(pitGroup)) v.Set(kv.Key, kv.Value); v.Set(colour, speedState); return v; }
            double t = 0.1;
            r.Update(V(-1), t += 0.1); r.Update(V(-1), t += 0.1);
            long worst = 0; bool same = true;
            foreach (var state in new[] { 0.0, 1.0, -1.0, 1.0 })
            {
                long b0 = sc.Bytes;
                var v = V(state);
                r.Update(v, t += 0.1);
                worst = Math.Max(worst, sc.Bytes - b0);
                var full = new UsbTestMain.Counter();
                var fr = new DashRenderer(full, m, 10, 20); fr.EnableTiles(); fr.UseTiles(true); fr.DrawAll(); fr.Update(v, t);
                same &= Same(sc.P, full.P);
            }
            Check("mustang: the pit oval changes colour from its RAM pictures (a few hundred bytes, as a full redraw shows it)", worst < 1500 && same, $"worst change {worst} B");
        }
        using (var p = new PreviewScreen())
        {
            var r = new DashRenderer(p, m, 10, 20);
            var pics = r.PictureList().ToList();
            Check("mustang: the ovals and the Ford script are pictures on the RAM drive", pics.Count > 20 && r.Tiles.Bytes + r.Tiles.FileCount * ScreenRam.FileOverhead < ScreenRam.Budget, $"{pics.Count} pictures, {r.Tiles.Bytes / 1024} KB");
        }
    }
}
