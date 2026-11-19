using System.Globalization;
using System.Text.RegularExpressions;

namespace BurningEmpires.Core;

/// <summary>How the next start runs.</summary>
public sealed record BeStartOptions
{
    /// <summary>The campaign to host, or the name of the new campaign.</summary>
    public string SaveName { get; init; } = "";

    /// <summary>A new Burning Empires campaign under SaveName (no .sav needed).</summary>
    public bool NewCampaign { get; init; }

    /// <summary>The -BattleSize the script hands the server. Null lets Start-ModdedServer.ps1 fall back
    /// to a battleSize in server-config.json, or 1000. Start server always passes the Settings value
    /// (BeServerLaunch.StartBattleSize), so the live test's 700 from an old server-config.json is gone.</summary>
    public int? BattleSize { get; init; }

    /// <summary>Set only after the person hosting pressed I accept on the Coop server terms.</summary>
    public bool AcceptTerms { get; init; }

    /// <summary>Set only after the host agreed to add mods to this save and a backup was made.</summary>
    public bool AllowAddedModules { get; init; }

    public int EnginePort { get; init; } = 7210;
    public string Region { get; init; } = "EU";
}

public sealed record BeServerStart(
    string FileName,
    string WorkingDirectory,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Starts the Burning Empires server the way the Burning Empires live tests started the Workshop
/// Coop server: the server folder's own Start-ModdedServer.ps1 with -NoPrompt and -NewSaveName
/// (new campaign) or -SaveName (existing save), the module list in modded-server-modules.txt
/// (BeServerPath.WriteOrder) and the tool's server-module-policy.json (BeServerPrepare.WritePolicy).
/// The script sets the BANNERLORD_DS_* variables, the runtime bootstrap startup hook and the
/// managed search folders the plain engine start (DedicatedEngineLaunch) does not, makes MCM
/// headless and passes the battle size. The console comes back on the process's own output.
///
/// The script route stays because it is the one the live tests proved. The live test will
/// confirm three notes. Burning Empires with MCM does not boot on the plain DedicatedEngineLaunch
/// route (if it does, that route could take a Data folder of its own; the script always uses
/// Documents). The policy (MCM forced headless, nothing else) is enough on a clean Workshop
/// server. Fast forward and 2x work with Burning Empires (an earlier run saw Play_2x held at 1x).
/// </summary>
public static class BeServerLaunch
{
    public const string AcceptTermsVariable = "DS_ACCEPT_EULA";
    public const int MinBattleSize = 300;
    // Start-ModdedServer.ps1 allows 2,000 unless Limitless is in the list.
    public const int MaxBattleSize = 2000;

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public const string TemplateReason =
        "default_new_game is the server's new campaign template, not a campaign. Click New campaign, type a name, and click Start server. Or pick a Burning Empires save.";

    public static bool IsTemplate(string? saveName) =>
        saveName != null && Path.GetFileNameWithoutExtension(saveName.Trim()).Equals("default_new_game", StringComparison.OrdinalIgnoreCase);

    // The start script in the server folder, or null when it cannot be read (then the old A to Z rule is assumed).
    // Prepare server and every start lay Coodsz Host's own script over it (BeServerPrepare.LayOverRepair),
    // so this is normally the any letter script.
    public static string? ReadServerScript(string serverRoot)
    {
        try
        {
            return File.ReadAllText(Path.Combine(serverRoot, BeServerPrepare.ScriptName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Only the fallback now: a script that cannot be read, or the Workshop's older A to Z script left in
    // a folder the tool's own copy could not be laid over (no DedicatedServerRepair folder next to the
    // tool, or a locked file). The shared older script words ask to press Start server again, which
    // does not help in those cases, so BE says this.
    public const string NewNameReason =
        "A new campaign name can only use the English letters A to Z, numbers, spaces, hyphens and underscores, up to 64 in all, and starts with a letter or number. Coop's server refuses other letters such as Ü in a new campaign name.";

    /// <summary>Why the server folder's start script would refuse this new campaign name, or null.
    /// SaveSet.NewSaveNameProblemFor, with BE's own words for an older A to Z script.</summary>
    public static string? NewNameProblem(string name, string? scriptText)
    {
        var problem = BannerlordCoopLauncher.Core.Saves.SaveSet.NewSaveNameProblemFor(name, scriptText);
        return problem == BannerlordCoopLauncher.Core.Saves.SaveSet.OldScriptNewSaveNameRule ? NewNameReason : problem;
    }

    /// <summary>Null when the name can be a new campaign in this Game Saves folder. scriptText is the
    /// server folder's Start-ModdedServer.ps1 (ReadServerScript), whose own -NewSaveName rule decides
    /// (SaveSet.NewSaveNameProblemFor). An existing save goes through -SaveName, which has no such rule.</summary>
    public static string? NewCampaignProblem(string? name, string gameSaves, string? scriptText)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "Type a name for the new campaign.";
        if (IsTemplate(trimmed))
            return TemplateReason;
        if (NewNameProblem(trimmed, scriptText) is { } nameProblem)
            return nameProblem;
        if (Reserved.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return trimmed + " is a name Windows keeps for itself. Pick another name.";
        foreach (var extension in new[] { ".sav", ".json", ".launcher.json" })
        {
            if (File.Exists(Path.Combine(gameSaves, trimmed + extension)))
                return "A campaign named " + trimmed + " is already on the server. Pick another name, or pick that save.";
        }

        return null;
    }

    /// <summary>The start of the server folder's Start-ModdedServer.ps1 for these options. With
    /// gameSaves (the shared Data folder's Game Saves), an existing save that is not a Burning
    /// Empires save (BeSaveVersion.IsBeOwned) is refused, so the config never points at a save of
    /// Coodsz Host or Coop's own server.</summary>
    public static BeServerStart Build(string serverRoot, BeStartOptions options, string? gameSaves = null)
    {
        if (options.EnginePort is < 1 or > 65535)
            throw new InvalidOperationException("The engine port must be from 1 to 65535.");
        var region = (options.Region ?? "").Trim();
        if (region.Length is < 2 or > 8 || !region.All(char.IsAsciiLetter))
            throw new InvalidOperationException("The region must be 2 to 8 letters, such as EU.");
        var save = options.SaveName.Trim();
        if (save.Length == 0)
            throw new InvalidOperationException("Pick a Burning Empires save or make a new campaign first.");
        if (IsTemplate(save))
            throw new InvalidOperationException(TemplateReason);
        if (save.IndexOfAny(['"', '\r', '\n', '`', '$']) >= 0)
            throw new InvalidOperationException("A save name cannot have quotes, line breaks, ` or $.");
        if (options.NewCampaign && NewNameProblem(save, ReadServerScript(serverRoot)) is { } nameProblem)
            throw new InvalidOperationException(nameProblem);
        if (!options.NewCampaign && !string.IsNullOrWhiteSpace(gameSaves))
        {
            var stem = save.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? save[..^4] : save;
            var savePath = Path.Combine(gameSaves, stem + ".sav");
            if (File.Exists(savePath) && !BeSaveVersion.IsBeOwned(savePath, BeSaveData.ServerModule(serverRoot)))
                throw new InvalidOperationException(BeSaveVersion.NotBeSave(stem));
        }
        if (options.BattleSize is { } size && size is < MinBattleSize or > MaxBattleSize)
            throw new InvalidOperationException("The battle size must be from " + MinBattleSize + " to " + MaxBattleSize.ToString("N0", CultureInfo.InvariantCulture) + ".");

        var script = Path.Combine(serverRoot, BeServerPrepare.ScriptName);
        if (!File.Exists(script))
            throw new InvalidOperationException(BeServerPrepare.ScriptName + " is missing from the server folder. Click Prepare server.");

        var arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + script + "\""
            + " -NoPrompt"
            + " -Port " + options.EnginePort.ToString(CultureInfo.InvariantCulture)
            + " -Region " + region.ToUpperInvariant()
            + (options.NewCampaign ? " -NewSaveName \"" : " -SaveName \"") + save + "\"";
        if (options.BattleSize is { } battleSize)
            arguments += " -BattleSize " + battleSize.ToString(CultureInfo.InvariantCulture);
        if (options.AllowAddedModules && !options.NewCampaign)
            arguments += " -AllowAddedModules";

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.AcceptTerms)
            environment[AcceptTermsVariable] = "true";
        return new BeServerStart(PowerShell(), serverRoot, arguments, environment);
    }

    /// <summary>
    /// The battle size Start server passes as -BattleSize: the one the Settings page keeps in Coop's
    /// mod-config.json (modOptions.battleSize), held to the 300 to 1000 the box allows. The script
    /// gives -BattleSize to the server's Launcher Battle Size module (BANNERLORD_DS_BATTLE_SIZE), and
    /// without it the script read a battleSize from server-config.json instead, so the live test
    /// started with 700 while Settings said 400. Coop's own server reads modOptions.battleSize, so both
    /// now get the same number. No file, or a file without the setting, gives Coop's 1000.
    /// </summary>
    public static int StartBattleSize(string? modConfigJson) =>
        Math.Clamp(
            string.IsNullOrWhiteSpace(modConfigJson) ? TaomCoop.Core.CoopModConfig.BattleSizeDefault : TaomCoop.Core.CoopModConfig.ReadBattleSize(modConfigJson),
            MinBattleSize,
            TaomCoop.Core.CoopModConfig.BattleSizeMax);

    /// <summary>The line the server tool logs at each start.</summary>
    public static string BattleSizeLine(int size) => "Battle size set to " + size.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>
    /// server-config.json without a top level battleSize. Coop's server does not know that key (its
    /// log says "server-config.json: unknown key 'battleSize' ignored"), and the script would read it
    /// in place of the Settings value when no -BattleSize is passed. Files the first tools or the
    /// script made still have it. Comments and the other keys stay as they are.
    /// </summary>
    public static string WithoutBattleSize(string json)
    {
        var found = TaomCoop.Core.JsoncValues.Properties(json)
            .Where(property => property.Depth == 1 && property.Key.Equals("battleSize", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (found.Count == 0)
            return json;
        var text = json;
        // From the end, so the earlier places stay right.
        foreach (var property in found.OrderByDescending(property => property.Start))
            text = TaomCoop.Core.JsoncValues.RemoveProperty(text, property);
        // When it was the last key, the comma before it now stands right before the closing brace.
        return Regex.Replace(text, ",(\\s*\\}\\s*)\\z", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    public static string PowerShell() =>
        Path.Combine(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// The time the uptime counts from. found is what DedicatedServerWatch.TryStarted saw: a server
    /// process's start, or with no process yet the creation time of server-instance.lock. The script
    /// opens that file with OpenOrCreate and never rewrites it, so a lock left by an earlier run keeps
    /// that run's creation time, and live run 4 showed "Up for 8:55:45" while the new start was still
    /// booting. A start this window made (runStartedUtc) is never older than its own Start click.
    /// </summary>
    public static DateTime? UptimeFrom(DateTime? found, DateTime? runStartedUtc)
    {
        if (found == null)
            return null;
        if (runStartedUtc is { } run && run.ToLocalTime() > found.Value)
            return run.ToLocalTime();
        return found;
    }

    /// <summary>The script's own refusal lines that mean the server never started, in plain words.
    /// Null for any other line.</summary>
    public static string? ScriptRefusal(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        if (line.Contains("already exists. Choose another name", StringComparison.OrdinalIgnoreCase))
            return "A campaign with that name is already on the server. Pick another name, or pick that save.";
        if (line.Contains("fresh-campaign template is missing", StringComparison.OrdinalIgnoreCase))
            return "The new campaign template is missing from Game Saves. Click Prepare server, then start again.";
        if (line.Contains("is already in use", StringComparison.OrdinalIgnoreCase) && line.Contains("port", StringComparison.OrdinalIgnoreCase))
            return "A server is already using that port. Stop the other server first.";
        if (line.Contains("Another Bannerlord Coop server is already starting or running", StringComparison.OrdinalIgnoreCase))
            return "Another Coop server is already running from this server folder. Stop it first.";
        if (line.Contains("Modules missing from engine\\Modules", StringComparison.OrdinalIgnoreCase))
            return "A module in the list is missing from the server folder. Click Prepare server, then start again.";
        if (line.Contains("Configured battle size must be between", StringComparison.OrdinalIgnoreCase))
            return "The battle size is out of range. Set it from " + MinBattleSize + " to " + MaxBattleSize.ToString("N0", CultureInfo.InvariantCulture) + " in Settings.";
        // The script hides a save whose mods or versions do not match the server and then stops here.
        if (line.Contains("was not found or is incompatible with the selected module set", StringComparison.OrdinalIgnoreCase))
            return BeHostReport.SaveMismatchStatus;
        return null;
    }

    /// <summary>True for a line where Start-ModdedServer.ps1 refuses to start the server. That exit is
    /// no crash, so it gets no crash bar and no save fix retry (Coodsz Host's IsSaveRefusedStop).</summary>
    public static bool IsScriptRefusal(string? line) => ScriptRefusal(line) != null;
}
