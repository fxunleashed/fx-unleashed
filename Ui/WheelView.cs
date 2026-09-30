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
    /// rev lights 23-37 (left to right).
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
        }

        private static double RevX(int i) => Mid - 101 + i * (202.0 / 14);

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
