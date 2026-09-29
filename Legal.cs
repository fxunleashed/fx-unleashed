using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The warnings and disclaimers, embedded from docs/legal/*.md (one source with the website; NEXT.md O3). Texts are
    /// plain paragraphs and "- " bullets, hard-wrapped in the files and unwrapped here.
    /// </summary>
    public static class Legal
    {
        public static string Disclaimer => Load("disclaimer");
        public static string FirmwareWarning => Load("firmware-warning");
        public static string LibraryTerms => Load("library-terms");
        public static string BackToStock => Load("back-to-stock");

        /// <summary>Short hash of a text: an acknowledgement is asked again when the text changes.</summary>
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }

        private static string Load(string name)
        {
            try
            {
                using (var s = typeof(Legal).Assembly.GetManifestResourceStream("User.FXProRpmSync.Legal." + name + ".md"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return Unwrap(r.ReadToEnd());
            }
            catch { return "(text missing: docs/legal/" + name + ".md)"; }
        }

        /// <summary>Joins hard-wrapped lines: blank lines split paragraphs, "- " starts a bullet.</summary>
        public static string Unwrap(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) { if (sb.Length > 0 && !sb.ToString().EndsWith("\n\n")) sb.Append("\n\n"); continue; }
                bool bullet = line.StartsWith("- ");
                if (bullet) { if (sb.Length > 0 && !sb.ToString().EndsWith("\n")) sb.Append('\n'); sb.Append("•  ").Append(line.Substring(2)); }
                else if (sb.Length == 0 || sb.ToString().EndsWith("\n")) sb.Append(line.Trim());
                else sb.Append(' ').Append(line.Trim());
            }
            return sb.ToString().Trim();
        }

        /// <summary>A text as a block for the settings page.</summary>
        public static TextBlock Block(string text, double size = 12.5) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text2, FontSize = size, LineHeight = size * 1.5,
        };
    }
}
