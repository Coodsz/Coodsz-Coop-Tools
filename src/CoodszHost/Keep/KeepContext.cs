using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace CoodszHost.Keep;

/// <summary>
/// What the Cheats page, the player profile and the Items window share: the server tools client,
/// the admin log, the running server and the host window. MainWindow makes one at start.
/// </summary>
public sealed class KeepContext
{
    private readonly Func<string> _blockReason;
    private readonly Func<string, Task<IReadOnlyList<string>>> _send;
    private readonly Func<IReadOnlyList<LivePlayerInfo>> _livePlayers;
    private readonly Func<string?> _saveName;

    public KeepContext(Window owner, ServerSession session, Func<IReadOnlyList<LivePlayerInfo>> livePlayers, Func<string?> saveName)
        : this(owner,
            () => !session.IsRunning ? "Start the server first. Cheats work while the server is running." : session.CommandBlockReason(),
            command => SendAsync(session, command), livePlayers, saveName, AuditLog.DefaultPath, ReadListFile)
    {
        HeroNames = new HeroNameMemory(ToolData.File("hero-names.json"));
    }

    /// <summary>
    /// Everything the pages need from outside, passed in: why commands are blocked, how a command
    /// reaches the server, the live players, the running save, the admin log file and the list reader.
    /// </summary>
    public KeepContext(Window owner, Func<string> blockReason, Func<string, Task<IReadOnlyList<string>>> send,
        Func<IReadOnlyList<LivePlayerInfo>> livePlayers, Func<string?> saveName, string auditPath, Func<string, string?> readListFile)
    {
        Owner = owner;
        _blockReason = blockReason;
        _send = send;
        _livePlayers = livePlayers;
        _saveName = saveName;
        Client = new KeepClient(RunAsync, readListFile);
        Log = new AuditLog(auditPath);
    }

    public static KeepContext? Current { get; set; }

    /// <summary>
    /// The server loads Coodsz Server Tools (DedicatedServer.CoodszTools), so the coodsz.* cheats, gold,
    /// profiles and pictures work. False in the AD1259 build: only Coop's own coop.debug commands are sent.
    /// </summary>
    public bool ToolsModule { get; init; } = true;

    /// <summary>Streamer mode is on (set by MainWindow): Steam ids show as dots.</summary>
    public static bool StreamerMode { get; set; }

    public Window Owner { get; }
    public KeepClient Client { get; }
    public AuditLog Log { get; }

    /// <summary>The player heroes' names from earlier runs, shown while the server is stopped (none in tests).</summary>
    public HeroNameMemory? HeroNames { get; set; }

    /// <summary>Opens the Cheats page with this hero picked (set by MainWindow).</summary>
    public Action<KeepTarget>? OpenInCheats { get; set; }

    /// <summary>Opens the Map page centred on this hero's party: (hero id, name) (set by MainWindow).</summary>
    public Action<string, string>? ShowOnMap { get; set; }

    /// <summary>Bans the player by SteamID64 (set by MainWindow); returns the line to show.</summary>
    public Func<string, string, string>? Ban { get; set; }

    /// <summary>A line for the host console (set by MainWindow).</summary>
    public Action<string>? Remember { get; set; }

    /// <summary>A change confirmed or refused: (message, ok). The pages show it.</summary>
    public event Action<string, bool>? Status;

    /// <summary>Why commands cannot be sent now, or "".</summary>
    public string BlockReason() => _blockReason();

    public bool CanSend => BlockReason().Length == 0;

    /// <summary>The favourite cheats of the Cheats page (their ids), kept in the host settings (set by MainWindow).</summary>
    public Func<IReadOnlyList<string>>? ReadFavourites { get; set; }

    /// <summary>Saves the favourite cheats in the host settings (set by MainWindow).</summary>
    public Action<IReadOnlyList<string>>? SaveFavourites { get; set; }

    /// <summary>
    /// Sends one Coop console command as it is and returns the reply lines and whether it worked: a
    /// command the server refused, or a reply that reads like a failure, is not ok.
    /// </summary>
    public async Task<(bool Ok, IReadOnlyList<string> Lines)> SendCoopAsync(string command)
    {
        var lines = await RunAsync(command);
        var shown = lines.Select(l => l.StartsWith(ToolsParser.ErrorPrefix, StringComparison.Ordinal) ? l[ToolsParser.ErrorPrefix.Length..].Trim() : l)
            .Where(l => l.Trim().Length > 0).ToList();
        var ok = !lines.Any(l => l.StartsWith(ToolsParser.ErrorPrefix, StringComparison.Ordinal))
                 && !shown.Any(l => TaomCoop.Core.CommandReply.Tone(l) != TaomCoop.Core.CommandReplyTone.Normal);
        return (ok, shown);
    }

