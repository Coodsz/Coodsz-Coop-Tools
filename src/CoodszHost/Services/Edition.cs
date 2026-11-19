using BannerlordCoopLauncher.Core.Updates;

namespace CoodszHost.Services;

/// <summary>
/// Which program this build is. CoodszHost.csproj builds Coodsz Host; AD1259Host.csproj builds the
/// same source with AD1259 defined as Coodsz AD1259 Server, a locked AD1259 setup with its own releases.
/// </summary>
public static class Edition
{
#if AD1259
    // A property, not a const, so the other edition's branches do not warn as unreachable.
    public static bool IsAD1259 => true;
    public const string Name = "Coodsz AD1259 Server Tool";
    // The sidebar shows COODSZ big with this under it, so together they read as the window title.
    public const string Brand = "AD1259 SERVER TOOL";
    // The picture behind the pages (Assets): the Coop castle until AD1259 has its own art.
    public const string BackdropArt = "redesign-backdrop-castle.jpg";
    public const string Repo = ReleaseNotice.AD1259Repo;
    public const string Version = ReleaseNotice.AD1259Version;
    public const string LogZipPrefix = "coodsz-ad1259-server-logs-";
#else
    // A property, not a const, so the other edition's branches do not warn as unreachable.
    public static bool IsAD1259 => false;
    public const string Name = "Coodsz Coop Server Tool";
    // The sidebar shows COODSZ big with this under it, so together they read as the window title.
    public const string Brand = "COOP SERVER TOOL";
    // The picture behind the pages (Assets).
    public const string BackdropArt = "redesign-backdrop-castle.jpg";
    public const string Repo = ReleaseNotice.FriendsRepo;
    public const string Version = ReleaseNotice.FriendsVersion;
    public const string LogZipPrefix = "coodsz-host-logs-";
#endif

    /// <summary>The Unknown, not tested badge, the badges in Add mods and this PC's own results
    /// (Worked for you, Passed on your server). Coodsz Host only; the AD1259 server tool leaves them out.</summary>
    public static bool ShowsModResults => !IsAD1259;

    /// <summary>The tool a mod report from the Modules page is sent as: coop (the community list) for
    /// Coodsz Host, ad1259 (the AD1259 pack) for the AD1259 server tool, the same as the client launchers.</summary>
    public static string ReportTool => IsAD1259 ? "ad1259" : "coop";
}
