using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>
/// Header-only screen uploads (Usb/ScreenImages.cs, Usb/ScreenFlasher.cs; FXProDashes docs/screen-header-flash.md):
/// the blocks built here must be byte for byte the ones checked offline by FXProDashes tools/tft/screen_header.py.
/// With FXPRO_STOCK_TFT set to the decrypted stock image, the blocks are also spliced into it and the screen's own
/// end check (the seal) is run.
/// </summary>
static class ScreenFlashTests
{
    public static void Run()
    {
        var all = ScreenImage.All;
        Check("screen: the image records load", all.Count >= 1 && all.All(i => i.Header?.Length == ScreenImage.HeaderBytes * 2 && i.Blocks.ContainsKey("stock") && i.Blocks.ContainsKey("ramfs")),
              $"{all.Count} records");
        var img = ScreenImage.Find("fxpro-screen-1.3.11");
        Check("screen: 1.3.11 is recorded and pairs with wheel app 1.3.11", img?.WheelApp == "1.3.11");
        if (img == null) return;

        var stock = img.Build("stock");
        var ramfs = img.Build("ramfs");
        Check("screen: stock block 0 matches its pinned sha256", ScreenImage.Sha256(stock) == img.Blocks["stock"]);
        Check("screen: ramfs block 0 matches its pinned sha256 (same as screen_header.py)", ScreenImage.Sha256(ramfs) == img.Blocks["ramfs"]);
        var diff = Enumerable.Range(0, ScreenImage.Block0).Where(i => stock[i] != ramfs[i]).ToList();
        bool allowed = diff.All(i => (i >= 0x48 && i < 0x4C) || (i >= 0xC4 && i < 0xC8) || i >= ScreenImage.Block0 - 4);
        Check("screen: ramfs differs from stock only at the RAM drive size, header CRC and fix-up word", allowed && diff.Count > 0, string.Join(",", diff.Take(12).Select(i => i.ToString("X"))));
        Check("screen: ramfs keeps the CRC register after block 0 (so Simagic's seal still matches)",
              ScreenImage.CrcWords(ramfs, 0, ramfs.Length) == ScreenImage.CrcWords(stock, 0, stock.Length));
        Check("screen: header record CRCs are right in both blocks",
              new[] { stock, ramfs }.All(b => BitConverter.ToUInt32(b, 0xC4) == ScreenImage.CrcBytes(b, 0, 0xC4) && BitConverter.ToUInt32(b, 0x18C) == ScreenImage.CrcBytes(b, 200, 0xC4)));
        Check("screen: the 'failtest' block is never built by the plugin", Throws(() => img.Build("failtest")));
        // the safe screen check (ScreenFlasher.ProbeRam): its picture must never contain a command terminator
        var probe = ScreenFlasher.ProbePicture();
        Check("screen: the check's picture fits one packet and has no FF FF (never a terminator)", ScreenFlasher.SafeForParser(probe), probe.Length + " bytes");
        Check("screen: a picture with FF FF is refused by the check", !ScreenFlasher.SafeForParser(new byte[] { 1, 0xFF, 0xFF, 2 }));
        var tampered = new ScreenImage { Id = img.Id, WheelApp = img.WheelApp, Header = "01" + img.Header.Substring(2), Blocks = img.Blocks };
        Check("screen: a header that differs from the record by one byte is refused", Throws(() => tampered.Build("stock")) && Throws(() => tampered.Build("ramfs")));

        var s = new UsbSettings();
        Check("screen: no uploads yet = picture memory off", ScreenFlasher.RamDriveOn(s) == false);
        s.ScreenRamDrive = true;
        Check("screen: no uploads yet, but the screen-has-RAM-drive tick set (flashed outside the plugin) = on", ScreenFlasher.RamDriveOn(s) == true);
        s.ScreenRamDrive = false;
        ScreenFlasher.Record(s, img, "ramfs", "sent");
        Check("screen: an unanswered upload = unknown", ScreenFlasher.RamDriveOn(s) == null);
        ScreenFlasher.Answer(s, "ok");
        Check("screen: a confirmed ramfs upload = on", ScreenFlasher.RamDriveOn(s) == true);
        Check("screen: a confirmed ramfs upload leaves the tick to the user (power cycle and Test first)", !s.ScreenRamDrive);
        s.ScreenRamDrive = true;
        ScreenFlasher.Record(s, img, "stock", "failed");
        Check("screen: a failed upload = unknown (recovery)", ScreenFlasher.RamDriveOn(s) == null);
        ScreenFlasher.Answer(s, "ok");
        Check("screen: Answer only touches an unanswered upload", s.ScreenFlashes.Last().Result == "failed" && s.ScreenRamDrive);
        ScreenFlasher.Record(s, img, "stock", "sent");
        ScreenFlasher.Answer(s, "failed");
        Check("screen: a failed answer clears the tick", s.ScreenFlashes.Last().Result == "failed" && !s.ScreenRamDrive);
        s.ScreenRamDrive = true;
        ScreenFlasher.Record(s, img, "stock", "sent");
        ScreenFlasher.Answer(s, "ok");
        Check("screen: a confirmed stock upload clears the tick", !s.ScreenRamDrive);
        Check("screen: a confirmed stock upload = off", ScreenFlasher.RamDriveOn(s) == false);
        s.ScreenRamDrive = true;
        Check("screen: ticked after a stock upload (flashed again elsewhere) = on", ScreenFlasher.RamDriveOn(s) == true);
        s.ScreenRamDrive = false;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<UsbSettings>(json);
        Check("screen: the upload records survive the settings file", back.ScreenFlashes.Count == 4 && back.ScreenFlashes[0].ImageId == img.Id && back.ScreenFlashes[3].Result == "ok");

        var full = Environment.GetEnvironmentVariable("FXPRO_STOCK_TFT");
        if (full != null && File.Exists(full))
        {
            var tft = File.ReadAllBytes(full);
            if (tft.Length > img.TftSize) Array.Resize(ref tft, (int)img.TftSize);
            Check("screen: FXPRO_STOCK_TFT is the recorded image", ScreenImage.Sha256(tft) == img.TftSha256);
            Check("screen: stock block 0 is the image's first 128 KB", tft.Take(ScreenImage.Block0).SequenceEqual(stock));
            foreach (var (mode, b) in new[] { ("stock", stock), ("ramfs", ramfs) })
            {
                var spliced = (byte[])tft.Clone();
                Array.Copy(b, spliced, b.Length);
                Check($"screen: {mode} spliced into the stock image passes the screen's seal check", SealOk(spliced));
            }
            var other = (byte[])tft.Clone();
            other[0x500000] ^= 1;
            Array.Copy(ramfs, other, ramfs.Length);
            Check("screen: ramfs on an image that differs by one bit fails the seal check (never 'Update Successed')", !SealOk(other));
        }
    }

    /// <summary>The screen's end check (FUN_0003F368): CRC of the image up to the seal, mixed with size, model and version bytes.</summary>
    static bool SealOk(byte[] img)
    {
        int n = (int)BitConverter.ToUInt32(img, 0x3C);
        uint seal = ScreenImage.CrcWords(img, 0, n - 4) ^ (uint)(n & 0xFF) ^ img[0x2E] ^ img[3];
        return seal == BitConverter.ToUInt32(img, n - 4);
    }

    static bool Throws(Action a)
    {
        try { a(); return false; } catch { return true; }
    }
}
