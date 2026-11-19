using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BannerlordCoopLauncher.Tests;

// The tooltip pass found buttons that deleted at once: Delete setup in Coodsz Host and Remove on a
// saved cheat command. Both ask first now, and the AD1259 server tool names its own client tool.
public sealed class DeleteConfirmTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), "src", .. parts]));

    // The body of one method, from its signature to the next method.
    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " not found");
        var next = Regex.Match(source[(start + signature.Length)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? source.Substring(start, signature.Length + next.Index) : source[start..];
    }

    [Fact]
    public void Coodsz_host_delete_setup_asks_before_deleting_the_setup_and_its_mcm()
    {
        var body = Method(Source("CoodszHost", "MainWindow.Setups.cs"), "private void DeleteProfile_Click");
        var ask = body.IndexOf("ConfirmWindow.Ask(this, \"Delete setup\"", StringComparison.Ordinal);
        Assert.True(ask >= 0);
        Assert.Contains("This also deletes its MCM settings. This cannot be undone.", body);
        // Nothing is deleted before the answer.
        Assert.True(ask < body.IndexOf("McmCatalog.DeleteSnapshot", StringComparison.Ordinal));
        Assert.True(ask < body.IndexOf("_settings.Profiles.Remove", StringComparison.Ordinal));

        var tip = (string?)XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml")).Descendants()
            .Single(element => (string?)element.Attribute(X + "Name") == "DeleteSetupButton").Attribute("ToolTip");
        Assert.Contains("after you confirm", tip);
        Assert.DoesNotContain("no confirmation", tip);
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher", "MainWindow.Setups.cs", "DeleteSetup_Click")]
    [InlineData("TaomCoopHost", "MainWindow.ModSetups.cs", "DeleteModSetup_Click")]
    [InlineData("TaomCoopLauncher", "MainWindow.Setups.cs", "DeleteSetup_Click")]
    [InlineData("BurningEmpiresHost", "MainWindow.ModSetups.cs", "DeleteModSetup_Click")]
    [InlineData("BurningEmpiresLauncher", "MainWindow.Setups.cs", "DeleteSetup_Click")]
    public void The_other_tools_already_ask_before_delete_setup(string project, string file, string handler)
    {
        var source = Source(project, file);
        var start = source.IndexOf("void " + handler, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = source[start..Math.Min(source.Length, start + 2500)];
        Assert.Contains("\"Delete setup\", MessageBoxButton.OKCancel", body);
    }

    [Fact]
    public void The_map_switch_dialog_and_map_note_name_the_client_tool_of_the_edition()
    {
        var modules = Method(Source("CoodszHost", "MainWindow.Modules.cs"), "private bool ConfirmMapSwitch");
        Assert.Contains("\" is not included in \" + ClientToolName + \".", modules);
        Assert.DoesNotContain("Coodsz Coop Client Launcher", modules);

        var main = Source("CoodszHost", "MainWindow.xaml.cs");
        Assert.Contains("MapSaveNote.Text = \"An existing save keeps the map it was created with. Friends need that same map selected in \" + ClientToolName + \".\";", main);

        var joining = Source("CoodszHost", "MainWindow.Joining.cs");
        Assert.Contains("ClientToolName => Edition.IsAD1259 ? \"Coodsz AD1259 Client Launcher\" : \"Coodsz Coop Client Launcher\"", joining);
    }
}
