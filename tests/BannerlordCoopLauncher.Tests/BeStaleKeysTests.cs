using BannerlordCoopLauncher.Tests;

namespace BurningEmpires.Core.Tests;

// The language files dropped the older Burning Empires wordings no code, XAML or guide shows any
// more (tools/ExtractUiText listed none of them), and the host-console log line that stays English.
public sealed class BeStaleKeysTests
{
    [Fact]
    public void Old_burning_empires_wordings_are_gone_from_every_language_file()
    {
        string[] stale =
        [
            "Safe mode joins with Plain Burning Empires this one time. Your setups are not changed.",
            "Deletes this setup. Your mods stay installed. The two locked setups cannot be deleted.",
            "Two locked setups, Plain Burning Empires and Burning Empires with Elephants. The host and every player pick the same one.",
            "Burning Empires is {0}. Co-op needs {1}, the main file and then the hotfix.",
            "Deleted the server copy of {0} and its Burning Empires data. Single-player saves were not touched.",
            " The hotfix download was not found. Download it from Nexus Mods and put it next to the main file.",
            "[BE host] Server exited."
        ];

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            foreach (var key in stale)
                Assert.False(json.Contains("\"" + key + "\":", StringComparison.Ordinal), language + " still has " + key);

            // The TAOM server tool's own line and the current Burning Empires wording stay.
            Assert.Contains("\"[TAOM host] Server exited.\":", json);
            Assert.Contains("\"Deletes this setup. Your mods stay installed. The locked Burning Empires setup cannot be deleted.\":", json);
        }
    }
}
