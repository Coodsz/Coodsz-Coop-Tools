using System.IO;
using System.Text.Json;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace CoodszHost.Services;

public sealed class HostSettings
{
    public string GamePath { get; set; } = "";
    public string ServerExecutable { get; set; } = "";
    public string AccessMode { get; set; } = "Banlist";
    public string ActiveProfile { get; set; } = "Default";
    // SHA-256 of the EULA.md and LICENSE.txt the operator accepted (Coop 0.1.6+).
    public string AcceptedEulaSha256 { get; set; } = "";
    // Open the server's UDP port on the router (UPnP or NAT-PMP) while the server runs.
    // On by default: it only maps the server port, with a lease, and removes it on stop.
    public bool OpenRouterPort { get; set; } = true;
    // Steam lobby was turned on once for a shared address (CGNAT); after that the host's own choice stays.
    public bool SteamLobbyAutoOn { get; set; }
    // Streamer mode (Coodsz Host): IPs, invite codes, Steam ids and the join password show as dots on screen.
    public bool StreamerMode { get; set; }
    // Automatic restart runs only after the host saved the Settings page once: older server-config.json files say
    // autoRestart true every 3 hours from a template, back when nothing acted on it.
    public bool AutoRestartSaved { get; set; }
    public List<HostBroadcast> Broadcasts { get; set; } = new();
    // The favourite cheats of the Cheats page (cheat ids) and the newest commands sent from the Console page.
    public List<string> FavouriteCheats { get; set; } = new();
    public List<string> RecentCommands { get; set; } = new();
    public List<HostProfile> Profiles { get; set; } = new();

