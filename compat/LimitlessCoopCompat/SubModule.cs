using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LimitlessCoopCompat
{
    // Makes Limitless safe in Bannerlord Coop (built against Limitless 1.0.1 and Coop 0.1.6).
    //
    // How Coop runs a battle: every player's PC fields only its own troops and shows the other
    // players' troops as puppets driven over the network, with at most 2000 agents per battle.
    // - Coop finds agents by index (Mission.FindAgentWithIndex) to route hits between players and
    //   to apply puppet deaths. Limitless answers that lookup from a cache that is only rebuilt when
    //   the mission time or the agent counts change, so a puppet removed and another spawned in the
    //   same tick can give the wrong agent or none.
    // - Coop's 2000 budget counts every agent the mission still holds, corpses included. Vanilla
    //   fades old corpses, Limitless keeps them, so a long battle would fill the budget and that PC
    //   would stop showing the other players' troops.
    //
    // So, only while a co-op session is running:
    // - Limitless's cached agent lookup is skipped, so the game's own exact lookup runs.
    // - On a PC where Limitless raised the engine's agent slots, Coop's budget counts living
    //   agents, still capped by the slots that are really left.
    // - Limitless's bigger battle budget and riderless horse cleanup are off. Coop builds its
    //   battles without the hooks they use, so this is a guard for any battle that does use them.
    // - Big battles, which are the point of Limitless: Coop caps a battle at 1000 troops on the
    //   server and 2000 agents on each PC. With Limitless in the mod list the server uses the host's
    //   own battleSize above 1000 (up to MaxBattleSize), and each PC's agent cap becomes the engine
    //   slots Limitless really gave that PC. Coop's join check already makes every player load
    //   Limitless, and each PC only renders what fits its own slots.
    // Everything else in Limitless stays on. Single player is untouched. This module also loads on
    // the dedicated server for the battle size; Limitless itself never runs there.
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "coodsz.limitless.coopcompat";
        private static bool _patched;

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            TryPatch();
        }

        // The dedicated server may never show the main menu, so also try when a game starts.
        protected override void OnGameStart(TaleWorlds.Core.Game game, TaleWorlds.Core.IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            TryPatch();
        }

        private static void TryPatch()
        {
            if (_patched)
                return;
            _patched = true;

            try
            {
                Coop.ModulesLoaded();
                var count = Patches.Apply(new Harmony(HarmonyId));
                Log("Limitless coop patch ready (" + count + " of " + Patches.HookCount + " hooks).");
            }
            catch (Exception ex)
            {
                Log("Limitless coop patch could not start: " + ex.Message);
            }
        }

        internal static void Log(string text)
        {
            try { Debug.Print("[Limitless.CoopCompat] " + text); }
            catch { }
        }
    }

    // Reads Coop's own state: GameInterface.ContainerProvider.TryGetContainer is true while a co-op
    // session runs (host or join) and false when it ends. It is public in Coop 0.1.6 and later.
    // If a future Coop removes it, the patch assumes a running session, so co-op stays safe.
    internal static class Coop
    {
        private static bool _resolved;
        private static bool _modulesLoaded;
        private static bool _coopLoaded;
        private static MethodInfo _tryGetContainer;

        public static bool InSession
        {
            get
            {
                Resolve();
                if (!_coopLoaded)
                    return false;
                if (_tryGetContainer == null)
                    return true;
                try { return (bool)_tryGetContainer.Invoke(null, new object[] { null }); }
                catch { return true; }
            }
        }

        // Every module's DLLs are loaded by now; without Coop the answer stays "no session".
        public static void ModulesLoaded()
        {
            _modulesLoaded = true;
            Resolve();
        }

        private static void Resolve()
        {
            if (_resolved)
                return;

            Type container = null;
            var coop = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var name = assembly.GetName().Name ?? "";
                    if (name == "GameInterface")
                    {
                        coop = true;
                        container = container ?? assembly.GetType("GameInterface.ContainerProvider", false);
                    }
                    else if (name == "Coop.Core" || (name == "Coop" && assembly.GetType("Coop.CoopMod", false) != null))
                    {
                        coop = true;
                    }
                }
                catch { }
            }

            if (!coop)
            {
                // Before the main menu Coop may simply not be loaded yet; look again later.
                if (_modulesLoaded)
                    _resolved = true;
                return;
            }

            _coopLoaded = true;
            _tryGetContainer = container == null ? null : AccessTools.Method(container, "TryGetContainer");
            if (_tryGetContainer == null)
                SubModule.Log("Could not read Coop's session state; assuming a session is running.");
            _resolved = true;
        }
    }

    internal static class Patches
    {
        public const int HookCount = 6;

        // Coop's own fixed budget, read from BattleAgentBudget.MaxRenderedAgents when it can be.
        private const int CoopAgentLimit = 2000;
        // Vanilla's engine agent slots. Limitless raises DefaultBattleMissionAgentSpawnLogic.MaxNumberOfAgentsForMission above this.
        private const int VanillaAgentSlots = 2048;
        // Slots kept free for missiles, mounts spawned with a rider and anything else that adds an agent outside Coop's count.
        private const int SlotMargin = 64;
        // Coop's own range is 200 to 1000. With Limitless the host may go up to this.
        private const int MinBattleSize = 200;
        private const int MaxBattleSize = 12000;

        public static int Apply(Harmony harmony)
        {
            var count = 0;

            var budget = AccessTools.TypeByName("GameInterface.Services.MapEvents.BattleAgentBudget");
            count += Patch(harmony, budget, "CountLiveAgents", "Coop", postfix: nameof(CountLiveAgentsPostfix));
            count += Patch(harmony, budget, "get_MaxRenderedAgents", "Coop", postfix: nameof(MaxRenderedAgentsPostfix));
            count += Patch(harmony, AccessTools.TypeByName("Missions.Battles.BattleSizeProvider"), "GetBattleSize", "Coop", postfix: nameof(BattleSizePostfix));

            var limitless = FindLimitless();
            if (limitless == null)
            {
                SubModule.Log("Limitless is not loaded; its hooks are left alone.");
                return count;
            }

            count += Patch(harmony, limitless.GetType("Limitless.AgentQueryPatches", false), "FindAgentWithIndexAuxPrefix", "Limitless", prefix: nameof(SkipLimitlessPrefixInCoop));
            count += Patch(harmony, limitless.GetType("Limitless.CampaignSpawnPatches", false), "RegisterBudget", "Limitless", prefix: nameof(SkipInCoop));
            count += Patch(harmony, limitless.GetType("Limitless.HorseCleanupLogic", false), "OnMissionTick", "Limitless", prefix: nameof(SkipInCoop));
            return count;
        }

        private static Assembly FindLimitless()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name == "Limitless" && assembly.GetType("Limitless.CampaignSpawnPatches", false) != null)
                        return assembly;
                }
                catch { }
            }
            return null;
        }

        private static int Patch(Harmony harmony, Type type, string method, string owner, string prefix = null, string postfix = null)
        {
            var original = type == null ? null : AccessTools.DeclaredMethod(type, method);
            if (original == null)
            {
                SubModule.Log(owner + " " + (type?.Name ?? "type") + "." + method + " not found; that part is left as it is.");
                return 0;
            }
            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
            return 1;
        }

        // Limitless's prefix returns false to skip the game's lookup. Answering true for it lets the
        // game's own FindAgentWithIndexAux run.
        private static bool SkipLimitlessPrefixInCoop(ref bool __result)
        {
            if (!Coop.InSession)
                return true;
            __result = true;
            return false;
        }

        private static bool SkipInCoop() => !Coop.InSession;

        // Only on a PC where Limitless raised the engine slots, so corpses have room of their own.
        // Remaining capacity becomes the smaller of Coop's budget for living agents and the engine slots really left.
        private static void CountLiveAgentsPostfix(object __instance, Mission mission, ref int __result)
        {
            try
            {
                if (mission?.Agents == null || mission.AllAgents == null || !Coop.InSession)
                    return;
                int engineSlots = DefaultBattleMissionAgentSpawnLogic.MaxNumberOfAgentsForMission;
                if (engineSlots <= VanillaAgentSlots)
                    return;
                int limit = CoopLimit(__instance);
                int slotsLeft = engineSlots - mission.AllAgents.Count;
                __result = Math.Max(mission.Agents.Count, limit - slotsLeft);
            }
            catch { }
        }

        // Each PC caps at the slots Limitless really gave it, so a PC is never pushed past its own engine limit.
        private static void MaxRenderedAgentsPostfix(ref int __result)
        {
            try
            {
                if (!Coop.InSession)
                    return;
                int engineSlots = DefaultBattleMissionAgentSpawnLogic.MaxNumberOfAgentsForMission;
                if (engineSlots > VanillaAgentSlots)
                    __result = Math.Max(__result, engineSlots - SlotMargin);
            }
            catch { }
        }

        // Runs where battles are sized (the server). Coop clamps the host's battleSize to 1000; with Limitless in
        // the mod list every player has the slots for more, so only the wider range applies. Coop reads
        // mod-config.json once at start, so the value is read again from the file whenever it changes: a host who
        // lowers the size after a laggy fight gets it on the next battle without restarting.
        private static void BattleSizePostfix(object __instance, ref int __result)
        {
            try
            {
                if (!LimitlessInModList())
                    return;
                var modConfig = Traverse.Create(__instance).Field("modConfig");
                var configured = LiveBattleSize(modConfig.GetValue())
                    ?? modConfig.Property("Data").Property("ModOptions").Property("BattleSize").GetValue<int?>();
                if (configured.HasValue)
                    __result = Math.Max(MinBattleSize, Math.Min(MaxBattleSize, configured.Value));
            }
            catch { }
        }

        private static readonly Regex BattleSizeLine = new Regex("^\\s*\"battleSize\"\\s*:\\s*(\\d+)", RegexOptions.Multiline);
        private static string _configPath;
        private static DateTime _configWritten;
        private static int? _liveBattleSize;

        // The battleSize line of the same mod-config.json Coop loaded (its own ResolveDirectory), or null to keep Coop's value.
        private static int? LiveBattleSize(object modConfig)
        {
            if (modConfig == null)
                return null;
            if (_configPath == null)
            {
                var dir = Traverse.Create(modConfig).Method("ResolveDirectory").GetValue<string>();
                if (string.IsNullOrEmpty(dir))
                    return null;
                _configPath = Path.Combine(dir, "mod-config.json");
            }
            if (!File.Exists(_configPath))
                return null;
            var written = File.GetLastWriteTimeUtc(_configPath);
            if (written != _configWritten)
            {
                _configWritten = written;
                var match = BattleSizeLine.Match(File.ReadAllText(_configPath));
                _liveBattleSize = match.Success && int.TryParse(match.Groups[1].Value, out var size) ? size : (int?)null;
            }
            return _liveBattleSize;
        }

        private static bool? _limitlessListed;

        private static bool LimitlessInModList()
        {
            if (_limitlessListed.HasValue)
                return _limitlessListed.Value;
            try
            {
                _limitlessListed = TaleWorlds.ModuleManager.ModuleHelper.GetActiveModules()
                    .Any(module => string.Equals(module.Id, "Limitless", StringComparison.OrdinalIgnoreCase));
            }
            catch { _limitlessListed = false; }
            return _limitlessListed.Value;
        }

        private static PropertyInfo _maxRendered;

        private static int CoopLimit(object budget)
        {
            try
            {
                _maxRendered = _maxRendered ?? AccessTools.Property(budget.GetType(), "MaxRenderedAgents");
                if (_maxRendered?.GetValue(budget, null) is int limit && limit > 0)
                    return limit;
            }
            catch { }
            return CoopAgentLimit;
        }
    }
}
