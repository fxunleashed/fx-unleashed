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
    /// Unlocked mode, dashes. Each car has a list of dashes it switches between (a button bound to the UsbNextDash
    /// action steps through it); cars without one use the default list. A list can mix the plugin's dashes and the
    /// wheel's own. Below: the library (your dashes and every wheel dash) with a live preview, the saved cars, and where
    /// the dash sits on the screen.
    /// </summary>
    public class UnlockedDashesTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly ComboBox targetBox;
        private readonly StackPanel listPanel, carsList;
        private readonly WrapPanel customTiles, wheelTiles;
        private readonly Image previewImage = new Image { Stretch = Stretch.Uniform };
        private readonly Border demoChip;
        private readonly LiveDashPreview preview;
        private readonly TextBlock focusName, focusInfo, focusProblems, designerInfo;
        private readonly WrapPanel focusButtons;
        private readonly Dictionary<string, (Border Tile, WrapPanel Badges)> tiles = new Dictionary<string, (Border, WrapPanel)>();

        /// <summary>The list being edited: null = the default one, else a car key.</summary>
        private string target;
        private bool targetChosen;
        private string focus; // a DashRef
        private string shownState;
        private bool loading;

        private readonly DispatcherTimer dashTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        public UnlockedDashesTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            preview = new LiveDashPreview(previewImage, () => plugin.Usb);

            // ----- The list -----
            var list = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            targetBox = new ComboBox { Width = 340, VerticalAlignment = VerticalAlignment.Center };
            targetBox.SelectionChanged += (s, e) =>
            {
                if (loading || !(targetBox.SelectedItem is ComboBoxItem i)) return;
                target = (string)i.Tag; targetChosen = true;
                Refresh(true);
            };
            DockPanel.SetDock(targetBox, Dock.Right);
            head.Children.Add(targetBox);
            var headText = new StackPanel();
            headText.Children.Add(Theme.Eyebrow("Dashes"));
            headText.Children.Add(Theme.Title("What the screen shows", 20));
            head.Children.Add(headText);
            list.Children.Add(head);
            listPanel = new StackPanel();
            list.Children.Add(listPanel);
            list.Children.Add(new TextBlock
            {
                Text = "Switch between them while driving: bind the SimHub actions FXProRpmSync.UsbNextDash and UsbPreviousDash " +
                       "(SimHub > Controls and events) to any wheel button, button box or key.",
                Foreground = Theme.Text3, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            });
            Children.Add(Theme.CardBox(list));

            // ----- Library -----
            var lib = new StackPanel();
            var libHead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(Theme.Btn("Designer", () => OpenDesigner(null), icon: ""));
            tools.Children.Add(Theme.Btn("Folder", OpenDashFolder, icon: ""));
            tools.Children.Add(Theme.Btn("Reload", () => { DashCache.All(force: true); BuildLibrary(); Usb?.ReloadDashes(); }, icon: ""));
            foreach (Button b in tools.Children) b.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(tools, Dock.Right);
            libHead.Children.Add(tools);
            var libText = new StackPanel();
            libText.Children.Add(Theme.Eyebrow("Library"));
            libText.Children.Add(Theme.Title("Pick a dash", 20));
            libHead.Children.Add(libText);
            lib.Children.Add(libHead);

            var focusGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            focusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            focusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RenderOptions.SetBitmapScalingMode(previewImage, BitmapScalingMode.HighQuality);
            demoChip = new Border
            {
                Background = Theme.B("#CC000000"), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "DEMO LAP", FontFamily = Theme.Display, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.Text2 },
            };
            var screen = new Grid();
            screen.Children.Add(previewImage);
            screen.Children.Add(demoChip);
            focusGrid.Children.Add(new Border
            {
                Width = 480, Height = 288, Background = Brushes.Black, CornerRadius = new CornerRadius(8), BorderBrush = Theme.Line2,
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 20, 0), Child = screen, ClipToBounds = true,
            });
            var info = new StackPanel();
            focusName = Theme.Title("", 22);
            info.Children.Add(focusName);
            focusInfo = Theme.Note("", new Thickness(0, 6, 0, 12));
            info.Children.Add(focusInfo);
            focusButtons = new WrapPanel();
            info.Children.Add(focusButtons);
            focusProblems = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Amber, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            info.Children.Add(focusProblems);
            Grid.SetColumn(info, 1);
            focusGrid.Children.Add(info);
            lib.Children.Add(focusGrid);

            lib.Children.Add(Theme.Eyebrow("Your dashes"));
            customTiles = new WrapPanel();
            lib.Children.Add(customTiles);
            lib.Children.Add(new Border { Height = 8 });
            lib.Children.Add(Theme.Eyebrow("The wheel's own dashes"));
            wheelTiles = new WrapPanel();
            lib.Children.Add(new ScrollViewer { Content = wheelTiles, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 4) });
            designerInfo = Theme.Note("", new Thickness(0, 8, 0, 0));
            lib.Children.Add(designerInfo);
            Children.Add(Theme.CardBox(lib));

            // ----- Saved cars -----
            var cars = new StackPanel();
            cars.Children.Add(Theme.Eyebrow("Cars with their own dashes"));
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

            dashTimer.Tick += (s, e) => { if (!DashRef.IsWheel(focus)) { preview.Show(DashCache.Find(DashRef.Id(focus)), S.PadLeft, S.PadTop); preview.Tick(); } };
            slowTimer.Tick += (s, e) => Refresh(false);
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); plugin.SaveSettings(); };
            Loaded += (s, e) => { dashTimer.Start(); slowTimer.Start(); BuildLibrary(); Refresh(true); };
            Unloaded += (s, e) =>
            {
                dashTimer.Stop(); slowTimer.Stop(); preview.Dispose();
                if (saveTimer.IsEnabled) { saveTimer.Stop(); plugin.SaveSettings(); }
            };

            var now = plugin.UsbRotation(plugin.DashCarKey, out _, out int cur);
            focus = now[((cur % now.Count) + now.Count) % now.Count];
        }

        private void Changed()
        {
            Usb?.SettingsChanged();
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---------- The list being edited ----------

        private List<string> TargetList(out bool own, out int current) => plugin.UsbRotation(target, out own, out current);

        private string TargetName => target == null ? "the default" : target == plugin.DashCarKey ? (plugin.CurrentCarNameForDash ?? "this car") : (plugin.GetUsbCarDash(target)?.CarName ?? target);

        private void Save(List<string> refs, int current)
        {
            plugin.SetUsbRotation(target, refs, current);
            Refresh(true);
        }

        private void Refresh(bool force)
        {
            string car = plugin.DashCarKey;
            if (!targetChosen) target = car != null && plugin.GetUsbCarDash(car) != null ? car : null;
            var refs = TargetList(out bool own, out int current);
            string state = $"{car}|{target}|{own}|{current}|{string.Join(",", refs)}|{Usb?.DemoOn}|{Usb?.DemoDashId}|{plugin.AllUsbCarDashes().Count}";
            if (!force && state == shownState) return;
            shownState = state;
            designerInfo.Text = plugin.Designer?.Running == true ? "Designer running at " + plugin.Designer.Url + "  ·  API for agents at " + plugin.Designer.Url + "api" : "";

            // the choice of list
            loading = true;
            targetBox.Items.Clear();
            targetBox.Items.Add(new ComboBoxItem { Content = "Default (cars without their own)", Tag = null });
            if (car != null) targetBox.Items.Add(new ComboBoxItem { Content = "This car: " + plugin.CurrentCarNameForDash, Tag = car });
            foreach (var c in plugin.AllUsbCarDashes().Where(c => c.CarKey != car))
                targetBox.Items.Add(new ComboBoxItem { Content = c.CarName + "  (" + c.Game + ")", Tag = c.CarKey });
            targetBox.SelectedItem = targetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == target) ?? targetBox.Items[0];
            loading = false;

            listPanel.Children.Clear();
            if (target != null && !own)
            {
                listPanel.Children.Add(Theme.Note($"{TargetName} uses the default dashes ({string.Join(", ", refs.Select(DashRef.Name))})."));
                listPanel.Children.Add(Theme.Btn("Give it its own dashes", () => Save(refs, current), primary: true, icon: ""));
            }
            else
            {
                var strip = new WrapPanel();
                for (int i = 0; i < refs.Count; i++) strip.Children.Add(ListItem(refs, i, current));
                listPanel.Children.Add(strip);
                var row = new WrapPanel { Margin = new Thickness(0, 6, 0, -8) };
                if (refs.Count > 1)
                {
                    row.Children.Add(Theme.Btn("Previous", () => Save(refs, current - 1), icon: ""));
                    row.Children.Add(Theme.Btn("Next", () => Save(refs, current + 1), icon: ""));
                }
                if (target != null) row.Children.Add(Theme.Btn("Back to the default", () => { plugin.DeleteUsbCarDash(target); if (target != plugin.DashCarKey) target = null; Refresh(true); }, icon: ""));
                listPanel.Children.Add(row);
                if (refs.Count == 1) listPanel.Children.Add(new TextBlock { Text = "Add more from the library below to switch between them.", Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 0) });
            }
            RefreshBadges();
            ShowFocus();
            RefreshCars();
        }

        /// <summary>One dash of the list: its picture, number and name; click = show it now; arrows move it; × removes it.</summary>
        private FrameworkElement ListItem(List<string> refs, int i, int current)
        {
            string r = refs[i];
            bool now = ((current % refs.Count) + refs.Count) % refs.Count == i;
            var body = new StackPanel();
            var pic = new Grid();
            pic.Children.Add(new Border { Width = 176, Height = 106, Background = Brushes.Black, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Picture(r) });
            var badge = new WrapPanel { Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            badge.Children.Add(Badge((i + 1).ToString(), Theme.B("#3A3F4A")));
            if (now) badge.Children.Add(Badge("NOW", Theme.Red));
            pic.Children.Add(badge);
            body.Children.Add(pic);
            var name = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            if (i > 0) actions.Children.Add(Mini("", "Earlier", () => { var l = new List<string>(refs); l.RemoveAt(i); l.Insert(i - 1, r); Save(l, now ? i - 1 : current); }));
            if (i < refs.Count - 1) actions.Children.Add(Mini("", "Later", () => { var l = new List<string>(refs); l.RemoveAt(i); l.Insert(i + 1, r); Save(l, now ? i + 1 : current); }));
            if (refs.Count > 1 || target != null) actions.Children.Add(Mini("", "Remove", () => { var l = new List<string>(refs); l.RemoveAt(i); Save(l, Math.Min(current, Math.Max(0, l.Count - 1))); }));
            DockPanel.SetDock(actions, Dock.Right);
            name.Children.Add(actions);
            name.Children.Add(new TextBlock { Text = DashRef.Name(r), FontFamily = Theme.Display, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(name);
            var tile = Theme.Tile(body, 198, () => { focus = r; if (!now) Save(refs, i); else { RefreshBadges(); ShowFocus(); } }, now ? "Showing now" : "Show this one now");
            tile.Padding = new Thickness(10);
            Theme.Select(tile, now);
            return tile;
        }

        private static Button Mini(string glyph, string tip, Action click)
        {
            var b = Theme.Btn("", click);
            b.Content = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 10 };
            b.Width = 24; b.Height = 22; b.Padding = new Thickness(0); b.Margin = new Thickness(4, 0, 0, 0); b.ToolTip = tip;
            return b;
        }

        private static UIElement Picture(string r)
        {
            if (DashRef.IsWheel(r))
            {
                var src = DashRef.Id(r) == null ? null : DashSection.ThumbSource(DashRef.Id(r));
                if (src != null) return new Image { Source = src, Stretch = Stretch.Uniform };
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 26, Foreground = Theme.Text2, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = "Whatever the wheel shows", Foreground = Theme.Text3, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
                return sp;
            }
            var img = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var d = DashCache.Find(DashRef.Id(r));
            img.Dispatcher.BeginInvoke(new Action(() => img.Source = DashPictures.Still(d)), DispatcherPriority.Background);
            return img;
        }

        // ---------- Library ----------

        private void BuildLibrary()
        {
            customTiles.Children.Clear();
            wheelTiles.Children.Clear();
            tiles.Clear();
            foreach (var d in DashCache.All()) customTiles.Children.Add(LibraryTile(DashRef.Custom(d.Id), d.Name, d.Author, 226, 124));
            wheelTiles.Children.Add(LibraryTile(DashRef.Wheel(null), "Whatever the wheel shows", "The dash you pick on the wheel", 170, 102));
            foreach (var w in DashCatalog.All.Where(x => x.Id != DashCatalog.SettingsPageId))
                wheelTiles.Children.Add(LibraryTile(DashRef.Wheel(w.Id), w.Name, "Wheel dash " + w.Id, 170, 102));
            RefreshBadges();
        }

        private Border LibraryTile(string r, string name, string sub, double width, double height)
        {
            var body = new StackPanel();
            var frame = new Grid();
            frame.Children.Add(new Border { Height = height, Background = Brushes.Black, CornerRadius = new CornerRadius(6), Child = Picture(r), ClipToBounds = true });
            var badges = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
            frame.Children.Add(badges);
            body.Children.Add(frame);
            body.Children.Add(new TextBlock { Text = name, FontFamily = Theme.Display, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = sub ?? " ", Foreground = Theme.Text3, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = Theme.Tile(body, width, () => { focus = r; RefreshBadges(); ShowFocus(); });
            tile.Padding = new Thickness(8);
            tiles[r] = (tile, badges);
            return tile;
        }

        private static Border Badge(string text, Brush bg) => new Border
        {
            Background = bg, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 4),
            Child = new TextBlock { Text = text, FontFamily = Theme.Display, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
        };

        private void RefreshBadges()
        {
            var refs = TargetList(out _, out _);
            foreach (var kv in tiles)
            {
                kv.Value.Badges.Children.Clear();
                int at = refs.IndexOf(kv.Key);
                if (at >= 0) kv.Value.Badges.Children.Add(Badge("#" + (at + 1), Theme.Red));
                Theme.Select(kv.Value.Tile, kv.Key == focus);
            }
        }

        private void ShowFocus()
        {
            bool wheel = DashRef.IsWheel(focus);
            var d = wheel ? null : DashCache.Find(DashRef.Id(focus));
            focusName.Text = DashRef.Name(focus);
            focusInfo.Text = wheel
                ? (DashRef.Id(focus) == null
                    ? "The plugin gives the screen back and leaves the wheel on the dash you pick with its dash button."
                    : "One of the wheel's own dashes: the plugin gives the screen back and switches the wheel to it through SimPro.")
                : string.Join("  ·  ", new[] { d?.Author, d?.Description, d?.BuiltIn == true ? "built in" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var refs = TargetList(out bool own, out int current);
            bool inList = refs.Contains(focus);
            focusButtons.Children.Clear();
            bool editable = target == null || own;
            if (!inList)
                focusButtons.Children.Add(Theme.Btn("Add to " + TargetName, () =>
                {
                    var l = new List<string>(refs) { focus };
                    Save(l, l.Count - 1);
                }, primary: true, icon: ""));
            else if (editable && (refs.Count > 1 || target != null))
                focusButtons.Children.Add(Theme.Btn("Remove from " + TargetName, () =>
                {
                    var l = new List<string>(refs); int at = l.IndexOf(focus); l.RemoveAt(at);
                    Save(l, Math.Min(current, Math.Max(0, l.Count - 1)));
                }, icon: ""));
            if (!wheel && d != null)
            {
                bool demoing = Usb?.DemoOn == true && Usb.DemoDashId == d.Id;
                var demo = Theme.Btn(demoing ? "Stop the demo" : "Demo on the wheel", () =>
                {
                    Usb?.SetDemo(!demoing, d.Id);
                    ShowFocus();
                }, icon: demoing ? "" : "");
                demo.IsEnabled = Usb != null && S.FirmwareConfirmed;
                focusButtons.Children.Add(demo);
                focusButtons.Children.Add(Theme.Btn("Edit in the designer", () => OpenDesigner(d.Id), icon: ""));
                if (d.BuiltIn) focusButtons.Children.Add(Theme.Btn("Save a copy", () => SaveCopy(d), icon: ""));
            }
            demoChip.Visibility = wheel ? Visibility.Collapsed : Visibility.Visible;
            if (wheel)
            {
                preview.Show(null, 0, 0);
                previewImage.Source = DashRef.Id(focus) == null ? null : DashSection.ThumbSource(DashRef.Id(focus));
            }
            else preview.Show(d, S.PadLeft, S.PadTop);
            var problems = d == null ? new List<string>() : preview.Problems.Concat(DashCache.Errors).ToList();
            focusProblems.Text = problems.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", problems.Take(6));
        }

        // ---------- Cars ----------

        private void RefreshCars()
        {
            carsList.Children.Clear();
            var all = plugin.AllUsbCarDashes();
            if (all.Count == 0) { carsList.Children.Add(new TextBlock { Text = "None yet: every car uses the default dashes.", Foreground = Theme.Text3 }); return; }
            foreach (var c in all)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
                var key = c.CarKey;
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(Theme.Btn("Edit", () => { target = key; targetChosen = true; Refresh(true); }, icon: ""));
                buttons.Children.Add(Theme.Btn("Forget", () => { plugin.DeleteUsbCarDash(key); if (target == key) target = null; Refresh(true); }, icon: ""));
                foreach (Button b in buttons.Children) b.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = c.CarName, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = c.Game + "  ·  " + string.Join("  ·  ", c.Dashes.Select(DashRef.Name)), Foreground = Theme.Text3, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(text);
                carsList.Children.Add(new Border
                {
                    Background = key == target ? Theme.RedWash : Theme.Raised, BorderBrush = key == target ? Theme.Red : Brushes.Transparent, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = row,
                });
            }
        }

        // ---------- Files ----------

        private void OpenDesigner(string dashId)
        {
            var url = plugin.StartDesigner();
            if (url == null) { MessageBox.Show("The designer couldn't start: port " + S.DesignerPort + " is in use. See SimHub's log.", "FXPro Unlocked"); return; }
            try { Process.Start(url + (string.IsNullOrEmpty(dashId) ? "" : "?dash=" + Uri.EscapeDataString(dashId))); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro Unlocked"); }
        }

        private void OpenDashFolder()
        {
            try { System.IO.Directory.CreateDirectory(DashLibrary.Folder); Process.Start("explorer.exe", DashLibrary.Folder); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro Unlocked"); }
        }

        private void SaveCopy(DashDefinition d)
        {
            try
            {
                var path = DashLibrary.Export(d);
                DashCache.All(force: true);
                BuildLibrary();
                MessageBox.Show(Window.GetWindow(this), "Saved as\n" + path + "\n\nEdit it in the designer or as JSON, then press Reload.", "FXPro Unlocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FXPro Unlocked"); }
        }
    }
}
