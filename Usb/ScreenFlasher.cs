using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace User.FXProRpmSync
{
    /// <summary>One header upload, kept in the settings: never put one image's header on a screen last given another's,
    /// and turning the RAM drive off uses the record that turned it on.</summary>
    public class ScreenFlashRecord
    {
        public DateTime When;
        public string ImageId;
        public string Mode;      // "ramfs" (RAM drive on) or "stock"
        public string Result;    // "sent" (no answer yet), "ok" (Update Successed), "failed"
    }

    /// <summary>
    /// The FX Pro screen's own UART update (TJC `whmi-wri`), reached through the wheel's screen pass-through, used for
    /// header-only uploads: image block 0 (128 KB) instead of the 9 MB image. Facts behind every step:
    /// FXProDashes docs/screen-header-flash.md (why block 0 alone works) and docs/screen-images.md (the upload, the
    /// recovery runbook for a screen without pages, proven on the wheel 2026-09-30).
    ///  - `whmi-wri size,baud,0` arms raw receive in 4096-byte packets; each packet is written as it arrives, and bytes
    ///    that come while it writes are lost, so we pause after each one. We can't read the screen's replies, so it goes
    ///    blind; the screen shows its own progress and then "Update Successed" or "Update Failed:check Error!".
    ///  - The screen checks the whole image against Simagic's seal afterwards: a header that doesn't belong to the image
    ///    on the screen can't pass, it ends in "Update Failed" and a screen without pages, put right by uploading the
    ///    right header (Recover).
    ///  - A screen without valid pages listens at 9600 and ignores `whmi-wri` until `com_star`; one left at its saved speed
    ///    listens at 115200. The wheel's UART is set to match through USART1's BRR (a power cycle sets 512000 again).
    /// </summary>
    internal static class ScreenFlasher
    {
        public const int Packet = 4096;
        public static readonly int[] Speeds = { 512000, 115200, 9600 };

        /// <summary>
        /// Which speed is the screen listening at? A red fill and the backlight dipped for 4 s at `baud` (the backlight is
        /// hardware PWM, so it shows even on a screen that doesn't refresh its panel). No flash writes anywhere. The wheel's
        /// UART goes back to 512000 afterwards.
        /// </summary>
        public static void Ping(string path, int baud)
        {
            using (var c = new FxConnection(path))
            using (var host = new FxHostScreen(c))
            {
                host.Take();
                Thread.Sleep(300);
                try
                {
                    c.SetScreenUart(baud);
                    Thread.Sleep(100);
                    host.Cmd("sleep=0"); host.Flush();   // asleep, it ignores everything else
                    Thread.Sleep(300);
                    host.Cmd(""); host.Flush();          // a lone terminator clears a half-received command
                    Thread.Sleep(50);
                    host.Cmd("cls 63488"); host.Cmd("dim=10"); host.Flush();
                    Thread.Sleep(4000);
                    host.Cmd("dim=100"); host.Flush();
                    Thread.Sleep(300);
                }
                finally
                {
                    try { c.SetScreenUart(512000); } catch { }
                    try { host.Release(null); } catch { }
                }
            }
        }

        /// <summary>
        /// Uploads image block 0. `screenAt`: the speed the screen listens at (512000 normally; 9600 for a screen without
        /// pages, 115200 for one left at its saved speed). `progress(text, fraction)` is called from this thread. After the
        /// last packet the screen is held (the wheel's own output kept away from it) until `holdUntil` is set or 3 minutes
        /// pass, while it checks the image.
        /// </summary>
        public static void Upload(string path, byte[] block, int screenAt, Action<string, double> progress, WaitHandle holdUntil)
        {
            if (block.Length != ScreenImage.Block0) throw new ArgumentException("not an image block 0");
            if (!Speeds.Contains(screenAt)) throw new ArgumentException("512000, 115200 or 9600");
            // At 115200 the wheel's UART (11.5 KB/s) is slower than USB: a 4 KB packet needs ~360 ms on the wire, and the
            // wheel's 5000-byte buffer holds one, so pause 450 ms. At 9600 only the command goes slowly; the data goes at
            // 512000 (whmi-wri switches the screen to the speed it names).
            int dataBaud = screenAt == 115200 ? 115200 : 512000;
            int pause = dataBaud == 115200 ? 450 : 150, firstPause = 1000;
            int packets = (block.Length + Packet - 1) / Packet;
            using (var c = new FxConnection(path))
            using (var host = new FxHostScreen(c))
            {
                host.Take();
                Thread.Sleep(300);
                try
                {
                    string cmd = "whmi-wri " + block.Length + "," + dataBaud + ",0";
                    if (screenAt == 9600)
                    {
                        // `com_star` re-enables the update hook, which a screen that never loaded its pages ignores.
                        c.SetScreenUart(9600); Thread.Sleep(100);
                        host.Cmd(""); host.Cmd("com_star"); host.Flush();
                        Thread.Sleep(300);
                        host.Cmd(cmd); host.Flush();
                        Thread.Sleep(300);           // ~32 bytes at 9600, then the screen switches
                        c.SetScreenUart(512000); Thread.Sleep(100);
                    }
                    else
                    {
                        if (screenAt == 115200) { c.SetScreenUart(115200); Thread.Sleep(100); }
                        host.Cmd(cmd); host.Flush();
                    }
                    progress("The screen is getting ready", 0);
                    Thread.Sleep(1500);

                    var chunk = new byte[61];
                    for (int p = 0; p < packets; p++)
                    {
                        int start = p * Packet, len = Math.Min(Packet, block.Length - start);
                        for (int o = 0; o < len; o += 61)
                        {
                            int n = Math.Min(61, len - o);
                            Array.Copy(block, start + o, chunk, 0, n);
                            c.ScreenBytes(chunk, n);
                        }
                        Thread.Sleep(p == 0 ? firstPause : pause);
                        progress($"Sending {p + 1} of {packets}", (p + 1.0) / packets);
                    }
                    progress("Sent. The screen is checking its image: watch it for \"Update Successed\"", 1);
                    holdUntil.WaitOne(TimeSpan.FromMinutes(3));
                }
                finally
                {
                    if (screenAt != 512000) try { c.SetScreenUart(512000); } catch { }
                    try { host.Release(null); } catch { }
                }
            }
        }

        /// <summary>
        /// The image to use for a header upload on this wheel, or why not. Only images recorded for the wheel's app, only
        /// the one this PC used last, and only while the wheel's app runs (the pass-through is the app's).
        /// </summary>
        public static ScreenImage Choose(UsbController u, UsbSettings s, out string refusal)
        {
            refusal = null;
            if (u == null || !u.WheelFound) { refusal = "The wheel isn't connected over USB."; return null; }
            if (!u.SupportedApp) { refusal = "The wheel isn't running app 1.3.11 (or is in update mode)."; return null; }
            if (!u.FirmwarePatched) { refusal = "The wheel needs the FXProDashes firmware (build 4 or later) to reach its screen."; return null; }
            var last = s.ScreenFlashes?.LastOrDefault();
            if (last != null)
            {
                var img = ScreenImage.Find(last.ImageId);
                if (img == null) { refusal = $"This PC last flashed screen image {last.ImageId}, which this version of the plugin doesn't know."; return null; }
                if (img.WheelApp != u.WheelVersion) { refusal = $"This PC last flashed the screen image for wheel app {img.WheelApp}; the wheel now runs {u.WheelVersion}."; return null; }
                return img;
            }
            var known = ScreenImage.ForWheelApp(u.WheelVersion);
            if (known.Count != 1) { refusal = $"No screen image recorded for wheel app {u.WheelVersion}."; return null; }
            return known[0];
        }

        /// <summary>
        /// The RAM drive as the records say: true = on, false = stock, null = unknown (a failed or unanswered upload). With no
        /// records the screen is taken as stock, unless the user said it was flashed already (ScreenRamDeclared).
        /// </summary>
        public static bool? RamDriveOn(UsbSettings s)
        {
            var last = s.ScreenFlashes?.LastOrDefault();
            if (last == null) return s.ScreenRamDeclared;
            if (last.Result != "ok") return null;
            return last.Mode == "ramfs";
        }

        public static void Record(UsbSettings s, ScreenImage img, string mode, string result)
        {
            if (s.ScreenFlashes == null) s.ScreenFlashes = new List<ScreenFlashRecord>();
            s.ScreenRamDeclared = false; // from here the records say what's on the screen
            s.ScreenFlashes.Add(new ScreenFlashRecord { When = DateTime.Now, ImageId = img.Id, Mode = mode, Result = result });
        }
    }
}
