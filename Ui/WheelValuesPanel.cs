using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "Wheel dash values" (NEXT.md G): any value the wheel's own dashes draw can come from another SimHub property or a
    /// formula instead of the built-in mapping. Stored in Settings.Feed.Overrides (shared with standard mode's SimGame
    /// feed), applied last in SimHubFeedMapper.Fill, in the units shown; WheelTelemetry.Build still clamps to the wire.
    /// </summary>
    internal sealed class WheelValuesPanel : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public WheelValuesPanel(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var fs = plugin.Settings.Feed;
            Children.Add(Theme.Note(
                "Each value one of the wheel's own dashes shows can come from another SimHub property (as in SimHub's property " +
                "list, e.g. DataCorePlugin.GameRawData.Telemetry.dcAntiRollFront) or a formula (ncalc:[DataCorePlugin.GameData.Fuel] * 2, " +
                "or js:...). Give it in the unit shown: the wheel converts to your units itself. Empty = the built-in value. " +
                "Also used by standard mode's \"Drive the wheel's dash from SimHub\".", new Thickness(0, 0, 0, 10)));

            var live = new System.Collections.Generic.List<(string Field, TextBlock Now)>();
            foreach (var group in WheelTelemetry.Fields.GroupBy(f => f.Group))
            {
                var rows = new StackPanel();
                foreach (var (_, field, label, unit) in group)
                {
                    fs.Overrides.TryGetValue(field, out var bind);
                    var box = new TextBox { Width = 380, Text = bind ?? "", ToolTip = field };
                    var f = field;
                    box.LostFocus += (s, e) =>
                    {
                        var v = box.Text.Trim();
                        if (v.Length == 0) fs.Overrides.Remove(f); else fs.Overrides[f] = v;
                        plugin.SaveSettings();
                    };
                    var now = new TextBlock { Width = 90, Margin = new Thickness(10, 0, 0, 0), Foreground = Theme.Text3, VerticalAlignment = VerticalAlignment.Center };
                    live.Add((field, now));
                    var line = new StackPanel { Orientation = Orientation.Horizontal };
                    line.Children.Add(box);
                    line.Children.Add(now);
                    rows.Children.Add(Theme.Field(label + (unit.Length > 0 ? $"  [{unit}]" : ""), line, 230));
                }
                int set = group.Count(g => fs.Overrides.ContainsKey(g.Field));
                Children.Add(new Expander { Header = group.Key + (set > 0 ? $"  ·  {set} changed" : ""), Content = rows, Margin = new Thickness(0, 2, 0, 2) });
            }

            timer.Tick += (s, e) =>
            {
                var t = plugin.WheelTelemetryNow;
                foreach (var (field, now) in live)
                    now.Text = t == null ? "" : "now " + Format(t.Get(field));
            };
            Loaded += (s, e) => timer.Start();
            Unloaded += (s, e) => timer.Stop();
        }

        private static string Format(double v) => Math.Abs(v - Math.Round(v)) < 0.005 ? v.ToString("0") : v.ToString("0.##");
    }
}
