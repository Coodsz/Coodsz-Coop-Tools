using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>Extraction could not run (no tar.exe, an archive it cannot read, an unsafe path). The
/// launcher then only checks Burning Empires. DownloadHelps is false when a new download from Nexus
/// Mods would not fix it (no free space, no tar.exe, a copy into Modules that stopped halfway), so
/// the tools only log it and do not offer the Nexus page.</summary>
public sealed class BeArchiveException(string message, bool downloadHelps = true) : Exception(message)
{
    public bool DownloadHelps { get; } = downloadHelps;
}

/// <summary>A Burning Empires download the launcher found by itself, for the Yes/No question
/// before it unpacks anything.</summary>
public sealed record BeFoundArchive(string Path, long Bytes)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeText => BeArchive.SizeText(Bytes);
}

/// <summary>
/// Install or repair Burning Empires from the archives the player downloaded from Nexus Mods
/// (mod 7750): the main file first, then the hotfix on top. Each archive is extracted into a new
/// empty folder inside one scratch folder, every entry is checked to stay inside it, and the
/// hotfix is laid over the main file there. Only when the result has the Burning Empires 2.0.2
/// DLLs, no other DLL and no SubModule.xml that loads another DLL are the BurningEmpires and
/// BurningEmpires_Elephants module folders copied over the game's Modules folder. Nothing is
/// ever deleted from Modules\BurningEmpires, and its BESavedata folder (the player's Burning
/// Empires save data) is never written. A .zip is read directly; .7z and .rar go through
/// Windows' own tar.exe.
///
/// tar.exe has to unpack the 5.2 GB main .7z and the hotfix .rar. The live test will confirm it
/// does; when it cannot, the player gets ExtractFailed and unpacks by hand.
/// </summary>
public static class BeArchive
{
    public const string NexusUrl = BeServerPath.NexusUrl;

    public const string ExtractFailed =
        "This PC could not unpack the Burning Empires download. Unpack it yourself and copy the BurningEmpires and BurningEmpires_Elephants folders into the game's Modules folder. Install the main file first, then the hotfix. Burning Empires is on Nexus Mods at " + NexusUrl;

    public const string MainThenHotfix = "Install the main file first, then the hotfix, both from Nexus Mods.";

    public static readonly IReadOnlyList<string> Extensions = [".7z", ".rar", ".zip"];

    /// <summary>Free space kept on top of what the unpacked files take.</summary>
    public const long SpaceMargin = 2_000_000_000;

