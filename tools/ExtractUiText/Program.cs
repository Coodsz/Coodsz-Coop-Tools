using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Collects the English text the tools show (XAML labels and user-facing strings in the
// C# code) so it can be translated into src\BannerlordCoopLauncher.Core\Localization\Languages.
//
//   dotnet run --project tools\ExtractUiText -- <repoRoot> <keys.json> [stats]
//
// keys.json gets every English string. For each language file it also writes
// keys.missing-<code>.json with the strings that file does not have yet; translate those
// and add them to the language file. Lines for the log boxes are included (shown translated);
// lines that only go to the host-action file stay English.
var root = args[0];
var found = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var callers = new Dictionary<string, int>();
string[] projects = { "AD1259Launcher", "BannerlordCoopLauncher", "BannerlordCoopLauncher.Core", "BurningEmpires.Core", "BurningEmpiresHost", "BurningEmpiresLauncher", "CoodszHost", "RealmOfThrones.Core", "RealmOfThronesHost", "RealmOfThronesLauncher", "TaomCoop.Core", "TaomCoopHost", "TaomCoopLauncher" };
var skipCalls = new HashSet<string>(StringComparer.Ordinal)
{
    "Combine", "GetEnvironmentVariable", "SetEnvironmentVariable", "StartsWith", "EndsWith", "Contains", "Equals", "IndexOf", "LastIndexOf",
    "Replace", "Split", "Trim", "TrimEnd", "TrimStart", "Match", "Matches", "IsMatch", "Regex", "GetProperty", "TryGetProperty", "GetValue",
    "SetValue", "Exists", "ReadAllText", "WriteAllText", "EnumerateFiles", "GetFiles", "Start", "Element", "Elements", "Attribute", "Descendants",
    "nameof", "Add" /* checked below */, "GetString", "Parse", "TryParse", "FindName", "Equal", "Assert", "Join", "Format", "ToString",
    "GetManifestResourceStream", "OpenSubKey", "CreateSubKey", "GetFolderPath", "ContainsKey", "TryGetValue", "Remove", "Delete", "Copy", "Move",
    /* host-action file only, stays English */ "Note", /* log pack lines (WorkshopCoopFacts), stay English */ "FactLine", "NoteAction", "Mod", "VersionOf", "GetAttribute", "GetMethod", "FindResource", "ParseWhole", "WriteLine", "Debug", "Trace", "SendCommand", "Send", "Run", "Kill", "GetProcessesByName", "Insert", "PadLeft", "PadRight",
    /* JSON property names and theme brush keys */ "Get", "Paint",
    /* WPF dependency property names (Coodsz.Ui frame controls). Screen text must never go into a RegisterAttached
       default: this skip leaves it out of the language files. */ "RegisterAttached"
};
// Not screen text either: a mod name, a file dialog filter, Windows event log names, lines the
// tools look for in a server log, and the sample log lines of the server crash self test.
var notText = new HashSet<string>(StringComparer.Ordinal)
{
    "Improved Garrisons Coop Compat", "Application", "Microsoft-Windows-Windows Defender/Operational",
    "first-time setup complete", "The server was stopped intentionally; start it again to load them.", "Server copy: remove the mod's Assets folder",
    "engine exited with code -1073741819", "Server process exited with code 1.", "pulse: players=1", "this Burning Empires build migrates older saves",
    // The start script's refusal line MissingCampaignMap looks for, and a host-action file label (RecopyShortCopies).
    "Campaign map missing:", "Map mod", "Locked setup",
    // Mod folder names in ServerModuleInstall.ServerFolders.
    "Assets", "Prefabs",
    // The server's Game Saves folder (NightlyServer) and an art folder the Realm of Thrones server copies leave out (RotServerPath).
    "Game Saves", "Shaders",
    // The Realm of Thrones module's failure lines RotWorldWatch looks for in the server console.
    "!!! World check FAILED", "!!! Fresh campaign:", "Host: !!! at world ready Hero.MainHero is", "Host: !!! at the first daily tick Hero.MainHero is",
    "THE HOST IS NOT COMPLETE", "DO NOT KEEP ITS SAVE",
    "PNG or JPG|*.png;*.jpg;*.jpeg|All files|*.*", "Zip (*.zip)|*.zip",
    // The folder picker's fallback start folder.
    @"C:\Program Files (x86)\Steam",
    // Lines the Burning Empires server tool writes only to its host-console log file, which stays English.
    "[BE host] Server exited.", "[BE host] engine exited with code {0}",
    "[RoT host] Server exited.", "[RoT host] Server exited with code {0}.",
    // The Donate menu lines are brand names and stay as written in every language (NoTranslate).
    "Linktree (Patreon, PayPal)", "Buy Me a Coffee",
    // A mod's MCM folder name in SettingsLockDefaults, a file path part and never screen text.
    "Diplomacy",
    // The Coop server console line PlayHold looks for, a regex and never screen text.
    @"\[Coop\] Peer requested time control: .*\bmode=""?Pause\b",
    // Lines the updater writes only to update-install.log, which stays English like the log zips.
    "{0} closed because {1} is already running.",
    "Windows Script Host (WScript.Shell) is missing, so shortcuts to the old program cannot be moved. The old program stays.",
    "{0} shortcut(s) to the old program could not be moved to {1}. The old program stays and the next start tries again.",
    // Lines that only go to settings-move.log (ToolData, ToolPayload, MoreNationsFoldIn), which stays English,
    // and the marker text CopiedFrom reads back.
    " copied from ", " newer copies from ", "{0} copied from {1}", "{0} newer copies from {1}",
    "Copied the {0} folder from beside the exe.", "Copied {0} from beside the exe.", "Could not copy {0}. {1}",
    "Kept the copy of {0} here as {1}.bak.", "Left the {0} folder beside the exe. It holds {1} MB, more than a settings folder should.",
    "Took the newer {0} from beside the exe, changed by an older version.",
    "Removed the old {0} folder beside the exe. This exe carries it and wrote it out to {1}.",
    "The payload contains a file outside its folder.", "{0} did not match after it was written out.", "{0} is being written out by another tool.",
    " (More Nations)", "Added {0} setup(s) from the {1} settings in {2}{3}. The whole file is kept as {4}.",
    "Could not copy {0} from {1}. {2}", "Could not fold in the settings from {0}. {1}", "Could not read the {0} {1} in {2}. It was kept as {3}.",
    "Could not read {0}, so it was kept as {1}.", "Took the {0} settings from {1}.",
    "Turned the More Nations switch on, because these settings come from Coodsz and Friends.",
    // A module name, a file name and a section id.
    "Coodsz Image Exporter", "README - where each file goes.txt", "Settingadvanced",
    // The tool name in the Still not working? message, which goes to Coodsz in English (ReportReplies.DiscordMessage).
    "Coodsz tool",
    // RotServerBuild: XML names of NavalDLC's SubModule.xml, the header of naval_map_points.txt (a server file) and the
    // War Sails part names, which are Coop's own names in its launcher's lines.
    "Module", "Tags", "# GENERATED by War Sails staging from NavalDLC SceneObj/Main_map/scene.xscene (global positions)",
    "NavalDLC module", "NavalDLC Main_map", "War Sails blank",
    // RotCoopInstall.WaitReason: why the Client Launcher still waits, only written to client-action.log.
    "game process", "Coop program {0}", "starting grace", "TaleWorlds launcher",
    // The lines of coodsz-modules.txt in a report (ModuleFingerprints), which stays English like the logs.
    "Coodsz modules on this PC (where | version | SHA256 | ", "No Coodsz module found.\r\n", "our build", "not in this tool's list",
    "Listed by ", "a Coodsz tool", "(no version)", "Stopped at ", " DLLs, so more may be on this PC.\r\n", "Server {0}",
    "not checked", "could not read", "over 64 MB, not hashed", "odd version",
    ". A hash it does not list may be a newer Coodsz build: check it against the module DLLs and src/BannerlordCoopLauncher.Core/Support/KnownModuleHashes.txt on master.\r\n",
    // The tool's name in that file's header (CrashReports.ToolName).
    "Coodsz {0}{1}", " Server Tool", " Client Launcher"
};

foreach (var project in projects)
{
    var dir = Path.Combine(root, "src", project);
    foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
        // UiLanguage.cs and Translator.cs hold the language names, which stay as written. UpdateApply.cs is the
        // update helper with no window: every line it writes goes to update-install.log, which stays English.
        if (file.Contains("\\obj\\") || file.Contains("\\bin\\") || file.EndsWith("UiLanguage.cs") || file.EndsWith("Translator.cs") || file.EndsWith("UpdateApply.cs")
            // crash-stack.txt and its note are read by whoever helps with a crash report, so they stay English like the log zips.
            || file.EndsWith("DumpStackSummary.cs") || file.EndsWith("DumpStackSummary.Child.cs") || file.EndsWith("DacTrust.cs")) continue;
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        var source = File.ReadAllText(file);
        // AD1259Host builds the same Coodsz Host files with AD1259 defined (Edition.cs), so that #if branch is read too.
        var nodes = new[] { CSharpParseOptions.Default, CSharpParseOptions.Default.WithPreprocessorSymbols("AD1259") }
            .SelectMany(options => CSharpSyntaxTree.ParseText(source, options).GetRoot().DescendantNodes());
        foreach (var node in nodes)
        {
            if (node is not (LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } or InterpolatedStringExpressionSyntax)) continue;
            // Only take the outermost + chain.
            ExpressionSyntax top = (ExpressionSyntax)node;
            while (top.Parent is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression)) top = b;
            while (top.Parent is ParenthesizedExpressionSyntax p) top = p;
            if (top != node && !ReferenceEquals(FirstLiteral(top), node)) continue;
            if (InSkippedContext(top)) continue;
            var template = Template(top);
            if (template == null || !LooksLikeUi(template)) continue;
            Add(template, rel);
            var call = top.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (call != null)
            {
                var name = CallName(call);
                callers[name] = callers.GetValueOrDefault(name) + 1;
            }
        }
    }

    foreach (var file in Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories))
    {
        if (file.Contains("\\obj\\") || file.Contains("\\bin\\")) continue;
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        var doc = XDocument.Load(file);
        foreach (var element in doc.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (name is not ("Text" or "Content" or "ToolTip" or "Header" or "Title")) continue;
                var value = attribute.Value;
                if (value.StartsWith("{") || !value.Any(char.IsLetter)) continue;
                Add(value, rel);
            }

            if (element.Name.LocalName is "Run" or "TextBlock" or "Hyperlink" or "Bold" or "Italic")
                foreach (var text in element.Nodes().OfType<XText>())
                {
                    var value = Regex.Replace(text.Value, @"\s+", " ").Trim();
                    if (value.Any(char.IsLetter)) Add(value, rel);
                }
        }
    }
}

