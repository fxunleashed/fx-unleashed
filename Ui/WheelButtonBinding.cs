using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// One action bound to an FX Pro button: shows the button, "Set" waits for the next wheel button pressed (10 s),
    /// × clears it. The plugin reads the wheel's buttons itself (WheelButtons), so this works without SimHub's
    /// Controls and events.
    /// </summary>
    public class WheelButtonBinding : DockPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly string action;
        private readonly TextBlock value;
        private readonly Button set, clear;
        private readonly DispatcherTimer timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        private bool listening;

        public WheelButtonBinding(FXProRpmSyncPlugin plugin, string action, string label)
        {
            this.plugin = plugin;
            this.action = action;
            Margin = new Thickness(0, 0, 0, 8);
            LastChildFill = false;

            var name = new StackPanel { Orientation = Orientation.Horizontal, Width = 190, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 14, Foreground = Theme.Red, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            name.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(name);

            value = new TextBlock { FontFamily = Theme.Display, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(new Border
            {
                Background = Theme.Raised, BorderBrush = Theme.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 6), MinWidth = 210, Margin = new Thickness(0, 0, 8, 0), Child = value,
            });
            set = Theme.Btn("Set", Toggle, icon: "");
            set.Margin = new Thickness(0, 0, 6, 0);
            Children.Add(set);
            clear = Theme.Btn("", Clear);
            clear.Content = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 10 };
            clear.Padding = new Thickness(10, 8, 10, 8);
            clear.Margin = new Thickness(0);
            clear.ToolTip = "Clear";
            Children.Add(clear);

            timeout.Tick += (s, e) => Stop();
            Unloaded += (s, e) => { if (listening) Stop(); };
            Show();
        }

        private int? Bound => plugin.Settings.Usb.WheelButtons != null && plugin.Settings.Usb.WheelButtons.TryGetValue(action, out var b) ? b : (int?)null;

        private void Show()
        {
            if (listening)
            {
                value.Text = "Press a wheel button…";
                value.Foreground = Theme.Red;
                set.Content = "Cancel";
            }
            else
            {
                value.Text = Bound is int b ? $"Wheel button {b}" : "Not set";
                value.Foreground = Bound != null ? Theme.Text : Theme.Text3;
                set.Content = Bound != null ? "Change" : "Set";
            }
            clear.Visibility = Bound != null && !listening ? Visibility.Visible : Visibility.Hidden;
        }

        private void Toggle()
        {
            if (listening) { Stop(); return; }
            var buttons = plugin.Buttons;
            if (buttons == null || !buttons.Found)
            {
                value.Text = "Wheel not found on USB";
                value.Foreground = Theme.Amber;
                return;
            }
            listening = true;
            buttons.Learn(b => Dispatcher.BeginInvoke(new Action(() => Learned(b))));
            timeout.Start();
            Show();
        }

        private void Learned(int button)
        {
            if (!listening) return;
            var map = plugin.Settings.Usb.WheelButtons;
            map[action] = button;
            plugin.SaveSettings();
            Stop();
        }

        private void Stop()
        {
            listening = false;
            timeout.Stop();
            plugin.Buttons?.CancelLearn();
            Show();
        }

        private void Clear()
        {
            plugin.Settings.Usb.WheelButtons.Remove(action);
            plugin.SaveSettings();
            Show();
        }
    }
}
