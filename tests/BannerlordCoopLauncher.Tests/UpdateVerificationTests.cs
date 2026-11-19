using System.IO.Compression;
using System.Security.Cryptography;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

public class UpdateVerificationTests
{
    private const string Exe = "Coodsz Coop and Friends.exe";
    private const string Repo = ReleaseNotice.FriendsRepo;
    private const string Tag = "v1.2.74";
    private const string ZipName = "Coodsz-Coop-and-Friends-v1.2.74.zip";
    private const string RealUrl = "https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip";
    private static readonly string Digest0 = "sha256:" + new string('0', 64);

    [Fact]
    public void A_zip_matching_the_release_digest_passes_and_extracts()
    {
        WithZip(Exe, zipPath =>
        {
            ToolUpdate.VerifyDownload(zipPath, Digest(zipPath), Exe);
            ToolUpdate.VerifyDownload(zipPath, "sha256:" + Digest(zipPath)["sha256:".Length..].ToUpperInvariant(), Exe);

            var extracted = Path.Combine(Path.GetDirectoryName(zipPath)!, "files");
            ToolUpdate.VerifyDownload(zipPath, Digest(zipPath), Exe, extracted);
            Assert.True(File.Exists(Path.Combine(extracted, "Coodsz-Coop-and-Friends", Exe)));
        });
    }

    [Fact]
    public void A_zip_with_a_different_hash_is_refused()
    {
        WithZip(Exe, zipPath =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => ToolUpdate.VerifyDownload(zipPath, Digest0, Exe));
            Assert.Equal(ToolUpdate.DamagedDownloadMessage, error.Message);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256:")]
    [InlineData("sha256:abc")]
    [InlineData("md5:00000000000000000000000000000000")]
    public void A_release_without_a_usable_digest_is_refused(string? digest)
    {
        WithZip(Exe, zipPath =>
        {
            var extracted = Path.Combine(Path.GetDirectoryName(zipPath)!, "files");
            var error = Assert.Throws<InvalidOperationException>(() => ToolUpdate.VerifyDownload(zipPath, digest, Exe, extracted));
            Assert.Equal(ToolUpdate.DamagedDownloadMessage, error.Message);
            Assert.False(Directory.Exists(extracted));
        });
    }

    [Fact]
    public void A_zip_without_the_program_is_refused()
    {
        WithZip("readme.txt", zipPath =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => ToolUpdate.VerifyDownload(zipPath, Digest(zipPath), Exe));
            Assert.Equal("The update does not contain " + Exe + ".", error.Message);
        });
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_refused()
    {
        WithZip(Exe, zipPath =>
        {
            File.WriteAllText(zipPath, "not a zip");
            var error = Assert.Throws<InvalidOperationException>(() => ToolUpdate.VerifyDownload(zipPath, Digest(zipPath), Exe));
            Assert.Equal(ToolUpdate.DamagedDownloadMessage, error.Message);
        });
    }

