using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The Wheel tab's quick controls: screen on/off, screen brightness, the lights' ceiling and night mode (by hand,
    /// on a schedule or with ATSR-Hub), plus buttons for them. Everything applies live; the same controls are SimHub
    /// actions (QuickControls.cs), so the sliders follow changes made from a button.
    /// </summary>
    public class QuickControlsCard : Border
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly CheckBox screenOn, night, schedule, followAtsr;
        private readonly Slider screen, ceiling, nightScreen, nightCeiling;
        private readonly TextBlock nightState;
        private readonly TextBox from, to;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private bool refreshing;

        public QuickControlsCard(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            bool hasScreen = plugin.ActiveModel.HasScreen; // the GT Neo has only lights
            var body = new StackPanel();
            body.Children.Add(Theme.Eyebrow("Quick controls"));

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            cols.ColumnDefinitions.Add(new ColumnDefinition());

            var day = new StackPanel();
            screenOn = Theme.Switch("Screen on", !S.ScreenOff, v => { if (!refreshing) plugin.SetScreenOff(!v); },
                "Off: the screen goes dark and the lights keep running.");
            var screenRow = SliderRow("Screen brightness", 5, 100, S.ScreenBrightness, 5, v => $"{v:0}%", v => { S.ScreenBrightness = (int)v; Changed(); }, out screen);
            if (hasScreen) { day.Children.Add(screenOn); day.Children.Add(screenRow); }
            day.Children.Add(SliderRow("Brightest light", 1, 90, S.LedCeiling, 1, v => $"{Math.Round(v / 0.9):0}%", v => { S.LedCeiling = (int)v; Changed(); }, out ceiling));
            day.Children.Add(Theme.Note("A cap on every light the plugin drives: presets, your own lights, ATSR-Hub, alerts. " +
                                        (hasScreen ? "SimPro's own lights" : "The wheel's own lights") + " (when the plugin isn't driving the LEDs) aren't affected.", new Thickness(0, -4, 0, 0)));
            cols.Children.Add(day);

            var nightPanel = new StackPanel();
            var nightHead = new DockPanel();
            nightState = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2, 0, 0) };
            DockPanel.SetDock(nightState, Dock.Right);
            nightHead.Children.Add(nightState);
            night = Theme.Switch("Night mode", S.NightMode, v => { if (!refreshing) plugin.SetNightMode(v); }, hasScreen ? "A dimmer screen and lights, for racing in the dark." : "Dimmer lights, for racing in the dark.");
            nightHead.Children.Add(night);
            nightPanel.Children.Add(nightHead);
            var nightScreenRow = SliderRow("Screen at night", 5, 100, S.NightScreenBrightness, 5, v => $"{v:0}%", v => { S.NightScreenBrightness = (int)v; Changed(); }, out nightScreen);
            if (hasScreen) nightPanel.Children.Add(nightScreenRow);
            nightPanel.Children.Add(SliderRow("Brightest light at night", 1, 90, S.NightLedCeiling, 1, v => $"{Math.Round(v / 0.9):0}%", v => { S.NightLedCeiling = (int)v; Changed(); }, out nightCeiling));
            var sched = new StackPanel { Orientation = Orientation.Horizontal };
            schedule = Theme.Switch("On a schedule, from", S.NightSchedule, v => { S.NightSchedule = v; Changed(); });
            schedule.Margin = new Thickness(0, 0, 8, 0);
            schedule.VerticalAlignment = VerticalAlignment.Center;
            sched.Children.Add(schedule);
            from = TimeBox(S.NightFrom, t => S.NightFrom = t);
            sched.Children.Add(from);
            sched.Children.Add(new TextBlock { Text = "to", Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            to = TimeBox(S.NightTo, t => S.NightTo = t);
            sched.Children.Add(to);
            sched.Margin = new Thickness(0, 0, 0, 12);
            nightPanel.Children.Add(sched);
            followAtsr = Theme.Switch("With ATSR-Hub's night mode", S.NightFollowAtsr, v => { S.NightFollowAtsr = v; Changed(); },
                "On whenever ATSR-Hub dims its lights (its night mode brightness below 100%).");
            nightPanel.Children.Add(followAtsr);
            Grid.SetColumn(nightPanel, 2);
            cols.Children.Add(nightPanel);
            body.Children.Add(cols);

            var buttons = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var keys = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (id, name, action) in new[]
            {
                ("screen", "Screen on / off", "UsbScreenToggle"), ("screenup", "Screen brighter", "UsbScreenBrightnessUp"),
                ("screendown", "Screen dimmer", "UsbScreenBrightnessDown"), ("ledup", "Lights brighter", "UsbLedBrightnessUp"),
                ("leddown", "Lights dimmer", "UsbLedBrightnessDown"), ("night", "Night mode on / off", "UsbNightModeToggle"),
                ("lightnext", "Next light preset", "UsbNextLightPreset"), ("lightprev", "Previous light preset", "UsbPreviousLightPreset"),
                ("wheeldash", "Custom / wheel's own dash", "UsbWheelDashToggle"),
            })
            {
                if (!hasScreen && (id.StartsWith("screen") || id == "wheeldash")) continue;
                buttons.Children.Add(new WheelButtonBinding(plugin, id, name));
                keys.Children.Add(Theme.Binding(name, action));
            }
            buttons.Children.Add(new Expander { Header = "A keyboard key or another controller instead (through SimHub)", Content = keys, Margin = new Thickness(0, 6, 0, 0) });
            buttons.Children.Add(Theme.Note("SimHub properties for dashes and Stream Deck: FXProRpmSyncPlugin.Usb.ScreenOn, .Usb.ScreenBrightness, " +
                                            ".Usb.LedCeiling, .Usb.NightMode, .Usb.LightPreset, .Usb.WheelDashSwapped.", new Thickness(0, 8, 0, 0)));
            body.Children.Add(new Expander { Header = "Wheel buttons for these", Content = buttons, Margin = new Thickness(0, 4, 0, 0) });

            Child = Theme.CardBox(body, 20, new Thickness(0, 0, 0, 14));
            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { timer.Start(); Refresh(); };
            Unloaded += (s, e) => timer.Stop();
        }

        private void Changed() { if (refreshing) return; plugin.SaveSettings(); plugin.Usb?.SettingsChanged(); }

        private FrameworkElement SliderRow(string label, double min, double max, double value, double step, Func<double, string> show, Action<double> set, out Slider slider)
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickFrequency = step, IsSnapToTickEnabled = true, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Theme.Text, FontFamily = Theme.Display, Text = show(s.Value) };
            s.ValueChanged += (o, e) => { text.Text = show(s.Value); if (!refreshing) set(s.Value); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(s);
            row.Children.Add(text);
            slider = s;
            return Theme.Field(label, row, 160);
        }

        private TextBox TimeBox(string value, Action<string> set)
        {
            var box = new TextBox { Text = value, Width = 64, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Local time, HH:mm" };
            box.LostFocus += (s, e) =>
            {
                if (TimeSpan.TryParse(box.Text, System.Globalization.CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                {
                    box.Text = t.ToString(@"hh\:mm");
                    set(box.Text);
                    Changed();
                }
                else box.Text = value; // back to the last good one
                value = box.Text;
            };
            return box;
        }

        /// <summary>Follows changes made from buttons and actions (unless the user is dragging that slider).</summary>
        private void Refresh()
        {
            refreshing = true;
            try
            {
                screenOn.IsChecked = !S.ScreenOff;
                night.IsChecked = plugin.NightActive;
                Set(screen, S.ScreenBrightness);
                Set(ceiling, S.LedCeiling);
                Set(nightScreen, S.NightScreenBrightness);
                Set(nightCeiling, S.NightLedCeiling);
                bool on = plugin.NightActive;
                nightState.Text = on ? (S.NightMode ? "on" : "on (automatic)") : "off";
                nightState.Foreground = on ? Theme.Blue : Theme.Text3;
            }
            finally { refreshing = false; }
        }

        private static void Set(Slider s, double v) { if (!s.IsMouseCaptureWithin && Math.Abs(s.Value - v) > 0.01) s.Value = v; }
    }
}
