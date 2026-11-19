#nullable disable

namespace RealmOfThronesCoopCompat
{
    // What the host does with one thing of Realm of Thrones on the dedicated server: run it,
    // leave it out for good, or leave it out until a server version of it exists. Guard: a patch
    // the host does not apply itself; the server guards (RotGuards) run it after a player test.
    internal enum HostDecision { Host, Skip, Later, Guard }

    // One patch class of Realm of Thrones, or one patch method of a class whose methods get
    // different answers.
    internal sealed class PatchRow
    {
        public readonly string Type;
        // The one patch method, or null for the whole class.
        public readonly string Method;
        // The game method a method row patches, as "Type.Method(parameter type names)", which
        // also tells two patch methods of one name apart.
        public readonly string Target;
        // Realm of Thrones applies it by hand as a prefix; the class carries no Harmony attribute.
        public readonly bool ByHand;
        public readonly HostDecision Decision;
        public readonly string Why;
        // "module id|path in that module": a file the patch reads without a check, so it is
        // only applied when the file is there.
        public readonly string NeedsFile;

        public PatchRow(string type, string method, string target, bool byHand, HostDecision decision, string why, string needsFile = null)
        {
            Type = type;
            Method = method;
            Target = target;
            ByHand = byHand;
            Decision = decision;
            Why = why;
            NeedsFile = needsFile;
        }
    }

    internal sealed class ModelRow
    {
        public readonly string Type;
        // The game's model type it stands in for.
        public readonly string Slot;
        public readonly HostDecision Decision;
        public readonly string Why;
        // Set when it runs unchanged although it tests the one local player: what to check.
        public readonly string PlayerCheck;
        // A behavior whose work it counts on and which it calls without a null check; it is
        // only registered when that one is hosted.
        public readonly string Needs;

        public ModelRow(string type, string slot, HostDecision decision, string why, string playerCheck = null, string needs = null)
        {
            Type = type;
            Slot = slot;
            Decision = decision;
            Why = why;
            PlayerCheck = playerCheck;
            Needs = needs;
        }
    }

    internal sealed class BehaviorRow
    {
        public readonly string Type;
        public readonly HostDecision Decision;
        public readonly string Why;
        // The static field of ROT.SubModule that holds it. Realm of Thrones reads those without
        // null checks, so the object is made and stored even when the behavior is not registered.
        public readonly string Field;
        // The ROT.ROTSettings switch Realm of Thrones makes it under.
        public readonly string Setting;
        // For one that is not hosted: it is still registered, with its RegisterEvents blocked,
        // because it stores data (the game only saves and loads registered behaviors) or other
        // code reads it. Without this a behavior that is not hosted is not made at all.
        public readonly bool Inert;

        public BehaviorRow(string type, HostDecision decision, string why, string field = null, string setting = null, bool inert = false)
        {
            Type = type;
            Decision = decision;
            Why = why;
            Field = field;
            Setting = setting;
            Inert = inert;
        }
    }

    // The host's three tables for Realm of Thrones v8.1.8 (ROT.dll of ROT-Core): every patch
    // class, game model and campaign behavior with what the dedicated server does with it and
    // why. Nothing of Realm of Thrones runs on the server unless a row here says Host; a
    // behavior marked inert is registered only so its saved data stays in the server's saves.
    // The answers come from the code inventory of 2026-11-10; the targets were checked by
    // machine against the server's own DLLs. Models and behaviors are in the order Realm of
    // Thrones registers them, which decides which model wraps which. No game types, so the
    // launcher tests can read them.
    internal static class RotHostTables
    {
        public const string CoreVersion = "v8.1.8";
        public const string SubModuleType = "ROT.SubModule";
        public const string SettingsType = "ROT.ROTSettings";

        // Realm of Thrones clears these four caches at every game start.
        public static readonly string[] CacheTypes = { "ROT.Misc.ROTLords", "ROT.Misc.ROTSettlements", "ROT.Misc.ROTKingdoms", "ROT.Misc.ROTClans" };

        // Realm of Thrones takes the vanilla FindingItemOnMapBehavior out because this one does its job.
        public const string ReplacingBehavior = "ROT.CampaignBehaviors.ROTDragonBehavior";

