using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Small fixes after v1.3.6.
public class AfterV136FixesTests
{
    [Fact]
    public void The_crash_bar_waits_for_the_load_mode_retry_and_shows_on_every_other_exit()
    {
        var crash = ServerExitInfo.From(-1073741819, wasExpected: false);
        var retry = new LoadCrashRetry();

        // The first load crash starts the retry: no bar yet (TAOM #42).
        var first = retry.Decide(crash, servedThisRun: false, RescueMode.Auto);
        Assert.Equal(LoadCrashStep.RetryInLoadMode, first);
        Assert.False(LoadCrashRetry.ShowsCrashBar(first));
        // The retry crashed too: the bar shows.
        Assert.True(LoadCrashRetry.ShowsCrashBar(retry.Decide(crash, servedThisRun: false, RescueMode.Load)));
        Assert.True(LoadCrashRetry.ShowsCrashBar(LoadCrashStep.NotALoadCrash));
        Assert.True(LoadCrashRetry.ShowsCrashBar(LoadCrashStep.LeftToCrashRestart));
    }

    [Theory]
    [InlineData("CoodszHost", "private void OnServerExited(ServerExitInfo exit)", "OfferServerCrashReport(info, crashLine);")]
    [InlineData("TaomCoopHost", "process.Exited += (_, _) =>", "OfferServerCrashReport(process);")]
    [InlineData("BurningEmpiresHost", "process.Exited += (_, _) =>", "OfferServerCrashReport(process);")]
    public void Every_Server_Tool_decides_the_retry_once_before_it_offers_the_crash_bar(string project, string handler, string offer)
    {
        var source = SourceFiles.Window(SourceFiles.Root(), project).Replace("\r\n", "\n");
        var at = source.IndexOf(handler, StringComparison.Ordinal);
        Assert.True(at >= 0, project);
        var exited = source[at..];
        var gate = exited.IndexOf("if (LoadCrashRetry.ShowsCrashBar(loadStep))\n", StringComparison.Ordinal);
        Assert.True(gate >= 0, project);
        // The crash bar offer sits behind the gate.
        Assert.StartsWith(offer, exited[(gate + "if (LoadCrashRetry.ShowsCrashBar(loadStep))\n".Length)..].TrimStart());
        // Decide runs once per exit: the retry step only reuses it.
        Assert.Equal(1, CountOf(source, "_loadRetry.Decide("));
    }

    [Fact]
    public void The_TAOM_save_fix_notes_name_Restore_save_backup_on_the_Saves_page()
    {
        var lines = new[] { TaomRescueText.FixedSaved, TaomRescueText.FixedNotSaved, TaomRescueText.RetryFailedBody };
        foreach (var line in lines.Append(TaomRescueText.BackupKept("TaomTest")))
        {
            Assert.Contains("Restore save backup on the Saves page", line);
            Assert.DoesNotContain("Load backup", line);
        }

        var host = SourceFiles.Read("src/TaomCoopHost/MainWindow.Rescue.cs");
        Assert.DoesNotContain("AutoRescue.FixedSaved", host);
        Assert.DoesNotContain("AutoRescue.FixedNotSaved", host);
        Assert.DoesNotContain("AutoRescue.RetryFailedBody", host);
        Assert.DoesNotContain("AutoRescue.BackupKept", host);
        Assert.Contains("Content=\"Restore save backup\"", SourceFiles.Read("src/TaomCoopHost/MainWindow.xaml"));
        foreach (var language in Languages)
        {
            var json = LanguageFile(language);
            foreach (var line in lines)
                Assert.True(json.Contains("\"" + line + "\":", StringComparison.Ordinal), language + " is missing " + line);
        }
    }

    [Fact]
    public void A_host_with_no_mod_settings_still_answers_and_the_client_says_so_once()
    {
        // The request asks for the empty answer, so an older add on never gets a message type it lacks.
        Assert.NotNull(typeof(CoodszSettingsSync.Messages.NetworkModSettingsRequest).GetProperty("WantsNoSettingsAnswer"));
        Assert.Equal("Settings sync: the host has no mod settings to share, so your own settings stay.", CoodszSettingsSync.SyncNotes.NoSettings);

        var server = SourceFiles.Read("compat/CoodszSettingsSync/src/Server/ServerSync.cs");
        var answer = SourceFiles.Method(server, "private static void Answer(List<NetPeer> peers)");
        Assert.Contains("if (snapshots.Count == 0 && wantsNone)", answer);
        Assert.Contains("network.SendImmediate(peer, new NetworkModSettingsNone", answer);
        Assert.Contains("QueueAnswer(peer, payload.What.WantsNoSettingsAnswer)", server);

        var client = SourceFiles.Read("compat/CoodszSettingsSync/src/Client/ClientSync.cs");
        Assert.Contains("WantsNoSettingsAnswer = true,", client);
        Assert.Contains("broker.Subscribe<NetworkModSettingsNone>(HandleNone);", client);
        Assert.Contains("_broker.Unsubscribe<NetworkModSettingsNone>(HandleNone);", client);
        // Only while it still waits, so the line comes once and never after the waiting line.
        var none = SourceFiles.Method(client, "internal static void ReceiveNone(");
        Assert.True(none.IndexOf("if (!_awaiting) return;", StringComparison.Ordinal) < none.IndexOf("_log(SyncNotes.NoSettings);", StringComparison.Ordinal));
    }

