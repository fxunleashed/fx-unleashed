using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The online library in the settings page (NEXT.md C): previews, search, filter by game, sort, install / update /
    /// remove without a restart, and "Use for this car" for dashes. `kind` = "dash" (Dashes tab) or "saver" (Idle tab).
    /// Network and disk work runs off the UI thread.
    /// </summary>
    internal sealed class LibraryPanel : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private readonly string kind;
        private readonly Action changed;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly WrapPanel grid = new WrapPanel();
        private readonly TextBlock status = new TextBlock { Foreground = Theme.Text3, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
        private readonly TextBox search = new TextBox { Width = 220 };
        private readonly ComboBox game = new ComboBox { Width = 190 }, sort = new ComboBox { Width = 150 };
        private LibraryIndex index;
        private bool loaded;
        /// <summary>The ids of the dashes already in the folder, read once per listing (dashes that didn't come through the library still count as installed).</summary>
        private List<string> localIds;

        public LibraryPanel(FXProRpmSyncPlugin plugin, string kind, Action changed)
        {
            this.plugin = plugin; this.kind = kind; this.changed = changed;
            var bar = new WrapPanel();
            bar.Children.Add(new TextBlock { Text = "Search", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            bar.Children.Add(search);
            bar.Children.Add(new TextBlock { Text = "Game", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            bar.Children.Add(game);
            sort.Items.Add("Newest first"); sort.Items.Add("By name"); sort.SelectedIndex = 0;
            bar.Children.Add(new TextBlock { Text = "Sort", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            bar.Children.Add(sort);
            var refresh = Theme.Btn("Refresh", () => Load(), icon: "");
            refresh.Margin = new Thickness(16, 0, 0, 0);
            bar.Children.Add(refresh);
            Children.Add(bar);
            Children.Add(status);
            Children.Add(grid);
            search.TextChanged += (s, e) => Show();
            game.SelectionChanged += (s, e) => Show();
            sort.SelectionChanged += (s, e) => Show();
            IsVisibleChanged += (s, e) => { if (IsVisible && !loaded) Load(); };
        }

        private void Load()
        {
            loaded = true;
            status.Text = "Loading the library...";
            var client = new LibraryClient(S.LibraryUrl);
            Task.Run(() => client.GetIndex()).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (t.IsFaulted) { status.Text = "The library isn't reachable: " + t.Exception.GetBaseException().Message; return; }
                index = t.Result;
                var games = index.Items.Where(i => i.Kind == kind).SelectMany(i => i.Games ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToList();
                game.Items.Clear();
                game.Items.Add("Any game");
                foreach (var g in games) game.Items.Add(g);
                game.SelectedIndex = 0;
                Show();
            })));
        }

        private void Show()
        {
            if (index == null) return;
            grid.Children.Clear();
            string q = search.Text.Trim();
            string g = game.SelectedIndex > 0 ? (string)game.SelectedItem : null;
            var items = index.Items.Where(i => i.Kind == kind)
                .Where(i => q.Length == 0 || new[] { i.Name, i.Author, i.Description }.Concat(i.Tags ?? new List<string>()).Concat(i.Cars ?? new List<string>())
                                                   .Any(x => x != null && x.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(i => g == null || (i.Games ?? new List<string>()).Contains(g, StringComparer.OrdinalIgnoreCase));
            items = sort.SelectedIndex == 1 ? items.OrderBy(i => i.Name) : items.OrderByDescending(i => i.Updated).ThenBy(i => i.Name);
            var list = items.ToList();
            localIds = kind == "saver" ? null : DashLibrary.LocalIds();
            int installed = list.Count(i => LibraryInstaller.Installed(S, i, localIds) != null);
            status.Text = list.Count == 0 ? "Nothing matches." :
                $"{list.Count} {(kind == "saver" ? "screensaver" : "dash")}{(list.Count == 1 ? "" : kind == "saver" ? "s" : "es")}" + (installed > 0 ? $", {installed} installed" : "") +
                ". Made by the community: installing one needs no restart.";
            foreach (var i in list) grid.Children.Add(Tile(i));
        }

        private Border Tile(LibraryItem item)
        {
            var body = new StackPanel();
            var img = new Image { Stretch = Stretch.UniformToFill };
            body.Children.Add(new Border { Width = 240, Height = 144, Background = Brushes.Black, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = img });
            var client = new LibraryClient(S.LibraryUrl);
            Task.Run(() => client.GetPreview(item)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (t.IsFaulted) return;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(t.Result); bmp.EndInit(); bmp.Freeze();
                    img.Source = bmp;
                }
                catch { }
            })));
            body.Children.Add(new TextBlock { Text = item.Name, FontFamily = Theme.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock
            {
                Text = "by " + (item.Author ?? "?") + (item.Games?.Count > 0 ? "  ·  " + string.Join(", ", item.Games) : "") + (item.Version != null ? "  ·  v" + item.Version : ""),
                Foreground = Theme.Text3, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrWhiteSpace(item.Description))
                body.Children.Add(new TextBlock { Text = item.Description, Foreground = Theme.Text2, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.BytesPerSecond > 0)
                body.Children.Add(new TextBlock { Text = $"{item.BytesPerSecond / 1000.0:0.0} KB/s on the wheel" + (item.License != null ? "  ·  " + item.License : ""), Foreground = Theme.Text3, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) });

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var rec = LibraryInstaller.Installed(S, item, localIds);
            bool update = LibraryInstaller.UpdateAvailable(S, item, localIds);
            if (LibraryClient.NeedsNewerPlugin(item) && rec == null)
                buttons.Children.Add(new TextBlock { Text = $"Needs plugin v{item.MinPlugin}: update the plugin (About tab)", Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap });
            else if (rec == null) buttons.Children.Add(Theme.Btn("Install", () => Install(item), primary: true, icon: ""));
            else
            {
                if (update) buttons.Children.Add(Theme.Btn("Update to v" + item.Version, () => Install(item), primary: true, icon: ""));
                else buttons.Children.Add(InstalledMark(rec));
                if (kind == "dash" && plugin.DashCarKey != null)
                    buttons.Children.Add(Theme.Btn("Use for this car", () => UseForCar(rec.DashId ?? "lib-" + item.Id, item.Name)));
                // a dash that was already here is the user's own file: it is removed from the dash's own tile, not from here
                if (!rec.Found) buttons.Children.Add(Theme.Btn("Remove", () => { LibraryInstaller.Remove(S, item.Kind, item.Id); Done("Removed " + item.Name); }));
            }
            buttons.Children.Add(Theme.Btn("Copy link", () => status.Text = DashShareUi.CopyLink(item.Kind, item.Id)));
            body.Children.Add(buttons);
            var tile = Theme.CardBox(body, 10, new Thickness(0, 0, 12, 12));
            tile.Width = 262;
            return tile;
        }

        private static TextBlock InstalledMark(LibraryInstall rec)
        {
            var t = new TextBlock { Text = "Installed ✓", Foreground = Theme.Green, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (rec.Found) t.ToolTip = "You already have this dash (" + rec.DashId + "), so it isn't installed again.";
            return t;
        }

        /// <summary>The library terms, once (again if their text changes).</summary>
        private bool TermsAccepted() => TermsGate.Accepted(Window.GetWindow(this), plugin, "Install from the library?", "FX Unleashed library");

        private void Install(LibraryItem item)
        {
            if (!TermsAccepted()) return;
            status.Text = "Installing " + item.Name + "...";
            var client = new LibraryClient(S.LibraryUrl);
            Task.Run(() => client.Download(item)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (t.IsFaulted) { status.Text = "Not installed: " + t.Exception.GetBaseException().Message; return; }
                try { LibraryInstaller.Install(S, item, t.Result); Done("Installed " + item.Name); }
                catch (Exception ex) { status.Text = "Not installed: " + ex.Message; }
            })));
        }

        private void UseForCar(string dashId, string name)
        {
            var car = plugin.DashCarKey;
            if (car == null) return;
            var refs = plugin.UsbRotation(car, out bool own, out _);
            var list = own ? new List<string>(refs) : new List<string>();
            string r = DashRef.Custom(dashId);
            list.Remove(r);
            list.Insert(0, r);
            plugin.SetUsbRotation(car, list, 0);
            Done(name + " is now this car's dash");
        }

        private void Done(string message)
        {
            plugin.SaveSettings();
            plugin.Usb?.ReloadDashes();
            plugin.Usb?.SettingsChanged();
            changed?.Invoke();
            Show();
            status.Text = message + ". " + status.Text;
        }
    }
}
