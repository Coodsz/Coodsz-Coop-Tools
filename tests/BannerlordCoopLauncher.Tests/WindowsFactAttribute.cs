namespace BannerlordCoopLauncher.Tests;

// A test that checks Windows path rules (drive letters, backslashes, case-
// insensitive folders) or starts Windows programs. It is skipped on Linux and
// macOS and runs everywhere else, including the GitHub Windows runner.
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Uses Windows paths or programs.";
    }
}

// The same for a theory, one run per data row.
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Uses Windows paths or programs.";
    }
}