    [Fact]
    public void TAOM_gives_no_rename_or_move_line_for_a_misplaced_Coop_of_another_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "after136-taom-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var pin = TaomStack.CoopVersion;

            // The ModDB 7z extracted to Coop.10: the other launchers say rename it, TAOM only for its own Coop.
            Module(Path.Combine(modules, "Coop.10"), "v0.1.7");
            Assert.Equal([CoopInstall.MisnamedLine("Coop.10")], CoopInstall.LayoutProblems(game));
            Assert.Empty(CoopInstall.LayoutProblems(game, pin));
            Assert.Empty(CoopInstall.CheckModulesCoop(game, pin));
            Module(Path.Combine(modules, "Coop.10"), pin);
            Assert.Equal([CoopInstall.MisnamedLine("Coop.10")], CoopInstall.LayoutProblems(game, pin));
            // TAOM's Coop written without the v or with a fourth number is still TAOM's Coop.
            Module(Path.Combine(modules, "Coop.10"), pin.TrimStart('v') + ".0");
            Assert.Equal([CoopInstall.MisnamedLine("Coop.10")], CoopInstall.LayoutProblems(game, pin));
            Directory.Delete(Path.Combine(modules, "Coop.10"), recursive: true);

            // Wrapped, one folder too deep and loose: none would become Modules\Coop for a newer Coop.
            Module(Path.Combine(modules, "BannerlordCoop-v0.1.7", "Coop"), "v0.1.7");
            Assert.Empty(CoopInstall.LayoutProblems(game, pin));
            Directory.Delete(Path.Combine(modules, "BannerlordCoop-v0.1.7"), recursive: true);
            Module(Path.Combine(modules, "Coop", "Coop"), "v0.1.7");
            Assert.Equal([CoopInstall.NestedLine], CoopInstall.LayoutProblems(game));
            Assert.Empty(CoopInstall.LayoutProblems(game, pin));
            Directory.Delete(Path.Combine(modules, "Coop"), recursive: true);
            Module(modules, "v0.1.7");
            Assert.Empty(CoopInstall.LayoutProblems(game, pin));
            File.Delete(Path.Combine(modules, "SubModule.xml"));

