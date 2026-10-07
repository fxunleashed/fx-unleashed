using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// Pictures on the wheel's screen: a GIF (or any picture file) turned into a screensaver keeps its colours for a screen with
/// the RAM drive (drawn from tiles) and is reduced only for the rectangles of a screen without; Block pixelation of image
/// elements; the picture savers going to the RAM like dashes.
/// </summary>
static class PicturesTests
{
    public static void Run(string dir)
    {
        string folder = Path.Combine(Path.GetTempPath(), "fx-pictures-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        string oldRoot = DashLibrary.Root;
        double k0 = GifSaver.MicrosecondsPerPixel, fixed0 = GifSaver.FixedMs; int hold0 = GifSaver.HoldArea;
        Check("gif speed: the hold is off by default (it was added untried and the first animation ran at half speed)", hold0 == int.MaxValue);
        DashLibrary.Root = folder; // the import writes its saver file under here, not in SimHub's folder
        try
        {
            GifPicture(folder);
            Block();
            Animations(folder);
            Choices(folder);
        }
        finally
        {
            GifSaver.MicrosecondsPerPixel = k0; GifSaver.FixedMs = fixed0; GifSaver.HoldArea = hold0;
            DashLibrary.Root = oldRoot;
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    /// <summary>A smooth, many-coloured picture: red and green across, blue down.</summary>
    static Bitmap Gradient(int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                b.SetPixel(x, y, Color.FromArgb(x * 255 / (w - 1), 255 - x * 255 / (w - 1), y * 255 / (h - 1)));
        return b;
    }

    static int Colours(Bitmap b, Rectangle r)
    {
        var set = new HashSet<int>();
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++) set.Add(b.GetPixel(x, y).ToArgb() & 0xFFFFFF);
        return set.Count;
    }

    static Bitmap Draw(DashDefinition d, bool tiles)
    {
        var screen = new PreviewScreen();
        var r = new DashRenderer(screen, d, 0, 0);
        if (tiles) { r.EnableTiles(); r.UseTiles(true); }
        r.DrawAll();
        return (Bitmap)screen.Bitmap.Clone();
    }

    static void GifPicture(string folder)
    {
        // a GIF as the file (256-colour palette, what GDI+ writes); the same picture is what the user adds as a screensaver
        string gif = Path.Combine(folder, "plasma.gif");
        using (var g = Gradient(360, 240)) g.Save(gif, ImageFormat.Gif);
        var item = IdleScreens.ImportImage(gif);
        var d = JsonConvert.DeserializeObject<DashDefinition>(File.ReadAllText(item.File));
        var e = d.Elements.Single(x => x.Type == "image");
        Check("gif: the import makes a picture saver", item.Kind == SaverKind.Image && IdleScreens.DrawnFromRam(d));
        Check("gif: the clock saver isn't one (no file to load for a few rectangles)", !IdleScreens.DrawnFromRam(IdleScreens.ClockDash()));
        Check("gif: fitted into the screen's usable area", e.W <= 790 && e.H <= 460 && (e.W == 790 || e.H == 460), $"{e.W}x{e.H}");
        Check("gif: the fallback for a screen without RAM draws in time", IdleScreens.DrawSeconds(d) <= IdleScreens.MaxDrawSeconds, $"{IdleScreens.DrawSeconds(d):0.0} s");

        // the file keeps the picture's colours: it used to hold the pixelated copy, so a RAM screen could only show blocks
        using (var stored = new Bitmap(new MemoryStream(Convert.FromBase64String(d.Images[e.Image]))))
        {
            Check("gif: the saved picture keeps its colours", Colours(stored, new Rectangle(0, 0, stored.Width, stored.Height)) > 1000,
                  Colours(stored, new Rectangle(0, 0, stored.Width, stored.Height)) + " colours");
            Check("gif: the saved picture is the fitted size", stored.Width == e.W && stored.Height == e.H);
        }

        var area = new Rectangle(e.X, e.Y, e.W, e.H);
        using (var fills = Draw(d, false))
        using (var tiles = Draw(d, true))
        {
            int cf = Colours(fills, area), ct = Colours(tiles, area);
            Check("gif: without RAM it's drawn in the colours the saver asks for", cf <= e.MaxColors, $"{cf} colours, MaxColors {e.MaxColors}");
            Check("gif: from the RAM drive it keeps its colours", ct > e.MaxColors * 20, $"{ct} colours");
        }
        Check("gif: it fits on the screen's RAM drive", DashRam.Bytes(new[] { d }) <= ScreenRam.Budget, DashRam.Text(DashRam.Bytes(new[] { d })));
        IdleScreens.Delete(item);
    }

    /// <summary>Records every command, and draws them on a preview screen.</summary>
    sealed class Recorder : IScreenSink
    {
        public readonly PreviewScreen Screen = new PreviewScreen();
        public readonly List<string> Commands = new List<string>();
        public void Cmd(string c) { Commands.Add(c); Screen.Cmd(c); }
        public void Flush() { }
    }

    static string Save(string folder, string name, string base64)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    /// <summary>The GIF's frames as a browser would show them (GDI+ combines them), scaled to w x h on black.</summary>
    static List<Bitmap> SourceFrames(string path, int w, int h)
    {
        var list = new List<Bitmap>();
        using (var gif = Image.FromFile(path))
        {
            var dim = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
            for (int i = 0; i < gif.GetFrameCount(dim); i++)
            {
                gif.SelectActiveFrame(dim, i);
                var b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    using (var edges = new ImageAttributes())
                    {
                        edges.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                        g.DrawImage(gif, new Rectangle(0, 0, w, h), 0, 0, gif.Width, gif.Height, GraphicsUnit.Pixel, edges);
                    }
                }
                list.Add(b);
            }
        }
        return list;
    }

    /// <summary>Mean difference (0-255) of the picture's area on the screen from a frame.</summary>
    static double Off(Bitmap screen, Bitmap frame, int ox, int oy)
    {
        long sum = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var a = screen.GetPixel(ox + x, oy + y); var b = frame.GetPixel(x, y);
                sum += Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
            }
        return sum / (double)(frame.Width * frame.Height);
    }

