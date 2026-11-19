using System.IO;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;

namespace CoodszHost.Services;

/// <summary>Every copy of each mod on this PC by mod id (AD1259Server.PcCopies).</summary>
public sealed record PcModCopies(Dictionary<string, List<(string Folder, string Directory, string Version)>> ById);

/// <summary>
/// The AD1259 edition's server steps: the locked setup, SkyApolo's compatibility module and
/// the version check. Every Coop server start hides the raw Assets folders the headless server
/// cannot load (ServerRawAssets).
/// </summary>
public static class AD1259Server
{
    public const string ProfileName = "AD1259 " + AD1259Stack.AD1259Version + " (locked)";
    public const string CampaignMap = "AD1259";
    public const string AddOnFolder = "ServerAddOns";
    public const string HiddenAssets = ServerRawAssets.HiddenName;

    public static string CompatPayload(string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(AddOnFolder), AD1259Stack.CompatModuleId) : Path.Combine(baseDirectory, AddOnFolder, AD1259Stack.CompatModuleId);

    /// <summary>Copies the shipped compatibility module into the server. Always the shipped copy.</summary>
    public static void InstallCompat(string serverRoot, string? payloadRoot = null)
    {
        var shown = payloadRoot ?? ToolPayload.Expected(AddOnFolder, AD1259Stack.CompatModuleId);
        payloadRoot ??= CompatPayload();
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            throw new InvalidOperationException(ToolPayload.MissingText(AD1259Stack.CompatName, shown));
        var target = Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), AD1259Stack.CompatModuleId);
        var subModule = Path.Combine(payloadRoot, "SubModule.xml");
        // SubModule.xml goes last, like ServerModuleInstall: a copy cut short (a DLL locked by antivirus) then still
        // reads as missing, so the next Start copies it again (NeedsCompat).
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !path.Equals(subModule, StringComparison.OrdinalIgnoreCase))
                     .Append(subModule))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Copies a mod into the server again when the server copy has the wrong version but the
    /// player-side copy (Workshop or the game's Modules folder) has the right one. A Nexus update
    /// is never picked up by the Workshop refresh, so this keeps the server in step.
    /// </summary>
    public static IReadOnlyList<string> RefreshWrongVersions(string serverRoot, IReadOnlyList<ModuleRow> serverRows,
        string gameModulesDirectory, IEnumerable<string> workshopRoots) =>
        RefreshWrongVersions(serverRoot, serverRows, PcCopies(gameModulesDirectory, workshopRoots));

    /// <param name="sources">The copies on this PC (PcCopies), read once per Start.</param>
    /// <param name="mapProblems">Gets a line when the map mod landed without its SceneObj\Main_map\scene.xscene.</param>
    public static IReadOnlyList<string> RefreshWrongVersions(string serverRoot, IReadOnlyList<ModuleRow> serverRows, PcModCopies sources,
        List<MapCopyProblem>? mapProblems = null, Action<string, Action>? runCopy = null)
    {
        var refreshed = new List<string>();
        foreach (var mod in AD1259Stack.ClientMods.Where(mod => !mod.BaseGame && AD1259Stack.ServerModules.Contains(mod.Id, StringComparer.OrdinalIgnoreCase)))
        {
            // The server runs its own Coop (engine\Modules\Coop, from the server package). A player side Coop is the client
            // build, which stops the server with COOP MODULE VERIFICATION FAILED, so Problem names the server package instead.
            if (mod.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase))
                continue;
            var row = serverRows.FirstOrDefault(item => item.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var good = sources.ById.TryGetValue(mod.Id, out var copies) ? GoodCopy(mod, copies) : default;
            // A server copy of an accepted version stays, unless this PC plays another accepted one: Open Source 2.0 on
            // the server while the host plays the 1.x set (or the other way round) would refuse every join.
            if (row != null && AD1259Stack.VersionMatches(mod, row.Version)
                && (good.Directory == null || AD1259Stack.SameVersion(row.Version, good.Version)))
                continue;
            if (good.Directory == null)
                continue;
            // A source the launcher turned off has its SubModule.xml renamed; the server copy gets it back.
            var installed = Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), mod.Id);
            ExtraModScan.RunCopy(runCopy, mod.Name, () =>
            {
                // The Workshop MCM has only bin\Win64_Shipping_Client, and Install keeps a server copy with a server bin over
                // a client only one. The server runs MCM from the client bin too (Start-ModdedServer.ps1 takes its MCMv5.dll).
                // So the old copy moves to removed-mods first, and comes back when the install fails.
                string? aside = null;
                if (mod.Id.Equals("Bannerlord.MBOptionScreen", StringComparison.OrdinalIgnoreCase) && Directory.Exists(installed))
                {
                    var removedRoot = Path.Combine(serverRoot, ModuleRemoval.RemovedFolderName);
                    Directory.CreateDirectory(removedRoot);
                    var stamp = Path.Combine(removedRoot, mod.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    aside = stamp;
                    for (var n = 2; Directory.Exists(aside) || File.Exists(aside); n++)
                        aside = stamp + "-" + n;
                    Directory.Move(installed, aside);
                }
                try
                {
                    ExtraModScan.Install(good.Directory, serverRoot, mod.Id);
                }
                catch when (aside != null && Directory.Exists(aside))
                {
                    // An install that failed part way leaves a half copied folder; it goes before the old copy comes back.
                    if (Directory.Exists(installed))
                        Directory.Delete(installed, recursive: true);
                    Directory.Move(aside, installed);
                    throw;
                }
                ExtraModScan.UnparkSubModule(installed);
            });
            // The source folder is named, so reports show where a copy came from.
            refreshed.Add(mod.Name + " " + good.Version + " from " + good.Directory);
            if (mapProblems != null && mod.Id.Equals(CampaignMap, StringComparison.OrdinalIgnoreCase) && !ServerModuleInstall.HasMapScene(installed))
                mapProblems.Add(new MapCopyProblem(mod.Id, MissingCampaignMap.CopiedWithoutMap(mod.Name, good.Directory)));
        }

        return refreshed;
    }

    /// <summary>
    /// The mods of the set to copy into the server again: the server copy has the right version but lacks files
    /// the good copy on this PC has (ServerModuleInstall.MissingFiles). RefreshWrongVersions only looks at the
    /// version, so a copy cut short stayed broken: Tatache's AD1259 had no bin and no SceneObj\Main_map, and the
    /// server ran plain Calradia, which crashed every join (AD1259 crash reports #13 to #15). Start copies each one
    /// (ExtraModScan.Recopy) after a line, since a copy of AD1259 takes minutes.
    /// </summary>
    public static IReadOnlyList<ShortServerCopy> ShortCopies(string serverRoot, IReadOnlyList<ModuleRow> serverRows,
        string gameModulesDirectory, IEnumerable<string> workshopRoots) =>
        ShortCopies(serverRoot, serverRows, PcCopies(gameModulesDirectory, workshopRoots));

    public static IReadOnlyList<ShortServerCopy> ShortCopies(string serverRoot, IReadOnlyList<ModuleRow> serverRows, PcModCopies sources)
    {
        var copied = new List<ShortServerCopy>();
        foreach (var mod in AD1259Stack.ClientMods.Where(mod => !mod.BaseGame && AD1259Stack.ServerModules.Contains(mod.Id, StringComparer.OrdinalIgnoreCase)))
        {
            if (mod.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase))
                continue;
            var row = serverRows.FirstOrDefault(item => item.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            if (row == null || !AD1259Stack.VersionMatches(mod, row.Version) || !sources.ById.TryGetValue(mod.Id, out var copies))
                continue;
            var good = GoodCopy(mod, copies);
            // Another accepted version than the server's is RefreshWrongVersions' job; its files never line up.
            if (good.Directory != null && AD1259Stack.SameVersion(row.Version, good.Version) && ExtraModScan.FindShortCopy(good.Directory, serverRoot, mod.Id, mod.Name) is { } shortCopy)
                copied.Add(shortCopy);
        }

        return copied;
    }

    /// <summary>
    /// The copy of a mod of the set to take: the pinned version; for a mod pinned to Nexus Mods, the game's Modules
    /// copy before any Workshop copy with the same id; and for the campaign map, a copy with its map scene before one
    /// without (CR-JTQ45: a Workshop copy with Id AD1259 but no SceneObj\Main_map). Default when none has the version.
    /// An Open Source mod or MCM (AD1259ModScan.ModulesCopyWins) takes only a copy in the game's Modules folder when there
    /// is one: Bannerlord 1.4.8's TaleWorlds.ModuleManager.ModuleHelper lists GetPhysicalModules (the Modules folder) before GetPlatformModules (the
    /// Workshop) and picks each loaded id with list.Find on Id, so the Modules copy loads and a Workshop copy with the same
    /// id is not loaded at all (AD1259ModScan.Status judges it the same way). A host with the 1.x set from Nexus in Modules
    /// and 2.0 still subscribed plays 1.x, so the server must get 1.x and never the Workshop's 2.0.
    /// </summary>
    public static (string Folder, string Directory, string Version) GoodCopy(AD1259Mod mod, IReadOnlyList<(string Folder, string Directory, string Version)> copies)
    {
        if (AD1259ModScan.ModulesCopyWins(mod.Id) && copies.Where(copy => !IsWorkshopCopy(copy.Folder)).ToList() is { Count: > 0 } inModules)
            copies = inModules;
        IEnumerable<(string Folder, string Directory, string Version)> matching = copies.Where(copy => AD1259Stack.VersionMatches(mod, copy.Version)).ToList();
        if (mod.WorkshopId == null)
            matching = matching.OrderBy(copy => IsWorkshopCopy(copy.Folder) ? 1 : 0);
        if (mod.Id.Equals(CampaignMap, StringComparison.OrdinalIgnoreCase))
            matching = matching.OrderBy(copy => ServerModuleInstall.HasMapScene(copy.Directory) ? 0 : 1);
        return matching.FirstOrDefault();
    }

    // PcCopies labels: "Workshop 123" for a Workshop item, "Modules\name" for the game's Modules folder.
    private static bool IsWorkshopCopy(string folder) => folder.StartsWith("Workshop ", StringComparison.Ordinal);

    /// <summary>
    /// The ids the Workshop refresh at Start (ExtraModScan.UpdateFromWorkshop) may touch in the AD1259 edition. The Open
    /// Source mods are left to RefreshWrongVersions: the Workshop has only 2.0, so the refresh would put 2.0 over the 1.x
    /// set a host plays from the game's Modules folder. So is MCM: the Workshop's 5.13.1 would replace the Nexus 5.12.3 a
    /// host plays from Modules, and RefreshWrongVersions then put it back.
    /// </summary>
    public static IReadOnlyList<string> WorkshopRefreshIds(IEnumerable<string> ids) =>
        ids.Where(id => !AD1259ModScan.ModulesCopyWins(id)).ToList();

    /// <summary>The ids Start's Workshop refresh and the Mods page Refresh pass on: WorkshopRefreshIds in the AD1259 edition.</summary>
    public static IReadOnlyList<string> RefreshIds(bool ad1259, IEnumerable<string> ids) =>
        ad1259 ? WorkshopRefreshIds(ids) : ids.ToList();

    /// <summary>The Start line about the server's Open Source set (OpenSourceSet.ServerLine), or null without one.</summary>
    /// <param name="withTextures">Adds the missing textures line for 2.0; Start passes true only on its first run.</param>
    public static string? OpenSourceLine(IReadOnlyList<ModuleRow> serverRows, bool withTextures = true) =>
        OpenSourceSet.ServerLine(id => serverRows.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Version, withTextures);

    /// <summary>
    /// Every copy of each mod on this PC, in the game's Modules folder and the Workshop folders. Start reads it
    /// once and hands it to RefreshWrongVersions, ShortCopies and PcVersions; copies into the server never change it.
    /// </summary>
    public static PcModCopies PcCopies(string gameModulesDirectory, IEnumerable<string> workshopRoots)
    {
        var roots = workshopRoots.ToList();
        var sources = AD1259ModScan.Index(gameModulesDirectory, roots);
        // An old MCM download the Coop and Friends launcher turned off on this PC is still a source here.
        foreach (var (id, parked) in AD1259ModScan.ParkedRetired(roots))
        {
            if (!sources.TryGetValue(id, out var list))
                sources[id] = list = [];
            list.AddRange(parked);
        }

        return new PcModCopies(sources);
    }

    /// <summary>
    /// The versions of each mod this PC has, the copies RefreshWrongVersions takes from. The set check names
    /// them, so an old copy on the PC reads as old (Tatache read the pinned versions as his own, AD1259 crash
    /// report #8: his Workshop copies were as old as the server's).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PcVersions(string gameModulesDirectory, IEnumerable<string> workshopRoots) =>
        PcVersions(PcCopies(gameModulesDirectory, workshopRoots));

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PcVersions(PcModCopies sources) =>
        sources.ById.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Select(copy => copy.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Turns every mod of the locked set back on and returns the ids it turned on. An extra map in
    /// SandBox once made the map picker treat Sandbox as the campaign map and turn off every pack mod
    /// that lists Sandbox before AD1259 (RBM, the armouries, the compat module), so a new AD1259 world
    /// started with 10 mods and crashed (Fraus, 2026-10-25). The server only runs with the whole set.
    /// </summary>
    public static IReadOnlyList<string> EnableLockedSet(IReadOnlyList<ModuleRow> serverRows)
    {
        var turnedOn = new List<string>();
        foreach (var row in serverRows.Where(row => !row.Enabled
                     && AD1259Stack.ServerModules.Contains(row.Id, StringComparer.OrdinalIgnoreCase)))
        {
            row.Enabled = true;
            turnedOn.Add(row.Id);
        }

        return turnedOn;
    }

    public static string TurnedOnLine(IReadOnlyList<string> ids) =>
        "Turned these AD1259 mods back on, because the server only runs with the whole set: " + string.Join(", ", ids) + ".";

    /// <summary>
    /// The compatibility module is missing from the server or has another version. Start then adds it itself: a setup
    /// made with New setup is not locked, so the module reload never copied it in (AD1259 crash report #20).
    /// </summary>
    public static bool NeedsCompat(IReadOnlyList<ModuleRow> serverRows) =>
        serverRows.FirstOrDefault(item => item.Id.Equals(AD1259Stack.CompatModuleId, StringComparison.OrdinalIgnoreCase)) is not { } row
        || !row.Version.Equals(AD1259Stack.CompatVersion, StringComparison.OrdinalIgnoreCase);

    public const string NightlyServerAdvice = "On the Mods page, click Use the Steam server, then Start again.";

    // A picked server folder while Use the Steam server can't show: the Workshop server is missing, can't be read, or is
    // itself newer than the Coop AD1259 pins.
    public const string NightlyPickedNoSteamAdvice = "In Steam, subscribe to Bannerlord Coop, or unsubscribe and subscribe again, so it downloads Coop 0.1.7. Then click Use the Steam server on the Mods page and Start again.";

    /// <summary>
    /// True when the server's Coop is the Coop nightly (a newer Coop version or build, or only a CoopNightly module), which
    /// the AD1259 tools never take. Start's refusal and the Use the Steam server line both ask this.
    /// </summary>
    public static bool IsNightlyServer(IReadOnlyList<ModuleRow> serverRows, string? serverBuild)
    {
        var coop = serverRows.FirstOrDefault(row => row.Id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase));
        return coop == null
            ? serverRows.Any(row => row.Id.Equals(BannerlordCoopModules.CoopNightly, StringComparison.OrdinalIgnoreCase))
            : AD1259Stack.IsNightlyCoop(coop.Version, serverBuild);
    }

    /// <summary>
    /// Why Start server refuses before any mod step, or null: the server's Coop is the nightly (IsNightlyServer). The way
    /// back depends on where the server is: Use the Steam server only when that line shows (<paramref name="steamServerButton"/>),
    /// a fresh Workshop download for another picked folder, moving the nightly out of the game's Modules folder, or a fresh
    /// Workshop download for the Workshop server. A newer game on this PC is only the NewerGame warning, since the server
    /// runs the Workshop's own 1.4.8.
    /// </summary>
    public static string? NightlyServer(IReadOnlyList<ModuleRow> serverRows, string? serverBuild, string? serverRoot, bool steamServerButton)
    {
        if (!IsNightlyServer(serverRows, serverBuild))
            return null;
        var advice = steamServerButton ? NightlyServerAdvice
            : CoopInstall.IsChosenServer(serverRoot) ? NightlyPickedNoSteamAdvice
            : ((serverRoot ?? "").Replace('/', '\\') + "\\").Contains(@"\Modules\Bannerlord Coop\", StringComparison.OrdinalIgnoreCase) ? AD1259ModStatus.NightlyInModules
            : AD1259ModStatus.NightlyInWorkshop;
        // Each sentence translated on its own, like Problem below.
        return Translator.T(AD1259Stack.NightlyNeeds) + " " + Translator.T(advice);
    }

    /// <summary>Every mod of the set present on the server with the right version, or the first problem.</summary>
    /// <param name="pcVersions">The versions on this PC (PcVersions). A wrong version then says what the PC has.</param>
    public static string? Problem(IReadOnlyList<ModuleRow> serverRows, IReadOnlyDictionary<string, IReadOnlyList<string>>? pcVersions = null)
    {
        var problems = new List<string>();
        var hasNewer = false;
        var coopServer = false;
        // An Open Source mod's line names its 2.0 pin, so the refusal adds that the 1.x set works too.
        var openSource = false;
        var compatMissing = NeedsCompat(serverRows);
        foreach (var id in AD1259Stack.ServerModules.Where(id => !id.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase)))
        {
            var row = serverRows.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            // The compat module gets its own line below (compatMissing).
            if (id.Equals(AD1259Stack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
                continue;

            var mod = AD1259Stack.Find(id)!;
            var before = problems.Count;
            // The server's own Coop (engine\Modules\Coop) is missing or old: Coop 0.1.7's server package says where to get it.
            var isCoop = id.Equals(BannerlordCoopModules.Coop, StringComparison.OrdinalIgnoreCase);
            if (isCoop && CoopInstall.NotOnSteamYet
                && !AD1259Stack.VersionMatches(mod, row?.Version) && !AD1259Stack.IsNewerThanPinned(mod, row?.Version))
                coopServer = true;
            // A missing server Coop gets the server package line below instead of the Steam Workshop.
            if (row == null && isCoop && coopServer)
                problems.Add(mod.Name + " " + mod.Version + " (missing)");
            else if (row == null)
                problems.Add(mod.Name + " " + mod.Version + " (missing; get it from " + mod.Source + ")");
            else if (!AD1259Stack.VersionMatches(mod, row.Version))
            {
                if (AD1259Stack.IsNewerThanPinned(mod, row.Version))
                {
                    hasNewer = true;
                    problems.Add(mod.Name + " " + mod.Version + " (the server has " + row.Version + ", which is newer than this tool supports)");
                }
                else if (pcVersions == null)
                    problems.Add(mod.Name + " " + mod.Version + " (the server has " + (row.Version.Length == 0 ? "no version" : row.Version) + ")");
                else if (pcVersions.TryGetValue(id, out var onPc) && onPc.Count > 0)
                    problems.Add(mod.Name + " needs " + mod.Version + " (the server has " + (row.Version.Length == 0 ? "no version" : row.Version)
                        + ", this PC has " + string.Join(", ", onPc.Select(version => version.Length == 0 ? "no version" : version)) + ")");
                else
                    problems.Add(mod.Name + " needs " + mod.Version + " (the server has " + (row.Version.Length == 0 ? "no version" : row.Version)
                        + ", not on this PC)");
            }

            openSource |= problems.Count > before && OpenSourceSet.IsOpenSource(id);
        }

        // The compat module ships inside this tool, so the Workshop or Modules advice never fits it.
        var compatLine = compatMissing
            ? string.Format(Translator.T("{0} comes with this tool but could not be added to the server. The server window says why. Start again, and if it still fails, download this tool again."),
                AD1259Stack.CompatName + " " + AD1259Stack.CompatVersion)
            : null;
        if (problems.Count == 0)
            return compatLine;
        var compatAfter = compatLine == null ? "" : " " + compatLine;
        var coopLine = (openSource ? " " + Translator.T(OpenSourceSet.OlderToo) : "")
            + (coopServer && CoopInstall.ServerGetLineNow("Set server folder") is { } serverLine ? " " + Translator.T(serverLine) : "") + compatAfter;
        // Each mod is translated on its own: the window's translator matches a list of several as one text and
        // left every mod line in English (Tatache's French refusal named 9 mods, AD1259 crash report #8).
        if (!hasNewer)
            return string.Format(Translator.T("The AD1259 set is not complete on this PC: {0}. Install those versions where the game finds them (Workshop or the game's Modules folder), then Start again."),
                string.Join("; ", problems.Select(Translator.T))) + coopLine;

        // Translated here, one known sentence at a time: as one English text the translator would match the
        // short list template and leave the advice after it in English.
        return string.Format(Translator.T("The AD1259 set is not complete on this PC: {0}."), string.Join("; ", problems.Select(Translator.T)))
            + " " + Translator.T("Update this tool first.")
            + " " + Translator.T("Install update shows at the top when there is one, or get the newest release.")
            + " " + Translator.T("Until then, install the versions above where the game finds them (Workshop or the game's Modules folder), then Start again.") + coopLine;
    }
}
