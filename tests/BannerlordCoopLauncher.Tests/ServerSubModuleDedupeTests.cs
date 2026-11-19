using System.Text;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Launch;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// peaches (CR-A9XVH): the Burning Empires server loaded Launcher Cheats and Launcher Battle Size 7 times and
// Coop Save Rescue 6 times, and one more each start. ServerFolderRepair saved its two entries with XDocument,
// which put them on the line of </SubModules>. The Coop Save Rescue insert took that whole line as its indent,
// so both came back in front of every line of its entry, and its own check then missed the entry.
public class ServerSubModuleDedupeTests
{
    private const string Cheats = "DedicatedServer.LauncherCheats.dll";
    private const string BattleSize = "DedicatedServer.LauncherBattleSize.dll";

    // DedicatedServer.Windows the way the game ships it: tabs, CRLF and a UTF-8 BOM.
    private static readonly string ServerWindowsXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
        + "<Module>\r\n"
        + "\t<Name value=\"DedicatedServer.Windows\"/>\r\n"
        + "\t<Id value=\"DedicatedServer.Windows\"/>\r\n"
        + "\t<Version value=\"v1.4.8\"/>\r\n"
        + "\t<SubModules>\r\n"
        + "\t\t<SubModule>\r\n"
        + "\t\t\t<Name value=\"DedicatedServer.Windows\"/>\r\n"
        + "\t\t\t<DLLName value=\"DedicatedServer.Windows.dll\"/>\r\n"
        + "\t\t\t<SubModuleClassType value=\"DedicatedServer.Windows.DedicatedServerSubModule\"/>\r\n"
        + "\t\t\t<Tags>\r\n"
        + "\t\t\t\t<Tag key=\"DedicatedServerType\" value=\"Custom\"/>\r\n"
        + "\t\t\t</Tags>\r\n"
        + "\t\t</SubModule>\r\n"
        + "\t</SubModules>\r\n"
        + "</Module>\r\n";

