using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomModNotesTests
{
    [Fact]
    public void BattleSizeResized_in_the_setup_gets_the_battle_size_note()
    {
        var setup = new TaomSetup { Name = "Test" };
        setup.Extras.Add(new TaomExtraMod { Name = "BattleSizeResized", ModuleId = "BattleSizeResized", Enabled = true });

        var notes = TaomModNotes.For(setup);

        Assert.Equal([TaomModNotes.BattleSizeResized], notes);
        Assert.Contains("Override Native Battlesize", notes[0]);
    }

    [Fact]
    public void A_turned_off_or_missing_BattleSizeResized_gets_no_note()
    {
        var setup = new TaomSetup { Name = "Test" };
        Assert.Empty(TaomModNotes.For(setup));
        Assert.Empty(TaomModNotes.For(null));

        setup.Extras.Add(new TaomExtraMod { Name = "BattleSizeResized", ModuleId = "BattleSizeResized", Enabled = false });
        Assert.Empty(TaomModNotes.For(setup));
    }
}
