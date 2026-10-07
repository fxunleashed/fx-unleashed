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

    /// <summary>How long each Unlocked tab takes to build and lay out (first open), offscreen; and one WheelView.</summary>
    public static void RunTiming(string only = null)
    {
        var t = new System.Threading.Thread(() =>
        {
            var w = new Window { Width = 1300, Height = 900, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            w.Show();
            var probe = new SettingsControl(Plugin(WheelMode.Unlocked));
            foreach (var tab in probe.TabNames.ToList())
            {
                if (only != null && tab.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int round = 0; round < 2; round++)
                {
                    var control = new SettingsControl(Plugin(WheelMode.Unlocked));
                    w.Content = control; w.UpdateLayout();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    control.OpenTab(tab);
                    long build = sw.ElapsedMilliseconds;
                    w.UpdateLayout();
                    long layout = sw.ElapsedMilliseconds - build;
                    int count = Count(control);
                    var types = new System.Collections.Generic.Dictionary<string, int>();
                    CountLogical(control, types);
                    Console.WriteLine($"tab {tab,-10} build {build,5} ms  layout {layout,5} ms  elements {count}  logical: " +
                        string.Join(", ", types.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value)));
                }
            }
            for (int round = 0; round < 3; round++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var panel = new WrapPanel();
                for (int i = 0; i < 10; i++) panel.Children.Add(new WheelView(WheelModel.FxPro, glow: false, lite: round > 0) { Width = 196 });
                long build = sw.ElapsedMilliseconds;
                w.Content = panel; w.UpdateLayout();
                Console.WriteLine((round > 0 ? "lite " : "full ") + $"10 small WheelViews: build {build} ms  layout {sw.ElapsedMilliseconds - build} ms  elements {Count(panel)}");
            }
            w.Close();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start(); t.Join();
    }

    private static void CountLogical(object o, System.Collections.Generic.Dictionary<string, int> types)
    {
        if (!(o is DependencyObject d)) return;
        string n = d.GetType().Name;
        types[n] = types.TryGetValue(n, out int c) ? c + 1 : 1;
        foreach (var child in LogicalTreeHelper.GetChildren(d)) CountLogical(child, types);
    }

    private static int Count(DependencyObject o)
    {
        int n = 1;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) n += Count(VisualTreeHelper.GetChild(o, i));
        return n;
    }

    private static FXProRpmSyncPlugin Plugin(WheelMode mode)
    {
        var plugin = new FXProRpmSyncPlugin { Settings = new FXProRpmSyncSettings() };
        plugin.Settings.Mode = mode;
        plugin.Settings.Usb.Enabled = mode == WheelMode.Unlocked;
        // UI_WHEEL=gtneo: the pages for the GT Neo
        var wheel = Environment.GetEnvironmentVariable("UI_WHEEL");
        if (!string.IsNullOrEmpty(wheel)) plugin.Settings.Usb.SwapWheel(WheelModel.Find(wheel));
        // UI_PRESET=id: that light preset selected
        var preset = Environment.GetEnvironmentVariable("UI_PRESET");
        if (!string.IsNullOrEmpty(preset)) plugin.Settings.Usb.LightPreset = preset;
        // UI_ANIM=1: animated GIF screensavers on the Idle tab (one that fits, one oversized, a sprite); UI_RAM=0: no picture memory;
        // UI_ANIM_DIR: where their files go
        if (Environment.GetEnvironmentVariable("UI_ANIM") == "1")
        {
            var u = plugin.Settings.Usb;
            u.ScreenRamDrive = Environment.GetEnvironmentVariable("UI_RAM") != "0";
            string dir = Environment.GetEnvironmentVariable("UI_ANIM_DIR") ?? Path.Combine(Path.GetTempPath(), "fx-ui-anim");
            Directory.CreateDirectory(dir);
            DashLibrary.Root = dir;
            var file = Path.Combine(dir, "partial.gif");
            File.WriteAllBytes(file, Convert.FromBase64String(TestGifs.Partial));
            var sprite = Path.Combine(dir, "sprite.gif");
            File.WriteAllBytes(sprite, Convert.FromBase64String(TestGifs.Sprite));
            var whole = IdleScreens.ImportAnimation(file); whole.Name = "Sliding square (fits)";
            var tight = IdleScreens.ImportAnimation(file, GifSaver.Load(whole).RamBytes * 6 / 10); tight.Name = "Sliding square (oversized)";
            var ball = IdleScreens.ImportAnimation(sprite); ball.Name = "Ball";
            u.Savers.Add(whole); u.Savers.Add(tight); u.Savers.Add(ball);
            u.SaverId = tight.Id;
        }
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
        // UI_RAM=1: the screen's RAM drive on, a rotation of two built-in dashes (the first set to skip the RAM) and a wheel dash
        if (Environment.GetEnvironmentVariable("UI_RAM") == "1")
        {
            var u = plugin.Settings.Usb;
            u.FirmwareConfirmed = true; u.ScreenRamDrive = true;
            DashLibrary.Root = Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub\";
            var all = BuiltInDashes.All().ToList();
            u.DefaultDashes = new System.Collections.Generic.List<string> { DashRef.Custom(all[0].Id), DashRef.Custom((DashCache.All().FirstOrDefault(d => !d.BuiltIn) ?? all[0]).Id), DashRef.Wheel("3") };
            u.SetDashUsesRam(all[0].Id, false);
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

    /// <summary>
    /// The FX Pro drawing (cutouts, rollers, funky switch) with presses fired on several controls, rendered while the
    /// animations run: wheel-fxpro.png (no presses) and wheel-fxpro-presses.png in `dir`.
    /// </summary>
    /// <summary>The firmware card's guided steps and the screen check's question, rendered offscreen (fwcard-*.png).</summary>
    public static void RunFirmwareCard(string dir)
    {
        var t = new System.Threading.Thread(() =>
        {
            var plugin = Plugin(WheelMode.Unlocked);
            var card = new FirmwareCard(plugin);
            var check = new ScreenCheckPanel(plugin);
            var col = new StackPanel { Width = 760 };
            col.Children.Add(card);
            col.Children.Add(new Border { Height = 20 });
            col.Children.Add(check);
            var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0D)), Padding = new Thickness(20), Child = col };
            Theme.Apply(host);
            var w = new Window { Width = 840, Height = 1400, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Content = new ScrollViewer { Content = host } };
            w.Show();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            void Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, flags).Invoke(o, a);
            Call(check, "Show", "What colour did the screen turn?", "Green: it has picture memory. Red: it doesn't. No change at all: the screen isn't taking commands.", true, Theme.Red);
            foreach (var (name, act) in new (string, Action)[]
            {
                ("watching", () => Call(card, "ShowWatching")),
                ("powercycle", () => Call(card, "ShowPowerCycle", (object)null)),
                ("done", () => { card.GetType().GetField("flowMode", flags).SetValue(card, "ramfs"); Call(card, "CheckAnswered", "on"); }),
                ("failed", () => Call(card, "Answer", "failed")),
                // the whole image (support only): its failure opens Screen recovery, where the whole-image section is
                ("full-failed", () => { card.GetType().GetField("flowMode", flags).SetValue(card, "full"); Call(card, "Answer", "failed"); }),
                ("full-done", () => { card.GetType().GetField("flowMode", flags).SetValue(card, "full"); Call(card, "CheckAnswered", "off"); }),
            })
            {
                act();
                host.UpdateLayout();
                SaveElement(host, Path.Combine(dir, "fwcard-" + name + ".png"));
            }
            w.Close();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start(); t.Join();
    }

    public static void RunWheel(string dir)
    {
        var t = new System.Threading.Thread(() =>
        {
            var s = new UsbSettings { DashSlot = 41, UpperPaddleA = 42, UpperPaddleB = 43 };
            var view = new WheelView(WheelModel.FxPro) { Width = 1326 };
            var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)), Padding = new Thickness(20), Child = view };
            var w = new Window { Width = 1400, Height = 900, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Content = host };
            w.Show();
            void Pump(int ms)
            {
                var frame = new DispatcherFrame();
                var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                tm.Tick += (a, b) => { tm.Stop(); frame.Continue = false; };
                tm.Start();
                Dispatcher.PushFrame(frame);
            }
            Pump(300);
            SaveElement(host, Path.Combine(dir, "wheel-fxpro.png"));
            // a button, a knob each way, both roller kinds, the funky switch, a paddle, the dash button
            view.ShowClutch(90, 40);
            foreach (int b in new[] { 7, 22, 9, 34, 37, 29, 15, 42, 13, 41 }) view.Press(b, s);
            Pump(450);
            SaveElement(host, Path.Combine(dir, "wheel-fxpro-presses.png"));
            w.Close();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
    }

    /// <summary>What DashRenderer.Check() says (the settings page's warnings) for the installed dashes matching `filter`, drawn with fills and from tiles.</summary>
    public static void RunChecks(string filter)
    {
        DashLibrary.Root = Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub\";
        foreach (var d in DashLibrary.Load(new System.Collections.Generic.List<string>()))
        {
            if (filter != null && (d.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (bool ram in new[] { false, true })
            {
                using (var screen = new PreviewScreen())
                {
                    var room = DashRenderer.Room(d);
                    var r = new DashRenderer(screen, d, Math.Min(10, room.Right), Math.Min(20, room.Down));
                    var issues = r.Check(); // before the tiles go on: what the settings page does (and the wheel)
                    if (ram) { r.EnableTiles(); r.UseTiles(true); }
                    r.DrawAll();
                    Console.WriteLine($"== {d.Name} ({(ram ? "tiles" : "fills")}): {issues.Count} issue(s)");
                    foreach (var i in issues.Take(14)) Console.WriteLine("   " + i);
                }
            }
        }
    }

    /// <summary>The plugin's own dash pictures (DashPictures.Still) with and without the RAM drive, for the installed dashes matching `filter`.</summary>
    public static void RunStills(string dir, string filter)
    {
        var t = new System.Threading.Thread(() =>
        {
            DashLibrary.Root = Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub\";
            foreach (var d in DashLibrary.Load(new System.Collections.Generic.List<string>()))
            {
                if (filter != null && (d.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (bool ram in new[] { false, true })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var img = DashPictures.Still(d, ram);
                    string file = Path.Combine(dir, "still_" + d.Id + (ram ? "_ram" : "_fills") + ".png");
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    using (var f = File.Create(file)) enc.Save(f);
                    Console.WriteLine($"{d.Name}: {(ram ? "RAM tiles" : "fills    ")} {sw.ElapsedMilliseconds} ms -> {Path.GetFileName(file)}");
                }
            }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
    }

    /// <summary>The header's RAM pill at a few fills (and loading), next to the USB pill, as a PNG.</summary>
    public static void RunRamPill(string dir)
    {
        var t = new System.Threading.Thread(() =>
        {
            var col = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0D)) };
            foreach (var (used, files, loading) in new[] { (24, 20, false), (120, 52, false), (232, 86, false), (288, 104, false), (334, 118, false), (346, 120, true) })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 14, 20, 0) };
                var pill = Theme.Pill(out var text, out var dot);
                text.Text = "USB  ·  Demo  ·  app 1.3.11"; dot.Fill = Theme.Green;
                row.Children.Add(pill);
                var ram = new RamPill { Visibility = Visibility.Visible };
                ram.Update(used * 1024, 350 * 1024, files, (used - files / 2) * 1024, 512, loading);
                row.Children.Add(ram);
                var car = Theme.Pill(out var ct, out var cd); ct.Text = "Ferrari 499P  ·  LMU"; cd.Fill = Theme.Green;
                row.Children.Add(car);
                col.Children.Add(row);
            }
            col.Children.Add(new Border { Height = 14 });
            var w = new Window { Width = 760, Height = 460, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Content = col };
            w.Show();
            var frame = new DispatcherFrame();
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            tm.Tick += (a, b) => { tm.Stop(); frame.Continue = false; };
            tm.Start();
            Dispatcher.PushFrame(frame);
            col.Measure(new Size(760, double.PositiveInfinity)); col.Arrange(new Rect(0, 0, 760, col.DesiredSize.Height)); col.UpdateLayout();
            SaveElement(col, Path.Combine(dir, "ram-pill.png"));
            w.Close();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
    }

    /// <summary>The "Add an animated GIF" window for a GIF, offscreen: gif-dialog.png in `dir`, once its best fit is worked out
    /// (UsbTest OUT gifdialog FILE.gif [noram]).</summary>
    public static void RunGifDialog(string dir, string gifPath, bool ramOn)
    {
        var t = new System.Threading.Thread(() =>
        {
            var info = AnimatedPicture.Inspect(gifPath);
            if (info == null) { Console.WriteLine("not an animated GIF: " + gifPath); return; }
            var d = new GifImportDialog(gifPath, info, ramOn);
            d.Window.Left = -3000; d.Window.Top = 0; d.Window.ShowActivated = false; d.Window.ShowInTaskbar = false;
            d.Window.Show();
            var frame = new DispatcherFrame();
            var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            tm.Tick += (a, b) => { tm.Stop(); frame.Continue = false; };
            tm.Start();
            Dispatcher.PushFrame(frame);
            // the window as it looks: its dark background and its whole height
            var content = (FrameworkElement)d.Window.Content;
            content.Measure(new Size(640, double.PositiveInfinity)); content.Arrange(new Rect(0, 0, 640, content.DesiredSize.Height)); content.UpdateLayout();
            var bmp = new RenderTargetBitmap(640, (int)content.DesiredSize.Height + 4, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0D)), null, new Rect(0, 0, 640, bmp.Height));
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, 640, content.DesiredSize.Height));
            }
            bmp.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(Path.Combine(dir, "gif-dialog.png"))) enc.Save(f);
            d.Window.Close();
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
    }

    private static void Pump(double seconds)
    {
        var frame = new DispatcherFrame();
        var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        tm.Tick += (a, b) => { tm.Stop(); frame.Continue = false; };
        tm.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Button FindButton(DependencyObject root, string text)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Button b && b.Content as string == text) return b;
            var found = FindButton(c, text);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// The import window's Add and Cancel, driven the way a click does (UI automation): open it for a GIF, wait for the best fit,
    /// press Add, and check a screensaver file came out. Returns 0 when it did (UsbTest OUT gifdialog-add FILE.gif).
    /// </summary>
    public static int RunGifDialogAdd(string dir, string gifPath)
    {
        int code = 1;
        var t = new System.Threading.Thread(() =>
        {
            DashLibrary.Root = Path.Combine(dir, "fakeSimHub"); // the saver's file goes under here, not into SimHub's folder
            var info = AnimatedPicture.Inspect(gifPath);
            var d = new GifImportDialog(gifPath, info, true);
            d.Window.Left = -3000; d.Window.Top = 0; d.Window.ShowActivated = false; d.Window.ShowInTaskbar = false;
            d.Window.Show();
            Pump(10);
            var add = FindButton(d.Window, "Add");
            Console.WriteLine("Add button found: " + (add != null) + ", enabled after the best fit: " + (add?.IsEnabled == true));
            if (add == null || !add.IsEnabled) return;
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(add)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            Pump(8);
            var r = d.Result;
            Console.WriteLine("result: " + (r == null ? "none" : $"{r.Name}, {r.Frames}/{r.FramesTotal} frames, oversized {r.IsOversized}, file {(File.Exists(r.File) ? new FileInfo(r.File).Length / 1024 + " KB" : "missing")}"));
            Console.WriteLine("window closed by Add: " + !d.Window.IsVisible);
            code = r != null && r.IsAnimation && File.Exists(r.File) && GifSaver.Load(r)?.Still != null ? 0 : 1;
            if (d.Window.IsVisible) d.Window.Close();

            // Cancel adds nothing
            var d2 = new GifImportDialog(gifPath, info, true);
            d2.Window.Left = -3000; d2.Window.ShowActivated = false; d2.Window.ShowInTaskbar = false;
            d2.Window.Show();
            Pump(3);
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(FindButton(d2.Window, "Cancel"))
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            Pump(1);
            Console.WriteLine("Cancel: closed " + !d2.Window.IsVisible + ", nothing added " + (d2.Result == null));
            if (d2.Window.IsVisible || d2.Result != null) code = 1;
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        return code;
    }

    private static void SaveElement(FrameworkElement e, string file)
    {
        e.UpdateLayout();
        int width = (int)e.ActualWidth, h = (int)e.ActualHeight;
        var bmp = new RenderTargetBitmap(width, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(file)) enc.Save(f);
        Console.WriteLine("wheel: " + file);
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
