using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost;

/// <summary>
/// The Coop setting dropdowns on the Settings page hold the raw values the server reads ("True",
/// "VeryEasy"), and show them as translated labels ("On", "Very easy"), in the list and in the
/// closed box. The DifficultyBox style shows every item through this converter; only the boxes
/// marked with <see cref="SetChoiceLabels"/> get labels, so save and player names stay as they are.
/// </summary>
public sealed class SettingChoiceLabel : IMultiValueConverter
{
    public static readonly DependencyProperty ChoiceLabelsProperty = DependencyProperty.RegisterAttached(
        "ChoiceLabels", typeof(bool), typeof(SettingChoiceLabel), new PropertyMetadata(false));

    public static bool GetChoiceLabels(DependencyObject box) => (bool)box.GetValue(ChoiceLabelsProperty);

    public static void SetChoiceLabels(DependencyObject box, bool value) => box.SetValue(ChoiceLabelsProperty, value);

    static SettingChoiceLabel() => Translator.Changed += () => StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(Language)));

    public static event EventHandler<PropertyChangedEventArgs>? StaticPropertyChanged;

    /// <summary>The picked language; the boxes bind to it so a language switch shows the new labels.</summary>
    public static string Language => Translator.Current;

    /// <summary>What a settings box shows for this value: the raw value as a translated label.</summary>
    public static string Show(string? value) => Translator.T(ModSettingText.ChoiceLabel(value));

    // values: the item, the box's Labels mark, the language.
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var item = values.Length > 0 ? values[0] : null;
        if (item == null || item == DependencyProperty.UnsetValue)
            return "";
        return values.Length > 1 && values[1] is true && item is string text ? Show(text) : item.ToString() ?? "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
