using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using BurningEmpires.Ui;
using Microsoft.Win32;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    // Install or repair Burning Empires unpacks the downloads here, on the game's own drive, and
    // removes the folder again.
    private const string UnpackFolder = "Coodsz-BE-unpack";

    // When Install or repair last unpacked into Modules, so its own new files never read as a copy by hand.
    private DateTime _unpackedAtUtc = DateTime.MinValue;

    // Install or repair Burning Empires, the same flow as the client. Start server copies Burning
    // Empires from the game's Modules folder, so it is unpacked there from the host's own Nexus Mods
    // downloads (the main file, then the hotfix on top). Downloads found by themselves are only
    // unpacked after a Yes. Then the bundled Burning Empires Coop Compat goes in too. Nothing is
    // deleted, and the Burning Empires save data is never written.
    private async void InstallOrRepairBe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        if (FindGameRoot() == null)
        {
            SetControl(GameFolderPick.NotFoundText, failed: true);
            return;
        }

        if (Process.GetProcessesByName("Bannerlord").Length > 0)
        {
            SetControl("Close Bannerlord first. Installing while the game is open can leave a module half replaced.", failed: true);
            return;
        }

        var gameRoot = _gameRoot;
        string? contentProblem;
        IReadOnlyList<string> archives;
        string? setProblem;
        bool hotfixOnly;
        bool copying;
        // Files this tool unpacked a moment ago are new too, so they never count as a copy by hand.
        var unpackedJustNow = DateTime.UtcNow - _unpackedAtUtc < TimeSpan.FromMinutes(5);
        _busy = true;
        try
        {
            (contentProblem, archives, setProblem, hotfixOnly, copying) = await Task.Run(() =>
            {
                var found = FindBeArchives();
                var problem = BeArchive.ContentProblem(gameRoot);
                var stillCopying = problem != null && !unpackedJustNow && BeArchive.ChangedRecently(gameRoot, DateTime.UtcNow, TimeSpan.FromMinutes(1));
                return (problem, found, BeArchive.SetProblem(found, gameRoot), !BeArchive.NeedsMainFile(gameRoot), stillCopying);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return;
        }
        finally
        {
            _busy = false;
        }

        // A copy into Modules that is still running looks like a broken Burning Empires, so nothing
        // is asked or unpacked over it.
        if (copying)
        {
            SetControl(BeUiText.CopyStillRunning, failed: true);
            return;
        }

        // Unpacking the 5 GB main file takes minutes, so it only runs after the host says Yes to the
        // files and sizes. A file picked by hand is already a yes. A hotfix with nothing under it asks
        // for the main file, and a main file with no hotfix is refused before the question, since
        // neither can make Burning Empires 2.0.2. With Burning Empires already in Modules (a copy by
        // hand) only the hotfix is asked for. Cancel in the picker stops there: nothing is unpacked
        // and no browser opens.
        var unpack = false;
        string? nexusReason = null;
        var nexusHotfixOnly = hotfixOnly;
        if (contentProblem != null && (archives.Count == 0 || setProblem == BeArchive.MainFileMissing))
        {
            var picked = PickBeDownload(contentProblem, archives, hotfixOnly);
            if (picked == null)
            {
                NoteLog(BeUiText.PickCancelled(hotfixOnly, BeStack.BeVersionText));
            }
            else
            {
                archives = picked;
                unpack = true;
            }
        }
        else if (setProblem != null && contentProblem != null)
        {
            NoteLog(contentProblem);
            NoteLog(setProblem);
            nexusReason = contentProblem + "\n" + setProblem;
            nexusHotfixOnly = hotfixOnly || setProblem == BeArchive.HotfixMissing;
        }
        else if (setProblem == null && archives.Count > 0)
        {
            unpack = AskUnpack(contentProblem, archives);
            if (!unpack)
                NoteLog("Nothing was unpacked.");
        }
        else
        {
            NoteLog("Burning Empires " + BeStack.BeVersionText + " is ready.");
        }

        var finished = false;
        var addOn = CompatAddOn;
        IProgress<string> progress = new Progress<string>(NoteLog);
        await RunAsync(() =>
        {
            if (unpack && !UnpackBe(archives, gameRoot, contentProblem, progress, out var failure))
                nexusReason = failure;
            if (!File.Exists(Path.Combine(addOn, "SubModule.xml")))
                throw new InvalidOperationException(ToolPayload.MissingText("Burning Empires Coop Compat", addOn));
            progress.Report(BeArchive.InstallCompat(addOn, gameRoot));
            var modules = Path.Combine(gameRoot, "Modules");
            var cleared = Directory.Exists(modules) ? DownloadMark.Clear(modules) : 0;
            if (cleared > 0)
                progress.Report("Cleared the download block on " + cleared + " module files.");
            finished = true;
        });
        if (unpack)
            SaveSettings();
        if (finished)
            ShowInstallResult(gameRoot);
        if (nexusReason != null)
            AskOpenNexus(nexusReason, nexusHotfixOnly);
        RefreshState();
    }

    private bool AskUnpack(string? problem, IReadOnlyList<string> archives)
    {
        var question = BeUiText.DownloadQuestion(problem, BeStack.BeVersionText, BeArchive.Describe(archives));
        return Coodsz.Ui.UiLanguage.Message(question, "Install or repair Burning Empires", MessageBoxButton.YesNo, MessageBoxImage.Question, this) == MessageBoxResult.Yes;
    }

    // What Start server still needs, the first line only. The rest go to the log.
    private void ShowInstallResult(string gameRoot)
    {
        var problems = BeServerPath.CoreProblems(CoreSources());
        foreach (var problem in problems)
            NoteLog(problem);
        SetControl(problems.Count == 0
            ? "Burning Empires " + BeStack.BeVersionText + " is ready on this PC. Start server copies it to the server."
            : BeUiText.FirstProblem(problems, BeLine(gameRoot)), failed: problems.Count > 0);
    }

    // When Burning Empires itself is missing or wrong on this PC, the status line points at the
    // Install or repair Burning Empires button in step 3. Null when it is ready.
    private static string? BeLine(string gameRoot)
    {
        try
        {
            return gameRoot.Length == 0 || BeArchive.ContentProblem(gameRoot) == null ? null : BeUiText.BeNotReady(BeStack.BeVersionText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // False when Burning Empires could not be put in from the downloads; failure says why, for the
    // question before the Nexus page opens.
    private bool UnpackBe(IReadOnlyList<string> archives, string gameRoot, string? problem, IProgress<string> progress, out string? failure)
    {
        failure = null;
        // InstallOverlay checks the free space itself, from the unpacked size, before it unpacks anything.
        progress.Report((problem == null ? "Putting Burning Empires in again" : problem + " Installing Burning Empires")
            + " from " + string.Join(" and ", archives.Select(Path.GetFileName)) + ". Your Burning Empires save data is not touched.");
        var scratch = Path.Combine(gameRoot, UnpackFolder);
        try
        {
            BeArchive.InstallOverlay(archives, gameRoot, scratch, progress);
        }
        catch (BeArchiveException ex)
        {
            // A full disk or a missing tar.exe is not fixed by a new download, so it is only logged.
            failure = ex.DownloadHelps ? ex.Message : null;
            progress.Report(ex.Message);
            return false;
        }
        finally
        {
            _unpackedAtUtc = DateTime.UtcNow;
            RemoveEmptyFolder(scratch);
        }

        RememberArchives(archives);
        var left = BeArchive.ContentProblem(gameRoot);
        if (left == null)
        {
            progress.Report("Burning Empires " + BeStack.BeVersionText + " is installed. Your Burning Empires save data was not touched.");
            return true;
        }

        // A missing hotfix is caught before the question (BeArchive.SetProblem), so only what is left is said.
        failure = left;
        progress.Report(left);
        return false;
    }

    private static void RemoveEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The host's own Burning Empires downloads: the newest main file and hotfix in Downloads, on the
    // Desktop or in the folder they came from last time. A file picked by hand is kept even when its
    // name does not start with Burning Empires.
    private IReadOnlyList<string> FindBeArchives()
    {
        var saved = new[] { _settings.BeArchive, _settings.BeHotfixArchive }.Where(File.Exists).ToList();
        var folders = BeArchive.DefaultFolders().ToList();
        foreach (var folder in saved.Select(Path.GetDirectoryName).OfType<string>())
        {
            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                folders.Add(folder);
        }

        var found = BeArchive.FindArchives(folders).ToList();
        foreach (var path in saved)
        {
            if (!found.Any(item => BeArchive.IsHotfix(item) == BeArchive.IsHotfix(path)))
                found.Add(path);
        }

        return found.OrderBy(BeArchive.IsHotfix).ToList();
    }

    private void RememberArchives(IReadOnlyList<string> archives)
    {
        _settings.BeArchive = archives.FirstOrDefault(path => !BeArchive.IsHotfix(path)) ?? _settings.BeArchive;
        _settings.BeHotfixArchive = archives.FirstOrDefault(BeArchive.IsHotfix) ?? _settings.BeHotfixArchive;
    }

    // No download in Downloads, on the Desktop or in the last folder, or only the hotfix: the host
    // points at the main file, and the hotfix next to it (or the one already found) is used too.
    // With Burning Empires already in Modules (hotfixOnly, a copy by hand) the hotfix is asked for
    // instead. Null on Cancel, so nothing is unpacked and a lone hotfix never goes in alone.
    private IReadOnlyList<string>? PickBeDownload(string problem, IReadOnlyList<string> found, bool hotfixOnly)
    {
        NoteLog(BeUiText.PickLine(problem, hotfixOnly, found.Count > 0, BeStack.BeVersionText));
        var dialog = new OpenFileDialog
        {
            Title = BannerlordCoopLauncher.Core.Localization.Translator.T(BeUiText.PickTitle(hotfixOnly)),
            Filter = "Burning Empires download|*.7z;*.rar;*.zip"
        };
        if (dialog.ShowDialog(this) != true)
            return null;
        var picked = dialog.FileName;
        return BeArchive.FindArchives(new[] { Path.GetDirectoryName(picked)! })
            .Concat(found)
            .Where(path => BeArchive.IsHotfix(path) != BeArchive.IsHotfix(picked))
            .Take(1)
            .Prepend(picked)
            .OrderBy(BeArchive.IsHotfix)
            .ToList();
    }

    // The browser only opens after a Yes to a question that says why, so it never opens unasked.
    private void AskOpenNexus(string reason, bool hotfixOnly)
    {
        var question = BeUiText.NexusQuestion(reason, hotfixOnly, BeStack.BeVersionText);
        if (Coodsz.Ui.UiLanguage.Message(question, "Install or repair Burning Empires", MessageBoxButton.YesNo, MessageBoxImage.Question, this) == MessageBoxResult.Yes)
            OpenNexusPage(hotfixOnly);
        else
            NoteLog(BeUiText.NexusNotOpened(BeStack.BeNexusPage, hotfixOnly, BeStack.BeVersionText));
    }

    private void OpenNexusPage(bool hotfixOnly)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BeStack.BeNexusPage) { UseShellExecute = true });
            NoteLog(BeUiText.NexusOpened(hotfixOnly, BeStack.BeVersionText));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            NoteLog("Burning Empires is on Nexus Mods. " + BeStack.BeNexusPage);
        }
    }
}
