using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using User.FXProRpmSync;

/// <summary>
/// fxdash: design FX Pro dashes from the command line (for people and AI agents). Output is JSON on stdout (except
/// files written); exit code 0 = ok, 1 = the dash has errors, 2 = usage or failure. See docs/dash-designer.md.
/// </summary>
internal static class FxDash
{
    private const string Usage = @"fxdash - FX Pro dash designer, command line

  fxdash schema                              the dash format: element types, fields, formats, limits (JSON)
  fxdash bindings                            data keys a dash can bind to
  fxdash fonts [--sample TEXT]               screen fonts: id, height, characters, width of TEXT
  fxdash suggest-font W H TEXT [--height PX] best font for a W x H box showing TEXT
  fxdash check DASH.json [--pad L,T]         layout problems and draw cost (exit 1 if errors)
  fxdash render DASH.json OUT.png [--mode preview|demo] [--seconds N] [--pad L,T]
                                             picture of the dash as the wheel shows it
  fxdash verify DASH.json [--seconds N] [--pad L,T]
                                             the demo lap on a simulated wheel: USB traffic, flashes, drawing
                                             errors, top senders (exit 1 if not ok; default pad 10,20, 120 s)
  fxdash tune DASH.json [OUT.json]           automatic fixes for an import: samples from a demo lap, fonts, labels,
                                             gear font, value boxes overlapping text
  fxdash fit-bands DASH.json [OUT.json]      values whose text crosses a border line: nudged or a smaller font
  fxdash builtin [ID] [OUT.json]             list built-in dashes, or write one out as a starting point
  fxdash simhub                              installed SimHub dashes
  fxdash simhub-screens NAME|PATH            screens of a SimHub dash
  fxdash import NAME|PATH OUT.json [--screen NAME|INDEX] [--no-images] [--colors N] [--max-seconds S] [--fit W,H] [--png OUT.png]
                                             (--fit: scale into W x H, e.g. 790,460 to leave the wheel's padding)
                                             convert a SimHub dash (report on stdout)
  fxdash serve [--port 8899]                 run the designer in the browser (no wheel; SimHub formulas not evaluated)
  fxdash package DASH.json LIBRARY_DIR --id ID --author NAME --license SPDX [--kind dash|saver] [--name N]
                 [--description D] [--games a,b] [--cars a,b] [--tags a,b] [--version 1.0.0] [--min-plugin V] [--source S (--permission URL | --maintained)]
                                             a library item: <dir>/dashes/<id>/{dash.json, meta.json, preview.png}
                                             (as the plugin's Package for the library; refuses js:/scripts) and <dir>/<id>.fxdash.zip, the
                                             file the library's Submit a dash form takes

  Options: --simhub DIR (SimHub's folder, default: SIMHUB_INSTALL_PATH or C:\Program Files (x86)\SimHub)";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false); // JSON out is UTF-8 whatever the console's code page
        try
        {
            var opts = Options(args, out var pos);
            DashLibrary.Root = opts.TryGetValue("simhub", out var sh) ? sh
                : Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub";
            string cmd = pos.Count > 0 ? pos[0] : "help";
            (int L, int T) pad = opts.TryGetValue("pad", out var p) ? ParsePad(p) : (0, 0);
            switch (cmd)
            {
                case "schema": return Out(DashTools.Schema());
                case "bindings": return Out(DashTools.Bindings());
                case "fonts": return Out(DashTools.Fonts(opts.TryGetValue("sample", out var smp) ? smp : null));
                case "suggest-font":
                    Need(pos, 4);
                    return Out(DashTools.SuggestFont(int.Parse(pos[1]), int.Parse(pos[2]), pos[3], opts.TryGetValue("height", out var hh) ? double.Parse(hh) : 0));
                case "check":
                {
                    Need(pos, 2);
                    var result = DashTools.Check(Load(pos[1]), pad.L, pad.T);
                    Out(result);
                    return result.Ok ? 0 : 1;
                }
                case "render":
                {
                    Need(pos, 3);
                    var mode = opts.TryGetValue("mode", out var m) ? m : "preview";
                    double seconds = opts.TryGetValue("seconds", out var sec) ? double.Parse(sec) : 20;
                    File.WriteAllBytes(pos[2], DashTools.Render(Load(pos[1]), mode, seconds, pad.L, pad.T, null, opts.ContainsKey("tiles")));
                    return Out(new { written = Path.GetFullPath(pos[2]), mode });
                }
                case "verify":
                {
                    // the demo lap on a simulated wheel: traffic, flashes, drawing errors; exit 1 when not ok
                    Need(pos, 2);
                    double vsec = opts.TryGetValue("seconds", out var vs) ? double.Parse(vs, System.Globalization.CultureInfo.InvariantCulture) : 120;
                    var vpad = opts.ContainsKey("pad") ? pad : (10, 20);
                    var vr = DashVerify.Run(Load(pos[1]), vpad.Item1, vpad.Item2, vsec, tiles: opts.ContainsKey("tiles"));
                    Out(vr);
                    return vr.Ok ? 0 : 1;
                }
                case "tune":
                {
                    // automatic fixes for an import: samples from a demo lap, fonts, labels, gear, overlapping boxes
                    Need(pos, 2);
                    var dash = Load(pos[1]);
                    var changes = DashTune.Run(dash);
                    string outPath = pos.Count > 2 ? pos[2] : pos[1];
                    File.WriteAllText(outPath, DashTools.Serialize(dash));
                    return Out(new { changes, written = Path.GetFullPath(outPath) });
                }
                case "fit-bands":
                {
                    // values whose text crosses a border line get a font that fits between the lines (no flashing)
                    Need(pos, 2);
                    var dash = Load(pos[1]);
                    var changes = DashTools.FitTextBands(dash);
                    string outPath = pos.Count > 2 ? pos[2] : pos[1];
                    if (changes.Count > 0) File.WriteAllText(outPath, DashTools.Serialize(dash));
                    return Out(new { changes, written = changes.Count > 0 ? Path.GetFullPath(outPath) : null });
                }
                case "builtin":
                    if (pos.Count < 2) return Out(BuiltInDashes.All().Select(d => new { id = d.Id, name = d.Name }));
                    var b = BuiltInDashes.All().FirstOrDefault(d => d.Id == pos[1]) ?? throw new Exception("no built-in dash " + pos[1]);
                    var bjson = DashTools.Serialize(b);
                    if (pos.Count > 2) { File.WriteAllText(pos[2], bjson); return Out(new { written = Path.GetFullPath(pos[2]) }); }
                    Console.WriteLine(bjson);
                    return 0;
                case "simhub": return Out(DashTools.SimHubDashes());
                case "simhub-screens": Need(pos, 2); return Out(SimHubImport.ScreenNames(DashTools.ResolveSimHub(pos[1])));
                case "import":
                {
                    Need(pos, 3);
                    var io = new ImportOptions
                    {
                        Screen = opts.TryGetValue("screen", out var scr) ? scr : null,
                        Images = !opts.ContainsKey("no-images"),
                        ImageColors = opts.TryGetValue("colors", out var col) ? int.Parse(col) : 6,
                        MaxDrawSeconds = opts.TryGetValue("max-seconds", out var ms) ? double.Parse(ms) : 8,
                    };
                    if (opts.TryGetValue("fit", out var fit)) { var (fw, fh) = ParsePad(fit); io.FitWidth = fw; io.FitHeight = fh; }
                    var (dash, report) = DashTools.Import(pos[1], io);
                    File.WriteAllText(pos[2], DashTools.Serialize(dash));
                    if (opts.TryGetValue("png", out var png)) File.WriteAllBytes(png, DashTools.Render(dash));
                    return Out(new { written = Path.GetFullPath(pos[2]), report, check = DashTools.Check(dash) });
                }
                case "serve":
                {
                    int port = opts.TryGetValue("port", out var po) ? int.Parse(po) : DesignerServer.DefaultPort;
                    using (var server = new DesignerServer(port, null))
                    {
                        server.Start();
                        Console.Error.WriteLine($"designer on http://127.0.0.1:{port}/  (Ctrl+C to stop)");
                        System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                    }
                    return 0;
                }
                case "package":
                {
                    Need(pos, 3);
                    string O(string k) => opts.TryGetValue(k, out var v) ? v : null;
                    List<string> L(string k) => (O(k) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    var dash = Load(pos[1]);
                    var meta = new LibraryItem
                    {
                        Id = O("id") ?? throw new Exception("--id needed"), Kind = O("kind") ?? "dash",
                        Name = O("name") ?? dash.Name, Author = O("author") ?? dash.Author ?? throw new Exception("--author needed"),
                        Description = O("description") ?? dash.Description, License = O("license") ?? throw new Exception("--license needed"),
                        Games = L("games"), Cars = L("cars"), Tags = L("tags"), Version = O("version") ?? "1.0.0", Source = O("source"), Permission = O("permission"), MinPlugin = O("min-plugin"),
                    };
                    // --maintained: converted work the maintainers add themselves (listed in the library's maintained.json instead of carrying a Permission link)
                    if (meta.Source != null && meta.Permission == null && !opts.ContainsKey("maintained")) throw new Exception("--source needs --permission: where the original's author agreed (the library refuses converted work without it)");
                    var dir = LibraryInstaller.Package(dash, meta, pos[2]);
                    var zip = LibraryInstaller.ZipPackage(dir);
                    return Out(new { written = Path.GetFullPath(dir), zip = Path.GetFullPath(zip), meta = JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(dir, "meta.json"))) });
                }
                default:
                    Console.WriteLine(Usage);
                    return cmd == "help" ? 0 : 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fxdash: " + ex.Message);
            return 2;
        }
    }

    private static DashDefinition Load(string path) => DashTools.Parse(File.ReadAllText(path));

    private static int Out(object o)
    {
        Console.WriteLine(JsonConvert.SerializeObject(o, DashTools.Json));
        return 0;
    }

    private static void Need(List<string> pos, int n) { if (pos.Count < n) throw new Exception("missing arguments; see fxdash help"); }

    private static (int, int) ParsePad(string s)
    {
        var parts = s.Split(',');
        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 0);
    }

    private static Dictionary<string, string> Options(string[] args, out List<string> positional)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i].Substring(2);
                bool flag = key == "no-images" || key == "tiles" || key == "maintained";
                opts[key] = flag || i + 1 >= args.Length ? "" : args[++i];
            }
            else positional.Add(args[i]);
        }
        return opts;
    }
}
