using System.IO.Compression;
using System.Text;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// The v1.3.7 known causes: a TAOM.CoopCompat DLL antivirus removed (TAOM crash report #43, CR-HZYX5) and another
// mod's Common.dll in front of Coop's, seen only in the dump's crash-stack.txt (Coop crash report #62, CR-HNRNZ).
public sealed class ReleaseV137SignatureTests : IDisposable
{
    private static readonly CrashSignatureFile Signatures = CrashSignatureFile.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "crash-signatures.json")))!;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-v137-triage-" + Guid.NewGuid().ToString("N"));

    private const string MissingDllLine = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: required TAOM.CoopCompat feature DLL is missing";
    private const string ForeignCommonStack = "Crash stack summary read from the crash dump on this PC.\n\nException: 0xE0434352 .NET exception\n\n"
        + "Managed exception: System.TypeLoadException: Could not load type 'Common.Network.Session.ServerVisibility' from assembly 'Common, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null'.\n\n"
        + "Faulting thread, managed frames (newest first):\n  TaleWorlds.MountAndBlade.Module.InitializeSubModuleBases()\n";

    public ReleaseV137SignatureTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private string Zip(string name, params (string Entry, string Text)[] files)
    {
        var path = Path.Combine(_directory, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return path;
    }

    [Fact]
    public void A_missing_TAOM_CoopCompat_DLL_says_Start_server_puts_it_back()
    {
        var zip = Zip("taom.zip",
            ("host/server-window.txt", "[TAOM host] Start dotnet.exe\n" + MissingDllLine + "\n[TAOM host] Server exited with code 1.\n"),
            ("host-console/host-console-20261101-004625.log", MissingDllLine + "\n"));
        var match = CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(zip, "server", exitCode: 1, tool: "taom"));

        Assert.Equal("taom-coopcompat-dll-missing", match?.Signature.Id);
        Assert.False(match!.Signature.ReportNeeded);
        Assert.Contains("Update the TAOM Server Tool and press Start server", match.WhatToDo);
        Assert.Contains("click Fix antivirus", match.WhatToDo);
        // TAOM only, and the console window alone is enough too.
        Assert.NotEqual("taom-coopcompat-dll-missing", CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(zip, "server", exitCode: 1, tool: "coop"))?.Signature.Id);
        Assert.Equal("taom-coopcompat-dll-missing", CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(
            Zip("console.zip", ("host/console-window.txt", MissingDllLine)), "server", tool: "taom"))?.Signature.Id);
    }

    // TAOM crash report CR-XXP67 (TAOM #85): the TAOM.CoopCompat.dll of the new TAOM download for 1.5.7 in a 0.3.25
    // server, then a missing SubModule.xml. Another hash is a changed file Start server puts back.
    [Fact]
    public void A_changed_or_new_TAOM_CoopCompat_says_what_it_is()
    {
        const string pin = " does not match the wrapper pin E0B27271032D062DE45A0E29A31C52A278EF9680A80AC11EBD1311FE6D84B21A";
        const string fatal = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: ";
        string? Id(string line, string file = "host/server-window.txt", string tool = "taom") => CrashSignatureMatcher.Match(Signatures,
            CrashEvidence.FromZip(Zip(Guid.NewGuid().ToString("N") + ".zip", (file, line + "\n[TAOM host] Server exited with code 1.\n")), "server", exitCode: 1, tool: tool))?.Signature.Id;

        var newModule = fatal + "TAOM.CoopCompat feature DLL hash FC45CB1294C77AAD1557B1DFB98F0122B7CD4A8D2553514482EB34A6280AE4D8" + pin;
        Assert.Equal("taom-coopcompat-new-module", Id(newModule));
        Assert.Equal("taom-coopcompat-new-module", Id(newModule.Replace("FC45CB12", "fc45cb12"), "host/console-window.txt"));
        Assert.Equal("taom-coopcompat-file-changed", Id(fatal + "TAOM.CoopCompat feature DLL hash 0123456789ABCDEF" + pin));
        Assert.Equal("taom-coopcompat-file-changed", Id(fatal + "required TAOM.CoopCompat module descriptor is missing"));
        Assert.Equal("taom-coopcompat-dll-missing", Id(MissingDllLine));
        Assert.NotEqual("taom-coopcompat-new-module", Id(newModule, tool: "coop"));

        var byId = Signatures.Signatures.ToDictionary(s => s.Id);
        Assert.Contains("new TAOM download for Bannerlord 1.5.7", byId["taom-coopcompat-new-module"].WhatHappened);
        Assert.Equal("These TAOM tools run TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6, so they leave that module alone. Use the TAOM download's own instructions for 1.5.7.",
            byId["taom-coopcompat-new-module"].WhatToDo);
        Assert.Contains("press Start server. It puts back the right file from the TAOM host kit", byId["taom-coopcompat-file-changed"].WhatToDo);
        Assert.Contains("click Fix antivirus", byId["taom-coopcompat-file-changed"].WhatToDo);
        Assert.All(new[] { "taom-coopcompat-new-module", "taom-coopcompat-file-changed" }, id =>
        {
            Assert.False(byId[id].ReportNeeded);
            Assert.DoesNotMatch(@"[a-z]-[a-z]", byId[id].WhatHappened + byId[id].WhatToDo);
        });
    }

    [Fact]
    public void Another_mods_Common_dll_in_the_crash_stack_says_turn_off_the_last_mod()
    {
        var zip = Zip("coop.zip", ("crash-stack.txt", ForeignCommonStack), ("game/Coop_client.log", "[INF] Coop loading\n"));
        var evidence = CrashEvidence.FromZip(zip, "client", modules: ["Coop"], tool: "coop");
        Assert.Equal([ForeignCommonStack], evidence.TextsOf(CrashLogSource.CrashStack));

        var match = CrashSignatureMatcher.Match(Signatures, evidence);
        Assert.Equal("coop-mod-common-dll-clash", match?.Signature.Id);
        Assert.Contains("version 3.0.0.0", match!.WhatHappened);
        Assert.Equal("Another mod brings a file named Common.dll that clashes with Coop. Turn off the mod you added last, then Test to main menu.", match.WhatToDo);

        // Coop's own Common is 1.0.0.0, so a missing type in it is something else.
        var own = Zip("own.zip", ("crash-stack.txt", ForeignCommonStack.Replace("Version=3.0.0.0", "Version=1.0.0.0")));
        Assert.NotEqual("coop-mod-common-dll-clash", CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(own, "client", tool: "coop"))?.Signature.Id);
        // The same line in the engine log keeps the bin folder cause.
        var rgl = Zip("rgl.zip", ("crash-stack.txt", ForeignCommonStack), ("engine-logs/rgl_log_1.txt", ForeignCommonStack));
        Assert.Equal("game-bin-foreign-common-dll", CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(rgl, "client", tool: "coop"))?.Signature.Id);
    }
}
