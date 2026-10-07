using System;
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
    /// "Add an animated GIF": what the GIF is, two choices (how big it is on the wheel, how many steps a second at most) and what
    /// those give: frames kept, memory used, whether the wheel's screen can keep up. It opens on the best fit; every change
    /// works the result out again in the background (the same import "Add" then saves).
    /// </summary>
    internal sealed class GifImportDialog
    {
        /// <summary>The screensaver that was added, or null (cancelled).</summary>
        public SaverItem Result { get; private set; }

        /// <summary>Shows the window; returns what was added, or null.</summary>
        public static SaverItem Show(Window owner, string path, GifInfo info, bool ramOn)
        {
            var d = new GifImportDialog(path, info, ramOn);
            d.Window.Owner = owner;
            d.Window.ShowDialog();
            return d.Result;
        }

        public readonly Window Window;
        private readonly string path;
        private readonly GifInfo info;
        private readonly Slider sizeSlider, smoothSlider;
        private readonly int smoothMax;
        private readonly Border resultBox;
        private readonly StackPanel resultLines;
        private readonly TextBlock status;
        private readonly Button addButton, bestButton;
        private readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private int version;
        private bool setting;           // the sliders are being set by the program: not a choice to work out
        private AnimatedPicture latest;  // what the settings give now (null while it's being worked out)

        private GifOptions Options => new GifOptions { Size = sizeSlider.Value / 100.0, MaxStepsPerSecond = smoothSlider.Value >= smoothMax ? 0 : smoothSlider.Value };

        public GifImportDialog(string path, GifInfo info, bool ramOn)
        {
            this.path = path; this.info = info;
            Window = new Window
            {
                Title = "Add an animated GIF", Width = 640, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Theme.Page,
            };
            Theme.Apply(Window);
            Window.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Window.Close(); };
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(Theme.Title("Add an animated GIF", 17));

            // what the GIF is
            var head = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
            var thumb = new Border { Width = 150, Height = 96, Background = Brushes.Black, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0) };
            var img = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            try
            {
                var bi = new BitmapImage();
                using (var ms = new MemoryStream(info.FirstFrame)) { bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); }
                bi.Freeze();
                img.Source = bi;
            }
            catch { }
            thumb.Child = img;
            DockPanel.SetDock(thumb, Dock.Left);
            head.Children.Add(thumb);
            var about = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            about.Children.Add(new TextBlock { Text = info.Name, FontFamily = Theme.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            about.Children.Add(new TextBlock
            {
                Text = $"{info.Width} × {info.Height} px · {info.Frames} frames · {info.Seconds:0.0} s · {info.StepsPerSecond:0.#} frames a second",
                Foreground = Theme.Text2, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
            head.Children.Add(about);
            p.Children.Add(head);
            p.Children.Add(Theme.Note("The wheel's screen plays a GIF by drawing a new picture for each step, and it can only draw so many pixels a second. " +
                                      "A smaller picture, or fewer steps a second, plays at the GIF's speed; a bigger one gets choppy. " +
                                      "This starts on the best fit: change what you like and read what you'll get.", new Thickness(0, 0, 0, 12)));
            if (!ramOn)
                p.Children.Add(new TextBlock
                {
                    Text = "Picture memory is off on this wheel (Wheel tab): until it's on, the wheel shows the GIF's first frame only.",
                    Foreground = Theme.Amber, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                });

            // the two choices
            smoothMax = Math.Max(3, (int)Math.Ceiling(info.StepsPerSecond));
            var size = Theme.SliderField(20, 100, 100, 5, v => $"{v:0}% · {info.ShownWidth(v / 100)} × {info.ShownHeight(v / 100)} px", v => Changed(), 260);
            sizeSlider = (Slider)((StackPanel)size).Children[0];
            p.Children.Add(Theme.Field("Size", size, 110));
            var smooth = Theme.SliderField(2, smoothMax, smoothMax, 1,
                v => v >= smoothMax ? $"Every frame ({info.StepsPerSecond:0.#} a second)" : $"Up to {v:0} steps a second", v => Changed(), 260);
            smoothSlider = (Slider)((StackPanel)smooth).Children[0];
            smoothSlider.ToolTip = "More steps a second is smoother, and dearer for the screen and its memory";
            p.Children.Add(Theme.Field("Smoothness", smooth, 110));
            bestButton = Theme.Btn("Best fit", () => BestFit());
            bestButton.ToolTip = "The biggest picture that plays at the GIF's speed";
            bestButton.HorizontalAlignment = HorizontalAlignment.Left;
            p.Children.Add(bestButton);

            // what they give
            resultLines = new StackPanel();
            resultBox = Theme.CardBox(resultLines, 14, new Thickness(0, 4, 0, 0));
            p.Children.Add(resultBox);
            status = new TextBlock { Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            p.Children.Add(status);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(Theme.Btn("Cancel", () => Window.Close()));
            addButton = Theme.Btn("Add", () => Add(), primary: true);
            addButton.IsEnabled = false;
            buttons.Children.Add(addButton);
            foreach (Button b in buttons.Children) b.Margin = new Thickness(8, 0, 0, 0);
            p.Children.Add(buttons);
            Window.Content = p;

            debounce.Tick += (s, e) => { debounce.Stop(); Work(Options); };
            Window.Closed += (s, e) => { debounce.Stop(); version++; };
            if (info.StepsPerSecond < 2.5) smoothSlider.IsEnabled = false; // too slow a GIF to thin out
            Window.Loaded += (s, e) => BestFit();
        }

        /// <summary>A choice changed: work out what it gives, once the slider stops moving.</summary>
        private void Changed()
        {
            if (setting || Window == null || resultLines == null) return;
            Busy("Working it out…");
            debounce.Stop(); debounce.Start();
        }

        private void Busy(string text)
        {
            latest = null;
            if (addButton != null) addButton.IsEnabled = false;
            if (status != null) status.Text = "";
            resultLines.Children.Clear();
            resultLines.Children.Add(new TextBlock { Text = text, Foreground = Theme.Text2 });
        }

        /// <summary>The biggest picture that plays at the GIF's speed: set both choices to it.</summary>
        private async void BestFit()
        {
            int mine = ++version;
            debounce.Stop();
            Busy("Finding the best fit…");
            AnimatedPicture pic = null; GifOptions o = null;
            try { await Task.Run(() => { o = AnimatedPicture.BestFit(path, info, out pic); }); }
            catch (Exception ex) { if (mine == version) { Busy("Couldn't work that out: " + ex.Message); } return; }
            if (mine != version) return; // a later choice
            setting = true;
            try
            {
                sizeSlider.Value = Math.Max(sizeSlider.Minimum, Math.Min(sizeSlider.Maximum, Math.Round(o.Size * 100)));
                smoothSlider.Value = o.MaxStepsPerSecond <= 0 ? smoothMax : Math.Max(smoothSlider.Minimum, Math.Min(smoothMax, Math.Round(o.MaxStepsPerSecond)));
            }
            finally { setting = false; }
            Show(pic);
        }

        /// <summary>Works out what these choices give (in the background).</summary>
        private async void Work(GifOptions o)
        {
            int mine = ++version;
            AnimatedPicture pic = null; string error = null;
            try { pic = await Task.Run(() => AnimatedPicture.FromGif(path, info.Name, AnimatedPicture.Budget, AnimatedPicture.MaxFiles, o, needStill: false)); }
            catch (Exception ex) { error = ex.Message; }
            if (mine != version) return; // a later choice
            if (pic == null) { Busy(error == null ? "Not even the first picture fits the screen's memory at this size: make it smaller." : "Couldn't work that out: " + error); return; }
            Show(pic);
        }

        /// <summary>What the settings give, in plain words.</summary>
        private void Show(AnimatedPicture pic)
        {
            latest = pic;
            resultLines.Children.Clear();
            var verdict = Verdict(pic, out bool good);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = good ? Theme.Green : Theme.Amber, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = verdict.Title, FontFamily = Theme.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = good ? Theme.Green : Theme.Amber, VerticalAlignment = VerticalAlignment.Center });
            resultLines.Children.Add(row);
            if (verdict.Detail != null)
            {
                resultLines.Children.Add(Line(verdict.Detail, Theme.Text, 0, true));
                resultLines.Children.Add(new Border { Height = 8 });
            }
            foreach (var l in Lines(pic)) resultLines.Children.Add(Line(l, Theme.Text2, 3, false));
            resultLines.Children.Add(new TextBlock
            {
                Text = $"The wheel's drawing speed is an estimate ({GifSaver.MicrosecondsPerPixel:0.0} µs a pixel): if your screen is faster, it plays better than this says.",
                Foreground = Theme.Text3, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            });
            addButton.IsEnabled = true;
        }

        private static TextBlock Line(string text, Brush brush, double top, bool wrap) =>
            new TextBlock { Text = text, Foreground = brush, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

        /// <summary>The verdict: whether the screen keeps up with these steps, and what to do if it doesn't.</summary>
        internal static (string Title, string Detail) Verdict(AnimatedPicture pic, out bool good)
        {
            if (pic.Frames.Count < 2)
            {
                good = false;
                return ("Only one picture fits: it will not move", "Make it smaller, or ask for fewer steps a second, to fit more frames.");
            }
            if (pic.ScreenLoad <= 1.0)
            {
                good = true;
                // right speed, but a slideshow when the memory held only some of the frames: say so, and what to do about it
                return ("Plays at the GIF's speed", pic.MemoryLimited
                    ? $"It shows about {pic.StepsPerSecond:0.#} pictures a second, because only {pic.FramesTaken} of the GIF's {pic.FramesTotal} frames fit in the screen's memory at this size. A smaller size keeps more of them."
                    : null);
            }
            good = false;
            return ("Too heavy for the wheel's screen at this size",
                    $"It can draw about {pic.DrawableStepsPerSecond:0.#} of the {pic.StepsPerSecond:0.#} steps a second, so steps are skipped and it looks choppy. " +
                    "Make it smaller, or ask for fewer steps a second.");
        }

        /// <summary>The numbers under the verdict.</summary>
        internal static string[] Lines(AnimatedPicture pic)
        {
            string frames = $"Frames kept: {pic.FramesTaken} of {pic.FramesTotal}";
            if (pic.MemoryLimited) frames += " (oversized: the rest don't fit in the screen's memory)";
            else if (pic.FramesTaken < pic.FramesTotal) frames += " (evenly spaced, to keep to the steps a second you asked for)";
            return new[]
            {
                $"On the wheel: {pic.Width} × {pic.Height} px",
                frames,
                $"Steps: about {pic.StepsPerSecond:0.#} a second (the GIF has {pic.FramesTotal / Math.Max(0.03, pic.LoopSeconds):0.#}), a loop of {pic.LoopSeconds:0.0} s",
                $"Picture memory: {pic.RamBytes / 1024} KB of {AnimatedPicture.Budget / 1024} KB (the rest stays for your dashes)",
            };
        }

        private async void Add()
        {
            var pic = latest;
            if (pic == null) return;
            addButton.IsEnabled = bestButton.IsEnabled = false;
            status.Text = "Adding…";
            try
            {
                Result = await Task.Run(() => IdleScreens.SaveAnimation(pic, path));
                try { Window.DialogResult = true; } catch { Window.Close(); }
            }
            catch (Exception ex)
            {
                status.Text = "Couldn't add it: " + ex.Message;
                addButton.IsEnabled = bestButton.IsEnabled = true;
            }
        }
    }
}
