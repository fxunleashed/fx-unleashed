using System;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>The designer server's view of the plugin: the wheel (through USB mode) and SimHub's live values.</summary>
    internal sealed class DesignerHost : IDesignerHost
    {
        private readonly FXProRpmSyncPlugin plugin;

        public DesignerHost(FXProRpmSyncPlugin plugin) { this.plugin = plugin; }

        private UsbController Usb => plugin.Usb;

        public object WheelStatus()
        {
            var u = Usb;
            var s = plugin.Settings.Usb;
            string why = !s.Enabled ? "USB mode is off (SimHub > FX Unleashed > Unleashed mode)"
                : !s.FirmwareConfirmed ? "the wheel's firmware isn't confirmed in USB mode's settings"
                : u == null || !u.WheelFound ? "the wheel isn't connected by USB"
                : !u.SupportedApp ? "the wheel's firmware isn't supported"
                : null;
            return new
            {
                available = why == null,
                why,
                state = u?.State,
                detail = u?.Detail,
                wheelVersion = u?.WheelVersion,
                previewing = u?.PreviewActive == true,
                gameRunning = u?.LiveNow != null,
                // the last LED frame sent, in the wheel's LED order (0-11 buttons, 12-16 encoders, 17-22 side, 23-37 rev)
                leds = u?.LastFrame?.Select(f => $"#{f.R:X2}{f.G:X2}{f.B:X2}/{f.Brightness}").ToArray(),
                lights = u?.LightsState,
                wheelPage = u?.WheelPage,
                wheelButtons = plugin.Buttons?.Found == true ? "found" : "not found",
            };
        }

        public void ShowOnWheel(DashDefinition dash, int left, int top)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            u.SetPreviewDash(dash, left, top);
        }

        public void StopWheelPreview() => Usb?.StopPreview();

        public void Demo(string dash)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            if (dash == null) { u.SetDemo(false); return; }
            u.SetDemo(true, dash.StartsWith("c:") ? dash.Substring(2) : dash);
        }

        public void TestLeds(string[] colours, int brightness, double seconds)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            var frame = new LedColor[LightEngine.Count];
            byte b = (byte)Math.Max(1, Math.Min(90, brightness));
            for (int i = 0; i < frame.Length && i < (colours?.Length ?? 0); i++)
            {
                if (string.IsNullOrEmpty(colours[i])) continue;
                var c = DashColors.Parse(colours[i], System.Drawing.Color.Black);
                frame[i] = new LedColor(c.R, c.G, c.B, b);
            }
            u.TestLeds(frame, seconds);
        }

        public DashValues LiveValues() => Usb?.LiveNow;

        public void DashesChanged() => Usb?.ReloadDashes();
    }
}
