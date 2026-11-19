using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Crash report #23: an old manual MCM copy (v5.11.99) builds MCMv5.dll against
// Bannerlord.UIExtenderEx, which the headless server never loads. The start script
// published it anyway and the engine died while loading submodules. It now refuses
// that MCM before the engine starts; the Workshop builds (5.12.x) still pass.
public class OldMcmServerTests
{
    private const string TooOld = "is too old for the dedicated server and crashes it";

    [Fact]
    public void Start_script_checks_mcm_before_publishing_it()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var check = script.IndexOf("$mcmProblem = Get-ServerMcmProblem -Path $mcmPath", StringComparison.Ordinal);
        var publish = script.IndexOf("Install-CentralRuntimeAssembly -SourcePath $mcmPath", StringComparison.Ordinal);
        Assert.True(check > 0 && check < publish, "The MCM check runs before MCMv5.dll is published.");
        Assert.Contains("throw ($mcmProblem + \" The old copy on the server is in this folder: \" + $mcmRoot)", script);
        // read without loading MCM into the script's process
        Assert.Contains("[System.Reflection.AssemblyName]::GetAssemblyName($Path).Version", script);
        Assert.DoesNotContain("ReflectionOnlyLoad", script);
        Assert.True(script.All(ch => ch < 128), "Start-ModdedServer.ps1 stays ASCII");
    }

    [WindowsFact]
    public void An_old_mcm_is_refused_and_the_workshop_mcm_passes()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var function = System.Text.RegularExpressions.Regex.Match(script, @"(?ms)^function\s+Get-ServerMcmProblem\s*\{.*?^\}").Value;
        Assert.NotEmpty(function);

        var root = Path.Combine(Path.GetTempPath(), "old-mcm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Tiny stand ins built by Windows PowerShell's own compiler: one UIExtenderEx and
            // four MCMv5.dll builds, old or new, with or without a reference to UIExtenderEx.
            var output = Path.Combine(root, "out.txt");
            var run = function + "\r\n" +
                      "$root = '" + root + "'\r\n" +
                      "$ui = Join-Path $root 'ui\\Bannerlord.UIExtenderEx.dll'\r\n" +
                      "[void][System.IO.Directory]::CreateDirectory((Split-Path $ui))\r\n" +
                      "Add-Type -TypeDefinition 'namespace Bannerlord.UIExtenderEx { public class UIExtender { } }' -OutputAssembly $ui -OutputType Library\r\n" +
                      "function New-Mcm([string]$Name, [string]$Version, [bool]$UsesUi) {\r\n" +
                      "    $path = Join-Path $root ($Name + '\\MCMv5.dll')\r\n" +
                      "    [void][System.IO.Directory]::CreateDirectory((Split-Path $path))\r\n" +
                      "    $field = if ($UsesUi) { 'public Bannerlord.UIExtenderEx.UIExtender Extender;' } else { 'public int Value;' }\r\n" +
                      "    $code = '[assembly: System.Reflection.AssemblyVersion(\"' + $Version + '\")] namespace MCM { public class Settings { ' + $field + ' } }'\r\n" +
                      "    if ($UsesUi) { Add-Type -TypeDefinition $code -OutputAssembly $path -OutputType Library -ReferencedAssemblies $ui }\r\n" +
                      "    else { Add-Type -TypeDefinition $code -OutputAssembly $path -OutputType Library }\r\n" +
                      "    $problem = Get-ServerMcmProblem -Path $path\r\n" +
                      "    if ($null -eq $problem) { 'ok' } else { $problem }\r\n" +
                      "}\r\n" +
                      "$lines = @(" +
                      "(New-Mcm 'old' '5.11.99.0' $true)," +
                      "(New-Mcm 'oldclean' '5.11.0.0' $false)," +
                      "(New-Mcm 'newui' '5.12.3.0' $true)," +
                      "(New-Mcm 'workshop' '5.12.3.0' $false)," +
                      "(New-Mcm 'legacy' '5.12.2.0' $false)," +
                      "(Get-ServerMcmProblem -Path (Join-Path $root 'missing.dll')))\r\n" +
                      "[System.IO.File]::WriteAllLines('" + output + "', [string[]]@($lines | ForEach-Object { if ($null -eq $_) { 'unreadable' } else { $_ } }))\r\n";
            File.WriteAllText(Path.Combine(root, "run.ps1"), run);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "run.ps1") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(120000), "The MCM check did not finish.");
            var lines = File.ReadAllLines(output);
            Assert.Equal(6, lines.Length);
            Assert.Equal(
                "Mod Configuration Menu v5.11.99 is too old for the dedicated server and crashes it. " +
                "Get MCM v5.12.3 or newer from Nexus Mods (mod 612) and put its Bannerlord.MBOptionScreen folder in the game's " +
                "Modules folder, then press Start server again. The tool copies it to the server.",
                lines[0]);
            // below 5.12 is refused even without the reference, and 5.12 with it too
            Assert.StartsWith("Mod Configuration Menu v5.11.0 " + TooOld, lines[1]);
            Assert.StartsWith("Mod Configuration Menu v5.12.3 " + TooOld, lines[2]);
            Assert.Equal("ok", lines[3]);
            Assert.Equal("ok", lines[4]);
            // a file that is not readable as an assembly is left to the old path
            Assert.Equal("unreadable", lines[5]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Coodsz_host_explains_the_refusal()
    {
        var log = "Mod Configuration Menu v5.11.99 " + TooOld + ". Update MCM to v5.12 or newer from the Steam Workshop or Nexus Mods.";
        var lines = ServerCrashExplain.Explain("engine exited with code 1", false, log, null, [], []);
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("What happened: The server did not start because Mod Configuration Menu (MCM) is an old copy.", lines[0]);
        // Nexus and the game's Modules folder: the official Workshop MCM is gone (crash reports #32 and #34).
        Assert.Contains(lines, line => line.StartsWith("What to do") && line.Contains("v5.12.3 or newer from Nexus Mods") && line.Contains("game's Modules folder") && line.Contains("Start server"));
        Assert.DoesNotContain(lines, line => line.Contains("subscribe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.StartsWith("What not to do") && line.Contains("Do not turn MCM off"));
    }
}
