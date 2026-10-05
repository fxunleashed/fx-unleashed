using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Smooth shapes from the screen's own commands (FXProDashes docs/screen-commands.md): the screen draws with SEGGER
    /// emWin, whose anti-aliased polygon (`draw_h`, the gauge needle: a hexagon symmetric about its axis) and filled circle
    /// (`cirs`) are reachable. Their smoothing costs ~2 us per pixel (a plain `fill` ~0.07), so a shape's inside is plain
    /// fills and only its edge is smoothed. Checked on the wheel 2026-10-04: an oval this way keeps up at 25 KB/s.
    /// </summary>
    internal static class ScreenShapes
    {
        /// <summary>Most a vertex may sit off the true edge (px). Vertices are whole pixels (the screen's AA has no
        /// sub-pixel coordinates), so the edge is a chain of chords through pixel corners near the curve.</summary>
        private const double Tolerance = 0.25;

        /// <summary>A `cirs` corner bigger than this costs too much smoothing (its whole disc is smoothed).</summary>
        public const int MaxCornerRadius = 24;

        /// <summary>Screen time of one smoothed pixel, seconds (measured: a 365x146 oval smoothed whole took ~0.1 s).</summary>
        public const double SecondsPerSmoothPixel = 2e-6;

        private static readonly ConcurrentDictionary<long, List<Point>> edges = new ConcurrentDictionary<long, List<Point>>();

        /// <summary>
        /// A filled ellipse in the box (x, y, w, h), as GDI+ FillEllipse draws it there, in RGB565 `colour`: for each pair of
        /// edge chords one `fill` (the inside) and two thin `draw_h` bands (the top edge, and the bottom edge mirrored), each
        /// symmetric about its own axis just inside the edge. Null when the box is too small for this to be worth it.
        /// </summary>
        public static List<string> Ellipse(int x, int y, int w, int h, int colour)
        {
            if (w < 8 || h < 8 || w > 800 || h > 480) return null;
            var top = TopEdge(w, h); // vertices relative to (x, y): left end to right end, y up to the middle
            int mid2 = h;            // 2 * centre y, relative: whole, so a mirror of a whole y is whole
            var cmds = new List<string>();
            var bands = new List<string>();
            int i = 0;
            while (i < top.Count - 1)
            {
                int take = i + 2 < top.Count ? 3 : 2;
                var a = top[i]; var c = top[i + take - 1]; var b = take == 3 ? top[i + 1] : c;
                i += take - 1;
                int low = Math.Max(a.Y, Math.Max(b.Y, c.Y));     // the band's axis: the chords' lowest vertex
                int axisTop = Math.Min(low, mid2 / 2);            // (never past the middle)
                int axisBottom = mid2 - axisTop;
                if (axisBottom > axisTop) cmds.Add(Fill(x + a.X, y + axisTop, c.X - a.X, axisBottom - axisTop, colour));
                bands.Add(Band(x, y, a, b, c, take, axisTop, colour));
                if (axisBottom != axisTop) bands.Add(Band(x, y, a, b, c, take, axisBottom, colour, mirror: true, axisTop: axisTop));
            }
            cmds.AddRange(bands); // fills first: a band blends its edge over what's under, its inside lands on the fill
            return cmds;
        }

        /// <summary>One band: the chords a-b-c (or a-c) above `axis` and their mirror below it (for the bottom edge, the
        /// top chords mirrored about the middle: the same half-heights about the mirrored axis).</summary>
        private static string Band(int x, int y, Point a, Point b, Point c, int take, int axis, int colour, bool mirror = false, int axisTop = 0)
        {
            int refY = mirror ? axisTop : axis;
            int hA = refY - a.Y, hB = refY - b.Y, hC = refY - c.Y;
            // draw_h x,y,r,angle,cu,colour,circle colour,circle size,up,down,left,v0,v2: points (-up,+-v0) (0,+-cu) (down,+-v2)
            return take == 3
                ? F("draw_h {0},{1},999,0,{2},{3},0,0,{4},{5},0,{6},{7}", x + b.X, y + axis, hB, colour, b.X - a.X, c.X - b.X, hA, hC)
                : F("draw_h {0},{1},999,0,{2},{3},0,0,{4},0,0,{5},0", x + c.X, y + axis, hC, colour, c.X - a.X, hA);
        }

        /// <summary>The top edge of a w x h ellipse (box at 0,0) as whole-pixel vertices within Tolerance of the curve, few
        /// as can be: from each vertex the farthest one whose chord stays within Tolerance. Cached per size.</summary>
        private static List<Point> TopEdge(int w, int h)
        {
            return edges.GetOrAdd(((long)w << 20) | (uint)h, _ =>
            {
                double cx = w / 2.0, cy = h / 2.0, ra = w / 2.0, rb = h / 2.0;
                double Dist(double px, double py)
                {
                    double f = (px - cx) * (px - cx) / (ra * ra) + (py - cy) * (py - cy) / (rb * rb) - 1;
                    double gx = 2 * (px - cx) / (ra * ra), gy = 2 * (py - cy) / (rb * rb);
                    return Math.Abs(f) / Math.Max(1e-9, Math.Sqrt(gx * gx + gy * gy));
                }
                // pixel corners near the upper half of the curve; per column the highest one kept (a vertical run at an end
                // is the band's own end)
                var byX = new SortedDictionary<int, int>();
                for (int px = 0; px <= w; px++)
                    for (int py = 0; py <= h / 2; py++)
                        if (Dist(px, py) <= 0.35 && (!byX.TryGetValue(px, out var have) || py < have)) byX[px] = py;
                byX[0] = byX.TryGetValue(0, out var y0) ? y0 : h / 2;
                byX[w] = byX.TryGetValue(w, out var y1) ? y1 : h / 2;
                var cands = byX.Select(kv => new Point(kv.Key, kv.Value)).ToList();
                bool ChordOk(Point p, Point q)
                {
                    for (int k = 1; k < 24; k++)
                    {
                        double t = k / 24.0;
                        if (Dist(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t) > Tolerance) return false;
                    }
                    return true;
                }
                var path = new List<Point> { cands[0] };
                int at = 0;
                while (at < cands.Count - 1)
                {
                    int next = at + 1;
                    for (int j = at + 2; j < cands.Count; j++) if (ChordOk(cands[at], cands[j])) next = j;
                    path.Add(cands[next]);
                    at = next;
                }
                return path;
            });
        }

        /// <summary>
        /// A filled rounded rectangle (GDI+ path with arcs of `radius`): two fills crossing, a `cirs` in each corner. A
        /// radius under 2 is a plain fill. Null when the corners would cost too much smoothing.
        /// </summary>
        public static List<string> RoundedBox(int x, int y, int w, int h, int radius, int colour)
        {
            if (w <= 0 || h <= 0) return new List<string>();
            radius = Math.Min(radius, Math.Min(w, h) / 2);
            if (radius < 2) return new List<string> { Fill(x, y, w, h, colour) };
            if (radius > MaxCornerRadius) return null;
            // emWin's filled circle of radius R at (cx, cy) covers columns cx-R .. cx+R: a disc of radius R + 1/2 about the
            // pixel centre; R = radius - 1 puts its edge on the box's
            int r = radius - 1;
            var list = new List<string>();
            if (w > 2 * radius) list.Add(Fill(x + radius, y, w - 2 * radius, h, colour));
            if (h > 2 * radius) list.Add(Fill(x, y + radius, w, h - 2 * radius, colour));
            foreach (var (cx, cy) in new[] { (x + r, y + r), (x + w - 1 - r, y + r), (x + r, y + h - 1 - r), (x + w - 1 - r, y + h - 1 - r) })
                list.Add(F("cirs {0},{1},{2},{3}", cx, cy, r, colour));
            return list;
        }

        /// <summary>Pixels the screen smooths for a command (draw_h polygon, cirs disc), 0 for anything else: what the pacing
        /// adds as screen time.</summary>
        public static double SmoothPixels(string cmd)
        {
            if (cmd.StartsWith("cirs ", StringComparison.Ordinal))
            {
                var a = Args(cmd);
                return a.Length >= 3 ? Math.PI * (a[2] + 0.5) * (a[2] + 0.5) : 0;
            }
            if (cmd.StartsWith("draw_h ", StringComparison.Ordinal))
            {
                var p = Polygon(cmd);
                if (p == null) return 0;
                double area = 0;
                for (int k = 0; k < p.Length; k++) { var u = p[k]; var v = p[(k + 1) % p.Length]; area += u.X * v.Y - v.X * u.Y; }
                var a = Args(cmd);
                double circle = a.Length >= 8 && a[7] > 0 ? Math.PI * a[7] * a[7] / 4 : 0;
                return Math.Abs(area) / 2 + circle;
            }
            return 0;
        }

        /// <summary>The polygon a `draw_h` fills, in screen coordinates (its axis rotated by the angle), or null.</summary>
        public static PointF[] Polygon(string cmd)
        {
            var a = Args(cmd);
            if (a.Length < 13) return null;
            int x0 = a[0], y0 = a[1], r = a[2], ang = a[3], cu = Math.Min(a[4] & 255, r), up = a[8], down = a[9], left = Math.Max(0, a[10]);
            int v0 = Math.Min(a[11], r), v2 = Math.Min(a[12], r);
            if (r < 2) return null;
            var pts = new List<PointF>();
            if (up > 0) { pts.Add(new PointF(-up, -v0)); if (v0 != 0) pts.Add(new PointF(-up, v0)); }
            pts.Add(new PointF(0, cu));
            if (down > 0) { pts.Add(new PointF(down, v2)); if (v2 != 0) pts.Add(new PointF(down, -v2)); }
            pts.Add(new PointF(0, -cu));
            if (up <= 0 && down <= 0) return null;
            double t = -Math.PI * ang / 180.0, cs = Math.Cos(t), sn = Math.Sin(t);
            return pts.Select(p => new PointF((float)(x0 + (int)((p.X - left) * cs + p.Y * sn)), (float)(y0 + (int)(-(p.X - left) * sn + p.Y * cs)))).ToArray();
        }

        /// <summary>The screen area a drawing command can change, or empty (for flash checks).</summary>
        public static Rectangle Area(string cmd)
        {
            if (cmd.StartsWith("draw_h ", StringComparison.Ordinal))
            {
                var p = Polygon(cmd);
                if (p == null) return Rectangle.Empty;
                return Rectangle.FromLTRB((int)Math.Floor(p.Min(q => q.X)), (int)Math.Floor(p.Min(q => q.Y)), (int)Math.Ceiling(p.Max(q => q.X)) + 1, (int)Math.Ceiling(p.Max(q => q.Y)) + 1);
            }
            if (cmd.StartsWith("cirs ", StringComparison.Ordinal))
            {
                var a = Args(cmd);
                return a.Length >= 3 ? new Rectangle(a[0] - a[2], a[1] - a[2], 2 * a[2] + 1, 2 * a[2] + 1) : Rectangle.Empty;
            }
            return Rectangle.Empty;
        }

        private static int[] Args(string cmd)
        {
            int sp = cmd.IndexOf(' ');
            if (sp < 0) return new int[0];
            var parts = cmd.Substring(sp + 1).Split(',');
            var a = new int[parts.Length];
            for (int k = 0; k < parts.Length; k++) if (!int.TryParse(parts[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out a[k])) return new int[0];
            return a;
        }

        private static string Fill(int x, int y, int w, int h, int c) => F("fill {0},{1},{2},{3},{4}", x, y, w, h, c);

        private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
