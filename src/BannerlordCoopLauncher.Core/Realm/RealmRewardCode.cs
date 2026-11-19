using System.Security.Cryptography;
using System.Text;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Realm;

// The Realm starter reward code. One code per Steam account: the code holds the
// player's SteamID64 plus a short signature, so staff can check in Coodsz Host
// that the code came from the tools and which Steam account it belongs to.
// The same account always gets the same code. Coop Server Tool also keeps the
// Steam accounts it checked (RealmClaims), so a second claim shows up even when
// it comes with a code from the old key. The signing key ships inside the tools,
// so this stops copied or made-up codes, not someone who takes the tool apart.
public static class RealmRewardCode
{
    public const string Prefix = "RLM";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TagLength = 6;
    // The real signing key is not in the public source. A release build embeds it from the private folder
    // (realm-reward-key.txt, 64 hex digits; CONTRIBUTING.md, Private files). A build without that file signs
    // with this dummy key, so its codes do not check in the released tools and the other way round.
    internal static readonly byte[] DummyKey = Encoding.ASCII.GetBytes("public-source-dummy-realm-key-not-real");
    internal const string KeyResource = "Coodsz.RealmRewardKey.txt";
    internal static readonly byte[] Key = ReadKey(typeof(RealmRewardCode).Assembly.GetManifestResourceStream(KeyResource)) ?? DummyKey;
    // The key the tools signed with before the key change (realm-reward-old-key.txt in the private folder).
    // Codes made with it still check until OldCodesAcceptedUntil, so a player who got a code from an older tool
    // can still claim. A build without the file has no old key.
    internal const string OldKeyResource = "Coodsz.RealmRewardOldKey.txt";
    internal static readonly byte[]? OldKey = ReadKey(typeof(RealmRewardCode).Assembly.GetManifestResourceStream(OldKeyResource));
    // The last day (UTC) old key codes still check. tools/ReleaseAssets.ps1 reads this line, so keep it a plain
    // yyyy-MM-dd text: until that day a release needs the old key file too.
    public const string OldCodesAcceptedUntilText = "2027-03-01";
    public static readonly DateTimeOffset OldCodesAcceptedUntil =
        DateTimeOffset.ParseExact(OldCodesAcceptedUntilText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal).AddDays(1).AddTicks(-1);

    // What a check found: a code made with today's key, one made with the old key while it still checks, one
    // made with the old key after that, or no real code. Old and OldExpired carry the Steam account too.
    public enum CodeKind { Invalid, Current, Old, OldExpired }

    public readonly record struct CodeCheck(CodeKind Kind, ulong SteamId64)
    {
        public bool Accepted => Kind is CodeKind.Current or CodeKind.Old;
    }

    public const string NoSteam = "Start Steam and sign in, then click the reward button again. The code is made from your Steam account.";

    public static string Create(ulong steamId64) => Create(steamId64, Key);

    internal static string Create(ulong steamId64, byte[] key)
    {
        var data = new byte[8 + TagLength];
        WriteId(data, steamId64);
        Array.Copy(Tag(steamId64, key), 0, data, 8, TagLength);
        var text = Encode(data);
        var groups = Enumerable.Range(0, (text.Length + 3) / 4).Select(i => text.Substring(i * 4, Math.Min(4, text.Length - i * 4)));
        return Prefix + "-" + string.Join("-", groups);
    }

    public static string? ForCurrentSteamUser()
    {
        var id = SteamRegistry.TryGetSteamId64();
        return id == null ? null : Create(id.Value);
    }

    // True for a code that checks now (today's key, or the old key until OldCodesAcceptedUntil).
    public static bool TryCheck(string? code, out ulong steamId64)
    {
        var check = Check(code, DateTimeOffset.UtcNow);
        steamId64 = check.Accepted ? check.SteamId64 : 0;
        return check.Accepted;
    }

    public static CodeCheck Check(string? code, DateTimeOffset now) => Check(code, now, Key, OldKey);

    internal static CodeCheck Check(string? code, DateTimeOffset now, byte[] key, byte[]? oldKey)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new CodeCheck(CodeKind.Invalid, 0);
        var text = new string(code.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (text.StartsWith(Prefix, StringComparison.Ordinal))
            text = text[Prefix.Length..];
        var data = Decode(text);
        if (data == null || data.Length != 8 + TagLength)
            return new CodeCheck(CodeKind.Invalid, 0);
        var id = ReadId(data);
        if (id < SteamIdBase || id > SteamIdBase + uint.MaxValue)
            return new CodeCheck(CodeKind.Invalid, 0);
        if (Signed(data, id, key))
            return new CodeCheck(CodeKind.Current, id);
        if (oldKey != null && Signed(data, id, oldKey))
            return new CodeCheck(now <= OldCodesAcceptedUntil ? CodeKind.Old : CodeKind.OldExpired, id);
        return new CodeCheck(CodeKind.Invalid, 0);
    }

    private static bool Signed(byte[] data, ulong id, byte[] key) =>
        CryptographicOperations.FixedTimeEquals(Tag(id, key).AsSpan(0, TagLength), data.AsSpan(8, TagLength));

    public static string ProfileUrl(ulong steamId64) => "https://steamcommunity.com/profiles/" + steamId64;

    public const ulong SteamIdBase = 76561197960265728UL;

    private static byte[] Tag(ulong steamId64, byte[] key)
    {
        var id = new byte[8];
        WriteId(id, steamId64);
        var message = Encoding.ASCII.GetBytes("realm-reward-v1:").Concat(id).ToArray();
        return HMACSHA256.HashData(key, message);
    }

    // The key file's hex text as bytes, or null when the build has no key file. A broken file throws, so a
    // release can never sign with the wrong key without anyone seeing it (the release scripts check it first).
    internal static byte[]? ReadKey(Stream? stream)
    {
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return Convert.FromHexString(reader.ReadToEnd().Trim());
    }

    private static void WriteId(byte[] buffer, ulong value)
    {
        for (var i = 7; i >= 0; i--)
        {
            buffer[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }

    private static ulong ReadId(byte[] buffer)
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | buffer[i];
        return value;
    }

    // Crockford base32: no I, L, O or U, so the code is easy to read and type.
    private static string Encode(byte[] data)
    {
        var builder = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    private static byte[]? Decode(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            var c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
