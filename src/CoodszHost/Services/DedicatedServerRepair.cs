using System.IO;
using BannerlordCoopLauncher.Core.Launch;

namespace CoodszHost.Services;

/// <summary>
/// Steam often installs a thin DedicatedServer without Start-ModdedServer.ps1,
/// packaged 0Harmony.dll or the launcher submodules (cheats, console queue,
/// player list, bans, battle size). Coodsz Host ships those files beside the
/// exe and copies any that are missing into the active server folder on Start.
/// The work is in ServerFolderRepair, shared with the Burning Empires server tool.
/// </summary>
public static class DedicatedServerRepair
{
    public const string FolderName = ServerFolderRepair.FolderName;

    public static string? Ensure(string serverRoot, string? payloadRoot = null) =>
        ServerFolderRepair.Ensure(serverRoot, payloadRoot, Edition.Name);

    public static readonly string[] ManagedScripts = ServerFolderRepair.ManagedScripts;

    public static readonly (string Name, string Dll, string Type)[] LauncherSubModules = ServerFolderRepair.LauncherSubModules;

    public static IReadOnlyList<string> EnsureLauncherSubModules(string serverRoot) =>
        ServerFolderRepair.EnsureLauncherSubModules(serverRoot);
}
