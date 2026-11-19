using BurningEmpires.Core;
using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat: from the first tick the server keeps branch autosaves on when only the
// save list screen check fails, which it does on every start, and that line never reads as the
// cause of a crash in the server tool (SaveSafetyRules.cs, linked).
public sealed class BurningEmpiresSaveSafetyTests
{
    // Burning Empires' reason on the live server (ds-console-pass2.log), which has no SandBox GUI files.
    private const string PrefabReason = "The Bannerlord SavedGameGroup prefab contract could not be verified: DirectoryNotFoundException: Could not find a part of the path 'C:\\bets\\ds\\engine\\Modules\\SandBox\\GUI\\Prefabs\\SaveLoad\\SavedGameGroup.xml'.";

    [Fact]
    public void Only_the_save_list_screen_checks_keep_autosaves_on()
    {
        Assert.True(SaveSafetyRules.IsScreenOnlyReason(PrefabReason));
        Assert.True(SaveSafetyRules.IsScreenOnlyReason("External enabled UIExtenderEx patches modify SavedGameGroup: ModA -> SavedGameGroup"));
        Assert.False(SaveSafetyRules.IsScreenOnlyReason("The Bannerlord 1.4.7 MBSaveLoad.OverwriteSaveAux signature is not available."));
        Assert.False(SaveSafetyRules.IsScreenOnlyReason("External Prefix/Transpiler patches were found on MBSaveLoad.AutoSaveCurrentGame: x -> y"));
        Assert.False(SaveSafetyRules.IsScreenOnlyReason("Harmony did not report the Burning Empires autosave patch after module initialization."));
        Assert.False(SaveSafetyRules.IsScreenOnlyReason(null));
    }

    [Fact]
    public void The_save_list_line_in_every_server_log_is_never_taken_for_a_crash()
    {
        var note = "[BurningEmpires.CoopCompat] " + SaveSafetyRules.ScreenCheckNote(PrefabReason);
        Assert.Contains("(The Bannerlord SavedGameGroup prefab contract could not be verified)", note);
        Assert.DoesNotContain("Exception", note);
        Assert.DoesNotContain("ERROR", note);

        var log = note + "\n[DedicatedServer] SERVING\n";
        Assert.Null(BeHostReport.CrashLine(log));
        Assert.Null(BeHostReport.Explain(log, wasServing: true, exited: true));

        // The whole reason would be named as the crash, which is why only its start is kept.
        var whole = "[BurningEmpires.CoopCompat] ERROR Save safety: the save list check failed. Reason: " + PrefabReason + "\n";
        Assert.NotNull(BeHostReport.CrashLine(whole));
        Assert.NotNull(BeHostReport.Explain(whole, wasServing: true, exited: true));
    }

    [Fact]
    public void The_autosave_guard_and_the_pop_up_drain_run_from_the_server_start()
    {
        // Coop's session (Coop.OnServer) only starts when the save loads, after Burning Empires'
        // first ticks, so these ask Coop.OnServerProcess.
        var safety = SourceFiles.Read("compat/BurningEmpiresCoopCompat/Persistence/DsSaveSafety.cs");
        Assert.Contains("if (!Coop.OnServerProcess)", SourceFiles.Method(safety, "private static bool DisableForSessionPrefix("));
        Assert.Contains("!Coop.OnServerProcess", SourceFiles.Method(safety, "private static Exception TickAuditFinalizer("));
        var popups = SourceFiles.Read("compat/BurningEmpiresCoopCompat/Headless/DsPopups.cs");
        Assert.Contains("!Coop.OnServerProcess", SourceFiles.Method(popups, "private static bool ErrorPumpPrefix("));

        var coop = SourceFiles.Method(SourceFiles.Read("compat/BurningEmpiresCoopCompat/Coop.cs"), "public static bool OnServerProcess");
        Assert.Contains("FreshCampaign.IsDedicatedServerProcess()", coop);
    }

    [Fact]
    public void A_reason_without_error_text_is_kept_whole()
    {
        var note = SaveSafetyRules.ScreenCheckNote("UIExtenderEx 2.13.3 did not expose the runtime registry required to audit SavedGameGroup extensions.");
        Assert.Contains("(UIExtenderEx 2.13.3 did not expose the runtime registry required to audit SavedGameGroup extensions.)", note);
        Assert.Contains("Branch autosaves stay on", SaveSafetyRules.ScreenCheckNote(null));
    }
}
