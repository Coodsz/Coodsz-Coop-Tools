using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealmOfThronesCoopCompat
{
    // Players' PCs only, next to ClientGates. Realm of Thrones features that cannot work with
    // several players are off in a Coop session, and the player is told once per session:
    // - enlistment: the "I want to enlist" line never shows (Enlist itself is a ClientGates gate),
    //   and a save that says enlisted loads as not enlisted;
    // - RoT's prefix on PlayerEncounter.Finish comes off for the session (it always skips the game's
    //   Finish and with it Coop's six patches there) and goes back when the session ends;
    // - RoT's clan finance model leaves the model chain in the server's world;
    // - with War Sails on, RoT's agent stat models pass the after deployment calls to War Sails' model;
    // - the Storyline Wars popup's Done button and rot.edit_storyline_wars change nothing;
    // - at mission start the statics RoT's battle patches read are checked once per session.
    // Every RoT name is found by name; each step has its own try and line.
    internal static class ClientDisable
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<ClientRules.Off> Told = new HashSet<ClientRules.Off>();
        private static readonly HashSet<string> StaticsLogged = new HashSet<string>();
        private static Harmony _harmony;
        private static Assembly _rot;

        public static void Install(Harmony harmony)
        {
            _harmony = harmony;
            try
            {
                var afterStart = typeof(Mission).GetMethod("AfterStart", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (afterStart == null)
                    SubModule.Log("Client: Mission.AfterStart not found; Realm of Thrones' battle statics are not checked.");
                else
                    _harmony.Patch(afterStart, postfix: new HarmonyMethod(typeof(ClientDisable), nameof(MissionStartPostfix)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Client could not hook Mission.AfterStart: " + ex.Message);
            }
            HookStatModels();
            Coop.SessionChanged += _ =>
            {
                lock (Sync)
                {
                    Told.Clear();
                    StaticsLogged.Clear();
                }
                SyncFinish();
            };
            // A hook that could not be made when ROT.dll loaded gets its last try at campaign start;
            // without the session edges the campaign start is also the next chance for the Finish prefix.
            Coop.CampaignBegan += () =>
            {
                if (_rot != null)
                    HookPending(true);
                SyncFinish();
                DropFinanceWrapper();
            };
        }

        private sealed class PendingHook
        {
            public string Type;
            public string Method;
            public string Prefix;
            public string Postfix;
            public string Line;
        }

        private static readonly List<PendingHook> Pending = new List<PendingHook>
        {
            new PendingHook { Type = "ROT.CampaignBehaviors.ROTEnlistmentBehavior", Method = "EnlistmentJoinCondition", Postfix = nameof(JoinConditionPostfix),
                Line = "the enlist dialog line is hidden in the server's world" },
            new PendingHook { Type = "ROT.CampaignBehaviors.ROTEnlistmentBehavior", Method = "SyncData", Postfix = nameof(EnlistmentSyncDataPostfix),
                Line = "a save that says enlisted loads as not enlisted in the server's world" },
            new PendingHook { Type = "ROT.Screens.StorylinePopupLayer+<>c", Method = "<ShowStorylineWarsPopup>b__6_3", Prefix = nameof(StorylineDonePrefix),
                Line = "the Storyline Wars popup's Done button changes nothing in the server's world" },
            new PendingHook { Type = "ROT.Commands.EditStorylineWars", Method = "Execute", Prefix = nameof(EditCommandPrefix),
                Line = "rot.edit_storyline_wars does nothing in the server's world" },
        };

        private static bool _finishLookupDone;

        // ROT.dll is loaded: hook the entries by name.
        public static void RotLoaded(Assembly rot, bool last)
        {
            _rot = rot;
            HookPending(last);
            SyncFinish();
        }

        // A type that is not there yet or a hook that throws stays pending until the last try.
        private static void HookPending(bool last)
        {
            lock (Sync)
            {
                foreach (var hook in Pending.ToList())
                {
                    if (Hook(hook, last))
                        Pending.Remove(hook);
                }
                if (_finishLookupDone)
                    return;
                try
                {
                    var patches = _rot.GetType("ROT.CampaignBehaviors.ROTEnlistmentBehaviorPatches", false);
                    _finishPatch = patches == null ? null : ClientRules.FindMethod(patches, "Finish", null);
                    _finishOriginal = typeof(PlayerEncounter).GetMethod("Finish", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                    if (_finishPatch != null && _finishOriginal != null)
                        _finishLookupDone = true;
                    else if (last)
                    {
                        _finishLookupDone = true;
                        SubModule.Log("!!! Client: " + (_finishPatch == null ? "ROTEnlistmentBehaviorPatches.Finish" : "PlayerEncounter.Finish")
                            + " not found, so Realm of Thrones' Finish prefix stays and Coop's patches on PlayerEncounter.Finish do not run.");
                    }
                }
                catch (Exception ex)
                {
                    if (last)
                    {
                        _finishLookupDone = true;
                        SubModule.Log("Client could not find Realm of Thrones' Finish prefix: " + ex.Message);
                    }
                }
            }
        }

        // True when the hook is done with (made, or given up on with a line).
        private static bool Hook(PendingHook hook, bool last)
        {
            try
            {
                var type = _rot.GetType(hook.Type, false);
                var method = type == null ? null : ClientRules.FindMethod(type, hook.Method, null);
                if (method == null)
                {
                    if (type == null && !last)
                        return false;
                    SubModule.Log("!!! Client: " + hook.Type + "." + hook.Method + " not found, so it runs as Realm of Thrones ships it.");
                    return true;
                }
                _harmony.Patch(method, prefix: hook.Prefix == null ? null : new HarmonyMethod(typeof(ClientDisable), hook.Prefix),
                    postfix: hook.Postfix == null ? null : new HarmonyMethod(typeof(ClientDisable), hook.Postfix));
                SubModule.Log("Client: " + hook.Line + ".");
                return true;
            }
            catch (Exception ex)
            {
                if (!last)
                    return false;
                SubModule.Log("Client could not hook " + hook.Type + "." + hook.Method + ": " + ex.Message);
                return true;
            }
        }

        private static bool World()
        {
            try { return ClientGates.IsGated(ClientRules.Scope.World); }
            catch (Exception ex) { return ClientGates.AnswerOnError(ClientRules.Scope.World, ex); }
        }

        // One message per feature and session, through the game's own message display.
        private static void Tell(ClientRules.Off feature)
        {
            try
            {
                lock (Sync)
                {
                    if (!Told.Add(feature))
                        return;
                }
                var text = ClientRules.OffTexts[feature];
                InformationManager.DisplayMessage(new InformationMessage(text));
                SubModule.Log("Client: told the player \"" + text + "\"");
            }
            catch { }
        }

        // The condition runs first (it only reads and sets the line's text), so the player is told
        // only when the line would have shown.
        private static void JoinConditionPostfix(ref bool __result)
        {
            try
            {
                if (!__result || !World())
                    return;
                __result = false;
                Tell(ClientRules.Off.Enlistment);
            }
            catch { }
        }

        // RoT's SyncData turns enlistment back on when a save has an enlisted lord.
        private static void EnlistmentSyncDataPostfix(object __instance, IDataStore __0)
        {
            try
            {
                if (__0 == null || !__0.IsLoading || !World())
                    return;
                var type = __instance.GetType();
                var enlisted = type.GetField("IsEnlisted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var hero = type.GetField("EnlistedHero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (enlisted == null || hero == null || (!(bool)enlisted.GetValue(__instance) && hero.GetValue(__instance) == null))
                    return;
                enlisted.SetValue(__instance, false);
                hero.SetValue(__instance, null);
                SubModule.Log("Client: the server's world said this player was enlisted; loaded as not enlisted (enlistment is off in Coop).");
            }
            catch (Exception ex)
            {
                SubModule.Log("Client could not clear the enlistment from the save: " + ex.Message);
            }
        }

        private static bool StorylineDonePrefix()
        {
            if (!World())
                return true;
            Tell(ClientRules.Off.StorylineWarsDone);
            return false;
        }

        private static bool EditCommandPrefix(ref string __result)
        {
            if (!World())
                return true;
            __result = ClientRules.OffTexts[ClientRules.Off.StorylineWarsCommand];
            Tell(ClientRules.Off.StorylineWarsCommand);
            return false;
        }

        private static MethodInfo _finishPatch;
        private static MethodBase _finishOriginal;
        private static Patch _finishSaved;
        private static bool _finishRemoved;

        // Takes RoT's Finish prefix off at the start of a session on this PC and puts it back, with
        // its own Harmony id, priority and order, when the session ends.
        private static void SyncFinish()
        {
            try
            {
                lock (Sync)
                {
                    if (_finishPatch == null || _finishOriginal == null)
                        return;
                    var step = ClientRules.Finish(Coop.OnPlayerPc, _finishRemoved);
                    if (step == ClientRules.FinishStep.Remove)
                    {
                        // Marked removed only once it is off, so a session edge that comes before RoT's
                        // patches are on, or a failed unpatch, is tried again at the next edge or campaign start.
                        _finishSaved = Harmony.GetPatchInfo(_finishOriginal)?.Prefixes.FirstOrDefault(patch => ClientGates.Same(patch.PatchMethod, _finishPatch));
                        if (_finishSaved == null)
                        {
                            SubModule.Log("Client: Realm of Thrones' prefix is not on PlayerEncounter.Finish yet; nothing to take off.");
                            return;
                        }
                        _harmony.Unpatch(_finishOriginal, _finishPatch);
                        var still = Harmony.GetPatchInfo(_finishOriginal)?.Prefixes.Any(patch => ClientGates.Same(patch.PatchMethod, _finishPatch)) == true;
                        _finishRemoved = !still;
                        SubModule.Log(still
                            ? "!!! Client: could not take Realm of Thrones' prefix off PlayerEncounter.Finish; Coop's patches there still do not run."
                            : "Client: Realm of Thrones' prefix (" + _finishSaved.owner + ") is off PlayerEncounter.Finish for this Coop session, so the game's Finish and Coop's patches run.");
                    }
                    else if (step == ClientRules.FinishStep.PutBack)
                    {
                        _finishRemoved = false;
                        if (_finishSaved == null)
                            return;
                        new Harmony(_finishSaved.owner).Patch(_finishOriginal,
                            prefix: new HarmonyMethod(_finishPatch, _finishSaved.priority, _finishSaved.before, _finishSaved.after));
                        _finishSaved = null;
                        SubModule.Log("Client: the Coop session ended; Realm of Thrones' prefix is back on PlayerEncounter.Finish.");
                    }
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Client could not switch Realm of Thrones' Finish prefix: " + ex.Message);
            }
        }

        // RoT's wrapper sits on top of War Sails' model, and Coop's clan screen casts the top model's
        // base to the game's own model. The wrapper only adds an enlisted soldier's wages, so the
        // model under it goes back on top.
        private static void DropFinanceWrapper()
        {
            try
            {
                if (!World())
                    return;
                var models = Campaign.Current?.Models;
                var top = models?.ClanFinanceModel;
                if (top == null || !ClientRules.IsRotFinanceWrapper(top.GetType().FullName))
                    return;
                var previous = top.GetType().GetField("_previousModel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(top) as ClanFinanceModel;
                var setter = typeof(GameModels).GetProperty("ClanFinanceModel", BindingFlags.Instance | BindingFlags.Public)?.GetSetMethod(true);
                if (previous == null || setter == null)
                {
                    SubModule.Log("!!! Client: Realm of Thrones' clan finance model could not be taken out (" + (previous == null ? "no model under it" : "no setter")
                        + "); Coop's clan screen may fail for a party leader who does not lead the clan.");
                    return;
                }
                setter.Invoke(models, new object[] { previous });
                SubModule.Log("Client: Realm of Thrones' clan finance model is out of the model chain; " + previous.GetType().Name + " is on top.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Client could not take Realm of Thrones' clan finance model out: " + ex.Message);
            }
        }

        // RoT's two agent stat models keep the model under them in _previousModel but leave out
        // InitializeAgentStatsAfterDeploymentFinished and InitializeMissionEquipmentAfterDeploymentFinished,
        // so the calls stop at the game's empty base methods and War Sails' model under them never
        // adds the figurehead, crew and ammo bonuses. RoT's models are not ours to replace and do real
        // work, so unlike the finance model they stay; a postfix on the two base methods passes the
        // call on when the model is one of RoT's. Without War Sails nothing is hooked, and the game's
        // own models never reach these postfixes since only RoT's lack the methods. On outside the
        // server's world too, since it only puts back what War Sails does without RoT.
        private static readonly Dictionary<Type, FieldInfo> PreviousFields = new Dictionary<Type, FieldInfo>();

        private static void HookStatModels()
        {
            try
            {
                if (!RotRules.HasModule(SubModule.ActiveModules().Select(module => module.Key), RotRules.WarSailsModuleId))
                    return;
                var stats = typeof(AgentStatCalculateModel).GetMethod(nameof(AgentStatCalculateModel.InitializeAgentStatsAfterDeploymentFinished));
                var equipment = typeof(AgentStatCalculateModel).GetMethod(nameof(AgentStatCalculateModel.InitializeMissionEquipmentAfterDeploymentFinished));
                _harmony.Patch(stats, postfix: new HarmonyMethod(typeof(ClientDisable), nameof(StatsAfterDeploymentPostfix)));
                _harmony.Patch(equipment, postfix: new HarmonyMethod(typeof(ClientDisable), nameof(EquipmentAfterDeploymentPostfix)));
                SubModule.Log("Client: Realm of Thrones' agent stat models pass the after deployment calls on, so War Sails' ship bonuses are passed on.");
            }
            catch (Exception ex)
            {
                SubModule.Log("!!! Client could not hook the agent stat model's after deployment calls; War Sails' figurehead and ship ammo bonuses are lost with Realm of Thrones: " + ex.Message);
            }
        }

        private static void StatsAfterDeploymentPostfix(AgentStatCalculateModel __instance, Agent agent) =>
            PreviousStatModel(__instance)?.InitializeAgentStatsAfterDeploymentFinished(agent);

        private static void EquipmentAfterDeploymentPostfix(AgentStatCalculateModel __instance, Agent agent) =>
            PreviousStatModel(__instance)?.InitializeMissionEquipmentAfterDeploymentFinished(agent);

        private static AgentStatCalculateModel PreviousStatModel(AgentStatCalculateModel model)
        {
            var type = model?.GetType();
            if (type == null || !ClientRules.IsRotStatModel(type.FullName))
                return null;
            FieldInfo field;
            lock (Sync)
            {
                if (!PreviousFields.TryGetValue(type, out field))
                {
                    field = type.GetField("_previousModel", BindingFlags.Instance | BindingFlags.NonPublic);
                    PreviousFields[type] = field;
                    if (field == null)
                        SubModule.Log("!!! Client: " + type.Name + " has no _previousModel, so War Sails' after deployment bonuses are lost under it.");
                }
            }
            var previous = field?.GetValue(model) as AgentStatCalculateModel;
            return ReferenceEquals(previous, model) ? null : previous;
        }

        // The statics RoT's agent spawn and battle patches read without a null check.
        private static void MissionStartPostfix()
        {
            try
            {
                if (_rot == null || !World())
                    return;
                var missing = new List<string>();
                foreach (var name in ClientRules.MissionStatics)
                {
                    var cut = name.LastIndexOf('.');
                    var field = _rot.GetType(name.Substring(0, cut), false)?.GetField(name.Substring(cut + 1), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null || field.GetValue(null) == null)
                        missing.Add(name);
                }
                var key = string.Join(",", missing);
                lock (Sync)
                {
                    if (!StaticsLogged.Add(key))
                        return;
                }
                SubModule.Log(ClientRules.StaticsLine(missing, ClientGates.DuelsReadersGated()));
            }
            catch (Exception ex)
            {
                SubModule.Log("Client could not check Realm of Thrones' battle statics: " + ex.Message);
            }
        }
    }
}
