namespace BurningEmpires.Core;

/// <summary>One BUTR framework module, with the version the Burning Empires Coop set was tested with.
/// WorkshopId is "0" for one that comes from Nexus Mods (NexusId) into the game's Modules folder.</summary>
public sealed record ButrModule(string ModuleId, string Name, string WorkshopId, string TestedVersion, int NexusId = 0)
{
    public bool FromNexus => NexusId > 0;

    public string NexusPage => "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + NexusId;
}

/// <summary>
/// The locked Burning Empires Coop set. The core is the BUTR frameworks, the game modules, Coop,
/// Burning Empires Coop Compat, Burning Empires and Burning Empires Elephants, in the order the
/// dedicated server proved it loads (it does not sort modules, and a run with Burning Empires first
/// failed). Elephants is part of the core: Burning Empires' own install steps pick both modules, and
/// without Elephants every battle shows "Elephant extension could not start".
/// </summary>
public static class BeStack
{
    public const string GameVersion = "v1.4.8";
    public const string BeVersionText = "2.0.2";
    public const string BeVersion = "v2.0.2";
    public const string BeModuleId = "BurningEmpires";
    public const string BeName = "Burning Empires";
    // Burning Empires is on Nexus only: the main file 2.0.1 plus the 2.0.2 hotfix.
    public const string BeNexusId = "7750";
    public const string BeNexusPage = "https://www.nexusmods.com/mountandblade2bannerlord/mods/" + BeNexusId;

    // Elephants v1.1.0 is data only, with no DLL. Burning Empires checks for v1.1.0 loaded right after it.
    public const string ElephantsModuleId = "BurningEmpires_Elephants";
    public const string ElephantsName = "Burning Empires Elephants";
    public const string ElephantsVersion = "v1.1.0";

    public const string CompatModuleId = "BurningEmpires.CoopCompat";
    public const string CompatName = "Burning Empires Coop Compat";
    public const string CompatVersion = "v1.0.0";

    // D3: the tools follow the Workshop Coop. These are the versions the set was tested with, not pins.
    public const string CoopModuleId = "Coop";
    public const string LegacyCoopModuleId = "CoopNightly";
    public const string CoopVersion = "v0.1.6";
    public const string CoopWorkshopId = "3770450698";
    public const string DirectPort = "4200";

    // No Coop version: the tools follow the Workshop Coop or a newer one unzipped into Modules (CoopVersion is only what the set was tested with).
    public const string SetText = "Bannerlord 1.4.8, Burning Empires 2.0.2, Burning Empires Elephants 1.1.0, Burning Empires Coop Compat 1.0.0, Bannerlord Coop";

    // SHA256 of BurningEmpires.dll and BurningEmpires.DistanceCacheProbe.dll in Burning Empires 2.0.2
    // (the main file with the hotfix on top). The server path and Install or repair check these too.
    public const string BeDllSha256 = "C22A980C29A16A09F4E98BD55225D588E09D66967028BEA4827EE57FC719EF3C";
    public const string DistanceCacheProbeDllSha256 = "BDAB7C118443D82BE9F81AFDFC41B4CEA57E9A7663B1E146D448A57F6E162871";
    public const string BeDllName = "BurningEmpires.dll";
    public const string DistanceCacheProbeDllName = "BurningEmpires.DistanceCacheProbe.dll";

    // The UIExtenderEx Burning Empires 2.0.2 is built against. It goes only into Burning Empires' server bin
    // folder; the UIExtenderEx module itself stays the Workshop 3.0.3.
    public const string ServerUiExtenderExVersion = "2.13.3.0";
    public const string ServerUiExtenderExDllName = "Bannerlord.UIExtenderEx.dll";
    public const string ServerUiExtenderExSha256 = "4F2782840E51391D66B7D701962CB6202969D34C0949B8EC1591964F9C479F99";
    public const long ServerUiExtenderExBytes = 269824;
    /// <summary>The bundled copy, from the repository root (the server tool ships it in ServerAddOns).</summary>
    public const string ServerUiExtenderExBundlePath = @"compat\BurningEmpiresCoopCompat\server-extra\UIExtenderEx-2.13.3\Bannerlord.UIExtenderEx.dll";

    public static readonly ButrModule Harmony = new("Bannerlord.Harmony", "Harmony", "2859188632", "v2.4.2.248");
    public static readonly ButrModule ButterLib = new("Bannerlord.ButterLib", "ButterLib", "2859232415", "v2.12.0");
    public static readonly ButrModule UIExtenderEx = new("Bannerlord.UIExtenderEx", "UIExtenderEx", "2859222409", "v3.0.3");
    // The official MCM Workshop item 2859238197 was removed; the Workshop only has re-uploads, some of them
    // old builds the dedicated server refuses. MCM comes from Nexus Mods (mod 612) into the game's Modules folder.
    public static readonly ButrModule Mcm = new("Bannerlord.MBOptionScreen", "MCM", "0", "v5.12.3", NexusId: 612);

    /// <summary>The oldest MCM the dedicated server loads (Start-ModdedServer.ps1 refuses an older one).</summary>
    public const string McmServerMinimum = "v5.12.0";

    /// <summary>The BUTR modules in load order. Burning Empires' own DependentVersion values (ButterLib
    /// 2.11.1, MCM 5.12.2) are older than these and are not a problem.</summary>
    public static readonly IReadOnlyList<ButrModule> Butr = [Harmony, ButterLib, UIExtenderEx, Mcm];

    /// <summary>The BUTR modules Steam downloads (all but MCM).</summary>
    public static readonly IReadOnlyList<ButrModule> WorkshopButr = Butr.Where(mod => !mod.FromNexus).ToList();

    public static readonly IReadOnlyDictionary<string, string> ContentVersions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BeModuleId] = BeVersion,
            [ElephantsModuleId] = ElephantsVersion,
            [CompatModuleId] = CompatVersion
        };

    public static readonly string[] ClientModules =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "SandBoxCore",
        "Sandbox",
        "StoryMode",
        "CustomBattle",
        "Coop",
        "BurningEmpires.CoopCompat",
        "BurningEmpires",
        ElephantsModuleId
    ];

    // be-ds-evidence3 rgl_last.txt: DedicatedServer.Windows right after Native, and no StoryMode or
    // CustomBattle (the server gets the CustomBattle DLL in Burning Empires' server bin instead).
    public static readonly string[] ServerModules =
    [
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen",
        "Native",
        "DedicatedServer.Windows",
        "SandBoxCore",
        "Sandbox",
        "Coop",
        "BurningEmpires.CoopCompat",
        "BurningEmpires",
        ElephantsModuleId
    ];

    /// <summary>True when Burning Empires Coop Compat loads directly before Burning Empires.</summary>
    public static bool CompatIsAdjacent(IReadOnlyList<string> modules)
    {
        var index = modules.ToList().FindIndex(id => id.Equals(CompatModuleId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < modules.Count && modules[index + 1].Equals(BeModuleId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsButr(string? moduleId) =>
        moduleId != null && Butr.Any(mod => mod.ModuleId.Equals(moduleId.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a full SHA256 matches a known hash. A known hash written as "first...last"
    /// (only its ends were recorded) matches on both ends.</summary>
    public static bool HashMatches(string? sha256, string known)
    {
        var hash = (sha256 ?? "").Trim();
        if (hash.Length != 64)
            return false;
        var parts = known.Split("...");
        if (parts.Length == 1)
            return hash.Equals(known, StringComparison.OrdinalIgnoreCase);
        return parts.Length == 2
            && hash.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)
            && hash.EndsWith(parts[1], StringComparison.OrdinalIgnoreCase);
    }
}