        // ROTCampaignTimeModel starts a new world in the year 299; the game's own model in 1084.
        public const int StartYear = 299;
        public const int VanillaStartYear = 1084;
        public const string TimeModelType = "ROT.Models.ROTCampaignTimeModel";

        private const string Core = "ROT.HarmonyPatches.Core.";
        private const string Others = Core + "OthersPatches";
        private const string Weapons = Core + "ROTWeaponComponetData";
        private const string Companions = "ROT.HarmonyPatches.CustomCompanions.";
        private const string Dragon = "ROT.HarmonyPatches.Dragon.";
        private const string Campaign = "TaleWorlds.CampaignSystem.";
        private const string Behaviors = "ROT.CampaignBehaviors.";
        private const string Models = "ROT.Models.";

        private const string NoMissions = "mission code; the server runs no missions";
        private const string OnlyUi = "screens and menus only";
        private const string Enlistment = "enlistment is one player's feature and off in co-op";
        private const string PlayerEncounter = "a player's encounter runs on that player's PC";

        private static PatchRow Class(string type, HostDecision decision, string why, string needsFile = null) =>
            new PatchRow(type, null, null, false, decision, why, needsFile);

        private static PatchRow Method(string type, string method, string target, HostDecision decision, string why) =>
            new PatchRow(type, method, target, false, decision, why);

        private static PatchRow Hand(string type, string method, string target, HostDecision decision, string why) =>
            new PatchRow(type, method, target, true, decision, why);

