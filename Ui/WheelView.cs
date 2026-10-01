using System;
using System.Collections.Generic;
using Path = System.Windows.Shapes.Path;
using StreamReader = System.IO.StreamReader;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A wheel drawn from the front with its LEDs (WheelModel). The GT Neo is traced from SimPro's front picture of it
    /// (assets/gtneo-outline.svg, tools/brand/trace_gtneo.py): its outline and grip openings, rev slats, ring segments and
    /// buttons measured from the picture's LED cut-outs. The FX Pro, with all 38 LEDs where they are on the wheel: the outline is traced from
    /// Simagic's front photo (assets/fxpro-outline.svg, 663x396), the LED positions measured on the same photo.
    /// Shows an LED frame (colour x brightness, with a glow), optionally a picture in the screen (the dash preview).
    /// LED numbers as the firmware counts them (mapped with a camera): buttons 0-5 left / 6-11 right (see LeftButtons),
    /// encoders 12-16 (ABS, TC, BB, DIFF, MAP), the lights beside the rev bar 17-19 left / 20-22 right (top to bottom),
    /// rev lights 23-37 (left to right). Also the FX Pro's controls without LEDs (thumb rollers, funky switch, upper
    /// paddles behind the thumb openings; FxProControls) and, on Press, which button a control sent.
    /// </summary>
    public class WheelView : Viewbox
    {
        public const double W = 663, H = 396;
        private const double Mid = 331.5;

        private enum Kind { Rev, Side, Button, Encoder, Ring, Slat }

        // Where each LED is (mapped on the wheel with a camera, 2026-09-27): per side, the lower cluster (outer-high,
        // inner-high, middle, bottom) and the top pair (outer, inner). Left: 0-3 = lower cluster bottom-up, 4 = top outer
        // (mic), 5 = top inner (PIT). Right: 6 = top inner, 7 = top outer, 8-11 = lower cluster top-down.
        private static readonly (double X, double Y) TopOuter = (101.7, 61.6), TopInner = (167.4, 76.1), OuterHigh = (151.8, 217.5),
                                                     InnerHigh = (192.9, 245.3), Middle = (174.0, 284.3), Bottom = (203.0, 325.5);
        private static readonly (double X, double Y)[] LeftButtons = { Bottom, Middle, InnerHigh, OuterHigh, TopOuter, TopInner };
        private static readonly (double X, double Y)[] RightButtons = { TopInner, TopOuter, OuterHigh, InnerHigh, Middle, Bottom }; // mirrored
        /// <summary>Encoders 12-16.</summary>
        private static readonly (double X, double Y, string Name)[] Encoders =
        {
            (260.8, 287.6, "ABS"), (402.2, 287.6, "TC"), (Mid, 239.7, "BB"), (251.9, 217.5, "DIFF"), (411.1, 217.5, "MAP"),
        };

                private static readonly Dictionary<int, (Brush Core, Brush Halo, Brush Rim)> brushes = new Dictionary<int, (Brush, Brush, Brush)>();

        private readonly Canvas canvas = new Canvas { Width = W, Height = H };
        private readonly Shape[] cores;
        private readonly Ellipse[] halos;
        private readonly Kind[] kinds;
        private readonly int[] shown;
        private readonly Ellipse[] marks;
        private readonly bool glow;

        /// <summary>The wheel drawn.</summary>
        public WheelModel Model { get; }
        private bool reverseRev;

        public Image Screen { get; } = new Image { Stretch = Stretch.Uniform };

        /// <summary>An LED was clicked (its number).</summary>
        public event Action<int> LedClicked;

        private static readonly Brush Unlit = Theme.B("#1B1E23"), UnlitRim = Theme.B("#2E323A"), KnobFill = Theme.B("#111316");

        /// <param name="glow">Halos and the neon outline (the big view); off for the small gallery ones.</param>
        public WheelView(bool glow = true) : this(WheelModel.FxPro, glow) { }

        public WheelView(WheelModel model, bool glow = true)
        {
            Model = model ?? WheelModel.FxPro;
            this.glow = glow;
            int n = Model.LedCount;
            cores = new Shape[n]; halos = new Ellipse[n]; kinds = new Kind[n]; shown = new int[n]; marks = new Ellipse[n];
            Stretch = Stretch.Uniform;
            Child = canvas;
            for (int i = 0; i < shown.Length; i++) shown[i] = -1;

            var geo = Outline(Model == WheelModel.GtNeo ? "gtneo-outline.svg" : "fxpro-outline.svg");
            if (Model == WheelModel.FxPro) BuildPaddles(); // behind the body: seen through the thumb openings
            if (geo != null)
            {
                if (glow)
                    canvas.Children.Add(new Path
                    {
                        Data = geo, Stroke = Theme.Red, StrokeThickness = 9, Opacity = 0.55, StrokeLineJoin = PenLineJoin.Round,
                        Effect = new BlurEffect { Radius = 14 }, CacheMode = new BitmapCache(), IsHitTestVisible = false,
                    });
                var body = new LinearGradientBrush(Color.FromRgb(0x1A, 0x1C, 0x21), Color.FromRgb(0x0B, 0x0C, 0x0E), 90);
                body.Freeze();
                canvas.Children.Add(new Path
                {
                    Data = geo, Fill = body, Stroke = Brushes.White, StrokeThickness = glow ? 2.6 : 4, StrokeLineJoin = PenLineJoin.Round,
                    CacheMode = new BitmapCache(), IsHitTestVisible = false,
                });
            }

            if (Model == WheelModel.GtNeo) { BuildGtNeo(); return; }

            // Bezel and screen
            Add(new Rectangle { Width = 262, Height = 148, RadiusX = 12, RadiusY = 12, Fill = Theme.B("#050607"), Stroke = Theme.B("#30343C"), StrokeThickness = 1.5 }, 201.5, 28);
            var screenFrame = new Border { Width = 206, Height = 114, Background = Brushes.Black, BorderBrush = Theme.B("#1F2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = Screen };
            RenderOptions.SetBitmapScalingMode(Screen, BitmapScalingMode.HighQuality);
            Add(screenFrame, 228.5, 54);

            // Encoder knobs (under their LEDs' rings)
            for (int i = 0; i < 5; i++)
            {
                var (x, y, name) = Encoders[i];
                Led(12 + i, Kind.Encoder, x, y, 15, "Encoder " + name);
            }
            // Buttons
            string[] names = { "bottom", "middle", "inner", "outer", "top outer", "top inner" };
            string[] rightNames = { "top inner", "top outer", "outer", "inner", "middle", "bottom" };
            for (int i = 0; i < 6; i++)
            {
                Led(i, Kind.Button, LeftButtons[i].X, LeftButtons[i].Y, 12, "Left button, " + names[i]);
                Led(6 + i, Kind.Button, 2 * Mid - RightButtons[i].X, RightButtons[i].Y, 12, "Right button, " + rightNames[i]);
            }
            // Lights beside the rev bar, top to bottom
            for (int i = 0; i < 3; i++)
            {
                Led(17 + i, Kind.Side, 212, 72 + i * 20, 5, $"Left light {i + 1}");
                Led(20 + i, Kind.Side, 2 * Mid - 212, 72 + i * 20, 5, $"Right light {i + 1}");
            }
            // Rev lights
            for (int i = 0; i < 15; i++) Led(23 + i, Kind.Rev, RevX(i), 41, 5, $"Rev light {i + 1}");
            BuildFxProControls();
            canvas.Children.Add(effects);
        }

        private static double RevX(int i) => Mid - 101 + i * (202.0 / 14);

        // ---------- FX Pro: the controls without LEDs, and the press animations ----------

        /// <summary>Press animations and button numbers, above everything.</summary>
        private readonly Canvas effects = new Canvas { Width = W, Height = H, IsHitTestVisible = false };

        private static readonly Brush Ridge = Theme.B("#B8343F"), RollerBody = Theme.B("#25282E"), Bracket = Theme.B("#0D0E10");

        /// <summary>The upper (shift) paddles: carbon plates behind the thumb openings.</summary>
        private void BuildPaddles()
        {
            foreach (var id in new[] { "l-paddle", "r-paddle" })
            {
                var c = FxProControls.ById(id);
                var carbon = new LinearGradientBrush(Color.FromRgb(0x22, 0x24, 0x29), Color.FromRgb(0x10, 0x11, 0x14), 60);
                carbon.Freeze();
                var plate = new Rectangle { Width = 58, Height = 82, RadiusX = 10, RadiusY = 10, Fill = carbon, Stroke = Theme.B("#2B2F36"), StrokeThickness = 1, ToolTip = c.Name };
                Add(plate, c.X - 29, c.Y - 44);
            }
        }

        /// <summary>The thumb rollers and the funky switch (from SimPro's front picture of the wheel).</summary>
        private void BuildFxProControls()
        {
            foreach (var c in FxProControls.All)
            {
                if (c.Kind == FxProControls.Kind.RollerSideways)
                {
                    // an upright cylinder at the top of the grip, ridges running up and down: it rolls sideways
                    var g = new Grid { Width = 30, Height = 40, ToolTip = c.Name };
                    g.Children.Add(new Rectangle { RadiusX = 6, RadiusY = 6, Fill = RollerBody, Stroke = UnlitRim, StrokeThickness = 1.5 });
                    for (int i = 0; i < 6; i++)
                        g.Children.Add(new Rectangle
                        {
                            Width = 2.2, Height = 15, Fill = Ridge, RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4.5 + i * 4, 0, 0, 5),
                        });
                    Add(g, c.X - 15, c.Y - 20);
                }
                else if (c.Kind == FxProControls.Kind.RollerUpDown)
                {
                    // a narrow wheel on a bracket at the inner edge of the thumb opening, ridges across: it rolls up and down
                    bool left = c.X < Mid;
                    Add(new Rectangle { Width = 14, Height = 22, RadiusX = 2, RadiusY = 2, Fill = Bracket, Stroke = UnlitRim, StrokeThickness = 1 }, left ? c.X - 20 : c.X + 6, c.Y - 13);
                    var g = new Grid { Width = 16, Height = 34, ToolTip = c.Name };
                    g.Children.Add(new Rectangle { RadiusX = 5, RadiusY = 5, Fill = RollerBody, Stroke = UnlitRim, StrokeThickness = 1.5 });
                    for (int i = 0; i < 6; i++)
                        g.Children.Add(new Rectangle
                        {
                            Width = 12, Height = 2.2, Fill = Ridge, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 4.5 + i * 4.7, 0, 0),
                        });
                    Add(g, c.X - 8, c.Y - 17);
                }
                else if (c.Kind == FxProControls.Kind.Funky)
                {
                    // the funky switch: a small knob that tilts four ways, pushes and turns
                    Add(new Ellipse { Width = 24, Height = 24, Fill = KnobFill, Stroke = UnlitRim, StrokeThickness = 2.5, ToolTip = c.Name }, c.X - 12, c.Y - 12);
                    Add(new Ellipse { Width = 9, Height = 9, Fill = Theme.B("#2A2E35"), IsHitTestVisible = false }, c.X - 4.5, c.Y - 4.5);
                    foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                        Add(new Ellipse { Width = 2.4, Height = 2.4, Fill = Theme.B("#4A4F58"), IsHitTestVisible = false }, c.X + dx * 16 - 1.2, c.Y + dy * 16 - 1.2);
                }
            }
        }

        /// <summary>How far an effect sits from a control's centre.</summary>
        private static double Reach(FxProControls.Kind k)
        {
            switch (k)
            {
                case FxProControls.Kind.Knob: return 19;
                case FxProControls.Kind.RollerSideways: return 22;
                case FxProControls.Kind.RollerUpDown: return 19;
                case FxProControls.Kind.Funky: return 15;
                case FxProControls.Kind.Paddle: return 30;
                case FxProControls.Kind.Clutch: return 18;
                default: return 15;
            }
        }

        /// <summary>
        /// A wheel button went down (FX Pro): its number pops up on the control it belongs to, with a ring for a press, a
        /// turning arc for a knob, and chevrons sliding the way a roller or the funky switch went. Unknown buttons are
        /// ignored. Call on the UI thread.
        /// </summary>
        public void Press(int button, UsbSettings settings)
        {
            if (Model != WheelModel.FxPro) return;
            var m = FxProControls.Lookup(button, settings);
            if (m == null) return;
            var (c, dir) = m.Value;
            double r = Reach(c.Kind);
            switch (dir)
            {
                case FxProControls.Dir.Press: Ring(c.X, c.Y, r); break;
                case FxProControls.Dir.Clockwise: Arc(c.X, c.Y, r + 3, true); break;
                case FxProControls.Dir.Anticlockwise: Arc(c.X, c.Y, r + 3, false); break;
                case FxProControls.Dir.Up: Chevrons(c.X, c.Y, r, 0, -1); break;
                case FxProControls.Dir.Down: Chevrons(c.X, c.Y, r, 0, 1); break;
                case FxProControls.Dir.Left: Chevrons(c.X, c.Y, r, -1, 0); break;
                case FxProControls.Dir.Right: Chevrons(c.X, c.Y, r, 1, 0); break;
            }
            Badge(button, c.X, c.Y - r - 13);
        }

        private void Run(UIElement e, double seconds, Action<Storyboard> fill)
        {
            effects.Children.Add(e);
            var sb = new Storyboard { Duration = TimeSpan.FromSeconds(seconds) };
            fill(sb);
            sb.Completed += (s, a) => effects.Children.Remove(e);
            sb.Begin();
        }

        private static DoubleAnimation Anim(DependencyObject target, string path, double from, double to, double begin, double secs, IEasingFunction ease = null)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(secs)) { BeginTime = TimeSpan.FromSeconds(begin), EasingFunction = ease };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, new PropertyPath(path));
            return a;
        }

        /// <summary>The button's number in a red pill: pops in, holds, fades.</summary>
        private void Badge(int button, double cx, double cy)
        {
            var text = new TextBlock
            {
                Text = button.ToString(), FontFamily = Theme.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var scale = new ScaleTransform(0.4, 0.4);
            var pill = new Border
            {
                Background = Theme.Red, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), MinWidth = 22, Child = text,
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale, Opacity = 0,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 },
            };
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(pill, cx - pill.DesiredSize.Width / 2);
            Canvas.SetTop(pill, Math.Max(0, cy - pill.DesiredSize.Height / 2));
            var back = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            Run(pill, 1.4, sb =>
            {
                sb.Children.Add(Anim(pill, "Opacity", 0, 1, 0, 0.08));
                sb.Children.Add(Anim(scale, "ScaleX", 0.4, 1, 0, 0.25, back));
                sb.Children.Add(Anim(scale, "ScaleY", 0.4, 1, 0, 0.25, back));
                sb.Children.Add(Anim(pill, "Opacity", 1, 0, 1.05, 0.35));
            });
        }

        /// <summary>A press: a ring that spreads out and fades.</summary>
        private void Ring(double cx, double cy, double r)
        {
            var scale = new ScaleTransform(0.8, 0.8);
            var ring = new Ellipse
            {
                Width = 2 * r, Height = 2 * r, Stroke = Theme.Red, StrokeThickness = 2.5,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            };
            Canvas.SetLeft(ring, cx - r); Canvas.SetTop(ring, cy - r);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            Run(ring, 0.6, sb =>
            {
                sb.Children.Add(Anim(scale, "ScaleX", 0.8, 1.7, 0, 0.6, ease));
                sb.Children.Add(Anim(scale, "ScaleY", 0.8, 1.7, 0, 0.6, ease));
                sb.Children.Add(Anim(ring, "Opacity", 1, 0, 0, 0.6));
            });
        }

        /// <summary>A knob or the funky switch turned: an arrowed arc sweeping round it the way it went.</summary>
        private void Arc(double cx, double cy, double r, bool clockwise)
        {
            // a 100-degree arc over the top, with an arrow head at its leading end
            double a0 = -140, a1 = -40;
            Point P(double deg, double rad) => new Point(r + rad * Math.Cos(deg * Math.PI / 180), r + rad * Math.Sin(deg * Math.PI / 180));
            var fig = new PathFigure { StartPoint = P(clockwise ? a0 : a1, r) };
            fig.Segments.Add(new ArcSegment(P(clockwise ? a1 : a0, r), new Size(r, r), 0, false, clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
            double tip = clockwise ? a1 : a0, back = clockwise ? -14 : 14;
            var head = new PathFigure { StartPoint = P(tip + back, r - 5) };
            head.Segments.Add(new LineSegment(P(tip, r), true));
            head.Segments.Add(new LineSegment(P(tip + back, r + 5), true));
            var rot = new RotateTransform(clockwise ? -50 : 50);
            var path = new Path
            {
                Data = new PathGeometry(new[] { fig, head }), Stroke = Theme.Red, StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 2 * r, Height = 2 * r, Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rot,
            };
            Canvas.SetLeft(path, cx - r); Canvas.SetTop(path, cy - r);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Run(path, 0.7, sb =>
            {
                sb.Children.Add(Anim(rot, "Angle", clockwise ? -50 : 50, clockwise ? 70 : -70, 0, 0.7, ease));
                sb.Children.Add(Anim(path, "Opacity", 0, 1, 0, 0.1));
                sb.Children.Add(Anim(path, "Opacity", 1, 0, 0.35, 0.35));
            });
        }

        /// <summary>A roller or the funky switch went one way: two chevrons slide out that way and fade.</summary>
        private void Chevrons(double cx, double cy, double r, int dx, int dy)
        {
            double angle = dx > 0 ? 90 : dx < 0 ? -90 : dy > 0 ? 180 : 0; // drawn pointing up
            for (int k = 0; k < 2; k++)
            {
                var move = new TranslateTransform();
                var group = new TransformGroup();
                group.Children.Add(new RotateTransform(angle, 7, 4));
                group.Children.Add(move);
                var chev = new Path
                {
                    Data = Geometry.Parse("M 0,7 L 7,0 L 14,7"), Stroke = Theme.Red, StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 14, Height = 8, Opacity = 0, RenderTransform = group,
                };
                double start = r * 0.55 + k * 7;
                Canvas.SetLeft(chev, cx - 7); Canvas.SetTop(chev, cy - 4);
                double begin = k * 0.08;
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                Run(chev, 0.6 + begin, sb =>
                {
                    sb.Children.Add(Anim(move, "X", dx * start, dx * (start + 14), begin, 0.5, ease));
                    sb.Children.Add(Anim(move, "Y", dy * start, dy * (start + 14), begin, 0.5, ease));
                    sb.Children.Add(Anim(chev, "Opacity", 0, 1, begin, 0.08));
                    sb.Children.Add(Anim(chev, "Opacity", 1, 0, begin + 0.25, 0.3));
                });
            }
        }

        // ---------- GT Neo ----------
        // Traced from SimPro's front picture of the wheel by tools/brand/trace_gtneo.py (assets/gtneo-outline.svg), like
        // the FX Pro's outline. The picture's LEDs are cut-outs, so these positions are measured. Which LED number sits
        // where was checked on the user's wheel with colour patterns (2026-09-29).

        /// <summary>Rev slats 58-72, left to right (centres; all at NeoRevY).</summary>
        private static readonly double[] NeoRevX =
        {
            235.6, 248.8, 262.5, 276.6, 290.2, 304.0, 317.9, 332.1, 345.6, 359.5, 373.6, 387.4, 401.5, 415.2, 428.2,
        };

        private const double NeoRevY = 92.8;

        /// <summary>
        /// Button lights 0-9, anticlockwise round the wheel: 0-4 the right grip from the bottom up (two lower, then three
        /// along its top, 4 = outermost), 5-9 the left grip from the top down (5 = outermost).
        /// </summary>
        private static readonly (double X, double Y)[] NeoButtons =
        {
            (483.7, 252.3), (496.2, 197.4), (496.1, 106.8), (522.0, 68.6), (562.3, 52.4),
            (101.1, 51.7), (142.1, 68.3), (167.9, 106.7), (167.7, 197.5), (180.5, 252.3),
        };

        /// <summary>The encoders whose rings are LEDs 10-21, 22-33, 34-45, 46-57: upper left, upper right, lower left, lower right.</summary>
        private static readonly (double X, double Y)[] NeoRings = { (273.6, 155.8), (391.0, 155.9), (283.1, 274.7), (380.9, 274.6) };

        private const double NeoRingR = 26.6;

        private void BuildGtNeo()
        {
            // the rev bar's housing
            Add(new Rectangle { Width = 214, Height = 16, RadiusX = 4, RadiusY = 4, Fill = Theme.B("#050607"), Stroke = Theme.B("#30343C"), StrokeThickness = 1 }, 225, NeoRevY - 8);
            for (int i = 0; i < 15; i++) Led(58 + i, Kind.Slat, NeoRevX[i], NeoRevY, 6, $"Rev light {i + 1}");
            // encoders: a knob inside its ring of 12 segments, the first at the top, clockwise
            string[] where = { "upper left", "upper right", "lower left", "lower right" };
            for (int k = 0; k < 4; k++)
            {
                var (cx, cy) = NeoRings[k];
                Add(new Ellipse { Width = 36, Height = 36, Fill = KnobFill, Stroke = UnlitRim, StrokeThickness = 2 }, cx - 18, cy - 18);
                for (int j = 0; j < 12; j++)
                {
                    double a = -90 + j * 30;
                    double rad = a * Math.PI / 180;
                    Led(10 + 12 * k + j, Kind.Ring, cx + NeoRingR * Math.Cos(rad), cy + NeoRingR * Math.Sin(rad), 5, $"Encoder ring {k + 1} ({where[k]}), segment {j + 1}", a + 90);
                }
            }
            for (int i = 0; i < 10; i++) Led(i, Kind.Button, NeoButtons[i].X, NeoButtons[i].Y, 14, i < 5 ? $"Right button {5 - i} from the top" : $"Left button {i - 4} from the top");
        }

        /// <summary>Rev LED 23 on the right (the plugin's "fill from the right").</summary>
        public bool ReverseRev
        {
            get => reverseRev;
            set
            {
                if (reverseRev == value || Model != WheelModel.FxPro) return;
                reverseRev = value;
                for (int i = 0; i < 15; i++)
                {
                    double x = RevX(value ? 14 - i : i);
                    Place(cores[23 + i], x); Place(halos[23 + i], x); Place(marks[23 + i], x);
                }
            }
        }

        private static void Place(FrameworkElement e, double cx)
        {
            if (e != null) Canvas.SetLeft(e, cx - e.Width / 2);
        }

        private void Add(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }

        /// <param name="angle">Ring segments: turned by this many degrees (along the ring).</param>
        private void Led(int index, Kind kind, double cx, double cy, double r, string name, double angle = 0)
        {
            kinds[index] = kind;
            if (glow)
            {
                double hr = kind == Kind.Rev || kind == Kind.Side || kind == Kind.Slat ? r * 3.2 : kind == Kind.Ring ? r * 2.2 : r * 2.1;
                var halo = new Ellipse { Width = hr * 2, Height = hr * 2, IsHitTestVisible = false, Visibility = Visibility.Hidden };
                Add(halo, cx - hr, cy - hr);
                halos[index] = halo;
            }
            Shape core;
            if (kind == Kind.Side) core = new Rectangle { Width = r * 1.6, Height = r * 2.6, RadiusX = 2, RadiusY = 2 };
            else if (kind == Kind.Slat) core = new Rectangle { Width = r * 2, Height = r, RadiusX = 1.5, RadiusY = 1.5 };
            else if (kind == Kind.Ring)
                core = new Rectangle { Width = r * 2.1, Height = r, RadiusX = 1.5, RadiusY = 1.5, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(angle) };
            else core = new Ellipse { Width = r * 2, Height = r * 2 };
            core.Fill = kind == Kind.Encoder ? KnobFill : Unlit;
            core.Stroke = UnlitRim;
            core.StrokeThickness = kind == Kind.Encoder ? 3.5 : kind == Kind.Button ? 2 : kind == Kind.Ring || kind == Kind.Slat ? 0.6 : 1;
            core.ToolTip = name + $"  (LED {index})";
            core.Cursor = Cursors.Hand;
            core.MouseLeftButtonUp += (s, e) => LedClicked?.Invoke(index);
            Add(core, cx - core.Width / 2, cy - core.Height / 2);
            cores[index] = core;

            var mark = new Ellipse
            {
                Width = core.Width + 10, Height = core.Height + 10, Stroke = Brushes.White, StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 2, 2 }, IsHitTestVisible = false, Visibility = Visibility.Hidden,
            };
            Add(mark, cx - mark.Width / 2, cy - mark.Height / 2);
            marks[index] = mark;
        }

        /// <summary>Dashed rings around these LEDs (the group being edited); null clears.</summary>
        public void Highlight(IEnumerable<int> leds)
        {
            var set = new HashSet<int>(leds ?? Enumerable.Empty<int>());
            for (int i = 0; i < marks.Length; i++) marks[i].Visibility = set.Contains(i) ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>Shows a frame: what the eye sees is colour x brightness (1-90), with a floor so dim LEDs still read as lit.</summary>
        public void Show(LedColor[] frame)
        {
            for (int i = 0; i < cores.Length; i++)
            {
                var f = frame != null && i < frame.Length ? frame[i] : default(LedColor);
                double k = Math.Min(1, f.Brightness / 90.0);
                int r = f.R, g = f.G, b = f.B;
                bool lit = r + g + b > 20 && f.Brightness > 0;
                int key = -1;
                if (lit)
                {
                    k = Math.Max(0.35, k);
                    key = (int)(r * k) << 16 | (int)(g * k) << 8 | (int)(b * k);
                }
                if (shown[i] == key) continue;
                shown[i] = key;
                var core = cores[i];
                if (!lit)
                {
                    core.Fill = kinds[i] == Kind.Encoder ? KnobFill : Unlit;
                    core.Stroke = UnlitRim;
                    if (halos[i] != null) halos[i].Visibility = Visibility.Hidden;
                    continue;
                }
                var (coreB, haloB, rimB) = BrushesFor(key);
                if (kinds[i] == Kind.Encoder) { core.Fill = KnobFill; core.Stroke = coreB; }
                else { core.Fill = coreB; core.Stroke = rimB; }
                if (halos[i] != null) { halos[i].Fill = haloB; halos[i].Visibility = Visibility.Visible; }
            }
        }

        private static (Brush, Brush, Brush) BrushesFor(int rgb)
        {
            lock (brushes)
            {
                if (brushes.TryGetValue(rgb, out var b)) return b;
                var c = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                var core = Theme.B(c);
                var halo = new RadialGradientBrush(Color.FromArgb(150, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                halo.GradientStops.Insert(1, new GradientStop(Color.FromArgb(60, c.R, c.G, c.B), 0.45));
                halo.Freeze();
                // the rim: the colour lifted towards white, like the lit edge of a button cap
                var rim = Theme.B(Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2)));
                if (brushes.Count > 4000) brushes.Clear();
                return brushes[rgb] = (core, halo, rim);
            }
        }

        private static readonly Dictionary<string, Geometry> outlines = new Dictionary<string, Geometry>();

        /// <summary>A wheel's outline from its embedded SVG's path (M/L points; several subpaths = openings, even-odd).</summary>
        private static Geometry Outline(string resource)
        {
            lock (outlines)
            {
                if (outlines.TryGetValue(resource, out var cached)) return cached;
                Geometry g = null;
                try
                {
                    using (var st = typeof(WheelView).Assembly.GetManifestResourceStream("User.FXProRpmSync." + resource))
                    using (var r = new StreamReader(st))
                    {
                        var m = Regex.Match(r.ReadToEnd(), "\\sd=\"([^\"]+)\"");
                        g = Geometry.Parse(m.Groups[1].Value + (m.Groups[1].Value.TrimEnd().EndsWith("Z") ? "" : " Z"));
                        g.Freeze();
                    }
                }
                catch { }
                return outlines[resource] = g;
            }
        }

        /// <summary>The LEDs of a group on this wheel, in the engine's order.</summary>
        public IEnumerable<int> LedsOf(LedGroup g) => Model.Leds(g);

        /// <summary>The group an LED belongs to.</summary>
        public LedGroup GroupOf(int led) => Model.GroupOf(led) ?? LedGroup.Rev;
    }
}
