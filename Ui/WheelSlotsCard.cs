using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// FX Pro, wheel app build 8+: which controller buttons the dash button and the two upper paddles report as (the
    /// patched firmware sends them on buttons the plugin picks; FxProControls). Build 9 has 48 buttons and 41-48 are
    /// free; build 8 only has the 40 stock ones, all taken, so there they share. Picking a button another of the three
    /// uses swaps the two, so they never collide with each other; a stock control's button is shown with its owner.
    /// </summary>
    public class WheelSlotsCard : Border
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly ComboBox dash = Box(), padA = Box(), padB = Box();
        private readonly CheckBox paddles;
        private readonly TextBlock note, warning, clutchNote;
        private readonly Action<int> selectClutch;
        private int shownBuild = -2;
        private bool loading;

        public WheelSlotsCard(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;
            var body = new StackPanel();
            body.Children.Add(Theme.Eyebrow("Wheel buttons"));
            body.Children.Add(Theme.Title("Dash button, paddles and clutch", 18));
            note = Theme.Note("", new Thickness(0, 6, 0, 12));
            body.Children.Add(note);
            body.Children.Add(Theme.Field("Dash button", dash));
            paddles = Theme.Switch("Send the upper paddles as buttons", S.UpperPaddles, v => { S.UpperPaddles = v; Apply(); Fill(); });
            paddles.Margin = new Thickness(0, 4, 0, 8);
            body.Children.Add(paddles);
            body.Children.Add(Theme.Field("Left upper paddle", padA));
            body.Children.Add(Theme.Field("Right upper paddle", padB));
            var clutch = Theme.Segmented(new[] { "As set in SimPro", "Two axes", "Buttons (24 / 27)" }, Math.Max(0, Math.Min(2, S.ClutchMode)), i =>
            {
                if (loading || S.ClutchMode == i) return;
                S.ClutchMode = i;
                Apply();
                Fill();
            }, out selectClutch);
            body.Children.Add(Theme.Field("Clutch paddles", clutch));
            clutchNote = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(170, -6, 0, 6) };
            body.Children.Add(clutchNote);
            warning = new TextBlock { Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            body.Children.Add(warning);
            var reset = Theme.Btn("Defaults", () =>
            {
                var (d, a, b) = FxProControls.Defaults(Build);
                S.DashSlot = d; S.UpperPaddleA = a; S.UpperPaddleB = b;
                Apply(); Fill();
            }, icon: "");
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(reset);

            Child = Theme.CardBox(body, 20, new Thickness(0, 0, 0, 14));
            dash.SelectionChanged += (s, e) => Picked(dash, "dash");
            padA.SelectionChanged += (s, e) => Picked(padA, "a");
            padB.SelectionChanged += (s, e) => Picked(padB, "b");
            Fill();
        }

        private static ComboBox Box() => new ComboBox { Width = 330 };

        /// <summary>The wheel's patch build, or the last one seen (8 when unknown: the conservative range).</summary>
        private int Build => plugin.Usb?.FirmwareBuild is int b && b > 0 ? b : Math.Max(8, S.DashSlot > 40 || S.UpperPaddleA > 40 || S.UpperPaddleB > 40 ? 9 : 8);

        /// <summary>Call from the tab's refresh: rebuilds the lists when the wheel's build becomes known or changes.</summary>
        public void Refresh()
        {
            if (Build != shownBuild) Fill();
        }

        private void Fill()
        {
            loading = true;
            int build = Build;
            shownBuild = build;
            FxProControls.Normalize(S, build);
            int max = FxProControls.MaxButton(build);
            note.Text = build >= 9
                ? "Your wheel runs patch build 9: it has 48 buttons, and 41-48 belong to these three alone. Any button a stock control uses is listed too, with its owner."
                : "Patch build 8 only has the 40 stock buttons, and the wheel's own controls use all of them: these three share one. " +
                  "Patch build 9 gives them buttons 41-48 of their own.";
            foreach (var (box, value) in new[] { (dash, S.DashSlot), (padA, S.UpperPaddleA), (padB, S.UpperPaddleB) })
            {
                box.Items.Clear();
                for (int b = max; b >= 1; b--)
                {
                    string owner = Lower(FxProControls.StockOwner(b, S));
                    string text = owner == null
                        ? (FxProControls.IsClutchButtonMode(b) ? $"{b}  ·  free while the clutch paddles are axes" : $"{b}  ·  free")
                        : $"{b}  ·  shared with the {owner}";
                    box.Items.Add(new ComboBoxItem { Content = text, Tag = b, Foreground = owner == null ? Theme.Text : Theme.Text3 });
                }
                box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value);
            }
            padA.IsEnabled = padB.IsEnabled = S.UpperPaddles;
            selectClutch?.Invoke(Math.Max(0, Math.Min(2, S.ClutchMode)));
            clutchNote.Text = S.ClutchMode == 0
                ? "The plugin leaves the clutch mode as SimPro set it. After picking another mode here, a power-off of the wheel brings SimPro's back."
                : S.ClutchMode == 1
                    ? "Each clutch paddle is its own axis. Set by the plugin while it drives the wheel; SimPro's setting comes back after a power-off."
                    : "The clutch paddles are buttons 24 (left) and 27 (right), with no axes. Set by the plugin while it drives the wheel; SimPro's setting comes back after a power-off.";
            paddles.IsChecked = S.UpperPaddles;
            Warn();
            loading = false;
        }

        private void Picked(ComboBox box, string which)
        {
            if (loading || !(box.SelectedItem is ComboBoxItem item) || !(item.Tag is int b)) return;
            // no two of the three on one button: picking another's button swaps them
            int old = which == "dash" ? S.DashSlot : which == "a" ? S.UpperPaddleA : S.UpperPaddleB;
            if (b == old) return;
            if (which != "dash" && b == S.DashSlot) S.DashSlot = old;
            if (which != "a" && b == S.UpperPaddleA) S.UpperPaddleA = old;
            if (which != "b" && b == S.UpperPaddleB) S.UpperPaddleB = old;
            if (which == "dash") S.DashSlot = b; else if (which == "a") S.UpperPaddleA = b; else S.UpperPaddleB = b;
            Apply();
            Fill();
        }

        private static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private void Apply()
        {
            plugin.SaveSettings();
            plugin.Usb?.ButtonSlotsChanged();
        }

        private void Warn()
        {
            var shared = new[] { ("Dash button", S.DashSlot), ("Left upper paddle", S.UpperPaddleA), ("Right upper paddle", S.UpperPaddleB) }
                .Where((x, i) => (i == 0 || S.UpperPaddles) && FxProControls.StockOwner(x.Item2, S) != null)
                .Select(x => $"{x.Item1} shares button {x.Item2} with the {Lower(FxProControls.StockOwner(x.Item2, S))}.").ToList();
            warning.Text = string.Join(" ", shared) + (shared.Count > 0 ? " A game bound to that button sees both." : "");
            warning.Visibility = shared.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
