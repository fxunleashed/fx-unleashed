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
            var version = Box("Version", "1.0.0", "x.y.z. Publishing an update to something you already published? Raise it, or players won't see the update");
            var desc = Box("Description", d.Description);
            var games = Box("Games", "", "comma separated, as SimHub names them (e.g. LMU, IRacing, AssettoCorsaCompetizione)");
            var cars = Box("Cars", "", "comma separated (optional)");
            var tags = Box("Tags", "", "comma separated, e.g. gt3, endurance, minimal");
            var license = new ComboBox { Width = 380, IsEditable = true };
            foreach (var l in new[] { "CC-BY-4.0", "CC-BY-SA-4.0", "CC0-1.0", "MIT" }) license.Items.Add(l);
            license.SelectedIndex = 0;
            p.Children.Add(Theme.Field("Licence", license, 170));
            var source = Box("Based on", d.Source, "if you converted someone else's dash: which one (you need their permission)");
            var permission = Box("Permission", "", "for converted work: a link to where its author agreed to it being shared (required with \"Based on\")");
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
                    Id = id.Text.Trim(), Kind = kind, Version = version.Text.Trim(), Name = name.Text.Trim(), Author = author.Text.Trim(), Description = desc.Text.Trim(),
                    Games = List(games).ToList(), Cars = List(cars).ToList(), Tags = List(tags).ToList(),
                    License = (license.Text ?? "").Trim(), Source = string.IsNullOrWhiteSpace(source.Text) ? null : source.Text.Trim(),
                    Permission = string.IsNullOrWhiteSpace(permission.Text) ? null : permission.Text.Trim(),
                };
                if (!Regex.IsMatch(meta.Version ?? "", @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")) { status.Text = "The version looks like 1.0.0 (three numbers with dots)."; return; }
                if (meta.Source != null && meta.Permission == null) { status.Text = "This is based on someone else's work: add a link to where its author agreed to it being shared (Permission). Without it the library can't take it."; return; }
                if (string.IsNullOrEmpty(meta.Name) || string.IsNullOrEmpty(meta.Author) || string.IsNullOrEmpty(meta.License)) { status.Text = "Name, author and licence are needed."; return; }
                try
                {
                    status.Text = "Rendering and measuring (a minute of demo lap)...";
                    var dir = LibraryInstaller.Package(d, meta, OutRoot);
                    var zip = LibraryInstaller.ZipPackage(dir);
                    var open = MessageBox.Show(w, $"Packaged as\n{zip}\n\n" +
                                       "To publish it: open the submission form, drag that .zip into it, and submit. The library checks it " +
                                       "and a maintainer adds it; you are credited as its author. (A folder with the same files is next to it, for a pull request.)\n\n" +
                                       "Open the form now?",
                                    "Packaged", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
                    if (open == MessageBoxResult.Yes)
                        try { Process.Start(LibraryRepoUrl + "/issues/new?template=submit-dash.yml"); } catch { }
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
