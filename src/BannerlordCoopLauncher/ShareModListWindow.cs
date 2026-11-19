using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher;

public sealed class ShareModListWindow : Window
{
    public ShareModListWindow()
    {
        Title = "Share mod list";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        Background = BrushOf(14, 20, 24);
        Foreground = BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(Heading("Share mod list"));
        stack.Children.Add(Body("This saves a file named coodsz-mod-list.json. Send that file to the other person. The Desktop is a fine place to save it. Do not put it in the Bannerlord folders."));
        stack.Children.Add(StepTitle($"What they do in {LauncherBrand.Name}"));
        stack.Children.Add(Body(
            $"1. Open {LauncherBrand.Name}.\n" +
            "2. Click Import mod list. It sits beside Share mod list.\n" +
            "3. Pick the file you sent them.\n" +
            "4. Make sure they press Save mods. The list is not kept until they do."));
        stack.Children.Add(StepTitle($"What they do in {LauncherBrand.ServerTool}"));
        stack.Children.Add(Body(
            $"1. Open {LauncherBrand.ServerTool} and go to Modules.\n" +
            "2. Click Import mod list.\n" +
            "3. Pick the same file.\n" +
            "4. Click Save module config."));
        stack.Children.Add(Note(SharedModList.ExtraModsClient));

        var save = ActionButton("Save the list", "Picks where to save coodsz-mod-list.json with this setup's mods.", 31, 78, 121, 213, 222, 230, 142, 180, 212);
        save.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        var close = ActionButton("Close", "Closes this window without saving.", 27, 40, 51, 213, 222, 230, 58, 76, 92);
        close.Click += (_, _) => Close();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        stack.Children.Add(buttons);
        Content = stack;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Palatino Linotype"),
        FontSize = 26,
        Foreground = BrushOf(213, 222, 230),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private static TextBlock StepTitle(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = BrushOf(226, 194, 122),
        Margin = new Thickness(0, 8, 0, 6)
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 15,
        LineHeight = 24,
        Foreground = BrushOf(213, 222, 230),
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Foreground = BrushOf(142, 160, 174),
        Margin = new Thickness(0, 4, 0, 12)
    };

    private static Button ActionButton(string content, string tip, byte fillR, byte fillG, byte fillB, byte textR, byte textG, byte textB, byte edgeR, byte edgeG, byte edgeB)
    {
        return new Button
        {
            Content = content,
            ToolTip = tip,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(fillR, fillG, fillB),
            Foreground = BrushOf(textR, textG, textB),
            BorderBrush = BrushOf(edgeR, edgeG, edgeB),
            BorderThickness = new Thickness(1)
        };
    }

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
