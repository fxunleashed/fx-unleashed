using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace User.FXProRpmSync
{
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
            hexBox = new TextBox { Width = 88, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = hex };
            Children.Add(swatch);
            Children.Add(hexBox);
            Show(hex);

            var grid = new UniformGrid { Columns = 8, Margin = new Thickness(6) };
            var popup = new Popup { PlacementTarget = swatch, StaysOpen = false, AllowsTransparency = true };
            foreach (var c in Palette)
            {
                var chip = new Border { Width = 22, Height = 22, Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = Theme.B((Color)ColorConverter.ConvertFromString(c)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                var cc = c;
                chip.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; hexBox.Text = cc; Commit(); };
                grid.Children.Add(chip);
            }
            popup.Child = new Border { Background = Theme.B(Color.FromRgb(0x25, 0x27, 0x2b)), CornerRadius = new CornerRadius(6), Child = grid };
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
            try { swatch.Background = Theme.B((Color)ColorConverter.ConvertFromString(hex)); } catch { swatch.Background = Brushes.Black; }
        }
    }
}