        public static readonly PatchRow[] Patches =
        {
            // ROT.SubModule applies this one by hand before its PatchAll; the other patches by
            // hand, at the end of the table, come after it. The rows in between are applied in the
            // order ROT.dll lists its types, as PatchAll does, whatever their order here.
            Hand("ROT.HarmonyPatches.NativeBugfixes.GameTextsPatches", "FindTextPrefix", "TaleWorlds.Core.GameTexts.FindText(String, String)",
                HostDecision.Host, "guard for a missing text table"),

            Class(Core + "BattleScenePatch", HostDecision.Host, "battle scene weights as in single player; the server picks the scene",
                "ROT-Content|ModuleData/sp_battle_scenes.xml"),
            Class(Core + "CaravanPatches", HostDecision.Host, "notables are made and caravan speed is computed on the server"),
            Class(Core + "ClanRemovalPatches", HostDecision.Host, "houses without land survive; the server decides who is removed"),
            Class(Core + "ClanScorePatches", HostDecision.Host, "the server scores a clan's kingdom join with the same numbers as players"),
            Class(Core + "DefaultLoyaltyPatch1", HostDecision.Host, "loyalty numbers; the server computes the daily change"),
            Class(Core + "DefaultLoyaltyPatch2", HostDecision.Host, "rebellion threshold; rebellions are decided on the server"),
            Class(Core + "DefaultLoyaltyPatch3", HostDecision.Host, "rebellious state threshold, set on the server"),
            Class(Core + "DefaultLoyaltyPatch4", HostDecision.Host, "loyalty numbers; the server computes the daily change"),
            Class(Core + "KingdomDecisionPatches", HostDecision.Host, "kingdom decisions are resolved on the server"),
            Class(Core + "SettlementPatches+SettlementOwnerClanGetter", HostDecision.Host, "null guard for bandit held castles"),
            Class(Core + "TakePrisonerActionPatch", HostDecision.Host, "plain veto for story heroes during two sieges"),
            Class(Core + "XMLParserAddons", HostDecision.Host, "item and culture data read from XML, the same as on players' PCs"),
            Class(Companions + "HeroSpawnCampaignBehaviorPatches", HostDecision.Host, "which minor faction heroes the world gets"),
            Class(Companions + "KillCharacterActionPatch+ApplyInLabor", HostDecision.Host, "plain veto for one hero"),
            Class("ROT.HarmonyPatches.NativeBugfixes.LeaveKingdomAsClanBarterablePatch", HostDecision.Host, "null guard on a world action"),
            Class("ROT.HarmonyPatches.NativeCrashfixes.GangLeadersIssue", HostDecision.Host, "null guard in issue generation"),

            Class("ROT.SubModule+RecruitPrisonersCampaignBehaviorPatch", HostDecision.Host,
                "AI parties recruit their prisoners; a server guard runs Coop's own player party test before it, so Coop's guard still holds"),
            Class(Dragon + "FoodConsumptionBehaviorPatch", HostDecision.Later,
                "it and Coop's prefix both replace the method, so only one of them would run"),
            Class(Dragon + "LootCollectorPatch", HostDecision.Later,
                "Coop commits player battles with its own code, which this prefix never sees"),
            Class(Core + "AIThinkPatch", HostDecision.Host,
                "the server owns party AI; a server guard runs Coop's own player party test before its prefix replaces the hourly tick"),
            Class(Core + "TourneyRewardsRemoval", HostDecision.Later,
                "in co-op the prize is picked before this postfix can change the pool"),
            Class(Companions + "CompanionsCampaignBehaviorPatches", HostDecision.Later,
                "Coop's postfix refills what this one filters out; the filter must come after Coop's"),
            Class("ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch", HostDecision.Guard,
                "the Night's Watch and the Free Folk stay out of wars; the server guard lets every player's clan through, before Coop sends the war"),

            Class("ROT.RealmOfThronesGameModeManagerPatch", HostDecision.Skip, "replaces the game's load step to open the character creation screens"),
            Class(Behaviors + "ROTEnlistmentBehaviorPatches", HostDecision.Skip, Enlistment + "; its Finish copy would also replace the one Coop needs"),
            Class("ROT.HarmonyPatches.Boats.BoatPatches", HostDecision.Skip, NoMissions),
            Class(Core + "CharacterCreationCultureFaceGen", HostDecision.Skip, "character creation screen"),
            Class(Core + "CharacterCreationPatches", HostDecision.Skip, "swaps in the character creation with screens; the server creates its hero headless"),
            Class(Core + "CharacterObjectPatches", HostDecision.Skip, "sets a value the game already set"),
            Class(Core + "ClothPhysics", HostDecision.Skip, NoMissions),
            Class(Core + "CraftedDataView_BuildWeaponMesh_SafeBatching_Timed", HostDecision.Skip, "weapon meshes"),
            Class(Core + "CustomVoiceLines", HostDecision.Skip, "dialog text"),
            Class(Core + "DefaultDiplomacyModelPatches+GetScoreOfWarInternal", HostDecision.Skip, "text only"),
            Class(Core + "DiplomaticBartersBehaviorPatch", HostDecision.Skip, "Coop turns the vanilla behavior off, so it is never called"),
            Class(Core + "DualWieldingPatches", HostDecision.Skip, OnlyUi),
            Class(Core + "DuelPatches", HostDecision.Skip, PlayerEncounter),
            Class(Core + "EnlistmentPatches", HostDecision.Skip, Enlistment),
            Class(Core + "FaceGenPatch", HostDecision.Skip, "face editor"),
            Class(Core + "FactionHelperPatches", HostDecision.Skip, "names the clan in a player's own character creation"),
            Class(Core + "InventoryPatches", HostDecision.Skip, OnlyUi),
            Class(Core + "IsCollisionBoneDifferentThanWeaponAttachBonePatch", HostDecision.Skip, NoMissions),
            Class(Core + "LoadingScreenPatches", HostDecision.Skip, "loading screen art"),
            Class(Core + "MBMusicManagerPatches", HostDecision.Skip, "music, with the hand patch on the soundtrack loader"),
            Class(Core + "OathPatch", HostDecision.Skip, "dialog text"),
            Class(Core + "SandBoxGauntletGameNotificationPatches", HostDecision.Skip, OnlyUi),
            Class(Core + "ScenePatches", HostDecision.Skip, "entering a scene on a player's PC"),
            Class(Core + "WarSailsPatches", HostDecision.Skip, "enlisted naval battles and a player's own character creation, with its two hand patches"),
            Class(Companions + "EncyclopediaListItemPatches", HostDecision.Skip, OnlyUi),
            Class(Dragon + "DisableUpgradeIfOnlyDragons", HostDecision.Skip, OnlyUi),
            Class(Dragon + "PartyCharacterVMPatch", HostDecision.Skip, OnlyUi),
            Class("ROT.HarmonyPatches.ModCompatibility.RBM", HostDecision.Skip, "only for Realistic Battle Mod, which does not run on the server"),
            Class("ROT.HarmonyPatches.NativeBugfixes.MarriageCrash", HostDecision.Skip, "holds no patch itself"),
            Class("ROT.HarmonyPatches.NativeBugfixes.MarriageCrash+Patch_MarriageOfferGeneration", HostDecision.Skip,
                "would stop Coop's marriage offers for every player"),
            Class("ROT.HarmonyPatches.TimeLock.MissionPatch", HostDecision.Skip, NoMissions),
            Class("RoT_Elephants.RoTElephantPatches+ElephantCameraPatches", HostDecision.Skip, "mission camera"),
            Class("RoT_Elephants.RoTElephantPatches+ItemThumbnailCache_GetItemPoseAndCamera_Patch", HostDecision.Skip, "item pictures"),

            // The weapon data class: its constructor patch is item data, the rest is combat in a mission.
            Method(Weapons, "patch1", "TaleWorlds.Core.WeaponComponentData..ctor(ItemObject, WeaponClass, WeaponFlags)",
                HostDecision.Host, "weapon data, the same as on players' PCs"),
            Method(Weapons, "patch2", "TaleWorlds.MountAndBlade.MissionCombatMechanicsHelper.DecideWeaponCollisionReaction(Blow&, AttackCollisionData&, Agent, Agent, MissionWeapon&, Boolean, Boolean, Single, MeleeCollisionReaction&)",
                HostDecision.Skip, NoMissions),
            Method(Weapons, "patch3", "TaleWorlds.MountAndBlade.Mission.MeleeHitCallback(AttackCollisionData&, Agent, Agent, GameEntity, Single&, MeleeCollisionReaction&, CrushThroughState, Vec3, Vec3, HitParticleResultData&, Boolean)",
                HostDecision.Skip, NoMissions),
            Method(Weapons, "patch4", "TaleWorlds.MountAndBlade.MissionCombatMechanicsHelper.GetDefendCollisionResults(Agent, Agent, CombatCollisionResult, Int32, Boolean, StrikeType, UsageDirection, Single, Single, Boolean, Boolean, Boolean, Single&, Single&, Boolean&, Boolean&)",
                HostDecision.Skip, NoMissions),

            Method(Others, "patch1", Campaign + "CampaignBehaviors.BarterBehaviors.DiplomaticBartersBehavior.ConsiderClanJoinAsMercenary(Clan, Kingdom)",
                HostDecision.Skip, "Coop turns the vanilla behavior off, so it is never called"),
            Method(Others, "patch2", Campaign + "CampaignBehaviors.BarterBehaviors.DiplomaticBartersBehavior.ConsiderClanJoin(Clan, Kingdom)",
                HostDecision.Skip, "Coop turns the vanilla behavior off, so it is never called"),
            Method(Others, "patch3", Campaign + "CampaignBehaviors.BarterBehaviors.DiplomaticBartersBehavior.ConsiderDefection(Clan, Kingdom)",
                HostDecision.Skip, "Coop turns the vanilla behavior off, so it is never called"),
            Method(Others, "patch4", Campaign + "CampaignBehaviors.BarterBehaviors.DiplomaticBartersBehavior.ConsiderPeace(Clan, Clan)",
                HostDecision.Skip, "Coop turns the vanilla behavior off, so it is never called"),
            Method(Others, "patch5", Campaign + "Encounters.PlayerEncounter.DoMeetingInternal()",
                HostDecision.Skip, PlayerEncounter),
            Method(Others, "patch6", Campaign + "CampaignBehaviors.EncounterGameMenuBehavior.game_menu_taken_prisoner_continue_on_condition(MenuCallbackArgs)",
                HostDecision.Skip, "a player's prisoner menu; the server opens no menus, and with players kept out of patch7 its flag is never set"),
            Method(Others, "patch7", Campaign + "Actions.TakePrisonerAction.ApplyInternal(PartyBase, Hero, Boolean)",
                HostDecision.Guard, "White Walker captives; the server guard leaves players and their clans' heroes to Coop's capture, and logs the culture it writes"),
            Method(Others, "patch8", Campaign + "CampaignBehaviors.AiBehaviors.AiVisitSettlementBehavior.GetApproximateVolunteersCanBeRecruitedDataFromSettlement(Hero, Settlement)",
                HostDecision.Host, "AI recruiting score"),
            Method(Others, "patch9", Campaign + "CampaignBehaviors.CompanionsCampaignBehavior.TrySpawnNewCompanion()",
                HostDecision.Skip, "it replaces the method Coop also replaces and drops Coop's wanderer limits; a server guard keeps Coop's new wanderers out of White Walker towns instead"),
            Method(Others, "patch10", Campaign + "CampaignBehaviors.CompanionsCampaignBehavior.SwapCompanions()",
                HostDecision.Host, "keeps wandering companions out of White Walker towns; Coop does not patch this method"),
            Method(Others, "patch11", Campaign + "CampaignBehaviors.RansomOfferCampaignBehavior.ConsiderRansomPrisoner(Hero)",
                HostDecision.Host, "plain veto on ransom offers"),
            Method(Others, "patch11", Campaign + "Actions.KillCharacterAction.ApplyByExecution(Hero, Hero, Boolean, Boolean)",
                HostDecision.Later, "Coop's server executes through another method, so this prefix never sees a player's execution"),
            Method(Others, "patch12", Campaign + "Actions.ChangeRelationAction.ApplyInternal(Hero, Hero, Int32, Boolean, ChangeRelationDetail)",
                HostDecision.Host, "plain veto by hero race"),
            Method(Others, "patch13", Campaign + "Party.MobilePartyAi.GetAccessibleTargetPointInDirection(CampaignVec2&, Vec2, Single, Boolean)",
                HostDecision.Host, "AI movement rule"),
            Method(Others, "patch14", Campaign + "CampaignBehaviors.PlayerTownVisitCampaignBehavior.game_menu_recruit_volunteers_on_condition(MenuCallbackArgs)",
                HostDecision.Skip, OnlyUi),
            Method(Others, "patch15", Campaign + "CampaignBehaviors.VillageHostileActionCampaignBehavior.game_menu_village_hostile_action_force_volunteers_condition(MenuCallbackArgs)",
                HostDecision.Skip, OnlyUi),
            Method(Others, "patch16", Campaign + "Settlements.Settlement.get_IsStarving()",
                HostDecision.Host, "a value the server's town and garrison rules read"),
            Method(Others, "patch17", Campaign + "Settlements.Locations.LocationCharacter..ctor(AgentData, AddBehaviorsDelegate, String, Boolean, CharacterRelations, String, Boolean, Boolean, ItemObject, Boolean, Boolean, Boolean, AfterAgentCreatedDelegate, Boolean)",
                HostDecision.Skip, "who stands in a scene on a player's PC"),
            Method(Others, "patch18", Campaign + "Hero.IsFriend(Hero)",
                HostDecision.Host, "a result the server's AI rules read"),
            Method(Others, "patch19", Campaign + "Hero.IsEnemy(Hero)",
                HostDecision.Host, "a result the server's AI rules read"),
            Method(Others, "patch20", Campaign + "Encounters.PlayerEncounter.DoCaptureHeroes()",
                HostDecision.Skip, PlayerEncounter),
            Method(Others, "patch21", Campaign + "Encounters.PlayerEncounter.DoPlayerVictory()",
                HostDecision.Skip, PlayerEncounter),
            Method(Others, "patch22", Campaign + "Encounters.PlayerEncounter.DoFreeOrCapturePrisonerHeroes()",
                HostDecision.Skip, PlayerEncounter),
            Method(Others, "patch23", Campaign + "CampaignBehaviors.VassalAndMercenaryOfferCampaignBehavior.DailyTick()",
                HostDecision.Skip, "its only rule is for a White Walker or wight player, a start that is off in co-op; with no main hero it would stop Coop's offers for everyone"),
            Method(Others, "patch24", Campaign + "CampaignBehaviors.HeroSpawnCampaignBehavior.FindASuitableSettlementToTeleportForHero(Hero, Single)",
                HostDecision.Host, "where the server places a hero"),
            Method(Others, "patch25", Campaign + "TournamentGames.FightTournamentGame.CanNpcJoinTournament(Hero, MBList`1, Boolean)",
                HostDecision.Host, "the server picks tournament participants"),
            Method(Others, "patch26", Campaign + "TournamentGames.TournamentCampaignBehavior.GetTournamentSimulationScore(Hero)",
                HostDecision.Host, "the server simulates tournament results"),
            Method(Others, "patch27", Campaign + "Actions.MakePeaceAction.ApplyInternal(IFaction, IFaction, Int32, Int32, MakePeaceDetail)",
                HostDecision.Guard, "nobody makes peace with the White Walkers; the server guard also skips Coop's MakePeaceAction postfix for a peace it vetoed, so that peace is not sent to players"),
            Method(Others, "patch28", Campaign + "CampaignBehaviors.PatrolPartiesCampaignBehavior.SpawnPatrolParty(Settlement)",
                HostDecision.Host, "cultures without a patrol template would throw in the server's daily tick"),

            // Applied by hand after ROT.SubModule's PatchAll, or at its first game start.
            Hand(Core + "NavalDLCMapDistanceModelPatch", "GetDistancePrefix", "NavalDLC.GameComponents.NavalDLCMapDistanceModel.GetDistance(Settlement, CampaignVec2&, Boolean, NavigationType)",
                HostDecision.Host, "crash guard for a distance asked from no settlement"),
            Hand(Core + "MBInitialScreenBasePatch", null, null, HostDecision.Skip, "main menu scene"),
            Hand("ROT.Dragon.AgentPatch", null, null, HostDecision.Skip, NoMissions),
        };