    private static void WriteWithBom(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(true));
    }

    // The DLLs in the order the engine loads them: the top level entries of SubModules.
    private static List<string> LoadedDlls(string xmlPath) =>
        XDocument.Load(xmlPath).Root!.Element("SubModules")!.Elements("SubModule")
            .Select(item => (string)item.Element("DLLName")!.Attribute("value")!).ToList();

    private static void AssertKeptFormat(string xmlPath)
    {
        var bytes = File.ReadAllBytes(xmlPath);
        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "the BOM stays");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.Contains("\r\n\t</SubModules>\r\n", text);
    }

    [Fact]
    public void Starts_and_Coop_update_prepares_keep_one_entry_per_server_add_on()
    {
        var root = NewRoot("submodule-repeat");
        try
        {
            var hostPayload = RepairPayload(root);
            var workshop = ThinWorkshopServer(root);
            WriteWithBom(Path.Combine(workshop, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml"), ServerWindowsXml);
            var server = Path.Combine(root, "server");
            var rescue = Write(Path.Combine(root, "tool", "SaveRescue", RescueSubModule.DllName), "rescue");
            var xml = Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml");
            var expected = new[] { "DedicatedServer.Windows.dll", Cheats, BattleSize, RescueSubModule.DllName };

            // Prepare server, then what Start server does each time: the tool's own files, then Coop Save Rescue.
            BeServerPrepare.Prepare(workshop, server, null, hostPayload);
            for (var start = 1; start <= 4; start++)
            {
                BeServerPrepare.LayOverRepair(server, hostPayload);
                RescueSubModule.Install(server, rescue);
                Assert.Equal(expected, LoadedDlls(xml));
            }

            // A Coop update on the Workshop: Start prepares again, then starts as before.
            Write(Path.Combine(workshop, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop core 2");
            Assert.True(BeServerPrepare.CoopChanged(workshop, server));
            for (var start = 1; start <= 3; start++)
            {
                BeServerPrepare.Prepare(workshop, server, null, hostPayload);
                BeServerPrepare.LayOverRepair(server, hostPayload);
                RescueSubModule.Install(server, rescue);
                Assert.Equal(expected, LoadedDlls(xml));
            }

            Assert.True(RescueSubModule.IsInstalled(server));
            AssertKeptFormat(xml);
            // Each entry on lines of its own in the file's tab indent.
            Assert.Contains("\r\n\t\t<SubModule>\r\n\t\t\t<Name value=\"Launcher Cheats\"/>\r\n", File.ReadAllText(xml));
            Assert.Contains("\r\n\t\t<SubModule>\r\n\t\t\t<Name value=\"Coop Save Rescue\"/>\r\n", File.ReadAllText(xml));
        }
        finally
        {
            Delete(root);
        }
    }

    // What the old writers left after six starts: Launcher Cheats and Launcher Battle Size on the line of
    // </SubModules> (the XDocument save), and six Coop Save Rescue inserts that each copied that line in
    // front of every line of the entry.
    private static string SixStartsOfTheOldWriters()
    {
        var cheatsAndBattleSize =
            "<SubModule><Name value=\"Launcher Cheats\" /><DLLName value=\"" + Cheats + "\" /><SubModuleClassType value=\"DedicatedServer.LauncherCheats.CheatsSubModule\" /></SubModule>"
            + "<SubModule><Name value=\"Launcher Battle Size\" /><DLLName value=\"" + BattleSize + "\" /><SubModuleClassType value=\"DedicatedServer.LauncherBattleSize.BattleSizeSubModule\" /></SubModule>";
        var xml = ServerWindowsXml.Replace("\t</SubModules>", "\t" + cheatsAndBattleSize + "</SubModules>");
        for (var start = 0; start < 6; start++)
        {
            var at = xml.LastIndexOf("</SubModules>", StringComparison.Ordinal);
            var lineStart = xml.LastIndexOf('\n', at) + 1;
            var indent = xml[lineStart..at];
            var entry = indent + "    <SubModule>\r\n"
                + indent + "      <Name value=\"Coop Save Rescue\"/>\r\n"
                + indent + "      <DLLName value=\"CoopSaveRescue.dll\"/>\r\n"
                + indent + "      <SubModuleClassType value=\"CoopSaveRescue.SubModule\"/>\r\n"
                + indent + "    </SubModule>\r\n";
            xml = xml.Insert(lineStart, entry);
        }

        return xml;
    }

    [Fact]
    public void A_file_with_six_copies_comes_out_with_one_each_on_the_next_start()
    {
        var root = NewRoot("submodule-collapse");
        try
        {
            var hostPayload = RepairPayload(root);
            var workshop = ThinWorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server, null, hostPayload);
            var xml = Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml");
            // A folder the old tools wrote before the backup came with them.
            File.Delete(xml + ServerSubModuleXml.BackupSuffix);
            WriteWithBom(xml, SixStartsOfTheOldWriters());
            var repeated = File.ReadAllBytes(xml);
            var before = LoadedDlls(xml);
            Assert.Equal(7, before.Count(dll => dll == Cheats));
            Assert.Equal(7, before.Count(dll => dll == BattleSize));
            Assert.Equal(6, before.Count(dll => dll == RescueSubModule.DllName));

            var message = BeServerPrepare.LayOverRepair(server, hostPayload);
            Assert.Contains("Removed 17 repeated entries in DedicatedServer.Windows", message);
            RescueSubModule.Install(server, Write(Path.Combine(root, "rescue", RescueSubModule.DllName), "rescue"));

            Assert.Equal(new[] { "DedicatedServer.Windows.dll", Cheats, BattleSize, RescueSubModule.DllName }, LoadedDlls(xml));
            var after = XDocument.Load(xml).Root!.Element("SubModules")!;
            // No entry nested in another, and the server's own entry keeps its Tags.
            Assert.All(after.Elements("SubModule"), item => Assert.Empty(item.Descendants("SubModule")));
            Assert.Equal("Custom", (string?)after.Descendants("Tag").Single().Attribute("value"));
            AssertKeptFormat(xml);
            // The backup is the file as it was before the collapse, repeats and all.
            Assert.Equal(repeated, File.ReadAllBytes(xml + ServerSubModuleXml.BackupSuffix));
            Assert.Null(BeServerPrepare.LayOverRepair(server, hostPayload));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Collapse_keeps_the_first_entry_per_DLL_and_the_files_spaces_and_line_ends()
    {
        var entry = "    <SubModule>\n      <Name value=\"Coop Save Rescue\"/>\n      <DLLName value=\"CoopSaveRescue.dll\"/>\n      <SubModuleClassType value=\"CoopSaveRescue.SubModule\"/>\n    </SubModule>\n";
        var text = "<Module>\n  <SubModules>\n    <SubModule>\n      <Name value=\"DedicatedServer.Windows\"/>\n      <DLLName value=\"DedicatedServer.Windows.dll\"/>\n    </SubModule>\n"
            + string.Concat(Enumerable.Repeat(entry, 6)) + "  </SubModules>\n</Module>\n";

        var collapsed = ServerSubModuleXml.Collapse(text, out var removed);

        Assert.Equal(5, removed);
        Assert.Equal("<Module>\n  <SubModules>\n    <SubModule>\n      <Name value=\"DedicatedServer.Windows\"/>\n      <DLLName value=\"DedicatedServer.Windows.dll\"/>\n    </SubModule>\n"
            + entry + "  </SubModules>\n</Module>\n", collapsed);
        Assert.Null(ServerSubModuleXml.Collapse(collapsed!, out _));
    }

    [Fact]
    public void Insert_puts_SubModules_end_tag_back_on_its_own_line()
    {
        var text = "<Module>\r\n\t<SubModules>\r\n\t\t<SubModule><Name value=\"A\"/><DLLName value=\"A.dll\"/></SubModule></SubModules>\r\n</Module>";

        var next = ServerSubModuleXml.Insert(text, "B", "B.dll", "B.SubModule")!;

        Assert.EndsWith("</SubModule>\r\n\t\t<SubModule>\r\n\t\t\t<Name value=\"B\"/>\r\n\t\t\t<DLLName value=\"B.dll\"/>\r\n\t\t\t<SubModuleClassType value=\"B.SubModule\"/>\r\n\t\t</SubModule>\r\n\t</SubModules>\r\n</Module>", next);
        Assert.True(ServerSubModuleXml.Has(next, "B.dll", "B"));
        Assert.True(ServerSubModuleXml.Has(next, "a.DLL", "other"));
        Assert.False(ServerSubModuleXml.Has(next, "C.dll", "C"));
    }

    private static string Entry(string name, string dll, string type, string tags = "") =>
        "\t\t<SubModule>\r\n\t\t\t<Name value=\"" + name + "\"/>\r\n\t\t\t<DLLName value=\"" + dll + "\"/>\r\n"
        + "\t\t\t<SubModuleClassType value=\"" + type + "\"/>\r\n" + tags + "\t\t</SubModule>\r\n";

    private static string ListXml(string inner, string openTag = "<SubModules>") =>
        "<Module>\r\n\t<Id value=\"DedicatedServer.Windows\"/>\r\n\t" + openTag + "\r\n" + inner + "\t</SubModules>\r\n</Module>\r\n";

    private static string TempXml(string text, bool bom = true)
    {
        var path = Path.Combine(NewRoot("submodule-xml"), "SubModule.xml");
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        return path;
    }

    // One DLL may carry several SubModules (CoodszFamily has CoopMarriage.dll twice with other classes and tags).
    // Only a true repeat goes; a third party entry, a comment and each class of a shared DLL stay.
    [Fact]
    public void Collapse_keeps_every_class_of_a_shared_DLL_a_third_party_entry_and_a_comment()
    {
        var marriageServer = Entry("Coop Marriage", "CoopMarriage.dll", "CoopMarriage.ServerSubModule",
            "\t\t\t<Tags>\r\n\t\t\t\t<Tag key=\"DedicatedServerType\" value=\"Custom\"/>\r\n\t\t\t</Tags>\r\n");
        var marriageClient = Entry("Coop Marriage", "CoopMarriage.dll", "CoopMarriage.ClientSubModule");
        var thirdParty = Entry("Someone Else Addon", "Someone.Addon.dll", "Someone.Addon.SubModule");
        var rescue = Entry("Coop Save Rescue", "CoopSaveRescue.dll", "CoopSaveRescue.SubModule");
        var path = TempXml(ListXml(marriageServer + marriageClient + "\t\t<!-- kept by hand -->\r\n" + thirdParty
            + string.Concat(Enumerable.Repeat(rescue, 6)) + marriageClient));
        try
        {
            Assert.Equal(6, ServerSubModuleXml.Collapse(path));

            var text = File.ReadAllText(path);
            Assert.Equal(ListXml(marriageServer + marriageClient + "\t\t<!-- kept by hand -->\r\n" + thirdParty + rescue), text);
            Assert.Equal(new[] { "CoopMarriage.dll", "CoopMarriage.dll", "Someone.Addon.dll", "CoopSaveRescue.dll" }, LoadedDlls(path));
            Assert.Equal(0, ServerSubModuleXml.Collapse(path));
        }
        finally
        {
            Delete(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Collapse_and_insert_keep_the_BOM_tabs_and_CRLF_and_back_up_the_file_once()
    {
        var root = NewRoot("submodule-bom");
        try
        {
            var module = ServerWindowsSubModules.ModuleDirectory(root);
            Directory.CreateDirectory(module);
            var xml = Path.Combine(module, "SubModule.xml");
            var repeated = ListXml(Entry("DedicatedServer.Windows", "DedicatedServer.Windows.dll", "X") + Entry("A", "A.dll", "A.S") + Entry("A", "A.dll", "A.S"));
            File.WriteAllText(xml, repeated, new UTF8Encoding(true));
            var original = File.ReadAllBytes(xml);

            // A collapse alone backs the file up first.
            Assert.Equal(1, ServerSubModuleXml.Collapse(xml));
            Assert.Equal(original, File.ReadAllBytes(xml + ServerSubModuleXml.BackupSuffix));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(xml)[..3]);

            // The text insert keeps the BOM, the tabs and CRLF, and never replaces the first backup.
            ServerWindowsSubModules.EnsureEntry(root, "B", "B.dll", "B.S");
            var bytes = File.ReadAllBytes(xml);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal(ListXml(Entry("DedicatedServer.Windows", "DedicatedServer.Windows.dll", "X") + Entry("A", "A.dll", "A.S") + Entry("B", "B.dll", "B.S")),
                Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            Assert.Equal(original, File.ReadAllBytes(xml + ServerSubModuleXml.BackupSuffix));

            // EnsureEntry on a folder with no backup yet makes one before it writes.
            File.Delete(xml + ServerSubModuleXml.BackupSuffix);
            var before = File.ReadAllBytes(xml);
            ServerWindowsSubModules.EnsureEntry(root, "C", "C.dll", "C.S");
            Assert.Equal(before, File.ReadAllBytes(xml + ServerSubModuleXml.BackupSuffix));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_file_without_a_BOM_stays_without_one()
    {
        var path = TempXml(ListXml(Entry("A", "A.dll", "A.S") + Entry("A", "A.dll", "A.S")), bom: false);
        try
        {
            Assert.Equal(1, ServerSubModuleXml.Collapse(path));
            Assert.Equal((byte)'<', File.ReadAllBytes(path)[0]);
        }
        finally
        {
            Delete(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Invalid_XML_is_left_alone_and_Has_falls_back_to_the_text()
    {
        var broken = ListXml(Entry("Coop Save Rescue", "CoopSaveRescue.dll", "S") + Entry("Coop Save Rescue", "CoopSaveRescue.dll", "S")).Replace("</Module>", "");
        var path = TempXml(broken);
        try
        {
            var before = File.ReadAllBytes(path);
            Assert.Equal(0, ServerSubModuleXml.Collapse(path));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ServerSubModuleXml.BackupSuffix));
            Assert.True(ServerSubModuleXml.Has(broken, "other.dll", "Coop Save Rescue"));
            Assert.False(ServerSubModuleXml.Has(broken, "other.dll", "Other"));
        }
        finally
        {
            Delete(Path.GetDirectoryName(path)!);
        }
    }

    // <SubModules> with an attribute is handled like the plain tag; its attribute stays.
    [Fact]
    public void A_SubModules_tag_with_attributes_is_collapsed_and_keeps_them()
    {
        var path = TempXml(ListXml(Entry("A", "A.dll", "A.S") + Entry("A", "A.dll", "A.S"), "<SubModules note=\"x\">"));
        try
        {
            Assert.Equal(1, ServerSubModuleXml.Collapse(path));
            Assert.Equal(ListXml(Entry("A", "A.dll", "A.S"), "<SubModules note=\"x\">"), File.ReadAllText(path));
            var inserted = ServerSubModuleXml.Insert(File.ReadAllText(path), "B", "B.dll", "B.S");
            Assert.Equal(ListXml(Entry("A", "A.dll", "A.S") + Entry("B", "B.dll", "B.S"), "<SubModules note=\"x\">"), inserted);
        }
        finally
        {
            Delete(Path.GetDirectoryName(path)!);
        }
    }
}