    /// <summary>Animated GIFs as screensavers: AnimatedPicture (frames kept to the RAM budget, changes only), GifSaver (played from the RAM drive).</summary>
    static void Animations(string folder)
    {
        string partial = Save(folder, "partial.gif", TestGifs.Partial), sprite = Save(folder, "sprite.gif", TestGifs.Sprite), busy = Save(folder, "busy.gif", TestGifs.Busy);

        // a GIF with one frame isn't an animation (it stays a picture), nor is anything but a GIF
        string still = Path.Combine(folder, "one.gif");
        using (var g = Gradient(80, 50)) g.Save(still, ImageFormat.Gif);
        Check("gif anim: one frame is a picture, not an animation", IdleScreens.ImportAnimation(still) == null);
        Check("gif anim: a picture of another format is not one", IdleScreens.ImportAnimation(Path.Combine(folder, "plasma.gif").Replace(".gif", ".png")) == null);

        // all the frames fit: every one kept, the GIF's own timing
        var item = IdleScreens.ImportAnimation(partial);
        var pic = GifSaver.Load(item);
        Check("gif anim: an animated GIF becomes an animation saver", item != null && item.IsAnimation && item.Kind == SaverKind.Image && pic != null);
        Check("gif anim: all 8 frames kept when they fit", item.Frames == 8 && item.FramesTotal == 8 && !pic.Oversized, $"{item.Frames}/{item.FramesTotal}");
        Check("gif anim: it keeps the GIF's timing", pic.Frames.Sum(f => f.Ms) == 60 * 5 + 80 + 100 + 200 && pic.Frames[2].Ms == 80, string.Join(",", pic.Frames.Select(f => f.Ms)));
        Check("gif anim: within the RAM budget, and the first frame as a picture for a screen without RAM", pic.RamBytes <= AnimatedPicture.Budget && pic.Still != null, $"{pic.RamBytes / 1024} KB");
        // played from the RAM drive: each step shows its frame, with no drift over two loops (the wrap back to the first included)
        GifSaver.MicrosecondsPerPixel = 0; GifSaver.FixedMs = 0; // (a screen that draws at once: the speed test below has the cost)
        var saver = GifSaver.Create(item, 10, 20);
        saver.UseTiles = true;
        var rec = new Recorder();
        saver.Start();
        int ox = 10 + (790 - pic.Width) / 2, oy = 20 + (460 - pic.Height) / 2;
        var source = SourceFrames(partial, pic.Width, pic.Height);
        double loop = pic.Frames.Sum(f => f.Ms) / 1000.0, worst = 0, now = 50;
        int seen = 0, last = -1, wraps = 0;
        for (double t = 0; t < loop * 2 + 0.3; t += 1 / 30.0)
        {
            saver.Step(rec, now + t, 24);
            if (saver.Drawing || saver.Current == last) continue;
            if (saver.Current == 0 && last > 0) wraps++;
            last = saver.Current;
            worst = Math.Max(worst, Off(rec.Screen.Bitmap, source[last], ox, oy));
            seen++;
        }
        Check("gif anim: every step shows its frame, over two loops", seen >= 16 && wraps >= 2 && worst < 12, $"{seen} steps, {wraps} wraps, worst {worst:0.0}");
        Check("gif anim: drawn from the screen's pictures (ramv), a few commands a frame", rec.Commands.Count(c => c.StartsWith("sets \"ramv: ")) >= seen && rec.Commands.Count < seen * 8, rec.Commands.Count + " commands");
        foreach (var f in source) f.Dispose();

        // timing: 30 steps a second, the frame changes when its time is up
        var timed = GifSaver.Create(item, 10, 20); timed.UseTiles = true; timed.Start();
        var tr = new Recorder();
        timed.Step(tr, 10.0, 24); // the first picture goes up; the loop starts after it
        timed.Step(tr, 10.0, 24);
        timed.Step(tr, 10.03, 24);
        Check("gif anim: stays on a frame for its time", timed.Current == 0);
        timed.Step(tr, 10.07, 24);
        Check("gif anim: moves on when it's up (60 ms)", timed.Current == 1, "frame " + timed.Current);

        // no RAM drive: the first frame as a picture drawn with rectangles
        var plain = GifSaver.Create(item, 10, 20); plain.UseTiles = false; plain.Start();
        var pr = new Recorder();
        for (int i = 0; i < 400 && plain.Drawing; i++) plain.Step(pr, 5, 500);
        Check("gif anim: without the RAM drive it shows the first frame with rectangles", !plain.Drawing && pr.Commands.Any(c => c.StartsWith("fill ")) && !pr.Commands.Any(c => c.StartsWith("sets \"ramv")));
        Check("gif anim: its dash is the first frame when it can't play, none when it can", IdleScreens.DashFor(item, null, false) != null && IdleScreens.DashFor(item, null, true) == null);

        // more than fits: evenly spaced frames, the loop as long as the GIF's, and the card says so
        double total = pic.Frames.Sum(f => f.Ms);
        int tight = pic.RamBytes * 6 / 10; // 60% of what all eight take
        var small = IdleScreens.ImportAnimation(partial, tight);
        var sp = GifSaver.Load(small);
        Check("gif anim: more frames than fit keeps as many as do", small.Frames < small.FramesTotal && small.Frames >= 2 && sp.Oversized && sp.RamBytes <= tight, $"{small.Frames}/{small.FramesTotal}, {sp.RamBytes} of {tight} B");
        Check("gif anim: an oversized GIF keeps its whole loop time", sp.Frames.Sum(f => f.Ms) == total, $"{sp.Frames.Sum(f => f.Ms)} vs {total} ms");
        var pre = IdleScreens.ImportAnimation(partial, 600 * 1024);
        Check("gif anim: more budget keeps more", GifSaver.Load(pre).FramesTaken == 8 && small.Frames < GifSaver.Load(pre).FramesTaken);
        Check("gif anim: the files are within the files limit", AnimatedPicture.MaxFiles >= 20 && new GifSaver(sp).Tiles.Files.Count() <= AnimatedPicture.MaxFiles);

        // a transparent sprite (disposal 2): frames are combined as a browser shows them, identical pictures are one file
        var si = IdleScreens.ImportAnimation(sprite);
        var spp = GifSaver.Load(si);
        var ss = new GifSaver(spp, 10, 20);
        Check("gif anim: a transparent sprite is played over black", si.Frames == 6 && spp.Frames.Count == 6);
        // changes only: the moving circle costs a small picture a frame, not the whole picture again
        long whole = (long)spp.Width * spp.Height, patch = spp.Frames[1].Tiles.Sum(t => (long)t.W * t.H);
        Check("gif anim: a frame after the first is only what changed", patch * 10 < whole * 4 && spp.Frames[0].Tiles[0].W == spp.Width, $"{patch} of {whole} px");
        Check("gif anim: the same picture of a moving sprite is stored once", ss.Tiles.Files.Count() < spp.Frames.Sum(f => f.Tiles.Count), $"{ss.Tiles.Files.Count()} files for {spp.Frames.Sum(f => f.Tiles.Count)} tiles");

        // what changed: two far-apart changes are two small pictures, one blob is one
        var a = new byte[300 * 200 * 3]; var b2 = (byte[])a.Clone();
        void Dot(int x, int y) { for (int dy = 0; dy < 10; dy++) for (int dx = 0; dx < 10; dx++) b2[((y + dy) * 300 + x + dx) * 3 + 1] = 255; }
        Dot(10, 10); Dot(280, 180);
        var two = AnimatedPicture.Changes(a, b2, 300, 200);
        Check("gif anim: changes far apart are separate pictures", two.Count == 2 && two.All(r => r.Width == 10 && r.Height == 10), string.Join(" ", two));
        Dot(20, 12);
        var near = AnimatedPicture.Changes(a, b2, 300, 200);
        Check("gif anim: changes close together are one picture", near.Count == 2 && near.Any(r => r.Width == 20), string.Join(" ", near));
        Check("gif anim: nothing changed, nothing to draw", AnimatedPicture.Changes(a, (byte[])a.Clone(), 300, 200).Count == 0);

        // settings: an animation is an Image item with a flag. A kind of its own would fail to load in a version that doesn't know
        // it (a roll back), and the settings with it
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(new UsbSettings { Savers = { item } });
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>(json).Savers.Single();
        Check("gif anim: saved as a plain Image kind with a flag, which a version without animations can read",
              json.Contains("\"Kind\":\"Image\"") && !json.Contains("Animation") && back.IsAnimation && back.Frames == item.Frames && back.FramesTotal == item.FramesTotal);
        Check("gif anim: an ordinary picture is not one", !new SaverItem { Kind = SaverKind.Image }.IsAnimation);

        ScreenSpeed(busy);

        // the item goes with its file
        Check("gif anim: the saver's file is removed with it", File.Exists(item.File) && Delete(item) && !File.Exists(item.File));
    }

