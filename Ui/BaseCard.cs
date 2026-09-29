using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "Base per car" on the Car tuning tab (NEXT.md I): rotation and overall force for the current car and its game,
    /// pushed through SimPro on each car change (BaseSwitcher) and put back on exit. Off by default.
    /// </summary>
    internal sealed class BaseCard : Border
    {
        private readonly FXProRpmSyncPlugin plugin;
        private BaseSettings S => plugin.Settings.Base;
        private readonly TextBlock nowText, carTitle, gameTitle;
        private readonly StackPanel editors, savedList;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private string shownCar;

        public BaseCard(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var body = new StackPanel();
            var head = new DockPanel();
            var on = Theme.Switch("Set the base per car", S.Enabled, v => { S.Enabled = v; Changed(); Refresh(true); });
            DockPanel.SetDock(on, Dock.Right);
            head.Children.Add(on);
            var titles = new StackPanel();
            titles.Children.Add(Theme.Eyebrow("Base per car"));
            titles.Children.Add(Theme.Title("Rotation and force", 18));
            head.Children.Add(titles);
            body.Children.Add(head);
            body.Children.Add(Theme.Note(
                "Per car (or per game): the steering rotation and the overall force of your Simagic base, set through SimPro when the " +
                "car loads and put back to SimPro's preset when SimHub closes or this is turned off. SimPro's preset itself is never " +
                "changed. Empty = the preset's own value. Some games set the rotation themselves (e.g. iRacing, ACC with soft lock): " +
                "leave rotation empty there.", new Thickness(0, 6, 0, 10)));
            nowText = new TextBlock { Foreground = Theme.Text2, Margin = new Thickness(0, 0, 0, 10) };
            body.Children.Add(nowText);

            editors = new StackPanel();
            carTitle = Theme.Eyebrow("This car");
            gameTitle = Theme.Eyebrow("This game");
            body.Children.Add(editors);

            savedList = new StackPanel();
            body.Children.Add(new Expander { Header = "Every car and game with its own base settings", Content = savedList, Margin = new Thickness(0, 8, 0, 0) });

            Child = Theme.CardBox(body);
            timer.Tick += (s, e) => Refresh(false);
            Loaded += (s, e) => { timer.Start(); Refresh(true); };
            Unloaded += (s, e) => timer.Stop();
        }

        private void Changed() { plugin.SaveSettings(); plugin.Reapply(); }

        private void Refresh(bool force)
        {
            var car = plugin.DashCarKey;
            string game = plugin.CurrentGameForDash;
            nowText.Text = !S.Enabled ? "Off: the base uses SimPro's preset."
                         : plugin.BaseNow != null ? $"{plugin.BaseName ?? "Base"} now: {plugin.BaseNow}"
                         : "Waiting for a car (and for SimPro to see the base).";
            editors.IsEnabled = S.Enabled;
            editors.Opacity = S.Enabled ? 1 : 0.5;
            if (!force && car == shownCar) return;
            shownCar = car;
            editors.Children.Clear();
            if (car == null) editors.Children.Add(Theme.Note("Start a car to set its rotation and force."));
            else
            {
                editors.Children.Add(Row("This car: " + car, S.Cars, car));
                if (!string.IsNullOrEmpty(game)) editors.Children.Add(Row("This game: " + game + " (cars without their own)", S.Games, game));
            }
            savedList.Children.Clear();
            foreach (var (label, dict) in new[] { ("Car", S.Cars), ("Game", S.Games) })
                foreach (var kv in dict.Where(k => k.Value != null && !k.Value.IsEmpty).OrderBy(k => k.Key).ToList())
                {
                    var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var remove = Theme.Btn("Remove", () => { dict.Remove(kv.Key); Changed(); Refresh(true); });
                    DockPanel.SetDock(remove, Dock.Right);
                    line.Children.Add(remove);
                    line.Children.Add(new TextBlock
                    {
                        Text = $"{label}: {kv.Key}  ·  {(kv.Value.Angle != null ? kv.Value.Angle + "°" : "preset rotation")}  ·  {(kv.Value.Force != null ? kv.Value.Force + "% force" : "preset force")}",
                        VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Text2,
                    });
                    savedList.Children.Add(line);
                }
            if (savedList.Children.Count == 0) savedList.Children.Add(Theme.Note("None yet."));
        }

        private FrameworkElement Row(string title, System.Collections.Generic.Dictionary<string, BaseCarSetting> dict, string key)
        {
            dict.TryGetValue(key, out var c);
            var p = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            p.Children.Add(Theme.Eyebrow(title));
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var angle = new TextBox { Width = 90, Text = c?.Angle?.ToString() ?? "", ToolTip = $"degrees, {BaseSwitcher.MinAngle}-{BaseSwitcher.MaxAngle}; empty = the preset's" };
            var force = new TextBox { Width = 70, Text = c?.Force?.ToString() ?? "", ToolTip = "%, 0-100; empty = the preset's" };
            void Save()
            {
                int? a = int.TryParse(angle.Text.Trim(), out var av) ? Math.Max(BaseSwitcher.MinAngle, Math.Min(BaseSwitcher.MaxAngle, av)) : (int?)null;
                int? f = int.TryParse(force.Text.Trim(), out var fv) ? Math.Max(0, Math.Min(100, fv)) : (int?)null;
                if (a == null && f == null) dict.Remove(key); else dict[key] = new BaseCarSetting { Angle = a, Force = f };
                angle.Text = a?.ToString() ?? ""; force.Text = f?.ToString() ?? "";
                Changed(); Refresh(true);
            }
            angle.LostFocus += (s, e) => Save();
            force.LostFocus += (s, e) => Save();
            line.Children.Add(new TextBlock { Text = "Rotation", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            line.Children.Add(angle);
            line.Children.Add(new TextBlock { Text = "°", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 22, 0) });
            line.Children.Add(new TextBlock { Text = "Force", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            line.Children.Add(force);
            line.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            p.Children.Add(line);
            return p;
        }
    }
}
