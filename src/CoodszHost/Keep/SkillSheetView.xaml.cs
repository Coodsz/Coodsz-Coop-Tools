using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CoodszHost.Keep;

/// <summary>The skill sheet (<see cref="SkillSheet"/>) shown in the player profile and on the Cheats page.</summary>
public partial class SkillSheetView : UserControl
{
    public SkillSheetView() => InitializeComponent();

    private SkillSheet? Sheet => DataContext as SkillSheet;

    private static int Amount(object sender) =>
        int.TryParse((sender as FrameworkElement)?.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private void Skill_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SkillCell skill)
            Sheet?.SelectSkill(skill);
    }

    private void Attribute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AttributeColumn attribute)
            Sheet?.SelectAttribute(attribute);
    }

    private async void SkillLevel_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.ChangeSkill(Amount(sender));
    }

    private async void Focus_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.ChangeFocus(Amount(sender));
    }

    private async void GiveXp_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.GiveXp();
    }

    private async void AttributePoint_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.ChangeAttribute(Amount(sender));
    }

    private async void UnspentAttribute_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.ChangeUnspent(true, Amount(sender));
    }

    private async void UnspentFocus_Click(object sender, RoutedEventArgs e)
    {
        if (Sheet is { } sheet)
            await sheet.ChangeUnspent(false, Amount(sender));
    }
}
