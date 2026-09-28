using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "USB mode" part of the settings page: the custom-firmware wheel over its own USB. Status, the custom dash with a
    /// live preview (drawn the way the screen draws it), and the wheel's lights: presets with animated previews and an
    /// editor for every group, the rev lights and the alerts.
    /// </summary>
    public class UsbSection : StackPanel
    {
        private static readonly Brush CardBackground = Frozen(Color.FromArgb(0x14, 0xff, 0xff, 0xff));
        private static readonly Brush CardBorder = Frozen(Color.FromArgb(0x26, 0xff, 0xff, 0xff));
        private static readonly Brush Accent = Frozen(Color.FromRgb(0x3d, 0x8b, 0xfd));
        private static readonly Color Good = Color.FromRgb(0x3f, 0xb9, 0x50);
        private static readonly Color Warn = Color.FromRgb(0xf0, 0xa0, 0x20);
        private static readonly Color Bad = Color.FromRgb(0xe8, 0x47, 0x49);
        private static readonly Color Info = Color.FromRgb(0x3d, 0x8b, 0xfd);
        private static readonly Color Idle = Color.FromRgb(0x9a, 0xa0, 0xa6);

        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly Border banner;
        private readonly TextBlock bannerTitle, bannerDetail, bannerIcon;
        private readonly Button demoButton;
        private readonly StackPanel body;

        // Dash
        private readonly ComboBox dashBox;
        private readonly Image preview;
        private readonly TextBlock dashInfo, dashProblems;
        private List<DashDefinition> dashes;
        private DashRenderer previewRenderer;
        private PreviewScreen previewScreen;
        private string previewKey;
        private UsbDemo previewDemo;
        private double previewLast;

        // Lights
        private readonly WrapPanel presetGallery;
        private readonly StackPanel atsrPanel, builtInPanel;
        private readonly ComboBox atsrDevice;
        private readonly TextBlock atsrState, atsrMapError;
        private readonly List<(string Id, Border Card, WheelLedPreview Preview)> presetCards = new List<(string, Border, WheelLedPreview)>();
        private readonly WheelLedPreview bigPreview;
        private readonly TextBlock bigTitle;
        private readonly StackPanel editor;
        private readonly LightEngine previewEngine = new LightEngine();
        private readonly Dictionary<string, LightEngine> cardEngines = new Dictionary<string, LightEngine>();

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private bool loading;

        public UsbSection(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            Margin = new Thickness(0, 0, 0, 24);

            Children.Add(new TextBlock { Text = "USB mode (custom wheel firmware)", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            Children.Add(Muted("For an FX Pro running the FXProDashes wheel firmware, with its USB cable plugged into this PC. While a game " +
                               "runs, the plugin draws its own dash on the wheel's screen and drives every light on the wheel (buttons, " +
                               "encoders, side lights and rev lights) straight over USB. When the game closes, the wheel's own dash and " +
                               "SimPro's lights come back. SimPro keeps running as usual (force feedback, settings).", new Thickness(0, 0, 0, 12)));

            // ----- Status -----
            bannerIcon = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            bannerTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            bannerDetail = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 4, 0, 0) };
            var bannerText = new StackPanel();
            bannerText.Children.Add(bannerTitle);
            bannerText.Children.Add(bannerDetail);
            var bannerBody = new DockPanel();
            DockPanel.SetDock(bannerIcon, Dock.Left);
            bannerBody.Children.Add(bannerIcon);
            bannerBody.Children.Add(bannerText);
            banner = Card(bannerBody, new Thickness(0, 0, 0, 12));
            banner.BorderThickness = new Thickness(5, 1, 1, 1);
            Children.Add(banner);

            var enabled = new CheckBox { Content = "Use USB mode", IsChecked = S.Enabled, Margin = new Thickness(0, 0, 0, 6) };
            enabled.Checked += (s, e) => { S.Enabled = true; Changed(); body.Visibility = Visibility.Visible; };
            enabled.Unchecked += (s, e) => { S.Enabled = false; Changed(); body.Visibility = Visibility.Collapsed; };
            Children.Add(enabled);

            body = new StackPanel { Visibility = S.Enabled ? Visibility.Visible : Visibility.Collapsed };
            Children.Add(body);

            var firmware = new CheckBox
            {
                Content = "My wheel runs the FXProDashes firmware (build 4, flashed through SimPro)",
                IsChecked = S.FirmwareConfirmed, Margin = new Thickness(0, 0, 0, 4),
            };
            firmware.Checked += (s, e) => { S.FirmwareConfirmed = true; Changed(); };
            firmware.Unchecked += (s, e) => { S.FirmwareConfirmed = false; Changed(); };
            body.Children.Add(firmware);
            body.Children.Add(Muted("The patched firmware reports the same version as stock, so the plugin can't tell them apart. Press Test: " +
                                    "with the patched firmware the wheel shows the demo dash steadily for 8 seconds; with stock firmware the " +
                                    "wheel's own dash keeps flickering through it (harmless, it goes back to normal right after).",
                                    new Thickness(24, 0, 0, 8)));

            var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            buttons.Children.Add(MakeButton("Test on the wheel (8 s)", () => Usb?.RunTest()));
            demoButton = MakeButton("Start the demo on the wheel", () => { Usb?.SetDemo(!Usb.DemoOn); UpdateDemoButton(); });
            buttons.Children.Add(demoButton);
            body.Children.Add(buttons);

            // ----- Dash -----
            body.Children.Add(Heading("Dash"));
            var showDash = new CheckBox { Content = "Draw a custom dash on the wheel's screen", IsChecked = S.DashEnabled, Margin = new Thickness(0, 0, 0, 8) };
            showDash.Checked += (s, e) => { S.DashEnabled = true; Changed(); };
            showDash.Unchecked += (s, e) => { S.DashEnabled = false; Changed(); };
            body.Children.Add(showDash);
            var saverBox = new CheckBox { Content = "Show the logo on the wheel's screen when you're not racing (instead of the wheel's own dash)", IsChecked = S.ScreenSaver, Margin = new Thickness(0, 0, 0, 8) };
            saverBox.Checked += (s, e) => { S.ScreenSaver = true; Changed(); };
            saverBox.Unchecked += (s, e) => { S.ScreenSaver = false; Changed(); };
            body.Children.Add(saverBox);

            // Dash designer (local web page + API for agents)
            var designerRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            var openDesigner = MakeButton("Open the dash designer", OpenDesigner);
            designerRow.Children.Add(openDesigner);
            designerInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Margin = new Thickness(4, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            designerRow.Children.Add(designerInfo);
            body.Children.Add(designerRow);
            body.Children.Add(Muted("Design dashes in your browser, import SimHub dashes and fine-tune them, and see your changes on the wheel as you " +
                                    "make them. AI agents can use the same thing through its API (GET /api) or the fxdash command line.", new Thickness(0, 0, 0, 10)));

            dashBox = new ComboBox { Width = 360 };
            dashBox.SelectionChanged += (s, e) =>
            {
                if (loading || !(dashBox.SelectedItem is ComboBoxItem i)) return;
                S.DashId = (string)i.Tag;
                Changed();
                ShowDashInfo();
            };
            body.Children.Add(Row("Dash", dashBox));
            dashInfo = Muted("", new Thickness(150, -4, 0, 8));
            body.Children.Add(dashInfo);

            body.Children.Add(Row("Padding left", Slider(0, 20, S.PadLeft, v => { S.PadLeft = v; Changed(); previewKey = null; }, "{0} px")));
            body.Children.Add(Row("Padding top", Slider(0, 38, S.PadTop, v => { S.PadTop = v; Changed(); previewKey = null; }, "{0} px")));

            preview = new Image { Width = 640, Height = 384, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            var previewBody = new StackPanel();
            previewBody.Children.Add(Small("PREVIEW (TEXT IN STAND-IN LETTERS AT THE SCREEN FONTS' REAL SIZES)"));
            previewBody.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(0), Child = preview, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) });
            dashProblems = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Frozen(Warn), Margin = new Thickness(0, 8, 0, 0) };
            previewBody.Children.Add(dashProblems);
            body.Children.Add(Card(previewBody, new Thickness(0, 4, 0, 8)));

            var dashButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            dashButtons.Children.Add(MakeButton("Open the dashes folder", OpenDashFolder));
            dashButtons.Children.Add(MakeButton("Save a copy of this dash to edit", SaveDashCopy));
            dashButtons.Children.Add(MakeButton("Reload dashes", () => { LoadDashes(); Usb?.ReloadDashes(); }));
            body.Children.Add(dashButtons);

            // ----- Lights -----
            body.Children.Add(Heading("Lights"));
            var lightsOn = new CheckBox { Content = "Drive every light on the wheel", IsChecked = S.LightsEnabled, Margin = new Thickness(0, 0, 0, 6) };
            lightsOn.Checked += (s, e) => { S.LightsEnabled = true; Changed(); };
            lightsOn.Unchecked += (s, e) => { S.LightsEnabled = false; Changed(); };
            body.Children.Add(lightsOn);
            var idle = new CheckBox { Content = "Keep them on when no game is running (untick to give the lights back to SimPro between sessions)", IsChecked = S.IdleLights, Margin = new Thickness(0, 0, 0, 6) };
            idle.Checked += (s, e) => { S.IdleLights = true; Changed(); };
            idle.Unchecked += (s, e) => { S.IdleLights = false; Changed(); };
            body.Children.Add(idle);
            var reverse = new CheckBox { Content = "Rev lights fill from the right (if they run the wrong way on your wheel)", IsChecked = S.ReverseRev, Margin = new Thickness(0, 0, 0, 10) };
            reverse.Checked += (s, e) => { S.ReverseRev = true; Changed(); };
            reverse.Unchecked += (s, e) => { S.ReverseRev = false; Changed(); };
            body.Children.Add(reverse);

            // ----- Where the lights come from -----
            var from = new ComboBox { Width = 360 };
            from.Items.Add(new ComboBoxItem { Content = "FXPro RPM Sync's own effects (below)", Tag = LightsSource.BuiltIn });
            from.Items.Add(new ComboBoxItem { Content = "ATSR-Hub", Tag = LightsSource.AtsrHub });
            from.SelectedItem = from.Items.Cast<ComboBoxItem>().First(i => (LightsSource)i.Tag == S.LightsFrom);
            body.Children.Add(Row("Lights come from", from));

            atsrPanel = new StackPanel();
            atsrPanel.Children.Add(Muted("ATSR-Hub works out every LED's colour (shift lights, flags, spotter, TC/ABS, animations) and this " +
                "plugin sends them to the FX Pro, which SimHub can't drive itself. In ATSR-Hub, add the FX Pro as a steering wheel " +
                "(VID 0483, PID 0529) and number its LEDs like the FX Pro does: buttons 0-11, encoders 12-16, the lights beside " +
                "the rev lights 17-22 (left 17-19, right 20-22), rev lights 23-37. If your ATSR-Hub layout numbers them " +
                "differently, enter the map below. While ATSR-Hub sends nothing, the built-in lights below stay on.", new Thickness(0, 0, 0, 8)));
            atsrDevice = new ComboBox { Width = 300, IsEditable = true, Text = S.AtsrDevice ?? "" };
            atsrDevice.LostFocus += (s, e) => SetAtsrDevice(atsrDevice.Text);
            atsrDevice.SelectionChanged += (s, e) => { if (atsrDevice.SelectedItem is string d) SetAtsrDevice(d); };
            var deviceRow = new StackPanel { Orientation = Orientation.Horizontal };
            deviceRow.Children.Add(atsrDevice);
            var refresh = MakeButton("Refresh", RefreshAtsrDevices);
            refresh.Margin = new Thickness(8, 0, 0, 0);
            deviceRow.Children.Add(refresh);
            atsrPanel.Children.Add(Row("ATSR-Hub device", deviceRow));
            var nm = new CheckBox { Content = "Follow ATSR-Hub's brightness (night mode)", IsChecked = S.AtsrBrightness, Margin = new Thickness(150, 0, 0, 8) };
            nm.Checked += (s, e) => { S.AtsrBrightness = true; Changed(); };
            nm.Unchecked += (s, e) => { S.AtsrBrightness = false; Changed(); };
            atsrPanel.Children.Add(nm);
            var map = new TextBox { Width = 560, Text = S.AtsrMap ?? "", ToolTip = "38 ATSR-Hub LED numbers, one per FX Pro LED in FX Pro order, -1 = off. Empty = same numbers." };
            atsrMapError = new TextBlock { Foreground = Frozen(Warn), Margin = new Thickness(150, -4, 0, 8), TextWrapping = TextWrapping.Wrap };
            map.LostFocus += (s, e) =>
            {
                AtsrBridge.ParseMap(map.Text, out var err);
                atsrMapError.Text = err == null ? "" : "Map not used: " + err;
                if (err == null) { S.AtsrMap = map.Text.Trim(); Changed(); }
            };
            atsrPanel.Children.Add(Row("LED map (optional)", map));
            atsrPanel.Children.Add(atsrMapError);
            atsrState = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(150, 0, 0, 12) };
            atsrPanel.Children.Add(atsrState);
            body.Children.Add(atsrPanel);

            builtInPanel = new StackPanel();
            body.Children.Add(builtInPanel);
            from.SelectionChanged += (s, e) =>
            {
                if (!(from.SelectedItem is ComboBoxItem i)) return;
                S.LightsFrom = (LightsSource)i.Tag;
                Changed();
                ShowLightsSource();
                if (S.LightsFrom == LightsSource.AtsrHub) RefreshAtsrDevices();
            };

            presetGallery = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var p in LightPresets.All) AddPresetCard(p.Id, p.Name, p.Description);
            AddPresetCard(LightPresets.CustomId, "Your own", "Start from the selected preset and change every group, colour, rev light and alert.");
            builtInPanel.Children.Add(Muted("Built-in lights (also shown while ATSR-Hub sends nothing):", new Thickness(0, 4, 0, 8)));
            builtInPanel.Children.Add(presetGallery);

            bigTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
            bigPreview = new WheelLedPreview(1.6);
            var bigBody = new StackPanel();
            bigBody.Children.Add(bigTitle);
            bigBody.Children.Add(bigPreview);
            bigBody.Children.Add(Muted("Layout is schematic. The preview revs up and down and triggers ABS and TC now and then; while the " +
                                       "wheel is in USB mode it shows what the wheel shows.", new Thickness(0, 8, 0, 0)));
            builtInPanel.Children.Add(Card(bigBody, new Thickness(0, 4, 0, 12)));

            var customize = MakeButton("Customize these lights", Customize);
            customize.HorizontalAlignment = HorizontalAlignment.Left;
            builtInPanel.Children.Add(customize);
            editor = new StackPanel();
            builtInPanel.Children.Add(editor);

            frameTimer.Tick += (s, e) => RenderFrame();
            slowTimer.Tick += (s, e) => { RefreshStatus(); RenderDashPreview(); };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); plugin.SaveSettings(); };
            Loaded += (s, e) => { frameTimer.Start(); slowTimer.Start(); };
            Unloaded += (s, e) =>
            {
                frameTimer.Stop(); slowTimer.Stop();
                if (saveTimer.IsEnabled) { saveTimer.Stop(); plugin.SaveSettings(); }
                previewScreen?.Dispose(); previewScreen = null; previewKey = null;
            };

            LoadDashes();
            RefreshLights();
            ShowLightsSource();
            if (S.LightsFrom == LightsSource.AtsrHub) RefreshAtsrDevices();
            RefreshStatus();
        }

        /// <summary>Saves (debounced) and tells the USB thread to pick the change up (redraws the dash).</summary>
        private void Changed()
        {
            if (loading) return;
            Usb?.SettingsChanged();
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---------- Status ----------

        private void RefreshStatus()
        {
            var u = Usb;
            if (u == null) return;
            Color tone; string icon;
            switch (u.State)
            {
                case "Active": case "Demo": case "Test": case "Lights on": case "Standing by": tone = Good; icon = ""; break;
                case "Ready": tone = Info; icon = ""; break;
                case "Error": case "Unsupported wheel firmware": tone = Bad; icon = ""; break;
                case "Off": tone = Idle; icon = ""; break;
                default: tone = Warn; icon = ""; break;
            }
            string title = u.State;
            if (u.WheelFound && u.WheelVersion != null) title += $"   ·   FX Pro app {u.WheelVersion}";
            if (u.DashActive && u.ActiveDashName != null) title += "   ·   " + u.ActiveDashName;
            bannerTitle.Text = title;
            bannerTitle.Foreground = Frozen(tone);
            bannerIcon.Text = icon;
            bannerIcon.Foreground = Frozen(tone);
            banner.BorderBrush = Frozen(tone);
            bannerDetail.Text = u.Detail;
            designerInfo.Text = plugin.Designer?.Running == true ? "running at " + plugin.Designer.Url + "   (API for agents: " + plugin.Designer.Url + "api)" : "not running";
            if (S.LightsFrom == LightsSource.AtsrHub)
                atsrState.Text = !u.Active ? "Not sending (USB mode isn't driving the wheel now)."
                               : "Lights now: " + u.LightsState + ".";
            UpdateDemoButton();
        }

        private void ShowLightsSource()
        {
            atsrPanel.Visibility = S.LightsFrom == LightsSource.AtsrHub ? Visibility.Visible : Visibility.Collapsed;
            builtInPanel.Opacity = S.LightsFrom == LightsSource.AtsrHub ? 0.75 : 1;
        }

        private void SetAtsrDevice(string name)
        {
            name = (name ?? "").Trim();
            if (name == (S.AtsrDevice ?? "")) return;
            S.AtsrDevice = name;
            Changed();
        }

        private void RefreshAtsrDevices()
        {
            var pm = plugin.PluginManager;
            var devices = pm == null ? new List<string>() : AtsrBridge.Devices(pm);
            string current = S.AtsrDevice ?? "";
            atsrDevice.ItemsSource = devices;
            atsrDevice.Text = current;
            if (string.IsNullOrEmpty(current) && devices.Count == 1) SetAtsrDevice(atsrDevice.Text = devices[0]);
        }

        private void UpdateDemoButton() => demoButton.Content = Usb?.DemoOn == true ? "Stop the demo" : "Start the demo on the wheel";

        // ---------- Dash ----------

        private void LoadDashes()
        {
            var errors = new List<string>();
            dashes = DashLibrary.Load(errors);
            loading = true;
            dashBox.Items.Clear();
            foreach (var d in dashes)
                dashBox.Items.Add(new ComboBoxItem { Content = d.Name + (d.BuiltIn ? "" : "   (from file)"), Tag = d.Id });
            dashBox.SelectedItem = dashBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == S.DashId) ?? dashBox.Items[0];
            loading = false;
            loadErrors = errors;
            previewKey = null;
            ShowDashInfo();
        }

        private List<string> loadErrors = new List<string>();

        private DashDefinition SelectedDash => dashes?.FirstOrDefault(d => d.Id == S.DashId) ?? dashes?.FirstOrDefault();

        private void ShowDashInfo()
        {
            var d = SelectedDash;
            if (d == null) return;
            dashInfo.Text = (d.Description ?? "") + (string.IsNullOrEmpty(d.Author) ? "" : "  (" + d.Author + ")");
            previewKey = null;
        }

        /// <summary>Draws the selected dash with the same renderer the wheel uses, into a bitmap (4 per second).</summary>
        private void RenderDashPreview()
        {
            if (body.Visibility != Visibility.Visible) return;
            var d = SelectedDash;
            if (d == null) return;
            string key = d.Id + "|" + S.PadLeft + "|" + S.PadTop;
            double now = clock.Elapsed.TotalSeconds;
            if (key != previewKey)
            {
                previewKey = key;
                previewScreen?.Dispose();
                previewScreen = new PreviewScreen();
                var room = DashRenderer.Room(d);
                previewRenderer = new DashRenderer(previewScreen, d, Math.Min(S.PadLeft, room.Right), Math.Min(S.PadTop, room.Down));
                previewRenderer.DrawAll();
                var problems = previewRenderer.Check();
                problems.AddRange(loadErrors);
                dashProblems.Text = problems.Count == 0 ? "" : "Layout warnings (the screen would cut these off):\n• " + string.Join("\n• ", problems.Take(12));
                dashProblems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            DashValues v = Usb?.DashActive == true ? Usb.Latest : null;
            if (v == null)
            {
                if (previewDemo == null) { previewDemo = new UsbDemo(); previewLast = now; }
                v = previewDemo.Step(Math.Min(1, now - previewLast));
                previewLast = now;
            }
            previewRenderer.Update(v, now);
            preview.Source = ToImage(previewScreen.Bitmap);
        }

        private static BitmapSource ToImage(System.Drawing.Bitmap bmp)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * bmp.Height, data.Stride);
                src.Freeze();
                return src;
            }
            finally { bmp.UnlockBits(data); }
        }

        private TextBlock designerInfo;

        private void OpenDesigner()
        {
            var url = plugin.StartDesigner();
            if (url == null) { MessageBox.Show("The designer couldn't start: port " + S.DesignerPort + " is in use. See SimHub's log.", "FXPro RPM Sync"); return; }
            var sel = S.DashId;
            try { Process.Start(url + (string.IsNullOrEmpty(sel) ? "" : "?dash=" + Uri.EscapeDataString(sel))); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }

        private void OpenDashFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(DashLibrary.Folder);
                Process.Start("explorer.exe", DashLibrary.Folder);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }

        private void SaveDashCopy()
        {
            var d = SelectedDash;
            if (d == null) return;
            try
            {
                var path = DashLibrary.Export(d);
                LoadDashes();
                MessageBox.Show(Window.GetWindow(this), "Saved as\n" + path + "\n\nEdit the JSON and press \"Reload dashes\". Its layout warnings show under the preview.",
                    "FXPro RPM Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }

        // ---------- Lights ----------

        private void AddPresetCard(string id, string name, string description)
        {
            var mini = new WheelLedPreview(0.5);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            body.Children.Add(mini);
            body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            var card = new Border
            {
                Width = 250, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
                Background = CardBackground, BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Child = body, ToolTip = description,
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (id == LightPresets.CustomId && S.CustomLights == null) { Customize(); return; }
                S.LightPreset = id;
                Changed();
                RefreshLights();
            };
            presetCards.Add((id, card, mini));
            cardEngines[id] = new LightEngine();
            presetGallery.Children.Add(card);
        }

        private void Customize()
        {
            if (S.LightPreset != LightPresets.CustomId || S.CustomLights == null)
            {
                var from = S.ActiveLights.Clone();
                from.Id = LightPresets.CustomId;
                from.Name = "Your own";
                from.Description = "Based on " + (LightPresets.Find(S.LightPreset)?.Name ?? "a preset");
                S.CustomLights = from;
            }
            S.LightPreset = LightPresets.CustomId;
            Changed();
            RefreshLights();
        }

        private void RefreshLights()
        {
            foreach (var (id, card, _) in presetCards)
            {
                card.BorderBrush = id == S.LightPreset ? Accent : CardBorder;
                card.Opacity = id == LightPresets.CustomId && S.CustomLights == null ? 0.6 : 1;
            }
            bigTitle.Text = "Preview: " + S.ActiveLights.Name;
            editor.Children.Clear();
            if (S.LightPreset == LightPresets.CustomId && S.CustomLights != null) BuildEditor(S.CustomLights);
        }

        /// <summary>Simulated driving for the previews: revs sweep up and flash, ABS and TC flicker now and then.</summary>
        private static DashValues SimValues(double t)
        {
            double cycle = t % 6;
            double rpm = cycle < 4.5 ? 3000 + 5200 * Math.Pow(cycle / 4.5, 1.4) : 8200 - 5200 * (cycle - 4.5) / 1.5;
            return new DashValues
            {
                Running = true, Rpm = rpm, MaxRpm = 8400, Redline = 8000, GearKey = "3",
                AbsActive = t % 11 > 9.5, TcActive = t % 13 > 11.8, FuelPercent = 50,
            };
        }

        private void RenderFrame()
        {
            if (body.Visibility != Visibility.Visible) return;
            double t = clock.Elapsed.TotalSeconds;
            var sim = SimValues(t);
            var live = Usb?.Active == true ? Usb.LastFrame : null;
            bigPreview.Show(live ?? previewEngine.Render(S.ActiveLights, sim, null, t, false));
            foreach (var (id, _, mini) in presetCards)
            {
                var p = id == LightPresets.CustomId ? S.CustomLights : LightPresets.Find(id);
                if (p == null) { mini.Show(new LedColor[LightEngine.Count]); continue; }
                mini.Show(cardEngines[id].Render(p, sim, null, t, false));
            }
        }

        // ---------- Editor ----------

        private void BuildEditor(LightProfile p)
        {
            loading = true;
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(Row("Brightness", Slider(1, 90, p.MaxBrightness, v => { p.MaxBrightness = v; Changed(); }, "{0} / 90")));

            foreach (LedGroup g in Enum.GetValues(typeof(LedGroup)))
            {
                var l = p.Group(g);
                var groupBody = new StackPanel();
                groupBody.Children.Add(new TextBlock { Text = LightPresets.GroupName(g), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
                var effect = new ComboBox { Width = 220 };
                var effects = g == LedGroup.Rev ? Enum.GetValues(typeof(LightEffect)).Cast<LightEffect>()
                                                : Enum.GetValues(typeof(LightEffect)).Cast<LightEffect>().Where(x => x != LightEffect.Rpm);
                foreach (var x in effects) effect.Items.Add(new ComboBoxItem { Content = EffectName(x), Tag = x });
                effect.SelectedItem = effect.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (LightEffect)i.Tag == l.Effect) ?? effect.Items[0];
                groupBody.Children.Add(Row("Effect", effect));
                var details = new StackPanel();
                groupBody.Children.Add(details);
                Action fill = () =>
                {
                    details.Children.Clear();
                    if (l.Effect == LightEffect.Rpm) { BuildRevEditor(details, p.Rev); return; }
                    if (l.Effect == LightEffect.Off) return;
                    if (l.Effect != LightEffect.Rainbow && l.Effect != LightEffect.RainbowBreathe)
                        details.Children.Add(Row("Colours", ColourList(l.Colors, 1, 4)));
                    if (l.Effect != LightEffect.Solid)
                        details.Children.Add(Row("Speed", SliderD(0.5, 15, l.Period, v => { l.Period = v; Changed(); }, "{0:0.0} s per cycle")));
                    details.Children.Add(Row("Brightness", Slider(5, 100, l.Brightness, v => { l.Brightness = v; Changed(); }, "{0}%")));
                };
                effect.SelectionChanged += (s, e) =>
                {
                    if (effect.SelectedItem is ComboBoxItem i) { l.Effect = (LightEffect)i.Tag; Changed(); fill(); }
                };
                fill();
                var groupCard = Card(groupBody, new Thickness(0, 0, 0, 8));
                groupCard.MinWidth = 620;
                panel.Children.Add(groupCard);
            }

            // Alerts
            var alerts = new StackPanel();
            alerts.Children.Add(new TextBlock { Text = "Alerts", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            alerts.Children.Add(Muted("While one is active, its lights blink in its colour over everything else (the first one in the list wins).", new Thickness(0, 0, 0, 8)));
            foreach (var a in p.Alerts)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                var on = new CheckBox { Content = LightPresets.AlertName(a.Trigger), IsChecked = a.Enabled, Width = 190, VerticalAlignment = VerticalAlignment.Center };
                on.Checked += (s, e) => { a.Enabled = true; Changed(); };
                on.Unchecked += (s, e) => { a.Enabled = false; Changed(); };
                row.Children.Add(on);
                row.Children.Add(new ColourField(a.Color, hex => { a.Color = hex; Changed(); }) { Margin = new Thickness(0, 0, 12, 0) });
                foreach (LedGroup g in Enum.GetValues(typeof(LedGroup)))
                {
                    var gb = new CheckBox { Content = ShortGroup(g), IsChecked = a.Groups.Contains(g), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                    var gg = g;
                    gb.Checked += (s, e) => { if (!a.Groups.Contains(gg)) a.Groups.Add(gg); Changed(); };
                    gb.Unchecked += (s, e) => { a.Groups.Remove(gg); Changed(); };
                    row.Children.Add(gb);
                }
                var hz = new ComboBox { Width = 110, Margin = new Thickness(4, 0, 0, 0) };
                foreach (var (label, value) in new[] { ("Steady", 0.0), ("Pulse 1/s", 1.0), ("Blink 2/s", 2.0), ("Blink 4/s", 4.0), ("Flicker 8/s", 8.0), ("Flicker 12/s", 12.0) })
                    hz.Items.Add(new ComboBoxItem { Content = label, Tag = value });
                hz.SelectedItem = hz.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((double)i.Tag - a.BlinkHz)).First();
                hz.SelectionChanged += (s, e) => { if (hz.SelectedItem is ComboBoxItem i) { a.BlinkHz = (double)i.Tag; Changed(); } };
                row.Children.Add(hz);
                alerts.Children.Add(row);
            }
            panel.Children.Add(Card(alerts, new Thickness(0, 0, 0, 8)));
            editor.Children.Add(panel);
            loading = false;
        }

        private void BuildRevEditor(StackPanel details, RevLighting rev)
        {
            var car = new CheckBox
            {
                Content = "Use the car's real rev lights when known (rev light database, your per-car overrides)",
                IsChecked = rev.UseCarData, Margin = new Thickness(0, 0, 0, 8),
            };
            car.Checked += (s, e) => { rev.UseCarData = true; Changed(); };
            car.Unchecked += (s, e) => { rev.UseCarData = false; Changed(); };
            details.Children.Add(car);
            details.Children.Add(Muted("Otherwise (and for cars without data):", new Thickness(0, 0, 0, 6)));
            details.Children.Add(Row("Colours (low to high)", ColourList(rev.Colors, 1, 4)));
            details.Children.Add(Row("First LED at", Slider(40, 98, (int)rev.StartPercent, v => { rev.StartPercent = v; Changed(); }, "{0}% of the shift point")));
            details.Children.Add(Row("Shift flash", new ColourField(rev.FlashColor, hex => { rev.FlashColor = hex; Changed(); })));
            var hz = new ComboBox { Width = 160 };
            foreach (var (label, value) in new[] { ("Steady", 0.0), ("Blink 4/s", 4.0), ("Blink 8/s", 8.0), ("Blink 12/s", 12.0) })
                hz.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            hz.SelectedItem = hz.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((double)i.Tag - rev.FlashHz)).First();
            hz.SelectionChanged += (s, e) => { if (hz.SelectedItem is ComboBoxItem i) { rev.FlashHz = (double)i.Tag; Changed(); } };
            details.Children.Add(Row("Flash", hz));
        }

        /// <summary>1-4 colours with add/remove.</summary>
        private FrameworkElement ColourList(List<string> colours, int min, int max)
        {
            var panel = new WrapPanel();
            Action build = null;
            build = () =>
            {
                panel.Children.Clear();
                for (int i = 0; i < colours.Count; i++)
                {
                    int k = i;
                    panel.Children.Add(new ColourField(colours[k], hex => { colours[k] = hex; Changed(); }) { Margin = new Thickness(0, 0, 8, 4) });
                }
                if (colours.Count < max)
                    panel.Children.Add(SmallButton("+", () => { colours.Add(colours.LastOrDefault() ?? "#FFFFFF"); Changed(); build(); }));
                if (colours.Count > min)
                    panel.Children.Add(SmallButton("−", () => { colours.RemoveAt(colours.Count - 1); Changed(); build(); }));
            };
            build();
            return panel;
        }

        private static string EffectName(LightEffect e)
        {
            switch (e)
            {
                case LightEffect.Off: return "Off";
                case LightEffect.Solid: return "Solid";
                case LightEffect.Breathe: return "Breathing";
                case LightEffect.Wave: return "Colour wave";
                case LightEffect.Rainbow: return "Rainbow flow";
                case LightEffect.RainbowBreathe: return "Breathing rainbow";
                case LightEffect.Scanner: return "Scanner";
                case LightEffect.Sparkle: return "Sparkle";
                default: return "Shift lights (RPM)";
            }
        }

        private static string ShortGroup(LedGroup g)
        {
            switch (g)
            {
                case LedGroup.Buttons: return "Buttons";
                case LedGroup.Encoders: return "Encoders";
                case LedGroup.SideLeft: return "Left";
                case LedGroup.SideRight: return "Right";
                default: return "Rev";
            }
        }

        // ---------- Small UI helpers ----------

        private FrameworkElement Slider(int min, int max, int value, Action<int> set, string format)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickFrequency = 1, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.8, Text = string.Format(format, (int)slider.Value) };
            slider.ValueChanged += (s, e) => { label.Text = string.Format(format, (int)slider.Value); if (!loading) set((int)slider.Value); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(slider);
            row.Children.Add(label);
            return row;
        }

        private FrameworkElement SliderD(double min, double max, double value, Action<double> set, string format)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickFrequency = 0.1, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.8, Text = string.Format(format, slider.Value) };
            slider.ValueChanged += (s, e) => { label.Text = string.Format(format, slider.Value); if (!loading) set(slider.Value); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(slider);
            row.Children.Add(label);
            return row;
        }

        private static TextBlock Heading(string text) =>
            new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) };

        private static Border Card(UIElement child, Thickness margin) => new Border
        {
            Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14), Margin = margin, Child = child, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left,
        };

        private static TextBlock Muted(string text, Thickness margin) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = margin, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

        private static TextBlock Small(string text) =>
            new TextBlock { Text = text, FontSize = 10, Opacity = 0.55, FontWeight = FontWeights.SemiBold };

        private static FrameworkElement Row(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            var l = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            DockPanel.SetDock(l, Dock.Left);
            row.Children.Add(l);
            if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(control);
            return row;
        }

        private static Button MakeButton(string text, Action onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (s, e) => onClick();
            return b;
        }

        private static Button SmallButton(string text, Action onClick)
        {
            var b = new Button { Content = text, Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => onClick();
            return b;
        }

        internal static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    /// <summary>A colour swatch with a hex box; the swatch opens a palette.</summary>
    public class ColourField : StackPanel
    {
        private static readonly string[] Palette =
        {
            "#FFFFFF", "#FF0020", "#FF1E00", "#FF5A00", "#FF7A00", "#FFB000", "#FFD000", "#FFFF00",
            "#B0FF00", "#00FF40", "#00FFA3", "#00F0FF", "#7FDBFF", "#00A0FF", "#0060FF", "#0020FF",
            "#7B2FFF", "#9D4EDD", "#C000FF", "#FF00D0", "#FF2E97", "#FF6090", "#FFE0B0", "#000000",
        };

        private readonly Border swatch;
        private readonly TextBox hexBox;
        private readonly Action<string> set;

        public ColourField(string hex, Action<string> set)
        {
            this.set = set;
            Orientation = Orientation.Horizontal;
            swatch = new Border { Width = 26, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            hexBox = new TextBox { Width = 76, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = hex };
            Children.Add(swatch);
            Children.Add(hexBox);
            Show(hex);

            var grid = new UniformGrid { Columns = 8, Margin = new Thickness(6) };
            var popup = new Popup { PlacementTarget = swatch, StaysOpen = false, AllowsTransparency = true };
            foreach (var c in Palette)
            {
                var chip = new Border { Width = 22, Height = 22, Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = UsbSection.Frozen((Color)ColorConverter.ConvertFromString(c)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                var cc = c;
                chip.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; hexBox.Text = cc; Commit(); };
                grid.Children.Add(chip);
            }
            popup.Child = new Border { Background = UsbSection.Frozen(Color.FromRgb(0x25, 0x27, 0x2b)), CornerRadius = new CornerRadius(6), Child = grid };
            swatch.MouseLeftButtonUp += (s, e) => popup.IsOpen = true;
            hexBox.LostFocus += (s, e) => Commit();
            hexBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
        }

        private void Commit()
        {
            var t = hexBox.Text.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            if (t.Length != 7 || !int.TryParse(t.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out _)) { hexBox.Text = hexBox.Tag as string ?? "#FFFFFF"; return; }
            t = t.ToUpperInvariant();
            hexBox.Text = t;
            Show(t);
            set(t);
        }

        private void Show(string hex)
        {
            hexBox.Tag = hex;
            try { swatch.Background = UsbSection.Frozen((Color)ColorConverter.ConvertFromString(hex)); } catch { swatch.Background = Brushes.Black; }
        }
    }

    /// <summary>The wheel's 38 LEDs, schematic: rev bar with the side lights, buttons, encoders.</summary>
    public class WheelLedPreview : Canvas
    {
        private readonly Ellipse[] leds = new Ellipse[LightEngine.Count];
        private static readonly Color Unlit = Color.FromRgb(0x22, 0x24, 0x28);

        public WheelLedPreview(double scale)
        {
            double d = 14 * scale, gap = 5 * scale;
            Width = 22 * (d + gap) + 20 * scale;
            Height = 3 * (d + gap) + 40 * scale;
            Background = UsbSection.Frozen(Color.FromRgb(0x0e, 0x0f, 0x11));
            double x0 = 10 * scale, y0 = 10 * scale;
            // Top row: left side lights, 15 rev lights, right side lights
            for (int i = 0; i < 3; i++) Place(17 + i, x0 + i * (d + gap), y0, d);
            for (int i = 0; i < 15; i++) Place(23 + i, x0 + (3.5 + i) * (d + gap), y0, d);
            for (int i = 0; i < 3; i++) Place(20 + i, x0 + (19 + i) * (d + gap), y0, d);
            // Buttons: two columns of six, left and right
            double by = y0 + d + gap * 3;
            for (int i = 0; i < 6; i++) Place(i, x0 + i * (d + gap), by, d);
            for (int i = 0; i < 6; i++) Place(6 + i, x0 + (16 + i) * (d + gap), by, d);
            // Encoders in the middle
            for (int i = 0; i < 5; i++) Place(12 + i, x0 + (8.5 + i) * (d + gap), by + d + gap * 2, d);
        }

        private void Place(int led, double x, double y, double d)
        {
            var e = new Ellipse { Width = d, Height = d, Fill = UsbSection.Frozen(Unlit) };
            SetLeft(e, x); SetTop(e, y);
            Children.Add(e);
            leds[led] = e;
        }

        public void Show(LedColor[] frame)
        {
            for (int i = 0; i < leds.Length && i < frame.Length; i++)
            {
                var f = frame[i];
                // What the eye sees: colour times brightness (1-90), with a floor so dim LEDs still read as lit.
                double k = Math.Min(1, f.Brightness / 90.0);
                byte r = (byte)(f.R * k), g = (byte)(f.G * k), b = (byte)(f.B * k);
                var c = r + g + b < 30 ? Unlit : Color.FromRgb(r, g, b);
                if (leds[i].Fill is SolidColorBrush sb && sb.Color == c) continue;
                leds[i].Fill = UsbSection.Frozen(c);
            }
        }
    }
}
