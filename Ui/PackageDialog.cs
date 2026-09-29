using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "Package for the library" (NEXT.md C, v1): asks for the item's details and the rights confirmation, then writes
    /// dashes/&lt;id&gt;/ (or savers/) with dash.json, meta.json and preview.png into PluginsData\...\LibraryPackages and opens
    /// it. The user adds that folder to the library repo with a pull request (the repo's CONTRIBUTING.md says how).
    /// </summary>
    internal static class PackageDialog
    {
        public static string OutRoot => Path.Combine(DashLibrary.SimHubFolder, "PluginsData", "Common", "FXProRpmSync", "LibraryPackages");
        public const string LibraryRepoUrl = "https://github.com/fxunleashed/fx-unleashed-library";

        public static void Show(Window owner, DashDefinition d, string kind)
        {
            var w = new Window
            {
                Title = "Package for the library", Owner = owner, Width = 620, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Theme.Page,
            };
            Theme.Apply(w);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(Theme.Title("Package \"" + d.Name + "\" for the library", 17));
            p.Children.Add(Theme.Note("Writes a folder you add to the library with a pull request. The plugin renders its preview and measures what it " +
                                      "sends to the wheel. Dashes with JavaScript (js: formulas, scripts) can't go in the library.", new Thickness(0, 6, 0, 12)));
            string Slug(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            TextBox Box(string label, string value, string hint = null)
            {
                var b = new TextBox { Width = 380, Text = value ?? "", ToolTip = hint };
                p.Children.Add(Theme.Field(label, b, 170));
                return b;
            }
            var id = Box("Id", Slug(d.Name), "lower case letters, digits and dashes; the folder's name");
            var name = Box("Name", d.Name);
            var author = Box("Author", d.Author);
            var desc = Box("Description", d.Description);
            var games = Box("Games", "", "comma separated, as SimHub names them (e.g. LMU, IRacing, AssettoCorsaCompetizione)");
            var cars = Box("Cars", "", "comma separated (optional)");
            var tags = Box("Tags", "", "comma separated, e.g. gt3, endurance, minimal");
            var license = new ComboBox { Width = 380, IsEditable = true };
            foreach (var l in new[] { "CC-BY-4.0", "CC-BY-SA-4.0", "CC0-1.0", "MIT" }) license.Items.Add(l);
            license.SelectedIndex = 0;
            p.Children.Add(Theme.Field("Licence", license, 170));
            var source = Box("Based on", d.Source, "if you converted someone else's dash: which one (you need their permission)");
            bool rights = false;
            var ack = Theme.Switch("I made this, or I have its author's permission to share it under this licence", false, v => rights = v);
            ack.Margin = new Thickness(0, 10, 0, 4);
            p.Children.Add(ack);
            var status = new TextBlock { Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(status);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(Theme.Btn("Cancel", () => w.Close()));
            buttons.Children.Add(Theme.Btn("Package", () =>
            {
                if (!rights) { status.Text = "Tick the permission box first: only your own work (or work you may share) goes in the library."; return; }
                string[] List(TextBox b) => b.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                var meta = new LibraryItem
                {
                    Id = id.Text.Trim(), Kind = kind, Name = name.Text.Trim(), Author = author.Text.Trim(), Description = desc.Text.Trim(),
                    Games = List(games).ToList(), Cars = List(cars).ToList(), Tags = List(tags).ToList(),
                    License = (license.Text ?? "").Trim(), Source = string.IsNullOrWhiteSpace(source.Text) ? null : source.Text.Trim(),
                };
                if (string.IsNullOrEmpty(meta.Name) || string.IsNullOrEmpty(meta.Author) || string.IsNullOrEmpty(meta.License)) { status.Text = "Name, author and licence are needed."; return; }
                try
                {
                    status.Text = "Rendering and measuring (a minute of demo lap)...";
                    var dir = LibraryInstaller.Package(d, meta, OutRoot);
                    try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
                    MessageBox.Show(w, $"Packaged in\n{dir}\n\nTo publish it: add the folder \"{Path.GetFileName(Path.GetDirectoryName(dir))}\\{meta.Id}\" to the library " +
                                       $"repo with a pull request ({LibraryRepoUrl}, see CONTRIBUTING.md). The library checks it again before it goes in.",
                                    "Packaged", MessageBoxButton.OK, MessageBoxImage.Information);
                    w.Close();
                }
                catch (Exception ex) { status.Text = "Not packaged: " + ex.Message; }
            }, primary: true));
            foreach (Button b in buttons.Children) b.Margin = new Thickness(8, 0, 0, 0);
            p.Children.Add(buttons);
            w.Content = p;
            w.ShowDialog();
        }
    }
}
