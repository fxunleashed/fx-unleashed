using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Lights per car and per game: "This car: [preset] · This game: [preset]" for what's driving now, and the list of
    /// every car and game with its own. Resolution is car, then game, then the preset selected in the gallery
    /// (FXProRpmSyncPlugin.ActiveLightsFor).
    /// </summary>
    public class LightsForCarPanel : StackPanel
    {
        private const string Inherit = "";
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly ComboBox carBox, gameBox;
        private readonly TextBlock carLabel, gameLabel, resolved;
        private readonly StackPanel saved = new StackPanel();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private string shownCar;
        private int shownCount = -1;
        private bool loading;

        public LightsForCarPanel(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            Children.Add(Theme.Eyebrow("Per game and per car"));
            Children.Add(Theme.Note("A car uses its own lights if it has some, else its game's, else the preset selected above.", new Thickness(0, 0, 0, 10)));
            carBox = Picker(id => plugin.SetCarLights(plugin.DashCarKey, id));
            gameBox = Picker(id => plugin.SetGameLights(FXProRpmSyncPlugin.GameOf(plugin.DashCarKey), id));
            Children.Add(Row(out carLabel, carBox));
            Children.Add(Row(out gameLabel, gameBox));
            resolved = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 10) };
            Children.Add(resolved);
            Children.Add(new Expander { Header = "Every car and game with its own lights", Content = saved });
            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { timer.Start(); Refresh(force: true); };
            Unloaded += (s, e) => timer.Stop();
        }

        private static FrameworkElement Row(out TextBlock label, ComboBox box)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
            label = new TextBlock { Width = 300, Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(label);
            row.Children.Add(box);
            return row;
        }

        private ComboBox Picker(Action<string> set)
        {
            var box = new ComboBox { Width = 260 };
            box.SelectionChanged += (s, e) =>
            {
                if (loading || !(box.SelectedItem is ComboBoxItem item) || !(item.Tag is string id)) return;
                set(id == Inherit ? null : id);
                Refresh(force: true);
            };
            return box;
        }

        /// <summary>Fills a picker: "same as the level below", then every preset; selects `selected`.</summary>
        private void Fill(ComboBox box, string inheritName, string selected)
        {
            box.Items.Clear();
            box.Items.Add(new ComboBoxItem { Content = inheritName, Tag = Inherit });
            foreach (var id in S.AllLightIds()) box.Items.Add(new ComboBoxItem { Content = S.FindLights(id)?.Name ?? id, Tag = id });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? box.Items[0];
        }

        /// <summary>Refreshes when the car, the settings or the presets changed (a combo box isn't rebuilt while open).</summary>
        public void Refresh(bool force = false)
        {
            string car = plugin.DashCarKey;
            int count = S.CarLights.Count * 1000 + S.GameLights.Count * 10 + S.AllLightIds().Count + (S.LightPreset?.GetHashCode() ?? 0);
            if (!force && car == shownCar && count == shownCount) return;
            if (carBox.IsDropDownOpen || gameBox.IsDropDownOpen) return;
            shownCar = car; shownCount = count;
            loading = true;
            try
            {
                string game = FXProRpmSyncPlugin.GameOf(car);
                bool inCar = car != null;
                carBox.IsEnabled = gameBox.IsEnabled = inCar;
                carLabel.Text = inCar ? "This car: " + (plugin.CurrentCarNameForDash ?? car) : "This car: start a game to set it";
                gameLabel.Text = inCar ? "This game: " + game : "This game: start a game to set it";
                string gameLights = game != null && S.GameLights.TryGetValue(game, out var g) ? g : null;
                Fill(carBox, "Same as the game (" + (S.FindLights(gameLights)?.Name ?? S.ActiveLights.Name) + ")",
                     inCar && S.CarLights.TryGetValue(car, out var c) && S.FindLights(c) != null ? c : Inherit);
                Fill(gameBox, "Same as everything else (" + S.ActiveLights.Name + ")", gameLights != null && S.FindLights(gameLights) != null ? gameLights : Inherit);
                string id = plugin.LightsIdFor(car, out string level);
                resolved.Text = inCar ? $"Showing now: {S.FindLights(id)?.Name ?? S.ActiveLights.Name} (from the {(level == "car" ? "car" : level == "game" ? "game" : "preset selected above")})." : "";
                BuildSaved();
            }
            finally { loading = false; }
        }

        private void BuildSaved()
        {
            saved.Children.Clear();
            var entries = S.GameLights.Select(kv => (Kind: "game", Key: kv.Key, Name: kv.Key, Id: kv.Value))
                .Concat(S.CarLights.Select(kv => (Kind: "car", Key: kv.Key, Name: CarName(kv.Key), Id: kv.Value)))
                .OrderBy(e => e.Kind).ThenBy(e => e.Name).ToList();
            if (entries.Count == 0) { saved.Children.Add(Theme.Note("None yet.", new Thickness(0, 6, 0, 0))); return; }
            foreach (var e in entries)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
                var entry = e;
                var remove = Theme.Btn("Remove", () =>
                {
                    if (entry.Kind == "car") plugin.SetCarLights(entry.Key, null); else plugin.SetGameLights(entry.Key, null);
                    Refresh(force: true);
                }, icon: "");
                remove.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock
                {
                    Text = (entry.Kind == "car" ? "Car  " : "Game  ") + entry.Name + "  →  " + (S.FindLights(entry.Id)?.Name ?? "(deleted lights: uses the next level)"),
                    Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                saved.Children.Add(row);
            }
        }

        /// <summary>A car's name from its dash entry if known, else its key.</summary>
        private string CarName(string key) =>
            S.CarDashes.TryGetValue(key, out var d) && !string.IsNullOrEmpty(d.CarName) ? d.CarName + " (" + d.Game + ")" : key;
    }
}
