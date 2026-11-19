using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

// AD1259 crash report #19 (CR-SR59T): a game (client) Coop 0.1.7 copied over the server's own engine\Modules\Coop.
// The server stopped itself with "FileLoadException: Assembly with same name is already loaded", but the exit text
// said exit code 1 and closed from outside.
[Collection(TranslatorLanguage.Name)]
public sealed class ServerFatalLineTests : IDisposable
{
    private const string FatalEvent = "@DS@{\"ev\":\"state\",\"phase\":\"fatal\",\"save\":\"ad1249\",\"detail\":\"FileLoadException: Assembly with same name is already loaded\",\"pw\":true}";

    // The stack under the FATAL line: Coop's own loader hit the second copy.
    private const string CoopDriverStack = "System.IO.FileLoadException: Assembly with same name is already loaded\n   at CoopDriver.EnsureLoaded()";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-fatal-" + Guid.NewGuid().ToString("N"));

    public ServerFatalLineTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private static readonly CrashSignatureFile Signatures = CrashSignatureFile.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "crash-signatures.json")))!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    // Sixty plain lines after the fatal one, like the window's last lines once the FATAL line scrolled out.
    private static string QuietWindow() =>
        string.Join("\n", Enumerable.Range(0, 60).Select(i => "DedicatedServer   shutting down step " + i));

    [Fact]
    public void A_FATAL_during_line_is_quoted_and_never_called_closed_from_outside()
    {
        const string fatal = "[12:01:02] [DedicatedServer] FATAL during boot: the campaign could not be started";
        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", false, "DedicatedServer   loading\n" + fatal + "\nDedicatedServer   bye", null, [], ["Coop"]);
        Assert.Contains(lines, line => line.StartsWith("What happened: The server stopped itself", StringComparison.Ordinal)
            && line.Contains("[DedicatedServer] FATAL during boot: the campaign could not be started", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("closed from outside", StringComparison.Ordinal));

        // The window lost the line, but the run kept it.
        var kept = ServerCrashExplain.Explain("Server process exited with code 1.", false, QuietWindow(), null, [], ["Coop"],
            apart: new ServerCrashExplain.Apart(QuietWindow(), null), fatalLine: fatal);
        Assert.Contains(kept, line => line.StartsWith("What happened: The server stopped itself", StringComparison.Ordinal) && line.Contains("FATAL during boot", StringComparison.Ordinal));
        Assert.DoesNotContain(kept, line => line.Contains("closed from outside", StringComparison.Ordinal));

        // Without it the quiet window stays a silent exit.
        var silent = ServerCrashExplain.Explain("Server process exited with code 1.", false, QuietWindow(), null, [], ["Coop"]);
        Assert.Contains(silent, line => line.Contains("closed from outside", StringComparison.Ordinal));
    }

    [Fact]
    public void The_DS_fatal_event_counts_and_is_quoted_by_its_detail()
    {
        Assert.True(ServerCrashExplain.IsFatalLine(FatalEvent));
        Assert.True(ServerCrashExplain.IsFatalLine("[DedicatedServer] FATAL during load: System.IO.FileLoadException: x"));
        Assert.False(ServerCrashExplain.IsFatalLine("@DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"a\",\"pw\":false}"));
        Assert.False(ServerCrashExplain.IsFatalLine("[CoopModPatch] not fatal at all"));
        Assert.Equal("FATAL FileLoadException: Assembly with same name is already loaded", ServerCrashExplain.FindFatal("a\n" + FatalEvent + "\nb"));
        Assert.Null(ServerCrashExplain.FindFatal("DedicatedServer   pulse: players=1"));

        var lines = ServerCrashExplain.FatalLines("FATAL x", wasServing: true);
        Assert.StartsWith("What happened: The server was serving players, then it stopped itself.", lines[0]);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void Assembly_already_loaded_says_a_game_Coop_is_in_the_server_folder()
    {
        var match = CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "server",
            ExitCode = 1,
            Tool = "ad1259",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.HostConsole] = [FatalEvent], [CrashLogSource.ServerLog] = [CoopDriverStack] }
        });
        Assert.Equal("server-client-coop-in-server-folder", match?.Signature.Id);
        Assert.Equal(CrashCategory.Setup, match!.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("3770450698", match.WhatToDo);
        Assert.Contains("DedicatedServer\\engine\\Modules\\Coop", match.WhatHappened);
        Assert.Contains("3770450698", match.Localized("de").WhatToDo);

        // The Server Tool's exit text, with the event only kept by the run and the stack in this run's Coop server log.
        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", false, QuietWindow() + "\n" + CoopDriverStack, null, [], ["Coop"],
            apart: new ServerCrashExplain.Apart(QuietWindow(), CoopDriverStack), fatalLine: FatalEvent);
        Assert.Contains(lines, line => line.StartsWith("What happened: The server stopped itself while it loaded Coop", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("closed from outside", StringComparison.Ordinal));

        // Another FileLoadException is not this.
        Assert.NotEqual("server-client-coop-in-server-folder", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "server",
            ExitCode = 1,
            Tool = "coop",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Server] = ["System.IO.FileLoadException: Could not load file or assembly 'X'"] }
        })?.Signature.Id);
    }

    [Fact]
    public void Two_mods_with_the_same_DLL_are_not_called_a_game_Coop_in_the_server_folder()
    {
        // The same exception from a mod's own loader, with no CoopDriver anywhere.
        const string modStack = "System.IO.FileLoadException: Assembly with same name is already loaded\n   at SomeMod.SubModule.OnSubModuleLoad()";
        foreach (var source in new[] { CrashLogSource.Server, CrashLogSource.ServerLog, CrashLogSource.HostConsole })
        {
            var texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.HostConsole] = [FatalEvent] };
            texts[source] = source == CrashLogSource.HostConsole ? [FatalEvent + "\n" + modStack] : [modStack];
            var match = CrashSignatureMatcher.Match(Signatures, new CrashEvidence { Side = "server", ExitCode = 1, Tool = "coop", Texts = texts });
            Assert.NotEqual("server-client-coop-in-server-folder", match?.Signature.Id);
        }

        // A CoopDriver line that is no stack frame is not enough.
        Assert.NotEqual("server-client-coop-in-server-folder", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "server",
            ExitCode = 1,
            Tool = "coop",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Server] = [FatalEvent + "\n[CoopDriver] loading Coop\n" + modStack] }
        })?.Signature.Id);

        // A CoopDriver frame in another log than the exception still counts: the window and the Coop server log are one crash.
        Assert.Equal("server-client-coop-in-server-folder", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "server",
            ExitCode = 1,
            Tool = "coop",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Server] = [FatalEvent], [CrashLogSource.ServerLog] = ["   at CoopDriver.EnsureLoaded()"] }
        })?.Signature.Id);

        // The exit text quotes the FATAL line instead.
        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", false, QuietWindow() + "\n" + modStack, null, [], ["Coop"],
            apart: new ServerCrashExplain.Apart(QuietWindow(), modStack), fatalLine: FatalEvent);
        Assert.DoesNotContain(lines, line => line.Contains("while it loaded Coop", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("What happened: The server stopped itself before it was ready for players. Its last error line says FATAL FileLoadException", StringComparison.Ordinal));
    }

    private string Server(string root, bool clientCopy, string serverVersion = "v0.1.6")
    {
        var coop = Path.Combine(root, "engine", "Modules", "Coop");
        Directory.CreateDirectory(Path.Combine(coop, "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(coop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "server");
        File.WriteAllText(Path.Combine(coop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"" + serverVersion + "\"/></Module>");
        if (clientCopy)
        {
            Directory.CreateDirectory(Path.Combine(coop, "bin", "Win64_Shipping_Client"));
            File.WriteAllText(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "client");
        }

        Directory.CreateDirectory(Path.Combine(root, "engine", "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(root, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll"), "harmony");
        return root;
    }

    [Fact]
    public void A_client_Coop_Core_dll_in_the_server_Coop_folder_is_told_before_Start()
    {
        var own = Server(Path.Combine(_directory, "own", "DedicatedServer"), clientCopy: true);
        Assert.Equal(CoopServerBin.ClientCoopInOwnServer, CoopServerBin.ClientCoopInServer(own));
        Assert.Contains(CoopServerBin.ClientCoopInOwnServer, CoopServerBin.Ensure(own, null)!.Split('\n'));
        // Nothing is moved or deleted.
        Assert.True(File.Exists(Path.Combine(own, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Client", "Coop.Core.dll")));

        var clean = Server(Path.Combine(_directory, "clean", "DedicatedServer"), clientCopy: false);
        Assert.Null(CoopServerBin.ClientCoopInServer(clean));
        Assert.Null(CoopServerBin.Ensure(clean, null));
    }

    [Fact]
    public void A_Workshop_server_says_to_subscribe_again_and_a_version_alone_is_quiet()
    {
        var item = Path.Combine(_directory, "steamapps", "workshop", "content", "261550", "3770450698");
        Directory.CreateDirectory(item);
        File.WriteAllText(Path.Combine(item, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.7\"/></Module>");

        var copied = Server(Path.Combine(item, "DedicatedServer"), clientCopy: true, serverVersion: "v0.1.7");
        Assert.Equal(CoopServerBin.ClientCoopInWorkshopServer, CoopServerBin.ClientCoopInServer(copied));
        Assert.Contains("unsubscribe from Workshop item 3770450698", CoopServerBin.ClientCoopInWorkshopServer);
        Assert.Contains("Verify does not repair Workshop items", CoopServerBin.ClientCoopInWorkshopServer);

        // A server Coop version that is not the item's own, as in the middle of a Steam update, is no client copy.
        File.Delete(Path.Combine(copied, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Client", "Coop.Core.dll"));
        File.WriteAllText(Path.Combine(copied, "engine", "Modules", "Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
        Assert.Null(CoopServerBin.ClientCoopInServer(copied));
        Assert.Null(CoopServerBin.Ensure(copied, null));

        // The server Coop of the same version is the real one.
        File.WriteAllText(Path.Combine(copied, "engine", "Modules", "Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.7\" /></Module>");
        Assert.Null(CoopServerBin.ClientCoopInServer(copied));
    }

    // The window strips What happened: and the like before it translates, so a key with the prefix never matched.
    [Fact]
    public void The_FATAL_text_is_in_the_tool_language_with_the_English_prefixes()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            var lines = ServerCrashExplain.FatalLines("FATAL x", wasServing: false);
            Assert.Equal("What happened: Der Server hat sich selbst gestoppt, bevor er für Spieler bereit war. Seine letzte Fehlerzeile lautet FATAL x", lines[0]);
            Assert.StartsWith("What to do: Diese Zeile nennt die Ursache.", lines[1]);
            Assert.StartsWith("What not to do: Verschieb dafür nicht Bannerlord Coop", lines[2]);
            Assert.Equal("Der Server war für Spieler da und hat sich dann selbst gestoppt. Seine letzte Fehlerzeile lautet FATAL {y}",
                LogLineFormatter.Format(ServerCrashExplain.FatalLines("FATAL {y}", wasServing: true)[0])!.Value.Message);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }

        string[] bodies =
        [
            "The server stopped itself before it was ready for players. Its last error line says {0}",
            "The server was serving players, then it stopped itself. Its last error line says {0}",
            "That line names the cause. Press Start server again. If it stops with the same line, click Send my logs so Coodsz can look at it.",
            "Do not move Bannerlord Coop or turn your mods off for this, unless that line names a mod.",
            // Shown as a whole line (a Note), so the whole text is the key.
            CoopServerBin.ClientCoopInWorkshopServer,
            CoopServerBin.ClientCoopInOwnServer
        ];
        foreach (var code in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var entries = BannerlordCoopLauncher.Core.Localization.Translator.Entries(code);
            foreach (var body in bodies)
                Assert.True(entries.ContainsKey(body), code + " has no " + body);
            Assert.DoesNotContain(entries.Keys, key => key.StartsWith("What happened: The server stopped itself before it was ready", StringComparison.Ordinal));
        }
    }

    // Start server empties the window's last lines with the FATAL line, so the exit text of this run never quotes the
    // FATAL line of an earlier one.
    [Fact]
    public void Start_server_forgets_the_last_run_s_lines_before_the_server_starts()
    {
        var start = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs")), "private void StartServer()");
        var clear = start.IndexOf("_recentServerLines.Clear();", StringComparison.Ordinal);
        Assert.True(clear > 0);
        Assert.True(clear > start.IndexOf("_fatalLine = null;", StringComparison.Ordinal));
        Assert.True(clear < start.IndexOf("_session.Start(", StringComparison.Ordinal));
    }
}
