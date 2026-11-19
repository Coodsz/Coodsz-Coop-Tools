using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class SaveModCardTests
{
    private static readonly string[] SaveMods =
    {
        "Native", "SandBoxCore", "Sandbox", "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.MBOptionScreen", "Coop", "ImprovedGarrisons", "XorberaxLegacy"
    };

    private static readonly string[] ServerOn = { "Native", "SandBoxCore", "Sandbox", "Bannerlord.Harmony", "Coop" };

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bannerlord.ButterLib"] = "ButterLib",
        ["Bannerlord.MBOptionScreen"] = "Mod Configuration Menu v5",
        ["ImprovedGarrisons"] = "  Improved Garrisons  ",
    };

    private static string? NameOf(string id) => Names.TryGetValue(id, out var name) ? name : null;

    private static SaveModCard Card(Func<string, bool> installed, IEnumerable<SaveModSetup>? setups = null, bool canTurnOn = true, bool canAddMods = true,
        string saveName = "Calradia", bool locked = false)
    {
        var change = SaveModChange.Check(SaveMods, ServerOn);
        Assert.Equal(SaveModAction.Refuse, change.Action);
        return SaveModCard.Build(saveName, SaveMods, change, NameOf, installed, setups ?? Array.Empty<SaveModSetup>(), canTurnOn, canAddMods, locked);
    }

    [Fact]
    public void Names_come_from_the_mod_list_or_fall_back_to_the_id()
    {
        Assert.Equal("Improved Garrisons", SaveModCard.FriendlyName("ImprovedGarrisons", NameOf));
        Assert.Equal("XorberaxLegacy", SaveModCard.FriendlyName("XorberaxLegacy", NameOf));
        Assert.Equal("Coop", SaveModCard.FriendlyName("Coop", _ => "   "));

        var card = Card(_ => true);
        Assert.Equal(new[] { "ButterLib", "Mod Configuration Menu v5", "Improved Garrisons", "XorberaxLegacy" }, card.Mods.Select(chip => chip.Name));
        // the hover shows the module id when the name is different
        Assert.Equal("Bannerlord.ButterLib", card.Mods[0].Tip);
        Assert.Equal("", card.Mods[3].Tip);
    }

    [Fact]
    public void The_title_has_the_count_and_the_text_stays_short()
    {
        var card = Card(_ => true);
        Assert.Equal("This save needs 4 mods that are turned off", card.Title);
        Assert.Equal("Starting without them can break the campaign.", card.Why);
        Assert.Equal("", card.Note);

        var one = SaveModCard.Build("Calradia", SaveMods, SaveModChange.Check(SaveMods, SaveMods.Where(id => id != "XorberaxLegacy")),
            NameOf, _ => true, Array.Empty<SaveModSetup>(), true, true);
        Assert.Equal("This save needs a mod that is turned off", one.Title);
        Assert.Equal("Starting without it can break the campaign.", one.Why);
        Assert.Equal("Turn this mod back on", one.FixButton);
    }

    [Fact]
    public void A_setup_made_for_this_save_is_the_main_action()
    {
        var setups = new[]
        {
            new SaveModSetup("Other world", "Other", ServerOn),
            new SaveModSetup("My campaign", "calradia", SaveMods.Append("BattleInstinct").ToList()),
        };
        var card = Card(_ => true, setups);
        Assert.Equal(SaveModFix.UseSetup, card.Fix);
        Assert.Equal("My campaign", card.SetupName);
        Assert.Equal("Use the setup this save was made with", card.FixButton);
        Assert.Contains("My campaign", card.FixTip);

        // another save's setup counts only when it loads exactly the save's mods
        var exact = Card(_ => true, new[] { new SaveModSetup("Copy", "Elsewhere", SaveMods) });
        Assert.Equal(SaveModFix.UseSetup, exact.Fix);
        var more = Card(_ => true, new[] { new SaveModSetup("Bigger", "Elsewhere", SaveMods.Append("BattleInstinct").ToList()) });
        Assert.Equal(SaveModFix.TurnOn, more.Fix);
    }

    [Fact]
    public void The_setup_made_for_this_save_wins_over_one_that_only_has_the_same_mods()
    {
        var setups = new[]
        {
            new SaveModSetup("Other save", "Elsewhere", SaveMods),
            new SaveModSetup("No save", "", SaveMods),
            new SaveModSetup("Mine", "Calradia", SaveMods),
        };
        Assert.Equal("Mine", SaveModCard.MatchingSetup("Calradia", SaveMods, setups)?.Name);
        // Without one made for it, a setup with no save of its own comes before another save's setup.
        Assert.Equal("No save", SaveModCard.MatchingSetup("Calradia", SaveMods, setups.Take(2))?.Name);
        Assert.Equal("Other save", SaveModCard.MatchingSetup("Calradia", SaveMods, setups.Take(1))?.Name);
    }

    [Fact]
    public void Use_the_setup_starts_only_the_cards_save_on_that_setup_and_never_a_new_campaign()
    {
        Assert.Null(SaveModCard.UseSetupBlock("Calradia", "Mine", "Mine", "calradia", newCampaign: false));
        Assert.Equal("Could not switch to Mine. Pick it in the setup list, then click Start server.",
            SaveModCard.UseSetupBlock("Calradia", "Mine", "Default", "Calradia", false));
        Assert.Equal("Switched to Mine. Pick Calradia in the save list, then click Start server.",
            SaveModCard.UseSetupBlock("Calradia", "Mine", "Mine", "Other", false));
        Assert.NotNull(SaveModCard.UseSetupBlock("Calradia", "Mine", "Mine", null, false));
        Assert.Equal("Mine is set to start a new campaign, so the server was not started. Pick Calradia in the save list, then click Start server.",
            SaveModCard.UseSetupBlock("Calradia", "Mine", "Mine", "Calradia", true));

        // Coodsz Host checks it before Start_Click.
        var host = SourceFiles.Read("src/CoodszHost/MainWindow.SaveMods.cs");
        var check = host.IndexOf("SaveModCard.UseSetupBlock(", StringComparison.Ordinal);
        Assert.True(check > 0);
        Assert.True(check < host.IndexOf("Start_Click(this, new RoutedEventArgs());", check, StringComparison.Ordinal));
    }

    [Fact]
    public void Installed_mods_turn_back_on_and_missing_ones_offer_add_mods()
    {
        var card = Card(_ => true);
        Assert.Equal(SaveModFix.TurnOn, card.Fix);
        Assert.Equal("Turn these mods back on", card.FixButton);

        var missing = Card(id => id != "XorberaxLegacy" && id != "ImprovedGarrisons");
        Assert.Equal(SaveModFix.AddMods, missing.Fix);
        Assert.Equal("Add the missing mods", missing.FixButton);
        Assert.Equal("2 of these are not on the server yet.", missing.Note);
        // the mods the server lacks come first, so +N more never hides them
        Assert.Equal(new[] { "ImprovedGarrisons", "XorberaxLegacy" }, missing.Mods.Take(2).Select(chip => chip.Id));
        Assert.Contains("Not on the server yet.", missing.Mods[0].Tip);

        Assert.Equal("One of these is not on the server yet.", Card(id => id != "XorberaxLegacy").Note);
        Assert.Equal("None of these are on the server yet.", Card(_ => false).Note);
    }

    [Fact]
    public void A_locked_setup_or_a_tool_without_the_path_has_no_main_button()
    {
        var locked = Card(_ => true, canTurnOn: false, canAddMods: false, locked: true);
        Assert.Equal(SaveModFix.None, locked.Fix);
        Assert.Equal("", locked.FixButton);
        Assert.Contains("locked", locked.Note);

        // the TAOM tool cannot add mods here, so it says which are missing and keeps Start anyway
        var taom = Card(id => id != "XorberaxLegacy", canTurnOn: false, canAddMods: false);
        Assert.Equal(SaveModFix.None, taom.Fix);
        Assert.Equal("One of these is not on the server yet.", taom.Note);
    }

    [Fact]
    public void More_than_eight_mods_fold_under_a_more_toggle()
    {
        var many = Enumerable.Range(1, 20).Select(i => "Mod" + i).ToList();
        var save = ServerOn.Concat(many).ToList();
        var card = SaveModCard.Build("Calradia", save, SaveModChange.Check(save, ServerOn), _ => null, _ => true, Array.Empty<SaveModSetup>(), true, true);
        Assert.Equal(20, card.Count);
        Assert.True(card.HasMore);
        Assert.Equal(SaveModCard.ChipLimit, card.Shown(false).Count);
        Assert.Equal(12, card.MoreCount(false));
        Assert.Equal("+12 more", card.MoreButton(false));
        Assert.Equal(20, card.Shown(true).Count);
        Assert.Equal(0, card.MoreCount(true));
        Assert.Equal("Show less", card.MoreButton(true));

        // eight or fewer show without a toggle
        var few = Card(_ => true);
        Assert.False(few.HasMore);
        Assert.Equal(few.Count, few.Shown(false).Count);
        Assert.Equal(0, few.MoreCount(false));
    }

    [Fact]
    public void The_host_tools_show_the_card_and_keep_the_full_list_in_the_log()
    {
        var host = SourceFiles.Read("src/CoodszHost/MainWindow.ServerControl.cs");
        Assert.Contains("ShowSaveModCard(BuildSaveModCard(saveName, info, change));", host);
        Assert.Contains("Append(change.RefuseText);", host);
        Assert.DoesNotContain("throw new InvalidOperationException(change.RefuseText);", host);
        Assert.Contains("<ui:SaveModsCard x:Name=\"ServerSaveMods\"", SourceFiles.Read("src/CoodszHost/MainWindow.xaml"));
        var taom = SourceFiles.Read("src/TaomCoopHost/MainWindow.ModSetups.cs");
        Assert.Contains("SaveModsWindow.Ask(this, card)", taom);
        Assert.Contains("SaveModsCard.xaml", SourceFiles.Read("src/TaomCoopHost/TaomCoopHost.csproj"));
    }
}
