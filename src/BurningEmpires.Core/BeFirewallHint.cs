namespace BurningEmpires.Core;

/// <summary>
/// The first start from a server folder makes Windows ask whether .NET Host (the server engine's
/// dotnet.exe) may use the network. The Start server card says what to click until that folder's
/// server once reached SERVING. A new server folder is a new dotnet.exe, so Windows asks again.
/// </summary>
public static class BeFirewallHint
{
    public const string Text = "Windows may ask to allow .NET Host. Click Allow so players can join.";

    public static bool Shows(string? doneForServerRoot, string? serverRoot) =>
        !string.IsNullOrWhiteSpace(serverRoot)
        && !string.Equals(Normalize(doneForServerRoot), Normalize(serverRoot), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? path) => (path ?? "").Trim().TrimEnd('\\', '/');
}
