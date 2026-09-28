using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace User.FXProRpmSync
{
    /// <summary>A car the plugin can key settings on ("Game | CarId").</summary>
    public class KnownCar
    {
        public string Game, CarId, Name;
        public string Key => Game + " | " + CarId;
    }

    /// <summary>
    /// Every car SimHub has seen (it keeps one settings file per car and game: PluginsData\&lt;game&gt;\Cars\*.shcarsettings,
    /// with the same CarId the plugin keys on), plus the cars the plugin already has settings for.
    /// </summary>
    internal static class KnownCars
    {
        public static List<KnownCar> All(FXProRpmSyncPlugin plugin)
        {
            var cars = new Dictionary<string, KnownCar>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData");
                foreach (var gameDir in Directory.Exists(root) ? Directory.GetDirectories(root) : new string[0])
                {
                    var carsDir = Path.Combine(gameDir, "Cars");
                    if (!Directory.Exists(carsDir)) continue;
                    string game = Path.GetFileName(gameDir);
                    foreach (var f in Directory.GetFiles(carsDir, "*.shcarsettings"))
                    {
                        try
                        {
                            var j = JObject.Parse(File.ReadAllText(f));
                            var id = (string)j["CarId"];
                            if (string.IsNullOrEmpty(id)) continue;
                            var car = new KnownCar { Game = game, CarId = id, Name = (string)j["CarModel"] ?? id };
                            cars[car.Key] = car;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            void Add(string key, string game, string id, string name)
            {
                if (key == null || cars.ContainsKey(key)) return;
                var parts = key.Split(new[] { " | " }, 2, StringSplitOptions.None);
                cars[key] = new KnownCar { Game = game ?? parts[0], CarId = id ?? (parts.Length > 1 ? parts[1] : key), Name = name ?? id ?? key };
            }
            foreach (var d in plugin.AllUsbCarDashes()) Add(d.CarKey, d.Game, d.CarId, d.CarName);
            foreach (var d in plugin.AllCarDashes()) Add(d.CarKey, d.Game, d.CarId, d.CarName);
            foreach (var o in plugin.AllOverrides()) Add(o.CarKey, o.Game, o.CarId, o.CarName);
            return cars.Values.OrderBy(c => c.Game).ThenBy(c => c.Name).ToList();
        }
    }

    /// <summary>Picks a car from KnownCars: a game, then a searchable list. Null if cancelled.</summary>
    internal sealed class CarPicker : Window
    {
        private readonly List<KnownCar> cars;
        private readonly ComboBox games = new ComboBox { Width = 260 };
        private readonly TextBox search = new TextBox { Width = 300 };
        private readonly ListBox list = new ListBox { Height = 360 };
        public KnownCar Picked { get; private set; }

        public static KnownCar Pick(FrameworkElement owner, FXProRpmSyncPlugin plugin, string title)
        {
            var w = new CarPicker(KnownCars.All(plugin), title) { Owner = Window.GetWindow(owner) };
            return w.ShowDialog() == true ? w.Picked : null;
        }

        private CarPicker(List<KnownCar> cars, string title)
        {
            this.cars = cars;
            Title = title; Width = 640; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize; Background = Theme.Card; ShowInTaskbar = false;
            var root = new StackPanel { Margin = new Thickness(20) };
            Theme.Apply(root);
            root.Children.Add(Theme.Title(title, 20));
            root.Children.Add(Theme.Note("Every car SimHub has seen, by game. A car you haven't driven yet shows up after its first session.", new Thickness(0, 6, 0, 14)));
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            games.Items.Add(new ComboBoxItem { Content = "All games", Tag = null });
            foreach (var g in cars.Select(c => c.Game).Distinct()) games.Items.Add(new ComboBoxItem { Content = g, Tag = g });
            games.SelectedIndex = 0;
            games.SelectionChanged += (s, e) => Fill();
            top.Children.Add(games);
            search.Margin = new Thickness(10, 0, 0, 0);
            search.TextChanged += (s, e) => Fill();
            top.Children.Add(search);
            root.Children.Add(top);
            list.Background = Theme.Raised; list.BorderBrush = Theme.Line2; list.Foreground = Theme.Text;
            list.MouseDoubleClick += (s, e) => Ok();
            root.Children.Add(list);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(Theme.Btn("Cancel", () => { DialogResult = false; }));
            buttons.Children.Add(Theme.Btn("Choose", Ok, primary: true, icon: ""));
            root.Children.Add(buttons);
            Content = root;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; if (e.Key == Key.Enter) Ok(); };
            Loaded += (s, e) => search.Focus();
            Fill();
        }

        private void Fill()
        {
            var game = (games.SelectedItem as ComboBoxItem)?.Tag as string;
            var q = search.Text.Trim();
            list.Items.Clear();
            foreach (var c in cars.Where(c => (game == null || c.Game == game) &&
                                              (q.Length == 0 || c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.CarId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                var row = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
                row.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = c.Game + (c.CarId != c.Name ? "  ·  " + c.CarId : ""), Foreground = Theme.Text3, FontSize = 11 });
                list.Items.Add(new ListBoxItem { Content = row, Tag = c });
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        private void Ok()
        {
            if (!(list.SelectedItem is ListBoxItem i)) return;
            Picked = (KnownCar)i.Tag;
            DialogResult = true;
        }
    }
}