        private const string OneLocalPlayer = "its player rules test the one local player, who is nobody on the server";

        public static readonly ModelRow[] ModelRows =
        {
            new ModelRow(Models + "ROTKingdomDecisionPermissionModel", "KingdomDecisionPermissionModel", HostDecision.Host, "AI war and peace decisions",
                playerCheck: "reads Clan.PlayerClan.Kingdom and exempts only that clan"),
            new ModelRow(Models + "ROTSettlementMilitiaModel", "SettlementMilitiaModel", HostDecision.Host, "militia numbers of the daily tick"),
            new ModelRow(Models + "ROTSettlementFoodModel", "SettlementFoodModel", HostDecision.Host, "town food stocks"),
            new ModelRow(Models + "ROTMilitaryPowerModel", "MilitaryPowerModel", HostDecision.Host, "party strength for AI and simulated battles"),
            new ModelRow(Models + "ROTCombatSimulationModel", "CombatSimulationModel", HostDecision.Host, "dragon damage in simulated battles",
                playerCheck: "uses the server's own dragon damage setting, not the players'"),
            new ModelRow(Models + "ROTEasyCastleMilitiaModel", "SettlementMilitiaModel", HostDecision.Host, "militia growth",
                playerCheck: "its owner test is Clan.PlayerClan"),
            new ModelRow(Models + "ROTVoiceOverModel", "VoiceOverModel", HostDecision.Skip, "sound only"),
            new ModelRow(Models + "ROTPartyFoodBuyingModel", "PartyFoodBuyingModel", HostDecision.Host, "AI parties buy more food on the larger map"),
            new ModelRow(Models + "ROTBanditDensityModel", "BanditDensityModel", HostDecision.Host, "bandit parties and hideouts"),
            new ModelRow(Models + "ROTSettlementAccessModel", "SettlementAccessModel", HostDecision.Host, "a player's own menu checks; nothing on the server calls it, kept for the same model on both sides"),
            new ModelRow(Models + "ROTPartySpeedModel", "PartySpeedModel", HostDecision.Skip, "its only rule is a speed bonus for an enlisted player's lord, and " + Enlistment),
            new ModelRow(Models + "ROTClanFinanceModel", "ClanFinanceModel", HostDecision.Skip,
                "its only rule is the enlisted wage, and Coop's clan finance casts the model under the top one to the vanilla one, which fails with this wrapper over War Sails' model"),
            new ModelRow(Models + "ROTMobilePartyFoodConsumptionModel", "MobilePartyFoodConsumptionModel", HostDecision.Host, "White Walker parties do not starve"),
            new ModelRow(Models + "ROTArmyManagementCalculationModel", "ArmyManagementCalculationModel", HostDecision.Skip, "its only rule is for an enlisted player, and " + Enlistment),
            new ModelRow(Models + "ROTPartyNavigationModel", "PartyNavigationModel", HostDecision.Host, "which AI parties may sail",
                playerCheck: "exempts only the local main party, so a White Walker player's party cannot sail on the server"),
            new ModelRow(Models + "ROTPartySizeLimitModel", "PartySizeLimitModel", HostDecision.Host, "party size limits, with the White Walkers' bonus",
                needs: Behaviors + "ROTOthersCampaignBehavior"),
            new ModelRow(Models + "ROTSettlementGarrisonModel", "SettlementGarrisonModel", HostDecision.Host, "garrison growth and AI troop transfers"),
            new ModelRow(Models + "ROTPartyImpairmentModel", "PartyImpairmentModel", HostDecision.Host, "the disorganized state"),
            new ModelRow(Models + "ROTKingdomCreationModel", "KingdomCreationModel", HostDecision.Host, "a player only rule that never triggers on the server, kept for the same model on both sides"),
            new ModelRow(Models + "ROTMarriageModel", "MarriageModel", HostDecision.Host, "who may marry whom",
                playerCheck: "its exemption tests Hero.MainHero and Clan.PlayerClan, so real players get the limits meant for lords"),
            new ModelRow(Models + "ROTAgentStatCalculateModel", "AgentStatCalculateModel", HostDecision.Host, "mission stats; never called on the server, kept for the same model on both sides"),
            new ModelRow(Models + "ROTAgeModel", "AgeModel", HostDecision.Host, "ages and old age deaths"),
            new ModelRow(Models + "ROTPartyWageModel", "PartyWageModel", HostDecision.Host, "wages; the White Walkers pay none"),
            new ModelRow(Models + "ROTTroopUpgradeModel", "PartyTroopUpgradeModel", HostDecision.Host, "AI troop upgrades",
                playerCheck: "its upgrade item check reads Hero.MainHero without a null check and exempts only the server's own hero; the recruiter tests it asks are server guards"),
            new ModelRow(Models + "ROTPrisonerRecruitmentCalculationModel", "PrisonerRecruitmentCalculationModel", HostDecision.Host, "prisoner recruitment"),
            new ModelRow(TimeModelType, "CampaignTimeModel", HostDecision.Host, "the calendar: start year and day lengths"),
            new ModelRow("RoT_Elephants.RoTElephantAgentStatCalculateModel", "AgentStatCalculateModel", HostDecision.Host, "mission stats; never called on the server, kept for the same model on both sides"),
        };

