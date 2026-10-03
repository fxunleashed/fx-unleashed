using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum UpdateChannel { Stable, Beta }

    /// <summary>Update settings (part of the plugin's settings).</summary>
    public class UpdateSettings
    {
        /// <summary>Look for a new version at every start-up and every few hours while SimHub stays open. Nothing is installed without a click.</summary>
        public bool AutoCheck = true;
        public UpdateChannel Channel = UpdateChannel.Stable;
        /// <summary>"owner/repo" on GitHub whose releases carry the plugin (zip + manifest.json).</summary>
        public string Repo = Updater.DefaultRepo;
        public DateTime LastCheckUtc;
        /// <summary>A version the user chose to skip (no banner for it).</summary>
        public string SkippedVersion;
    }

    /// <summary>A SemVer 2.0 version ("1.2.3", "v1.2.3-beta.4"); build metadata ("+...") is ignored.</summary>
    public sealed class SemVer : IComparable<SemVer>
    {
        public int Major, Minor, Patch;
        public string[] Pre = new string[0];

        public bool IsPrerelease => Pre.Length > 0;

        public static SemVer Parse(string s)
        {
            if (!TryParse(s, out var v)) throw new FormatException("Not a version: " + s);
            return v;
        }

        public static bool TryParse(string s, out SemVer v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = null;
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            var n = new int[3];
            for (int i = 0; i < Math.Min(3, parts.Length); i++)
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return false;
            v = new SemVer { Major = n[0], Minor = n[1], Patch = n[2], Pre = string.IsNullOrEmpty(pre) ? new string[0] : pre.Split('.') };
            return true;
        }

        public int CompareTo(SemVer o)
        {
            if (o == null) return 1;
            int c = Major.CompareTo(o.Major); if (c != 0) return c;
            c = Minor.CompareTo(o.Minor); if (c != 0) return c;
            c = Patch.CompareTo(o.Patch); if (c != 0) return c;
            // a pre-release sorts before its release
            if (Pre.Length == 0 && o.Pre.Length == 0) return 0;
            if (Pre.Length == 0) return 1;
            if (o.Pre.Length == 0) return -1;
            for (int i = 0; i < Math.Min(Pre.Length, o.Pre.Length); i++)
            {
                bool an = int.TryParse(Pre[i], out int a), bn = int.TryParse(o.Pre[i], out int b);
                if (an && bn) c = a.CompareTo(b);
                else if (an) c = -1;              // numeric identifiers sort before alphanumeric ones
                else if (bn) c = 1;
                else c = string.CompareOrdinal(Pre[i], o.Pre[i]);
                if (c != 0) return Math.Sign(c);
            }
            return Pre.Length.CompareTo(o.Pre.Length);
        }

        public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + string.Join(".", Pre) : "");
    }

    /// <summary>manifest.json, published with every release (release.ps1 writes it).</summary>
    public class UpdateManifest
    {
        public string version;
        public string channel;
        public FileInfoPart dll;
        public FileInfoPart zip;
        public string minSimHub;
        public FirmwarePart firmware;
        public string notes;
        public string published;

        public class FileInfoPart { public string name; public string sha256; public long size; }
        public class FirmwarePart { public int min; public int recommended; }
    }

    /// <summary>A release found on GitHub.</summary>
    public class ReleaseInfo
    {
        public SemVer Version;
        public string Tag, Name, Notes, PageUrl, ZipUrl, ManifestUrl, ZipName;
        public bool Prerelease;
        public DateTime Published;
    }

    /// <summary>
    /// In-plugin updates from GitHub releases (NEXT.md B). Check (at start-up, every few hours after, or "Check now") →
    /// banner → Install on a click: download the zip and manifest over HTTPS, check both SHA-256s and the DLL's size and
    /// assembly name, then swap the DLL. A loaded DLL can't be overwritten but can be renamed: the running one becomes
    /// User.FXProRpmSync.dll.old (kept for Roll back), the new one takes its name, and SimHub restarts. The DLL name and
    /// plugin class never change, so SimHub keeps the plugin enabled and its settings. A start-up marker catches a new
    /// version that fails to start: the settings page offers Roll back, and after three failed starts it rolls back by itself.
    /// </summary>
    public sealed class Updater
    {
        public const string DefaultRepo = "fxunleashed/fx-unleashed";
        public const string DllName = "User.FXProRpmSync.dll";
        private const long MaxZipBytes = 30 * 1024 * 1024;

        public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Installed, Error }

        private readonly FXProRpmSyncPlugin plugin;
        private readonly string pluginDir, dataDir;
        private readonly object gate = new object();

        public UpdateState State { get; private set; } = UpdateState.Idle;
        public string Message { get; private set; } = "";
        public ReleaseInfo Latest { get; private set; }
        public UpdateManifest LatestManifest { get; private set; }
        /// <summary>The previous start of this version didn't finish (see the start-up marker).</summary>
        public bool StartupFailedBefore { get; private set; }
        public string AutoRolledBackFrom { get; private set; }
        /// <summary>A newer version would show in the banner.</summary>
        public bool UpdateAvailable => State == UpdateState.Available && Latest != null;

        /// <summary>For the tests: another folder (the DLL's folder is SimHub's otherwise).</summary>
        public Updater(FXProRpmSyncPlugin plugin, string pluginDir = null, string dataDir = null)
        {
            this.plugin = plugin;
            this.pluginDir = pluginDir ?? Path.GetDirectoryName(typeof(Updater).Assembly.Location);
            this.dataDir = dataDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync");
        }

        private string Target => Path.Combine(pluginDir, DllName);
        private string OldPath => Target + ".old";
        private string MarkerPath => Path.Combine(dataDir, "startup-marker.json");

        /// <summary>This plugin's version (the csproj's &lt;Version&gt;, e.g. "0.3.0-beta.1").</summary>
        public static string CurrentVersion
        {
            get
            {
                var info = typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (SemVer.TryParse(info, out var v)) return v.ToString();
                var a = typeof(Updater).Assembly.GetName().Version;
                return $"{a.Major}.{a.Minor}.{a.Build}";
            }
        }

        /// <summary>The version kept for Roll back (null if none).</summary>
        public string PreviousVersion
        {
            get
            {
                try
                {
                    if (!File.Exists(OldPath)) return null;
                    var fv = FileVersionInfo.GetVersionInfo(OldPath);
                    return SemVer.TryParse(fv.ProductVersion, out var v) ? v.ToString() : fv.FileVersion;
                }
                catch { return null; }
            }
        }

        // ---------- Start-up ----------

        private class Marker { public string version; public int attempts; }

        /// <summary>
        /// First thing in Init: counts starts that didn't finish (the marker is removed by StartupSucceeded). After three
        /// failed starts of a version that came from an update, the previous DLL is put back (it loads on the next start).
        /// </summary>
        public void StartupBegin()
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                var m = File.Exists(MarkerPath) ? JsonConvert.DeserializeObject<Marker>(File.ReadAllText(MarkerPath)) : null;
                int attempts = m != null && m.version == CurrentVersion ? m.attempts + 1 : 1;
                StartupFailedBefore = attempts > 1;
                File.WriteAllText(MarkerPath, JsonConvert.SerializeObject(new Marker { version = CurrentVersion, attempts = attempts }));
                if (attempts >= 3 && File.Exists(OldPath))
                {
                    SimHub.Logging.Current.Warn($"[FXProRpmSync] v{CurrentVersion} failed to start {attempts - 1} times: rolling back to v{PreviousVersion}");
                    AutoRolledBackFrom = CurrentVersion;
                    Rollback();
                    File.Delete(MarkerPath);
                }
                else if (StartupFailedBefore)
                    SimHub.Logging.Current.Warn($"[FXProRpmSync] the last start of v{CurrentVersion} didn't finish" + (File.Exists(OldPath) ? $"; roll back to v{PreviousVersion} is offered" : ""));
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] start-up marker: " + ex.Message); }
        }

        /// <summary>Last thing in Init: the start worked. Cleans up what an update left (the .old DLL stays for Roll back).</summary>
        public void StartupSucceeded()
        {
            try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
            foreach (var f in SafeFiles(pluginDir, DllName + ".new").Concat(SafeFiles(pluginDir, DllName + ".rb")).Concat(SafeFiles(pluginDir, "User.FXProRpmSync.pdb.old")))
                try { File.Delete(f); } catch { }
            foreach (var f in SafeFiles(pluginDir, DllName + ".old.*.del")) try { File.Delete(f); } catch { }
        }

        private static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern); } catch { return new string[0]; }
        }

        // ---------- Check ----------

        /// <summary>How long SimHub can stay open before the next automatic check (a release shows without a restart).</summary>
        internal static readonly TimeSpan AutoCheckEvery = TimeSpan.FromHours(6);
        private System.Threading.Timer autoTimer;

        /// <summary>Whether an automatic check is due: always at start-up, then once the last good check is old enough.</summary>
        internal static bool CheckDue(bool autoCheck, DateTime lastCheckUtc, DateTime nowUtc, bool atStartup) =>
            autoCheck && (atStartup || nowUtc - lastCheckUtc >= AutoCheckEvery || lastCheckUtc > nowUtc);

        /// <summary>
        /// Last thing in Init: checks now (if automatic checks are on), then looks every 30 minutes whether one is due again.
        /// A failed check (offline) leaves the last good time alone, so it is tried again at the next look.
        /// </summary>
        public void CheckInBackground()
        {
            if (CheckDue(plugin.Settings.Updates.AutoCheck, plugin.Settings.Updates.LastCheckUtc, DateTime.UtcNow, true)) Task.Run(() => CheckAsync());
            autoTimer = new System.Threading.Timer(_ =>
            {
                var s = plugin.Settings.Updates;
                if (State != UpdateState.Installed && CheckDue(s.AutoCheck, s.LastCheckUtc, DateTime.UtcNow, false)) _ = CheckAsync();
            }, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }

        public void StopChecking()
        {
            try { autoTimer?.Dispose(); } catch { }
            autoTimer = null;
        }

        public async Task CheckAsync()
        {
            var s = plugin.Settings.Updates;
            lock (gate)
            {
                if (State == UpdateState.Checking || State == UpdateState.Downloading) return;
                State = UpdateState.Checking;
                Message = "Checking for updates...";
            }
            try
            {
                var release = await FindLatest(s.Repo, s.Channel).ConfigureAwait(false);
                s.LastCheckUtc = DateTime.UtcNow;
                plugin.SaveSettings();
                var current = SemVer.Parse(CurrentVersion);
                if (release == null) { State = UpdateState.UpToDate; Message = "No releases found."; return; }
                Latest = release;
                if (release.Version.CompareTo(current) <= 0)
                {
                    State = UpdateState.UpToDate;
                    Message = $"You have the latest version (v{current}).";
                    SimHub.Logging.Current.Info($"[FXProRpmSync] update check: v{current} is the latest on the {s.Channel} channel");
                    return;
                }
                LatestManifest = null;
                if (release.ManifestUrl != null)
                    try { LatestManifest = JsonConvert.DeserializeObject<UpdateManifest>(await GetString(release.ManifestUrl).ConfigureAwait(false)); }
                    catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] update manifest: " + ex.Message); }
                State = UpdateState.Available;
                Message = $"v{release.Version} is available (you have v{current}).";
                SimHub.Logging.Current.Info("[FXProRpmSync] update available: v" + release.Version);
            }
            catch (Exception ex)
            {
                State = UpdateState.Error;
                Message = "Couldn't check for updates: " + ex.Message;
                SimHub.Logging.Current.Info("[FXProRpmSync] update check failed: " + ex.Message);
            }
        }

        /// <summary>The newest release on the channel: Stable = GitHub's "latest" release, Beta = the newest release or pre-release.</summary>
        public static async Task<ReleaseInfo> FindLatest(string repo, UpdateChannel channel)
        {
            if (string.IsNullOrWhiteSpace(repo) || repo.Split('/').Length != 2) throw new Exception("the update source should be \"owner/repo\"");
            string url = $"https://api.github.com/repos/{repo.Trim()}/releases" + (channel == UpdateChannel.Stable ? "/latest" : "?per_page=20");
            string json;
            try { json = await GetString(url).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.Message.Contains("404")) { return null; }
            var releases = channel == UpdateChannel.Stable ? new List<JObject> { JObject.Parse(json) } : JArray.Parse(json).OfType<JObject>().ToList();
            return releases.Select(ParseRelease).Where(r => r != null).OrderByDescending(r => r.Version).FirstOrDefault();
        }

        public static ReleaseInfo ParseRelease(JObject r)
        {
            if ((bool?)r["draft"] == true) return null;
            if (!SemVer.TryParse((string)r["tag_name"], out var v)) return null;
            var assets = (r["assets"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var zip = assets.FirstOrDefault(a => ((string)a["name"])?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
            var manifest = assets.FirstOrDefault(a => string.Equals((string)a["name"], "manifest.json", StringComparison.OrdinalIgnoreCase));
            DateTime.TryParse((string)r["published_at"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var published);
            return new ReleaseInfo
            {
                Version = v, Tag = (string)r["tag_name"], Name = (string)r["name"], Notes = (string)r["body"] ?? "", PageUrl = (string)r["html_url"],
                Prerelease = (bool?)r["prerelease"] == true, Published = published,
                ZipUrl = (string)zip?["browser_download_url"], ZipName = (string)zip?["name"], ManifestUrl = (string)manifest?["browser_download_url"],
            };
        }

        private static HttpClient Http()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FXUnleashed/" + CurrentVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        private static async Task<string> GetString(string url)
        {
            using (var http = Http())
            using (var r = await http.GetAsync(url).ConfigureAwait(false))
            {
                if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode} {r.ReasonPhrase}");
                return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> GetBytes(string url, long max)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new Exception("downloads must use HTTPS");
            using (var http = Http())
            using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode} {r.ReasonPhrase}");
                if (r.Content.Headers.ContentLength > max) throw new Exception("the download is too big");
                var bytes = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length > max) throw new Exception("the download is too big");
                return bytes;
            }
        }

        public static string Sha256(byte[] b)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        // ---------- Install ----------

        /// <summary>
        /// Downloads and checks the release, then swaps the DLL. Returns null when done (restart SimHub to use it), else
        /// what stopped it. Nothing on disk changes unless every check passed.
        /// </summary>
        public async Task<string> InstallAsync()
        {
            var r = Latest;
            if (r == null) return "Check for updates first.";
            lock (gate)
            {
                if (State == UpdateState.Downloading) return "Already downloading.";
                State = UpdateState.Downloading;
                Message = $"Downloading v{r.Version}...";
            }
            try
            {
                if (r.ZipUrl == null || r.ManifestUrl == null) throw new Exception("this release has no zip or manifest.json; install it by hand from its page");
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(await GetString(r.ManifestUrl).ConfigureAwait(false));
                var zip = await GetBytes(r.ZipUrl, MaxZipBytes).ConfigureAwait(false);
                var dll = Verify(manifest, r.Version, zip, SimHubVersion());
                Swap(dll);
                State = UpdateState.Installed;
                Message = $"v{r.Version} is installed. Restart SimHub to use it.";
                SimHub.Logging.Current.Info($"[FXProRpmSync] installed v{r.Version} (was v{CurrentVersion}); restart SimHub to load it");
                return null;
            }
            catch (Exception ex)
            {
                State = UpdateState.Error;
                Message = "Update not installed: " + ex.Message;
                SimHub.Logging.Current.Warn("[FXProRpmSync] update failed: " + ex.Message);
                return ex.Message;
            }
        }

        /// <summary>
        /// Every check on a downloaded release, before anything is written: manifest version = release version, zip
        /// SHA-256, DLL inside with the manifest's name, size and SHA-256, the right assembly name, SimHub new enough.
        /// Returns the DLL's bytes.
        /// </summary>
        public static byte[] Verify(UpdateManifest m, SemVer releaseVersion, byte[] zip, Version simHub)
        {
            if (m?.dll == null || m.zip == null || string.IsNullOrEmpty(m.version)) throw new Exception("manifest.json is incomplete");
            if (!SemVer.TryParse(m.version, out var mv) || mv.CompareTo(releaseVersion) != 0)
                throw new Exception($"manifest.json is for v{m.version}, the release is v{releaseVersion}");
            if (!string.Equals(Sha256(zip), m.zip.sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("the zip's checksum doesn't match (damaged or tampered download)");
            if (!string.Equals(m.dll.name, DllName, StringComparison.OrdinalIgnoreCase)) throw new Exception($"the release carries {m.dll.name}, not {DllName}");
            byte[] dll;
            using (var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var entry = z.Entries.FirstOrDefault(e => string.Equals(e.Name, DllName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new Exception(DllName + " isn't in the zip");
                if (entry.Length > 50 * 1024 * 1024) throw new Exception("the DLL is too big");
                using (var s = entry.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); dll = ms.ToArray(); }
            }
            if (dll.Length != m.dll.size) throw new Exception("the DLL's size doesn't match the manifest");
            if (!string.Equals(Sha256(dll), m.dll.sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("the DLL's checksum doesn't match the manifest");
            if (dll.Length < 2 || dll[0] != 'M' || dll[1] != 'Z') throw new Exception("the DLL isn't a Windows library");
            if (!string.IsNullOrEmpty(m.minSimHub) && simHub != null && Version.TryParse(m.minSimHub, out var min) && simHub < min)
                throw new Exception($"it needs SimHub {m.minSimHub} or newer (you have {simHub}); update SimHub first");
            return dll;
        }

        /// <summary>
        /// SimHub's own version (null if unknown, e.g. in the tests). SimHub sets it on SimHub.Plugins.Configuration at start-up
        /// (the "Starting SimHub v9.12.8" of its log); the exe's file version is 1.0.0.0, so that can't be used. When it can't be
        /// read the check is skipped: a minimum SimHub version is advice, never a reason to refuse on a guess.
        /// </summary>
        public static Version SimHubVersion()
        {
            try
            {
                return FromSimHub(SimHub.Plugins.Configuration.SimHubVersion);
            }
            catch { }
            return null;
        }

        internal static Version FromSimHub(SimHub.Plugins.VersionParser v) =>
            v != null && v.Major > 0 ? new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Revision)) : null;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string path);

        /// <summary>
        /// Puts the new DLL in place: written next to it as .new (mark of the web removed: .NET refuses to load a
        /// "downloaded" DLL, silently), its assembly name checked, the running DLL renamed to .old, the new one renamed in.
        /// Any failure puts the old one back. A folder that needs admin rights gets the same moves through one elevated
        /// command (a Windows prompt).
        /// </summary>
        public void Swap(byte[] dll)
        {
            string target = Target, newPath = target + ".new", old = OldPath;
            try
            {
                File.WriteAllBytes(newPath, dll);
            }
            catch (UnauthorizedAccessException)
            {
                SwapElevated(dll);
                return;
            }
            DeleteFileW(newPath + ":Zone.Identifier");
            try
            {
                var name = AssemblyName.GetAssemblyName(newPath); // reads the metadata only, runs nothing
                if (name.Name != Path.GetFileNameWithoutExtension(DllName)) throw new Exception($"the new DLL is {name.Name}, not {Path.GetFileNameWithoutExtension(DllName)}");
            }
            catch { try { File.Delete(newPath); } catch { } throw; }

            if (File.Exists(old))
            {
                try { File.Delete(old); }
                catch { File.Move(old, old + "." + DateTime.UtcNow.Ticks + ".del"); } // somehow in use: out of the way
            }
            bool moved = false;
            try
            {
                if (File.Exists(target)) { File.Move(target, old); moved = true; }
                File.Move(newPath, target);
            }
            catch
            {
                if (moved && !File.Exists(target)) try { File.Move(old, target); } catch { }
                throw;
            }
            // the old symbols don't match the new DLL
            string pdb = Path.Combine(pluginDir, "User.FXProRpmSync.pdb");
            try { if (File.Exists(pdb)) { if (File.Exists(pdb + ".old")) File.Delete(pdb + ".old"); File.Move(pdb, pdb + ".old"); } } catch { }
        }

        private void SwapElevated(byte[] dll)
        {
            Directory.CreateDirectory(dataDir);
            string staged = Path.Combine(dataDir, "update-" + DllName);
            File.WriteAllBytes(staged, dll);
            DeleteFileW(staged + ":Zone.Identifier");
            var name = AssemblyName.GetAssemblyName(staged);
            if (name.Name != Path.GetFileNameWithoutExtension(DllName)) throw new Exception("the new DLL has the wrong name");
            string target = Target, old = OldPath;
            string args = $"/c del /f /q \"{old}\" 2>nul & move /y \"{target}\" \"{old}\" && copy /y \"{staged}\" \"{target}\"";
            var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null || !p.WaitForExit(30000)) throw new Exception("the elevated copy didn't finish");
            if (!File.Exists(target) || Sha256(File.ReadAllBytes(target)) != Sha256(dll)) throw new Exception("the elevated copy didn't replace the DLL (SimHub's folder needs admin rights)");
            try { File.Delete(staged); } catch { }
        }

        /// <summary>Swaps the current DLL and the previous one (.old). Restart SimHub to use it.</summary>
        public void Rollback()
        {
            string target = Target, old = OldPath, tmp = target + ".rb";
            if (!File.Exists(old)) throw new Exception("there's no previous version to go back to");
            if (File.Exists(tmp)) File.Delete(tmp);
            File.Move(target, tmp);
            try { File.Move(old, target); }
            catch { File.Move(tmp, target); throw; }
            File.Move(tmp, old);
            State = UpdateState.Installed;
            Message = "The previous version is back. Restart SimHub to use it.";
            SimHub.Logging.Current.Info("[FXProRpmSync] rolled back to the previous version; restart SimHub");
        }

        /// <summary>Restarts SimHub (it asks the user to confirm nothing; the plugin saves its settings in End).</summary>
        public void RestartSimHub()
        {
            try { plugin.PluginManager?.RequestApplicationExit(true); }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] restart: " + ex.Message); }
        }
    }
}
