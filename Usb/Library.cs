using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>One item in the online library (meta.json, and its line in index.json).</summary>
    public class LibraryItem
    {
        public string Id;
        /// <summary>"dash" or "saver".</summary>
        public string Kind = "dash";
        public string Name, Author, Description;
        /// <summary>SemVer of the item itself ("1.0.0"); a higher one shows "Update available".</summary>
        public string Version = "1.0.0";
        public List<string> Games = new List<string>(), Cars = new List<string>(), Tags = new List<string>();
        /// <summary>SPDX id or text, e.g. "CC-BY-4.0".</summary>
        public string License;
        /// <summary>Where it came from if converted (then the author's permission is needed, see TERMS).</summary>
        public string Source;
        public DateTime Created, Updated;
        public int FormatVersion = DashDefinition.CurrentFormat;
        public string MinPlugin;
        /// <summary>Measured by the plugin when packaged: first draw and a demo lap.</summary>
        public int BytesStatic, BytesPerSecond;
        /// <summary>sha256 (hex, lower case) of dash.json.</summary>
        public string Sha256;
        /// <summary>index.json only: paths relative to the library's base URL.</summary>
        public string DashUrl, PreviewUrl;
    }

    public class LibraryIndex
    {
        public const int CurrentSchema = 1;
        public int Schema = CurrentSchema;
        public DateTime Generated;
        public List<LibraryItem> Items = new List<LibraryItem>();
    }

    /// <summary>What's installed from the library (kept in the settings).</summary>
    public class LibraryInstall
    {
        public string Id, Kind, Version, Sha256;
        /// <summary>The file written (a dash in DashLibrary.Folder or a saver in IdleScreens.Folder).</summary>
        public string File;
        public DateTime When;
    }

    /// <summary>
    /// The online library of dashes and screensavers (NEXT.md C): a public repo (fx-unleashed-library) with
    /// dashes/&lt;id&gt;/{dash.json, meta.json, preview.png}, savers/&lt;id&gt;/..., and a generated index.json.
    /// Installing writes the dash into the dashes folder (or a screensaver into the savers folder) and reloads: no
    /// plugin update, no restart. Every download is checked against the index's sha256 and against the same safety
    /// rules the library's CI applies (Check): no scripts, no newer format, size caps.
    /// The base URL can be a local folder or file:// (testing, offline use).
    /// </summary>
    public sealed class LibraryClient
    {
        public const string DefaultBaseUrl = "https://raw.githubusercontent.com/fxunleashed/fx-unleashed-library/main/";
        public const int MaxDashBytes = 1024 * 1024, MaxPreviewBytes = 512 * 1024;
        /// <summary>The first plugin version with the library: the default minPlugin of a packaged item.</summary>
        public const string FirstLibraryVersion = "0.3.0-beta.1";

        /// <summary>The item needs a newer plugin than this one (its minPlugin).</summary>
        public static bool NeedsNewerPlugin(LibraryItem item) =>
            SemVer.TryParse(item.MinPlugin, out var min) && SemVer.TryParse(Updater.CurrentVersion, out var cur) && min.CompareTo(cur) > 0;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly string baseUrl;
        public static string CacheFolder => Path.Combine(DashLibrary.SimHubFolder, "PluginsData", "Common", "FXProRpmSync", "LibraryCache");

        public LibraryClient(string baseUrl)
        {
            this.baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            if (!this.baseUrl.EndsWith("/") && !this.baseUrl.EndsWith("\\")) this.baseUrl += "/";
        }

        /// <summary>index.json from the library (cached copy when offline). Refuses a newer schema.</summary>
        public LibraryIndex GetIndex(bool allowCache = true)
        {
            string cache = Path.Combine(CacheFolder, "index.json");
            byte[] data;
            try
            {
                data = Get("index.json", 4 * 1024 * 1024);
                Directory.CreateDirectory(CacheFolder);
                File.WriteAllBytes(cache, data);
            }
            catch when (allowCache && File.Exists(cache)) { data = File.ReadAllBytes(cache); }
            var index = JsonConvert.DeserializeObject<LibraryIndex>(Encoding.UTF8.GetString(data));
            if (index == null) throw new Exception("the library's index is empty");
            if (index.Schema > LibraryIndex.CurrentSchema) throw new Exception("the library needs a newer version of the plugin: update the plugin");
            index.Items = index.Items?.Where(i => i != null && ValidId(i.Id)).ToList() ?? new List<LibraryItem>();
            return index;
        }

        /// <summary>The preview picture (cached per item version).</summary>
        public byte[] GetPreview(LibraryItem item)
        {
            var file = Path.Combine(CacheFolder, "previews", $"{item.Kind}-{item.Id}-{item.Version}.png");
            if (File.Exists(file)) return File.ReadAllBytes(file);
            var png = Get(item.PreviewUrl ?? $"{Folder(item)}/{item.Id}/preview.png", MaxPreviewBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, png);
            return png;
        }

        /// <summary>Downloads an item's dash.json and checks its hash and content. Throws with a reason if it's refused.</summary>
        public DashDefinition Download(LibraryItem item)
        {
            if (!ValidId(item.Id)) throw new Exception("bad item id");
            var data = Get(item.DashUrl ?? $"{Folder(item)}/{item.Id}/dash.json", MaxDashBytes);
            if (!string.Equals(Sha256(data), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("the download doesn't match the library's checksum");
            var d = JsonConvert.DeserializeObject<DashDefinition>(Encoding.UTF8.GetString(data));
            if (NeedsNewerPlugin(item)) throw new Exception($"needs plugin v{item.MinPlugin} or newer: update the plugin");
            var problems = Check(d, data.Length);
            if (problems.Count > 0) throw new Exception(string.Join("; ", problems));
            return d;
        }

        private static string Folder(LibraryItem item) => item.Kind == "saver" ? "savers" : "dashes";

        /// <summary>Library ids: lower case letters, digits and dashes (they become file names).</summary>
        public static bool ValidId(string id) => id != null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{1,63}$");

        /// <summary>
        /// The library's rules for a dash (the same as tools/validate.py in the library repo): nothing that runs code,
        /// nothing newer than this plugin reads, a sane size. Returns the problems (empty = fine).
        /// </summary>
        public static List<string> Check(DashDefinition d, long bytes)
        {
            var p = new List<string>();
            if (d?.Elements == null || d.Elements.Count == 0) { p.Add("not a dash (no elements)"); return p; }
            if (d.FormatVersion > DashDefinition.CurrentFormat) p.Add($"made for a newer plugin (dash format {d.FormatVersion}): update the plugin");
            if (!string.IsNullOrEmpty(d.ScriptsFolder)) p.Add("uses a scripts folder (JavaScript): not allowed in the library");
            if (d.Bindings.Any(b => b.TrimStart().StartsWith("js:", StringComparison.OrdinalIgnoreCase)))
                p.Add("uses js: formulas (JavaScript): not allowed in the library");
            if (bytes > MaxDashBytes) p.Add($"too big ({bytes / 1024} KB, max {MaxDashBytes / 1024} KB)");
            return p;
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        /// <summary>Any file of the library (e.g. cars/index.json), at most max bytes.</summary>
        public byte[] GetFile(string relative, int max) => Get(relative, max);

        private byte[] Get(string relative, int max)
        {
            if (relative.Contains("..")) throw new Exception("bad path");
            if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using (var resp = Http.GetAsync(new Uri(new Uri(baseUrl), relative), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    if (!resp.IsSuccessStatusCode) throw new Exception($"{relative}: HTTP {(int)resp.StatusCode}");
                    if (resp.Content.Headers.ContentLength > max) throw new Exception($"{relative} is too big");
                    var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    if (bytes.Length > max) throw new Exception($"{relative} is too big");
                    return bytes;
                }
            }
            string root = baseUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(baseUrl).LocalPath : baseUrl;
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new Exception("bad path");
            if (new FileInfo(path).Length > max) throw new Exception($"{relative} is too big");
            return File.ReadAllBytes(path);
        }
    }

    /// <summary>Installing, updating and removing library items in the plugin's folders and settings.</summary>
    public static class LibraryInstaller
    {
        /// <summary>Writes the item; returns the file. Dashes keep their library id as dash Id ("lib-&lt;id&gt;").</summary>
        public static LibraryInstall Install(UsbSettings s, LibraryItem item, DashDefinition d)
        {
            d.Id = "lib-" + item.Id;
            d.Name = string.IsNullOrWhiteSpace(item.Name) ? d.Name : item.Name;
            d.Author = item.Author ?? d.Author;
            d.ScriptsFolder = null;
            d.FormatVersion = DashDefinition.CurrentFormat;
            string file;
            if (item.Kind == "saver")
            {
                Directory.CreateDirectory(IdleScreens.Folder);
                file = Path.Combine(IdleScreens.Folder, d.Id + ".json");
                File.WriteAllText(file, DashTools.Serialize(d));
                s.Savers = s.Savers ?? new List<SaverItem>();
                s.Savers.RemoveAll(x => x.Id == d.Id);
                s.Savers.Add(new SaverItem { Id = d.Id, Name = d.Name, Kind = SaverKind.Image, File = file });
            }
            else file = DashTools.Save(d);
            var rec = new LibraryInstall { Id = item.Id, Kind = item.Kind, Version = item.Version, Sha256 = item.Sha256, File = file, When = DateTime.UtcNow };
            s.LibraryInstalled = s.LibraryInstalled ?? new List<LibraryInstall>();
            s.LibraryInstalled.RemoveAll(x => x.Id == item.Id && x.Kind == item.Kind);
            s.LibraryInstalled.Add(rec);
            return rec;
        }

        public static void Remove(UsbSettings s, string kind, string id)
        {
            var rec = s.LibraryInstalled?.FirstOrDefault(x => x.Id == id && x.Kind == kind);
            if (rec == null) return;
            try { if (rec.File != null && File.Exists(rec.File)) File.Delete(rec.File); } catch { }
            if (kind == "saver")
            {
                s.Savers?.RemoveAll(x => x.Id == "lib-" + id);
                if (s.SaverId == "lib-" + id) s.SaverId = SaverItem.LogoId;
            }
            s.LibraryInstalled.Remove(rec);
        }

        public static LibraryInstall Installed(UsbSettings s, LibraryItem item) =>
            s.LibraryInstalled?.FirstOrDefault(x => x.Id == item.Id && x.Kind == item.Kind && x.File != null && File.Exists(x.File));

        public static bool UpdateAvailable(UsbSettings s, LibraryItem item)
        {
            var rec = Installed(s, item);
            if (rec == null) return false;
            if (SemVer.TryParse(item.Version, out var a) && SemVer.TryParse(rec.Version, out var b)) return a.CompareTo(b) > 0;
            return !string.Equals(rec.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// "Package for the library": a folder dashes/&lt;id&gt;/ (or savers/) with dash.json, meta.json (measured budget)
        /// and preview.png (rendered as the wheel shows it), ready for a pull request to the library repo.
        /// </summary>
        public static string Package(DashDefinition source, LibraryItem meta, string outRoot)
        {
            if (!LibraryClient.ValidId(meta.Id)) throw new Exception("the id must be lower case letters, digits and dashes (2-64)");
            var d = source.Clone();
            d.Id = meta.Id; d.Name = meta.Name ?? d.Name; d.Author = meta.Author ?? d.Author; d.Description = meta.Description ?? d.Description;
            d.FormatVersion = DashDefinition.CurrentFormat;
            var json = Encoding.UTF8.GetBytes(DashTools.Serialize(d));
            var problems = LibraryClient.Check(d, json.Length);
            if (problems.Count > 0) throw new Exception(string.Join("; ", problems));
            var dir = Path.Combine(outRoot, meta.Kind == "saver" ? "savers" : "dashes", meta.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "dash.json"), json);
            // dashes: a moment of the demo lap; screensavers show their preview texts (the demo lap has no clock)
            File.WriteAllBytes(Path.Combine(dir, "preview.png"), DashTools.Render(d, meta.Kind == "saver" ? "preview" : "demo", 20, 0, 0));
            var check = DashTools.Check(d);
            var verify = DashVerify.Run(d, 0, 0, 60);
            meta.FormatVersion = d.FormatVersion;
            meta.Sha256 = LibraryClient.Sha256(json);
            meta.BytesStatic = check.Cost?.StaticBytes ?? 0;
            meta.BytesPerSecond = verify.AvgBytesPerSecond;
            meta.MinPlugin = meta.MinPlugin ?? LibraryClient.FirstLibraryVersion;
            if (meta.Created == default) meta.Created = DateTime.UtcNow.Date;
            meta.Updated = DateTime.UtcNow.Date;
            meta.DashUrl = meta.PreviewUrl = null;
            File.WriteAllText(Path.Combine(dir, "meta.json"), JsonConvert.SerializeObject(meta, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DateFormatString = "yyyy-MM-dd" }));
            return dir;
        }
    }
}
