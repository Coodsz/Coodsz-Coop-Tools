using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;

namespace BannerlordCoopLauncher.Tests;

public class CampaignSaveInfoTests
{
    [Fact]
    public void A_save_header_names_the_character_the_day_and_the_mods()
    {
        var header = "xxxx{\"List\":{\"Modules\":\"Native;Coop\",\"CharacterName\":\"Ada\",\"MainHeroLevel\":\"4\",\"DayLong\":\"12\",\"MainHeroVisual\":\"key=\\\"001\\\"\"}}junk";
        var info = CampaignSaveInfoReader.ReadHeader(header);
        Assert.NotNull(info);
        Assert.Equal("Ada", info!.Character);
        Assert.Equal("4", info.Level);
        Assert.Equal("12", info.Day);
        Assert.Equal(new[] { "Native", "Coop" }, info.Modules);
        Assert.Contains("Ada", CampaignSaveInfoReader.Describe(info));
        Assert.Contains("day 12", CampaignSaveInfoReader.Describe(info));
        Assert.Equal("12", CampaignSaveInfoReader.CampaignDay("12"));
        Assert.Equal("1", CampaignSaveInfoReader.CampaignDay("0"));
        Assert.Equal("1", CampaignSaveInfoReader.CampaignDay("0.761857"));
    }

    [Fact]
    public void A_shared_list_warns_when_the_version_does_not_match()
    {
        var json = """
            {"kind":"CoodszModList","modules":[{"id":"Coop","name":"Bannerlord Coop","version":"v0.1.5","workshopId":"3770450698"}]}
            """;
        var list = SharedModList.Read(json);
        Assert.Single(list);
        Assert.Equal("Coop", list[0].Id);
        Assert.Equal("Bannerlord Coop is v0.1.5 here. The shared list says v0.1.4.", SharedModList.VersionWarning("Bannerlord Coop", "v0.1.4", "v0.1.5"));
        Assert.Null(SharedModList.VersionWarning("Bannerlord Coop", "v0.1.5", "v0.1.5"));
        Assert.True(SharedModList.IsWorkshopId("3770450698"));
        Assert.False(SharedModList.IsWorkshopId("0"));
    }
}
