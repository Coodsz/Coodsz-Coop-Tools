using System;
using System.Reflection;

namespace RealmOfThronesCoopCompat
{
    // Players' PCs only, with Realm of Thrones' map. Coop gives a blocking game thread action 30
    // seconds (Common.GameThread.BlockingTimeout) before it gives up on it. Realm of Thrones' map is
    // so large that preparing it after character creation kept the game thread busy for 81 seconds
    // in the first join test, so Coop's join steps expired ("It did not start and will not run") and
    // the client stayed on "Loading Host Campaign". This raises that wait to 10 minutes; the server's
    // own join limit is 15 minutes.
    // How: BlockingTimeout is a public static readonly TimeSpan, set here by reflection, which .NET
    // Framework allows and whose readers (EffectiveBlockingTimeout, the cap of the second wait in
    // RunningWaitLimit, the default of LimitFrameDrain) read it each time; the offline net472 proof
    // shows a blocking action queued while the game loop is busy for 45 seconds still runs. Coop's own
    // diagnostics then report a real deadlock after 10 minutes instead of 30 seconds. BCL types only.
    // COODSZ_ROT_PATIENCE=off leaves Coop's wait as it is for a test, with one line saying so.
    internal static class CoopPatience
    {
        public static readonly TimeSpan Wait = TimeSpan.FromMinutes(10);
        private const string GameThreadType = "Common.GameThread";

        private static readonly object Gate = new object();
        private static bool _done;
        private static bool _missingLogged;

        public static void Install()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
            {
                string name;
                try { name = e.LoadedAssembly.GetName().Name; }
                catch { return; }
                if (name == "Common")
                    TryRaise(false);
            };
            Coop.SessionChanged += started =>
            {
                if (started)
                    TryRaise(false);
            };
            Coop.CampaignBegan += () => TryRaise(true);
            TryRaise(false);
        }

        private static void TryRaise(bool last)
        {
            try
            {
                lock (Gate)
                {
                    if (_done)
                        return;
                    Type gameThread;
                    try { gameThread = Coop.FindType(GameThreadType); }
                    catch { gameThread = null; }
                    if (gameThread == null)
                    {
                        if (last && !_missingLogged && Coop.FindType("GameInterface.ContainerProvider") != null)
                        {
                            _missingLogged = true;
                            SubModule.Log("Coop patience: " + GameThreadType + " not found; a join may time out while Realm of Thrones' map is prepared.");
                        }
                        return;
                    }
                    _done = true;
                    var variable = Environment.GetEnvironmentVariable(JoinRules.PatienceVariable);
                    var on = JoinRules.ProtectionOn(variable, out var notUnderstood);
                    if (notUnderstood)
                        SubModule.Log(JoinRules.NotUnderstoodLine(JoinRules.PatienceVariable, variable, "Coop patience"));
                    if (!on)
                    {
                        SubModule.Log(JoinRules.PatienceOffLine(variable, OwnWaitText(gameThread)));
                        return;
                    }
                    SubModule.Log(Raise(gameThread, Wait));
                    SubModule.Log(RaiseJoinCatchUp(gameThread.Assembly.GetType(JoinLimitsType, false), JoinRules.JoinWait));
                }
            }
            catch (Exception ex)
            {
                _done = true;
                SubModule.Log("Coop patience: could not raise Coop's blocking game thread wait (" + ex.GetType().Name + ": " + ex.Message + "); a join may time out while Realm of Thrones' map is prepared.");
            }
        }

        // Raises GameThread.BlockingTimeout to at least wait and says what the wait is now, read back
        // through the instance's EffectiveBlockingTimeout. Never throws.
        internal static string Raise(Type gameThread, TimeSpan wait)
        {
            try
            {
                var field = gameThread.GetField("BlockingTimeout", BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(TimeSpan))
                    return "Coop patience: " + gameThread.FullName + ".BlockingTimeout not found; a join may time out while Realm of Thrones' map is prepared.";
                var before = (TimeSpan)field.GetValue(null);
                if (before < wait)
                    field.SetValue(null, wait);
                var now = Effective(gameThread) ?? (TimeSpan)field.GetValue(null);
                return now >= wait
                    ? "Coop patience: Coop's blocking game thread wait is now " + Seconds(now) + " (was " + Seconds(before) + "), so a join survives Realm of Thrones' long map preparation; a real deadlock is reported that much later."
                    : "!!! Coop patience: Coop's blocking game thread wait is still " + Seconds(now) + " after setting it to " + Seconds(wait) + "; a join may time out while Realm of Thrones' map is prepared.";
            }
            catch (Exception ex)
            {
                return "Coop patience: could not raise Coop's blocking game thread wait (" + ex.GetType().Name + ": " + ex.Message + "); a join may time out while Realm of Thrones' map is prepared.";
            }
        }

        // The wait Coop applies to new blocking work now, or null when it cannot be read.
        internal static TimeSpan? Effective(Type gameThread)
        {
            try
            {
                var instance = gameThread?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return instance == null ? (TimeSpan?)null : gameThread.GetProperty("EffectiveBlockingTimeout")?.GetValue(instance, null) as TimeSpan?;
            }
            catch
            {
                return null;
            }
        }

        // While a join's replay is applied, Coop's CampaignState sets the game thread's blocking wait
        // to NetworkJoinLimits.ReplayAppliedTimeout (5 minutes; GameThread.LimitFrameDrain), above
        // whatever BlockingTimeout says, so a first join that compiles shaders for longer would see
        // its blocking work expire. Raised the same way as BlockingTimeout, before CampaignState
        // reads it. Never throws.
        internal const string JoinLimitsType = "Common.Network.NetworkJoinLimits";

        internal static string RaiseJoinCatchUp(Type limits, TimeSpan wait)
        {
            try
            {
                var field = limits?.GetField("ReplayAppliedTimeout", BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(TimeSpan))
                    return "Coop patience: " + JoinLimitsType + ".ReplayAppliedTimeout not found; while a join catches up Coop keeps its own wait.";
                var before = (TimeSpan)field.GetValue(null);
                if (before < wait)
                    field.SetValue(null, wait);
                return JoinRules.ClientJoinWaitLine(before, (TimeSpan)field.GetValue(null));
            }
            catch (Exception ex)
            {
                return "Coop patience: could not raise Coop's wait while a join catches up (" + ex.GetType().Name + ": " + ex.Message + ").";
            }
        }

        // Coop's wait as it stands, untouched: the instance's, else the static field's.
        private static string OwnWaitText(Type gameThread)
        {
            try
            {
                var wait = Effective(gameThread) ?? gameThread.GetField("BlockingTimeout", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as TimeSpan?;
                return wait == null ? "unreadable" : Seconds(wait.Value);
            }
            catch
            {
                return "unreadable";
            }
        }

        // For the join probe's session start line.
        public static string CurrentWaitText()
        {
            var wait = Effective(Coop.FindType(GameThreadType));
            return wait == null ? "unreadable" : Seconds(wait.Value);
        }

        private static string Seconds(TimeSpan span) => ((int)span.TotalSeconds) + " s";
    }
}