            // A second copy beside a real Modules\Coop still gets its move out line.
            Module(Path.Combine(modules, "Coop"), pin);
            Module(Path.Combine(modules, "Coop.10"), "v0.1.7");
            Assert.Equal([CoopInstall.DuplicateLine("Coop.10")], CoopInstall.LayoutProblems(game, pin));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.Equal("Coop v0.1.7 in Modules\\Coop.10 is not the Coop 0.1.6 TAOM needs. Open Settings and click Use Workshop Coop 0.1.6. It moves that folder out of Modules, kept, not deleted, so the Workshop Coop loads.",
            CoopStaging.ModulesCoopNotPinned("v0.1.7", "Coop.10"));
        var taom = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot, TaomStack.CoopVersion)", SourceFiles.Method(taom, "private void CheckMods_Click("));
        Assert.Contains("CoopInstall.CheckModulesCoop(_gameRoot, TaomStack.CoopVersion)", SourceFiles.Method(taom, "private async Task ConnectOnceAsync()"));
        Assert.Contains("CoopVersionProblem()", SourceFiles.Method(taom, "private void CheckMods_Click("));
        Assert.Contains("CoopStaging.ModulesCoopNotPinned(identity.Version,", SourceFiles.Method(taom, "private string? CoopVersionProblem()"));
        foreach (var language in Languages)
            Assert.Contains("\"Coop {0} in Modules\\\\{1} is not the Coop {2} TAOM needs.", LanguageFile(language));
    }

    [Fact]
    public void The_compat_lists_name_the_Coop_each_tool_takes()
    {
        // Burning Empires follows the newest Coop; the built in copy and the community list copy stay the same file.
        foreach (var file in new[] { "be-compat/compat/be-mod-compat.json", "src/BurningEmpires.Core/Compat/be-mod-compat.json" })
        {
            using var be = System.Text.Json.JsonDocument.Parse(SourceFiles.Read(file));
            Assert.Equal("v0.1.7", be.RootElement.GetProperty("coop").GetString());
        }
        Assert.Equal("v0.1.7", BurningEmpires.Core.BeCompatList.BuiltIn().Coop);

        // AD1259 takes both. Only the tally reads the field, as text in the MODS.md heading, so one string names both.
        using var ad1259 = System.Text.Json.JsonDocument.Parse(SourceFiles.Read("ad1259-compat/compat/ad1259-mod-compat.json"));
        Assert.Equal("v0.1.7 or v0.1.6", ad1259.RootElement.GetProperty("coop").GetString());
        Assert.Contains("Coop v0.1.7 or v0.1.6)", SourceFiles.Read("ad1259-compat/MODS.md"));
        Assert.Contains("Coop v0.1.7)", SourceFiles.Read("be-compat/MODS.md"));

        foreach (var form in new[] { "ad1259-compat/.github/ISSUE_TEMPLATE/mod-compat.yml", "be-compat/.github/ISSUE_TEMPLATE/mod-compat.yml" })
        {
            var text = SourceFiles.Read(form);
            Assert.Contains("placeholder: \"v0.1.7\"", text);
            Assert.DoesNotContain("v0.1.6", text);
        }
    }

    [Fact]
    public void A_TAOM_war_only_server_gives_the_empty_answer_to_an_add_on_that_asks_for_it()
    {
        var server = SourceFiles.Read("compat/CoodszSettingsSync/src/Server/ServerSync.cs");
        var tick = SourceFiles.Method(server, "public static void Tick(float dt)");
        var warBranch = tick[..tick.IndexOf("if (!_active) return;", StringComparison.Ordinal)];
        Assert.Contains("Safe(\"answer\", () => AnswerNone(none));", warBranch);
        Assert.Contains("WotrServerSync.Tick(dt)", warBranch);
        var none = SourceFiles.Method(server, "private static void AnswerNone(List<NetPeer> peers)");
        Assert.Contains("!wantsNone", none);
        Assert.Contains("network.SendImmediate(peer, new NetworkModSettingsNone", none);
        // The handler lets a war only request through only when it asked for the empty answer, so an older add on gets nothing.
        var handler = SourceFiles.Method(server, "private void HandleRequest(MessagePayload<NetworkModSettingsRequest> payload)");
        Assert.Contains("if (!global::CoodszSettingsSync.ServerSync.Active && !warOnly) return;", handler);
        Assert.Contains("if (warOnly && !payload.What.WantsNoSettingsAnswer) return;", handler);

        // Both shipped server copies are the rebuilt file.
        var coodsz = File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "src", "CoodszHost", "ServerTools", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "CoodszSettingsSync.Coop.dll"));
        var taom = File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", "WarSync", "CoodszSettingsSync.Coop.dll"));
        Assert.Equal(coodsz, taom);
        Assert.Contains("AnswerNone", System.Text.Encoding.UTF8.GetString(coodsz));
    }

    [Fact]
    public void Coodsz_Host_reads_the_crash_line_before_it_hides_a_crashed_Assets_folder()
    {
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var exited = SourceFiles.Method(host, "private void OnServerExited(ServerExitInfo exit)");
        var read = exited.IndexOf("var crashLine = RunCrashLine();", StringComparison.Ordinal);
        Assert.True(read >= 0 && read < exited.IndexOf("HideCrashedAssets(info)", StringComparison.Ordinal));
        Assert.DoesNotContain("FindCrashLine", SourceFiles.Method(host, "private void OfferServerCrashReport("));
    }

    [Fact]
    public void Loose_Coop_DLLs_of_unknown_version_get_no_line_for_TAOM()
    {
        var root = Path.Combine(Path.GetTempPath(), "after136-loose-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var bin = Path.Combine(game, "Modules", "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "Coop.dll"), "dll");
            Assert.Equal([CoopInstall.LooseLine], CoopInstall.LayoutProblems(game));
            Assert.Empty(CoopInstall.LayoutProblems(game, TaomStack.CoopVersion));
            // With a readable SubModule.xml of TAOM's Coop the line comes back.
            Module(Path.Combine(game, "Modules"), TaomStack.CoopVersion);
            Assert.Equal([CoopInstall.LooseLine], CoopInstall.LayoutProblems(game, TaomStack.CoopVersion));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void Module(string folder, string version)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"" + version + "\"/></Module>");
    }

    private static readonly string[] Languages = { "ar", "de", "fr", "pt", "ru", "tr" };

    private static string LanguageFile(string language) =>
        SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
