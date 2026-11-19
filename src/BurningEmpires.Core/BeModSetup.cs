using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>The setups saved in be-launcher.json (client) or be-server.json (host), in the TAOM
/// setup model. "Burning Empires" is always there, always first and always locked: it is exactly
/// the sealed Burning Empires stack, Elephants included. There is no plus set. Files saved before
/// Elephants joined the core named two locked setups, "Plain Burning Empires" and "Burning Empires
/// with Elephants"; both become "Burning Empires".</summary>
public sealed class BeSetupBook
{
    public const string CoreName = "Burning Empires";

    /// <summary>The locked setups of the first Burning Empires tools. A saved file or a host's
    /// setup with one of these names means CoreName now.</summary>
    public static readonly IReadOnlyList<string> OldNames = ["Plain Burning Empires", "Burning Empires with Elephants"];

    public string Active { get; set; } = CoreName;
    public List<TaomSetup> Setups { get; set; } = new();

    /// <summary>True for the locked setup the tools bring, by its name now or an old name.</summary>
    public static bool IsBuiltIn(string? name) =>
        name != null && (name.Trim().Equals(CoreName, StringComparison.OrdinalIgnoreCase) || IsOldName(name));

    public static bool IsOldName(string? name) =>
        name != null && OldNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The setup name to use for a name that may be an old locked setup name.</summary>
    public static string CurrentName(string name) => IsOldName(name) ? CoreName : name.Trim();

    /// <summary>The line under the server tool's setup dropdown when the picked setup is locked.</summary>
    public static string LockedNote(string setupName) =>
        setupName + " is locked. Copy it to change mods or their order.";

    /// <summary>The tooltip of a Burning Empires core row on the server tool's Mods page.</summary>
    public const string CoreRowTip = "Part of the Burning Empires core. It always loads in this order.";

    /// <summary>Adds the locked setup when it is missing, keeps it without extras, puts it first,
    /// drops setups without a name or with a repeated name, and fixes the active name. The old
    /// locked setups are dropped and an active old name becomes the locked setup.</summary>
    public void Ensure()
    {
        Setups ??= new List<TaomSetup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<TaomSetup>();
        foreach (var setup in Setups)
        {
            if (setup == null || string.IsNullOrWhiteSpace(setup.Name) || IsOldName(setup.Name))
                continue;
            setup.Name = setup.Name.Trim();
            setup.Extras ??= new List<TaomExtraMod>();
            if (!seen.Add(setup.Name))
                continue;
            kept.Add(setup);
        }

        var core = kept.FirstOrDefault(setup => setup.Name.Equals(CoreName, StringComparison.OrdinalIgnoreCase)) ?? new TaomSetup();
        kept.Remove(core);
        core.Name = CoreName;
        // A locked setup is the same on every PC, so players and hosts match.
        core.Locked = true;
        core.Extras.Clear();
        kept.Insert(0, core);
        foreach (var setup in kept.Skip(1))
        {
            setup.Locked = false;
            BeLoadOrder.Normalize(setup);
        }

        Setups = kept;
        var active = string.IsNullOrWhiteSpace(Active) ? null : Find(CurrentName(Active));
        Active = active?.Name ?? CoreName;
    }

    public TaomSetup? Find(string? name) =>
        name == null ? null : Setups.FirstOrDefault(setup => setup.Name.Equals(CurrentName(name), StringComparison.OrdinalIgnoreCase));

    public TaomSetup Current()
    {
        Ensure();
        return Find(Active)!;
    }

    public string NextName()
    {
        var number = 2;
        while (Find("Setup " + number) != null)
            number++;
        return "Setup " + number;
    }

    /// <summary>A new setup with only the Burning Empires core, made active.</summary>
    public TaomSetup Create(string? name = null)
    {
        Ensure();
        var setup = new TaomSetup { Name = CheckNewName(name ?? NextName()) };
        Setups.Add(setup);
        Active = setup.Name;
        return setup;
    }

    /// <summary>A copy of a setup with its extra mods, made active. The locked setup can be
    /// copied, which is the quick way to start a setup that adds mods.</summary>
    public TaomSetup CopyOf(string source, string? name = null)
    {
        Ensure();
        var from = Find(source) ?? throw new InvalidOperationException("That setup was not found.");
        var copy = from.Copy(CheckNewName(name ?? NextName()));
        Setups.Add(copy);
        Active = copy.Name;
        return copy;
    }

    public void Rename(string from, string to)
    {
        Ensure();
        var setup = Find(from) ?? throw new InvalidOperationException("That setup was not found.");
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Its name cannot be changed.");
        var name = (to ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Type a setup name first.");
        if (name.Equals(setup.Name, StringComparison.Ordinal))
            return;
        var other = Find(name);
        if (other != null && !ReferenceEquals(other, setup))
            throw new InvalidOperationException("A setup named " + name + " already exists.");
        var wasActive = Active.Equals(setup.Name, StringComparison.OrdinalIgnoreCase);
        setup.Name = name;
        if (wasActive)
            Active = name;
    }

    /// <summary>Deletes an unlocked setup. The active setup falls back to the locked Burning Empires setup.</summary>
    public void Delete(string name)
    {
        Ensure();
        var setup = Find(name) ?? throw new InvalidOperationException("That setup was not found.");
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. It cannot be deleted.");
        Setups.Remove(setup);
        if (Active.Equals(setup.Name, StringComparison.OrdinalIgnoreCase))
            Active = CoreName;
    }

    public void Switch(string name)
    {
        Ensure();
        Active = (Find(name) ?? throw new InvalidOperationException("That setup was not found.")).Name;
    }

    private string CheckNewName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Type a setup name first.");
        if (Find(trimmed) != null)
            throw new InvalidOperationException("A setup named " + trimmed + " already exists.");
        return trimmed;
    }
}
