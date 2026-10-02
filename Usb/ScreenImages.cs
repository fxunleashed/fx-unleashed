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
