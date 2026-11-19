using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Antivirus aware file checks (CR-88BP7). A file a tool put there and is gone later only changes the
// wording of a problem the tool has anyway. A file that never existed keeps the usual message, and the
// Fix antivirus window only shows folders and opens Windows Security.
public sealed class AntivirusWatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-antivirus-" + Guid.NewGuid().ToString("N"));

    public AntivirusWatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string JsonPath => Path.Combine(_root, "settings", AntivirusWatch.FileName);

    private AntivirusWatch Watch() => new(JsonPath);

    private string Server => Path.Combine(_root, "Server");

    private string Touch(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void A_file_that_existed_and_is_gone_gives_the_antivirus_text()
    {
        var exe = Touch("Server", "BannerlordCoopServer.exe");
        Watch().Remember([Server], [exe]);
        File.Delete(exe);

        var gone = Watch().Gone([Server]);

        Assert.Equal([Path.GetFullPath(exe)], gone);
        var text = AntivirusText.Taken(AntivirusFiles.Shown(gone[0], Server), "Prepare server");
        Assert.StartsWith("Your antivirus most likely removed BannerlordCoopServer.exe.", text);
        Assert.Contains("its quarantine list", text);
        Assert.Contains("then click Prepare server again.", text);
        Assert.EndsWith(AntivirusText.PlaceServer, text);
    }

    [Fact]
    public void A_file_that_never_existed_is_not_called_antivirus()
    {
        Directory.CreateDirectory(Server);
        var exe = Path.Combine(Server, "BannerlordCoopServer.exe");
        Watch().Remember([Server], [exe]);

        Assert.Empty(Watch().Gone([Server]));
        Assert.Equal("usual", AntivirusWording.Reword("usual", exe, Watch().Gone([Server]), Server, null, "Start server"));
    }

    [Fact]
    public void A_restored_file_clears_it()
    {
        var dotnet = Touch("Server", "engine", "dotnet", "dotnet.exe");
        Watch().Remember([Server], [dotnet]);
        File.Delete(dotnet);
        Assert.Single(Watch().Gone([Server]));

        Touch("Server", "engine", "dotnet", "dotnet.exe");

        Assert.Empty(Watch().Gone([Server]));
    }

    [Fact]
    public void A_deleted_folder_or_another_folder_is_not_antivirus()
    {
        var dll = Touch("Server", "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll");
        var other = Touch("Other", "BannerlordCoopServer.exe");
        Watch().Remember([Server, Path.Combine(_root, "Other")], [dll, other]);
        Directory.Delete(Path.GetDirectoryName(dll)!, recursive: true);
        File.Delete(other);

        Assert.Empty(Watch().Gone([Server]));
    }

    [Fact]
    public void A_stale_entry_is_forgotten_by_the_next_good_list()
    {
        var kept = Touch("Server", "engine", "bin", "Win64_Shipping_Server", "Kept.dll");
        var renamed = Touch("Server", "engine", "bin", "Win64_Shipping_Server", "Old.dll");
        var elsewhere = Touch("Other", "Other.dll");
        Watch().Remember([Path.Combine(_root, "Other")], [elsewhere]);
        Watch().Remember([Server], [kept, renamed]);

        // The next tool version no longer ships Old.dll, and the host removed it.
        Watch().Remember([Server], [kept]);
        File.Delete(renamed);
        Assert.Empty(Watch().Gone([Server]));

        // A file removed on purpose and then a good start forgets it too.
        File.Delete(kept);
        Watch().Remember([Server], [kept]);
        Assert.Empty(Watch().Gone([Server]));

        // Another folder's list is left as it was.
        File.Delete(elsewhere);
        Assert.Equal([Path.GetFullPath(elsewhere)], Watch().Gone([Path.Combine(_root, "Other")]));
    }

    [Fact]
    public void A_watch_without_a_file_remembers_nothing()
    {
        var exe = Touch("Server", "BannerlordCoopServer.exe");
        var watch = new AntivirusWatch(null);
        watch.Remember([Server], [exe]);
        File.Delete(exe);

        Assert.Empty(watch.Gone([Server]));
    }

    [Fact]
    public void A_broken_json_keeps_the_last_good_list()
    {
        var exe = Touch("Server", "BannerlordCoopServer.exe");
        var dll = Touch("Server", "Coop.dll");
        Watch().Remember([Server], [exe]);
        Watch().Remember([Server], [exe, dll]);
        File.WriteAllText(JsonPath, "{ not json");
        File.Delete(exe);

        Assert.Equal([Path.GetFullPath(exe)], Watch().Gone([Server]));

        // A good start writes a new list and never keeps the broken one as the backup.
        Touch("Server", "BannerlordCoopServer.exe");
        Watch().Remember([Server], [exe, dll]);
        Assert.NotNull(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(JsonPath)));
        Assert.NotNull(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(JsonPath + ".bak")));

        File.WriteAllText(JsonPath, "broken");
        File.WriteAllText(JsonPath + ".bak", "broken");
        Assert.Empty(Watch().Gone([Server]));
    }

    [Fact]
    public void A_locked_json_never_throws()
    {
        var exe = Touch("Server", "BannerlordCoopServer.exe");
        Watch().Remember([Server], [exe]);
        using (new FileStream(JsonPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Watch().Remember([Server], [exe]);
            File.Delete(exe);
            // The main file is locked, so the last good list (none yet) is read. Nothing throws.
            _ = Watch().Gone([Server]);
        }

        Assert.Equal([Path.GetFullPath(exe)], Watch().Gone([Server]));
    }

    [Fact]
    public void Two_writers_keep_both_lists()
    {
        var first = Enumerable.Range(0, 20).Select(i => Touch("A", "a" + i + ".dll")).ToList();
        var second = Enumerable.Range(0, 20).Select(i => Touch("B", "b" + i + ".dll")).ToList();
        Parallel.For(0, 40, i =>
        {
            if (i % 2 == 0)
                new AntivirusWatch(JsonPath).Remember([Path.Combine(_root, "A")], first);
            else
                new AntivirusWatch(JsonPath).Remember([Path.Combine(_root, "B")], second);
        });

        File.Delete(first[3]);
        File.Delete(second[7]);
        Assert.Equal([Path.GetFullPath(first[3]), Path.GetFullPath(second[7])], Watch().Gone([Path.Combine(_root, "A"), Path.Combine(_root, "B")]));
    }

    [Fact]
    public void A_workshop_file_steam_is_updating_is_not_called_antivirus()
    {
        var library = Path.Combine(_root, "Steam");
        var coop = Path.Combine(library, "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId);
        var exe = Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "DedicatedServer", "BannerlordCoopServer.exe");
        Watch().Remember([coop], [exe]);
        File.Delete(exe);
        var acf = Path.Combine(library, "steamapps", "workshop", "appworkshop_261550.acf");

        File.WriteAllText(acf, Acf(installed: "1", details: ("1", "2")));
        Assert.Empty(Watch().GoneSettled([coop], coop, TimeSpan.Zero));

        File.WriteAllText(acf, Acf(installed: "2", details: ("2", "2")));
        Assert.Single(Watch().GoneSettled([coop], coop, TimeSpan.Zero));

        Directory.CreateDirectory(Path.Combine(library, "steamapps", "workshop", "downloads", "261550", AntivirusFiles.CoopWorkshopId));
        Assert.Empty(Watch().GoneSettled([coop], coop, TimeSpan.Zero));
    }

    [Fact]
    public void Only_the_workshop_coop_itself_pending_in_steam_counts()
    {
        var workshop = Path.Combine(_root, "workshop");
        var acf = Path.Combine(workshop, "appworkshop_261550.acf");
        Directory.CreateDirectory(workshop);

        // Another mod's update sets the app level NeedsUpdate and NeedsDownload; Coop is current.
        File.WriteAllText(acf, Acf(installed: "7", details: ("7", "7")));
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));

        File.WriteAllText(acf, Acf(installed: "7", details: ("7", "8")));
        Assert.True(SteamWorkshopAcf.CoopPending(workshop));

        // The installed manifest (what is on disk) is behind, though the details one already says latest.
        File.WriteAllText(acf, Acf(installed: "6", details: ("7", "7")));
        Assert.True(SteamWorkshopAcf.CoopPending(workshop));

        // Subscribed, not downloaded yet.
        File.WriteAllText(acf, Acf(installed: null, details: ("0", "8")));
        Assert.True(SteamWorkshopAcf.CoopPending(workshop));

        // A blank installed manifest falls back to the details one.
        File.WriteAllText(acf, Acf(installed: "", details: ("7", "7")));
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));
        File.WriteAllText(acf, Acf(installed: "", details: ("7", "8")));
        Assert.True(SteamWorkshopAcf.CoopPending(workshop));

        // The installed manifest is already the latest while the details one lags behind.
        File.WriteAllText(acf, Acf(installed: "8", details: ("7", "8")));
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));

        // Coop's id only under another item's keys is not Coop's entry.
        File.WriteAllText(acf, "\"AppWorkshop\"\n{\n\t\"WorkshopItemDetails\"\n\t{\n\t\t\"111\"\n\t\t{\n\t\t\t\"" + SteamWorkshopAcf.CoopWorkshopId + "\"\t\t\"1\"\n\t\t}\n\t}\n}");
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));

        File.WriteAllText(acf, "\"AppWorkshop\"\n{\n\t\"WorkshopItemDetails\"\n\t{");
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));
        File.Delete(acf);
        Assert.False(SteamWorkshopAcf.CoopPending(workshop));
        Assert.False(SteamWorkshopAcf.CoopPending(Path.Combine(_root, "no such folder")));

        Directory.CreateDirectory(Path.Combine(workshop, "downloads", "261550", SteamWorkshopAcf.CoopWorkshopId));
        Assert.True(SteamWorkshopAcf.CoopPending(workshop));
    }

    // appworkshop_261550.acf with another mod pending (app level flags on, its manifest behind) and Coop's
    // installed manifest and details (manifest, latest_manifest); null leaves Coop out of WorkshopItemsInstalled.
    private static string Acf(string? installed, (string Manifest, string Latest) details)
    {
        var coop = SteamWorkshopAcf.CoopWorkshopId;
        var installedCoop = installed != null ? $"\t\t\"{coop}\"\n\t\t{{\n\t\t\t\"size\"\t\t\"1\"\n\t\t\t\"manifest\"\t\t\"{installed}\"\n\t\t}}\n" : "";
        return "\"AppWorkshop\"\n{\n\t\"appid\"\t\t\"261550\"\n\t\"NeedsUpdate\"\t\t\"1\"\n\t\"NeedsDownload\"\t\t\"1\"\n"
            + "\t\"WorkshopItemsInstalled\"\n\t{\n\t\t\"2859188632\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"5\"\n\t\t}\n" + installedCoop + "\t}\n"
            + "\t\"WorkshopItemDetails\"\n\t{\n\t\t\"2859188632\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"5\"\n\t\t\t\"latest_manifest\"\t\t\"6\"\n\t\t}\n"
            + $"\t\t\"{coop}\"\n\t\t{{\n\t\t\t\"manifest\"\t\t\"{details.Manifest}\"\n\t\t\t\"latest_manifest\"\t\t\"{details.Latest}\"\n\t\t}}\n\t}}\n}}";
    }

    [Fact]
    public void The_wording_only_changes_a_problem_the_tool_has_anyway()
    {
        var exe = Touch("Server", "BannerlordCoopServer.exe");
        var dll = Touch("Server", "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll");
        Watch().Remember([Server], [exe, dll]);
        File.Delete(exe);
        File.Delete(dll);
        var gone = Watch().Gone([Server]);

        Assert.Equal("usual", AntivirusWording.Reword("usual", Path.Combine(Server, "Other.exe"), gone, Server, null, "Start server"));
        Assert.Equal("usual", AntivirusWording.Reword("usual", null, gone, Server, null, "Start server"));
        Assert.Equal(AntivirusText.Taken("BannerlordCoopServer.exe", "Start server") + "\n" + AntivirusText.RemovedYourselfSetFolder,
            AntivirusWording.Reword("usual", exe, gone, Server, null, "Start server", AntivirusText.RemovedYourselfSetFolder));
        Assert.Equal(AntivirusText.WorkshopTaken("BannerlordCoopServer.exe"), AntivirusWording.Reword("usual", exe, gone, Server, Server, "Start server"));

        Assert.Null(AntivirusWording.Note([], Server, null));
        var note = AntivirusWording.Note(gone, Server, null);
        Assert.Equal(AntivirusText.Noticed("BannerlordCoopServer.exe, engine\\bin\\Win64_Shipping_Server\\0Harmony.dll"), note);
        Assert.DoesNotContain('\n', note);
    }

    [Fact]
    public void The_start_flows_never_stop_on_the_watch_alone()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var coodszStart = File.ReadAllText(Path.Combine(src, "CoodszHost", "MainWindow.ServerControl.cs")).Replace("\r\n", "\n");
        // The same exe check as before stops the start. The watch only picks its words.
        Assert.Contains("if (!File.Exists(Executable))\n            throw new InvalidOperationException(ServerMissingText(antivirusGone));\n        NoteGoneFiles(antivirusGone);", coodszStart);
        var taomStart = File.ReadAllText(Path.Combine(src, "TaomCoopHost", "MainWindow.ServerControl.cs"));
        Assert.DoesNotContain("StopIf", taomStart);
        Assert.DoesNotContain("StopIf", coodszStart);
        Assert.Contains("RememberServerFiles(serverRoot);", taomStart);
        Assert.Contains("RememberServerFiles(server);", taomStart);
        foreach (var tool in new[] { "CoodszHost", "TaomCoopHost" })
        {
            var watch = File.ReadAllText(Path.Combine(src, tool, "MainWindow.Antivirus.cs"));
            Assert.DoesNotContain("throw ", watch);
            Assert.DoesNotContain("return false", watch);
            Assert.DoesNotContain("SetControl(", watch);
        }
    }

    [Fact]
    public void The_coodsz_host_list_has_every_file_the_repair_and_compat_copy_in()
    {
        Directory.CreateDirectory(Server);
        Touch("Repair", "Start-ModdedServer.ps1");
        Touch("Repair", "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll");
        Touch("Repair", "engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.LauncherCheats.dll");
        var repair = Path.Combine(_root, "Repair");
        ServerFolderRepair.Ensure(Server, repair, "test");
        Touch("Compat", "SubModule.xml");
        Touch("Compat", "bin", "Win64_Shipping_Server", "AD1259CoopCompat.dll");
        AD1259Server.InstallCompat(Server, Path.Combine(_root, "Compat"));

        var watched = ServerFileWatch.Files(Server, null, repair, [(AD1259Stack.CompatModuleId, Path.Combine(_root, "Compat"))])
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copied = Directory.EnumerateFiles(Server, "*.dll", SearchOption.AllDirectories).ToList();

        Assert.Equal(3, copied.Count);
        foreach (var dll in copied)
            Assert.Contains(Path.GetFullPath(dll), watched);
        Assert.Contains(Path.GetFullPath(Path.Combine(Server, AntivirusFiles.ServerExeName)), watched);
        Assert.Contains(Path.GetFullPath(Path.Combine(Server, "engine", "dotnet", "dotnet.exe")), watched);
    }

    [Fact]
    public void The_taom_list_has_every_file_prepare_and_the_start_repair_put_back()
    {
        var watched = TaomServerFiles.Watched(Server, null).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ServerProgramRepair.Files.Where(file => file.Target.EndsWith(".dll") || file.Target.EndsWith(".exe")))
            Assert.Contains(Path.Combine(Server, file.Target), watched);
        foreach (var file in ServerProgramRepair.EngineBinFiles)
            Assert.Contains(Path.Combine(Server, ServerProgramRepair.EngineBin, file.Name), watched);
        foreach (var file in ServerProgramRepair.ModuleFiles)
            Assert.Contains(Path.Combine(Server, file.Target), watched);
        Assert.Contains(Path.Combine(ServerWindowsSubModules.BinDirectory(Server), RescueSubModule.DllName), watched);
        Assert.Contains(Path.Combine(Server, "engine", "dotnet", "dotnet.exe"), watched);
    }

    [Fact]
    public void The_workshop_problem_tells_no_coop_from_a_coop_without_its_server_program()
    {
        Assert.Equal(AntivirusText.NoWorkshopCoop("Prepare server"), AntivirusFiles.WorkshopServerProblem(null, "Prepare server"));

        var library = Path.Combine(_root, "Steam");
        Assert.Null(AntivirusFiles.WorkshopCoop([library]));
        var coop = Path.Combine(library, "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId);
        Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "SubModule.xml");
        Assert.Equal(coop, AntivirusFiles.WorkshopCoop([library]));

        // Never seen there, so not antivirus. The download is incomplete and gets the resubscribe step.
        var noExe = AntivirusFiles.WorkshopServerProblem(coop, "Prepare server", Watch().Gone([coop]));
        Assert.Equal(AntivirusText.WorkshopIncomplete("DedicatedServer\\BannerlordCoopServer.exe", "Prepare server"), noExe);
        Assert.Contains("unsubscribe from Bannerlord Coop and subscribe again", noExe);
        Assert.DoesNotContain("antivirus", noExe);

        Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "DedicatedServer", "BannerlordCoopServer.exe");
        Assert.Contains("engine\\dotnet\\dotnet.exe", AntivirusFiles.WorkshopServerProblem(coop, "Prepare server"));

        var server = Path.Combine(coop, "DedicatedServer");
        foreach (var file in AntivirusFiles.Engine(server))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }

        Assert.Null(AntivirusFiles.WorkshopServerProblem(coop, "Prepare server"));

        // Every server file there but Steam is still updating Coop: wait for Steam, never copy mid swap.
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "workshop", "downloads", "261550", AntivirusFiles.CoopWorkshopId));
        Assert.Equal(AntivirusText.WorkshopDownloading("Prepare server"), AntivirusFiles.WorkshopServerProblem(coop, "Prepare server"));
    }

    [Fact]
    public void A_taom_compat_dll_removed_after_a_good_start_reads_as_gone()
    {
        Touch("Kit", "Artifacts", "TAOM.CoopCompat", "0.3.25+abc", "Modules", "TAOM.CoopCompat", "SubModule.xml");
        Touch("Kit", "Artifacts", "TAOM.CoopCompat", "0.3.25+abc", "Modules", "TAOM.CoopCompat", "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll");
        var kit = TaomServerFiles.KitCompatModule(Path.Combine(_root, "Kit"));
        Assert.NotNull(kit);
        var dll = Touch("Server", "engine", "Modules", "TAOM.CoopCompat", "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll");
        Watch().Remember([Server], TaomServerFiles.Watched(Server, null, kit));

        File.Delete(dll);

        Assert.Equal([Path.GetFullPath(dll)], AntivirusFiles.StillShipped(Watch().Gone([Server]), TaomServerFiles.Watched(Server, null, kit)));
        // What is on disk now no longer has it, which is why the list comes from the kit.
        Assert.Empty(AntivirusFiles.StillShipped(Watch().Gone([Server]), TaomServerFiles.Watched(Server, null)));
        Assert.Null(TaomServerFiles.KitCompatModule(Path.Combine(_root, "No kit")));
    }

    [Fact]
    public void A_workshop_server_file_seen_before_and_gone_says_antivirus()
    {
        var coop = Path.Combine(_root, "Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId);
        Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "SubModule.xml");
        var exe = Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "DedicatedServer", "BannerlordCoopServer.exe");
        Watch().Remember([coop], [exe]);
        File.Delete(exe);

        var problem = AntivirusFiles.WorkshopServerProblem(coop, "Prepare server", Watch().Gone([coop]));

        Assert.Equal(AntivirusText.WorkshopTaken("DedicatedServer\\BannerlordCoopServer.exe"), problem);
        Assert.Contains("unsubscribe from Bannerlord Coop in Steam and subscribe again", problem);
    }

    [Fact]
    public void A_workshop_coop_steam_is_still_downloading_says_let_steam_finish()
    {
        var library = Path.Combine(_root, "Steam");
        var coop = Path.Combine(library, "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId);
        // Steam writes SubModule.xml first, the server program comes later.
        Touch("Steam", "steamapps", "workshop", "content", "261550", AntivirusFiles.CoopWorkshopId, "SubModule.xml");
        File.WriteAllText(Path.Combine(library, "steamapps", "workshop", "appworkshop_261550.acf"), Acf(installed: null, details: ("0", "5")));

        var problem = AntivirusFiles.WorkshopServerProblem(coop, "Prepare server", Watch().Gone([coop]));

        Assert.Equal(AntivirusText.WorkshopDownloading("Prepare server"), problem);
        Assert.DoesNotContain("antivirus", problem);
    }

    [Fact]
    public void A_dll_a_tool_update_renamed_or_dropped_is_never_called_antivirus()
    {
        var game = Path.Combine(_root, "Game");
        var oldPayload = Path.Combine(_root, "PayloadV1");
        var newPayload = Path.Combine(_root, "PayloadV2");
        Touch("PayloadV1", "SubModule.xml");
        Touch("PayloadV1", "bin", "Win64_Shipping_Client", "Kept.dll");
        Touch("PayloadV1", "bin", "Win64_Shipping_Client", "OldName.dll");
        Touch("PayloadV2", "SubModule.xml");
        Touch("PayloadV2", "bin", "Win64_Shipping_Client", "Kept.dll");
        Touch("PayloadV2", "bin", "Win64_Shipping_Client", "NewName.dll");
        var watch = Watch();
        Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", oldPayload, watch));

        // The update cleaned up OldName.dll before the first Connect of the new version.
        var bin = Path.Combine(game, "Modules", "DedicatedServer.Test", "bin", "Win64_Shipping_Client");
        File.Delete(Path.Combine(bin, "OldName.dll"));

        Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", newPayload, watch));

        // The same rule for a server tool's list, built from the payloads the running version ships.
        Directory.CreateDirectory(Server);
        Touch("RepairV2", "engine", "bin", "Win64_Shipping_Server", "New.dll");
        var oldDll = Touch("Server", "engine", "bin", "Win64_Shipping_Server", "Old.dll");
        Watch().Remember([Server], [oldDll]);
        File.Delete(oldDll);
        var shipped = ServerFileWatch.Files(Server, null, Path.Combine(_root, "RepairV2"), []);
        Assert.Single(Watch().Gone([Server]));
        Assert.Empty(AntivirusFiles.StillShipped(Watch().Gone([Server]), shipped));
    }

    [Fact]
    public void A_client_add_on_dll_that_was_gone_is_named_on_the_next_connect()
    {
        var game = Path.Combine(_root, "Game");
        var payload = Path.Combine(_root, "Payload");
        Touch("Payload", "SubModule.xml");
        Touch("Payload", "bin", "Win64_Shipping_Client", "DedicatedServer.Test.dll");
        var watch = Watch();

        Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch));
        var dll = Path.Combine(game, "Modules", "DedicatedServer.Test", "bin", "Win64_Shipping_Client", "DedicatedServer.Test.dll");
        File.Delete(dll);

        var note = ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch);

        Assert.Equal(AntivirusText.TakenPutBack("DedicatedServer.Test\\bin\\Win64_Shipping_Client\\DedicatedServer.Test.dll", "Connect"), note);
        Assert.True(File.Exists(dll));
        Assert.Null(ClientAddOns.Install(game, "DedicatedServer.Test", "Test module", payload, watch));
    }

    [Fact]
    public void The_window_lists_only_real_folders_once()
    {
        var tool = Path.Combine(_root, "Tool");
        var game = Path.Combine(_root, "Game");
        Directory.CreateDirectory(tool);
        Directory.CreateDirectory(Path.Combine(game, "Modules"));

        var folders = AntivirusFolders.For(tool, tool + "\\", Path.Combine(_root, "NoServer"), game, null);

        Assert.Equal([AntivirusText.ToolFolder, AntivirusText.ModulesFolder], folders.Select(folder => folder.Label));
        Assert.Equal(Path.Combine(game, "Modules"), folders[1].Path);
    }

    [Fact]
    public void The_antivirus_text_has_no_colons_and_no_hyphens_between_words()
    {
        var texts = typeof(AntivirusText).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name != nameof(AntivirusText.SecurityPage))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Concat([
                AntivirusText.Taken("x.dll", "Start server"), AntivirusText.TakenPutBack("x.dll", "Connect"),
                AntivirusText.WorkshopTaken("x.exe"), AntivirusText.NoWorkshopCoop("Prepare server"),
                AntivirusText.Noticed("x.dll"), AntivirusText.OtherServer("x.exe", "D"),
                AntivirusText.WorkshopDownloading("Prepare server"), AntivirusText.WorkshopIncomplete("x.exe", "Prepare server")
            ])
            .ToList();
        Assert.NotEmpty(texts);
        foreach (var text in texts)
        {
            Assert.DoesNotContain(":", text);
            Assert.DoesNotMatch(new Regex("[A-Za-z]-[A-Za-z]"), text);
            Assert.DoesNotContain("exclusions", text.Replace("Add or remove exclusions", ""));
        }
    }

    [Fact]
    public void The_window_only_opens_windows_security_and_changes_nothing()
    {
        Assert.Equal("windowsdefender://threat", AntivirusText.SecurityPage);
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "AntivirusWindow.cs"));
        Assert.Single(Regex.Matches(window, @"ShellOpen\.TryOpen\("));
        Assert.Contains("ShellOpen.TryOpen(AntivirusText.SecurityPage, out _)", window);
        Assert.DoesNotContain("Math.Max(480", window);
        Assert.Contains("DockPanel.SetDock(close, Dock.Bottom);", window);

        var handlers = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "MainWindow.Antivirus.cs", SearchOption.AllDirectories).ToList();
        Assert.Equal(9, handlers.Count);
        foreach (var source in handlers.Append(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "AntivirusWindow.cs"))
                     .Append(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Support", "AntivirusWatch.cs")))
        {
            var text = File.ReadAllText(source);
            foreach (var banned in new[] { "Process.Start", "ProcessStartInfo", "Registry", "Add-MpPreference", "MpPreference", "powershell", "cmd.exe" })
                Assert.False(text.Contains(banned, StringComparison.OrdinalIgnoreCase), source + " has " + banned);
        }
    }

    [Theory]
    [InlineData("CoodszHost")]
    [InlineData("AD1259Host")]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    [InlineData("RealmOfThronesHost")]
    [InlineData("CoodszCoopAndFriends")]
    [InlineData("AD1259Launcher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("RealmOfThronesLauncher")]
    public void Every_tool_has_the_fix_antivirus_window(string tool)
    {
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "src", tool, tool + ".csproj"));
        Assert.Contains(@"..\BannerlordCoopLauncher\AntivirusWindow.cs", project);
        // AD1259Host builds Coodsz Host's own source, the Coop client builds BannerlordCoopLauncher's.
        var source = tool switch { "AD1259Host" => "CoodszHost", "CoodszCoopAndFriends" => "BannerlordCoopLauncher", _ => tool };
        Assert.Contains("Click=\"FixAntivirus_Click\"", File.ReadAllText(Path.Combine(RepoRoot(), "src", source, "MainWindow.xaml")));
        Assert.Contains("private void FixAntivirus_Click(", File.ReadAllText(Path.Combine(RepoRoot(), "src", source, "MainWindow.Antivirus.cs")));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

// The antivirus message with the Set server folder sentence added is translated whole, both sentences.
[Collection(TranslatorLanguage.Name)]
public sealed class AntivirusWordingLanguageTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void The_taken_text_with_the_set_folder_sentence_is_translated(string language)
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(language);
            var text = AntivirusText.Taken("BannerlordCoopServer.exe", "Start server") + "\n" + AntivirusText.RemovedYourselfSetFolder;
            var shown = BannerlordCoopLauncher.Core.Localization.Translator.T(text);
            Assert.DoesNotContain("Your antivirus most likely", shown);
            Assert.DoesNotContain("If you removed it yourself", shown);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
