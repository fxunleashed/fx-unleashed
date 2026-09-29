using System.Windows;
using System.Windows.Controls;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Wiring, start-up and game controls (NEXT.md K): the recommended setup, what happens at power-on, what to avoid,
    /// and that games see the wheel as its own controller in USB mode. Collapsed by default on the Wheel tab.
    /// </summary>
    internal static class WiringCard
    {
        public static Border Build()
        {
            var body = new StackPanel();
            body.Children.Add(Section("Recommended wiring",
                "The base powers the wheel. The wheel's USB cable carries data only: use a 5 V-blocking USB adapter, or a hub " +
                "port with its power switch off (it cuts only the 5 V; data still flows)."));
            body.Children.Add(Section("What happens at start-up",
                "Base on → the wheel starts on the base → about 3 s later the patched firmware (build 6+) sees the PC on its " +
                "cable and restarts into USB mode → the plugin waits 6 s for it to finish starting → it takes over the screen " +
                "and lights when a game runs."));
            var warn = Section("Avoid",
                "Plugging the USB cable into a wheel on a base that is switched off. The cable's 5 V then also feeds the base " +
                "through the quick release: the wheel can turn on and off and come up with a garbled screen. With a data-only " +
                "cable this can't happen. If the screen freezes: base off and cable out (full power off), then start again.");
            body.Children.Add(warn);
            body.Children.Add(Section("Game controls in USB mode",
                "In USB mode the wheel's buttons and paddles reach the PC only through the wheel's own USB controller " +
                "(\"FX Pro\", not the base). Bind your controls to it once per game. The dash button is button 40; some games " +
                "only show 32 buttons, so bind it to a plugin action here instead (Quick controls → Wheel buttons)."));
            body.Children.Add(Section("SimPro keeps working",
                "Force feedback, base settings and firmware updates stay with SimPro Manager. The plugin only draws the " +
                "screen and drives the lights while it's in charge (a game, the demo, a screensaver or idle lights), and gives " +
                "both back to the wheel and SimPro when it stops."));

            return Theme.CardBox(new Expander
            {
                Header = new TextBlock { Text = "Wiring, start-up and game controls", FontFamily = Theme.Display, FontSize = 15 },
                Content = body,
            }, 14);
        }

        private static FrameworkElement Section(string title, string text)
        {
            var s = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            s.Children.Add(Theme.Eyebrow(title));
            s.Children.Add(Theme.Note(text, new Thickness(0, 2, 0, 0)));
            return s;
        }
    }
}