    static bool Delete(SaverItem item) { IdleScreens.Delete(item); return true; }

    /// <summary>The import window's choices (size, steps a second), what they give, and the best fit.</summary>
    static void Choices(string folder)
    {
        string busy = Save(folder, "choices-busy.gif", TestGifs.Busy), partial = Save(folder, "choices-partial.gif", TestGifs.Partial);
        GifSaver.MicrosecondsPerPixel = 1.9; GifSaver.FixedMs = 5; // the model's own numbers (earlier tests changed them)

        var info = AnimatedPicture.Inspect(busy);
        Check("gif choices: the window reads the GIF", info != null && info.Frames == 5 && info.Width == 100 && info.Height == 60 && Math.Abs(info.Seconds - 0.5) < 0.01 && info.FirstFrame.Length > 100,
              info == null ? "null" : $"{info.Frames} frames, {info.Width}x{info.Height}, {info.Seconds:0.00} s");
        Check("gif choices: a one-frame picture is not offered as a GIF", AnimatedPicture.Inspect(Path.Combine(folder, "one.gif")) == null);
        Check("gif choices: nor a missing file", AnimatedPicture.Inspect(Path.Combine(folder, "nope.gif")) == null);
        Check("gif choices: the size slider's numbers", info.ShownWidth(1) == 300 && info.ShownHeight(1) == 180 && info.ShownWidth(0.5) == 150, $"{info.ShownWidth(1)}x{info.ShownHeight(1)}");

        // size
        var full = AnimatedPicture.FromGif(busy, "b", needStill: false);
        var half = AnimatedPicture.FromGif(busy, "b", options: new GifOptions { Size = 0.5 }, needStill: false);
        Check("gif choices: half the size is a picture half as wide and high", full.Width == 300 && half.Width == 150 && half.Height == 90, $"{full.Width}x{full.Height} and {half.Width}x{half.Height}");
        Check("gif choices: working it out for the window makes no still picture (that's made when it's added)", full.Still == null);

        // smoothness: fewer steps a second keeps fewer frames, and says it was the choice, not the memory
        var thin = AnimatedPicture.FromGif(busy, "b", options: new GifOptions { MaxStepsPerSecond = 4 }, needStill: false);
        Check("gif choices: fewer steps a second keeps fewer frames", thin.FramesTaken < 5 && thin.FramesTaken >= 2 && thin.Frames.Count < full.Frames.Count, $"{thin.FramesTaken} of {thin.FramesTotal}");
        Check("gif choices: that is the person's choice, not the memory", !thin.MemoryLimited && !full.MemoryLimited);
        Check("gif choices: the loop keeps its length", Math.Abs(thin.LoopSeconds - full.LoopSeconds) < 0.001, $"{thin.LoopSeconds:0.000} vs {full.LoopSeconds:0.000} s");
        var cramped = AnimatedPicture.FromGif(partial, "p", budget: 14 * 1024, needStill: false);
        Check("gif choices: frames cut by the memory say so", cramped.MemoryLimited && cramped.FramesTaken < cramped.FramesTotal, $"{cramped.FramesTaken}/{cramped.FramesTotal}");

        // what it gives: the screen's load
        Check("gif choices: whole pictures every 100 ms are too much for the screen (by the model)", full.ScreenLoad > 1.0 && full.DrawableStepsPerSecond < full.StepsPerSecond, $"load {full.ScreenLoad:0.00}, {full.DrawableStepsPerSecond:0.0} of {full.StepsPerSecond:0.0} steps/s");
        Check("gif choices: smaller is lighter", half.ScreenLoad < full.ScreenLoad / 2, $"{half.ScreenLoad:0.00} vs {full.ScreenLoad:0.00}");
        var v1 = GifImportDialog.Verdict(full, out bool good1);
        var v2 = GifImportDialog.Verdict(half, out bool good2);
        Check("gif choices: the window says when it's too heavy, and what to do", !good1 && v1.Title.Contains("Too heavy") && v1.Detail.Contains("smaller"));
        Check("gif choices: and when it plays at the GIF's speed", good2 && v2.Detail == null, v2.Title);
        Check("gif choices: the numbers it shows", GifImportDialog.Lines(cramped).Any(l => l.Contains("oversized")) && GifImportDialog.Lines(thin).Any(l => l.Contains("you asked for")) && GifImportDialog.Lines(full).Any(l => l.Contains("KB of 200 KB")));

        // best fit: the biggest size that plays at the GIF's speed
        var best = AnimatedPicture.BestFit(busy, info, out var picture);
        Check("gif choices: best fit shrinks a picture the screen can't draw in time", best.Size < 1 && picture != null && picture.ScreenLoad <= 0.95, $"{best.Size:0%}, load {picture?.ScreenLoad:0.00}");
        Check("gif choices: and is the biggest that does (the next size up is too heavy)",
              AnimatedPicture.FromGif(busy, "b", options: new GifOptions { Size = best.Size + 0.05 }, needStill: false).ScreenLoad > 0.95 || best.Size >= 1);
        Check("gif choices: best fit is a size the window's slider can show (steps of 5%)", Math.Abs(best.Size * 100 % 5) < 1e-6 || Math.Abs(best.Size * 100 % 5 - 5) < 1e-6, $"{best.Size * 100}");
        var bestP = AnimatedPicture.BestFit(partial, AnimatedPicture.Inspect(partial), out var pp);
        Check("gif choices: a clip the screen draws easily stays full size", bestP.Size >= 0.7 && pp != null, $"{bestP.Size:0%}, load {pp?.ScreenLoad:0.00}");

        // adding it: the still picture is made then, and the item says whether the memory limited it
        var added = IdleScreens.SaveAnimation(thin, busy);
        var back = GifSaver.Load(added);
        Check("gif choices: adding makes the file and the still picture for a screen without memory", back != null && back.Still != null && added.IsAnimation && added.Frames == thin.FramesTaken);
        Check("gif choices: a smoothness choice is not 'oversized'", !added.IsOversized && added.MemoryLimited == false);
        var addedCramped = IdleScreens.SaveAnimation(cramped, partial);
        Check("gif choices: a memory cut is 'oversized'", addedCramped.IsOversized && addedCramped.MemoryLimited == true);
        Check("gif choices: items saved before this was kept count as oversized when frames are missing",
              new SaverItem { Kind = SaverKind.Image, Animated = true, Frames = 12, FramesTotal = 38 }.IsOversized && !new SaverItem { Kind = SaverKind.Image, Animated = true, Frames = 38, FramesTotal = 38 }.IsOversized);
        IdleScreens.Delete(added); IdleScreens.Delete(addedCramped);
        GifSaver.MicrosecondsPerPixel = 1.9;
    }

