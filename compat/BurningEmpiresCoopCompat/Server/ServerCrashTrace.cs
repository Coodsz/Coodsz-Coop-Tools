using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BurningEmpiresCoopCompat
{
    // Crash trail for the dedicated server's console. A new campaign that fails on the server
    // often ends the process with no line at all: the game swallows XML load errors and the
    // server drops what was printed last. On the dedicated server only:
    // - every distinct first chance exception (by its call stack) prints its type, message and
    //   top frames, up to MaxPrinted, except the tools' Check server probe being turned away
    //   (CrashTraceRules.IsProbeNoise) and Coop's log sink retrying a Seq server nobody runs
    //   (CrashTraceRules.IsSeqSinkNoise);
    // - an unhandled exception prints in full;
    // - Campaign.DoLoadingForGameType and CampaignEventDispatcher.OnNewGameCreated print the
    //   exception that leaves them and flush the console before it goes on up;
    // - MBObjectManager.LoadXml prints the error MBObjectManager.LoadXML swallows, with the XML
    //   list and the object it was reading.
    // Nothing is caught: every exception carries on as before.
    internal static class ServerCrashTrace
    {
        private const int MaxPrinted = 200;
        private const int MaxSeen = 20000;
        private const int TopFrames = 6;
        private const int HashFrames = 16;

        private static readonly object Gate = new object();
        private static readonly HashSet<string> Printed = new HashSet<string>();
        private static int _seen;
        private static bool _stopped;

        [ThreadStatic] private static bool _inHandler;
        [ThreadStatic] private static string _readingType;
        [ThreadStatic] private static string _readingId;

        public static void Install(Harmony harmony)
        {
            if (!FreshCampaign.IsDedicatedServerProcess())
                return;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;

            Patch(harmony, "Campaign.DoLoadingForGameType",
                AccessTools.Method(typeof(Campaign), "DoLoadingForGameType",
                    new[] { typeof(GameTypeLoadingStates), typeof(GameTypeLoadingStates).MakeByRefType() }),
                null, nameof(LoadingFinalizer));
            Patch(harmony, "CampaignEventDispatcher.OnNewGameCreated",
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnNewGameCreated", new[] { typeof(CampaignGameStarter) }),
                null, nameof(NewGameCreatedFinalizer));
            Patch(harmony, "MBObjectManager.LoadXml",
                AccessTools.Method(typeof(MBObjectManager), "LoadXml", new[] { typeof(XmlDocument), typeof(bool) }),
                nameof(LoadXmlPrefix), nameof(LoadXmlFinalizer));
            Patch(harmony, "MBObjectManager.GetPresumedObject",
                AccessTools.Method(typeof(MBObjectManager), "GetPresumedObject", new[] { typeof(string), typeof(string), typeof(bool) }),
                nameof(PresumedObjectPrefix), null);
            SubModule.Log("Crash trace: dedicated server exception logging ready.");
        }

        private static void Patch(Harmony harmony, string name, System.Reflection.MethodBase target, string prefix, string finalizer)
        {
            if (target == null)
            {
                SubModule.Log("Crash trace: " + name + " not found; not traced.");
                return;
            }
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(ServerCrashTrace), prefix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(ServerCrashTrace), finalizer));
            }
            catch (Exception ex)
            {
                SubModule.Log("Crash trace: could not trace " + name + ": " + ex.Message);
            }
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            if (_stopped || _inHandler || e?.Exception == null)
                return;
            _inHandler = true;
            try
            {
                // The exception's own trace is still empty at first chance, so the stack is read
                // here, without this handler's frame.
                var frames = new StackTrace(1, false).GetFrames() ?? new StackFrame[0];
                var type = e.Exception.GetType().FullName;
                if (type == CrashTraceRules.ProbeException && CrashTraceRules.IsProbeNoise(type, frames.Select(FrameName)))
                    return;
                if ((type == CrashTraceRules.SeqHttpException || type == CrashTraceRules.SeqSocketException)
                    && CrashTraceRules.IsSeqSinkNoise(type, e.Exception.Message, frames.Select(FrameName)))
                    return;
                var key = StackKey(e.Exception, frames);
                string stopLine = null;
                lock (Gate)
                {
                    if (_stopped)
                        return;
                    if (++_seen >= MaxSeen)
                    {
                        _stopped = true;
                        stopLine = MaxSeen + " first chance exceptions seen; first chance logging stops here.";
                    }
                    if (!Printed.Add(key))
                        key = null;
                    else if (Printed.Count >= MaxPrinted)
                    {
                        _stopped = true;
                        stopLine = MaxPrinted + " different first chance exceptions printed; first chance logging stops here.";
                    }
                }
                if (key != null)
                    SubModule.Log("First chance " + e.Exception.GetType().FullName + ": " + Shorten(e.Exception.Message) + TopOf(frames));
                if (stopLine != null)
                {
                    AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
                    SubModule.Log("Crash trace: " + stopLine);
                }
            }
            catch { }
            finally
            {
                _inHandler = false;
            }
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Report("Unhandled exception" + (e.IsTerminating ? " (the server stops)" : "") + ": " + e.ExceptionObject);
        }

        private static Exception LoadingFinalizer(Exception __exception, GameTypeLoadingStates __0)
        {
            if (__exception != null)
                Report("Campaign.DoLoadingForGameType (" + __0 + ") failed: " + __exception);
            return __exception;
        }

        private static Exception NewGameCreatedFinalizer(Exception __exception)
        {
            if (__exception != null)
                Report("CampaignEventDispatcher.OnNewGameCreated failed: " + __exception);
            return __exception;
        }

        private static void LoadXmlPrefix()
        {
            _readingType = null;
            _readingId = null;
        }

        // LoadXml reads each node through GetPresumedObject with isInitialize true, so the last
        // such call names the node that was being read when it threw.
        private static void PresumedObjectPrefix(string __0, string __1, bool __2)
        {
            if (!__2)
                return;
            _readingType = __0;
            _readingId = __1;
        }

        private static Exception LoadXmlFinalizer(Exception __exception, XmlDocument __0)
        {
            if (__exception != null)
            {
                Report("XML load error (the game skips the rest of this XML): list " + ListName(__0)
                    + ", reading " + (_readingType ?? "?") + " '" + (_readingId ?? "?") + "': " + __exception);
            }
            return __exception;
        }

        private static string ListName(XmlDocument doc)
        {
            try
            {
                foreach (XmlNode node in doc.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Element)
                        return node.Name + " (" + node.ChildNodes.Count + " nodes)";
                }
            }
            catch { }
            return "?";
        }

        private static void Report(string text)
        {
            SubModule.Log(text);
            try { Console.Out.Flush(); }
            catch { }
        }

        private static string StackKey(Exception ex, StackFrame[] frames)
        {
            var key = new StringBuilder(ex.GetType().FullName);
            for (var i = 0; i < frames.Length && i < HashFrames; i++)
                key.Append('|').Append(FrameName(frames[i]));
            return key.ToString();
        }

        private static string TopOf(StackFrame[] frames)
        {
            var text = new StringBuilder();
            for (var i = 0; i < frames.Length && i < TopFrames; i++)
                text.Append(Environment.NewLine).Append("    at ").Append(FrameName(frames[i]));
            return text.ToString();
        }

        private static string FrameName(StackFrame frame)
        {
            var method = frame?.GetMethod();
            if (method == null)
                return "?";
            return (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 300 ? text : text.Substring(0, 300) + "...";
        }
    }
}
