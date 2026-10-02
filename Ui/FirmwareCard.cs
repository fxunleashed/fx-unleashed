using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro's firmware tools on the Wheel tab: the screen's picture memory (RAM drive) on and off by a header-only
    /// upload (ScreenFlasher), screen recovery (a screen without pages after an upload: find its speed, put the stock
    /// header back; FXProDashes docs/screen-images.md, the 2026-09-30 runbook), and the wheel app's update mode for a
    /// SimPro reinstall (FXProDashes tools/usb/boot-mode.ps1).
    /// </summary>
    internal sealed class FirmwareCard : ContentControl
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly TextBlock memState, progressText, simproState, recoverSpeedText;
        private readonly ProgressBar progress;
        private readonly CheckBox ack, declared;
        private readonly Button turnOn, turnOff, recover, updateMode;
        private readonly Button[] pings;
        private readonly StackPanel answer;
        private ManualResetEvent answered;
        private volatile bool busy;
        private int recoverSpeed = 9600;

        private const string SimProExe = @"C:\Program Files (x86)\SIMAGIC\Simpro3\bin\simpro3.exe";
        private static readonly string SimProWheelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"SIMAGIC\Simpro3\firmware\wheel\fx_pro\FXPro_App-V1.3.11.0-00000000.sfu");
        private const string StockWheelSha = "16dd09cf2e76d6ee34e2c16d7aa2b6c2f3ae407a2b909298fee98a46248eb6cf";

        private static string AckText => Legal.FirmwareWarning + "\n\nThe screen's picture memory: the plugin rewrites only the first " +
            "128 KB of the screen's image (its header), never the rest. The screen checks its whole image afterwards and shows " +
            "\"Update Successed\" or \"Update Failed\". After a failed one the screen shows no dash until the right header is " +
            "put back (Screen recovery, below). Turning it off puts Simagic's header back byte for byte.";

        public FirmwareCard(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var body = new StackPanel();
            body.Children.Add(Theme.Eyebrow("Firmware", Theme.Red));
            body.Children.Add(Theme.Title("Screen memory and recovery", 18));

            // ----- the RAM drive -----
            body.Children.Add(Theme.Note("Picture memory lets the plugin keep dash backgrounds and pictures in the screen itself and swap them " +
                "instantly, instead of drawing them piece by piece. Turning it on or off takes about 10 seconds plus the screen's own check. " +
                "Wheel on the base, USB plugged in, no game running.", new Thickness(0, 6, 0, 8)));
            memState = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(memState);
            declared = Theme.Switch("My screen's picture memory is already on", S.ScreenRamDeclared, v =>
            {
                S.ScreenRamDeclared = v;
                plugin.SaveSettings();
                Refresh();
            }, "Tick this if you flashed the screen before (with the PC tool or an earlier version): this PC has no record of it, so the plugin would take the screen as stock.");
            body.Children.Add(declared);
            body.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "Before you start: the risks", Foreground = Theme.Text2 },
                Content = Legal.Block(AckText),
                Margin = new Thickness(0, 0, 0, 6),
            });
            ack = Theme.Switch("I've read the risks and want to go ahead", S.ScreenFirmwareAck == Legal.Hash(AckText), v =>
            {
                S.ScreenFirmwareAck = v ? Legal.Hash(AckText) : null;
                plugin.SaveSettings();
                Refresh();
            });
            body.Children.Add(ack);
            var memButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            turnOn = Theme.Btn("Turn picture memory on", () => Flash("ramfs", 512000), primary: true);
            turnOff = Theme.Btn("Turn it off (Simagic's header)", () => Flash("stock", 512000));
            memButtons.Children.Add(turnOn);
            memButtons.Children.Add(turnOff);
            body.Children.Add(memButtons);

            progressText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2, Margin = new Thickness(0, 10, 0, 4) };
            body.Children.Add(progressText);
            progress = new ProgressBar { Height = 6, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(progress);

            answer = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 8) };
            answer.Children.Add(new TextBlock { Text = "What does the wheel's screen show?", Foreground = Theme.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var answers = new WrapPanel();
            answers.Children.Add(Theme.Btn("\"Update Successed\", or the dash again", () => Answer("ok"), primary: true));
            answers.Children.Add(Theme.Btn("\"Update Failed\", \"Data ERROR\" or white", () => Answer("failed")));
            answer.Children.Add(answers);
            body.Children.Add(answer);

            // ----- screen recovery -----
            var rec = new StackPanel();
            rec.Children.Add(Theme.Note("For a screen that stays white, shows \"Data ERROR!\" or no dash after an upload, while the wheel itself " +
                "(buttons, lights) works. It puts Simagic's header back.", new Thickness(0, 4, 0, 8)));
            rec.Children.Add(Step("1", "Switch the base off and unplug the wheel's USB for a few seconds, then start it again (base on, USB in)."));
            rec.Children.Add(Step("2", "Find the speed the screen listens at: try each. The right one turns the screen red and dips its backlight for 4 seconds."));
            var pingRow = new WrapPanel { Margin = new Thickness(36, 0, 0, 8) };
            pings = ScreenFlasher.Speeds.Select(b => Theme.Btn("Try " + b, () => Ping(b))).ToArray();
            foreach (var p in pings) pingRow.Children.Add(p);
            rec.Children.Add(pingRow);
            rec.Children.Add(Step("3", "Pick the speed that turned it red, then put the header back. A screen without its dash usually listens at 9600."));
            var pick = Theme.Segmented(ScreenFlasher.Speeds.Select(b => b.ToString()).ToArray(), Array.IndexOf(ScreenFlasher.Speeds, recoverSpeed),
                i => { recoverSpeed = ScreenFlasher.Speeds[i]; Refresh(); }, out _);
            pick.Margin = new Thickness(36, 0, 0, 8);
            rec.Children.Add(pick);
            recoverSpeedText = Theme.Note("", new Thickness(36, 0, 0, 6));
            rec.Children.Add(recoverSpeedText);
            var recRow = new WrapPanel { Margin = new Thickness(36, 0, 0, 8) };
            recover = Theme.Btn("Put Simagic's header back", () => Flash("stock", recoverSpeed));
            recRow.Children.Add(recover);
            rec.Children.Add(recRow);
            rec.Children.Add(Step("4", "After \"Update Successed\", switch the base off and unplug USB once more, then start again: the dash is back."));
            body.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "Screen recovery", FontFamily = Theme.Display, FontSize = 15 },
                Content = rec, Margin = new Thickness(0, 10, 0, 0),
            });

            // ----- the wheel app: update mode for a SimPro reinstall -----
            var wh = new StackPanel();
            wh.Children.Add(Theme.Note("For a wheel whose firmware misbehaves, or to go back to Simagic's stock firmware: SimPro reinstalls the wheel " +
                "app. If SimPro can't start the install (it sits at 0% or doesn't find the wheel), put the wheel in update mode first.", new Thickness(0, 4, 0, 8)));
            simproState = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) };
            wh.Children.Add(simproState);
            wh.Children.Add(Step("1", "Check the line above: SimPro must have Simagic's own 1.3.11 file, or it reinstalls whatever is there."));
            wh.Children.Add(Step("2", "Put the wheel in update mode. It restarts into its updater and stays there, even after a power cycle, until SimPro finishes an install. Its screen and lights stop until then; the base keeps working."));
            var whRow = new WrapPanel { Margin = new Thickness(36, 0, 0, 8) };
            updateMode = Theme.Btn("Put the wheel in update mode", EnterUpdateMode);
            whRow.Children.Add(updateMode);
            whRow.Children.Add(Theme.Btn("Open SimPro", () => { try { Process.Start(SimProExe); } catch (Exception e) { MessageBox.Show("Couldn't start SimPro: " + e.Message); } }));
            wh.Children.Add(whRow);
            wh.Children.Add(Step("3", "In SimPro (started directly, as this button does), open the wheel's firmware page and reinstall 1.3.11. Don't unplug or switch off until it's done."));
            wh.Children.Add(Step("4", "If it sits at 0%, close SimPro completely and open it again with the wheel already in update mode, then reinstall."));
            body.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "Wheel firmware recovery", FontFamily = Theme.Display, FontSize = 15 },
                Content = wh, Margin = new Thickness(0, 6, 0, 0),
            });

            Content = Theme.CardBox(body);
            Refresh();
        }

        private static FrameworkElement Step(string n, string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var num = new TextBlock { Text = n + ".", Width = 36, Foreground = Theme.Red, FontFamily = Theme.Display, FontWeight = FontWeights.Bold };
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2 });
            return row;
        }

        /// <summary>Called by the Wheel tab's refresh timer.</summary>
        public void Refresh()
        {
            var u = Usb;
            var img = ScreenFlasher.Choose(u, S, out var refusal);
            bool? on = ScreenFlasher.RamDriveOn(S);
            var last = S.ScreenFlashes?.LastOrDefault();
            declared.Visibility = last == null ? Visibility.Visible : Visibility.Collapsed; // once the plugin has flashed, its records say
            memState.Text = (on == true ? "Picture memory is on" + (last == null ? " (you said it's already flashed)" : "") : on == false ? "Picture memory is off (Simagic's header)"
                    : last.Result == "failed" ? "The last upload failed: use Screen recovery below"
                    : "The last upload wasn't confirmed: answer below, or use Screen recovery if the screen has no dash")
                + (last != null ? $"  ·  last change {last.When:d MMM HH:mm}" : "")
                + (refusal != null ? "\n" + refusal : img != null ? $"\nScreen image: {img.Id}" : "");
            bool can = !busy && img != null && ack.IsChecked == true;
            turnOn.IsEnabled = can && on != true;
            turnOff.IsEnabled = can && on != false;
            recover.IsEnabled = can;
            foreach (var p in pings) p.IsEnabled = !busy && img != null;
            recoverSpeedText.Text = recoverSpeed == 9600 ? "At 9600 the plugin wakes the screen's updater first (com_star), as in the runbook; the data then goes at full speed."
                : recoverSpeed == 115200 ? "At 115200 the upload is slower (about 20 s)." : "The normal speed: for a screen whose dash still works.";

            string simpro = SimProFileState();
            simproState.Text = (u?.WheelFound == true && u.InUpdateMode ? "The wheel is in update mode now: reinstall 1.3.11 in SimPro.\n" : "") + simpro;
            updateMode.IsEnabled = !busy && u?.WheelFound == true && !u.InUpdateMode;
        }

        private DateTime checkedStamp;
        private string checkedState;

        /// <summary>Whether SimPro's 1.3.11 wheel file is Simagic's (hashed again only when the file changes).</summary>
        private string SimProFileState()
        {
            try
            {
                if (!File.Exists(SimProWheelFile)) return "SimPro's wheel file wasn't found (is SimPro 3 installed?).";
                var stamp = File.GetLastWriteTimeUtc(SimProWheelFile);
                if (stamp == checkedStamp && checkedState != null) return checkedState;
                bool stock;
                using (var sha = SHA256.Create()) using (var f = File.OpenRead(SimProWheelFile))
                    stock = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant() == StockWheelSha;
                checkedStamp = stamp;
                return checkedState = stock ? "✓ SimPro has Simagic's stock 1.3.11 wheel file."
                    : "✗ SimPro's 1.3.11 wheel file isn't Simagic's stock file: a reinstall would install that file.";
            }
            catch (Exception e) { return "Couldn't check SimPro's wheel file: " + e.Message; }
        }

        private bool Confirm(string text) =>
            MessageBox.Show(text, "FX Pro", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

        /// <summary>Runs `work` with the wheel lent by USB mode, off the UI thread.</summary>
        private void Run(string reason, Action<string> work)
        {
            if (busy || Usb == null) return;
            busy = true;
            Refresh();
            Task.Run(() =>
            {
                string error = null;
                try
                {
                    using (var lend = Usb.Lend(reason))
                    {
                        if (lend == null) throw new Exception("USB mode didn't let go of the wheel.");
                        var path = FxUsb.FindPath() ?? throw new Exception("The wheel isn't connected over USB.");
                        work(path);
                    }
                }
                catch (Exception e) { error = e.Message; SimHub.Logging.Current.Error("[FXProRpmSync] firmware tool: " + e); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    if (error != null) Show("Stopped: " + error + " Leave the wheel as it is; a power cycle and Screen recovery put the screen right.", null);
                    Refresh();
                }));
            });
        }

        private void Show(string text, double? fraction)
        {
            progressText.Text = text;
            progress.Visibility = fraction.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (fraction.HasValue) progress.Value = fraction.Value;
        }

        private void Ping(int baud)
        {
            Show($"Trying {baud}: watch the screen for red and a dip in brightness (4 s).", null);
            Run("Testing the screen's speed", path =>
            {
                ScreenFlasher.Ping(path, baud);
                Dispatcher.BeginInvoke(new Action(() => Show($"Tried {baud}. Red = that's its speed (pick it in step 3). Nothing = try the next.", null)));
            });
        }

        private void Flash(string mode, int screenAt)
        {
            var img = ScreenFlasher.Choose(Usb, S, out var refusal);
            if (img == null) { Show(refusal, null); return; }
            byte[] block;
            try { block = img.Build(mode); }
            catch (Exception e) { Show("Not sent: " + e.Message, null); return; }
            string what = mode == "ramfs" ? "turn the screen's picture memory on" : "put Simagic's header back on the screen";
            if (!Confirm($"This will {what} (screen image {img.Id}).\n\nWheel on the base, USB plugged in, no game running. " +
                         "Don't unplug or switch off until the screen shows its result. Go ahead?")) return;
            ScreenFlasher.Record(S, img, mode, "sent");
            plugin.SaveSettings();
            answered = new ManualResetEvent(false);
            var hold = answered;
            answer.Visibility = Visibility.Visible;
            Run("Updating the screen", path => ScreenFlasher.Upload(path, block, screenAt,
                (t, f) => Dispatcher.BeginInvoke(new Action(() => Show(t, f))), hold));
        }

        private void Answer(string result)
        {
            var last = S.ScreenFlashes?.LastOrDefault();
            if (last != null && last.Result == "sent") { last.Result = result; plugin.SaveSettings(); }
            answered?.Set();
            answer.Visibility = Visibility.Collapsed;
            Show(result == "ok"
                ? "Done. Switch the base off and unplug the wheel's USB for a few seconds, then start again."
                : "The upload didn't take, and the screen has no dash until Simagic's header is back. Open Screen recovery below and follow the steps.", null);
            Refresh();
        }

        private void EnterUpdateMode()
        {
            if (!Confirm("The wheel restarts into its updater and stays there (even after a power cycle) until SimPro finishes an install. " +
                         "Its screen and lights stop until then; the base keeps working.\n\nThen: open SimPro and reinstall wheel app 1.3.11. Go ahead?")) return;
            Show("Putting the wheel in update mode...", null);
            Run("Putting the wheel in update mode", path =>
            {
                string err = FxUsb.EnterBootloader(path);
                if (err != null) throw new Exception(err);
                Thread.Sleep(3000);
                Dispatcher.BeginInvoke(new Action(() => Show("Sent. When the wheel is back (a few seconds), open SimPro and reinstall 1.3.11.", null)));
            });
        }
    }
}
