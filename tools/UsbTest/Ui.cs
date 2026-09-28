using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using User.FXProRpmSync;

static class UiTest
{
    public static void Run(string outFile, bool custom)
    {
        var t = new System.Threading.Thread(() =>
        {
            var plugin = new FXProRpmSyncPlugin { Settings = new FXProRpmSyncSettings() };
            plugin.Settings.Usb.Enabled = true;
            if (custom) { plugin.Settings.Usb.LightsFrom = LightsSource.AtsrHub; plugin.Settings.Usb.CustomLights = LightPresets.Find("synthwave").Clone(); plugin.Settings.Usb.LightPreset = "custom"; }
            var section = new UsbSection(plugin);
            var w = new Window { Width = 1180, Height = 900, Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x21, 0x25)), Foreground = Brushes.White,
                                 Content = new ScrollViewer { Content = new Border { Padding = new Thickness(16), Child = section } }, Left = -3000, Top = 0, ShowActivated = false };
            TextElement_SetForeground(section);
            w.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                section.Measure(new Size(1150, double.PositiveInfinity));
                section.Arrange(new Rect(0, 0, 1150, section.DesiredSize.Height));
                var bmp = new RenderTargetBitmap(1150, (int)section.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1f, 0x21, 0x25)), null, new Rect(0, 0, 1150, section.DesiredSize.Height)); dc.DrawRectangle(new VisualBrush(section), null, new Rect(0, 0, 1150, section.DesiredSize.Height)); }
                bmp.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var f = File.Create(outFile)) enc.Save(f);
                Console.WriteLine("ui: " + outFile + " " + section.DesiredSize);
                w.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            };
            timer.Start();
            Dispatcher.Run();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start(); t.Join();
    }
    static void TextElement_SetForeground(FrameworkElement e) => System.Windows.Documents.TextElement.SetForeground(e, Brushes.White);
}
