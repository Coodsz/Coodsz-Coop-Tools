using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace CoopMarriage;

internal sealed class MarriageFamilyBehavior : CampaignBehaviorBase
{
	internal const int MaximumPartners = 3;

	private const int DailyPregnancyChance = 4;

	private const int MaximumPregnancyAge = 52;

	private const int ChildAdultAge = 18;

	private const int PregnancyCooldownDays = 120;

	private List<string> _mothers = new List<string>();

	private List<string> _fathers = new List<string>();

	private List<string> _partnerPlayers = new List<string>();

	private List<string> _partnerSpouses = new List<string>();

	private List<double> _dueDays = new List<double>();

	private List<string> _acceleratedChildren = new List<string>();

	private List<double> _childMaturityDays = new List<double>();

	private List<string> _cooldownMothers = new List<string>();

	private List<double> _cooldownUntilDays = new List<double>();

	private List<string> _repairedAdultChildren = new List<string>();

	private List<string> _frozenPlayerAgeIds = new List<string>();

	private List<double> _frozenPlayerAges = new List<double>();

	[field: CompilerGenerated]
	internal static MarriageFamilyBehavior Current
	{
		[CompilerGenerated]
		get;
		[CompilerGenerated]
		private set;
	}

	internal MarriageFamilyBehavior()
	{
		Current = this;
	}

	public override void RegisterEvents()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		CampaignEvents.DailyTickEvent.AddNonSerializedListener((object)this, new Action(OnDailyTick));
		CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener((object)this, new Action<Hero, List<Hero>, int>(OnVanillaBirth));
	}

	public override void SyncData(IDataStore d)
	{
		d.SyncData<List<string>>("coop_marriage_mothers", ref _mothers);
		d.SyncData<List<string>>("coop_marriage_fathers", ref _fathers);
		d.SyncData<List<double>>("coop_marriage_due_days", ref _dueDays);
		d.SyncData<List<string>>("coop_marriage_players", ref _partnerPlayers);
		d.SyncData<List<string>>("coop_marriage_spouses", ref _partnerSpouses);
		d.SyncData<List<string>>("coop_marriage_accelerated_children", ref _acceleratedChildren);
		d.SyncData<List<double>>("coop_marriage_child_maturity_days", ref _childMaturityDays);
		d.SyncData<List<string>>("coop_marriage_cooldown_mothers", ref _cooldownMothers);
		d.SyncData<List<double>>("coop_marriage_cooldown_until_days", ref _cooldownUntilDays);
		d.SyncData<List<string>>("coop_marriage_repaired_adult_children", ref _repairedAdultChildren);
		d.SyncData<List<string>>("coop_marriage_frozen_player_age_ids", ref _frozenPlayerAgeIds);
		d.SyncData<List<double>>("coop_marriage_frozen_player_ages", ref _frozenPlayerAges);
		_mothers = _mothers ?? new List<string>();
		_fathers = _fathers ?? new List<string>();
		_dueDays = _dueDays ?? new List<double>();
		_partnerPlayers = _partnerPlayers ?? new List<string>();
		_partnerSpouses = _partnerSpouses ?? new List<string>();
		_acceleratedChildren = _acceleratedChildren ?? new List<string>();
		_childMaturityDays = _childMaturityDays ?? new List<double>();
		_cooldownMothers = _cooldownMothers ?? new List<string>();
		_cooldownUntilDays = _cooldownUntilDays ?? new List<double>();
		_repairedAdultChildren = _repairedAdultChildren ?? new List<string>();
		_frozenPlayerAgeIds = _frozenPlayerAgeIds ?? new List<string>();
		_frozenPlayerAges = _frozenPlayerAges ?? new List<double>();
		TrimPregnancies();
		TrimPartners();
		TrimAcceleratedChildren();
		TrimCooldowns();
	}

	private void OnDailyTick()
	{
		if (!CoopCompatibility.IsAuthoritative)
		{
			return;
		}
		try
		{
			// Native DailyTickHero now owns conception and delivery exactly once.
			MatureChildren();
			RepairZeroSkillAdultChildren();
		}
		catch (System.Exception ex)
		{
			RuntimeProof.Write("Child aging daily tick failed: " + (object)ex);
		}
	}

	private void OnVanillaBirth(Hero mother, List<Hero> children, int stillbornCount)
	{
		if (!CoopCompatibility.IsAuthoritative || children == null)
		{
			return;
		}
		foreach (Hero child in children)
		{
			if (child == null)
			{
				continue;
			}
			string id = ((MBObjectBase)child).StringId;
			StopAcceleratedAging(id);
			if (child.IsAlive && child.HeroDeveloper != null && Campaign.Current != null && Campaign.Current.GameStarted && child.Age < 0.1f)
			{
				int skills = 0;
				foreach (SkillObject skill in MBObjectManager.Instance.GetObjectTypeList<SkillObject>())
				{
					if (skill != null)
					{
						child.SetSkillValue(skill, MBRandom.RandomInt(80) + 1);
						skills++;
					}
				}
				RuntimeProof.Write("Newborn random starting skills 1-80: child=" + id + "; skills=" + skills);
			}
			RuntimeProof.Write("Native newborn age retained; natural education and coming-of-age progression: mother=" + ((MBObjectBase)mother).StringId + "; child=" + id + "; age=" + child.Age);
		}
	}
