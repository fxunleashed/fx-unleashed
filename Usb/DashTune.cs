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
            var demo = new UsbDemo(d) { BudgetMs = null };
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
                // (its centre in the panel; a caption at the panel's top or bottom only takes those rows)
                var gc = new Point(g.X + g.W / 2, g.Y + g.H / 2);
                var panel = d.Elements.Where(sh => (sh.Type == "rect" || sh.Type == "box" || sh.Type == "ellipse") && (sh.Visible == null || sh.Visible.Count == 0)
                                                   && Box(sh).Contains(gc) && sh.W * sh.H < 16 * Math.Max(1, g.W * g.H) && sh.W >= 40 && sh.H >= 40)
                                      .OrderBy(sh => sh.W * sh.H).FirstOrDefault();
                var inPanel = panel == null ? new List<DashElement>() :
                    d.Elements.Where(o => o != g && o != panel && (o.Type == "value" || o.Type == "label") && (o.Visible == null || o.Visible.Count == 0)
                                          && Box(o).IntersectsWith(Box(panel))).ToList();
                // captions: labels in the panel's top or bottom quarter; any other text in it and the panel isn't the gear's
                bool Caption(DashElement o) => o.Type == "label" && Box(panel).Contains(new Point(o.X + o.W / 2, o.Y + o.H / 2))
                                               && (o.Y + o.H <= panel.Y + panel.H / 4 + 8 || o.Y >= panel.Y + panel.H * 3 / 4 - 8);
                if (panel != null && inPanel.All(Caption))
                {
                    int inset = Math.Max(2, panel.Border + 2);
                    var inner = new Rectangle(panel.X + inset, panel.Y + inset, panel.W - 2 * inset, panel.H - 2 * inset);
                    foreach (var c in inPanel)
                    {
                        var cb = Covers(c);
                        if (cb.Y < inner.Y + inner.Height / 2) inner = Rectangle.FromLTRB(inner.Left, Math.Max(inner.Top, cb.Bottom + 2), inner.Right, inner.Bottom);
                        else inner = Rectangle.FromLTRB(inner.Left, inner.Top, inner.Right, Math.Min(inner.Bottom, cb.Top - 2));
                    }
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
                    if (o == v || DashPages.Apart(o, v)) continue; // (on another page: never shown together)
                    // shown under other conditions than this value: it comes and goes over it
                    // (a pop-up with formula conditions is dealt with later: the value hides while it shows)
                    bool popup = o.Visible != null && o.Visible.Count > 0 && !(v.Visible != null && o.Visible.SequenceEqual(v.Visible));
                    if (!popup && (o.Type == "value" || o.Type == "label")) { var oo = o; obstacles.Add((oo, () => Band(oo, Widest(oo)))); }
                    else if (popup && d.Elements.IndexOf(o) > d.Elements.IndexOf(v) && !Box(o).Contains(Band(v, vs)) && !o.Visible.All(c => c.StartsWith("ncalc:"))) { var oo = o; obstacles.Add((oo, () => Box(oo))); }
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
                            int nf = BestFont(v, Texts(v).DefaultIfEmpty(vs).ToList(), v.H, preferNarrow: true, gearOnly: false);
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
                    if (!bb.IntersectsWith(vb) || bb.Contains(vb) || DashPages.Apart(bg, v)) continue;
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
                var demo2 = new UsbDemo(d) { BudgetMs = null };
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

            // 8. what check still calls an overlap (a value's whole box, its background redrawn, running into other text
            //    shown with it): the value's box trimmed from the side that clears it with the least loss, keeping its
            //    widest text; a smaller font if no trim keeps it; a label moved out of the way as the last resort
            MergeColourTwins(d, changes, Name);
            SplitStackedBars(d, changes, Name);
            FlattenGradients(d, changes, Name);
            SwapOverlaid(d, changes, Name);
            MergeTurnTwins(d, changes, Name);
            GrowTags(d, changes, Name);
            OffFrames(d, changes, Name);
            FitText(d, changes, Name);
            ClearOverlaps(d, changes, Name);
            OffPopups(d, changes, Name);
            OffLabels(d, changes, Name);
            OffFrames(d, changes, Name); // again: fonts and boxes have settled since

            // 9. values on a busy background (a picture or gradient under them): each change redraws all those fills.
            //    They get a plain Background: the colour most of the area under them has.
            using (var p = new PreviewScreen())
            {
                var r = new DashRenderer(p, d, 0, 0);
                foreach (var v in d.Elements.Where(x => x.Type == "value" && x.Background == null))
                {
                    int fills = r.StaticFillsIn(Box(v));
                    if (fills <= 80) continue;
                    var c = r.CommonStaticColour(Box(v));
                    if (c == null) continue;
                    var col = DashRenderer.ToColor(c.Value);
                    v.Background = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
                    changes.Add($"{Name(v)}: {fills} fills redrawn per change -> Background {v.Background} (the colour under most of it)");
                }
            }
            return changes;
        }

        private static Rectangle Box(DashElement e) => new Rectangle(e.X, e.Y, e.W, e.H);

        /// <summary>What check counts as a text's area: a label's text only (it has no background), a value's whole box.</summary>
        private static Rectangle Covers(DashElement e)
        {
            if (e.Type != "label") return Box(e);
            int w = Math.Max(0, Width(e.Font, DashRenderer.Clean(e.Text)));
            int x = e.Align == "center" ? e.X + (e.W - w) / 2 : e.Align == "right" ? e.X + e.W - w : e.X;
            int fh = DashRenderer.FontHeight(e.Font);
            return fh > 0 && fh <= e.H ? new Rectangle(x, e.Y + (e.H - fh) / 2, w, fh) : new Rectangle(x, e.Y, w, e.H);
        }

        private static List<string> Texts(DashElement e) =>
            (e.Type == "label" ? new[] { e.Text ?? "" } : (e.Samples ?? new string[0]).Concat(new[] { e.Empty, e.PreviewText }))
            .Where(t => !string.IsNullOrEmpty(t)).Select(DashRenderer.Clean).ToList();

        /// <summary>The widest text an element must fit, as check measures it (samples, Empty, PreviewText).</summary>
        private static int NeedWidth(DashElement e, int font) => Texts(e).Select(t => Width(font, t)).DefaultIfEmpty(0).Max();

        /// <summary>Always shown, or always on its page (a page is no condition of its own: it shows whenever its page does).</summary>
        private static bool OnItsPage(DashElement x) => x.Visible == null || x.Visible.Count == 0 || x.Visible.All(c => DashPages.Parse(c).HasValue);

        private static bool Clash(Rectangle a, Rectangle b) { var o = Rectangle.Intersect(a, b); return o.Width > 2 && o.Height > 2; }

        /// <summary>
        /// The same value drawn twice in the same place in two colours, each under its own condition (SimHub's way of
        /// colouring a delta): both can show at once and redraw over each other. One value instead, shown under either
        /// condition, with a ColorBind that picks the later one's colour when its condition holds.
        /// </summary>
        private static void MergeColourTwins(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            for (int i = 0; i < d.Elements.Count; i++)
                for (int j = i + 1; j < d.Elements.Count; j++)
                {
                    var a = d.Elements[i]; var b = d.Elements[j];
                    if (a.Type != "value" || b.Type != "value" || a.Bind != b.Bind || Box(a) != Box(b) || a.Font != b.Font) continue;
                    if (a.Visible == null || b.Visible == null || a.Visible.Count != 1 || b.Visible.Count != 1) continue;
                    if (!string.IsNullOrEmpty(a.ColorBind) || !string.IsNullOrEmpty(b.ColorBind) || a.Visible[0] == b.Visible[0]) continue;
                    if (InPopup(d, a) || InPopup(d, b)) continue; // each on its own pop-up background: they never show together
                    string Expr(string c) => c.StartsWith("ncalc:") ? c.Substring(6) : "[" + c + "]";
                    string ca = Expr(a.Visible[0]), cb = Expr(b.Visible[0]);
                    string an = name(a), bn = name(b);
                    a.ColorBind = $"ncalc:if({cb}, '{b.Color ?? "#FFFFFF"}', '{a.Color ?? "#FFFFFF"}')";
                    a.Visible = new List<string> { $"ncalc:({ca}) or ({cb})" };
                    a.Samples = (a.Samples ?? new string[0]).Concat(b.Samples ?? new string[0]).Distinct().ToArray();
                    // drawn where the later one was (over anything between them)
                    d.Elements[j] = a; d.Elements.RemoveAt(i); i--; break;
                    changes.Add($"{an} + {bn}: one value coloured by condition (both showed at once, over each other)");
                }
        }

        /// <summary>
        /// A plain tag behind a value (a filled rect made for it, e.g. the session name on a green tag) narrower or
        /// lower than the value's widest text: the text runs off it onto what's around. The tag grows to hold the text
        /// with a pixel to spare, if that runs into no other text.
        /// </summary>
        /// <summary>
        /// A value shown only at times (clutch while the revs are low) drawn over a value always shown (speed), with no
        /// pop-up box of its own: both keep updating and redraw each other. The one always shown gets the opposite
        /// condition, so they take turns instead.
        /// </summary>
        /// <summary>
        /// Just the "take turns" step (the designer's Fix for overlays): values and bars half under an overlay's box hide
        /// while it shows, instead of redrawing the box at every change. Returns what changed.
        /// </summary>
        public static List<string> TakeTurns(DashDefinition d)
        {
            var changes = new List<string>();
            SwapOverlaid(d, changes, e => $"#{d.Elements.IndexOf(e)} {e.Name}");
            return changes;
        }

        private static void SwapOverlaid(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            // what comes and goes over other things: a value of its own, or a pop-up's opaque shape
            // (pages are no condition of their own here: an element on a page is shown on its own terms there)
            List<string> Own(DashElement x) => x.Visible.Where(c => !OverlayShowcase.IsTurn(c) && !DashPages.Parse(c).HasValue).ToList();
            bool Opaque(DashElement x) => x.Type == "rect" || ((x.Type == "box" || x.Type == "ellipse") && x.Fill != null);
            foreach (var top in d.Elements.Where(x => x.Visible != null && x.Visible.Count >= 1 &&
                                                      ((x.Type == "value" && !InPopup(d, x)) || (Opaque(x) && x.Opacity >= 100))).ToList())
            {
                // all its conditions (they must all hold) as one formula
                var conds = top.Visible.Distinct().ToList();
                if (!conds.All(c => c.StartsWith("ncalc:"))) continue;
                string cond = conds.Count == 1 ? conds[0] : "ncalc:" + string.Join(" and ", conds.Select(c => "(" + c.Substring(6) + ")"));
                string not = "ncalc:!(" + cond.Substring(6) + ")";
                int ti = d.Elements.IndexOf(top);
                // values shown on their own terms (always, or under conditions of their own), not part of this pop-up
                // (bars and delta bars too: one half under a pop-up box redraws the box at every change)
                foreach (var under in d.Elements.Take(ti).Where(x => (x.Type == "value" || (top.Type != "value" && (x.Type == "bar" || x.Type == "deltabar"))) &&
                                                                      (x.Visible == null || x.Visible.Count == 0 || Own(x).Count == 0 ||
                                                                       // shown whenever the pop-up is (its conditions a part of the pop-up's): always under it
                                                                       // (the take-turns conditions added here don't count)
                                                                       (Own(x).Count < top.Visible.Distinct().Count() && Own(x).All(c => top.Visible.Contains(c)) && !InPopup(d, x)) ||
                                                                       Own(x).Count == 0)))
                {
                    // never shown together: on another page than the pop-up
                    if (DashPages.Apart(under, top)) continue;
                    // where the value's text is (its box is often far bigger); a bar's whole box
                    var band = under.Type == "value" ? Band(under, Widest(under)) : Box(under);
                    var o = Rectangle.Intersect(top.Type == "value" ? Band(top, Widest(top)) : Box(top), band);
                    if (o.Width <= 2 || o.Height <= 2) continue;
                    // a shape over all of the value's text hides it anyway (the renderer skips it while covered)
                    if (top.Type != "value" && Box(top).Contains(band)) continue;
                    if (under.Visible != null && under.Visible.Contains(not)) continue;
                    under.Visible = (under.Visible ?? new List<string>()).Concat(new[] { not }).ToList();
                    changes.Add($"{name(under)}: hidden while {name(top)} shows over it ({cond}), so they take turns");
                }
            }
        }

        /// <summary>
        /// A value box poking a few px into a neighbouring frame (its centre outside it): the frame's edge runs through
        /// the text's band, and every change draws the text then puts the line back. The box is cut off at the edge
        /// when its font still fits.
        /// </summary>
        private static void OffFrames(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var v in d.Elements.Where(x => x.Type == "value").ToList())
            {
                int fh = DashRenderer.FontHeight(v.Font);
                foreach (var f in d.Elements.Where(x => x != v && (x.Type == "box" || x.Type == "rect") && OnItsPage(x) && !DashPages.Apart(x, v)))
                {
                    var vb = Box(v); var fb = Box(f);
                    var o = Rectangle.Intersect(vb, fb);
                    if (o.Width <= 0 || o.Height <= 0) continue;
                    if (fb.Contains(new Point(vb.X + vb.Width / 2, vb.Y + vb.Height / 2))) continue; // it sits in that frame
                    Rectangle cut = vb;
                    if (o.Width >= o.Height && fb.Top > vb.Top && fb.Top < vb.Bottom) cut = Rectangle.FromLTRB(vb.Left, vb.Top, vb.Right, fb.Top - 1);
                    else if (o.Width >= o.Height && fb.Bottom > vb.Top && fb.Bottom < vb.Bottom) cut = Rectangle.FromLTRB(vb.Left, fb.Bottom + 1, vb.Right, vb.Bottom);
                    else if (o.Height > o.Width && fb.Left > vb.Left && fb.Left < vb.Right) cut = Rectangle.FromLTRB(vb.Left, vb.Top, fb.Left - 1, vb.Bottom);
                    else if (o.Height > o.Width && fb.Right > vb.Left && fb.Right < vb.Right) cut = Rectangle.FromLTRB(fb.Right + 1, vb.Top, vb.Right, vb.Bottom);
                    if (cut == vb || cut.Height < fh || cut.Width < NeedWidth(v, v.Font) + 2) continue;
                    // only a sliver: a big overlap is a layout choice, not a stray edge
                    if (vb.Height - cut.Height > Math.Max(12, vb.Height / 5) || vb.Width - cut.Width > Math.Max(12, vb.Width / 5)) continue;
                    changes.Add($"{name(v)}: box {Str(vb)} -> {Str(cut)}, off {name(f)}'s edge");
                    v.X = cut.X; v.Y = cut.Y; v.W = cut.Width; v.H = cut.Height;
                }
                // a frame around it (always there or at times, drawn after it or before) whose border runs through its
                // box: the box brought inside the border
                foreach (var f in d.Elements.Where(x => x != v && x.Type == "box" && x.Border > 0))
                {
                    var vb = Box(v); var fb = Box(f);
                    if (!fb.Contains(new Point(vb.X + vb.Width / 2, vb.Y + vb.Height / 2))) continue;
                    var inner = Rectangle.Inflate(fb, -(f.Border + 1), -(f.Border + 1));
                    if (inner.Contains(vb)) continue;
                    var cut = Rectangle.Intersect(vb, inner);
                    if (cut.Height < fh || cut.Width < NeedWidth(v, v.Font) + 2) continue;
                    if (vb.Height - cut.Height > Math.Max(12, vb.Height / 5) || vb.Width - cut.Width > Math.Max(12, vb.Width / 5)) continue;
                    changes.Add($"{name(v)}: box {Str(vb)} -> {Str(cut)}, inside {name(f)}'s border");
                    v.X = cut.X; v.Y = cut.Y; v.W = cut.Width; v.H = cut.Height;
                }
            }
        }

        /// <summary>
        /// Text still taller or wider than its box (what check calls an error): the box grows into free space around it
        /// (never into other text), else a smaller font (no less than 3/4 of the height), else a label is shortened.
        /// </summary>
        private static void FitText(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            // room to grow: no other text newly under it (where a value's text is: its box gets trimmed after)
            bool Free(DashElement e, Rectangle r) =>
                Usable.Contains(r) && !d.Elements.Any(o => o != e && (o.Type == "value" || o.Type == "label")
                                                       && Clash(r, o.Type == "label" ? Covers(o) : Band(o, Widest(o)))
                                                       && !Clash(Box(e), o.Type == "label" ? Covers(o) : Band(o, Widest(o))));
            foreach (var e in d.Elements.Where(x => x.Type == "value" || x.Type == "label").ToList())
            {
                var texts = Texts(e);
                if (texts.Count == 0) continue;
                var before = Box(e); int f0 = e.Font;
                int fh = DashRenderer.FontHeight(e.Font);
                // too low: grow, centred, by what's missing
                if (fh > e.H)
                {
                    int need = fh - e.H;
                    var r = new Rectangle(e.X, e.Y - need / 2, e.W, e.H + need);
                    if (!Free(e, r)) r = new Rectangle(e.X, e.Y, e.W, e.H + need);
                    if (!Free(e, r)) r = new Rectangle(e.X, e.Y - need, e.W, e.H + need);
                    if (Free(e, r)) { e.Y = r.Y; e.H = r.Height; }
                    else
                    {
                        int nf = BestFont(e, texts, e.H, preferNarrow: true, gearOnly: false);
                        if (nf >= 0 && DashRenderer.FontHeight(nf) >= 0.75 * fh) e.Font = nf;
                    }
                }
                // too narrow: grow from the side its text is anchored to (both for centred), else a smaller font
                int w = NeedWidth(e, e.Font);
                if (w > e.W)
                {
                    int need = w - e.W + 2;
                    var tries = e.Align == "center"
                        ? new[] { new Rectangle(e.X - (need + 1) / 2, e.Y, e.W + need + 1, e.H) }
                        : e.Align == "right"
                            ? new[] { new Rectangle(e.X - need, e.Y, e.W + need, e.H), new Rectangle(e.X, e.Y, e.W + need, e.H) }
                            : new[] { new Rectangle(e.X, e.Y, e.W + need, e.H), new Rectangle(e.X - need, e.Y, e.W + need, e.H) };
                    var ok = tries.FirstOrDefault(r => Free(e, r));
                    if (ok != Rectangle.Empty) { e.X = ok.X; e.W = ok.Width; }
                    else
                    {
                        int nf = BestFont(e, texts, e.H, preferNarrow: true, gearOnly: false);
                        if (nf >= 0 && DashRenderer.FontHeight(nf) >= 0.75 * DashRenderer.FontHeight(f0)) e.Font = nf;
                        else if (e.Type == "label")
                        {
                            string t = Shorten(e.Text, x => Width(e.Font, x) >= 0 && Width(e.Font, x) <= e.W);
                            if (t.Length > 0 && t != e.Text) { changes.Add($"{name(e)}: \"{e.Text}\" -> \"{t}\" to fit"); e.Text = t; }
                        }
                    }
                }
                if (Box(e) != before || e.Font != f0)
                    changes.Add($"{name(e)}: box {Str(before)} -> {Str(Box(e))}{(e.Font != f0 ? $", font {f0} -> {e.Font}" : "")}, so its text fits");
            }
        }

        /// <summary>
        /// A value box reaching under a pop-up's shape (a flag box beside the gear) while its text is clear of it: the
        /// room the renderer leaves beside the text reaches the pop-up, so each change redraws the pop-up over it while
        /// it shows. The box is trimmed off the shape, when its text still fits.
        /// </summary>
        private static void OffPopups(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var v in d.Elements.Where(x => x.Type == "value").ToList())
            {
                int vi = d.Elements.IndexOf(v);
                // there whenever the value is: always, or under the very same conditions
                bool Always(DashElement x) => x.Visible == null || x.Visible.Count == 0 || (v.Visible != null && x.Visible.SequenceEqual(v.Visible));
                // pop-up shapes, and opaque shapes always there that are drawn over it (a panel's background covering
                // the bottom of its digits: every change redraws the panel over them)
                foreach (var sh in d.Elements.Skip(vi + 1).Where(x => (x.Type == "rect" || x.Type == "box" || x.Type == "ellipse" || x.Type == "image")
                                                                     && ((x.Visible != null && x.Visible.Count > 0 && !Always(x))
                                                                         || (Always(x) && (x.Type == "rect" || (x.Type == "box" && x.Fill != null)) && x.Opacity >= 100))).ToList())
                {
                    var sb = Box(sh);
                    if (!Box(v).IntersectsWith(sb) || sb.Contains(Band(v, Widest(v)))) continue; // (a shape over all of its text hides it anyway)
                    if (DashPages.Apart(sh, v)) continue; // on another page: never shown together
                    // hidden while that shape shows (they take turns): never on the screen together
                    if (v.Visible != null && sh.Visible != null && sh.Visible.Count > 0 && sh.Visible.All(c => c.StartsWith("ncalc:")))
                    {
                        var conds = sh.Visible.Distinct().ToList();
                        string cond = conds.Count == 1 ? conds[0].Substring(6) : string.Join(" and ", conds.Select(c => "(" + c.Substring(6) + ")"));
                        if (v.Visible.Contains("ncalc:!(" + cond + ")")) continue;
                    }
                    Trim(v, sb, changes, name(v), name(sh), slack: 0, minRatio: Always(sh) ? 0.0 : 0.75);
                }
            }
        }

        /// <summary>
        /// A gradient under a value's text: the text can't be redrawn on it in one step (every change wipes and redraws
        /// the gradient there, a flash). The gradient becomes a flat fill in its middle colour.
        /// </summary>
        private static void FlattenGradients(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var g in d.Elements.Where(x => x.Type == "gradient" && x.Colors != null && x.Colors.Count > 1).ToList())
            {
                int gi = d.Elements.IndexOf(g);
                bool under = d.Elements.Skip(gi + 1).Any(v => v.Type == "value" && Box(g).IntersectsWith(Band(v, Widest(v))));
                if (!under) continue;
                var a = DashColors.Parse(g.Colors[0], Color.Black);
                var b = DashColors.Parse(g.Colors[g.Colors.Count - 1], Color.Black);
                var mid = g.Colors.Count % 2 == 1 ? DashColors.Parse(g.Colors[g.Colors.Count / 2], Color.Black)
                                                  : Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
                g.Type = "rect"; g.Color = $"#{mid.R:X2}{mid.G:X2}{mid.B:X2}"; g.Colors = null;
                changes.Add($"{name(g)}: gradient -> flat {g.Color}, as a value's text is drawn on it");
            }
        }

        /// <summary>
        /// The same value twice in the same place, one shown under a condition (red while the energy is low) and the
        /// other hidden then (they take turns): one value, coloured by that condition, drawn once.
        /// </summary>
        private static void MergeTurnTwins(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var b in d.Elements.Where(x => x.Type == "value" && x.Visible != null && x.Visible.Count == 1 && x.Visible[0].StartsWith("ncalc:")).ToList())
            {
                if (!d.Elements.Contains(b)) continue;
                string not = "ncalc:!(" + b.Visible[0].Substring(6) + ")";
                var a = d.Elements.FirstOrDefault(x => x != b && x.Type == "value" && x.Bind == b.Bind && x.Visible != null && x.Visible.Contains(not)
                                                       && Rectangle.Intersect(Box(x), Box(b)).Width * Rectangle.Intersect(Box(x), Box(b)).Height >= 0.8 * Math.Min(x.W * x.H, b.W * b.H)
                                                       && string.IsNullOrEmpty(x.ColorBind) && x.Format == b.Format);
                if (a == null) continue;
                string ca = a.Color ?? "#FFFFFF", cb = b.Color ?? "#FFFFFF";
                // b's own colour rule (if any) wins while b's condition holds
                string bColour = !string.IsNullOrEmpty(b.ColorBind) && b.ColorBind.StartsWith("ncalc:") ? b.ColorBind.Substring(6) : "'" + cb + "'";
                a.ColorBind = $"ncalc:if({b.Visible[0].Substring(6)}, {bColour}, '{ca}')";
                a.Visible = a.Visible.Where(c => c != not).ToList();
                if (a.Visible.Count == 0) a.Visible = null;
                a.Samples = (a.Samples ?? new string[0]).Concat(b.Samples ?? new string[0]).Distinct().ToArray();
                string an = name(a), bn = name(b);
                // drawn where the later of the two was (over the tiles the conditional one sat on)
                int at = Math.Max(d.Elements.IndexOf(a), d.Elements.IndexOf(b));
                d.Elements[at] = a;
                d.Elements.RemoveAt(d.Elements.IndexOf(a) == at ? d.Elements.IndexOf(b) : d.Elements.IndexOf(a));
                changes.Add($"{an} + {bn}: one value, coloured while {bn} would show (they took turns in the same place)");
            }
        }

        /// <summary>
        /// Bars drawn in the same place (throttle and brake on one strip, the later one on top): each change of either
        /// redraws the other over it, a flash. They're laid side by side across the strip instead, each keeping its
        /// length.
        /// </summary>
        private static void SplitStackedBars(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            var bars = d.Elements.Where(x => x.Type == "bar").ToList();
            var done = new HashSet<DashElement>();
            foreach (var a in bars)
            {
                if (done.Contains(a)) continue;
                var group = bars.Where(b => !done.Contains(b) && b.Orientation == a.Orientation && SameVis(a, b)
                                            && Rectangle.Intersect(Box(a), Box(b)).Width * Rectangle.Intersect(Box(a), Box(b)).Height >= 0.8 * Math.Min(a.W * a.H, b.W * b.H)).ToList();
                if (group.Count < 2) continue;
                var r = Box(a);
                bool horizontal = a.Orientation != "vertical";
                int across = horizontal ? r.Height : r.Width;
                if (across < 2 * group.Count) continue;
                int each = across / group.Count;
                for (int k = 0; k < group.Count; k++)
                {
                    var b = group[k];
                    var before = Box(b);
                    if (horizontal) { b.X = r.X; b.W = r.Width; b.Y = r.Y + k * each; b.H = k == group.Count - 1 ? r.Bottom - b.Y : each; }
                    else { b.Y = r.Y; b.H = r.Height; b.X = r.X + k * each; b.W = k == group.Count - 1 ? r.Right - b.X : each; }
                    done.Add(b);
                    changes.Add($"{name(b)}: {Str(before)} -> {Str(Box(b))}, beside the bars drawn in the same place");
                }
            }
        }

        private static bool SameVis(DashElement a, DashElement b) =>
            (a.Visible == null || a.Visible.Count == 0) ? (b.Visible == null || b.Visible.Count == 0) : (b.Visible != null && a.Visible.SequenceEqual(b.Visible));

        /// <summary>
        /// A value box touching a label's text rows (by a pixel or two, which check lets pass): each change of the value
        /// redraws the label too. The box is trimmed clear of it when its text still fits.
        /// </summary>
        private static void OffLabels(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var v in d.Elements.Where(x => x.Type == "value").ToList())
                foreach (var l in d.Elements.Where(x => x.Type == "label" && SameOrAlways(x, v)).ToList())
                {
                    var lb = Covers(l);
                    if (lb.Width <= 0 || !Box(v).IntersectsWith(lb) || Box(v).Contains(lb) || DashPages.Apart(l, v)) continue;
                    Trim(v, Rectangle.Inflate(lb, 2, 0), changes, name(v), name(l), slack: 0); // (2 px for the glyphs' overhang)
                }
            // bars too: a bar running under a label's text (a scale printed over a gauge) redraws the label at each move;
            // the bar gets thinner, off the text, keeping at least half its thickness
            foreach (var bar in d.Elements.Where(x => x.Type == "bar").ToList())
                foreach (var l in d.Elements.Where(x => x.Type == "label" && ((x.Visible == null || x.Visible.Count == 0) || (bar.Visible != null && x.Visible != null && x.Visible.Any(bar.Visible.Contains)))).ToList())
                {
                    var lb = Rectangle.Inflate(Covers(l), 0, 1);
                    var bb = Box(bar);
                    if (lb.Width <= 0 || !bb.IntersectsWith(lb) || lb.Contains(bb)) continue;
                    bool horizontal = bar.Orientation != "vertical";
                    Rectangle cut = bb;
                    if (horizontal)
                        cut = lb.Top > bb.Top ? Rectangle.FromLTRB(bb.Left, bb.Top, bb.Right, lb.Top) : Rectangle.FromLTRB(bb.Left, lb.Bottom, bb.Right, bb.Bottom);
                    else
                        cut = lb.Left > bb.Left ? Rectangle.FromLTRB(bb.Left, bb.Top, lb.Left, bb.Bottom) : Rectangle.FromLTRB(lb.Right, bb.Top, bb.Right, bb.Bottom);
                    if ((horizontal ? cut.Height : cut.Width) < (horizontal ? bb.Height : bb.Width) / 2) continue;
                    bar.X = cut.X; bar.Y = cut.Y; bar.W = cut.Width; bar.H = cut.Height;
                    changes.Add($"{name(bar)}: {Str(bb)} -> {Str(cut)}, off {name(l)}'s text");
                }
        }

        private static bool SameOrAlways(DashElement l, DashElement v) =>
            l.Visible == null || l.Visible.Count == 0 || SameVis(l, v);

        private static void GrowTags(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            foreach (var v in d.Elements.Where(x => x.Type == "value").ToList())
            {
                string ws = Widest(v);
                if (ws.Length == 0) continue;
                var band = DashRenderer.TextBand(v, v.Font, ws, 1);
                int vi = d.Elements.IndexOf(v);
                // a tag made for it (a small rect under it), or a filled panel it sits on that it pokes out of a little
                // shown when the value is, and only then (both always, or under the same conditions)
                bool Same(DashElement r) => (r.Visible == null || r.Visible.Count == 0) ? (v.Visible == null || v.Visible.Count == 0) : (v.Visible != null && r.Visible.SequenceEqual(v.Visible));
                var tag = d.Elements.Take(vi).LastOrDefault(r => r.Type == "rect" && Same(r)
                                                                 && Box(r).Contains(new Point(band.X + band.Width / 2, band.Y + band.Height / 2))
                                                                 && Box(v).Contains(new Point(r.X + r.W / 2, r.Y + r.H / 2))
                                                                 && r.W * r.H <= v.W * v.H * 1.2);
                bool panel = false;
                if (tag == null)
                {
                    tag = d.Elements.Take(vi).LastOrDefault(r => (r.Type == "rect" || (r.Type == "box" && r.Fill != null)) && Same(r)
                                                               && Box(r).Contains(new Point(band.X + band.Width / 2, band.Y + band.Height / 2)));
                    panel = true;
                }
                if (tag == null) continue;
                var tb = Box(tag);
                int pad = panel ? Math.Max(tag.Border, 0) + Math.Max(0, tag.Radius / 2) + 1 : 1;
                if (tb.Contains(Rectangle.Inflate(band, panel ? pad : 0, 0))) continue;
                var grown = Rectangle.Union(tb, Rectangle.Inflate(band, pad, panel ? 0 : 1));
                // (a panel only grows a little, as its layout is the designer's, and only sideways)
                bool hits = !Usable.Contains(grown) || (panel && (grown.Width - tb.Width > 12 || grown.Height != tb.Height)) ||
                            d.Elements.Any(o => o != v && o != tag && (o.Type == "value" || o.Type == "label") && !DashPages.Apart(o, v) && Clash(grown, Covers(o)) && !Clash(tb, Covers(o))
                                                && !(o.Visible != null && o.Visible.Count > 0));
                if (hits)
                {
                    // no room to grow the tag: the value comes inside it instead, if its text fits there
                    var inside = Rectangle.Intersect(Box(v), Rectangle.Inflate(tb, -pad, 0));
                    if (!panel && inside.Width >= NeedWidth(v, v.Font) + 2 && inside.Height >= DashRenderer.FontHeight(v.Font))
                    {
                        changes.Add($"{name(v)}: box {Str(Box(v))} -> {Str(inside)}, inside {name(tag)} (no room to widen it)");
                        v.X = inside.X; v.Y = inside.Y; v.W = inside.Width; v.H = inside.Height;
                    }
                    continue;
                }
                changes.Add($"{name(tag)}: {Str(tb)} -> {Str(grown)}, so {name(v)}'s widest text sits on it");
                tag.X = grown.X; tag.Y = grown.Y; tag.W = grown.Width; tag.H = grown.Height;
            }
        }

        private static void ClearOverlaps(DashDefinition d, List<string> changes, Func<DashElement, string> name)
        {
            bool Shown(DashElement e) => e.Visible == null || e.Visible.Count == 0 || e.PreviewVisible != false;
            var texts = d.Elements.Where(e => (e.Type == "value" || e.Type == "label") && Shown(e)).ToList();
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                {
                    var a = texts[i]; var b = texts[j];
                    if (a.Type == "label" && b.Type == "label") continue;
                    if (!Clash(Covers(a), Covers(b))) continue;
                    // trim a value (the bigger box first when both are)
                    bool done = false;
                    foreach (var v in new[] { a, b }.Where(x => x.Type == "value").OrderByDescending(x => x.W * x.H))
                    {
                        var o = v == a ? b : a;
                        if (Trim(v, Covers(o), changes, name(v), name(o))) { done = true; break; }
                    }
                    if (done) continue;
                    // a label over a value it can't be trimmed off: the label moves just clear of it
                    var lab = a.Type == "label" ? a : b.Type == "label" ? b : null;
                    if (lab != null && Nudge(lab, texts, changes, name(lab))) continue;
                    // or its text gets smaller (a caption beside its value), down to the smallest screen font, until
                    // the value can be trimmed clear of it
                    if (lab != null)
                    {
                        var val = lab == a ? b : a;
                        int f0 = lab.Font; var vb0 = Box(val); int vf0 = val.Font; bool ok = false;
                        foreach (var f in Enumerable.Range(0, FontMetrics.Fonts.Length)
                                                    .Where(f => DashRenderer.FontHeight(f) >= 16 && DashRenderer.FontHeight(f) < DashRenderer.FontHeight(f0)
                                                                && DashFonts.FullAscii.Contains(f) && Width(f, DashRenderer.Clean(lab.Text)) >= 0
                                                                && Width(f, DashRenderer.Clean(lab.Text)) <= lab.W)
                                                    .OrderByDescending(f => DashRenderer.FontHeight(f)).ThenBy(f => Width(f, lab.Text ?? "")))
                        {
                            lab.Font = f;
                            if (!Clash(Covers(lab), Box(val)) || Trim(val, Covers(lab), new List<string>(), "", "")) { ok = true; break; }
                        }
                        if (ok)
                        {
                            changes.Add($"{name(lab)}: font {f0} -> {lab.Font}, so {name(val)} fits beside it" + (Box(val) != vb0 ? $" ({name(val)}: box {Str(vb0)} -> {Str(Box(val))})" : ""));
                            continue;
                        }
                        lab.Font = f0; val.X = vb0.X; val.Y = vb0.Y; val.W = vb0.Width; val.H = vb0.Height; val.Font = vf0;
                        // no room for both: the caption is shortened (abbreviations, then letters) to clear the value's
                        // text, in the tallest font that allows it
                        string t0 = lab.Text; var vband = Band(val, Widest(val));
                        // three letters or more ("WAT"): anything shorter reads as noise, so the overlap stays for a person to fix
                        var fonts = new[] { f0 }.Concat(Enumerable.Range(0, FontMetrics.Fonts.Length)
                                                    .Where(f => DashRenderer.FontHeight(f) >= 16 && DashRenderer.FontHeight(f) < DashRenderer.FontHeight(f0) && DashFonts.FullAscii.Contains(f))
                                                    .OrderByDescending(f => DashRenderer.FontHeight(f))).ToList();
                        int want = Math.Min(3, t0.Replace(" ", "").Length);
                        foreach (var (f, min) in fonts.Select(f => (f, want)))
                        {
                            lab.Font = f;
                            string t = Shorten(t0, x => { lab.Text = x; return Width(f, x) >= 0 && Width(f, x) <= lab.W && !Covers(lab).IntersectsWith(vband); });
                            lab.Text = t;
                            if (t.Length >= min && !Clash(Covers(lab), vband) && !Covers(lab).IntersectsWith(vband))
                            {
                                // the value's box trimmed off the (now shorter) caption
                                if (Covers(lab).IntersectsWith(Box(val))) Trim(val, Rectangle.Inflate(Covers(lab), 2, 0), new List<string>(), "", "", slack: 0);
                                if (!Rectangle.Inflate(Covers(lab), 2, 0).IntersectsWith(Box(val))) { ok = true; break; }
                            }
                        }
                        if (ok) { changes.Add($"{name(lab)}: \"{t0}\" -> \"{lab.Text}\", font {f0} -> {lab.Font}, so {name(val)} fits beside it"); continue; }
                        lab.Text = t0; lab.Font = f0; val.X = vb0.X; val.Y = vb0.Y; val.W = vb0.Width; val.H = vb0.Height; val.Font = vf0;
                    }
                    changes.Add($"{name(a)} still overlaps {name(b)}: fix by hand");
                }
        }

        /// <summary>Trims a value's box off `ob` (to at most 2 px of overlap), keeping its widest text; else a smaller font.</summary>
        private static bool Trim(DashElement v, Rectangle ob, List<string> changes, string vn, string on, int slack = 2, double minRatio = 0.75)
        {
            var box = Box(v);
            var cands = new List<Rectangle>();
            if (ob.Left > box.Left) cands.Add(Rectangle.FromLTRB(box.Left, box.Top, ob.Left + slack, box.Bottom));
            if (ob.Right < box.Right) cands.Add(Rectangle.FromLTRB(ob.Right - slack, box.Top, box.Right, box.Bottom));
            if (ob.Top > box.Top) cands.Add(Rectangle.FromLTRB(box.Left, box.Top, box.Right, ob.Top + slack));
            if (ob.Bottom < box.Bottom) cands.Add(Rectangle.FromLTRB(box.Left, ob.Bottom - slack, box.Right, box.Bottom));
            var samples = Texts(v);
            // biggest first: the least loss
            foreach (var c in cands.OrderByDescending(c => c.Width * c.Height))
            {
                int font = v.Font;
                if (DashRenderer.FontHeight(font) > c.Height || NeedWidth(v, font) > c.Width)
                {
                    // a smaller font that takes all its texts, no less than 3/4 of the height it had
                    if (samples.Count == 0) continue;
                    var probe = new DashElement { W = c.Width + 4, H = c.Height };
                    int nf = BestFont(probe, samples, c.Height, preferNarrow: true, gearOnly: false);
                    if (nf < 0 || DashRenderer.FontHeight(nf) < minRatio * DashRenderer.FontHeight(v.Font) || NeedWidth(v, nf) > c.Width) continue;
                    font = nf;
                }
                int f0 = v.Font;
                v.X = c.X; v.Y = c.Y; v.W = c.Width; v.H = c.Height; v.Font = font;
                changes.Add($"{vn}: box {Str(box)} -> {Str(c)}{(font != f0 ? $", font {f0} -> {font}" : "")}, clear of {on}");
                return true;
            }
            return false;
        }

        /// <summary>Moves a label (up to 12 px, any direction) until it clears every value; true if it could.</summary>
        private static bool Nudge(DashElement lab, List<DashElement> texts, List<string> changes, string ln)
        {
            var box = Box(lab);
            for (int dist = 1; dist <= 12; dist++)
                foreach (var (dx, dy) in new[] { (0, -dist), (0, dist), (-dist, 0), (dist, 0) })
                {
                    lab.X = box.X + dx; lab.Y = box.Y + dy;
                    var c = Covers(lab);
                    if (Usable.Contains(Box(lab)) && !texts.Any(t => t != lab && t.Type == "value" && Clash(c, Covers(t))))
                    {
                        changes.Add($"{ln}: moved {dx},{dy} px, clear of the values around it");
                        return true;
                    }
                }
            lab.X = box.X; lab.Y = box.Y;
            return false;
        }

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
            // a unit in brackets ("NRG LVL (%)") goes first
            var nb = System.Text.RegularExpressions.Regex.Replace(t, @"\s*[\(\[][^\)\]]*[\)\]]?\s*$", "").Trim();
            if (nb.Length > 0 && nb != t) { t = nb; if (fits(t)) return t; }
            foreach (var (l, sh) in Abbreviations.OrderByDescending(a => a.Long.Length))
            {
                var r = System.Text.RegularExpressions.Regex.Replace(t, @"\b" + System.Text.RegularExpressions.Regex.Escape(l) + @"\b", sh,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (r != t) { t = r; if (fits(t)) return t; }
            }
            t = t.Replace(" ", "");
            while (t.Length > 1 && !fits(t)) t = t.Substring(0, t.Length - 1);
            return t.TrimEnd('(', '[', '/', '-', '.', ':', ',');
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
