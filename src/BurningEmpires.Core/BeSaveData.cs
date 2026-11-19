using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BurningEmpires.Core;

/// <summary>
/// Burning Empires keeps most of a campaign's world (populations, religions, relations) outside
/// the .sav, in its module's BESavedata folder: BESavedata\campaigns\&lt;campaign id&gt;\save_slots\
/// &lt;save name&gt;\..., one slot folder per save name. On the server that is
/// engine\Modules\BurningEmpires\BESavedata. A .sav copied, renamed, backed up or restored without
/// its slot folder loads on empty Burning Empires data, so every save action carries the slot
/// folders of that save name with it. Everything here only touches BESavedata slot folders, so it
/// never reaches a save of Coodsz Host or Coop's own server in the shared Data folder; the .sav
/// side goes through BeSaveVersion.IsBeOwned.
///
/// Burning Empires finds a slot by its folder name, and each snapshot's manifest.json in it names
/// the save too. A snapshot whose name is not the save being loaded is refused, and the save then
/// loads the small copy inside the .sav, so a slot moved or copied to a new save name gets that
/// name in its manifests as well (PointManifestAt). The campaign's index.json is not carried,
/// because Burning Empires lists a slot's snapshot folders itself.
///
/// On the server a slot only counts until the server's first save of that campaign. From then on
/// the save carries the server's own copy of the world data, which the compat module loads instead
/// (WorldDataTransfer).
/// </summary>
public static class BeSaveData
{
    public const string FolderName = BeServerPath.SaveDataFolder;

    public static string Root(string beModuleDirectory) => Path.Combine(beModuleDirectory, FolderName);

    /// <summary>engine\Modules\BurningEmpires in a server folder.</summary>
    public static string ServerModule(string serverRoot) => Path.Combine(BeServerPath.ModulesRoot(serverRoot), BeServerPath.BeModuleId);

