using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class CoopDifficultyTests
{
    private const string Config = """
        {
          // Campaign difficulty, applied by the HOSTING side at every boot.
          "difficulty": {
            // VeryEasy: -75% Damage
            "playerReceivedDamage": "VeryEasy",
            "combatAIDifficulty": "Easy",
            "battleDeath": "Easy",
            "birthAndDeath": false,
          },
          "modOptions": {
            "fastForwardEnabled": true,
          }
        }
        """;

    [Fact]
    public void Reads_and_writes_levels_in_the_difficulty_block_and_keeps_comments()
    {
        Assert.Equal("VeryEasy", CoopDifficulty.Read(Config, "playerReceivedDamage"));
        Assert.Equal("Easy", CoopDifficulty.Read(Config, "combatAIDifficulty"));
        Assert.Null(CoopDifficulty.Read(Config, "persuasionSuccessChance"));

        var json = Config;
        Assert.True(CoopDifficulty.TrySet(ref json, "playerReceivedDamage", "Realistic"));
        Assert.True(CoopDifficulty.TrySet(ref json, "combatAIDifficulty", "Realistic"));
        Assert.Equal("Realistic", CoopDifficulty.Read(json, "playerReceivedDamage"));
        Assert.Equal("Realistic", CoopDifficulty.Read(json, "combatAIDifficulty"));
        Assert.Contains("// VeryEasy: -75% Damage", json);
        Assert.Contains("\"fastForwardEnabled\": true", json);

        // A missing setting is not added outside the block; the file stays as it is.
        var before = json;
        Assert.False(CoopDifficulty.TrySet(ref json, "persuasionSuccessChance", "Easy"));
        Assert.Equal(before, json);
        Assert.Throws<ArgumentException>(() => CoopDifficulty.TrySet(ref json, "battleDeath", "Hard"));
    }

    [Fact]
    public void Every_setting_is_one_of_Coops_difficulty_keys()
    {
        Assert.Equal(9, CoopDifficulty.Settings.Count);
        Assert.Equal(CoopDifficulty.Settings.Count, CoopDifficulty.Settings.Select(setting => setting.Key).Distinct().Count());
        Assert.Equal("Very easy", CoopDifficulty.LevelName("VeryEasy"));
        Assert.Equal("Realistic", CoopDifficulty.LevelName("Realistic"));
    }
}
