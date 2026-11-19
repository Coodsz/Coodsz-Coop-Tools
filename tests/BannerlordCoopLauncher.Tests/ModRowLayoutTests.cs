using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

// Coodsz's screenshot of the Coop Server Tool's Mods page (2026-10-31): rows without Delete had their chips, arrows and
// switch about 60 px further right, two line rows were taller and one row had no chip. Every tool's mod row now uses
// the fixed slots of ModRowLayout.
public class ModRowLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XDocument Xaml(string project) => XDocument.Load(Path.Combine(SourceFiles.Root(), "src", project, "MainWindow.xaml"));

    // Review of e87173db: a move to the edge turned off the arrow the focus went back to, so the focus left the list and
    // Alt+Up and Alt+Down stopped working.
    [Theory]
    [InlineData(true, true, true, MoveFocusTarget.Up)]
    [InlineData(false, true, true, MoveFocusTarget.Down)]
    [InlineData(true, false, true, MoveFocusTarget.Down)]
    [InlineData(false, true, false, MoveFocusTarget.Up)]
    [InlineData(true, false, false, MoveFocusTarget.Row)]
    [InlineData(false, false, false, MoveFocusTarget.Row)]
    public void A_move_to_the_edge_keeps_the_focus_on_an_arrow_that_works(bool movedUp, bool canUp, bool canDown, MoveFocusTarget expected) =>
        Assert.Equal(expected, MoveFocus.After(movedUp, canUp, canDown));

    [Fact]
    public void The_coop_client_puts_the_focus_back_by_the_edge_rule()
    {
        var move = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BannerlordCoopLauncher"), "private void MoveRow(ModuleRow? row, int delta)");
        Assert.Contains("Core.Modules.MoveFocus.After(delta < 0, buttons.UpButton.IsEnabled, buttons.DownButton.IsEnabled)", move);
        Assert.Contains("container.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));", move);
        Assert.DoesNotContain("(delta < 0 ? buttons.UpButton : buttons.DownButton).Focus();", move);
    }

    [Fact]
    public void An_official_module_row_says_what_it_is_instead_of_no_chip()
    {
        var chip = ModChips.Role("CustomBattle", alwaysOn: false);
        Assert.NotNull(chip);
        Assert.Equal(ModChips.Official, chip!.Text);
        Assert.Equal(ModChipKind.Mute, chip.Kind);
        // Always on, Coodsz module and Bundled still come first, and a plain mod still gets no role chip.
        Assert.Equal(ModChips.AlwaysOn, ModChips.Role("Native", alwaysOn: true)!.Text);
        Assert.Null(ModChips.Role("ImprovedGarrisons", alwaysOn: false));
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var entries = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            Assert.True(entries.ContainsKey(ModChips.Official), language);
            Assert.True(entries.ContainsKey(ModChips.OfficialTip), language);
        }
    }

    [Fact]
    public void The_server_tool_rows_keep_the_delete_slot_and_one_height_and_never_look_selected()
    {
        var xaml = Xaml("CoodszHost");
        var list = xaml.Descendants(Wpf + "ListBox").Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ModuleList");
        Assert.Equal("{StaticResource Keep.ModRowItem}", (string?)list.Attribute("ItemContainerStyle"));
        var delete = list.Descendants(Wpf + "Button").Single(element => (string?)element.Attribute("Click") == "DeleteModule_Click");
        var hidden = delete.Descendants(Wpf + "Setter").First(setter => (string?)setter.Attribute("Property") == "Visibility");
        Assert.Equal("Hidden", (string?)hidden.Attribute("Value"));
        var edge = list.Descendants(Wpf + "Border").First(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "RowEdge");
        Assert.Equal("{x:Static ui:ModRowLayout.RowHeight}", (string?)edge.Attribute("MinHeight"));

        // The shared item style: a quiet hover, the dashed ring only for keyboard focus, and no selected look.
        var theme = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "KeepTheme.xaml"));
        var style = theme[theme.IndexOf("x:Key=\"Keep.ModRowItem\"", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.DoesNotContain("IsSelected", style);
        Assert.Contains("{StaticResource Keep.ModRowFocus}", style);
        Assert.Contains("IsMouseOver", style);
    }

    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void The_order_list_rows_use_fixed_columns(string project)
    {
        var source = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var row = source[source.IndexOf("<ListBox x:Name=\"OrderList\"", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</ListBox>", StringComparison.Ordinal)];
        Assert.Contains("Width=\"{x:Static ui:ModRowLayout.SwitchColumn}\"", row);
        Assert.Contains("Width=\"{x:Static ui:ModRowLayout.ChipColumn}\"", row);
        Assert.Contains("Width=\"{x:Static ui:ModRowLayout.EditButtonsColumn}\"", row);
        Assert.DoesNotContain("DockPanel.Dock=\"Right\"", row);
        var style = source[source.IndexOf("<Style x:Key=\"ModuleRow\"", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Contains("{x:Static ui:ModRowLayout.RowHeight}", style);
        Assert.Contains("{StaticResource Keep.ModRowFocus}", style);
        Assert.DoesNotContain("IsSelected", style);
    }

    [Theory]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    public void A_client_core_row_keeps_the_switch_and_button_slots_of_an_extra_row(string project)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var core = SourceFiles.Method(window, "private Border CoreRow(");
        Assert.Contains("((Panel)chips).Children.Insert(0, locked);", core);
        Assert.Contains("Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.ClientActionColumn)", core);
        Assert.Contains("Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.SwitchColumn)", core);
        Assert.Contains("Coodsz.Ui.ModRowLayout.FixHeight(border);", core);
        var extra = SourceFiles.Method(window, "private Border ExtraRow(");
        Assert.Contains("Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.ClientActionColumn, buttons)", extra);
        Assert.Contains("Coodsz.Ui.ModRowLayout.Slot(Coodsz.Ui.ModRowLayout.SwitchColumn, toggle)", extra);
        Assert.Contains("Coodsz.Ui.ModRowLayout.FixHeight(border);", extra);
    }

    [Fact]
    public void The_ad1259_and_coop_client_rows_keep_their_slots()
    {
        var ad = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "AD1259Launcher"), "private Border ModuleRow(");
        Assert.Contains("Coodsz.Ui.ModRowLayout.Slot(GetItColumn, get)", ad);
        Assert.Contains("Width = Coodsz.Ui.ModRowLayout.ClientChipColumn", ad);
        Assert.Contains("if (status.Mod.BaseGame)", ad);
        Assert.Contains("Coodsz.Ui.ModRowLayout.FixHeight(border);", ad);

        var coop = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.xaml"));
        Assert.Contains("MinHeight=\"{x:Static ui:ModRowLayout.RowHeight}\"", coop);
        Assert.Contains("x:Name=\"RemoveButton\" Content=\"Remove\" Visibility=\"Hidden\"", coop);
    }

    // Review of 90ef25c5: the name's own tooltip hid the row's tip (a TAOM or Burning Empires core row's "always loads").
    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void The_row_tip_starts_with_the_name_and_the_name_has_no_tooltip_of_its_own(string project)
    {
        var source = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var row = source[source.IndexOf("<ListBox x:Name=\"OrderList\"", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</ListBox>", StringComparison.Ordinal)];
        Assert.Contains("ToolTip=\"{Binding RowTip}\"", row);
        Assert.DoesNotContain("ToolTip=\"{Binding Name}\"", row);
        var code = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml.cs"));
        Assert.Contains("public string RowTip => Tip == null ? Name : Name + \"\\n\" + BannerlordCoopLauncher.Core.Localization.Translator.T(Tip);", code);
    }

    // The status pill and the state word say Server online; only lines that quote the server's own console word keep SERVING.
    [Fact]
    public void Player_facing_state_words_say_server_online()
    {
        foreach (var file in new[] { "src/CoodszHost/MainWindow.xaml.cs", "src/TaomCoopHost/MainWindow.State.cs", "src/BurningEmpiresHost/MainWindow.State.cs" })
        {
            var code = SourceFiles.Read(file);
            Assert.DoesNotContain("\"SERVING\"", code);
            Assert.Contains("\"SERVER ONLINE\"", code);
        }

        var lobby = SourceFiles.Read("src/BannerlordCoopLauncher.Core/Support/SteamLobbyLine.cs");
        Assert.Contains("It comes up a few seconds after the status says Server online.", lobby);
        Assert.DoesNotContain("a few seconds after SERVING", lobby);
        Assert.Contains("If the status says Server online, click Send my logs", SourceFiles.Read("src/CoodszHost/Services/ServerSession.cs"));
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
            Assert.True(BannerlordCoopLauncher.Core.Localization.Translator.Entries(language).ContainsKey("SERVER ONLINE"), language);
    }
}
