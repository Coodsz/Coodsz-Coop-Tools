using System.Reflection;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// Windows and antivirus tools show the publisher, copyright and description from each exe. A blank
// publisher on an unsigned exe looks like malware, so every tool exe names Coodsz.
public class ExeMetadataTests
{
    [Fact]
    public void Every_project_under_src_names_Coodsz_and_keeps_the_plain_version()
    {
        var props = XDocument.Parse(SourceFiles.Read(Path.Combine("src", "Directory.Build.props")));

        Assert.Equal("Coodsz", Property(props, "Company"));
        Assert.Equal("Copyright (c) 2026 Coodsz", Property(props, "Copyright"));
        Assert.Equal("false", Property(props, "IncludeSourceRevisionInInformationalVersion"));
    }

    [Theory]
    [InlineData("CoodszHost")]
    [InlineData("CoodszCoopAndFriends")]
    [InlineData("TaomCoopHost")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Host")]
    [InlineData("AD1259Launcher")]
    [InlineData("BurningEmpiresHost")]
    [InlineData("BurningEmpiresLauncher")]
    public void Every_tool_exe_has_a_product_version_and_description(string project)
    {
        var csproj = XDocument.Parse(SourceFiles.Read(Path.Combine("src", project, project + ".csproj")));
        var name = Property(csproj, "AssemblyName");

        Assert.StartsWith("Coodsz ", name);
        Assert.Equal(name, Property(csproj, "Product"));
        Assert.Equal(name, Property(csproj, "AssemblyTitle"));
        Assert.Matches(@"^\d+\.\d+\.\d+$", Property(csproj, "Version"));
        Assert.StartsWith(name + ": ", Property(csproj, "Description"));
        // The shared props set these, so a tool never overrides them with something else.
        Assert.Null(Property(csproj, "Company"));
        Assert.Null(Property(csproj, "Copyright"));
        Assert.Null(Property(csproj, "IncludeSourceRevisionInInformationalVersion"));
    }

    [Fact]
    public void A_built_assembly_carries_the_publisher_and_a_version_without_a_commit()
    {
        var assembly = typeof(ToolUpdate).Assembly;

        Assert.Equal("Coodsz", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal("Copyright (c) 2026 Coodsz", assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Assert.DoesNotContain("+", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "+");
    }

    private static string? Property(XDocument document, string name) =>
        document.Root?.Elements("PropertyGroup").Elements(name).Select(element => element.Value).FirstOrDefault();
}