    private Task<IReadOnlyList<string>> RunAsync(string command)
    {
        var blocked = BlockReason();
        return blocked.Length > 0
            ? Task.FromResult<IReadOnlyList<string>>(new[] { ToolsParser.ErrorPrefix + " " + blocked })
            : _send(command);
    }

    /// <summary>Sends one console command through server-data\console-queue and returns the answer lines.</summary>
    private static async Task<IReadOnlyList<string>> SendAsync(ServerSession session, string command)
    {
        var reply = await Task.Run(() => session.SendDetailed(command, TimeSpan.FromSeconds(20)));
        var text = (reply.Text ?? "").Replace("\r", "");
        if (reply.Failed || !reply.PickedUp)
        {
            var lines = new[] { text };
            return AdminCommand.IsUnknownCommandReply(lines) ? lines : new[] { ToolsParser.ErrorPrefix + " " + text };
        }

        if (!reply.Answered)
            return new[] { ToolsParser.ErrorPrefix + " " + ConsoleQueue.StillWorking };
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>A list file the module wrote: only its own out folder inside the server is read.</summary>
    private static string? ReadListFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var marker = Path.DirectorySeparatorChar + ToolsCommand.ModuleId + Path.DirectorySeparatorChar + "out" + Path.DirectorySeparatorChar;
            if (!full.Contains(marker, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                return null;
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r", "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends one change for a hero. When the server confirms it, it goes into the admin log (with
    /// the optional note) and, when <paramref name="undo"/> gives the inverse command, can be undone.
    /// </summary>
    public async Task<KeepResult> ChangeAsync(KeepTarget hero, string command, string auditText, string note = "", Func<ToolsChange, string?>? undo = null, bool quiet = false)
    {
        var result = await Client.ChangeAsync(command);
        if (!result.Ok)
        {
            Say(hero.Name + ": " + result.Message, false);
            return result;
        }

        var inverse = undo != null && result.Change != null ? undo(result.Change) : null;
        Log.Add(hero.Name, auditText + Confirmed(result.Change), note,
            inverse == null ? null : async () =>
            {
                var back = await Client.ChangeAsync(inverse);
                if (back.Ok)
                    Log.Add(hero.Name, "undo: " + auditText + Confirmed(back.Change));
                Say(back.Ok ? hero.Name + ": undone" : hero.Name + ": " + back.Message, back.Ok);
            });
        Remember?.Invoke("[Cheats] " + hero.Name + ": " + auditText + Confirmed(result.Change));
        if (!quiet)
            Say(hero.Name + ": " + auditText + Confirmed(result.Change), true);
        return result;
    }

    /// <summary>" (100 → 160)", the value the server read back.</summary>
    private static string Confirmed(ToolsChange? change) =>
        change == null ? "" : " (" + KeepFormat.N(change.Old) + " → " + KeepFormat.N(change.New) + ")";

    public void Say(string message, bool ok) => Status?.Invoke(message, ok);

    /// <summary>
    /// Who can be picked: the heroes the server says look like players, the players saved in the
    /// running campaign's .json and the players connected now; a connected player whose hero is
    /// still unknown is looked up by name.
    /// </summary>
    public async Task<IReadOnlyList<KeepPlayer>> LoadPlayersAsync()
    {
        var live = _livePlayers();
        var save = _saveName();
        var session = string.IsNullOrWhiteSpace(save)
            ? Array.Empty<SessionPlayer>()
            : KeepRoster.ReadSession(Path.Combine(ServerSession.SavesPath, save + ".json"));

        // The save's .json has no hero names, so the names the server gave on an earlier run fill in.
        var known = HeroNames?.For(save);
        if (!CanSend || !ToolsModule)
            return KeepRoster.Merge(live, session, Array.Empty<HeroSummary>(), known);

        if (Client.ToolsReady != true)
            await Client.CheckToolsAsync();
        if (Client.ToolsReady != true)
            return KeepRoster.Merge(live, session, Array.Empty<HeroSummary>(), known);

        var heroes = (await Client.HeroesAsync()).ToList();
        var rows = KeepRoster.Merge(live, session, heroes, known);
        foreach (var name in KeepRoster.Unmatched(rows).Take(20))
            heroes.AddRange((await Client.HeroesAsync(name)).Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)));
        rows = KeepRoster.Merge(live, session, heroes, known);
        HeroNames?.Remember(save, rows.Where(r => r.Hero != null).Select(r => r.Hero!));
        return rows;
    }
}
