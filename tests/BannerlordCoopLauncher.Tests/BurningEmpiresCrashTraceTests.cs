using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat: the server crash trace skips the first chance exception Coop throws
// and catches when it turns away the tools' Check server probe, and the errors of Coop's log sink
// retrying a Seq server nobody runs, and nothing else (CrashTraceRules.cs, linked).
public sealed class BurningEmpiresCrashTraceTests
{
    // Frames the pass 2 server console printed for two of the Seq sink's retries (ds-console-pass2.log).
    private static readonly string[] SeqSocketFrames =
    {
        "System.Net.Sockets.Socket+AwaitableSocketAsyncEventArgs.ThrowException",
        "System.Net.Sockets.Socket+AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable+ConfiguredValueTaskAwaiter.GetResult",
        "System.Net.Sockets.Socket+<<ConnectAsync>g__WaitForConnectWithCancellation|277_0>d.MoveNext",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1.ExecutionContextCallback",
        "System.Threading.ExecutionContext.RunInternal",
    };

    private static readonly string[] SeqPoolFrames =
    {
        "System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw",
        "System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess",
        "System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification",
        "System.Net.Http.HttpConnectionPool+<ConnectToTcpHostAsync>d__98.MoveNext",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1.ExecutionContextCallback",
        "System.Threading.ExecutionContext.RunInternal",
    };

    private const string Refused = "No connection could be made because the target machine actively refused it.";

    // The frames the live run 3 server console printed for one probe (host-console-20261026-110449.log).
    private static readonly string[] ProbeFrames =
    {
        "System.ThrowHelper.ThrowArgumentOutOfRangeException",
        "System.Text.UTF8Encoding.GetCharCount",
        "LiteNetLib.Utils.NetDataReader.GetString",
        "Coop.Core.Server.CoopServer.OnConnectionRequest",
        "LiteNetLib.NetManager.ProcessEvent",
        "LiteNetLib.NetManager.PollEvents",
    };

    [Fact]
    public void The_check_server_probe_being_turned_away_is_skipped()
    {
        Assert.True(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", ProbeFrames));
    }

    [Fact]
    public void Another_exception_in_the_connection_request_is_still_printed()
    {
        Assert.False(CrashTraceRules.IsProbeNoise("System.NullReferenceException", ProbeFrames));
        Assert.False(CrashTraceRules.IsProbeNoise("System.ArgumentException", ProbeFrames));
    }

    [Fact]
    public void The_same_exception_anywhere_else_is_still_printed()
    {
        var elsewhere = ProbeFrames.Where(frame => frame != CrashTraceRules.ProbeFrame).Append("Coop.Core.Server.CoopServer.OnPeerConnected").ToArray();

        Assert.False(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", elsewhere));
        Assert.False(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", null!));
    }

    [Fact]
    public void A_real_out_of_range_deeper_under_the_connection_request_is_still_printed()
    {
        // Coop's own code under OnConnectionRequest throws, not LiteNetLib reading the password.
        var deeper = new[]
        {
            "System.ThrowHelper.ThrowArgumentOutOfRangeException",
            "System.Collections.Generic.List`1.get_Item",
            "Coop.Core.Server.Connections.ConnectionLogic.Accept",
            "Coop.Core.Server.CoopServer.OnConnectionRequest",
            "LiteNetLib.NetManager.ProcessEvent",
        };
        // GetString throwing for a caller other than OnConnectionRequest prints too.
        var otherCaller = new[]
        {
            "System.ThrowHelper.ThrowArgumentOutOfRangeException",
            "System.Text.UTF8Encoding.GetCharCount",
            "LiteNetLib.Utils.NetDataReader.GetString",
            "Coop.Core.Server.Messages.ReadName",
            "Coop.Core.Server.CoopServer.OnConnectionRequest",
        };

        Assert.False(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", deeper));
        Assert.False(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", otherCaller));
    }

    [Fact]
    public void Coops_seq_log_sink_retries_are_skipped()
    {
        Assert.True(CrashTraceRules.IsSeqSinkNoise("System.Net.Http.HttpRequestException", Refused + " (localhost:5341)", SeqPoolFrames));
        // The message is in the language of Windows, the address .NET adds after it is not.
        Assert.True(CrashTraceRules.IsSeqSinkNoise("System.Net.Http.HttpRequestException",
            "Es konnte keine Verbindung hergestellt werden, da der Zielcomputer die Verbindung verweigerte. (localhost:5341)", null!));
        Assert.True(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused, SeqSocketFrames));
        Assert.True(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused, SeqPoolFrames));
    }

    [Fact]
    public void Other_network_errors_still_print()
    {
        // Another address, such as a bug report upload.
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Http.HttpRequestException", Refused + " (example.com:443)", SeqPoolFrames));
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Http.HttpRequestException", null!, SeqPoolFrames));
        // A connect that Coop or a mod makes itself has its own frame on the stack.
        var coopConnect = SeqSocketFrames.Append("Coop.Core.Client.Services.Connection.ConnectToServer").ToArray();
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused, coopConnect));
        // A socket error that is not a connect, and the Seq address on another exception type.
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused,
            new[] { "System.Net.Sockets.Socket.ReceiveFrom", "System.Threading.ThreadHelper.ThreadStart" }));
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.IO.IOException", "Unable to read data from the transport connection. (localhost:5341)", SeqPoolFrames));
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused, null!));
        Assert.False(CrashTraceRules.IsSeqSinkNoise("System.Net.Sockets.SocketException", Refused, Array.Empty<string>()));
    }

    [Fact]
    public void The_trace_skips_the_seq_retries_before_it_counts_or_prints_them()
    {
        var handler = SourceFiles.Method(SourceFiles.Read("compat/BurningEmpiresCoopCompat/Server/ServerCrashTrace.cs"),
            "private static void OnFirstChance(");
        var skip = handler.IndexOf("CrashTraceRules.IsSeqSinkNoise(type, e.Exception.Message, frames.Select(FrameName))", StringComparison.Ordinal);
        Assert.True(skip >= 0);
        Assert.True(skip < handler.IndexOf("++_seen", StringComparison.Ordinal));
    }
}
