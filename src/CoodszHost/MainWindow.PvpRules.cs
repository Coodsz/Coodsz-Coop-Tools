using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;

namespace CoodszHost;

// PvP rules on the Settings page (both builds, like Max clans per kingdom; the AD1259 server
// reads the same coodsz-server-tools.json). Save settings writes them to coodsz-server-tools.json, and the module reads them
// within a few seconds (compat\CoodszServerTools\src\PvpRules.cs). Both switches are off by default.
public partial class MainWindow
{
    private static string PvpRulesPath => ClanCapFile.PathIn(ServerSession.DocumentsData);

    // Called once from the constructor.
    private void StartPvpRules()
    {
        PvpRulesGroup.Visibility = Visibility.Visible;
        LoadPvpRules();
    }

    private void LoadPvpRules()
    {
        var rules = PvpRulesFile.Read(PvpRulesPath);
        PvpBlockLeaveBox.IsChecked = rules.BlockLeave;
        PvpTimerBox.IsChecked = rules.ResponseTimer;
        PvpSecondsBox.Text = rules.Seconds.ToString();
    }

    // Checked before Save settings writes anything, so a bad number changes no file.
    private HostPvpRules PvpRulesFromPage() =>
        new(PvpBlockLeaveBox.IsChecked == true, PvpTimerBox.IsChecked == true, PvpRulesFile.ParseBox(PvpSecondsBox.Text));

    private void SavePvpRules(HostPvpRules rules)
    {
        var before = PvpRulesFile.Read(PvpRulesPath);
        PvpSecondsBox.Text = rules.Seconds.ToString();
        if (rules == before && File.Exists(PvpRulesPath) && File.ReadAllText(PvpRulesPath).Contains(PvpRulesFile.BlockLeaveKey, StringComparison.Ordinal))
            return;
        PvpRulesFile.Write(PvpRulesPath, rules);
        if (rules.BlockLeave != before.BlockLeave)
            Append(rules.BlockLeave
                ? "Players at war can't leave when an enemy player catches them. The server uses it within a few seconds."
                : "Players can leave player battles again, like Coop on its own.");
        if (rules.ResponseTimer != before.ResponseTimer || (rules.ResponseTimer && rules.Seconds != before.Seconds))
            Append(rules.ResponseTimer
                ? "Player encounters with no answer for " + rules.Seconds + " seconds now end, with a battle if the two are at war. The server uses it within a few seconds."
                : "Player encounters wait for an answer as long as it takes, like Coop on its own.");
    }
}
