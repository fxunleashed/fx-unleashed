using SimHub.Plugins.OutputPlugins.Dash.GLCDTemplating;
using SimHub.Plugins.OutputPlugins.Dash.TemplatingCommon;
using System;
using System.Collections.Generic;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Evaluates dash bindings written as SimHub formulas ("ncalc:..." / "js:...", e.g. from imported SimHub dashes) with
    /// SimHub's own formula engine (NCalcEngineBase.ParseValue, the same call SimHub's dashes use), so they give exactly
    /// what they give in SimHub. Only works inside SimHub (it reads SimHub's live data); call it from DataUpdate.
    /// A formula that fails is remembered and skipped for 5 s, so a broken one doesn't cost every frame.
    /// </summary>
    internal sealed class SimHubFormulas
    {
        // created on first use: its constructor needs SimHub's JavascriptExtensions folder, i.e. a running SimHub
        private NCalcEngineBase engine;
        private bool unavailable;
        private readonly Dictionary<string, ExpressionValue> compiled = new Dictionary<string, ExpressionValue>();
        private readonly Dictionary<string, DateTime> failing = new Dictionary<string, DateTime>();
        private string jsDirectory;

        public static bool IsFormula(string bind) =>
            bind != null && (bind.StartsWith("ncalc:", StringComparison.OrdinalIgnoreCase) || bind.StartsWith("js:", StringComparison.OrdinalIgnoreCase));

        /// <summary>The dash's JavascriptExtensions folder (for js: formulas calling its helper functions).</summary>
        public void SetJavascriptDirectory(string directory)
        {
            if (directory == jsDirectory) return;
            jsDirectory = directory;
            try { if (!string.IsNullOrEmpty(directory) && Engine() != null) engine.SetExtraJavasccriptExtensionsDirectory(directory); } catch { }
        }

        private NCalcEngineBase Engine()
        {
            if (engine != null || unavailable) return engine;
            try { engine = new NCalcEngineBase(); }
            catch (Exception ex)
            {
                unavailable = true;
                SimHub.Logging.Current.Warn("[FXProRpmSync] SimHub's formula engine isn't available (dash formulas stay empty): " + ex.Message);
            }
            return engine;
        }

        public object Eval(string bind)
        {
            if (!IsFormula(bind) || Engine() == null) return null;
            if (failing.TryGetValue(bind, out var until) && DateTime.UtcNow < until) return null;
            try
            {
                if (!compiled.TryGetValue(bind, out var ev))
                {
                    bool js = bind.StartsWith("js:", StringComparison.OrdinalIgnoreCase);
                    ev = new ExpressionValue(bind.Substring(js ? 3 : 6), js ? Interpreter.Javascript : Interpreter.NCalc);
                    compiled[bind] = ev;
                }
                return engine.ParseValue(ev);
            }
            catch
            {
                // a formula that fails now often works a moment later (a sector time that isn't there yet): skip it only
                // briefly. 5 s here showed a sector result up to ~10 s late on the wheel (2026-10-01).
                failing[bind] = DateTime.UtcNow.AddMilliseconds(300);
                return null;
            }
        }

        /// <summary>Evaluates a formula now (no caching of failures) and says what came back or what went wrong, for tools.</summary>
        public object Test(string bind)
        {
            if (!IsFormula(bind)) return new { error = "not a formula (ncalc:... or js:...)" };
            if (Engine() == null) return new { error = "SimHub's formula engine isn't available" };
            try
            {
                bool js = bind.StartsWith("js:", StringComparison.OrdinalIgnoreCase);
                var v = engine.ParseValue(new ExpressionValue(bind.Substring(js ? 3 : 6), js ? Interpreter.Javascript : Interpreter.NCalc));
                return new { value = v is TimeSpan ts ? (object)ts.TotalSeconds : v, type = v?.GetType().Name ?? "null" };
            }
            catch (Exception ex) { return new { error = ex.GetType().Name + ": " + ex.Message }; }
        }
    }
}
