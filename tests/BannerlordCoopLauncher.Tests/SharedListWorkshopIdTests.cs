using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class SharedListWorkshopIdTests
{
    [Fact]
    public void Export_takes_the_stored_id_or_the_number_in_the_Workshop_folder_path()
    {
        Assert.Equal("2859188632", SharedModList.WorkshopIdFor("2859188632", @"C:\Games\Modules\Bannerlord.Harmony"));
        // A setup made with New setup has no stored id; the folder still tells it.
        Assert.Equal("3402516341", SharedModList.WorkshopIdFor("", @"C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\3402516341"));
        Assert.Equal("3402516341", SharedModList.WorkshopIdFor("0", @"D:/SteamLibrary/steamapps/workshop/content/261550/3402516341/SettlementCultureDrift"));
        Assert.Equal("", SharedModList.WorkshopIdFor(null, @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Native"));
        Assert.Equal("", SharedModList.WorkshopIdFor(null, null));
    }

    [Fact]
    public void Server_export_finds_Workshop_ids_by_module_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "workshop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = Path.Combine(root, "3402516341", "SettlementCultureDrift");
            Directory.CreateDirectory(item);
            File.WriteAllText(Path.Combine(item, "SubModule.xml"),
                "<Module><Name value=\"Settlement Culture Drift\"/><Id value=\"SettlementCultureDrift\"/><Version value=\"v1.1.4\"/></Module>");
            Directory.CreateDirectory(Path.Combine(root, "not-a-number"));

            var ids = SharedModList.WorkshopIdsIn([root, Path.Combine(root, "missing")]);

            Assert.Equal("3402516341", ids["settlementculturedrift"]);
            Assert.Single(ids);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
