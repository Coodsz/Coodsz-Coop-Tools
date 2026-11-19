using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Keep;

namespace CoodszHost;

// The CoopKeep style Encyclopedia and Guides pages (Coodsz Host only; the AD1259 build has neither).
// The Encyclopedia reads the running world through the Coodsz Server Tools module and never changes it;
// its buttons open the existing player profile, Cheats page and Items window.
public partial class MainWindow
{
    /// <summary>Called once from StartKeepPages.</summary>
    private void StartEncyclopedia()
    {
        if (KeepContext.Current is not { } keep)
            return;
        KeepEncyclopedia.Attach(keep, () => _session.IsRunning && !_engineExited);
        KeepEncyclopedia.StartRequested += () => Start_Click(this, new System.Windows.RoutedEventArgs());
        KeepEncyclopedia.ProfileRequested += (heroId, name) => _ = OpenHeroProfile(keep, heroId, name);
        KeepEncyclopedia.CheatsRequested += (heroId, name) => _ = OpenHeroInCheats(keep, heroId, name);
        KeepEncyclopedia.ItemsRequested = OpenItemsFor;
    }

    /// <summary>A player hero of the Encyclopedia: the Cheats page's row for it (with its SteamID64), or one made from the id.</summary>
    private static async Task<KeepTarget> HeroTarget(KeepContext keep, string heroId, string name)
    {
        var players = await keep.LoadPlayersAsync();
        return new KeepTarget(players.FirstOrDefault(p => p.HeroId == heroId) ?? new KeepPlayer(name, heroId, "", false, null));
    }

    private async Task OpenHeroProfile(KeepContext keep, string heroId, string name) =>
        new PlayerProfileWindow(keep, await HeroTarget(keep, heroId, name)).Show();

    private async Task OpenHeroInCheats(KeepContext keep, string heroId, string name)
    {
        var target = await HeroTarget(keep, heroId, name);
        ShowPage("Cheats");
        KeepCheats.Pick(target);
    }

    /// <summary>Give with the Items window: opens it for the players picked on the Cheats page, with the item searched.</summary>
    private string OpenItemsFor(string itemName)
    {
        if (KeepContext.Current is not { } keep)
            return "";
        var targets = KeepCheats.PickedTargets;
        if (targets.Count == 0)
            return "Pick who gets it on the Cheats page first, then press this again.";
        new ItemsWindow(keep, targets, itemName).Show();
        return "";
    }
}
