using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>Checks for the later NEXT.md workstreams (G, H, C, D, I, ...), run by `UsbTest.exe OUT features`.</summary>
static class WorkstreamTests
{
    public static void Run()
    {
        WheelValues();
        Mirror();
        LedDevice();
        BaseSettingsTests();
        LibraryTests();
        ShareTests();
        ServerOrigins();
        TrafficPanelTests();
        BundledDashTests();
        ScriptCheckTests();
        RealLibrary();
    }

    static void LibraryTests()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fxu-libtest-" + System.Guid.NewGuid().ToString("N"));
        string lib = System.IO.Path.Combine(root, "library"), simhub = System.IO.Path.Combine(root, "simhub");
        var oldRoot = DashLibrary.Root;
        try
        {
            DashLibrary.Root = simhub;
            var dash = BuiltInDashes.MustangGt3();
            var meta = new LibraryItem { Id = "test-dash", Name = "Test dash", Author = "tester", License = "CC-BY-4.0", Games = { "LMU" }, Version = "1.0.0" };
            var dir = LibraryInstaller.Package(dash, meta, lib);
            Check("C: package writes dash.json, meta.json, preview.png", new[] { "dash.json", "meta.json", "preview.png" }.All(f => System.IO.File.Exists(System.IO.Path.Combine(dir, f))));
            var zipFile = LibraryInstaller.ZipPackage(dir);
            string[] zipped; using (var za = System.IO.Compression.ZipFile.OpenRead(zipFile)) zipped = za.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
            Check("C: the package zip holds <id>/dash.json, meta.json, preview.png (what the submission form takes)",
                  zipped.SequenceEqual(new[] { "test-dash/dash.json", "test-dash/meta.json", "test-dash/preview.png" }) && zipFile.EndsWith("test-dash.fxdash.zip"), string.Join(", ", zipped));
            var conv = dash.Clone(); conv.Id = "conv";
            var convDir = LibraryInstaller.Package(conv, new LibraryItem { Id = "conv-dash", Name = "Conv", Author = "t", License = "CC0-1.0", Source = "https://example.com/a", Permission = "https://example.com/ok" }, lib);
            var convMeta = System.IO.File.ReadAllText(System.IO.Path.Combine(convDir, "meta.json"));
            Check("C: converted work's Source and Permission reach meta.json", convMeta.Contains("\"Source\": \"https://example.com/a\"") && convMeta.Contains("\"Permission\": \"https://example.com/ok\""));
            var m = Newtonsoft.Json.JsonConvert.DeserializeObject<LibraryItem>(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "meta.json")));
            Check("C: meta has sha256 and measured budget", m.Sha256?.Length == 64 && m.BytesPerSecond > 0 && m.BytesStatic > 0, $"{m.BytesPerSecond} B/s, {m.BytesStatic} B");
            var index = new LibraryIndex { Items = { m } };
            System.IO.File.WriteAllText(System.IO.Path.Combine(lib, "index.json"), Newtonsoft.Json.JsonConvert.SerializeObject(index));
            var client = new LibraryClient(lib);
            var got = client.GetIndex(allowCache: false);
            Check("C: index from a local folder", got.Items.Count == 1 && got.Items[0].Id == "test-dash");
            var d = client.Download(got.Items[0]);
            var s = new UsbSettings();
            var rec = LibraryInstaller.Install(s, got.Items[0], d);
            Check("C: installed into the dashes folder as lib-<id>", System.IO.File.Exists(rec.File) && DashLibrary.Load(null).Any(x => x.Id == "lib-test-dash"));
            Check("C: no update for the same version", !LibraryInstaller.UpdateAvailable(s, got.Items[0]));
            got.Items[0].Version = "1.1.0";
            Check("C: newer version = update available", LibraryInstaller.UpdateAvailable(s, got.Items[0]));
            // tampered download
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "dash.json"), " ");
            bool refused = false; try { client.Download(got.Items[0]); } catch { refused = true; }
            Check("C: a download that doesn't match its sha256 is refused", refused);
            // scripts refused
            var js = dash.Clone(); js.Elements[0].Bind = "js:return eval(1)";
            Check("C: js: bindings refused", LibraryClient.Check(js, 100).Any(x => x.Contains("js:")));
            var sf = dash.Clone(); sf.ScriptsFolder = "x";
            Check("C: scripts folder refused", LibraryClient.Check(sf, 100).Count > 0);
            var nf = dash.Clone(); nf.FormatVersion = DashDefinition.CurrentFormat + 1;
            Check("C: newer dash format refused", LibraryClient.Check(nf, 100).Count > 0);
            Check("C: ids are safe file names", LibraryClient.ValidId("gt3-minimal") && !LibraryClient.ValidId("../x") && !LibraryClient.ValidId("A") && !LibraryClient.ValidId("a b"));
            bool pkgJs = false; try { LibraryInstaller.Package(js, new LibraryItem { Id = "js-dash", Name = "x", Author = "x", License = "MIT" }, lib); } catch { pkgJs = true; }
            Check("C: packaging a js: dash is refused", pkgJs);
            // savers
            var saverMeta = new LibraryItem { Id = "test-saver", Kind = "saver", Name = "Test saver", Author = "t", License = "CC0-1.0", Version = "1.0.0", Sha256 = m.Sha256 };
            var srec = LibraryInstaller.Install(s, saverMeta, dash.Clone());
            Check("C: saver installed as a picture-kind saver", System.IO.File.Exists(srec.File) && s.Savers.Any(x => x.Id == "lib-test-saver" && x.Kind == SaverKind.Image));
            s.SaverId = "lib-test-saver";
            LibraryInstaller.Remove(s, "saver", "test-saver");
            Check("C: removing the shown saver falls back to the logo", !System.IO.File.Exists(srec.File) && s.SaverId == SaverItem.LogoId && s.Savers.All(x => x.Id != "lib-test-saver"));
            LibraryInstaller.Remove(s, "dash", "test-dash");
            Check("C: removed", !System.IO.File.Exists(rec.File) && s.LibraryInstalled.Count == 0);
            // dashes that are already here but didn't come through the library (hand-copied, imported, the early fx- names) count as installed
            var s2 = new UsbSettings();
            var halo = new LibraryItem { Id = "halo", Name = "HALO", Version = "1.0.0" };
            var slip = new LibraryItem { Id = "slipstream", Name = "SLIPSTREAM", Version = "1.0.0" };
            var other = new LibraryItem { Id = "not-bundled-dash", Name = "Not bundled", Version = "1.0.0" }; // (APEX, HALO and the others come with the plugin now)
            var byHand = dash.Clone(); byHand.Id = "fx-halo"; DashTools.Save(byHand);
            var plain = dash.Clone(); plain.Id = "slipstream"; DashTools.Save(plain);
            var noId = System.IO.Path.Combine(DashLibrary.Folder, "from-a-friend.json");
            System.IO.File.WriteAllText(noId, "{\"Name\":\"x\",\"Elements\":[{\"Type\":\"rect\"}]}");
            var local = DashLibrary.LocalIds();
            Check("C: local dash ids are read from the files", local.Contains("fx-halo") && local.Contains("slipstream") && local.Contains("file:from-a-friend"), string.Join(", ", local));
            var found = LibraryInstaller.Installed(s2, halo, local);
            Check("C: a hand-copied fx-<id> dash shows as installed", found != null && found.Found && found.DashId == "fx-halo");
            Check("C: a dash with the item's own id shows as installed", LibraryInstaller.Installed(s2, slip, local)?.DashId == "slipstream");
            Check("C: a library item with no local dash is not installed", LibraryInstaller.Installed(s2, other, local) == null);
            Check("C: a found dash never offers an update", !LibraryInstaller.UpdateAvailable(s2, halo, local));
            Check("C: a found dash is never written to the settings", s2.LibraryInstalled == null || s2.LibraryInstalled.Count == 0);
            // lib-<id> wins over the others when several exist, and a recorded install wins over all of them
            var lib2 = dash.Clone(); lib2.Id = "lib-halo"; DashTools.Save(lib2);
            Check("C: lib-<id> is preferred", LibraryInstaller.Installed(s2, halo, DashLibrary.LocalIds())?.DashId == "lib-halo");
            var rec2 = LibraryInstaller.Install(s2, halo, dash.Clone());
            var again = LibraryInstaller.Installed(s2, halo, DashLibrary.LocalIds());
            Check("C: a recorded install is used as is", again != null && !again.Found && again.DashId == "lib-halo" && again.Version == "1.0.0");
            halo.Version = "1.2.0";
            Check("C: a recorded install still offers its update", LibraryInstaller.UpdateAvailable(s2, halo, DashLibrary.LocalIds()));
        }
        finally
        {
            DashLibrary.Root = oldRoot;
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>Sharing a dash as a file: export, import, never overwriting, the library's rules, links.</summary>
    static void ShareTests()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fxu-sharetest-" + System.Guid.NewGuid().ToString("N"));
        string simhub = System.IO.Path.Combine(root, "simhub"), inbox = System.IO.Path.Combine(root, "inbox");
        var oldRoot = DashLibrary.Root;
        try
        {
            DashLibrary.Root = simhub;
            System.IO.Directory.CreateDirectory(inbox);
            var mine = BuiltInDashes.MustangGt3().Clone();
            mine.Id = "my-dash"; mine.Name = "My dash"; mine.Author = "me"; mine.Source = null;

            var path = DashShare.Export(mine, System.IO.Path.Combine(inbox, "my-dash.json"));
            Check("share: export names the file <name>.fxdash.json", path.EndsWith(".fxdash.json") && System.IO.File.Exists(path) && System.IO.Path.GetFileName(path) == "my-dash.fxdash.json", path);
            Check("share: the file is plain JSON a person can read", System.IO.File.ReadAllText(path).Contains("\"Name\": \"My dash\""));

            var r = DashShare.Import(path);
            Check("share: import saves the dash under its own id", r.Dash.Id == "my-dash" && !r.Renamed && !r.AlreadyThere && System.IO.File.Exists(r.File) && DashLibrary.Load(null).Any(x => x.Id == "my-dash" && x.Author == "me"));
            Check("share: an imported dash says where it came from", DashLibrary.Load(null).First(x => x.Id == "my-dash").Source == "Imported from a file");

            int files = System.IO.Directory.GetFiles(DashLibrary.Folder).Length;
            var again = DashShare.Import(path);
            Check("share: the same dash twice adds nothing", again.AlreadyThere && System.IO.Directory.GetFiles(DashLibrary.Folder).Length == files);

            var changed = mine.Clone(); changed.Elements[0].X += 1;
            string before = System.IO.File.ReadAllText(r.File);
            var r2 = DashShare.Import(DashShare.Export(changed, System.IO.Path.Combine(inbox, "changed")));
            Check("share: a different dash with a taken id gets a new id and nothing is overwritten",
                  r2.Renamed && r2.Dash.Id == "my-dash-2" && System.IO.File.ReadAllText(r.File) == before, r2.Dash.Id);

            var asBuiltIn = mine.Clone(); asBuiltIn.Id = BuiltInDashes.MustangGt3().Id;
            var r3 = DashShare.Import(DashShare.Export(asBuiltIn, System.IO.Path.Combine(inbox, "builtin")));
            Check("share: a file can't take a built-in dash's id", r3.Dash.Id != BuiltInDashes.MustangGt3().Id && DashLibrary.Load(null).Count(x => x.Id == BuiltInDashes.MustangGt3().Id) == 1, r3.Dash.Id);

            var lib = mine.Clone(); lib.Id = "lib-cool-dash"; lib.Name = "Cool";
            Check("share: a shared copy never takes a library item's lib- id", DashShare.Import(DashShare.Export(lib, System.IO.Path.Combine(inbox, "lib"))).Dash.Id == "cool-dash");

            var noId = mine.Clone(); noId.Id = null; noId.Name = "Night Stint 24h!";
            Check("share: no id: made from the name", DashShare.Import(DashShare.Export(noId, System.IO.Path.Combine(inbox, "noid"))).Dash.Id == "night-stint-24h");

            // refusals: what the library refuses, the receiver refuses
            string Refusal(string file, string content) { var f = System.IO.Path.Combine(inbox, file); System.IO.File.WriteAllText(f, content); try { DashShare.Read(f); return null; } catch (System.Exception ex) { return ex.Message; } }
            var js = mine.Clone(); js.Elements[0].Bind = "js:return eval(1)";
            Check("share: a js: formula is refused", (Refusal("js.fxdash.json", DashTools.Serialize(js)) ?? "").Contains("js:"));
            var sf = mine.Clone(); sf.ScriptsFolder = "x";
            Check("share: a scripts folder is refused", Refusal("sf.fxdash.json", DashTools.Serialize(sf)) != null);
            var nf = mine.Clone(); nf.FormatVersion = DashDefinition.CurrentFormat + 1;
            Check("share: a newer dash format is refused, not half-loaded", (Refusal("nf.fxdash.json", DashTools.Serialize(nf)) ?? "").Contains("newer"));
            Check("share: not JSON", Refusal("x.fxdash.json", "hello") == "this isn't a dash file");
            Check("share: JSON that isn't a dash", Refusal("y.fxdash.json", "{\"a\":1}") != null && Refusal("z.fxdash.json", "[1,2]") != null);
            Check("share: a dash with no elements", Refusal("e.fxdash.json", "{\"Id\":\"x\",\"Elements\":[]}") != null);
            Check("share: a file over the size limit", (Refusal("big.fxdash.json", new string(' ', LibraryClient.MaxDashBytes + 10)) ?? "").Contains("too big"));
            bool exportJs = false; try { DashShare.Export(js, System.IO.Path.Combine(inbox, "nojs")); } catch { exportJs = true; }
            Check("share: a dash with js: isn't exported (the receiver would refuse it)", exportJs && !System.IO.File.Exists(System.IO.Path.Combine(inbox, "nojs.fxdash.json")));
            Check("share: nothing refused was saved", DashLibrary.Load(null).All(x => x.Id != "x" && x.Elements.All(e => e.Bind != "js:return eval(1)")));

            Check("share: slugs", DashShare.Slug("Night Stint 24h!") == "night-stint-24h" && DashShare.Slug("a") == null && DashShare.Slug("  ") == null && DashShare.Slug(new string('a', 100)).Length == 64);
            Check("share: library links match the website's pages", DashShare.LinkFor("dash", "slipstream") == "https://fxunleashed.com/library/#dash-slipstream"
                  && DashShare.LinkFor("saver", "paddock-clock") == "https://fxunleashed.com/library/#saver-paddock-clock");
        }
        finally
        {
            DashLibrary.Root = oldRoot;
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>With FXU_LIBRARY = a checkout of the library repo: every item there downloads and passes the plugin's checks.</summary>
    static void RealLibrary()
    {
        var path = System.Environment.GetEnvironmentVariable("FXU_LIBRARY");
        if (string.IsNullOrEmpty(path)) return;
        var client = new LibraryClient(path);
        var index = client.GetIndex(allowCache: false);
        foreach (var item in index.Items)
        {
            string why = null;
            try { client.Download(item); client.GetPreview(item); } catch (System.Exception ex) { why = ex.Message; }
            Check($"C: library item {item.Kind}/{item.Id} installs", why == null, why ?? $"{item.BytesPerSecond} B/s");
        }
    }

    static (int Status, string Headers) Http(int port, string method, string path, string origin, string extra = "")
    {
        using (var c = new System.Net.Sockets.TcpClient("127.0.0.1", port))
        {
            var s = c.GetStream();
            var req = $"{method} {path} HTTP/1.1\r\nHost: 127.0.0.1\r\n" + (origin != null ? $"Origin: {origin}\r\n" : "") + extra + "Content-Length: 0\r\n\r\n";
            var b = System.Text.Encoding.ASCII.GetBytes(req); s.Write(b, 0, b.Length);
            var r = new System.IO.StreamReader(s).ReadToEnd();
            int status = int.Parse(r.Split(' ')[1]);
            return (status, r.Substring(0, r.IndexOf("\r\n\r\n")));
        }
    }

    // the checked-script rules (ScriptCheck); the same cases run against the library's copy of the rules
    static void ScriptCheckTests()
    {
        var path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "script-vectors.json");
        var v = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        var bad = new System.Collections.Generic.List<string>();
        int allowed = 0, refused = 0;
        foreach (var c in v["allow"])
        {
            allowed++;
            var p = ScriptCheck.Check((string)c["source"]);
            if (p.Count > 0) bad.Add("should pass: " + (string)c["name"] + " -> " + p[0]);
        }
        foreach (var c in v["refuse"])
        {
            refused++;
            var p = ScriptCheck.Check((string)c["source"]);
            if (p.Count == 0) bad.Add("should be refused: " + (string)c["name"]);
        }
        Check($"S: the shared script cases ({allowed} allowed, {refused} refused) all come out right", bad.Count == 0, string.Join("; ", bad));
        Check("S: a script over the length limit is refused", ScriptCheck.Check("return 1;" + new string(' ', ScriptCheck.MaxChars)).Count > 0);
        Check("S: a text over the length limit is refused", ScriptCheck.Check("return '" + new string('a', ScriptCheck.MaxTextChars + 1) + "';").Count > 0);
        Check("S: deeply nested code is refused", ScriptCheck.Check("return " + new string('(', 3) + string.Concat(Enumerable.Repeat("!", 40)) + "1" + new string(')', 3) + ";").Count > 0);
        Check("S: a long chain of 'else if' is refused", ScriptCheck.Check(string.Concat(Enumerable.Range(0, 60).Select(i => $"if (root.a == {i}) {{ root.b = {i}; }} else ")) + "{ root.b = 0; }").Count > 0);
        Check("S: null and nothing check without throwing", ScriptCheck.Check(null).Count == 0 && ScriptCheck.Check("").Count == 0);

        // a dash: allowed scripts pass the library rules, an unsafe one doesn't, a scripts folder never does
        var d = BuiltInDashes.MustangGt3();
        var withLatch = d.Clone();
        withLatch.Elements[0].Bind = "js:" + (string)v["allow"][0]["source"];
        Check("S: a dash with the LIFT latch passes the library's check", LibraryClient.Check(withLatch, 1000).Count == 0, string.Join("; ", LibraryClient.Check(withLatch, 1000)));
        var evil = d.Clone();
        evil.Elements[0].Bind = "js:return eval('1');";
        Check("S: a dash with eval does not", LibraryClient.Check(evil, 1000).Any(x => x.Contains("js:")));
        var folder = withLatch.Clone(); folder.ScriptsFolder = @"C:\x";
        Check("S: a scripts folder is still refused", LibraryClient.Check(folder, 1000).Any(x => x.Contains("scripts folder")));
        var packed = LibraryInstaller.Package(withLatch, new LibraryItem { Id = "script-test", Name = "x", Author = "t", License = "CC0-1.0", Version = "1.0.0" }, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fxu-script-" + System.Guid.NewGuid().ToString("N")));
        Check("S: packaging a dash with a checked script works (and drops any scripts folder)", System.IO.File.Exists(System.IO.Path.Combine(packed, "dash.json")));
        try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(packed)), true); } catch { }
    }

    // the dashes that come inside the plugin (Usb/Bundled, tools/bundle-dashes.py)
    static void BundledDashTests()
    {
        var errors = new System.Collections.Generic.List<string>();
        var all = BundledDashes.All(errors);
        var expected = new[] { "lib-slipstream", "lib-apex", "lib-halo", "lib-nocturne", "lib-nocturne-blue", "lib-nocturne-red", "lmgt3-mclaren-720s", "toyota-gr010-hybrid", "lmp3-ginetta-g61", "lmgt3-aston-martin" };
        Check("B: all ten dashes come with the plugin", errors.Count == 0 && all.Count == expected.Length && expected.All(id => all.Any(d => d.Id == id)), string.Join("; ", errors));
        Check("B: each is read-only (built in and bundled) and names its author", all.All(d => d.BuiltIn && d.Bundled && !string.IsNullOrWhiteSpace(d.Author)));
        var lmu = all.Where(d => d.Id == "lmgt3-mclaren-720s" || d.Id == "toyota-gr010-hybrid").ToList();
        Check("B: the two LMU conversions credit Redadeg and say they were converted", lmu.Count == 2 && lmu.All(d => d.Author.Contains("Redadeg") && d.Description.Contains("Converted") && d.Description.Contains("lmu-dashboards.com")));
        Check("B: no scripts folder path from the author's PC is kept", all.All(d => d.ScriptsFolder == null));
        Check("B: every bundled dash passes the layout check with no errors", all.All(d => DashTools.Check(d).Errors == 0),
              string.Join(", ", all.Where(d => DashTools.Check(d).Errors > 0).Select(d => d.Id)));
        Check("B: every bundled dash passes the library's rules (format, size, checked scripts)", all.All(d => LibraryClient.Check(d, 100000).Count == 0),
              string.Join("; ", all.SelectMany(d => LibraryClient.Check(d, 100000).Select(x => d.Id + ": " + x))));

        // the loader: bundled dashes are in the list, and a file with the same id takes the place of the bundled one
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fxu-bundled-" + System.Guid.NewGuid().ToString("N"));
        var oldRoot = DashLibrary.Root;
        try
        {
            DashLibrary.Root = root;
            var list = DashLibrary.Load(null);
            Check("B: the dash list holds the Mustang and all ten bundled dashes", list.Any(d => d.Id == BuiltInDashes.MustangId) && expected.All(id => list.Any(d => d.Id == id && d.Bundled)));
            var mine = BuiltInDashes.MustangGt3(); mine.Id = "lib-halo"; mine.Name = "Halo, updated from the library"; mine.BuiltIn = false;
            DashTools.Save(mine);
            list = DashLibrary.Load(null);
            Check("B: a file with a bundled dash's id replaces it (no duplicate)", list.Count(d => d.Id == "lib-halo") == 1 && list.First(d => d.Id == "lib-halo").Name.StartsWith("Halo, updated") && !list.First(d => d.Id == "lib-halo").Bundled);

            // the library recognises them as installed, and offers an update only for a newer version
            var s = new UsbSettings();
            var item = new LibraryItem { Id = "slipstream", Name = "SLIPSTREAM", Version = "1.0.1" };
            var rec = LibraryInstaller.Installed(s, item, DashLibrary.LocalIds());
            Check("B: the library shows a bundled dash as installed, at the version it came with", rec != null && rec.Found && rec.Bundled && rec.DashId == "lib-slipstream" && rec.Version == "1.0.1");
            Check("B: no update for the same version", !LibraryInstaller.UpdateAvailable(s, item, DashLibrary.LocalIds()));
            // the two LMU conversions keep their own dash ids but are library items too
            var toyota = LibraryInstaller.Installed(s, new LibraryItem { Id = "toyota-gr010-hybrid", Name = "Toyota GR010 Hybrid", Version = "1.0.0", MinPlugin = "0.5.2" }, DashLibrary.LocalIds());
            Check("B: the library shows a bundled LMU conversion as installed under its own dash id", toyota != null && toyota.Found && toyota.Bundled && toyota.DashId == "toyota-gr010-hybrid");
            var mustang = LibraryInstaller.Installed(s, new LibraryItem { Id = "lmgt3-mustang", Name = "LMGT3 Ford Mustang GT3", Version = "1.0.0" }, DashLibrary.LocalIds());
            Check("B: the library shows the built-in Mustang as installed, with nothing to update at the same version", mustang != null && mustang.Found && mustang.Bundled && mustang.DashId == BuiltInDashes.MustangId && !LibraryInstaller.UpdateAvailable(s, new LibraryItem { Id = "lmgt3-mustang", Version = "1.0.0" }, DashLibrary.LocalIds()));
            var must = BuiltInDashes.MustangGt3();
            Check("B: the Mustang credits Redadeg and lmu-dashboards.com and passes the library's rules", must.Author.Contains("Redadeg") && must.Description.Contains("lmu-dashboards.com") && !must.Description.Contains("permission") && LibraryClient.Check(must, 100000).Count == 0);
            item.Version = "1.1.0";
            Check("B: a newer version in the library is an update", LibraryInstaller.UpdateAvailable(s, item, DashLibrary.LocalIds()));
        }
        finally { DashLibrary.Root = oldRoot; try { System.IO.Directory.Delete(root, true); } catch { } }
    }

    // what the designer's Wheel traffic panel is built on (fxdash verify and fit-bands)
    static void TrafficPanelTests()
    {
        var d = BuiltInDashes.MustangGt3();
        var v = DashVerify.Run(d, 10, 20, 10);
        Check("T: verify measures a demo lap (updates, average and busiest second, a timeline)", v.Updates > 0 && v.AvgBytesPerSecond > 0 && v.WorstSecondBytes >= v.AvgBytesPerSecond && v.Timeline.Count >= 9,
              $"{v.Updates} updates, {v.AvgBytesPerSecond} B/s avg, {v.WorstSecondBytes} B/s worst, {v.Timeline.Count} s");
        Check("T: verify names what sends the most", v.Traffic.Count > 0 && v.Traffic[0].BytesPerSecond >= v.Traffic[v.Traffic.Count - 1].BytesPerSecond);
        var ram = DashVerify.Run(d, 10, 20, 10, tiles: true);
        Check("T: verify with the RAM patch (tiles) also runs", ram.Updates == v.Updates && ram.Seconds == 10);
        // a dash built to be bad: copies of a value drawn on top of each other and over other elements (what the panel shows red)
        var examplePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs", "examples", "example-gt.json"));
        if (System.IO.File.Exists(examplePath))
        {
            var good = DashTools.Parse(System.IO.File.ReadAllText(examplePath));
            Check("T: the example dash passes verify", DashVerify.Run(good, 10, 20, 20).Ok);
            var worse = DashTools.Parse(System.IO.File.ReadAllText(examplePath));
            var speed = worse.Elements.First(e => e.Type == "value" && e.Bind == "speed");
            string J(DashElement e) => Newtonsoft.Json.JsonConvert.SerializeObject(e);
            for (int i = 0; i < 6; i++)
            {
                var extra = Newtonsoft.Json.JsonConvert.DeserializeObject<DashElement>(J(speed)); extra.Name = "extra " + i; extra.Y = 100 + i * 40; extra.Bind = "rpm";
                worse.Elements.Add(extra);
            }
            var bad = DashVerify.Run(worse, 10, 20, 20);
            Check("T: values drawn over other elements flash (the panel's red gate)", !bad.Ok && bad.FlashingUpdates > 0, $"{bad.FlashingUpdates} of {bad.Updates} flash");
        }
        var plain = d.Clone();
        Check("T: fit-bands changes nothing on a dash that passes", DashTools.FitTextBands(plain).Count == 0);
    }

    static void ServerOrigins()
    {
        int port = 8897;
        var server = new DesignerServer(port, null); server.Start();
        try
        {
            System.Threading.Thread.Sleep(200);
            Check("C: no Origin (curl, fxdash, agents) works", Http(port, "GET", "/api/schema", null).Status == 200);
            Check("C: the server's own page works", Http(port, "POST", "/api/check", $"http://127.0.0.1:{port}").Status != 403);
            var evil = Http(port, "POST", "/api/wheel/leds", "https://evil.example");
            Check("C: another website is refused (403, no CORS)", evil.Status == 403 && !evil.Headers.Contains("Access-Control-Allow-Origin"));
            Check("C: the website can't reach other routes", Http(port, "DELETE", "/api/dashes/x", "https://fxunleashed.com").Status == 403);
            var site = Http(port, "OPTIONS", "/api/library/status", "https://fxunleashed.com", "Access-Control-Request-Method: GET\r\nAccess-Control-Request-Private-Network: true\r\n");
            Check("C: the website's preflight for library routes (CORS + private network)", site.Status == 204 && site.Headers.Contains("Access-Control-Allow-Origin: https://fxunleashed.com")
                                                                                              && site.Headers.Contains("Access-Control-Allow-Private-Network: true"));
            Check("C: the website reads the plugin status", Http(port, "GET", "/api/library/status", "https://fxunleashed.com").Status == 200);
        }
        finally { server.Dispose(); }
    }

    static void BaseSettingsTests()
    {
        var orig = Newtonsoft.Json.Linq.JObject.Parse("{\"max_wheel_angle\":900,\"wheel_angle_limit\":900,\"total_force\":80,\"game_damper\":100}");
        var a = BaseSwitcher.Apply(orig, new BaseCarSetting { Angle = 5000, Force = 55 });
        Check("I: angle clamped, both angle fields, force set, rest kept", (int)a["max_wheel_angle"] == BaseSwitcher.MaxAngle && (int)a["wheel_angle_limit"] == BaseSwitcher.MaxAngle
                                                                          && (int)a["total_force"] == 55 && (int)a["game_damper"] == 100 && (int)orig["total_force"] == 80);
        var s = new BaseSettings { Enabled = true };
        s.Games["iRacing"] = new BaseCarSetting { Force = 70 };
        s.Cars["iRacing | car1"] = new BaseCarSetting { Angle = 540 };
        Check("I: car wins over game, game for other cars", s.For("iRacing | car1", "iRacing").Angle == 540 && s.For("iRacing | car2", "IRACING").Force == 70 && s.For("AC | x", "AC") == null);

        // the whole flow against a fake SimPro
        var servos = (Newtonsoft.Json.Linq.JObject)orig.DeepClone();
        var sets = new System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject>();
        var sim = new SimProClient();
        sim.Fake = (m, b) =>
        {
            if (m == "get_device_list") return Newtonsoft.Json.Linq.JArray.Parse("[{\"product_type\":\"wheel\",\"device_uuid\":\"w\",\"product_uuid\":\"w\"},{\"product_type\":\"base\",\"device_uuid\":\"b\",\"product_uuid\":\"b\",\"product_short_name\":\"EVO\"}]");
            if (m == "preset_get_selected_dev_config") return new Newtonsoft.Json.Linq.JObject { ["preset_uuid"] = "P1", ["config"] = new Newtonsoft.Json.Linq.JObject { ["servos"] = new Newtonsoft.Json.Linq.JObject { ["1"] = servos.DeepClone() } } };
            if (m == "preset_set_dev_config") { sets.Add(b); if ((string)b["device_uuid"] == "b" && (string)b["part_type"] == "servos") servos = (Newtonsoft.Json.Linq.JObject)b["config"].DeepClone(); return null; }
            throw new System.Exception("unexpected " + m);
        };
        int saves = 0;
        var sw = new BaseSwitcher(sim, () => s, () => saves++);
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: car 1 pushed to the base only (servos part 1)", sets.Count == 1 && (string)sets[0]["device_uuid"] == "b" && (int)sets[0]["part_id"] == 1
                                                                  && (int)servos["max_wheel_angle"] == 540 && (int)servos["total_force"] == 80 && s.Originals.ContainsKey("P1"));
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: same car again = no push", sets.Count == 1);
        sw.OnCarAsync("AC | x", "AC").Wait();
        Check("I: car without settings = the original back", sets.Count == 2 && (int)servos["max_wheel_angle"] == 900 && !sw.HasChanges);
        sw.OnCarAsync("iRacing | car2", "iRacing").Wait();
        Check("I: game setting for another car", (int)servos["total_force"] == 70 && (int)servos["max_wheel_angle"] == 900);
        servos["game_damper"] = 50; // the user edits the preset in SimPro
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: a user edit becomes the new original", (int)servos["game_damper"] == 50 && (int)servos["max_wheel_angle"] == 540);
        sw.RestoreAsync().Wait();
        Check("I: restore = the (edited) original", (int)servos["max_wheel_angle"] == 900 && (int)servos["game_damper"] == 50 && (int)servos["total_force"] == 80 && !sw.HasChanges);
        s.Enabled = false; int before = sets.Count;
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: off = nothing pushed", sets.Count == before);
    }

    static void LedDevice()
    {
        var reg = new FXProLedDeviceRegistry().GetDevices().ToList();
        Check("D: one device, instances > 0", reg.Count == 1 && reg[0].MaximumInstances > 0 && reg[0].DeviceTypeID == FXProLedDeviceRegistry.DeviceTypeId);
        var red = System.Drawing.Color.Red; var none = System.Drawing.Color.Transparent;
        var strip = new System.Drawing.Color[21]; strip[0] = red; strip[3] = red; strip[20] = red;
        var f = SimHubLedDevice.ToFrame(strip, new[] { System.Drawing.Color.Blue }, new System.Drawing.Color[5], new System.Drawing.Color[0], new System.Drawing.Color[0], 1, 1, 1);
        Check("D: strip 0 = left side top (17), 3 = first rev LED (23), 20 = right side bottom (22)", f[17].R == 255 && f[23].R == 255 && f[22].R == 255 && f[37].Brightness == 0);
        Check("D: button 0 = LED 0, full brightness = 90", f[0].B == 255 && f[0].Brightness == 90);
        var half = SimHubLedDevice.ToFrame(strip, new System.Drawing.Color[0], new System.Drawing.Color[0], new System.Drawing.Color[0], new System.Drawing.Color[0], 0.5, 1, 1);
        Check("D: rpm brightness scales", half[23].Brightness == 45);
        var raw = new System.Drawing.Color[38]; raw[23] = System.Drawing.Color.Lime; raw[5] = none;
        var r = SimHubLedDevice.ToFrame(strip, new System.Drawing.Color[0], new System.Drawing.Color[0], raw, new System.Drawing.Color[0], 1, 1, 1);
        Check("D: individual LEDs override the groups", r[23].G == 255 && r[23].R == 0 && r[17].R == 255);
        // the device's maps against the wheel model, so the two can't drift: every one of the 38 LEDs is reachable from exactly one
        // SimHub source, and each source lands in its own group in the model's order (the model is what the presets and ATSR-Hub use too)
        var model = WheelModel.FxPro;
        int One(LedColor[] frame) { var on = Enumerable.Range(0, frame.Length).Where(i => frame[i].Brightness > 0).ToList(); return on.Count == 1 ? on[0] : -1; }
        var white = System.Drawing.Color.White; var empty = new System.Drawing.Color[0];
        var reached = new System.Collections.Generic.List<int>();
        var stripLeds = new System.Collections.Generic.List<int>(); var buttonLeds = new System.Collections.Generic.List<int>(); var encoderLeds = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 21; i++) { var one = new System.Drawing.Color[21]; one[i] = white; stripLeds.Add(One(SimHubLedDevice.ToFrame(one, empty, empty, empty, empty, 1, 1, 1))); }
        for (int i = 0; i < 12; i++) { var one = new System.Drawing.Color[12]; one[i] = white; buttonLeds.Add(One(SimHubLedDevice.ToFrame(empty, one, empty, empty, empty, 1, 1, 1))); }
        for (int i = 0; i < 5; i++) { var one = new System.Drawing.Color[5]; one[i] = white; encoderLeds.Add(One(SimHubLedDevice.ToFrame(empty, empty, one, empty, empty, 1, 1, 1))); }
        reached.AddRange(stripLeds); reached.AddRange(buttonLeds); reached.AddRange(encoderLeds);
        Check("D: all 38 wheel LEDs are reachable, each from exactly one SimHub source", reached.Distinct().Count() == 38 && reached.All(i => i >= 0 && i < 38), string.Join(",", reached));
        Check("D: strip 0-2 = the left side lights, top to bottom (the model's SideLeft)", stripLeds.Take(3).SequenceEqual(model.Leds(LedGroup.SideLeft)));
        Check("D: strip 3-17 = the rev lights, left to right (the model's Rev)", stripLeds.Skip(3).Take(15).SequenceEqual(model.Leds(LedGroup.Rev)));
        Check("D: strip 18-20 = the right side lights, top to bottom (the model's SideRight)", stripLeds.Skip(18).SequenceEqual(model.Leds(LedGroup.SideRight)));
        Check("D: the 12 buttons are the model's Buttons, in order", buttonLeds.SequenceEqual(model.Leds(LedGroup.Buttons)));
        Check("D: the 5 encoders (ABS, TC, BB, DIFF, MAP) are the model's Encoders, in order", encoderLeds.SequenceEqual(model.Leds(LedGroup.Encoders)));
        Check("D: a SimHub colour with no alpha or no colour lights nothing", One(SimHubLedDevice.ToFrame(new[] { System.Drawing.Color.FromArgb(0, 255, 0, 0), System.Drawing.Color.Black }, empty, empty, empty, empty, 1, 1, 1)) == -1);
        Check("D: brightness 0 lights nothing, and over 1 is capped at the firmware's 90", One(SimHubLedDevice.ToFrame(new[] { white }, empty, empty, empty, empty, 0, 1, 1)) == -1
              && SimHubLedDevice.ToFrame(new[] { white }, empty, empty, empty, empty, 5, 1, 1)[17].Brightness == 90);
        if (System.Environment.GetEnvironmentVariable("UI_DEVICE") == "1")
        {
            try { var inst = reg[0].Factory(); System.Console.WriteLine("D: created " + (inst?.GetType().FullName ?? "null")); }
            catch (System.Exception ex) { System.Console.WriteLine("D: create threw " + ex); }
        }
        Check("D: device id not used by SimHub itself", !System.IO.File.ReadAllText(System.IO.Path.Combine(SimHubDir(), "SimHub.Plugins.dll"), System.Text.Encoding.Unicode)
                                                         .Contains(FXProLedDeviceRegistry.DeviceTypeId));
    }

    static string SimHubDir() => System.Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub";

    /// <summary>Feeds ScreenMirror the way FxHostScreen does and checks what the mirror page would get.</summary>
    sealed class MirrorSink : IScreenSink
    {
        public void Cmd(string c) => ScreenMirror.Cmd(c);
        public void Flush() { }
    }

    static void Mirror()
    {
        ScreenMirror.Held(false);
        ScreenMirror.WheelDash(null);
        Check("H: nothing to show while the wheel has its screen", ScreenMirror.Png() == null);
        ScreenMirror.Held(true);
        var d = DashLibrary.Load(new System.Collections.Generic.List<string>()).First();
        var rn = new DashRenderer(new MirrorSink(), d, 10, 20);
        rn.DrawAll();
        var png = ScreenMirror.Png();
        bool drawn = false;
        if (png != null)
            using (var bmp = new System.Drawing.Bitmap(new System.IO.MemoryStream(png)))
                for (int y = 0; y < bmp.Height && !drawn; y += 7)
                    for (int x = 0; x < bmp.Width && !drawn; x += 7)
                        drawn = bmp.GetPixel(x, y).ToArgb() != System.Drawing.Color.Black.ToArgb();
        Check("H: mirror shows the drawn dash", drawn, d.Name);
        Check("H: PNG cached until the next command", ReferenceEquals(png, ScreenMirror.Png()));
        ScreenMirror.Cmd("dim=0");
        Check("H: screen off shows black", !ReferenceEquals(png, ScreenMirror.Png()));
        ScreenMirror.Held(false);
        Check("H: released = nothing", ScreenMirror.Png() == null);
        ScreenMirror.Leds(new[] { new LedColor(255, 0, 0, 90) });
        var st = Newtonsoft.Json.JsonConvert.SerializeObject(ScreenMirror.State());
        Check("H: state has the LEDs", st.Contains("#FF0000"), st);
        ScreenMirror.Leds(null);

        // how the page looks comes from the settings: defaults, a round trip, bad values made safe, old settings without it
        var look = new MirrorSettings { Frame = MirrorFrame.Carbon, FrameColor = "rev", LightStyle = MirrorLightStyle.Bars, LightsAt = MirrorLightsAt.Below, Background = MirrorBackground.Green, SideLights = false };
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<MirrorSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(look));
        Check("H: mirror options survive the settings file", back.Frame == MirrorFrame.Carbon && back.IsFollowRev && back.LightStyle == MirrorLightStyle.Bars &&
              back.LightsAt == MirrorLightsAt.Below && back.Background == MirrorBackground.Green && !back.SideLights && back.RevLights);
        var bad = new MirrorSettings { FrameColor = "red", Corners = 400, Fps = 0 }; bad.Clamp();
        Check("H: bad mirror options are made safe", bad.FrameColor == MirrorSettings.Accents[0] && bad.Corners == 32 && bad.Fps == 1);
        Check("H: settings from before the mirror options still load", Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>("{\"Enabled\":true}").Mirror.Frame == MirrorFrame.Bezel);
        ScreenMirror.Options = () => look;
        var opt = Newtonsoft.Json.JsonConvert.SerializeObject(ScreenMirror.State());
        Check("H: the page gets the options", opt.Contains("\"frame\":\"carbon\"") && opt.Contains("\"frameColor\":\"rev\"") && opt.Contains("\"lightsAt\":\"below\"") && opt.Contains("\"sides\":false"), opt);
        ScreenMirror.Options = () => throw new System.Exception("settings not available");
        Check("H: the page still gets defaults when the settings can't be read", Newtonsoft.Json.JsonConvert.SerializeObject(ScreenMirror.State()).Contains("\"frame\":\"bezel\""));
        ScreenMirror.Options = () => new MirrorSettings();
    }

    static void WheelValues()
    {
        var missing = WheelTelemetry.Fields.Where(f => !SimProTelemetry.Fields.ContainsKey(f.Field)).Select(f => f.Field).ToList();
        Check("G: every wheel dash value is a SimPro struct field", missing.Count == 0, string.Join(", ", missing));
        Check("G: no field listed twice", WheelTelemetry.Fields.Select(f => f.Field).Distinct().Count() == WheelTelemetry.Fields.Length);
    }
}

/// <summary>Screen commands into the mirror only (UsbTest's "mirror" mode).</summary>
sealed class MirrorDemoSink : IScreenSink
{
    public void Cmd(string c) => ScreenMirror.Cmd(c);
    public void Flush() { }
}
