using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 6 of the Coop 0.1.7 work: the module notes show, a new profile never wipes a hand set password, the Unblock
// button keeps the server folder guard, Modules\Coop taken by another mod, and a misplaced Coop gets unblocked too.
public sealed class Coop017Round6Tests
{
    [Fact]
    public void The_saved_notes_show_on_the_modules_page()
    {
        var save = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void SaveModules_Click(");
        Assert.Contains("ShowModuleNote(\"Already saved.", save);
        Assert.Contains("ShowModuleNote(\"Saved. The highlight on new mods is cleared.\");", save);
        Assert.DoesNotContain("ModuleRefreshStatus.Text = ", save);
    }

    [Fact]
    public void A_new_profile_and_a_load_without_an_edit_leave_the_file_password_alone()
    {
        // A load that only showed the file's password keeps null; so does an empty box over an empty file.
        Assert.Null(ServerConfigFlags.PasswordToKeep(null, "123", "123"));
        Assert.Null(ServerConfigFlags.PasswordToKeep(null, "", ""));
        // The host cleared or typed it: that is kept, and spaces only count as cleared.
        Assert.Equal("", ServerConfigFlags.PasswordToKeep(null, "", "123"));
        Assert.Equal("", ServerConfigFlags.PasswordToKeep(null, "   ", "123"));
        Assert.Equal("abc", ServerConfigFlags.PasswordToKeep(null, "abc", "123"));
        Assert.Equal("123", ServerConfigFlags.PasswordToKeep("123", "123", "123"));

        const string file = "{ \"password\": \"123\", \"steam\": true }";
        // A new profile (null) leaves the file's password; an explicit clear writes "".
        Assert.Equal("123", JsoncEditor.GetString(ServerConfigFlags.ApplyAtStart(file, null, true, true), "password"));
        Assert.False(ServerConfigFlags.ReplacesPassword(file, null));
        Assert.Equal("", JsoncEditor.GetString(ServerConfigFlags.ApplyAtStart(file, "", true, true), "password"));
        Assert.Equal("", JsoncEditor.GetString(ServerConfigFlags.ApplyAtStart(file, "  ", true, true), "password"));
        Assert.True(ServerConfigFlags.ReplacesPassword(file, ""));
        Assert.False(ServerConfigFlags.ReplacesPassword(file, "123"));
        Assert.False(ServerConfigFlags.ReplacesPassword("{ \"password\": \"\" }", "abc"));

        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var create = host[host.IndexOf("var created = new HostProfile", StringComparison.Ordinal)..];
        Assert.Contains("Password = null,", create[..create.IndexOf("};", StringComparison.Ordinal)]);
        Assert.Contains("ServerConfigFlags.PasswordToKeep(profile.Password, PasswordBox.Text, SharedServerPassword())", host);
        Assert.Contains("ServerConfigFlags.PasswordChangeLine(serverConfig, profile.Password) is { } passwordLine", host);
    }

    [Fact]
    public void The_unblock_button_keeps_the_server_folder_guard()
    {
        var unblock = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void Unblock_Click(");
        Assert.True(unblock.IndexOf("CoopInstall.IsServerFolder(ServerRoot)", StringComparison.Ordinal)
            < unblock.IndexOf("DownloadMark.Clear(ServerRoot)", StringComparison.Ordinal));
    }

    private static string Marked(string path)
    {
        Write(path, "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        return path;
    }

    private static bool HasMark(string path) => File.Exists(path + ":Zone.Identifier");

    [WindowsFact]
    public void A_misplaced_coop_is_unblocked_and_nothing_else_in_modules_is()
    {
        var root = NewRoot("coop-misplaced-marks");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Coop.10", "Coop", "v0.1.7");
            var misnamed = Marked(Path.Combine(modules, "Coop.10", "bin", "Win64_Shipping_Client", "Coop.dll"));
            Module(modules, "OtherMod", "OtherMod", "v1.0");
            var other = Marked(Path.Combine(modules, "OtherMod", "bin", "Win64_Shipping_Client", "Other.dll"));
            var rootFile = Marked(Path.Combine(modules, "readme.txt"));
            Module(Path.Combine(modules, "BannerlordCoop-v0.1.7"), "Coop", "Coop", "v0.1.7");
            var wrapped = Marked(Path.Combine(modules, "BannerlordCoop-v0.1.7", "Coop", "bin", "Win64_Shipping_Client", "Coop.dll"));

            var lines = CoopInstall.CheckModulesCoop(game);

            Assert.False(HasMark(misnamed));
            Assert.False(HasMark(wrapped));
            Assert.True(HasMark(other), "another mod beside it is not touched");
            Assert.True(HasMark(rootFile), "Modules itself is never swept");
            Assert.Contains(CoopInstall.UnblockedLine(2)!, lines);
            Assert.Contains(CoopInstall.MisnamedLine("Coop.10"), lines);
            Assert.Equal([Path.Combine(modules, "BannerlordCoop-v0.1.7", "Coop"), Path.Combine(modules, "Coop.10")],
                CoopInstall.MisplacedCoopFolders(game).Order(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_modules_coop_holding_another_mod_says_to_move_that_one_first()
    {
        var root = NewRoot("coop-taken");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Coop", "SomethingElse", "v1.0");
            Module(modules, "Coop.10", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.TakenLine("Coop.10")], CoopInstall.LayoutProblems(game));
            Assert.Equal("Modules\\Coop holds another mod. Move that mod out of Modules first, then rename Modules\\Coop.10 to Coop.", CoopInstall.TakenLine("Coop.10"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_dead_movement_limit_code_is_gone_and_the_new_texts_are_translated()
    {
        Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "src", "CoodszHost", "Services", "MovementLimit.cs")));
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        Assert.DoesNotContain("Steam lobby under your Steam name. Press Save settings, then restart the server.", xaml);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            foreach (var key in new[] { ServerConfigFlags.PasswordReplacedLine,
                         "Modules\\Coop holds another mod. Move that mod out of Modules first, then rename Modules\\{0} to Coop.",
                         "Ticked, the server shows a Steam lobby under your Steam name. The next Start server uses it.",
                         "The next Start server uses this. A running server keeps what it started with until you restart it." })
                Assert.True(table.ContainsKey(key), language + ": " + key);
        }
    }
}
