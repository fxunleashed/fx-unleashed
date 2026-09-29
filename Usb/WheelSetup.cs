using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    /// <summary>
    /// While the wheel isn't on USB: says why, from what SimPro and Windows see (NEXT.md K).
    ///  - SimPro lists a wheel: it's running on the base. Build 6+ restarts into USB mode ~3 s after it sees a PC on its
    ///    cable, so either the cable isn't in or the firmware is older.
    ///  - SimPro just stopped listing it: most likely that restart ("Restarting into USB mode", up to 15 s).
    ///  - Windows has an "Unknown USB Device" (VID_0000&PID_0002, descriptor request failed): the wheel may have missed the
    ///    PC's bus reset; unplugging and replugging the cable fixes it.
    /// Polls in the background (at most every 3 s), so the USB thread never waits on SimPro or WMI.
    /// </summary>
    internal sealed class WheelSetup
    {
        private readonly SimProClient simPro = new SimProClient();
        private DateTime nextPoll, lastOnBase = DateTime.MinValue;
        private volatile bool onBase, unknownDevice;
        private volatile string wheelName;
        private int polling;

        public static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(15);

        /// <summary>Starts a background check when the last one is 3 s old (call often; cheap).</summary>
        public void Poll()
        {
            if (DateTime.UtcNow < nextPoll || System.Threading.Interlocked.Exchange(ref polling, 1) == 1) return;
            nextPoll = DateTime.UtcNow.AddSeconds(3);
            Task.Run(async () =>
            {
                try
                {
                    bool seen = false;
                    try
                    {
                        var w = await simPro.FindWheel().ConfigureAwait(false);
                        seen = w != null;
                        if (seen) wheelName = w.Name;
                    }
                    catch { } // SimPro not running: say nothing about the base
                    if (seen) lastOnBase = DateTime.UtcNow;
                    onBase = seen;
                    unknownDevice = UnknownUsbDevicePresent();
                }
                finally { System.Threading.Interlocked.Exchange(ref polling, 0); }
            });
        }

        /// <summary>Forget the base state (the wheel showed up on USB).</summary>
        public void Reset() { onBase = false; lastOnBase = DateTime.MinValue; }

        public (string State, string Detail) Describe()
        {
            if (unknownDevice)
                return ("Unknown USB device", "Windows shows an unknown USB device. If it's the wheel, unplug its USB cable and plug it in again.");
            if (onBase)
                return ("Wheel on the base", $"SimPro sees the {wheelName ?? "wheel"} on the base. With its USB cable in the PC, the patched firmware (build 6+) " +
                                             "restarts it into USB mode a few seconds after it has started. Is the cable plugged in?");
            if (DateTime.UtcNow - lastOnBase < RestartWindow)
                return ("Restarting into USB mode", "The wheel left the base; waiting for it to come up on USB.");
            return ("Waiting for the wheel", "Plug the FX Pro's USB cable into the PC. Recommended: data only (5 V cut), the wheel powered by the base.");
        }

        private static bool UnknownUsbDevicePresent()
        {
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_0000&PID_0002%'"))
                using (var r = q.Get())
                    return r.Cast<ManagementBaseObject>().Any();
            }
            catch { return false; }
        }
    }
}
