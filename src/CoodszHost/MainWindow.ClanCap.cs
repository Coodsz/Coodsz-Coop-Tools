using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.Ui;
using CoodszHost.Services;

namespace CoodszHost;

// Max clans per kingdom on the Settings page (both builds, since both ship
// Coodsz Server Tools and share the file). Save settings writes coodsz-server-tools.json
// next to server-config.json, and the module enforces it on the server (compat\CoodszServerTools).
public partial class MainWindow
{
    private static string ClanCapPath => ClanCapFile.PathIn(ServerSession.DocumentsData);

    // Called once from the constructor.
    private void StartClanCap()
    {
        ClanCapGroup.Visibility = Visibility.Visible;
        // The clan cap is the one setting on this page the running server picks up without a restart.
        SettingsSaveDetail.Text = "Save settings writes this page. Max clans per kingdom applies within a few seconds. The running server keeps the other values until you restart it.";
        NewBadges.Attach(ClanCapHeading, NewFeatures.ClanCap, BadgePlace.After);
        ClanCapBox.GotKeyboardFocus += (_, _) => NewBadges.Seen(NewFeatures.ClanCap);
        ClanCapMercenariesBox.Click += (_, _) => NewBadges.Seen(NewFeatures.ClanCap);
        LoadClanCap();
    }

    private void LoadClanCap()
    {
        var cap = ClanCapFile.Read(ClanCapPath);
        ClanCapBox.Text = cap.MaxClans.ToString();
        ClanCapMercenariesBox.IsChecked = cap.CountMercenaries;
    }

    // Checked before Save settings writes anything, so a bad number changes no file.
    private ClanCap ClanCapFromPage() =>
        new(ClanCapFile.ParseBox(ClanCapBox.Text), ClanCapMercenariesBox.IsChecked == true);

    private void SaveClanCap(ClanCap cap)
    {
        var before = ClanCapFile.Read(ClanCapPath);
        if (cap == before && File.Exists(ClanCapPath))
            return;
        ClanCapFile.Write(ClanCapPath, cap);
        ClanCapBox.Text = cap.MaxClans.ToString();
        Append(!cap.Enabled
            ? "Max clans per kingdom is off, so kingdoms have no limit."
            : cap.CountMercenaries
                ? "Max clans per kingdom is now " + cap.MaxClans + ", mercenaries included. The server uses it within a few seconds."
                : "Max clans per kingdom is now " + cap.MaxClans + ". The server uses it within a few seconds.");
    }
}
