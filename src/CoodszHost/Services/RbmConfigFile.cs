using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace CoodszHost.Services;

public enum RbmValueKind { Switch, Number, WholeNumber }

/// <summary>
/// One setting in RBM's config.xml, a direct child of a part like &lt;RBMCampaign&gt;.
/// <see cref="RbmDefault"/> is what RBM's parseXmlConfig uses when the element is missing.
/// </summary>
public sealed record RbmOption(string Section, string Name, RbmValueKind Kind, string RbmDefault, double Min = 0, double Max = 0)
{
    public string Key => Section + "/" + Name;
}

/// <summary>
/// The settings the RBM settings card shows. Defaults and number types come from RBM 4.5's
/// RBMConfig.parseXmlConfig: switches count as on only when the text is exactly 1, the multipliers
/// are read with float.Parse and BuildingCostMultiplier with int.Parse (invariant culture). A value
/// RBM cannot parse makes it reset the whole file to its defaults, so the card only writes values
/// in those forms.
/// </summary>
public static class RbmOptions
{
    public static readonly RbmOption Combat = new("RBMCombat", "Enabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption Ai = new("RBMAI", "Enabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption Tournament = new("RBMTournament", "Enabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption Campaign = new("RBMCampaign", "Enabled", RbmValueKind.Switch, "1");

    // RBMCampaign.LordEquipmentUpgrade: lords buy better gear from the town market when they enter a town.
    public static readonly RbmOption LordEquipment = new("RBMCampaign", "LordEquipmentUpgradeEnabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption KingdomCaravans = new("RBMCampaign", "KingdomCaravansEnabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption CaravanInvestment = new("RBMCampaign", "CaravanInvestmentEnabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption DeserterRaiders = new("RBMCampaign", "DeserterRaidersEnabled", RbmValueKind.Switch, "1");
    public static readonly RbmOption RecruitStock = new("RBMCampaign", "RecruitDrawsFromSettlementStock", RbmValueKind.Switch, "1");
    public static readonly RbmOption UpgradeSupplyTown = new("RBMCampaign", "TroopUpgradeRequireSupplyTown", RbmValueKind.Switch, "1");
    public static readonly RbmOption WorkshopProduction = new("RBMCampaign", "WorkshopProductionMultiplier", RbmValueKind.Number, "1", 0, 10);
    public static readonly RbmOption VillageProduction = new("RBMCampaign", "VillageProductionMultiplier", RbmValueKind.Number, "0.5", 0, 10);
    // ConstructionPatches multiplies a building's cost by this whole number; 1 or less keeps the game's cost.
    public static readonly RbmOption BuildingCost = new("RBMCampaign", "BuildingCostMultiplier", RbmValueKind.WholeNumber, "250", 1, 1000);
    // Only used once, when a new campaign's character creation ends.
    public static readonly RbmOption StartingGold = new("RBMCampaign", "CampaignStartingGoldMultiplier", RbmValueKind.Number, "5", 0, 100);

    public static readonly IReadOnlyList<RbmOption> Parts = [Combat, Ai, Tournament, Campaign];

    public static readonly IReadOnlyList<RbmOption> CampaignOptions =
    [
        LordEquipment, KingdomCaravans, CaravanInvestment, DeserterRaiders, RecruitStock, UpgradeSupplyTown,
        WorkshopProduction, VillageProduction, BuildingCost, StartingGold,
    ];

    public static readonly IReadOnlyList<RbmOption> All = [.. Parts, .. CampaignOptions];

    /// <summary>
    /// The Coop friendly button. Only Lords buy equipment changes: on a coop server lords keep buying
    /// gear in towns and empty the markets (the RBM economy report). RBM's code gives no coop reason to
    /// change the others, so they keep the values shown.
    /// </summary>
    public static Dictionary<RbmOption, string> CoopFriendly(IReadOnlyDictionary<RbmOption, string> shown)
    {
        var values = new Dictionary<RbmOption, string>(shown);
        values[LordEquipment] = "0";
        return values;
    }
}

/// <summary>What the card read from config.xml.</summary>
public sealed class RbmConfigRead
{
    public bool Exists { get; init; }
    /// <summary>Why the file could not be used, or null.</summary>
    public string? Problem { get; init; }
    /// <summary>Every card setting: the file's text, or RBM's default when the element is missing.</summary>
    public Dictionary<RbmOption, string> Values { get; init; } = new();
    public HashSet<RbmOption> Missing { get; init; } = new();
    public bool Usable => Exists && Problem == null;
}

/// <summary>
/// Reads and writes the card's settings in RBM's config.xml as text edits: only the edited values
/// change, and everything else in the file (other parts, unknown elements, comments, spacing, line
/// ends, the byte order mark) stays byte for byte. RBM 4.5 rewrites the whole file each time it
/// loads (parseXmlConfig ends with saveXmlConfig) and keeps any value it can parse.
/// </summary>
public static class RbmConfigFile
{
    /// <summary>RBM 4.5's config version. A file with another version is reset to defaults by RBM.</summary>
    public const string ConfigVersion = "2";

    public static string BackupPath(string path) => path + ".bak";

    public static RbmConfigRead Read(string? path = null)
    {
        path ??= RbmSwitches.ConfigPath();
        if (!File.Exists(path))
            return new RbmConfigRead { Exists = false };
        try
        {
            var (text, _) = ReadText(path);
            return FromText(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RbmConfigRead { Exists = true, Problem = "RBM's config.xml could not be opened (" + ex.Message + ")." };
        }
    }

    public static RbmConfigRead FromText(string text)
    {
        Layout layout;
        try
        {
            layout = Locate(text);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return new RbmConfigRead { Exists = true, Problem = "RBM's config.xml could not be read (" + ex.Message + "). RBM puts back its defaults the next time it loads." };
        }

        var values = new Dictionary<RbmOption, string>();
        var missing = new HashSet<RbmOption>();
        foreach (var option in RbmOptions.All)
        {
            if (layout.Elements.TryGetValue(option.Key, out var span) && !span.HasChildren)
                values[option] = span.SelfClosing ? "" : Unescape(text.Substring(span.ContentStart, span.ContentEnd - span.ContentStart)).Trim();
            else
            {
                values[option] = option.RbmDefault;
                missing.Add(option);
            }
        }

        return new RbmConfigRead { Exists = true, Values = values, Missing = missing };
    }

    /// <summary>True when RBM reads this text as on (RBM compares the text with "1").</summary>
    public static bool IsOn(string value) => value.Trim() == "1";

    public static string SwitchValue(bool on) => on ? "1" : "0";

    /// <summary>True when RBM reads both texts as the same value (1 and 1.0 are the same number).</summary>
    public static bool SameValue(RbmOption option, string left, string right)
    {
        if (option.Kind == RbmValueKind.Switch)
            return IsOn(left) == IsOn(right);
        if (double.TryParse(left.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(right.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return a == b;
        return left.Trim() == right.Trim();
    }

    /// <summary>
    /// The value to write for a box's text, in the form RBM parses, or an error for the card's note.
    /// </summary>
    public static bool TryNormalize(RbmOption option, string? input, string label, out string value, out string error)
    {
        value = "";
        error = "";
        var text = (input ?? "").Trim().Replace(',', '.');
        switch (option.Kind)
        {
            case RbmValueKind.Switch:
                value = IsOn(text) ? "1" : "0";
                return true;
            case RbmValueKind.WholeNumber:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) && whole >= option.Min && whole <= option.Max)
                {
                    value = whole.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                error = string.Format(CultureInfo.InvariantCulture, "{0} must be a whole number from {1} to {2}.", label, option.Min, option.Max);
                return false;
            default:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    && number >= option.Min && number <= option.Max)
                {
                    value = ((float)number).ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                error = string.Format(CultureInfo.InvariantCulture, "{0} must be a number from {1} to {2}.", label, option.Min, option.Max);
                return false;
        }
    }

    /// <summary>
    /// Writes the given values. Values the file already has are left alone; missing elements are
    /// added at the end of their part (and a missing part at the end of &lt;Config&gt;). The first
    /// write keeps the old file as config.xml.bak. Returns the keys that changed.
    /// </summary>
    public static IReadOnlyList<string> Save(string path, IReadOnlyDictionary<RbmOption, string> values)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("RBM's config.xml does not exist yet.", path);
        var (text, encoding) = ReadText(path);
        var edited = Edit(text, values, out var changed);
        if (changed.Count == 0)
            return changed;
        // The edited text must still read back with every value in place before it replaces the file.
        var check = FromText(edited);
        if (!check.Usable || values.Any(pair => check.Values[pair.Key] != pair.Value))
            throw new InvalidDataException("RBM's config.xml was left alone because the edited file did not read back the same.");

        var backup = BackupPath(path);
        if (!File.Exists(backup))
            File.Copy(path, backup);
        WriteAtomic(path, encoding, edited);
        return changed;
    }

    /// <summary>A new config.xml with only these values, in the form RBM completes with its defaults on its next load.</summary>
    public static string NewFile(IReadOnlyDictionary<RbmOption, string> values)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Config version=\"").Append(ConfigVersion).Append("\">\r\n");
        foreach (var section in values.Keys.Select(option => option.Section).Distinct())
        {
            builder.Append("  <").Append(section).Append(">\r\n");
            foreach (var pair in values.Where(pair => pair.Key.Section == section))
                builder.Append("    ").Append(Element(pair.Key.Name, pair.Value)).Append("\r\n");
            builder.Append("  </").Append(section).Append(">\r\n");
        }

        return builder.Append("</Config>\r\n").ToString();
    }

    public static void WriteNew(string path, IReadOnlyDictionary<RbmOption, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, new UTF8Encoding(true), NewFile(values));
    }

    /// <summary>The text edit on its own: the new text, and the keys whose value changed.</summary>
    public static string Edit(string text, IReadOnlyDictionary<RbmOption, string> values, out List<string> changed)
    {
        var layout = Locate(text);
        changed = new List<string>();
        var edits = new List<(int Start, int Length, string Text)>();
        var newLine = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var missing = new List<KeyValuePair<RbmOption, string>>();

        foreach (var pair in values)
        {
            var option = pair.Key;
            var wanted = pair.Value;
            if (!layout.Elements.TryGetValue(option.Key, out var span))
            {
                missing.Add(pair);
                changed.Add(option.Key);
                continue;
            }

            if (span.HasChildren)
                throw new InvalidDataException(option.Key + " has other settings inside it, so it was left alone.");
            if (span.SelfClosing)
            {
                edits.Add((span.Open, span.End - span.Open, Element(option.Name, wanted)));
                changed.Add(option.Key);
                continue;
            }

            var content = text.Substring(span.ContentStart, span.ContentEnd - span.ContentStart);
            if (Unescape(content).Trim() == wanted)
                continue;
            var core = content.Trim();
            var start = core.Length == 0 ? span.ContentStart : span.ContentStart + content.IndexOf(core, StringComparison.Ordinal);
            var length = core.Length == 0 ? content.Length : core.Length;
            edits.Add((start, length, Escape(wanted)));
            changed.Add(option.Key);
        }

        // Missing settings go to the end of their part; a missing part goes to the end of <Config>.
        var config = layout.Config;
        var newSections = new List<string>();
        foreach (var group in missing.GroupBy(pair => pair.Key.Section))
        {
            var elements = group.Select(pair => Element(pair.Key.Name, pair.Value)).ToList();
            if (layout.Sections.TryGetValue(group.Key, out var section))
            {
                edits.Add(InsertInto(text, section, elements, newLine));
                continue;
            }

            var sectionIndent = ChildIndent(text, config);
            var childIndent = sectionIndent + IndentUnit(sectionIndent);
            var block = new StringBuilder("<" + group.Key + ">");
            foreach (var element in elements)
                block.Append(newLine).Append(childIndent).Append(element);
            block.Append(newLine).Append(sectionIndent).Append("</").Append(group.Key).Append('>');
            newSections.Add(block.ToString());
        }

        if (newSections.Count > 0)
            edits.Add(InsertInto(text, config, newSections, newLine));

        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return result.ToString();
    }

    private static (int Start, int Length, string Text) InsertInto(string text, Span parent, List<string> children, string newLine)
    {
        var parentIndent = IndentBefore(text, parent.Open) ?? "";
        var childIndent = ChildIndent(text, parent);
        if (parent.SelfClosing)
        {
            var block = new StringBuilder("<" + parent.Name + ">");
            foreach (var child in children)
                block.Append(newLine).Append(childIndent).Append(child);
            block.Append(newLine).Append(parentIndent).Append("</").Append(parent.Name).Append('>');
            return (parent.Open, parent.End - parent.Open, block.ToString());
        }

        // A closing tag on its own line: the new lines go above it, indented like the other children.
        if (IndentBefore(text, parent.ContentEnd) != null)
        {
            var lineStart = LineStartOf(text, parent.ContentEnd);
            var lines = new StringBuilder();
            foreach (var child in children)
                lines.Append(childIndent).Append(child).Append(newLine);
            // The part's content ended on the closing tag's line only when the part was empty.
            if (lineStart <= parent.ContentStart)
                return (parent.ContentEnd, 0, newLine + lines + parentIndent);
            return (lineStart, 0, lines.ToString());
        }

        return (parent.ContentEnd, 0, string.Concat(children));
    }

    private static string ChildIndent(string text, Span parent)
    {
        var parentIndent = IndentBefore(text, parent.Open) ?? "";
        if (parent.LastChildOpen >= 0 && IndentBefore(text, parent.LastChildOpen) is { } indent)
            return indent;
        return parentIndent + IndentUnit(parentIndent);
    }

    private static string IndentUnit(string indent) => indent.Length > 0 && indent.All(c => c == '\t') ? "\t" : "  ";

    private static string Element(string name, string value) => "<" + name + ">" + Escape(value) + "</" + name + ">";

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Unescape(string value) =>
        value.Contains('&')
            ? value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&")
            : value;

    private static int LineStartOf(string text, int index)
    {
        var i = index;
        while (i > 0 && text[i - 1] != '\n' && text[i - 1] != '\r')
            i--;
        return i;
    }

    /// <summary>The spaces before the tag at <paramref name="index"/> when nothing else is before it on its line.</summary>
    private static string? IndentBefore(string text, int index)
    {
        var start = LineStartOf(text, index);
        var before = text.Substring(start, index - start);
        return before.All(c => c == ' ' || c == '\t') ? before : null;
    }

    private static (string Text, Encoding Encoding) ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding;
        int skip;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            (encoding, skip) = (new UTF8Encoding(true), 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            (encoding, skip) = (new UnicodeEncoding(false, true), 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            (encoding, skip) = (new UnicodeEncoding(true, true), 2);
        else
            (encoding, skip) = (new UTF8Encoding(false), 0);
        return (encoding.GetString(bytes, skip, bytes.Length - skip), encoding);
    }

    private static void WriteAtomic(string path, Encoding encoding, string text)
    {
        var temp = path + ".coodsz-tmp";
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(preamble);
            stream.Write(body);
            stream.Flush(true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private sealed class Span
    {
        public string Name = "";
        public int Open;
        public int ContentStart = -1;
        public int ContentEnd = -1;
        public int End;
        public bool SelfClosing;
        public bool HasChildren;
        public int LastChildOpen = -1;
    }

    private sealed class Layout
    {
        public Span Config = new();
        public Dictionary<string, Span> Sections = new(StringComparer.Ordinal);
        public Dictionary<string, Span> Elements = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Finds &lt;Config&gt;, its parts and their direct children in the text. Like RBM's XPath only
    /// the first element of each name counts.
    /// </summary>
    private static Layout Locate(string text)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                lineStarts.Add(i + 1);
            }
            else if (text[i] == '\n')
                lineStarts.Add(i + 1);
        }

        var layout = new Layout();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var info = (IXmlLineInfo)reader;
        var stack = new List<Span>();
        var paths = new List<string>();
        var sawRoot = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var nameAt = lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
                var span = new Span { Name = reader.Name, Open = nameAt - 1 };
                if (stack.Count == 0)
                {
                    if (sawRoot || reader.LocalName != "Config")
                        throw new InvalidDataException("it does not start with <Config>");
                    sawRoot = true;
                }
                else
                {
                    stack[^1].HasChildren = true;
                    stack[^1].LastChildOpen = span.Open;
                }

                var path = stack.Count == 0 ? reader.Name : paths[^1] + "/" + reader.Name;
                var gt = EndOfTag(text, nameAt);
                if (reader.IsEmptyElement)
                {
                    span.SelfClosing = true;
                    span.End = gt + 1;
                    Record(layout, path, stack.Count, span);
                }
                else
                {
                    span.ContentStart = gt + 1;
                    stack.Add(span);
                    paths.Add(path);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                var nameAt = lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
                var span = stack[^1];
                var path = paths[^1];
                stack.RemoveAt(stack.Count - 1);
                paths.RemoveAt(paths.Count - 1);
                span.ContentEnd = nameAt - 2;
                span.End = text.IndexOf('>', nameAt) + 1;
                Record(layout, path, stack.Count, span);
            }
        }

        if (!sawRoot)
            throw new InvalidDataException("it has no <Config>");
        return layout;
    }

    private static void Record(Layout layout, string path, int depth, Span span)
    {
        switch (depth)
        {
            case 0:
                layout.Config = span;
                break;
            case 1:
                layout.Sections.TryAdd(path["Config/".Length..], span);
                break;
            case 2:
                layout.Elements.TryAdd(path["Config/".Length..], span);
                break;
        }
    }

    private static int EndOfTag(string text, int from)
    {
        char quote = '\0';
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c == '"' || c == '\'')
                quote = c;
            else if (c == '>')
                return i;
        }

        throw new InvalidDataException("a tag is not closed");
    }
}
