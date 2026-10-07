using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The FX Pro screen images we know (Resources/screen-images.json, a copy of FXProDashes tools/tft/screen-images.json):
    /// Simagic's stock header (the first 400 bytes of the image) and the sha256 of every image block 0 we may upload.
    /// A header-only flash rewrites just block 0 (one 128 KB NAND erase block: the header and 0xFF), so turning the RAM
    /// drive on or off never needs Simagic's image (FXProDashes docs/screen-header-flash.md).
    /// </summary>
    internal sealed class ScreenImage
    {
        public string Id, WheelApp, SimproFile, SfuSha256, TftSha256, Seal, Header;
        public long TftSize;
        public Dictionary<string, string> Blocks = new Dictionary<string, string>();

        public const int Block0 = 0x20000;      // 64 pages of 2 KB
        public const int HeaderBytes = 0x190;   // two 200-byte header records
        public const uint RamDrive = 0x60000;   // header 0x48; 384 KB, tested on the wheel
        private const int Fix = Block0 - 4;     // the word that keeps the image's CRC (and so Simagic's seal) unchanged

        private static List<ScreenImage> all;

        public static IReadOnlyList<ScreenImage> All
        {
            get
            {
                if (all != null) return all;
                using (var s = typeof(ScreenImage).Assembly.GetManifestResourceStream("User.FXProRpmSync.screen-images.json"))
                using (var r = new StreamReader(s))
                    all = JsonConvert.DeserializeAnonymousType(r.ReadToEnd(), new { images = new List<ScreenImage>() }).images;
                return all;
            }
        }

        public static ScreenImage Find(string id) => All.FirstOrDefault(i => i.Id == id);

        /// <summary>The images that pair with this wheel app (e.g. "1.3.11").</summary>
        public static List<ScreenImage> ForWheelApp(string app) => All.Where(i => i.WheelApp == app).ToList();

        /// <summary>
        /// Image block 0 for `mode` ("stock" or "ramfs"), checked against the pinned sha256; throws if it doesn't match,
        /// so a block that differs by one bit from what was checked offline is never sent.
        /// ramfs: the RAM drive size at 0x48, header record 0's CRC (0xC4, checked at boot), and the last word of the block
        /// set so the CRC register after block 0 equals stock's: the CRC over the whole image, and so the seal at its end
        /// that the screen checks after an upload, stay Simagic's.
        /// </summary>
        public byte[] Build(string mode)
        {
            var b = new byte[Block0];
            for (int i = 0; i < b.Length; i++) b[i] = 0xFF;
            var h = Hex(Header);
            if (h.Length != HeaderBytes) throw new InvalidDataException("header record has the wrong length");
            Array.Copy(h, b, h.Length);
            if (mode == "ramfs")
            {
                var stock = (byte[])b.Clone();
                if (BitConverter.ToUInt32(b, 0x48) != 0) throw new InvalidDataException("the stock header already has a RAM drive");
                Put(b, 0x48, RamDrive);
                Put(b, 0xC4, CrcBytes(b, 0, 0xC4));
                Put(b, Fix, 0xFFFFFFFF ^ CrcWords(b, 0, Fix) ^ CrcWords(stock, 0, Fix));
            }
            else if (mode != "stock") throw new ArgumentException("mode: stock or ramfs");
            if (!Blocks.TryGetValue(mode, out var pinned) || Sha256(b) != pinned)
                throw new InvalidDataException($"block 0 ({mode}) for {Id} doesn't match its pinned sha256");
            return b;
        }

        // ---------- the whole image (support only) ----------
        // For a screen whose image isn't the recorded one (a header upload ends in "Update Failed" every time): the whole
        // recorded image written once makes it the recorded one. Simagic's image is never in a release: support sends the
        // decrypted file to that user, the plugin keeps a checked copy here and refuses anything that isn't byte for byte
        // the record's .tft.

        public static string FullImageFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync", "screen");
        public string FullImagePath => Path.Combine(FullImageFolder, Id + ".tft");

        /// <summary>A copy of this image's whole file is in the plugin's folder (its size only; Load checks every byte).</summary>
        public bool HasFullImage
        {
            get { try { var f = new FileInfo(FullImagePath); return f.Exists && f.Length == TftSize; } catch { return false; } }
        }

        /// <summary>
        /// Why `tft` isn't this record's whole image, or null when it is. Every check the screen makes and then some: the
        /// pinned size and sha256, the header record, the declared size, both header CRCs, block 0 = the pinned stock block,
        /// and the seal the screen checks at the end of an upload.
        /// </summary>
        public string CheckFull(byte[] tft)
        {
            if (tft == null || tft.Length != TftSize) return $"wrong size ({tft?.Length ?? 0} bytes, expected {TftSize})";
            if (Sha256(tft) != TftSha256) return "not Simagic's image " + Id + " (sha256 differs)";
            var h = Hex(Header);
            for (int i = 0; i < h.Length; i++) if (tft[i] != h[i]) return "the header differs from the record";
            if (BitConverter.ToUInt32(tft, 0x3C) != TftSize) return "the declared size differs";
            if (BitConverter.ToUInt32(tft, 0xC4) != CrcBytes(tft, 0, 0xC4) || BitConverter.ToUInt32(tft, 0x18C) != CrcBytes(tft, 200, 0xC4))
                return "a header CRC is wrong";
            var b0 = new byte[Block0];
            Array.Copy(tft, b0, Block0);
            if (!Blocks.TryGetValue("stock", out var pinned) || Sha256(b0) != pinned) return "block 0 isn't the pinned stock block";
            if (!SealOk(tft)) return "the seal doesn't match (the screen would answer \"Update Failed\")";
            return null;
        }

        /// <summary>The screen's end check (FUN_0003F368): CRC of the image up to the seal, mixed with size, model and version bytes.</summary>
        public static bool SealOk(byte[] img)
        {
            int n = (int)BitConverter.ToUInt32(img, 0x3C);
            if (n < 0x200 || n > img.Length || (n & 3) != 0) return false;
            uint seal = CrcWords(img, 0, n - 4) ^ (uint)(n & 0xFF) ^ img[0x2E] ^ img[3];
            return seal == BitConverter.ToUInt32(img, n - 4);
        }

        /// <summary>The whole image from the plugin's folder, checked byte for byte; throws if it isn't the record's.</summary>
        public byte[] LoadFull()
        {
            var tft = File.ReadAllBytes(FullImagePath);
            var why = CheckFull(tft);
            if (why != null) throw new InvalidDataException("The screen image file in the plugin's folder can't be used: " + why + ". Add it again.");
            return tft;
        }

        /// <summary>
        /// Takes a whole image file the user picked: finds its record, checks it and keeps a copy in the plugin's folder
        /// (written to a temporary name first, then checked again after the move). Returns the record, or null and why not.
        /// </summary>
        public static ScreenImage ImportFull(string file, out string error)
        {
            error = null;
            try
            {
                // support sends a zip (the image + a read-me): take the entry that has a recorded image's size
                byte[] tft = null;
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var z = System.IO.Compression.ZipFile.OpenRead(file))
                    {
                        var entry = z.Entries.FirstOrDefault(e => All.Any(i => i.TftSize == e.Length));
                        if (entry == null) { error = "This zip has no screen image the plugin knows. Nothing was changed."; return null; }
                        using (var s = entry.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); tft = m.ToArray(); }
                    }
                }
                else tft = File.ReadAllBytes(file);
                var img = All.FirstOrDefault(i => i.TftSize == tft.Length);
                if (img == null) { error = "This isn't a screen image the plugin knows (its size doesn't match any). Nothing was changed."; return null; }
                var why = img.CheckFull(tft);
                if (why != null) { error = "This file can't be used: " + why + ". Nothing was changed."; return null; }
                Directory.CreateDirectory(FullImageFolder);
                var tmp = img.FullImagePath + ".tmp";
                File.WriteAllBytes(tmp, tft);
                if (File.Exists(img.FullImagePath)) File.Delete(img.FullImagePath);
                File.Move(tmp, img.FullImagePath);
                img.LoadFull();
                return img;
            }
            catch (Exception e) { error = "Couldn't add the file: " + e.Message; return null; }
        }

        public static string Sha256(byte[] data)
        {
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        // The screen's CRC (STM32 hardware CRC: poly 0x04C11DB7, init 0xFFFFFFFF, no reflection), over little-endian words
        // (image CRC, seal) or over single bytes each fed as a word (header record CRCs). FXProDashes tools/tft/tftseal.py.
        private static readonly uint[] Table = MakeTable();

        private static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++) c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;
                t[i] = c;
            }
            return t;
        }

        private static uint Shift32(uint reg)
        {
            for (int k = 0; k < 4; k++) reg = (reg << 8) ^ Table[reg >> 24];
            return reg;
        }

        public static uint CrcWords(byte[] d, int start, int length, uint reg = 0xFFFFFFFF)
        {
            for (int i = start; i < start + length; i += 4) reg = Shift32(reg ^ BitConverter.ToUInt32(d, i));
            return reg;
        }

        public static uint CrcBytes(byte[] d, int start, int length, uint reg = 0xFFFFFFFF)
        {
            for (int i = start; i < start + length; i++) reg = Shift32(reg ^ d[i]);
            return reg;
        }

        private static void Put(byte[] d, int at, uint v) => Array.Copy(BitConverter.GetBytes(v), 0, d, at, 4);

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
