namespace CoodszHost.Services;

/// <summary>
/// The on and off switches of server-config.json on the Settings page. A fresh setup has no server-config.json until
/// the server's first start, so the page used to show Steam and Write server log unticked, and a later Save settings
/// wrote "steam": false and "logFile": false the host never chose (Coop #60, CR-F86VX).
/// </summary>
public static class ServerConfigFlags
{
    /// <summary>What a box shows when server-config.json is not there yet: Coop's own defaults. Automatic restart starts
    /// off until Settings were saved once (HostSettings.AutoRestartSaved).</summary>
    public static readonly IReadOnlyDictionary<string, bool> Defaults = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["steam"] = true,
        ["logFile"] = true,
        ["autoRestart"] = false,
        ["allowClientTimeControls"] = true,
    };

    /// <summary>
    /// The value Save settings writes for a box. The page's value when it loaded that box from the file, or when the
    /// host changed it from its default. A box the page never loaded and the host left alone keeps the value the file
    /// has now (re-read at save time), and the box's value only when the file has no such key. Automatic restart always
    /// takes the box: an old file's "autoRestart": true was never the host's choice.
    /// </summary>
    public static bool ToWrite(string key, bool box, bool loadedFromFile, string fileJson)
    {
        if (loadedFromFile || key == "autoRestart" || !Defaults.TryGetValue(key, out var fallback) || box != fallback)
            return box;
        return JsoncEditor.GetRaw(fileJson, key) switch
        {
            "true" => true,
            "false" => false,
            _ => box
        };
    }

    /// <summary>
    /// What every Start writes into server-config.json from Settings, Join: the password the host set (an empty one
    /// clears the old password; null means the host never set one here, so the file keeps its own) and the Steam switch
    /// (ToWrite). Before, an empty password was never written and Steam only on Save settings, so a cleared password
    /// and Steam turned off stayed "123" and true in the file (v1.3.6 live test on a manual Coop 0.1.7 server).
    /// </summary>
    public static string ApplyAtStart(string json, string? password, bool steamBox, bool loadedFromFile)
    {
        if (password != null)
            json = JsoncEditor.SetString(json, "password", Normal(password));
        return JsoncEditor.SetBool(json, "steam", ToWrite("steam", steamBox, loadedFromFile, json));
    }

    /// <summary>True when ApplyAtStart replaces a password the file already has with another one, so Start says so.</summary>
    public static bool ReplacesPassword(string json, string? password)
    {
        if (password == null)
            return false;
        var current = JsoncEditor.GetString(json, "password");
        return current.Length > 0 && !current.Equals(Normal(password), StringComparison.Ordinal);
    }

    public const string PasswordReplacedLine = "Start changed the server password to the one on Settings, under Join.";

    public const string PasswordClearedLine = "Start cleared the server password because the password box on Settings, under Join, is empty.";

    /// <summary>The line Start logs when it changes the file's password (ReplacesPassword): cleared, or changed. Null when it doesn't.</summary>
    public static string? PasswordChangeLine(string json, string? password) =>
        !ReplacesPassword(json, password) ? null : Normal(password!).Length == 0 ? PasswordClearedLine : PasswordReplacedLine;

    /// <summary>A password as Start and Save settings write it: only spaces counts as cleared.</summary>
    public static string NormalPassword(string password) => Normal(password);

    /// <summary>
    /// The password a profile keeps after Settings, Join was shown: null (the file's own password stays) unless the host
    /// really changed the box. A box that still shows the file's password, set when the page loaded, keeps null; a box the
    /// host cleared keeps "" (Start then clears the password). A password of only spaces counts as cleared.
    /// </summary>
    public static string? PasswordToKeep(string? current, string box, string filePassword) =>
        current == null && Normal(box) == filePassword ? null : Normal(box);

    private static string Normal(string password) => string.IsNullOrWhiteSpace(password) ? "" : password;
}