    /// <summary>The step that should show `t` seconds into the loop (the GIF's own clock).</summary>
    static int Expected(AnimatedPicture pic, double t)
    {
        double loop = pic.Frames.Sum(f => Math.Max(0.03, f.Ms / 1000.0)), pos = t % loop, acc = 0;
        for (int i = 0; i < pic.Frames.Count; i++) { acc += Math.Max(0.03, pic.Frames[i].Ms / 1000.0); if (pos < acc) return i; }
        return pic.Frames.Count - 1;
    }

    /// <summary>
    /// The screen takes time to draw a picture and the PC can't see how much: the player models it, sends a step only when the last
    /// should be drawn, and when that puts it behind the GIF's clock jumps to the step that should show now (a whole picture), so
    /// the animation keeps its speed with fewer steps (the first animation on the wheel ran at half speed: 460 x 460 steps every
    /// 200 ms, each a whole picture).
    /// </summary>
    static void ScreenSpeed(string busy)
    {
        var item = IdleScreens.ImportAnimation(busy);
        var pic = GifSaver.Load(item);
        double pixels = (double)pic.Width * pic.Height, dwell = pic.Frames[0].Ms / 1000.0;
        Check("gif speed: a clip that changes everything is whole pictures, each can stand alone",
              pic.Frames.All(f => f.Tiles.Any(t => t.W == pic.Width && t.H == pic.Height)));

        // a screen that takes 250 ms a picture, steps that last 100 ms
        GifSaver.FixedMs = 0; GifSaver.MicrosecondsPerPixel = 250000.0 / pixels;
        var saver = GifSaver.Create(item, 10, 20); saver.UseTiles = true; saver.Start();
        var rec = new Recorder();
        long skipped0 = GifSaver.StepsSkipped;
        double t0 = 100, last = -1, minGap = 99; int draws = 0, wrong = 0, prev = 0;
        for (double t = 0; t < 4.0; t += 0.0333)
        {
            saver.Step(rec, t0 + t, 24);
            int ramv = rec.Commands.Count(c => c.StartsWith("sets \"ramv: "));
            if (ramv == prev) continue;
            if (prev > 0 && last >= 0) minGap = Math.Min(minGap, t - last);   // (the first one is the picture going up)
            if (prev > 0 && saver.Current != Expected(pic, t)) wrong++;
            last = t; draws += ramv - prev; prev = ramv;
        }
        Check("gif speed: pictures go out no faster than the screen draws them", minGap >= 0.2, $"closest {minGap * 1000:0} ms apart for a 250 ms picture");
        Check("gif speed: about one step per drawing time over 4 s, not one per 100 ms", draws >= 10 && draws <= 20, $"{draws} draws");
        Check("gif speed: it skips the steps it can't draw", GifSaver.StepsSkipped > skipped0, $"{GifSaver.StepsSkipped - skipped0} skipped");
        Check("gif speed: each step drawn is the one that should show then (the animation keeps its speed)", wrong == 0, $"{wrong} drawn late");

        // a stall (the screen was busy with a file for a second): back on the GIF's clock at once
        double now = t0 + 4.0 + 1.0;
        saver.Step(rec, now, 24);
        Check("gif speed: after a stall it's on the step that should show now", saver.Current == Expected(pic, now - t0), $"step {saver.Current}, clock says {Expected(pic, now - t0)}");

        // a screen that draws at once: one step per 100 ms, none skipped
        GifSaver.MicrosecondsPerPixel = 0;
        var fast = GifSaver.Create(item, 10, 20); fast.UseTiles = true; fast.Start();
        var fr = new Recorder();
        long skipped1 = GifSaver.StepsSkipped; int fastDraws = 0, fprev = 0;
        for (double t = 0; t < 4.0; t += 0.0333)
        {
            fast.Step(fr, t0 + t, 24);
            int ramv = fr.Commands.Count(c => c.StartsWith("sets \"ramv: "));
            if (fprev > 0 && ramv > fprev) fastDraws += ramv - fprev;
            fprev = ramv;
        }
        Check("gif speed: with a screen that keeps up, every step is drawn on time", fastDraws >= 34 && GifSaver.StepsSkipped == skipped1, $"{fastDraws} draws in 4 s of 100 ms steps");

        // the hold is off unless asked for, and then wraps the big steps in a pair
        GifSaver.HoldArea = 1000; GifSaver.MicrosecondsPerPixel = 0;
        var held = GifSaver.Create(item, 10, 20); held.UseTiles = true; held.Start();
        var hr = new Recorder();
        for (double t = 0; t < 1.0; t += 1 / 30.0) held.Step(hr, t0 + t, 24);
        int stops = hr.Commands.Count(c => c == "ref_stop"), stars = hr.Commands.Count(c => c == "ref_star");
        Check("gif speed: with the hold on, big steps are wrapped in a pair", stops > 0 && stops == stars, $"{stops} holds");
        GifSaver.HoldArea = int.MaxValue;
        var plain = GifSaver.Create(item, 10, 20); plain.UseTiles = true; plain.Start();
        var pr = new Recorder();
        for (double t = 0; t < 1.0; t += 1 / 30.0) plain.Step(pr, t0 + t, 24);
        Check("gif speed: and without it no hold is sent", !pr.Commands.Any(c => c == "ref_stop" || c == "ref_star"));
    }

