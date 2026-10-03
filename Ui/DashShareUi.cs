using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;

namespace User.FXProRpmSync
{
    /// <summary>The terms notice, once, before anything that came from outside is installed (a library item or a dash file).</summary>
    internal static class TermsGate
    {
        public static bool Accepted(Window owner, FXProRpmSyncPlugin plugin, string question, string title)
        {
            var s = plugin.Settings.Usb;
            string hash = Legal.Hash(Legal.LibraryTerms);
            if (s.LibraryTermsAccepted == hash) return true;
            var ok = MessageBox.Show(owner, Legal.Unwrap(Legal.LibraryTerms) + "\n\n" + question, title,
                                     MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (ok) { s.LibraryTermsAccepted = hash; plugin.SaveSettings(); }
            return ok;
        }
    }

    /// <summary>Import a dash file, share a dash as a file, copy a library link: the buttons' work, with plain messages.</summary>
    internal static class DashShareUi
    {
        private const string Title = "FX Unleashed";

        public static bool IsDashFile(string path) => path != null && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        /// <summary>Asks for files with a picker, then <see cref="Import(Window, FXProRpmSyncPlugin, IEnumerable{string})"/>.</summary>
        public static List<DashShare.Imported> ImportWithPicker(Window owner, FXProRpmSyncPlugin plugin)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import a dash file",
                Filter = "Dash files (*.fxdash.json;*.json)|*.fxdash.json;*.json|All files|*.*",
                Multiselect = true,
                CheckFileExists = true,
            };
            return dlg.ShowDialog(owner) == true ? Import(owner, plugin, dlg.FileNames) : new List<DashShare.Imported>();
        }

        /// <summary>
        /// Imports dash files after the terms notice (once). Shows what was added and what was refused and why. The caller
        /// reloads the dashes when the result is not empty.
        /// </summary>
        public static List<DashShare.Imported> Import(Window owner, FXProRpmSyncPlugin plugin, IEnumerable<string> files)
        {
            var done = new List<DashShare.Imported>();
            var refused = new List<string>();
            var list = files.ToList();
            if (list.Count == 0) return done;

            // read first: refusals need no question, and the notice can name the dash
            var readable = new List<string>();
            string firstName = null;
            foreach (var f in list)
            {
                try { var d = DashShare.Read(f); readable.Add(f); firstName = firstName ?? $"\"{d.Name ?? Path.GetFileName(f)}\"" + (string.IsNullOrWhiteSpace(d.Author) ? "" : " by " + d.Author); }
                catch (Exception ex) { refused.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            if (readable.Count > 0 && !TermsGate.Accepted(owner, plugin,
                    (readable.Count == 1 ? "Import " + firstName : "Import these " + readable.Count + " dash files") +
                    "?\n\nIt comes from a file, not from the library. The plugin checks its format, its size and that it contains no scripts, " +
                    "but not who made it or whether they allow it to be shared. A dash can only draw on the wheel's screen and read SimHub values.",
                    "Import a dash file"))
                return done;

            foreach (var f in readable)
            {
                try { done.Add(DashShare.Import(f)); }
                catch (Exception ex) { refused.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }

            var lines = new List<string>();
            foreach (var i in done)
                lines.Add(i.AlreadyThere ? $"\"{i.Dash.Name}\" was already installed."
                        : i.Renamed ? $"Added \"{i.Dash.Name}\" (you already had a different dash with that id, so it got the id \"{i.Dash.Id}\")."
                        : $"Added \"{i.Dash.Name}\".");
            if (refused.Count > 0)
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add("Not imported:");
                lines.AddRange(refused.Select(r => "  " + r));
            }
            if (lines.Count > 0)
                MessageBox.Show(owner, string.Join("\n", lines) + (done.Any(x => !x.AlreadyThere) ? "\n\nFind it in the dash list: add it to a car, or try it with Demo on the wheel." : ""),
                    Title, MessageBoxButton.OK, refused.Count > 0 && done.Count == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return done;
        }

        /// <summary>Saves the dash as a shareable file and puts the file on the clipboard (paste it into a message).</summary>
        public static void Share(Window owner, DashDefinition d)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(d.Source) && d.Source.StartsWith("SimHub", StringComparison.OrdinalIgnoreCase))
                {
                    var ask = MessageBox.Show(owner, "This dash was converted from a SimHub dash (" + d.Source + ").\n\nShare it only if its author " +
                                                     "allows that. Share it anyway?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (ask != MessageBoxResult.Yes) return;
                }
                var dlg = new SaveFileDialog
                {
                    Title = "Share this dash as a file",
                    FileName = DashShare.FileName(d.Id),
                    Filter = "FX Unleashed dash (*.fxdash.json)|*.fxdash.json",
                    AddExtension = false,
                    OverwritePrompt = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                };
                if (dlg.ShowDialog(owner) != true) return;
                var path = DashShare.Export(d, dlg.FileName);
                bool onClipboard = false;
                try { Clipboard.SetFileDropList(new StringCollection { path }); onClipboard = true; } catch (Exception) { }
                MessageBox.Show(owner, "Saved as\n" + path + "\n\n" + (onClipboard ? "The file is on the clipboard too: paste it into a message. " : "") +
                                       "To use it, the other person drops it onto the Dashes tab, or presses \"Import a file\".\n\n" +
                                       "To share it with everyone, use \"Package for the library\" and submit it at fxunleashed.com/library.",
                                Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        /// <summary>Copies a library item's page address; the page shows the preview and an Install button.</summary>
        public static string CopyLink(string kind, string id)
        {
            var link = DashShare.LinkFor(kind, id);
            try { Clipboard.SetText(link); return "Link copied: " + link; }
            catch (Exception) { return "Couldn't use the clipboard. The link is " + link; }
        }
    }
}
