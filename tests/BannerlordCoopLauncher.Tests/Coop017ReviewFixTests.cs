using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Launch;
using CoodszHost.Services;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// The review findings on the Coop 0.1.7 prep (Claude, Codex and Devin): TAOM's newer Coop wording on Start and
// Connect, Coop 0.1.7's join refusals, the smithing default per Coop version and the installed Coop in reports.
public sealed class Coop017ReviewFixTests
{
    private const string SmithingId = "modOptions.smithingStaminaRecoveryMultiplier";

    [Fact]
    public void Taom_start_with_a_newer_workshop_coop_gives_the_coop_016_server_advice_instead_of_a_verify()
    {
        var root = NewRoot("taom-newer-coop");
        try
        {
            // The prepared server's Coop was swapped, and the Workshop server copy is Coop 0.1.7 now.
            var serverCoop = Module(Path.Combine(root, "server", "engine", "Modules"), "Coop", "Coop", "v0.1.6");
            Write(Path.Combine(serverCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "not the pinned file");
            var workshopCoop = Module(Path.Combine(root, "workshop", "DedicatedServer", "engine", "Modules"), "Coop", "Coop", "v0.1.7");
            Write(Path.Combine(workshopCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop 0.1.7");

            var start = Assert.Throws<InvalidOperationException>(() => CoopStaging.EnsurePinnedServerBin(serverCoop, workshopCoop));
            // The host also reads where to get the Coop 0.1.6 server (KitCoopServer).
            Assert.Equal(KitCoopServer.WithWhereToGet(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true)), start.Message);
            // No client zip advice for a server.
            Assert.DoesNotContain("BannerlordCoop-v0.1.6.zip", start.Message);
            Assert.DoesNotContain("verify", start.Message, StringComparison.OrdinalIgnoreCase);

            // Prepare server's check of the Workshop server Coop.Core.dll says the same.
            var prepare = Assert.Throws<InvalidOperationException>(() =>
                CoopStaging.RestoreQualifiedDll(Path.Combine(workshopCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll")));
            Assert.Equal(KitCoopServer.WithWhereToGet(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true)), prepare.Message);

            // An older Workshop Coop still says to verify.
            Module(Path.Combine(root, "workshop", "DedicatedServer", "engine", "Modules"), "Coop", "Coop", "v0.1.5");
            Assert.Contains(CoopStaging.NoPlaytestServerCoop,
                Assert.Throws<InvalidOperationException>(() => CoopStaging.EnsurePinnedServerBin(serverCoop, workshopCoop)).Message);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Taom_connect_gives_a_wrong_coop_the_tools_update_wording()
    {
        var connect = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private async Task ConnectOnceAsync()");
        Assert.Contains("CoopVersionProblem()", connect);
        var check = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private string? CoopVersionProblem()");
        Assert.Contains("CoopStaging.WrongWorkshopCoop(identity.Id, identity.Version)", check);
        // With the Workshop on Coop 0.1.7 too, Use Workshop Coop 0.1.6 stops on the Workshop's version, so a Modules Coop
        // gets the Workshop's line first instead of advice to click that button.
        var workshopFirst = check.IndexOf("CoopStaging.NewerCoopProblem(CoopStaging.FindWorkshopModule(", StringComparison.Ordinal);
        Assert.True(workshopFirst > 0);
        Assert.True(workshopFirst < check.IndexOf("CoopStaging.ModulesCoopNotPinned(", StringComparison.Ordinal));
        Assert.DoesNotContain("Use Workshop Coop", CoopParking.DontAskTip);
    }

    [Fact]
    public void The_wrong_workshop_coop_texts_are_whole_translated_keys()
    {
        // tools/ExtractUiText reads each message as one expression, so the three whole texts are the keys.
        string[] keys =
        [
            "The Workshop Coop is {0} {1}. TAOM.CoopCompat {2} needs Coop {3}. The Coop 0.1.6 client from Coop's GitHub v0.1.6 release works. Get BannerlordCoop-v0.1.6.zip there and unzip it into the game's Modules folder, so SubModule.xml sits in Modules\\Coop. If a Modules\\Coop folder is already there, move it out first. Then try again.",
            "The Workshop Coop is {0} {1}. TAOM.CoopCompat {2} needs Coop {3}. Wait for Steam to update the Workshop item, or in Steam unsubscribe from Workshop item 3770450698 and subscribe again.",
            "The Workshop Coop is {0} {1}. TAOM.CoopCompat {2} needs Coop {3}, and Steam now has that newer Coop."
        ];
        Assert.Equal(string.Format(keys[0], "Coop", "v0.1.7", "0.3.25", "0.1.6"), CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7"));
        Assert.EndsWith(keys[1][keys[1].IndexOf(" Wait for Steam", StringComparison.Ordinal)..], CoopStaging.WrongWorkshopCoop("Coop", "v0.1.5"));
        Assert.Equal(string.Format(keys[2], "Coop", "v0.1.7", "0.3.25", "0.1.6"), CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true));
        Assert.Equal(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.5"), CoopStaging.WrongWorkshopCoop("Coop", "v0.1.5", server: true));
        var source = SourceFiles.Read(Path.Combine("src", "TaomCoop.Core", "CoopStaging.cs"));
        Assert.DoesNotContain("var head", source);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            foreach (var key in keys)
                Assert.True(table.ContainsKey(key), language + " is missing: " + key);
        }
    }

    [Fact]
    public void Coop_017_lists_every_required_mod_and_the_bridge_line_counts_on_any_of_them()
    {
        var lines = JoinResultExplain.Explain("Module validation failed:\n"
            + "To join the server the module 'ButterLib' (Bannerlord.ButterLib) with version 'v2.12.0' is required.\n"
            + "To join the server the module 'PS Coop Bridge' (PSCoopBridge) with version 'v0.17.6' is required.");
        Assert.Contains(lines, line => line.Contains("updates Coodsz Coop Server Tool", StringComparison.Ordinal));
    }

    [Fact]
    public void Coop_017_unsupported_module_lines_are_matched_by_their_id()
    {
        var lines = JoinResultExplain.Explain("Module validation failed!\nReason: Server does not support module 'PS Coop Bridge' (PSCoopBridge).");
        Assert.Contains(lines, line => line.StartsWith("What not to do: Do not leave PSCoopBridge on", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("What not to do: Do not turn those mods off on the client", StringComparison.Ordinal));
    }

    [Fact]
    public void Coop_017_ids_with_spaces_are_read()
    {
        var lines = JoinResultExplain.Explain("Module validation failed! Reason: Wrong version of module 'Swadian Armoury' (swadian armoury) detected. Server uses 'v1.2.0', client uses 'v1.1.0'.");
        Assert.Contains(lines, line => line.StartsWith("Swadian Armoury does not match the server. The server is v1.2.0.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_smithing_default_follows_the_servers_coop_version()
    {
        Assert.Equal(0.1, ModSettingsCatalog.Defaults()[SmithingId]);
        Assert.Equal(0.1, ModSettingsCatalog.Defaults("v0.1.6")[SmithingId]);
        Assert.Equal(0.6, ModSettingsCatalog.Defaults("v0.1.7")[SmithingId]);
        Assert.Equal(0.6, ModSettingsCatalog.Defaults("0.1.8")[SmithingId]);

        // A file without the key reads as that server's default; a key in the file stays as it is.
        Assert.Equal(0.1, ModConfigStore.ReadText("{ \"modOptions\": { } }")[SmithingId]);
        Assert.Equal(0.6, ModConfigStore.ReadText("{ \"modOptions\": { } }", "v0.1.7")[SmithingId]);
        Assert.Equal(0.1, ModConfigStore.ReadText("{ \"modOptions\": { \"smithingStaminaRecoveryMultiplier\": 0.1 } }", "v0.1.7")[SmithingId]);
        Assert.Equal(0.6, new NewWorldPlan(null, "v0.1.7").Values[SmithingId]);
        Assert.Equal(0.1, new NewWorldPlan().Values[SmithingId]);

        // Coodsz Host passes its server's Coop version; the TAOM and Burning Empires tools stay on Coop 0.1.6's.
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains(".Read(ServerCoopVersion())", host);
        Assert.Contains("new NewWorldPlan(CoopSettingsFor(null, out _), ServerCoopVersion())", host);
    }

    [Fact]
    public void Ad1259_refresh_never_copies_a_player_coop_into_the_server()
    {
        var root = NewRoot("ad1259-coop-refresh");
        try
        {
            var game = Path.Combine(root, "game", "Modules");
            Module(game, "Coop", "Coop", "v0.1.7");
            var server = Path.Combine(root, "server");
            var rows = AD1259ToolsTests.CompleteServerRows();
            rows[rows.FindIndex(row => row.Id == "Coop")] = new ModuleRow { Id = "Coop", Version = "v0.1.5" };

            var refreshed = AD1259Server.RefreshWrongVersions(server, rows, game, []);

            Assert.Empty(refreshed);
            Assert.False(Directory.Exists(Path.Combine(ModuleCatalog.ModulesRoot(server), "Coop")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Reports_send_the_coop_this_pc_or_server_has()
    {
        var ad1259 = SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher");
        Assert.DoesNotContain("mod.Id == \"Coop\")?.Version", ad1259);
        Assert.Contains("Coop = AD1259Stack.Find(\"Coop\") is { } coop ? InstalledVersion(coop) : \"\",", ad1259);
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("Coop = ModuleVersion(\"Coop\") is { Length: > 0 } coop ? coop", host);

        var coop = AD1259Stack.Find("Coop")!;
        var ready = new AD1259ModStatus(coop, AD1259ModState.Ready, "v0.1.6", ["Workshop 3770450698"], [@"C:\w\3770450698"]);
        Assert.StartsWith("v0.1.6 ", ready.RowDetail);
    }
}
