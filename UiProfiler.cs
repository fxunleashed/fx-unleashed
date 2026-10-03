using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Diagnostics: times every operation on SimHub's UI thread (ours, SimHub's and other plugins') and logs what takes the
    /// time, every 10 s, plus any single operation over 50 ms at once. A timer's tick is named after its handlers. Also probes
    /// how long input waits for the UI thread. On only while `PluginsData\Common\FXProRpmSync\ui-profile.on` exists.
    /// </summary>
    internal static class UiProfiler
    {
        private static readonly FieldInfo MethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ArgsField = typeof(DispatcherOperation).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TickField = typeof(DispatcherTimer).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<DispatcherOperation, long> started = new Dictionary<DispatcherOperation, long>();
        private static readonly Dictionary<string, (double Ms, int Count, double Max)> totals = new Dictionary<string, (double, int, double)>();
        private static readonly List<double> probes = new List<double>();
        private static long windowStart;
        private static bool on;
        public static bool On => on;

        private static readonly Stopwatch lap = new Stopwatch();

        /// <summary>Logs the time since the last Lap (null starts the laps), while the profiler is on.</summary>
        public static void Lap(string what)
        {
            if (!on) return;
            if (what != null) SimHub.Logging.Current.Info($"[FXProRpmSync] UI lap {what}: {lap.ElapsedMilliseconds} ms");
            lap.Restart();
        }

        /// <summary>Logs how long the block took (`using (UiProfiler.Time("..."))`), while the profiler is on.</summary>
        public static IDisposable Time(string what) => on ? new Timer(what) : null;

        private sealed class Timer : IDisposable
        {
            private readonly string what;
            private readonly Stopwatch sw = Stopwatch.StartNew();
            public Timer(string what) { this.what = what; }
            public void Dispose() => SimHub.Logging.Current.Info($"[FXProRpmSync] UI timing {what}: {sw.ElapsedMilliseconds} ms");
        }
        private static double windowBusy;

        public static void StartIfWanted(string folder)
        {
            try
            {
                if (!File.Exists(Path.Combine(folder, "ui-profile.on"))) return;
                var d = Application.Current?.Dispatcher;
                if (d == null) return;
                on = true;
                d.BeginInvoke(new Action(() =>
                {
                    windowStart = Stopwatch.GetTimestamp();
                    d.Hooks.OperationStarted += (s, e) => started[e.Operation] = Stopwatch.GetTimestamp();
                    d.Hooks.OperationCompleted += (s, e) => Done(e.Operation);
                    d.Hooks.OperationAborted += (s, e) => started.Remove(e.Operation);
                    var report = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromSeconds(10) };
                    report.Tick += (s, e) => Report();
                    report.Start();
                    SimHub.Logging.Current.Info($"[FXProRpmSync] UI profiler on; render tier {System.Windows.Media.RenderCapability.Tier >> 16}, process mode {System.Windows.Media.RenderOptions.ProcessRenderMode}" +
                        (Application.Current.MainWindow != null && PresentationSource.FromVisual(Application.Current.MainWindow) is System.Windows.Interop.HwndSource hs ? $", window mode {hs.CompositionTarget.RenderMode}" : ""));
                }));
                var probe = new Thread(() => Probe(d)) { IsBackground = true, Name = "FXProRpmSync UI probe" };
                probe.Start();
            }
            catch (Exception ex) { SimHub.Logging.Current.Info("[FXProRpmSync] UI profiler: " + ex.Message); }
        }

        private static void Probe(Dispatcher d)
        {
            while (true)
            {
                Thread.Sleep(100);
                long t0 = Stopwatch.GetTimestamp();
                try { d.Invoke(new Action(() => { }), DispatcherPriority.Input); } catch { return; }
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                lock (probes) probes.Add(ms);
            }
        }

        private static void Done(DispatcherOperation op)
        {
            if (!started.TryGetValue(op, out long t0)) return;
            started.Remove(op);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            windowBusy += ms;
            string name = Name(op);
            totals.TryGetValue(name, out var v);
            totals[name] = (v.Ms + ms, v.Count + 1, Math.Max(v.Max, ms));
            if (ms > 50) SimHub.Logging.Current.Info($"[FXProRpmSync] UI slow op {ms:0} ms: {name}");
        }

        private static string Name(DispatcherOperation op)
        {
            try
            {
                var del = MethodField?.GetValue(op) as Delegate;
                if (del == null) return "?";
                if (del.Target is DispatcherTimer timer && TickField?.GetValue(timer) is Delegate tick)
                    return "timer " + timer.Interval.TotalMilliseconds + "ms: " + string.Join(" + ", tick.GetInvocationList().Select(Describe));
                // an await's continuation: name the async method it resumes
                if (del.Method.DeclaringType?.FullName?.Contains("Continuation") == true)
                {
                    var args = ArgsField?.GetValue(op);
                    if (args is Delegate inner) return "await -> " + Describe(inner) + " " + AsyncName(inner);
                    if (args is object[] arr && arr.Length > 0 && arr[0] is Delegate first) return "await -> " + Describe(first) + " " + AsyncName(first);
                    return "await (" + args?.GetType().FullName + ")";
                }
                return Describe(del);
            }
            catch { return "?"; }
        }

        private static string Describe(Delegate d)
        {
            var m = d.Method;
            var t = m.DeclaringType;
            // lambdas live in compiler classes nested in the type that wrote them
            while (t?.DeclaringType != null && t.Name.StartsWith("<")) t = t.DeclaringType;
            string asm = t?.Assembly.GetName().Name ?? "?";
            return $"[{asm}] {t?.Name}.{m.Name}";
        }

        private static string AsyncName(Delegate d)
        {
            try
            {
                // Action of MoveNextRunner / ContinuationWrapper: dig for the state machine
                object t = d.Target;
                for (int depth = 0; depth < 4 && t != null; depth++)
                {
                    var ty = t.GetType();
                    if (ty.Name.Contains("<") && ty.Name.Contains(">d__")) return "(" + ty.DeclaringType?.Name + "." + ty.Name + ")";
                    var f = ty.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                              .FirstOrDefault(x => x.Name.Contains("stateMachine") || x.Name.Contains("m_stateMachine") || x.Name == "_continuation" || x.Name == "m_continuation" || x.Name == "_innerTask");
                    if (f == null) return "(" + ty.FullName + ")";
                    t = f.GetValue(t);
                    if (t is Delegate dd) t = dd.Target;
                }
            }
            catch { }
            return "";
        }

        private static void Report()
        {
            double window = (Stopwatch.GetTimestamp() - windowStart) * 1000.0 / Stopwatch.Frequency;
            double[] p;
            lock (probes) { p = probes.ToArray(); probes.Clear(); }
            Array.Sort(p);
            string lag = p.Length == 0 ? "no probes" : $"input wait avg {p.Average():0} ms, p95 {p[(int)(p.Length * 0.95)]:0} ms, max {p[p.Length - 1]:0} ms";
            var top = totals.OrderByDescending(kv => kv.Value.Ms).Take(8)
                            .Select(kv => $"  {kv.Value.Ms,7:0} ms  x{kv.Value.Count,-4} max {kv.Value.Max,4:0}  {kv.Key}");
            SimHub.Logging.Current.Info($"[FXProRpmSync] UI thread busy {windowBusy / window * 100:0}% of {window / 1000:0} s; {lag}\n" + string.Join("\n", top));
            totals.Clear();
            windowBusy = 0;
            windowStart = Stopwatch.GetTimestamp();
        }
    }
}