    static void Block()
    {
        var d = new DashDefinition { Id = "block-test", Name = "Block", Images = new Dictionary<string, string>() };
        using (var g = Gradient(120, 90))
        using (var ms = new MemoryStream())
        {
            // a texture: noise on the gradient, so neighbouring pixels differ and every one of them could be a rectangle
            var rnd = new Random(7);
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    var c = g.GetPixel(x, y);
                    int n = rnd.Next(-60, 61);
                    g.SetPixel(x, y, Color.FromArgb(Math.Max(0, Math.Min(255, c.R + n)), Math.Max(0, Math.Min(255, c.G - n)), Math.Max(0, Math.Min(255, c.B + n))));
                }
            g.Save(ms, ImageFormat.Png); d.Images["p"] = Convert.ToBase64String(ms.ToArray());
        }
        var e = new DashElement { Type = "image", Name = "p", Image = "p", X = 33, Y = 21, W = 120, H = 90, MaxColors = 64, Block = 6 };
        d.Elements.Add(e);
        Check("block: an old dash has no Block: 1 = as it was", JsonConvert.DeserializeObject<DashElement>("{\"Type\":\"image\"}").Block == 1);

        using (var fills = Draw(d, false))
        {
            // every 6 x 6 square counted from the picture's corner is one colour
            bool uniform = true;
            for (int cy = e.Y; cy < e.Y + e.H && uniform; cy += e.Block)
                for (int cx = e.X; cx < e.X + e.W && uniform; cx += e.Block)
                    uniform = Colours(fills, new Rectangle(cx, cy, Math.Min(e.Block, e.X + e.W - cx), Math.Min(e.Block, e.Y + e.H - cy))) == 1;
            Check("block: drawn with rectangles it is squares counted from its corner", uniform);

            new DashRenderer(new PreviewScreen(), d, 0, 0).CheckDetailed(out var c6);
            e.Block = 1;
            new DashRenderer(new PreviewScreen(), d, 0, 0).CheckDetailed(out var c1);
            e.Block = 6;
            Check("block: bigger pixels are fewer rectangles", c6.StaticFills * 5 < c1.StaticFills, $"{c6.StaticFills} vs {c1.StaticFills}");
        }
        // with the RAM drive nothing of it is used
        e.Block = 6; e.MaxColors = 2;
        using (var tiles = Draw(d, true))
            Check("block: from the RAM drive every pixel stays", Colours(tiles, new Rectangle(e.X, e.Y, e.W, e.H)) > 500);
    }
}
