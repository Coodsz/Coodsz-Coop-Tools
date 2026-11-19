using System.Text.RegularExpressions;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Coop #55: players' Pause, Play and Fast forward did nothing. The start script reads a server-config.json
// without allowClientTimeControls as off, while the Server Tool showed the box ticked.
public sealed class ClientTimeControlsTests
{
    // The start script's own match (Start-ModdedServer.ps1), so the added key is one it reads.
    private static readonly Regex ScriptReads = new(ServerSession.ScriptClientTimePattern, RegexOptions.IgnoreCase);

    private const string OldConfig = "{\r\n  // Save to host.\r\n  \"saveName\": \"saveauto1\",\r\n  \"steam\": true,\r\n  \"traceTick\": false,\r\n}\r\n";

    [Fact]
    public void An_older_config_without_the_key_gets_it_as_on_where_the_start_script_reads_it()
    {
        var added = ServerSession.WithClientTimeKey(OldConfig);
        Assert.NotNull(added);
        var match = ScriptReads.Match(added!);
        Assert.True(match.Success, added);
        Assert.Equal("true", match.Groups["value"].Value);
        // The rest of the file is kept.
        Assert.Contains("// Save to host.", added);
        Assert.Contains("\"saveName\": \"saveauto1\"", added);
        Assert.Null(ServerSession.WithClientTimeKey(added!));
    }

    [Fact]
    public void The_added_line_keeps_the_file_valid_and_its_line_endings()
    {
        // The last value has a trailing comment and no comma, which an insert at the end would break.
        const string config = "{\r\n  \"saveName\": \"a\",\r\n  \"traceTick\": false // diagnostics } here\r\n}\r\n";
        var added = ServerSession.WithClientTimeKey(config)!;
        Assert.StartsWith("{\r\n  \"allowClientTimeControls\": true,\r\n  \"saveName\"", added);
        Assert.EndsWith("\"traceTick\": false // diagnostics } here\r\n}\r\n", added);
        Assert.DoesNotContain("\r\r", added);
        Assert.Equal(added.Split("\r\n").Length - 1, added.Count(c => c == '\n'));

        // An LF file stays LF, a header comment with a brace is skipped, and a one line file gets its own line.
        Assert.Equal("// a {note}\n{\n  \"allowClientTimeControls\": true,\n  \"steam\": true\n}\n",
            ServerSession.WithClientTimeKey("// a {note}\n{\n  \"steam\": true\n}\n"));
        Assert.True(ScriptReads.IsMatch(ServerSession.WithClientTimeKey("{\"steam\": true}")!));
        // A stray letter before the brace does not stop the server, so the key still goes in.
        Assert.True(ScriptReads.IsMatch(ServerSession.WithClientTimeKey("h {\r\n  \"steam\": true,\r\n}\r\n")!));
        Assert.Null(ServerSession.WithClientTimeKey(""));
    }

    [Fact]
    public void Only_a_key_the_start_script_reads_counts()
    {
        // A hand typed False is the host's off: the script reads it (ignoring case), so it stays.
        Assert.Null(ServerSession.WithClientTimeKey(OldConfig.Replace("\"steam\": true,", "\"steam\": true,\r\n  \"allowClientTimeControls\": False,")));
        // A key only in a comment is none for the script, so it is added.
        var commented = OldConfig.Replace("// Save to host.", "// \"allowClientTimeControls\": true");
        Assert.True(ScriptReads.Matches(ServerSession.WithClientTimeKey(commented)!).Count == 1);
    }

    [Fact]
    public void A_one_line_off_stays_off()
    {
        // The script's line match misses it, but the Settings page shows it unticked: no insert that turns it on.
        Assert.Null(ServerSession.WithClientTimeKey("{\"saveName\":\"x\",\"allowClientTimeControls\": false}"));
        // The page reads only a lowercase false there and shows FALSE as ticked, so it is turned on for both.
        Assert.True(ScriptReads.IsMatch(ServerSession.WithClientTimeKey("{ \"steam\": true, \"allowClientTimeControls\": FALSE }")!));
    }