    public HostProfile Current()
    {
        var profile = Profiles.FirstOrDefault(item => item.Name.Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase));
        if (profile != null)
            return profile;
        profile = Profiles.FirstOrDefault() ?? new HostProfile { Name = "Default" };
        if (Profiles.Count == 0)
            Profiles.Add(profile);
        ActiveProfile = profile.Name;
        return profile;
    }

    public static HostSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(path)) ?? new HostSettings();
                if (settings.Profiles.Count == 0)
                    settings.Profiles.Add(new HostProfile { Name = "Default" });
                StableCoop.Ensure(settings);
                return settings;
            }
        }
        catch (JsonException)
        {
        }

        var created = new HostSettings { Profiles = { new HostProfile { Name = "Default" } } };
        StableCoop.Ensure(created);
        if (Edition.IsAD1259)
            created.ActiveProfile = AD1259Server.ProfileName;
        return created;
    }

    public static void Save(string path, HostSettings settings) =>
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class HostProfile
{
    public string Name { get; set; } = "Default";
    public string SaveName { get; set; } = "";
    public bool NewCampaign { get; set; }
    public int EnginePort { get; set; } = 7210;
    public int CoopPort { get; set; } = 4200;
    public string Region { get; set; } = "EU";
    public string SteamLobbyName { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public string CampaignMap { get; set; } = "";
    public string? Password { get; set; }
    public int AutosaveMinutes { get; set; }
    public string McmFolder { get; set; } = "";
    // Mods (module ids) whose settings players keep for themselves. Every other mod is locked to the
    // host: players who join with the Coodsz client add on use the host's MCM settings for the session.
    // McmKeepOwn writes it as ModderLords' recipes.json PlayersOwnSettings for the server module.
    public List<string> PlayersOwnSettings { get; set; } = new();
    public bool Locked { get; set; }
    public List<string> ModuleOrder { get; set; } = new();
    public List<string> DisabledModules { get; set; } = new();
    // RBM's parts for this setup. Null until the host saves them; Start then uses
    // the defaults (everything on except Campaign).
    public RbmSettings? Rbm { get; set; }
    // Start the server again when it crashes (off until the host turns it on).
    public bool RestartOnCrash { get; set; }
}

public static class StableCoop
{
    // Same names as Coop and Friends, so a player picks the setup the host runs.
    public const string LegacyName = StableClientSetup.LegacyName;
    public const string VanillaName = StableClientSetup.VanillaName;
    public const string MoreNationsName = StableClientSetup.MoreNationsName;
    public const string VanillaMap = "Vanilla";
    public const string MoreNationsMap = "MoreNationsRemastered";
    public const string PlainName = "Plain Coop (no mods)";

    public static readonly string[] Order = Edition.IsAD1259 ? AD1259Stack.ServerModules : BuildOrder();

    // Plain Coop: only the server's own modules and Coop, on the vanilla map. That is the list the
    // Workshop server starts with, so players join it with Coop alone. Harmony, ButterLib, UI
    // Extender and MCM stay off too, since Coop counts them as mods players must load.
    public static readonly string[] PlainOrder = ["Native", "DedicatedServer.Windows", "SandBoxCore", "SandBox", "Coop"];

    public static bool IsPlain(HostProfile profile) =>
        !Edition.IsAD1259 && profile.Name.Equals(PlainName, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> OrderFor(HostProfile profile) => IsPlain(profile) ? PlainOrder : Order;

    // A module the host always loads (the libraries) unless the setup is Plain Coop, which loads
    // only its own list.
    public static bool KeepsRequired(HostProfile profile, string moduleId) =>
        !IsPlain(profile) || PlainOrder.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] Disabled =
    [
        "CoopNightly",
        "FamilyAppearanceEditor",
        "CoopMarriage",
        "CoopSycn"
    ];

    public static IEnumerable<string> DisabledFor(HostProfile profile)
    {
        if (Edition.IsAD1259)
            yield break;
        if (IsPlain(profile))
        {
            foreach (var id in Order.Concat(Disabled).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!PlainOrder.Contains(id, StringComparer.OrdinalIgnoreCase) && !ServerModuleNotes.IsLibrary(id))
                    yield return id;
            }
            yield break;
        }
        foreach (var id in Disabled)
            yield return id;
        if (!string.Equals(profile.CampaignMap, MoreNationsMap, StringComparison.OrdinalIgnoreCase))
        {
            yield return "MoreNationsRemastered";
            yield return "MoreNationsRemasteredFix";
        }
    }

    public static bool AlreadyPresent(string wantedId, IEnumerable<string> installedIds)
    {
        var installed = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);
        if (installed.Contains(wantedId))
            return true;
        return IsBannerlordCoop(wantedId) && (installed.Contains("Coop") || installed.Contains("CoopNightly"));
    }

    public static bool ShouldEnable(HostProfile profile, string moduleId, IEnumerable<string> installedIds)
    {
        var installed = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);
        if (moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)
            && installed.Contains("CoopNightly")
            && !installed.Contains("Coop"))
            return true;
        if (IsPlain(profile))
            return PlainOrder.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
        if (DisabledFor(profile).Contains(moduleId, StringComparer.OrdinalIgnoreCase))
            return false;
        return Order.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }

    // An unlocked setup turns on only the mods in its order that it does not turn off. A mod
    // installed on the server later (RBM, armour packs) stays off until the host turns it on
    // and saves. An old setup that never saved an order keeps every mod it does not turn off.
    public static bool UnlockedEnables(HostProfile profile, string moduleId)
    {
        if (profile.DisabledModules.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
            return false;
        return profile.ModuleOrder.Count == 0 || profile.ModuleOrder.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBannerlordCoop(string moduleId) =>
        moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
        || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);

    private static string[] BuildOrder()
    {
        var order = ServerModuleSet.WorkingOrder(includeMoreNations: true).ToList();
        var native = order.FindIndex(id => id.Equals("Native", StringComparison.OrdinalIgnoreCase));
        order.Insert(native + 1, "DedicatedServer.Windows");
        var coop = order.FindIndex(id => id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
        order.Insert(coop + 1, "CoopNightly");
        return order.ToArray();
    }

    public static HostProfile CreateFresh(string name)
    {
        var profile = new HostProfile
        {
            Name = name,
            // The AD1259 set runs on its own map only.
            CampaignMap = Edition.IsAD1259 ? AD1259Server.CampaignMap : VanillaMap,
            ModuleOrder = Order.ToList(),
            McmFolder = Guid.NewGuid().ToString("N")
        };
        profile.DisabledModules = DisabledFor(profile).ToList();
        return profile;
    }

    public static string NextProfileName(IEnumerable<string> existing)
    {
        var names = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var number = 2;
        while (names.Contains("Profile " + number))
            number++;
        return "Profile " + number;
    }

    public static void Ensure(HostSettings settings)
    {
        if (Edition.IsAD1259)
        {
            // The AD1259 edition has one locked setup: the tested AD1259 set on its own map.
            PlaceAfterDefault(settings, LockProfile(settings, AD1259Server.ProfileName, AD1259Server.CampaignMap));
            return;
        }

        foreach (var (from, to) in StableClientSetup.Renames)
            RenameProfile(settings, from, to);

        var vanilla = LockProfile(settings, VanillaName, VanillaMap);
        var nations = LockProfile(settings, MoreNationsName, MoreNationsMap);
        var plain = LockProfile(settings, PlainName, VanillaMap);
        PlaceAfterDefault(settings, vanilla, nations, plain);
    }

    private static void RenameProfile(HostSettings settings, string from, string to)
    {
        var source = settings.Profiles.FirstOrDefault(item => item.Name.Equals(from, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return;
        var target = settings.Profiles.FirstOrDefault(item => item.Name.Equals(to, StringComparison.OrdinalIgnoreCase));
        if (target != null && !ReferenceEquals(source, target))
        {
            if (settings.ActiveProfile.Equals(source.Name, StringComparison.OrdinalIgnoreCase))
                settings.ActiveProfile = to;
            settings.Profiles.Remove(source);
            return;
        }

        if (settings.ActiveProfile.Equals(source.Name, StringComparison.OrdinalIgnoreCase))
            settings.ActiveProfile = to;
        source.Name = to;
    }

    private static HostProfile LockProfile(HostSettings settings, string name, string campaignMap)
    {
        var profile = settings.Profiles.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = new HostProfile
            {
                Name = name,
                McmFolder = Guid.NewGuid().ToString("N")
            };
            settings.Profiles.Add(profile);
        }

        profile.Name = name;
        profile.Locked = true;
        profile.CampaignMap = campaignMap;
        profile.ModuleOrder = OrderFor(profile).ToList();
        profile.DisabledModules = DisabledFor(profile).ToList();
        return profile;
    }

    private static void PlaceAfterDefault(HostSettings settings, params HostProfile[] locked)
    {
        foreach (var profile in locked)
            settings.Profiles.Remove(profile);
        var index = settings.Profiles.FindIndex(item => item.Name.Equals("Default", StringComparison.OrdinalIgnoreCase));
        var insertAt = index >= 0 ? index + 1 : 0;
        foreach (var profile in locked)
            settings.Profiles.Insert(insertAt++, profile);
    }
}

public sealed class HostBroadcast
{
    public int EveryMinutes { get; set; }
    public string Message { get; set; } = "";
}
