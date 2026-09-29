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
        LedDevice();
        BaseSettingsTests();
    }

    static void BaseSettingsTests()
    {
        var orig = Newtonsoft.Json.Linq.JObject.Parse("{\"max_wheel_angle\":900,\"wheel_angle_limit\":900,\"total_force\":80,\"game_damper\":100}");
        var a = BaseSwitcher.Apply(orig, new BaseCarSetting { Angle = 5000, Force = 55 });
        Check("I: angle clamped, both angle fields, force set, rest kept", (int)a["max_wheel_angle"] == BaseSwitcher.MaxAngle && (int)a["wheel_angle_limit"] == BaseSwitcher.MaxAngle
                                                                          && (int)a["total_force"] == 55 && (int)a["game_damper"] == 100 && (int)orig["total_force"] == 80);
        var s = new BaseSettings { Enabled = true };
        s.Games["iRacing"] = new BaseCarSetting { Force = 70 };
        s.Cars["iRacing | car1"] = new BaseCarSetting { Angle = 540 };
        Check("I: car wins over game, game for other cars", s.For("iRacing | car1", "iRacing").Angle == 540 && s.For("iRacing | car2", "IRACING").Force == 70 && s.For("AC | x", "AC") == null);

        // the whole flow against a fake SimPro
        var servos = (Newtonsoft.Json.Linq.JObject)orig.DeepClone();
        var sets = new System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject>();
        var sim = new SimProClient();
        sim.Fake = (m, b) =>
        {
            if (m == "get_device_list") return Newtonsoft.Json.Linq.JArray.Parse("[{\"product_type\":\"wheel\",\"device_uuid\":\"w\",\"product_uuid\":\"w\"},{\"product_type\":\"base\",\"device_uuid\":\"b\",\"product_uuid\":\"b\",\"product_short_name\":\"EVO\"}]");
            if (m == "preset_get_selected_dev_config") return new Newtonsoft.Json.Linq.JObject { ["preset_uuid"] = "P1", ["config"] = new Newtonsoft.Json.Linq.JObject { ["servos"] = new Newtonsoft.Json.Linq.JObject { ["1"] = servos.DeepClone() } } };
            if (m == "preset_set_dev_config") { sets.Add(b); if ((string)b["device_uuid"] == "b" && (string)b["part_type"] == "servos") servos = (Newtonsoft.Json.Linq.JObject)b["config"].DeepClone(); return null; }
            throw new System.Exception("unexpected " + m);
        };
        int saves = 0;
        var sw = new BaseSwitcher(sim, () => s, () => saves++);
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: car 1 pushed to the base only (servos part 1)", sets.Count == 1 && (string)sets[0]["device_uuid"] == "b" && (int)sets[0]["part_id"] == 1
                                                                  && (int)servos["max_wheel_angle"] == 540 && (int)servos["total_force"] == 80 && s.Originals.ContainsKey("P1"));
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: same car again = no push", sets.Count == 1);
        sw.OnCarAsync("AC | x", "AC").Wait();
        Check("I: car without settings = the original back", sets.Count == 2 && (int)servos["max_wheel_angle"] == 900 && !sw.HasChanges);
        sw.OnCarAsync("iRacing | car2", "iRacing").Wait();
        Check("I: game setting for another car", (int)servos["total_force"] == 70 && (int)servos["max_wheel_angle"] == 900);
        servos["game_damper"] = 50; // the user edits the preset in SimPro
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: a user edit becomes the new original", (int)servos["game_damper"] == 50 && (int)servos["max_wheel_angle"] == 540);
        sw.RestoreAsync().Wait();
        Check("I: restore = the (edited) original", (int)servos["max_wheel_angle"] == 900 && (int)servos["game_damper"] == 50 && (int)servos["total_force"] == 80 && !sw.HasChanges);
        s.Enabled = false; int before = sets.Count;
        sw.OnCarAsync("iRacing | car1", "iRacing").Wait();
        Check("I: off = nothing pushed", sets.Count == before);
    }

    static void LedDevice()
    {
        var reg = new FXProLedDeviceRegistry().GetDevices().ToList();
        Check("D: one device, instances > 0", reg.Count == 1 && reg[0].MaximumInstances > 0 && reg[0].DeviceTypeID == FXProLedDeviceRegistry.DeviceTypeId);
        var red = System.Drawing.Color.Red; var none = System.Drawing.Color.Transparent;
        var strip = new System.Drawing.Color[21]; strip[0] = red; strip[3] = red; strip[20] = red;
        var f = SimHubLedDevice.ToFrame(strip, new[] { System.Drawing.Color.Blue }, new System.Drawing.Color[5], new System.Drawing.Color[0], new System.Drawing.Color[0], 1, 1, 1);
        Check("D: strip 0 = left side top (17), 3 = first rev LED (23), 20 = right side bottom (22)", f[17].R == 255 && f[23].R == 255 && f[22].R == 255 && f[37].Brightness == 0);
        Check("D: button 0 = LED 0, full brightness = 90", f[0].B == 255 && f[0].Brightness == 90);
        var half = SimHubLedDevice.ToFrame(strip, new System.Drawing.Color[0], new System.Drawing.Color[0], new System.Drawing.Color[0], new System.Drawing.Color[0], 0.5, 1, 1);
        Check("D: rpm brightness scales", half[23].Brightness == 45);
        var raw = new System.Drawing.Color[38]; raw[23] = System.Drawing.Color.Lime; raw[5] = none;
        var r = SimHubLedDevice.ToFrame(strip, new System.Drawing.Color[0], new System.Drawing.Color[0], raw, new System.Drawing.Color[0], 1, 1, 1);
        Check("D: individual LEDs override the groups", r[23].G == 255 && r[23].R == 0 && r[17].R == 255);
        if (System.Environment.GetEnvironmentVariable("UI_DEVICE") == "1")
        {
            try { var inst = reg[0].Factory(); System.Console.WriteLine("D: created " + (inst?.GetType().FullName ?? "null")); }
            catch (System.Exception ex) { System.Console.WriteLine("D: create threw " + ex); }
        }
        Check("D: device id not used by SimHub itself", !System.IO.File.ReadAllText(System.IO.Path.Combine(SimHubDir(), "SimHub.Plugins.dll"), System.Text.Encoding.Unicode)
                                                         .Contains(FXProLedDeviceRegistry.DeviceTypeId));
    }

    static string SimHubDir() => System.Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub";

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
