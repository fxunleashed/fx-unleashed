using System;
using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// What the settings page shows when the plugin couldn't start (Init threw): the error, and Roll back when an update
    /// left the previous version next to it. Needs nothing from the plugin's settings (they may be what failed).
    /// </summary>
    public class StartupFailedControl : UserControl
    {
        public StartupFailedControl(FXProRpmSyncPlugin plugin)
        {
            Theme.Apply(this);
            Background = Theme.Page;
            var body = new StackPanel { Margin = new Thickness(28), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(Theme.Eyebrow("FX Unleashed v" + Updater.CurrentVersion, Theme.Red));
            body.Children.Add(Theme.Title("The plugin couldn't start", 24));
            body.Children.Add(Theme.Note("Nothing was sent to the wheel or to SimPro. The details are in SimHub's log (Logs\\SimHub.txt, lines starting with [FXProRpmSync]).",
                                         new Thickness(0, 8, 0, 12)));
            body.Children.Add(new TextBox
            {
                Text = plugin.InitError?.ToString() ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = Theme.Mono, FontSize = 11, Margin = new Thickness(0, 0, 0, 16),
            });
            var result = Theme.Note("", new Thickness(0, 8, 0, 0));
            var prev = plugin.Updates?.PreviousVersion;
            var buttons = new WrapPanel();
            if (prev != null)
            {
                buttons.Children.Add(Theme.Btn($"Roll back to v{prev}", () =>
                {
                    try { plugin.Updates.Rollback(); result.Text = "Done. Restart SimHub to use v" + prev + "."; }
                    catch (Exception ex) { result.Text = "Roll back failed: " + ex.Message; }
                }, primary: true, icon: ""));
            }
            buttons.Children.Add(Theme.Btn("Restart SimHub", () => plugin.Updates?.RestartSimHub(), icon: ""));
            body.Children.Add(buttons);
            body.Children.Add(result);
            if (prev == null)
                body.Children.Add(Theme.Note("No previous version is kept here. Reinstall the plugin from its release page, or report the error on GitHub.", new Thickness(0, 8, 0, 0)));
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
