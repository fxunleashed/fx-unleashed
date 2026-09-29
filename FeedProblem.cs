using SimHub.Plugins;
using System;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Standard mode with "Drive the dash from SimHub" on: flags, loudly and in plain words, when SimPro is reading the
    /// game instead of SimHub's data. SimPro keeps a game it picked until the game's process exits and a real game always
    /// beats SimGame (CLAUDE.md "No way to force SimPro onto SimGame"), so the only fix is closing the game and starting it
    /// again. Shown on every tab of the settings page, as SimHub properties and once as a SimHub toast.
    /// </summary>
    public partial class FXProRpmSyncPlugin
    {
        private readonly FeedWatch feedWatch = new FeedWatch();
        /// <summary>For the offline UI test.</summary>
        internal FeedWatch FeedWatchState => feedWatch;

        /// <summary>SimPro reads the game, not SimHub's data (debounced over two source checks, ~10 s).</summary>
        public bool FeedProblem => feedWatch.Problem;
        public string FeedProblemTitle => feedWatch.Title;
        public string FeedProblemAction => feedWatch.Action;
        public string FeedProblemDetail => feedWatch.Detail;
        /// <summary>Title and action in one line, for SimHub dashes and Stream Deck.</summary>
        public string FeedProblemText => feedWatch.Problem ? feedWatch.Title + " " + feedWatch.Action : "";

        private void RegisterFeedProblem()
        {
            this.AttachDelegate("FeedProblem", () => FeedProblem);
            this.AttachDelegate("FeedProblemText", () => FeedProblemText);
        }

        /// <summary>After every source check (every 5 s while the feed is on), and in between (only to clear).</summary>
        private void EvaluateFeedProblem(bool fromSourceCheck)
        {
            bool wanted = Settings.Feed.Enabled && Settings.Mode == WheelMode.Standard && feedOn;
            bool? onGame = SimProSourceKnown && SimProReachable ? SimProSource != null : (bool?)null;
            var change = feedWatch.Update(wanted, GameRunning, FeedRunning, onGame, fromSourceCheck, RunningGameName ?? SimProSource);
            if (change == FeedWatch.Change.Raised)
            {
                SimHub.Logging.Current.Warn($"[FXProRpmSync] SimPro is reading {SimProSource ?? RunningGameName ?? "the game"} instead of SimGame" +
                                            (feedWatch.StubDown ? " (simgame.exe isn't running)" : "") + $" at {DateTime.Now:HH:mm:ss}");
                try { PluginManager?.ToastManager?.ShowInformation("FXPro: " + FeedProblemTitle + " " + FeedProblemAction); }
                catch (Exception ex) { SimHub.Logging.Current.Debug("[FXProRpmSync] toast: " + ex.Message); }
            }
            else if (change == FeedWatch.Change.Cleared)
                SimHub.Logging.Current.Info("[FXProRpmSync] SimPro feed problem cleared (" + (GameRunning ? "SimPro is reading SimGame" : "the game closed") + ")");
        }
    }

    /// <summary>
    /// The "SimPro grabbed the game" state, fed with what the plugin knows (no SimHub, so the offline tests can drive it).
    /// Raised: feed wanted, a game running, and SimPro on that game (or simgame.exe not running) at two source checks in
    /// a row. Cleared the moment SimPro is back on SimGame or the game closes. If it comes back after the user closed the
    /// game with simgame.exe running, the message adds the full SimPro restart (the 2026-09-27 case).
    /// </summary>
    public sealed class FeedWatch
    {
        public enum Change { None, Raised, Cleared }

        public bool Problem { get; private set; }
        public bool StubDown { get; private set; }
        public bool StillAfterRestart { get; private set; }
        public string Title { get; private set; } = "";
        public string Action { get; private set; } = "";
        public string Detail { get; private set; } = "";

        private int hits;
        private bool restartPending; // the last occurrence ended with the game closing while simgame.exe ran

        /// <param name="simProOnGame">SimPro reads a real game (null = unknown: SimPro not reached / not checked yet).</param>
        public Change Update(bool wanted, bool gameRunning, bool stubRunning, bool? simProOnGame, bool fromSourceCheck, string gameName)
        {
            bool stubDown = wanted && !stubRunning;
            bool bad = wanted && gameRunning && (simProOnGame == true || stubDown);
            if (!bad)
            {
                var change = Change.None;
                if (Problem)
                {
                    restartPending = !gameRunning && stubRunning;
                    change = Change.Cleared;
                }
                else if (wanted && gameRunning && simProOnGame == false) restartPending = false; // a game on SimGame: it works
                Problem = false;
                hits = 0;
                return change;
            }
            if (!fromSourceCheck) return Change.None;
            if (Problem) { SetText(stubDown, gameName); return Change.None; }
            if (++hits < 2) return Change.None;
            StillAfterRestart = restartPending;
            SetText(stubDown, gameName);
            Problem = true;
            return Change.Raised;
        }

        private void SetText(bool stubDown, string game)
        {
            game = string.IsNullOrEmpty(game) ? "the game" : game;
            StubDown = stubDown;
            Title = "Your dash isn't showing SimHub's data.";
            if (stubDown)
            {
                Action = $"Turn 'Drive the dash from SimHub' off and on, then close {game} and start it again.";
                Detail = "SimHub's helper for SimPro (simgame.exe) isn't running, so SimPro went to the game.";
                return;
            }
            Action = $"Close {game} and start it again.";
            Detail = "SimPro chose the game before SimHub was ready. Leave 'Drive the dash from SimHub' on." +
                     (StillAfterRestart ? "\nStill showing? Fully restart SimPro Manager (tray icon > Exit), then the wheelbase." : "");
        }
    }
}
