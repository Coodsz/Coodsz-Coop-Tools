using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoodszHost.Services;

// Start-ModdedServer.ps1 sets "clientsCanUseCheats" to false in mod-config.json on
// every start, so the Client cheats setting never reached the server. Host starts a
// copy of the script with only that step taken out; the copy sits next to the
// original so the script's own folder ($PSScriptRoot) stays the same. If the script
// ever changes so the step is not found, Host uses the original script unchanged.
public static class HostStartScript
{
    public const string OriginalName = "Start-ModdedServer.ps1";
    // Keeps "Start-ModdedServer.ps1" in the name so finding a left-over server process still matches it.
    public const string CopyName = "Coodsz-Start-ModdedServer.ps1";

    private static readonly Regex ForcedCheatsOff = new(
        @"^[ \t]*Set-CoopBooleanModOption[ \t]*`[ \t]*\r?\n[ \t]*-Key[ \t]+'clientsCanUseCheats'[ \t]*`[ \t]*\r?\n[ \t]*-Enabled[ \t]+\$false[ \t]*\r?\n",
        RegexOptions.Multiline);

    private const string BlockedBanner = "Write-Host 'Client cheats: ALWAYS blocked by the authoritative server'";
    private const string KeptBanner = "Write-Host 'Client cheats: as set in mod-config.json (Coodsz Coop Server Tool)'";

    // Returns the script text with the forced step removed, or null when that step is not in it.
    public static string? WithoutForcedCheats(string script)
    {
        if (!ForcedCheatsOff.IsMatch(script))
            return null;
        var text = ForcedCheatsOff.Replace(script, "", 1);
        return text.Replace(BlockedBanner, KeptBanner);
    }

    // The script Host should run: the patched copy when it could be written, else the original.
    public static string Prepare(string serverFolder)
    {
        var original = Path.Combine(serverFolder, OriginalName);
        try
        {
            var raw = File.ReadAllBytes(original);
            var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            var patched = WithoutForcedCheats(text);
            if (patched == null)
                return original;
            var copy = Path.Combine(serverFolder, CopyName);
            var bytes = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(patched)).ToArray();
            if (!File.Exists(copy) || !File.ReadAllBytes(copy).SequenceEqual(bytes))
                File.WriteAllBytes(copy, bytes);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return original;
        }
    }
}
