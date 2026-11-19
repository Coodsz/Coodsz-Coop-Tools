using System.Diagnostics;
using System.IO;
using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace CoodszHost.Services;

public sealed class LaunchOptions
{
    public int EnginePort { get; init; } = 7210;
    public string Region { get; init; } = "EU";
    public string SaveName { get; init; } = "";
    public bool CreateNew { get; init; }

    // Set only after the operator pressed I accept on the Coop server terms.
    public bool AcceptEula { get; init; }

    // Set only after the host agreed to add mods to this save and a backup was made.
    public bool AllowAddedModules { get; init; }

    // How much Coop Save Rescue may change on this start (the module reads it from the environment).
    public RescueMode Rescue { get; init; }

    // The map mod the host picked (null for the vanilla map). The start script refuses to serve plain Calradia when it brings no Main_map.
    public string? CampaignMap { get; init; }

    public string ToArguments()
    {
        if (EnginePort is < 1 or > 65535)
            throw new InvalidOperationException("Engine port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(Region) || Region.Length is < 2 or > 8 || Region.Any(ch => !char.IsLetter(ch)))
            throw new InvalidOperationException("Region must be 2 to 8 letters.");
        var args = "-NoPrompt -Port " + EnginePort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " -Region " + Region.Trim().ToUpperInvariant();
        var save = SaveName.Trim();
        if (save.Length > 0)
        {
            if (save.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new InvalidOperationException("Save name cannot contain quotes or line breaks.");
            args += (CreateNew ? " -NewSaveName \"" : " -SaveName \"") + save + "\"";
        }

        if (AllowAddedModules && !CreateNew)
            args += " -AllowAddedModules";
        return args;
    }

    public const string TemplateSaveReason =
        "default_new_game is the server's new-world template, not a campaign. The server will not host it. Click New campaign, type a name, and press Start server. Or pick another save.";

    public static string? MissingSaveReason(string? saveName, bool fileExists, bool newCampaign)
    {
        if (string.IsNullOrWhiteSpace(saveName))
            return "Make a new campaign save first. The save field is empty.";
        if (SaveCatalog.IsTemplate(saveName))
            return TemplateSaveReason;
        if (!fileExists && !newCampaign)
            return "That save is not in the dedicated server Game Saves folder. Make a new campaign save, or pick one that is already there.";
        return null;
    }
}

public sealed class ServerSession : IDisposable
{
    // Start refuses a running server; Coodsz Host's StartServer says the same before any module step.
    public const string AlreadyRunningText = "The server is already running.";

    // The dedicated server's own save (its SaveNow). Coop 0.1.6 turns the game's
    // autosave off on the server, so the old coop.debug.save.force_autosave no
    // longer writes anything there; it is only used when launcher.server.save is
    // unknown (an older server module).
    public const string SaveCommand = "launcher.server.save";
    public const string LegacySaveCommand = "coop.debug.save.force_autosave";

    // The dedicated server only runs dotted commands from this tool (kick, say
    // and stop are refused), and its only time command is launcher.time.set.
    public const string PauseCommand = "launcher.time.set Pause";
    public const string PlayCommand = "launcher.time.set Play";

    private readonly object _gate = new();
    private Process? _process;
    // One console line at a time, so two lines never mix (ServerCommandQueue.WriteBounded).
    private readonly object _stdinLock = new();
    // Counts the starts. An exit of an older run (Restart already started the next one) does not
    // raise Exited, so it never shows the crash bar or counts as a crash of the new run.
    private int _run;
    private DateTime _lastStartCheck;
    private FileSystemWatcher? _logWatcher;
    private string? _logPath;
    private long _logPosition;
    private readonly object _consoleGate = new();
    private StreamWriter? _console;

    public const string ConsoleLogPattern = "host-console-*.log";

    // The server output this window captured during its latest start. It is
    // written even when the engine stops before it opens its own coop-server log.
    public string? ConsoleLogPath { get; private set; }

    public event Action<string>? ServerLine;
    // Only the output of the process this window started, never the log tail ServerLine also carries, so a
    // line is seen once (Play hold counts Coop's pauses).
    public event Action<string>? ProcessLine;
    // True while ServerLine replays the end of an older log (opening the window shows the last
    // lines again). Those lines already happened, so nothing should act on them again.
    public bool ReplayingLog { get; private set; }
    public event Action<string>? StatusLine;
    public event Action? StateChanged;

    // The process this window started ended. WasExpected is true when Stop asked for it.
    public event Action<ServerExitInfo>? Exited;

    // The phase from the server's @DS@ state events (boot, loading, serving, stopping),
    // read from its output and from the coop-server log. Older servers without those
    // events still get Serving from the "[DedicatedServer] SERVING" line.
    public ServerPhase Phase { get; private set; } = ServerPhase.Unknown;
    public event Action<ServerPhase>? PhaseChanged;

    private volatile bool _stopRequested;
    private volatile bool _killedThisRun;

    // Stop or KillEngines ended a server process since the last Start. A server that exits with
    // no stop asked for and this still false was ended by something outside this tool.
    public bool KilledThisRun => _killedThisRun;

    public static ServerPhase? PhaseFromLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return null;
        if (DsEventParser.TryParse(line, out var evt))
            return evt is DsEvent.State { Phase: not ServerPhase.Unknown } state ? state.Phase : null;
        if (ServingLine.IsServing(line))
            return ServerPhase.Serving;
        if (line.Contains("engine exited", StringComparison.OrdinalIgnoreCase))
            return ServerPhase.Shutdown;
        return null;
    }

    // Set once this run printed a @DS@ state event. From then on the events alone say the
    // phase, and a text SERVING line (the fallback for older servers) is not used.
    private volatile bool _sawStateEvent;

    // The server sent a @DS@ "fatal" state event: the campaign did not load, so no save path may save it.
    // BrokenWorldGuard.LoadFailedAfter decides: it stays set through the "stopping" after it, and a later
    // boot, loading or serving event clears it. That matters because Start replays the last run's log
    // (Follow, EmitTail) when it was written just before, so its fatal line comes back before the new boot.
    private readonly object _loadGate = new();
    private volatile bool _loadFailed;
    public bool LoadFailed => _loadFailed;

    private void ForgetLoadFailure()
    {
        lock (_loadGate)
            _loadFailed = false;
    }

    // The process this window started (the PowerShell wrapper), while it runs.
    public int? StartedProcessId
    {
        get
        {
            try
            {
                return CurrentProcess() is { HasExited: false } process ? process.Id : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    // HasExited can raise Exited on the thread that asks, and the window's Exited handler waits
    // for the window thread. Asked under _gate, a Stop on a worker thread then waits for the window
    // while the window waits for _gate (a Restart that never started again, with a black window).
    // So _process is only read under _gate and HasExited is always asked outside it.
    private Process? CurrentProcess()
    {
        lock (_gate)
            return _process;
    }

    private void Observe(string? line)
    {
        // The stdout and stderr readers and the log follower all call this.
        lock (_loadGate)
            _loadFailed = BrokenWorldGuard.LoadFailedAfter(_loadFailed, line);
        var phase = PhaseFromLine(line);
        if (phase == null)
            return;
        if (line!.Contains(DsEventParser.Marker, StringComparison.Ordinal))
            _sawStateEvent = true;
        else if (_sawStateEvent && phase == ServerPhase.Serving)
            return;
        if (phase == Phase)
            return;
        Phase = phase.Value;
        PhaseChanged?.Invoke(phase.Value);
    }

    /// <summary>True while the server process this window started runs (not a server found running).</summary>
    public bool OwnsProcess => CurrentProcess() is { HasExited: false };

    public bool IsRunning
    {
        get
        {
            if (CurrentProcess() is { HasExited: false })
                return true;

            if (Process.GetProcessesByName("BannerlordCoopServer").Length > 0)
                return true;
            if (InstanceLockHeld(ServerRoot))
                return true;
            return FindEngineStart() != null;
        }
    }

    public void UseServer(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return;
        ExecutablePath = executable;
    }

    public static bool IsThisServerEngine(string? processPath, string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(processPath)
            || processPath.Replace('/', '\\').IndexOf(@"\DedicatedServer\engine\dotnet", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (string.IsNullOrWhiteSpace(serverRoot))
            return true;
        var root = Path.GetFullPath(serverRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(processPath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // True only while the server process this window launched is still alive.
    public bool StartedHere
    {
        get
        {
            return CurrentProcess() is { HasExited: false };
        }
    }

    // Commands travel through server-data\console-queue, which the running
    // server reads no matter which program started it, so a server found running
    // (with no console pipe from this window) still takes them. The server's own
    // console commands go to the stdin pipe Start hands it (TryConsoleLine).
    public bool CanSendCommands => IsRunning && ServerDataRoots().Count > 0;

    public const string TwoServersRunning =
        "More than one dedicated server is running, and none of them is the one picked in Set server folder. The command was not sent. Pick the server this tool controls with Set server folder, or stop the other one.";

    public string CommandBlockReason()
    {
        if (!IsRunning)
            return "Server is not running.";
        return ServerDataRoots().Count == 0 ? TwoServersRunning : "";
    }

    private IReadOnlyList<string>? _runningRoots;
    private DateTime _runningRootsAt;

    // The DedicatedServer folders of the server engines running right now.
    // Reading every dotnet process is slow, so the answer is kept for 2 seconds.
    public IReadOnlyList<string> RunningServerRoots()
    {
        lock (_gate)
        {
            if (_runningRoots != null && DateTime.UtcNow - _runningRootsAt < TimeSpan.FromSeconds(2))
                return _runningRoots;
        }

        var found = ScanRunningServerRoots();
        lock (_gate)
        {
            _runningRoots = found;
            _runningRootsAt = DateTime.UtcNow;
        }

        return found;
    }

    private static IReadOnlyList<string> ScanRunningServerRoots()
    {
        var roots = new List<string>();
        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                var root = ServerRootFromEngine(process.MainModule?.FileName);
                if (root != null && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    roots.Add(root);
            }
            catch (Exception)
            {
                // A process can refuse its path while it is starting.
            }
        }

        return roots;
    }

    public static string? ServerRootFromEngine(string? enginePath)
    {
        if (!IsThisServerEngine(enginePath, null))
            return null;
        var index = enginePath!.Replace('/', '\\').IndexOf(@"\engine\dotnet", StringComparison.OrdinalIgnoreCase);
        return index > 0 ? enginePath[..index] : null;
    }

    // Where commands and live-players.json live. When the server that is
    // running sits in another folder than the one picked here (Steam and
    // Workshop copies), the running one wins.
    public IReadOnlyList<string> ServerDataRoots()
    {
        var running = RunningServerRoots();
        return ChooseDataRoots(ServerRoot, running);
    }

    // The picked server when it runs (or nothing runs yet), otherwise the one
    // running server. With several other servers running it is unclear which
    // one is meant, so nothing is returned and no command is sent.
    public static IReadOnlyList<string> ChooseDataRoots(string chosenRoot, IReadOnlyList<string> runningRoots)
    {
        if (runningRoots.Count == 0 || runningRoots.Contains(chosenRoot, StringComparer.OrdinalIgnoreCase))
            return new[] { Path.Combine(chosenRoot, "server-data") };
        if (runningRoots.Count == 1)
            return new[] { Path.Combine(runningRoots[0], "server-data") };
        return Array.Empty<string>();
    }

    public string ServerRoot =>
        Path.GetDirectoryName(string.IsNullOrWhiteSpace(ExecutablePath) ? DefaultExecutable : ExecutablePath)
        ?? AppContext.BaseDirectory;

    public DateTime? StartedAt { get; private set; }
    public string? ExecutablePath { get; private set; }

    public static string DefaultExecutable { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "Mount & Blade II Bannerlord",
            "Modules", "Bannerlord Coop", "DedicatedServer", "BannerlordCoopServer.exe");

    public static string DocumentsData { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer");

    public static string ServerConfigPath => Path.Combine(DocumentsData, "server-config.json");

    // The start script reads a server-config.json without allowClientTimeControls as off (Coop #55). The text
    // with the key added as on, or null when the file has it. Shared with the Burning Empires Server Tool.
    public static string? WithClientTimeKey(string serverConfig) => ClientTimeControls.WithKey(serverConfig);

    // Start-ModdedServer.ps1's match for the setting (a test keeps the two equal).
    public const string ScriptClientTimePattern = ClientTimeControls.ScriptPattern;

    public static string ModConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "CoopData", "mod-config.json");
    public static string SavesPath => Path.Combine(DocumentsData, "Game Saves");
    public static string LogsPath => Path.Combine(DocumentsData, "logs");

    public void Start(string executable, LaunchOptions? options = null)
    {
        ExecutablePath = executable;
        if (IsRunning)
            throw new InvalidOperationException(AlreadyRunningText);
        if (!File.Exists(executable))
            throw new InvalidOperationException("Server executable was not found.");

        options ??= new LaunchOptions();
        var folder = Path.GetDirectoryName(executable)!;
        var script = Path.Combine(folder, "Start-ModdedServer.ps1");
        // Steam often ships a thin DedicatedServer without this script. Starting
        // BannerlordCoopServer.exe bare only loads Native/SandBox/Coop, so Host
        // shows dozens of mods on while joins fail with "server does not support".
        if (!File.Exists(script))
            throw new InvalidOperationException(
                "Start-ModdedServer.ps1 is missing next to BannerlordCoopServer.exe. "
                + "Steam installed a thin DedicatedServer folder, so the engine would only load Native, SandBoxCore, Sandbox, Coop, and DedicatedServer.Windows. "
                + "Put Start-ModdedServer.ps1 back in that folder (and 0Harmony.dll under engine\\bin\\Win64_Shipping_Server), then press Start server again.");

        script = HostStartScript.Prepare(folder);
        // The engine and PowerShell write this pipe in the PC's OEM code page (857 on a Turkish Windows); as UTF-8
        // a Turkish save name came in with question marks. The server reads its commands as UTF-8 (ServerConsoleEncoding.Input).
        var consoleEncoding = ServerConsoleEncoding.Oem() ?? new UTF8Encoding(false);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + options.ToArguments(),
            WorkingDirectory = folder,
            UseShellExecute = false,
            // The server reads its own console commands (save, status, stop...) from
            // stdin; Start-ModdedServer.ps1 hands this pipe to the server engine.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = ServerConsoleEncoding.Input(),
            StandardOutputEncoding = consoleEncoding,
            StandardErrorEncoding = consoleEncoding
        };
        if (options.AcceptEula)
            start.Environment[ServerEula.EnvironmentVariable] = "true";
        start.Environment[AutoRescue.EnvironmentVariable] = AutoRescue.ModeValue(options.Rescue);
        if (MissingCampaignMap.EnvironmentValue(options.CampaignMap) is { } map)
            start.Environment[MissingCampaignMap.EnvironmentVariable] = map;

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var consoleLog = OpenConsoleLog(start.Arguments);
        // The run moves and the old process goes in one step, before the queue is cleared: a save, stop or say that began
        // before this Start carries the old run, so its console fallback writes nothing (TryConsoleLine checks the run), and
        // one that begins now finds no process until the new one is set below, so it can't reach the old one either.
        int run;
        lock (_gate)
        {
            run = ++_run;
            _process = null;
        }

        // A tool closed while it waited for an answer leaves its command file, and this new run would run it. A file
        // that stays locked is named in the host log; the start still goes ahead. A Send still waiting on a cleared file
        // gets a reply marked Cleared, and nothing falls back to a console after that.
        foreach (var locked in ConsoleQueue.ClearPending(Path.Combine(folder, "server-data", "console-queue")).Locked)
            WriteConsole("[CoodszHost] A waiting command file could not be removed before the start, so the server may still run it: " + locked);
        _stopRequested = false;
        _killedThisRun = false;
        ForgetLoadFailure();
        _sawStateEvent = false;
        Phase = ServerPhase.Starting;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { Observe(e.Data); WriteConsole(e.Data); ServerLine?.Invoke(e.Data); ProcessLine?.Invoke(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { Observe(e.Data); WriteConsole(e.Data); ServerLine?.Invoke(e.Data); ProcessLine?.Invoke(e.Data); } };
        process.Exited += (_, _) =>
        {
            // Exited can fire before the last output lines arrive. Waiting here lets
            // the async readers finish, so the error that stopped the server is kept.
            try { process.WaitForExit(5000); }
            catch (InvalidOperationException) { }
            int? exitCode;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { exitCode = null; }
            var code = exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            // A restart may already have opened the next run's log; leave that one open.
            CloseConsoleLog(consoleLog, "[CoodszHost] Server process exited with code " + code + ".");
            bool current;
            lock (_gate)
            {
                // A newer start already owns the phase.
                if (ReferenceEquals(_process, process))
                    Phase = ServerPhase.Unknown;
                current = run == _run;
            }

            if (exitCode != null && current)
                Exited?.Invoke(ServerExitInfo.From(exitCode.Value, _stopRequested));
            StatusLine?.Invoke(exitCode == null ? "Server process exited." : "Server process exited with code " + code + ".");
            StateChanged?.Invoke();
        };

        bool started;
        try
        {
            started = process.Start();
        }
        catch
        {
            CloseConsoleLog();
            throw;
        }

        if (!started)
        {
            WriteConsole("[CoodszHost] The server did not start.");
            CloseConsoleLog();
            throw new InvalidOperationException("The server did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
            _process = process;
        ExecutablePath = executable;
        StartedAt = DateTime.Now;
        WatchLogs();
        StatusLine?.Invoke(File.Exists(script)
            ? "Modded server is starting."
            : "Server started.");
        StateChanged?.Invoke();
    }

    public void AttachLogs()
    {
        SyncStartedAt();
        WatchLogs();
        StateChanged?.Invoke();
    }

    public void SyncStartedAt()
    {
        if (StartedAt != null && DateTime.Now - _lastStartCheck < TimeSpan.FromSeconds(5))
            return;
        _lastStartCheck = DateTime.Now;
        var engineStarted = FindEngineStart();
        if (engineStarted != null)
        {
            if (StartedAt == null || engineStarted.Value > StartedAt.Value.AddSeconds(5))
                StartedAt = engineStarted;
            return;
        }

        if (IsRunning && StartedAt == null)
        {
            var lockPath = Path.Combine(ServerRoot, "server-data", "server-instance.lock");
            StartedAt = File.Exists(lockPath) ? File.GetCreationTime(lockPath) : DateTime.Now;
        }
    }

    private DateTime? FindEngineStart()
    {
        DateTime? newest = null;
        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!IsThisServerEngine(path, ServerRoot))
                    continue;
                if (newest == null || process.StartTime > newest)
                    newest = process.StartTime;
            }
            catch (Exception)
            {
                // A process can refuse its path while it is starting.
            }
        }

        return newest;
    }

    public string RequestSave()
    {
        if (!CanSendCommands)
            throw new InvalidOperationException(CommandBlockReason());
        var reply = SendSave();
        ThrowIfSaveRefused(reply);
        return string.IsNullOrWhiteSpace(reply.Text) ? "Save sent." : reply.Text.Trim();
    }

    // Every write of one save stays with the server run it began in (<paramref name="run"/>, or the current one): after a
    // Start the legacy retry and the console fallback write nothing.
    private ConsoleQueueReply SendSave(int? run = null)
    {
        var saveRun = run ?? CurrentRun();
        return SaveReply(() =>
        {
            var reply = SendDetailed(SaveCommand);
            if (!reply.Failed || !reply.Text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase))
                return reply;
            return SameRun(saveRun) ? SendDetailed(LegacySaveCommand) : RunEndedReply;
        }, () => TryConsoleLine("save", saveRun), () => StartedHere);
    }

    // Which server start this is; a caller that waited compares it to see a Restart happened meanwhile.
    public int RunId => CurrentRun();

    private int CurrentRun()
    {
        lock (_gate)
            return _run;
    }

    private bool SameRun(int run) => run == CurrentRun();

    // A write a Start overtook: marked like a cleared command, so nothing falls back to the next run's console.
    private static readonly ConsoleQueueReply RunEndedReply = new(false, false, true, ServerCommandQueue.RunEnded) { WasCleared = true };

    // Save game's rules, apart so the tests can count the console writes. Since the October dedicated-server update,
    // launcher.server.save answers "The dedicated-server save service is unavailable": the save bridge it called was
    // removed, and the server's own console command "save" still works, so the console is tried then. Never after a
    // save a server start cleared (it belonged to the run before). A save stuck on a full pipe may still land, so it is
    // not a failure, and the caller never sends it again.
    internal static ConsoleQueueReply SaveReply(Func<ConsoleQueueReply> queue, Func<ConsoleWrite> console, Func<bool> startedHere)
    {
        var reply = queue();
        if (reply.WasCleared)
            return reply;
        if (reply.Succeeded && !reply.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return reply;
        var written = console();
        if (written == ConsoleWrite.Sent)
            return new ConsoleQueueReply(true, true, false, "Save sent to the server.");
        if (written == ConsoleWrite.MaybeSent)
            return new ConsoleQueueReply(true, false, false, ServerCommandQueue.ConsoleSlow);
        if (reply.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return reply with { Text = startedHere() ? ServerCommandQueue.ConsoleTimeout : SaveNeedsRestart };
        return reply;
    }

    // Stop's rules: the console first; only a stop that was never written (NotSent) goes the console queue's way, where
    // only a stop the server took and did not refuse counts (the queue answers "unknown" to stop today, so that is no).
    // A cleared stop was not taken. Asked means the save on stop is waited for.
    internal static (bool Asked, ConsoleWrite Console) StopAsked(Func<ConsoleWrite> console, Func<ConsoleQueueReply?> queue)
    {
        var written = console();
        if (written != ConsoleWrite.NotSent)
            return (true, written);
        var reply = queue();
        return (reply != null && reply.Succeeded && !reply.Text.Contains("unknown", StringComparison.OrdinalIgnoreCase), written);
    }

    // A chat line to the players (restart warnings). Like stop: the queue only when the console never took it, so a say
    // that may still land is never sent twice. The queue today only runs dotted commands, so say there is refused; only an
    // answer that is no refusal counts as told, never a file that went without one.
    public ConsoleWrite Say(string message)
    {
        var line = ServerCommand.Say(message);
        return SayResult(() => TryConsoleLine(line), () => CanSendCommands ? SendDetailed(line) : null);
    }

    internal static ConsoleWrite SayResult(Func<ConsoleWrite> console, Func<ConsoleQueueReply?> queue)
    {
        var written = console();
        if (written != ConsoleWrite.NotSent)
            return written;
        var reply = queue();
        return reply is { Answered: true, Failed: false } && !reply.Text.Contains("unknown", StringComparison.OrdinalIgnoreCase) ? ConsoleWrite.Sent : ConsoleWrite.NotSent;
    }

    public const string BuiltinSent = "Sent to the server's own console. Its answer shows in the server output.";

    public const string BuiltinNeedsRestart =
        "That is a server console command, and only a server started from this tool has a console it can reach. Stop the server once and start it again here.";

    public const string StopUseButton = "To stop the server, click Stop server. It saves the campaign first.";

    public const string KickNeedsName = "Type kick and the player's name or id, for example kick 2.";

    public const string SayNeedsText = "Type say and the message, for example say Restart in 5 minutes.";

    public const string SaveNeedsRestart =
        $"This server was started before {Edition.Name} was opened, so Save game cannot reach it. Stop it once and start it again from {Edition.Name}; after that Save game works.";

    // Writes one line to the server's own console. Only works for a server this
    // Coodsz Host started (a server found after reopening has no console pipe).
    // That console also runs every game cheat, so only the builtin commands in
    // ServerCommand.Allowed, on one line, are written. A server that stops reading its
    // console gets 5 seconds. NotSent was never written, so the console queue may still be
    // tried. MaybeSent began but was stuck on a full pipe and may still land: not a failure,
    // and never sent again. A stuck write keeps the lock, so later lines give up too. With
    // <paramref name="run"/> only that server run's console is written: a request that began before
    // a Restart never reaches the next run.
    public ConsoleWrite TryConsoleLine(string line, int? run = null)
    {
        if (!ServerCommand.IsAllowed(line))
            return ConsoleWrite.NotSent;
        Process? process;
        int writeRun;
        lock (_gate)
        {
            if (run != null && run != _run)
                return ConsoleWrite.NotSent;
            process = _process;
            writeRun = _run;
        }

        try
        {
            if (process is not { HasExited: false } || !process.StartInfo.RedirectStandardInput)
                return ConsoleWrite.NotSent;
            var stdin = process.StandardInput;
            var verb = line.Split(' ')[0];
            var (written, writing) = ServerCommandQueue.WriteBounded(_stdinLock, ServerCommandQueue.StdinLockWait, () => { stdin.WriteLine(line); stdin.Flush(); })
                .GetAwaiter().GetResult();
            if (written == ConsoleWrite.Sent)
                return written;
            WriteConsole("[CoodszHost] The server's console did not take " + verb + " within 5 seconds.");
            // A write still stuck on the pipe that breaks later is one more line in that run's log, never an unseen fault;
            // once a Start moved on, the line would land in the next run's log, so it is dropped.
            if (written == ConsoleWrite.MaybeSent)
                writing.ContinueWith(task =>
                    {
                        if (SameRun(writeRun))
                            WriteConsole("[CoodszHost] The server's console did not take " + verb + ": " + task.Exception!.InnerException!.Message);
                    },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return written;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
        {
            WriteConsole("[CoodszHost] The server's console did not take " + line.Split(' ')[0] + ": " + ex.Message);
            return ConsoleWrite.NotSent;
        }
    }

    private static void ThrowIfSaveRefused(ConsoleQueueReply reply)
    {
        if (!reply.PickedUp)
            throw new InvalidOperationException(reply.Text);
        if (reply.Failed
            || reply.Text.Contains("No active campaign", StringComparison.OrdinalIgnoreCase)
            || reply.Text.Contains("save failed", StringComparison.OrdinalIgnoreCase)
            || reply.Text.Contains("was not started", StringComparison.OrdinalIgnoreCase)
            || reply.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(reply.Text);
    }

    public string SaveCurrentWorld(string saveName)
    {
        var name = saveName.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Choose a campaign save before saving.");
        if (name.IndexOfAny(new[] { '"', '\\', '/', '\r', '\n' }) >= 0)
            throw new InvalidOperationException("Save name cannot contain quotes or path separators.");
        if (!CanSendCommands)
            throw new InvalidOperationException(CommandBlockReason());

        var path = Path.Combine(SavesPath, name + ".sav");
        var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        var started = DateTime.UtcNow.AddSeconds(-1);
        var run = CurrentRun();
        var sent = SendSave(run);
        ThrowIfSaveRefused(sent);
        // A save that may still land is never sent a second time.
        var unconfirmed = sent.Text == ServerCommandQueue.ConsoleSlow;

        // The Coop server queues that save for the next campaign tick, and the
        // campaign does not tick while time is stopped (no players, or paused).
        // After a few seconds time is let run briefly, then paused again.
        var deadline = DateTime.UtcNow.AddSeconds(45);
        var nudgeAt = DateTime.UtcNow.AddSeconds(8);
        var resumed = false;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > before)
                    return "Saved " + name + ".";
                var written = NewestSaveSince(started);
                if (written != null)
                    return "Saved " + Path.GetFileNameWithoutExtension(written) + ".";
                // A Start in between means this server is gone: the nudge never writes to the next run.
                if (!IsRunning || !SameRun(run))
                    throw new InvalidOperationException("The server stopped before " + name + " was saved.");
                if (!resumed && DateTime.UtcNow >= nudgeAt)
                {
                    resumed = SendDetailed(PlayCommand).Succeeded;
                    if (!unconfirmed && SameRun(run))
                        SendSave(run);
                }

                Thread.Sleep(200);
            }
        }
        finally
        {
            if (resumed && IsRunning && SameRun(run))
                SendDetailed(PauseCommand);
        }

        throw new InvalidOperationException(SaveNotWritten(name));
    }

    public static string SaveNotWritten(string name) =>
        "The server did not save " + name + ". This Coop server writes the save on its autosave timer, and a save asked for while campaign time is stopped can wait until time runs. "
        + "Let time run for a moment and press Save game again, or set a shorter Autosave interval in Settings. The last autosave is still in Game Saves.";

    private static string? NewestSaveSince(DateTime startedUtc)
    {
        if (!Directory.Exists(SavesPath))
            return null;
        return Directory.GetFiles(SavesPath, "*.sav")
            .Select(file => new FileInfo(file))
            .Where(file => file.LastWriteTimeUtc >= startedUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    public enum StopOutcome
    {
        // The server took stop, wrote its save on stop and exited.
        Stopped,
        // It took stop but was still running after the wait, so the engine was ended.
        Ended,
        // Stop could not reach it (a server started before this window opened), so it was ended.
        NotReached,
        // This window's own server did not take stop on its console in time, so it was ended.
        NotTaken
    }

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(120);

    // The way the server's own console stops it: "stop" makes it write its save on stop
    // and exit. Written to the console pipe when this window started the server, else to
    // server-data\console-queue (the queue only runs dotted commands today, so that is
    // tried and its answer read). Waits up to two minutes, then ends whatever is left of
    // the engine's process tree. A stop that may still land on the pipe is waited for like a
    // sent one and never written a second time through the queue.
    public StopOutcome StopGracefully(TimeSpan? wait = null)
    {
        _stopRequested = true;
        var ownConsole = StartedHere;
        var (asked, console) = StopAsked(() => TryConsoleLine(ServerCommand.Stop()), () => CanSendCommands ? SendDetailed(ServerCommand.Stop()) : null);
        var outcome = ownConsole ? StopOutcome.NotTaken : StopOutcome.NotReached;
        if (asked)
        {
            var seconds = (int)(wait ?? StopWait).TotalSeconds;
            WriteConsole(console == ConsoleWrite.MaybeSent
                ? "[CoodszHost] Stop may not have reached the server yet, since its console is slow. Waiting up to " + seconds + " seconds for the save on stop."
                : "[CoodszHost] Sent stop. Waiting up to " + seconds + " seconds for the save on stop.");
            var deadline = DateTime.UtcNow + (wait ?? StopWait);
            while (ThisServerRunning() && DateTime.UtcNow < deadline)
                Thread.Sleep(500);
            outcome = ThisServerRunning() ? StopOutcome.Ended : StopOutcome.Stopped;
        }

        Stop();
        return outcome;
    }

    // Unlike IsRunning, a TAOM server (also BannerlordCoopServer.exe) does not count.
    private bool ThisServerRunning()
    {
        if (CurrentProcess() is { HasExited: false })
            return true;

        return FindEngineStart() != null || InstanceLockHeld(ServerRoot);
    }

    // Ends the server now, without its save on stop. Stop on a broken world, Last good
    // save and the autosave guard need exactly that.
    public void Stop()
    {
        _stopRequested = true;
        Process? process;
        lock (_gate)
            process = _process;
        if (process is { HasExited: false })
        {
            // Start-ModdedServer.ps1 runs the engine as its child, so the whole tree goes.
            _killedThisRun = true;
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { process.WaitForExit(5000); }
            catch (InvalidOperationException) { }
            KillEngines();
        }
        else
        {
            // Only the server in this tool's folder (or the single running one).
            // A TAOM server also runs as BannerlordCoopServer.exe and must be left alone.
            var roots = ServerDataRoots().Select(root => Path.GetDirectoryName(root) ?? root).ToList();
            foreach (var existing in Process.GetProcessesByName("BannerlordCoopServer"))
            {
                try
                {
                    var path = existing.MainModule?.FileName;
                    if (path == null || !roots.Any(root => IsUnder(path, root)))
                        continue;
                    existing.CloseMainWindow();
                    _killedThisRun = true;
                    if (!existing.WaitForExit(8000))
                        existing.Kill();
                }
                catch (Exception)
                {
                    // The process ended or refused its path.
                }
            }

            KillEngines();
        }

        if (InstanceLockHeld(ServerRoot))
            StopForeignHost();
        Phase = ServerPhase.Unknown;
        ForgetLoadFailure();
        StartedAt = null;
        StatusLine?.Invoke("Server stopped.");
        StateChanged?.Invoke();
    }

    // The engine of the server in this tool's folder only (engine\dotnet\dotnet.exe under
    // ServerRoot), so a TAOM server or another copy keeps running.
    private void KillEngines()
    {
        foreach (var process in Process.GetProcessesByName("dotnet"))
        {
            try
            {
                if (!IsThisServerEngine(process.MainModule?.FileName, ServerRoot))
                    continue;
                _killedThisRun = true;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                WriteConsole("[CoodszHost] Ended the server engine (process " + process.Id + ").");
            }
            catch (Exception)
            {
                // The process ended or refused its path.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public ConsoleQueueReply SendDetailed(string command, TimeSpan? timeout = null)
    {
        var blocked = CommandBlockReason();
        if (blocked.Length > 0)
            return new ConsoleQueueReply(false, false, true, blocked);
        return Explain(ConsoleQueue.Send(ServerDataRoots(), command, timeout));
    }

    // When the server did not take a command, say where it was put and which
    // running server this tool can see, and keep that in the host-console log.
    internal ConsoleQueueReply Explain(ConsoleQueueReply reply)
    {
        if (reply.PickedUp)
            return reply;
        var text = reply.Text + " " + NotPickedUpDetail(reply.Folders, RunningServerRoots(), ServerRoot);
        WriteConsole("[CoodszHost] Command not picked up. " + text);
        return reply with { Text = text };
    }

    public static string NotPickedUpDetail(IReadOnlyList<string> folders, IReadOnlyList<string> runningRoots, string chosenRoot)
    {
        var where = folders.Count == 0 ? "no folder" : string.Join(" and ", folders);
        var running = runningRoots.Count == 0
            ? "This tool could not see a running server engine (engine\\dotnet\\dotnet.exe), so it used the folder from Set server folder."
            : "Running server: " + string.Join(", ", runningRoots) + ".";
        var hint = runningRoots.Count == 0 || runningRoots.Contains(chosenRoot, StringComparer.OrdinalIgnoreCase)
            ? " If the status says Server online, click Send my logs and post the code in Discord."
            : " The running server is not the one in Set server folder. Pick it with Set server folder.";
        return "Command folder: " + where + ". " + running + hint;
    }

    public string Send(string command)
    {
        var text = command.Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("Enter a command.");
        if (text.Length > 2048)
            throw new InvalidOperationException("Commands may not exceed 2048 characters.");
        var blocked = CommandBlockReason();
        if (blocked.Length > 0)
            return blocked;

        // save, status, players and the other builtins belong to the server's own console, which the
        // command queue never runs: save goes the way of the Save game button, the rest to stdin.
        var builtin = ServerCommand.TypedBuiltin(text);
        if (builtin == ServerCommand.Save())
            return RequestSave();
        // stop written here would skip what the Stop button does (the save first, the crash restart, the status).
        if (builtin == ServerCommand.Stop())
            return StopUseButton;
        if (builtin == "kick")
            return KickNeedsName;
        if (builtin == "say")
            return SayNeedsText;
        if (builtin != null)
        {
            return TryConsoleLine(builtin) switch
            {
                ConsoleWrite.Sent => BuiltinSent,
                ConsoleWrite.MaybeSent => ServerCommandQueue.ConsoleSlow,
                _ => StartedHere ? ServerCommandQueue.ConsoleTimeout : BuiltinNeedsRestart
            };
        }

        var reply = Explain(ConsoleQueue.Send(ServerDataRoots(), text));
        return reply.Failed ? "The server refused that command: " + reply.Text : reply.Text;
    }

    private StreamWriter? OpenConsoleLog(string arguments)
    {
        CloseConsoleLog();
        try
        {
            Directory.CreateDirectory(LogsPath);
            var path = Path.Combine(LogsPath, "host-console-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            lock (_consoleGate)
                _console = writer;
            ConsoleLogPath = path;
            WriteConsole("[CoodszHost] Start " + ExecutablePath + " " + arguments);
            return writer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConsoleLogPath = null;
            return null;
        }
    }

    private void CloseConsoleLog(StreamWriter? writer, string lastLine)
    {
        if (writer == null)
            return;
        lock (_consoleGate)
        {
            if (!ReferenceEquals(_console, writer))
                return;
        }

        WriteConsole(lastLine);
        lock (_consoleGate)
        {
            if (!ReferenceEquals(_console, writer))
                return;
            _console.Dispose();
            _console = null;
        }
    }

    // A note from this window kept in the run's console log, next to the server output.
    public void NoteConsole(string line) => WriteConsole("[CoodszHost] " + line);

    private void WriteConsole(string line)
    {
        lock (_consoleGate)
        {
            if (_console == null)
                return;
            try
            {
                _console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + BannerlordCoopLauncher.Core.Support.ProblemReport.Redact(line));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private void CloseConsoleLog()
    {
        lock (_consoleGate)
        {
            _console?.Dispose();
            _console = null;
        }
    }

    public void Dispose()
    {
        CloseConsoleLog();
        _logWatcher?.Dispose();
        lock (_gate)
            _process?.Dispose();
    }

    public const string NotPickedUp = ConsoleQueue.NotPickedUp;
    public const string StillWorking = ConsoleQueue.StillWorking;

    public static bool InstanceLockHeld(string serverRoot)
    {
        var path = Path.Combine(serverRoot, "server-data", "server-instance.lock");
        if (!File.Exists(path))
            return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Stops the PowerShell wrapper and engine of the server in this folder only,
    // so a TAOM server or another copy keeps running.
    public static string ForeignHostStopArguments(int selfId, string serverRoot)
    {
        var root = serverRoot.Replace("'", "''").Replace("\"", "");
        return "-NoLogo -NoProfile -Command \"$self = " + selfId + "; $root = '" + root + "'; "
            + "Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $self -and $_.Name -ne 'CoodszHost' -and $_.CommandLine "
            + "-and $_.CommandLine -notlike '*Get-CimInstance*' "
            + "-and ($_.CommandLine -like '*Start-ModdedServer.ps1*' -or $_.CommandLine -like '*TaleWorlds.Starter.DotNetCore.dll*') "
            + "-and ($_.CommandLine.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase))) } "
            + "| ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"";
    }

    private void StopForeignHost()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = ForeignHostStopArguments(Environment.ProcessId, ServerRoot),
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(8000);
        }
        catch (Exception ex)
        {
            StatusLine?.Invoke(ex.Message);
        }
    }

    private void WatchLogs()
    {
        if (!Directory.Exists(LogsPath))
            return;
        _logWatcher?.Dispose();
        _logWatcher = new FileSystemWatcher(LogsPath, "coop-server-*.log")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _logWatcher.Created += (_, e) => Follow(e.FullPath, fromStart: false);
        _logWatcher.Changed += (_, _) => Pump();
        _logWatcher.EnableRaisingEvents = true;
        var latest = new DirectoryInfo(LogsPath).GetFiles("coop-server-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest != null)
            Follow(latest.FullName, fromStart: false);
    }

    private void Follow(string path, bool fromStart)
    {
        _logPath = path;
        var currentLog = StartedAt == null || File.GetLastWriteTime(path) >= StartedAt.Value.AddSeconds(-5);
        if (fromStart)
            _logPosition = 0;
        else if (!currentLog)
        {
            _logPosition = new FileInfo(path).Length;
            return;
        }
        else
        {
            EmitTail(path, 120);
            _logPosition = new FileInfo(path).Length;
        }

        Pump();
    }

    private void EmitTail(string path, int maxLines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Enqueue(line);
                if (lines.Count > maxLines)
                    lines.Dequeue();
            }

            ReplayingLog = true;
            try
            {
                foreach (var item in lines)
                {
                    Observe(item);
                    ServerLine?.Invoke(item);
                }
            }
            finally
            {
                ReplayingLog = false;
            }
        }
        catch (IOException)
        {
        }
    }

    private void Pump()
    {
        if (_logPath == null || !File.Exists(_logPath))
            return;
        try
        {
            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _logPosition)
                _logPosition = 0;
            stream.Position = _logPosition;
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                Observe(line);
                ServerLine?.Invoke(line);
            }

            _logPosition = stream.Position;
        }
        catch (IOException)
        {
        }
    }
}
