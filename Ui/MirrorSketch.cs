using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A drawing of the stream page (Usb/Designer/mirror.html) at the page's own size (the screen is 800x480), for the
    /// settings card: put it in a Viewbox. The geometry follows the page's applyOptions(); change one, change the other.
    /// `look` is asked on every Rebuild, so a tile can show the current settings with one thing swapped.
    /// </summary>
    internal sealed class MirrorSketch : Border
    {
        private static readonly Color Green = Theme.C("#00FF84"), Red = Theme.C("#FF0054"), Blue = Theme.C("#3D7BFF"), Amber = Theme.C("#FFFD51"), Idle = Theme.C("#3A3F48");

        private readonly Func<MirrorSettings> look;
        private readonly bool effects, tile;
        private readonly Image picture = new Image { Stretch = Stretch.Fill };
        private readonly SolidColorBrush accent = new SolidColorBrush(Colors.Red);
        private readonly List<DropShadowEffect> accentEffects = new List<DropShadowEffect>();
        private Grid inner;
        private Shape[] rev = new Shape[0], left = new Shape[0], right = new Shape[0];
        private Color?[] shown;
        private MirrorSettings o;
        private bool followRev;
        private double k = 1, kl = 1; // tile mode's scale for frame features and for lights (1 = the page's own sizes)

        /// <param name="effects">Glows and shadows.</param>
        /// <param name="tile">A choice's small picture: frame features and lights drawn bolder (they'd be hairlines at that size)
        /// over a stand-in screen, so what differs between the choices can be seen.</param>
        public MirrorSketch(Func<MirrorSettings> look, bool effects, bool tile = false)
        {
            this.look = look;
            this.effects = effects;
            this.tile = tile;
            Rebuild();
        }

        public ImageSource Picture { get => picture.Source; set => picture.Source = value; }

        public void Rebuild()
        {
            inner?.Children.Remove(picture); // an element has one parent: take the picture out of the old drawing first
            accentEffects.Clear();
            o = look().Clone();
            o.Clamp();
            followRev = o.IsFollowRev;
            accent.Color = followRev ? Idle : Theme.C(o.FrameColor);
            k = tile ? 2.4 : 1;
            kl = tile ? 1.8 : 1;

            bool integrated = o.Frame == MirrorFrame.Bezel || o.Frame == MirrorFrame.Carbon;
            double t = (o.LightStyle == MirrorLightStyle.Dots ? 22 : o.LightStyle == MirrorLightStyle.Bars ? 14 : 6) * kl;
            bool top = o.LightsAt == MirrorLightsAt.Above;
            double ct, cb, cl, cr;
            if (integrated)
            {
                double pad = (o.Frame == MirrorFrame.Carbon ? 20 : 16) * k, band = t + 30 * k;
                ct = o.RevLights && top ? band : pad; cb = o.RevLights && !top ? band : pad;
                cl = cr = o.SideLights ? band : pad;
            }
            else
            {
                double gap = (o.Frame == MirrorFrame.None ? 14 : 28) * k;
                ct = o.RevLights && top ? t + gap : 0; cb = o.RevLights && !top ? t + gap : 0;
                cl = cr = o.SideLights ? t + gap : 0;
            }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cl) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(800) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cr) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ct) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(480) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cb) });
            double r = o.Corners * k;

            if (integrated)
            {
                var housing = new Border
                {
                    CornerRadius = new CornerRadius(r + 14 * k), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
                    Background = o.Frame == MirrorFrame.Carbon ? CarbonBrush(tile ? 3 : 1) : BezelBrush(),
                };
                if (effects) housing.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 46, ShadowDepth = 22, Direction = 270, Opacity = 0.6 };
                Span(housing); g.Children.Add(housing);
                var sheen = new Border { CornerRadius = new CornerRadius(r + 14 * k) };
                if (o.Frame == MirrorFrame.Carbon) sheen.Background = Gradient(Color.FromArgb(0x14, 255, 255, 255), Color.FromArgb(0x70, 0, 0, 0));
                sheen.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)); sheen.BorderThickness = new Thickness(1);
                Span(sheen); g.Children.Add(sheen);
            }

            // the screen, clipped to its corners; frames that sit outside it are siblings (the clip would cut them)
            var screen = new Grid { Width = 800, Height = 480 };
            Grid.SetRow(screen, 1); Grid.SetColumn(screen, 1);
            inner = new Grid { Background = Brushes.Black, Clip = new RectangleGeometry(new Rect(0, 0, 800, 480), r, r) };
            inner.Children.Add(new Border { Background = new LinearGradientBrush(Theme.C("#1C2027"), Theme.C("#07080A"), 60) });
            if (tile)
            {
                // a stand-in dash: a dial and a row of boxes
                inner.Children.Add(new Ellipse { Width = 340, Height = 190, Fill = Theme.B("#252B3A"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, 0, 0) });
                inner.Children.Add(new Border { Height = 110, Margin = new Thickness(40, 0, 40, 40), VerticalAlignment = VerticalAlignment.Bottom, Background = Theme.B("#1B202B"), CornerRadius = new CornerRadius(14) });
            }
            else inner.Children.Add(picture);
            if (integrated)
                inner.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x10, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.45, 0.5)), IsHitTestVisible = false });
            screen.Children.Add(inner);
            switch (o.Frame)
            {
                case MirrorFrame.Line:
                    screen.Children.Add(Ring(-9 * k, 2 * k, r + 9 * k, accent));
                    break;
                case MirrorFrame.Glow:
                    if (tile) screen.Children.Add(Ring(-2 * k - 18, 18, r + 2 * k + 18, accent, 0.3)); // the halo, drawn as a wide faint ring
                    var glow = Ring(-2 * k, 2 * k, r + 2 * k, accent);
                    if (effects) { var fx = new DropShadowEffect { Color = accent.Color, BlurRadius = 46, ShadowDepth = 0, Opacity = 0.85 }; accentEffects.Add(fx); glow.Effect = fx; }
                    screen.Children.Add(glow);
                    break;
                case MirrorFrame.Carbon:
                    screen.Children.Add(Ring(-2 * k, 2 * k, r + 2 * k, new SolidColorBrush(Theme.C("#050505"))));
                    var pipe = Ring(-3 * k, 1 * k, r + 3 * k, accent);
                    if (effects) { var fx = new DropShadowEffect { Color = accent.Color, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.5 }; accentEffects.Add(fx); pipe.Effect = fx; }
                    screen.Children.Add(pipe);
                    break;
                case MirrorFrame.Bezel:
                    screen.Children.Add(Ring(-1 * k, 1 * k, r + 1 * k, Brushes.Black));
                    screen.Children.Add(Ring(-3 * k, 2 * k, r + 3 * k, new SolidColorBrush(Theme.C(tile ? "#2A2D34" : "#16181C"))));
                    break;
            }
            g.Children.Add(screen);

            // the lights
            rev = new Shape[0]; left = new Shape[0]; right = new Shape[0];
            if (o.RevLights)
            {
                var s = Strip(15, true, out rev);
                Grid.SetRow(s, top ? 0 : 2); Grid.SetColumn(s, 1);
                g.Children.Add(s);
            }
            if (o.SideLights)
            {
                var l = Strip(3, false, out left); Grid.SetRow(l, 1); Grid.SetColumn(l, 0); g.Children.Add(l);
                var rr = Strip(3, false, out right); Grid.SetRow(rr, 1); Grid.SetColumn(rr, 2); g.Children.Add(rr);
            }
            shown = new Color?[rev.Length + left.Length + right.Length];
            Child = g;
            Step(0, still: true);
        }

        /// <summary>One frame of the lights: a rev sweep green-red-blue and the side lights taking turns (`still`: a fixed picture for tiles).</summary>
        public void Step(double seconds, bool still = false)
        {
            int n = still ? 9 : (int)Math.Min(15, (Math.Sin(seconds * 1.5 - Math.PI / 2) + 1) / 2 * 16);
            bool leftOn = still || ((int)(seconds * 1.4) % 2 == 0), rightOn = !still && !leftOn;
            Color lead = Idle;
            int idx = 0;
            for (int i = 0; i < rev.Length; i++)
            {
                Color c = i < 5 ? Green : i < 10 ? Red : Blue;
                if (i < n) lead = c;
                Light(rev[i], idx++, i < n ? c : (Color?)null);
            }
            for (int i = 0; i < left.Length; i++) Light(left[i], idx++, leftOn ? Amber : (Color?)null);
            for (int i = 0; i < right.Length; i++) Light(right[i], idx++, rightOn ? Amber : (Color?)null);
            if (followRev && accent.Color != lead)
            {
                accent.Color = lead;
                foreach (var fx in accentEffects) fx.Color = lead;
            }
        }

        // ---------- pieces ----------

        private void Light(Shape s, int index, Color? c)
        {
            if (index < shown.Length && shown[index] == c) return;
            if (index < shown.Length) shown[index] = c;
            bool dots = o.LightStyle == MirrorLightStyle.Dots;
            if (c == null) { s.Fill = Off(); s.Effect = null; return; }
            var col = c.Value;
            s.Fill = dots
                ? new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.28), Center = new Point(0.5, 0.5), RadiusX = 0.6, RadiusY = 0.6,
                                            GradientStops = { new GradientStop(Mix(col, Colors.White, 0.55), 0), new GradientStop(col, 0.55) } }
                : (Brush)new SolidColorBrush(col);
            s.Effect = effects ? new DropShadowEffect { Color = col, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 } : null;
        }

        private Brush Off() => o.LightStyle == MirrorLightStyle.Dots
            ? new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), GradientStops = { new GradientStop(Theme.C("#30343C"), 0), new GradientStop(Theme.C("#111317"), 0.72) } }
            : (Brush)new SolidColorBrush(Theme.C(o.LightStyle == MirrorLightStyle.Bars ? "#1B1E24" : "#24272E"));

        private FrameworkElement Strip(int count, bool horizontal, out Shape[] shapes)
        {
            shapes = new Shape[count];
            var style = o.LightStyle;
            Panel panel;
            if (horizontal && style != MirrorLightStyle.Dots) panel = new UniformGrid { Columns = count, VerticalAlignment = VerticalAlignment.Center };
            else panel = new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            double dotGapH = tile ? 2 : 6, dotGapV = tile ? 3 : 8; // (tile dots are bigger: less room between them)
            for (int i = 0; i < count; i++)
            {
                Shape s;
                if (style == MirrorLightStyle.Dots)
                    s = new Ellipse { Width = 22 * kl, Height = 22 * kl, Margin = horizontal ? new Thickness(dotGapH, 0, dotGapH, 0) : new Thickness(0, dotGapV, 0, dotGapV) };
                else
                {
                    double thick = (style == MirrorLightStyle.Bars ? 14 : 6) * kl, radius = (style == MirrorLightStyle.Bars ? 4 : 3) * kl;
                    double edge = style == MirrorLightStyle.Bars ? 3 : 1.5;
                    s = horizontal
                        ? new Rectangle { Height = thick, RadiusX = radius, RadiusY = radius, Margin = new Thickness(edge, 0, edge, 0) }
                        : new Rectangle { Width = thick, Height = 46 * kl, RadiusX = radius, RadiusY = radius, Margin = new Thickness(0, 4, 0, 4) };
                }
                s.Fill = Off();
                shapes[i] = s;
                panel.Children.Add(s);
            }
            return panel;
        }

        private static Border Ring(double margin, double thickness, double radius, Brush brush, double opacity = 1) => new Border
        {
            Margin = new Thickness(margin), BorderBrush = brush, BorderThickness = new Thickness(thickness), CornerRadius = new CornerRadius(radius), IsHitTestVisible = false, Opacity = opacity,
        };

        private static void Span(UIElement e) { Grid.SetColumnSpan(e, 3); Grid.SetRowSpan(e, 3); }

        private static Brush Gradient(Color a, Color b) => new LinearGradientBrush(a, b, new Point(0.15, 0), new Point(0.85, 1));

        private static Brush BezelBrush() => new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Theme.C("#31353D"), 0), new GradientStop(Theme.C("#191B20"), 0.36), new GradientStop(Theme.C("#0D0E11"), 1),
        }, new Point(0.1, 0), new Point(0.9, 1));

        /// <summary>A woven look: a two-tone twill tile (the page uses CSS gradients for the same thing); `scale` for the small tiles.</summary>
        private static Brush CarbonBrush(double scale)
        {
            var tileDrawing = new DrawingGroup();
            tileDrawing.Children.Add(new GeometryDrawing(Theme.B("#131313"), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            foreach (var (x, y, c) in new[] { (0, 0, "#1D1D1D"), (10, 10, "#1D1D1D"), (10, 0, "#262626"), (0, 10, "#262626") })
                tileDrawing.Children.Add(new GeometryDrawing(new LinearGradientBrush(Theme.C(c), Theme.C("#101010"), 45), null, new RectangleGeometry(new Rect(x + 0.5, y + 0.5, 9, 9), 1.5, 1.5)));
            return new DrawingBrush(tileDrawing)
            {
                TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20 * scale, 20 * scale), ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 20, 20), ViewboxUnits = BrushMappingMode.Absolute,
            };
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
