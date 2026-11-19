using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class SharedModVersionsTests
{
    private static readonly List<SharedModEntry> Host =
    [
        new("Native", "Native", "v1.4.8", "0"),
        new("Bannerlord.Harmony", "Harmony", "v2.4.2.248", "2859188632"),
        new("Extra", "Extra mod", "v1.0.0", "123"),
        new("DedicatedServer.Addon", "Add on", "v1.0.0", "0")
    ];

    private static SharedModPart Part(string id) =>
        id == "Native" ? SharedModPart.Core : id.StartsWith("Bannerlord.") ? SharedModPart.Library : SharedModPart.Extra;

    private static bool Skip(string id) => id.StartsWith("DedicatedServer.");

    private static Dictionary<string, string> Same() =>
        Host.ToDictionary(entry => entry.Id, entry => entry.Version);

    [Fact]
    public void The_same_list_has_no_problems_and_skipped_ids_are_never_reported()
    {
        var installed = Same();
        installed.Remove("DedicatedServer.Addon");
        Assert.Empty(SharedModVersions.AgainstHost(Host, installed, Part, Skip));
        Assert.Empty(SharedModVersions.NotOnHost(Host, installed.Keys.Append("DedicatedServer.Other"), Part, Skip));
        // Without a skip rule the add on counts like any module.
        Assert.Equal("DedicatedServer.Addon", Assert.Single(SharedModVersions.AgainstHost(Host, installed, Part)).ModuleId);
    }

    [Fact]
    public void Missing_and_different_modules_carry_the_callers_part_both_versions_and_a_line()
    {
        var installed = Same();
        installed.Remove("Extra");
        installed["Bannerlord.Harmony"] = "v2.4.3.0";
        var problems = SharedModVersions.AgainstHost(Host, installed, Part, Skip);
        Assert.Equal(new[] { "Bannerlord.Harmony", "Extra" }, problems.Select(problem => problem.ModuleId));

        Assert.Equal(new SharedModProblem(SharedModProblemKind.Different, SharedModPart.Library, "Bannerlord.Harmony", "Harmony", "v2.4.2.248", "v2.4.3.0",
            "Harmony is v2.4.3.0 here. The shared list says v2.4.2.248."), problems[0]);
        Assert.Equal(new SharedModProblem(SharedModProblemKind.Missing, SharedModPart.Extra, "Extra", "Extra mod", "v1.0.0", "",
            "Extra mod is on the host's list but not on this PC."), problems[1]);
    }

    [Fact]
    public void Installed_ids_are_matched_without_case_and_the_first_of_two_wins()
    {
        var installed = new List<KeyValuePair<string, string>>
        {
            new("native", "v1.4.8"),
            new("BANNERLORD.HARMONY", "v2.4.2.248"),
            new("Bannerlord.Harmony", "v0.0.1"),
            new(" Extra ", "v1.0.0")
        };
        Assert.Empty(SharedModVersions.AgainstHost(Host, installed, Part, Skip));
    }

    [Fact]
    public void Versions_are_compared_as_written_apart_from_spaces_and_case()
    {
        var installed = Same();
        installed["Bannerlord.Harmony"] = "2.4.2.248";
        installed["Native"] = " V1.4.8 ";
        var problem = Assert.Single(SharedModVersions.AgainstHost(Host, installed, Part, Skip));
        Assert.Equal(SharedModProblemKind.Different, problem.Kind);
        Assert.Equal("Bannerlord.Harmony", problem.ModuleId);
    }

    [Fact]
    public void An_empty_version_on_either_side_and_a_repeated_host_entry_are_not_reported()
    {
        var host = Host.Append(new SharedModEntry("extra", "Again", "v5", "0")).ToList();
        host[0] = host[0] with { Version = "" };
        var installed = Same();
        installed["Bannerlord.Harmony"] = "";
        Assert.Empty(SharedModVersions.AgainstHost(host, installed, Part, Skip));
    }

    [Fact]
    public void Active_modules_the_host_lacks_are_named_once_in_launch_order()
    {
        var active = new[] { "Native", "Bannerlord.Harmony", "Mine", "Extra", "mine", "Other" };
        var problems = SharedModVersions.NotOnHost(Host, active, Part, Skip, id => id == "Mine" ? "My mod" : null);
        Assert.Equal(new[] { "Mine", "Other" }, problems.Select(problem => problem.ModuleId));
        Assert.All(problems, problem => Assert.Equal(SharedModProblemKind.NotOnHost, problem.Kind));
        Assert.Equal("My mod is on this setup but not on the host's list.", problems[0].Text);
        Assert.Equal("Other", problems[1].Name);
        Assert.Equal(SharedModPart.Extra, problems[1].Part);
    }
}
