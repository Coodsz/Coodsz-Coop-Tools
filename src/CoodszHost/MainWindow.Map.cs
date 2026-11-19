using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Keep;

namespace CoodszHost;

// The CoopKeep style live map (Coodsz Host only; the AD1259 build has no map). It reads where
// everything is through the Coodsz Server Tools module and never changes the game; Show on map in
// the player profile and the Encyclopedia opens it centred on a party or settlement.
public partial class MainWindow
{
    /// <summary>Called once from StartKeepPages.</summary>
    private void StartMap()
    {
        if (KeepContext.Current is not { } keep)
            return;
        KeepMap.Attach(keep, () => _session.IsRunning && !_engineExited);
        KeepMap.StartRequested += () => Start_Click(this, new System.Windows.RoutedEventArgs());
        KeepMap.ProfileRequested += (heroId, name) => _ = OpenHeroProfile(keep, heroId, name);
        KeepMap.EncyclopediaRequested += (section, id) =>
        {
            ShowPage("Encyclopedia");
            KeepEncyclopedia.OpenEntry(section, id);
        };
        keep.ShowOnMap = ShowHeroOnMap;
        KeepEncyclopedia.MapRequested += (section, id, name) =>
        {
            if (section == EncSection.Settlements)
            {
                ShowPage("Map");
                KeepMap.FocusSettlement(id, name);
            }
            else
                ShowHeroOnMap(id, name);
        };
    }

    /// <summary>Show on map: the Map page, centred on the hero's party.</summary>
    private void ShowHeroOnMap(string heroId, string name)
    {
        ShowPage("Map");
        KeepMap.FocusHero(heroId, name);
        Activate();
    }
}
