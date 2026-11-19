using BannerlordCoopLauncher.Core.Realm;

namespace BannerlordCoopLauncher.Tests;

// The Realm reward key change: codes from the key before it still check until OldCodesAcceptedUntil, and
// Coop Server Tool notes each checked Steam account so a second claim shows up whatever code it comes with.
public class RealmRewardKeyGraceTests : IDisposable
{
    private static readonly byte[] NewKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OldKey = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();
    private const ulong Player = 76561198000000001UL;

    private readonly string _before = RealmClaims.Folder;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "coodsz-realm-claims-" + Guid.NewGuid().ToString("N"));

    public RealmRewardKeyGraceTests() => RealmClaims.Folder = _folder;

    public void Dispose()
    {
        RealmClaims.Folder = _before;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Fact]
    public void A_new_key_code_checks_and_an_old_key_code_checks_until_the_last_day()
    {
        var until = RealmRewardCode.OldCodesAcceptedUntil;
        var fresh = RealmRewardCode.Create(Player, NewKey);
        var old = RealmRewardCode.Create(Player, OldKey);
        Assert.NotEqual(fresh, old);

        Assert.Equal(new RealmRewardCode.CodeCheck(RealmRewardCode.CodeKind.Current, Player), RealmRewardCode.Check(fresh, until.AddDays(400), NewKey, OldKey));
        Assert.Equal(new RealmRewardCode.CodeCheck(RealmRewardCode.CodeKind.Old, Player), RealmRewardCode.Check(old, until.AddDays(-30), NewKey, OldKey));
        // The whole last day still counts, the next one does not.
        Assert.Equal(RealmRewardCode.CodeKind.Old, RealmRewardCode.Check(old, until, NewKey, OldKey).Kind);
        Assert.Equal(new RealmRewardCode.CodeCheck(RealmRewardCode.CodeKind.OldExpired, Player), RealmRewardCode.Check(old, until.AddTicks(1), NewKey, OldKey));
        Assert.True(RealmRewardCode.Check(old, until, NewKey, OldKey).Accepted);
        Assert.False(RealmRewardCode.Check(old, until.AddTicks(1), NewKey, OldKey).Accepted);

        // Without an old key (a build without the private file) an old code is just not a real code.
        Assert.Equal(new RealmRewardCode.CodeCheck(RealmRewardCode.CodeKind.Invalid, 0), RealmRewardCode.Check(old, until.AddDays(-30), NewKey, null));
    }

    [Fact]
    public void Made_up_changed_and_empty_codes_never_check()
    {
        var now = RealmRewardCode.OldCodesAcceptedUntil.AddDays(-1);
        var other = Enumerable.Repeat((byte)7, 32).ToArray();
        Assert.Equal(RealmRewardCode.CodeKind.Invalid, RealmRewardCode.Check(RealmRewardCode.Create(Player, other), now, NewKey, OldKey).Kind);
        Assert.Equal(RealmRewardCode.CodeKind.Invalid, RealmRewardCode.Check("", now, NewKey, OldKey).Kind);
        Assert.Equal(RealmRewardCode.CodeKind.Invalid, RealmRewardCode.Check(null, now, NewKey, OldKey).Kind);
        Assert.Equal(RealmRewardCode.CodeKind.Invalid, RealmRewardCode.Check("RLM-1234-5678", now, NewKey, OldKey).Kind);

        // Change a character inside the signature, not the last one (its low bits are only padding).
        var chars = RealmRewardCode.Create(Player, OldKey).ToCharArray();
        var at = chars[^3] == '-' ? chars.Length - 4 : chars.Length - 3;
        chars[at] = chars[at] == 'A' ? 'B' : 'A';
        Assert.Equal(RealmRewardCode.CodeKind.Invalid, RealmRewardCode.Check(new string(chars), now, NewKey, OldKey).Kind);
    }

    [Fact]
    public void The_last_day_is_a_plain_date_the_release_scripts_can_read()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", RealmRewardCode.OldCodesAcceptedUntilText);
        Assert.Equal(RealmRewardCode.OldCodesAcceptedUntilText, RealmRewardCode.OldCodesAcceptedUntil.UtcDateTime.ToString("yyyy-MM-dd"));
        Assert.Equal(TimeSpan.Zero, RealmRewardCode.OldCodesAcceptedUntil.Offset);
        Assert.Contains("OldCodesAcceptedUntilText = \"" + RealmRewardCode.OldCodesAcceptedUntilText + "\"", SourceFiles.Read("src/BannerlordCoopLauncher.Core/Realm/RealmRewardCode.cs"));
        Assert.Contains("Get-OldRealmCodesUntil", SourceFiles.Read("tools/ReleaseAssets.ps1"));
    }

    // A build that has the private realm-reward-old-key.txt embeds it; one without it has no old key.
    [Fact]
    public void The_old_key_comes_from_the_private_folder_when_the_build_has_it()
    {
        var file = Path.Combine(SourceFiles.PrivateRoot(), "realm-reward-old-key.txt");
        var has = File.Exists(file);
        Assert.Equal(has, typeof(RealmRewardCode).Assembly.GetManifestResourceNames().Contains(RealmRewardCode.OldKeyResource));
        if (has)
        {
            Assert.Equal(Convert.FromHexString(File.ReadAllText(file).Trim()), RealmRewardCode.OldKey);
            Assert.NotEqual(RealmRewardCode.Key, RealmRewardCode.OldKey);
        }
        else
            Assert.Null(RealmRewardCode.OldKey);
    }

    [Fact]
    public void A_second_check_of_the_same_steam_account_shows_the_first_one()
    {
        var first = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.Null(RealmClaims.Record(Player, " rlm-abcd-efgh ", first));
        Assert.Null(RealmClaims.Record(Player + 1, "RLM-ZZZZ", first.AddMinutes(5)));

        // Another code for the same account (an old key code, say) still finds the first check.
        var earlier = RealmClaims.Record(Player, "RLM-WXYZ-0000", first.AddDays(3));
        Assert.NotNull(earlier);
        Assert.Equal(first, earlier!.FirstChecked);
        Assert.Equal("RLM-ABCD-EFGH", earlier.Code);

        // Noted once per account, and kept on disk for the next start.
        Assert.Equal(2, RealmClaims.Load().Count);
        Assert.True(File.Exists(RealmClaims.FilePath));
    }

    [Fact]
    public void A_broken_claims_file_is_kept_aside_and_checks_go_on()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(RealmClaims.FilePath, "{ not json");
        Assert.Null(RealmClaims.Record(Player, "RLM-AAAA", DateTimeOffset.UtcNow));
        Assert.Single(Directory.GetFiles(_folder, "realm-claims.broken-*.json"));
        Assert.Single(RealmClaims.Load());
    }
}
