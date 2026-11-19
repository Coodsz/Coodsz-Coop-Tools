using System.Collections.ObjectModel;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace BannerlordCoopLauncher;

public partial class MainWindow
{
    private void ShowUpdateNotice(ReleaseOffer offer)
    {
        _updateOffer = offer;
        UpdateNotice.Text = offer.Message;
        UpdateNoticeBar.Visibility = Visibility.Visible;
        Log(offer.Message);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateOffer == null)
            return;
        InstallUpdateButton.IsEnabled = false;
        InstallUpdateButton.Opacity = 1;
        InstallUpdateButton.Content = "Installing...";
        ShowUpdateProgress("Installing the update. This window stays open until the download finishes.", null);
        await Task.Yield();
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("This copy cannot update itself.");
            var progress = new Progress<UpdateStep>(step => ShowUpdateProgress(step.Message, step.Fraction));
            await ToolUpdate.InstallAsync(_updateOffer, AppContext.BaseDirectory, exe, Environment.ProcessId, progress, CancellationToken.None);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateNotice.Text = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            InstallUpdateButton.Content = "Install update";
            InstallUpdateButton.ClearValue(OpacityProperty);
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void ShowUpdateProgress(string message, double? fraction)
    {
        UpdateNotice.Text = message;
        UpdateProgress.Visibility = Visibility.Visible;
        if (fraction is double value)
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = value;
        }
        else
        {
            UpdateProgress.IsIndeterminate = true;
        }
    }

    private async Task WatchForUpdateAsync(string repo, string version)
    {
        try
        {
            var offer = await ReleaseNotice.CheckOfferAsync(repo, version);
            if (offer != null)
                Dispatcher.Invoke(() => ShowUpdateNotice(offer));
        }
        catch
        {
        }
    }
}
