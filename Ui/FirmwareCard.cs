using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro's firmware tools on the Wheel tab: the screen's picture memory (RAM drive) on and off by a header-only
    /// upload (ScreenFlasher), as one guided flow: send (~10 s), the user watches the screen's own check and says what it
    /// showed, power-cycles the wheel (the plugin notices: UsbController.ExpectPowerCycle), then the safe screen check
    /// (ScreenCheckPanel) confirms the result and sets the picture-memory switch. Also screen recovery (a screen without
    /// pages after an upload: find its speed, put the stock header back; FXProDashes docs/screen-images.md, the 2026-09-30
    /// runbook), and the wheel app's update mode for a SimPro reinstall (FXProDashes tools/usb/boot-mode.ps1).
    /// </summary>
    internal sealed class FirmwareCard : ContentControl
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly TextBlock memState, simproState, recoverSpeedText;
        private readonly CheckBox ack;
        private readonly Button turnOn, turnOff, recover, updateMode;
        private readonly Button[] pings;
        private readonly Expander recovery;
        private volatile bool busy;
        private int recoverSpeed = 9600;

        // the guided flow
        private enum Step { None, Sending, Watching, PowerCycle, Checking, Done, Failed, Message }
        private Step step = Step.None;
        private string flowMode;                 // "ramfs" or "stock"
        private ManualResetEvent answered;
        private volatile bool answeredOk;
        private DateTime backAt;
        private bool keptPowerShown;
        private readonly Border stepBox;
        private readonly TextBlock stepNo, stepTitle, stepText, stepStatus;
        private readonly ProgressBar progress;
        private readonly WrapPanel stepButtons;
        private readonly ScreenCheckPanel afterCheck;

        /// <summary>Written to the wheel's marker word after a confirmed upload; only a power loss clears it (UsbController.ExpectPowerCycle).</summary>
        private const int PowerToken = 0x5A;

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
                "instantly, instead of drawing them piece by piece. Turning it on or off takes about a minute: 10 seconds to send, the screen's " +
                "own check, a power cycle of the wheel, then a quick check of the screen. Wheel on the base, USB plugged in, no game running. " +
                "Not sure whether yours already has it? Use \"Check my screen\" in the card above.", new Thickness(0, 6, 0, 8)));
            memState = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(memState);
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

            // ----- the step box: one step at a time, big and plain -----
            stepNo = new TextBlock { FontFamily = Theme.Display, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Red };
            stepTitle = new TextBlock { FontFamily = Theme.Display, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            stepText = new TextBlock { FontSize = 14.5, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 21 };
            stepStatus = new TextBlock { FontSize = 13, Foreground = Theme.Text2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            progress = new ProgressBar { Height = 8, Maximum = 1, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            stepButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            afterCheck = new ScreenCheckPanel(plugin, button: false);
            afterCheck.Answered += CheckAnswered;
            var stepInner = new StackPanel();
            foreach (UIElement e in new UIElement[] { stepNo, stepTitle, stepText, progress, stepStatus, stepButtons, afterCheck }) stepInner.Children.Add(e);
            stepBox = new Border
            {
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 16, 20, 18),
                Margin = new Thickness(0, 14, 0, 4), Child = stepInner, Visibility = Visibility.Collapsed,
            };
            body.Children.Add(stepBox);

            // ----- screen recovery -----
            var rec = new StackPanel();
            rec.Children.Add(Theme.Note("For a screen that stays white, shows \"Data ERROR!\" or no dash after an upload, while the wheel itself " +
                "(buttons, lights) works. It puts Simagic's header back.", new Thickness(0, 4, 0, 8)));
            rec.Children.Add(StepLine("1", "Switch the base off and unplug the wheel's USB for a few seconds, then start it again (base on, USB in)."));
            rec.Children.Add(StepLine("2", "Find the speed the screen listens at: try each. The right one turns the screen red and dips its backlight for 4 seconds."));
            var pingRow = new WrapPanel { Margin = new Thickness(36, 0, 0, 8) };
            pings = ScreenFlasher.Speeds.Select(b => Theme.Btn("Try " + b, () => Ping(b))).ToArray();
            foreach (var p in pings) pingRow.Children.Add(p);
            rec.Children.Add(pingRow);
            rec.Children.Add(StepLine("3", "Pick the speed that turned it red, then put the header back. A screen without its dash usually listens at 9600."));
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
            rec.Children.Add(StepLine("4", "The steps above then guide you: watch the screen, power-cycle the wheel, check the screen."));
            recovery = new Expander
            {
                Header = new TextBlock { Text = "Screen recovery", FontFamily = Theme.Display, FontSize = 15 },
                Content = rec, Margin = new Thickness(0, 10, 0, 0),
            };
            body.Children.Add(recovery);

            // ----- the wheel app: update mode for a SimPro reinstall -----
            var wh = new StackPanel();
            wh.Children.Add(Theme.Note("For a wheel whose firmware misbehaves, or to go back to Simagic's stock firmware: SimPro reinstalls the wheel " +
                "app. If SimPro can't start the install (it sits at 0% or doesn't find the wheel), put the wheel in update mode first.", new Thickness(0, 4, 0, 8)));
            simproState = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) };
            wh.Children.Add(simproState);
            wh.Children.Add(StepLine("1", "Check the line above: SimPro must have Simagic's own 1.3.11 file, or it reinstalls whatever is there."));
            wh.Children.Add(StepLine("2", "Put the wheel in update mode. It restarts into its updater and stays there, even after a power cycle, until SimPro finishes an install. Its screen and lights stop until then; the base keeps working."));
            var whRow = new WrapPanel { Margin = new Thickness(36, 0, 0, 8) };
            updateMode = Theme.Btn("Put the wheel in update mode", EnterUpdateMode);
            whRow.Children.Add(updateMode);
            whRow.Children.Add(Theme.Btn("Open SimPro", () => { try { Process.Start(SimProExe); } catch (Exception e) { MessageBox.Show("Couldn't start SimPro: " + e.Message); } }));
            wh.Children.Add(whRow);
            wh.Children.Add(StepLine("3", "In SimPro (started directly, as this button does), open the wheel's firmware page and reinstall 1.3.11. Don't unplug or switch off until it's done."));
            wh.Children.Add(StepLine("4", "If it sits at 0%, close SimPro completely and open it again with the wheel already in update mode, then reinstall."));
            body.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "Wheel firmware recovery", FontFamily = Theme.Display, FontSize = 15 },
                Content = wh, Margin = new Thickness(0, 6, 0, 0),
            });

            Content = Theme.CardBox(body);
            Refresh();
        }

        private static FrameworkElement StepLine(string n, string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var num = new TextBlock { Text = n + ".", Width = 36, Foreground = Theme.Red, FontFamily = Theme.Display, FontWeight = FontWeights.Bold };
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2 });
            return row;
        }

        private bool FlowActive => step == Step.Sending || step == Step.Watching || step == Step.PowerCycle || step == Step.Checking;

        /// <summary>Called by the Wheel tab's refresh timer.</summary>
        public void Refresh()
        {
            var u = Usb;
            var img = ScreenFlasher.Choose(u, S, out var refusal);
            bool? on = ScreenFlasher.RamDriveOn(S);
            var last = S.ScreenFlashes?.LastOrDefault();
            memState.Text = (on == true ? "Picture memory is on" : on == false ? "Picture memory is off (Simagic's header)"
                    : last.Result == "failed" ? "The last upload failed: use Screen recovery below"
                    : "The last upload wasn't finished: check the screen in the card above, or use Screen recovery if it has no dash")
                + (last != null ? $"  ·  last change {last.When:d MMM HH:mm}" : "")
                + (refusal != null ? "\n" + refusal : img != null ? $"\nScreen image: {img.Id}" : "");
            bool can = !busy && !FlowActive && img != null && ack.IsChecked == true;
            turnOn.IsEnabled = can && on != true;
            turnOff.IsEnabled = can && on != false;
            recover.IsEnabled = can;
            foreach (var p in pings) p.IsEnabled = !busy && !FlowActive && img != null;
            recoverSpeedText.Text = recoverSpeed == 9600 ? "At 9600 the plugin wakes the screen's updater first (com_star), as in the runbook; the data then goes at full speed."
                : recoverSpeed == 115200 ? "At 115200 the upload is slower (about 20 s)." : "The normal speed: for a screen whose dash still works.";

            string simpro = SimProFileState();
            simproState.Text = (u?.WheelFound == true && u.InUpdateMode ? "The wheel is in update mode now: reinstall 1.3.11 in SimPro.\n" : "") + simpro;
            updateMode.IsEnabled = !busy && !FlowActive && u?.WheelFound == true && !u.InUpdateMode;
            PollPowerCycle();
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

        /// <summary>Runs `work` with the wheel lent by USB mode, off the UI thread; `failed` gets the error on the UI thread.</summary>
        private void Run(string reason, Action<string> work, Action<string> failed = null)
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
                    if (error != null)
                    {
                        if (failed != null) failed(error);
                        else ShowStep(Step.Message, "", "Stopped", error + " Leave the wheel as it is; a power cycle and Screen recovery put the screen right.", Theme.Amber);
                    }
                    Refresh();
                }));
            });
        }

        // ---------- the step box ----------

        private void ShowStep(Step s, string number, string title, string text, Brush edge, params Button[] buttons)
        {
            step = s;
            stepBox.Visibility = Visibility.Visible;
            stepBox.BorderBrush = edge;
            stepBox.Background = edge == Theme.Red ? Theme.RedWash : Theme.Raised;
            stepNo.Text = number;
            stepNo.Visibility = string.IsNullOrEmpty(number) ? Visibility.Collapsed : Visibility.Visible;
            stepTitle.Text = title;
            stepText.Text = text;
            Status("");
            progress.Visibility = Visibility.Collapsed;
            stepButtons.Children.Clear();
            foreach (var b in buttons) { b.Margin = new Thickness(0, 0, 10, 6); stepButtons.Children.Add(b); }
            stepButtons.Visibility = buttons.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (s != Step.Checking) afterCheck.Clear();
            Refresh();
        }

        private void Status(string text)
        {
            stepStatus.Text = text ?? "";
            stepStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private Button CloseButton() => Theme.Btn("Close", () => { step = Step.None; stepBox.Visibility = Visibility.Collapsed; afterCheck.Clear(); Refresh(); });

        private void Ping(int baud)
        {
            ShowStep(Step.Message, "", $"Trying {baud}", "Watch the screen: the right speed turns it red and dips the backlight for 4 seconds.", Theme.Line2);
            Run("Testing the screen's speed", path =>
            {
                ScreenFlasher.Ping(path, baud);
                Dispatcher.BeginInvoke(new Action(() => ShowStep(Step.Message, "", $"Tried {baud}",
                    "Did the screen turn red? Then that's its speed: pick it in step 3 below. Nothing? Try the next speed.", Theme.Line2, CloseButton())));
            });
        }

        private void Flash(string mode, int screenAt)
        {
            var img = ScreenFlasher.Choose(Usb, S, out var refusal);
            if (img == null) { ShowStep(Step.Message, "", "Not sent", refusal, Theme.Amber, CloseButton()); return; }
            byte[] block;
            try { block = img.Build(mode); }
            catch (Exception e) { ShowStep(Step.Message, "", "Not sent", e.Message, Theme.Amber, CloseButton()); return; }
            string what = mode == "ramfs" ? "turn the screen's picture memory on" : "put Simagic's header back on the screen (picture memory off)";
            if (!Confirm($"This will {what} (screen image {img.Id}).\n\n" +
                         "What happens:\n" +
                         "1. The plugin sends the screen's new header (about 10 seconds).\n" +
                         "2. You watch the wheel's screen: it checks itself, then restarts into its dash if it worked (its \"Update Successed\" flashes by too fast to see), or stops on \"Update Failed\". You say which.\n" +
                         "3. You power-cycle the wheel: base off AND USB unplugged for 5 seconds, then both back.\n" +
                         "4. The plugin checks the screen (green or red) and you're done.\n\n" +
                         "Wheel on the base, USB plugged in, no game running. Don't unplug or switch off before step 3. Go ahead?")) return;

            // From now on the screen's picture memory is unknown: nothing may use it until the check at the end says so,
            // and whatever was in it is gone (the screen restarts after the upload).
            ScreenFlasher.Record(S, img, mode, "sent");
            S.ScreenRamDrive = false;
            Usb?.CancelPowerCycle();
            Usb?.ForgetScreenFiles();
            Usb?.SettingsChanged();
            plugin.SaveSettings();

            flowMode = mode;
            answeredOk = false;
            keptPowerShown = false;
            answered = new ManualResetEvent(false);
            var hold = answered;
            ShowStep(Step.Sending, "STEP 1 OF 4", "Sending the screen's header",
                "About 10 seconds. Don't unplug the wheel or switch the base off.", Theme.Red);
            progress.Visibility = Visibility.Visible;
            progress.Value = 0;
            Run("Updating the screen", path =>
            {
                ScreenFlasher.Upload(path, block, screenAt, (t, f) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (f >= 1 && step == Step.Sending) ShowWatching();
                    else if (step == Step.Sending) { progress.Value = f; Status(t); }
                })), hold);
                // a confirmed upload: mark the wheel so the plugin can tell a real power cycle from a USB replug
                if (answeredOk)
                {
                    using (var c = new FxConnection(path)) c.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)PowerToken));
                    Usb.ExpectPowerCycle(PowerToken);
                }
            }, error => ShowStep(Step.Failed, "", "The upload stopped", error + "\n\nLeave the wheel as it is. If the screen has no dash after a power cycle, " +
                                                                         "open Screen recovery below.", Theme.Amber, CloseButton()));
        }

        private void ShowWatching()
        {
            ShowStep(Step.Watching, "STEP 2 OF 4", "Watch the wheel's screen",
                "It's checking its whole image now (up to a minute; the progress may sit at 100% meanwhile).\n" +
                "If it worked, the screen simply restarts into its dash. Its \"Update Successed\" message flashes by too fast to see: " +
                "that's normal, a restart into the dash means it passed.\n" +
                "If it didn't, it stops on \"Update Failed:check Error!\" and stays there. What happened?", Theme.Red,
                Theme.Btn("It restarted into the dash", () => Answer("ok"), primary: true),
                Theme.Btn("It stopped on \"Update Failed\" (or went white)", () => Answer("failed")));
        }

        private void Answer(string result)
        {
            ScreenFlasher.Answer(S, result);
            S.ScreenRamDrive = false; // set again by the check after the power cycle
            Usb?.SettingsChanged();
            plugin.SaveSettings();
            answeredOk = result == "ok";
            bool uploadDone = !busy; // answered after the upload stopped holding the screen (3 minutes): mark the wheel here
            answered?.Set();
            if (answeredOk && uploadDone)
                Run("Marking the wheel", path =>
                {
                    using (var c = new FxConnection(path)) c.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)PowerToken));
                    Usb.ExpectPowerCycle(PowerToken);
                });
            if (answeredOk) ShowPowerCycle(null);
            else
            {
                recovery.IsExpanded = true;
                ShowStep(Step.Failed, "", "The screen needs Simagic's header back",
                    "Nothing is broken: the screen's image is intact, only its header doesn't match it, so the screen won't show a dash yet. " +
                    "Follow Screen recovery just below (opened for you): power-cycle the wheel, find the speed the screen listens at, then put the header back.",
                    Theme.Amber, CloseButton());
            }
        }

        private void ShowPowerCycle(string note)
        {
            ShowStep(Step.PowerCycle, "STEP 3 OF 4", "Power-cycle the wheel",
                "Switch the base OFF and unplug the wheel's USB cable. Wait 5 seconds. Then plug the USB back in and switch the base on.\n" +
                "The screen only sets up its picture memory when it starts from power off. The plugin carries on by itself when the wheel is back.",
                Theme.Red, Theme.Btn("Skip: check the screen now", StartCheck));
            Status(note ?? "Waiting for the wheel to switch off...");
            backAt = DateTime.MinValue;
        }

        /// <summary>Step 3: wait for the power cycle (from the Wheel tab's refresh timer).</summary>
        private void PollPowerCycle()
        {
            if (step != Step.PowerCycle || busy) return;
            var u = Usb;
            if (u == null) return;
            int r = u.PowerCycleResult;
            if (r == 0) { Status(u.WheelFound ? "Waiting for the wheel to switch off..." : "Wheel off. Plug the USB back in and switch the base on."); return; }
            if (r == 2)
            {
                if (keptPowerShown) return;
                keptPowerShown = true;
                // the build query on reconnect cleared the token: mark the wheel again and keep waiting
                Run("Marking the wheel", path =>
                {
                    using (var c = new FxConnection(path)) c.WriteRam(FxUsb.MarkerWord, BitConverter.GetBytes((uint)PowerToken));
                    u.ExpectPowerCycle(PowerToken);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        keptPowerShown = false;
                        Status("The wheel came back but never lost power: the base kept it on. Switch the base OFF too (and unplug USB), then back on.");
                    }));
                });
                return;
            }
            // back after a power loss: give the screen a few seconds to start, and the plugin to find the wheel's build
            if (backAt == DateTime.MinValue) backAt = DateTime.UtcNow;
            Status("The wheel is back. Getting ready to check the screen...");
            if (DateTime.UtcNow - backAt > TimeSpan.FromSeconds(5) && u.WheelFound && u.SupportedApp && u.FirmwarePatched) StartCheck();
        }

        private void StartCheck()
        {
            Usb?.CancelPowerCycle();
            ShowStep(Step.Checking, "STEP 4 OF 4", "Checking the screen",
                flowMode == "ramfs" ? "Expect a green card on red: picture memory is on." : "Expect all red: picture memory is off.", Theme.Red);
            afterCheck.Run();
        }

        private void CheckAnswered(string result)
        {
            string expected = flowMode == "ramfs" ? "on" : "off";
            if (result == expected)
                ShowStep(Step.Done, "", flowMode == "ramfs" ? "Done: picture memory is on" : "Done: picture memory is off",
                    flowMode == "ramfs" ? "Your dashes and screensavers are now drawn from pictures kept in the screen. The first time each one shows it loads for a few seconds."
                                        : "The screen has Simagic's header again, byte for byte. Dashes are drawn with rectangles.", Theme.Green, CloseButton());
            else if (result == "no-reply")
            {
                recovery.IsExpanded = true;
                ShowStep(Step.Failed, "", "The screen isn't responding",
                    "It doesn't take commands at the normal speed, which happens when it has no dash. Follow Screen recovery just below (opened for you).", Theme.Amber, CloseButton());
            }
            else
                ShowStep(Step.Done, "", flowMode == "ramfs" ? "The screen still has no picture memory" : "The screen still has picture memory",
                    "The upload didn't change it. Was the wheel fully powered off (base off and USB unplugged)? Check again, or run the steps once more.",
                    Theme.Amber, Theme.Btn("Check again", StartCheck, primary: true), CloseButton());
        }

        private void EnterUpdateMode()
        {
            if (!Confirm("The wheel restarts into its updater and stays there (even after a power cycle) until SimPro finishes an install. " +
                         "Its screen and lights stop until then; the base keeps working.\n\nThen: open SimPro and reinstall wheel app 1.3.11. Go ahead?")) return;
            ShowStep(Step.Message, "", "Putting the wheel in update mode...", "", Theme.Line2);
            Run("Putting the wheel in update mode", path =>
            {
                string err = FxUsb.EnterBootloader(path);
                if (err != null) throw new Exception(err);
                Thread.Sleep(3000);
                Dispatcher.BeginInvoke(new Action(() => ShowStep(Step.Message, "", "The wheel is in update mode",
                    "When it's back (a few seconds), open SimPro and reinstall 1.3.11.", Theme.Line2, CloseButton())));
            });
        }
    }
}
