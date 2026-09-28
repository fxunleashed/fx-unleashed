using System;

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
            string why = !s.Enabled ? "USB mode is off (SimHub > FXPro RPM Sync > USB mode)"
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
            };
        }

        public void ShowOnWheel(DashDefinition dash, int left, int top)
        {
            var u = Usb ?? throw new Exception("USB mode isn't running");
            u.SetPreviewDash(dash, left, top);
        }

        public void StopWheelPreview() => Usb?.StopPreview();

        public DashValues LiveValues() => Usb?.LiveNow;

        public void DashesChanged() => Usb?.ReloadDashes();
    }
}
