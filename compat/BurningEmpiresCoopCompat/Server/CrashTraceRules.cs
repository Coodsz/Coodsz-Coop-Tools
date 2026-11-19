#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BurningEmpiresCoopCompat
{
    // ServerCrashTrace's plain rules, without game types, so the launcher tests can run them.
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
    }
}
