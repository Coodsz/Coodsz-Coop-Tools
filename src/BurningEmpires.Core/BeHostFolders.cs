namespace BurningEmpires.Core;

/// <summary>
/// Where the Burning Empires server tool keeps its server folder. The server folder is a
/// whitelist copy of the Workshop Coop dedicated server (BeServerPrepare) with the Burning
/// Empires core staged in (BeServerPath).
///
/// The server starts through the server folder's own Start-ModdedServer.ps1, the way the Burning
/// Empires live tests started it. That script always uses
/// Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer as its data folder
/// (server-config.json, Game Saves, logs) and CoopData\mod-config.json, so the Data folder is
/// that one. Coodsz Host and Coop's own server use the same folder. The live test confirms the
/// server really reads and writes there.
/// </summary>
public static class BeHostFolders
{
    public const string HostFolderName = "BurningEmpires-Coop-Host";
    public const string ServerFolderName = "Server";
    public const string DataFolderName = "Data";

    public static string DefaultServerRoot(string driveRoot) =>
        Path.Combine(driveRoot, HostFolderName, ServerFolderName);

    /// <summary>The Data folder a later start route could use. Not used by the script route.</summary>
    public static string DefaultDataRoot(string driveRoot) =>
        Path.Combine(driveRoot, HostFolderName, DataFolderName);

    /// <summary>CoopData in Documents, where Start-ModdedServer.ps1 keeps mod-config.json.</summary>
    public static string ScriptCoopDataRoot(string? documentsRoot = null) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(documentsRoot) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : documentsRoot,
            "Mount and Blade II Bannerlord", "CoopData");

    /// <summary>The data folder Start-ModdedServer.ps1 always uses (server-config.json, Game Saves, logs).</summary>
    public static string ScriptDataRoot(string? documentsRoot = null) =>
        Path.Combine(ScriptCoopDataRoot(documentsRoot), "DedicatedServer");

    public static string GameSaves(string dataRoot) => Path.Combine(dataRoot, "Game Saves");

    public static string Logs(string dataRoot) => Path.Combine(dataRoot, "logs");

    public static string ServerConfig(string dataRoot) => Path.Combine(dataRoot, "server-config.json");

    public static string ModConfig(string coopDataRoot) => Path.Combine(coopDataRoot, "mod-config.json");
}
