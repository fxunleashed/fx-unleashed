using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>Standard mode, rev lights: what's applied now, the options, and the style for cars without data.</summary>
    public class StandardLightsTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private FallbackStyle Fb => plugin.Settings.Fallback;

        private readonly List<(PatternKind Kind, Border Tile, LedStrip Strip)> tiles = new List<(PatternKind, Border, LedStrip)>();
        private readonly List<LedStrip> animated = new List<LedStrip>();
        private readonly LedStrip bigStrip;
        private readonly TextBlock bigRpm, bigTitle, presetNote, flashNote, carName, carSource, shiftStat, maxStat;
        private readonly StackPanel customize, customColors;
        private ComboBox schemeBox, flashBox;
        private ComboBox color1, color2, color3, flashColor;
        private FrameworkElement color2Row, color3Row, flashColorRow;
        private Slider startSlider;
        private TextBlock startLabel;
        private bool loading;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DispatcherTimer applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly DispatcherTimer statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public StandardLightsTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var S = plugin.Settings;

            // ----- Now -----
            var now = new Grid();
            now.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            now.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Theme.Eyebrow("Now on the wheel"));
            carName = Theme.Title("No car yet", 22);
            left.Children.Add(carName);
            carSource = Theme.Note("", new Thickness(0, 4, 0, 0));
            left.Children.Add(carSource);
            now.Children.Add(left);
            var stats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            stats.Children.Add(Stat("SHIFT", out shiftStat));
            stats.Children.Add(Stat("GAME MAX", out maxStat));
            Grid.SetColumn(stats, 1);
            now.Children.Add(stats);
            Children.Add(Theme.CardBox(now));

            // ----- Options -----
            var opts = new StackPanel();
            opts.Children.Add(Theme.Eyebrow("Sync"));
            opts.Children.Add(Theme.Switch("Match the rev lights to the car", S.Enabled, v =>
            {
                S.Enabled = v; plugin.SaveSettings();
                if (v) plugin.Reapply(); else plugin.RequestRestore();
            }, "Off: the preset's own lights, as SimPro has them."));
            opts.Children.Add(Theme.Switch("Use each car's real lights when it's in the database", S.UseCarDatabase, v => { S.UseCarDatabase = v; plugin.SaveSettings(); plugin.Reapply(); },
                "Off: the style below for every car."));
            opts.Children.Add(Theme.Switch("Switch the lights by gear", S.LiveGearCurves, v => { S.LiveGearCurves = v; plugin.SaveSettings(); plugin.Reapply(); },
                "For cars whose real lights differ per gear: the gear's lights go to SimPro on every change."));
            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, -8) };
            buttons.Children.Add(Theme.Btn("Restore the preset's lights", () => plugin.RequestRestore(), icon: ""));
            buttons.Children.Add(Theme.Btn("Re-capture the preset", () => { plugin.RequestRestore(); plugin.ForgetOriginals(); }, icon: ""));
            opts.Children.Add(buttons);
            opts.Children.Add(Theme.Note("Re-capture after editing the preset's lights in SimPro, so the plugin starts from your version.", new Thickness(0, 4, 0, 0)));
            Children.Add(Theme.CardBox(opts));

            // ----- Fallback style -----
            var style = new StackPanel();
            style.Children.Add(Theme.Eyebrow("Cars without rev light data"));
            style.Children.Add(Theme.Note("The shift point is the car's redline in SimHub's Car Settings. The previews sweep up to it."));
            var gallery = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var entry in LedPatterns.Catalog)
            {
                var strip = new LedStrip(9);
                animated.Add(strip);
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = entry.Title, FontFamily = Theme.Display, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
                body.Children.Add(strip);
                body.Children.Add(new TextBlock { Text = entry.Description, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 0) });
                var kind = entry.Kind;
                var tile = Theme.Tile(body, 250, () => { Fb.Pattern = kind; StyleChanged(); }, entry.Description);
                tiles.Add((kind, tile, strip));
                gallery.Children.Add(tile);
            }
            style.Children.Add(gallery);

            bigTitle = new TextBlock { FontFamily = Theme.Display, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
            bigStrip = new LedStrip(20);
            bigRpm = new TextBlock { Foreground = Theme.Text2, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, FontFamily = Theme.Display };
            var bigBody = new StackPanel();
            bigBody.Children.Add(bigTitle);
            bigBody.Children.Add(bigStrip);
            bigBody.Children.Add(bigRpm);
            style.Children.Add(new Border
            {
                Background = Theme.B("#07080A"), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18), Margin = new Thickness(0, 4, 0, 16), MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left, Child = bigBody,
            });

            presetNote = Theme.Note("Uses the pattern and colours of the preset selected in SimPro. Change them there, then press \"Re-capture the preset\".");
            style.Children.Add(presetNote);

            customize = new StackPanel();
            style.Children.Add(customize);
            schemeBox = new ComboBox { Width = 230 };
            foreach (var sc in LedPatterns.SchemeCatalog) schemeBox.Items.Add(new ComboBoxItem { Content = sc.Title, Tag = sc.Scheme });
            schemeBox.SelectionChanged += (s, e) => { if (!loading) { Fb.Colors = (ColorScheme)((ComboBoxItem)schemeBox.SelectedItem).Tag; StyleChanged(); } };
            customize.Children.Add(Theme.Field("Colours", schemeBox));
            color1 = ColorPicker(hex => Fb.Color1 = hex);
            color2 = ColorPicker(hex => Fb.Color2 = hex);
            color3 = ColorPicker(hex => Fb.Color3 = hex);
            customColors = new StackPanel();
            customColors.Children.Add(Theme.Field("First LEDs", color1));
            customColors.Children.Add(color2Row = Theme.Field("Middle LEDs", color2));
            customColors.Children.Add(color3Row = Theme.Field("Last LEDs", color3));
            customize.Children.Add(customColors);

            startSlider = new Slider { Minimum = 50, Maximum = 98, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center };
            startLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontFamily = Theme.Display };
            startSlider.ValueChanged += (s, e) => { if (!loading) { Fb.StartPercent = startSlider.Value; StyleChanged(); } };
            var startRow = new StackPanel { Orientation = Orientation.Horizontal };
            startRow.Children.Add(startSlider);
            startRow.Children.Add(startLabel);
            customize.Children.Add(Theme.Field("First LED at", startRow));

            flashBox = new ComboBox { Width = 230 };
            flashBox.Items.Add(new ComboBoxItem { Content = "No flash", Tag = FlashMode.None });
            flashBox.Items.Add(new ComboBoxItem { Content = "Solid colour at the shift point", Tag = FlashMode.Solid });
            flashBox.Items.Add(new ComboBoxItem { Content = "Blink at the shift point", Tag = FlashMode.Blink });
            flashBox.SelectionChanged += (s, e) => { if (!loading) { Fb.Flash = (FlashMode)((ComboBoxItem)flashBox.SelectedItem).Tag; StyleChanged(); } };
            customize.Children.Add(Theme.Field("Shift flash", flashBox));
            flashColor = ColorPicker(hex => Fb.FlashColor = hex);
            customize.Children.Add(flashColorRow = Theme.Field("Flash colour", flashColor));
            flashNote = Theme.Note("Without real data the shift point is 95% of the car's max RPM unless you set its redline in SimHub's Car Settings.", new Thickness(170, 0, 0, 0));
            customize.Children.Add(flashNote);
            Children.Add(Theme.CardBox(style));

            frameTimer.Tick += (s, e) => RenderFrame();
            statusTimer.Tick += (s, e) => RefreshNow();
            applyTimer.Tick += (s, e) => { applyTimer.Stop(); plugin.SaveSettings(); plugin.Reapply(); };
            Loaded += (s, e) => { frameTimer.Start(); statusTimer.Start(); RefreshNow(); };
            Unloaded += (s, e) =>
            {
                frameTimer.Stop(); statusTimer.Stop();
                if (applyTimer.IsEnabled) { applyTimer.Stop(); plugin.SaveSettings(); plugin.Reapply(); }
            };

            LoadStyleIntoControls();
            Refresh();
        }

        private static FrameworkElement Stat(string label, out TextBlock value)
        {
            var sp = new StackPanel { Margin = new Thickness(28, 0, 0, 0), MinWidth = 96 };
            sp.Children.Add(Theme.Eyebrow(label));
            value = new TextBlock { FontFamily = Theme.Display, FontSize = 26, FontWeight = FontWeights.SemiBold, Text = "—" };
            sp.Children.Add(value);
            sp.Children.Add(new TextBlock { Text = "rpm", Foreground = Theme.Text3, FontSize = 11 });
            return sp;
        }

        private void RefreshNow()
        {
            bool hasCar = !string.IsNullOrEmpty(plugin.CurrentCar);
            carName.Text = hasCar ? (plugin.CurrentCarName ?? plugin.CurrentCar) : plugin.GameRunning ? "Waiting for the car" : "No game running";
            carSource.Text = hasCar ? "Lights: " + plugin.LightsSource : plugin.Status;
            shiftStat.Text = hasCar && plugin.AppliedRedline > 0 ? plugin.AppliedRedline.ToString("0") : "—";
            maxStat.Text = hasCar && plugin.AppliedMaxRpm > 0 ? plugin.AppliedMaxRpm.ToString("0") : "—";
        }

        private void LoadStyleIntoControls()
        {
            loading = true;
            schemeBox.SelectedItem = schemeBox.Items.Cast<ComboBoxItem>().First(i => (ColorScheme)i.Tag == Fb.Colors);
            flashBox.SelectedItem = flashBox.Items.Cast<ComboBoxItem>().First(i => (FlashMode)i.Tag == Fb.Flash);
            SelectColor(color1, Fb.Color1);
            SelectColor(color2, Fb.Color2);
            SelectColor(color3, Fb.Color3);
            SelectColor(flashColor, Fb.FlashColor);
            startSlider.Value = Fb.StartPercent;
            loading = false;
        }

        private void StyleChanged()
        {
            Refresh();
            applyTimer.Stop();
            applyTimer.Start(); // save + push to the wheel once the user stops fiddling
        }

        private void Refresh()
        {
            foreach (var (kind, tile, strip) in tiles)
            {
                strip.Layout = LayoutFor(kind, withFlash: false);
                Theme.Select(tile, kind == Fb.Pattern);
            }
            bool preset = Fb.Pattern == PatternKind.SimProPreset;
            bigStrip.Layout = LayoutFor(Fb.Pattern, withFlash: true);
            bigTitle.Text = LedPatterns.Catalog.First(c => c.Kind == Fb.Pattern).Title.ToUpperInvariant();
            presetNote.Visibility = preset ? Visibility.Visible : Visibility.Collapsed;
            customize.Visibility = preset ? Visibility.Collapsed : Visibility.Visible;
            customColors.Visibility = Fb.Colors == ColorScheme.Single || Fb.Colors == ColorScheme.Custom ? Visibility.Visible : Visibility.Collapsed;
            color2Row.Visibility = color3Row.Visibility = Fb.Colors == ColorScheme.Custom ? Visibility.Visible : Visibility.Collapsed;
            flashColorRow.Visibility = flashNote.Visibility = Fb.Flash == FlashMode.None ? Visibility.Collapsed : Visibility.Visible;
            startLabel.Text = $"{Fb.StartPercent:0}% of the shift point";
        }

        /// <summary>Tile previews show each pattern with the current colours/start point, so they compare like for like.</summary>
        private LedLayout LayoutFor(PatternKind kind, bool withFlash)
        {
            if (kind == PatternKind.SimProPreset) return RpmLightsMapper.PresetLayout(plugin.PresetTemplate);
            var s = Fb.Clone();
            s.Pattern = kind;
            if (!withFlash) s.Flash = FlashMode.None;
            return LedPatterns.Build(s);
        }

        private void RenderFrame()
        {
            double t = clock.Elapsed.TotalSeconds;
            bool blinkOn = ((int)(t * 1000 / (RpmLightsMapper.DefaultBlinkUnits * RpmLightsMapper.BlinkMsPerUnit))) % 2 == 0;
            foreach (var strip in animated.Append(bigStrip))
            {
                var first = strip.Layout?.Fractions.Where(f => f > 0).DefaultIfEmpty(0.8).Min() ?? 0.8;
                double rpm = LedStrip.SweepRpm(t, first);
                strip.Render(rpm, blinkOn);
                if (strip == bigStrip) bigRpm.Text = rpm >= 1.0 ? "AT THE SHIFT POINT" : $"{rpm * 100:0}% OF THE SHIFT POINT";
            }
        }

        private ComboBox ColorPicker(Action<string> set)
        {
            var box = new ComboBox { Width = 230 };
            foreach (var (name, hex) in LedPalette.All)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), Fill = Theme.B(hex) });
                content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                box.Items.Add(new ComboBoxItem { Content = content, Tag = hex });
            }
            box.SelectionChanged += (s, e) => { if (!loading && box.SelectedItem is ComboBoxItem i) { set((string)i.Tag); StyleChanged(); } };
            return box;
        }

        private static void SelectColor(ComboBox box, string hex) =>
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, hex, StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
    }
}
