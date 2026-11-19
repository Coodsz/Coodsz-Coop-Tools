using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

public class HostActionLogTests
{
    [Fact]
    public void Action_lines_are_written_without_passwords_addresses_or_steam_ids()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new HostActionLog(() => root);
            log.Note("Start", "Save=save018. Password=secret123 host=192.168.1.10 steam=76561198000000000");
            log.NotePassword(true);

            Assert.NotNull(log.FilePath);
            var path = log.FilePath!;
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("Start", text);
            Assert.Contains("Save=save018", text);
            Assert.Contains("[password]", text);
            Assert.Contains("[address]", text);
            Assert.Contains("[steam id]", text);
            Assert.DoesNotContain("secret123", text);
            Assert.DoesNotContain("192.168.1.10", text);
            Assert.DoesNotContain("76561198000000000", text);
            Assert.Contains("Password  set", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_log_folder_is_skipped_instead_of_throwing()
    {
        var log = new HostActionLog(() => null);
        log.Note("Start", "ignored");
        Assert.Null(log.FilePath);
    }

    [Fact]
    public void Open_server_logs_highlights_the_newest_coop_log_and_a_dated_host_action_share_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var older = Path.Combine(root, "coop-server-20260101-010101.log");
            var newer = Path.Combine(root, "coop-server-20260102-020202.log");
            var action = Path.Combine(root, "host-action.log");
            File.WriteAllText(older, "old");
            File.WriteAllText(newer, "new");
            File.WriteAllText(action, "actions");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(action, DateTime.UtcNow);

            var paths = LogFolderOpen.FilesToHighlight(root);
            Assert.Equal(2, paths.Count);
            Assert.Equal(newer, paths[0]);
            Assert.NotEqual(action, paths[1]);
            Assert.StartsWith("host-action-", Path.GetFileName(paths[1]), StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".log", paths[1], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("actions", File.ReadAllText(paths[1]));
            Assert.True(File.Exists(action));
            Assert.Empty(LogFolderOpen.FilesToHighlight(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Share_copy_uses_machine_and_time_so_downloads_do_not_all_collide()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var live = Path.Combine(root, "host-action.log");
            File.WriteAllText(live, "click trail");
            var share = LogFolderOpen.ShareCopy(live);
            Assert.NotNull(share);
            Assert.NotEqual(live, share);
            Assert.Contains("host-action-", Path.GetFileName(share), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("click trail", File.ReadAllText(share!));
            var other = Path.Combine(root, "coop-server-20260101-010101.log");
            File.WriteAllText(other, "server");
            Assert.Equal(Path.GetFullPath(other), LogFolderOpen.ShareCopy(other));
            Assert.Single(Directory.GetFiles(root, "host-action-*.log"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_action_log_uses_its_own_file_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-client-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new HostActionLog(() => root, LogFolderOpen.ClientActionLogName);
            log.Note("Connect", "direct");
            Assert.Equal(Path.Combine(root, "client-action.log"), log.FilePath);
            Assert.True(File.Exists(log.FilePath));
            Assert.Contains("Connect", File.ReadAllText(log.FilePath!));
            Assert.False(File.Exists(Path.Combine(root, "host-action.log")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
