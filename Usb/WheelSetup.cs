using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace User.FXProRpmSync
{
    /// <summary>
    /// While the wheel isn't on USB: says why, from what SimPro and Windows see (NEXT.md K). Per wheel: the FX Pro comes
    /// over its own cable with the patched firmware; the GT Neo through the quick release when it powers up with
    /// button 3 held.
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
        private volatile bool unknownDevice;
        private volatile List<SimProClient.Wheel> onBase = new List<SimProClient.Wheel>();
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
                    var seen = new List<SimProClient.Wheel>();
                    try { seen = await simPro.FindWheels().ConfigureAwait(false); }
                    catch { } // SimPro not running: say nothing about the base
                    if (seen.Count > 0) lastOnBase = DateTime.UtcNow;
                    onBase = seen;
                    unknownDevice = UnknownUsbDevicePresent();
                }
                finally { System.Threading.Interlocked.Exchange(ref polling, 0); }
            });
        }

        /// <summary>Forget the base state (the wheel showed up on USB).</summary>
        public void Reset() { onBase = new List<SimProClient.Wheel>(); lastOnBase = DateTime.MinValue; }

        public (string State, string Detail) Describe(WheelModel model)
        {
            if (model == WheelModel.GtNeo)
            {
                if (onBase.Any(w => WheelModel.BySimPro(w.ProductUuid) == model))
                    return ("Wheel on the base", "SimPro sees the GT Neo on the base. To drive its lights from here, switch the base off and on " +
                                                 "again while holding button 3 on the wheel (keep holding it for about 2 seconds).");
                return ("Waiting for the wheel", "Switch the base on while holding button 3 on the GT Neo (keep holding it for about 2 seconds). " +
                                                 "It then shows up on USB through the quick release.");
            }
            var name = onBase.FirstOrDefault(w => WheelModel.BySimPro(w.ProductUuid) != WheelModel.GtNeo)?.Name;
            if (unknownDevice)
                return ("Unknown USB device", "Windows shows an unknown USB device. If it's the wheel, unplug its USB cable and plug it in again.");
            if (name != null)
                return ("Wheel on the base", $"SimPro sees the {name} on the base. With its USB cable in the PC, the patched firmware (build 6+) " +
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
