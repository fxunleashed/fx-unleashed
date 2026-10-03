using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The header pill for the screen's RAM drive (shown only while the drive is switched on): a small bar that fills as the
    /// drive does, its colour running green, amber, red, and the number beside it ("334 / 350 KB"). A full drive is normal (the
    /// least recently used pictures are replaced when something new loads), so red means "full", not "broken"; the tooltip says
    /// so. No pulsing or glow: just the bar, in one colour.
    /// </summary>
    internal sealed class RamPill : Border
    {
        private const double BarW = 84, BarH = 8;
        private readonly Border fill;
        private readonly TextBlock used = new TextBlock(), of = new TextBlock();
        private double fraction = -1;

        public RamPill()
        {
            Background = Theme.Raised; BorderBrush = Theme.Line; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(12); Padding = new Thickness(11, 4, 12, 4); Margin = new Thickness(8, 0, 0, 0);
            Visibility = Visibility.Collapsed;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = "SCREEN RAM", FontFamily = Theme.Display, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text3,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0),
            });

            // a recessed track with the fill inside it
            var bar = new Grid { Width = BarW, Height = BarH, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            bar.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(BarH / 2), Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0D)),
                BorderBrush = Theme.Line2, BorderThickness = new Thickness(1),
            });
            fill = new Border { CornerRadius = new CornerRadius((BarH - 2) / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = BarH - 2, Margin = new Thickness(1) };
            bar.Children.Add(fill);
            row.Children.Add(bar);

            used.FontFamily = of.FontFamily = Theme.Display;
            used.FontSize = of.FontSize = 11.5;
            used.FontWeight = FontWeights.SemiBold;
            used.Foreground = Theme.Text; of.Foreground = Theme.Text3;
            used.VerticalAlignment = of.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(used);
            row.Children.Add(of);
            Child = row;
        }

        /// <summary>The drive's state: used and budget in (accounted) bytes, the pictures on it, their data, the per-file overhead.</summary>
        public void Update(int usedBytes, int budgetBytes, int files, int dataBytes, int overhead, bool loading)
        {
            double f = budgetBytes <= 0 ? 0 : Math.Max(0, Math.Min(1, usedBytes / (double)budgetBytes));
            used.Text = (usedBytes / 1024).ToString();
            of.Text = " / " + budgetBytes / 1024 + " KB";
            if (Math.Abs(f - fraction) > 0.005)
            {
                bool first = fraction < 0;
                fraction = f;
                var c = Ramp(f);
                fill.Background = new SolidColorBrush(c);
                double w = Math.Max(0, f * (BarW - 2));
                if (first) fill.Width = w;
                else fill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            ToolTip = $"The screen's RAM drive (the dashes' pictures)\n" +
                      $"{files} picture file{(files == 1 ? "" : "s")}: {dataBytes / 1024} KB of data + {overhead} B per file = {usedBytes / 1024} KB counted\n" +
                      $"of the {budgetBytes / 1024} KB the plugin fills (the drive is 384 KB; the rest is the screen's own overhead).\n" +
                      "Full is normal: the least recently used pictures are replaced when a dash that isn't loaded shows." +
                      (loading ? "\nPictures are being loaded into the screen now." : "");
        }

        /// <summary>Green while there's room, through amber, to red as the drive fills.</summary>
        private static Color Ramp(double f)
        {
            if (f <= 0.55) return Theme.GreenC;
            if (f <= 0.82) return Lerp(Theme.GreenC, Theme.AmberC, (f - 0.55) / 0.27);
            return Lerp(Theme.AmberC, Theme.RedC, (f - 0.82) / 0.18);
        }

        private static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    }
}
