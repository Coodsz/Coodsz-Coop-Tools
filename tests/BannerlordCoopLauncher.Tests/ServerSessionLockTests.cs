namespace BannerlordCoopLauncher.Tests;

// Live test 2026-10-26: an automatic restart stopped the server and never started it again, and the
// Coodsz Host window went black. StopGracefully (worker thread) asked Process.HasExited under _gate,
// HasExited raised Exited on that thread, the window's Exited handler waited for the window thread,
// and the window thread waited for _gate in IsRunning.
public class ServerSessionLockTests
{
    private static readonly string Session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));

    [Fact]
    public void HasExited_is_never_asked_while_the_session_lock_is_held()
    {
        var locked = LockedParts(Session);
        Assert.True(locked.Count >= 5, "Expected the lock (_gate) parts of ServerSession.cs, found " + locked.Count + ".");
        Assert.All(locked, part => Assert.DoesNotContain("HasExited", part));
    }

    [Fact]
    public void The_check_finds_HasExited_inside_a_lock_block()
    {
        var old = "lock (_gate)\n{\n    if (_process is { HasExited: false })\n        return true;\n}\nreturn false;";
        Assert.Contains(LockedParts(old), part => part.Contains("HasExited"));
        Assert.Contains(LockedParts("lock (_gate)\n    return _process is { HasExited: false };"), part => part.Contains("HasExited"));
    }

    [Fact]
    public void The_running_checks_read_the_process_through_CurrentProcess()
    {
        Assert.Contains("private Process? CurrentProcess()", Session);
        Assert.Contains("CurrentProcess() is { HasExited: false }", Session);
    }

    // The body of every lock (_gate): a { } block (braces matched) or the one statement after it.
    private static List<string> LockedParts(string source)
    {
        var parts = new List<string>();
        const string marker = "lock (_gate)";
        for (var at = source.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = source.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            var start = at + marker.Length;
            while (start < source.Length && char.IsWhiteSpace(source[start]))
                start++;
            if (start < source.Length && source[start] == '{')
            {
                var depth = 0;
                for (var i = start; i < source.Length; i++)
                {
                    if (source[i] == '{')
                        depth++;
                    else if (source[i] == '}' && --depth == 0)
                    {
                        parts.Add(source[start..(i + 1)]);
                        break;
                    }
                }
            }
            else
            {
                var end = source.IndexOf(';', start);
                parts.Add(source[start..(end < 0 ? source.Length : end + 1)]);
            }
        }

        return parts;
    }
}
