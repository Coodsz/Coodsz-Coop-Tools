using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat on the dedicated server: which save is the server's untouched copy of
// default_new_game.sav, so a new Burning Empires campaign starts instead of loading it
// (FreshCampaignRules.cs, linked).
public sealed class BurningEmpiresFreshCampaignTests : IDisposable
{
    private static readonly string[] VanillaModules = { "Native", "SandBoxCore", "Sandbox", "Coop" };
    private static readonly string[] BurningEmpiresModules = { "Native", "SandBoxCore", "Sandbox", "BurningEmpires", "Coop" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-befresh-" + Guid.NewGuid().ToString("N"));
    private readonly string _bin;
    private readonly string _saves;

    public BurningEmpiresFreshCampaignTests()
    {
        _bin = Directory.CreateDirectory(Path.Combine(_dir, "engine", "bin", "Win64_Shipping_Server")).FullName;
        _saves = Directory.CreateDirectory(Path.Combine(_dir, "Game Saves")).FullName;
        File.WriteAllBytes(Path.Combine(_bin, FreshCampaignRules.TemplateFileName), Template());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static byte[] Template()
    {
        var bytes = new byte[200_000];
        new Random(1259).NextBytes(bytes);
        return bytes;
    }

    private string Save(string name, byte[] bytes)
    {
        var path = Path.Combine(_saves, name + ".sav");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private bool IsTemplateCopy(string[] modules, string savePath) =>
        FreshCampaignRules.IsTemplateCopy(modules, savePath, FreshCampaignRules.TemplateCandidates(savePath, _bin, _saves));

    [Fact]
    public void The_servers_copy_of_the_template_starts_a_new_campaign()
    {
        var save = Save("MyBurningEmpires", Template());
        Assert.True(IsTemplateCopy(VanillaModules, save));
        // Unreadable metadata does not stop it, the bytes still match.
        Assert.True(IsTemplateCopy(null!, save));
    }

    [Fact]
    public void A_save_with_Burning_Empires_in_its_metadata_always_loads()
    {
        var save = Save("MyBurningEmpires", Template());
        Assert.False(IsTemplateCopy(BurningEmpiresModules, save));
    }

    [Fact]
    public void A_vanilla_campaign_that_is_not_the_template_loads_as_it_is()
    {
        var bytes = Template();
        bytes[bytes.Length / 2] ^= 0xFF;
        Assert.False(IsTemplateCopy(VanillaModules, Save("OldCalradia", bytes)));
        Assert.False(IsTemplateCopy(VanillaModules, Save("Shorter", Template().AsSpan(0, 1000).ToArray())));
    }

    [Fact]
    public void A_save_named_default_new_game_is_never_its_own_template()
    {
        // With no template next to the exe, a campaign saved as default_new_game.sav in Game Saves
        // only compares with itself, and that does not count.
        File.Delete(Path.Combine(_bin, FreshCampaignRules.TemplateFileName));
        var bytes = Template();
        bytes[0] ^= 0xFF;
        var save = Save("default_new_game", bytes);
        Assert.Empty(FreshCampaignRules.TemplateCandidates(save, _saves));
        Assert.False(IsTemplateCopy(VanillaModules, save));

        // The template next to the exe still tells an untouched copy under that name.
        File.WriteAllBytes(Path.Combine(_bin, FreshCampaignRules.TemplateFileName), Template());
        Assert.True(IsTemplateCopy(VanillaModules, Save("default_new_game", Template())));
    }

    [Fact]
    public void A_missing_save_or_template_is_not_a_template_copy()
    {
        Assert.False(IsTemplateCopy(VanillaModules, Path.Combine(_saves, "missing.sav")));
        Assert.False(FreshCampaignRules.IsTemplateCopy(VanillaModules, Save("x", Template()), Array.Empty<string>()));
        Assert.False(FreshCampaignRules.IsTemplateCopy(VanillaModules, null!, null!));
    }

    [Fact]
    public void Only_the_server_bin_folder_counts_as_the_dedicated_server()
    {
        Assert.True(FreshCampaignRules.IsServerBinFolder(_bin));
        Assert.True(FreshCampaignRules.IsServerBinFolder(@"C:\ds\engine\bin\win64_shipping_server\"));
        Assert.False(FreshCampaignRules.IsServerBinFolder(@"C:\Steam\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\"));
        Assert.False(FreshCampaignRules.IsServerBinFolder(null!));
    }

    [Fact]
    public void Module_ids_match_without_case()
    {
        Assert.True(FreshCampaignRules.HasModule(new[] { "Native", "burningempires" }, FreshCampaignRules.BurningEmpiresModuleId));
        Assert.False(FreshCampaignRules.HasModule(new[] { "BurningEmpires.CoopCompat" }, FreshCampaignRules.BurningEmpiresModuleId));
        Assert.False(FreshCampaignRules.HasModule(null!, FreshCampaignRules.BurningEmpiresModuleId));
    }
}
