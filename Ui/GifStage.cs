using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The wheel's screen (800 x 480) as a picture, with an animated GIF on it where the saver puts it and at the size it will
    /// have: the import window's preview. It plays the import's result the way the wheel is told to: the real
    /// <see cref="GifSaver"/> drives the same <see cref="PreviewScreen"/> the settings page draws its previews with, so the
    /// JPEG quality, the frames left out and the steps the screen's model would skip are all in what you see. Until that
    /// result is ready (it takes a few seconds), the GIF's first frame stands in at the right size, so moving the size
    /// slider shows at once how big the picture will be.
    /// </summary>
    internal sealed class GifStage : IDisposable
    {
        public readonly FrameworkElement View;

        /// <summary>How many times the screen has been redrawn from the player (a test checks that it plays).</summary>
        public int Updates { get; private set; }

        /// <summary>The animation shows, and not just the first frame standing in.</summary>
        public bool Playing => live.Visibility == Visibility.Visible;

        private readonly int padLeft, padTop;
        private readonly Image standIn = new Image(), live = new Image();
        private readonly Rectangle outline, guide;
        private readonly WriteableBitmap target = new WriteableBitmap(DashRenderer.Width, DashRenderer.Height, 96, 96, PixelFormats.Bgr32, null);
        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(20) };
        private readonly Stopwatch clock = new Stopwatch();
        private GifSaver saver;
        private PreviewScreen screen;
        private AnimatedPicture pic;
        private int generation, seen = -1, liveW, liveH, footW, footH;
        private bool disposed;

        /// <param name="width">The preview's width on the page (its height follows the screen's 800 x 480).</param>
        /// <param name="padLeft">The padding the wheel's pictures are moved by (settings): where the saver puts the GIF.</param>
        public GifStage(double width, int padLeft, int padTop)
        {
            // the same clamps the player applies
            this.padLeft = Math.Max(0, Math.Min(padLeft, DashRenderer.Width - 790));
            this.padTop = Math.Max(0, Math.Min(padTop, DashRenderer.Height - 460));
            var canvas = new Canvas { Width = DashRenderer.Width, Height = DashRenderer.Height, Background = Brushes.Black };
            standIn.Stretch = Stretch.Fill;
            foreach (var im in new[] { standIn, live }) RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            live.Source = target; live.Width = DashRenderer.Width; live.Height = DashRenderer.Height; live.Visibility = Visibility.Collapsed;
            // the area the picture may use (790 x 460, inside the padding), and the picture's own box: both over the picture,
            // faint, so a GIF with a black background still shows where it is
            guide = new Rectangle { Width = 790, Height = 460, Stroke = Brushes.White, Opacity = 0.2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 4 }, IsHitTestVisible = false };
            Canvas.SetLeft(guide, this.padLeft); Canvas.SetTop(guide, this.padTop);
            outline = new Rectangle { Stroke = Brushes.White, Opacity = 0.6, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
            canvas.Children.Add(standIn);
            canvas.Children.Add(live);
            canvas.Children.Add(guide);
            canvas.Children.Add(outline);
            View = new Border
            {
                Background = Brushes.Black, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true,
                Child = new Viewbox { Stretch = Stretch.Uniform, Width = width, Height = width * DashRenderer.Height / DashRenderer.Width, Child = canvas },
            };
            timer.Tick += (s, e) => Tick();
        }

        /// <summary>The GIF's first frame (a PNG), shown at the picture's place until the animation is ready.</summary>
        public void SetStandIn(byte[] png)
        {
            try
            {
                var bi = new BitmapImage();
                using (var ms = new MemoryStream(png)) { bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); }
                bi.Freeze();
                standIn.Source = bi;
            }
            catch { }
        }

        /// <summary>The picture will be w x h: show where and how big at once. An animation of another size stops.</summary>
        public void SetFootprint(int w, int h)
        {
            int ox = padLeft + (790 - w) / 2, oy = padTop + (460 - h) / 2; // as GifSaver centres it
            foreach (FrameworkElement e in new FrameworkElement[] { standIn, outline })
            {
                e.Width = w; e.Height = h;
                Canvas.SetLeft(e, ox); Canvas.SetTop(e, oy);
            }
            if (w != footW || h != footH) generation++; // a result being readied for another size is no use now
            footW = w; footH = h;
            if (Playing && (w != liveW || h != liveH)) Release(); // so is the one playing
        }

        /// <summary>Back to the first frame standing in (nothing playing).</summary>
        public void Stop()
        {
            generation++;
            Release();
        }

        /// <summary>Plays this result (null: stops). The pictures are decoded first, off the UI thread, so it doesn't stutter.</summary>
        public async void Play(AnimatedPicture next)
        {
            int mine = ++generation;
            if (next == null || disposed) { Release(); return; }
            // what is playing now goes on until the new one is ready (a change of quality or smoothness keeps the picture's size)
            var player = new GifSaver(next, padLeft, padTop) { UseTiles = true, Quiet = true };
            var scr = new PreviewScreen();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in player.Tiles.FrameTiles)
                        if (t.Name != null) scr.Cmd(ScreenTiles.Ramv(t, 0, 0));
                });
            }
            catch { }
            if (mine != generation || disposed) { scr.Dispose(); GifSaver.Forget(next); return; }
            var oldScreen = screen; var oldPic = pic;
            saver = player; screen = scr; pic = next; liveW = next.Width; liveH = next.Height;
            player.Start();
            seen = -1;
            clock.Restart();
            timer.Start();
            oldScreen?.Dispose();
            if (oldPic != null) GifSaver.Forget(oldPic);
        }

        private void Release()
        {
            timer.Stop();
            live.Visibility = Visibility.Collapsed;
            standIn.Visibility = Visibility.Visible;
            var s = screen; var p = pic;
            screen = null; saver = null; pic = null; liveW = liveH = 0;
            s?.Dispose();
            if (p != null) GifSaver.Forget(p);
        }

        private void Tick()
        {
            var player = saver; var scr = screen;
            if (player == null || scr == null) return;
            try { player.Step(scr, clock.Elapsed.TotalSeconds); } catch { return; }
            if (scr.Commands == seen) return; // nothing drawn since the last look
            seen = scr.Commands;
            var all = new System.Drawing.Rectangle(0, 0, scr.Bitmap.Width, scr.Bitmap.Height);
            var bits = scr.Bitmap.LockBits(all, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try { target.WritePixels(new Int32Rect(0, 0, all.Width, all.Height), bits.Scan0, bits.Stride * all.Height, bits.Stride); }
            finally { scr.Bitmap.UnlockBits(bits); }
            Updates++;
            if (live.Visibility != Visibility.Visible) { live.Visibility = Visibility.Visible; standIn.Visibility = Visibility.Collapsed; }
        }

        public void Dispose()
        {
            disposed = true;
            generation++;
            Release();
        }
    }
}
