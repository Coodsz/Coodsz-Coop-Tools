using System.Reflection;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// The NEW badges: which features a tool badges, the version rule, and the seen file in %AppData%\Coodsz.
public sealed class NewFeaturesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "newfeatures-" + Guid.NewGuid().ToString("N"));

    private static readonly NewFeature[] Features =
    [
        new("old", "v1.2.52"),
        new("map", "v1.2.53"),
        new("later", "v1.2.54"),
    ];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_fresh_install_badges_only_this_versions_new_things()
    {
        var store = NewFeatureStore.Load("test-tool", "v1.2.53", Features, _folder);
        Assert.Null(store.Previous);
        Assert.True(store.Shows("map"));
        Assert.False(store.Shows("old"));
        // A feature for a newer version than this copy never shows, and nor does an unknown id.
        Assert.False(store.Shows("later"));
        Assert.False(store.Shows("nothing"));
        Assert.True(File.Exists(NewFeatureStore.PathFor("test-tool", _folder)));
    }

    [Fact]
    public void Clicking_marks_a_feature_seen_for_good()
    {
        var store = NewFeatureStore.Load("test-tool", "v1.2.53", Features, _folder);
        store.MarkSeen("map");
        Assert.False(store.Shows("map"));

        var again = NewFeatureStore.Load("test-tool", "v1.2.53", Features, _folder);
        Assert.True(again.IsSeen("map"));
        Assert.False(again.Shows("map"));
        // The next update keeps what was tried.
        Assert.True(NewFeatureStore.Load("test-tool", "v1.2.54", Features, _folder).IsSeen("map"));
    }

    [Fact]
    public void An_update_badges_only_what_came_after_the_version_before()
    {
        NewFeatureStore.Load("test-tool", "v1.2.53", Features, _folder);
        var updated = NewFeatureStore.Load("test-tool", "v1.2.54", Features, _folder);
        Assert.Equal("v1.2.53", updated.Previous);
        Assert.Equal("v1.2.54", updated.Version);
        // Map was this player's new thing last version; it does not come back after the update.
        Assert.False(updated.Shows("map"));
        Assert.True(updated.Shows("later"));

        // Opening the same version again keeps the version before.
        Assert.Equal("v1.2.53", NewFeatureStore.Load("test-tool", "v1.2.54", Features, _folder).Previous);
    }

    [Fact]
    public void Skipping_a_version_still_badges_its_new_things()
    {
        NewFeatureStore.Load("test-tool", "v1.2.52", Features, _folder);
        var updated = NewFeatureStore.Load("test-tool", "v1.2.54", Features, _folder);
        Assert.True(updated.Shows("map"));
        Assert.True(updated.Shows("later"));
        Assert.False(updated.Shows("old"));
    }

    [Fact]
    public void A_broken_file_counts_as_a_fresh_install()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(NewFeatureStore.PathFor("test-tool", _folder), "{ not json");
        var store = NewFeatureStore.Load("test-tool", "v1.2.53", Features, _folder);
        Assert.Null(store.Previous);
        Assert.True(store.Shows("map"));
    }

    [Fact]
    public void The_rule_on_its_own()
    {
        var map = new NewFeature("map", "v1.2.53");
        Assert.True(NewFeatureStore.Shows(map, "v1.2.53", null, seen: false));
        Assert.False(NewFeatureStore.Shows(map, "v1.2.53", null, seen: true));
        Assert.False(NewFeatureStore.Shows(map, "v1.2.54", null, seen: false));
        Assert.True(NewFeatureStore.Shows(map, "v1.2.55", "v1.2.52", seen: false));
        Assert.False(NewFeatureStore.Shows(map, "v1.2.55", "v1.2.53", seen: false));
        Assert.False(NewFeatureStore.Shows(map, "v1.2.52", null, seen: false));
    }

    [Fact]
    public void Each_tool_badges_this_releases_new_things()
    {
        Assert.Equal(
            [NewFeatures.Home, NewFeatures.Players, NewFeatures.Map, NewFeatures.Encyclopedia, NewFeatures.Guides, NewFeatures.Cheats,
                NewFeatures.StreamerMode, NewFeatures.GameSpeed, NewFeatures.WorldPresets, NewFeatures.CreateWorld, NewFeatures.WhatsNewButton,
                NewFeatures.RbmSettings, NewFeatures.ClanCap],
            NewFeatures.CoodszHost.Select(f => f.Id));
        Assert.Equal([NewFeatures.Guides, NewFeatures.GameSpeed, NewFeatures.WhatsNewButton], NewFeatures.TaomServer.Select(f => f.Id));
        Assert.Equal([NewFeatures.Guides, NewFeatures.WhatsNewButton], NewFeatures.TaomLauncher.Select(f => f.Id));
        // The AD1259 tools badge the What's new button and the client launcher's Keep RBM coop friendly.
        Assert.Equal([NewFeatures.WhatsNewButton, NewFeatures.RbmCoopFriendly], NewFeatures.AD1259.Select(f => f.Id));
        Assert.Equal([NewFeatures.WhatsNewButton, NewFeatures.JoinHelp, NewFeatures.SaveLogs, NewFeatures.RbmCoopFriendly], NewFeatures.Friends.Select(f => f.Id));
        Assert.Same(NewFeatures.AD1259, NewFeatures.For("ad1259-server"));
        Assert.Same(NewFeatures.AD1259, NewFeatures.For("ad1259-launcher"));
        // The Burning Empires tools are new, so the badges they carry over from the TAOM tools are new there too.
        Assert.Equal([NewFeatures.Guides, NewFeatures.GameSpeed, NewFeatures.WhatsNewButton], NewFeatures.BeServer.Select(f => f.Id));
        Assert.Equal([NewFeatures.Guides, NewFeatures.WhatsNewButton], NewFeatures.BeLauncher.Select(f => f.Id));
        Assert.Same(NewFeatures.BeServer, NewFeatures.For("be-server"));
        Assert.Same(NewFeatures.BeLauncher, NewFeatures.For("be-launcher"));
        Assert.Empty(NewFeatures.For("someone-else"));

        // Every badge belongs to a version with What's new notes, and none to a version after this copy.
        foreach (var (list, notes, version) in new[]
                 {
                     (NewFeatures.CoodszHost, WhatsNew.Friends, ReleaseNotice.FriendsVersion),
                     (NewFeatures.Friends, WhatsNew.Friends, ReleaseNotice.FriendsVersion),
                     (NewFeatures.TaomServer, WhatsNew.Taom, ReleaseNotice.TaomVersion),
                     (NewFeatures.TaomLauncher, WhatsNew.Taom, ReleaseNotice.TaomVersion),
                     (NewFeatures.AD1259, WhatsNew.AD1259, ReleaseNotice.AD1259Version),
                     (NewFeatures.BeServer, WhatsNew.Be, ReleaseNotice.BeVersion),
                     (NewFeatures.BeLauncher, WhatsNew.Be, ReleaseNotice.BeVersion),
                 })
        {
            Assert.Equal(list.Count, list.Select(f => f.Id).Distinct().Count());
            Assert.All(list, f => Assert.Contains(notes, entry => entry.Version == f.Version));
            Assert.All(list, f => Assert.False(ReleaseNotice.IsNewer(f.Version, version)));
        }
    }

    [Fact]
    public void Every_tool_that_shows_whats_new_has_a_badge_list()
    {
        foreach (var app in new[] { "CoodszHost", "BannerlordCoopLauncher", "TaomCoopHost", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher" })
        {
            var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", app, "App.xaml.cs"));
            foreach (var tool in System.Text.RegularExpressions.Regex.Matches(code, "WhatsNewOnce\\(\"([a-z0-9-]+)\"").Select(m => m.Groups[1].Value))
                Assert.NotEmpty(NewFeatures.For(tool));
        }
    }

    [Fact]
    public void Every_badged_feature_is_put_on_a_button()
    {
        var ids = typeof(NewFeatures).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name is not (nameof(NewFeatures.Badge) or nameof(NewFeatures.BadgeTip)))
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name);
        var code = string.Join("\n", new[] { "CoodszHost", "BannerlordCoopLauncher", "TaomCoopHost", "TaomCoopLauncher", "BurningEmpiresHost", "BurningEmpiresLauncher" }
            .SelectMany(app => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", app), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains("\\obj\\") && !file.Contains("\\bin\\"))
            .Select(File.ReadAllText));
        foreach (var feature in NewFeatures.CoodszHost.Concat(NewFeatures.Friends).Concat(NewFeatures.TaomServer).Concat(NewFeatures.TaomLauncher).Concat(NewFeatures.BeServer).Concat(NewFeatures.BeLauncher))
            Assert.Contains("NewFeatures." + ids[feature.Id], code);
    }

    [Fact]
    public void The_badge_is_translated()
    {
        foreach (var language in Translator.Languages.Where(l => l.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.True(table.ContainsKey(NewFeatures.Badge), language.Code);
            Assert.True(table.ContainsKey(NewFeatures.BadgeTip), language.Code);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
