#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // ServerCrashTrace's plain rules, the same as Burning Empires Coop Compat's, without game
    // types, so the launcher tests can run them.
    internal static class CrashTraceRules
    {
        public const string ProbeException = "System.ArgumentOutOfRangeException";
        public const string ProbeReader = "LiteNetLib.Utils.NetDataReader.GetString";
        public const string ProbeFrame = "Coop.Core.Server.CoopServer.OnConnectionRequest";

        // The tools' Check server probe sends FF FF as its password on purpose
        // (UdpServerProbe), so LiteNetLib's GetString throws inside Coop's OnConnectionRequest,
        // which catches it and rejects the probe as malformed password data. Expected noise, so
        // only that exact stack is skipped: the throw inside .NET (ThrowHelper, then
        // UTF8Encoding.GetCharCount), then GetString as the first frame outside .NET, called
        // straight from OnConnectionRequest. Any other exception under OnConnectionRequest
        // still prints.
        public static bool IsProbeNoise(string exceptionType, IEnumerable<string> frames)
        {
            if (!string.Equals(exceptionType, ProbeException, StringComparison.Ordinal) || frames == null)
                return false;
            var outside = frames.SkipWhile(frame => frame != null && frame.StartsWith("System.", StringComparison.Ordinal)).Take(2).ToList();
            return outside.Count == 2
                && string.Equals(outside[0], ProbeReader, StringComparison.Ordinal)
                && string.Equals(outside[1], ProbeFrame, StringComparison.Ordinal);
        }

        public const string SeqHttpException = "System.Net.Http.HttpRequestException";
        public const string SeqSocketException = "System.Net.Sockets.SocketException";
        public const string SeqAddress = "localhost:5341";

        // Coop sends its log to a Seq server at localhost:5341 (Common.Logging.LogManager) that
        // nobody runs, so every retry is refused. Each await level throws the error again with a
        // new stack, which would fill the trace up to MaxPrinted. Expected noise, so two errors
        // are skipped. The HTTP error that names localhost:5341 (.NET adds the address in every
        // message language), and the socket error under it while .NET connects, with nothing but
        // .NET on the stack. The same errors for another address, or with a frame of Coop or a
        // mod on the stack, still print.
        public static bool IsSeqSinkNoise(string exceptionType, string message, IEnumerable<string> frames)
        {
            if (string.Equals(exceptionType, SeqHttpException, StringComparison.Ordinal))
                return message != null && message.IndexOf(SeqAddress, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!string.Equals(exceptionType, SeqSocketException, StringComparison.Ordinal) || frames == null)
                return false;
            var list = frames.ToList();
            return list.Any(IsConnectFrame) && list.All(IsDotNetFrame);
        }

        // Socket.ConnectAsync and HttpConnectionPool's connect steps, as their async state
        // machines show on the stack.
        private static bool IsConnectFrame(string frame)
        {
            return frame != null
                && (frame.StartsWith("System.Net.Sockets.Socket+<<ConnectAsync>", StringComparison.Ordinal)
                    || frame.StartsWith("System.Net.Http.HttpConnectionPool+<Connect", StringComparison.Ordinal));
        }

        private static bool IsDotNetFrame(string frame)
        {
            return frame != null
                && (frame.StartsWith("System.", StringComparison.Ordinal) || frame.StartsWith("Microsoft.", StringComparison.Ordinal));
        }

        // Exceptions the engine or Coop throws and handles itself while it probes, seen on every
        // boot of the 2026-11-09 nightly with Realm of Thrones (boot test 2). Each rule needs the
        // exception type plus a message part or a frame, never a whole type, so the same type thrown
        // anywhere else is still logged. NullReferenceException is only skipped where
        // GameTextManager.LoadFromXML throws and catches it itself.
        public static bool IsHandledNoise(string exceptionType, string message, IList<string> frames)
        {
            if (exceptionType == null || frames == null)
                return false;
            message = message ?? "";
            switch (exceptionType)
            {
                // The engine lists modules and asks each assembly for its file, which a dynamic
                // (Harmony made) assembly does not have.
                case "System.NotSupportedException":
                    return message.Contains("dynamic assembly") && Has(frames, "TaleWorlds.DotNet.Managed.GetModuleList");
                // Harmony's AccessTools.Method (2.4.2) asks Type.GetMethod by name, catches this
                // when the name is overloaded and asks again for the overload without parameters.
                // Only that shape: the runtime's own message, thrown inside GetMethod right under
                // AccessTools.Method's lookup, whoever called it. Harmony's own rethrow when the
                // second ask fails too has another message and stays visible.
                case "System.Reflection.AmbiguousMatchException":
                    return message == "Ambiguous match found." && IsHarmonyMethodLookup(frames);
                // A UI assembly the server never has, missed while the engine lists types:
                // GetTypesSafe catches it, and so does the engine's crash information collector.
                // Only the names in EngineUiAssemblies, and only when that probe is the first frame
                // outside .NET, so a mod DLL with a missing dependency is still logged.
                case "System.IO.FileNotFoundException":
                case "System.Reflection.ReflectionTypeLoadException":
                    return OnlyEngineUiAssemblies(message) && IsEngineTypeProbe(frames);
                // Coop checks every file next to the runtime for being a .NET assembly; the native
                // ones answer with this.
                case "System.BadImageFormatException":
                    return Has(frames, "System.Reflection.AssemblyName.GetAssemblyName")
                        && Has(frames, "GameInterface.AutoSync.Builders.AutoSyncBuilder.IsManagedAssembly");
                // A string node without an attribute; LoadFromXML catches everything per node.
                case "System.NullReferenceException":
                    return frames.Count > 0 && frames[0] == "TaleWorlds.Core.GameTextManager.LoadFromXML";
                // The engine building a stack text that holds a patched method, which has no token.
                case "System.InvalidOperationException":
                    return frames.Count > 1 && frames[0] == "System.Reflection.MemberInfo.get_MetadataToken"
                        && frames[1] == "TaleWorlds.DotNet.Managed.GetStackTraceRaw";
                default:
                    return false;
            }
        }

        // The engine's UI assemblies the server is never given, as boot test 2 logged them.
        public static readonly string[] EngineUiAssemblies = { "StoryMode.GauntletUI", "NavalDLC.View", "NavalDLC.ViewModelCollection" };

        private const string LoadFailure = "Could not load file or assembly '";

        // Other loader errors a type list can hold: a type, field or method that does not load.
        private static readonly string[] OtherLoadFailures =
        {
            "Could not load type", "Could not load field", "does not have an implementation", "Method not found", "Field not found", "Could not resolve",
        };

        // True when the message names at least one assembly and every one is an engine UI assembly.
        // A message may name a path or a full name; the simple name is compared exactly. .NET puts
        // each loader error of a type list on its own line, so a line that is not a missing file, or
        // other loader wording anywhere, keeps the exception visible.
        public static bool OnlyEngineUiAssemblies(string message)
        {
            if (message == null || OtherLoadFailures.Any(text => message.IndexOf(text, StringComparison.Ordinal) >= 0))
                return false;
            if (message.Split('\n').Skip(1).Select(line => line.Trim()).Any(line => line.Length > 0 && !line.StartsWith(LoadFailure, StringComparison.Ordinal)))
                return false;
            var names = new List<string>();
            var at = 0;
            while (message != null && (at = message.IndexOf(LoadFailure, at, StringComparison.Ordinal)) >= 0)
            {
                at += LoadFailure.Length;
                var end = message.IndexOf('\'', at);
                if (end < 0)
                    return false;
                names.Add(SimpleName(message.Substring(at, end - at)));
                at = end;
            }
            return names.Count > 0 && names.All(name => EngineUiAssemblies.Contains(name, StringComparer.Ordinal));
        }

        private static string SimpleName(string reference)
        {
            var comma = reference.IndexOf(',');
            var name = comma < 0 ? reference : reference.Substring(0, comma);
            var slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0)
                name = name.Substring(slash + 1);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim();
        }

        private static string FirstOutsideDotNet(IList<string> frames) =>
            frames.FirstOrDefault(f => f == null || !f.StartsWith("System.", StringComparison.Ordinal));

        private static bool IsEngineTypeProbe(IList<string> frames)
        {
            var first = FirstOutsideDotNet(frames);
            return first == "TaleWorlds.Library.Extensions.GetTypesSafe" || first == "TaleWorlds.Engine.CrashInformationCollector.CollectInformation";
        }

        // System frames down to Type.GetMethod, then AccessTools.Method's lookup lambda, then
        // FindIncludingBaseTypes, then AccessTools.Method itself.
        private static bool IsHarmonyMethodLookup(IList<string> frames)
        {
            var outside = frames.SkipWhile(f => f != null && f.StartsWith("System.", StringComparison.Ordinal)).Take(3).ToList();
            return frames.Contains("System.Type.GetMethod")
                && outside.Count == 3
                && outside[0] != null && outside[0].StartsWith("HarmonyLib.AccessTools+", StringComparison.Ordinal) && outside[0].EndsWith(".<Method>b__0", StringComparison.Ordinal)
                && outside[1] == "HarmonyLib.AccessTools.FindIncludingBaseTypes"
                && outside[2] == "HarmonyLib.AccessTools.Method";
        }

        private static bool Has(IList<string> frames, string frame) => frames.Any(f => string.Equals(f, frame, StringComparison.Ordinal));

        public const int MaxFrames = 60;
        public const int MaxMessage = 600;

        // The whole first chance stack is wanted, since the frame that matters for a boot crash
        // can sit deep under the engine's own. A runaway recursion still stops at MaxFrames, so
        // one line can never be huge.
        public static string StackText(IEnumerable<string> frames)
        {
            var list = (frames ?? Enumerable.Empty<string>()).ToList();
            var text = new StringBuilder();
            foreach (var frame in list.Take(MaxFrames))
                text.Append(Environment.NewLine).Append("    at ").Append(frame ?? "?");
            if (list.Count > MaxFrames)
                text.Append(Environment.NewLine).Append("    ... ").Append(list.Count - MaxFrames).Append(" more frames");
            return text.ToString();
        }

        // One line per message, so a multi line message cannot pass for log lines of its own.
        public static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= MaxMessage ? text : text.Substring(0, MaxMessage) + "...";
        }

        // Every Realm of Thrones server start makes the native engine write about 90 report folders
        // (one per second with a warning) under ProgramData\Mount and Blade II Bannerlord\crashes. An
        // experiment: the native console command named in Rgl.dll's command list (no argument known,
        // so it is sent bare), at module load on the server, only with COODSZ_ROT_NOREPORTS=1.
        public const string NoReportsVariable = "COODSZ_ROT_NOREPORTS";
        public const string NoReportsCommand = "donot_create_dumps_for_warnings";

        public static bool NoReportsOn(string variable) => (variable ?? "").Trim() == "1";

        public static string NoReportsLine(string answer) =>
            "Crash reports: sent the engine console command \"" + NoReportsCommand + "\" (" + NoReportsVariable + "=1, an experiment); the engine answered: "
            + (string.IsNullOrWhiteSpace(answer) ? "nothing" : "\"" + Shorten(answer.Trim()) + "\"") + ".";

        public static string NoReportsMissingLine() =>
            "Crash reports: " + NoReportsVariable + "=1, but TaleWorlds.Engine.Utilities.ExecuteCommandLineCommand(string) was not found; nothing was sent.";

        public static string NoReportsFailedLine(string error) =>
            "Crash reports: the engine console command \"" + NoReportsCommand + "\" failed: " + Shorten(error) + ".";
    }

    // ServerCrashTrace's caps and its guard against its own exceptions, without game types. A
    // noisy retry loop must stay cheap and must stop, so every exception counts towards MaxSeen,
    // noise included, and each distinct stack is logged once, up to MaxPrinted.
    internal sealed class CrashTraceGate
    {
        private readonly object _sync = new object();
        private readonly HashSet<string> _printed = new HashSet<string>();
        private readonly int _maxPrinted;
        private readonly int _maxSeen;
        private int _seen;

        [ThreadStatic] private static bool _inHandler;

        public CrashTraceGate(int maxPrinted, int maxSeen)
        {
            _maxPrinted = maxPrinted;
            _maxSeen = maxSeen;
        }

        public bool Stopped { get; private set; }

        // False when this thread is already inside the handler: an exception the handler itself
        // raises would otherwise loop back into it.
        public bool Enter()
        {
            if (_inHandler)
                return false;
            _inHandler = true;
            return true;
        }

        public void Exit() => _inHandler = false;

        // Counts one exception before anything costly happens. False once the trace has stopped.
        public bool Count(out string stopLine)
        {
            stopLine = null;
            lock (_sync)
            {
                if (Stopped)
                    return false;
                if (++_seen >= _maxSeen)
                {
                    Stopped = true;
                    stopLine = _maxSeen + " first chance exceptions seen; first chance logging stops here.";
                }
                return true;
            }
        }

        // True the first time a stack is seen. The stack that reaches MaxPrinted is still logged,
        // then the trace stops.
        public bool ShouldPrint(string key, out string stopLine)
        {
            stopLine = null;
            lock (_sync)
            {
                if (!_printed.Add(key))
                    return false;
                if (_printed.Count >= _maxPrinted && !Stopped)
                {
                    Stopped = true;
                    stopLine = _maxPrinted + " different first chance exceptions logged; first chance logging stops here.";
                }
                return true;
            }
        }
    }
}
