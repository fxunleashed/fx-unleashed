using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode, streaming: the OBS browser source's address and every option of how the page looks (frame, lights,
    /// background), with an animated preview. The page reads the options from the plugin on every poll (ScreenMirror), so
    /// a change shows in OBS at once; nothing goes in the address.
    /// </summary>
    public class UnlockedStreamTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private MirrorSettings M => plugin.Settings.Usb.Mirror ?? (plugin.Settings.Usb.Mirror = new MirrorSettings());

        private readonly MirrorSketch preview;
        private readonly Border previewBack;
        private readonly WrapPanel frameTiles = new WrapPanel(), styleTiles = new WrapPanel(), swatches = new WrapPanel();
        private readonly StackPanel accentRow, styleRow, positionRow;
        private readonly Image dashPicture = new Image();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer animate = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly DispatcherTimer save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly System.ComponentModel.DependencyPropertyDescriptor pictureWatch =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
        private readonly EventHandler pictureChanged;
        private bool building;

        private static readonly (MirrorFrame Frame, string Name, string Hint)[] Frames =
        {
            (MirrorFrame.None, "No frame", "Just the screen"),
            (MirrorFrame.Line, "Thin line", "A fine ring, set off from the screen"),
            (MirrorFrame.Bezel, "Bezel", "A matte housing; the lights sit in it"),
            (MirrorFrame.Carbon, "Carbon", "Woven carbon with coloured piping"),
            (MirrorFrame.Glow, "Neon glow", "A glowing edge, like the wheel's lights"),
        };

        private static readonly (MirrorLightStyle Style, string Name, string Hint)[] Styles =
        {
            (MirrorLightStyle.Dots, "Dots", "Round lights, like the wheel's"),
            (MirrorLightStyle.Bars, "Bars", "Chunky segments across the screen"),
            (MirrorLightStyle.Line, "Thin line", "A fine light bar"),
        };

        public UnlockedStreamTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;

            // ----- Address and preview -----
            var top = new StackPanel();
            top.Children.Add(Theme.Eyebrow("Streaming"));
            top.Children.Add(Theme.Title("The wheel on your stream", 20));
            top.Children.Add(Theme.Note("Add a Browser source in OBS with this address (1280×720 works well). It shows what the plugin draws on the wheel and its " +
                                        "lights, live. Everything below applies to that page straight away: the address never changes, so there's nothing to " +
                                        "edit or reload in OBS. Needs the dash designer server, which is on by default (Wheel page).", new Thickness(0, 8, 0, 14)));
            string url = $"http://127.0.0.1:{plugin.Settings.Usb.DesignerPort}/mirror";
            var address = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(Theme.Btn("Copy address", () => { try { Clipboard.SetText(url); } catch { } }, icon: ""));
            buttons.Children.Add(Theme.Btn("Open", Open, icon: ""));
            DockPanel.SetDock(buttons, Dock.Right);
            address.Children.Add(buttons);
            address.Children.Add(new TextBox { Text = url, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center, FontFamily = Theme.Mono, FontSize = 12.5, MinHeight = 34 });
            top.Children.Add(address);
            preview = new MirrorSketch(() => M, effects: true);
            previewBack = new Border { CornerRadius = new CornerRadius(10), Height = 390, Padding = new Thickness(18), ClipToBounds = true };
            previewBack.Child = new Viewbox { Stretch = Stretch.Uniform, Child = preview };
            top.Children.Add(previewBack);
            Children.Add(Theme.CardBox(top));

            // ----- Frame -----
            var frame = new StackPanel();
            frame.Children.Add(Theme.Eyebrow("Frame"));
            frame.Children.Add(frameTiles);
            accentRow = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            accentRow.Children.Add(Theme.Eyebrow("Frame colour"));
            accentRow.Children.Add(swatches);
            frame.Children.Add(accentRow);
            frame.Children.Add(Theme.Field("Screen corners", Theme.SliderField(0, 32, M.Corners, 1, v => v < 1 ? "square" : $"{v:0} px", v => { M.Corners = (int)v; Changed(); }), 150));
            Children.Add(Theme.CardBox(frame));

            // ----- Lights -----
            var lights = new StackPanel();
            lights.Children.Add(Theme.Eyebrow("Lights"));
            var switches = new WrapPanel { Orientation = Orientation.Horizontal };
            var rev = Theme.Switch("Rev lights", M.RevLights, v => { M.RevLights = v; Changed(); });
            var sides = Theme.Switch("Side lights", M.SideLights, v => { M.SideLights = v; Changed(); }, "The three on each side (flags, alerts)");
            rev.Margin = new Thickness(0, 0, 36, 12); sides.Margin = new Thickness(0, 0, 0, 12);
            switches.Children.Add(rev); switches.Children.Add(sides);
            lights.Children.Add(switches);
            styleRow = new StackPanel();
            styleRow.Children.Add(Theme.Eyebrow("Light style"));
            styleRow.Children.Add(styleTiles);
            lights.Children.Add(styleRow);
            positionRow = new StackPanel();
            positionRow.Children.Add(Theme.Field("Rev lights sit", Theme.Segmented(new[] { "Above the screen", "Below the screen" }, M.LightsAt == MirrorLightsAt.Above ? 0 : 1,
                i => { M.LightsAt = i == 0 ? MirrorLightsAt.Above : MirrorLightsAt.Below; Changed(); }, out _), 150));
            lights.Children.Add(positionRow);
            Children.Add(Theme.CardBox(lights));

            // ----- Page -----
            var page = new StackPanel();
            page.Children.Add(Theme.Eyebrow("Page"));
            page.Children.Add(Theme.Field("Background", Theme.Segmented(new[] { "See-through", "Dark", "Green screen" }, (int)M.Background,
                i => { M.Background = (MirrorBackground)i; Changed(); }, out _), 150));
            page.Children.Add(Theme.Field("Updates per second", Theme.SliderField(5, 30, M.Fps, 1, v => $"{v:0}", v => { M.Fps = (int)v; Changed(); }), 150));
            page.Children.Add(Theme.Switch("Hide everything while the plugin isn't drawing on the screen", M.HideWhenIdle, v => { M.HideWhenIdle = v; Changed(); },
                "Otherwise the page shows a short note instead (between sessions, or while the wheel runs its own dash)."));
            Children.Add(Theme.CardBox(page));

            // the dash's picture for the drawing (loaded off the UI thread by DashPictures)
            pictureChanged = (s, e) => ShowPicture();
            save.Tick += (s, e) => { save.Stop(); plugin.SaveSettings(); };
            animate.Tick += (s, e) => preview.Step(clock.Elapsed.TotalSeconds);
            Loaded += (s, e) =>
            {
                var d = DashCache.Find(plugin.Settings.Usb.DashId);
                pictureWatch.AddValueChanged(dashPicture, pictureChanged);
                if (d != null) DashPictures.ShowStill(dashPicture, d, plugin.Settings.Usb.RamFor(d.Id));
                ShowPicture();
                animate.Start();
            };
            Unloaded += (s, e) =>
            {
                animate.Stop();
                pictureWatch.RemoveValueChanged(dashPicture, pictureChanged);
                if (save.IsEnabled) { save.Stop(); plugin.SaveSettings(); }
            };
            Refresh();
        }

        private void Open()
        {
            var started = plugin.StartDesigner();
            if (started == null) { MessageBox.Show("The page's server couldn't start: port " + plugin.Settings.Usb.DesignerPort + " is in use. See SimHub's log.", "FX Unleashed"); return; }
            try { Process.Start($"http://127.0.0.1:{plugin.Settings.Usb.DesignerPort}/mirror"); } catch { }
        }

        private void Changed()
        {
            if (building) return;
            save.Stop(); save.Start();
            Refresh();
        }

        private void ShowPicture() => preview.Picture = dashPicture.Source;

        /// <summary>Everything that depends on the settings: the drawing, the tiles, the rows that only matter for some choices.</summary>
        private void Refresh()
        {
            building = true;
            M.Clamp();
            preview.Rebuild();
            previewBack.Background = M.Background == MirrorBackground.Dark ? Theme.B("#07080A")
                                   : M.Background == MirrorBackground.Green ? Theme.B("#00B140")
                                   : Checkerboard();

            frameTiles.Children.Clear();
            foreach (var (frame, name, hint) in Frames)
            {
                var f = frame;
                frameTiles.Children.Add(Tile(() => { var c = M.Clone(); c.Frame = f; return c; }, name, hint, M.Frame == f, () => { M.Frame = f; Changed(); }));
            }
            styleTiles.Children.Clear();
            foreach (var (style, name, hint) in Styles)
            {
                var st = style;
                styleTiles.Children.Add(Tile(() => { var c = M.Clone(); c.LightStyle = st; c.RevLights = true; c.SideLights = true; return c; }, name, hint, M.LightStyle == st, () => { M.LightStyle = st; Changed(); }));
            }

            swatches.Children.Clear();
            foreach (var hex in MirrorSettings.Accents) swatches.Children.Add(Swatch(hex));
            swatches.Children.Add(FollowChip());

            accentRow.Visibility = M.Frame == MirrorFrame.Line || M.Frame == MirrorFrame.Carbon || M.Frame == MirrorFrame.Glow ? Visibility.Visible : Visibility.Collapsed;
            styleRow.Visibility = M.RevLights || M.SideLights ? Visibility.Visible : Visibility.Collapsed;
            positionRow.Visibility = M.RevLights ? Visibility.Visible : Visibility.Collapsed;
            ShowPicture();
            building = false;
        }

        /// <summary>A choice shown as a small drawing of the page with that choice (and everything else as it is now).</summary>
        private Border Tile(Func<MirrorSettings> look, string name, string hint, bool selected, Action pick)
        {
            var sketch = new MirrorSketch(look, effects: false, tile: true);
            var body = new StackPanel();
            body.Children.Add(new Border
            {
                Height = 100, Background = Theme.B("#0A0B0D"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), ClipToBounds = true,
                Child = new Viewbox { Stretch = Stretch.Uniform, Child = sketch },
            });
            body.Children.Add(new TextBlock { Text = name, FontFamily = Theme.Display, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            var tile = Theme.Tile(body, 176, pick, hint);
            tile.Padding = new Thickness(8);
            Theme.Select(tile, selected);
            return tile;
        }

        private Border Swatch(string hex)
        {
            bool on = !M.IsFollowRev && string.Equals(M.FrameColor, hex, StringComparison.OrdinalIgnoreCase);
            var b = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 10, 8), Cursor = System.Windows.Input.Cursors.Hand,
                Background = Theme.B(hex), BorderThickness = new Thickness(2), BorderBrush = on ? Brushes.White : Theme.Line2, ToolTip = hex,
                Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.C(hex), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 } : null,
            };
            b.MouseLeftButtonUp += (s, e) => { M.FrameColor = hex; Changed(); };
            return b;
        }

        private Border FollowChip()
        {
            bool on = M.IsFollowRev;
            var b = new Border
            {
                Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(6, 0, 0, 8), Padding = new Thickness(12, 0, 12, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = on ? Theme.RedWash : Theme.Raised, BorderThickness = new Thickness(1.5), BorderBrush = on ? Theme.Red : Theme.Line2,
                ToolTip = "The frame takes the colour of the rev lights as they climb",
                Child = new TextBlock { Text = "Follow the rev lights", FontFamily = Theme.Display, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? Theme.Text : Theme.Text2 },
            };
            b.MouseLeftButtonUp += (s, e) => { M.FrameColor = "rev"; Changed(); };
            return b;
        }

        private static Brush Checkerboard()
        {
            var a = Theme.B("#0D0E11"); var b = Theme.B("#15171B");
            var tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(a, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            tile.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            tile.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(16, 16, 16, 16))));
            return new DrawingBrush(tile) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 32, 32), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 32, 32), ViewboxUnits = BrushMappingMode.Absolute };
        }
    }
}
