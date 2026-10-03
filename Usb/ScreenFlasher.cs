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
        /// Does the screen have picture memory (the RAM drive)? Safe on any screen, at any time. The screen turns red, then a
        /// small green picture is uploaded to `ram/` and drawn in the middle: green = picture memory, red = none, no change =
        /// the screen doesn't take commands at the normal speed (no pages after an upload: Screen recovery).
        /// Why it's safe without the drive: `twfile` then fails and the screen stays in command mode (TJC simulator, CodeRun
        /// case 6), so the picture's bytes reach the command parser. The parser only runs a command at a terminator, three
        /// 0xFF bytes in a row, and this picture is checked to have no two 0xFF bytes in a row (ProbePicture; a JPEG's data
        /// escapes 0xFF as FF 00, its headers are small numbers) and to fit one packet (the packet header has only FF FE).
        /// So the bytes pile up as one unfinished command; Unstick's 4 KB of zeros, abort packet and lone terminator then end
        /// it as a single bad command, ignored. (A first version found the file with `findfile` and drew green with
        /// `fill ...,480*sys2`: the FX Pro's screen build doesn't take variables there, 2026-10-02, so it always showed red.)
        /// </summary>
        public static void ProbeRam(string path)
        {
            var jpeg = ProbePicture();
            using (var c = new FxConnection(path))
            using (var host = new FxHostScreen(c))
            {
                host.Take();
                Thread.Sleep(300);
                try
                {
                    host.Cmd("sleep=0"); host.Flush();   // asleep, it ignores everything else
                    Thread.Sleep(300);
                    host.Cmd(""); host.Flush();          // a lone terminator clears a half-received command
                    Thread.Sleep(50);
                    host.Cmd("page 0"); host.Cmd("vis 255,0"); host.Cmd("dim=100");
                    // the screen repaints its page (page 0's Check1 picture) after it receives a file: hold its refresh and
                    // draw the red again after the upload, as the plugin does while loading pictures (UsbController.Held)
                    host.Cmd("ref_stop");
                    host.Cmd("cls 63488");
                    host.Cmd("twfile \"ram/" + ProbeFile + "\"," + jpeg.Length);
                    host.Flush();
                    host.Pause(Math.Max(60, ScreenRam.ArmMs));
                    var pk = new byte[12 + jpeg.Length];
                    pk[0] = 0x3A; pk[1] = 0xA1; pk[2] = 0xBB; pk[3] = 0x44; pk[4] = 0x7F; pk[5] = 0xFF; pk[6] = 0xFE;
                    pk[7] = 0; pk[8] = 0; pk[9] = 0; pk[10] = (byte)jpeg.Length; pk[11] = (byte)(jpeg.Length >> 8);
                    Array.Copy(jpeg, 0, pk, 12, jpeg.Length);
                    host.Raw(pk);
                    host.Pause(Math.Max(80, ScreenRam.DoneMs));
                    // back to command mode whatever happened (as after every batch of pictures, ScreenRam.Unstick)
                    host.Raw(new byte[Packet]);
                    host.Pause(60);
                    host.Raw(new byte[] { 0x3A, 0xA1, 0xBB, 0x44, 0x7F, 0xFF, 0xFE, 0, 0xFF, 0xFF, 0, 0 });
                    host.Pause(60);
                    host.Raw(new byte[] { 0xFF, 0xFF, 0xFF });
                    host.Pause(60);
                    host.Cmd("cls 63488");
                    host.Cmd("sets \"ramv: " + (400 - ProbeW / 2) + ", " + (240 - ProbeH / 2) + ", ram/" + ProbeFile + "\"");
                    host.Cmd("ref_star");
                    host.Flush();
                    Thread.Sleep(5000);
                    host.Cmd("delfile \"ram/" + ProbeFile + "\"");
                    host.Cmd("delfile \"ram/" + ProbeFile + ".tm\"");
                    host.Flush();
                    host.Pause(100);                     // a delete repaints the page on the next refresh: let it pass
                }
                finally
                {
                    try { host.Cmd("ref_star"); host.Flush(); } catch { } // never leave the screen's refresh held
                    try { host.Release(); } catch { }
                }
            }
        }

        public const string ProbeFile = "fxprobe.jpg";
        private const int ProbeW = 320, ProbeH = 192;

        /// <summary>
        /// The check's picture: a plain green card, 320 x 192. Refused (throws) unless it fits one packet and has no
        /// two 0xFF bytes in a row, the guarantee that makes the check harmless on a screen without picture memory.
        /// </summary>
        public static byte[] ProbePicture()
        {
            byte[] jpeg;
            using (var bmp = new System.Drawing.Bitmap(ProbeW, ProbeH))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.FromArgb(0x00, 0xC8, 0x50)); // plain: text edges cost kilobytes and the colour is the answer
                }
                jpeg = ScreenTiles.Jpeg(bmp, 60);
            }
            if (!SafeForParser(jpeg)) throw new InvalidOperationException("the check's picture isn't safe to send (" + jpeg.Length + " bytes)");
            return jpeg;
        }

        /// <summary>One packet, and no two 0xFF bytes in a row (so never a command terminator, FF FF FF).</summary>
        public static bool SafeForParser(byte[] b)
        {
            if (b == null || b.Length == 0 || b.Length > Packet) return false;
            for (int i = 0; i + 1 < b.Length; i++) if (b[i] == 0xFF && b[i + 1] == 0xFF) return false;
            return true;
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
            if (!u.FirmwarePatched) { refusal = "The wheel needs the FX Unleashed wheel app patch (build 4 or later) to reach its screen."; return null; }
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
        /// The RAM drive: true = on, false = stock, null = unknown (a failed or unanswered upload). With no uploads from this
        /// PC it is what the user says (ScreenRamDrive: flashed before, outside the plugin); else what the last upload made,
        /// unless the user has ticked the screen has it (a flash elsewhere can come after the records).
        /// </summary>
        public static bool? RamDriveOn(UsbSettings s)
        {
            var last = s.ScreenFlashes?.LastOrDefault();
            if (last == null) return s.ScreenRamDrive;
            if (last.Result != "ok") return null;
            return s.ScreenRamDrive || last.Mode == "ramfs";
        }

        /// <summary>
        /// The user's answer to what the screen showed after the last upload ("ok" or "failed"). The picture-memory tick
        /// (ScreenRamDrive, which the dash code reads) is cleared unless the upload turned the memory on: after turning it on
        /// the screen needs a power cycle and the Test before the user ticks it.
        /// </summary>
        public static void Answer(UsbSettings s, string result)
        {
            var last = s.ScreenFlashes?.LastOrDefault();
            if (last == null || last.Result != "sent") return;
            last.Result = result;
            if (!(result == "ok" && last.Mode == "ramfs")) s.ScreenRamDrive = false;
        }

        public static void Record(UsbSettings s, ScreenImage img, string mode, string result)
        {
            if (s.ScreenFlashes == null) s.ScreenFlashes = new List<ScreenFlashRecord>();
            s.ScreenFlashes.Add(new ScreenFlashRecord { When = DateTime.Now, ImageId = img.Id, Mode = mode, Result = result });
        }
    }
}