    /// <summary>The slot folders of this save name, one per campaign that has it.</summary>
    public static IReadOnlyList<string> SlotFolders(string beModuleDirectory, string saveName)
    {
        var name = SlotName(saveName);
        var campaigns = Path.Combine(Root(beModuleDirectory), "campaigns");
        if (!Directory.Exists(campaigns))
            return Array.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(campaigns)
                .Select(campaign => Path.Combine(campaign, "save_slots", name))
                .Where(Directory.Exists)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static bool Has(string beModuleDirectory, string saveName) => SlotFolders(beModuleDirectory, saveName).Count > 0;

    /// <summary>Copies the save's slot folders into backupFolder\BESavedata, in the same layout.
    /// Returns how many slot folders were copied.</summary>
    public static int Backup(string beModuleDirectory, string saveName, string backupFolder)
    {
        var slots = SlotFolders(beModuleDirectory, saveName);
        foreach (var slot in slots)
            CopyTree(slot, Path.Combine(Root(backupFolder), Relative(beModuleDirectory, slot)));
        return slots.Count;
    }

    /// <summary>
    /// Copies the slot folders a Backup made back over the module's BESavedata. Files in the slot
    /// that the backup does not have stay, nothing is deleted. Refused while the server log says
    /// Burning Empires could not restore a save's data, because the server then runs on empty data.
    /// Returns how many slot folders were restored.
    /// </summary>
    public static int Restore(string backupFolder, string beModuleDirectory, string saveName, string? serverLog = null)
    {
        if (RestoreBlocked(serverLog))
            throw new InvalidOperationException(RestoreBlockedReason);
        var slots = SlotFolders(backupFolder, saveName);
        foreach (var slot in slots)
            CopyTree(slot, Path.Combine(Root(beModuleDirectory), Relative(backupFolder, slot)));
        return slots.Count;
    }

    /// <summary>Copies the save's slot folders to a new save name (Name server copy, save to a new
    /// name). Refused when the new name already has data. Returns how many were copied.</summary>
    public static int CopyTo(string beModuleDirectory, string fromSave, string toSave)
    {
        var from = SlotName(fromSave);
        var to = SlotName(toSave);
        var slots = SlotFolders(beModuleDirectory, fromSave);
        foreach (var slot in slots)
        {
            var target = Path.Combine(Path.GetDirectoryName(slot)!, to);
            if (Directory.Exists(target))
                throw new InvalidOperationException("Burning Empires already has data for a save named " + to + ". Pick another name.");
        }

        foreach (var slot in slots)
        {
            var target = Path.Combine(Path.GetDirectoryName(slot)!, to);
            CopyTree(slot, target);
            PointSnapshotsAt(target, from, to);
        }
        return slots.Count;
    }

    /// <summary>Moves the save's slot folders to the new save name. Refused when the new name
    /// already has data. Returns how many were moved.</summary>
    public static int Rename(string beModuleDirectory, string fromSave, string toSave)
    {
        var from = SlotName(fromSave);
        var to = SlotName(toSave);
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            return 0;
        var slots = SlotFolders(beModuleDirectory, fromSave);
        foreach (var slot in slots)
        {
            if (Directory.Exists(Path.Combine(Path.GetDirectoryName(slot)!, to)))
                throw new InvalidOperationException("Burning Empires already has data for a save named " + to + ". Pick another name.");
        }

        foreach (var slot in slots)
        {
            var target = Path.Combine(Path.GetDirectoryName(slot)!, to);
            Directory.Move(slot, target);
            PointSnapshotsAt(target, from, to);
        }
        return slots.Count;
    }

    /// <summary>
    /// A snapshot manifest (manifest.json) with its SaveName changed from the old save name to the
    /// new one, or null when it is not a JSON object that names the old save. Burning Empires reads
    /// SaveName trimmed and in any case, as it does here. Every other value stays as it is.
    /// </summary>
    public static string? PointManifestAt(string json, string fromSave, string toSave)
    {
        var from = SlotName(fromSave);
        var to = SlotName(toSave);
        try
        {
            if (JsonNode.Parse(json) is not JsonObject manifest
                || manifest["SaveName"] is not JsonValue value || !value.TryGetValue(out string? name)
                || !name.Trim().Equals(from, StringComparison.OrdinalIgnoreCase))
                return null;
            manifest["SaveName"] = to;
            return manifest.ToJsonString(ManifestJson);
        }
        // Not JSON, or an object with a property twice.
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // The slot folder has already moved or been copied, so a manifest that cannot be written is
    // skipped and the save action still completes. That snapshot keeps the old name, and Burning
    // Empires tries the slot's next snapshot or the copy inside the .sav, as before.
    private static void PointSnapshotsAt(string slotFolder, string fromSave, string toSave)
    {
        string[] folders;
        try
        {
            var snapshots = Path.Combine(slotFolder, "snapshots");
            folders = Directory.Exists(snapshots) ? Directory.GetDirectories(snapshots) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var snapshot in folders)
        {
            var manifest = Path.Combine(snapshot, "manifest.json");
            var temp = manifest + ".coodsz-new";
            try
            {
                if (!File.Exists(manifest))
                    continue;
                var text = PointManifestAt(File.ReadAllText(manifest), fromSave, toSave);
                if (text == null)
                    continue;
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                File.Move(temp, manifest, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>Deletes the save's slot folders, after the .sav itself was deleted. Returns how many.</summary>
    public static int Delete(string beModuleDirectory, string saveName)
    {
        var slots = SlotFolders(beModuleDirectory, saveName);
        foreach (var slot in slots)
            Directory.Delete(slot, recursive: true);
        return slots.Count;
    }

    // The compat module's save safety lines (DsSaveSafety) when Burning Empires runs on empty data.
    public static bool RestoreBlocked(string? serverLog) =>
        !string.IsNullOrEmpty(serverLog)
        && (serverLog.Contains("could not restore this save's data", StringComparison.OrdinalIgnoreCase)
            || serverLog.Contains("its data was not restored", StringComparison.OrdinalIgnoreCase));

    public const string RestoreBlockedReason =
        "Not restored. The server log says Burning Empires could not restore this save's data. The server runs on empty data. Stop the server first, then click Restore save backup.";

    /// <summary>Names and sizes of every file in BESavedata, for the log pack. No content.</summary>
    public static string Listing(string beModuleDirectory, int maxLines = 400)
    {
        var root = Root(beModuleDirectory);
        var text = new StringBuilder();
        if (!Directory.Exists(root))
            return "There is no BESavedata folder in " + beModuleDirectory + ".";
        text.AppendLine("BESavedata file list (names and sizes only).");
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (count++ >= maxLines)
                {
                    text.AppendLine("... more files left out.");
                    break;
                }

                var info = new FileInfo(file);
                text.Append(Path.GetRelativePath(root, file)).Append("  ").Append(info.Length).Append(" bytes  ")
                    .AppendLine(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text.AppendLine("The rest of the folder could not be read.");
        }

        if (count == 0)
            text.AppendLine("The folder is empty.");
        return text.ToString();
    }

    // A save name as Burning Empires uses it for the slot folder: no .sav and no path.
    public static string SlotName(string saveName)
    {
        var name = (saveName ?? "").Trim();
        if (name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException("That save name cannot be a folder name.");
        return name;
    }

    private static string Relative(string beModuleDirectory, string slot) =>
        Path.GetRelativePath(Root(beModuleDirectory), slot);

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
