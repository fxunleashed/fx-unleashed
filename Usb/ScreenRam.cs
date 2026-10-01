using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>What the plugin put on the screen's RAM drive, kept in the settings (it survives a SimHub restart while
    /// the wheel stays powered; the token tells).</summary>
    public class ScreenRamState
    {
        /// <summary>Written to the wheel's marker word (FxUsb.MarkerWord, echoed in its status): still there on reconnect
        /// = the wheel kept power, so the screen still has the files. 128-255 (never a build number); 0 = none yet.</summary>
        public int Token;
        /// <summary>File name -> size in bytes.</summary>
        public Dictionary<string, int> Files = new Dictionary<string, int>();
    }

    /// <summary>
    /// The screen's RAM drive (needs the RAM-drive screen image, FXProDashes docs/screen-images.md): uploads dash tiles
    /// with `twfile` (blind: the wheel doesn't pass the screen's replies on, so each step waits long enough), keeps them
    /// within the drive (the least recently used out first, never the shown dash's), forgets them when the wheel lost
    /// power. Runs on the USB thread.
    /// </summary>
    internal sealed class ScreenRam
    {
        /// <summary>The drive's size in the RAM-drive image (header 0x48).</summary>
        public const int Drive = 0x60000;
        /// <summary>What the plugin fills: the drive less room for a file's temporary copy (`twfile` writes NAME.tm, then
        /// renames) and the drive's own entries. Never more: a `twfile` the drive can't take would leave the file's data
        /// to the screen's command parser.</summary>
        public const int Budget = Drive - 48 * 1024;
        public const int Packet = 4096;

        // Waits, in ms. On the wheel (2026-09-30, rotation of 84 files): 60/30/60 and 30/15/40 clean (16 s); 15/5/20 lost
        // files (holes in the dash, the screen swallowing commands until Unstick); 0/0/0 stuck the screen until a power
        // cycle. Background preloading doubles them.
        public static int ArmMs = 30, PacketMs = 15, DoneMs = 40;

        private readonly Func<ScreenRamState> state;
        private readonly Dictionary<string, double> lastUsed = new Dictionary<string, double>();
        private static readonly Random rnd = new Random();

        public ScreenRam(Func<ScreenRamState> state) { this.state = state; }

        private ScreenRamState S => state();

        public bool Has(string name) => S.Files.ContainsKey(name);
        public int Used => S.Files.Values.Sum();
        public int Count => S.Files.Count;

        /// <summary>The wheel lost power (or we can't tell): nothing is on the screen.</summary>
        public void Forget() { S.Files.Clear(); lastUsed.Clear(); }

        /// <summary>The wheel's echoed marker byte matches our token (it kept power since we wrote it).</summary>
        public bool TokenMatches(int marker) => S.Token >= 128 && marker == S.Token;

        /// <summary>The token to write after connecting (a new one after a power loss).</summary>
        public int Token()
        {
            if (S.Token < 128 || S.Files.Count == 0) S.Token = 128 + rnd.Next(128);
            return S.Token;
        }

        public void Touch(IEnumerable<string> names, double now) { foreach (var n in names) lastUsed[n] = now; }

        /// <summary>
        /// Back to the screen's command mode whatever state an upload left it in (blind uploads: a command or packet the
        /// screen missed leaves it waiting for file data, swallowing everything after; seen with no waits at all).
        /// 4 KB of zeros finish a half-received packet; after the screen's 16 ms re-sync gap, an empty packet with id
        /// FFFF ends the upload (Usart StopcomdataRec); a lone terminator then ends whatever the command parser got (in
        /// command mode all of this is one bad command, ignored). The half-written file is deleted.
        /// </summary>
        public void Unstick(FxHostScreen screen, string lastName)
        {
            screen.Flush();
            screen.Raw(new byte[Packet]);
            screen.Pause(Math.Max(40, DoneMs));
            screen.Raw(new byte[] { 0x3A, 0xA1, 0xBB, 0x44, 0x7F, 0xFF, 0xFE, 0, 0xFF, 0xFF, 0, 0 });
            screen.Pause(Math.Max(40, DoneMs));
            screen.Raw(new byte[] { 0xFF, 0xFF, 0xFF });
            if (lastName != null) screen.Cmd("delfile \"ram/" + lastName + ".tm\"");
            screen.Flush();
            screen.Pause(Math.Max(40, DoneMs)); // a delete repaints the page on the next refresh (see Delete)
        }

        /// <summary>Deletes one file from the screen.</summary>
        public void Delete(FxHostScreen screen, string name)
        {
            screen.Cmd("delfile \"ram/" + name + "\"");
            screen.Flush();
            // the screen repaints its page (page 0's "Check1" picture) after a delete on its next refresh, not right
            // away: drawing straight after it had the repaint land behind the dash (the RAM test, 2026-10-01)
            screen.Pause(Math.Max(40, DoneMs));
            S.Files.Remove(name); lastUsed.Remove(name);
        }

        /// <summary>
        /// Puts a tile on the screen (the screen must be held: nothing else may be sent while it receives). Makes room
        /// first by deleting the least recently used files not in `keep`. False if it can't fit.
        /// </summary>
        public bool Upload(FxHostScreen screen, ScreenTile t, ICollection<string> keep, double now)
        {
            int size = t.Jpeg.Length;
            if (size > Budget) return false;
            while (Used + size > Budget)
            {
                var victim = S.Files.Keys.Where(k => !keep.Contains(k))
                    .OrderBy(k => lastUsed.TryGetValue(k, out var u) ? u : double.NegativeInfinity).FirstOrDefault();
                if (victim == null) return false;
                screen.Cmd("delfile \"ram/" + victim + "\"");
                S.Files.Remove(victim); lastUsed.Remove(victim);
            }
            screen.Flush();
            S.Files.Remove(t.Name); // not there until it's complete
            screen.Cmd("twfile \"ram/" + t.Name + "\"," + size);
            screen.Pause(ArmMs);
            for (int p = 0, o = 0; o < size; p++, o += Packet)
            {
                int len = Math.Min(Packet, size - o);
                var pk = new byte[12 + len];
                pk[0] = 0x3A; pk[1] = 0xA1; pk[2] = 0xBB; pk[3] = 0x44; pk[4] = 0x7F; pk[5] = 0xFF; pk[6] = 0xFE;
                pk[7] = 0; // no CRC
                pk[8] = (byte)p; pk[9] = (byte)(p >> 8); pk[10] = (byte)len; pk[11] = (byte)(len >> 8);
                Array.Copy(t.Jpeg, o, pk, 12, len);
                screen.Raw(pk);
                screen.Pause(PacketMs);
            }
            screen.Pause(DoneMs);
            S.Files[t.Name] = size;
            lastUsed[t.Name] = now;
            return true;
        }
    }
}
