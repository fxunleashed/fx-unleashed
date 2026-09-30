using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Settings page, built in code (no XAML files): a header with live status, the mode (Standard = stock wheel through
    /// SimPro, Unlocked = flashed wheel over USB) as two cards, and the mode's tabs. Tabs are built when first opened and
    /// only the open one is on screen, so only it animates. Everything follows the active wheel (FX Pro or GT Neo): a chip
    /// in the header names it, and turns into a switch when both wheels are connected; a switch rebuilds the page.
    /// </summary>
    public class SettingsControl : UserControl
    {
        private readonly FXProRpmSyncPlugin plugin;
        private Border standardCard, unlockedCard;
        private readonly Grid modes;
        private readonly Border wheelChip;
        private readonly TextBlock wheelChipText, wheelChipIcon;
        /// <summary>The wheel the page is built for (rebuilt when plugin.ActiveModel changes).</summary>
        private WheelModel shownModel;
        private readonly StackPanel tabStrip;
        private readonly ContentControl tabHost = new ContentControl();
        private readonly Dictionary<string, FrameworkElement> built = new Dictionary<string, FrameworkElement>();
        private readonly TextBlock wheelPill, carPill;
        private readonly Border feedProblem, updateBanner;
        private readonly TextBlock updateText;
        private readonly TextBlock feedProblemTitle, feedProblemAction, feedProblemDetail;
        private readonly Ellipse wheelDot, carDot;
        private readonly DispatcherTimer statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        // the last tab per mode (for this SimHub session)
        private static readonly Dictionary<WheelMode, string> lastTab = new Dictionary<WheelMode, string>();

        private WheelMode Mode => plugin.Settings.Mode;

        public SettingsControl(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            Theme.Apply(this);
            plugin.OpenDashesFor = key =>
            {
                Open("Dashes");
                if (tabHost.Content is UnlockedDashesTab t) t.Target(key);
            };
            Background = Theme.Page;

            var page = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Left };

            // ----- Header -----
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22), LastChildFill = true };
            var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // the wheel in use; with both wheels connected, a click switches to the other one
            var chipBody = new StackPanel { Orientation = Orientation.Horizontal };
            wheelChipIcon = new TextBlock { Text = "\uE8AB", FontFamily = Theme.Icons, FontSize = 12, Foreground = Theme.Red, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            chipBody.Children.Add(wheelChipIcon);
            wheelChipText = new TextBlock { FontFamily = Theme.Display, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center };
            chipBody.Children.Add(wheelChipText);
            wheelChip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 6), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1),
                BorderBrush = Theme.Line2, Background = Theme.Raised, VerticalAlignment = VerticalAlignment.Center, Child = chipBody,
            };
            wheelChip.MouseLeftButtonUp += (s, e) => SwitchToOther();
            pills.Children.Add(wheelChip);
            pills.Children.Add(Theme.Pill(out wheelPill, out wheelDot));
            pills.Children.Add(Theme.Pill(out carPill, out carDot));
            DockPanel.SetDock(pills, Dock.Right);
            header.Children.Add(pills);
            var logo = new Image { Source = Theme.Resource("logo-nobg.png"), Width = 58, Height = 58, Margin = new Thickness(0, 0, 14, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            logo.Effect = new DropShadowEffect { Color = Theme.RedC, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 };
            DockPanel.SetDock(logo, Dock.Left);
            header.Children.Add(logo);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { FontFamily = Theme.Display, FontSize = 26, FontWeight = FontWeights.Bold };
            name.Inlines.Add(new System.Windows.Documents.Run("FX") { Foreground = Theme.Red });
            name.Inlines.Add(new System.Windows.Documents.Run(" UNLEASHED") { Foreground = Theme.Text, FontWeight = FontWeights.Light });
            titles.Children.Add(name);
            titles.Children.Add(new TextBlock { Text = $"Custom dashes and lights for the Simagic FX Pro  ·  v{Updater.CurrentVersion}", Foreground = Theme.Text3, FontSize = 12 });
            header.Children.Add(titles);
            page.Children.Add(header);
            page.Children.Add(feedProblem = FeedProblemBanner(out feedProblemTitle, out feedProblemAction, out feedProblemDetail));
            page.Children.Add(updateBanner = UpdateBanner(out updateText));

            // ----- Mode -----
            modes = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            modes.ColumnDefinitions.Add(new ColumnDefinition());
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            modes.ColumnDefinitions.Add(new ColumnDefinition());
            page.Children.Add(modes);

            // ----- Tabs -----
            tabStrip = new StackPanel { Orientation = Orientation.Horizontal };
            page.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 20), Child = tabStrip });
            page.Children.Add(tabHost);

            page.Children.Add(new TextBlock
            {
                Text = "Car rev light data: Lovely Car Data by Lovely Sim Racing and contributors (github.com/Lovely-Sim-Racing/lovely-car-data), CC BY-NC-SA 4.0.",
                Foreground = Theme.Text3, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 24, 0, 0),
            });

            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page, Background = Theme.Page };

            statusTimer.Tick += (s, e) => RefreshStatus();
            Loaded += (s, e) => { statusTimer.Start(); RefreshStatus(); };
            Unloaded += (s, e) => statusTimer.Stop();

            BuildModeCards();
            ShowMode();
        }

        /// <summary>The two mode cards, worded for the active wheel.</summary>
        private void BuildModeCards()
        {
            shownModel = plugin.ActiveModel;
            modes.Children.Clear();
            if (shownModel == WheelModel.GtNeo)
            {
                standardCard = ModeCard(Glyph(0), "STANDARD", "Through SimPro",
                    "The rev lights follow each car, through SimPro.",
                    new[] { "Rev lights per car" }, WheelMode.Standard);
                unlockedCard = ModeCard(Glyph(1), "USB", "Button 3 at power-up · over USB",
                    "The plugin drives every light on the wheel itself: presets, each car's shift lights, alerts and sleep.",
                    new[] { "Every light", "Shift lights per car", "Alerts", "Sleep" }, WheelMode.Unlocked);
            }
            else
            {
                standardCard = ModeCard(Glyph(0), "STANDARD", "Stock wheel · over RF",
                    "Nothing to flash. The rev lights follow each car and the wheel switches to each car's dash, through SimPro.",
                    new[] { "Rev lights per car", "Dash per car", "SimHub data on wheel dashes" }, WheelMode.Standard);
                unlockedCard = ModeCard(Glyph(1), "UNLEASHED", "Flashed wheel · over USB",
                    "The plugin drives the wheel itself: your own dashes per car, all 38 lights, screensavers and sleep.",
                    new[] { "Custom dashes", "Every light", "Screensavers", "Sleep" }, WheelMode.Unlocked);
            }
            Grid.SetColumn(unlockedCard, 2);
            modes.Children.Add(standardCard);
            modes.Children.Add(unlockedCard);
        }

        /// <summary>The other connected wheel, or null when only one is connected.</summary>
        private WheelModel OtherConnected()
        {
            var connected = plugin.ConnectedWheels.Select(w => w.Model).Distinct().ToList();
            return connected.Count > 1 ? connected.FirstOrDefault(m => m != plugin.ActiveModel) : null;
        }

        private void SwitchToOther()
        {
            var other = OtherConnected();
            if (other == null) return;
            plugin.SwitchWheel(other);
            RefreshStatus();
        }

        /// <summary>Rebuilds everything that depends on the wheel (mode cards, tabs) after a switch.</summary>
        private void ShowWheel()
        {
            BuildModeCards();
            built.Clear();
            ShowMode();
        }

        /// <summary>For the UI test: opens a tab by its name.</summary>
        internal void OpenTab(string name) => Open(name);

        internal IEnumerable<string> TabNames => Tabs().Select(t => t.Name);

        private static string Glyph(int card) => card == 0 ? "\uE701" : "\uE88E";

        private Border ModeCard(string glyph, string title, string tag, string text, string[] features, WheelMode mode)
        {
            var body = new StackPanel();
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var badge = new Border
            {
                Background = Theme.Red, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "ACTIVE", FontFamily = Theme.Display, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
                Name = "badge",
            };
            DockPanel.SetDock(badge, Dock.Right);
            top.Children.Add(badge);
            var icon = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(9), Background = Theme.Raised, Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Text },
            };
            DockPanel.SetDock(icon, Dock.Left);
            top.Children.Add(icon);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = title, FontFamily = Theme.Display, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Theme.Text });
            titles.Children.Add(new TextBlock { Text = tag, FontSize = 12, Foreground = Theme.Text3 });
            top.Children.Add(titles);
            body.Children.Add(top);
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 12), LineHeight = 18 });
            var chips = new WrapPanel();
            foreach (var f in features)
                chips.Children.Add(new Border
                {
                    BorderBrush = Theme.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = f, FontSize = 11.5, Foreground = Theme.Text2 },
                });
            body.Children.Add(chips);

            var card = new Border
            {
                CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Padding = new Thickness(18, 16, 18, 12), Cursor = Cursors.Hand, Child = body,
                Tag = badge,
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (Mode == mode) return;
                plugin.SetMode(mode);
                ShowMode();
            };
            return card;
        }

        private void ShowMode()
        {
            StyleModeCard(standardCard, Mode == WheelMode.Standard);
            StyleModeCard(unlockedCard, Mode == WheelMode.Unlocked);
            tabStrip.Children.Clear();
            foreach (var (tabName, glyph, _) in Tabs())
            {
                var n = tabName;
                var label = new TextBlock { Text = tabName.ToUpperInvariant(), FontFamily = Theme.Display, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(label);
                var tab = new Border
                {
                    Padding = new Thickness(2, 10, 2, 11), Margin = new Thickness(0, 0, 26, -1), BorderThickness = new Thickness(0, 0, 0, 3),
                    Cursor = Cursors.Hand, Child = sp, Tag = n, Background = Brushes.Transparent,
                };
                tab.MouseLeftButtonUp += (s, e) => Open(n);
                tabStrip.Children.Add(tab);
            }
            var names = Tabs().Select(t => t.Name).ToList();
            Open(lastTab.TryGetValue(Mode, out var last) && names.Contains(last) ? last : names[0]);
        }

        private static void StyleModeCard(Border card, bool on)
        {
            card.BorderBrush = on ? Theme.Red : Theme.Line;
            card.Background = on
                ? new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0x1F, 0x2D), Color.FromArgb(0x08, 0xFF, 0x1F, 0x2D), 30)
                : (Brush)Theme.Card;
            card.Effect = on ? new DropShadowEffect { Color = Theme.RedC, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.35 } : null;
            card.Opacity = on ? 1 : 0.8;
            ((Border)card.Tag).Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private IEnumerable<(string Name, string Glyph, Func<FrameworkElement> Build)> Tabs()
        {
            bool screen = plugin.ActiveModel.HasScreen;
            if (Mode == WheelMode.Standard)
            {
                yield return ("Rev lights", "", () => new StandardLightsTab(plugin));
                if (screen) yield return ("Dashes", "", () => Wrap(new DashSection(plugin)));
                yield return ("Car tuning", "", () => CarTuning());
                if (screen) yield return ("Dash data", "", () => Wrap(new FeedSection(plugin)));
                yield return ("About", "", () => new AboutTab(plugin));
            }
            else
            {
                yield return ("Wheel", "", () => new UnlockedWheelTab(plugin, Open));
                if (screen) yield return ("Dashes", "", () => new UnlockedDashesTab(plugin));
                yield return ("Lights", "", () => new UnlockedLightsTab(plugin));
                yield return ("Idle & sleep", "", () => new UnlockedIdleTab(plugin));
                yield return ("Car tuning", "", () => CarTuning());
                yield return ("About", "", () => new AboutTab(plugin));
            }
        }

        /// <summary>Car tuning: the base per car (both modes), then the rev light overrides.</summary>
        private FrameworkElement CarTuning()
        {
            var p = new StackPanel();
            p.Children.Add(new CalibrationCard(plugin));
            p.Children.Add(new BaseCard(plugin));
            p.Children.Add(Wrap(new OverridesSection(plugin)));
            return p;
        }

        private static FrameworkElement Wrap(FrameworkElement old)
        {
            old.Margin = new Thickness(0);
            return Theme.CardBox(old, 22);
        }

        private void Open(string name)
        {
            var tab = Tabs().FirstOrDefault(t => t.Name == name);
            if (tab.Build == null) return;
            lastTab[Mode] = name;
            string key = Mode + "|" + plugin.ActiveModel.Id + "|" + name;
            if (!built.TryGetValue(key, out var content)) built[key] = content = tab.Build();
            tabHost.Content = content;
            foreach (Border t in tabStrip.Children)
            {
                bool on = (string)t.Tag == name;
                t.BorderBrush = on ? Theme.Red : Brushes.Transparent;
                System.Windows.Documents.TextElement.SetForeground(t, on ? Theme.Text : Theme.Text3);
            }
        }

        /// <summary>
        /// Red, big, on every tab: SimPro is reading the game instead of SimHub's data (FXProRpmSyncPlugin.FeedProblem),
        /// with the one thing that fixes it.
        /// </summary>
        private static Border FeedProblemBanner(out TextBlock title, out TextBlock action, out TextBlock detail)
        {
            var body = new DockPanel();
            var icon = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 30, Foreground = Theme.Red, Margin = new Thickness(0, 2, 18, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
            var text = new StackPanel();
            title = new TextBlock { FontFamily = Theme.Display, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
            action = new TextBlock { FontFamily = Theme.Display, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Theme.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            detail = new TextBlock { Foreground = Theme.Text3, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            text.Children.Add(title);
            text.Children.Add(action);
            text.Children.Add(detail);
            body.Children.Add(text);
            return new Border
            {
                Child = body, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Theme.Red,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x1F, 0x2D)), Padding = new Thickness(22, 18, 22, 18),
                Margin = new Thickness(0, 0, 0, 22), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { Color = Theme.RedC, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.45 },
            };
        }

        /// <summary>A new version (or a start that didn't finish after an update): one line and a button to the About tab.</summary>
        private Border UpdateBanner(out TextBlock text)
        {
            var row = new DockPanel();
            var open = Theme.Btn("Updates", () => Open("About"), primary: true, icon: "");
            open.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text };
            row.Children.Add(text);
            return new Border
            {
                Child = row, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Theme.Blue,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x3B, 0x82, 0xF6)), Padding = new Thickness(16, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 18), Visibility = Visibility.Collapsed,
            };
        }

        private void RefreshUpdateBanner()
        {
            var u = plugin.Updates;
            string msg = null;
            if (u?.AutoRolledBackFrom != null) msg = $"v{u.AutoRolledBackFrom} failed to start three times, so the previous version was put back. Restart SimHub to use it.";
            else if (u?.StartupFailedBefore == true && u.PreviousVersion != null) msg = $"The last start of v{Updater.CurrentVersion} didn't finish. If something's wrong, you can roll back to v{u.PreviousVersion}.";
            else if (u?.UpdateAvailable == true && u.Latest.Version.ToString() != plugin.Settings.Updates.SkippedVersion)
                msg = $"FX Unleashed v{u.Latest.Version} is available (you have v{Updater.CurrentVersion}). Provided as is, without warranty.";
            else if (u?.State == Updater.UpdateState.Installed) msg = u.Message;
            updateBanner.Visibility = msg != null ? Visibility.Visible : Visibility.Collapsed;
            if (msg != null) updateText.Text = msg;
        }

        private void RefreshStatus()
        {
            if (plugin.ActiveModel != shownModel) ShowWheel();
            var other = OtherConnected();
            wheelChipText.Text = shownModel.Name + (other != null ? "   ·   switch to " + other.Name : "");
            wheelChip.Cursor = other != null ? Cursors.Hand : null;
            wheelChipIcon.Visibility = other != null ? Visibility.Visible : Visibility.Collapsed; // the switch icon only when there's something to switch to
            wheelChip.BorderBrush = other != null ? Theme.Red : Theme.Line2;
            wheelChip.ToolTip = other != null ? $"Both wheels are connected. The pages are for the {shownModel.Name}; click to set up the {other.Name} instead."
                                              : $"The pages are for the {shownModel.Name} (the wheel found).";
            RefreshUpdateBanner();
            feedProblem.Visibility = plugin.FeedProblem ? Visibility.Visible : Visibility.Collapsed;
            if (plugin.FeedProblem)
            {
                feedProblemTitle.Text = plugin.FeedProblemTitle;
                feedProblemAction.Text = plugin.FeedProblemAction;
                feedProblemDetail.Text = plugin.FeedProblemDetail;
            }
            if (Mode == WheelMode.Unlocked)
            {
                var u = plugin.Usb;
                string state = u?.State ?? "Off";
                wheelPill.Text = "USB  ·  " + state + (u?.WheelVersion != null && shownModel == WheelModel.FxPro ? "  ·  app " + u.WheelVersion : "");
                wheelDot.Fill = StateBrush(state);
            }
            else
            {
                bool error = plugin.Status?.StartsWith("Error") == true;
                wheelPill.Text = "SimPro  ·  " + (error ? "not reachable" : plugin.Settings.Enabled ? "syncing" : "rev lights off");
                wheelDot.Fill = error ? Theme.Red : plugin.Settings.Enabled ? Theme.Green : Theme.Text3;
            }
            string car = plugin.CurrentCarNameForDash;
            carPill.Text = car != null ? car + "  ·  " + plugin.CurrentGameForDash : plugin.GameRunning ? "In game  ·  no car yet" : "No game running";
            carDot.Fill = car != null ? Theme.Green : Theme.Text3;
        }

        internal static Brush StateBrush(string state)
        {
            switch (state)
            {
                case "Active": case "Demo": case "Test": case "Lights on": case "Standing by": case "Designer preview": return Theme.Green;
                case "Ready": return Theme.Blue;
                case "Sleeping": return Theme.B("#8B7CF6");
                case "Error": case "Unsupported wheel firmware": return Theme.Red;
                case "Off": return Theme.Text3;
                default: return Theme.Amber;
            }
        }
    }
}
