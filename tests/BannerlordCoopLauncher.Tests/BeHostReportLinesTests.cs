namespace BurningEmpires.Core.Tests;

public class BeHostReportLinesTests
{
    [Fact]
    public void PatchReadyIsTheHealthyLine()
    {
        var log = "[BurningEmpires.CoopCompat] Recruit deduction: hooked Coop's recruitment commit.\n[BurningEmpires.CoopCompat] Burning Empires coop patch ready (125 hooks).\n";
        Assert.Equal(125, BeHostReport.PatchReady(log));
        Assert.Equal("Burning Empires Coop Compat is ready (125 hooks).", BeHostReport.ReadyLine(125));
        Assert.Null(BeHostReport.PatchReady("[DedicatedServer] SERVING"));
        Assert.Null(BeHostReport.Explain(log, wasServing: false, exited: false));
    }

    [Fact]
    public void TheCompatFailingToHookBurningEmpiresIsReportedWhileTheServerRuns()
    {
        var log = "[BurningEmpires.CoopCompat] Burning Empires coop patch could not hook Burning Empires: Method not found.\n";
        var report = BeHostReport.Explain(log, wasServing: false, exited: false);
        Assert.NotNull(report);
        Assert.True(report.Value.Failed);
        Assert.Contains(BeServerPath.BeVersion, report.Value.Lines[0]);

        // One method that could not be hooked leaves the rest of the patch on.
        Assert.Null(BeHostReport.Explain("[BurningEmpires.CoopCompat] Could not hook Burning Empires BEGov.Tick: gone\n", wasServing: false, exited: false));
    }

    [Fact]
    public void ASetupStepThatFailedIsAWarningNotAStop()
    {
        var log = "[BurningEmpires.CoopCompat] TimeFlowSync could not be set up: System.NullReferenceException: Object reference\n";
        var report = BeHostReport.Explain(log, wasServing: true, exited: false);
        Assert.NotNull(report);
        Assert.False(report.Value.Failed);
        Assert.Contains("TimeFlowSync", report.Value.Status);
    }

    [Fact]
    public void TheSaveSafetyLinesGetAPlainExplanation()
    {
        var blocked = "[BurningEmpires.CoopCompat] ERROR Save safety: Burning Empires could not restore this save's data (load failed) and runs on empty data. The server will not write Burning Empires data until a good save is loaded.\n";
        var report = BeHostReport.Explain(blocked, wasServing: true, exited: false);
        Assert.NotNull(report);
        Assert.True(report.Value.Failed);
        Assert.Contains("your Burning Empires save data", report.Value.Lines[0]);

        var migration = "[BurningEmpires.CoopCompat] Save safety: this Burning Empires build migrates older saves (2). A migrated save blocks saving until a player saves it under a new name.\n";
        var warning = BeHostReport.Explain(migration, wasServing: false, exited: false);
        Assert.NotNull(warning);
        Assert.False(warning.Value.Failed);
        Assert.Contains("new name", warning.Value.Status);
    }

    [Fact]
    public void AMissingServerDllNamesTheServerBinCopies()
    {
        var log = "System.IO.FileNotFoundException: Could not load file or assembly 'Bannerlord.UIExtenderEx, Version=2.13.3.0, Culture=neutral'.\n[BE host] engine exited with code 2\n";
        var report = BeHostReport.Explain(log, wasServing: false);
        Assert.NotNull(report);
        Assert.Contains("Bannerlord.UIExtenderEx", report.Value.Status);
        Assert.Contains("Win64_Shipping_Server", report.Value.Lines[0]);
        Assert.Null(BeHostReport.Explain(log, wasServing: false, exited: false));
    }

    [Fact]
    public void AWrongOrderFromTheScriptsBannerIsReported()
    {
        var log = "Active load order:\n   1. BurningEmpires\n   2. Bannerlord.Harmony\n   3. Native\n\nStarting the native engine.\n[BE host] engine exited with code 4\n";
        var report = BeHostReport.Explain(log, wasServing: false);
        Assert.NotNull(report);
        Assert.Contains("order", report.Value.Status);

        var good = "Active load order:\n" + string.Join("\n", BeServerFixtures.ServerOrder.Select((id, i) => "  " + (i + 1) + ". " + id)) + "\n[BE host] engine exited with code 0\n";
        Assert.DoesNotContain("order", BeHostReport.Explain(good, wasServing: true)!.Value.Status);
    }

    [Fact]
    public void ASaveTheScriptRefusedIsExplained()
    {
        var report = BeHostReport.Explain("Save 'Vanilla' was not found or is incompatible with the selected module set.\n", wasServing: false);
        Assert.NotNull(report);
        Assert.Contains("New campaign", report.Value.Status);
    }

    [Fact]
    public void NoTaomWordsInTheReports()
    {
        var logs = new[]
        {
            "[BurningEmpires.CoopCompat] Burning Empires coop patch could not hook Burning Empires: x\n",
            "Save 'x' was not found or is incompatible\n",
            "BurningEmpires could not be loaded correctly\n",
            "BurningEmpires depends on Bannerlord.ButterLib\n",
            "module BurningEmpires is already loaded\n",
            "System.NullReferenceException at BurningEmpires.SubModule\n[BE host] engine exited with code 3\n",
            "[BE host] engine exited with code 0\n"
        };
        foreach (var log in logs)
        {
            var report = BeHostReport.Explain(log, wasServing: true);
            Assert.NotNull(report);
            Assert.DoesNotContain("TAOM", string.Join(" ", report.Value.Lines) + report.Value.Status);
        }
    }
}