// The Guides pages (Coodsz Host, and the TAOM tools in the subfolders): every title, summary,
// heading, step and tip of the guide files.
var guides = Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Keep", "Guides");
if (Directory.Exists(guides))
    foreach (var file in Directory.EnumerateFiles(guides, "*.json", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        using var guide = JsonDocument.Parse(File.ReadAllText(file));
        void Take(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
                Add(text.Trim(), rel);
        }

        Take(guide.RootElement, "title");
        Take(guide.RootElement, "summary");
        if (guide.RootElement.TryGetProperty("sections", out var sections))
            foreach (var section in sections.EnumerateArray())
            {
                Take(section, "heading");
                Take(section, "tip");
                if (section.TryGetProperty("steps", out var steps))
                    foreach (var step in steps.EnumerateArray())
                        if (step.GetString() is { Length: > 0 } text)
                            Add(text.Trim(), rel);
            }
    }

var output = found.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { key = pair.Key, files = pair.Value.ToArray() }).ToList();
File.WriteAllText(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{output.Count} keys, {output.Sum(o => o.key.Length)} chars");
var languages = Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Localization", "Languages");
if (Directory.Exists(languages))
    foreach (var file in Directory.EnumerateFiles(languages, "*.json"))
    {
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
        var missing = output.Select(o => o.key).Where(key => !table.ContainsKey(key)).ToList();
        var code = Path.GetFileNameWithoutExtension(file);
        Console.WriteLine($"{code}: {missing.Count} missing");
        File.WriteAllText(Path.ChangeExtension(args[1], null) + ".missing-" + code + ".json",
            JsonSerializer.Serialize(missing, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

if (args.Length > 2)
    foreach (var pair in callers.OrderByDescending(p => p.Value).Take(60)) Console.WriteLine($"{pair.Value,5} {pair.Key}");

void Add(string key, string file)
{
    // Setup names can be renamed (Plain TAOM is the locked TAOM setup, the two Burning Empires ones and Plain Realm of Thrones are locked
    // too), and a time format is not text.
    if (key.StartsWith("Working stable mods") || key == "Plain TAOM" || key == "TAOM plus" || key == "Plain Burning Empires" || key == "Burning Empires with Elephants" || key == "Plain Realm of Thrones" || key == "Plain Coop (no mods)" || key == "h:mm tt") return;
    if (notText.Contains(key) || key.StartsWith("[20:14:48.304] Loading packages")) return;
    // Program file names (ToolNames.cs) and the update install script's own lines are not screen text.
    if (Regex.IsMatch(key, @"^[A-Za-z0-9 ]+\.exe$") || key.Contains("$steps.Add(")) return;
    if (!found.TryGetValue(key, out var set)) found[key] = set = new SortedSet<string>(StringComparer.Ordinal);
    set.Add(file);
}

static SyntaxNode? FirstLiteral(ExpressionSyntax top) =>
    top.DescendantNodesAndSelf().FirstOrDefault(n => n is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } or InterpolatedStringExpressionSyntax);

static string CallName(InvocationExpressionSyntax call) => call.Expression switch
{
    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
    IdentifierNameSyntax i => i.Identifier.Text,
    GenericNameSyntax g => g.Identifier.Text,
    _ => "?"
};

bool InSkippedContext(ExpressionSyntax top)
{
    for (SyntaxNode? n = top.Parent; n != null; n = n.Parent)
    {
        switch (n)
        {
            case AttributeSyntax:
            case CaseSwitchLabelSyntax:
            case ConstantPatternSyntax:
            case SwitchExpressionArmSyntax arm when arm.Pattern.Span.Contains(top.Span):
            case ElementAccessExpressionSyntax:
            case BracketedArgumentListSyntax:
                return true;
            case InvocationExpressionSyntax call:
                var name = CallName(call);
                if (skipCalls.Contains(name) && name != "Add") return true;
                return false;
            case ObjectCreationExpressionSyntax create:
                var type = create.Type.ToString();
                return type is "Regex" or "ProcessStartInfo" or "Uri" or "HttpRequestMessage";
            case StatementSyntax:
            case MemberDeclarationSyntax:
                return false;
        }
    }
    return false;
}

static string? Template(ExpressionSyntax top)
{
    var parts = new List<ExpressionSyntax>();
    Flatten(top, parts);
    var sb = new StringBuilder();
    var n = 0;
    foreach (var part in parts)
    {
        switch (part)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression) || lit.IsKind(SyntaxKind.CharacterLiteralExpression):
                var text = lit.Token.ValueText;
                if (text.Contains('{') || text.Contains('}')) return null;
                sb.Append(text);
                break;
            case InterpolatedStringExpressionSyntax interp:
                foreach (var content in interp.Contents)
                {
                    if (content is InterpolatedStringTextSyntax t)
                    {
                        var value = t.TextToken.ValueText;
                        if (value.Contains('{') || value.Contains('}')) return null;
                        sb.Append(value);
                    }
                    else sb.Append('{').Append(n++).Append('}');
                }
                break;
            default:
                sb.Append('{').Append(n++).Append('}');
                break;
        }
    }
    return sb.ToString();
}

static void Flatten(ExpressionSyntax e, List<ExpressionSyntax> parts)
{
    while (e is ParenthesizedExpressionSyntax p && p.Expression is BinaryExpressionSyntax pb && pb.IsKind(SyntaxKind.AddExpression) && ContainsString(pb)) e = p.Expression;
    if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
    {
        Flatten(b.Left, parts);
        Flatten(b.Right, parts);
    }
    else parts.Add(e);
}

static bool ContainsString(ExpressionSyntax e) => e.DescendantNodesAndSelf().Any(n => n is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } or InterpolatedStringExpressionSyntax);

