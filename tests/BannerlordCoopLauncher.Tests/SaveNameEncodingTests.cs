using System.IO.Compression;
using System.Text;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

public class NewSaveNameRuleTests
{
    [Theory]
    [InlineData("YÜZÜK TEST", true)]
    [InlineData("YÜZÜKLERİN EFENDİSİ", true)]
    [InlineData("Кальрадия 2", true)]
    [InlineData("YUZUK TEST", true)]
    [InlineData("save_1-a", true)]
    [InlineData(" lead", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a\"b", false)]
    [InlineData("..", false)]
    [InlineData("a.b", false)]
    [InlineData("_lead", false)]
    public void New_save_names_match_the_start_script(string name, bool allowed) =>
        Assert.Equal(allowed, SaveSet.NewSaveNameProblem(name) == null);

    // A server folder whose start script is an older copy (A to Z only), or one no Coodsz tool wrote.
    [Fact]
    public void The_rule_is_read_from_the_server_folders_own_script_with_an_a_to_z_fallback()
    {
        var ours = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        Assert.Equal(SaveSet.NewSaveNamePattern, SaveSet.NewSaveNamePatternIn(ours));
        var old = "    if ($selectedSaveName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$') {";
        Assert.Equal(SaveSet.OldNewSaveNamePattern, SaveSet.NewSaveNamePatternIn(old));
        Assert.Equal(SaveSet.OldNewSaveNamePattern, SaveSet.NewSaveNamePatternIn(null));
        Assert.Equal(SaveSet.OldNewSaveNamePattern, SaveSet.NewSaveNamePatternIn("no rule here"));

        var turkish = "Y\u00dcZ\u00dcK TEST";
        Assert.Null(SaveSet.NewSaveNameProblemFor(turkish, ours));
        Assert.Equal(SaveSet.OldScriptNewSaveNameRule, SaveSet.NewSaveNameProblemFor(turkish, old));
        Assert.Null(SaveSet.NewSaveNameProblemFor("YUZUK TEST", old));
        Assert.Equal(SaveSet.NewSaveNameRule, SaveSet.NewSaveNameProblemFor("a/b", ours));
        Assert.Equal(SaveSet.NewSaveNameRule, SaveSet.NewSaveNameProblemFor("a/b", old));

        var start = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs")), "private void StartServer(");
        Assert.Contains("createNew && SaveSet.NewSaveNameProblemFor(profile.SaveName, ReadServerScript())", start);
    }

    [Fact]
    public void The_rule_is_the_one_in_the_start_script_and_new_save_checks_it_before_start()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        Assert.Contains("$selectedSaveName -cnotmatch '^[\\p{L}\\p{Nd}][\\p{L}\\p{Nd} _-]{0,63}$'", script);
        // PowerShell 5.1 reads a file without a BOM in the ANSI code page, so the script stays plain ASCII.
        Assert.All(script, ch => Assert.True(ch < 128, "Start-ModdedServer.ps1 has a non ASCII character"));
        var saves = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Saves.cs")), "private void NewSave_Click(");
        Assert.Contains("!exists && SaveSet.NewSaveNameProblem(name) is { } rule", saves);
    }
}

// The server writes its console in the OEM code page and the hosts read it as another one, so a
// non ASCII save name arrives bent in the "world saved to" line (TAOM crash report CR-7W2FC).
public sealed class SaveNameEncodingTests : IDisposable
{
    private readonly string _root;
    private readonly string _saves;
    private readonly BackupService _service;

