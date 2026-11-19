using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RealmOfThronesCoopCompat
{
    // The server guards (plan item S15). On the dedicated server Hero.MainHero, Clan.PlayerClan
    // and MobileParty.MainParty are the server's own hero, clan and party, and several heroes and
    // clans belong to players, so Realm of Thrones' rules that test the one local player waited
    // in the host's tables. Here each gets a test for every player and is hosted:
    // - ROTStorylineWars: the kings protect the Night's Watch from every kingdom a player's clan
    //   rules. EnforceWars runs as is: Realm of Thrones forces storyline wars on the player's
    //   kingdom too, so player kingdoms are included, also while their ruler is offline (Coop's
    //   offline war protection only covers kingdom decisions). That is for the owner to decide.
    // - ROTRelationshipsBehavior: players' heroes and their clans stay out of NPC marriages, as
    //   Coop's guard keeps them out of the vanilla marriage tick this behavior replaces.
    // - ROTTroopRecruiter and ROTCoreBehavior: no troop swaps for players' parties, clans and
    //   fiefs, no gold floor for players' clans, and IsHeroManageable no longer reads the server's
    //   own hero unguarded.
    // - ROTOthersCampaignBehavior: the Night King's death spares players, never hands a castle to
    //   a player's clan by distance and starts no story popup; King's Landing and the Red Keep stay
    //   apart when a player's clan takes one; the wight player branch stays off; each culture write
    //   Coop does not send is logged and sent over our link (plan item S16, RotWorldSync). ROTPartySizeLimitModel waited
    //   for this behavior and is hosted with it.
    // - Patches: OthersPatches.patch7, the Diplomacy war rule and patch27 run inside a guard (the
    //   host does not apply them, HostDecision.Guard); Coop's own player party tests run before
    //   Realm of Thrones' AI tick and prisoner recruit prefixes, which skip every later prefix.
    // A behavior whose guard fails runs inert (events blocked, saved data kept); a patch whose
    // guard fails does not run, and a Realm of Thrones prefix that would skip Coop's guard is taken
    // off again. Every failure counts with the host's (RotHost.AddGuardFailures), so it is loud
    // and holds a new world back at full. Coop's types are looked up by name, again at campaign
    // start when they were not loaded yet. Installed from SubModule on the server only.
    internal static class RotGuards
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Behaviors = "ROT.CampaignBehaviors.";
        private const string OthersPatches = "ROT.HarmonyPatches.Core.OthersPatches";

        private static Harmony _harmony;
        private static Assembly _rot;
        private static int _applied;
        private static int _failed;
        private static bool _coopDone;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Install()
        {
            var rot = RotHost.Rot;
            if (rot == null || !RotHostRules.LoadsRot(RotHost.Running))
            {
                SubModule.Log(RotGuardsRules.Prefix + "not installed: the host runs nothing of Realm of Thrones on this server (the Host lines above say why).");
                return;
            }
            _rot = rot;
            _harmony = new Harmony(MapRedirect.HarmonyId);
            ApplyRot(rot);
            var moved = LogMoved();
            var waiting = 0;
            if (!TryCoop(false))
            {
                waiting = 3;
                Coop.CampaignBegan += OnCampaignBegan;
            }
            SubModule.Log(RotGuardsRules.SummaryLine(_applied, _failed, waiting, moved));
            RotHost.AddGuardFailures(_failed);
        }

        // Coop's assemblies load late on the server; at campaign start they are there.
        private static void OnCampaignBegan()
        {
            try
            {
                if (_coopDone)
                    return;
                var before = _failed;
                TryCoop(true);
                SubModule.Log((_failed > before ? RotGuardsRules.Loud : RotGuardsRules.Prefix) + "Coop's guards at campaign start: " + (3 - (_failed - before)) + " of 3 in.");
                RotHost.AddGuardFailures(_failed - before);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotGuardsRules.Loud + "Coop's guards could not be put in at campaign start: " + ex);
            }
        }

        // The guards on Realm of Thrones' own code and the game's, there since ROT.dll loaded.
        // Every lookup is inside a guard, so a missing type fails that guard and counts.
        private static void ApplyRot(Assembly rot)
        {
            Type Settings() => rot.GetType(RotHostTables.SettingsType, true);

            BehaviorGuard("ROTStorylineWars", "the kings protect the Night's Watch from every kingdom a player's clan rules", type =>
            {
                var kingdoms = rot.GetType("ROT.Misc.ROTKingdoms", true);
                RotGuardsPatches.Defenders = type.GetProperty("NightsWatchDefenders", AnyInstance) ?? throw new MissingMemberException(type.FullName, "NightsWatchDefenders");
                RotGuardsPatches.NightsWatch = Getter<Kingdom>(kingdoms, "NightsWatch");
                RotGuardsPatches.FreeFolk = Getter<Kingdom>(kingdoms, "FreeFolk");
                RotGuardsPatches.KingsProtectNightsWatch = Getter<bool>(Settings(), "KingsProtectNightsWatch");
                Patch(Find(type, "OnWarDeclared", typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail)), prefix: nameof(RotGuardsPatches.WarDeclaredPrefix));
            });
            BehaviorGuard("ROTRelationshipsBehavior", "players' heroes and their clans stay out of NPC marriages", type =>
            {
                RotGuardsPatches.UnmarriedHeroes = type.GetField("_unmarriedHeroes", AnyInstance) ?? throw new MissingFieldException(type.FullName, "_unmarriedHeroes");
                Patch(Find(type, "DailyTick"), postfix: nameof(RotGuardsPatches.UnmarriedPostfix));
                Patch(Find(type, "CheckNPCMarriages", typeof(Clan)), prefix: nameof(RotGuardsPatches.MarriagePrefix));
            });
            BehaviorGuard("ROTCoreBehavior", "the daily gold for poor clans never goes to a player's clan", type =>
                Patch(Find(type, "DailyTickClan", typeof(Clan)), prefix: nameof(RotGuardsPatches.GoldPrefix)));
            BehaviorGuard("ROTTroopRecruiter", "no troop swaps in players' parties, their clans' parties or their fiefs; IsHeroManageable reads the server's own hero safely", type =>
            {
                RotGuardsPatches.WarbandSetting = Getter<bool>(Settings(), "MyLittleWarbandAllowCustomKingdomPartyTroops");
                Patch(Find(type, "IsHeroManageable", typeof(Hero)), prefix: nameof(RotGuardsPatches.ManageablePrefix));
                Patch(Find(type, "IsGarrisonManageable", typeof(Settlement)), prefix: nameof(RotGuardsPatches.GarrisonPrefix));
            });
            BehaviorGuard("ROTOthersCampaignBehavior", "the Night King's death spares players and its story event runs with no popup screen, King's Landing and the Red Keep stay apart when a player's clan takes one, "
                + "the wight player branch is off and each culture write Coop does not send is logged and sent over the link", type =>
            {
                var subModule = rot.GetType(RotHostTables.SubModuleType, true);
                var recruiter = rot.GetType(Behaviors + "ROTTroopRecruiter", true);
                var clans = rot.GetType("ROT.Misc.ROTClans", true);
                var places = rot.GetType("ROT.Misc.ROTSettlements", true);
                RotGuardsPatches.WhiteWalkers = Getter<Clan>(clans, "WhiteWalkers");
                RotGuardsPatches.TheWall = Getter<Settlement>(places, "TheWall");
                RotGuardsPatches.CastleBlack = Getter<Settlement>(places, "CastleBlack");
                RotGuardsPatches.KingsLanding = Getter<Settlement>(places, "KingsLanding");
                RotGuardsPatches.RedKeep = Getter<Settlement>(places, "RedKeep");
                RotGuardsPatches.IsBeyondTheWall = Rule<Func<Vec2, bool>>(rot.GetType("ROT.Misc.ROTUtilities", true), "IsBeyondTheWall", typeof(Vec2));
                RotGuardsPatches.NightKing = type.GetProperty("NightKing", AnyInstance) ?? throw new MissingMemberException(type.FullName, "NightKing");
                RotGuardsPatches.InvasionStarted = type.GetField("IsInvasionStarted", AnyInstance) ?? throw new MissingFieldException(type.FullName, "IsInvasionStarted");
                RotGuardsPatches.ResolveHeir = Find(type, "ResolveSettlementHeir", typeof(Settlement));
                RotGuardsPatches.ChangeToOthersCulture = Find(type, "ChangeToOthersCulture", typeof(Settlement), typeof(Clan));
                RotGuardsPatches.RevertFromOthersCulture = Find(type, "RevertBackFromOthersCulture", typeof(Settlement));
                RotGuardsPatches.Recruiter = subModule.GetField("Recruiter", AnyStatic) ?? throw new MissingFieldException(subModule.FullName, "Recruiter");
                RotGuardsPatches.ClearSettlementCache = Find(recruiter, "ClearSettlementCache", typeof(Settlement));
                // The culture logs first, so the replaced handlers below never write unlogged. The
                // prefix and finalizer hand what each write changed to the link (RotWorldSync, S16).
                foreach (var writer in new[] { Find(type, "ChangeToOthersCulture", typeof(Settlement), typeof(Clan)), Find(type, "RevertBackFromOthersCulture", typeof(Settlement)),
                    Find(type, "EvacuateHeroes", typeof(Settlement), typeof(bool)), Find(type, "ChangeNotableCulture", typeof(Settlement), typeof(int)) })
                    Patch(writer, prefix: nameof(RotGuardsPatches.CultureWritePrefix), postfix: nameof(RotGuardsPatches.CultureWrittenPostfix), finalizer: nameof(RotGuardsPatches.CultureWriteFinalizer));
                Patch(Find(type, "FindNearestViableClan", typeof(Settlement), typeof(Func<Clan, bool>), typeof(bool)), prefix: nameof(RotGuardsPatches.FindHeirPrefix));
                Patch(Find(type, "OnSettlementOwnerChanged", typeof(Settlement), typeof(bool), typeof(Hero), typeof(Hero), typeof(Hero), typeof(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail)),
                    prefix: nameof(RotGuardsPatches.OwnerChangedPrefix));
                Patch(Find(type, "OnHourlyTick"), prefix: nameof(RotGuardsPatches.HourlyPrefix));
            });

            // The patch rows run inside a guard: the host does not apply them (HostDecision.Guard),
            // so a guard that fails leaves the rule out. High priority puts each before Coop's
            // prefix on the same game method, where Realm of Thrones' own prefix also was.
            Guard("OthersPatches.patch7", "White Walker captives, with players and their clans' heroes left to Coop's capture", () =>
            {
                RotGuardsPatches.RotCapture = Rule<Func<PartyBase, Hero, bool>>(rot.GetType(OthersPatches, true), "patch7", typeof(PartyBase), typeof(Hero));
                Patch(Find(typeof(TakePrisonerAction), "ApplyInternal", typeof(PartyBase), typeof(Hero), typeof(bool)), prefix: nameof(RotGuardsPatches.CapturePrefix), prefixPriority: Priority.High);
            });
            Guard("Diplomacy+DeclareWarActionPatch", "the Night's Watch and the Free Folk stay out of wars, every player's clan excepted, before Coop sends a war", () =>
            {
                var rule = rot.GetType("ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch", true);
                RotGuardsPatches.RotWarRule = Rule<Func<IFaction, IFaction, DeclareWarAction.DeclareWarDetail, bool>>(rule, "Prefix", typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail));
                Patch(Find(typeof(DeclareWarAction), "ApplyInternal", typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail)), prefix: nameof(RotGuardsPatches.WarPrefix), prefixPriority: Priority.High);
            });
            // In place of OthersPatches.patch9, which would replace Coop's own wanderer spawner.
            Guard("The wanderer town filter", "Coop's spawner keeps its wanderer limits and new wanderers stay out of White Walker towns", () =>
                Patch(Find(typeof(CompanionsCampaignBehavior), "CreateCompanionAndAddToSettlement", typeof(Settlement)), prefix: nameof(RotGuardsPatches.CompanionTownPrefix)));
        }

        // The three guards that need Coop's own types. Waits until a campaign starts when one of
        // them is not loaded yet; then each missing one counts as failed.
        private static bool TryCoop(bool last)
        {
            var aiThink = Coop.FindType(RotGuardsRules.CoopAiThinkGuardType);
            var prisoner = Coop.FindType(RotGuardsRules.CoopPrisonerGuardType);
            var peace = Coop.FindType(RotGuardsRules.CoopPeacePatchType);
            if (!last && (aiThink == null || prisoner == null || peace == null))
                return false;
            _coopDone = true;
            CoopFirst("AIThinkPatch", "Coop's player party test runs before Realm of Thrones' hourly AI tick", aiThink, RotGuardsRules.CoopAiThinkGuardType, RotGuardsRules.CoopAiThinkGuardMethod,
                typeof(AiPartyThinkBehavior), "PartyHourlyAiTick", nameof(RotGuardsPatches.AiThinkPrefix), guard => RotGuardsPatches.CoopAiThinkGuard = guard);
            CoopFirst("RecruitPrisonersCampaignBehaviorPatch", "Coop's player party test runs before Realm of Thrones' prisoner recruiting", prisoner, RotGuardsRules.CoopPrisonerGuardType,
                RotGuardsRules.CoopPrisonerGuardMethod, typeof(RecruitPrisonersCampaignBehavior), "DailyTickAIMobileParty", nameof(RotGuardsPatches.PrisonerPrefix), guard => RotGuardsPatches.CoopPrisonerGuard = guard);
            if (!Guard("OthersPatches.patch27", "nobody makes peace with the White Walkers, and Coop's postfix does not send a peace that was refused", () =>
            {
                if (peace == null)
                    throw new TypeLoadException(RotGuardsRules.CoopPeacePatchType + " is not loaded");
                var peaceTypes = new[] { typeof(IFaction), typeof(IFaction), typeof(int), typeof(int), typeof(MakePeaceAction.MakePeaceDetail) };
                var coopPostfix = peace.GetMethod(RotGuardsRules.CoopPeacePatchMethod, AnyStatic, null, peaceTypes, null)
                    ?? throw new MissingMethodException(RotGuardsRules.CoopPeacePatchType, RotGuardsRules.CoopPeacePatchMethod);
                RotGuardsPatches.RotPeaceRule = Rule<Func<IFaction, IFaction, int, int, MakePeaceAction.MakePeaceDetail, bool>>(_rot.GetType(OthersPatches, true), "patch27", peaceTypes);
                // Coop's postfix first, so the peace action is patched last and calls it guarded.
                Patch(coopPostfix, prefix: nameof(RotGuardsPatches.PeaceSentPrefix));
                Patch(Find(typeof(MakePeaceAction), "ApplyInternal", peaceTypes), prefix: nameof(RotGuardsPatches.PeacePrefix), prefixPriority: Priority.High,
                    postfix: nameof(RotGuardsPatches.PeaceDonePostfix), postfixPriority: Priority.Last);
            }))
                SubModule.Log(RotGuardsRules.Loud + "PEACE WITH THE WHITE WALKERS IS NOT REFUSED ON THIS SERVER: a clan leaving a kingdom makes peace with them, which Realm of Thrones never allows.");
            return true;
        }

        // Coop's own test, run first at high priority. Without it, Realm of Thrones' prefix on that
        // game method is taken off again, since it would skip Coop's guard there.
        private static void CoopFirst(string row, string what, Type coopType, string typeName, string methodName, Type gameType, string gameMethod, string prefix, Action<Func<MobileParty, bool>> set)
        {
            MethodBase original = null;
            if (Guard(row, what, () =>
            {
                original = Find(gameType, gameMethod, typeof(MobileParty));
                if (coopType == null)
                    throw new TypeLoadException(typeName + " is not loaded");
                var guard = coopType.GetMethod(methodName, AnyStatic, null, new[] { typeof(MobileParty) }, null) ?? throw new MissingMethodException(typeName, methodName);
                set((Func<MobileParty, bool>)Delegate.CreateDelegate(typeof(Func<MobileParty, bool>), guard));
                Patch(original, prefix: prefix, prefixPriority: Priority.High);
            }))
                return;
            try
            {
                original = original ?? AccessTools.DeclaredMethod(gameType, gameMethod, new[] { typeof(MobileParty) });
                var info = original == null ? null : Harmony.GetPatchInfo(original);
                if (info == null || !info.Prefixes.Any(patch => patch.owner == RotHostRules.HarmonyId))
                    return;
                _harmony.Unpatch(original, HarmonyPatchType.Prefix, RotHostRules.HarmonyId);
                SubModule.Log(RotGuardsRules.Loud + "Realm of Thrones' prefix on " + gameType.Name + "." + gameMethod + " is taken off again, so Coop's own guard there is not skipped.");
            }
            catch (Exception ex)
            {
                SubModule.Log(RotGuardsRules.Loud + "Realm of Thrones' prefix on " + gameType.Name + "." + gameMethod + " could not be taken off, so it may skip Coop's guard: " + RotHost.Short(ex));
            }
        }

        // A behavior's guard. When it fails, the behavior runs inert: its RegisterEvents is
        // blocked, so it keeps its saved data and does nothing with a player test missing.
        private static void BehaviorGuard(string name, string what, Action<Type> apply)
        {
            Type type = null;
            if (Guard(name, what, () =>
            {
                type = _rot.GetType(Behaviors + name, true);
                apply(type);
            }) || type == null)
                return;
            try
            {
                _harmony.Patch(Find(type, "RegisterEvents"), prefix: new HarmonyMethod(typeof(RotGuardsPatches).GetMethod(nameof(RotGuardsPatches.NoEvents), AnyStatic)));
                SubModule.Log(RotGuardsRules.Loud + type.FullName + " RUNS INERT: its guard failed, so its events are blocked and only its saved data is kept.");
            }
            catch (Exception ex)
            {
                SubModule.Log(RotGuardsRules.Loud + type.FullName + " RUNS WITHOUT ITS GUARD: its guard failed and its events could not be blocked either: " + RotHost.Short(ex));
            }
        }

        private static bool Guard(string name, string what, Action apply)
        {
            try
            {
                apply();
                _applied++;
                SubModule.Log(RotGuardsRules.Prefix + "in: " + name + ": " + what + ".");
                return true;
            }
            catch (Exception ex)
            {
                _failed++;
                SubModule.Log(RotGuardsRules.Loud + "GUARD FAILED: " + name + " (" + what + "): " + RotHost.Short(ex));
                return false;
            }
        }

        // One line for each row the guards took out of later, with its new answer from the tables.
        private static int LogMoved()
        {
            var count = 0;
            foreach (var name in RotGuardsRules.MovedBehaviors)
            {
                var row = RotHostTables.BehaviorRows.FirstOrDefault(behavior => behavior.Type == name);
                if (row == null)
                    continue;
                SubModule.Log(RotGuardsRules.MovedLine("behavior", name, row.Decision, row.Why));
                count++;
            }
            var model = RotHostTables.ModelRows.FirstOrDefault(row => row.Type == RotGuardsRules.MovedModel);
            if (model != null)
            {
                SubModule.Log(RotGuardsRules.MovedLine("model", model.Type, model.Decision, model.Why + "; it waited for " + model.Needs + ", which is hosted now"));
                count++;
            }
            foreach (var name in RotGuardsRules.MovedPatches)
            {
                var row = RotGuardsRules.PatchRowOf(RotHostTables.Patches, name);
                if (row == null)
                    continue;
                SubModule.Log(RotGuardsRules.MovedLine("patch", name, row.Decision, row.Why));
                count++;
            }
            return count;
        }

        private static MethodInfo Find(Type type, string name, params Type[] parameters) =>
            AccessTools.DeclaredMethod(type, name, parameters)
            ?? throw new MissingMethodException(type.FullName, name + "(" + string.Join(", ", parameters.Select(parameter => parameter.Name)) + ")");

        private static Func<T> Getter<T>(Type type, string name)
        {
            var getter = type.GetProperty(name, AnyStatic)?.GetGetMethod(true) ?? throw new MissingMemberException(type.FullName, name);
            return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), getter);
        }

        private static T Rule<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), type.GetMethod(name, AnyStatic, null, parameters, null) ?? throw new MissingMethodException(type.FullName, name));

        private static void Patch(MethodBase original, string prefix = null, string postfix = null, int prefixPriority = -1, int postfixPriority = -1, string finalizer = null) =>
            _harmony.Patch(original, Method(prefix, prefixPriority), Method(postfix, postfixPriority), finalizer: Method(finalizer, -1));

        private static HarmonyMethod Method(string name, int priority)
        {
            if (name == null)
                return null;
            var method = new HarmonyMethod(typeof(RotGuardsPatches).GetMethod(name, AnyStatic) ?? throw new MissingMethodException(nameof(RotGuardsPatches), name));
            if (priority >= 0)
                method.priority = priority;
            return method;
        }
    }
}
