using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

public class TestWorkerUrlTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("http://localhost:8787/")]
    public void Local_http_address_swaps_all_three_endpoints(string value)
    {
        Assert.Equal("http://" + (value.Contains("localhost") ? "localhost" : "127.0.0.1") + ":8787/report", TestWorkerUrl.Apply(ModReports.Endpoint, value));
        Assert.EndsWith("/crash", TestWorkerUrl.Apply(ModReports.CrashEndpoint, value));
        Assert.StartsWith("http://", TestWorkerUrl.Apply(ReportReplies.Endpoint, value));
        Assert.EndsWith(":8787/replies", TestWorkerUrl.Apply(ReportReplies.Endpoint, value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://127.0.0.1:8787")]
    [InlineData("http://example.com:8787")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:8787@evil.com")]
    [InlineData("http://127.0.0.1:8787/x")]
    public void Anything_else_is_ignored(string? value)
    {
        Assert.Equal(ModReports.Endpoint, TestWorkerUrl.Apply(ModReports.Endpoint, value));
        Assert.Equal(ReportReplies.Endpoint, TestWorkerUrl.Apply(ReportReplies.Endpoint, value));
    }
}