        public static readonly BehaviorRow[] BehaviorRows =
        {
            new BehaviorRow(Behaviors + "ROTStorylineWars", HostDecision.Host,
                "storyline wars, forced on player kingdoms too as on the player's; a server guard makes the kings protect the Night's Watch from every kingdom a player's clan rules", field: "StorylineWars"),
            new BehaviorRow(Behaviors + "ROTDuelsBehavior", HostDecision.Later,
                "a duel's outcome must be applied on the server for the right player; as written it acts on the local main party", field: "DuelsBehavior", setting: "Duels", inert: true),
            new BehaviorRow(Behaviors + "ROTRavenBehavior", HostDecision.Skip, "ravens are each player's own", field: "RavenBehavior", setting: "Ravens", inert: true),
            new BehaviorRow(Behaviors + "ROTEventBehavior", RotStoryRules.Decision(RotStoryRules.StoryOn),
                RotStoryRules.Why(RotStoryRules.StoryOn), field: "EventBehavior", inert: !RotStoryRules.StoryOn),
            new BehaviorRow(Behaviors + "ROTGankBehavior", HostDecision.Later, "the take happens in a player's dialog on the local main party", inert: true),
            new BehaviorRow(Behaviors + "ROTSpawnUniqueWanderers", HostDecision.Host, "keeps one of each named wanderer in the world"),
            new BehaviorRow(Behaviors + "ROTTownTradersBehavior", HostDecision.Later,
                "purchases must be applied on the server for the right player; as written they are dialogs on the local main party", field: "TownTradersBehavior", inert: true),
            new BehaviorRow(Behaviors + "ROTTradeBoundBehavior", HostDecision.Host, "gives castle villages a trade town"),
            new BehaviorRow(Behaviors + "ROTCoreBehavior", HostDecision.Host,
                "gold for poor AI clans; a server guard runs its rule without the server's hero and keeps every player's clan out", field: "CoreBehavior"),
            new BehaviorRow(Behaviors + "ROTRaceMap", HostDecision.Host, "keeps each hero's race in the server's save"),
            // Stores nothing, but the troop upgrade model asks it about every party.
            new BehaviorRow(Behaviors + "ROTTroopRecruiter", HostDecision.Host,
                "AI lords' house troops; server guards keep players' parties, clans and fiefs out and stop it reading the server's own hero", field: "Recruiter"),
            new BehaviorRow(Behaviors + "ROTDragonBehavior", HostDecision.Host, "the find a mount rule that never hands out dragons", field: "DragonBehavior"),
            new BehaviorRow(Behaviors + "ValyrianThiefCampaignBehavior", HostDecision.Skip, "only acts in a player's mission"),
            new BehaviorRow(Behaviors + "ROTClanRecruitBehavior", HostDecision.Later, "the gold and the kingdom join happen in a player's dialog"),
            new BehaviorRow(Behaviors + "ROTHouseTroopsBehavior", HostDecision.Later, "the purchase happens in a player's dialog with popups"),
            new BehaviorRow(Behaviors + "ROTEnlistmentBehavior", HostDecision.Skip, Enlistment, field: "EnlistmentBehavior", inert: true),
            new BehaviorRow(Behaviors + "ROTEnlistmentMenuBehavior", HostDecision.Skip, Enlistment),
            new BehaviorRow(Behaviors + "ROTRedWeddingBehavior", HostDecision.Skip, "a menu and a mission for one player", inert: true),
            new BehaviorRow(Behaviors + "ROTSendToWallBehavior", HostDecision.Later, "the clan and relation changes happen in a player's dialog"),
            new BehaviorRow(Behaviors + "ROTOthersCampaignBehavior", HostDecision.Host,
                "the White Walkers; server guards spare players at the Night King's death (its story event runs with no popup screen), keep the wight player branch off and log each culture write Coop does not send"),
            new BehaviorRow(Behaviors + "ROTEasterEggWeapons", HostDecision.Later, "the find comes from a player's mission and goes to the local main party", inert: true),
            new BehaviorRow(Behaviors + "ROTBanditLeaveHideouts", HostDecision.Host, "bandit parties leave full hideouts"),
            new BehaviorRow(Behaviors + "ROTRelationshipsBehavior", HostDecision.Host,
                "lords' marriages, in place of the vanilla tick Coop guards; a server guard keeps players' heroes and their clans out"),
            new BehaviorRow(Behaviors + "ROTRBMCompatibility", HostDecision.Skip, "only for Realistic Battle Mod, which does not run on the server"),
            // Not in Realm of Thrones' game start list: a patch swaps it in for the vanilla one.
            new BehaviorRow("ROT.ROTCharacterCreationCampaignBehavior", HostDecision.Later,
                "the server creates its hero headless; a player who starts as a White Walker still needs the server to set that clan's wars"),
        };

        // BannerColorPersistence.dll, the second DLL of ROT-Core. Of its 16 patches only these
        // decide world data; the others are pictures and screens and are never applied.
        public const string BannerHarmonyId = "BannerColorPersistance with BannerPaste";
        public const string BannerSubModuleType = "BannerColorPersistence.PersistColorSubModule";

        public static readonly PatchRow[] BannerPatches =
        {
            Class("BannerColorPersistence.PreventBannerColorUpdates", HostDecision.Host, "clans keep their own banner colours, as on players' PCs"),
            Class("BannerColorPersistence.AssignUniqueSecondaryBannerColor", HostDecision.Host, "the icon colour a clan gets from its banner"),
            Class("BannerColorPersistence.UpdateKingdomColorOnBannerChange", HostDecision.Skip, "Coop's own banner handler moves a ruler's colours onto the kingdom"),
        };
    }
}
