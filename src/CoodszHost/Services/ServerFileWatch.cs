using System.IO;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Support;

namespace CoodszHost.Services;

/// <summary>
/// The files a Coodsz Host start runs with (both builds), which the antivirus watch remembers after a
/// start that worked. The exe and the engine come with the server folder. The DLLs come from the payloads
/// this tool version ships, DedicatedServerRepair (ServerFolderRepair) and the server modules it copies in,
/// plus Coop.Core.dll (CoopServerBin). Built from the payloads, so a DLL a tool update renamed or dropped is
/// not in the list and is never called antivirus.
/// </summary>
public static class ServerFileWatch
{
    /// <summary>Each server module this tool copies in, with the payload folder it comes from.</summary>
    public static IReadOnlyList<(string Id, string Payload)> ModulePayloads(string? baseDirectory = null) =>
    [
        (SaveRescue.ModuleId, SaveRescue.PayloadRoot(baseDirectory)),
        (CoodszServerTools.ModuleId, CoodszServerTools.PayloadRoot(baseDirectory)),
        (ImprovedGarrisonsCompat.ModuleId, ImprovedGarrisonsCompat.PayloadRoot(baseDirectory)),
        (AD1259Stack.CompatModuleId, AD1259Server.CompatPayload(baseDirectory))
    ];

    public static List<string> Files(string root, string? workshopCoop, string? repairPayload, IEnumerable<(string Id, string Payload)> modulePayloads)
    {
        var modules = ModuleCatalog.ModulesRoot(root);
        var files = new List<string> { Path.Combine(root, AntivirusFiles.ServerExeName) };
        files.AddRange(AntivirusFiles.Engine(root));
        files.Add(Path.Combine(modules, "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"));
        files.AddRange(AntivirusFiles.RepairPayloadDlls(root, repairPayload));
        foreach (var (id, payload) in modulePayloads)
            files.AddRange(AntivirusFiles.RepairPayloadDlls(Path.Combine(modules, id), payload));
        if (workshopCoop != null)
            files.Add(AntivirusFiles.WorkshopServerExe(workshopCoop));
        return files;
    }
}
