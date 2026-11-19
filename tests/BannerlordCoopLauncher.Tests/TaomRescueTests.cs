using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomRescueTests
{
    private const string ServerWindowsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Module>
          <Name value="DedicatedServer.Windows"/>
          <Id value="DedicatedServer.Windows"/>
          <SubModules>
            <SubModule>
              <Name value="DedicatedServer.Windows"/>
              <DLLName value="DedicatedServer.Windows.dll"/>
              <SubModuleClassType value="DedicatedServer.Windows.SubModule"/>
            </SubModule>
          </SubModules>
        </Module>
        """;

    [Fact]
    public void Rescue_rides_in_DedicatedServer_Windows_on_every_start_once_and_drops_a_stray_mode_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "taomrescue-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = RescueSubModule.ModuleDirectory(root);
            Directory.CreateDirectory(module);
            var xmlPath = Path.Combine(module, "SubModule.xml");
            File.WriteAllText(xmlPath, ServerWindowsXml.Replace("\n", "\r\n"));
            var dll = Path.Combine(root, "CoopSaveRescue.dll");
            File.WriteAllText(dll, "dll");

            RescueSubModule.Install(root, dll);
            RescueSubModule.Install(root, dll); // twice adds it once
            var installed = File.ReadAllText(xmlPath);
            Assert.True(RescueSubModule.IsInstalled(root));
            Assert.Equal(1, installed.Split("Coop Save Rescue").Length - 1);
            Assert.Contains("<SubModuleClassType value=\"CoopSaveRescue.SubModule\"/>", installed);
            Assert.True(installed.IndexOf("Coop Save Rescue", StringComparison.Ordinal) < installed.IndexOf("</SubModules>", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(module, "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll")));

            // A CoopSaveRescue.mode file from the plain Coop zip would turn on every fix on every start.
            var mode = Path.Combine(module, "bin", "Win64_Shipping_Server", "CoopSaveRescue.mode");
            File.WriteAllText(mode, "full");
            File.WriteAllText(dll, "dll v2");
            RescueSubModule.Install(root, dll);
            Assert.False(File.Exists(mode));
            Assert.Equal("dll v2", File.ReadAllText(Path.Combine(module, "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll")));
            Assert.Equal(installed, File.ReadAllText(xmlPath));

            Assert.Equal("nothing to fix was found.", RescueSubModule.ReportText("12:00  [CoopSaveRescue] Report: nothing to fix was found."));
            Assert.Throws<InvalidOperationException>(() => RescueSubModule.Install(root, Path.Combine(root, "missing.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
