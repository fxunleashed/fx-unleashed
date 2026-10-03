using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>The designer server's view of the plugin: the wheel (through USB mode) and SimHub's live values.</summary>
    internal sealed class DesignerHost : IDesignerHost
    {
        private readonly FXProRpmSyncPlugin plugin;

        public DesignerHost(FXProRpmSyncPlugin plugin) { this.plugin = plugin; }

        private UsbController Usb => plugin.Usb;

        public object WheelStatus()
        {
            var u = Usb;
            var s = plugin.Settings.Usb;
            string why = !s.Enabled ? "USB mode is off (SimHub > FX Unleashed > Unleashed mode)"
                : !(u?.FirmwarePatched ?? s.FirmwareConfirmed) ? "the wheel's firmware isn't confirmed in USB mode's settings"
                : u == null || !u.WheelFound ? "the wheel isn't connected by USB"
                : !u.SupportedApp ? "the wheel's firmware isn't supported"
                : null;
            return new
            {
                available = why == null,
                why,
                state = u?.State,
                detail = u?.Detail,
                wheelVersion = u?.WheelVersion,
                previewing = u?.PreviewActive == true,
                gameRunning = u?.LiveNow != null,
                // the last LED frame sent, in the wheel's LED order (0-11 buttons, 12-16 encoders, 17-22 side, 23-37 rev)
                leds = u?.LastFrame?.Select(f => $"#{f.R:X2}{f.G:X2}{f.B:X2}/{f.Brightness}").ToArray(),
                lights = u?.LightsState,
                wheelPage = u?.WheelPage,
                wheelButtons = plugin.Buttons?.Found == true ? "found" : "not found",
            };
        }

        public void ShowOnWheel(DashDefinition dash, int left, int top)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            u.SetPreviewDash(dash, left, top);
        }

        public void StopWheelPreview() => Usb?.StopPreview();

        private readonly SimHubFormulas formulas = new SimHubFormulas();

        public object Eval(string formula) { lock (formulas) return formulas.Test(formula); }

        public object Props(string[] names)
        {
            var pm = plugin.PluginManager ?? throw new Exception("SimHub isn't ready");
            var result = new Dictionary<string, object>();
            foreach (var n in names.Take(64))
            {
                object v;
                try { v = pm.GetPropertyValue(n); } catch (Exception ex) { v = "error: " + ex.Message; }
                result[n] = v is TimeSpan ts ? ts.TotalSeconds : v;
            }
            result["_utc"] = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            return result;
        }

        public object Ram(string op, int? arm, int? packet, int? done, int? budgetKb = null, int? overhead = null)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            if (op == "waits") u.RamWaits(arm, packet, done);
            if (op == "budget") u.RamBudget(budgetKb, overhead);
            if (op == "clear") u.RamClear();
            return u.RamInfo();
        }

        public void Demo(string dash)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            if (dash == null) { u.SetDemo(false); return; }
            if (dash == "rotation") { u.SetDemo(true, null); return; } // the car's / default rotation (dash button cycles)
            u.SetDemo(true, dash.StartsWith("c:") ? dash.Substring(2) : dash);
        }

        public object Scenario(string id, bool stop)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            if (stop) u.StopScenario();
            else if (!string.IsNullOrEmpty(id) && !u.PlayScenario(id)) throw new Exception("no such scenario: " + id);
            return new
            {
                playing = u.ScenarioNow?.Id, note = u.ScenarioNote, seconds = Math.Round(u.ScenarioElapsed, 1),
                scenarios = LightScenarios.All.Select(s => new { s.Id, s.Title, s.Seconds, s.Expect }).ToList(),
            };
        }

        public void TestLeds(string[] colours, int brightness, double seconds)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            var frame = new LedColor[plugin.ActiveModel.LedCount];
            byte b = (byte)Math.Max(1, Math.Min(90, brightness));
            for (int i = 0; i < frame.Length && i < (colours?.Length ?? 0); i++)
            {
                if (string.IsNullOrEmpty(colours[i])) continue;
                var c = DashColors.Parse(colours[i], System.Drawing.Color.Black);
                frame[i] = new LedColor(c.R, c.G, c.B, b);
            }
            u.TestLeds(frame, seconds);
        }

        public DashValues LiveValues() => Usb?.LiveNow;

        public void DashesChanged() => Usb?.ReloadDashes();

        public object LibraryInstalled() =>
            (plugin.Settings.Usb.LibraryInstalled ?? new List<LibraryInstall>())
                .Where(x => x.File != null && System.IO.File.Exists(x.File)).Select(x => new { id = x.Id, kind = x.Kind, version = x.Version }).ToList();

        /// <summary>
        /// The website's Install button: the item is looked up by id in the library's own index (never a URL from the
        /// request), the user confirms in a dialog (with the library terms), then it's downloaded, checked and installed.
        /// </summary>
        public object LibraryInstall(string kind, string id)
        {
            kind = kind == "saver" ? "saver" : "dash";
            if (!LibraryClient.ValidId(id)) return new { installed = false, error = "bad id" };
            var s = plugin.Settings.Usb;
            var client = new LibraryClient(s.LibraryUrl);
            var item = client.GetIndex(allowCache: false).Items.FirstOrDefault(i => i.Id == id && i.Kind == kind);
            if (item == null) return new { installed = false, error = "not in the library" };
            var app = System.Windows.Application.Current;
            if (app == null) return new { installed = false, error = "no settings window to ask in" };
            bool ok = false;
            app.Dispatcher.Invoke(() =>
            {
                string terms = s.LibraryTermsAccepted == Legal.Hash(Legal.LibraryTerms) ? "" : "\n\n" + Legal.Unwrap(Legal.LibraryTerms);
                ok = System.Windows.MessageBox.Show($"The FX Unleashed website asks to install the {(kind == "saver" ? "screensaver" : "dash")} \"{item.Name}\" by {item.Author} " +
                                                    $"(v{item.Version}) from the library.{terms}\n\nInstall it?", "FX Unleashed library",
                                                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
                                                    System.Windows.MessageBoxResult.No, System.Windows.MessageBoxOptions.DefaultDesktopOnly) == System.Windows.MessageBoxResult.Yes;
            });
            if (!ok) return new { installed = false, error = "declined in the plugin" };
            s.LibraryTermsAccepted = Legal.Hash(Legal.LibraryTerms);
            var d = client.Download(item);
            LibraryInstaller.Install(s, item, d);
            plugin.SaveSettings();
            Usb?.ReloadDashes();
            Usb?.SettingsChanged();
            SimHub.Logging.Current.Info($"[FXProRpmSync] library: installed {kind} {id} v{item.Version} from the website");
            return new { installed = true, id, kind, version = item.Version, name = item.Name };
        }
    }
}
