using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// The tool follow ups from the player crash reports: raw Assets folders on the server, CoopModPatch's
// first time setup stop, exit code 1 with no error, and Diplomacy without Diplomacy.CoopCompat.
public sealed class CrashReportFollowupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-followups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // The end of the engine log of a server that died on RaiseYourBanner's raw Assets folder.
    private const string AssetsCrashLog =
        "[20:14:48.233] Unable to find item to add dependency(depender walk_forward_unarmed)\r\n"
        + "[20:14:48.256] Initializing items\r\n"
        + "[20:14:48.284] Loading packages $BASE/Modules/DedicatedServer.Windows/DsAssetPackages...\r\n"
        + "[20:14:48.294] Registering items...\r\n"
        + "[20:14:48.301] Initializing items\r\n"
        + "[20:14:48.304] Loading packages $BASE/Modules/RaiseYourBanner/Assets...\r\n"
        + "[20:14:48.309] Registering items...\r\n"
        + "[20:14:48.309] Initializing items\r\n";

    [Fact]
    public void The_hidden_assets_list_has_the_built_in_mods_and_the_compat_list_ones()
    {
        Assert.Contains("BannerFix", ServerRawAssets.Builtin);
        Assert.Contains("RaiseYourBanner", ServerRawAssets.Builtin);
        Assert.Contains("RaiseYourTorch", ServerRawAssets.Builtin);
        Assert.Contains("AD1259", ServerRawAssets.Builtin);
        Assert.Contains("swadian armoury", ServerRawAssets.Builtin);
        Assert.Contains("ArtemCore", ServerRawAssets.Builtin);

        var list = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "OldNote", "status": "Works", "note": "Library. Server copy: remove the mod's Assets folder. Source: thread." },
              { "id": "SomeGuess", "status": "Works", "note": "Server copy: remove the mod's Assets folder.", "idGuess": true },
              { "id": "NewField", "status": "Works", "note": "Players keep theirs.", "serverHideAssets": true },
              { "id": "FieldOff", "status": "Works", "serverHideAssets": false },
              { "id": "Other", "status": "Works", "note": "Works fine." }
            ] }
            """);
        var ids = ServerRawAssets.Ids(list);
        // A list from before the field still works through the note's words.
        Assert.Contains("OldNote", ids);
        // A guessed id only matches a folder or a SubModule.xml id with that exact name.
        Assert.Contains("SomeGuess", ids);
        Assert.Contains("NewField", ids);
        Assert.DoesNotContain("FieldOff", ids);
        Assert.DoesNotContain("Other", ids);
        // The list built into the tools sets the field for these four. ArtemCore also keeps the note words, since
        // older tools don't carry it in their own list; the other three are in every tool's list.
        var embedded = ModCompatDatabase.Embedded();
        foreach (var id in new[] { "BannerFix", "RaiseYourBanner", "RaiseYourTorch", "ArtemCore" })
            Assert.True(embedded.All.Single(e => e.Id == id).ServerHideAssets, id);
        Assert.Contains(ServerRawAssets.NoteMarker, embedded.All.Single(e => e.Id == "ArtemCore").Note);
        Assert.Contains("ArtemCore", ServerRawAssets.Ids(embedded));
    }

    [Fact]
    public void Raw_assets_are_hidden_in_the_server_copy_only()
    {
        var modules = Path.Combine(_directory, "DedicatedServer", "engine", "Modules");
        Directory.CreateDirectory(Path.Combine(modules, "RaiseYourBanner", "Assets"));
        Directory.CreateDirectory(Path.Combine(modules, "BannerFix", "Assets"));
        Directory.CreateDirectory(Path.Combine(modules, "Native", "Assets"));
        Directory.CreateDirectory(Path.Combine(modules, "SomeMod", "Assets"));
        // Found by its SubModule.xml id when the folder has another name.
        Directory.CreateDirectory(Path.Combine(modules, "Raise Your Torch", "Assets"));
        File.WriteAllText(Path.Combine(modules, "Raise Your Torch", "SubModule.xml"), "<Module><Id value=\"RaiseYourTorch\"/></Module>");
        // A stale hidden copy from an older version goes.
        Directory.CreateDirectory(Path.Combine(modules, "BannerFix", ServerRawAssets.HiddenName, "old"));

        var hidden = ServerRawAssets.Hide(modules, ServerRawAssets.Ids());
        Assert.Equal(["BannerFix", "Raise Your Torch", "RaiseYourBanner"], hidden.OrderBy(id => id, StringComparer.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(modules, "RaiseYourBanner", ServerRawAssets.HiddenName)));
        Assert.False(Directory.Exists(Path.Combine(modules, "BannerFix", ServerRawAssets.HiddenName, "old")));
        Assert.True(Directory.Exists(Path.Combine(modules, "Native", "Assets")));
        Assert.True(Directory.Exists(Path.Combine(modules, "SomeMod", "Assets")));
        Assert.Contains("in the server copy (renamed to Assets.off)", ServerRawAssets.Note(hidden));
        Assert.Empty(ServerRawAssets.Hide(modules, ServerRawAssets.Ids()));
        Assert.Null(ServerRawAssets.Note([]));
        // The crash path hides the one mod the engine log named, never an official module.
        Assert.Equal(["SomeMod"], ServerRawAssets.Hide(modules, ["SomeMod"]));
        Assert.Empty(ServerRawAssets.Hide(modules, ["Native"]));
    }

    [Fact]
    public void An_access_violation_on_a_raw_assets_folder_blames_the_mod_not_the_save()
    {
        Assert.Equal("RaiseYourBanner", ServerCrashExplain.FindAssetsCrash(ServerCrashExplain.AccessViolationCode, AssetsCrashLog));
        // Another exit code, a log that went on after the packages, or the official packages are not this.
        Assert.Null(ServerCrashExplain.FindAssetsCrash(1, AssetsCrashLog));
        Assert.Null(ServerCrashExplain.FindAssetsCrash(ServerCrashExplain.AccessViolationCode, AssetsCrashLog + "[20:14:50.000] Loading Save Game\r\n"));
        Assert.Null(ServerCrashExplain.FindAssetsCrash(ServerCrashExplain.AccessViolationCode,
            "[20:14:48.284] Loading packages $BASE/Modules/DedicatedServer.Windows/DsAssetPackages...\r\n[20:14:48.294] Registering items...\r\n"));
        Assert.Null(ServerCrashExplain.FindAssetsCrash(ServerCrashExplain.AccessViolationCode, null));

        var lines = ServerCrashExplain.Explain("Server process exited with code -1073741819.", false, "", AssetsCrashLog, [], ["Coop"]);
        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("The Assets folder of RaiseYourBanner crashed the server while it was loading packages"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Press Start server again") && line.Contains("Assets.off"));
        Assert.DoesNotContain(lines, line => line.Contains("could not load the campaign save"));
    }

    [Fact]
    public void CoopModPatch_first_time_setup_is_not_a_crash()
    {
        const string console = "[CoopModPatch]   CoopModPatch first-time setup complete\n"
            + "[CoopModPatch]   Registered for dedicated-server loading: SettlementCultureDrift\n"
            + "[CoopModPatch]   The server was stopped intentionally; start it again to load them.\n";
        Assert.True(ModPatchSetup.Finished(1, console));
        Assert.False(ModPatchSetup.Finished(0, console));
        Assert.False(ModPatchSetup.Finished(1, "[CoopModPatch]   CoopModPatch first-time setup complete"));

        var watch = new ModPatchSetup.Watch();
        foreach (var line in console.Split('\n'))
            watch.Observe(line);
        Assert.True(watch.Finished(1));
        Assert.False(watch.Finished(-1073741819));
        watch.Reset();
        Assert.False(watch.Finished(1));

        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", false, console, null, [], ["Coop"]);
        // crash-signatures.json gives the answer now; Setup finished stays the What to do.
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Setup finished. Press Start server again."));
    }

    [Fact]
    public void Exit_code_1_with_no_error_points_outside_the_server()
    {
        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", true, "DedicatedServer   pulse: players=1 parties=1517", "[03:12:00.000] Saving...", [], ["Coop"]);
        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("exit code 1 and no error") && line.Contains("closed from outside") && line.Contains("Windows Insider"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("antivirus"));
        Assert.DoesNotContain(lines, line => line.Contains("Nothing in the log says a mod broke"));
        Assert.Contains(ServerCrashExplain.SilentExitLines(false), line => line.Contains("before it was ready for players"));

        // An error in the log is not a silent exit.
        var withError = ServerCrashExplain.Explain("Server process exited with code 1.", false, "System.NullReferenceException: Object reference not set", null, [], ["Coop"]);
        Assert.DoesNotContain(withError, line => line.Contains("closed from outside"));
        Assert.True(ServerCrashExplain.HasErrorLine("Unhandled exception in the main loop"));
        Assert.False(ServerCrashExplain.HasErrorLine("Failed to get network handle for object"));
    }

    [Fact]
    public void The_event_log_keeps_server_entries_around_the_exit()
    {
        var exitAt = new DateTime(2026, 10, 24, 1, 31, 50);
        var entries = new[]
        {
            new EventLogEntryInfo(exitAt.AddMinutes(-10), "Application Error", 1000, "Error", "Faulting application name: dotnet.exe"),
            new EventLogEntryInfo(exitAt.AddSeconds(5), "Windows Error Reporting", 1001, "Information", "P1: dotnet.exe"),
            new EventLogEntryInfo(exitAt.AddSeconds(-30), "Application Error", 1000, "Error", "Faulting application name: BannerlordCoopServer.exe"),
            new EventLogEntryInfo(exitAt, "Some Service", 7, "Information", "Started chrome.exe"),
            new EventLogEntryInfo(exitAt.AddMinutes(2), ".NET Runtime", 1026, "Error", "Application: dotnet.exe")
        };
        var picked = ServerEventLog.Select(entries, exitAt);
        Assert.Equal(2, picked.Count);
        Assert.Equal("Application Error", picked[0].Source);
        var text = ServerEventLog.Format(picked, exitAt);
        Assert.NotNull(text);
        Assert.Contains("P1: dotnet.exe", text);
        Assert.Null(ServerEventLog.Format([], exitAt));

        var log = string.Join("\n", Enumerable.Range(1, 250).Select(i => "line " + i));
        var tail = ServerEventLog.Tail(log)!.Split(Environment.NewLine);
        Assert.Equal(200, tail.Length);
        Assert.Equal("line 51", tail[0]);
        Assert.Equal("line 250", tail[^1]);
        Assert.Null(ServerEventLog.Tail(""));
    }

    [Fact]
    public void Diplomacy_without_its_coop_compat_gets_a_warning()
    {
        Assert.Equal(DiplomacyCompatRule.Warning, DiplomacyCompatRule.WarningFor(["Coop", "Bannerlord.Diplomacy"]));
        Assert.Null(DiplomacyCompatRule.WarningFor(["Coop", "bannerlord.diplomacy", "Diplomacy.CoopCompat"]));
        Assert.Null(DiplomacyCompatRule.WarningFor(["Coop"]));
        Assert.Equal("Diplomacy needs Diplomacy Coop Compat in co op. Without it messengers and some buttons break the game.", DiplomacyCompatRule.Warning);
    }
}
