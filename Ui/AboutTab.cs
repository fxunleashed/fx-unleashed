using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Updates (check, install, roll back, channel) and About (version, disclaimer, licence, credits, the legal texts).
    /// In both modes: everyone updates the same plugin.
    /// </summary>
    public class AboutTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private Updater U => plugin.Updates;
        private UpdateSettings S => plugin.Settings.Updates;
        private readonly TextBlock state, notesTitle, requirement, rollbackText;
        private readonly TextBox notes;
        private readonly Button check, install, skip, restart, rollback, page;
        private readonly Border releaseBox;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        public AboutTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;

            // ----- Updates -----
            var up = new StackPanel();
            up.Children.Add(Theme.Eyebrow("Updates"));
            up.Children.Add(Theme.Title("FX Unleashed v" + Updater.CurrentVersion, 20));
            state = Theme.Note("", new Thickness(0, 6, 0, 12));
            up.Children.Add(state);
            var buttons = new WrapPanel();
            check = Theme.Btn("Check now", () => { _ = U?.CheckAsync(); Refresh(); }, icon: "");
            install = Theme.Btn("Install", Install, primary: true, icon: "");
            skip = Theme.Btn("Skip this version", () => { S.SkippedVersion = U?.Latest?.Version.ToString(); plugin.SaveSettings(); Refresh(); });
            restart = Theme.Btn("Restart SimHub", () => U?.RestartSimHub(), primary: true, icon: "");
            page = Theme.Btn("Release page", () => Open(U?.Latest?.PageUrl), icon: "");
            foreach (var b in new[] { check, install, skip, restart, page }) buttons.Children.Add(b);
            up.Children.Add(buttons);

            var rel = new StackPanel();
            notesTitle = new TextBlock { FontFamily = Theme.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            rel.Children.Add(notesTitle);
            requirement = new TextBlock { Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            rel.Children.Add(requirement);
            notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12 };
            rel.Children.Add(notes);
            rel.Children.Add(new TextBlock { Text = "Provided as is, without warranty. Nothing installs without your click; the download is checked against the release's checksums.",
                                             Foreground = Theme.Text3, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            releaseBox = new Border { Background = Theme.Raised, CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 8, 0, 12), Child = rel };
            up.Children.Add(releaseBox);

            up.Children.Add(Theme.Switch("Check for updates when SimHub starts (once a day)", S.AutoCheck, v => { S.AutoCheck = v; plugin.SaveSettings(); }));
            var channel = Theme.Segmented(new[] { "Stable", "Beta" }, S.Channel == UpdateChannel.Beta ? 1 : 0, i =>
            {
                S.Channel = i == 1 ? UpdateChannel.Beta : UpdateChannel.Stable;
                S.LastCheckUtc = DateTime.MinValue;
                plugin.SaveSettings();
                _ = U?.CheckAsync();
            }, out _);
            up.Children.Add(Theme.Field("Channel", channel, 130));
            up.Children.Add(Theme.Note("Beta gets test versions first (GitHub pre-releases). They can have more bugs; you can always roll back.", new Thickness(130, -6, 0, 12)));

            var adv = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var repo = new TextBox { Width = 280, Text = S.Repo };
            repo.LostFocus += (s, e) => { if (repo.Text.Trim().Split('/').Length == 2) { S.Repo = repo.Text.Trim(); plugin.SaveSettings(); } else repo.Text = S.Repo; };
            adv.Children.Add(Theme.Field("Releases from (GitHub)", repo, 170));
            rollbackText = Theme.Note("", new Thickness(0, 0, 0, 6));
            adv.Children.Add(rollbackText);
            rollback = Theme.Btn("Roll back", Rollback, icon: "");
            adv.Children.Add(rollback);
            up.Children.Add(new Expander { Header = "More", Content = adv });
            Children.Add(Theme.CardBox(up));

            // ----- About -----
            var about = new StackPanel();
            about.Children.Add(Theme.Eyebrow("About"));
            about.Children.Add(Legal.Block(Legal.Disclaimer, 13));
            about.Children.Add(Theme.Note("Licence: GNU GPL-3.0 or later. Free, non-commercial; source on GitHub.", new Thickness(0, 12, 0, 0)));
            var credits = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            credits.Children.Add(Theme.Eyebrow("Credits"));
            foreach (var c in new[]
            {
                "Car rev light data: Lovely Car Data by Lovely Sim Racing and contributors (github.com/Lovely-Sim-Racing/lovely-car-data), CC BY-NC-SA 4.0. Downloaded at runtime, not bundled.",
                "The built-in LMGT3 Ford Mustang GT3 dash is after the SimHub dash of the same name by Redadeg (lmu-dashboards.com), used with the author's permission.",
                "ATSR-Hub EVO by ATSR-Alex: the FX Pro layout file for it is generated from ATSR-Hub's GSI FPE-V2 device preset.",
                "SimHub by Wotever: the host for everything here, and the source of every game's data.",
            })
                credits.Children.Add(Theme.Note("•  " + c, new Thickness(0, 0, 0, 4)));
            about.Children.Add(credits);
            var links = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            links.Children.Add(Theme.Btn("fxunleashed.com", () => Open("https://fxunleashed.com"), icon: ""));
            links.Children.Add(Theme.Btn("Report a problem", () => Open("https://github.com/" + S.Repo + "/issues"), icon: ""));
            about.Children.Add(links);
            var legal = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            legal.Children.Add(Theme.Eyebrow("Firmware warning"));
            legal.Children.Add(Legal.Block(Legal.FirmwareWarning));
            legal.Children.Add(new Border { Height = 12 });
            legal.Children.Add(Theme.Eyebrow("Library"));
            legal.Children.Add(Legal.Block(Legal.LibraryTerms));
            about.Children.Add(new Expander { Header = "Warnings and terms", Content = legal, Margin = new Thickness(0, 6, 0, 0) });
            Children.Add(Theme.CardBox(about));

            timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { timer.Start(); Refresh(); };
            Unloaded += (s, e) => timer.Stop();
            Refresh();
        }

        private static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private async void Install()
        {
            var u = U;
            if (u?.Latest == null) return;
            if (MessageBox.Show(Window.GetWindow(this), $"Install FX Unleashed v{u.Latest.Version}?\n\n{Legal.Disclaimer}\n\nSimHub restarts to load it; your settings stay.",
                    "FX Unleashed", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var err = await u.InstallAsync();
            Refresh();
            if (err == null && MessageBox.Show(Window.GetWindow(this), "Installed. Restart SimHub now?", "FX Unleashed", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                u.RestartSimHub();
        }

        private void Rollback()
        {
            var prev = U?.PreviousVersion;
            if (prev == null) return;
            if (MessageBox.Show(Window.GetWindow(this), $"Go back to v{prev}? SimHub restarts to load it.", "FX Unleashed", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try { U.Rollback(); U.RestartSimHub(); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "Roll back failed: " + ex.Message, "FX Unleashed"); }
            Refresh();
        }

        private void Refresh()
        {
            var u = U;
            if (u == null)
            {
                state.Text = "Updates aren't available (the plugin didn't start normally).";
                foreach (var b in new[] { install, skip, restart, page, rollback }) b.Visibility = Visibility.Collapsed;
                check.IsEnabled = false;
                releaseBox.Visibility = Visibility.Collapsed;
                return;
            }
            var st = u.State;
            state.Text = st == Updater.UpdateState.Idle
                ? (S.LastCheckUtc > DateTime.MinValue ? $"Last checked {S.LastCheckUtc.ToLocalTime():g}." : "Not checked yet.")
                : u.Message;
            state.Foreground = st == Updater.UpdateState.Error ? Theme.Amber : st == Updater.UpdateState.Available || st == Updater.UpdateState.Installed ? Theme.Green : Theme.Text2;
            bool available = st == Updater.UpdateState.Available;
            install.Visibility = skip.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            install.IsEnabled = st != Updater.UpdateState.Downloading;
            restart.Visibility = st == Updater.UpdateState.Installed ? Visibility.Visible : Visibility.Collapsed;
            check.IsEnabled = st != Updater.UpdateState.Checking && st != Updater.UpdateState.Downloading;
            var r = u.Latest;
            page.Visibility = r?.PageUrl != null ? Visibility.Visible : Visibility.Collapsed;
            releaseBox.Visibility = available && r != null ? Visibility.Visible : Visibility.Collapsed;
            if (r != null)
            {
                notesTitle.Text = (string.IsNullOrWhiteSpace(r.Name) ? "v" + r.Version : r.Name) + (r.Prerelease ? "  ·  beta" : "") +
                                  (r.Published > DateTime.MinValue ? "  ·  " + r.Published.ToLocalTime().ToString("d") : "");
                if (notes.Text != r.Notes) notes.Text = r.Notes;
                var m = u.LatestManifest;
                requirement.Text = m?.firmware != null && m.firmware.min > 0 ? $"Unleashed mode in this version needs wheel firmware build {m.firmware.min} or newer (Wheel tab > Firmware)." : "";
                requirement.Visibility = requirement.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            var prev = u.PreviousVersion;
            rollback.IsEnabled = prev != null;
            rollbackText.Text = prev != null ? $"The previous version (v{prev}) is kept: roll back to it if this one misbehaves." : "No previous version kept yet (one is kept after the first update).";
        }
    }
}
