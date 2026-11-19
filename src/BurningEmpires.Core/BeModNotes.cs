using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>Notes for extra mods that change what the Burning Empires tools' settings do.</summary>
public static class BeModNotes
{
    public const string BattleSizeResizedId = TaomModNotes.BattleSizeResizedId;

    // BattleSizeResized replaces the game's battle size on each player's game, so the battle size the
    // server tool writes to Coop's mod config (modOptions.battleSize) does not show up in battles.
    public const string BattleSizeResized =
        "BattleSizeResized is in this setup. It overrides the server's battle size on each player's game, so the \"Battle size\" set in the server tool may not apply. Remove it, or turn on \"Override Native Battlesize\" in its MCM settings and set the size there on every player's game.";

    public static IReadOnlyList<string> For(TaomSetup? setup)
    {
        if (setup == null)
            return [];
        var notes = new List<string>();
        if (setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(BattleSizeResizedId, StringComparison.OrdinalIgnoreCase)))
            notes.Add(BattleSizeResized);
        return notes;
    }
}
