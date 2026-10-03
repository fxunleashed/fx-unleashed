using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "Check my screen": does the wheel's screen have picture memory? Safe on any screen at any time (ScreenFlasher.ProbeRam:
    /// text commands only, never file data). The plugin can't read the screen's replies, so the user says what it showed:
    /// green = picture memory (the "My screen has picture memory" setting is switched on), red = none (switched off), no
    /// change = the screen doesn't take commands (no dash after an upload: Screen recovery). Used on the Wheel tab and by
    /// the firmware card after an upload.
    /// </summary>
    internal sealed class ScreenCheckPanel : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly Button check;
        private readonly Border box;
        private readonly TextBlock title, text;
        private readonly WrapPanel answers;
        private volatile bool busy;

        /// <summary>The user's answer: "on", "off" or "no-reply".</summary>
        public event Action<string> Answered;

        public ScreenCheckPanel(FXProRpmSyncPlugin plugin, bool button = true)
        {
            this.plugin = plugin;
            check = Theme.Btn("Check my screen (safe, 5 s)", () => Run(), primary: true, icon: "");
            check.ToolTip = "Shows a green card on a red screen if it has picture memory, all red if not. Safe on any screen, flashed or not:\n" +
                            "its small test picture can't be mistaken for a command, and the dash comes back after 5 seconds.";
            if (button) Children.Add(new WrapPanel { Children = { check } });

            title = new TextBlock { FontFamily = Theme.Display, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
            text = new TextBlock { FontSize = 13.5, Foreground = Theme.Text2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            answers = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            answers.Children.Add(Answer("Green card", Theme.Green, "on"));
            answers.Children.Add(Answer("All red", Theme.Red, "off"));
            answers.Children.Add(Answer("No change", Theme.Line2, "no-reply"));
            var again = Theme.Btn("Missed it? Show it again", () => Run());
            again.Margin = new Thickness(8, 0, 0, 0);
            answers.Children.Add(again);
            var inner = new StackPanel();
            inner.Children.Add(title);
            inner.Children.Add(text);
            inner.Children.Add(answers);
            box = new Border
            {
                Background = Theme.Raised, BorderBrush = Theme.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 10, 0, 0), Child = inner, Visibility = Visibility.Collapsed,
            };
            Children.Add(box);
        }

        private Button Answer(string label, Brush swatch, string result)
        {
            var b = Theme.Btn("", () => Done(result));
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = swatch, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
            b.Margin = new Thickness(0, 0, 8, 0);
            return b;
        }

        /// <summary>Why the check can't run now, or null.</summary>
        public string Refusal()
        {
            var u = plugin.Usb;
            if (u == null || !u.WheelFound) return "The wheel isn't connected over USB.";
            if (!u.SupportedApp) return "The wheel isn't running app 1.3.11 (or is in update mode).";
            if (!u.FirmwarePatched) return "The wheel needs the FX Unleashed custom firmware to reach its screen.";
            return null;
        }

        public void Refresh()
        {
            string why = Refusal();
            check.IsEnabled = !busy && why == null;
            check.ToolTip = why ?? check.ToolTip;
        }

        /// <summary>Runs the check (also called by the firmware card after a power cycle).</summary>
        public void Run()
        {
            if (busy) return;
            string why = Refusal();
            if (why != null) { Show("Can't check now", why, false, Theme.Line2); return; }
            busy = true;
            Refresh();
            Show("Watch the wheel's screen", "It turns red, and a green card appears in the middle if it has picture memory. In 5 seconds the dash comes back.", false, Theme.Line2);
            var usb = plugin.Usb;
            Task.Run(() =>
            {
                string error = null;
                try
                {
                    using (var lend = usb.Lend("Checking the screen"))
                    {
                        if (lend == null) throw new Exception("USB mode didn't let go of the wheel.");
                        var path = FxUsb.FindPath() ?? throw new Exception("the wheel isn't connected over USB.");
                        ScreenFlasher.ProbeRam(path);
                    }
                }
                catch (Exception e) { error = e.Message; SimHub.Logging.Current.Error("[FXProRpmSync] screen check: " + e); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    if (error != null) Show("The check didn't run", error, false, Theme.Amber);
                    else Show("What did the screen show?", "A green card in the middle of red: it has picture memory. All red: it doesn't. No change at all: the screen isn't taking commands " +
                                                                  "(after a screen upload: open Screen recovery below).", true, Theme.Red);
                    Refresh();
                }));
            });
        }

        private void Show(string head, string body, bool ask, Brush edge)
        {
            box.Visibility = Visibility.Visible;
            box.BorderBrush = edge;
            box.Background = ask ? Theme.RedWash : Theme.Raised;
            title.Text = head;
            text.Text = body;
            answers.Visibility = ask ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Done(string result)
        {
            S.ScreenCheck = result;
            S.ScreenCheckWhen = DateTime.Now;
            bool was = S.ScreenRamDrive;
            S.ScreenRamDrive = result == "on";
            plugin.Usb?.SettingsChanged();
            plugin.SaveSettings();
            if (result == "on") Show("Picture memory: on", "Your dashes and screensavers are drawn from pictures kept in the screen." + (was ? "" : " Switched on."), false, Theme.Green);
            else if (result == "off") Show("Picture memory: off", "Dashes are drawn with rectangles. To add picture memory, use the firmware card below." + (was ? " Switched off." : ""), false, Theme.Line2);
            else Show("The screen didn't respond", "It isn't taking commands at the normal speed. If this follows a screen upload, open Screen recovery below. " +
                                                  "Otherwise switch the base off and unplug USB for a few seconds, then check again.", false, Theme.Amber);
            Answered?.Invoke(result);
        }

        /// <summary>Hides the result box.</summary>
        public void Clear() { box.Visibility = Visibility.Collapsed; }
    }
}
