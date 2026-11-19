using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using BannerlordCoopLauncher.Core.Support;

namespace CoodszHost.Services;

// Reads the Windows Application event log around a server exit, read only, for the server
// crash report (ServerEventLog picks and writes the entries). Any failure leaves it out quietly.
public static class WindowsEventLog
{
    // The newest entries are read first; the window is three minutes, so this is plenty.
    private const int MaxEntries = 5000;

    public static string? ServerEntries(DateTime exitAt)
    {
        try
        {
            using var log = new EventLog("Application");
            var entries = log.Entries;
            var from = exitAt - ServerEventLog.Before;
            var found = new List<EventLogEntryInfo>();
            for (int i = entries.Count - 1, read = 0; i >= 0 && read < MaxEntries; i--, read++)
            {
                var entry = entries[i];
                if (entry.TimeGenerated < from)
                    break;
                found.Add(new EventLogEntryInfo(entry.TimeGenerated, entry.Source, entry.InstanceId & 0xFFFF, entry.EntryType.ToString(), entry.Message ?? ""));
            }

            return ServerEventLog.Format(ServerEventLog.Select(found, exitAt), exitAt);
        }
        catch (Exception)
        {
            // No access to the log, or it changed while it was read: the report goes without it.
            return null;
        }
    }

    // The Application, System and Defender entries in the exit evidence window
    // (ServerExitEvidence picks which to keep). None of these logs needs admin rights to read.
    // A log that cannot be read is named in unread and the others still count.
    public static IReadOnlyList<EventLogEntryInfo> ExitEntries(DateTime exitAt, out IReadOnlyList<string> unread)
    {
        var found = new List<EventLogEntryInfo>();
        var failed = new List<string>();
        var from = (exitAt - ServerEventLog.Before).ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var to = (exitAt + ServerEventLog.After).ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var query = "*[System[TimeCreated[@SystemTime>='" + from + "' and @SystemTime<='" + to + "']]]";
        foreach (var log in ServerExitEvidence.Logs)
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query));
                for (var read = 0; read < MaxEntries && reader.ReadEvent() is { } record; read++)
                {
                    using (record)
                        found.Add(new EventLogEntryInfo(record.TimeCreated ?? exitAt, record.ProviderName ?? "", record.Id, Level(record.Level), Message(record), log));
                }
            }
            catch (Exception)
            {
                // A missing log (Defender removed) or no access: the evidence says so.
                failed.Add(log);
            }
        }

        unread = failed;
        return found;
    }

    private static string Level(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        _ => "Information"
    };

    // The full message, or the raw values when the provider's message text is not on this PC.
    private static string Message(EventRecord record)
    {
        try
        {
            var message = record.FormatDescription();
            if (!string.IsNullOrWhiteSpace(message))
                return message;
        }
        catch (Exception)
        {
        }

        return string.Join(" | ", record.Properties.Select(property => property.Value?.ToString() ?? ""));
    }
}
