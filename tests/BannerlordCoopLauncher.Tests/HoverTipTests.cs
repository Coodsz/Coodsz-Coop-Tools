using System.Text.RegularExpressions;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// Every button, switch, tick box, dropdown and menu item in the seven tools says what it does
// when you hover it, in the picked language. Found by a player asking for it.
public sealed class HoverTipTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] Apps = ["BannerlordCoopLauncher", "CoodszHost", "TaomCoopHost", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher"];

    private static readonly string[] Controls = ["Button", "ToggleButton", "CheckBox", "ComboBox", "RadioButton", "MenuItem"];

    // Controls that have no hover tip on purpose. Key is "<file>|<x:Name or Content>".
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> Files(string app, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", app), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                           && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    // A control inside a style or a control template is a part of another control's look.
    private static bool InTemplate(XElement element) =>
        element.Ancestors().Any(parent => parent.Name.LocalName is "ControlTemplate" or "Style" or "Setter");

    private static bool HasTip(XElement element, string code)
    {
        if (!string.IsNullOrWhiteSpace((string?)element.Attribute("ToolTip")))
            return true;
        if (element.Elements().Any(child => child.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal)))
            return true;
        // Some tips change while the tool runs (a locked setup says why), so code sets them.
        var name = (string?)element.Attribute(X + "Name");
        return name != null && Regex.IsMatch(code, @"\b" + Regex.Escape(name) + @"\.ToolTip\s*=");
    }

    [Fact]
    public void Every_button_and_switch_in_the_windows_has_a_hover_tip()
    {
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in Apps)
        {
            var code = string.Join("\n", Files(app, "*.cs").Select(File.ReadAllText));
            foreach (var file in Files(app, "*.xaml"))
            {
                var rel = Relative(file);
                foreach (var element in XDocument.Load(file).Descendants().Where(element => Controls.Contains(element.Name.LocalName) && !InTemplate(element)))
                {
                    if (HasTip(element, code))
                        continue;
                    var id = rel + "|" + ((string?)element.Attribute(X + "Name") ?? (string?)element.Attribute("Content") ?? (string?)element.Attribute("Header") ?? element.Name.LocalName);
                    if (Exempt.ContainsKey(id))
                        used.Add(id);
                    else
                        missing.Add(id + " (line " + ((System.Xml.IXmlLineInfo)element).LineNumber + ")");
                }
            }
        }

        Assert.True(missing.Count == 0, missing.Count + " controls have no hover tip:\n" + string.Join("\n", missing));
        Assert.Empty(Exempt.Keys.Where(key => key.Contains(".xaml|", StringComparison.Ordinal)).Except(used));
    }

    // new Button { ... } in code: the initializer, or a later line on the same variable, sets ToolTip.
    [Fact]
    public void Every_button_and_switch_made_in_code_has_a_hover_tip()
    {
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var create = new Regex(@"new (?:System\.Windows\.Controls\.(?:Primitives\.)?)?(" + string.Join("|", Controls) + @")\b\s*(\(\s*\))?\s*");
        foreach (var app in Apps)
            foreach (var file in Files(app, "*.cs"))
            {
                var source = File.ReadAllText(file);
                var rel = Relative(file);
                foreach (Match match in create.Matches(source))
                {
                    var after = match.Index + match.Length;
                    var hasTip = false;
                    if (after < source.Length && source[after] == '{')
                        hasTip = Initializer(source, after).Contains("ToolTip", StringComparison.Ordinal);
                    if (!hasTip)
                    {
                        // var name = new Button ...; then name.ToolTip = ... in the same file.
                        var before = source[..match.Index];
                        var assigned = Regex.Match(before, @"(\w+)\s*=\s*$");
                        if (assigned.Success)
                            hasTip = Regex.IsMatch(source, @"\b" + Regex.Escape(assigned.Groups[1].Value) + @"\.ToolTip\s*=");
                    }

                    if (hasTip)
                        continue;
                    var line = source[..match.Index].Count(c => c == '\n') + 1;
                    var id = rel + "|" + line;
                    if (Exempt.ContainsKey(id))
                        used.Add(id);
                    else
                        missing.Add(id + " " + match.Value.Trim());
                }
            }

        Assert.True(missing.Count == 0, missing.Count + " controls made in code have no hover tip:\n" + string.Join("\n", missing));
        Assert.Empty(Exempt.Keys.Where(key => key.Contains(".cs|", StringComparison.Ordinal)).Except(used));
    }

    private static string Initializer(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        return source[open..];
    }

    // The written tips: ToolTip="..." in the windows and ToolTip = "..." in code.
    private static IEnumerable<(string Tip, string File)> WrittenTips()
    {
        // A window marked NoTranslate translates its tips itself with Translator.T("...").
        var literal = new Regex(@"ToolTip\s*=\s*(?:Translator\.T\()?""((?:[^""\\]|\\.)*)""\s*\)?\s*[,;}\r\n]");
        foreach (var app in Apps)
        {
            foreach (var file in Files(app, "*.xaml"))
                foreach (var attribute in XDocument.Load(file).Descendants().Attributes().Where(attribute => attribute.Name.LocalName == "ToolTip"))
                    if (!attribute.Value.StartsWith('{') && attribute.Value.Any(char.IsLetter))
                        yield return (attribute.Value, Relative(file));
            foreach (var file in Files(app, "*.cs"))
                foreach (Match match in literal.Matches(File.ReadAllText(file)))
                {
                    var tip = Regex.Unescape(match.Groups[1].Value);
                    if (tip.Any(char.IsLetter))
                        yield return (tip, Relative(file));
                }
        }
    }

    [Fact]
    public void Every_hover_tip_is_translated()
    {
        var tips = WrittenTips().ToList();
        Assert.True(tips.Count > 300, "Only " + tips.Count + " hover tips were found.");
        // The language button's tip names each language in its own words.
        tips.RemoveAll(tip => tip.Tip.StartsWith("Language · ", StringComparison.Ordinal));
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            var untranslated = tips.Where(tip => !table.ContainsKey(tip.Tip)).Select(tip => tip.File + ": " + tip.Tip).Distinct().ToList();
            Assert.True(untranslated.Count == 0, language.Code + " has no translation for " + untranslated.Count + " hover tips:\n" + string.Join("\n", untranslated.Take(20)));
        }
    }

    [Fact]
    public void Every_tool_has_one_hover_tip_look_and_shows_tips_soon_and_on_buttons_that_are_off()
    {
        foreach (var app in Apps)
        {
            var style = XDocument.Load(Path.Combine(RepoRoot(), "src", app, "App.xaml")).Descendants()
                .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ToolTip");
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "Border" && (string?)element.Attribute("MaxWidth") == "320");
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Value") == "Wrap");
        }

        var language = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "UiLanguage.cs"));
        Assert.Contains("InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(400))", language);
        Assert.Contains("ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true))", language);
    }
}
