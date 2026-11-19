using System.IO.Compression;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// Wave A of the two exe plan: settings move to %LOCALAPPDATA%\Coodsz\<Product> (copied once from beside
// the exe), and the folders that shipped beside the exe ride inside it and are written out on first use.
// ToolPayload keeps one written out folder per payload for the whole process, so these tests run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ToolPayloadCollection
{
    public const string Name = "ToolPayload";
}

[Collection(ToolPayloadCollection.Name)]
public sealed class ToolDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-tooldata-" + Guid.NewGuid().ToString("N"));
    private readonly ToolPayload.PayloadSource _source = ToolPayload.Source;

    public ToolDataTests()
    {
        Directory.CreateDirectory(_dir);
        ToolPayload.Reset();
    }

    public void Dispose()
    {
        ToolPayload.Source = _source;
        ToolPayload.Reset();
        PayloadWriter.LockWait = TimeSpan.FromMinutes(2);
        ToolDataFolder.MaxFolderBytes = 200L * 1024 * 1024;
        ToolPayload.RetryAfter = TimeSpan.FromSeconds(30);
        ToolPayload.Log = ToolData.Log;
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private string ExeFolder()
    {
        var exe = Path.Combine(_dir, "exe");
        Directory.CreateDirectory(exe);
        return exe;
    }

    [Fact]
    public void A_blank_or_missing_server_folder_never_writes_out_the_repair_files()
    {
        // ServerFolderRepair.DefaultPayload writes the files out of the exe, so Ensure only asks for it
        // once the server folder is there, as Coodsz Host did before the shared class.
        var asked = 0;
        ToolPayload.Source = new ToolPayload.PayloadSource(
            _ => { asked++; return null; },
            () => { asked++; return []; },
            () => { asked++; return null; },
            ExeFolder());
        ToolPayload.Reset();

        Assert.Null(BannerlordCoopLauncher.Core.Launch.ServerFolderRepair.Ensure("", null, "test"));
        Assert.Null(BannerlordCoopLauncher.Core.Launch.ServerFolderRepair.Ensure(Path.Combine(_dir, "no-server"), null, "test"));
        Assert.Equal(0, asked);

        // A real server folder does ask for it.
        var server = Path.Combine(_dir, "server");
        Directory.CreateDirectory(server);
        BannerlordCoopLauncher.Core.Launch.ServerFolderRepair.Ensure(server, null, "test");
        Assert.NotEqual(0, asked);
    }

    [Fact]
    public void First_start_copies_the_old_files_and_folders_once_and_leaves_them_beside_the_exe()
    {
        var exe = ExeFolder();
        File.WriteAllText(Path.Combine(exe, "host.json"), "old host");
        Directory.CreateDirectory(Path.Combine(exe, "save-settings", "world"));
        File.WriteAllText(Path.Combine(exe, "save-settings", "world", "a.json"), "save");
        var data = new ToolDataFolder(Path.Combine(_dir, "appdata"), ToolData.Coop);

        var copied = data.CopyOldFiles(exe, "coop-server", ["host.json", "save-settings", "cheats.json"]);

        Assert.Equal(new[] { "host.json", "save-settings" }, copied);
        Assert.Equal("old host", File.ReadAllText(Path.Combine(data.Folder, "host.json")));
        Assert.Equal("save", File.ReadAllText(Path.Combine(data.Folder, "save-settings", "world", "a.json")));
        Assert.False(Directory.Exists(Path.Combine(data.Folder, "save-settings.partial")));
        // A copy, so an older version started from the exe folder still has its settings.
        Assert.True(File.Exists(Path.Combine(exe, "host.json")));
        Assert.True(File.Exists(data.MarkerPath("coop-server")));

        // The marker stops a second copy of a file nobody changed since, even when the AppData copy is gone.
        File.Delete(Path.Combine(data.Folder, "host.json"));
        File.SetLastWriteTimeUtc(Path.Combine(exe, "host.json"), DateTime.UtcNow.AddDays(-1));
        Assert.Empty(data.CopyOldFiles(exe, "coop-server", ["host.json"]));
        Assert.False(File.Exists(Path.Combine(data.Folder, "host.json")));
    }

    [Fact]
    public void A_downgrade_then_upgrade_takes_the_newer_file_and_keeps_the_replaced_one_as_bak()
    {
        const string current = "Coodsz Coop Server Tool.exe";
        var exe = ExeFolder();
        // The older version is still beside the new exe under its old name.
        File.WriteAllText(Path.Combine(exe, "CoodszHost.exe"), "old exe");
        File.WriteAllText(Path.Combine(exe, "host.json"), "first");
        Directory.CreateDirectory(Path.Combine(exe, "save-settings"));
        File.WriteAllText(Path.Combine(exe, "save-settings", "a.json"), "save one");
        File.SetLastWriteTimeUtc(Path.Combine(exe, "save-settings", "a.json"), DateTime.UtcNow.AddHours(-4));
        var data = new ToolDataFolder(Path.Combine(_dir, "appdata"), ToolData.Coop);
        data.CopyOldFiles(exe, "coop-server", ["host.json", "save-settings"], current);
        File.SetLastWriteTimeUtc(data.MarkerPath("coop-server"), DateTime.UtcNow.AddHours(-3));
        // The new version changed its own copy, then the older version run from the exe folder changed the old one.
        File.WriteAllText(Path.Combine(data.Folder, "host.json"), "new version");
        File.SetLastWriteTimeUtc(Path.Combine(data.Folder, "host.json"), DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(Path.Combine(exe, "host.json"), "older version");
        File.WriteAllText(Path.Combine(exe, "save-settings", "b.json"), "save two");

        var copied = data.CopyOldFiles(exe, "coop-server", ["host.json", "save-settings"], current);

        Assert.Equal(new[] { "host.json", Path.Combine("save-settings", "b.json") }, copied);
        Assert.Equal("older version", File.ReadAllText(Path.Combine(data.Folder, "host.json")));
        Assert.Equal("new version", File.ReadAllText(Path.Combine(data.Folder, "host.json.bak")));
        Assert.Equal("save two", File.ReadAllText(Path.Combine(data.Folder, "save-settings", "b.json")));
        Assert.Contains("host.json.bak", File.ReadAllText(Path.Combine(data.Folder, ToolDataFolder.LogName)));

        // Taken once: the marker moved on, so the next start leaves the newer copy here alone.
        File.WriteAllText(Path.Combine(data.Folder, "host.json"), "changed again here");
        Assert.Empty(data.CopyOldFiles(exe, "coop-server", ["host.json", "save-settings"], current));
        Assert.Equal("changed again here", File.ReadAllText(Path.Combine(data.Folder, "host.json")));
    }

    // Settings live per user in %LOCALAPPDATA%. Once no old name exe is left beside the tool, a newer file in
    // the exe folder (a shared, writable folder) never replaces the per user copy.
    [Theory]
    [InlineData("Coodsz Coop Server Tool.exe", null)]
    [InlineData("Coodsz Coop Client Launcher.exe", null)]
    [InlineData("Coodsz Coop Client Launcher.exe", "Coodsz and Friends.exe")]
    [InlineData("Coodsz Coop Client Launcher.exe", "Coodsz Coop and Friends.exe")]
    public void A_newer_file_beside_the_exe_is_taken_only_while_an_old_exe_of_the_tool_is_there(string current, string? oldExe)
    {
        var exe = ExeFolder();
        if (oldExe != null)
            File.WriteAllText(Path.Combine(exe, oldExe), "old exe");
        // Another product's old exe never counts.
        File.WriteAllText(Path.Combine(exe, "Coodsz Dedicated TAOM Server Tool.exe"), "other");
        File.WriteAllText(Path.Combine(exe, "launcher.json"), "first");
        var data = new ToolDataFolder(Path.Combine(_dir, "appdata"), ToolData.Coop);
        data.CopyOldFiles(exe, "coop-client", ["launcher.json"], current);
        File.SetLastWriteTimeUtc(data.MarkerPath("coop-client"), DateTime.UtcNow.AddHours(-3));
        File.WriteAllText(Path.Combine(data.Folder, "launcher.json"), "per user");
        File.SetLastWriteTimeUtc(Path.Combine(data.Folder, "launcher.json"), DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(Path.Combine(exe, "launcher.json"), "from the exe folder");

        var copied = data.CopyOldFiles(exe, "coop-client", ["launcher.json"], current);

        var taken = oldExe != null && ToolNames.Find(oldExe) == ToolNames.Find(current);
        Assert.Equal(taken, copied.Count == 1);
        Assert.Equal(taken ? "from the exe folder" : "per user", File.ReadAllText(Path.Combine(data.Folder, "launcher.json")));
        if (taken)
            Assert.Contains("Took the newer launcher.json from beside the exe", File.ReadAllText(Path.Combine(data.Folder, ToolDataFolder.LogName)));
    }

    [Fact]
    public void A_generic_folder_comes_along_only_with_our_files_and_under_the_size_cap()
    {
        var exe = ExeFolder();
        Directory.CreateDirectory(Path.Combine(exe, "Data"));
        File.WriteAllText(Path.Combine(exe, "Data", "somebody-else.txt"), "not ours");
        Directory.CreateDirectory(Path.Combine(exe, "ModSettings"));
        File.WriteAllText(Path.Combine(exe, "ModSettings", "Plain TAOM.overrides.json"), "{}");
        Directory.CreateDirectory(Path.Combine(exe, "McmProfiles"));
        File.WriteAllText(Path.Combine(exe, "McmProfiles", "big.json"), new string('x', 4096));
        ToolDataFolder.MaxFolderBytes = 1024;
        var data = new ToolDataFolder(Path.Combine(_dir, "appdata"), ToolData.Coop);

        var copied = data.CopyOldFiles(exe, "coop-server", ToolData.CoopServer.OldFiles);

        Assert.Equal(new[] { "ModSettings" }, copied);
        Assert.False(Directory.Exists(Path.Combine(data.Folder, "Data")));
        Assert.False(Directory.Exists(Path.Combine(data.Folder, "McmProfiles")));
        Assert.Contains("Left the McmProfiles folder beside the exe", File.ReadAllText(Path.Combine(data.Folder, ToolDataFolder.LogName)));

        // The verdict sits in the marker: later starts neither sum the folder nor log it again.
        Assert.Contains("left McmProfiles", File.ReadAllLines(data.MarkerPath("coop-server")));
        File.SetLastWriteTimeUtc(data.MarkerPath("coop-server"), DateTime.UtcNow.AddHours(-1));
        data.CopyOldFiles(exe, "coop-server", ToolData.CoopServer.OldFiles);
        data.CopyOldFiles(exe, "coop-server", ToolData.CoopServer.OldFiles);
        var log = File.ReadAllText(Path.Combine(data.Folder, ToolDataFolder.LogName));
        Assert.Equal(1, log.Split("Left the McmProfiles folder").Length - 1);
        Assert.False(Directory.Exists(Path.Combine(data.Folder, "McmProfiles")));
    }

    [Fact]
    public void A_newer_AppData_file_is_never_overwritten()
    {
        var exe = ExeFolder();
        File.WriteAllText(Path.Combine(exe, "launcher.json"), "old");
        File.WriteAllText(Path.Combine(exe, "profile-cache.json"), "old cache");
        var data = new ToolDataFolder(Path.Combine(_dir, "appdata"), ToolData.Coop);
        Directory.CreateDirectory(data.Folder);
        File.WriteAllText(Path.Combine(data.Folder, "launcher.json"), "newer");

        var copied = data.CopyOldFiles(exe, "coop-client", ToolData.CoopClient.OldFiles);

        Assert.Equal(new[] { "profile-cache.json" }, copied);
        Assert.Equal("newer", File.ReadAllText(Path.Combine(data.Folder, "launcher.json")));
    }

    [Fact]
    public void The_server_tool_and_client_of_one_product_each_copy_their_own_files()
    {
        var root = Path.Combine(_dir, "appdata");
        var server = ExeFolder();
        var client = Path.Combine(_dir, "client");
        Directory.CreateDirectory(client);
        File.WriteAllText(Path.Combine(server, "host.json"), "host");
        File.WriteAllText(Path.Combine(client, "launcher.json"), "client");

        new ToolDataFolder(root, ToolData.Coop).CopyOldFiles(server, ToolData.CoopServer.Key, ToolData.CoopServer.OldFiles);
        var copied = new ToolDataFolder(root, ToolData.Coop).CopyOldFiles(client, ToolData.CoopClient.Key, ToolData.CoopClient.OldFiles);

        // The server's marker does not stop the client's copy.
        Assert.Equal(new[] { "launcher.json" }, copied);
        Assert.True(File.Exists(Path.Combine(root, "Coop", "host.json")));
        Assert.True(File.Exists(Path.Combine(root, "Coop", "launcher.json")));
    }

    [Fact]
    public void Tools_share_a_product_folder_only_when_their_file_names_do_not_collide()
    {
        var plans = new[]
        {
            ToolData.CoopServer, ToolData.CoopClient, ToolData.AD1259Server, ToolData.AD1259Client,
            ToolData.TaomServer, ToolData.TaomClient, ToolData.MoreNationsClient
        };
        foreach (var group in plans.GroupBy(plan => (plan.Product, plan.Role)))
        {
            var names = group.SelectMany(plan => plan.OldFiles).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        // Both TAOM tools keep a taom-mod-compat.json, so each has its own subfolder.
        Assert.Equal("Server", ToolData.TaomServer.Role);
        Assert.Equal("Client", ToolData.TaomClient.Role);
        Assert.Equal(new[] { "Coop", "Coop", "AD1259", "AD1259", "TAOM", "TAOM" }, plans.Take(6).Select(plan => plan.Product));
        // The folder test builds of Coodsz and Friends used, read once by MoreNationsFoldIn.
        Assert.Equal(ToolData.MoreNations, ToolData.MoreNationsClient.Product);
        Assert.NotEqual(ToolData.CoopClient.Key, ToolData.MoreNationsClient.Key);
    }

    [Fact]
    public void The_shared_license_and_language_files_stay_in_roaming_AppData()
    {
        var plans = new[] { ToolData.CoopServer, ToolData.CoopClient, ToolData.AD1259Server, ToolData.AD1259Client, ToolData.TaomServer, ToolData.TaomClient, ToolData.MoreNationsClient };
        foreach (var name in plans.SelectMany(plan => plan.OldFiles))
            Assert.DoesNotContain("license", name, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Local", "Coodsz"), ToolData.DefaultRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_settings_file_the_updater_keeps_is_still_kept_beside_the_exe()
    {
        var plans = new[] { ToolData.CoopServer, ToolData.CoopClient, ToolData.AD1259Server, ToolData.AD1259Client, ToolData.TaomServer, ToolData.TaomClient, ToolData.BeServer, ToolData.BeClient, ToolData.RotServer, ToolData.RotClient };
        foreach (var kept in Core.Updates.ToolUpdate.KeptUserFiles.Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            Assert.Contains(plans, plan => plan.OldFiles.Contains(kept));
        Assert.Contains("update-install.log", Core.Updates.ToolUpdate.KeptUserFiles);
    }

    private static MemoryStream Zip(params (string Path, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files)
            {
                var entry = zip.CreateEntry(path);
                entry.LastWriteTime = DateTimeOffset.Now.AddMinutes(-Random.Shared.Next(1, 1000));
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static readonly (string, string)[] ServerTools =
    [
        ("DedicatedServer.CoodszTools/SubModule.xml", "<Module />"),
        ("DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll", "dll bytes")
    ];

    [Fact]
    public void Payload_is_written_out_to_its_version_folder_with_a_hash_list()
    {
        var version = Path.Combine(_dir, "Coop", "payload", "1.3.0");
        using var zip = Zip(ServerTools);

        var folder = PayloadWriter.Ensure(zip, version, "ServerTools");

        Assert.Equal(Path.Combine(version, "ServerTools"), folder);
        var dll = Path.Combine(folder, "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll");
        Assert.Equal("dll bytes", File.ReadAllText(dll));
        var manifest = File.ReadAllLines(folder + PayloadWriter.ManifestSuffix);
        Assert.StartsWith("content ", manifest[0]);
        Assert.Contains(manifest, line => line.StartsWith(FileHash.Sha256(dll) + " ", StringComparison.Ordinal));
        // The hash list sits beside the folder, so code that copies the whole folder never copies it.
        Assert.DoesNotContain(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories), path => path.EndsWith(PayloadWriter.ManifestSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void A_damaged_write_out_is_written_again()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        var folder = PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        var xml = Path.Combine(folder, "DedicatedServer.CoodszTools", "SubModule.xml");
        var dll = Path.Combine(folder, "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll");

        File.WriteAllText(dll, "changed by something");
        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        Assert.Equal("dll bytes", File.ReadAllText(dll));

        File.Delete(xml);
        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        Assert.Equal("<Module />", File.ReadAllText(xml));

        // An extra file would be copied into the server with the rest, so it goes too.
        var stray = Path.Combine(folder, "DedicatedServer.CoodszTools", "stray.dll");
        File.WriteAllText(stray, "x");
        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        Assert.False(File.Exists(stray));

        File.Delete(folder + PayloadWriter.ManifestSuffix);
        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        Assert.True(File.Exists(folder + PayloadWriter.ManifestSuffix));
    }

    [Fact]
    public void An_intact_write_out_is_left_alone_and_new_content_replaces_it()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        var folder = PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        var xml = Path.Combine(folder, "DedicatedServer.CoodszTools", "SubModule.xml");
        var written = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(xml, written);

        // The same files in a zip made at another time (the server tool and the client of one release).
        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        Assert.Equal(written, File.GetLastWriteTimeUtc(xml));

        // A dev build with changed files under the same version number writes them out again.
        PayloadWriter.Ensure(Zip(("DedicatedServer.CoodszTools/SubModule.xml", "<Module v2 />")), version, "ServerTools");
        Assert.Equal("<Module v2 />", File.ReadAllText(xml));
        Assert.False(File.Exists(Path.Combine(folder, "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll")));
    }

    [Fact]
    public void Only_versions_older_than_this_one_and_the_two_newest_others_go_and_never_one_in_use()
    {
        var payload = Path.Combine(_dir, "payload");
        foreach (var name in new[] { "1.1.0", "1.2.0", "1.2.9", "1.3.0", "1.10.0", "not-a-version" })
            Directory.CreateDirectory(Path.Combine(payload, name));
        // A tool on 1.1.0 still runs and holds its lock file.
        using var running = new FileStream(Path.Combine(payload, "1.1.0", PayloadWriter.InUsePrefix + "4242.lock"), FileMode.Create, FileAccess.Write, FileShare.None);
        // A tool on 1.2.0 closed without cleaning up; its lock file can be deleted.
        File.WriteAllText(Path.Combine(payload, "1.2.0", PayloadWriter.InUsePrefix + "4343.lock"), "");

        PayloadWriter.Ensure(Zip(ServerTools), Path.Combine(payload, "1.3.1"), "ServerTools");

        // 1.10.0 and 1.3.0 are the two newest others (1.10.0 is newer than this one), 1.2.9 and 1.2.0 are
        // older than both and this one, but 1.1.0 is in use. An unknown name is left alone.
        Assert.Equal(new[] { "1.1.0", "1.10.0", "1.3.0", "1.3.1", "not-a-version" }, Directory.GetDirectories(payload).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_write_out_waits_for_another_tool_and_gives_up_with_an_IOException()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        var target = Path.Combine(version, "ServerTools");
        PayloadWriter.LockWait = TimeSpan.FromMilliseconds(200);
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = new Thread(() =>
        {
            using var mutex = new Mutex(false, PayloadWriter.MutexName(target));
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        other.Start();
        held.Wait();
        try
        {
            Assert.Throws<IOException>(() => PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools"));
            // The caller gets the folder the payload belongs in, never the one beside the exe.
            Assert.Equal(target, ToolPayload.Resolve("ServerTools", Zip(ServerTools), version, Path.Combine(_dir, "exe")));
        }
        finally
        {
            release.Set();
            other.Join();
        }

        Assert.Equal(target, PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools"));
    }

    [Fact]
    public void A_half_written_copy_is_removed_only_when_its_tool_is_gone()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        var dead = Path.Combine(version, "ServerTools.new-999999-abcdef12");
        var alive = Path.Combine(version, "ServerTools.new-" + Environment.ProcessId + "-abcdef12");
        Directory.CreateDirectory(dead);
        Directory.CreateDirectory(alive);

        PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");

        Assert.False(Directory.Exists(dead));
        Assert.True(Directory.Exists(alive));
    }

    [Fact]
    public void A_cached_folder_that_was_deleted_is_written_out_again()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        UseSource(version, ("ServerTools", ServerTools));

        var folder = ToolPayload.Folder("ServerTools");
        Assert.Equal(Path.Combine(version, "ServerTools"), folder);
        Directory.Delete(folder, recursive: true);

        Assert.Equal(folder, ToolPayload.Folder("ServerTools"));
        Assert.True(File.Exists(Path.Combine(folder, "DedicatedServer.CoodszTools", "SubModule.xml")));
    }

    [Fact]
    public void Peek_never_waits_and_the_folder_is_ready_after_the_background_write_out()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        UseSource(version, (ToolPayload.BundledMods, [("BannerFix/SubModule.xml", "<Module />")]));
        var ready = new TaskCompletionSource<string>();
        void OnReady(string name) => ready.TrySetResult(name);
        ToolPayload.Ready += OnReady;
        try
        {
            var first = ToolPayload.Peek(ToolPayload.BundledMods);
            Assert.True(ready.Task.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(first == null || first == Path.Combine(version, ToolPayload.BundledMods));
            Assert.Equal(Path.Combine(version, ToolPayload.BundledMods), ToolPayload.Peek(ToolPayload.BundledMods));
            // A payload the exe does not carry answers at once, with the folder it would belong in.
            Assert.Equal(Path.Combine(version, "ServerTools"), ToolPayload.Peek("ServerTools"));
        }
        finally
        {
            ToolPayload.Ready -= OnReady;
        }
    }

    // An exe that carries some payloads and is asked for one it does not carry (the AD1259 server tool and
    // ImageExporter) never answers with the folder beside the exe, where anybody could have put one.
    [Fact]
    public void A_payload_the_exe_does_not_carry_is_never_taken_from_beside_the_exe()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        UseSource(version, ("ServerTools", ServerTools));
        var exe = Path.Combine(_dir, "exe");
        Directory.CreateDirectory(Path.Combine(exe, "ImageExporter", "CoodszImageExporter"));
        File.WriteAllText(Path.Combine(exe, "ImageExporter", "CoodszImageExporter", "SubModule.xml"), "<Module />");

        Assert.Equal(Path.Combine(version, "ImageExporter"), ToolPayload.Folder("ImageExporter"));
        Assert.Equal(Path.Combine(version, "ImageExporter"), ToolPayload.Peek("ImageExporter"));
        Assert.Equal(Path.Combine(version, "ImageExporter", "CoodszImageExporter"), ToolPayload.Expected("ImageExporter", "CoodszImageExporter"));
        Assert.False(Directory.Exists(Path.Combine(version, "ImageExporter")));

        // Without a settings folder too: the folder named in the missing line is the one Folder answers with.
        ToolPayload.Source = new ToolPayload.PayloadSource(
            name => name == "ServerTools" ? Zip(ServerTools) : null,
            () => ["ServerTools"],
            () => null,
            exe);
        ToolPayload.Reset();
        var root = Path.Combine(ToolData.DefaultRoot, "payload", "ImageExporter");
        Assert.Equal(root, ToolPayload.Folder("ImageExporter"));
        Assert.Equal(root, ToolPayload.Peek("ImageExporter"));
        Assert.Equal(root, ToolPayload.Expected("ImageExporter"));
        Assert.Equal(Path.Combine(ToolData.DefaultRoot, "payload", "ServerTools"), ToolPayload.Expected("ServerTools"));
    }

    [Fact]
    public void After_the_write_out_the_old_tool_folders_beside_the_exe_go_but_bundled_mods_stay()
    {
        var exe = ExeFolder();
        Directory.CreateDirectory(Path.Combine(exe, "ServerTools", "DedicatedServer.CoodszTools"));
        Directory.CreateDirectory(Path.Combine(exe, "BannerFix"));
        Directory.CreateDirectory(Path.Combine(exe, "ClientAddOns"));
        UseSource(Path.Combine(_dir, "payload", "1.3.0"), ("ServerTools", ServerTools));

        var logged = new List<string>();
        ToolPayload.Log = logged.Add;
        ToolPayload.WriteOutAll();
        Assert.Contains(logged, line => line.StartsWith("Removed the old ServerTools folder beside the exe", StringComparison.Ordinal));

        Assert.False(Directory.Exists(Path.Combine(exe, "ServerTools")));
        // ClientAddOns is not one this exe carries, so it stays for the tool that does.
        Assert.True(Directory.Exists(Path.Combine(exe, "ClientAddOns")));
        Assert.True(Directory.Exists(Path.Combine(exe, "BannerFix")));
        Assert.False(ToolPayload.RemoveOldFolder(exe, "BannerFix"));

        // Never in a source checkout.
        File.WriteAllText(Path.Combine(exe, "Tool.csproj"), "<Project />");
        Assert.False(ToolPayload.RemoveOldFolder(exe, "ClientAddOns"));
        Assert.True(Directory.Exists(Path.Combine(exe, "ClientAddOns")));
    }

    [Fact]
    public void A_bundled_mod_an_older_version_left_beside_the_launcher_is_listed_once()
    {
        var exe = ExeFolder();
        var bundled = Path.Combine(_dir, "payload", "1.3.0", ToolPayload.BundledMods);
        const string xml = "<Module><Name value=\"Banner Fix\" /><Id value=\"BannerFix\" /><Version value=\"v1.0.0\" /></Module>";
        foreach (var root in new[] { exe, bundled })
        {
            Directory.CreateDirectory(Path.Combine(root, "BannerFix"));
            File.WriteAllText(Path.Combine(root, "BannerFix", "SubModule.xml"), xml);
        }

        Assert.True(Core.Launch.BundledModules.IsCopyOf(Path.Combine(exe, "BannerFix"), bundled));
        var found = Core.Profile.CustomModCatalog.Scan(exe, [], null, bundled);
        Assert.Single(found, mod => mod.ModuleId == "BannerFix");
        Assert.StartsWith(bundled, found.Single(mod => mod.ModuleId == "BannerFix").Directory);

        // A different copy the player put there is their own, so it wins.
        File.WriteAllText(Path.Combine(exe, "BannerFix", "extra.txt"), "mine");
        Assert.False(Core.Launch.BundledModules.IsCopyOf(Path.Combine(exe, "BannerFix"), bundled));
        Assert.StartsWith(exe, Core.Profile.CustomModCatalog.Scan(exe, [], null, bundled).Single(mod => mod.ModuleId == "BannerFix").Directory);
    }

    [Fact]
    public void The_missing_module_line_names_the_AppData_folder_and_what_to_do()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        UseSource(version);
        var text = ToolPayload.MissingText("Coodsz Join Password", ToolPayload.Expected("ClientAddOns", "DedicatedServer.CoodszJoinPassword"));
        Assert.Contains(Path.Combine(version, "ClientAddOns", "DedicatedServer.CoodszJoinPassword"), text);
        Assert.Contains("Start the tool again", text);
        Assert.Contains("antivirus", text);
        Assert.DoesNotContain("zip", text, StringComparison.OrdinalIgnoreCase);
        var words = text.Replace(Path.Combine(version, "ClientAddOns", "DedicatedServer.CoodszJoinPassword"), "");
        Assert.DoesNotContain(":", words);
        Assert.DoesNotMatch("[A-Za-z]-[A-Za-z]", words);
    }

    [Fact]
    public void A_failed_write_out_answers_at_once_never_with_the_folder_beside_the_exe_and_is_not_tried_again_for_a_while()
    {
        var exe = ExeFolder();
        // A folder somebody put beside the exe. Release zips no longer ship one, so it is never used.
        var planted = Path.Combine(exe, ToolPayload.BundledMods);
        Directory.CreateDirectory(Path.Combine(planted, "BannerFix"));
        File.WriteAllText(Path.Combine(planted, "BannerFix", "SubModule.xml"), "<Module />");
        var own = Path.Combine(_dir, "payload", "1.3.0", ToolPayload.BundledMods);
        var opened = 0;
        ToolPayload.Source = new ToolPayload.PayloadSource(
            name =>
            {
                Interlocked.Increment(ref opened);
                return name == ToolPayload.BundledMods ? new MemoryStream(new byte[] { 1, 2, 3 }) : null;
            },
            () => [ToolPayload.BundledMods],
            () => Path.Combine(_dir, "payload", "1.3.0"),
            exe);
        ToolPayload.Reset();

        Assert.Equal(own, ToolPayload.Folder(ToolPayload.BundledMods));
        Assert.False(Directory.Exists(own));
        var afterFailure = opened;
        // Within the back off every caller gets the payload's own folder, where it is missing, without a new write-out.
        Assert.Equal(own, ToolPayload.Peek(ToolPayload.BundledMods));
        Assert.Equal(own, ToolPayload.Folder(ToolPayload.BundledMods));
        Assert.Equal(afterFailure, opened);
        Assert.False(ToolPayload.IsWrittenOut(ToolPayload.BundledMods, own));

        // After it, the next use tries again, and still never answers with the planted folder.
        ToolPayload.RetryAfter = TimeSpan.Zero;
        Assert.Equal(own, ToolPayload.Folder(ToolPayload.BundledMods));
        Assert.True(opened > afterFailure);
        Assert.True(File.Exists(Path.Combine(planted, "BannerFix", "SubModule.xml")));
    }

    [Fact]
    public void The_TAOM_server_reloads_its_mod_list_once_and_only_after_a_real_write_out()
    {
        var setups = SourceFiles.Method(SourceFiles.Read("src/TaomCoopHost/MainWindow.ModSetups.cs"), "private void ReloadWhenBundledReady()");
        Assert.Contains("if (_bundledReloadStarted)", setups);
        Assert.Contains("ToolPayload.IsWrittenOut(ToolPayload.BundledMods, task.Result)", setups);
        Assert.DoesNotContain("_bundledReloadStarted = false", setups);

        // Every rescan that needs the bundled mods waits for them on a worker thread first.
        foreach (var file in new[] { "src/TaomCoopHost/MainWindow.ModTest.cs", "src/TaomCoopHost/MainWindow.ModSetups.cs" })
        {
            var text = SourceFiles.Read(file);
            var rescans = text.Split("HostMods(rescan: true)").Length - 1;
            Assert.True(rescans > 0, file);
            Assert.True(text.Split("await ToolPayload.FolderAsync(ToolPayload.BundledMods);").Length - 1 >= rescans - 1, file);
        }

        // Add mods and Import mod list wait for them too, so a copy left beside the exe is listed once.
        foreach (var (file, method) in new[]
        {
            ("src/BannerlordCoopLauncher/MainWindow.Modules.cs", "private async void AddMods_Click("),
            ("src/BannerlordCoopLauncher/MainWindow.SharedList.cs", "private async Task OpenSharedListFileAsync("),
            ("src/TaomCoopLauncher/MainWindow.Setups.cs", "private async void AddMods_Click("),
            ("src/TaomCoopLauncher/MainWindow.Setups.cs", "private async void ImportList_Click("),
            ("src/TaomCoopHost/MainWindow.SetupEditing.cs", "private async void AddHostMods_Click(")
        })
            Assert.Contains("await ToolPayload.FolderAsync(ToolPayload.BundledMods);", SourceFiles.Method(SourceFiles.Read(file), method));
    }

    // Payloads from a list instead of the exe's resources, written out under versionFolder.
    private void UseSource(string versionFolder, params (string Name, (string, string)[] Files)[] payloads)
    {
        var map = payloads.ToDictionary(payload => payload.Name, payload => payload.Files, StringComparer.OrdinalIgnoreCase);
        ToolPayload.Source = new ToolPayload.PayloadSource(
            name => map.TryGetValue(name, out var files) ? Zip(files) : null,
            () => map.Keys,
            () => versionFolder,
            Path.Combine(_dir, "exe"));
        ToolPayload.Reset();
    }

    [Fact]
    public void Without_an_embedded_payload_the_folder_beside_the_exe_is_used()
    {
        var exe = ExeFolder();
        Assert.Equal(Path.Combine(exe, "ClientAddOns"), ToolPayload.Resolve("ClientAddOns", null, Path.Combine(_dir, "payload", "1.3.0"), exe));

        // A damaged resource is still a payload this exe carries, so it never falls back to the folder beside
        // the exe: the caller finds the module missing in its own folder and says so.
        using var broken = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Equal(Path.Combine(_dir, "payload", "1.3.0", "ClientAddOns"), ToolPayload.Resolve("ClientAddOns", broken, Path.Combine(_dir, "payload", "1.3.0"), exe));
        using var noVersion = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Equal(Path.Combine(ToolData.DefaultRoot, "payload", "ClientAddOns"), ToolPayload.Resolve("ClientAddOns", noVersion, null, exe));

        // The real entry point: the test host carries no payloads and never called ToolData.Start.
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "ServerTools"), ToolPayload.Folder("ServerTools"));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, ToolPayload.BundledMods), ToolPayload.Folder(ToolPayload.BundledMods));

        using var zip = Zip(ServerTools);
        Assert.Equal(Path.Combine(_dir, "payload", "1.3.0", "ServerTools"), ToolPayload.Resolve("ServerTools", zip, Path.Combine(_dir, "payload", "1.3.0"), exe));
    }

    // An old tool of another product in the same folder may still use its SaveRescue or ClientAddOns there,
    // and so does an old name exe of this tool that ToolRename.Cleanup could not remove yet (a shortcut to it
    // could not be moved): CoodszHost.exe v1.2.78 reads ServerTools and SaveRescue beside it.
    [Theory]
    [InlineData("Coodsz Coop Server Tool.exe", "Coodsz Dedicated TAOM Server Tool.exe", false)]
    [InlineData("Coodsz Coop Server Tool.exe", "Coodsz AD1259 Client Launcher.exe", false)]
    [InlineData("Coodsz Coop Server Tool.exe", "CoodszHost.exe", false)]
    [InlineData("Coodsz Coop Client Launcher.exe", "Coodsz and Friends.exe", false)]
    [InlineData("Coodsz Coop Client Launcher.exe", "Coodsz Coop and Friends.exe", false)]
    [InlineData("Renamed by the player.exe", "CoodszHost.exe", false)]
    // The other tool of the same product under its new name is no reason to keep it.
    [InlineData("Coodsz Coop Server Tool.exe", "Coodsz Coop Client Launcher.exe", true)]
    [InlineData("Coodsz Coop Client Launcher.exe", "Coodsz Coop Server Tool.exe", true)]
    public void An_old_tool_folder_stays_while_another_product_or_an_old_exe_of_this_tool_shares_the_folder(string current, string beside, bool removed)
    {
        var exe = ExeFolder();
        Directory.CreateDirectory(Path.Combine(exe, "SaveRescue"));
        File.WriteAllText(Path.Combine(exe, beside), "exe");

        Assert.Equal(removed, ToolPayload.RemoveOldFolder(exe, "SaveRescue", current));
        Assert.Equal(!removed, Directory.Exists(Path.Combine(exe, "SaveRescue")));
    }

    [Fact]
    public void A_manifest_line_that_points_outside_the_payload_folder_makes_it_not_intact()
    {
        var version = Path.Combine(_dir, "payload", "1.3.0");
        var target = PayloadWriter.Ensure(Zip(ServerTools), version, "ServerTools");
        var manifest = target + PayloadWriter.ManifestSuffix;
        var id = File.ReadLines(manifest).First()["content ".Length..];
        Assert.True(PayloadWriter.IsIntact(target, manifest, id));

        // A file outside that the line would check and count as part of the payload.
        var outside = Path.Combine(version, "outside.txt");
        File.WriteAllText(outside, "x");
        File.AppendAllLines(manifest, [FileHash.Sha256(outside) + " " + new FileInfo(outside).Length + " " + Path.Combine("..", "outside.txt")]);
        Assert.False(PayloadWriter.IsIntact(target, manifest, id));

        Assert.False(PayloadWriter.InsideFolder(target, outside));
        Assert.False(PayloadWriter.InsideFolder(target, Path.Combine("a", "..", "..", "outside.txt")));
        if (OperatingSystem.IsWindows())
        {
            // A drive relative path, a network share and the long path prefix are all rooted on Windows.
            Assert.False(PayloadWriter.InsideFolder(target, @"C:outside.txt"));
            Assert.False(PayloadWriter.InsideFolder(target, @"\\server\share\outside.txt"));
            Assert.False(PayloadWriter.InsideFolder(target, @"\\?\C:\outside.txt"));
            Assert.False(PayloadWriter.InsideFolder(target, @"\outside.txt"));
        }
        Assert.True(PayloadWriter.InsideFolder(target, Path.Combine("DedicatedServer.CoodszTools", "SubModule.xml")));
    }

    [Fact]
    public void A_payload_entry_outside_its_folder_is_refused()
    {
        using var zip = Zip(("../outside.txt", "x"));
        Assert.Throws<InvalidDataException>(() => PayloadWriter.Ensure(zip, Path.Combine(_dir, "payload", "1.3.0"), "ServerTools"));
        Assert.False(File.Exists(Path.Combine(_dir, "payload", "1.3.0", "outside.txt")));

        // An absolute path too, with or without a drive.
        var outside = Path.Combine(_dir, "elsewhere.txt");
        using var rooted = Zip((outside.Replace('\\', '/'), "x"));
        Assert.Throws<InvalidDataException>(() => PayloadWriter.Ensure(rooted, Path.Combine(_dir, "payload", "1.3.0"), "ServerTools"));
        Assert.False(File.Exists(outside));
        using var slash = Zip(("/elsewhere.txt", "x"));
        Assert.Throws<InvalidDataException>(() => PayloadWriter.Ensure(slash, Path.Combine(_dir, "payload", "1.3.0"), "ServerTools"));
    }

    // The source checks: no program reads a shipped module folder from AppContext.BaseDirectory any more.

    // Uses of AppContext.BaseDirectory that are about the exe itself or the player's own mod folder.
    private static readonly string[] AllowedBaseDirectory =
    [
        "ToolUpdate.RemovePreviousVersion(AppContext.BaseDirectory",
        "ToolUpdate.InstallAsync(_updateOffer, AppContext.BaseDirectory",
        // Written only when the program crashes; the updater keeps it (ToolUpdate.KeptUserFiles).
        "Path.Combine(AppContext.BaseDirectory, \"launcher-error.txt\")",
        // Mods a player unzipped beside the tool ("From Nexus or ModDB"), not shipped ones.
        "CustomModCatalog.Scan(AppContext.BaseDirectory",
        "ExtraModScan.Scan(AppContext.BaseDirectory",
        // Also reads an old taom-server.json beside the tools.
        "TaomServerFolders.OnThisPc(AppContext.BaseDirectory)",
        // Also reads an old be-server.json beside the tools.
        "BeServerFolders.OnThisPc(AppContext.BaseDirectory)",
        // LiteNetLib.dll for a loose dev build.
        "FindLocal(name, AppContext.BaseDirectory)",
        // The update helper's logs, which ToolUpdate writes into the install folder (log packs read them).
        "string.IsNullOrWhiteSpace(toolFolder) ? AppContext.BaseDirectory : toolFolder",
        // The server root when no server exe is known yet.
        "?? AppContext.BaseDirectory;",
        // Coodsz and Friends settings beside the exe, folded into the client's folder once.
        "MoreNationsFoldIn.CameFromFriends(AppContext.BaseDirectory)",
        "MoreNationsFoldIn.Run(ToolData.FolderOf(ToolData.CoopClient), AppContext.BaseDirectory",
        // Only shown in Fix antivirus as a folder to exclude, never read.
        "AntivirusFolders.For(AppContext.BaseDirectory"
    ];

    [Fact]
    public void No_program_reads_module_folders_or_settings_from_the_exe_folder()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Regex.IsMatch(relative, "/(obj|bin)/") || relative.EndsWith("Core/IO/ToolData.cs", StringComparison.Ordinal) || relative.EndsWith("Core/IO/ToolPayload.cs", StringComparison.Ordinal))
                continue;
            foreach (var line in File.ReadLines(file))
            {
                if ((line.Contains("AppContext.BaseDirectory", StringComparison.Ordinal) || line.Contains("AppDomain.CurrentDomain.BaseDirectory", StringComparison.Ordinal))
                    && !AllowedBaseDirectory.Any(allowed => line.Contains(allowed, StringComparison.Ordinal)))
                    offenders.Add(relative + ": " + line.Trim());
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_tool_embeds_its_folders_instead_of_shipping_them_beside_the_exe()
    {
        var root = RepoRoot();
        var tools = new Dictionary<string, string[]>
        {
            ["src/CoodszHost/CoodszHost.csproj"] = ["DedicatedServerRepair", "SaveRescue", "ServerTools", "PatchModules", "ImageExporter"],
            ["src/AD1259Host/AD1259Host.csproj"] = ["DedicatedServerRepair", "SaveRescue", "ImageExporter", "ServerAddOns"],
            ["src/AD1259Launcher/AD1259Launcher.csproj"] = ["ClientAddOns"],
            ["src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj"] = ["ClientAddOns", "BundledMods"],
            ["src/TaomCoopHost/TaomCoopHost.csproj"] = ["SaveRescue", "WarSync", "BundledMods"],
            ["src/TaomCoopLauncher/TaomCoopLauncher.csproj"] = ["ClientAddOns", "BundledMods"],
            ["src/BurningEmpiresHost/BurningEmpiresHost.csproj"] = ["DedicatedServerRepair", "SaveRescue", "ServerAddOns"],
            ["src/BurningEmpiresLauncher/BurningEmpiresLauncher.csproj"] = ["ClientAddOns"]
        };
        foreach (var (project, payloads) in tools)
        {
            var text = File.ReadAllText(Path.Combine(root, project));
            Assert.DoesNotContain("ExcludeFromSingleFile", text);
            Assert.DoesNotContain("CopyToPublishDirectory", text);
            Assert.Contains("<Import Project=\"..\\CoodszPayload.targets\" />", text);
            foreach (var payload in payloads)
                Assert.Contains("Payload=\"" + payload + "\"", text);
        }

        // The bundled mods only come in when Make-Release passes them, so a dev build still builds.
        foreach (var project in tools.Where(tool => tool.Value.Contains("BundledMods")).Select(tool => tool.Key))
            Assert.Contains("<ItemGroup Condition=\"'$(CoodszBundledMods)' != ''\">", File.ReadAllText(Path.Combine(root, project)));
        var release = File.ReadAllText(Path.Combine(root, "tools", "Make-Release.ps1"));
        Assert.Contains("-p:CoodszBundledMods=$bundledCoop", release);
        Assert.Contains("-p:CoodszBundledMods=$bundledTaom", release);

        // The zips are made on every build, so a release never embeds one from an older build, and both
        // release scripts check every payload file for file against its sources and the built program.
        var targets = File.ReadAllText(Path.Combine(root, "src", "CoodszPayload.targets"));
        Assert.DoesNotContain("Inputs=", targets);
        Assert.DoesNotContain("Outputs=", targets);
        foreach (var script in new[] { release, File.ReadAllText(Path.Combine(root, "tools", "Make-AD1259-Release.ps1")) })
        {
            Assert.Contains(". (Join-Path $PSScriptRoot 'PayloadCheck.ps1')", script);
            Assert.DoesNotContain("Assert-Payloads", script);
        }

        var check = File.ReadAllText(Path.Combine(root, "tools", "PayloadCheck.ps1"));
        Assert.Contains("has an old or different", check);
        Assert.Contains("does not carry this build's", check);
    }

    [Fact]
    public void Every_program_picks_its_settings_folder_first()
    {
        var root = RepoRoot();
        var apps = new Dictionary<string, string>
        {
            ["src/CoodszHost/App.xaml.cs"] = "ToolData.Start(CoodszHost.Services.Edition.IsAD1259 ? ToolData.AD1259Server : ToolData.CoopServer);",
            ["src/BannerlordCoopLauncher/App.xaml.cs"] = "ToolData.Start(ToolData.CoopClient);",
            ["src/TaomCoopHost/App.xaml.cs"] = "ToolData.Start(ToolData.TaomServer);",
            ["src/TaomCoopLauncher/App.xaml.cs"] = "ToolData.Start(ToolData.TaomClient);",
            ["src/AD1259Launcher/App.xaml.cs"] = "ToolData.Start(ToolData.AD1259Client);"
        };
        foreach (var (app, start) in apps)
        {
            var text = File.ReadAllText(Path.Combine(root, app));
            Assert.Contains(start, text);
            // After the rename hand over, before anything reads a setting.
            Assert.True(text.IndexOf("ToolRename.OnStartup()", StringComparison.Ordinal) < text.IndexOf(start, StringComparison.Ordinal), app);
            Assert.True(text.IndexOf(start, StringComparison.Ordinal) < text.IndexOf("UiLanguage.Start()", StringComparison.Ordinal), app);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
