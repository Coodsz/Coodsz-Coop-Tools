using System;
using System.Reflection;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // The server's side of CoopPatience (why: JoinRules.JoinWait). Coop drops a joining player who
    // applies no world replay for NetworkJoinLimits.ReplayAppliedTimeout (5 minutes), and a first
    // join compiles Realm of Thrones' terrain shaders for that long. The limit is a public static
    // readonly TimeSpan in Coop's Common, but the server runs .NET 6, which refuses to set such a
    // field by reflection once its type is initialized (and may treat it as a constant in compiled
    // code), so the client's way does not work here. Instead its one reader, Coop's own
    // OverloadedPeerManager.AbortStalledJoiners in Coop.Core (Coop's open source, not the obfuscated
    // server), gets a transpiler that reads our own field in place of Coop's (JoinWaitPatch): each
    // ldsfld and ldsflda of that field now names ours, so the check, its log line and the field's
    // other uses stay as Coop wrote them. Anything but the 2 reads Coop has today takes the patch
    // off again with a loud line. Coop.Core loads late, so this tries at install and again at
    // campaign start. COODSZ_ROT_PATIENCE=off leaves Coop's limit as it is.
    internal static class JoinPatience
    {
        internal const string ManagerType = "Coop.Core.Server.Services.Time.OverloadedPeerManager";
        internal const string MethodName = "AbortStalledJoiners";
        internal const string LimitsType = "Common.Network.NetworkJoinLimits";
        internal const string FieldName = "ReplayAppliedTimeout";

        private static readonly object Gate = new object();
        private static bool _done;

        public static void Install()
        {
            TryPatch(false);
            Coop.CampaignBegan += () => TryPatch(true);
        }

        // Also called by the offline .NET 6 proof. Never throws.
        internal static void TryPatch(bool last)
        {
            try
            {
                lock (Gate)
                {
                    if (_done)
                        return;
                    var manager = Coop.FindType(ManagerType);
                    var limits = Coop.FindType(LimitsType);
                    if (manager == null || limits == null)
                    {
                        if (!last)
                            return;
                        _done = true;
                        SubModule.Log(JoinRules.ServerPatienceMissingLine(manager == null ? ManagerType : LimitsType));
                        return;
                    }
                    _done = true;
                    var field = limits.GetField(FieldName, BindingFlags.Public | BindingFlags.Static);
                    var method = AccessTools.DeclaredMethod(manager, MethodName);
                    if (field == null || field.FieldType != typeof(TimeSpan) || method == null)
                    {
                        SubModule.Log(JoinRules.ServerPatienceMissingLine(field == null || field.FieldType != typeof(TimeSpan) ? LimitsType + "." + FieldName : ManagerType + "." + MethodName));
                        return;
                    }
                    var coop = (TimeSpan)field.GetValue(null);
                    var variable = Environment.GetEnvironmentVariable(JoinRules.PatienceVariable);
                    var on = JoinRules.ProtectionOn(variable, out var notUnderstood);
                    if (notUnderstood)
                        SubModule.Log(JoinRules.NotUnderstoodLine(JoinRules.PatienceVariable, variable, "Coop patience (server)"));
                    if (!on)
                    {
                        SubModule.Log(JoinRules.ServerPatienceOffLine(variable, coop));
                        return;
                    }
                    var wait = JoinRules.ServerJoinWait(coop);
                    var applied = JoinWaitPatch.Apply(new Harmony(MapRedirect.HarmonyId), method, field, wait, out var reads);
                    SubModule.Log(JoinRules.ServerPatienceLine(coop, wait, reads, applied));
                }
            }
            catch (Exception ex)
            {
                _done = true;
                SubModule.Log("!!! Coop patience (server): could not raise Coop's join limit (" + ex.GetType().Name + ": " + ex.Message + "), so a first join that compiles shaders for over 5 minutes is dropped.");
            }
        }

        // How many reads of Coop's field the transpiler moved last (for the offline proof).
        internal static int Reads => JoinWaitPatch.Reads;
    }
}
