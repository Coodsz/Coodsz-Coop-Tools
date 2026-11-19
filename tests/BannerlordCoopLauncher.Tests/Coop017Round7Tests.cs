using System.Diagnostics;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 7 of the Coop 0.1.7 work: a junctioned inner Coop is never followed, a wrapped Coop beside a taken
// Modules\Coop gets one line, an unreadable Modules\Coop gives none, and Start says when it cleared the password.
public sealed class Coop017Round7Tests
{
    private static string Marked(string path)
    {
        Write(path, "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        return path;
    }

    [WindowsFact]
    public void A_wrapped_coop_that_is_a_junction_is_never_followed()
    {
        var root = NewRoot("coop-junction-inner");
        var link = "";
        try
        {
            var outside = Path.Combine(root, "outside");
            Module(root, "outside", "Coop", "v0.1.7");
            var mark = Marked(Path.Combine(outside, "bin", "Win64_Shipping_Client", "Coop.dll"));
            var game = Path.Combine(root, "game");
            var wrapper = Path.Combine(game, "Modules", "BannerlordCoop-v0.1.7");
            Directory.CreateDirectory(wrapper);
            link = Path.Combine(wrapper, "Coop");
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"")
                   { CreateNoWindow = true, UseShellExecute = false })!)
                mklink.WaitForExit();
            Assert.True(Directory.Exists(link), "the junction was made");

            Assert.Empty(CoopInstall.MisplacedCoopFolders(game));
            CoopInstall.CheckModulesCoop(game);
            Assert.True(File.Exists(mark + ":Zone.Identifier"), "the folder behind the junction keeps its mark");
        }
        finally
        {
            if (link.Length > 0 && Directory.Exists(link))
                Directory.Delete(link);
            Delete(root);
        }
    }

    [Fact]
    public void A_wrapped_coop_beside_a_taken_modules_coop_gets_one_line_and_an_unreadable_one_none()
    {
        var root = NewRoot("coop-wrapped-taken");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Coop", "SomethingElse", "v1.0");
            Module(Path.Combine(modules, "BannerlordCoop-v0.1.7"), "Coop", "Coop", "v0.1.7");
            Assert.Equal([CoopInstall.WrappedTakenLine("BannerlordCoop-v0.1.7")], CoopInstall.LayoutProblems(game));
            Assert.Equal("Modules\\Coop holds another mod. Move that mod out of Modules first, then move Modules\\BannerlordCoop-v0.1.7\\Coop into Modules.",
                CoopInstall.WrappedTakenLine("BannerlordCoop-v0.1.7"));

            // A Modules\Coop SubModule.xml that can't be read is neither Coop nor another mod: no rename or move line.
            File.WriteAllText(Path.Combine(modules, "Coop", "SubModule.xml"), "<Module><Id value=");
            Module(modules, "Coop.10", "Coop", "v0.1.7");
            Assert.Empty(CoopInstall.LayoutProblems(game));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Start_says_cleared_or_changed_for_the_password()
    {
        const string file = "{ \"password\": \"123\" }";
        Assert.Equal(ServerConfigFlags.PasswordClearedLine, ServerConfigFlags.PasswordChangeLine(file, ""));
        Assert.Equal(ServerConfigFlags.PasswordClearedLine, ServerConfigFlags.PasswordChangeLine(file, "   "));
        Assert.Equal(ServerConfigFlags.PasswordReplacedLine, ServerConfigFlags.PasswordChangeLine(file, "abc"));
        Assert.Null(ServerConfigFlags.PasswordChangeLine(file, "123"));
        Assert.Null(ServerConfigFlags.PasswordChangeLine(file, null));
        Assert.Equal("", ServerConfigFlags.NormalPassword("  "));

        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("ServerConfigFlags.PasswordChangeLine(serverConfig, profile.Password) is { } passwordLine", host);
        Assert.Contains("var password = ServerConfigFlags.NormalPassword(PasswordBox.Text);", host);

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            foreach (var key in new[] { ServerConfigFlags.PasswordClearedLine,
                         "Modules\\Coop holds another mod. Move that mod out of Modules first, then move Modules\\{0}\\Coop into Modules." })
                Assert.True(table.ContainsKey(key), language + ": " + key);
        }
    }
}
