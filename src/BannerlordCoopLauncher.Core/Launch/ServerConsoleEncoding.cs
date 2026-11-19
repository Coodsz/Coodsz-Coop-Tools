using System.Runtime.InteropServices;
using System.Text;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// What the dedicated server writes its console in. It is a .NET console app, and with its output
/// sent to a pipe it writes the OEM code page of the PC (857 on a Turkish Windows, 437 on an English
/// one), through Start-ModdedServer.ps1 too. A host with no console of its own reads a pipe as the
/// ANSI code page unless told otherwise, which turned YÜZÜKLERİN into YšZšKLER˜N (CR-7W2FC).
/// </summary>
public static class ServerConsoleEncoding
{
    /// <summary>The OEM code page of this PC, or null (keep the default) when it cannot be made.</summary>
    public static Encoding? Oem() => OperatingSystem.IsWindows() ? ForCodePage((int)GetOEMCP()) : null;

    /// <summary>
    /// What the server reads its console commands in. Its ServerConsole reads a redirected stdin as UTF-8 whatever the
    /// code page, so a say written in the OEM page reached players as Gr□e (one bad byte per letter). No BOM, so the
    /// first command never starts with one.
    /// </summary>
    public static Encoding Input() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal static Encoding? ForCodePage(int codePage)
    {
        if (codePage == 65001)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [DllImport("kernel32")]
    private static extern uint GetOEMCP();
}
