using System.Text.Json;

namespace BannerlordCoopLauncher.Core.Realm;

// The Steam accounts whose Realm reward code was checked in Coop Server Tool on this PC, so a second claim
// shows up by Steam account. That matters from the key change on: the same account then has an old key code
// and a new one, and the two codes look different. Kept in %AppData%\Coodsz\realm-claims.json.
public static class RealmClaims
{
    public const string FileName = "realm-claims.json";

    // Tests point this at a temp folder.
    public static string Folder { get; set; } = BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot;

    public static string FilePath => Path.Combine(Folder, FileName);

    public sealed record Claim(ulong SteamId64, DateTimeOffset FirstChecked, string Code);

    // Notes the check and returns the first earlier check of this Steam account, or null when this is the
    // first one. A check of the same account is noted once; later checks keep the first date and code.
    public static Claim? Record(ulong steamId64, string code, DateTimeOffset now)
    {
        var claims = Load();
        var earlier = claims.FirstOrDefault(claim => claim.SteamId64 == steamId64);
        if (earlier != null)
            return earlier;
        claims.Add(new Claim(steamId64, now, Normalize(code)));
        Save(claims);
        return null;
    }

    public static List<Claim> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<Claim>();
            return JsonSerializer.Deserialize<List<Claim>>(File.ReadAllText(FilePath)) ?? new List<Claim>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken file is kept aside instead of overwritten, so no earlier claim is lost for good.
            TryKeepAside();
            return new List<Claim>();
        }
    }

    private static void Save(List<Claim> claims)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(claims, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The check itself still shows; only the note for next time is lost.
        }
    }

    private static void TryKeepAside()
    {
        try
        {
            File.Move(FilePath, Path.Combine(Folder, "realm-claims.broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Normalize(string code) =>
        new string(code.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
}
