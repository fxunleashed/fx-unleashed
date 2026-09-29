using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>Checks for the later NEXT.md workstreams (G, H, C, D, I, ...), run by `UsbTest.exe OUT features`.</summary>
static class WorkstreamTests
{
    public static void Run()
    {
        WheelValues();
        Mirror();
    }

    /// <summary>Feeds ScreenMirror the way FxHostScreen does and checks what the mirror page would get.</summary>
    sealed class MirrorSink : IScreenSink
    {
        public void Cmd(string c) => ScreenMirror.Cmd(c);
        public void Flush() { }
    }

    static void Mirror()
    {
        ScreenMirror.Held(false);
        ScreenMirror.WheelDash(null);
        Check("H: nothing to show while the wheel has its screen", ScreenMirror.Png() == null);
        ScreenMirror.Held(true);
        var d = DashLibrary.Load(new System.Collections.Generic.List<string>()).First();
        var rn = new DashRenderer(new MirrorSink(), d, 10, 20);
        rn.DrawAll();
        var png = ScreenMirror.Png();
        bool drawn = false;
        if (png != null)
            using (var bmp = new System.Drawing.Bitmap(new System.IO.MemoryStream(png)))
                for (int y = 0; y < bmp.Height && !drawn; y += 7)
                    for (int x = 0; x < bmp.Width && !drawn; x += 7)
                        drawn = bmp.GetPixel(x, y).ToArgb() != System.Drawing.Color.Black.ToArgb();
        Check("H: mirror shows the drawn dash", drawn, d.Name);
        Check("H: PNG cached until the next command", ReferenceEquals(png, ScreenMirror.Png()));
        ScreenMirror.Cmd("dim=0");
        Check("H: screen off shows black", !ReferenceEquals(png, ScreenMirror.Png()));
        ScreenMirror.Held(false);
        Check("H: released = nothing", ScreenMirror.Png() == null);
        ScreenMirror.Leds(new[] { new LedColor(255, 0, 0, 90) });
        var st = Newtonsoft.Json.JsonConvert.SerializeObject(ScreenMirror.State());
        Check("H: state has the LEDs", st.Contains("#FF0000"), st);
        ScreenMirror.Leds(null);
    }

    static void WheelValues()
    {
        var missing = WheelTelemetry.Fields.Where(f => !SimProTelemetry.Fields.ContainsKey(f.Field)).Select(f => f.Field).ToList();
        Check("G: every wheel dash value is a SimPro struct field", missing.Count == 0, string.Join(", ", missing));
        Check("G: no field listed twice", WheelTelemetry.Fields.Select(f => f.Field).Distinct().Count() == WheelTelemetry.Fields.Length);
    }
}

/// <summary>Screen commands into the mirror only (UsbTest's "mirror" mode).</summary>
sealed class MirrorDemoSink : IScreenSink
{
    public void Cmd(string c) => ScreenMirror.Cmd(c);
    public void Flush() { }
}