    // The only program files Burning Empires 2.0.2 ships, by module and path inside the module.
    // Elephants is data only. Any other .dll or .exe in the download stops the install.
    private static readonly IReadOnlyDictionary<string, string[]> KnownDlls =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [BeServerPath.BeModuleId] =
            [
                "bin/Win64_Shipping_Client/" + BeServerPath.BeDll,
                "bin/Win64_Shipping_Client/" + BeServerPath.ProbeDll
            ],
            [BeServerPath.ElephantsModuleId] = []
        };

    private static readonly string[] ProgramExtensions = [".dll", ".exe"];

    // ----- What is installed -----

    /// <summary>Null when the game has Burning Empires 2.0.2 and Elephants 1.1.0; otherwise what is wrong.</summary>
    public static string? ContentProblem(string gameRoot)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        var be = Path.Combine(modules, BeServerPath.BeModuleId);
        var version = BeServerPath.VersionOf(be);
        if (version.Length == 0)
            return "Burning Empires is missing.";
        if (!version.Equals(BeServerPath.BeVersion, StringComparison.OrdinalIgnoreCase))
            return "Burning Empires is " + version + ". Coop needs " + BeServerPath.BeVersion + ". " + MainThenHotfix;
        var dll = BeServerPath.HashProblem(Path.Combine(be, "bin", "Win64_Shipping_Client", BeServerPath.BeDll), BeServerPath.BeDllSha256);
        if (dll != null)
            return dll + " " + MainThenHotfix;
        var elephantsVersion = BeServerPath.VersionOf(Path.Combine(modules, BeServerPath.ElephantsModuleId));
        if (elephantsVersion.Length == 0)
            return "Burning Empires Elephants is missing.";
        if (!elephantsVersion.Equals(BeServerPath.ElephantsVersion, StringComparison.OrdinalIgnoreCase))
            return "Burning Empires Elephants is " + elephantsVersion + ". Coop needs " + BeServerPath.ElephantsVersion + ".";
        return null;
    }

    /// <summary>Null when the game has the same Burning Empires Coop Compat as the tool brings.</summary>
    public static string? CompatProblem(string gameRoot, string addOnModule)
    {
        var wanted = BeServerPath.VersionOf(addOnModule);
        var found = BeServerPath.VersionOf(Path.Combine(gameRoot, "Modules", BeServerPath.CompatModuleId));
        if (found.Length == 0)
            return "Burning Empires Coop Compat is missing.";
        if (wanted.Length > 0 && !found.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            return "Burning Empires Coop Compat is " + found + ". This tool brings " + wanted + ".";
        return null;
    }

    /// <summary>
    /// Puts the tool's own Burning Empires Coop Compat (ClientAddOns) in the game's Modules folder.
    /// Only that module's folder is replaced. The copy is staged outside Modules (stagingRoot, or
    /// %TEMP%) and its SubModule.xml goes in last, so a crash halfway never leaves a second
    /// loadable copy or a half module the game would load.
    /// </summary>
    public static string InstallCompat(string addOnModule, string gameRoot, string? stagingRoot = null)
    {
        if (!File.Exists(Path.Combine(addOnModule, "SubModule.xml")))
            throw new InvalidOperationException(ToolPayload.MissingText("Burning Empires Coop Compat", addOnModule));
        var modules = Path.Combine(gameRoot, "Modules");
        var destination = Path.Combine(modules, BeServerPath.CompatModuleId);
        // An older build staged here, where the game could load it as a second copy.
        TryDelete(Path.Combine(modules, "." + BeServerPath.CompatModuleId + ".installing"));

        var staging = Path.Combine(stagingRoot ?? Path.GetTempPath(), "be-compat-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Copy(addOnModule, staging);
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            Copy(staging, destination, top: true);
        }
        finally
        {
            TryDelete(staging);
        }

        return "Installed Burning Empires Coop Compat " + BeServerPath.VersionOf(destination) + ".";
    }

    // ----- Finding the downloads -----

    public static bool IsHotfix(string archivePath) =>
        Path.GetFileName(archivePath).Contains("hotfix", StringComparison.OrdinalIgnoreCase);

    public const string MainFileMissing =
        "The Burning Empires main file was not found, and the hotfix only goes on top of it. Nothing was installed. Put the main file from Nexus Mods next to the hotfix, then try again.";

    public const string HotfixMissing =
        "The hotfix download was not found. The main file alone does not have the Burning Empires version Coop needs. Nothing was installed. Download the hotfix from Nexus Mods and put it next to the main file, then try again.";

    /// <summary>True when the game has no earlier Burning Empires the hotfix can go on top of: no
    /// Burning Empires, no ModuleData in it, or no Elephants.</summary>
    public static bool NeedsMainFile(string gameRoot)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        var be = Path.Combine(modules, BeServerPath.BeModuleId);
        return BeServerPath.VersionOf(be).Length == 0
            || !Directory.Exists(Path.Combine(be, "ModuleData"))
            || BeServerPath.VersionOf(Path.Combine(modules, BeServerPath.ElephantsModuleId)).Length == 0;
    }

    /// <summary>
    /// True when a file or folder in Burning Empires or Elephants in the game's Modules folder was
    /// made or changed within window before nowUtc: a copy into Modules that is still running. A copy
    /// by hand keeps each file's own changed date, so the made date counts too. A date further ahead
    /// than window (a wrong clock in an archive) does not count. Junctions are not followed.
    /// </summary>
    public static bool ChangedRecently(string gameRoot, DateTime nowUtc, TimeSpan window)
    {
        var since = nowUtc - window;
        var until = nowUtc + window;
        bool Recent(FileSystemInfo entry) =>
            (entry.CreationTimeUtc >= since && entry.CreationTimeUtc <= until)
            || (entry.LastWriteTimeUtc >= since && entry.LastWriteTimeUtc <= until);

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var id in new[] { BeServerPath.BeModuleId, BeServerPath.ElephantsModuleId })
        {
            var folder = new DirectoryInfo(Path.Combine(gameRoot, "Modules", id));
            if (!folder.Exists)
                continue;
            if (Recent(folder) || folder.EnumerateFileSystemInfos("*", options).Any(Recent))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Null when these downloads can make Burning Empires 2.0.2 here; otherwise why not. It only
    /// reads names, the game's Modules folder and a zip's entry list, so a set that can never work
    /// is refused before minutes of unpacking: a hotfix with no main file under it, or a main file
    /// with no hotfix that is not 2.0.2 itself.
    /// </summary>
    public static string? SetProblem(IReadOnlyList<string> archives, string gameRoot) =>
        SetProblem(archives, gameRoot, BeServerPath.BeDllSha256);

    internal static string? SetProblem(IReadOnlyList<string> archives, string gameRoot, string beDllSha256)
    {
        var main = archives.FirstOrDefault(path => !IsHotfix(path));
        if (main == null)
            return archives.Count > 0 && NeedsMainFile(gameRoot) ? MainFileMissing : null;
        if (archives.Any(IsHotfix) || MainHasFix(main, beDllSha256))
            return null;
        return HotfixMissing;
    }

    // A main file that already has the 2.0.2 BurningEmpires.dll: its name says the version, or it is
    // a zip whose DLL has the 2.0.2 hash. A 5 GB .7z is never unpacked just to find out.
    private static bool MainHasFix(string main, string beDllSha256)
    {
        var parts = BeServerPath.Short(BeServerPath.BeVersion).Split('.').Select(Regex.Escape);
        if (Regex.IsMatch(Path.GetFileNameWithoutExtension(main), @"(?<!\d)" + string.Join(@"[ ._-]", parts) + @"(?!\d)", RegexOptions.CultureInvariant))
            return true;
        if (!Path.GetExtension(main).Equals(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(main))
            return false;
        try
        {
            using var zip = ZipFile.OpenRead(main);
            var dll = zip.Entries.FirstOrDefault(entry => ("/" + entry.FullName.Replace('\\', '/')).EndsWith(
                "/" + BeServerPath.BeModuleId + "/bin/Win64_Shipping_Client/" + BeServerPath.BeDll, StringComparison.OrdinalIgnoreCase));
            if (dll == null)
                return false;
            using var stream = dll.Open();
            return BeStack.HashMatches(Sha256Files.Hash(stream), beDllSha256);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // An unreadable zip is refused later with its own reason.
            return true;
        }
    }

    /// <summary>The newest Burning Empires main file and hotfix in these folders, main first.
    /// The War Sails variant is left out (Coop does not support it).</summary>
    public static IReadOnlyList<string> FindArchives(IEnumerable<string> folders)
    {
        var found = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                found.AddRange(Directory.EnumerateFiles(folder)
                    .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .Where(path => Squash(Path.GetFileName(path)).StartsWith("burningempires", StringComparison.Ordinal))
                    .Where(path => !Path.GetFileName(path).Contains("naval", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var main = found.Where(path => !IsHotfix(path)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        var hotfix = found.Where(IsHotfix).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return new[] { main, hotfix }.OfType<string>().ToList();
    }

    public static IReadOnlyList<string> DefaultFolders() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
    ];

    /// <summary>Path and size of each archive, main first, for the question the launcher asks
    /// before it unpacks downloads it found by itself.</summary>
    public static IReadOnlyList<BeFoundArchive> Describe(IEnumerable<string> archives) =>
        archives.OrderBy(IsHotfix)
            .Select(path => new BeFoundArchive(path, File.Exists(path) ? new FileInfo(path).Length : 0))
            .ToList();

    /// <summary>A size the way File Explorer shows it (1 GB is 1024 MB), so the player sees the same number there.</summary>
    public static string SizeText(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        const double mb = 1024d * 1024;
        return bytes >= gb
            ? (bytes / gb).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Max(1, Math.Ceiling(bytes / mb)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }

    // ----- Install -----

    /// <summary>
    /// Extracts each archive (main before hotfix, whatever order they come in) into scratchRoot,
    /// lays the hotfix over the main file there, checks the result (CheckUnpacked) and only then
    /// copies its Burning Empires modules over the game's Modules folder. The scratch folder is
    /// removed afterwards. Throws BeArchiveException when an archive cannot be extracted safely,
    /// there is not enough free space, or the result is not Burning Empires 2.0.2.
    /// </summary>
    public static IReadOnlyList<string> InstallOverlay(IReadOnlyList<string> archives, string gameRoot, string scratchRoot, IProgress<string>? progress = null, string? tar = null) =>
        InstallOverlay(archives, gameRoot, scratchRoot, progress, tar, BeServerPath.BeDllSha256, BeServerPath.ProbeDllSha256, FreeBytes);

    // Tests give their own hashes and free space.
    internal static IReadOnlyList<string> InstallOverlay(
        IReadOnlyList<string> archives,
        string gameRoot,
        string scratchRoot,
        IProgress<string>? progress,
        string? tar,
        string beDllSha256,
        string probeSha256,
        Func<string, long> freeBytes)
    {
        if (archives.Count == 0)
            throw new BeArchiveException("No Burning Empires download was found. Download the main file and the hotfix into your Downloads folder, then try again. Burning Empires is on Nexus Mods at " + NexusUrl);
        var setProblem = SetProblem(archives, gameRoot, beDllSha256);
        if (setProblem != null)
            throw new BeArchiveException(setProblem);
        var ordered = archives.OrderBy(IsHotfix).ToList();
        var modules = Path.Combine(gameRoot, "Modules");
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(scratchRoot);

        long unpacked = 0;
        foreach (var archive in ordered)
            unpacked += UnpackedBytes(archive, tar);
        var space = SpaceProblem(unpacked, scratchRoot, modules, freeBytes);
        if (space != null)
            throw new BeArchiveException(space, downloadHelps: false);

        var lines = new List<string>();
        var scratch = Path.Combine(scratchRoot, "be-extract-" + Guid.NewGuid().ToString("N")[..12]);
        var merged = Path.Combine(scratch, "merged");
        Directory.CreateDirectory(merged);
        try
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                var archive = ordered[i];
                var extracted = Path.Combine(scratch, "archive" + i);
                progress?.Report("Unpacking " + Path.GetFileName(archive) + ". A big file can take several minutes.");
                Extract(archive, extracted, tar);
                var found = FindModules(extracted);
                if (found.Count == 0)
                    throw new BeArchiveException(Path.GetFileName(archive) + " has no Burning Empires module folder in it. Nothing was installed. " + MainThenHotfix);
                foreach (var (id, folder) in found)
                {
                    // The main file moves in whole; the hotfix is laid over it, still inside scratch.
                    var target = Path.Combine(merged, id);
                    if (Directory.Exists(target))
                        Overlay(folder, target);
                    else
                        Directory.Move(folder, target);
                }

                TryDelete(extracted);
            }

            var problem = CheckUnpacked(merged, beDllSha256, probeSha256);
            if (problem != null)
                throw new BeArchiveException(problem);

            // All modules go in or none: each file the copy replaces is moved aside first (on the same
            // drive, so no extra space), and a failure halfway puts Modules back the way it was.
            var replaced = Path.Combine(scratchRoot, "be-replaced-" + Guid.NewGuid().ToString("N")[..12]);
            var journal = new OverlayJournal(replaced);
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(merged))
                {
                    var id = Path.GetFileName(folder);
                    var copied = Overlay(folder, Path.Combine(modules, id), journal);
                    lines.Add("Copied " + PlainName(id) + " into the game's Modules folder (" + copied + " files).");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!journal.Undo())
                    throw new BeArchiveException("Copying Burning Empires into the game's Modules folder stopped halfway, and some files it replaced could not be put back. They are kept in " + replaced + ". " + ExtractFailed, downloadHelps: false);
                TryDelete(replaced);
                throw new BeArchiveException("Copying Burning Empires into the game's Modules folder stopped halfway (" + Shorten(ex.Message) + "). The game's Modules folder was put back the way it was. Close the game and anything that uses its files, then try again.", downloadHelps: false);
            }

            TryDelete(replaced);
            foreach (var folder in Directory.EnumerateDirectories(merged))
                DownloadMark.Clear(Path.Combine(modules, Path.GetFileName(folder)));
            foreach (var line in lines)
                progress?.Report(line);
        }
        finally
        {
            TryDelete(scratch);
        }

        return lines;
    }

    /// <summary>
    /// The undo record of a copy into Modules. A file the copy replaces is moved into the backup
    /// folder first, and new files and folders are noted, so Undo can put Modules back.
    /// </summary>
    internal sealed class OverlayJournal(string backupFolder)
    {
        public const string IndexName = "README - where each file goes.txt";

        private readonly List<(string Target, string? Backup)> _files = new();
        private readonly List<string> _createdFolders = new();

        public void CreateFolder(string folder)
        {
            if (Directory.Exists(folder))
                return;
            Directory.CreateDirectory(folder);
            _createdFolders.Add(folder);
        }

        public void Copy(string source, string target)
        {
            string? backup = null;
            if (File.Exists(target))
            {
                Directory.CreateDirectory(backupFolder);
                backup = Path.Combine(backupFolder, _files.Count.ToString(CultureInfo.InvariantCulture));
                File.Move(target, backup);
            }

            _files.Add((target, backup));
            // When Undo cannot put a file back, this list tells the host where each numbered file goes.
            if (backup != null)
                File.AppendAllText(Path.Combine(backupFolder, IndexName), Path.GetFileName(backup) + "\t" + target + Environment.NewLine);
            File.Copy(source, target);
        }

        /// <summary>Puts every replaced file back and removes what the copy added. False when a
        /// file could not be put back; it then stays in the backup folder.</summary>
        public bool Undo()
        {
            var complete = true;
            for (var i = _files.Count - 1; i >= 0; i--)
            {
                var (target, backup) = _files[i];
                try
                {
                    if (File.Exists(target))
                        File.Delete(target);
                    if (backup != null)
                        File.Move(backup, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    complete = false;
                }
            }

            for (var i = _createdFolders.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Directory.Exists(_createdFolders[i]) && !Directory.EnumerateFileSystemEntries(_createdFolders[i]).Any())
                        Directory.Delete(_createdFolders[i]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            return complete;
        }
    }

    /// <summary>
    /// Null when the unpacked modules (main file with the hotfix on top, one folder per module id)
    /// are safe to copy into Modules: BurningEmpires.dll and its DistanceCacheProbe are the 2.0.2
    /// files, no other .dll or .exe is there, and no SubModule.xml loads another DLL. Otherwise
    /// the reason, and nothing may be copied.
    /// </summary>
    public static string? CheckUnpacked(string merged) =>
        CheckUnpacked(merged, BeServerPath.BeDllSha256, BeServerPath.ProbeDllSha256);

    internal static string? CheckUnpacked(string merged, string beDllSha256, string probeSha256)
    {
        if (HasLink(merged))
            return "The Burning Empires download unpacked a link. Nothing was installed.";
        var be = Path.Combine(merged, BeServerPath.BeModuleId);
        if (!Directory.Exists(be))
            return "The download has no Burning Empires module folder. Nothing was installed. " + MainThenHotfix;

        foreach (var folder in Directory.EnumerateDirectories(merged))
        {
            var id = Path.GetFileName(folder);
            if (!KnownDlls.TryGetValue(id, out var known))
                return "The download has a module this tool does not know (" + id + "). Nothing was installed. " + MainThenHotfix;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!ProgramExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (!known.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    return "The download has a program file this tool does not know (" + id + "\\" + relative.Replace('/', '\\') + "). Nothing was installed. Download Burning Empires again. " + MainThenHotfix;
            }

            var named = DllNames(Path.Combine(folder, "SubModule.xml"));
            if (named == null)
                return "The " + PlainName(id) + " SubModule.xml in the download could not be read. Nothing was installed. " + MainThenHotfix;
            var allowed = known.Select(path => path[(path.LastIndexOf('/') + 1)..]).ToList();
            var other = named.FirstOrDefault(name => !allowed.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (other != null)
                return "The " + PlainName(id) + " SubModule.xml in the download loads a file this tool does not know (" + other + "). Nothing was installed. Download Burning Empires again. " + MainThenHotfix;
        }

        var bin = Path.Combine(be, "bin", "Win64_Shipping_Client");
        foreach (var (name, sha256) in new[] { (BeServerPath.BeDll, beDllSha256), (BeServerPath.ProbeDll, probeSha256) })
        {
            var path = Path.Combine(bin, name);
            if (!File.Exists(path))
                return name + " is missing from the download. Nothing was installed. " + MainThenHotfix;
            if (!BeStack.HashMatches(Sha256Files.Hash(path), sha256))
                return name + " in the download is not the Burning Empires " + BeServerPath.Short(BeServerPath.BeVersion) + " file. Nothing was installed. " + MainThenHotfix;
        }

        return null;
    }

    // The DLLName values of a SubModule.xml; empty when it has none, null when it cannot be read.
    private static IReadOnlyList<string>? DllNames(string xmlPath)
    {
        if (!File.Exists(xmlPath))
            return Array.Empty<string>();
        try
        {
            var names = new List<string>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(xmlPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName.Equals("DLLName", StringComparison.OrdinalIgnoreCase))
                {
                    var value = (reader.GetAttribute("value") ?? "").Trim();
                    // A value with a folder in it never matches a known name, so it is refused too.
                    if (value.Length > 0)
                        names.Add(value);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Copies a module folder over the game's copy: every file in the source is written,
    /// nothing is deleted, and nothing goes into BESavedata.</summary>
    public static int Overlay(string source, string destination) => Overlay(source, destination, null);

    // With a journal, every file a copy replaces is moved aside first, so a failure can put it back.
    internal static int Overlay(string source, string destination, OverlayJournal? journal)
    {
        CreateFolder(destination, journal);
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(source))
        {
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)), journal);
            copied++;
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals(BeSaveData.FolderName, StringComparison.OrdinalIgnoreCase))
                continue;
            copied += OverlayInner(directory, Path.Combine(destination, name), journal);
        }

        return copied;
    }

    private static int OverlayInner(string source, string destination, OverlayJournal? journal)
    {
        CreateFolder(destination, journal);
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(source))
        {
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)), journal);
            copied++;
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            copied += OverlayInner(directory, Path.Combine(destination, Path.GetFileName(directory)), journal);
        return copied;
    }

    private static void CreateFolder(string folder, OverlayJournal? journal)
    {
        if (journal == null)
            Directory.CreateDirectory(folder);
        else
            journal.CreateFolder(folder);
    }

    private static void CopyFile(string source, string target, OverlayJournal? journal)
    {
        if (journal == null)
            File.Copy(source, target, overwrite: true);
        else
            journal.Copy(source, target);
    }

    // The module folders in an extracted archive, by their SubModule.xml id. The main file has
    // them at the top, the hotfix inside a BurningEmpires_2.0.2_Hotfix folder.
    public static IReadOnlyList<(string Id, string Folder)> FindModules(string extracted)
    {
        var wanted = new[] { BeServerPath.BeModuleId, BeServerPath.ElephantsModuleId };
        var found = new List<(string, string)>();
        var level = new List<string> { extracted };
        for (var depth = 0; depth < 3 && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var folder in level)
            {
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    var xml = Path.Combine(child, "SubModule.xml");
                    var id = File.Exists(xml) ? ModuleIdentityReader.Read(xml).Id : "";
                    var match = wanted.FirstOrDefault(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (match != null && found.All(item => item.Item1 != match))
                        found.Add((match, child));
                    else if (match == null)
                        next.Add(child);
                }
            }

            level = next;
        }

        return found;
    }

    // ----- Free space -----

    /// <summary>What the archive takes once unpacked: the zip entry sizes, or the sizes tar -tvf lists.</summary>
    public static long UnpackedBytes(string archive, string? tar = null)
    {
        if (!File.Exists(archive))
            throw new BeArchiveException(Path.GetFileName(archive) + " was not found.");
        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var zip = ZipFile.OpenRead(archive);
                return zip.Entries.Sum(entry => entry.Length);
            }
            catch (InvalidDataException)
            {
                throw new BeArchiveException(Path.GetFileName(archive) + " is not a zip this PC can read. " + ExtractFailed);
            }
        }

        return TarListedBytes(RunTar(tar, ["-tvf", archive], archive), Path.GetFileName(archive));
    }

    // One tar -tvf line: mode, links, owner, group, size, then the date and the name. Windows'
    // tar.exe prints the month in the PC's regional format (French, Russian, Arabic script) and
    // ignores LC_ALL, so only the columns before the date are read and the date is never parsed.
    private static readonly Regex TarSize = new(@"^[-a-zA-Z][-rwxsStTlL]{9}\S*\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+\S.*\S", RegexOptions.CultureInvariant);

    /// <summary>The total of the sizes in tar -tvf output. A line it cannot read stops the
    /// install, so an archive never unpacks without a known size.</summary>
    public static long TarListedBytes(string listing, string archiveName)
    {
        long total = 0;
        foreach (var line in listing.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = TarSize.Match(line);
            if (!match.Success || !long.TryParse(match.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                throw new BeArchiveException("This PC could not read the size of " + archiveName + ". Nothing was installed. " + ExtractFailed);
            total = checked(total + size);
        }

        return total;
    }

    /// <summary>Null when both the scratch folder's drive and the game's drive have room for the
    /// unpacked files plus SpaceMargin. One drive for both needs room for two copies.</summary>
    public static string? SpaceProblem(long unpackedBytes, string scratchRoot, string modules, Func<string, long>? freeBytes = null)
    {
        freeBytes ??= FreeBytes;
        var scratchDrive = DriveOf(scratchRoot);
        var gameDrive = DriveOf(modules);
        var needs = new List<(string Drive, long Bytes)>();
        if (scratchDrive.Equals(gameDrive, StringComparison.OrdinalIgnoreCase))
            needs.Add((gameDrive, unpackedBytes * 2 + SpaceMargin));
        else
        {
            needs.Add((scratchDrive, unpackedBytes + SpaceMargin));
            needs.Add((gameDrive, unpackedBytes + SpaceMargin));
        }

        foreach (var (drive, bytes) in needs)
        {
            var free = freeBytes(drive);
            if (free < bytes)
                return "Burning Empires needs about " + SizeText(bytes) + " free on " + drive.TrimEnd('\\') + " to unpack. That drive has " + SizeText(Math.Max(0, free)) + " free. Free up space, then try again.";
        }

        return null;
    }

    private static string DriveOf(string path) => Path.GetPathRoot(Path.GetFullPath(path)) ?? path;

    private static long FreeBytes(string drive)
    {
        try
        {
            return new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // An unknown drive is not refused here; the copy itself still fails cleanly when full.
            return long.MaxValue;
        }
    }

    // ----- Extraction -----

    /// <summary>True for an entry that could land outside the folder it is unpacked into.</summary>
    public static bool UnsafeEntry(string name)
    {
        var normal = name.Replace('\\', '/').Trim();
        if (normal.Length == 0)
            return false;
        if (normal.StartsWith('/') || (normal.Length >= 2 && normal[1] == ':') || normal.StartsWith("//", StringComparison.Ordinal))
            return true;
        return normal.Split('/').Any(part => part == "..");
    }

    public static IReadOnlyList<string> ListEntries(string archive, string? tar = null)
    {
        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var zip = ZipFile.OpenRead(archive);
                return zip.Entries.Select(entry => entry.FullName).ToList();
            }
            catch (InvalidDataException)
            {
                throw new BeArchiveException(Path.GetFileName(archive) + " is not a zip this PC can read. " + ExtractFailed);
            }
        }

        var output = RunTar(tar, ["-tf", archive], archive);
        return output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Unpacks the archive into an empty folder. Every entry is checked first; a link left
    /// in the folder afterwards fails the install too.</summary>
    public static void Extract(string archive, string emptyFolder, string? tar = null)
    {
        if (!File.Exists(archive))
            throw new BeArchiveException(Path.GetFileName(archive) + " was not found.");
        if (Directory.Exists(emptyFolder) && Directory.EnumerateFileSystemEntries(emptyFolder).Any())
            throw new InvalidOperationException("The folder to unpack into has to be empty.");
        Directory.CreateDirectory(emptyFolder);

        var unsafeEntry = ListEntries(archive, tar).FirstOrDefault(UnsafeEntry);
        if (unsafeEntry != null)
            throw new BeArchiveException(Path.GetFileName(archive) + " has a file that would land outside its folder (" + unsafeEntry + "). Nothing was installed.");

        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetFullPath(emptyFolder) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(emptyFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new BeArchiveException(Path.GetFileName(archive) + " has a file that would land outside its folder. Nothing was installed.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        else
        {
            // A link entry could point outside the folder, so it is refused from the listing before
            // anything is written, the way zip entries are checked above.
            if (HasTarLink(RunTar(tar, ["-tvf", archive], archive)))
                throw new BeArchiveException(Path.GetFileName(archive) + " has a link in it, which could point outside its folder. Nothing was installed.");
            RunTar(tar, ["-xf", archive, "-C", emptyFolder], archive);
        }

        if (HasLink(emptyFolder))
            throw new BeArchiveException(Path.GetFileName(archive) + " unpacked a link. Nothing was installed.");
    }

    public static string TarPath() => Path.Combine(Environment.SystemDirectory, "tar.exe");

    private static string RunTar(string? tar, IReadOnlyList<string> arguments, string archive)
    {
        var exe = tar ?? TarPath();
        if (!File.Exists(exe))
            throw new BeArchiveException("Windows' tar.exe was not found. " + ExtractFailed, downloadHelps: false);
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new BeArchiveException("tar.exe did not start. " + ExtractFailed, downloadHelps: false);
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var reason = error.Result.Trim();
            throw new BeArchiveException(Path.GetFileName(archive) + " could not be unpacked" + (reason.Length > 0 ? " (" + Shorten(reason) + ")" : "") + ". " + ExtractFailed);
        }

        return output;
    }

    /// <summary>True when tar -tvf output lists a symbolic link (mode starts with l) or a hard link
    /// (mode starts with h). The mode column comes first and is never translated.</summary>
    public static bool HasTarLink(string listing) =>
        listing.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line => line.Length > 0 && line[0] is 'l' or 'L' or 'h' or 'H');

    private static bool HasLink(string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                return true;
        }

        return false;
    }

    // top: the module folder itself, whose SubModule.xml is written last.
    private static void Copy(string source, string destination, bool top = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
        var files = Directory.EnumerateFiles(source).ToList();
        if (top)
            files = files.OrderBy(file => Path.GetFileName(file).Equals("SubModule.xml", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var file in files)
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static void TryDelete(string scratch)
    {
        try
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch folder in the tool's temp folder does no harm.
        }
    }

    private static string PlainName(string moduleId) => BeHostReport.PlainName(moduleId);

    private static string Squash(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string Shorten(string text)
    {
        var line = text.Replace("\r", "").Split('\n')[0].Trim();
        return line.Length <= 160 ? line : line[..157] + "...";
    }
}
