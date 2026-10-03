using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using User.FXProRpmSync;

/// <summary>The updater without the network: versions, release parsing, every download check, swap and roll back in a
/// scratch folder with a real copy of the plugin DLL, the start-up marker.</summary>
static class UpdaterTests
{
    static UpdateManifest WithMin(this UpdateManifest m, string min) { m.minSimHub = min; return m; }
    static void Check(string name, bool ok, string detail = "") => FeatureTests.Check("update: " + name, ok, detail);

    public static void Run(string dir)
    {
        // SemVer
        string[] order = { "0.2.1", "0.3.0-alpha", "0.3.0-alpha.1", "0.3.0-alpha.beta", "0.3.0-beta", "0.3.0-beta.2", "0.3.0-beta.11", "0.3.0-rc.1", "0.3.0", "0.3.1", "1.0.0" };
        bool sorted = true;
        for (int i = 1; i < order.Length; i++) sorted &= SemVer.Parse(order[i - 1]).CompareTo(SemVer.Parse(order[i])) < 0;
        Check("semver order (spec example)", sorted);
        Check("semver v prefix and build metadata", SemVer.Parse("v1.2.3+abc").CompareTo(SemVer.Parse("1.2.3")) == 0);
        Check("semver rejects junk", !SemVer.TryParse("latest", out _) && !SemVer.TryParse("", out _));
        Check("current version parses", SemVer.TryParse(Updater.CurrentVersion, out _), Updater.CurrentVersion);

        // Release JSON as GitHub returns it
        var rel = JObject.Parse(@"{""tag_name"":""v0.3.0-beta.1"",""name"":""Beta 1"",""body"":""notes"",""html_url"":""https://github.com/x/y/releases/tag/v0.3.0-beta.1"",
            ""prerelease"":true,""draft"":false,""published_at"":""2026-09-29T10:00:00Z"",
            ""assets"":[{""name"":""FXUnleashed-v0.3.0-beta.1.zip"",""browser_download_url"":""https://github.com/x/y/a.zip""},{""name"":""manifest.json"",""browser_download_url"":""https://github.com/x/y/manifest.json""}]}");
        var r = Updater.ParseRelease(rel);
        Check("release parsed", r.Version.ToString() == "0.3.0-beta.1" && r.Prerelease && r.ZipUrl.EndsWith("a.zip") && r.ManifestUrl.EndsWith("manifest.json"));
        rel["draft"] = true;
        Check("drafts ignored", Updater.ParseRelease(rel) == null);

        // A release zip made from the real plugin DLL
        string pluginDll = typeof(Updater).Assembly.Location; // the test's own assembly: wrong name on purpose for one check
        string realDll = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\bin\Release\net48\User.FXProRpmSync.dll"));
        if (!File.Exists(realDll)) { Check("plugin DLL built (bin/Release/net48)", false, realDll); return; }
        byte[] dll = File.ReadAllBytes(realDll);
        byte[] zip = Zip(("User.FXProRpmSync.dll", dll), ("README.md", new byte[] { 1, 2, 3 }));
        var v = SemVer.Parse("0.3.0-beta.1");
        UpdateManifest M() => new UpdateManifest
        {
            version = "0.3.0-beta.1", channel = "beta", minSimHub = "9.0",
            dll = new UpdateManifest.FileInfoPart { name = "User.FXProRpmSync.dll", sha256 = Updater.Sha256(dll), size = dll.Length },
            zip = new UpdateManifest.FileInfoPart { name = "x.zip", sha256 = Updater.Sha256(zip) },
        };
        Check("verify ok", Updater.Verify(M(), v, zip, new Version(9, 11, 0)).SequenceEqual(dll));
        Check("verify: version mismatch", Throws(() => Updater.Verify(M(), SemVer.Parse("0.3.0"), zip, null)));
        var bad = (byte[])zip.Clone(); bad[bad.Length / 2] ^= 0xFF;
        Check("verify: damaged zip", Throws(() => Updater.Verify(M(), v, bad, null)));
        var m2 = M(); m2.dll.sha256 = new string('0', 64);
        Check("verify: DLL checksum", Throws(() => Updater.Verify(m2, v, zip, null)));
        var m3 = M(); m3.dll.size = 5;
        Check("verify: DLL size", Throws(() => Updater.Verify(m3, v, zip, null)));
        var m4 = M(); m4.minSimHub = "99.0";
        Check("verify: SimHub too old", Throws(() => Updater.Verify(m4, v, zip, new Version(9, 11, 0))));
        // SimHub's real version (its exe's file version is 1.0.0.0, which once made every update look "too old")
        SimHub.Plugins.VersionParser realSimHub = "9.12.8";
        var sh = Updater.FromSimHub(realSimHub);
        Check("SimHub version read from SimHub's own value: 9.12.8", sh != null && sh.Major == 9 && sh.Minor == 12 && sh.Build == 8, sh?.ToString());
        Check("verify: minimum SimHub 9.11 accepts SimHub 9.12.8", Updater.Verify(M().WithMin("9.11"), v, zip, sh).SequenceEqual(dll));
        Check("verify: minimum SimHub 9.13 refuses SimHub 9.12.8", Throws(() => Updater.Verify(M().WithMin("9.13"), v, zip, sh)));
        Check("SimHub version unknown (not running in SimHub): no refusal", Updater.SimHubVersion() == null && Updater.FromSimHub(null) == null);
        // when the automatic check runs: every start-up, then every 6 hours while SimHub stays open, never when switched off
        var t0 = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Check("auto check: at start-up even if checked an hour ago", Updater.CheckDue(true, t0.AddHours(-1), t0, true));
        Check("auto check: not again within 6 hours", !Updater.CheckDue(true, t0.AddHours(-5), t0, false));
        Check("auto check: again after 6 hours while open", Updater.CheckDue(true, t0.AddHours(-6), t0, false));
        Check("auto check: never checked yet", Updater.CheckDue(true, DateTime.MinValue, t0, false));
        Check("auto check: clock set back", Updater.CheckDue(true, t0.AddHours(3), t0, false));
        Check("auto check: switched off = never", !Updater.CheckDue(false, DateTime.MinValue, t0, true) && !Updater.CheckDue(false, DateTime.MinValue, t0, false));
        var noDll = Zip(("README.md", new byte[] { 1 }));
        var m5 = M(); m5.zip.sha256 = Updater.Sha256(noDll);
        Check("verify: no DLL in zip", Throws(() => Updater.Verify(m5, v, noDll, null)));

        // Swap and roll back in a scratch "SimHub folder"
        string simhub = Path.Combine(dir, "updater-simhub"), data = Path.Combine(dir, "updater-data");
        if (Directory.Exists(simhub)) Directory.Delete(simhub, true);
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Directory.CreateDirectory(simhub);
        string target = Path.Combine(simhub, Updater.DllName);
        File.WriteAllBytes(target, dll);
        File.WriteAllText(target + ".pdb-marker", "");
        var up = new Updater(FeatureTests.NewPlugin(), simhub, data);
        var changed = (byte[])dll.Clone();
        up.Swap(changed);
        Check("swap: new DLL in place, old kept", File.Exists(target) && File.Exists(target + ".old") && !File.Exists(target + ".new"));
        Check("swap: previous version readable", up.PreviousVersion != null, up.PreviousVersion ?? "null");
        Check("swap: refuses a DLL with another assembly name", Throws(() => up.Swap(File.ReadAllBytes(pluginDll))) && File.Exists(target) && !File.Exists(target + ".new"));
        up.Rollback();
        Check("rollback swaps back", File.Exists(target) && File.Exists(target + ".old"));

        // Start-up marker: a start that never finishes, three times -> automatic roll back
        var s1 = new Updater(FeatureTests.NewPlugin(), simhub, data); s1.StartupBegin();
        Check("marker: first start is fine", !s1.StartupFailedBefore);
        var s2 = new Updater(FeatureTests.NewPlugin(), simhub, data); s2.StartupBegin();
        Check("marker: an unfinished start is noticed", s2.StartupFailedBefore && s2.AutoRolledBackFrom == null);
        var s3 = new Updater(FeatureTests.NewPlugin(), simhub, data); s3.StartupBegin();
        Check("marker: third failed start rolls back", s3.AutoRolledBackFrom != null && File.Exists(target) && File.Exists(target + ".old"));
        var s4 = new Updater(FeatureTests.NewPlugin(), simhub, data); s4.StartupBegin(); s4.StartupSucceeded();
        var s5 = new Updater(FeatureTests.NewPlugin(), simhub, data); s5.StartupBegin();
        Check("marker: a finished start clears it", !s5.StartupFailedBefore);
        s5.StartupSucceeded();
    }

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using (var ms = new MemoryStream())
        {
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var f in files)
                    using (var s = z.CreateEntry(f.Name).Open()) s.Write(f.Data, 0, f.Data.Length);
            return ms.ToArray();
        }
    }
}
