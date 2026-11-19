using System.Text;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class UIExtenderExPrefabsFixTests
{
    // UIExtenderEx v3.0.3 as it ships on the Workshop (2859222409), the long hint texts cut short.
    private const string Shipped = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Module xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/BUTR/Bannerlord.XmlSchemas/master/SubModule.xsd">
          <Id value="Bannerlord.UIExtenderEx" />
          <Name value="UIExtenderEx" />
          <Version value="v3.0.3" />
          <DependedModules>
            <DependedModule Id="Bannerlord.Harmony" DependentVersion="v2.3.3" />
          </DependedModules>
          <SubModules>
            <SubModule>
              <Name value="UIExtenderEx" />
              <DLLName value="Bannerlord.UIExtenderEx.dll" />
              <SubModuleClassType value="Bannerlord.UIExtenderEx.SubModule" />
              <Assemblies />
              <Tags />
            </SubModule>
            <!-- The prefab runtimes, after the core: each installs itself when the game builds it, in this order -->
            <SubModule>
              <Name value="UIExtenderEx XML Prefabs" />
              <DLLName value="Bannerlord.UIExtenderEx.XmlPrefabs.dll" />
              <SubModuleClassType value="Bannerlord.UIExtenderEx.XmlPrefabs.SubModule" />
              <Assemblies />
              <Tags />
            </SubModule>
            <SubModule>
              <Name value="UIExtenderEx Game Prefabs" />
              <DLLName value="Bannerlord.UIExtenderEx.GamePrefabs.dll" />
              <SubModuleClassType value="Bannerlord.UIExtenderEx.GamePrefabs.SubModule" />
              <Assemblies />
              <Tags />
            </SubModule>
            <SubModule>
              <Name value="UIExtenderEx Compiled Prefabs" />
              <DLLName value="Bannerlord.UIExtenderEx.CompiledPrefabs.dll" />
              <SubModuleClassType value="Bannerlord.UIExtenderEx.CompiledPrefabs.SubModule" />
              <Assemblies />
              <Tags />
            </SubModule>
          </SubModules>
          <Settings DisplayName="UIExtenderEx v3.0.3">
            <Group Name="{=uX4aVQ2kHc}Compiled Prefabs" Order="0">
              <Bool Id="CompiledPrefabs" DisplayName="{=Mh7rTq3wPd}Use compiled prefabs" HintText="Compile patched movies to C#." Default="true" IsToggle="true" />
              <Bool Id="DisableGeneratedPrefabs" DisplayName="{=Wn3jKp6eSf}Disable generated prefabs" HintText="Previous behaviour." Default="false" Value="true" />
            </Group>
          </Settings>
        </Module>

        """;

    private const string CompiledBlock = """
            <SubModule>
              <Name value="UIExtenderEx Compiled Prefabs" />
              <DLLName value="Bannerlord.UIExtenderEx.CompiledPrefabs.dll" />
              <SubModuleClassType value="Bannerlord.UIExtenderEx.CompiledPrefabs.SubModule" />
              <Assemblies />
              <Tags />
            </SubModule>

        """;

    private const string CodeGenerator = "Bannerlord.UIExtenderEx.CodeGenerator.dll";
    private const string Compiler = "Bannerlord.UIExtenderEx.Compiler.dll";

    private static readonly string[] ShippedDlls =
    [
        "Bannerlord.UIExtenderEx.dll", "Bannerlord.UIExtenderEx.XmlPrefabs.dll", "Bannerlord.UIExtenderEx.GamePrefabs.dll",
        "Bannerlord.UIExtenderEx.CompiledPrefabs.dll", CodeGenerator, Compiler
    ];

    private static readonly string[] Launch = ["Bannerlord.Harmony", "Bannerlord.UIExtenderEx", "Native", "BannerFix", "XorberaxLegacy", "Coop"];

    private static string Text(string newLine) => Shipped.ReplaceLineEndings("\n").Replace("\n", newLine);

    // UIExtenderEx's own folder as the player has it: SubModule.xml with a BOM, and the shipped DLLs
    // in bin\Win64_Shipping_Client except the missing ones.
    private static string Folder(string root, string name, string xml, params string[] missing)
    {
        var dir = Path.Combine(root, name);
        var bin = Path.Combine(dir, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(dir, "SubModule.xml"), xml, new UTF8Encoding(true));
        foreach (var dll in ShippedDlls.Except(missing))
            File.WriteAllBytes(Path.Combine(bin, dll), [0x4D, 0x5A]);
        return dir;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "uiex-fix-" + Guid.NewGuid().ToString("N"));

    private const string AppliedLine = "UIExtenderEx is missing Bannerlord.UIExtenderEx.CodeGenerator.dll, so its Compiled Prefabs are off for this game. Verify UIExtenderEx in Steam or check your antivirus to get the file back.";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Change_takes_out_only_the_compiled_prefabs_submodule_and_turns_the_setting_off(string newLine)
    {
        var xml = Text(newLine);
        var changed = UIExtenderExPrefabsFix.Change(xml)!;

        var expected = xml.Replace(CompiledBlock.ReplaceLineEndings("\n").Replace("\n", newLine), "")
            .Replace("Default=\"true\" IsToggle=\"true\" />", "Default=\"true\" IsToggle=\"true\" Value=\"false\" />");
        Assert.Equal(expected, changed);
        Assert.DoesNotContain("CompiledPrefabs.dll", changed);
        Assert.Contains("Bannerlord.UIExtenderEx.dll", changed);
        Assert.Contains("Bannerlord.UIExtenderEx.XmlPrefabs.dll", changed);
        Assert.Contains("Bannerlord.UIExtenderEx.GamePrefabs.dll", changed);
        Assert.Contains("Id=\"DisableGeneratedPrefabs\" DisplayName=\"{=Wn3jKp6eSf}Disable generated prefabs\" HintText=\"Previous behaviour.\" Default=\"false\" Value=\"true\" />", changed);
        if (newLine == "\r\n")
            Assert.DoesNotContain("\n", changed.Replace("\r\n", ""));
        Assert.Null(UIExtenderExPrefabsFix.Change(changed));
    }

    [Fact]
    public void Change_replaces_a_value_the_game_wrote()
    {
        var xml = Text("\n").Replace("IsToggle=\"true\" />", "IsToggle=\"true\" Value=\"true\" />");
        var changed = UIExtenderExPrefabsFix.Change(xml)!;
        Assert.Contains("IsToggle=\"true\" Value=\"false\" />", changed);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(changed, "Id=\"CompiledPrefabs\"[^>]*Value="));
    }

    [Fact]
    public void Lines_name_the_missing_files()
    {
        Assert.Equal(AppliedLine, UIExtenderExPrefabsFix.Line([CodeGenerator]));
        Assert.Equal("UIExtenderEx is missing Bannerlord.UIExtenderEx.CodeGenerator.dll and Bannerlord.UIExtenderEx.Compiler.dll, so its Compiled Prefabs are off for this game. Verify UIExtenderEx in Steam or check your antivirus to get the files back.",
            UIExtenderExPrefabsFix.Line([CodeGenerator, Compiler]));
    }

    [Fact]
    public void A_complete_copy_is_left_alone_even_with_banner_fix_and_xorberax()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\r\n"));
            var before = File.ReadAllBytes(Path.Combine(dir, "SubModule.xml"));
            Assert.Empty(UIExtenderExPrefabsFix.MissingFiles(dir));
            Assert.Null(UIExtenderExPrefabsFix.HealthProblem(dir));
            Assert.Empty(UIExtenderExPrefabsFix.HealthProblems([root]));
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(dir, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.MarkName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Nothing_happens_when_uiextenderex_is_not_in_the_launch_list()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\n"), CodeGenerator);
            var before = File.ReadAllBytes(Path.Combine(dir, "SubModule.xml"));
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(["Native", "BannerFix", "Coop"], [root]));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(dir, "SubModule.xml")));
            Assert.True(UIExtenderExPrefabsFix.InLaunch(["native", " bannerlord.uiextenderex "]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Launch_without_uiextenderex_installed_does_nothing()
    {
        var root = NewRoot();
        try
        {
            Folder(root, "BannerFix", "<Module><Id value=\"BannerFix\"/></Module>");
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root, Path.Combine(root, "missing")]));
            Assert.False(File.Exists(Path.Combine(root, "BannerFix", UIExtenderExPrefabsFix.MarkName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_copy_without_the_compiled_prefabs_dll_is_left_alone()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\n"), CodeGenerator, UIExtenderExPrefabsFix.CompiledPrefabsDll);
            Assert.Empty(UIExtenderExPrefabsFix.MissingFiles(dir));
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Check_mods_says_what_is_missing()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\n"), CodeGenerator);
            Assert.Equal([CodeGenerator], UIExtenderExPrefabsFix.MissingFiles(dir));
            const string expected = "UIExtenderEx is incomplete. Bannerlord.UIExtenderEx.CodeGenerator.dll is missing. Verify it in Steam (Library, Bannerlord, Properties, Installed Files, Verify) or check your antivirus quarantine, then Check mods again.";
            Assert.Equal(expected, UIExtenderExPrefabsFix.HealthProblem(dir));
            Assert.Equal([expected], UIExtenderExPrefabsFix.HealthProblems([root]));

            // Still reported while the fix is on (the game is running).
            Assert.NotEmpty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
            Assert.Equal(expected, UIExtenderExPrefabsFix.HealthProblem(dir));

            File.Delete(Path.Combine(dir, "bin", "Win64_Shipping_Client", Compiler));
            Assert.Equal("UIExtenderEx is incomplete. Bannerlord.UIExtenderEx.CodeGenerator.dll and Bannerlord.UIExtenderEx.Compiler.dll are missing. Verify it in Steam (Library, Bannerlord, Properties, Installed Files, Verify) or check your antivirus quarantine, then Check mods again.",
                UIExtenderExPrefabsFix.HealthProblem(dir));
            Assert.Null(UIExtenderExPrefabsFix.HealthProblem(null));
            Assert.Null(UIExtenderExPrefabsFix.HealthProblem(Path.Combine(root, "nowhere")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_code_generator_is_patched_keeping_the_bom_and_restored_byte_for_byte()
    {
        var root = NewRoot();
        try
        {
            var complete = Folder(root, "Bannerlord.UIExtenderEx", Text("\r\n"));
            var completeBefore = File.ReadAllBytes(Path.Combine(complete, "SubModule.xml"));
            var dir = Folder(root, "2859222409", Text("\r\n"), CodeGenerator);
            var xmlPath = Path.Combine(dir, "SubModule.xml");
            var original = File.ReadAllBytes(xmlPath);

            Assert.Equal([AppliedLine], UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
            var patched = File.ReadAllBytes(xmlPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, patched.Take(3).ToArray());
            Assert.Equal(UIExtenderExPrefabsFix.Change(Text("\r\n")), new UTF8Encoding(false).GetString(patched, 3, patched.Length - 3));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));
            Assert.True(SubModuleXmlPatch.IsApplied(dir, UIExtenderExPrefabsFix.MarkName));
            Assert.Equal(completeBefore, File.ReadAllBytes(Path.Combine(complete, "SubModule.xml")));

            // A second launch changes nothing and logs nothing.
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
            Assert.Equal(patched, File.ReadAllBytes(xmlPath));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));

            Assert.Equal([dir], UIExtenderExPrefabsFix.RestoreAll([root]));
            Assert.Equal(original, File.ReadAllBytes(xmlPath));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.MarkName)));
            Assert.Empty(UIExtenderExPrefabsFix.RestoreAll([root]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_file_already_like_that_is_left_alone()
    {
        var root = NewRoot();
        try
        {
            var done = UIExtenderExPrefabsFix.Change(Text("\n"))!;
            var dir = Folder(root, "Bannerlord.UIExtenderEx", done, CodeGenerator);
            var before = File.ReadAllBytes(Path.Combine(dir, "SubModule.xml"));
            Assert.Empty(UIExtenderExPrefabsFix.ApplyForLaunch(Launch, [root]));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(dir, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.MarkName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_steam_update_while_the_game_ran_is_not_written_over()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\n"), CodeGenerator);
            var xmlPath = Path.Combine(dir, "SubModule.xml");
            Assert.True(UIExtenderExPrefabsFix.Apply(dir));

            var update = Text("\n").Replace("v3.0.3", "v3.0.4");
            File.WriteAllText(xmlPath, update, new UTF8Encoding(true));
            var updated = File.ReadAllBytes(xmlPath);
            Assert.False(UIExtenderExPrefabsFix.Restore(dir));
            Assert.Equal(updated, File.ReadAllBytes(xmlPath));
            Assert.False(File.Exists(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));

            // The next launch takes the update as the new original.
            Assert.True(UIExtenderExPrefabsFix.Apply(dir));
            Assert.Equal(updated, File.ReadAllBytes(Path.Combine(dir, UIExtenderExPrefabsFix.BackupName)));
            Assert.True(UIExtenderExPrefabsFix.Restore(dir));
            Assert.Equal(updated, File.ReadAllBytes(xmlPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Uiextenderex_and_taom_fixes_keep_their_own_files()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "2859222409", Text("\n"), CodeGenerator);
            Assert.True(UIExtenderExPrefabsFix.Apply(dir));
            Assert.Empty(TaomDependencyFix.RestoreAll([root]));
            Assert.True(SubModuleXmlPatch.IsApplied(dir, UIExtenderExPrefabsFix.MarkName));
            Assert.NotEqual(TaomDependencyFix.MarkName, UIExtenderExPrefabsFix.MarkName);
            Assert.NotEqual(TaomDependencyFix.BackupName, UIExtenderExPrefabsFix.BackupName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Every_client_launcher_turns_it_off_before_the_game_starts_and_back_on_after()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        foreach (var file in new[] { Path.Combine("BannerlordCoopLauncher", "MainWindow.Launch.cs"), Path.Combine("AD1259Launcher", "MainWindow.xaml.cs") })
        {
            var launch = File.ReadAllText(Path.Combine(dir!.FullName, "src", file));
            var apply = launch.IndexOf("UIExtenderExPrefabsFix.ApplyForLaunch(", StringComparison.Ordinal);
            var start = launch.IndexOf("var started = Process.Start(start);", StringComparison.Ordinal);
            Assert.True(apply > 0, file);
            Assert.True(apply < start, file);
            Assert.True(launch.IndexOf("RestoreUIExtenderExFixWhenClosed(started);", StringComparison.Ordinal) > start, file);
        }

        var taom = File.ReadAllText(Path.Combine(dir!.FullName, "src", "TaomCoopLauncher", "MainWindow.Checks.cs"));
        Assert.Contains("UIExtenderExPrefabsFix.ApplyForLaunch(", taom);
        Assert.Contains("UIExtenderExPrefabsFix.RestoreAll(", taom);
    }
}
