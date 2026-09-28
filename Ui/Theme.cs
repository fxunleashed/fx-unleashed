using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The settings page's look, after the logo: black carbon, red neon, white lines. Colours, the control styles
    /// (applied to everything under the page, the older sections too) and small builders for the recurring pieces.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color PageC = C("#0A0B0D"), CardC = C("#121418"), RaisedC = C("#1A1D23"), LineC = C("#262A31"),
                                     Line2C = C("#343944"), TextC = C("#EEF0F3"), Text2C = C("#A1A8B3"), Text3C = C("#6A717C"),
                                     RedC = C("#FF1F2D"), RedDeepC = C("#8C0A12"), GreenC = C("#34D27B"), AmberC = C("#F5A524"),
                                     BlueC = C("#4DA3FF");

        public static readonly Brush Page = B(PageC), Card = B(CardC), Raised = B(RaisedC), Line = B(LineC), Line2 = B(Line2C),
                                     Text = B(TextC), Text2 = B(Text2C), Text3 = B(Text3C), Red = B(RedC), Green = B(GreenC),
                                     Amber = B(AmberC), Blue = B(BlueC), RedWash = B(Color.FromArgb(0x22, RedC.R, RedC.G, RedC.B));

        public static readonly FontFamily Display = new FontFamily("Bahnschrift, Segoe UI");
        public static readonly FontFamily Body = new FontFamily("Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");
        public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush B(string hex) => B(C(hex));

        private static ResourceDictionary styles;

        /// <summary>Implicit styles for the page: buttons, switches (check boxes), tabs, sliders, text boxes, combo boxes.</summary>
        public static ResourceDictionary Styles => styles ?? (styles = (ResourceDictionary)XamlReader.Parse(Xaml));

        public static void Apply(FrameworkElement root)
        {
            root.Resources.MergedDictionaries.Add(Styles);
            TextElement.SetForeground(root, Text);
            TextElement.SetFontFamily(root, Body);
            TextElement.SetFontSize(root, 13);
        }

        // ---------- builders ----------

        public static Border CardBox(UIElement child, double pad = 18, Thickness? margin = null) => new Border
        {
            Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(pad), Margin = margin ?? new Thickness(0, 0, 0, 14), Child = child,
        };

        /// <summary>A small caps heading over a card's content, e.g. "CURRENT CAR".</summary>
        public static TextBlock Eyebrow(string text, Brush brush = null) => new TextBlock
        {
            Text = text.ToUpperInvariant(), FontFamily = Display, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = brush ?? Text3, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis,
        };

        public static TextBlock Title(string text, double size = 20) => new TextBlock
        {
            Text = text, FontFamily = Display, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Text,
            TextWrapping = TextWrapping.Wrap,
        };

        public static TextBlock Note(string text, Thickness? margin = null) => new TextBlock
        {
            Text = text, Foreground = Text2, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = margin ?? new Thickness(0, 0, 0, 10),
            MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, LineHeight = 18,
        };

        public static TextBlock Icon(string glyph, double size = 16, Brush brush = null) => new TextBlock
        {
            Text = glyph, FontFamily = Icons, FontSize = size, Foreground = brush ?? Text2, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>A switch with a label (and an optional hint under it).</summary>
        public static CheckBox Switch(string label, bool value, Action<bool> set, string hint = null)
        {
            object content = label;
            if (hint != null)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
                sp.Children.Add(new TextBlock { Text = hint, Foreground = Text3, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                content = sp;
            }
            var cb = new CheckBox { Content = content, IsChecked = value, Margin = new Thickness(0, 0, 0, 12) };
            cb.Checked += (s, e) => set(true);
            cb.Unchecked += (s, e) => set(false);
            return cb;
        }

        public static Button Btn(string text, Action click, bool primary = false, string icon = null)
        {
            object content = text;
            if (icon != null)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                content = sp;
            }
            var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8) };
            if (primary) b.Style = (Style)Styles["PrimaryButton"];
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>A label on the left, a control on the right.</summary>
        public static FrameworkElement Field(string label, UIElement control, double labelWidth = 170)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
            var l = new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center, Foreground = Text2 };
            DockPanel.SetDock(l, Dock.Left);
            row.Children.Add(l);
            if (control is FrameworkElement fe && fe.HorizontalAlignment == HorizontalAlignment.Stretch) fe.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(control);
            return row;
        }

        /// <summary>A slider with its value shown after it.</summary>
        public static FrameworkElement SliderField(double min, double max, double value, double step, Func<double, string> show, Action<double> set, double width = 260)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickFrequency = step, IsSnapToTickEnabled = true, Width = width, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Text, FontFamily = Display, Text = show(slider.Value) };
            slider.ValueChanged += (s, e) => { label.Text = show(slider.Value); set(slider.Value); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(slider);
            row.Children.Add(label);
            return row;
        }

        /// <summary>A rounded status chip: a dot and a text.</summary>
        public static Border Pill(out TextBlock text, out Ellipse dot)
        {
            dot = new Ellipse { Width = 7, Height = 7, Fill = Text3, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            text = new TextBlock { FontSize = 11.5, FontFamily = Display, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(text);
            return new Border { Background = Raised, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), Child = sp };
        }

        /// <summary>
        /// Segmented choice (one of a few): pill buttons in a track; `pick` gets the chosen index.
        /// </summary>
        public static FrameworkElement Segmented(string[] options, int selected, Action<int> pick, out Action<int> select)
        {
            var track = new Border { Background = Raised, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            track.Child = sp;
            var items = new List<Border>();
            Action<int> show = i =>
            {
                for (int k = 0; k < items.Count; k++)
                {
                    items[k].Background = k == i ? Red : Brushes.Transparent;
                    ((TextBlock)items[k].Child).Foreground = k == i ? Brushes.White : Text2;
                }
            };
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = options[i], FontFamily = Display, FontSize = 13, FontWeight = FontWeights.SemiBold },
                };
                b.MouseLeftButtonUp += (s, e) => { show(k); pick(k); };
                items.Add(b);
                sp.Children.Add(b);
            }
            show(selected);
            select = show;
            return track;
        }

        /// <summary>A selectable tile (gallery item) with a red frame when selected.</summary>
        public static Border Tile(UIElement child, double width, Action click, string tooltip = null)
        {
            var t = new Border
            {
                Width = width, Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Child = child, ToolTip = tooltip,
            };
            t.MouseEnter += (s, e) => { if (!IsSelected(t)) t.BorderBrush = Line2; };
            t.MouseLeave += (s, e) => { if (!IsSelected(t)) t.BorderBrush = Line; };
            t.MouseLeftButtonUp += (s, e) => click?.Invoke();
            return t;
        }

        public static void Select(Border tile, bool on)
        {
            tile.Tag = on ? "selected" : null;
            tile.BorderBrush = on ? Red : Line;
            tile.Background = on ? RedWash : Card;
            tile.Effect = on ? new DropShadowEffect { Color = RedC, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.45 } : null;
        }

        private static bool IsSelected(Border b) => (b.Tag as string) == "selected";

        public static ImageSource Resource(string name)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = typeof(Theme).Assembly.GetManifestResourceStream("User.FXProRpmSync." + name);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>A bitmap (System.Drawing) as a WPF image.</summary>
        public static BitmapSource ToImage(System.Drawing.Bitmap bmp)
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

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Red' Color='#FF1F2D'/>
  <SolidColorBrush x:Key='Raised' Color='#1A1D23'/>
  <SolidColorBrush x:Key='Raised2' Color='#22262E'/>
  <SolidColorBrush x:Key='Line' Color='#262A31'/>
  <SolidColorBrush x:Key='Line2' Color='#343944'/>
  <SolidColorBrush x:Key='Text' Color='#EEF0F3'/>
  <SolidColorBrush x:Key='Text2' Color='#A1A8B3'/>
  <SolidColorBrush x:Key='Text3' Color='#6A717C'/>

  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource Raised}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Line2}'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='FontFamily' Value='Bahnschrift, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Background' Value='{StaticResource Raised2}'/>
              <Setter TargetName='b' Property='BorderBrush' Value='#4A505C'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#2A2F38'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='b' Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='7' Padding='{TemplateBinding Padding}' BorderThickness='1' BorderBrush='#FF5560'>
            <Border.Background>
              <LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>
                <GradientStop Color='#FF2B38' Offset='0'/>
                <GradientStop Color='#D5101C' Offset='1'/>
              </LinearGradientBrush>
            </Border.Background>
            <Border.Effect>
              <DropShadowEffect Color='#FF1F2D' BlurRadius='14' ShadowDepth='0' Opacity='0.45'/>
            </Border.Effect>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Opacity' Value='0.9'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='b' Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Check boxes are switches -->
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='Auto'/>
              <ColumnDefinition Width='*'/>
            </Grid.ColumnDefinitions>
            <Border x:Name='track' Width='36' Height='20' CornerRadius='10' Background='#2A2F38' BorderBrush='#3A404B' BorderThickness='1' VerticalAlignment='Top' Margin='0,0,0,0'>
              <Ellipse x:Name='thumb' Width='14' Height='14' Fill='#8A919C' HorizontalAlignment='Left' Margin='2,0,0,0'/>
            </Border>
            <ContentPresenter Grid.Column='1' Margin='10,1,0,0' VerticalAlignment='Top' RecognizesAccessKey='True'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='#FF1F2D'/>
              <Setter TargetName='track' Property='BorderBrush' Value='#FF5560'/>
              <Setter TargetName='thumb' Property='Fill' Value='White'/>
              <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
              <Setter TargetName='thumb' Property='Margin' Value='0,0,2,0'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Grid Width='18' Height='18' VerticalAlignment='Center'>
              <Ellipse Stroke='#4A505C' StrokeThickness='1.5' Fill='#1A1D23'/>
              <Ellipse x:Name='dot' Width='8' Height='8' Fill='#FF1F2D' Visibility='Collapsed'/>
            </Grid>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='dot' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource Raised}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Line2}'/>
    <Setter Property='CaretBrush' Value='White'/>
    <Setter Property='Padding' Value='6,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#FF1F2D'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Sliders: a thin track, red fill, round white thumb -->
  <Style x:Key='SliderThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Ellipse Width='16' Height='16' Fill='White' Stroke='#FF1F2D' StrokeThickness='2'>
            <Ellipse.Effect><DropShadowEffect Color='#FF1F2D' BlurRadius='8' ShadowDepth='0' Opacity='0.6'/></Ellipse.Effect>
          </Ellipse>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='SliderRepeat' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='{TemplateBinding Background}'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='Slider'>
    <Setter Property='Height' Value='22'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Track x:Name='PART_Track' VerticalAlignment='Center'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='Slider.DecreaseLarge' Background='#FF1F2D' Style='{StaticResource SliderRepeat}'/>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='Slider.IncreaseLarge' Background='#2E333C' Style='{StaticResource SliderRepeat}'/>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Style='{StaticResource SliderThumb}'/>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Combo boxes: dark field, dark list -->
  <Style x:Key='ComboToggle' TargetType='ToggleButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='b' Background='#1A1D23' BorderBrush='#343944' BorderThickness='1' CornerRadius='6'>
            <TextBlock Text='&#xE70D;' FontFamily='Segoe MDL2 Assets' FontSize='10' Foreground='#A1A8B3' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#4A505C'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#FF1F2D'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='MinHeight' Value='30'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Style='{StaticResource ComboToggle}' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'/>
            <ContentPresenter IsHitTestVisible='False' Margin='10,4,28,4' VerticalAlignment='Center' HorizontalAlignment='Left'
                              Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <TextBox x:Name='PART_EditableTextBox' Visibility='Collapsed' Margin='3,3,24,3' Background='Transparent' BorderThickness='0'/>
            <Popup IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
              <Border Background='#16181D' BorderBrush='#343944' BorderThickness='1' CornerRadius='6' Margin='0,4,0,0'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='360'>
                <ScrollViewer><ItemsPresenter/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEditable' Value='True'>
              <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='b' Padding='10,6' Background='Transparent'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#262A31'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#3A0D12'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Expander'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Expander'>
          <StackPanel>
            <ToggleButton Cursor='Hand' HorizontalAlignment='Left' Content='{TemplateBinding Header}'
                          IsChecked='{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <StackPanel Orientation='Horizontal' Background='Transparent'>
                    <TextBlock x:Name='chev' Text='&#xE76C;' FontFamily='Segoe MDL2 Assets' FontSize='11' Foreground='#FF1F2D' VerticalAlignment='Center' Margin='0,0,10,0'/>
                    <ContentPresenter VerticalAlignment='Center' TextElement.FontFamily='Bahnschrift, Segoe UI' TextElement.Foreground='#EEF0F3'/>
                  </StackPanel>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsChecked' Value='True'>
                      <Setter TargetName='chev' Property='Text' Value='&#xE70D;'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name='body' Visibility='Collapsed' Margin='0,12,0,0'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsExpanded' Value='True'>
              <Setter TargetName='body' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='#1A1D23'/>
    <Setter Property='Foreground' Value='#EEF0F3'/>
    <Setter Property='BorderBrush' Value='#343944'/>
  </Style>
</ResourceDictionary>";
    }
}
