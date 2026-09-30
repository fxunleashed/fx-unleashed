using System;
using System.IO;
using System.Linq;
using System.Text;
using User.FXProRpmSync;

/// <summary>FX Pro light frames for every built-in preset (Sparkle ones left out: random) on the simulated lap, as text.</summary>
static class Golden
{
    public static string Dump(Func<LightEngine> newEngine)
    {
        var sb = new StringBuilder();
        foreach (var p in LightPresets.FxPro.Where(x => !x.Groups.Values.Any(g => g.Effect == LightEffect.Sparkle)))
        {
            var e = newEngine();
            var prof = p.Clone();
            foreach (var a in prof.Alerts) a.Enabled = true;
            for (double t = 0; t < 40; t += 0.37)
            {
                var v = SimLap.Values(t);
                v.BlueFlag = t % 7 > 6; v.CheckeredFlag = t % 23 > 21; v.PitLimiter = t % 29 > 27;
                var f = e.Render(prof, v, null, t, false);
                sb.Append(p.Id).Append(' ').Append(t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(':');
                foreach (var c in f) sb.Append(' ').Append(c.R.ToString("X2")).Append(c.G.ToString("X2")).Append(c.B.ToString("X2")).Append(c.Brightness.ToString("X2"));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
