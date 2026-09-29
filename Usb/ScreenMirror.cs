using System;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Screen mirror for streaming (NEXT.md H): replays every command FxHostScreen sends to the wheel on a simulated
    /// screen (PreviewScreen), so a browser source (designer server `/mirror`, `/api/wheel/frame.png`) shows what the
    /// wheel was told to draw. While the wheel shows its own dash (screen not held), the mirror can't know what's on it
    /// and says so. Also keeps the last LED frame for the page's light strip.
    /// Commands arrive on the USB thread, frames are read on the server's: everything under one lock.
    /// </summary>
    internal static class ScreenMirror
    {
        private static readonly object lk = new object();
        private static PreviewScreen screen = new PreviewScreen();
        private static bool held, dark;
        private static long version;
        private static byte[] png;
        private static long pngVersion = -1;
        private static LedColor[] leds;
        private static string wheelDash; // the wheel's own dash on the screen (page id), or null

        /// <summary>A command as sent to the wheel (after FxHostScreen's own filtering).</summary>
        public static void Cmd(string cmd)
        {
            lock (lk)
            {
                if (cmd.StartsWith("dim=", StringComparison.Ordinal)) dark = cmd == "dim=0";
                else if (cmd == "page 0") { } // the plugin's canvas page; its drawing follows
                else screen.Cmd(cmd);
                version++;
            }
        }

        /// <summary>The plugin took the screen (true) or gave it back to the wheel's own dash (false).</summary>
        public static void Held(bool on)
        {
            lock (lk)
            {
                held = on;
                if (on) { screen.Dispose(); screen = new PreviewScreen(); dark = false; }
                version++;
            }
        }

        /// <summary>The wheel's own dash page being shown (fed over USB), or null.</summary>
        public static void WheelDash(string page) { lock (lk) if (page != wheelDash) { wheelDash = page; version++; } }

        public static void Leds(LedColor[] frame) { lock (lk) leds = frame == null ? null : (LedColor[])frame.Clone(); }

        /// <summary>
        /// The screen as PNG (cached until the next command). While the wheel shows one of its own dashes: SimPro's
        /// preview picture of that dash (from the local SimPro install; not live values). Else null.
        /// </summary>
        public static byte[] Png()
        {
            lock (lk)
            {
                if (!held)
                {
                    var img = wheelDash == null ? null : DashCatalog.Find(wheelDash)?.ImagePath;
                    try { return img == null ? null : System.IO.File.ReadAllBytes(img); } catch { return null; }
                }
                if (pngVersion != version)
                {
                    if (dark) { using (var black = new PreviewScreen()) png = black.Png(); }
                    else png = screen.Png();
                    pngVersion = version;
                }
                return png;
            }
        }

        /// <summary>For the mirror page: who owns the screen and the 38 LEDs as #RRGGBB with brightness 0-90.</summary>
        public static object State()
        {
            lock (lk)
                return new
                {
                    held,
                    dark,
                    wheelDash = held || wheelDash == null ? null : DashCatalog.NameOf(wheelDash),
                    version,
                    leds = leds?.Select(c => new { c = $"#{c.R:X2}{c.G:X2}{c.B:X2}", b = (int)c.Brightness }).ToArray(),
                };
        }
    }
}
