using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>What the plugin adds to the designer server: the wheel and live data. Null in fxdash.</summary>
    public interface IDesignerHost
    {
        object WheelStatus();
        /// <summary>Shows a dash on the wheel (live data while a game runs, else the demo lap) until Stop or ~60 s idle.</summary>
        void ShowOnWheel(DashDefinition dash, int left, int top);
        void StopWheelPreview();
        /// <summary>Shows these LED colours (38, "#RRGGBB", null/"" = off) for `seconds`, over whatever the lights show.</summary>
        void TestLeds(string[] colours, int brightness, double seconds);
        /// <summary>The demo lap on the wheel with a dash ("c:id" / "w:page" / a plugin dash id); null stops it.</summary>
        void Demo(string dash);
        /// <summary>Current SimHub values (null when no game), for rendering with live data.</summary>
        DashValues LiveValues();
        void DashesChanged();
        /// <summary>Library items installed (id, kind, version).</summary>
        object LibraryInstalled();
        /// <summary>Installs a library item by id after asking the user in the plugin; the item comes from the library itself.</summary>
        object LibraryInstall(string kind, string id);
        /// <summary>Screen RAM drive: status; "waits" (arm/packet/done ms) or "clear" (developer hooks for tuning).</summary>
        object Ram(string op, int? arm, int? packet, int? done);
        /// <summary>SimHub properties by name, as SimHub has them now (TimeSpans in seconds).</summary>
        object Props(string[] names);
        /// <summary>A SimHub formula evaluated now with SimHub's own engine: {value, type} or {error}.</summary>
        object Eval(string formula);
    }

    /// <summary>
    /// The dash designer's local web server: the designer page (Usb/Designer/*, embedded) and a JSON API for it and
    /// for AI agents. Bound to 127.0.0.1 only. A plain TcpListener (HttpListener can need admin rights for a port).
    /// API reference: GET /api, and docs/dash-designer.md.
    /// </summary>
    public sealed class DesignerServer : IDisposable
    {
        public const int DefaultPort = 8899;
        private readonly int port;
        private readonly IDesignerHost host;
        private TcpListener listener;
        private Thread thread;
        private volatile bool stop;

        public int Port => port;
        public bool Running => listener != null;
        public string Url => $"http://127.0.0.1:{port}/";
        public string Error { get; private set; }

        public DesignerServer(int port, IDesignerHost host) { this.port = port; this.host = host; }

        public void Start()
        {
            if (listener != null) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Error = null;
            }
            catch (Exception ex) { listener = null; Error = ex.Message; throw; }
            stop = false;
            thread = new Thread(Accept) { IsBackground = true, Name = "FX Pro dash designer" };
            thread.Start();
        }

        private void Accept()
        {
            while (!stop)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { if (stop) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        public void Dispose()
        {
            stop = true;
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        // ---------- HTTP ----------

        private sealed class Request
        {
            public string Method, Path;
            public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body = "";
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Origin => Headers.TryGetValue("Origin", out var o) ? o : null;
            public string Q(string k, string d = null) => Query.TryGetValue(k, out var v) ? v : d;
            public int QI(string k, int d) => int.TryParse(Q(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : d;
            public double QD(string k, double d) => double.TryParse(Q(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
        }

        private sealed class Response
        {
            public int Status = 200;
            public string Type = "application/json; charset=utf-8";
            public byte[] Body = new byte[0];
            /// <summary>The page allowed to read this answer (CORS), or null.</summary>
            public string AllowOrigin;
            public bool PrivateNetwork;
        }

        /// <summary>
        /// Sites allowed to call the library routes from a browser (the website's "Install" button). Everything else that
        /// comes with an Origin header must come from this server's own pages: a page on another site can't make the
        /// plugin do anything (a plain form POST needs no CORS preflight, so the Origin check is what stops it).
        /// </summary>
        public static readonly List<string> LibraryOrigins = new List<string> { "https://fxunleashed.com", "https://www.fxunleashed.com" };

        private bool LocalOrigin(string origin) =>
            origin == $"http://127.0.0.1:{port}" || origin == $"http://localhost:{port}";

        private static bool LibraryRoute(string path) => path.StartsWith("/api/library/", StringComparison.Ordinal);

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    var stream = client.GetStream();
                    var req = Read(stream);
                    if (req == null) return;
                    Response res;
                    string origin = req.Origin;
                    bool siteAllowed = origin != null && LibraryRoute(req.Path) && LibraryOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
                    if (origin != null && !LocalOrigin(origin) && !siteAllowed)
                        res = Json(new { error = "requests from other websites aren't accepted" }, 403);
                    else if (req.Method == "OPTIONS") res = new Response { Status = 204 };
                    else
                    {
                        try { res = Route(req); }
                        catch (Exception ex) { res = Json(new { error = ex.Message }, 400); }
                    }
                    if (origin != null && res.Status != 403) res.AllowOrigin = origin;
                    res.PrivateNetwork = siteAllowed && req.Headers.ContainsKey("Access-Control-Request-Private-Network");
                    Write(stream, res);
                }
                catch { }
            }
        }

        private static Request Read(NetworkStream s)
        {
            var head = new StringBuilder();
            var buf = new List<byte>();
            // headers
            int prev3 = 0;
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) return null;
                buf.Add((byte)b);
                prev3 = (prev3 << 8 | b) & 0x7FFFFFFF;
                if (buf.Count >= 4 && buf[buf.Count - 4] == 13 && buf[buf.Count - 3] == 10 && buf[buf.Count - 2] == 13 && buf[buf.Count - 1] == 10) break;
                if (buf.Count > 65536) return null;
            }
            var lines = Encoding.ASCII.GetString(buf.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var req = new Request { Method = first[0].ToUpperInvariant() };
            var target = first[1];
            int q = target.IndexOf('?');
            req.Path = Uri.UnescapeDataString(q < 0 ? target : target.Substring(0, q));
            if (q >= 0)
                foreach (var kv in target.Substring(q + 1).Split('&').Where(x => x.Length > 0))
                {
                    int e = kv.IndexOf('=');
                    req.Query[Uri.UnescapeDataString(e < 0 ? kv : kv.Substring(0, e))] = e < 0 ? "" : Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
                }
            int length = 0;
            foreach (var l in lines.Skip(1))
            {
                int c = l.IndexOf(':');
                if (c > 0) req.Headers[l.Substring(0, c).Trim()] = l.Substring(c + 1).Trim();
            }
            if (req.Headers.TryGetValue("Content-Length", out var cl)) int.TryParse(cl, out length);
            if (length > 0)
            {
                if (length > 64 * 1024 * 1024) return null;
                var body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = s.Read(body, read, length - read);
                    if (n <= 0) break;
                    read += n;
                }
                req.Body = Encoding.UTF8.GetString(body, 0, read);
            }
            return req;
        }

        private static void Write(NetworkStream s, Response r)
        {
            string reason = r.Status == 200 ? "OK" : r.Status == 404 ? "Not Found" : r.Status == 204 ? "No Content" : r.Status == 403 ? "Forbidden" : "Error";
            var head = $"HTTP/1.1 {r.Status} {reason}\r\nContent-Type: {r.Type}\r\nContent-Length: {r.Body.Length}\r\n" +
                       (r.AllowOrigin == null ? "" :
                           $"Access-Control-Allow-Origin: {r.AllowOrigin}\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n" +
                           "Access-Control-Allow-Headers: Content-Type\r\n") +
                       (r.PrivateNetwork ? "Access-Control-Allow-Private-Network: true\r\n" : "") +
                       "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
            var h = Encoding.ASCII.GetBytes(head);
            s.Write(h, 0, h.Length);
            s.Write(r.Body, 0, r.Body.Length);
        }

        private static Response Json(object o, int status = 200) =>
            new Response { Status = status, Body = Encoding.UTF8.GetBytes(o is string str ? str : JsonConvert.SerializeObject(o, DashTools.Json)) };

        // ---------- Routes ----------

        private static readonly (string Method, string Path, string What)[] Endpoints =
        {
            ("GET", "/api", "this list"),
            ("GET", "/api/schema", "the dash format: element types, fields, formats, limits"),
            ("GET", "/api/bindings", "data keys a dash can bind to"),
            ("GET", "/api/fonts?sample=TEXT", "screen fonts (id, height, characters, sample width)"),
            ("GET", "/api/fonts/metrics", "every font's height and ASCII advance widths (for measuring text yourself)"),
            ("GET", "/api/fonts/suggest?w=W&h=H&text=TEXT[&height=PX]", "best font for a box"),
            ("GET", "/api/dashes", "dashes in the library (built-in and saved)"),
            ("GET", "/api/dashes/{id}", "one dash (JSON)"),
            ("PUT", "/api/dashes/{id}", "save the body as a dash (Id taken from the path)"),
            ("DELETE", "/api/dashes/{id}", "delete a saved dash"),
            ("POST", "/api/check[?left=L&top=T]", "body = dash: layout problems and draw cost"),
            ("POST", "/api/render[?mode=preview|demo|live&seconds=N&left=L&top=T]", "body = dash: PNG as the wheel shows it"),
            ("GET", "/api/simhub", "installed SimHub dashes"),
            ("GET", "/api/simhub/screens?name=NAME", "screens of a SimHub dash"),
            ("POST", "/api/import", "body = {name|path, screen?, images?, colors?, maxSeconds?, fitWidth?, fitHeight?}: convert a SimHub dash -> {dash, report, check}"),
            ("GET", "/api/wheel", "wheel status (plugin only)"),
            ("POST", "/api/wheel/show[?left=L&top=T]", "body = dash: show it on the wheel now (plugin only)"),
            ("POST", "/api/verify[?seconds=N&left=L&top=T]", "body = dash: demo lap on a simulated wheel: traffic, flashes, drawing errors"),
            ("POST", "/api/wheel/stop", "back to the normal dash (plugin only)"),
            ("GET", "/api/props?names=A,B,...", "SimHub properties now, e.g. DataCorePlugin.GameData.NewData.Sector1Time (TimeSpans in seconds; plugin only)"),
            ("GET", "/api/eval?f=ncalc:...", "a SimHub formula evaluated now with SimHub's own engine: {value, type} or {error} (plugin only)"),
            ("GET", "/api/wheel/ram", "the screen's RAM drive: files, bytes, upload waits (plugin only)"),
            ("POST", "/api/wheel/ram/waits?arm=MS&packet=MS&done=MS", "upload waits for this session, for tuning (plugin only)"),
            ("POST", "/api/wheel/ram/clear", "delete the plugin's files from the screen's RAM; the dash loads again (plugin only)"),
            ("POST", "/api/wheel/demo?dash=c:ID|w:PAGE|rotation|off", "the demo lap on the wheel with that dash, e.g. w:12 for the wheel's own dash page 12; rotation = the default rotation, cycled with the dash button (plugin only)"),
            ("GET", "/api/library/status", "plugin version (for the website's Install button)"),
            ("GET", "/api/library/installed", "library items installed: [{id, kind, version}]"),
            ("POST", "/api/library/install?kind=dash|saver&id=ID", "install a library item by id (asks the user in the plugin first; the plugin downloads it from the library itself)"),
            ("GET", "/mirror[?bg=transparent&leds=0&all=1&fps=N&label=0]", "screen mirror page for OBS: the wheel's screen and lights (plugin only)"),
            ("GET", "/api/wheel/frame.png", "what the plugin last drew on the wheel's screen (204 while the wheel shows its own screen)"),
            ("GET", "/api/wheel/mirror", "mirror state: held, dark, wheelDash, version, 38 LEDs {c, b}"),
            ("POST", "/api/wheel/leds[?brightness=1-90&seconds=N]", "body = 38 LED colours [\"#RRGGBB\" or null], in the wheel's LED order: shown for N s (plugin only)"),
        };

        private Response Route(Request r)
        {
            if (r.Method == "OPTIONS") return new Response { Status = 204 };
            var path = r.Path.TrimEnd('/');
            if (path == "" || path == "/index.html") return Asset("index.html");
            if (path == "/mirror") return Asset("mirror.html");
            if (path == "/api/library/status") return Json(new { plugin = "FX Unleashed", version = Updater.CurrentVersion, available = host != null });
            if (path == "/api/library/installed") return Json(host?.LibraryInstalled() ?? new object[0]);
            if (path == "/api/library/install")
            {
                if (r.Method != "POST") return Json(new { error = "POST it" }, 400);
                if (host == null) return Json(new { error = "installing needs the plugin (SimHub)" }, 400);
                return Json(host.LibraryInstall(r.Q("kind", "dash"), r.Q("id", "")));
            }
            if (path == "/api/wheel/mirror") return Json(ScreenMirror.State());
            if (path == "/api/wheel/frame.png")
            {
                var png = ScreenMirror.Png();
                return png == null ? new Response { Status = 204 } : new Response { Type = "image/png", Body = png };
            }
            if (!path.StartsWith("/api")) return Asset(path.TrimStart('/'));

            if (path == "/api") return Json(new
            {
                name = "FX Pro dash designer API",
                docs = "docs/dash-designer.md and docs/dash-format.md in the FXPro RPM Sync repo",
                host = host == null ? "fxdash (no wheel; SimHub formulas not evaluated)" : "SimHub plugin",
                endpoints = Endpoints.Select(e => new { method = e.Method, path = e.Path, what = e.What }),
            });
            if (path == "/api/schema") return Json(DashTools.Schema());
            if (path == "/api/bindings") return Json(DashTools.Bindings());
            if (path == "/api/fonts") return Json(DashTools.Fonts(r.Q("sample")));
            if (path == "/api/fonts/metrics") return Json(new { heights = FontMetrics.Fonts.Select(f => f[0]), widths = FontMetrics.Fonts.Select(f => f.Skip(1)), first = 32 });
            if (path == "/api/fonts/suggest") return Json(DashTools.SuggestFont(r.QI("w", 100), r.QI("h", 30), r.Q("text", "0"), r.QD("height", 0)));

            if (path == "/api/dashes" && r.Method == "GET") return Json(DashTools.Dashes());
            if (path.StartsWith("/api/dashes/"))
            {
                var id = path.Substring("/api/dashes/".Length);
                if (r.Method == "GET")
                {
                    var d = DashTools.Find(id);
                    return d == null ? Json(new { error = "no dash " + id }, 404) : Json(DashTools.Serialize(d));
                }
                if (r.Method == "PUT" || r.Method == "POST")
                {
                    var d = DashTools.Parse(r.Body);
                    d.Id = id;
                    var file = DashTools.Save(d);
                    host?.DashesChanged();
                    return Json(new { saved = file, check = DashTools.Check(d) });
                }
                if (r.Method == "DELETE")
                {
                    var d = DashTools.Find(id);
                    if (d == null || d.BuiltIn || d.FilePath == null) return Json(new { error = "not a saved dash: " + id }, 404);
                    File.Delete(d.FilePath);
                    host?.DashesChanged();
                    return Json(new { deleted = d.FilePath });
                }
            }
            if (path == "/api/check") return Json(DashTools.Check(DashTools.Parse(r.Body), r.QI("left", 0), r.QI("top", 0)));
            if (path == "/api/verify") return Json(DashVerify.Run(DashTools.Parse(r.Body), r.QI("left", 10), r.QI("top", 20), r.QD("seconds", 60)));
            if (path == "/api/render")
            {
                var mode = r.Q("mode", "preview");
                var live = mode == "live" ? host?.LiveValues() : null;
                if (mode == "live" && live == null) mode = "demo";
                var png = DashTools.Render(DashTools.Parse(r.Body), mode, r.QD("seconds", 20), r.QI("left", 0), r.QI("top", 0), live);
                return new Response { Type = "image/png", Body = png };
            }
            if (path == "/api/simhub") return Json(DashTools.SimHubDashes());
            if (path == "/api/simhub/screens") return Json(SimHubImport.ScreenNames(DashTools.ResolveSimHub(r.Q("name", ""))));
            if (path == "/api/import")
            {
                var o = string.IsNullOrWhiteSpace(r.Body) ? new JObject() : JObject.Parse(r.Body);
                var opt = new ImportOptions
                {
                    Screen = (string)o["screen"],
                    Images = (bool?)o["images"] ?? true,
                    ImageColors = (int?)o["colors"] ?? 6,
                    MaxDrawSeconds = (double?)o["maxSeconds"] ?? 8,
                    FitWidth = (int?)o["fitWidth"] ?? DashRenderer.Width,
                    FitHeight = (int?)o["fitHeight"] ?? DashRenderer.Height,
                };
                var (dash, report) = DashTools.Import((string)o["path"] ?? (string)o["name"] ?? throw new Exception("name or path needed"), opt);
                return Json(new { dash, report, check = DashTools.Check(dash) });
            }
            if (path == "/api/wheel") return Json(host?.WheelStatus() ?? new { available = false, why = "not running inside SimHub" });
            if (path == "/api/wheel/show")
            {
                if (host == null) return Json(new { error = "the wheel is only available when the designer runs in SimHub" }, 400);
                host.ShowOnWheel(DashTools.Parse(r.Body), r.QI("left", 10), r.QI("top", 20));
                return Json(new { shown = true, status = host.WheelStatus() });
            }
            if (path == "/api/wheel/stop") { host?.StopWheelPreview(); return Json(new { stopped = true }); }
            if (path == "/api/eval")
            {
                if (host == null) return Json(new { error = "formulas are only available when the designer runs in SimHub" }, 400);
                return Json(host.Eval(r.Q("f") ?? ""));
            }
            if (path == "/api/props")
            {
                if (host == null) return Json(new { error = "SimHub's properties are only available when the designer runs in SimHub" }, 400);
                var names = (r.Q("names") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToArray();
                return Json(host.Props(names));
            }
            if (path == "/api/wheel/ram" || path == "/api/wheel/ram/waits" || path == "/api/wheel/ram/clear")
            {
                if (host == null) return Json(new { error = "the wheel is only available when the designer runs in SimHub" }, 400);
                int? Q(string k) => r.Q(k) == null ? (int?)null : r.QI(k, 0);
                string op = path.EndsWith("/waits") ? "waits" : path.EndsWith("/clear") ? "clear" : null;
                return Json(host.Ram(op, Q("arm"), Q("packet"), Q("done")));
            }
            if (path == "/api/wheel/demo")
            {
                if (host == null) return Json(new { error = "the wheel is only available when the designer runs in SimHub" }, 400);
                var dash = r.Q("dash");
                WheelTelemetry.TestFlag = r.Q("flag") == null ? (int?)null : r.QI("flag", 0);
                host.Demo(string.IsNullOrEmpty(dash) || dash == "off" ? null : dash);
                return Json(new { demo = dash, status = host.WheelStatus() });
            }
            if (path == "/api/wheel/leds")
            {
                if (host == null) return Json(new { error = "the wheel is only available when the designer runs in SimHub" }, 400);
                var colours = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(string.IsNullOrWhiteSpace(r.Body) ? "[]" : r.Body);
                host.TestLeds(colours, r.QI("brightness", 60), r.QD("seconds", 5));
                return Json(new { shown = colours.Length });
            }
            return Json(new { error = "no such endpoint; GET /api lists them" }, 404);
        }

        private static Response Asset(string name)
        {
            var resource = "User.FXProRpmSync.Designer." + name.Replace('/', '.');
            // Development: FXDASH_DESIGNER_DIR serves the page's files from disk (edit, reload, no rebuild)
            var dir = Environment.GetEnvironmentVariable("FXDASH_DESIGNER_DIR");
            var onDisk = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
            using (var st = onDisk != null && File.Exists(onDisk) ? File.OpenRead(onDisk)
                          : typeof(DesignerServer).Assembly.GetManifestResourceStream(resource))
            {
                if (st == null) return Json(new { error = "not found: " + name }, 404);
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    string type = ext == ".html" ? "text/html; charset=utf-8" : ext == ".js" ? "text/javascript; charset=utf-8"
                                : ext == ".css" ? "text/css; charset=utf-8" : ext == ".png" ? "image/png" : ext == ".svg" ? "image/svg+xml" : "application/octet-stream";
                    return new Response { Type = type, Body = ms.ToArray() };
                }
            }
        }
    }
}