    public SaveNameEncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _root = Path.Combine(Path.GetTempPath(), "coodsz-savename-" + Guid.NewGuid().ToString("N"));
        _saves = Path.Combine(_root, "Game Saves");
        Directory.CreateDirectory(_saves);
        _service = new BackupService(_saves, Path.Combine(_root, "Save Backups"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteWorld(string name, DateTime? writtenUtc = null)
    {
        var sav = Path.Combine(_saves, name + ".sav");
        File.WriteAllText(sav, "world");
        File.WriteAllText(Path.Combine(_saves, name + ".json"), "{\"Players\":[]}");
        if (writtenUtc != null)
            File.SetLastWriteTimeUtc(sav, writtenUtc.Value);
    }

    private static string Bend(string name, int written, int read) =>
        Encoding.GetEncoding(read).GetString(Encoding.GetEncoding(written).GetBytes(name));

    private string? Resolve(string logged, out string? refusal) =>
        SaveSet.ResolveLoggedName(_saves, logged, DateTime.UtcNow, out refusal);

    private void AssertBacksUp(string realName, string loggedName)
    {
        var name = Resolve(loggedName, out var refusal);
        Assert.Null(refusal);
        Assert.Equal(realName, name);
        var entry = _service.Create(name!, BackupReason.Autosave);
        using var zip = ZipFile.OpenRead(entry.FilePath);
        Assert.Contains(zip.Entries, item => item.FullName == realName + ".sav");
        Assert.Contains(zip.Entries, item => item.FullName == realName + ".json");
    }

    // Either refused, or the name comes back as it came and there is no such save to back up.
    private void AssertNoBackup(string loggedName)
    {
        var name = Resolve(loggedName, out var refusal);
        if (name == null)
            Assert.StartsWith("Did not back up " + loggedName + ".", refusal);
        else
            Assert.Equal(loggedName, name);
        Assert.False(File.Exists(Path.Combine(_saves, loggedName + ".sav")));
    }

    [Fact]
    public void The_turkish_save_from_the_crash_report_backs_up()
    {
        WriteWorld("YÜZÜKLERİN EFENDİSİ");

        // The name exactly as host-action.log had it in CR-7W2FC.
        AssertBacksUp("YÜZÜKLERİN EFENDİSİ", "YšZšKLER˜N EFEND˜S˜");
    }

    [Fact]
    public void The_turkish_save_backs_up_when_the_console_code_page_bends_it()
    {
        WriteWorld("YÜZÜKLERİN EFENDİSİ");

        // Turkish OEM 857 read as Turkish ANSI 1254 is the CR-7W2FC case.
        Assert.Equal("YšZšKLER˜N EFEND˜S˜", Bend("YÜZÜKLERİN EFENDİSİ", 857, 1254));
        AssertBacksUp("YÜZÜKLERİN EFENDİSİ", Bend("YÜZÜKLERİN EFENDİSİ", 857, 1254));
    }

    [Fact]
    public void Read_as_the_oem_code_page_the_turkish_name_comes_through_whole()
    {
        var oem = ServerConsoleEncoding.ForCodePage(857)!;
        Assert.Equal("YÜZÜKLERİN EFENDİSİ", oem.GetString(Encoding.GetEncoding(857).GetBytes("YÜZÜKLERİN EFENDİSİ")));
        Assert.Equal(65001, ServerConsoleEncoding.ForCodePage(65001)!.CodePage);
        Assert.Null(ServerConsoleEncoding.ForCodePage(1));
    }

    [Fact]
    public void An_english_oem_code_page_writes_i_with_a_dot_as_i_and_the_save_still_backs_up()
    {
        WriteWorld("YÜZÜKLERİN EFENDİSİ");

        // 437 has Ü but writes İ as its best fit I.
        var logged = Bend("YÜZÜKLERİN EFENDİSİ", 437, 437);
        Assert.Equal("YÜZÜKLERIN EFENDISI", logged);
        AssertBacksUp("YÜZÜKLERİN EFENDİSİ", logged);
    }

    [Theory]
    [InlineData("Café Straße", 437, 1252)]
    [InlineData("Café Straße", 437, 65001)]
    [InlineData("Café Straße", 65001, 1252)]
    [InlineData("Москва 1", 437, 1252)]
    [InlineData("Москва 1", 866, 1252)]
    [InlineData("Москва 1", 65001, 1252)]
    [InlineData("Москва 1", 866, 65001)]
    [InlineData("Ægir øst é ß", 850, 1252)]
    public void Saves_with_other_non_ascii_names_back_up(string realName, int written, int read)
    {
        WriteWorld(realName);
        WriteWorld("save001");

        var logged = Bend(realName, written, read);
        Assert.NotEqual(realName, logged);
        AssertBacksUp(realName, logged);
    }

    [Fact]
    public void A_bent_run_does_not_stand_for_ascii_characters()
    {
        WriteWorld("Kale 2 Çağ");

        AssertNoBackup("Kale ?");
    }

    [Fact]
    public void A_bent_run_does_not_stand_for_more_than_three_times_its_length()
    {
        WriteWorld("Kale ÄÖÜÄ");

        AssertNoBackup("Kale ?");
    }

    [Fact]
    public void A_plain_ascii_name_that_is_not_on_disk_is_not_moved_to_an_accented_save()
    {
        WriteWorld("Şehir Kalesi");

        Assert.Equal("Sehir Kalesi", Resolve("Sehir Kalesi", out var refusal));
        Assert.Null(refusal);
        AssertNoBackup("Sehir Kalesi");
    }

    [Fact]
    public void When_two_saves_fit_nothing_is_backed_up()
    {
        WriteWorld("Kale Ä");
        WriteWorld("Kale Ö");

        Assert.Null(Resolve("Kale ?", out var refusal));
        Assert.Contains("More than one save fits", refusal);
        AssertNoBackup("Kale ?");
    }

    [Fact]
    public void When_the_true_save_is_the_older_of_two_the_newer_one_is_not_backed_up()
    {
        WriteWorld("Kale Ä", DateTime.UtcNow.AddSeconds(-30));
        WriteWorld("Kale Ö", DateTime.UtcNow);

        Assert.Null(Resolve("Kale ?", out _));
        Assert.False(Directory.Exists(_service.BackupRoot));
    }

    [Fact]
    public void A_save_that_fits_but_was_not_written_just_now_is_not_backed_up()
    {
        WriteWorld("YÜZÜKLERİN EFENDİSİ", DateTime.UtcNow.AddMinutes(-10));

        Assert.Null(Resolve("YšZšKLER˜N EFEND˜S˜", out var refusal));
        Assert.Contains("was not written just now", refusal);
        Assert.False(Directory.Exists(_service.BackupRoot));
    }

    [Fact]
    public void A_name_that_is_on_disk_or_fits_nothing_comes_back_as_it_came()
    {
        WriteWorld("YÜZÜKLERİN EFENDİSİ");
        WriteWorld("save001");

        Assert.Equal("save001", Resolve("save001", out _));
        Assert.Equal("YÜZÜKLERİN EFENDİSİ", Resolve("YÜZÜKLERİN EFENDİSİ", out _));
        Assert.Equal("save002", Resolve("save002", out _));
        Assert.Equal("Other ?", Resolve("Other ?", out _));
        Assert.Equal("x?", SaveSet.ResolveLoggedName(Path.Combine(_root, "missing"), "x?", DateTime.UtcNow, out _));
    }

    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void The_taom_and_burning_empires_hosts_read_the_server_in_the_oem_code_page(string project)
    {
        var start = SourceFiles.Read(Path.Combine("src", project, "MainWindow.ServerControl.cs"));
        Assert.Contains("StandardOutputEncoding = ServerConsoleEncoding.Oem(),", start);
        Assert.Contains("StandardErrorEncoding = ServerConsoleEncoding.Oem(),", start);
        Assert.Contains("StandardInputEncoding = ServerConsoleEncoding.Oem(),", start);
    }

    [Fact]
    public void Coodsz_host_and_ad1259_read_the_start_script_in_the_oem_code_page()
    {
        // The engine and PowerShell write the pipe in the OEM code page; UTF-8 turned non ASCII letters into question marks.
        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Contains("var consoleEncoding = ServerConsoleEncoding.Oem() ?? new UTF8Encoding(false);", session);
        Assert.Contains("StandardInputEncoding = ServerConsoleEncoding.Input(),", session);
        Assert.Contains("StandardOutputEncoding = consoleEncoding,", session);
        Assert.Contains("StandardErrorEncoding = consoleEncoding", session);
        // Byte 0x9A is Ü in code page 857, the Turkish OEM page.
        Assert.Equal("Ü", BannerlordCoopLauncher.Core.Launch.ServerConsoleEncoding.ForCodePage(857)!.GetString(new byte[] { 0x9A }));
        Assert.DoesNotContain("StandardOutputEncoding = Encoding.UTF8", session);
    }

    // The live test: say Grüße hällo ñandú reached players as Gr□□e h□llo □and□. The server reads stdin as UTF-8, so
    // the OEM page (437 here) sent one bad byte per letter. This writes through a real powershell -File that hands its
    // stdin to a child with &, like Start-ModdedServer.ps1 does, and reads what the child got.
    [Fact]
    public void A_say_with_non_ascii_letters_reaches_the_server_as_utf8_with_no_bom()
    {
        var text = "say Grüße hällo ñandú";
        Assert.Equal("say Gr��e h�llo �and�", ServerConsoleEncoding.Input().GetString(ServerConsoleEncoding.ForCodePage(437)!.GetBytes(text)));

        var root = Path.Combine(Path.GetTempPath(), "coodsz-stdin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "got.bin");
            var inner = Path.Combine(root, "inner.ps1");
            var outer = Path.Combine(root, "outer.ps1");
            // The paths go in as arguments, so a user name with an apostrophe can't break the scripts.
            File.WriteAllText(inner, "param($out)\r\n$in = [Console]::OpenStandardInput(); $ms = New-Object System.IO.MemoryStream; $in.CopyTo($ms); [System.IO.File]::WriteAllBytes($out, $ms.ToArray())\r\n");
            File.WriteAllText(outer, "param($inner, $out)\r\n& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $inner $out\r\n");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + outer + "\" \"" + inner + "\" \"" + output + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                StandardInputEncoding = ServerConsoleEncoding.Input()
            });
            Assert.NotNull(process);
            process!.StandardInput.WriteLine(text);
            process.StandardInput.WriteLine("say Привет 你好");
            process.StandardInput.Close();
            if (!process.WaitForExit(120000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The stdin check did not finish.");
            }

            var bytes = File.ReadAllBytes(output);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "stdin began with a BOM.");
            var lines = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false)).ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(new[] { text, "say Привет 你好" }, lines);
        }
        finally
        {
            // A folder a stuck child still holds must not hide the real result.
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
