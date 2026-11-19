using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Saves;

namespace BannerlordCoopLauncher.Tests;

// After a single player save is copied to the server, every host shows that only Sandbox campaigns run
// there. It is a note, never a check: the save header lists StoryMode for Sandbox saves too.
public class SandboxOnlyNoteTests
{
    [Theory]
    [InlineData("CoodszHost", "private void LoadSinglePlayerSave_Click(", @"SetControl(note + ""\n"" + SaveSet.SandboxOnlyNote);")]
    [InlineData("TaomCoopHost", "private void NameSave_Click(", @"SetControl((renamingServerCopy ? made : made + ""\n"" + SaveSet.SandboxOnlyNote) + ""\n"" + SaveSwitchWarning.NowHosts(name));")]
    [InlineData("BurningEmpiresHost", "private void NameSave_Click(", @"SetControl((renamingServerCopy ? made : made + ""\n"" + SaveSet.SandboxOnlyNote) + ""\n"" + SaveSwitchWarning.NowHosts(name));")]
    public void The_sandbox_line_is_shown_after_a_single_player_save_is_copied(string project, string handler, string shown)
    {
        var method = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), handler);
        Assert.Contains(shown, method);
        // Only after the copy worked: the line comes after the file is copied, never before a refusal.
        var copy = project == "CoodszHost" ? "SaveCatalog.ImportSinglePlayer(" : "File.Copy(selected.FullPath, destination";
        Assert.True(method.IndexOf(copy, StringComparison.Ordinal) < method.IndexOf(shown, StringComparison.Ordinal), project);
        if (project == "CoodszHost")
            Assert.Contains("Append(SaveSet.SandboxOnlyNote);", method);
    }

    [Fact]
    public void The_sandbox_line_is_translated_in_every_language()
    {
        Assert.Equal("Only Sandbox campaigns run on a Coop server. A save from the Story Mode campaign can't load there.", SaveSet.SandboxOnlyNote);
        foreach (var code in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            Assert.True(Translator.Entries(code).TryGetValue(SaveSet.SandboxOnlyNote, out var text), code);
            Assert.DoesNotContain(":", text);
        }
    }
}
