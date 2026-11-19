#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // What Realm of Thrones' settings answer in a Coop session, the same on the server and on every
    // player's PC. Realm of Thrones reads its MCM settings through the 48 static getters of
    // ROT.ROTSettings (and AI elephants read RoTElephantSettings.elephantAttackChance). On the
    // server no MCM provider runs, so every getter gives Realm of Thrones' own default; on a
    // player's PC each getter gives that player's own MCM value. In a session SettingsPins answers
    // from this one table instead: the pins for the first co-op version, Realm of Thrones' default
    // for every other setting that changes world rules, models or AI, and nothing for the
    // settings that only change what one player sees or hears (those keep the player's value).
    // A player's saved MCM values are never written. No game types, so the launcher tests can
    // read it.
    internal static class SettingsRules
    {
        public const string SettingsType = "ROT.ROTSettings";
        public const string ElephantSettingsType = "RoT_Elephants.RoTElephantSettings";
        public const string RavenManagerType = "ROT.Ravens.RavenManager";
        public const string RavenMixinType = "ROT.UIExtenderPatches.EncyclopediaHeroPageVMMixin";
        public const string DuelsBehaviorType = "ROT.CampaignBehaviors.ROTDuelsBehavior";
        public const string RotSubModuleType = "ROT.SubModule";
        public const string RavensOffText = "Ravens are off in co-op.";

        public enum Kind
        {
            // Fixed for the first co-op version, whatever the player picked.
            Pinned,
            // Must match on every PC, so Realm of Thrones' default.
            Default,
            // Only this player sees or hears it; the getter is not patched.
            PlayersOwn,
        }

        public sealed class Row
        {
            public readonly string Type;
            public readonly string Name;
            public readonly Kind Kind;
            // bool, int or float as the getter returns it; null for a player's own setting.
            public readonly object Value;

            public Row(string type, string name, Kind kind, object value)
            {
                Type = type;
                Name = name;
                Kind = kind;
                Value = value;
            }

            public bool Patched => Kind != Kind.PlayersOwn;
        }

        private static Row Pin(string name, object value) => new Row(SettingsType, name, Kind.Pinned, value);
        private static Row Default(string name, object value) => new Row(SettingsType, name, Kind.Default, value);
        private static Row Own(string name) => new Row(SettingsType, name, Kind.PlayersOwn, null);

        // Every getter of ROT.ROTSettings in ROT-Core v8.1.8, then the elephant one. Defaults are
        // ROTSettingsMCM's, which the getters also give without MCM, except RBMIntegration (true
        // without MCM, false in MCM), which is pinned.
        public static readonly Row[] Rows =
        {
            // Pinned: the duel and raven behaviors are off in co-op; plate encumbrance and the
            // custom kingdom troops rule test the local player; RBM never runs on the server; the
            // event list is seeded once at session launch on every machine. RBMIntegration only
            // holds for the readers at play time: Realm of Thrones' two RBM XML merge steps run at
            // module load, before any session, so a player with RBM still gets their own value there.
            Pin("Duels", false),
            Pin("Ravens", false),
            Pin("PlayerPlateEncumbrance", true),
            Pin("RBMIntegration", false),
            Pin("MyLittleWarbandAllowCustomKingdomPartyTroops", false),
            Pin("Events", true),
            // War rules and the world.
            Default("KingsProtectNightsWatch", true),
            Default("NightsWatchMayNotWar", true),
            Default("FreeFolkMayNotWar", true),
            Default("DebuffedMilitias", true),
            // Dragons.
            Default("DragonDamageScaling", 2000),
            Default("LethalDragonDamage", 0f),
            Default("FriendlyFire", false),
            Default("DragonHealth", 5000),
            // The Others.
            Default("OthersInvasion", true),
            Default("OthersRaiding", true),
            Default("OthersSieging", true),
            Default("OthersAIShackles", true),
            Default("OthersNecromancyMultiplier", 1f),
            Default("OthersLordConversionRate", 0.05f),
            Default("OthersPartySizeLimitGrowth", 3f),
            Default("OthersNKCanDieFromStart", false),
            // Features that are off in co-op for now, kept equal in case one comes back.
            Default("AllowableClanTierDifference", 2),
            Default("EnableRavenAccidents", true),
            Default("EnableRavenRestictions", true),
            Default("RavenFlightTime", 3),
            Default("SendRavenGoldCost", 100),
            Default("MinimumEnemyPartySize", 10),
            Default("RenownPerBattle", 1f),
            Default("RelationPerXBattles", 2f),
            Default("CraftingStaminaPerBattle", 0.15f),
            Default("PartyRoleChangeCoolDown", 5),
            Default("EnlistWithEnemiesCooldown", 7),
            Default("GlobalXPBonus", 2.5f),
            Default("GearLevelOffset", -7),
            Default("RelationshipPenaltiesForLeaving", false),
            Default("RelationshipRequirements", true),
            Default("AllowMultipleRepurchasesofGear", false),
            Default("EnlistmentQuestRequirements", 1f),
            // Music, the flight sound and the enlistment markers and callouts.
            Own("ROTMusicTracks"),
            Own("DanysMusic"),
            Own("FormationOrderCallouts"),
            Own("FormationMarker"),
            Own("MarkerBob"),
            Own("MarkerHeight"),
            Own("MarkerSize"),
            Own("MarkerColor"),
            Own("MarkerBorderColor"),
            // Read straight from MCM's object by AI elephants, not through ROTSettings. That reader
            // is null safe: where no MCM object exists (the server, a PC without MCM) it never
            // calls the getter and elephants never do that attack, so this row only holds on PCs
            // where MCM runs, and the session line says so.
            new Row(ElephantSettingsType, "elephantAttackChance", Kind.Default, 0.5f),
            // Not in the table: the player's dragon reads DragonTageting (its flight aiming aid)
            // and the elephant camera reads elephantCameraDistance straight from MCM's objects, so
            // both stay each player's own whatever the table says.
        };

        public static IEnumerable<Row> PatchedRows => Rows.Where(row => row.Patched);

        // A pinned on or off switch (Duels, Ravens, ...) the host makes behaviors under; null when
        // the setting is not pinned or not a bool.
        public static bool? PinnedSwitch(string name)
        {
            var row = Rows.FirstOrDefault(r => r.Type == SettingsType && r.Name == name && r.Kind == Kind.Pinned);
            return row?.Value is bool value ? value : (bool?)null;
        }

        // The patched values in table order, "Duels False, ..., elephantAttackChance 0.5".
        public static string ValueText(IEnumerable<Row> rows) =>
            string.Join(", ", rows.Select(row => row.Name + " " + Text(row.Value)));

        // A short fingerprint of every patched name and value, so two logs show at a glance
        // whether they answer from the same table.
        public static string Fingerprint()
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in ValueText(PatchedRows))
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        // The one line each side logs when the table starts to answer, the same text on the
        // server and on a player's PC when both cover the same getters.
        public static string SessionLine(string side, int covered, IEnumerable<string> missing, string readsNow)
        {
            var patched = PatchedRows.Count();
            var missingList = (missing ?? Enumerable.Empty<string>()).ToList();
            return "Settings: in this Coop session Realm of Thrones' settings answer from the co-op table on the " + side + ": "
                + covered + " of " + patched + " getters covered" + (missingList.Count == 0 ? "" : " (NOT covered: " + string.Join(", ", missingList) + ")")
                + ", " + Rows.Count(row => !row.Patched) + " left to each player; pinned " + ValueText(Rows.Where(row => row.Kind == Kind.Pinned))
                + "; elephantAttackChance holds only where MCM runs; table " + Fingerprint() + (string.IsNullOrEmpty(readsNow) ? "" : "; reads now " + readsNow) + ".";
        }

        public static string Text(object value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
