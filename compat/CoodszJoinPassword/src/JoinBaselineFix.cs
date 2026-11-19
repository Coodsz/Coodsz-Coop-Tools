using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace CoodszJoinPassword
{
    // Coop 0.1.6 refuses the join baseline when the client has fewer active parties than the server
    // ("party count mismatch"), retries 16 times and leaves the player on "Final Synchronization".
    // Seen 2026-10-22 on a TAOM save: five garrison parties the server has active came out of the
    // client's load inactive and outside the campaign's party list ("baseline object outside local
    // collection"). The server is authoritative for the baseline, so before Coop counts, every party it
    // lists that this client knows but has dropped is put back in the campaign lists, made active and,
    // for a garrison whose town has no garrison of its own, bound to that town again. Each one is logged.
    // IsActive is a Coop auto-synced property that clients may not set, so its backing field is written.
    internal static class JoinBaselineFix
    {
        private static readonly HashSet<string> Noted = new HashSet<string>(StringComparer.Ordinal);
        private static FieldInfo _isActiveField;
        private static FieldInfo _mobilePartiesField;
        private static MethodInfo _addToListMethod;
        private static FieldInfo _objectManagerField;
        private static Action<string> _log;

        internal static void Install(Harmony harmony, Action<string> log)
        {
            Type snapshot = AccessTools.TypeByName("GameInterface.Services.MobileParties.Data.MobilePartyBehaviorSnapshot");
            MethodInfo target = snapshot == null ? null : AccessTools.Method(snapshot, "TryApplyJoinBaseline");
            _objectManagerField = snapshot == null ? null : AccessTools.Field(snapshot, "objectManager");
            _isActiveField = AccessTools.Field(typeof(MobileParty), "<IsActive>k__BackingField");
            _mobilePartiesField = AccessTools.Field(typeof(CampaignObjectManager), "_mobileParties");
            _addToListMethod = AccessTools.Method(typeof(CampaignObjectManager), "AddPartyToAppropriateList");
            string missing = target == null ? "TryApplyJoinBaseline"
                : _objectManagerField == null ? "the snapshot's object manager"
                : _isActiveField == null ? "MobileParty.IsActive"
                : _mobilePartiesField == null || _addToListMethod == null ? "the campaign party lists"
                : null;
            if (missing != null)
            {
                log("Join baseline fix not installed: " + missing + " was not found.");
                return;
            }

            _log = log;
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(JoinBaselineFix), nameof(BeforeBaseline)) { priority = Priority.First });
            log("Installed: parties in the server's join baseline that this client dropped are put back.");
        }

        private static void BeforeBaseline(object __instance, Array states)
        {
            try
            {
                CampaignObjectManager manager = Campaign.Current?.CampaignObjectManager;
                if (states == null || states.Length == 0 || manager == null) return;
                var parties = (MBList<MobileParty>)_mobilePartiesField.GetValue(manager);
                int active = 0;
                foreach (MobileParty party in parties)
                    if (party != null && party.IsActive) active++;
                if (active == states.Length) return;

                object objectManager = _objectManagerField.GetValue(__instance);
                int restored = 0, unknown = 0;
                foreach (object state in (IEnumerable)states)
                {
                    string id = PartyId(state);
                    if (string.IsNullOrEmpty(id)) continue;
                    MobileParty party = Resolve(objectManager, id);
                    if (party == null)
                    {
                        unknown++;
                        Note("unknown:" + id, "Baseline party " + id + " is not known on this client; left for Coop to report.");
                        continue;
                    }
                    bool inList = parties.Contains(party);
                    if (inList && party.IsActive) continue;

                    if (!inList)
                    {
                        parties.Add(party);
                        _addToListMethod.Invoke(manager, new object[] { party });
                    }
                    _isActiveField.SetValue(party, true);
                    string bound = BindGarrison(party);
                    party.Party?.SetVisualAsDirty();
                    restored++;
                    Note("fixed:" + id, "FIXED: party " + id + " (" + Describe(party) + ") was "
                        + (inList ? "inactive" : "dropped from the party list") + " here but active on the server; put back" + bound + ".");
                }
                Note("count:" + states.Length + ":" + active + ":" + restored,
                    "Join baseline lists " + Count(states.Length) + " parties, this client had " + Count(active)
                    + " active; put back " + Count(restored) + ", " + Count(unknown) + " unknown.");
            }
            catch (Exception e)
            {
                // Coop's own check still runs and reports the mismatch.
                Note("error:" + e.GetType().Name, "Join baseline fix failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Only fills an empty town slot, or one held by a garrison that is itself dropped; never replaces
        // a live garrison.
        private static string BindGarrison(MobileParty party)
        {
            if (!(party.PartyComponent is GarrisonPartyComponent component)) return "";
            Town town = party.HomeSettlement?.Town;
            if (town == null) return ", no home town";
            MobileParty current = town.GarrisonPartyComponent?.MobileParty;
            if (current == party) return "";
            if (current != null && current.IsActive) return ", " + town.Settlement.StringId + " already has a garrison";
            town.GarrisonPartyComponent = component;
            return ", garrison of " + town.Settlement.StringId;
        }

        private static MobileParty Resolve(object objectManager, string id)
        {
            if (objectManager != null)
            {
                foreach (MethodInfo method in objectManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name != "TryGetObject" || !method.IsGenericMethodDefinition) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string)) continue;
                    object[] arguments = { id, null };
                    if ((bool)method.MakeGenericMethod(typeof(MobileParty)).Invoke(objectManager, arguments))
                        return arguments[1] as MobileParty;
                    break;
                }
            }
            return null;
        }

        private static string PartyId(object state)
        {
            object behavior = AccessTools.Property(state.GetType(), "Behavior")?.GetValue(state, null)
                ?? AccessTools.Field(state.GetType(), "Behavior")?.GetValue(state);
            if (behavior == null) return null;
            return (AccessTools.Property(behavior.GetType(), "MobilePartyId")?.GetValue(behavior, null)
                ?? AccessTools.Field(behavior.GetType(), "MobilePartyId")?.GetValue(behavior)) as string;
        }

        private static string Describe(MobileParty party)
        {
            string name = party.Name?.ToString() ?? "?";
            return name + ", " + (party.PartyComponent?.GetType().Name ?? "no component")
                + ", leader " + (party.LeaderHero?.StringId ?? "none")
                + ", home " + (party.HomeSettlement?.StringId ?? "none");
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static void Note(string key, string message)
        {
            lock (Noted)
            {
                if (!Noted.Add(key)) return;
            }
            _log?.Invoke(message);
        }
    }
}
