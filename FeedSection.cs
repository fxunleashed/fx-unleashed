using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>"Dash values from SimHub" part of the settings page: on/off, status, options, live readout, overrides.</summary>
    public class FeedSection : StackPanel
    {
        private static readonly Brush CardBackground = Theme.Raised;
        private static readonly Brush CardBorder = Theme.Line;

        // Status banner tones: one color per state, so a glance says whether the dash shows SimHub's data.
        private static readonly Color Good = Color.FromRgb(0x3f, 0xb9, 0x50);
        private static readonly Color Warn = Color.FromRgb(0xf0, 0xa0, 0x20);
        private static readonly Color Bad = Color.FromRgb(0xe8, 0x47, 0x49);
        private static readonly Color Info = Color.FromRgb(0x3d, 0x8b, 0xfd);
        private static readonly Color Idle = Color.FromRgb(0x9a, 0xa0, 0xa6);
        // Segoe MDL2 Assets glyphs
        private const string IconOk = "\uE73E", IconWarn = "\uE7BA", IconError = "\uEA39", IconInfo = "\uE946", IconOff = "\uE711";

        /// <summary>Values SimHub only has in the raw game data, with what the wheel shows them as.</summary>
        private static readonly (string Field, string Label)[] RawFields =
        {
            ("tcCut", "TC2 / TC cut (tc2)"),
            ("ersMode", "ERS mode (ersm, 0-15)"),
            ("frontAntiRollBar", "Front ARB (farb)"),
            ("rearAntiRollBar", "Rear ARB (rarb)"),
            ("engineBraking", "Engine braking (eb)"),
            ("diffEntry", "Diff entry (entr)"),
            ("diffMiddle", "Diff mid (mid)"),
            ("diffExit", "Diff high speed (hspd)"),
            ("diffAdjOnThrottle", "Diff (diff)"),
            ("throttleShape", "Throttle shape (tps)"),
        };

        private readonly FXProRpmSyncPlugin plugin;
        private readonly TextBlock bannerIcon, bannerTitle, bannerDetail, bannerProblem, readout;
        private readonly Border banner, readoutCard;
        private readonly CheckBox enabled;
        private bool reverting;
        private string shownBanner;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        public FeedSection(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            Margin = new Thickness(0, 0, 0, 24);
            var fs = plugin.Settings.Feed;
            Children.Add(Muted("Sends SimHub's data to the wheel's own dashes through SimPro's \"SimGame\" source, so every value SimHub knows " +
                               "reaches them, in every game. SimPro picks its source when a game starts: turn this on before starting the game. " +
                               "Force feedback is unaffected." + (plugin.Unlocked ? " In unlocked mode it matters for cars showing the wheel's own dash." : ""), new Thickness(0, 0, 0, 12)));

            enabled = new CheckBox { Content = "Drive the wheel's dash from SimHub", IsChecked = fs.Enabled, Margin = new Thickness(0, 0, 0, 6) };
            var demo = new CheckBox
            {
                Content = "Demo: a simulated lap on the wheel's dash (no game running)",
                IsChecked = plugin.DemoOn, Margin = new Thickness(0, 0, 0, 8),
            };
            enabled.Checked += (s, e) => { if (!reverting) plugin.SetFeedEnabled(true); Refresh(); };
            enabled.Unchecked += (s, e) =>
            {
                if (reverting) return;
                if (plugin.TurningOffNeedsGameRestart && !ConfirmTurnOff())
                {
                    reverting = true;
                    enabled.IsChecked = true;
                    reverting = false;
                    return;
                }
                plugin.SetFeedEnabled(false);
                demo.IsChecked = false;
                Refresh();
            };
            demo.Checked += (s, e) => { plugin.SetDemo(true); enabled.IsChecked = plugin.Settings.Feed.Enabled; };
            demo.Unchecked += (s, e) => plugin.SetDemo(false);
            Children.Add(enabled);
            Children.Add(demo);

            // Status banner: icon + headline in the state's color, then what it means / what to do.
            bannerIcon = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            bannerTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            bannerDetail = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 4, 0, 0) };
            bannerProblem = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Frozen(Bad), Margin = new Thickness(0, 6, 0, 0) };
            var bannerText = new StackPanel();
            bannerText.Children.Add(bannerTitle);
            bannerText.Children.Add(bannerDetail);
            bannerText.Children.Add(bannerProblem);
            var bannerBody = new DockPanel();
            DockPanel.SetDock(bannerIcon, Dock.Left);
            bannerBody.Children.Add(bannerIcon);
            bannerBody.Children.Add(bannerText);
            banner = Card(bannerBody, new Thickness(0, 0, 0, 12));
            banner.BorderThickness = new Thickness(5, 1, 1, 1);
            Children.Add(banner);

            // Options
            var gaps = new ComboBox { Width = 380 };
            AddItem(gaps, "Auto: race = by position in my class, other sessions = on track", GapMode.Auto);
            AddItem(gaps, "By race position, in my class", GapMode.RaceClass);
            AddItem(gaps, "By race position, overall", GapMode.RaceOverall);
            AddItem(gaps, "On track (nearest car, any class or lap)", GapMode.OnTrack);
            Select(gaps, fs.Gaps);
            gaps.SelectionChanged += (s, e) => { fs.Gaps = (GapMode)((ComboBoxItem)gaps.SelectedItem).Tag; plugin.SaveSettings(); };
            Children.Add(Row("Gap ahead / behind", gaps));

            var delta = new ComboBox { Width = 380 };
            AddItem(delta, "Session best lap", DeltaSource.SessionBest);
            AddItem(delta, "All-time best lap", DeltaSource.AllTimeBest);
            Select(delta, fs.Delta);
            delta.SelectionChanged += (s, e) => { fs.Delta = (DeltaSource)((ComboBoxItem)delta.SelectedItem).Tag; plugin.SaveSettings(); };
            Children.Add(Row("Delta (gain / loss)", delta));

            // Live readout of what the wheel is being sent
            readout = new TextBlock { FontFamily = new FontFamily("Consolas"), Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
            var readoutBody = new StackPanel();
            readoutBody.Children.Add(Small("SENDING TO THE WHEEL (AS THE DASH SHOWS IT, IN SIMPRO'S BASE UNITS)"));
            readoutBody.Children.Add(readout);
            readoutCard = Card(readoutBody, new Thickness(0, 4, 0, 12));
            Children.Add(readoutCard);

            // Game-specific values
            var raw = new StackPanel();
            raw.Children.Add(Muted(
                "These aren't in SimHub's standard data. Built in: ACC's TC cut, and iRacing's in-car adjustments (dc* values) " +
                "for cars that have them. To use another value, enter a SimHub property path (as in SimHub's property list, " +
                "e.g. DataCorePlugin.GameRawData.Telemetry.dcAntiRollFront). Leave empty for the built-in value.", new Thickness(0, 0, 0, 8)));
            foreach (var (field, label) in RawFields)
            {
                fs.Overrides.TryGetValue(field, out var path);
                var box = new TextBox { Width = 460, Text = path ?? "" };
                var f = field;
                box.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(box.Text)) fs.Overrides.Remove(f);
                    else fs.Overrides[f] = box.Text.Trim();
                    plugin.SaveSettings();
                };
                raw.Children.Add(Row(label, box));
            }
            Children.Add(new Expander { Header = "Game-specific values", Content = raw, Margin = new Thickness(0, 0, 0, 4) });

            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { timer.Start(); Refresh(); };
            Unloaded += (s, e) => timer.Stop();
        }

        private bool ConfirmTurnOff()
        {
            var game = plugin.RunningGameName ?? "the game";
            return MessageBox.Show(Window.GetWindow(this),
                $"{game} is running and the wheel's dash is showing SimHub's data.\n\n" +
                $"If you turn this off now, SimPro switches to reading {game} directly and keeps it until the game closes. " +
                "Turning this back on won't do anything until you restart the game.\n\n" +
                "Turn it off anyway?",
                "FXPro Unlocked", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void SetBanner(Color tone, string icon, string title, string detail)
        {
            var key = title + "|" + detail;
            if (key == shownBanner) return;
            shownBanner = key;
            var brush = Frozen(tone);
            banner.BorderBrush = brush;
            banner.Background = Frozen(Color.FromArgb(0x1f, tone.R, tone.G, tone.B));
            bannerIcon.Foreground = brush;
            bannerIcon.Text = icon;
            bannerTitle.Foreground = brush;
            bannerTitle.Text = title;
            bannerDetail.Text = detail;
            bannerDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshBanner()
        {
            bool on = plugin.Settings.Feed.Enabled;
            var src = plugin.SimProSource;
            var game = plugin.RunningGameName;
            var status = plugin.FeedStatus ?? "";
            bool error = status.StartsWith("Error", StringComparison.Ordinal);
            // Mapper errors don't stop the feed: show them under the state instead of replacing it.
            bannerProblem.Text = on && error && plugin.FeedRunning ? status : "";
            bannerProblem.Visibility = bannerProblem.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            readoutCard.Opacity = on && src == null ? 1 : 0.45;

            if (!on && error)
                SetBanner(Bad, IconError, "Couldn't start sending SimHub's data", status);
            else if (!on)
                SetBanner(Idle, IconOff, "Off: SimPro reads the game directly",
                    "The dash shows SimPro's own game telemetry. Turn this on before starting a game to send SimHub's data instead.");
            else if (!plugin.FeedRunning)
                SetBanner(Bad, IconError, "SimGame helper isn't running",
                    "simgame.exe stopped, so SimPro can't read SimHub's data. Turn this off and on again." + (error ? "\n" + status : ""));
            else if (!plugin.SimProSourceKnown)
                SetBanner(Info, IconInfo, "Checking what SimPro is reading...", "");
            else if (!plugin.SimProReachable)
                SetBanner(Warn, IconWarn, "Can't reach SimPro Manager",
                    "SimPro Manager must be running for anything to reach the wheel. Start it and this updates by itself.");
            else if (src != null)
                SetBanner(Warn, IconWarn, $"SimPro is reading {src}, not SimHub",
                    $"The dash shows SimPro's own {src} telemetry: SimPro picked the game before SimHub's data was available, " +
                    "and it keeps that choice until the game closes.\n" +
                    "To fix: close the game and start it again (leave this on). If that doesn't help, restart SimPro Manager " +
                    "(tray icon > Exit, then start it) and, if needed, the wheelbase.");
            else if (plugin.DemoOn)
                SetBanner(Info, IconInfo, "Demo running on the dash", "The dash is animating a simulated lap. Untick Demo to stop it.");
            else if (game != null)
                SetBanner(Good, IconOk, "Dash is showing SimHub's data",
                    $"SimPro is reading SimHub's {game} data. Turning this off hands the dash to SimPro until you restart the game.");
            else
                SetBanner(Good, IconOk, "Ready: the dash will show SimHub's data",
                    "Start a game (with SimHub already running) and the dash follows SimHub's data.");
        }

        private void Refresh()
        {
            RefreshBanner();
            if (!plugin.Settings.Feed.Enabled)
            {
                readout.Text = "(off)";
                return;
            }

            var t = plugin.FeedSnapshot;
            if (t.Get("isGameRunning") == 0) { readout.Text = "No game running in SimHub (tick Demo to animate the dash)."; return; }
            string gear = t.Get("gear") < 0 ? "R" : t.Get("gear") == 0 ? "N" : t.Get("gear").ToString("0");
            readout.Text =
                $"gear {gear}   speed {t.Get("speed"):0} km/h   rpm {t.Get("rpm"):0} / {t.Get("maxRpm"):0}\n" +
                $"pos {t.Get("position"):0}   lap {t.Get("completedLaps"):0}   last {Lap(t.Get("lastLapTime"))}   best {Lap(t.Get("bestLapTime"))}   delta {t.Get("gainLoss"):+0.00;-0.00}\n" +
                $"gap ahead {t.Get("gapAhead") / 100:0.0}   gap behind -{t.Get("gapBehind") / 100:0.0}\n" +
                $"fuel {t.Get("fuel"):0.0} L   per lap {t.Get("fuelPerLap") / 10:0.0} L   bias {t.Get("brakeBias"):0.0}%   ABS {t.Get("absLevel"):0}  TC {t.Get("tcLevel"):0}/{t.Get("tcCut"):0}  map {t.Get("engineMap"):0}\n" +
                $"tyres °C   {t.Get("tyreTemperature0"):0} {t.Get("tyreTemperature1"):0} {t.Get("tyreTemperature2"):0} {t.Get("tyreTemperature3"):0}" +
                $"   psi {t.Get("tyrePressure0"):0.0} {t.Get("tyrePressure1"):0.0} {t.Get("tyrePressure2"):0.0} {t.Get("tyrePressure3"):0.0}" +
                $"   wear % {t.Get("tyreWear0"):0} {t.Get("tyreWear1"):0} {t.Get("tyreWear2"):0} {t.Get("tyreWear3"):0}\n" +
                $"brakes °C {t.Get("brakeTemperature0"):0} {t.Get("brakeTemperature1"):0} {t.Get("brakeTemperature2"):0} {t.Get("brakeTemperature3"):0}" +
                $"   water {t.Get("waterTemperature"):0} °C   oil {t.Get("oilTemperature"):0} °C";
        }

        private static string Lap(double ms)
        {
            if (ms <= 0) return "-";
            var ts = TimeSpan.FromMilliseconds(ms);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        private static void AddItem(ComboBox box, string text, object tag) => box.Items.Add(new ComboBoxItem { Content = text, Tag = tag });

        private static void Select(ComboBox box, object tag) =>
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tag)) ?? box.Items[0];

        private static Border Card(UIElement child, Thickness margin) => new Border
        {
            Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = margin, Child = child, MaxWidth = 1060,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        private static TextBlock Muted(string text, Thickness margin) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = margin, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

        private static TextBlock Small(string text) =>
            new TextBlock { Text = text, FontSize = 10, Opacity = 0.55, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };

        private static FrameworkElement Row(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var l = new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            DockPanel.SetDock(l, Dock.Left);
            row.Children.Add(l);
            if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(control);
            return row;
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
