using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpires.Core.Tests;

// Fake game, Workshop and server folders for the Burning Empires server half tests.
internal static class BeServerFixtures
{
    public static string NewRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "be-" + name + "-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        return root;
    }

    public static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public static string Module(string parent, string folder, string id, string version, string subModules = "")
    {
        var dir = Path.Combine(parent, folder);
        Write(Path.Combine(dir, "SubModule.xml"),
            "<Module>\n  <Id value=\"" + id + "\"/>\n  <Name value=\"" + id + "\"/>\n  <Version value=\"" + version + "\"/>\n  <SubModules>\n" + subModules + "  </SubModules>\n</Module>\n");
        return dir;
    }

    public static string ClientSubModule(string dll) =>
        "    <SubModule>\n      <Name value=\"" + dll + "\"/>\n      <DLLName value=\"" + dll + ".dll\"/>\n      <SubModuleClassType value=\"X.SubModule\"/>\n    </SubModule>\n";

    /// <summary>A host PC: game with Burning Empires, Elephants, CustomBattle and MCM, the three BUTR
    /// Workshop items, the bundled compat module and a UIExtenderEx 2.13.3 stand in.</summary>
    public static BeCoreSources HostPc(string root)
    {
        var game = Path.Combine(root, "game");
        var modules = Path.Combine(game, "Modules");
        var be = Module(modules, BeServerPath.BeModuleId, BeServerPath.BeModuleId, BeServerPath.BeVersion, ClientSubModule("BurningEmpires"));
        Write(Path.Combine(be, "bin", "Win64_Shipping_Client", BeServerPath.BeDll), "be dll");
        Write(Path.Combine(be, "bin", "Win64_Shipping_Client", BeServerPath.ProbeDll), "probe dll");
        Write(Path.Combine(be, "ModuleData", "settlements.xml"), "<Settlements/>");
        Module(modules, BeServerPath.ElephantsModuleId, BeServerPath.ElephantsModuleId, BeServerPath.ElephantsVersion);
        Write(BeServerPath.CustomBattleSource(game), "custom battle dll");

        var library = Path.Combine(root, "steam");
        foreach (var butr in BeStack.WorkshopButr)
        {
            var item = Module(Path.Combine(library, "steamapps", "workshop", "content", "261550"), butr.WorkshopId, butr.ModuleId, "v1.0.0");
            Write(Path.Combine(item, "bin", "Win64_Shipping_Client", butr.ModuleId + ".dll"), butr.ModuleId);
        }

        // MCM from Nexus Mods in the game's Modules folder.
        var mcm = Module(modules, BeStack.Mcm.ModuleId, BeStack.Mcm.ModuleId, BeStack.Mcm.TestedVersion);
        Write(Path.Combine(mcm, "bin", "Win64_Shipping_Client", "MCMv5.dll"), "mcm " + BeStack.Mcm.TestedVersion);

        var compat = Module(Path.Combine(root, "tool", "ServerAddOns"), BeServerPath.CompatModuleId, BeServerPath.CompatModuleId, "v1.0.0", ClientSubModule("BurningEmpires.CoopCompat"));
        Write(Path.Combine(compat, "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll"), "compat dll");
        var ui = Write(Path.Combine(root, "tool", "ServerAddOns", "UIExtenderEx-2.13.3", BeServerPath.UIExtenderExDll), "uiextender 2.13.3");

        return new BeCoreSources(game, [library], compat, ui)
        {
            BeDllSha256 = Sha256Files.Hash(Path.Combine(be, "bin", "Win64_Shipping_Client", BeServerPath.BeDll)),
            ProbeDllSha256 = Sha256Files.Hash(Path.Combine(be, "bin", "Win64_Shipping_Client", BeServerPath.ProbeDll)),
            UIExtenderExSha256 = Sha256Files.Hash(ui)
        };
    }

    /// <summary>A Workshop Coop dedicated server folder, hand edited the way this PC's is.</summary>
    public static string WorkshopServer(string root)
    {
        var ds = Path.Combine(root, "workshop", "DedicatedServer");
        foreach (var name in new[] { "BannerlordCoopServer.exe", BeServerPrepare.RuntimeBootstrap, BeServerPrepare.ScriptName, "EULA.md", "LICENSE.txt", "release-info.txt" })
            Write(Path.Combine(ds, name), name + " 1");
        // Hand edits and other tools' files that must stay out.
        Write(Path.Combine(ds, "Coodsz-Start-ModdedServer.ps1"), "copy");
        Write(Path.Combine(ds, "Start-ModdedServer.ps1.coodsz-old"), "old");
        Write(Path.Combine(ds, "server-module-policy.json"), "{\"forceHeadlessModules\":[\"CoopModPatch\"]}");
        Write(Path.Combine(ds, "server-module-policy.json.coopmodpatch.bak"), "bak");
        Write(Path.Combine(ds, "modded-server-modules.txt"), "AD1259");
        Write(Path.Combine(ds, "removed-mods", "Bannerlord.UIExtenderEx-v2.13.3", "SubModule.xml"), "<Module/>");
        Write(Path.Combine(ds, "server-data", "Game Saves", "x.sav"), "save");

        Write(Path.Combine(ds, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"), "starter");
        Write(Path.Combine(ds, "engine", "bin", "Win64_Shipping_Server", BeServerPrepare.TemplateSave), "template");
        Write(Path.Combine(ds, "engine", "dotnet", "dotnet.exe"), "dotnet");
        Write(Path.Combine(ds, "engine", "Parameters", "Version.xml"), "<Version/>");
        foreach (var id in new[] { "Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle", "DedicatedServer.Windows" })
            Module(Path.Combine(ds, "engine", "Modules"), id, id, "v1.4.8");
        var coop = Module(Path.Combine(ds, "engine", "Modules"), "Coop", "Coop", "v0.1.6");
        Write(Path.Combine(coop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop core 1");
        Write(Path.Combine(coop, "mod-config.default.json"), "{\"modOptions\":{\"battleSize\":1000}}");
        foreach (var id in new[] { "AD1259", "DedicatedServer.AD1259CoopCompat", "RBM", "CoopModPatch" })
            Module(Path.Combine(ds, "engine", "Modules"), id, id, "v1.0.0");
        return ds;
    }

    /// <summary>Coodsz Host's DedicatedServerRepair payload the way the tools carry it: the repository's files plus
    /// stand ins for the three prebuilt DLLs a release packs from the private folder (CONTRIBUTING.md, Private files).</summary>
    public static string RepairPayload(string root)
    {
        var source = Path.Combine(BannerlordCoopLauncher.Tests.SourceFiles.Root(), "src", "CoodszHost", ServerFolderRepair.FolderName);
        var payload = Path.Combine(root, "tool", ServerFolderRepair.FolderName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(payload, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        Write(Path.Combine(payload, BeServerPrepare.RuntimeBootstrap), "runtime bootstrap");
        foreach (var (_, dll, _) in ServerFolderRepair.LauncherSubModules)
            Write(Path.Combine(payload, "engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", dll), dll);
        return payload;
    }

    /// <summary>The Workshop Coop's DedicatedServer the way a clean download has it, from Steam's manifest of the
    /// 2026-10-06 build: no Start-ModdedServer.ps1, runtime bootstrap, 0Harmony, StoryMode or CustomBattle, and
    /// none of the files other tools or hand edits leave in a used copy.</summary>
    public static string ThinWorkshopServer(string root)
    {
        var ds = Path.Combine(root, "workshop", "DedicatedServer");
        foreach (var name in new[] { "BannerlordCoopServer.exe", "EULA.md", "LICENSE.txt", "release-info.txt" })
            Write(Path.Combine(ds, name), name + " 1");
        Directory.CreateDirectory(Path.Combine(ds, "server-data"));
        Write(Path.Combine(ds, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"), "starter");
        Write(Path.Combine(ds, "engine", "bin", "Win64_Shipping_Server", BeServerPrepare.TemplateSave), "template");
        Write(Path.Combine(ds, "engine", "dotnet", "dotnet.exe"), "dotnet");
        Write(Path.Combine(ds, "engine", "Parameters", "Version.xml"), "<Version/>");
        foreach (var id in new[] { "Native", "SandBoxCore", "SandBox", "DedicatedServer.Windows" })
            Module(Path.Combine(ds, "engine", "Modules"), id, id, "v1.4.8");
        var coop = Module(Path.Combine(ds, "engine", "Modules"), "Coop", "Coop", "v0.1.6");
        Write(Path.Combine(coop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop core 1");
        Write(Path.Combine(coop, "mod-config.default.json"), "{\"modOptions\":{\"battleSize\":1000}}");
        return ds;
    }

    public static readonly string[] ServerOrder =
    [
        "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
        "Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox",
        "Coop", "BurningEmpires.CoopCompat", "BurningEmpires", "BurningEmpires_Elephants"
    ];

    public static string Zip(string path, params (string Entry, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }

        return path;
    }

    public static string SubModuleXml(string id, string version) =>
        "<Module>\n  <Id value=\"" + id + "\"/>\n  <Version value=\"" + version + "\"/>\n</Module>\n";

    /// <summary>A save file whose header names these modules and Burning Empires version.</summary>
    public static string Save(string folder, string name, string modules, string? beVersion)
    {
        var json = "{\"List\":{\"Modules\":\"" + modules + "\",\"Module_Native\":\"v1.4.8.0\""
            + (beVersion == null ? "" : ",\"Module_BurningEmpires\":\"" + beVersion + "\"") + "}}";
        return Write(Path.Combine(folder, name + ".sav"), "\u0001\u0002\u0000\u0000" + json + "rest of the save");
    }
}
