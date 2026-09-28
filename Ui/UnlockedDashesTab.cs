using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode, dashes: the current car's dash (one of yours, or the wheel's own), the library with a live preview,
    /// the default for cars without a choice, the saved cars, and where the dash sits on the screen.
    /// </summary>
    public class UnlockedDashesTab : StackPanel
    {
        /// <summary>The library tile standing for "the wheel's own dashes".</summary>
        private const string WheelTileId = "\u0001wheel";

        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly StackPanel carCard, carsList;
        private readonly WrapPanel library;
        private readonly Image previewImage = new Image { Stretch = Stretch.Uniform };
        private readonly LiveDashPreview preview;
        private readonly TextBlock focusName, focusInfo, focusProblems, designerInfo;
        private readonly WrapPanel focusBadges, focusButtons;
        private readonly Dictionary<string, (Border Tile, WrapPanel Badges)> tiles = new Dictionary<string, (Border, WrapPanel)>();
        private string focusId;
        private string shownCar, shownState;

        private readonly DispatcherTimer dashTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        public UnlockedDashesTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            preview = new LiveDashPreview(previewImage, () => plugin.Usb);

            // ----- This car -----
            carCard = new StackPanel();
            Children.Add(Theme.CardBox(carCard));

            // ----- Preview + library -----
            var lib = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(Theme.Btn("Designer", OpenDesigner, icon: ""));
            tools.Children.Add(Theme.Btn("Folder", OpenDashFolder, icon: ""));
            tools.Children.Add(Theme.Btn("Reload", () => { DashCache.All(force: true); BuildLibrary(); Usb?.ReloadDashes(); }, icon: ""));
            foreach (Button b in tools.Children) b.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);
            var headText = new StackPanel();
            headText.Children.Add(Theme.Eyebrow("Library"));
            headText.Children.Add(Theme.Title("Your dashes", 20));
            head.Children.Add(headText);
            lib.Children.Add(head);

            var focus = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            focus.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            focus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RenderOptions.SetBitmapScalingMode(previewImage, BitmapScalingMode.HighQuality);
            focus.Children.Add(new Border
            {
                Width = 480, Height = 288, Background = Brushes.Black, CornerRadius = new CornerRadius(8), BorderBrush = Theme.Line2,
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 20, 0), Child = previewImage, ClipToBounds = true,
            });
            var info = new StackPanel();
            focusBadges = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            info.Children.Add(focusBadges);
            focusName = Theme.Title("", 22);
            info.Children.Add(focusName);
            focusInfo = Theme.Note("", new Thickness(0, 6, 0, 12));
            info.Children.Add(focusInfo);
            focusButtons = new WrapPanel();
            info.Children.Add(focusButtons);
            focusProblems = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Amber, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            info.Children.Add(focusProblems);
            Grid.SetColumn(info, 1);
            focus.Children.Add(info);
            lib.Children.Add(focus);

            library = new WrapPanel();
            lib.Children.Add(library);
            designerInfo = Theme.Note("", new Thickness(0, 4, 0, 0));
            lib.Children.Add(designerInfo);
            Children.Add(Theme.CardBox(lib));

            // ----- Saved cars -----
            var cars = new StackPanel();
            cars.Children.Add(Theme.Eyebrow("Cars"));
            cars.Children.Add(Theme.Note("Cars you've picked a dash for. Every other car gets the default."));
            carsList = new StackPanel();
            cars.Children.Add(carsList);
            Children.Add(Theme.CardBox(cars));

            // ----- Position -----
            var pos = new StackPanel();
            pos.Children.Add(Theme.Eyebrow("Position on the screen"));
            pos.Children.Add(Theme.Note("The wheel's bezel covers the screen's edges: move the dash in from the left and the top."));
            pos.Children.Add(Theme.Field("From the left", Theme.SliderField(0, 20, S.PadLeft, 1, v => $"{v:0} px", v => { S.PadLeft = (int)v; Changed(); })));
            pos.Children.Add(Theme.Field("From the top", Theme.SliderField(0, 38, S.PadTop, 1, v => $"{v:0} px", v => { S.PadTop = (int)v; Changed(); })));
            Children.Add(Theme.CardBox(pos));

            dashTimer.Tick += (s, e) => { preview.Show(DashCache.Find(focusId), S.PadLeft, S.PadTop); preview.Tick(); };
            slowTimer.Tick += (s, e) => RefreshCar();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); plugin.SaveSettings(); };
            Loaded += (s, e) => { dashTimer.Start(); slowTimer.Start(); BuildLibrary(); RefreshCar(true); RefreshCars(); };
            Unloaded += (s, e) =>
            {
                dashTimer.Stop(); slowTimer.Stop(); preview.Dispose();
                if (saveTimer.IsEnabled) { saveTimer.Stop(); plugin.SaveSettings(); }
            };

            var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
            focusId = wheelDash ? WheelTileId : id;
            BuildLibrary();
            RefreshCar(true);
            RefreshCars();
        }

        private void Changed()
        {
            Usb?.SettingsChanged();
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---------- Library ----------

        private void BuildLibrary()
        {
            library.Children.Clear();
            tiles.Clear();
            var all = DashCache.All();
            if (focusId == null || (focusId != WheelTileId && all.All(d => d.Id != focusId))) focusId = all.FirstOrDefault()?.Id;
            library.Children.Add(MakeTile(WheelTileId, "Wheel's own dashes", null));
            foreach (var d in all) MakeTileFor(d);
            RefreshBadges();
            ShowFocus();
        }

        private void MakeTileFor(DashDefinition d)
        {
            var tile = MakeTile(d.Id, d.Name, d.Author);
            library.Children.Add(tile);
            var img = (Image)((Border)((Grid)((StackPanel)tile.Child).Children[0]).Children[0]).Child;
            // drawn after the page is up, one at a time
            Dispatcher.BeginInvoke(new Action(() => img.Source = DashPictures.Still(d)), DispatcherPriority.Background);
        }

        private Border MakeTile(string id, string name, string author)
        {
            var body = new StackPanel();
            var frame = new Grid();
            var img = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var screen = new Border { Height = 124, Background = Brushes.Black, CornerRadius = new CornerRadius(6), Child = img, ClipToBounds = true };
            if (id == WheelTileId)
            {
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 28, Foreground = Theme.Text2, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = "SimPro's dashes", Foreground = Theme.Text3, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
                screen.Child = sp;
                screen.Background = Theme.Raised;
            }
            frame.Children.Add(screen);
            var badges = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
            frame.Children.Add(badges);
            body.Children.Add(frame);
            body.Children.Add(new TextBlock { Text = name, FontFamily = Theme.Display, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = author ?? (id == WheelTileId ? "Switched per car like standard mode" : " "), Foreground = Theme.Text3, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = Theme.Tile(body, 226, () => { focusId = id; RefreshBadges(); ShowFocus(); });
            tiles[id] = (tile, badges);
            return tile;
        }

        private static Border Badge(string text, Brush bg) => new Border
        {
            Background = bg, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 4),
            Child = new TextBlock { Text = text, FontFamily = Theme.Display, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
        };

        private string DefaultTileId => S.DashEnabled ? S.DashId : WheelTileId;

        private string CarTileId
        {
            get
            {
                var d = plugin.GetUsbCarDash(plugin.DashCarKey);
                return d == null ? null : d.WheelDash ? WheelTileId : d.DashId;
            }
        }

        private void RefreshBadges()
        {
            string def = DefaultTileId, car = CarTileId;
            foreach (var kv in tiles)
            {
                kv.Value.Badges.Children.Clear();
                if (kv.Key == def) kv.Value.Badges.Children.Add(Badge("DEFAULT", Theme.B("#3A3F4A")));
                if (kv.Key == car) kv.Value.Badges.Children.Add(Badge("THIS CAR", Theme.Red));
                Theme.Select(kv.Value.Tile, kv.Key == focusId);
            }
        }

        private void ShowFocus()
        {
            bool wheel = focusId == WheelTileId;
            var d = wheel ? null : DashCache.Find(focusId);
            focusBadges.Children.Clear();
            if (focusId == DefaultTileId) focusBadges.Children.Add(Badge("DEFAULT", Theme.B("#3A3F4A")));
            if (focusId == CarTileId) focusBadges.Children.Add(Badge("THIS CAR", Theme.Red));
            focusName.Text = wheel ? "The wheel's own dashes" : d?.Name ?? "";
            focusInfo.Text = wheel
                ? "The plugin gives the screen back and the wheel shows its own dash, switched per car through SimPro (pick it below, or with the wheel's dash button while driving)."
                : string.Join("  ·  ", new[] { d?.Author, d?.Description, d?.BuiltIn == true ? "built in" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
            focusButtons.Children.Clear();
            if (focusId != DefaultTileId)
                focusButtons.Children.Add(Theme.Btn("Make it the default", () =>
                {
                    if (wheel) S.DashEnabled = false; else { S.DashEnabled = true; S.DashId = focusId; }
                    Changed(); RefreshBadges(); ShowFocus(); RefreshCar(true);
                }, primary: true, icon: ""));
            if (plugin.DashCarKey != null && focusId != CarTileId)
                focusButtons.Children.Add(Theme.Btn("Use for " + (plugin.CurrentCarNameForDash ?? "this car"), () =>
                {
                    plugin.PickUsbDashForCurrentCar(wheel, wheel ? null : focusId);
                    RefreshBadges(); ShowFocus(); RefreshCar(true); RefreshCars();
                }, primary: focusId == DefaultTileId, icon: ""));
            if (!wheel && d != null)
            {
                focusButtons.Children.Add(Theme.Btn("Edit in the designer", () => OpenDesigner(d.Id), icon: ""));
                if (d.BuiltIn) focusButtons.Children.Add(Theme.Btn("Save a copy", () => SaveCopy(d), icon: ""));
            }
            preview.Show(d, S.PadLeft, S.PadTop);
            if (wheel) previewImage.Source = DashSection.ThumbSource(plugin.GetCarDash(plugin.DashCarKey)?.DashId ?? plugin.WheelDash);
            var problems = d == null ? new List<string>() : preview.Problems.Concat(DashCache.Errors).ToList();
            focusProblems.Text = problems.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", problems.Take(6));
        }

        // ---------- This car ----------

        private void RefreshCar(bool force = false)
        {
            string key = plugin.DashCarKey;
            var mine = plugin.GetUsbCarDash(key);
            string state = $"{key}|{mine?.WheelDash}|{mine?.DashId}|{S.DashEnabled}|{S.DashId}|{plugin.GetCarDash(key)?.DashId}|{plugin.WheelDash}";
            if (!force && state == shownState) return;
            bool carChanged = key != shownCar;
            shownState = state;
            shownCar = key;
            designerInfo.Text = plugin.Designer?.Running == true ? "Designer running at " + plugin.Designer.Url + "  ·  API for agents at " + plugin.Designer.Url + "api" : "";

            carCard.Children.Clear();
            carCard.Children.Add(Theme.Eyebrow("This car"));
            if (key == null)
            {
                carCard.Children.Add(Theme.Title("No car yet", 20));
                carCard.Children.Add(Theme.Note("Get in a car in any game to give it its own dash. Cars without one show the default: " +
                                                (S.DashEnabled ? DashCache.NameOf(S.DashId) : "the wheel's own dash") + ".", new Thickness(0, 6, 0, 0)));
                return;
            }
            carCard.Children.Add(Theme.Title($"{plugin.CurrentCarNameForDash}", 20));
            carCard.Children.Add(new TextBlock { Text = plugin.CurrentGameForDash, Foreground = Theme.Text3, Margin = new Thickness(0, 2, 0, 14) });

            var (wheelDash, id) = plugin.UsbDashFor(key);
            var seg = Theme.Segmented(new[] { "One of your dashes", "The wheel's own dash" }, wheelDash ? 1 : 0, i =>
            {
                plugin.PickUsbDashForCurrentCar(i == 1, i == 1 ? null : (plugin.UsbDashFor(key).DashId ?? S.DashId));
                RefreshCar(true); RefreshBadges(); ShowFocus(); RefreshCars();
            }, out _);
            seg.Margin = new Thickness(0, 0, 0, 14);
            carCard.Children.Add(seg);

            if (!wheelDash)
            {
                var box = new ComboBox { Width = 320 };
                foreach (var d in DashCache.All()) box.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
                box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (id ?? S.DashId));
                box.SelectionChanged += (s, e) =>
                {
                    if (box.SelectedItem is ComboBoxItem i && (string)i.Tag != plugin.UsbDashFor(key).DashId)
                    {
                        plugin.PickUsbDashForCurrentCar(false, (string)i.Tag);
                        focusId = (string)i.Tag;
                        RefreshBadges(); ShowFocus(); RefreshCars();
                    }
                };
                carCard.Children.Add(Theme.Field("Dash", box, 120));
            }
            else
            {
                var box = new ComboBox { Width = 320 };
                box.Items.Add(new ComboBoxItem { Content = "Whatever the wheel shows (learned from the dash button)", Tag = null });
                foreach (var d in DashCatalog.All.Where(x => x.Id != DashCatalog.SettingsPageId)) box.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
                var saved = plugin.GetCarDash(key)?.DashId;
                box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == saved) ?? box.Items[0];
                box.SelectionChanged += (s, e) =>
                {
                    if (box.SelectedItem is ComboBoxItem i && (string)i.Tag != null) plugin.PickUsbDashForCurrentCar(true, (string)i.Tag);
                };
                carCard.Children.Add(Theme.Field("Wheel dash", box, 120));
            }
            var row = new WrapPanel { Margin = new Thickness(120, 0, 0, -8) };
            if (mine != null) row.Children.Add(Theme.Btn("Back to the default", () => { plugin.DeleteUsbCarDash(key); RefreshCar(true); RefreshBadges(); ShowFocus(); RefreshCars(); }, icon: ""));
            carCard.Children.Add(row);
            if (carChanged) { RefreshBadges(); ShowFocus(); }
        }

        private void RefreshCars()
        {
            carsList.Children.Clear();
            var all = plugin.AllUsbCarDashes();
            if (all.Count == 0) { carsList.Children.Add(new TextBlock { Text = "None yet.", Foreground = Theme.Text3 }); return; }
            foreach (var c in all)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var key = c.CarKey;
                var forget = Theme.Btn("Forget", () => { plugin.DeleteUsbCarDash(key); RefreshCars(); RefreshCar(true); RefreshBadges(); }, icon: "");
                forget.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(forget, Dock.Right);
                row.Children.Add(forget);
                var icon = Theme.Icon(c.WheelDash ? "" : "", 16, c.WheelDash ? Theme.Text3 : Theme.Red);
                icon.Margin = new Thickness(0, 0, 14, 0);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = c.CarName, FontWeight = FontWeights.SemiBold });
                string what = c.WheelDash ? "Wheel's own dash" + (plugin.GetCarDash(key)?.DashId is string w ? ": " + DashCatalog.NameOf(w) : "") : DashCache.NameOf(c.DashId);
                text.Children.Add(new TextBlock { Text = $"{c.Game}  ·  {what}", Foreground = Theme.Text3, FontSize = 11.5 });
                row.Children.Add(text);
                carsList.Children.Add(new Border { Background = Theme.Raised, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
        }

        // ---------- Files ----------

        private void OpenDesigner() => OpenDesigner(focusId == WheelTileId ? null : focusId);

        private void OpenDesigner(string dashId)
        {
            var url = plugin.StartDesigner();
            if (url == null) { MessageBox.Show("The designer couldn't start: port " + S.DesignerPort + " is in use. See SimHub's log.", "FXPro RPM Sync"); return; }
            try { Process.Start(url + (string.IsNullOrEmpty(dashId) ? "" : "?dash=" + Uri.EscapeDataString(dashId))); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }

        private void OpenDashFolder()
        {
            try { System.IO.Directory.CreateDirectory(DashLibrary.Folder); Process.Start("explorer.exe", DashLibrary.Folder); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }

        private void SaveCopy(DashDefinition d)
        {
            try
            {
                var path = DashLibrary.Export(d);
                DashCache.All(force: true);
                BuildLibrary();
                MessageBox.Show(Window.GetWindow(this), "Saved as\n" + path + "\n\nEdit it in the designer or as JSON, then press Reload.", "FXPro RPM Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro RPM Sync"); }
        }
    }
}
