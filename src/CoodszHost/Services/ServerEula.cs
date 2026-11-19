using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CoodszHost.Services;

// Coop 0.1.6 added a license agreement (EULA) to its dedicated server. The
// server exits with code 6 until the person hosting accepts it. Coodsz Host
// shows the packaged EULA.md and LICENSE.txt, and only after the operator
// presses the accept button does it pass DS_ACCEPT_EULA=true to that start.
// The server then writes its own eula.txt in CoopData\DedicatedServer.
public static class ServerEula
{
    public const string EnvironmentVariable = "DS_ACCEPT_EULA";
    public const int ExitCode = BannerlordCoopLauncher.Core.Server.ServerExit.EulaRequiredCode;
    public const string EulaFileName = "EULA.md";
    public const string LicenseFileName = "LICENSE.txt";

    public const string Title = "Coop server terms";

    public const string Body =
        "Coop 0.1.6 added a license agreement (EULA) for its dedicated server. The server will not start until the person hosting accepts it.\n\n"
        + $"Read the terms below. Press I accept only if you agree. {Edition.Name} then tells the server you accepted, and the server saves that in CoopData\\DedicatedServer\\eula.txt. You are asked again only when Coop changes the terms.\n\n"
        + "The terms allow private, non-commercial community hosting. Paid hosting needs written permission from the Coop team.";

    public const string Refused =
        "The server was not started. The Coop server only starts after the person hosting accepts its terms. Press Start server again to read them.";

    public const string AcceptButton = "I accept";

    public static string? TermsText(string serverFolder)
    {
        var eula = Path.Combine(serverFolder, EulaFileName);
        if (!File.Exists(eula))
            return null;
        var text = new StringBuilder(File.ReadAllText(eula).TrimEnd());
        var license = Path.Combine(serverFolder, LicenseFileName);
        if (File.Exists(license))
            text.Append("\n\n--- " + LicenseFileName + " ---\n\n").Append(File.ReadAllText(license).TrimEnd());
        return text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    // A server without EULA.md (Coop 0.1.5 and older) has nothing to accept.
    public static string? TermsHash(string serverFolder)
    {
        var text = TermsText(serverFolder);
        return text == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static bool NeedsAnswer(string? termsHash, string? acceptedHash) =>
        termsHash != null && !string.Equals(termsHash, acceptedHash, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string>? Explain(string log, int? exitCode)
    {
        var named = log.Contains("EULA acceptance required", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Bundled server terms are missing", StringComparison.OrdinalIgnoreCase);
        if (!named && exitCode != ExitCode)
            return null;
        if (log.Contains("Bundled server terms are missing", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "What happened: The server stopped before it was ready for players. Its license terms are missing from the DedicatedServer folder, so it refuses to start (exit code 6).",
                "What to do: In Steam, verify Workshop item 3770450698 and wait until the download finishes. Then press Start server again.",
                "What not to do: Do not copy EULA.md or LICENSE.txt in from another Coop version. The server checks they belong to its own release."
            ];
        }

        return
        [
            "What happened: The server stopped before it was ready for players. Coop 0.1.6 needs the person hosting to accept its server terms (EULA) first, and they were not accepted for this start (exit code 6).",
            $"What to do: Press Start server. {Edition.Name} shows the terms. Press I accept if you agree, and the server starts.",
            "What not to do: Do not edit eula.txt by hand to get around the terms. Accepting is the host's own decision, and the server checks the terms it was released with."
        ];
    }
}
