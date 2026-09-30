using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Lights tab: a button's LED lights up while it's held. Which LED sits under which button isn't known in advance
    /// (buttons are numbered by the wheel's controller, LEDs by the firmware), so "Map the buttons" walks the wheel's button
    /// LEDs one by one (FX Pro 12, GT Neo 10): the LED is shown here (and lit on the wheel while the plugin drives it), the user presses the
    /// button at it. The small wheel then shows held buttons live, so the map can be checked without a game.
    /// </summary>
    public class ButtonLightsCard : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private readonly WheelModel model;
        private readonly WheelView view;
        /// <summary>The wheel's button lights, in mapping order.</summary>
        private readonly int[] leds;
        private readonly TextBlock status, prompt;
        private readonly Button map, skip, clear;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private int mapping = -1; // the LED being mapped, -1 = not mapping
        private readonly Action changed;

        public ButtonLightsCard(FXProRpmSyncPlugin plugin, Action changed)
        {
            this.plugin = plugin;
            this.changed = changed;
            model = plugin.ActiveModel;
            view = new WheelView(model, glow: false) { Width = 300 };
            leds = model.Leds(LedGroup.Buttons);
            if (S.ButtonLeds == null) S.ButtonLeds = new Dictionary<int, int>();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new Border
            {
                Background = Theme.B("#07080A"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 12, 10, 10),
                Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top, Child = view,
            });

            var side = new StackPanel();
            side.Children.Add(Theme.Eyebrow("Button lights"));
            var on = Theme.Switch("Light a button while it's pressed", S.PressLights, v => { S.PressLights = v; changed(); },
                "Over any lights (presets, ATSR-Hub, alerts), while the plugin drives the wheel's lights.");
            side.Children.Add(on);
            side.Children.Add(Theme.Field("Colour", new ColourField(S.PressColor, hex => { S.PressColor = hex; changed(); }), 90));
            status = new TextBlock { Foreground = Theme.Text2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
            side.Children.Add(status);
            prompt = new TextBlock { Foreground = Theme.Red, FontFamily = Theme.Display, FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            side.Children.Add(prompt);
            var buttons = new WrapPanel();
            map = Theme.Btn("Map the buttons", ToggleMapping, primary: true, icon: "");
            skip = Theme.Btn("Skip this light", () => Next(), icon: "");
            clear = Theme.Btn("Clear the map", () => { S.ButtonLeds.Clear(); changed(); Show(); }, icon: "");
            buttons.Children.Add(map); buttons.Children.Add(skip); buttons.Children.Add(clear);
            side.Children.Add(buttons);
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);
            Children.Add(grid);

            timer.Tick += (s, e) => Render();
            Loaded += (s, e) => timer.Start();
            Unloaded += (s, e) => { timer.Stop(); if (mapping >= 0) Stop(); };
            Show();
        }

        private void Show()
        {
            int n = S.ButtonLeds.Values.Distinct().Count(led => leds.Contains(led));
            bool found = plugin.Buttons?.Found == true;
            status.Text = mapping >= 0 ? "Press the wheel button at the lit light. Skip lights without a button."
                        : (n == 0 ? "Not mapped yet: map the buttons once, then pressing one lights it."
                                  : $"{n} of {leds.Length} button lights mapped. Press a button to check: it lights here.")
                          + (found ? "" : " The wheel isn't being read on USB now (USB mode on and the wheel plugged in).");
            prompt.Visibility = mapping >= 0 ? Visibility.Visible : Visibility.Collapsed;
            prompt.Text = mapping >= 0 ? $"Light {mapping + 1} of {leds.Length}: press its button" : "";
            map.Content = mapping >= 0 ? "Stop" : S.ButtonLeds.Count > 0 ? "Map again" : "Map the buttons";
            map.IsEnabled = found || mapping >= 0;
            skip.Visibility = mapping >= 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = mapping < 0 && S.ButtonLeds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleMapping()
        {
            if (mapping >= 0) { Stop(); return; }
            if (plugin.Buttons?.Found != true) { Show(); return; }
            mapping = -1;
            Next();
        }

        /// <summary>On to the next LED (or done after the last).</summary>
        private void Next()
        {
            mapping++;
            if (mapping >= leds.Length) { Stop(); return; }
            // light it on the wheel too, while the plugin drives the LEDs
            var frame = new LedColor[model.LedCount];
            for (int i = 0; i < frame.Length; i++) frame[i] = new LedColor(0, 0, 0, 1);
            frame[leds[mapping]] = new LedColor(255, 255, 255, 90);
            plugin.Usb?.TestLeds(frame, 30);
            plugin.Buttons?.Learn(b => Dispatcher.BeginInvoke(new Action(() => Learned(b))));
            Show();
        }

        private void Learned(int button)
        {
            if (mapping < 0) return;
            // one button per LED, one LED per button
            int led = leds[mapping];
            foreach (var k in S.ButtonLeds.Where(kv => kv.Value == led || kv.Key == button).Select(kv => kv.Key).ToList()) S.ButtonLeds.Remove(k);
            S.ButtonLeds[button] = led;
            changed();
            Next();
        }

        private void Stop()
        {
            mapping = -1;
            plugin.Buttons?.CancelLearn();
            plugin.Usb?.TestLeds(new LedColor[model.LedCount], 0.05); // ends the test frame
            Show();
        }

        private void Render()
        {
            var frame = new LedColor[model.LedCount];
            var (r, g, b) = LightEngine.Rgb(S.PressColor);
            if (mapping >= 0)
            {
                bool on = DateTime.Now.Millisecond < 700;
                frame[leds[mapping]] = on ? new LedColor(255, 255, 255, 90) : new LedColor(0, 0, 0, 1);
            }
            else
            {
                ulong down = plugin.Buttons?.Down ?? 0;
                foreach (var kv in S.ButtonLeds)
                    if (leds.Contains(kv.Value))
                        frame[kv.Value] = kv.Key >= 1 && kv.Key <= 64 && (down >> (kv.Key - 1) & 1) != 0
                            ? new LedColor(r, g, b, 90) : new LedColor(40, 40, 40, 30); // mapped: faintly lit
            }
            view.Show(frame);
        }
    }
}
