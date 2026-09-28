using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Automatic fixes for an imported dash (`fxdash tune`), what every SimHub import needed by hand
    /// (docs/examples/lmgt3-mclaren-tune.py):
    /// 1. every value's Samples = its widest text over a demo lap (the importer copies SimHub's preview text, often "0",
    ///    so text that doesn't fit went unnoticed and the screen dropped digits);
    /// 2. fonts re-picked with those samples (the importer often lands on the wide "S" fonts: "59" shows as "5 9");
    /// 3. labels that fit no font: a small font and a box widened around its centre;
    /// 4. gear values: the tallest gear font the box takes;
    /// 5. value boxes that run into neighbouring text: trimmed back (they would flash together).
    /// Run fit-bands and verify after it.
    /// </summary>
    public static class DashTune
    {
        public static List<string> Run(DashDefinition d, double demoSeconds = 90)
        {
            var changes = new List<string>();
            string Name(DashElement e) => $"#{d.Elements.IndexOf(e)} {e.Name}";

            // 0. inside the usable area (SimHub elements may run past their dash's edge): shrunk if their text still fits,
            //    else moved in
            foreach (var e in d.Elements)
            {
                var before = Box(e);
                int fh = e.Type == "value" || e.Type == "label" ? DashRenderer.FontHeight(e.Font) : 0;
                if (e.X < Usable.Left) { e.W -= Usable.Left - e.X; e.X = Usable.Left; }
                if (e.Y < Usable.Top) { e.H -= Usable.Top - e.Y; e.Y = Usable.Top; }
                if (e.X + e.W > Usable.Right) e.W = Usable.Right - e.X;
                if (e.Y + e.H > Usable.Bottom) e.H = Usable.Bottom - e.Y;
                // too small now for its text: moved back in whole instead
                if ((fh > 0 && e.H < fh) || e.W < 4 || e.H < 2)
                {
                    e.W = Math.Min(before.Width, Usable.Width); e.H = Math.Min(before.Height, Usable.Height);
                    e.X = Math.Max(Usable.Left, Math.Min(before.X, Usable.Right - e.W));
                    e.Y = Math.Max(Usable.Top, Math.Min(before.Y, Usable.Bottom - e.H));
                }
                if (Box(e) != before) changes.Add($"{Name(e)}: {Str(before)} -> {Str(Box(e))}, inside the screen");
            }

            // 1. samples from a demo lap: the widest text each value shows, digits made 8s (the widest digit)
            var widest = new Dictionary<DashElement, string>();
            var demo = new UsbDemo(d);
            var values = d.Elements.Where(e => e.Type == "value").ToList();
            for (int k = 1; k <= demoSeconds * 10; k++)
            {
                var v = demo.Step(0.1);
                if (k % 2 != 0) continue;
                foreach (var e in values)
                {
                    string t = DashRenderer.Format(e, v, out _);
                    if (string.IsNullOrEmpty(t)) continue;
                    t = Eights(t);
                    if (!widest.TryGetValue(e, out var w) || Wider(e.Font, t, w)) widest[e] = t;
                }
            }
            foreach (var e in values)
            {
                // what the demo lap showed; SimHub's preview texts ("FL Temp", "Text") only when it showed nothing
                var all = new List<string>();
                if (widest.TryGetValue(e, out var w)) all.Add(w);
                else
                {
                    if (e.Samples != null) all.AddRange(e.Samples.Where(s => !string.IsNullOrEmpty(s)).Select(Eights));
                    if (!string.IsNullOrEmpty(e.PreviewText)) all.Add(Eights(e.PreviewText));
                }
                if (!string.IsNullOrEmpty(e.Empty)) all.Add(e.Empty);
                all = all.Distinct().ToList();
                if (all.Count == 0) continue;
                var best = all.OrderByDescending(s => Width(e.Font, s)).First();
                var old = e.Samples == null ? "" : string.Join(",", e.Samples);
                var now = all.OrderByDescending(s => Width(e.Font, s)).Take(3).ToArray();
                if (old != string.Join(",", now)) { e.Samples = now; changes.Add($"{Name(e)}: samples [{old}] -> [{string.Join(", ", now)}]"); }
            }

            // 2. + 4. fonts: gear values the tallest gear font; others re-picked when their text doesn't fit or the font is a wide one
            foreach (var e in values)
            {
                var texts = (e.Samples ?? new string[0]).Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (texts.Count == 0) continue;
                int h0 = Math.Max(8, DashRenderer.FontHeight(e.Font));
                bool gear = e.Format == "gear" || (e.Bind ?? "").IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0;
                if (gear)
                {
                    var g = new[] { "8", "N", "R" }.Concat(texts).Distinct().ToList();
                    int f = BestFont(e, g, e.H, preferNarrow: false, gearOnly: true);
                    if (f >= 0 && DashRenderer.FontHeight(f) > h0 && f != e.Font) { changes.Add($"{Name(e)}: gear font {e.Font} -> {f} ({DashRenderer.FontHeight(f)} px)"); e.Font = f; }
                    if (!string.IsNullOrEmpty(e.Empty) && DashRenderer.TextWidth(e.Font, e.Empty) < 0) e.Empty = "N";
                    continue;
                }
                bool fits = texts.All(t => Width(e.Font, t) >= 0 && Width(e.Font, t) + 4 <= e.W) && DashRenderer.FontHeight(e.Font) <= e.H;
                bool wide = Wide(e.Font);
                if (fits && !wide) continue;
                int nf = BestFont(e, texts, h0, preferNarrow: true, gearOnly: false);
                if (nf < 0 || DashRenderer.FontHeight(nf) < h0 * 0.7)
                {
                    // SimHub lets text run past its box; the screen doesn't: room from the free space beside it
                    int need = texts.Max(t => Width(PreferredFont(e, texts, h0), t)) + 6;
                    if (Widen(d, e, need, changes, Name(e)))
                        nf = BestFont(e, texts, h0, preferNarrow: true, gearOnly: false);
                }
                // still nothing near its height: the tallest font that fits at all (small beats cut off)
                if (nf < 0 && !(fits && !wide)) nf = BestFont(e, texts, e.H, preferNarrow: true, gearOnly: false);
                if (nf >= 0 && nf != e.Font) { changes.Add($"{Name(e)}: font {e.Font} ({h0} px{(wide ? ", wide" : "")}) -> {nf} ({DashRenderer.FontHeight(nf)} px)"); e.Font = nf; }
            }

            // 3. labels that don't fit: a font that does, else a small one and the box widened around its centre
            foreach (var e in d.Elements.Where(x => x.Type == "label" && !string.IsNullOrEmpty(x.Text)))
            {
                int w = Width(e.Font, e.Text);
                if (w >= 0 && w <= e.W && DashRenderer.FontHeight(e.Font) <= e.H) continue;
                int h0 = Math.Max(8, DashRenderer.FontHeight(e.Font));
                int nf = BestFont(e, new List<string> { e.Text }, h0, preferNarrow: true, gearOnly: false);
                if (nf >= 0) { changes.Add($"{Name(e)}: label font {e.Font} -> {nf}"); e.Font = nf; continue; }
                int small = DashFonts.FullAscii.Where(f => DashRenderer.FontHeight(f) <= e.H).OrderBy(f => Width(f, e.Text)).FirstOrDefault();
                if (Widen(d, e, Width(small, e.Text) + 6, changes, Name(e)))
                {
                    int f2 = BestFont(e, new List<string> { e.Text }, h0, preferNarrow: true, gearOnly: false);
                    if (f2 >= 0) small = f2;
                }
                changes.Add($"{Name(e)}: \"{e.Text}\" label font {e.Font} -> {small}");
                e.Font = small;
                // still too wide (the screen's small fonts are wide): the words shortened as a person would
                if (Width(e.Font, e.Text) > e.W)
                {
                    string old = e.Text, t = Shorten(e.Text, x => Width(e.Font, x) <= e.W);
                    if (t != old) { e.Text = t; changes.Add($"{Name(e)}: \"{old}\" -> \"{t}\" (no room for it on the screen)"); }
                }
            }

            // 6. gear values: as wide as the free space around them allows, then the tallest gear font that fits
            foreach (var g in values.Where(e => (e.Format == "gear" || (e.Bind ?? "").IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0) && (e.Visible == null || e.Visible.Count == 0)))
            {
                // in a panel of its own (a filled shape around it, nothing else in it): the gear takes the panel's inside
                var panel = d.Elements.Where(sh => (sh.Type == "rect" || sh.Type == "box" || sh.Type == "ellipse") && (sh.Visible == null || sh.Visible.Count == 0)
                                                   && Box(sh).Contains(Box(g)) && sh.W * sh.H < 4 * Math.Max(1, g.W * g.H) * 4)
                                      .OrderBy(sh => sh.W * sh.H).FirstOrDefault();
                if (panel != null && !d.Elements.Any(o => o != g && o != panel && (o.Type == "value" || o.Type == "label") && (o.Visible == null || o.Visible.Count == 0) && Box(o).IntersectsWith(Box(panel))))
                {
                    int inset = Math.Max(2, panel.Border + 2);
                    var inner = new Rectangle(panel.X + inset, panel.Y + inset, panel.W - 2 * inset, panel.H - 2 * inset);
                    if (inner.Width * inner.Height > g.W * g.H)
                    {
                        changes.Add($"{Name(g)}: gear box {Str(Box(g))} -> {Str(inner)}, the inside of {Name(panel)}");
                        g.X = inner.X; g.Y = inner.Y; g.W = inner.Width; g.H = inner.Height;
                        int pf = BestFont(g, new List<string> { "8", "N", "R" }, g.H, preferNarrow: false, gearOnly: true);
                        if (pf >= 0) g.Font = pf;
                        continue;
                    }
                }
                int left = 0, right = DashRenderer.Width - 10;
                foreach (var o in d.Elements)
                {
                    if (o == g || (o.Visible != null && o.Visible.Count > 0) || o.Y >= g.Y + g.H || o.Y + o.H <= g.Y) continue;
                    if (Box(o).IntersectsWith(Box(g))) continue; // already under/over it (frames, backgrounds)
                    if (o.X + o.W <= g.X) left = Math.Max(left, o.X + o.W + 2);
                    else if (o.X >= g.X + g.W) right = Math.Min(right, o.X - 2);
                }
                int cx = g.X + g.W / 2, half = Math.Min(cx - left, right - cx);
                int w = Math.Max(g.W, Math.Min(2 * half, (int)(g.H * 0.9)));
                var gtexts = new List<string> { "8", "N", "R" };
                int f = BestFont(new DashElement { X = cx - w / 2, Y = g.Y, W = w, H = g.H, Align = g.Align }, gtexts, g.H, preferNarrow: false, gearOnly: true);
                if (f >= 0 && DashRenderer.FontHeight(f) > DashRenderer.FontHeight(g.Font))
                {
                    changes.Add($"{Name(g)}: gear box {g.W} -> {w} px wide, font {g.Font} -> {f} ({DashRenderer.FontHeight(f)} px)");
                    g.X = cx - w / 2; g.W = w; g.Font = f;
                }
            }

            // 5. value text running into other text (their text rows overlap: they would redraw, and flash, together):
            //    the value's box shrinks from that side, a pixel at a time, until the rows clear; a smaller font if needed
            var others = d.Elements.Where(x => x.Type == "value" || x.Type == "label").ToList();
            foreach (var v in values)
            {
                if (InPopup(d, v)) continue; // a pop-up's own layout
                string vs = Widest(v);
                // obstacles: other text that is always there (where its text is), and anything that comes and goes over
                // this value (pop-up frames, their text: their whole box) unless it covers the value's text completely
                var obstacles = new List<(DashElement E, Func<Rectangle> R)>();
                foreach (var o in d.Elements)
                {
                    if (o == v) continue;
                    // shown under other conditions than this value: it comes and goes over it
                    bool popup = o.Visible != null && o.Visible.Count > 0 && !(v.Visible != null && o.Visible.SequenceEqual(v.Visible));
                    if (!popup && (o.Type == "value" || o.Type == "label")) { var oo = o; obstacles.Add((oo, () => Band(oo, Widest(oo)))); }
                    else if (popup && d.Elements.IndexOf(o) > d.Elements.IndexOf(v) && !Box(o).Contains(Band(v, vs))) { var oo = o; obstacles.Add((oo, () => Box(oo))); }
                }
                foreach (var (o, obox) in obstacles)
                {
                    var before = Box(v); int f0 = v.Font;
                    for (int step = 0; step < 40 && Band(v, vs).IntersectsWith(obox()); step++)
                    {
                        var vb = Band(v, vs); var ob = obox();
                        var ov = Rectangle.Intersect(vb, ob);
                        if (ov.Height > ov.Width * 2) break; // text beside text: widths, not heights (check/verify report it)
                        bool below = ob.Y + ob.Height / 2 > vb.Y + vb.Height / 2;
                        if (below) v.H -= 2; else { v.Y += 2; v.H -= 2; }
                        if (DashRenderer.FontHeight(v.Font) > v.H)
                        {
                            int nf = BestFont(v, (v.Samples ?? new[] { vs }).ToList(), v.H, preferNarrow: true, gearOnly: false);
                            if (nf < 0) break;
                            v.Font = nf;
                        }
                    }
                    // not cleared, or only by shrinking the text by more than a quarter (a gear or speed that a rare pop-up
                    // overlaps): left as it was; it flashes only while that pop-up shows
                    bool popupObstacle = o.Visible != null && o.Visible.Count > 0;
                    if (Band(v, vs).IntersectsWith(obox()) || (popupObstacle && DashRenderer.FontHeight(v.Font) < 0.75 * DashRenderer.FontHeight(f0)))
                    {
                        v.X = before.X; v.Y = before.Y; v.W = before.Width; v.H = before.Height; v.Font = f0;
                        if (popupObstacle) changes.Add($"{Name(v)}: left as it is; {Name(o)} (shown at times) overlaps it and it will flash while that shows");
                        continue;
                    }
                    if (Box(v) != before) changes.Add($"{Name(v)}: box {Str(before)} -> {Str(Box(v))}{(v.Font != f0 ? $", font {f0} -> {v.Font}" : "")}, its text clear of {Name(o)}");
                }
            }

            // 5b. a pop-up's background that partly covers a value always shown: while it shows, every change of the value
            //     redraws the pop-up over it (a flash). If nothing else of the pop-up is in the part over the value's text,
            //     that part of the background goes: the background is trimmed back to just clear of it.
            foreach (var v in values.Where(x => x.Visible == null || x.Visible.Count == 0 || !InPopup(d, x)))
            {
                string vs = Widest(v);
                var vb = Band(v, vs);
                foreach (var bg in d.Elements.Where(x => (x.Type == "rect" || x.Type == "box" || x.Type == "gradient" || x.Type == "image")
                                                         && x.Visible != null && x.Visible.Count > 0 && d.Elements.IndexOf(x) > d.Elements.IndexOf(v)).ToList())
                {
                    var bb = Box(bg);
                    if (!bb.IntersectsWith(vb) || bb.Contains(vb)) continue;
                    var members = d.Elements.Where(m => m != bg && m.Visible != null && m.Visible.SequenceEqual(bg.Visible)).ToList();
                    Rectangle trimmed = bb;
                    var ov = Rectangle.Intersect(bb, vb);
                    if (ov.Width >= ov.Height)
                    {
                        if (bb.Y > vb.Y) trimmed = Rectangle.FromLTRB(bb.Left, vb.Bottom + 2, bb.Right, bb.Bottom);   // cut its top
                        else trimmed = Rectangle.FromLTRB(bb.Left, bb.Top, bb.Right, vb.Top - 2);                     // cut its bottom
                    }
                    else
                    {
                        if (bb.X > vb.X) trimmed = Rectangle.FromLTRB(vb.Right + 2, bb.Top, bb.Right, bb.Bottom);
                        else trimmed = Rectangle.FromLTRB(bb.Left, bb.Top, vb.Left - 2, bb.Bottom);
                    }
                    if (trimmed.Width < 8 || trimmed.Height < 8) continue;
                    // the strip cut away must hold none of the pop-up's own content (its text, frames, pictures)
                    var cut = trimmed.Y > bb.Y ? Rectangle.FromLTRB(bb.Left, bb.Top, bb.Right, trimmed.Top)
                            : trimmed.Bottom < bb.Bottom ? Rectangle.FromLTRB(bb.Left, trimmed.Bottom, bb.Right, bb.Bottom)
                            : trimmed.X > bb.X ? Rectangle.FromLTRB(bb.Left, bb.Top, trimmed.Left, bb.Bottom)
                            : Rectangle.FromLTRB(trimmed.Right, bb.Top, bb.Right, bb.Bottom);
                    if (members.Any(m => (m.Type == "value" || m.Type == "label" ? Band(m, Widest(m)) : Box(m)).IntersectsWith(cut)))
                    {
                        // content there: a plain filled rect can instead get a notch around the value's text (the value
                        // then shows through it, drawn once, never under the pop-up); replaced by the rects around it
                        var hole = vb; hole.Inflate(2, 2); hole.Intersect(bb);
                        if (bg.Type != "rect" || members.Any(m => (m.Type == "value" || m.Type == "label" ? Band(m, Widest(m)) : Box(m)).IntersectsWith(hole))) continue;
                        var pieces = new List<Rectangle>();
                        if (hole.Top > bb.Top) pieces.Add(Rectangle.FromLTRB(bb.Left, bb.Top, bb.Right, hole.Top));
                        if (hole.Bottom < bb.Bottom) pieces.Add(Rectangle.FromLTRB(bb.Left, hole.Bottom, bb.Right, bb.Bottom));
                        if (hole.Left > bb.Left) pieces.Add(Rectangle.FromLTRB(bb.Left, hole.Top, hole.Left, hole.Bottom));
                        if (hole.Right < bb.Right) pieces.Add(Rectangle.FromLTRB(hole.Right, hole.Top, bb.Right, hole.Bottom));
                        string bgName = Name(bg); int at = d.Elements.IndexOf(bg);
                        d.Elements.RemoveAt(at);
                        for (int k = 0; k < pieces.Count; k++)
                        {
                            var copy = JsonClone(bg);
                            copy.Name = bg.Name + (k == 0 ? "" : " " + (k + 1));
                            copy.X = pieces[k].X; copy.Y = pieces[k].Y; copy.W = pieces[k].Width; copy.H = pieces[k].Height;
                            d.Elements.Insert(at + k, copy);
                        }
                        changes.Add($"{bgName}: a notch around {Name(v)}'s text ({pieces.Count} parts), so the pop-up never covers half of it");
                        continue;
                    }
                    changes.Add($"{Name(bg)}: {Str(bb)} -> {Str(trimmed)}, clear of {Name(v)} (a pop-up half over it flashes)");
                    bg.X = trimmed.X; bg.Y = trimmed.Y; bg.W = trimmed.Width; bg.H = trimmed.Height;
                }
            }

            // 7. pictures that come and go often (shown while ABS/TC works, blinking): every toggle redraws the picture.
            //    A short-named one becomes a label in its main colour, others a lamp (a rect) in that colour.
            var toggles = new Dictionary<DashElement, int>();
            var imgs = d.Elements.Where(e => e.Type == "image" && e.Visible != null && e.Visible.Count > 0).ToList();
            if (imgs.Count > 0)
            {
                var demo2 = new UsbDemo(d);
                var last = new Dictionary<DashElement, bool>();
                for (int k = 1; k <= demoSeconds * 10; k++)
                {
                    var v = demo2.Step(0.1);
                    foreach (var e in imgs)
                    {
                        bool vis = e.Visible.All(c => v.Truthy(c) ?? (e.PreviewVisible ?? true));
                        if (last.TryGetValue(e, out var was) && was != vis) toggles[e] = (toggles.TryGetValue(e, out var n) ? n : 0) + 1;
                        last[e] = vis;
                    }
                }
            }
            // every toggle redraws the picture (or what was under it): costly when it toggles often or is big
            foreach (var kv in toggles.Where(kv => kv.Value >= demoSeconds / 10 || (kv.Value >= 2 && kv.Key.W * kv.Key.H >= 12000)))
            {
                var e = kv.Key;
                var colour = MainColour(d, e) ?? "#FFA000";
                string word = new string((e.Name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                if (word.Length >= 2 && word.Length <= 5)
                {
                    int f = BestFont(e, new List<string> { word }, e.H, preferNarrow: true, gearOnly: false);
                    if (f >= 0)
                    {
                        e.Type = "label"; e.Text = word; e.Color = colour; e.Align = "center"; e.Font = f; e.Image = null;
                        changes.Add($"{Name(e)}: picture shown/hidden {kv.Value}x in {demoSeconds:0} s -> label \"{word}\" in {colour}");
                        continue;
                    }
                }
                int side2 = Math.Min(e.W, e.H) * 2 / 3;
                e.Type = "rect"; e.Color = colour; e.Image = null;
                e.X += (e.W - side2) / 2; e.Y += (e.H - side2) / 2; e.W = side2; e.H = side2;
                changes.Add($"{Name(e)}: picture shown/hidden {kv.Value}x in {demoSeconds:0} s -> a {colour} lamp");
            }
            return changes;
        }

        private static Rectangle Box(DashElement e) => new Rectangle(e.X, e.Y, e.W, e.H);

        private static DashElement JsonClone(DashElement e) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<DashElement>(Newtonsoft.Json.JsonConvert.SerializeObject(e));

        /// <summary>
        /// Part of a pop-up: shown under a condition, inside a shape shown under the same one (a value that only shows
        /// while the engine runs is not).
        /// </summary>
        private static bool InPopup(DashDefinition d, DashElement e) =>
            e.Visible != null && e.Visible.Count > 0 &&
            d.Elements.Any(s => s != e && (s.Type == "rect" || s.Type == "box" || s.Type == "ellipse" || s.Type == "gradient" || s.Type == "image")
                                && s.Visible != null && s.Visible.SequenceEqual(e.Visible) && Box(s).Contains(Box(e)));

        private static readonly (string Long, string Short)[] Abbreviations =
        {
            ("PERSONAL", "PB"), ("OVERALL", "OVR"), ("SESSION", "SESS"), ("TEMPERATURE", "TEMP"), ("TEMP", "T"),
            ("ENERGY", "NRG"), ("LEVEL", "LVL"), ("BRAKE", "BRK"), ("PRESSURE", "PRESS"), ("PRESS", "PR"), ("FRONT", "F"),
            ("REAR", "R"), ("LAST", "LST"), ("BEST", "BST"), ("COLLECTOR", "COLL"), ("CURRENT", "CUR"), ("REMAINING", "REM"),
            ("PREDICTED", "PRED"), ("LAPTIME", "LAP"), ("LAP TIME", "LAP"), ("DIFFERENCE", "DIFF"), ("POSITION", "POS"),
            ("FUEL", "FL"), ("REGEN", "RGN"), ("METER", "MTR"), ("BIAS", "BB"), ("EFFORT", "EFF"), ("TRACK", "TRK"),
            ("AIR", "A"), ("WATER", "WAT"), ("OIL", "OIL"), ("DISABLED", "OFF"), ("FLAGS", "FLG"),
        };

        /// <summary>
        /// A label text shortened until `fits`: runs of spaces squeezed, then common abbreviations (whole words, longest
        /// first), then spaces dropped, then cut.
        /// </summary>
        private static string Shorten(string text, Func<string, bool> fits)
        {
            string t = System.Text.RegularExpressions.Regex.Replace(text, " {2,}", "  ");
            if (fits(t)) return t;
            t = System.Text.RegularExpressions.Regex.Replace(t, " {2,}", " ");
            if (fits(t)) return t;
            foreach (var (l, sh) in Abbreviations.OrderByDescending(a => a.Long.Length))
            {
                var r = System.Text.RegularExpressions.Regex.Replace(t, @"" + System.Text.RegularExpressions.Regex.Escape(l) + @"", sh,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (r != t) { t = r; if (fits(t)) return t; }
            }
            t = t.Replace(" ", "");
            while (t.Length > 1 && !fits(t)) t = t.Substring(0, t.Length - 1);
            return t;
        }

        /// <summary>The area the dash may use: the screen less the wheel's default padding (10 px left, 20 px top).</summary>
        private static readonly Rectangle Usable = new Rectangle(0, 0, DashRenderer.Width - 10, DashRenderer.Height - 20);

        private static int PreferredFont(DashElement e, List<string> texts, int h0)
        {
            // the narrowest full-ASCII font near the height the element had, to size the room it needs
            var c = DashFonts.FullAscii.Where(f => DashRenderer.FontHeight(f) <= Math.Max(8, h0) && DashRenderer.FontHeight(f) >= h0 * 0.7 && texts.All(t => Width(f, t) >= 0))
                .OrderBy(f => texts.Max(t => Width(f, t))).ToList();
            return c.Count > 0 ? c[0] : e.Font;
        }

        /// <summary>
        /// Widens an element's box to `need` px into the free space beside it (growing from the side its text is anchored
        /// to: both sides for centred text), without running into other elements shown with it or off the usable area.
        /// True if it grew.
        /// </summary>
        private static bool Widen(DashDefinition d, DashElement e, int need, List<string> changes, string name)
        {
            if (need <= e.W) return false;
            int left = Usable.Left, right = Usable.Right;
            var own = Band(e, Widest(e));
            foreach (var o in d.Elements)
            {
                if (o == e || (o.Type != "value" && o.Type != "label" && o.Type != "bar")) continue; // shapes: under it
                bool together = (o.Visible == null || o.Visible.Count == 0) || (e.Visible != null && o.Visible != null && o.Visible.SequenceEqual(e.Visible));
                if (!together) continue;
                // where the neighbour's text really is (its box is often much wider), a bar its whole box
                var ob = o.Type == "bar" ? Box(o) : Band(o, Widest(o));
                if (ob.Y >= own.Y + own.Height || ob.Y + ob.Height <= own.Y) continue;
                if (ob.X + ob.Width <= own.X) left = Math.Max(left, ob.X + ob.Width + 2);
                else if (ob.X >= own.X + own.Width) right = Math.Min(right, ob.X - 2);
            }
            int grow = need - e.W, x0 = e.X, w0 = e.W;
            if (e.Align == "center")
            {
                int g = Math.Min(grow / 2 + 1, Math.Min(e.X - left, right - (e.X + e.W)));
                if (g <= 0) return false;
                e.X -= g; e.W += 2 * g;
            }
            else if (e.Align == "right") { int g = Math.Min(grow, e.X - left); if (g <= 0) return false; e.X -= g; e.W += g; }
            else { int g = Math.Min(grow, right - (e.X + e.W)); if (g <= 0) return false; e.W += g; }
            changes.Add($"{name}: box widened {w0} -> {e.W} px into the free space beside it");
            return true;
        }

        private static string Widest(DashElement e) =>
            e.Type == "label" ? e.Text ?? "" : (e.Samples ?? new string[0]).Concat(new[] { e.PreviewText ?? "" }).OrderByDescending(t => Width(e.Font, t)).FirstOrDefault() ?? "";

        /// <summary>The rows and width a text is drawn in (the renderer's band, with a pixel of room).</summary>
        private static Rectangle Band(DashElement e, string text)
        {
            var b = DashRenderer.TextBand(e, e.Font, text, 1);
            b.Inflate(0, 1);
            return b;
        }

        /// <summary>The most common colour of an image element's picture that isn't near black, as #RRGGBB.</summary>
        private static string MainColour(DashDefinition d, DashElement e)
        {
            try
            {
                if (e.Image == null || d.Images == null || !d.Images.TryGetValue(e.Image, out var b64)) return null;
                using (var ms = new System.IO.MemoryStream(Convert.FromBase64String(b64)))
                using (var bmp = new Bitmap(ms))
                {
                    var counts = new Dictionary<int, int>();
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        {
                            var c = bmp.GetPixel(x, y);
                            if (c.A < 128 || c.R + c.G + c.B < 120) continue;
                            int k = (c.R >> 4 << 8) | (c.G >> 4 << 4) | (c.B >> 4);
                            counts[k] = (counts.TryGetValue(k, out var n) ? n : 0) + 1;
                        }
                    if (counts.Count == 0) return null;
                    int top = counts.OrderByDescending(kv => kv.Value).First().Key;
                    return $"#{((top >> 8) & 15) * 17:X2}{((top >> 4) & 15) * 17:X2}{(top & 15) * 17:X2}";
                }
            }
            catch { return null; }
        }
        private static string Str(Rectangle r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";
        private static int Width(int font, string t) => DashRenderer.TextWidth(font, t ?? "");
        private static bool Wider(int font, string a, string b) => Width(font, a) > Width(font, b) || (Width(font, a) == Width(font, b) && a.Length > b.Length);
        private static string Eights(string t) => new string((t ?? "").Select(c => char.IsDigit(c) ? '8' : c).ToArray());

        /// <summary>Fonts whose digits are as wide as the font is tall (Simagic's "S" fonts): letter-spaced, and wide.</summary>
        private static bool Wide(int font)
        {
            int h = DashRenderer.FontHeight(font), dw = Width(font, "8");
            return h > 0 && dw >= 0.85 * h;
        }

        /// <summary>
        /// The tallest font (up to `height` x 1.15 and the box) in which every text fits the box with a little room;
        /// preferring narrow fonts, then ordinary proportions. -1 if none.
        /// </summary>
        private static int BestFont(DashElement e, List<string> texts, int height, bool preferNarrow, bool gearOnly)
        {
            var cands = Enumerable.Range(0, FontMetrics.Fonts.Length)
                .Where(f => DashRenderer.FontHeight(f) > 0 && DashRenderer.FontHeight(f) <= Math.Max(8, height * 1.15) && DashRenderer.FontHeight(f) <= e.H)
                .Where(f => texts.All(t => Width(f, t) >= 0 && Width(f, t) + 4 <= e.W))
                .Where(f => gearOnly || DashFonts.FullAscii.Contains(f) || texts.All(t => t.All(char.IsDigit)))
                .ToList();
            if (cands.Count == 0) return -1;
            if (preferNarrow && cands.Any(f => !Wide(f))) cands = cands.Where(f => !Wide(f)).ToList();
            int top = cands.Max(DashRenderer.FontHeight);
            return cands.Where(f => DashRenderer.FontHeight(f) == top)
                        .OrderBy(f => Math.Abs(Width(f, "8") / (double)top - 0.5)).First();
        }
    }
}
