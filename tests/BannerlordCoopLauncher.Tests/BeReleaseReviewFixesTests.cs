using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;
using Coodsz.MapCheck;

namespace BannerlordCoopLauncher.Tests;

// Burning Empires v1.0.11 security review: Send my logs packs the Join Password module's own log, so the map
// check guard's lines reach a report; a save's ids stay one short line in that log; and the SubModule.xml
// collapse leaves a file alone when an end tag also sits in a comment.
public class BeReleaseReviewFixesTests
{
    [Fact]
    public void Send_my_logs_packs_the_join_password_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-jp-log-" + Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "Documents");
        var programData = Path.Combine(root, "ProgramData");
        Directory.CreateDirectory(ClientLogZip.ClientDiagnostics(documents));
        File.WriteAllText(Path.Combine(ClientLogZip.ClientDiagnostics(documents), ClientLogZip.JoinPasswordLog),
            "2026-11-06 10:00:00 Map check: skipped party lord_1_party (EngageParty, MoveTargetParty and TargetParty are null)");
        var zip = Path.Combine(root, "logs.zip");
        try
        {
            ClientLogZip.Create(zip, null, documents, programData);
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.GetEntry("client-diagnostics/CoodszJoinPassword.log");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            Assert.Contains("Map check: skipped party lord_1_party", reader.ReadToEnd());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_pack_without_the_join_password_log_still_works()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-jp-log-" + Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "Documents");
        Directory.CreateDirectory(documents);
        var zip = Path.Combine(root, "logs.zip");
        try
        {
            ClientLogZip.Create(zip, null, documents, Path.Combine(root, "ProgramData"));
            using var archive = ZipFile.OpenRead(zip);
            Assert.Null(archive.GetEntry("client-diagnostics/CoodszJoinPassword.log"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_save_id_is_logged_on_one_line_and_cut_short()
    {
        Assert.Equal("(no id)", MapCheckGuardRules.Id(null));
        Assert.Equal("lord_1_party", MapCheckGuardRules.Id("lord_1_party"));
        Assert.Equal("a b  c", MapCheckGuardRules.Id("a\nb\r\nc"));
        var cut = MapCheckGuardRules.Id(new string('x', 5000));
        Assert.Equal(MapCheckGuardRules.MaxIdLength, cut.Length);
        Assert.Equal(new string('x', MapCheckGuardRules.MaxIdLength), MapCheckGuardRules.Id(new string('x', MapCheckGuardRules.MaxIdLength)));
        Assert.EndsWith("...", cut);

        var line = MapCheckGuardRules.PartyLine("MobileParty.CheckAiForMapChangeAndUpdateIfNeeded", "bad\r\n2026-11-06 Installed: fake", "EngageParty", null);
        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
    }

    [Fact]
    public void Collapse_leaves_a_file_alone_when_a_comment_holds_another_end_tag()
    {
        var text = "<Module>\r\n  <SubModules>\r\n    <SubModule>\r\n      <Name value=\"A\" />\r\n      <DLLName value=\"A.dll\" />\r\n    </SubModule>\r\n"
            + "    <SubModule>\r\n      <Name value=\"A\" />\r\n      <DLLName value=\"A.dll\" />\r\n    </SubModule>\r\n  </SubModules>\r\n"
            + "  <Xmls>\r\n    <XmlNode />\r\n  </Xmls>\r\n  <!-- old list ended here: </SubModules> -->\r\n</Module>\r\n";
        Assert.Null(ServerSubModuleXml.Collapse(text, out _));
    }

    // Codex: the start tag regex stops at a > inside a quoted attribute, so the splice would land inside the tag.
    [Fact]
    public void Collapse_leaves_a_file_alone_when_the_list_tag_has_a_quoted_angle_bracket()
    {
        var text = "<Module>\r\n  <SubModules note=\"a > b\">\r\n    <SubModule>\r\n      <Name value=\"A\" />\r\n    </SubModule>\r\n"
            + "    <SubModule>\r\n      <Name value=\"A\" />\r\n    </SubModule>\r\n  </SubModules>\r\n</Module>\r\n";
        Assert.Null(ServerSubModuleXml.Collapse(text, out _));
    }

    [Fact]
    public void Collapse_keeps_everything_outside_the_list_when_it_drops_a_repeat()
    {
        var text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Module>\r\n  <Id value=\"DedicatedServer.Windows\" />\r\n  <SubModules>\r\n"
            + "    <!-- keep me -->\r\n    <SubModule>\r\n      <Name value=\"A\" />\r\n    </SubModule>\r\n"
            + "    <SubModule>\r\n      <Name value=\"A\" />\r\n    </SubModule>\r\n  </SubModules>\r\n  <Xmls>\r\n    <XmlNode />\r\n  </Xmls>\r\n</Module>\r\n";
        var collapsed = ServerSubModuleXml.Collapse(text, out var removed);
        Assert.NotNull(collapsed);
        Assert.Equal(1, removed);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Module>\r\n  <Id value=\"DedicatedServer.Windows\" />\r\n  <SubModules>", collapsed);
        Assert.EndsWith("  </SubModules>\r\n  <Xmls>\r\n    <XmlNode />\r\n  </Xmls>\r\n</Module>\r\n", collapsed);
        Assert.Contains("<!-- keep me -->", collapsed);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(collapsed!, "<Name value=\"A\""));
    }
}