    [Theory]
    [InlineData("Write-Utf8TextAtomically : Exception calling \"WriteAllText\" with \"3\" argument(s): \"Could not find file\"", true)]
    [InlineData("    + CategoryInfo          : NotSpecified: (:) [Write-Utf8TextAtomically], MethodInvocationException", true)]
    [InlineData("    + FullyQualifiedErrorId : FileNotFoundException,Write-Utf8TextAtomically", true)]
    [InlineData("At C:\\x\\DedicatedServer\\Coodsz-Start-ModdedServer.ps1:1598 char:5", true)]
    [InlineData("System.NullReferenceException: Object reference not set to an instance of an object.", false)]
    [InlineData("   at NoCompanionLimit.SubModule.OnSubModuleLoad()", false)]
    public void Start_script_error_records_are_told_apart_from_mod_exceptions(string line, bool record) =>
        Assert.Equal(record, BannerlordCoopLauncher.Core.Support.StartScriptOutput.IsPowerShellRecord(line));

    [Fact]
    public void The_pattern_is_the_start_script_own()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var match = Regex.Match(script, @"\$clientTimeControlsMatch = \[regex\]::Match\(\s*\$configuredText,\s*'(?<pattern>[^']+)'");
        Assert.True(match.Success);
        Assert.Equal(match.Groups["pattern"].Value, ServerSession.ScriptClientTimePattern);
        // And the script reads a missing key as off, which is why the tool adds it.
        Assert.Contains("$allowClientTimeControls = $false", script);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void A_config_that_has_the_key_is_left_as_it_is(string value)
    {
        var config = OldConfig.Replace("\"steam\": true,", "\"steam\": true,\r\n  \"allowClientTimeControls\": " + value + ",");
        Assert.Null(ServerSession.WithClientTimeKey(config));
    }

    // Burning Empires #11, #12: a server-config.json the BE Server Tool made before it wrote the key, with the
    // keys its older default had. The server log said "Client time controls: disabled".
    private const string OldBeConfig = "{\r\n  \"saveName\": \"default_new_game\",\r\n  \"autosaveMinutes\": 15,\r\n  \"password\": \"\",\r\n  \"port\": 4200,\r\n  \"logFile\": true,\r\n  \"steam\": true\r\n}\r\n";

    [Fact]
    public void An_older_burning_empires_config_gets_the_key_and_the_new_default_already_has_it()
    {
        var added = TaomCoop.Core.ClientTimeControls.WithKey(OldBeConfig);
        Assert.NotNull(added);
        Assert.Equal("true", ScriptReads.Match(added!).Groups["value"].Value);
        Assert.Contains("\"steam\": true\r\n}", added);
        Assert.Null(TaomCoop.Core.ClientTimeControls.WithKey(added!));
        Assert.Null(TaomCoop.Core.ClientTimeControls.WithKey(BurningEmpires.Core.BeServerPrepare.DefaultServerConfig));
        // The Coodsz Server Tool runs the same repair.
        Assert.Equal(added, ServerSession.WithClientTimeKey(OldBeConfig));
    }

    [Fact]
    public void Every_burning_empires_start_adds_the_key_before_the_server_starts()
    {
        var start = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ServerControl.cs"));
        var repair = start.IndexOf("RepairServerConfig();", StringComparison.Ordinal);
        Assert.True(repair > 0);
        Assert.True(repair < start.IndexOf("process.Start()", StringComparison.Ordinal));
        var settings = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.Settings.cs")).Replace("\r\n", "\n");
        var method = settings[settings.IndexOf("private void RepairServerConfig()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    }\n", StringComparison.Ordinal)];
        // After the other repair and outside its "something changed" branch, so a file with nothing else to fix gets it too.
        Assert.EndsWith("KeepClientTimeControls(path, repaired);", method.TrimEnd());
        Assert.Contains("ClientTimeControls.WithKey(json)", settings);
    }

    [Fact]
    public void Every_start_adds_the_key_before_the_server_starts()
    {
        var start = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        var keep = start.IndexOf("KeepClientTimeControls();", StringComparison.Ordinal);
        Assert.True(keep > 0);
        Assert.True(keep < start.IndexOf("_session.Start(Executable", StringComparison.Ordinal));
    }
}
