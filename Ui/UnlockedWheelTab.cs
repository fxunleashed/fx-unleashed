using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode, the overview: the wheel drawn with what it shows (live while the plugin drives it, else a preview
    /// of your dash and lights), its state, the first-time setup, and shortcuts to the other tabs.
    /// </summary>
    public class UnlockedWheelTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly WheelView wheel = new WheelView { Width = 640 };
        private readonly LiveDashPreview dashPreview;
        private readonly LightEngine engine = new LightEngine();
        private readonly TextBlock state, detail, firmware, screenInfo, lightsInfo;
        private readonly System.Windows.Shapes.Ellipse stateDot;
        private readonly Button demoButton, sleepButton;
        private readonly Border setup;
        private readonly TextBlock step2;
        private readonly TextBlock dashTile, lightsTile, idleTile;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly DispatcherTimer dashTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        public UnlockedWheelTab(FXProRpmSyncPlugin plugin, Action<string> openTab)
        {
            this.plugin = plugin;
            dashPreview = new LiveDashPreview(wheel.Screen, () => plugin.Usb);

            // ----- First-time setup -----
            var steps = new StackPanel();
            steps.Children.Add(Theme.Eyebrow("Set up", Theme.Red));
            steps.Children.Add(Theme.Title("Unlock your wheel", 20));
            steps.Children.Add(Theme.Note("Unlocked mode needs the FXProDashes wheel firmware and the wheel's USB cable. Build 7 and later tell the plugin " +
                                          "themselves; builds 4-6 report the same as stock, so confirm them once:", new Thickness(0, 6, 0, 12)));
            steps.Children.Add(Step("1", "Flash the FXProDashes firmware (build 7, or 4-6) through SimPro."));
            steps.Children.Add(Step("2", "Plug the wheel's USB cable into this PC.", out step2));
            steps.Children.Add(Step("3", "Press Test: the demo dash stays steady for 8 seconds. On stock firmware the wheel's own dash flickers through it (harmless)."));
            var setupButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            setupButtons.Children.Add(Theme.Btn("Test on the wheel (8 s)", () => Usb?.RunTest(), icon: ""));
            steps.Children.Add(setupButtons);
            steps.Children.Add(Theme.Switch("My wheel runs the patched firmware", S.FirmwareConfirmed, v => { S.FirmwareConfirmed = v; Changed(); Refresh(); }));
            setup = Theme.CardBox(steps);
            setup.BorderBrush = Theme.Red;
            Children.Add(setup);

            // ----- Hero: the wheel + status -----
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stage = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 22, 18, 18), Margin = new Thickness(0, 0, 18, 0),
                Background = new RadialGradientBrush(Color.FromRgb(0x1C, 0x0A, 0x0D), Color.FromRgb(0x08, 0x09, 0x0B)) { RadiusX = 0.7, RadiusY = 0.8 },
                BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = wheel,
            };
            hero.Children.Add(stage);

            var status = new StackPanel();
            status.Children.Add(Theme.Eyebrow("Wheel"));
            var stateRow = new StackPanel { Orientation = Orientation.Horizontal };
            stateDot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            stateRow.Children.Add(stateDot);
            state = Theme.Title("", 24);
            stateRow.Children.Add(state);
            status.Children.Add(stateRow);
            detail = Theme.Note("", new Thickness(0, 6, 0, 14));
            status.Children.Add(detail);
            firmware = Info(status, "");
            screenInfo = Info(status, "");
            lightsInfo = Info(status, "");
            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            demoButton = Theme.Btn("Run the demo", () => { Usb?.SetDemo(!Usb.DemoOn); Refresh(); }, primary: true, icon: "");
            actions.Children.Add(demoButton);
            sleepButton = Theme.Btn("Sleep now", () => { if (Usb?.Sleeping == true) Usb.Wake(); else Usb?.SleepNow(); }, icon: "");
            actions.Children.Add(sleepButton);
            status.Children.Add(actions);
            status.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 14) });
            status.Children.Add(Shortcut("", "Dashes", out dashTile, () => openTab("Dashes")));
            status.Children.Add(Shortcut("", "Lights", out lightsTile, () => openTab("Lights")));
            status.Children.Add(Shortcut("", "Idle & sleep", out idleTile, () => openTab("Idle & sleep")));
            var statusCard = Theme.CardBox(status, 20, new Thickness(0));
            Grid.SetColumn(statusCard, 1);
            hero.Children.Add(statusCard);
            Children.Add(new Border { Margin = new Thickness(0, 0, 0, 14), Child = hero });
            Children.Add(new QuickControlsCard(plugin));
            Children.Add(WiringCard.Build());

            frameTimer.Tick += (s, e) => RenderLights();
            dashTimer.Tick += (s, e) => RenderScreen();
            slowTimer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { frameTimer.Start(); dashTimer.Start(); slowTimer.Start(); Refresh(); };
            Unloaded += (s, e) => { frameTimer.Stop(); dashTimer.Stop(); slowTimer.Stop(); dashPreview.Dispose(); };
            Refresh();
        }

        private void Changed() { Usb?.SettingsChanged(); plugin.SaveSettings(); }

        private static FrameworkElement Step(string n, string text) => Step(n, text, out _);

        private static FrameworkElement Step(string n, string text, out TextBlock body)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var num = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderBrush = Theme.Red, BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = n, FontFamily = Theme.Display, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Text };
            row.Children.Add(body);
            return row;
        }

        private static TextBlock Info(Panel parent, string glyph)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var icon = Theme.Icon(glyph, 14, Theme.Text3);
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2 };
            row.Children.Add(t);
            parent.Children.Add(row);
            return t;
        }

        private static Border Shortcut(string glyph, string title, out TextBlock value, Action open)
        {
            var body = new DockPanel();
            var arrow = Theme.Icon("", 12, Theme.Text3);
            DockPanel.SetDock(arrow, Dock.Right);
            body.Children.Add(arrow);
            var icon = Theme.Icon(glyph, 20, Theme.Red);
            icon.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Theme.Eyebrow(title));
            value = new TextBlock { FontFamily = Theme.Display, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, -4, 0, 0) };
            text.Children.Add(value);
            body.Children.Add(text);
            var tile = Theme.Tile(body, double.NaN, open);
            tile.Padding = new Thickness(14, 10, 14, 10);
            tile.Margin = new Thickness(0, 0, 0, 8);
            tile.Background = Theme.Raised;
            return tile;
        }

        private void Refresh()
        {
            var u = Usb;
            bool patched = u?.FirmwarePatched ?? S.FirmwareConfirmed;
            setup.Visibility = patched ? Visibility.Collapsed : Visibility.Visible;
            step2.Text = u?.WheelFound == true ? "Plug the wheel's USB cable into this PC.  ✓ Found it." : "Plug the wheel's USB cable into this PC.";
            string st = u?.State ?? "Off";
            state.Text = st;
            stateDot.Fill = SettingsControl.StateBrush(st);
            detail.Text = u?.Detail ?? "";
            firmware.Text = u?.WheelFound == true
                ? $"FX Pro wheel app {u.WheelVersion ?? "?"}" + (u.FirmwareBuild > 0 ? $" · patch build {u.FirmwareBuild}"
                    : S.FirmwareConfirmed ? " · patched firmware confirmed" : " · not confirmed yet")
                : "Wheel not found on USB";
            var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
            string dashName = wheelDash ? (id == null ? "the wheel's own dash" : DashCatalog.NameOf(id)) : DashCache.NameOf(id);
            screenInfo.Text = u?.Sleeping == true ? "Screen off (sleeping)"
                : u?.DashActive == true ? "Showing " + u.ActiveDashName
                : u?.SaverActive == true ? "Screensaver: " + IdleScreens.Find(S, u.SaverShown ?? S.SaverId).Name
                : (plugin.DashCarKey != null ? "This car: " : "Races show ") + dashName;
            lightsInfo.Text = !S.LightsEnabled ? "Lights: SimPro's" : S.LightsFrom != LightsSource.BuiltIn ? "Lights from " + (S.LightsFrom == LightsSource.AtsrHub ? "ATSR-Hub" : "SimHub's device") + (u?.Active == true ? " (" + u.LightsState + ")" : "") : "Lights: " + plugin.ActiveLightsFor(plugin.DashCarKey).Name;
            demoButton.Content = Label(u?.DemoOn == true ? "Stop the demo" : "Run the demo", u?.DemoOn == true ? "" : "");
            sleepButton.Content = Label(u?.Sleeping == true ? "Wake" : "Sleep now", u?.Sleeping == true ? "" : "");
            demoButton.IsEnabled = sleepButton.IsEnabled = patched;

            var rot = plugin.UsbRotation(plugin.DashCarKey, out _, out int cur);
            dashTile.Text = DashRef.Name(rot[((cur % rot.Count) + rot.Count) % rot.Count]) + (rot.Count > 1 ? $"  ·  1 of {rot.Count}" : "") +
                            (S.CarDashes.Count > 0 ? $"  ·  {S.CarDashes.Count} car" + (S.CarDashes.Count == 1 ? "" : "s") : "");
            lightsTile.Text = !S.LightsEnabled ? "SimPro's lights" : S.LightsFrom == LightsSource.AtsrHub ? "ATSR-Hub" : S.LightsFrom == LightsSource.SimHubDevice ? "SimHub device" : plugin.ActiveLightsFor(plugin.DashCarKey).Name;
            idleTile.Text = (S.ScreenSaver ? IdleScreens.Find(S, S.SaverId).Name : "No screensaver") + "  ·  " +
                            (S.SleepEnabled ? $"sleep after {S.SleepMinutes} min" : "no sleep");
        }

        private static object Label(string text, string glyph)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        /// <summary>The LEDs: the wheel's own frame while the plugin drives them, else the chosen lights on a simulated lap.</summary>
        private void RenderLights()
        {
            var u = Usb;
            if (u?.Sleeping == true) { wheel.Show(null); return; }
            var live = u?.Active == true ? u.LastFrame : null;
            double t = clock.Elapsed.TotalSeconds;
            wheel.Show(live ?? engine.Render(plugin.ActiveLightsFor(plugin.DashCarKey), SimLap.Values(t), null, t, false));
        }

        private void RenderScreen()
        {
            var u = Usb;
            if (u?.Sleeping == true) { dashPreview.Show(null, 0, 0); return; }
            if (u?.SaverActive == true)
            {
                dashPreview.Show(null, 0, 0);
                wheel.Screen.Source = DashPictures.Saver(IdleScreens.Find(S, u.SaverShown ?? S.SaverId));
                return;
            }
            var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
            if (wheelDash && u?.DemoOn != true)
            {
                dashPreview.Show(null, 0, 0);
                wheel.Screen.Source = DashSection.ThumbSource(id ?? plugin.WheelDash);
                return;
            }
            var d = DashCache.Find(Usb?.DemoDashId ?? id) ?? BuiltInDashes.MustangGt3();
            dashPreview.Show(d, S.PadLeft, S.PadTop);
            dashPreview.Tick();
        }
    }

    /// <summary>Simulated driving for the light previews: revs sweep up and flash, ABS and TC flicker now and then.</summary>
    internal static class SimLap
    {
        public static DashValues Values(double t)
        {
            double cycle = t % 6;
            double rpm = cycle < 4.5 ? 3000 + 5200 * Math.Pow(cycle / 4.5, 1.4) : 8200 - 5200 * (cycle - 4.5) / 1.5;
            var v = new DashValues
            {
                Running = true, Rpm = rpm, MaxRpm = 8400, Redline = 8000, GearKey = "3",
                AbsActive = t % 11 > 9.5, TcActive = t % 13 > 11.8, FuelPercent = 50,
                SpotterLeft = t % 17 > 15.5, SpotterRight = t % 19 > 17.2,
            };
            // settings changing now and then (the encoders' Levels effect)
            v.Set("absLevel", 2.0 + (int)(t / 5) % 7); v.Set("tcLevel", 9.0 - (int)((t + 2) / 7) % 6);
            v.Set("brakeBias", 52 + (int)((t + 4) / 9) % 8 * 1.5); v.Set("engineMap", 1.0 + (int)((t + 1) / 11) % 9);
            return v;
        }
    }

    /// <summary>Equal columns that wrap nothing: a Grid with one row.</summary>
    internal sealed class UniformGrid3
    {
        public readonly Grid Panel = new Grid();

        public void Add(FrameworkElement e)
        {
            if (Panel.ColumnDefinitions.Count > 0) Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition());
            e.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(e, Panel.ColumnDefinitions.Count - 1);
            Panel.Children.Add(e);
        }
    }
}
