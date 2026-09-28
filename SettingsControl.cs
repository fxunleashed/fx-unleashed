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
    /// only the open one is on screen, so only it animates.
    /// </summary>
    public class SettingsControl : UserControl
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly Border standardCard, unlockedCard;
        private readonly StackPanel tabStrip;
        private readonly ContentControl tabHost = new ContentControl();
        private readonly Dictionary<string, FrameworkElement> built = new Dictionary<string, FrameworkElement>();
        private readonly TextBlock wheelPill, carPill;
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
            name.Inlines.Add(new System.Windows.Documents.Run("PRO ") { Foreground = Theme.Text });
            name.Inlines.Add(new System.Windows.Documents.Run("UNLOCKED") { Foreground = Theme.Text, FontWeight = FontWeights.Light });
            titles.Children.Add(name);
            var version = typeof(SettingsControl).Assembly.GetName().Version;
            titles.Children.Add(new TextBlock { Text = $"Simagic FX Pro companion  ·  v{version.Major}.{version.Minor}.{version.Build}", Foreground = Theme.Text3, FontSize = 12 });
            header.Children.Add(titles);
            page.Children.Add(header);

            // ----- Mode -----
            var modes = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            modes.ColumnDefinitions.Add(new ColumnDefinition());
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            modes.ColumnDefinitions.Add(new ColumnDefinition());
            standardCard = ModeCard("", "STANDARD", "Stock wheel · over RF",
                "Nothing to flash. The rev lights follow each car and the wheel switches to each car's dash, through SimPro.",
                new[] { "Rev lights per car", "Dash per car", "SimHub data on wheel dashes" }, WheelMode.Standard);
            unlockedCard = ModeCard("", "UNLOCKED", "Flashed wheel · over USB",
                "The plugin drives the wheel itself: your own dashes per car, all 38 lights, screensavers and sleep.",
                new[] { "Custom dashes", "Every light", "Screensavers", "Sleep" }, WheelMode.Unlocked);
            Grid.SetColumn(unlockedCard, 2);
            modes.Children.Add(standardCard);
            modes.Children.Add(unlockedCard);
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

            ShowMode();
        }

        /// <summary>For the UI test: opens a tab by its name.</summary>
        internal void OpenTab(string name) => Open(name);

        internal IEnumerable<string> TabNames => Tabs().Select(t => t.Name);

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
            if (Mode == WheelMode.Standard)
            {
                yield return ("Rev lights", "", () => new StandardLightsTab(plugin));
                yield return ("Dashes", "", () => Wrap(new DashSection(plugin)));
                yield return ("Car tuning", "", () => Wrap(new OverridesSection(plugin)));
                yield return ("Dash data", "", () => Wrap(new FeedSection(plugin)));
            }
            else
            {
                yield return ("Wheel", "", () => new UnlockedWheelTab(plugin, Open));
                yield return ("Dashes", "", () => new UnlockedDashesTab(plugin));
                yield return ("Lights", "", () => new UnlockedLightsTab(plugin));
                yield return ("Idle & sleep", "", () => new UnlockedIdleTab(plugin));
                yield return ("Car tuning", "", () => Wrap(new OverridesSection(plugin)));
            }
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
            string key = Mode + "|" + name;
            if (!built.TryGetValue(key, out var content)) built[key] = content = tab.Build();
            tabHost.Content = content;
            foreach (Border t in tabStrip.Children)
            {
                bool on = (string)t.Tag == name;
                t.BorderBrush = on ? Theme.Red : Brushes.Transparent;
                System.Windows.Documents.TextElement.SetForeground(t, on ? Theme.Text : Theme.Text3);
            }
        }

        private void RefreshStatus()
        {
            if (Mode == WheelMode.Unlocked)
            {
                var u = plugin.Usb;
                string state = u?.State ?? "Off";
                wheelPill.Text = "USB  ·  " + state + (u?.WheelVersion != null ? "  ·  app " + u.WheelVersion : "");
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
