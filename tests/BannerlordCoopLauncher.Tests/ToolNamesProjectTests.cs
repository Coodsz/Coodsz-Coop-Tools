using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// The exe names the updater and the rename cleanup expect (ToolNames) must be the names the projects
// really build. A tool built under another name never renames itself (ToolRename checks its own
// assembly name), so a typo here would leave every player on the old exe.
public class ToolNamesProjectTests
{
    [Theory]
    [InlineData("src/CoodszHost/CoodszHost.csproj", "Coop", ToolRole.Server)]
    [InlineData("src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj", "Coop", ToolRole.Client)]
    [InlineData("src/TaomCoopHost/TaomCoopHost.csproj", "TAOM", ToolRole.Server)]
    [InlineData("src/TaomCoopLauncher/TaomCoopLauncher.csproj", "TAOM", ToolRole.Client)]
    [InlineData("src/AD1259Host/AD1259Host.csproj", "AD1259", ToolRole.Server)]
    [InlineData("src/AD1259Launcher/AD1259Launcher.csproj", "AD1259", ToolRole.Client)]
    [InlineData("src/BurningEmpiresHost/BurningEmpiresHost.csproj", "BurningEmpires", ToolRole.Server)]
    [InlineData("src/BurningEmpiresLauncher/BurningEmpiresLauncher.csproj", "BurningEmpires", ToolRole.Client)]
    [InlineData("src/RealmOfThronesHost/RealmOfThronesHost.csproj", "RealmOfThrones", ToolRole.Server)]
    [InlineData("src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj", "RealmOfThrones", ToolRole.Client)]
    public void Each_new_exe_name_is_the_assembly_name_of_its_project(string project, string product, ToolRole role)
    {
        var tool = ToolNames.All.Single(item => item.Product == product && item.Role == role);
        var assemblyName = Regex.Match(SourceFiles.Read(project), "<AssemblyName>(?<name>[^<]+)</AssemblyName>").Groups["name"].Value;

        Assert.Equal(tool.NewExeName, assemblyName + ".exe");
    }

    [Fact]
    public void Every_tool_row_has_a_project()
    {
        Assert.Equal(10, ToolNames.All.Count);
        Assert.Equal(10, ToolNames.All.Select(tool => (tool.Product, tool.Role)).Distinct().Count());
    }

    // One client build: the Coodsz and Friends build and its compile time switch are gone, so every
    // More Nations check reads the runtime switch.
    [Fact]
    public void The_client_launcher_is_the_only_client_build()
    {
        var root = SourceFiles.Root();
        Assert.False(File.Exists(Path.Combine(root, "src", "BannerlordCoopLauncher", "BannerlordCoopLauncher.csproj")));
        Assert.DoesNotContain("VANILLA_MAP", SourceFiles.Read("src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj"));
        Assert.DoesNotContain("BannerlordCoopLauncher.csproj", SourceFiles.Read("BannerlordCoopLauncher.sln"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "BannerlordCoopLauncher"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("LauncherBrand.IncludeMoreNations", text);
            Assert.DoesNotContain("ToolData.MoreNationsClient :", text);
            Assert.DoesNotContain("#if VANILLA_MAP", text);
        }
    }
}
