using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using User.FXProRpmSync;

static class UiTest
{
    /// <summary>
    /// The settings page in both modes, every tab, rendered offscreen: ui-standard-*.png and ui-unlocked-*.png in `dir`.
    /// `only` limits it to the tabs whose name contains it.
    /// </summary>
    public static void RunFull(string dir, string only = null)
    {
        var t = new System.Threading.Thread(() =>
        {
            var jobs = new System.Collections.Generic.Queue<(WheelMode Mode, string Tab)>();
            foreach (var mode in new[] { WheelMode.Standard, WheelMode.Unlocked })
            {
                var probe = new SettingsControl(Plugin(mode));
                foreach (var tab in probe.TabNames)
                    if (only == null || tab.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0 || mode.ToString().Equals(only, StringComparison.OrdinalIgnoreCase))
                        jobs.Enqueue((mode, tab));
            }
            var w = new Window { Width = 1300, Height = 900, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            w.Show();
            SettingsControl control = null;
            WheelMode? shownMode = null;
            (WheelMode Mode, string Tab) job = default;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.2) };
            bool waiting = false;
            timer.Tick += (s, e) =>
            {
                if (waiting)
                {
                    waiting = false;
                    Save(control, Path.Combine(dir, $"ui-{job.Mode.ToString().ToLowerInvariant()}-{job.Tab.ToLowerInvariant().Replace(" ", "").Replace("&", "-")}.png"));
                }
                if (jobs.Count == 0) { timer.Stop(); w.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); return; }
                job = jobs.Dequeue();
                if (shownMode != job.Mode) { control = new SettingsControl(Plugin(job.Mode)); w.Content = control; shownMode = job.Mode; }
                control.OpenTab(job.Tab);
                waiting = true;
                timer.Interval = TimeSpan.FromSeconds(2.5); // animations and gallery pictures
            };
            timer.Start();
            Dispatcher.Run();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start(); t.Join();
    }

    private static FXProRpmSyncPlugin Plugin(WheelMode mode)
    {
        var plugin = new FXProRpmSyncPlugin { Settings = new FXProRpmSyncSettings() };
        plugin.Settings.Mode = mode;
        plugin.Settings.Usb.Enabled = mode == WheelMode.Unlocked;
        // UI_WHEEL=gtneo: the pages for the GT Neo
        var wheel = Environment.GetEnvironmentVariable("UI_WHEEL");
        if (!string.IsNullOrEmpty(wheel)) plugin.Settings.Usb.SwapWheel(WheelModel.Find(wheel));
        if (Environment.GetEnvironmentVariable("UI_CUSTOM") == "1")
        {
            var u = plugin.Settings.Usb;
            u.FirmwareConfirmed = true;
            var mine = LightPresets.Find("synthwave").Clone(); mine.Id = "user-test"; mine.Name = "Night stint";
            u.UserLights.Add(mine);
            var two = LightPresets.Find("ember").Clone(); two.Id = "user-two"; two.Name = "Ember copy";
            u.UserLights.Add(two);
            u.LightPreset = mine.Id;
            u.DefaultDashes = new System.Collections.Generic.List<string> { DashRef.Custom(BuiltInDashes.MustangId), DashRef.Wheel("3"), DashRef.Wheel("10") };
            u.SleepEnabled = true;
            u.SaverSwitchMinutes = 5; u.SaverRotation.Add("lights-out"); u.SaverRotation.Add("pit-board"); u.SaverId = "clock";
            mine.Rev.Pattern = PatternKind.EdgesToCenter;
            mine.Groups[LedGroup.Encoders] = new GroupLighting { Effect = LightEffect.Levels, Colors = new System.Collections.Generic.List<string> { "#00FF40", "#FFB000", "#FF0020" } };
            mine.Alerts.Insert(2, new AlertRule { Trigger = AlertTrigger.Custom, Name = "Damage", Condition = "[CarDamagesMax] > 5", Color = "#FF00C0", Groups = { LedGroup.Buttons } });
            u.PressLights = true; u.ButtonLeds[1] = 0; u.ButtonLeds[2] = 6;
        }
        if (Environment.GetEnvironmentVariable("UI_FEEDPROBLEM") == "1")
            for (int i = 0; i < 2; i++) plugin.FeedWatchState.Update(true, true, true, true, true, "Le Mans Ultimate");
        return plugin;
    }

    private static void ExpandAll(DependencyObject o)
    {
        if (o is System.Windows.Controls.Expander e) e.IsExpanded = true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) ExpandAll(VisualTreeHelper.GetChild(o, i));
    }

    private static void Save(SettingsControl control, string file)
    {
        var page = (FrameworkElement)((ScrollViewer)control.Content).Content;
        const double width = 1240;
        if (Environment.GetEnvironmentVariable("UI_EXPAND") == "1") // open every expander (to check what's inside)
        {
            page.Measure(new Size(width, double.PositiveInfinity)); page.Arrange(new Rect(0, 0, width, page.DesiredSize.Height)); page.UpdateLayout();
            for (int pass = 0; pass < 4; pass++)
            {
                ExpandAll(page);
                page.Measure(new Size(width, double.PositiveInfinity)); page.Arrange(new Rect(0, 0, width, page.DesiredSize.Height)); page.UpdateLayout();
            }
        }
        page.Measure(new Size(width, double.PositiveInfinity));
        page.Arrange(new Rect(0, 0, width, page.DesiredSize.Height));
        page.UpdateLayout();
        double h = page.DesiredSize.Height;
        var bmp = new RenderTargetBitmap((int)width, (int)h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0D)), null, new Rect(0, 0, width, h));
            dc.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, h));
        }
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(file)) enc.Save(f);
        Console.WriteLine("ui: " + file + " " + (int)h + " px");
    }
}
