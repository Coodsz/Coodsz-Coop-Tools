using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// Debug builds only: COODSZ_TEST_WORKER_URL=http://127.0.0.1:port (or http://localhost:port) swaps the base of
/// the report, crash and replies endpoints, so a live test can talk to a local worker. Release builds never
/// read it and return every address unchanged.
/// </summary>
public static class TestWorkerUrl
{
    private static readonly Regex Allowed = new(@"^http://(127\.0\.0\.1|localhost):[0-9]{1,5}$", RegexOptions.CultureInvariant);
    private const string Live = "https://coodsz-mod-reports.coodsz.workers.dev";

    /// <summary>The accepted override base, or null when it is unset or not a local http address.</summary>
    public static string? Base(string? value)
    {
        var text = value?.Trim().TrimEnd('/');
        return text != null && Allowed.IsMatch(text) ? text : null;
    }

    /// <summary>Swaps the live worker's base for the override. Anything else comes back unchanged.</summary>
    public static string? Apply(string? endpoint, string? overrideValue)
    {
        var b = Base(overrideValue);
        return b != null && endpoint != null && endpoint.StartsWith(Live, StringComparison.Ordinal)
            ? b + endpoint.Substring(Live.Length)
            : endpoint;
    }

#if DEBUG
    public static string? Apply(string? endpoint) => Apply(endpoint, Environment.GetEnvironmentVariable("COODSZ_TEST_WORKER_URL"));
#else
    public static string? Apply(string? endpoint) => endpoint;
#endif
}
