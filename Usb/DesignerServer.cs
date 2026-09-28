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
        /// <summary>Current SimHub values (null when no game), for rendering with live data.</summary>
        DashValues LiveValues();
        void DashesChanged();
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
            public string Q(string k, string d = null) => Query.TryGetValue(k, out var v) ? v : d;
            public int QI(string k, int d) => int.TryParse(Q(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : d;
            public double QD(string k, double d) => double.TryParse(Q(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
        }

        private sealed class Response
        {
            public int Status = 200;
            public string Type = "application/json; charset=utf-8";
            public byte[] Body = new byte[0];
        }

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
                    try { res = Route(req); }
                    catch (Exception ex) { res = Json(new { error = ex.Message }, 400); }
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
            foreach (var l in lines)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out length);
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
            string reason = r.Status == 200 ? "OK" : r.Status == 404 ? "Not Found" : r.Status == 204 ? "No Content" : "Error";
            var head = $"HTTP/1.1 {r.Status} {reason}\r\nContent-Type: {r.Type}\r\nContent-Length: {r.Body.Length}\r\n" +
                       "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n" +
                       "Access-Control-Allow-Headers: Content-Type\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
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
            ("POST", "/api/import", "body = {name|path, screen?, images?, colors?, maxSeconds?}: convert a SimHub dash -> {dash, report, check}"),
            ("GET", "/api/wheel", "wheel status (plugin only)"),
            ("POST", "/api/wheel/show[?left=L&top=T]", "body = dash: show it on the wheel now (plugin only)"),
            ("POST", "/api/wheel/stop", "back to the normal dash (plugin only)"),
        };

        private Response Route(Request r)
        {
            if (r.Method == "OPTIONS") return new Response { Status = 204 };
            var path = r.Path.TrimEnd('/');
            if (path == "" || path == "/index.html") return Asset("index.html");
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
            return Json(new { error = "no such endpoint; GET /api lists them" }, 404);
        }

        private static Response Asset(string name)
        {
            var resource = "User.FXProRpmSync.Designer." + name.Replace('/', '.');
            using (var st = typeof(DesignerServer).Assembly.GetManifestResourceStream(resource))
            {
                if (st == null) return Json(new { error = "not found: " + name }, 404);
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    string type = ext == ".html" ? "text/html; charset=utf-8" : ext == ".js" ? "text/javascript; charset=utf-8"
                                : ext == ".css" ? "text/css; charset=utf-8" : ext == ".png" ? "image/png" : "application/octet-stream";
                    return new Response { Type = type, Body = ms.ToArray() };
                }
            }
        }
    }
}
