using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The dashes that come inside the plugin (Usb/Bundled/*.json, embedded in the DLL; tools/bundle-dashes.py refreshes them from the
    /// library), so a fresh install has something to pick without a download.
    ///  - Library dashes keep the library's id with the prefix a library install gives them ("lib-slipstream"), so the library
    ///    shows them as installed, and a newer version installed from the library (a file in the dashes folder) takes their place.
    ///  - The two LMU conversions keep their own id (the one the dashes made earlier on the user's PC already use) and are in the
    ///    library as well (InLibrary); a file with that id takes their place too.
    /// They are read-only like the built-in Mustang ("Save a copy" to change one) and carry their author's credit.
    /// </summary>
    public static class BundledDashes
    {
        public class Entry
        {
            public string Id, Name, Author, Description, Version, Source, License, Sha256;
            /// <summary>Installed from the online library gives it the id "lib-" + Id; this one has that id (else it keeps Id).</summary>
            public bool Library;
            /// <summary>Is also an item of the online library with this Id (the library then shows it as installed).</summary>
            public bool InLibrary;
            public string DashId => Library ? "lib-" + Id : Id;
        }

        private const string Prefix = "User.FXProRpmSync.Bundled.";
        private static readonly object gate = new object();
        private static List<Entry> index;
        private static List<DashDefinition> parsed;

        public static List<Entry> Index
        {
            get
            {
                lock (gate)
                {
                    if (index != null) return index;
                    try
                    {
                        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + "index.json"))
                            index = s == null ? new List<Entry>() : JsonConvert.DeserializeObject<List<Entry>>(new StreamReader(s).ReadToEnd()) ?? new List<Entry>();
                    }
                    catch { index = new List<Entry>(); }
                    return index;
                }
            }
        }

        /// <summary>The bundled entry for a library item id (null if none).</summary>
        public static Entry ForLibraryItem(string libraryId) => Index.FirstOrDefault(e => (e.Library || e.InLibrary) && e.Id == libraryId);

        public static bool IsBundled(string dashId) => dashId != null && Index.Any(e => e.DashId == dashId);

        /// <summary>The dashes, parsed once. Failures are reported and skipped, never thrown.</summary>
        public static List<DashDefinition> All(List<string> errors = null)
        {
            lock (gate)
            {
                if (parsed != null) return parsed;
                var list = new List<DashDefinition>();
                var asm = Assembly.GetExecutingAssembly();
                foreach (var e in Index)
                {
                    try
                    {
                        using (var s = asm.GetManifestResourceStream(Prefix + e.Id + ".json"))
                        {
                            if (s == null) throw new Exception("not in the plugin");
                            var d = JsonConvert.DeserializeObject<DashDefinition>(new StreamReader(s).ReadToEnd());
                            if (d?.Elements == null) throw new Exception("no elements");
                            if (d.FormatVersion > DashDefinition.CurrentFormat) throw new Exception("made for a newer dash format");
                            d.Id = e.DashId;
                            d.Name = string.IsNullOrWhiteSpace(e.Name) ? d.Name : e.Name;
                            d.Author = e.Author ?? d.Author;
                            d.Description = e.Description ?? d.Description;
                            d.Source = e.Source ?? d.Source;
                            d.ScriptsFolder = null; // a path on the author's PC; the dash's own formulas are in the file
                            d.BuiltIn = true; d.Bundled = true;
                            list.Add(d);
                        }
                    }
                    catch (Exception ex) { errors?.Add("bundled " + e.Id + ": " + ex.Message); }
                }
                return parsed = list;
            }
        }
    }
}
