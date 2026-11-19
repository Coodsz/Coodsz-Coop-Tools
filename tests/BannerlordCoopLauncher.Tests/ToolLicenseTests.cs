using BannerlordCoopLauncher.Core.Legal;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// Every tool asks once for the Coodsz Coop Tools License, and one answer covers all of them.
public sealed class ToolLicenseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "license-" + Guid.NewGuid().ToString("N"));
    private readonly string _before = ToolLicense.SettingsFolder;

    public ToolLicenseTests() => ToolLicense.SettingsFolder = _folder;

    public void Dispose()
    {
        ToolLicense.SettingsFolder = _before;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_tools_show_the_license_from_the_repository_root()
    {
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", ToolLicense.Text);
        Assert.Equal(ToolLicense.Hash(File.ReadAllText(Path.Combine(RepoRoot(), "LICENSE"))), ToolLicense.Hash(ToolLicense.Text));
    }

    [Fact]
    public void The_first_start_asks_and_I_agree_is_remembered_for_every_tool()
    {
        Assert.False(ToolLicense.IsAccepted());
        ToolLicense.Accept("friends-launcher");
        Assert.True(File.Exists(Path.Combine(_folder, ToolLicense.FileName)));
        Assert.True(ToolLicense.IsAccepted());

        var saved = System.Text.Json.JsonSerializer.Deserialize<ToolLicense.Acceptance>(File.ReadAllText(ToolLicense.SettingsPath));
        Assert.Equal(ToolLicense.Hash(ToolLicense.Text), saved?.Hash);
        Assert.Equal("friends-launcher", saved?.Tool);
        Assert.False(string.IsNullOrEmpty(saved?.Accepted));
    }

    [Fact]
    public void A_changed_license_asks_again()
    {
        ToolLicense.Accept("Old license text", "taom-server", DateTimeOffset.UtcNow);
        Assert.True(ToolLicense.IsAccepted("Old license text"));
        Assert.True(ToolLicense.IsAccepted("Old license text\r\n"));
        Assert.False(ToolLicense.IsAccepted("New license text"));
        Assert.False(ToolLicense.IsAccepted());
    }

    [Fact]
    public void A_tool_with_the_old_text_and_a_tool_with_the_new_text_ask_only_once_each()
    {
        ToolLicense.Accept("Old license text", "taom-server", DateTimeOffset.UtcNow);
        ToolLicense.Accept("New license text", "be-server", DateTimeOffset.UtcNow);
        Assert.True(ToolLicense.IsAccepted("Old license text"));
        Assert.True(ToolLicense.IsAccepted("New license text"));

        // Accepting the same text again does not grow the list.
        ToolLicense.Accept("Old license text", "taom-server", DateTimeOffset.UtcNow);
        var saved = System.Text.Json.JsonSerializer.Deserialize<ToolLicense.Acceptance>(File.ReadAllText(ToolLicense.SettingsPath));
        Assert.Equal([ToolLicense.Hash("Old license text"), ToolLicense.Hash("New license text")], saved?.Hashes);
        // Older tools read only hash, so it keeps the first answer.
        Assert.Equal(ToolLicense.Hash("Old license text"), saved?.Hash);
    }

    private const string OldText = "Old license text";
    private const string NewText = "New license text";

    // What Coop and Friends v1.2.77, TAOM v1.1.53 and AD1259 v1.0.32 do with the answer file:
    // read only hash, and on I agree write a new file with only hash, accepted and tool.
    private static bool ShippedIsAccepted(string text)
    {
        try
        {
            if (!File.Exists(ToolLicense.SettingsPath))
                return false;
            var saved = System.Text.Json.JsonSerializer.Deserialize<ToolLicense.Acceptance>(File.ReadAllText(ToolLicense.SettingsPath));
            return saved?.Hash != null && string.Equals(saved.Hash, ToolLicense.Hash(text), StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static void ShippedAccept(string text) =>
        File.WriteAllText(ToolLicense.SettingsPath, "{ \"hash\": \"" + ToolLicense.Hash(text) + "\", \"accepted\": \"2026-10-20T10:00:00Z\", \"tool\": \"taom-launcher\" }");

    private int _shippedAsked;
    private int _newAsked;

    // Opens the shipped tool (old text, old code) and clicks I agree when it asks.
    private void OpenShipped()
    {
        if (ShippedIsAccepted(OldText))
            return;
        _shippedAsked++;
        Directory.CreateDirectory(_folder);
        ShippedAccept(OldText);
    }

    // Opens a tool with this code and the new text, and clicks I agree when it asks.
    private void OpenNew()
    {
        if (ToolLicense.IsAccepted(NewText))
            return;
        _newAsked++;
        ToolLicense.Accept(NewText, "be-launcher", DateTimeOffset.UtcNow);
    }

    private void SwitchBackAndForth()
    {
        for (var i = 0; i < 4; i++)
        {
            OpenShipped();
            OpenNew();
        }
    }

    [Fact]
    public void A_shipped_tool_first_then_a_new_text_tool_ask_once_each_and_never_again()
    {
        OpenShipped();
        OpenNew();
        SwitchBackAndForth();
        Assert.Equal(1, _shippedAsked);
        Assert.Equal(1, _newAsked);
        var saved = System.Text.Json.JsonSerializer.Deserialize<ToolLicense.Acceptance>(File.ReadAllText(ToolLicense.SettingsPath));
        Assert.Equal(ToolLicense.Hash(OldText), saved?.Hash);
    }

    [Fact]
    public void A_new_text_tool_first_then_a_shipped_tool_that_drops_the_list_ask_once_each_and_never_again()
    {
        OpenNew();
        // The shipped tool sees a hash that is not its own, asks, and rewrites the file without the list.
        OpenShipped();
        Assert.DoesNotContain("hashes", File.ReadAllText(ToolLicense.SettingsPath));
        OpenNew();
        SwitchBackAndForth();
        Assert.Equal(1, _shippedAsked);
        Assert.Equal(1, _newAsked);
    }

    [Fact]
    public void A_corrupt_answer_file_asks_each_tool_once_more_and_then_never_again()
    {
        OpenShipped();
        OpenNew();
        File.WriteAllText(ToolLicense.SettingsPath, "{ not json");
        // The side file alone never counts, so a corrupt answer file still asks again.
        Assert.False(ToolLicense.IsAccepted(NewText));
        Assert.False(ToolLicense.IsAccepted(OldText));
        OpenNew();
        SwitchBackAndForth();
        Assert.Equal(2, _shippedAsked);
        Assert.Equal(2, _newAsked);
    }

    [Fact]
    public void A_missing_answer_file_asks_again_even_with_the_side_file()
    {
        ToolLicense.Accept(NewText, "be-launcher", DateTimeOffset.UtcNow);
        Assert.True(File.Exists(ToolLicense.ListPath));
        File.Delete(ToolLicense.SettingsPath);
        Assert.False(ToolLicense.IsAccepted(NewText));
    }

    [Fact]
    public void A_corrupt_side_file_only_loses_the_list()
    {
        ToolLicense.Accept(OldText, "taom-launcher", DateTimeOffset.UtcNow);
        ToolLicense.Accept(NewText, "be-launcher", DateTimeOffset.UtcNow);
        File.WriteAllText(ToolLicense.ListPath, "{ not json");
        Assert.True(ToolLicense.IsAccepted(OldText));
        Assert.True(ToolLicense.IsAccepted(NewText));

        // The shipped tool's rewrite plus the corrupt side file loses the new answer, so it asks once and repairs both.
        ShippedAccept(OldText);
        Assert.False(ToolLicense.IsAccepted(NewText));
        ToolLicense.Accept(NewText, "be-launcher", DateTimeOffset.UtcNow);
        ShippedAccept(OldText);
        Assert.True(ToolLicense.IsAccepted(NewText));
    }

    [Fact]
    public void The_single_hash_an_older_tool_wrote_still_counts_and_is_kept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ToolLicense.SettingsPath, "{ \"hash\": \"" + ToolLicense.Hash("Old license text") + "\", \"tool\": \"taom-server\" }");
        Assert.True(ToolLicense.IsAccepted("Old license text"));
        Assert.False(ToolLicense.IsAccepted("New license text"));

        ToolLicense.Accept("New license text", "be-server", DateTimeOffset.UtcNow);
        Assert.True(ToolLicense.IsAccepted("Old license text"));
        Assert.True(ToolLicense.IsAccepted("New license text"));
        // The older tool reads only hash, so it must still hold the older tool's answer.
        var saved = System.Text.Json.JsonSerializer.Deserialize<ToolLicense.Acceptance>(File.ReadAllText(ToolLicense.SettingsPath));
        Assert.Equal(ToolLicense.Hash("Old license text"), saved?.Hash);
    }

    [Fact]
    public void A_corrupt_or_empty_answer_file_asks_again()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ToolLicense.SettingsPath, "{ not json");
        Assert.False(ToolLicense.IsAccepted());
        File.WriteAllText(ToolLicense.SettingsPath, "{}");
        Assert.False(ToolLicense.IsAccepted());
        File.WriteAllText(ToolLicense.SettingsPath, "");
        Assert.False(ToolLicense.IsAccepted());

        ToolLicense.Accept("coodsz-host");
        Assert.True(ToolLicense.IsAccepted());
    }

    [Fact]
    public void A_build_without_license_text_never_locks_a_tool() =>
        Assert.True(ToolLicense.IsAccepted(""));

    [Fact]
    public void Every_tool_asks_for_the_license_at_startup()
    {
        foreach (var app in new[] { "BannerlordCoopLauncher", "CoodszHost", "TaomCoopHost", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher" })
            Assert.Contains("Coodsz.Ui.UiLanguage.LicenseOnce(", File.ReadAllText(Path.Combine(RepoRoot(), "src", app, "App.xaml.cs")));
    }

    [Fact]
    public void The_license_window_is_translated_into_every_language()
    {
        string[] lines = [ToolLicense.Title, ToolLicense.Intro, ToolLicense.EnglishOnly, ToolLicense.Agree, ToolLicense.AgreeTip, ToolLicense.Exit, ToolLicense.ExitTip];
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.All(lines, line => Assert.True(table.ContainsKey(line), language.Code + " has no translation for: " + line));
        }
    }
}
