using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using Coodsz.Ui;

namespace BannerlordCoopLauncher;

public partial class MainWindow
{
    // Fix antivirus in the log tools menu. It lists this PC's folders and opens Windows Security, nothing more.
    private void FixAntivirus_Click(object sender, RoutedEventArgs e) =>
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            null, _gameRoot, ReadWorkshops().Select(library => Path.Combine(library.ContentDirectory, AntivirusFiles.CoopWorkshopId)).FirstOrDefault(Directory.Exists)));
}
