#if COOPFAMILY_VERIFY
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace CoopMarriage;

// Compile-time-only probe: absent from all distributable DLLs. Runs solely on
// an explicitly requested MashTool TestRuns campaign, never normal user saves.
internal static class CoopFamilyVerification
{
    private static bool _ran;
    internal static void Tick()
    {
        if (_ran || Environment.GetEnvironmentVariable("COOPFAMILY_VERIFY_PREGNANCY") != "1") return;
        string data = Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR");
        if (string.IsNullOrWhiteSpace(data) || !Path.GetFullPath(data).Contains("\\MashTool\\TestRuns\\")) return;
        string directory = Path.Combine(data, "FamilyVerification");
        if (!File.Exists(Path.Combine(directory, "request")) || File.Exists(Path.Combine(directory, "result.txt"))) return;
        if (Campaign.Current == null || !Campaign.Current.GameStarted || !CoopCompatibility.IsAuthoritative) return;
        _ran = true;
        var proof = new System.Collections.Generic.List<string>();
        var fixtureHarmony = new Harmony("s1mash.coopfamily.fixture-only");
        bool lifeDeathWasDisabled = CampaignOptions.IsLifeDeathCycleDisabled;
        try
        {
            proof.Add("Fixture original life/death disabled=" + lifeDeathWasDisabled + "; enabled temporarily for native daily pregnancy test.");
            CampaignOptions.IsLifeDeathCycleDisabled = false;
            var behavior = Campaign.Current.CampaignBehaviorManager.GetBehavior<PregnancyCampaignBehavior>();
            var records = (IList)AccessTools.Field(typeof(PregnancyCampaignBehavior), "_heroPregnancies").GetValue(behavior);
            var recordType = typeof(PregnancyCampaignBehavior).GetNestedType("Pregnancy", BindingFlags.Public | BindingFlags.NonPublic);
            var motherField = AccessTools.Field(recordType, "Mother");
            var fatherField = AccessTools.Field(recordType, "Father");
            var ctor = recordType.GetConstructor(new[] { typeof(Hero), typeof(Hero), typeof(CampaignTime) });
            var daily = AccessTools.Method(typeof(PregnancyCampaignBehavior), "DailyTickHero", new[] { typeof(Hero) });
            var modelType = Campaign.Current.Models.PregnancyModel.GetType();
            foreach (string name in new[] { "StillbirthProbability", "DeliveringTwinsProbability", "MaternalMortalityProbabilityInLabor" })
                fixtureHarmony.Patch(AccessTools.PropertyGetter(modelType, name), postfix: new HarmonyMethod(typeof(CoopFamilyVerification), nameof(Zero)));
            var mother = Hero.AllAliveHeroes.First(h => h.IsFemale && h.IsAlive && h.Age > 18 && h.Age < 40 && h.Clan != null && !h.Clan.IsRebelClan && !h.IsPregnant && h.Spouse != null && h.Spouse.IsAlive && !h.Spouse.IsFemale && !records.Cast<object>().Any(r => ReferenceEquals(motherField.GetValue(r), h)));
            var father = mother.Spouse;
            int before = records.Count;
            MakePregnantAction.Apply(mother);
            var matches = records.Cast<object>().Where(r => ReferenceEquals(motherField.GetValue(r), mother)).ToList();
            Require(mother.IsPregnant && records.Count == before + 1 && matches.Count == 1 && ReferenceEquals(fatherField.GetValue(matches[0]), father), "native conception event created one exact-parent record");
            proof.Add("PASS actual MakePregnantAction -> restored event -> canonical mother/father record: " + mother.StringId + "/" + father.StringId);
            var originalRecord = matches[0];
            for (int i = 0; i < 5; i++) MakePregnantAction.Apply(mother);
            Require(records.Count == before + 1 && records.Contains(originalRecord), "repeated native action preserves original record and deadline");
            proof.Add("PASS repeated MakePregnantAction preserves pregnancy record and deadline");
            CampaignEventDispatcher.Instance.OnChildConceived(mother);
            Require(records.Count == before + 1, "duplicate conception event must not duplicate record");
            proof.Add("PASS repeated conception event deduplicated");
            int childrenBefore = mother.Children.Count;
            daily.Invoke(behavior, new object[] { mother });
            Require(mother.Children.Count == childrenBefore && records.Contains(originalRecord), "future pregnancy must not deliver early");
            proof.Add("PASS future pregnancy does not deliver early");
            records.Remove(matches[0]);
            records.Add(ctor.Invoke(new object[] { mother, father, CampaignTime.DaysFromNow(-1) }));
            daily.Invoke(behavior, new object[] { mother });
            Require(!mother.IsPregnant && mother.Children.Count == childrenBefore + 1 && records.Count == before, "actual native due delivery: pregnant=" + mother.IsPregnant + "; children=" + childrenBefore + "->" + mother.Children.Count + "; records=" + before + "->" + records.Count);
            var child = mother.Children.Last();
            Require(child.Mother == mother && child.Father == father && child.Clan != null, "newborn parents and clan");
            Require(child.Age >= 0f && child.Age < 0.1f, "native newborn age retained without 12-year override");
            proof.Add("PASS actual native birth: child=" + child.StringId + "; correct parents/clan; age=" + child.Age);
            // Prevent a fresh unrelated conception only in this fixture while
            // checking that the consumed due record cannot deliver again.
            fixtureHarmony.Patch(AccessTools.Method(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit"), prefix: new HarmonyMethod(typeof(CoopFamilyVerification), nameof(SkipFixtureConception)));
            for (int i = 0; i < 5; i++) daily.Invoke(behavior, new object[] { mother });
            Require(mother.Children.Count == childrenBefore + 1 && !mother.IsPregnant && records.Count == before, "completed pregnancy cannot deliver repeatedly");
            proof.Add("PASS five subsequent native daily calls do not repeat the completed birth");
            var family = Campaign.Current.CampaignBehaviorManager.GetBehavior<MarriageFamilyBehavior>();
            var childIds = (System.Collections.Generic.List<string>)AccessTools.Field(typeof(MarriageFamilyBehavior), "_acceleratedChildren").GetValue(family);
            var maturityDays = (System.Collections.Generic.List<double>)AccessTools.Field(typeof(MarriageFamilyBehavior), "_childMaturityDays").GetValue(family);
            float motherAge = mother.Age;
            childIds.Add(child.StringId); maturityDays.Add(0);
            childIds.Add(mother.StringId); maturityDays.Add(0);
            AccessTools.Method(typeof(MarriageFamilyBehavior), "MatureChildren").Invoke(family, null);
            Require(child.Age < 0.1f && mother.Age == motherAge && childIds.Count == 0 && maturityDays.Count == 0, "legacy schedules removed without birthday changes");
            proof.Add("PASS legacy accelerated schedules retired without changing newborn or existing hero ages");
            proof.Add("SCOPE: actual Bannerlord/Coop host, deterministic fixture-only birth probabilities; no remote client/player session tested.");
            File.WriteAllLines(Path.Combine(directory, "result.txt"), proof);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "result.txt"), string.Join(Environment.NewLine, proof) + Environment.NewLine + "FAIL " + ex);
        }
        finally { fixtureHarmony.UnpatchAll(fixtureHarmony.Id); CampaignOptions.IsLifeDeathCycleDisabled = lifeDeathWasDisabled; }
    }
    private static void Zero(ref float __result) { __result = 0f; }
    private static bool SkipFixtureConception() { return false; }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
#endif
