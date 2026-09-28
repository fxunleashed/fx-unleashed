using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class P
{
    static int Main(string[] a)
    {
        string sh = @"C:\Program Files (x86)\SimHub";
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { var p = Path.Combine(sh, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        var asm = Assembly.LoadFrom(Path.Combine(sh, "ATSR_Hub_EVO.dll"));
        var fmt = asm.GetType("ATSR_Hub_EVO.ViewModel.DialogViewModels.WheelSetupViewModel+SetupPresetFormat", true);
        var conv = asm.GetType("ATSR_Hub_EVO.Services.UniversalProfileFactory.ElementInfoConverter", true);
        var tok = JObject.Parse(File.ReadAllText(a[0]))["deviceSettings"];
        object d = tok.ToObject(fmt);
        Func<string, int> I = n => (int)fmt.GetProperty(n).GetValue(d);
        var m = conv.GetMethod("ConvertFromWheelInfo", BindingFlags.NonPublic | BindingFlags.Static);
        object tuple = m.Invoke(null, new object[] { I("ButtonCount"), I("EncoderTopCount") + I("EncoderBottomCount"), I("TelemetryCount"), I("RPMCount"), fmt.GetProperty("ElementInformation").GetValue(d) });
        object layout = tuple.GetType().GetField("Item1").GetValue(tuple);
        Console.WriteLine(JsonConvert.SerializeObject(layout, Formatting.None));
        return 0;
    }
}
