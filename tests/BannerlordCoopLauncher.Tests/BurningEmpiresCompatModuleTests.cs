using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Tests;

// compat/BurningEmpiresCoopCompat/module is the committed Build.cmd output the Burning Empires
// tools bundle as BurningEmpires.CoopCompat, so it has to match the source next to it.
public sealed class BurningEmpiresCompatModuleTests
{
    internal static readonly string Compat = Path.Combine(RepoRoot(), "compat", "BurningEmpiresCoopCompat");
    internal static readonly string UIExtenderExDll = Path.Combine(Compat, "server-extra", "UIExtenderEx-2.13.3", "Bannerlord.UIExtenderEx.dll");
    private static readonly string Module = Path.Combine(Compat, "module");

    [Fact]
    public void Module_folder_carries_the_source_manifest_and_readme()
    {
        Assert.Equal(File.ReadAllBytes(Path.Combine(Compat, "SubModule.xml")), File.ReadAllBytes(Path.Combine(Module, "SubModule.xml")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Compat, "README.txt")), File.ReadAllBytes(Path.Combine(Module, "README.txt")));
    }

    [Fact]
    public void Module_ships_one_dll_for_client_and_server_at_the_manifest_version()
    {
        var client = Path.Combine(Module, "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll");
        var server = Path.Combine(Module, "bin", "Win64_Shipping_Server", "BurningEmpires.CoopCompat.dll");
        Assert.NotEmpty(File.ReadAllBytes(client));
        Assert.Equal(File.ReadAllBytes(client), File.ReadAllBytes(server));

        var xml = File.ReadAllText(Path.Combine(Module, "SubModule.xml"));
        Assert.Contains("<Id value=\"BurningEmpires.CoopCompat\"", xml);
        var version = Regex.Match(xml, "<Version value=\"v([0-9.]+)\"").Groups[1].Value;
        Assert.Equal(version, AssemblyName.GetAssemblyName(client).Version!.ToString(3));
    }

    [Fact]
    public void Readme_names_the_probe_dll_the_way_Burning_Empires_ships_it()
    {
        var readme = File.ReadAllText(Path.Combine(Compat, "README.txt"));
        Assert.Contains("BurningEmpires.DistanceCacheProbe.dll", readme);
        Assert.DoesNotMatch(@"(?<!BurningEmpires\.)DistanceCacheProbe\.dll", readme);
    }

    [Fact]
    public void UIExtenderEx_folder_says_what_goes_there()
    {
        var readme = File.ReadAllText(Path.Combine(Path.GetDirectoryName(UIExtenderExDll)!, "README.txt"));
        Assert.Contains("Bannerlord.UIExtenderEx.dll", readme);
        Assert.Contains("https://github.com/BUTR/Bannerlord.UIExtenderEx/releases/tag/v2.13.3", readme);
        Assert.Contains("269824", readme);
    }

    [UIExtenderEx2133Fact]
    public void UIExtenderEx_dll_is_the_official_2_13_3_build()
    {
        var bytes = File.ReadAllBytes(UIExtenderExDll);
        Assert.Equal(269824, bytes.Length);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal("4F2782840E51391D66B7D701962CB6202969D34C0949B8EC1591964F9C479F99", hash);
        Assert.Equal("2.13.3.0", AssemblyName.GetAssemblyName(UIExtenderExDll).Version!.ToString());
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

// The UIExtenderEx 2.13.3 dll is downloaded only with Coodsz's OK, so until it is in
// server-extra the hash test is skipped with a message instead of failing.
public sealed class UIExtenderEx2133FactAttribute : FactAttribute
{
    public UIExtenderEx2133FactAttribute()
    {
        if (!File.Exists(BurningEmpiresCompatModuleTests.UIExtenderExDll))
            Skip = "Bannerlord.UIExtenderEx.dll 2.13.3 is not in compat/BurningEmpiresCoopCompat/server-extra/UIExtenderEx-2.13.3 yet (see the README there).";
    }
}
