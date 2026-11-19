namespace BannerlordCoopLauncher.Tests;

// A first host saw the empty step 2 save box, went off to make a single player save, and got lost.
// The guide also numbered Prepare server 2 while the tool calls it step 3. Now the empty box says what
// to do, and the guide uses the tool's own step numbers.
public sealed class BeHostStepsTests
{
    private const string Empty = "No campaign yet. Type a name in step 1 and click New campaign. You don't need a single player save.";

    [Fact]
    public void An_empty_save_box_with_no_new_campaign_named_says_to_use_step_1()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"SaveEmpty\"", xaml);
        Assert.Contains("Text=\"" + Empty + "\"", xaml);
        var detail = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost"), "private void ShowSaveDetail()");
        Assert.Contains("SaveEmpty.Visibility = SaveBox.Items.Count == 0 && newCampaign.Length == 0 ? Visibility.Visible : Visibility.Collapsed;", detail);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            Assert.True(json.Contains("\"" + Empty + "\":", StringComparison.Ordinal), language);
        }
    }

    [Fact]
    public void The_host_guide_uses_the_tool_step_numbers()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.xaml"));
        // The tool's Saves page: 1 new campaign, 2 pick that save, 3 set up the host once (Prepare server). Start server is
        // on the Dashboard since redesign wave 6.
        Assert.Contains("Text=\"Set up the host once\"", xaml);
        var guide = SourceFiles.Read("Coodsz-BurningEmpires-Instructions.txt");
        var host = guide[guide.IndexOf("PART 2, host", StringComparison.Ordinal)..guide.IndexOf("PART 3", StringComparison.Ordinal)];
        Assert.Contains("start with step **3, Set up the host once**, and click **Prepare server**", host);
        Assert.Contains("In step **1**, type a name and click **New campaign**", host);
        Assert.Contains("you don't need a single player save", host);
        Assert.Contains("pick it in step **2** instead", host);
        Assert.Contains("On the **Dashboard**, click **Start server**", host);
        Assert.Contains("The **Saves** page has numbered steps.", host);
    }
}
