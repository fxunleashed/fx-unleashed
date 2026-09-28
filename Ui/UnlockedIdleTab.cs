using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode, between sessions: the screensaver (the logo, the clock, your pictures or any dash; pick the one
    /// shown) and sleep (lights and screen off after a while without a game).
    /// </summary>
    public class UnlockedIdleTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly WrapPanel gallery;
        private readonly TextBlock sleepState;
        private readonly Button sleepButton;
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public UnlockedIdleTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;

            // ----- Screensaver -----
            var saver = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var on = Theme.Switch("Show a screensaver", S.ScreenSaver, v => { S.ScreenSaver = v; Changed(); gallery.Opacity = v ? 1 : 0.5; });
            on.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(on, Dock.Right);
            head.Children.Add(on);
            var headText = new StackPanel();
            headText.Children.Add(Theme.Eyebrow("Screensaver"));
            headText.Children.Add(Theme.Title("When you're not racing", 20));
            head.Children.Add(headText);
            saver.Children.Add(head);
            saver.Children.Add(Theme.Note("Shown on the wheel's screen between sessions; off gives the screen back to the wheel's own dash. " +
                                          "Click one to use it. Pictures are drawn with the screen's rectangles, so they're simplified to draw in within 15 seconds."));
            gallery = new WrapPanel { Opacity = S.ScreenSaver ? 1 : 0.5 };
            saver.Children.Add(gallery);
            var add = new WrapPanel { Margin = new Thickness(0, 4, 0, -8) };
            add.Children.Add(Theme.Btn("Add a picture…", AddPicture, icon: ""));
            var dashBox = new ComboBox { Width = 260, Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
            dashBox.Items.Add(new ComboBoxItem { Content = "Add a dash as a screensaver…", Tag = null, IsEnabled = false });
            foreach (var d in DashCache.All()) dashBox.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
            dashBox.SelectedIndex = 0;
            dashBox.SelectionChanged += (s, e) =>
            {
                if (!(dashBox.SelectedItem is ComboBoxItem i) || i.Tag == null) return;
                var id = (string)i.Tag;
                var item = S.Savers.FirstOrDefault(x => x.Kind == SaverKind.Dash && x.DashId == id);
                if (item == null) S.Savers.Add(item = new SaverItem { Id = "dash-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = DashCache.NameOf(id), Kind = SaverKind.Dash, DashId = id });
                S.SaverId = item.Id;
                dashBox.SelectedIndex = 0;
                Changed();
                BuildGallery();
            };
            add.Children.Add(dashBox);
            saver.Children.Add(add);
            Children.Add(Theme.CardBox(saver));

            // ----- Sleep -----
            var sleep = new StackPanel();
            var sleepHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var sleepOn = Theme.Switch("Sleep when idle", S.SleepEnabled, v => { S.SleepEnabled = v; Changed(); });
            sleepOn.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(sleepOn, Dock.Right);
            sleepHead.Children.Add(sleepOn);
            var sleepText = new StackPanel();
            sleepText.Children.Add(Theme.Eyebrow("Sleep"));
            sleepText.Children.Add(Theme.Title("Lights out", 20));
            sleepHead.Children.Add(sleepText);
            sleep.Children.Add(sleepHead);
            sleep.Children.Add(Theme.Note("After a while without a game, every light goes dark and the screen's backlight turns off. " +
                                          "Starting a game (or the demo, or changing a setting here) wakes the wheel."));
            sleep.Children.Add(Theme.Field("Sleep after", Theme.SliderField(1, 60, S.SleepMinutes, 1, v => $"{v:0} min", v => { S.SleepMinutes = (int)v; Changed(); }), 130));
            var row = new DockPanel();
            sleepButton = Theme.Btn("Sleep now", () => { if (Usb?.Sleeping == true) Usb.Wake(); else Usb?.SleepNow(); Refresh(); }, icon: "");
            DockPanel.SetDock(sleepButton, Dock.Left);
            row.Children.Add(sleepButton);
            sleepState = new TextBlock { Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 8), FontFamily = Theme.Display };
            row.Children.Add(sleepState);
            sleep.Children.Add(row);
            sleep.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 8, 0, 14) });
            sleep.Children.Add(Theme.Binding("Sleep now", "UsbSleepNow"));
            sleep.Children.Add(Theme.Binding("Wake", "UsbWake"));
            Children.Add(Theme.CardBox(sleep));

            slowTimer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { slowTimer.Start(); BuildGallery(); Refresh(); };
            Unloaded += (s, e) => slowTimer.Stop();
            BuildGallery();
            Refresh();
        }

        private void Changed()
        {
            Usb?.SettingsChanged();
            plugin.SaveSettings();
        }

        private void Refresh()
        {
            var u = Usb;
            sleepButton.Content = u?.Sleeping == true ? "Wake" : "Sleep now";
            sleepButton.IsEnabled = u?.Active == true || u?.Sleeping == true || S.FirmwareConfirmed;
            if (u?.Sleeping == true) sleepState.Text = "Asleep";
            else if (!S.SleepEnabled) sleepState.Text = "";
            else if (u?.SleepIn is double left) sleepState.Text = $"Sleeping in {(int)(left / 60)}:{(int)(left % 60):00}";
            else sleepState.Text = u?.State == "Active" || u?.State == "Demo" ? "Awake: a game is running" : "";
        }

        private void BuildGallery()
        {
            gallery.Children.Clear();
            foreach (var item in IdleScreens.All(S))
            {
                var img = new Image { Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var frame = new Grid();
                frame.Children.Add(new Border { Height = 124, Background = Brushes.Black, CornerRadius = new CornerRadius(6), Child = img, ClipToBounds = true });
                if (item.Kind == SaverKind.Image || item.Kind == SaverKind.Dash)
                {
                    var captured = item;
                    var remove = new Button
                    {
                        Content = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 10 }, Width = 24, Height = 24, Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), ToolTip = "Remove",
                    };
                    remove.Click += (s, e) =>
                    {
                        S.Savers.RemoveAll(x => x.Id == captured.Id);
                        IdleScreens.Delete(captured);
                        if (S.SaverId == captured.Id) S.SaverId = SaverItem.LogoId;
                        Changed();
                        BuildGallery();
                    };
                    frame.Children.Add(remove);
                }
                var body = new StackPanel();
                body.Children.Add(frame);
                body.Children.Add(new TextBlock { Text = item.Name, FontFamily = Theme.Display, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                body.Children.Add(new TextBlock { Text = KindText(item), Foreground = Theme.Text3, FontSize = 11 });
                var id = item.Id;
                var tile = Theme.Tile(body, 226, () => { S.SaverId = id; Changed(); BuildGallery(); });
                Theme.Select(tile, item.Id == IdleScreens.Find(S, S.SaverId).Id);
                gallery.Children.Add(tile);
                var it = item;
                Dispatcher.BeginInvoke(new Action(() => img.Source = DashPictures.Saver(it)), DispatcherPriority.Background);
            }
        }

        private static string KindText(SaverItem item)
        {
            if (item.Blurb != null) return "Built in · " + item.Blurb.ToLowerInvariant();
            switch (item.Kind)
            {
                case SaverKind.Image: return "Picture";
                default: return "Dash";
            }
        }

        private void AddPicture()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "A picture for the wheel's screen" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                var item = IdleScreens.ImportImage(dlg.FileName);
                S.Savers.Add(item);
                S.SaverId = item.Id;
                Changed();
                BuildGallery();
            }
            catch (Exception ex) { MessageBox.Show("Couldn't use that picture: " + ex.Message, "FXPro Unlocked"); }
            finally { Cursor = null; }
        }
    }
}
