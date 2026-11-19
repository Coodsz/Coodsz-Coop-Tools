// Coodsz Server Tools: the Max clans per kingdom rule, without the game, so the launcher tests
// can run it (tests\BannerlordCoopLauncher.Tests links this file).

using System;
using System.Globalization;
using System.Text.RegularExpressions;

// Its own namespace: the tests also see Coodsz Host's CoodszServerTools class.
namespace Coodsz.ServerRules;

/// <summary>
/// The clan cap Coodsz Host writes to <c>Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer\coodsz-server-tools.json</c>:
/// <c>{ "maxClansPerKingdom": 0, "countMercenaries": false }</c>. 0 (or no file) means no limit.
/// </summary>
public sealed class ClanCapSettings
{
    public const string FileName = "coodsz-server-tools.json";
    public const string MaxKey = "maxClansPerKingdom";
    public const string MercenaryKey = "countMercenaries";
    public const int Highest = 500;

    public static readonly ClanCapSettings Off = new ClanCapSettings(0, false);

    public ClanCapSettings(int maxClans, bool countMercenaries)
    {
        MaxClans = maxClans < 0 ? 0 : maxClans > Highest ? Highest : maxClans;
        CountMercenaries = countMercenaries;
    }

    /// <summary>The most clans a kingdom may have, its ruling clan included. 0 is no limit.</summary>
    public int MaxClans { get; }

    /// <summary>Whether clans serving as mercenaries count, and are capped when they are hired.</summary>
    public bool CountMercenaries { get; }

    public bool Enabled => MaxClans > 0;

    /// <summary>Whether a clan counts toward its kingdom's size.</summary>
    public bool Counts(bool mercenary) => CountMercenaries || !mercenary;

    /// <summary>
    /// Whether a join is refused: the kingdom already has <paramref name="counted"/> counted clans,
    /// the join would add one more (<paramref name="joinCounts"/>), and the clan is not counted there yet.
    /// </summary>
    public bool Refuses(int counted, bool joinCounts, bool alreadyCounted) =>
        Enabled && joinCounts && !alreadyCounted && counted >= MaxClans;

    /// <summary>
    /// Only the Realm of Thrones and TAOM Server Tools' starts set this, to their own server data folder. Coodsz Host
    /// never does, so its server keeps reading Documents.
    /// </summary>
    public const string ServerDataDirVariable = "COODSZ_SERVER_DATA_DIR";

    /// <summary>
    /// The settings file: in the folder <see cref="ServerDataDirVariable"/> names (<paramref name="serverDataDirectory"/>),
    /// else in CoopData\DedicatedServer in Documents, where Coodsz Host writes it.
    /// </summary>
    public static string PathIn(string documents, string? serverDataDirectory) =>
        string.IsNullOrWhiteSpace(serverDataDirectory)
            ? System.IO.Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", FileName)
            : System.IO.Path.Combine(serverDataDirectory!.Trim(), FileName);

    /// <summary>Reads the two keys; anything missing or broken means no limit.</summary>
    public static ClanCapSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Off;
        var max = Regex.Match(json, "\"" + MaxKey + "\"\\s*:\\s*\"?(-?\\d+)", RegexOptions.IgnoreCase);
        var mercenaries = Regex.Match(json, "\"" + MercenaryKey + "\"\\s*:\\s*\"?(true|false)", RegexOptions.IgnoreCase);
        var value = max.Success && int.TryParse(max.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
        return new ClanCapSettings(value, mercenaries.Success && mercenaries.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public string Describe() => Enabled
        ? "max " + MaxClans.ToString(CultureInfo.InvariantCulture) + " clans per kingdom" + (CountMercenaries ? ", mercenaries count" : ", mercenaries do not count")
        : "no limit";
}
