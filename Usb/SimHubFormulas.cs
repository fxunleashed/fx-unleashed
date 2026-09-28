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
        private readonly NCalcEngineBase engine = new NCalcEngineBase();
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
            try { if (!string.IsNullOrEmpty(directory)) engine.SetExtraJavasccriptExtensionsDirectory(directory); } catch { }
        }

        public object Eval(string bind)
        {
            if (!IsFormula(bind)) return null;
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
                failing[bind] = DateTime.UtcNow.AddSeconds(5);
                return null;
            }
        }
    }
}
