using BannerlordCoopLauncher.Core.Updates;
using BurningEmpires.Core;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// The v1.3.6 release review (Codex): Steam advertise read before a missing mod-config.json returns early, the Burning
// Empires repair never mixes an older Coop into a newer server, and the release texts say what the tools really do.
public sealed class Release136CodexFixTests
{
    [Fact]
    public void Start_keeps_steam_on_when_mod_config_is_missing()
    {
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var load = SourceFiles.Method(host, "private void LoadDifficulty()");
        var switches = load.IndexOf("LoadServerSwitches();", StringComparison.Ordinal);
        var early = load.IndexOf("if (!File.Exists(ServerSession.ModConfigPath))", StringComparison.Ordinal);
        Assert.True(switches >= 0 && switches < early, "the switches are read before the missing mod-config.json return");
        var read = SourceFiles.Method(host, "private void LoadServerSwitches()");
        Assert.True(read.IndexOf("_serverFlagsLoaded = File.Exists(ServerSession.ServerConfigPath);", StringComparison.Ordinal)
            < read.IndexOf("SteamBox.IsChecked = Flag(server, \"steam\", true);", StringComparison.Ordinal));
        // The password, autosave and restart hours boxes load there too, so an unread password box never clears the file's.
        Assert.Contains("PasswordBox.Text = _settings.Current().Password ?? JsoncEditor.GetString(server, \"password\");", read);
        Assert.Contains("AutosaveBox.Text = ", read);
        Assert.Contains("RestartHoursBox.Text = JsoncEditor.GetRaw(server, \"autoRestartHours\");", read);
        Assert.DoesNotContain("PasswordBox.Text = ", load);

        // With the box read from the file (true), Start writes true back.
        var root = NewRoot("steam-no-modconfig");
        try
        {
            var path = Write(Path.Combine(root, "server-config.json"), "{\n  \"steam\": true,\n  \"password\": \"\"\n}\n");
            JsoncEditor.Write(path, ServerConfigFlags.ApplyAtStart(JsoncEditor.Read(path), null, steamBox: true, loadedFromFile: true));
            Assert.Equal("true", JsoncEditor.GetRaw(JsoncEditor.Read(path), "steam"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_repair_never_takes_files_from_an_older_coop_package()
    {
        var root = NewRoot("be-repair-source");
        try
        {
            var workshop = WorkshopServer(Path.Combine(root, "w"));
            var server = WorkshopServer(Path.Combine(root, "s"));
            var source = new BeServerSource(workshop, Picked: false);
            Assert.Equal(workshop, BeServerPrepare.RepairSource(server, source));
            Module(Path.Combine(server, "engine", "Modules"), "Coop", "Coop", "v0.1.7");
            Assert.Null(BeServerPrepare.RepairSource(server, source));
            Assert.Null(BeServerPrepare.RepairSource(server, null));
        }
        finally
        {
            Delete(root);
        }

        var host = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        Assert.Contains("var workshop = ModuleRepairSource(serverRoot);", host);
        Assert.Contains("var workshop = owner.ModuleRepairSource(serverRoot);", host);
        Assert.Contains("NoteLog(BeServerPrepare.RepairSourceSkipped);", SourceFiles.Method(host, "private string? ModuleRepairSource("));
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
            Assert.True(BannerlordCoopLauncher.Core.Localization.Translator.Entries(language).ContainsKey(BeServerPrepare.RepairSourceSkipped), language);
    }

    [Fact]
    public void The_readme_and_release_texts_say_what_the_tools_do()
    {
        Assert.Contains("Coop is the exception. A Coop in Modules\\Coop is the one the game uses", SourceFiles.Read("README.txt"));

        var lines = WhatsNew.Friends.First(entry => entry.Version == "v1.3.6").Lines;
        Assert.Contains(lines, line => line.Contains("The ModDB download has no Coop folder inside, so make a folder named Coop in Modules", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("one level too high", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("Start server now writes the Server password and Steam advertise into the server's settings, so you don't need Save settings first.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("A new campaign now stays new after a crash or CoopModPatch's first setup stop before its first save.", StringComparison.Ordinal));

        var be = WhatsNew.Be.First(entry => entry.Version == "v1.0.6").Lines;
        Assert.DoesNotContain(be, line => line.StartsWith("Start server now unblocks the server's files", StringComparison.Ordinal));
        Assert.Contains("Prepare server now asks before it puts an older Coop over a newer one, and Start server keeps the newer Coop.", be);

        foreach (var file in new[] { "v1.3.6-coop.md", "v1.1.6-ad1259.md" })
            Assert.DoesNotContain("one level too high", SourceFiles.Read(Path.Combine("docs", "release-notes", file)));
        Assert.DoesNotContain("the Server Tool unblocks the server's files", SourceFiles.Read(Path.Combine("docs", "release-notes", "v1.0.6-burningempires.md")));
        Assert.Contains("README.-.Instructions.txt", SourceFiles.Read(Path.Combine("docs", "release-notes", "v1.2.6-taom.md")));
    }
}
