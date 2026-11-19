using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopMarriage;

public abstract class CoopMarriageSubModuleBase : MBSubModuleBase
{
	private static bool _initialized;

	private static Game _behaviorGame;

	private Harmony _harmony;

	protected virtual bool IsDedicatedServer => false;

#if COOPFAMILY_VERIFY
    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        CoopFamilyVerification.Tick();
    }
#endif

	protected override void OnSubModuleLoad()
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		base.OnSubModuleLoad();
		if (_initialized)
		{
			RuntimeProof.Write("Duplicate submodule load ignored");
			return;
		}
		CoopCompatibility.Initialize(IsDedicatedServer);
		RuntimeProof.Write("OnSubModuleLoad starting");
		try
		{
            _harmony = new Harmony("s1mash.coopmarriage");
            _harmony.PatchAll(typeof(CoopMarriageSubModuleBase).Assembly);
			if (!IsDedicatedServer)
			{
				EncyclopediaCompatibility.Install(_harmony);
				ClanNameRefreshCompatibility.Install(_harmony);
			}
			VanillaMarriageBarterCompatibility.Install(_harmony);
            _initialized = true;
            RuntimeProof.Write("OnSubModuleLoad OK; COOP Family 1.6; paid marriage barter retry; NPC birth balance; player age freeze after the world exists; newborn skills after the world exists");
		}
		catch (System.Exception ex)
		{
            _harmony?.UnpatchAll("s1mash.coopmarriage");
            _harmony = null;
            RuntimeProof.Write("OnSubModuleLoad failed; partial patches rolled back: " + ex);
            throw;
		}
	}

	protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
	{
		base.OnGameStart(game, gameStarterObject);
		CampaignGameStarter val = (CampaignGameStarter)(object)((gameStarterObject is CampaignGameStarter) ? gameStarterObject : null);
		if (_initialized && val != null && !ReferenceEquals(_behaviorGame, game))
		{
			_behaviorGame = game;
			val.AddBehavior((CampaignBehaviorBase)(object)new MarriageFamilyBehavior());
			RuntimeProof.Write("Campaign behavior added");
		}
	}

	protected override void OnBeforeInitialModuleScreenSetAsRoot()
	{
		base.OnBeforeInitialModuleScreenSetAsRoot();
		if (!IsDedicatedServer)
		{
            RuntimeProof.Write("Initial screen ready");
		}
	}

	protected override void OnSubModuleUnloaded()
	{
		Harmony harmony = _harmony;
		if (harmony != null)
		{
			harmony.UnpatchAll("s1mash.coopmarriage");
            _harmony = null;
            _initialized = false;
            _behaviorGame = null;
		}
		base.OnSubModuleUnloaded();
	}
}

