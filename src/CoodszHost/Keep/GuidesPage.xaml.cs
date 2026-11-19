using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

/// <summary>A guide in the list, in the picked language.</summary>
public sealed record GuideRow(Guide Guide, bool IsPicked)
{
    public string Title => Translator.T(Guide.Title);
    public string Summary => Translator.T(Guide.Summary);
}

public sealed record GuideStepView(int Number, string Text);

public sealed record GuideSectionView(string Heading, IReadOnlyList<GuideStepView> Steps, string Tip)
{
    public bool HasTip => Tip.Length > 0;
}

/// <summary>The picked guide, every line in the picked language.</summary>
public sealed class GuideView
{
    public GuideView(Guide guide)
    {
        Title = Translator.T(guide.Title);
        Summary = Translator.T(guide.Summary);
        Sections = guide.Sections
            .Select(s => new GuideSectionView(Translator.T(s.Heading), s.Steps.Select((step, i) => new GuideStepView(i + 1, Translator.T(step))).ToList(),
                s.Tip.Length > 0 ? Translator.T(s.Tip) : ""))
            .ToList();
    }

    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<GuideSectionView> Sections { get; }
}

/// <summary>
/// The Guides page: Coodsz Host's own help pages (<see cref="GuideLibrary"/>). It needs no server and
/// works the same whether the server runs or not.
/// </summary>
public partial class GuidesPage : UserControl
{
    private IReadOnlyList<Guide> _guides = GuideLibrary.All;
    private Guide? _picked;

    public GuidesPage()
    {
        InitializeComponent();
        _picked = _guides.FirstOrDefault();
        Show();
        Translator.Changed += () => Dispatcher.BeginInvoke(Show);
    }

    /// <summary>The guides to show (the render check passes its own).</summary>
    public void Use(IReadOnlyList<Guide> guides)
    {
        _guides = guides;
        _picked = guides.FirstOrDefault();
        Show();
    }

    /// <summary>Opens one guide by id (from a link elsewhere in the window).</summary>
    public void Open(string id)
    {
        if (_guides.FirstOrDefault(g => g.Id == id) is { } guide)
        {
            _picked = guide;
            SearchBox.Text = "";
            Show();
        }
    }

    private void Show()
    {
        SearchBox.Tag = Translator.T("Search the guides...");
        var shown = GuideLibrary.Search(_guides, SearchBox.Text, Translator.T);
        GuideList.ItemsSource = shown.Select(g => new GuideRow(g, g == _picked)).ToList();
        NothingText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GuideView.DataContext = _picked == null ? null : new GuideView(_picked);
        GuideScroll.ScrollToTop();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var shown = GuideLibrary.Search(_guides, SearchBox.Text, Translator.T);
        // The first match opens, so a search answers straight away.
        if (shown.Count > 0 && (_picked == null || !shown.Contains(_picked)))
            _picked = shown[0];
        Show();
    }

    private void Guide_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GuideRow row || row.Guide == _picked)
            return;
        _picked = row.Guide;
        GuideView.DataContext = new GuideView(row.Guide);
        GuideScroll.ScrollToTop();
    }
}
