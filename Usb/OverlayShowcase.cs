using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace User.FXProRpmSync
{
    /// <summary>
    /// A dash's overlays (each set of conditions its elements share: a flag oval, a pit screen, a warning) and how to bring
    /// one up over simulated data: its conditions made true, the "take turns" negations of them (ncalc:!(...), written by
    /// tune and dash scripts) false, and what waits a while after an event it starts (!changed(N, [X])) following the
    /// time since. Used by verify's overlay sweep and by the demo on the wheel, which takes the overlays in turn so the
    /// whole dash shows, not just what a simulated lap reaches (pit limiter, ignition off, flags...).
    /// </summary>
    public sealed class OverlayShowcase
    {
        public readonly List<List<string>> Groups = new List<List<string>>();
        private readonly List<string> allConditions;

        /// <summary>An element's own conditions: not its page, not the "take turns" ones.</summary>
        public static List<string> Own(DashElement e) =>
            (e.Visible ?? new List<string>()).Where(c => !DashPages.Parse(c).HasValue && !c.StartsWith("ncalc:!(")).Distinct().ToList();

        public OverlayShowcase(DashDefinition d)
        {
            foreach (var e in d.Elements)
            {
                var own = Own(e);
                if (own.Count > 0 && !Groups.Any(g => g.SequenceEqual(own))) Groups.Add(own);
            }
            allConditions = d.Elements.Where(e => e.Visible != null).SelectMany(e => e.Visible).Distinct().ToList();
        }

        /// <summary>
        /// The overlays for a list (the designer's overlay picker): index, a name people recognise (its biggest shape's or
        /// first text's name), how many elements, its conditions, and the overlay it is inside.
        /// </summary>
        public List<object> Describe(DashDefinition d)
        {
            var list = new List<object>();
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                var members = d.Elements.Where(e => Own(e).SequenceEqual(g)).ToList();
                // named by what it says (its texts: "PITSTOP REQ", "BLUE FLAG"), else its biggest shape's name
                List<string> Texts(IEnumerable<DashElement> els) => els.Where(e => e.Type == "label" && !string.IsNullOrWhiteSpace(e.Text) && e.Text.Trim().Length > 1).Select(e => e.Text.Trim()).Distinct().Take(2).ToList();
                // its own text, else its biggest shape's name made readable ("IGN ON background" -> "IGN ON"), else what the
                // overlays inside it say (a race start screen's blinking "HOLD LINE"), else its condition
                var texts = Texts(members);
                var big = members.Where(e => e.Type != "label" && e.Type != "value").OrderByDescending(e => e.W * e.H).FirstOrDefault();
                string name = texts.Count > 0 ? string.Join(" ", texts) : Readable(big?.Name);
                if (name == null)
                {
                    var inner = Texts(d.Elements.Where(e => { var o = Own(e); return o.Count > g.Count && g.All(o.Contains); }));
                    if (inner.Count > 0) name = string.Join(" ", inner);
                }
                if (name == null)
                {
                    var c = Regex.Replace(string.Join(" & ", g.Select(x => Regex.Replace(x, "^(ncalc|js):", ""))), "\\s+", " ").Trim();
                    name = "When " + (c.Length > 48 ? c.Substring(0, 47) + "…" : c);
                }
                var p = Parent(g);
                int area = members.Where(e => e.Type != "label" && e.Type != "value").Select(e => e.W * e.H).DefaultIfEmpty(0).Max();
                list.Add(new
                {
                    index = i, name, elements = members.Count, conditions = g,
                    first = members.Count > 0 ? d.Elements.IndexOf(members[0]) : -1,
                    parent = p == null ? -1 : Groups.IndexOf(p),
                    // a real overlay (a box or oval of some size, or several things) or a small conditional item (an icon)
                    major = area >= 0.025 * DashRenderer.Width * DashRenderer.Height || members.Count >= 3,
                });
            }
            return list;
        }

        /// <summary>An element name made readable for people ("IGN OFF background" -> "IGN OFF", "BLU+GRN+RED_Elipse" ->
        /// "BLU+GRN+RED"), or null when nothing is left ("RectangleItem2").</summary>
        private static string Readable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = Regex.Replace(name, @"(?i)\b(background|rectangle ?item\d*|item\d*)\b|_?el+ip+se\d*|_", " ");
            n = Regex.Replace(n, @"\s+", " ").Trim();
            return n.Length > 1 ? n : null;
        }

        /// <summary>The values that bring overlay `index` up (with its parent), for a preview; null for none.</summary>
        public Dictionary<string, bool> ForceFor(int index) => index >= 0 && index < Groups.Count ? Force(Groups[index], 5000) : null;

        /// <summary>The overlay `g` is inside (its conditions all among g's, the most of them), or null.</summary>
        public List<string> Parent(List<string> g) =>
            Groups.Where(x => x != g && x.Count < g.Count && x.All(g.Contains)).OrderByDescending(x => x.Count).FirstOrDefault();

        private static readonly Regex Event = new Regex(@"(?<!!)\bchanged\(\s*\d+\s*,\s*(\[[^\]]+\])\s*\)", RegexOptions.IgnoreCase);
        private static Regex Waits(string prop) => new Regex(@"^ncalc:\s*!\s*changed\(\s*(\d+)\s*,\s*" + Regex.Escape(prop) + @"\s*\)\s*$", RegexOptions.IgnoreCase);

        /// <summary>
        /// The values that bring `g` up `sinceMs` after it started: its conditions true, their negations (single, and every
        /// part joined in order) false, and the conditions waiting on an event it starts true once their time has passed.
        /// </summary>
        public Dictionary<string, bool> Force(List<string> g, double sinceMs = 0)
        {
            var force = new Dictionary<string, bool>();
            foreach (var c in g)
            {
                force[c] = true;
                if (c.StartsWith("ncalc:")) force["ncalc:!(" + c.Substring(6) + ")"] = false;
            }
            // an overlay inside another shows its parent too: the negations of every part of its conditions go false
            var nc = g.Where(c => c.StartsWith("ncalc:")).Take(8).ToList();
            for (int mask = 1; mask < (1 << nc.Count); mask++)
            {
                var part = nc.Where((c, i) => (mask >> i & 1) != 0).ToList();
                if (part.Count > 1) force["ncalc:!(" + string.Join(" and ", part.Select(c => "(" + c.Substring(6) + ")")) + ")"] = false;
            }
            // an event just happened (changed(N, [X]) true): whatever waits M ms after it (!changed(M, [X])) comes then
            foreach (var c in g)
                foreach (Match ev in Event.Matches(c))
                {
                    var waits = Waits(ev.Groups[1].Value);
                    foreach (var other in allConditions)
                    {
                        if (g.Contains(other)) continue;
                        var m = waits.Match(other);
                        if (m.Success) force[other] = sinceMs >= double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            return force;
        }

        // ---------- the demo's turn-taking ----------

        /// <summary>Seconds each overlay shows, and the plain dash between two.</summary>
        public double ShowSeconds = 3, GapSeconds = 2;

        /// <summary>
        /// The overlays the demo takes in turn: every one but those that only add a blink or a wait to their parent (they
        /// come with it: a blinking label of a warning, the staged items of a start-up screen).
        /// </summary>
        public List<List<string>> Turns()
        {
            bool OnlyTiming(List<string> g)
            {
                var p = Parent(g);
                return p != null && g.Where(c => !p.Contains(c)).All(c => c.IndexOf("blink(", StringComparison.OrdinalIgnoreCase) >= 0 || Regex.IsMatch(c, @"^ncalc:\s*!\s*changed\(", RegexOptions.IgnoreCase));
            }
            return Groups.Where(g => !OnlyTiming(g)).ToList();
        }

        private List<List<string>> turns;

        /// <summary>The demo at `t` seconds: sets the values of the overlay whose turn it is (none in the gaps).</summary>
        public void Apply(DashValues v, double t)
        {
            if (turns == null) turns = Turns();
            if (turns.Count == 0 || t < GapSeconds) return;
            double slot = ShowSeconds + GapSeconds, at = (t - GapSeconds) % (slot * turns.Count);
            int i = (int)(at / slot);
            double into = at - i * slot;
            if (into >= ShowSeconds) return;
            var g = turns[i];
            // inside another overlay: that one first, for a moment, as it would come
            var p = Parent(g);
            var use = p != null && into < 0.6 ? p : g;
            var force = Force(use, into * 1000);
            // every other overlay stays away meanwhile (the lap's own setting pop-ups and lap summaries would come up with
            // it, two at once, which a real session hardly ever shows): their conditions false, their "take turns"
            // negations true
            foreach (var other in Groups)
                foreach (var c in other)
                {
                    if (force.ContainsKey(c) || c.StartsWith("ncalc:!(")) continue;
                    v.Set(c, false);
                    if (c.StartsWith("ncalc:") && !force.ContainsKey("ncalc:!(" + c.Substring(6) + ")")) v.Set("ncalc:!(" + c.Substring(6) + ")", true);
                }
            foreach (var kv in force) v.Set(kv.Key, kv.Value);
        }

        /// <summary>What shows now (for the settings page): the first element's name of the overlay whose turn it is.</summary>
        public int TurnCount => (turns ?? (turns = Turns())).Count;
    }
}
