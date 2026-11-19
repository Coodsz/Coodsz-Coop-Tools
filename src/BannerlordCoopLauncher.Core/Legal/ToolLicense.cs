using System.Text;

namespace BannerlordCoopLauncher.Core.Legal;

/// <summary>
/// The tools' license, the GNU General Public License version 3 (LICENSE at the repository root,
/// embedded here so every tool carries the same copy). The tools never ask anyone to agree to it:
/// the License button in each tool's sidebar shows it read only, with the third party notices under it.
/// </summary>
public static class ToolLicense
{
    public const string ResourceName = "Coodsz.LICENSE.txt";

    // The License window. The license itself stays English (legal text).
    public const string Title = "License";
    public const string ButtonTip = "Shows the license of the tools and the licenses of code from other projects.";
    public const string EnglishOnly = "The license is in English.";

    private static readonly Lazy<string> Embedded = new(ReadEmbedded);

    /// <summary>The full license text, with Windows line endings for the text box.</summary>
    public static string Text => Embedded.Value;

    /// <summary>What the License window shows: the license, then the third party notices.</summary>
    public static string WithNotices => Text + (ThirdPartyNotices.Text.Length == 0 ? "" : "\r\n\r\n\r\n" + ThirdPartyNotices.Text);

    private static string ReadEmbedded()
    {
        using var stream = typeof(ToolLicense).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n").Trim().Replace("\n", "\r\n");
    }
}