#if LEGACY_COOPMARRIAGE_PRIVATE_PREGNANCY_DISABLED
	private void RemoveUntrackedPregnancies()
	{
		TrimPregnancies();
		TrimPartners();
		for (int num = _mothers.Count - 1; num >= 0; num--)
		{
			Hero mother = Find(_mothers[num]);
			Hero father = Find(_fathers[num]);
			bool flag = IsKnownPartnerPair(father, mother);
			if (!flag)
			{
				Hero val = Find(_mothers[num]);
				if (val != null)
				{
					val.IsPregnant = false;
				}
				RuntimeProof.Write("Removed untracked pregnancy created by earlier scan: mother=" + _mothers[num] + "; father=" + _fathers[num]);
				RemovePregnancy(num);
			}
		}
	}

	// Repairs pregnancies that were saved only as Hero.IsPregnant by an older
	// version. The child limit applies to conception only, never to delivery.
	private bool IsKnownPartnerPair(Hero father, Hero mother)
	{
		if (father == null || mother == null)
		{
			return false;
		}
		for (int i = 0; i < _partnerPlayers.Count; i++)
		{
			if ((_partnerPlayers[i] == ((MBObjectBase)father).StringId && _partnerSpouses[i] == ((MBObjectBase)mother).StringId) || (_partnerPlayers[i] == ((MBObjectBase)mother).StringId && _partnerSpouses[i] == ((MBObjectBase)father).StringId))
			{
				return true;
			}
		}
		if (father.Spouse == mother || mother.Spouse == father)
		{
			return true;
		}
		return Romance.RomanticStateList != null && Enumerable.Any<Romance.RomanticState>((System.Collections.Generic.IEnumerable<Romance.RomanticState>)Romance.RomanticStateList, (Func<Romance.RomanticState, bool>)((Romance.RomanticState state) => state != null && (int)state.Level == 7 && ((state.Person1 == father && state.Person2 == mother) || (state.Person1 == mother && state.Person2 == father))));
	}

	private void RemoveExternalPregnancies()
	{
		TrimPregnancies();
		foreach (Hero mother in (List<Hero>)(object)Hero.AllAliveHeroes)
		{
			if (mother == null || !mother.IsFemale || !mother.IsPregnant ||
				_mothers.Contains(((MBObjectBase)mother).StringId))
			{
				continue;
			}
			mother.IsPregnant = false;
			RuntimeProof.Write("Blocked pregnancy created outside COOP Family: mother=" +
				((MBObjectBase)mother).StringId);
		}
	}
	private void TryConceptions()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Expected O, but got Unknown
		if (12 <= 0)
		{
			return;
		}
		List<Tuple<Hero, Hero>> eligibleCouples = GetEligibleCouples();
		RuntimeProof.Write("Pregnancy scan: eligibleCouples=" + eligibleCouples.Count);
		var enumerator = eligibleCouples.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				Tuple<Hero, Hero> current = enumerator.Current;
				Hero item = current.Item1;
				Hero item2 = current.Item2;
				if (CanConceive(item, item2) && MBRandom.RandomInt(100) < DailyPregnancyChance)
				{
					MakePregnantAction.Apply(item);
					_mothers.Add(((MBObjectBase)item).StringId);
					_fathers.Add(((MBObjectBase)item2).StringId);
					List<double> dueDays = _dueDays;
					float pregnancyDurationInDays = 21;
					CampaignTime val = CampaignTime.DaysFromNow(pregnancyDurationInDays);
					dueDays.Add(val.ToDays);
					RuntimeProof.Write(string.Concat(new string[5]
					{
						"Pregnancy synchronized: mother=",
						((MBObjectBase)item).StringId,
						"; father=",
						((MBObjectBase)item2).StringId,
						"; chance=" + DailyPregnancyChance + "; dueDays=" + pregnancyDurationInDays
					}));
					TextObject val2 = new TextObject("{=coop_marriage_notice_pregnant}{MOTHER} is pregnant.", (Dictionary<string, object>)null);
					val2.SetTextVariable("MOTHER", item.Name);
					Notify(val2);
				}
			}
		}
		finally
		{
			((System.IDisposable)enumerator/*cast due to .constrained prefix*/).Dispose();
		}
	}

	private List<Tuple<Hero, Hero>> GetEligibleCouples()
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Invalid comparison between Unknown and I4
		List<Tuple<Hero, Hero>> val = new List<Tuple<Hero, Hero>>();
		TrimPartners();
		for (int i = 0; i < _partnerPlayers.Count; i++)
		{
			AddCouple(val, Find(_partnerPlayers[i]), Find(_partnerSpouses[i]));
		}
		if (Romance.RomanticStateList != null)
		{
			var enumerator = Romance.RomanticStateList.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Romance.RomanticState current = enumerator.Current;
					if (current != null && (int)current.Level == 7)
					{
						AddCouple(val, current.Person1, current.Person2);
					}
				}
			}
			finally
			{
				((System.IDisposable)enumerator/*cast due to .constrained prefix*/).Dispose();
			}
		}
		var enumerator2 = ((List<Hero>)(object)Hero.AllAliveHeroes).GetEnumerator();
		try
		{
			while (enumerator2.MoveNext())
			{
				Hero current2 = enumerator2.Current;
				if (current2 != null && current2.Spouse != null)
				{
					AddCouple(val, current2, current2.Spouse);
				}
			}
			return val;
		}
		finally
		{
			((System.IDisposable)enumerator2/*cast due to .constrained prefix*/).Dispose();
		}
	}

	private static void AddCouple(List<Tuple<Hero, Hero>> r, Hero a, Hero b)
	{
		if (a != null && b != null && a != b)
		{
			Hero m = (a.IsFemale ? a : (b.IsFemale ? b : null));
			Hero f = ((m == a) ? b : a);
			if (m != null && f != null && !f.IsFemale && !Enumerable.Any<Tuple<Hero, Hero>>((System.Collections.Generic.IEnumerable<Tuple<Hero, Hero>>)r, (Func<Tuple<Hero, Hero>, bool>)((Tuple<Hero, Hero> x) => x.Item1 == m && x.Item2 == f)))
			{
				r.Add(Tuple.Create<Hero, Hero>(m, f));
			}
		}
	}

	private bool CanConceive(Hero m, Hero f)
	{
		if (!m.IsAlive || !f.IsAlive || m.Age > MaximumPregnancyAge || m.PartyBelongedTo == null || m.PartyBelongedTo != f.PartyBelongedTo || m.IsPregnant || _mothers.Contains(((MBObjectBase)m).StringId) || IsOnPregnancyCooldown(m) || ((List<Hero>)(object)m.Children).Count >= 12)
		{
			return false;
		}
		float num = Campaign.Current.Models.AgeModel.HeroComesOfAge;
		if (m.Age >= num)
		{
			return f.Age >= num;
		}
		return false;
	}

	private void DeliverBirths()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Expected O, but got Unknown
		TrimPregnancies();
		CampaignTime now = CampaignTime.Now;
		double toDays = now.ToDays;
		for (int num = _mothers.Count - 1; num >= 0; num--)
		{
			if (!(_dueDays[num] > toDays))
			{
				Hero val = Find(_mothers[num]);
				Hero val2 = Find(_fathers[num]);
				RemovePregnancy(num);
				if (val == null || val2 == null || !val.IsAlive || !val2.IsAlive)
				{
					if (val != null)
					{
						val.IsPregnant = false;
					}
				}
				else
				{
					Hero val3 = HeroCreator.DeliverOffSpring(val, val2, MBRandom.RandomInt(2) == 0);
					CampaignEventDispatcher instance = CampaignEventDispatcher.Instance;
					List<Hero> obj = new List<Hero>();
					obj.Add(val3);
					((CampaignEventReceiver)instance).OnGivenBirth(val, obj, 0);
					if (val3 != null)
					{
						StopAcceleratedAging(((MBObjectBase)val3).StringId);
					}
					val.IsPregnant = false;
					SetPregnancyCooldown(val);
					RuntimeProof.Write(string.Concat(new string[6]
					{
						"Birth synchronized: mother=",
						((MBObjectBase)val).StringId,
						"; father=",
						((MBObjectBase)val2).StringId,
						"; child=",
						(val3 == null) ? "null" : ((MBObjectBase)val3).StringId + "; sex=" + (val3.IsFemale ? "female" : "male")
					}));
					TextObject val4 = new TextObject("{=coop_marriage_notice_birth}{MOTHER} has given birth to {CHILD}.", (Dictionary<string, object>)null);
					val4.SetTextVariable("MOTHER", val.Name);
					val4.SetTextVariable("CHILD", val3.Name);
					Notify(val4);
				}
			}
		}
	}

