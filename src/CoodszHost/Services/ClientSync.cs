using System.IO;
using System.Xml;

namespace CoodszHost.Services;

public static class ClientSync
{
    public static string DocumentsRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");

    public static string ClientSaves => Path.Combine(DocumentsRoot, "Game Saves");

    public static string LauncherData => Path.Combine(DocumentsRoot, "Configs", "LauncherData.xml");

    public static Dictionary<string, bool> ReadClientSelection()
    {
        var selected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(LauncherData))
            return selected;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(LauncherData, settings);
        string? id = null;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "UserModData")
                id = null;
            else if (reader.NodeType == XmlNodeType.Element && reader.Name == "Id")
                id = reader.ReadElementContentAsString();
            else if (reader.NodeType == XmlNodeType.Element && reader.Name == "IsSelected" && id != null)
                selected[id] = reader.ReadElementContentAsString().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return selected;
    }

    public static int Apply(IList<ModuleRow> modules)
    {
        var client = ReadClientSelection();
        if (client.Count == 0)
            return 0;
        var changed = 0;
        foreach (var row in modules)
        {
            if (row.Required || !client.TryGetValue(row.Id, out var enabled))
                continue;
            if (row.Enabled != enabled)
                changed++;
            row.Enabled = enabled;
        }

        return changed;
    }
}
