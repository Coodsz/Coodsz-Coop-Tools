using System.Windows;
using BannerlordCoopLauncher.Core.Keep;

namespace BurningEmpiresLauncher;

public partial class MainWindow
{
    /// <summary>
    /// Guides in the sidebar opens the launcher's own help pages as the fifth page, and Back goes to Play.
    /// </summary>
    private void Guides_Click(object sender, RoutedEventArgs e)
    {
        GuidesPage.Content ??= new Coodsz.Ui.TaomGuidesPage(GuideLibrary.For(GuideSet.BeClient),
            "Short help for joining Burning Empires with this launcher. Pick a guide, or search.", CloseGuides);
        ShowPage("Guides");
    }

    private void CloseGuides() => ShowPage("Play");
}