    [Theory]
    [InlineData(RealUrl, true)]
    [InlineData("https://github.com/Someone/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz/Coodsz-TAOM/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz/..;/Someone/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz/%2e%2e%2fSomeone/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz/..%2FSomeone/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz//Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com//Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip\r\n", false)]
    [InlineData("https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/\nCoodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("http://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://github.com:8443/Coodsz/Coodsz-Coop-and-Friends/releases/download/v1.2.74/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("https://evil.example/Coodsz-Coop-and-Friends-v1.2.74.zip", false)]
    [InlineData("", false)]
    public void Only_the_exact_release_link_is_trusted(string url, bool trusted)
    {
        Assert.Equal(trusted, ToolUpdate.IsTrustedDownloadUrl(url, Repo, Tag, ZipName));
    }

    [Theory]
    [InlineData("Someone/Coodsz-Coop-and-Friends", Tag, ZipName)]
    [InlineData("Coodsz/../Someone", Tag, ZipName)]
    [InlineData(Repo, "../v1", ZipName)]
    [InlineData(Repo, "v1/../..", ZipName)]
    [InlineData(Repo, Tag, "../a.zip")]
    [InlineData(Repo, Tag, "..")]
    public void Odd_repo_tag_or_name_parts_are_never_trusted(string repo, string tag, string name)
    {
        var url = "https://github.com/" + repo + "/releases/download/" + tag + "/" + name;
        Assert.False(ToolUpdate.IsTrustedDownloadUrl(url, repo, tag, name));
    }

    [Theory]
    [InlineData(ZipName, true)]
    [InlineData("..\\outside.zip", false)]
    [InlineData("../outside.zip", false)]
    [InlineData("sub/Coodsz-Coop-and-Friends.zip", false)]
    [InlineData("C:Coodsz-Coop-and-Friends.zip", false)]
    [InlineData("Coodsz-Coop-and-Friends?.zip", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void Asset_names_must_be_plain_file_names(string name, bool safe)
    {
        Assert.Equal(safe, ToolUpdate.IsSafeAssetName(name));
    }

    [Fact]
    public async Task An_asset_name_that_leaves_the_stage_folder_stops_the_update()
    {
        var processId = Random.Shared.Next(1_000_000, 2_000_000);
        var name = "..\\Coodsz-Coop-and-Friends-v9.9.9.zip";
        var offer = new ReleaseOffer("", Tag, [new ReleaseAsset(name, "https://github.com/" + Repo + "/releases/download/" + Tag + "/" + name, Digest0)], Repo);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(offer, processId));

        Assert.Equal(ToolUpdate.UntrustedDownloadMessage, error.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "coodsz-update-" + processId)));
    }

    [Fact]
    public async Task A_foreign_download_link_stops_the_update_before_anything_is_downloaded()
    {
        var processId = Random.Shared.Next(1_000_000, 2_000_000);
        var offer = new ReleaseOffer("", Tag, [new ReleaseAsset(ZipName, "https://github.com/Coodsz/..;/Someone/" + ZipName, Digest0)], Repo);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Install(offer, processId));

        Assert.Equal(ToolUpdate.UntrustedDownloadMessage, error.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "coodsz-update-" + processId)));
    }

    [Fact]
    public async Task A_failed_download_removes_the_stage_folder()
    {
        // A cancelled token makes the download throw right after the stage folder is created, with no network call.
        var processId = Random.Shared.Next(1_000_000, 2_000_000);
        var offer = new ReleaseOffer("", Tag, [new ReleaseAsset(ZipName, "https://github.com/" + Repo + "/releases/download/" + Tag + "/" + ZipName, Digest0)], Repo);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Install(offer, processId, new CancellationToken(canceled: true)));

        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "coodsz-update-" + processId)));
    }

    [Fact]
    public void Release_offers_carry_the_repo_and_assets_carry_their_digest()
    {
        const string json = """
            {"assets":[
              {"name":"Coodsz-Coop-and-Friends-v1.2.74.zip","browser_download_url":"https://github.com/Coodsz/x/a.zip","digest":"sha256:3ff15cef3bb18aaadccd59de2a0c8ff5638af76f3120577dcd01250109c3df33"},
              {"name":"Old.zip","browser_download_url":"https://github.com/Coodsz/x/b.zip","digest":null},
              {"name":"Older.zip","browser_download_url":"https://github.com/Coodsz/x/c.zip"}
            ]}
            """;

        var assets = ReleaseNotice.ReadAssets(json);

        Assert.Equal("sha256:3ff15cef3bb18aaadccd59de2a0c8ff5638af76f3120577dcd01250109c3df33", assets[0].Digest);
        Assert.Null(assets[1].Digest);
        Assert.Null(assets[2].Digest);
        Assert.Equal(Repo, ReleaseNotice.Offer("v1.0.0", Tag, Repo, assets)?.Repo);
    }

    private static Task Install(ReleaseOffer offer, int processId, CancellationToken cancellationToken = default) =>
        ToolUpdate.InstallAsync(offer, Path.GetTempPath(), Path.Combine(Path.GetTempPath(), Exe), processId, null, cancellationToken);

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WithZip(string entryName, Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "update.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Coodsz-Coop-and-Friends/" + entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("program");
        }

        try
        {
            test(zipPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
