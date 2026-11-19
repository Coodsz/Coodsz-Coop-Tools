using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Tests;

// Each tool has its own Coodsz banner icon (assets\icons, made from the SVGs by tools\MakeIcons):
// red for the Coop server tool, blue for the Coop client launcher, green for TAOM, purple for AD1259, orange for Burning Empires and black
// with a red edge and a red crown for Realm of Thrones,
// with a lighter shade on each Client Launcher so it is told apart from its Server Tool.
// Coodsz and Friends is the client launcher's More Nations switch now.
public class ToolIconTests
{
    [Theory]
    [InlineData("src/CoodszHost/CoodszHost.csproj", "coodsz-coop-server")]
    [InlineData("src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj", "coodsz-coop")]
    [InlineData("src/TaomCoopHost/TaomCoopHost.csproj", "coodsz-taom")]
    [InlineData("src/TaomCoopLauncher/TaomCoopLauncher.csproj", "coodsz-taom-client")]
    [InlineData("src/AD1259Host/AD1259Host.csproj", "coodsz-ad1259")]
    [InlineData("src/AD1259Launcher/AD1259Launcher.csproj", "coodsz-ad1259-client")]
    [InlineData("src/BurningEmpiresHost/BurningEmpiresHost.csproj", "coodsz-be")]
    [InlineData("src/BurningEmpiresLauncher/BurningEmpiresLauncher.csproj", "coodsz-be-client")]
    [InlineData("src/RealmOfThronesHost/RealmOfThronesHost.csproj", "coodsz-rot")]
    [InlineData("src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj", "coodsz-rot-client")]
    public void Each_tool_uses_its_banner_icon(string project, string icon)
    {
        var match = Regex.Match(SourceFiles.Read(project), "<ApplicationIcon>(?<path>[^<]+)</ApplicationIcon>");

        Assert.True(match.Success, project + " has no ApplicationIcon.");
        Assert.Equal(@"..\..\assets\icons\" + icon + ".ico", match.Groups["path"].Value);
    }

    [Theory]
    [InlineData("coodsz-coop-server", "#791F1F")]
    [InlineData("coodsz-coop", "#0C447C")]
    [InlineData("coodsz-taom", "#085041")]
    [InlineData("coodsz-ad1259", "#3C3489")]
    [InlineData("coodsz-taom-client", "#0F6E56")]
    [InlineData("coodsz-ad1259-client", "#534AB7")]
    [InlineData("coodsz-be", "#D35400")]
    [InlineData("coodsz-be-client", "#E67E22")]
    [InlineData("coodsz-rot", "#141416")]
    [InlineData("coodsz-rot-client", "#2E1D1F")]
    public void Each_icon_has_its_svg_and_four_png_sizes(string icon, string colour)
    {
        Assert.Contains("fill=\"" + colour + "\"", SourceFiles.Read("assets/icons/" + icon + ".svg"));

        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "assets", "icons", icon + ".ico"));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i, 16);
            var size = entry[0] == 0 ? 256 : entry[0];
            Assert.Equal(entry[0], entry[1]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.True(offset + length <= bytes.Length);
            // Each image is a PNG of that size.
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[offset..(offset + 4)]);
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 16)));
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 20)));
            sizes.Add(size);
        }

        Assert.Equal([16, 32, 48, 256], sizes);
    }

    [Fact]
    public void The_icon_tool_simplifies_the_16_px_image()
    {
        var tool = SourceFiles.Read("tools/MakeIcons/Program.cs");
        Assert.Contains("public static readonly int[] Sizes = [16, 32, 48, 256];", tool);
        Assert.Contains("if (small && shape.RodPart)", tool);
        Assert.Contains("Geometry.Parse(", tool);
        Assert.DoesNotContain("PackageReference", SourceFiles.Read("tools/MakeIcons/MakeIcons.csproj"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
