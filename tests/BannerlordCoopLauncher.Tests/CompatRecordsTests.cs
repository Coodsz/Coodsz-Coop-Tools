using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class CompatRecordsTests
{
    private const string Sample = """
        {
          "schemaversion": 1,
          "GeneratedBy": "someone",
          "records": [
            { "id": "TAOM", "VERDICT": "needsrecipe", "testedVersions": ["v2.0.29"], "TestedCoopVersion": "v0.1.4",
              "DefaultRole": "DependencyOnly", "ServerAuthoritative": true, "ClientSideBehaviors": ["X.Y"],
              "EnsureLines": [{ "File": "coop-modules.txt", "Value": "{coopModuleId}" }],
              "DefaultSettings": { "TAOM": { "EnableTroopWeight": "false", "Count": 12, "On": true } },
              "Notes": "Turn troop weight off.", "FutureField": { "a": 1 }, "UpdatedAt": "2026-09-07T00:00:00Z" },
            { "Id": "GoodMod", "Verdict": 1 },
            { "Id": "BadMod", "Verdict": "Broken", "Url": "javascript:alert(1)" },
            { "Id": "goodmod", "Verdict": "Broken" },
            { "Id": "../evil", "Verdict": "Works" },
            { "Verdict": "Works" },
            { "Id": "Weird", "Verdict": "Works,Broken", "Notes": "line one\nline two" },
            { "Id": "Hidden", "Verdict": "Works", "Notes": "fine‮flipped", "TestedVersions": ["v1​", "v2"] },
            "not an object"
          ]
        }
        """;

    [Fact]
    public void Reads_leniently_ignoring_case_and_unknown_fields()
    {
        var db = CompatRecords.Parse(Sample);
        Assert.NotNull(db);
        var taom = db!.Find("taom");
        Assert.NotNull(taom);
        Assert.Equal(CompatVerdict.NeedsRecipe, taom!.Verdict);
        Assert.Equal(new[] { "v2.0.29" }, taom.TestedVersions);
        Assert.Equal("v0.1.4", taom.TestedCoopVersion);
        Assert.Equal("Turn troop weight off.", taom.Notes);
        Assert.Equal(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), taom.UpdatedAt);
        var settings = taom.DefaultSettings["TAOM"];
        Assert.Equal("false", settings["EnableTroopWeight"]);
        Assert.Equal("12", settings["Count"]);
        Assert.Equal("true", settings["On"]);
        // Enums as numbers too.
        Assert.Equal(CompatVerdict.Works, db.Find("GoodMod")!.Verdict);
    }

    [Fact]
    public void Treats_the_file_as_untrusted()
    {
        var db = CompatRecords.Parse(Sample)!;
        // A record without a valid Id is dropped; the first of two same Ids wins.
        Assert.Null(db.Find("../evil"));
        Assert.Equal(CompatVerdict.Works, db.Find("GOODMOD")!.Verdict);
        Assert.Equal(5, db.Count);
        // A bad address, a line break and invisible characters drop the field, not the record.
        Assert.Null(db.Find("BadMod")!.Url);
        Assert.Null(db.Find("Weird")!.Notes);
        Assert.Equal(CompatVerdict.Unknown, db.Find("Weird")!.Verdict);
        Assert.Null(db.Find("Hidden")!.Notes);
        Assert.Equal(new[] { "v2" }, db.Find("Hidden")!.TestedVersions);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\": 2, \"Records\": []}")]
    [InlineData("{\"SchemaVersion\": \"1\", \"Records\": []}")]
    [InlineData("{\"Records\": {}}")]
    [InlineData("[]")]
    [InlineData("{ broken")]
    [InlineData("")]
    public void Refuses_newer_or_damaged_files(string json) =>
        Assert.Null(CompatRecords.Parse(json));

    [Fact]
    public void Reads_a_file_without_schema_version_and_refuses_a_huge_one()
    {
        Assert.Equal(1, CompatRecords.Parse("{\"Records\":[{\"Id\":\"A\"}]}")!.Count);
        Assert.Null(CompatRecords.Parse("{\"Records\":[],\"x\":\"" + new string('a', CompatRecords.MaxFileChars) + "\"}"));
    }

    [Theory]
    [InlineData(CompatVerdict.Works, "Works", "Tested: works")]
    [InlineData(CompatVerdict.NeedsRecipe, "NeedsRecipe", "Works with settings")]
    [InlineData(CompatVerdict.Broken, "Broken", "Tested: broken")]
    public void Maps_the_verdict_to_a_chip(CompatVerdict verdict, string kind, string text)
    {
        var chip = CompatRecords.Chip(new CompatRecord { Id = "A", Verdict = verdict, Notes = "Some notes.", TestedCoopVersion = "v0.1.6" }, "v1.0.0");
        Assert.NotNull(chip);
        Assert.Equal(kind, chip!.Kind);
        Assert.Equal(text, chip.Text);
        Assert.False(chip.OlderVersion);
        var lines = chip.Tip.Split('\n');
        Assert.Contains("Some notes.", lines);
        Assert.Contains("Tested with Coop v0.1.6.", lines);
        Assert.Equal(CompatRecords.Credit, lines[^1]);
    }

    [Fact]
    public void No_chip_without_a_verdict() =>
        Assert.Null(CompatRecords.Chip(new CompatRecord { Id = "A" }, "v1"));

    [Theory]
    [InlineData("v1.2.0", true)]
    [InlineData("V1.2.0", true)]
    [InlineData("1.2.0", true)]
    [InlineData(" v1.2.0 ", true)]
    [InlineData("v1.3.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_the_installed_version(string? installed, bool tested) =>
        Assert.Equal(tested, CompatRecords.IsTestedVersion(new CompatRecord { Id = "A", TestedVersions = ["v1.2.0", "e1.1"] }, installed));

    [Fact]
    public void An_untested_installed_version_shows_tested_on_an_older_version()
    {
        var record = new CompatRecord { Id = "A", Verdict = CompatVerdict.Works, TestedVersions = ["v1.2.0"] };
        var chip = CompatRecords.Chip(record, "v1.3.0")!;
        Assert.True(chip.OlderVersion);
        Assert.Equal("Tested on an older version", chip.Text);
        Assert.Equal("Works", chip.Kind);
        Assert.Contains("You have v1.3.0, which was not tested.", chip.Tip.Split('\n'));
        // No version on this PC, or a record without versions: the plain verdict.
        Assert.False(CompatRecords.Chip(record, "")!.OlderVersion);
        Assert.False(CompatRecords.Chip(record with { TestedVersions = [] }, "v9")!.OlderVersion);
    }

    [Fact]
    public void Long_notes_are_shortened_and_wrapped_in_the_tip()
    {
        var notes = string.Join(" ", Enumerable.Repeat("word", 400));
        var chip = CompatRecords.Chip(new CompatRecord { Id = "A", Verdict = CompatVerdict.Works, Notes = notes }, null)!;
        Assert.True(chip.Tip.Length < 800);
        Assert.All(chip.Tip.Split('\n'), line => Assert.True(line.Length <= 100));
        Assert.Contains("...", chip.Tip);
    }

    [Fact]
    public void The_community_badge_carries_the_chip()
    {
        var records = CompatRecords.Parse(Sample)!;
        var badge = CommunityCompat.Badge("TAOM", ModCompatDatabase.Empty, records: records, installedVersion: "v2.0.29");
        Assert.Equal("Works with settings", badge!.Tested!.Text);
        Assert.Null(CommunityCompat.Badge("TAOM", ModCompatDatabase.Empty)!.Tested);
    }

    [Fact]
    public void Default_settings_fill_in_under_the_host_values()
    {
        var records = CompatRecords.Parse(Sample)!.All;
        var host = new SettingsOverrides();
        host.Set("TAOM", new Dictionary<string, string> { ["EnableTroopWeight"] = "true", ["Other"] = "5" });
        var log = new List<string>();
        var merged = CompatRecordDefaults.WithDefaults(host, records, ["Native", "taom"], log);

        // The host's value wins; the others come from the record.
        Assert.Equal("true", merged.Get("TAOM", "EnableTroopWeight"));
        Assert.Equal("12", merged.Get("TAOM", "Count"));
        Assert.Equal("true", merged.Get("TAOM", "On"));
        Assert.Equal("5", merged.Get("TAOM", "Other"));
        // The host's own overrides are not changed.
        Assert.Null(host.Get("TAOM", "Count"));
        Assert.Equal(2, host.Count);
        Assert.Single(log);
        Assert.Contains("TAOM uses 2 default setting(s) from ModderLords by Ryvius and keeps 1 you set.", log[0]);
    }

    [Fact]
    public void Default_settings_only_for_mods_the_server_runs()
    {
        var records = CompatRecords.Parse(Sample)!.All;
        var log = new List<string>();
        var merged = CompatRecordDefaults.WithDefaults(new SettingsOverrides(), records, ["Native", "Coop"], log);
        Assert.True(merged.IsEmpty);
        Assert.Empty(log);
    }

    [Fact]
    public void The_AD1259_tool_never_takes_record_defaults_and_keeps_the_host_values()
    {
        var records = CompatRecords.Parse(Sample)!.All;
        var host = new SettingsOverrides();
        host.Set("TAOM", new Dictionary<string, string> { ["Other"] = "5" });

        // AD1259 is a pinned set: a downloaded record with defaults for a running mod changes nothing.
        var log = new List<string>();
        var pinned = CompatRecordDefaults.WithDefaults(host, records, ["Native", "taom"], log, pinnedSet: true);
        Assert.Equal("5", pinned.Get("TAOM", "Other"));
        Assert.Null(pinned.Get("TAOM", "Count"));
        Assert.Equal(1, pinned.Count);
        Assert.Empty(log);

        // The Coop build still takes them.
        Assert.Equal("12", CompatRecordDefaults.WithDefaults(host, records, ["Native", "taom"], new List<string>()).Get("TAOM", "Count"));
        Assert.Contains("lines, pinnedSet: Edition.IsAD1259);", SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ModSettings.cs")));
    }

    [Fact]
    public void Kept_copy_is_used_offline_and_a_damaged_one_gives_no_records()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coodsz-compat-records-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "compat-records.json");
            File.WriteAllText(path, Sample);
            Assert.Equal(5, CompatRecords.LoadCached(path).Count);
            File.WriteAllText(path, "{ broken");
            Assert.Equal(0, CompatRecords.LoadCached(path).Count);
            Assert.Equal(0, CompatRecords.LoadCached(Path.Combine(dir, "missing.json")).Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Fetch_keeps_a_copy_and_returns_null_when_offline()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coodsz-compat-records-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "compat-records.json");
            using var ok = new HttpClient(new Answer(Sample));
            Assert.Equal(5, (await CompatRecords.FetchAsync(ok, path, "https://example.invalid/db.json"))!.Count);
            Assert.True(File.Exists(path));
            using var refused = new HttpClient(new Answer("{\"SchemaVersion\": 9, \"Records\": []}"));
            Assert.Null(await CompatRecords.FetchAsync(refused, path, "https://example.invalid/db.json"));
            // The refused file did not replace the kept copy.
            Assert.Equal(5, CompatRecords.LoadCached(path).Count);
            using var offline = new HttpClient(new Answer(null));
            Assert.Null(await CompatRecords.FetchAsync(offline, path, "https://example.invalid/db.json"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private sealed class Answer(string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            body == null
                ? throw new HttpRequestException("offline")
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
