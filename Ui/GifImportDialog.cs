using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// "Add an animated GIF": the wheel's screen with the GIF on it (at the size it will have, playing the way the wheel will
    /// play it), three choices (how big, how many steps a second at most, how fine a picture) and what those give: frames
    /// kept, memory used, whether the wheel's screen can keep up. It opens on the best fit; every change works the result out
    /// again in the background (the same import "Add" then saves) while the size shows at once.
    /// </summary>
    internal sealed class GifImportDialog
    {
        /// <summary>The screensaver that was added, or null (cancelled).</summary>
        public SaverItem Result { get; private set; }

        /// <summary>Shows the window; returns what was added, or null.</summary>
        public static SaverItem Show(Window owner, string path, GifInfo info, bool ramOn, int padLeft = 10, int padTop = 20)
        {
            var d = new GifImportDialog(path, info, ramOn, padLeft, padTop);
            d.Window.Owner = owner;
            d.Window.ShowDialog();
            return d.Result;
        }

        public readonly Window Window;
        /// <summary>The preview of the wheel's screen (the tests look at it).</summary>
        internal readonly GifStage Stage;
        // what the tests move and read
        internal Slider SizeSlider => sizeSlider;
        internal Slider QualitySlider => qualitySlider;
        internal AnimatedPicture Latest => latest;
        private readonly string path;
        private readonly GifInfo info;
        private readonly bool ramOn;
        private readonly Slider sizeSlider, smoothSlider, qualitySlider;
        private readonly int smoothMax;
        private readonly Border resultBox;
        private readonly StackPanel resultLines;
        private readonly TextBlock status, caption, previewNote;
        private readonly Button addButton, bestButton;
        private readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private int version;
        private bool setting;           // the sliders are being set by the program: not a choice to work out
        private AnimatedPicture latest;  // what the settings give now (null while it's being worked out)

        private GifOptions Options => new GifOptions
        {
            Size = sizeSlider.Value / 100.0,
            MaxStepsPerSecond = smoothSlider.Value >= smoothMax ? 0 : smoothSlider.Value,
            Quality = (int)qualitySlider.Value,
        };

        public GifImportDialog(string path, GifInfo info, bool ramOn, int padLeft = 10, int padTop = 20)
        {
            this.path = path; this.info = info; this.ramOn = ramOn;
            Window = new Window
            {
                Title = "Add an animated GIF", SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Theme.Page,
            };
            Theme.Apply(Window);
            Window.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Window.Close(); };
            var page = new StackPanel { Margin = new Thickness(20) };
            page.Children.Add(Theme.Title("Add an animated GIF", 17));
            var body = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            page.Children.Add(body);

            // ---- left: the wheel's screen with the GIF on it, and what that gives
            var left = new StackPanel();
            Stage = new GifStage(460, padLeft, padTop);
            Stage.SetStandIn(info.FirstFrame);
            left.Children.Add(Stage.View);
            caption = new TextBlock { Foreground = Theme.Text, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            left.Children.Add(caption);
            previewNote = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 12), TextWrapping = TextWrapping.Wrap };
            left.Children.Add(previewNote);
            resultLines = new StackPanel();
            resultBox = Theme.CardBox(resultLines, 14, new Thickness(0));
            left.Children.Add(resultBox);
            status = new TextBlock { Foreground = Theme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            left.Children.Add(status);
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            // ---- right: the GIF, the choices
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var choices = new StackPanel();
            choices.Children.Add(new TextBlock { Text = info.Name, FontFamily = Theme.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            choices.Children.Add(new TextBlock
            {
                Text = $"{info.Width} × {info.Height} px · {info.Frames} frames · {info.Seconds:0.0} s · {info.StepsPerSecond:0.#} frames a second",
                Foreground = Theme.Text2, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 10), TextWrapping = TextWrapping.Wrap,
            });
            choices.Children.Add(Theme.Note("The wheel's screen plays a GIF by drawing a new picture for each step, and it can only draw so many pixels a second: " +
                                            "a bigger picture or more steps a second get choppy. This opens on the best fit; change what you like and watch the screen.", new Thickness(0, 0, 0, 12)));
            if (!ramOn)
                choices.Children.Add(new TextBlock
                {
                    Text = "Picture memory is off on this wheel (Wheel tab): until it's on, the wheel shows the GIF's first frame only.",
                    Foreground = Theme.Amber, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                });

            smoothMax = Math.Max(3, (int)Math.Ceiling(info.StepsPerSecond));
            var size = Theme.SliderField(20, 100, 100, 5, v => $"{v:0}% · {info.ShownWidth(v / 100)} × {info.ShownHeight(v / 100)} px", v => Changed(), 190);
            sizeSlider = (Slider)((StackPanel)size).Children[0];
            sizeSlider.ToolTip = "How big the picture is on the screen: 100% fills the area the wheel allows";
            choices.Children.Add(Choice("Size", size));
            var smooth = Theme.SliderField(2, smoothMax, smoothMax, 1,
                v => v >= smoothMax ? $"Every frame ({info.StepsPerSecond:0.#} a second)" : $"Up to {v:0} steps a second", v => Changed(), 190);
            smoothSlider = (Slider)((StackPanel)smooth).Children[0];
            smoothSlider.ToolTip = "More steps a second is smoother, and dearer for the screen and its memory";
            choices.Children.Add(Choice("Smoothness", smooth));
            var quality = Theme.SliderField(1, AnimatedPicture.QualityLevels, AnimatedPicture.DefaultQuality, 1,
                v => AnimatedPicture.QualityName((int)v), v => Changed(), 190);
            qualitySlider = (Slider)((StackPanel)quality).Children[0];
            qualitySlider.ToolTip = "Lower: smaller pictures, and small changes between frames are not redrawn. The screen draws less each step and more frames fit, but the picture gets softer";
            choices.Children.Add(Choice("Quality", quality, "Lower draws less each step and fits more frames, at the cost of a softer picture. The screen shows the difference."));
            bestButton = Theme.Btn("Best fit", () => BestFit());
            bestButton.ToolTip = "The biggest picture that plays at the GIF's speed, at the quality you chose";
            bestButton.HorizontalAlignment = HorizontalAlignment.Left;
            choices.Children.Add(bestButton);
            Grid.SetRow(choices, 0);
            right.Children.Add(choices);

            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(Theme.Btn("Cancel", () => Window.Close()));
            addButton = Theme.Btn("Add", () => Add(), primary: true);
            addButton.IsEnabled = false;
            buttons.Children.Add(addButton);
            foreach (Button b in buttons.Children) b.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetRow(buttons, 2);
            right.Children.Add(buttons);
            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            Window.Content = page;

            debounce.Tick += (s, e) => { debounce.Stop(); Work(Options); };
            Window.Closed += (s, e) => { debounce.Stop(); version++; Stage.Dispose(); };
            if (info.StepsPerSecond < 2.5) smoothSlider.IsEnabled = false; // too slow a GIF to thin out
            Footprint(info.ShownWidth(1), info.ShownHeight(1));
            Window.Loaded += (s, e) => BestFit();
        }

        /// <summary>A choice: its name over the slider, and a line about it.</summary>
        private static FrameworkElement Choice(string name, FrameworkElement field, string hint = null)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            p.Children.Add(Theme.Eyebrow(name));
            p.Children.Add(field);
            if (hint != null) p.Children.Add(new TextBlock { Text = hint, Foreground = Theme.Text3, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            return p;
        }

        /// <summary>The picture will be w x h: the screen shows it at once, and says so.</summary>
        private void Footprint(int w, int h)
        {
            Stage.SetFootprint(w, h);
            caption.Text = $"{w} × {h} px picture on the wheel's {DashRenderer.Width} × {DashRenderer.Height} px screen";
        }

        /// <summary>A choice changed: show the new size at once, work out what it gives once the slider stops moving.</summary>
        private void Changed()
        {
            if (setting || Window == null || resultLines == null) return;
            Footprint(info.ShownWidth(sizeSlider.Value / 100), info.ShownHeight(sizeSlider.Value / 100));
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

        /// <summary>The biggest picture that plays at the GIF's speed: set the size and smoothness to it.</summary>
        private async void BestFit()
        {
            int mine = ++version;
            debounce.Stop();
            Busy("Finding the best fit…");
            int quality = (int)qualitySlider.Value;
            AnimatedPicture pic = null; GifOptions o = null;
            try { await Task.Run(() => { o = AnimatedPicture.BestFit(path, info, out pic, AnimatedPicture.Budget, quality); }); }
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

        /// <summary>What the settings give: on the screen, and in plain words.</summary>
        private void Show(AnimatedPicture pic)
        {
            latest = pic;
            Footprint(pic.Width, pic.Height);
            if (ramOn && pic.Frames.Count > 1)
            {
                Stage.Play(pic);
                previewNote.Text = pic.ScreenLoad <= 1.0
                    ? "Playing as the wheel will play it."
                    : "Playing as the wheel's screen would manage it (an estimate): at this size it skips steps.";
            }
            else
            {
                Stage.Stop();
                previewNote.Text = !ramOn ? "Picture memory is off: the wheel shows this first frame only." : "Only one picture fits, so it will not move.";
            }

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
