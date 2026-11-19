using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Services;

public static class LogLineFormatter
{
    public readonly record struct Entry(string Time, string Kind, string Message, string Tone);

    public static Entry? Format(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
            return null;

        var time = "";
        var stamped = Regex.Match(line, @"^\[(\d{2}:\d{2}:\d{2})(?:\.\d+)?\]\s*(.*)$");
        if (stamped.Success)
        {
            time = stamped.Groups[1].Value;
            line = stamped.Groups[2].Value.Trim();
        }

        if (line.Length == 0 || IsStartupNoise(line) || IsEngineProbeMiss(line))
            return null;
        if (line.StartsWith("What happened:", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "What happened", line["What happened:".Length..].Trim(), "bad");
        if (line.StartsWith("What to do:", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "What to do", line["What to do:".Length..].Trim(), "warn");
        if (line.StartsWith("What not to do:", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "What not to do", line["What not to do:".Length..].Trim(), "warn");
        if (line.StartsWith("Assembly load result:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (line.StartsWith("SAVE:", StringComparison.OrdinalIgnoreCase))
        {
            var saved = line["SAVE:".Length..].Trim();
            var tone = saved.StartsWith("Saved ", StringComparison.OrdinalIgnoreCase) ? "good"
                : saved.StartsWith("Saving ", StringComparison.OrdinalIgnoreCase) ? "warn"
                : "bad";
            return new Entry(time, "SAVE", saved, tone);
        }

        if (line.StartsWith("Loading assembly:", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "Loaded", FileName(line["Loading assembly:".Length..]), "info");
        if (line.StartsWith("opening ", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "Opened", FileName(line["opening ".Length..]), "info");
        if (line.StartsWith("Loading xml file:", StringComparison.OrdinalIgnoreCase))
            return new Entry(time, "Opened", FileName(line["Loading xml file:".Length..]), "info");

        var box = Regex.Match(line, @"^Messagebox\s+\[(?<level>[^\]]+)\]\s+message:\s*(?<msg>.*)$", RegexOptions.IgnoreCase);
        if (box.Success)
        {
            var msg = box.Groups["msg"].Value.Trim();
            if (msg.StartsWith("Cannot find:", StringComparison.OrdinalIgnoreCase))
                return new Entry(time, "Missing", FileName(msg["Cannot find:".Length..]), "warn");
            if (msg.StartsWith("Cannot load:", StringComparison.OrdinalIgnoreCase))
            {
                if (IsEngineProbeMiss(msg))
                    return null;
                return new Entry(time, "Cannot load", msg["Cannot load:".Length..].Trim(), "bad");
            }
            return new Entry(time, CleanKind(box.Groups["level"].Value), msg, "bad");
        }

        var tag = Regex.Match(line, @"^\[(?<tag>[A-Za-z][A-Za-z0-9_.]*)\]\s*(?<msg>.+)$");
        if (tag.Success)
            return new Entry(time, tag.Groups["tag"].Value, tag.Groups["msg"].Value.Trim(), "info");

        return new Entry(time, "Note", line, "info");
    }

    /// <summary>
    /// The message in the picked language. The language files hold a report line whole ("What happened: ..."),
    /// so it is looked up with its label and the translated label is cut off again, since the Logs page
    /// shows the label on its own. A line with no entry stays in English.
    /// </summary>
    public static string ShownMessage(Entry entry)
    {
        if (entry.Kind.StartsWith("What ", StringComparison.Ordinal) && Translator.Current != Translator.English)
        {
            var english = entry.Kind + ": " + entry.Message;
            var whole = Translator.T(english);
            if (whole != english)
            {
                var label = Regex.Match(whole, @"^\s*" + Regex.Escape(Translator.T(entry.Kind)) + @"\s*[:\uFF1A]\s*");
                if (label.Success)
                    return whole[label.Length..];
                // Some lines word the label another way (Arabic has a few), so it goes up to the first colon (Chinese and Japanese write a full width one).
                var colon = whole.IndexOfAny([':', '\uFF1A']);
                if (colon > 0 && colon < 40)
                    return whole[(colon + 1)..].TrimStart();
            }
        }

        return Translator.T(entry.Message);
    }

    private static bool IsEngineProbeMiss(string line)
    {
        if (line.StartsWith("Cannot load:", StringComparison.OrdinalIgnoreCase))
        {
            var target = line["Cannot load:".Length..].Trim();
            return target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !target.Contains('\\')
                && !target.Contains('/');
        }

        return line.Contains("Could not load file or assembly", StringComparison.OrdinalIgnoreCase)
            && line.Contains(@"\engine\bin\Win64_Shipping_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStartupNoise(string line)
    {
        return line.Contains("HexTool", StringComparison.OrdinalIgnoreCase)
            || line.Contains("shared CLR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("compatibility responsibility", StringComparison.OrdinalIgnoreCase)
            || line.Contains("incompatible identity", StringComparison.OrdinalIgnoreCase)
            || line.Contains("permits this mismatch", StringComparison.OrdinalIgnoreCase)
            || line.Contains("SHA256=", StringComparison.OrdinalIgnoreCase)
            || line.Contains("PublicKeyToken=", StringComparison.OrdinalIgnoreCase)
            || line.Contains("System.Runtime.CompilerServices.Unsafe", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanKind(string level)
    {
        var text = level.Trim();
        return text.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ? "Error" : text;
    }

    private static string FileName(string path)
    {
        var text = path.Trim().Trim('"', ' ');
        var slash = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
        return slash >= 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
    }
}
