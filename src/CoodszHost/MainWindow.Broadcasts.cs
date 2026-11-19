using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private Window Dialog(string title, string note, TextBox box, Action save)
    {
        var message = new TextBlock
        {
            Text = note,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        box.Background = new SolidColorBrush(Color.FromRgb(16, 22, 27));
        box.Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 68));
        box.CaretBrush = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        box.FontFamily = new FontFamily("Segoe UI");
        box.FontSize = 13;
        box.Padding = new Thickness(8, 6, 8, 6);
        var button = new Button
        {
            Content = "Save",
            ToolTip = "Saves what is in the box and closes this window.",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromRgb(31, 78, 121)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(142, 180, 212)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(button, Dock.Bottom);
        DockPanel.SetDock(message, Dock.Top);
        panel.Children.Add(button);
        panel.Children.Add(message);
        panel.Children.Add(box);
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            Height = 360,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(14, 20, 24)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            Content = panel
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(dialog);
        button.Click += (_, _) =>
        {
            try
            {
                save();
                dialog.Close();
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
            }
        };
        return dialog;
    }
}
