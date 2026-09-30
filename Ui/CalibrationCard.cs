using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Car tuning: find the current car's shift points from a lap or two (ShiftCalibrator). Start it, drive with some
    /// full-throttle pulls through the gears, watch each gear's result come in, then Apply: saved as the car's override
    /// (its own light pattern moved per gear so the shift lands on the measured point).
    /// </summary>
    public class CalibrationCard : Border
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly TextBlock status;
        private readonly StackPanel gears;
        private readonly Button start, apply, remove;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public CalibrationCard(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var body = new StackPanel();
            body.Children.Add(Theme.Eyebrow("Find this car's shift points"));
            body.Children.Add(Theme.Note("Drive a lap or two with some full-throttle pulls through the gears (each gear up to the limiter at least once). " +
                "The plugin measures how hard the car pulls in each gear and finds where the next gear pulls harder: that's the best upshift, " +
                "worked out from the car itself, not from when you shift. Apply moves the car's lights so they flash there, gear by gear."));
            status = new TextBlock { Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), FontFamily = Theme.Display };
            body.Children.Add(status);
            gears = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(gears);
            var buttons = new WrapPanel();
            start = Theme.Btn("Start calibrating", Toggle, primary: true);
            apply = Theme.Btn("Apply to this car", () => { if (plugin.ApplyCalibration()) { plugin.StopCalibration(); Refresh(); } });
            remove = Theme.Btn("Remove this car's calibration", () => { if (plugin.CurrentCarKey != null) plugin.DeleteOverride(plugin.CurrentCarKey); Refresh(); });
            buttons.Children.Add(start); buttons.Children.Add(apply); buttons.Children.Add(remove);
            body.Children.Add(buttons);
            Child = Theme.CardBox(body);
            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { timer.Start(); Refresh(); };
            Unloaded += (s, e) => timer.Stop();
        }

        private void Toggle()
        {
            if (plugin.Calibrator != null) plugin.StopCalibration(); else plugin.StartCalibration();
            Refresh();
        }

        private static string Rpm(double r) => r.ToString("#,0", CultureInfo.InvariantCulture) + " rpm";

        private void Refresh()
        {
            var c = plugin.Calibrator;
            var car = plugin.CurrentCarKey;
            var ov = car != null ? plugin.GetOverride(car) : null;
            bool calibrated = ov?.Kind == OverrideKind.Calibrated;
            start.Content = c != null ? "Stop" : "Start calibrating";
            start.IsEnabled = c != null || car != null;
            remove.Visibility = calibrated && c == null ? Visibility.Visible : Visibility.Collapsed;
            gears.Children.Clear();
            if (c == null)
            {
                apply.Visibility = Visibility.Collapsed;
                status.Text = car == null ? "Start a game to calibrate the car you're driving."
                            : calibrated ? $"{plugin.CurrentCarName}: calibrated ({ov.Summary.Replace("Calibrated shift points: ", "")})."
                            : $"{plugin.CurrentCarName}: using {(plugin.LightsSource ?? "its lights")}.";
                return;
            }
            var results = c.Results();
            status.Text = $"Calibrating {plugin.CurrentCarName}: {c.Samples} full-throttle samples" + (c.Limiter > 0 ? $", highest revs so far {Rpm(c.Limiter)}." : ".");
            foreach (var r in results)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var label = new TextBlock { Text = $"{r.Gear} → {r.Gear + 1}", Width = 70, Foreground = Theme.Text2, FontFamily = Theme.Display };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                string now = "";
                var baseLayout = plugin.CurrentBaseLayout;
                if (baseLayout != null) now = $"  (the car data: {Rpm(baseLayout.ForGear(r.Gear.ToString(CultureInfo.InvariantCulture)).ShiftRpm)})";
                row.Children.Add(new TextBlock
                {
                    Text = r.ShiftRpm.HasValue ? Rpm(r.ShiftRpm.Value) + (r.AtLimiter ? " (at the limiter)" : r.Estimated ? " (from the engine's torque curve)" : " (measured)") + now
                         : r.Need + (r.Coverage > 0 ? $"  ({r.Coverage * 100:0}% of the revs covered)" : ""),
                    Foreground = r.ShiftRpm.HasValue ? Theme.Text : Theme.Text3, TextWrapping = TextWrapping.Wrap,
                });
                gears.Children.Add(row);
            }
            if (results.Count == 0) gears.Children.Add(new TextBlock { Text = "Waiting for full throttle…", Foreground = Theme.Text3 });
            apply.Visibility = results.Any(r => r.ShiftRpm.HasValue) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
