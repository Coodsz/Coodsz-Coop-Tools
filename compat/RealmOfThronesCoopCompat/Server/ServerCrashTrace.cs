using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // Crash trail for the dedicated server, adapted from Burning Empires Coop Compat. A boot that
    // ends in exit 0xE0434352 (an unhandled .NET exception) leaves no exception text in the
    // server console, so on the dedicated server only:
    // - every distinct first chance exception (by its call stack) goes to the log with its type,
    //   message and stack, up to MaxPrinted, minus the known harmless noise in CrashTraceRules;
    // - an unhandled exception goes to the log in full, inner exceptions included.
    // Nothing is caught: every exception carries on as before.
    internal static class ServerCrashTrace
    {
        private const int MaxPrinted = 200;
        private const int MaxSeen = 20000;
        private const int HashFrames = 16;

        private static readonly CrashTraceGate Gate = new CrashTraceGate(MaxPrinted, MaxSeen);
        private static bool _installed;

        public static void Install()
        {
            if (_installed)
                return;
            _installed = true;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            SubModule.Log("Crash trace: dedicated server exception logging ready.");
        }

        // An experiment against the report folders (CrashTraceRules.NoReportsCommand): with
        // COODSZ_ROT_NOREPORTS=1 the engine gets one native console command at module load, through
        // TaleWorlds.Engine.Utilities.ExecuteCommandLineCommand found by name, and the log says what
        // was sent and what came back. Off by default; never throws.
        public static void NoWarningReports()
        {
            var variable = Environment.GetEnvironmentVariable(CrashTraceRules.NoReportsVariable);
            if (!CrashTraceRules.NoReportsOn(variable))
                return;
            try
            {
                var utilities = Coop.FindType("TaleWorlds.Engine.Utilities");
                var execute = utilities?.GetMethod("ExecuteCommandLineCommand", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (execute == null)
                {
                    SubModule.Log(CrashTraceRules.NoReportsMissingLine());
                    return;
                }
                SubModule.Log(CrashTraceRules.NoReportsLine(execute.Invoke(null, new object[] { CrashTraceRules.NoReportsCommand }) as string));
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                SubModule.Log(CrashTraceRules.NoReportsFailedLine(inner.GetType().Name + ": " + inner.Message));
            }
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            if (Gate.Stopped || e?.Exception == null || !Gate.Enter())
                return;
            try
            {
                // Counted before the stack is read, so a retry loop of noise stops at MaxSeen too.
                if (!Gate.Count(out var stopLine))
                    return;
                // The exception's own trace is still empty at first chance, so the stack is read
                // here, without this handler's frame.
                var frames = (new StackTrace(1, false).GetFrames() ?? new StackFrame[0]).Select(FrameName).ToList();
                var type = e.Exception.GetType().FullName;
                var noise = (type == CrashTraceRules.ProbeException && CrashTraceRules.IsProbeNoise(type, frames))
                    || ((type == CrashTraceRules.SeqHttpException || type == CrashTraceRules.SeqSocketException)
                        && CrashTraceRules.IsSeqSinkNoise(type, e.Exception.Message, frames))
                    || CrashTraceRules.IsHandledNoise(type, e.Exception.Message, frames);
                if (!noise && Gate.ShouldPrint(StackKey(type, frames), out var printStop))
                {
                    SubModule.Log("First chance " + type + ": " + CrashTraceRules.Shorten(e.Exception.Message) + CrashTraceRules.StackText(frames));
                    stopLine = stopLine ?? printStop;
                }
                if (stopLine != null)
                {
                    AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
                    SubModule.Log("Crash trace: " + stopLine);
                }
            }
            catch { }
            finally
            {
                Gate.Exit();
            }
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            SubModule.Log("Unhandled exception" + (e.IsTerminating ? " (the server stops)" : "") + ": " + e.ExceptionObject);
            try { Console.Out.Flush(); }
            catch { }
        }

        private static string StackKey(string type, List<string> frames)
        {
            var key = new StringBuilder(type);
            for (var i = 0; i < frames.Count && i < HashFrames; i++)
                key.Append('|').Append(frames[i]);
            return key.ToString();
        }

        private static string FrameName(StackFrame frame)
        {
            var method = frame?.GetMethod();
            if (method == null)
                return "?";
            return (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
        }
    }
}
