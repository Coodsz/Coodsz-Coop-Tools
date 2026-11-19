using System.Windows;
using BannerlordCoopLauncher.Core.Keep;

namespace TaomCoopLauncher;

public partial class MainWindow
{
    /// <summary>
    /// Guides in the sidebar opens the launcher's own help pages as the fifth page, and Back goes to Play.
    /// </summary>
    private void Guides_Click(object sender, RoutedEventArgs e)
    {
        GuidesPage.Content ??= new Coodsz.Ui.TaomGuidesPage(GuideLibrary.For(GuideSet.TaomClient),
            "Short help for joining TAOM with this launcher. Pick a guide, or search.", CloseGuides);
        ShowPage("Guides");
    }

    private void CloseGuides() => ShowPage("Play");
}
