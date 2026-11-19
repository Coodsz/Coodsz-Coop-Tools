namespace TaomCoop.Core;

/// <summary>Notes for extra mods that change what the TAOM tools' settings do.</summary>
public static class TaomModNotes
{
    public const string BattleSizeResizedId = "BattleSizeResized";

    // BattleSizeResized replaces the game's battle size on each player's game: TAOM.CoopCompat applies the
    // server's size through the top battle size slot, which BattleSizeResized turns into 2040 (or its own
    // MCM size when "Override Native Battlesize" is on). So the server tool's battle size never shows up
    // in battles, and big battles hit the engine's agent limit.
    public const string BattleSizeResized =
        "BattleSizeResized is in this setup. It overrides the server's battle size on each player's game, so the \"Field battle size\" set in the server tool does not apply. Remove it, or turn on \"Override Native Battlesize\" in its MCM settings and set the size there on every player's game.";

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