#endif
	private void MatureChildren()
	{
		TrimAcceleratedChildren();
		for (int i = _acceleratedChildren.Count - 1; i >= 0; i--)
		{
			// Retire old acceleration schedules without rewriting any birthday.
			// Existing children keep their saved ages; newborns remain newborns.
			_acceleratedChildren.RemoveAt(i);
			_childMaturityDays.RemoveAt(i);
		}
	}

	private void StopAcceleratedAging(string childId)
	{
		TrimAcceleratedChildren();
		for (int i = _acceleratedChildren.Count - 1; i >= 0; i--)
		{
			if (_acceleratedChildren[i] == childId)
			{
				_acceleratedChildren.RemoveAt(i);
				_childMaturityDays.RemoveAt(i);
			}
		}
	}

	private void RepairZeroSkillAdultChildren()
	{
		foreach (Hero child in (List<Hero>)(object)Hero.AllAliveHeroes)
		{
			if (child == null || child.Age < ChildAdultAge || child.Mother == null || child.Father == null ||
				(!IsRecordedPlayer(child.Mother) && !IsRecordedPlayer(child.Father)))
			{
				continue;
			}
			string id = ((MBObjectBase)child).StringId;
			if (_repairedAdultChildren.Contains(id))
			{
				continue;
			}
			List<ValueTuple<SkillObject, int>> inherited = Campaign.Current.Models.HeroCreationModel.GetInheritedSkillsForHero(child);
			bool allZero = inherited != null && inherited.Count > 0 && inherited.All(x => child.GetSkillValue(x.Item1) <= 0);
			if (allZero)
			{
				CampaignEventDispatcher.Instance.OnHeroComesOfAge(child);
				RuntimeProof.Write("Repaired zero-skill adult child through vanilla coming-of-age event: child=" + id);
			}
			_repairedAdultChildren.Add(id);
		}
	}
	internal void RecordPartner(Hero p, Hero s)
	{
		if (p == null || s == null)
		{
			return;
		}
		TrimPartners();
		for (int i = 0; i < _partnerPlayers.Count; i++)
		{
			if (_partnerPlayers[i] == ((MBObjectBase)p).StringId && _partnerSpouses[i] == ((MBObjectBase)s).StringId)
			{
				return;
			}
		}
		_partnerPlayers.Add(((MBObjectBase)p).StringId);
		_partnerSpouses.Add(((MBObjectBase)s).StringId);
		RuntimeProof.Write("Vanilla marriage recorded: " + ((MBObjectBase)p).StringId + " + " + ((MBObjectBase)s).StringId);
	}

	internal static int PartnerCount(Hero h)
	{
		if (h == null)
		{
			return 0;
		}
		MarriageFamilyBehavior b = Current;
		if (b != null)
		{
			b.TrimPartners();
			int num = Enumerable.Count<string>(Enumerable.Distinct<string>(Enumerable.Select(Enumerable.Where(Enumerable.Select((System.Collections.Generic.IEnumerable<string>)b._partnerPlayers, (string id, int i) => new { id, i }), x => x.id == ((MBObjectBase)h).StringId && Find(b._partnerSpouses[x.i]) != null), x => b._partnerSpouses[x.i])));
			if (num > 0)
			{
				return num;
			}
		}
		return Enumerable.Count<Hero>((System.Collections.Generic.IEnumerable<Hero>)Hero.AllAliveHeroes, (Func<Hero, bool>)((Hero x) => x != null && x != h && (x.Spouse == h || ((List<Hero>)(object)x.ExSpouses).Contains(h))));
	}

	internal static bool IsSecondaryPartner(Hero a, Hero b)
	{
		if (a == null || b == null)
		{
			return false;
		}
		MarriageFamilyBehavior current = Current;
		if (current != null)
		{
			current.TrimPartners();
			List<string> val = new List<string>();
			for (int i = 0; i < current._partnerPlayers.Count; i++)
			{
				if (current._partnerPlayers[i] == ((MBObjectBase)a).StringId && !val.Contains(current._partnerSpouses[i]))
				{
					val.Add(current._partnerSpouses[i]);
				}
				else if (current._partnerSpouses[i] == ((MBObjectBase)a).StringId && !val.Contains(current._partnerPlayers[i]))
				{
					val.Add(current._partnerPlayers[i]);
				}
			}
			int num = val.IndexOf(((MBObjectBase)b).StringId);
			if (num >= 0)
			{
				return num > 0;
			}
		}
		List<Hero> val2 = Enumerable.ToList<Hero>(Enumerable.Where<Hero>((System.Collections.Generic.IEnumerable<Hero>)a.ExSpouses, (Func<Hero, bool>)((Hero h) => h != null && h.IsAlive)));
		if (a.Spouse != null && a.Spouse.IsAlive && !val2.Contains(a.Spouse))
		{
			val2.Add(a.Spouse);
		}
		List<Hero> val3 = Enumerable.ToList<Hero>(Enumerable.Where<Hero>((System.Collections.Generic.IEnumerable<Hero>)b.ExSpouses, (Func<Hero, bool>)((Hero h) => h != null && h.IsAlive)));
		if (b.Spouse != null && b.Spouse.IsAlive && !val3.Contains(b.Spouse))
		{
			val3.Add(b.Spouse);
		}
		List<Hero> obj = ((val2.Count >= val3.Count) ? val2 : val3);
		Hero val4 = ((val2.Count >= val3.Count) ? b : a);
		return obj.IndexOf(val4) > 0;
	}

	internal float GetOrCapturePlayerAge(Hero hero, float currentAge)
	{
		if (hero == null || string.IsNullOrEmpty(((MBObjectBase)hero).StringId))
		{
			return currentAge;
		}
		string id = ((MBObjectBase)hero).StringId;
		int index = _frozenPlayerAgeIds.IndexOf(id);
		if (index >= 0 && index < _frozenPlayerAges.Count)
		{
			double frozen = _frozenPlayerAges[index];
			if (!double.IsNaN(frozen) && !double.IsInfinity(frozen) && frozen >= 18.0)
			{
				return (float)frozen;
			}
		}
		if (float.IsNaN(currentAge) || float.IsInfinity(currentAge) || currentAge < 18f)
		{
			return currentAge;
		}
		if (index < 0)
		{
			_frozenPlayerAgeIds.Add(id);
			_frozenPlayerAges.Add(currentAge);
			RuntimeProof.Write("Coop player age frozen at current age: hero=" + id + "; age=" + currentAge);
		}
		return currentAge;
	}

	internal static bool IsRecordedPlayer(Hero h)
	{
		if (h == null)
		{
			return false;
		}
		if (h == Hero.MainHero || CoopCompatibility.IsPlayerHero(h))
		{
			return true;
		}
		MarriageFamilyBehavior current = Current;
		if (current == null)
		{
			return false;
		}
		current.TrimPartners();
		return current._partnerPlayers.Contains(((MBObjectBase)h).StringId);
	}

	internal static Hero ResolveRecordedPlayer(Hero a, Hero b)
	{
		// Current Coop ownership wins over historical records that older builds
		// always stored male-first, including an NPC husband of a female player.
		if (a != null && (a == Hero.MainHero || CoopCompatibility.IsPlayerHero(a))) return a;
		if (b != null && (b == Hero.MainHero || CoopCompatibility.IsPlayerHero(b))) return b;
		if (IsRecordedPlayer(a))
		{
			return a;
		}
		if (IsRecordedPlayer(b))
		{
			return b;
		}
		return null;
	}

	internal static bool CanTakeAdditionalPartner(Hero player, Hero target)
	{
		if (player == null || !player.IsAlive || !IsRecordedPlayer(player))
		{
			return false;
		}
		int num = PartnerCount(player);
		if (num < 1 || num >= 3)
		{
			return false;
		}
		if (target == null)
		{
			return true;
		}
		if (target == player || !target.IsAlive)
		{
			return false;
		}
		if (target.Spouse != null && target.Spouse != player)
		{
			return false;
		}
		return !IsSecondaryPartner(player, target);
	}

	private static Hero Find(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return Enumerable.FirstOrDefault<Hero>((System.Collections.Generic.IEnumerable<Hero>)Hero.AllAliveHeroes, (Func<Hero, bool>)((Hero h) => ((MBObjectBase)h).StringId == id));
		}
		return null;
	}

	private void TrimPregnancies()
	{
		int num = Math.Min(_mothers.Count, Math.Min(_fathers.Count, _dueDays.Count));
		if (_mothers.Count > num)
		{
			_mothers.RemoveRange(num, _mothers.Count - num);
		}
		if (_fathers.Count > num)
		{
			_fathers.RemoveRange(num, _fathers.Count - num);
		}
		if (_dueDays.Count > num)
		{
			_dueDays.RemoveRange(num, _dueDays.Count - num);
		}
	}

	private void TrimPartners()
	{
		int num = Math.Min(_partnerPlayers.Count, _partnerSpouses.Count);
		if (_partnerPlayers.Count > num)
		{
			_partnerPlayers.RemoveRange(num, _partnerPlayers.Count - num);
		}
		if (_partnerSpouses.Count > num)
		{
			_partnerSpouses.RemoveRange(num, _partnerSpouses.Count - num);
		}
	}

	private void RemovePregnancy(int i)
	{
		_mothers.RemoveAt(i);
		_fathers.RemoveAt(i);
		_dueDays.RemoveAt(i);
	}

	private bool IsOnPregnancyCooldown(Hero mother)
	{
		TrimCooldowns();
		string id = ((MBObjectBase)mother).StringId;
		for (int i = _cooldownMothers.Count - 1; i >= 0; i--)
		{
			if (_cooldownUntilDays[i] <= CampaignTime.Now.ToDays)
			{
				_cooldownMothers.RemoveAt(i);
				_cooldownUntilDays.RemoveAt(i);
				continue;
			}
			if (_cooldownMothers[i] == id)
			{
				return true;
			}
		}
		return false;
	}

	private void SetPregnancyCooldown(Hero mother)
	{
		TrimCooldowns();
		string id = ((MBObjectBase)mother).StringId;
		double until = CampaignTime.DaysFromNow(PregnancyCooldownDays).ToDays;
		int index = _cooldownMothers.IndexOf(id);
		if (index >= 0)
		{
			_cooldownUntilDays[index] = until;
		}
		else
		{
			_cooldownMothers.Add(id);
			_cooldownUntilDays.Add(until);
		}
		RuntimeProof.Write("Pregnancy cooldown started: mother=" + id + "; days=" + PregnancyCooldownDays);
	}

	private void TrimCooldowns()
	{
		int count = Math.Min(_cooldownMothers.Count, _cooldownUntilDays.Count);
		if (_cooldownMothers.Count > count)
		{
			_cooldownMothers.RemoveRange(count, _cooldownMothers.Count - count);
		}
		if (_cooldownUntilDays.Count > count)
		{
			_cooldownUntilDays.RemoveRange(count, _cooldownUntilDays.Count - count);
		}
	}
	private void TrimAcceleratedChildren()
	{
		int count = Math.Min(_acceleratedChildren.Count, _childMaturityDays.Count);
		if (_acceleratedChildren.Count > count)
		{
			_acceleratedChildren.RemoveRange(count, _acceleratedChildren.Count - count);
		}
		if (_childMaturityDays.Count > count)
		{
			_childMaturityDays.RemoveRange(count, _childMaturityDays.Count - count);
		}
	}
	private static void Notify(TextObject x)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		if (x != (TextObject)null)
		{
			InformationManager.DisplayMessage(new InformationMessage("[COOP Family] " + (object)x));
		}
	}
}



