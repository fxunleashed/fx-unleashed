using System.Linq;
using User.FXProRpmSync;
using static FeatureTests;

/// <summary>Checks for the later NEXT.md workstreams (G, H, C, D, I, ...), run by `UsbTest.exe OUT features`.</summary>
static class WorkstreamTests
{
    public static void Run()
    {
        WheelValues();
    }

    static void WheelValues()
    {
        var missing = WheelTelemetry.Fields.Where(f => !SimProTelemetry.Fields.ContainsKey(f.Field)).Select(f => f.Field).ToList();
        Check("G: every wheel dash value is a SimPro struct field", missing.Count == 0, string.Join(", ", missing));
        Check("G: no field listed twice", WheelTelemetry.Fields.Select(f => f.Field).Distinct().Count() == WheelTelemetry.Fields.Length);
    }
}