static bool LooksLikeUi(string s)
{
    var plain = Regex.Replace(s, @"\{\d+\}", "");
    // XML snippets and regex patterns in code are not screen text.
    if (Regex.IsMatch(plain, @"<\w+[ >/]|\\s\*|\\[sdb][*+)]|\(\?[:i]")) return false;
    var letters = plain.Count(char.IsLetter);
    if (letters < 3) return false;
    if (plain.Contains("\\") && !plain.Contains(' ')) return false;
    if (Regex.IsMatch(plain, @"https?://") && !Regex.IsMatch(plain, @"[A-Za-z]+ [a-z]+ [a-z]+")) return false;
    if (plain.Contains("[CoopSaveRescue]") && !plain.Contains(". ")) return false;
    var trimmed = plain.Trim();
    if (!trimmed.Contains(' '))
    {
        // Single words: only Capitalized plain words read as UI ("Stop", "Running", "Starting...").
        if (!Regex.IsMatch(trimmed, @"^[A-Z][a-z]+(?:[.:!?]|\.\.\.)?$")) return false;
    }
    else
    {
        // Needs a real word pair ("Community: works" counts too). A capital I counts as a word, so the
        // server terms button "I accept" is translated like the sentences that name it.
        if (!Regex.IsMatch(plain, @"(?:[A-Za-z]{2,}|\bI):? [A-Za-z]{2,}")) return false;
        if (Regex.IsMatch(trimmed, @"^[\w.\-]+ [\w.\-/=]+$") && trimmed.Contains('.') ) return false;
        if (trimmed.StartsWith("--") || trimmed.StartsWith("-") && !trimmed.StartsWith("- ")) return false;
    }
    return true;
}
