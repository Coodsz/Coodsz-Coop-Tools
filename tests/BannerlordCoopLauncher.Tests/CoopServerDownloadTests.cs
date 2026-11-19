using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Coop's v0.1.7 GitHub release has only BannerlordCoop-v0.1.7.zip and manifest.json. The 4.5 GB server .7z is on
// Coop's file host, linked from the release page, so the server texts name that download and Download Coop server
// opens the link manifest.json gives, or the release page for anything it does not trust.
public sealed class CoopServerDownloadTests
{
    private const string R2 = "https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z";

    // The v0.1.7 manifest.json as published on 2026-11-01.
    private const string Manifest = """
        {
          "tag": "v0.1.7",
          "client_sha": "ecd4c479b0c35348c13c3ff4710cea87f799aa40",
          "server_sha": "8780cd95aab85697236ca86381addcaa720d5b95",
          "windows": {
            "name": "BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z",
            "bytes": 4525962186,
            "sha256": "c5818885b229cbbd6e2fb8d7c8464efaaa34da9bdac090bfb9066e60cf0cb8fd",
            "url": "https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z"
          },
          "client": {
            "name": "BannerlordCoop-v0.1.7.zip",
            "bytes": 12790228,
            "url": "https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7/BannerlordCoop-v0.1.7.zip"
          }
        }
        """;

    private static string WithUrl(string url) => "{\"windows\":{\"name\":\"x.7z\",\"bytes\":1,\"url\":\"" + url + "\"}}";

    [Fact]
    public void The_manifest_gives_the_windows_name_size_and_link()
    {
        var package = CoopServerDownload.Parse(Manifest);
        Assert.NotNull(package);
        Assert.Equal("BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z", package!.Name);
        Assert.Equal(4525962186L, package.Bytes);
        Assert.Equal(R2, package.Url);
        Assert.Equal(R2, CoopServerDownload.LinkFor(Manifest));
    }

    [Fact]
    public void A_server_7z_on_github_is_trusted_too()
    {
        const string github = "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/BannerlordCoop-DedicatedServer-Win64-v0.1.8.7z";
        Assert.Equal(github, CoopServerDownload.LinkFor(WithUrl(github)));
    }

    // The accepted shape: the host in any case comes back lower case, and a query string stays.
    [Theory]
    [InlineData("https://GITHUB.COM/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z", "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z")]
    [InlineData("https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z?raw=1", "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z?raw=1")]
    [InlineData("https://PUB-C80EFA34191141FD803F8508E025F726.R2.DEV/release/v0.1.8/x.7z", "https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.8/x.7z")]
    public void A_trusted_link_opens_in_its_normal_form(string url, string opened)
    {
        Assert.Equal(opened, CoopServerDownload.Trusted(url));
        Assert.Equal(opened, CoopServerDownload.LinkFor(WithUrl(url)));
    }

    [Theory]
    [InlineData("http://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z")]
    [InlineData("https://evil.example/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z")]
    [InlineData("https://github.com.evil.example/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z")]
    [InlineData("https://pub-c80efa34191141fd803f8508e025f726.r2.dev.evil.example/server.7z")]
    [InlineData("https://other-bucket.r2.dev/release/v0.1.7/BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z")]
    [InlineData("https://github.com@evil.example/server.7z")]
    [InlineData("https://user@github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/server.7z")]
    [InlineData("https://github.com:8443/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/server.7z")]
    [InlineData("https://github.com/evil.exe?x=.7z")]
    [InlineData("https://github.com/evil.exe#.7z")]
    [InlineData("https://github.com/a.7z%00.exe")]
    [InlineData("https://github.com/a.7z/../evil.exe")]
    [InlineData("https://github.com./a.7z")]
    [InlineData("https://github.com./Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z")]
    [InlineData("https://g\u0456thub.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z")]
    [InlineData("https://github.com/someone/BannerlordCoop/releases/download/v0.1.8/x.7z")]
    [InlineData("https://github.com/Bannerlord-Coop-Team/BannerlordCoop/blob/main/x.7z")]
    [InlineData("https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/..%2f..%2f..%2fevil%2fx.7z")]
    [InlineData("https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.8/x.7z%00.exe")]
    [InlineData("https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7%2f..%2f..%2fother.7z")]
    [InlineData("https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.7/setup.exe")]
    [InlineData("https://github.com/server.7z.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)//.7z")]
    [InlineData("C:\\\\CoopServer\\\\server.7z")]
    [InlineData("")]
    public void A_link_it_does_not_trust_opens_the_release_page(string url)
    {
        Assert.Null(CoopServerDownload.Trusted(url));
        Assert.Equal(CoopInstall.ReleaseUrl, CoopServerDownload.LinkFor(WithUrl(url)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"windows\":\"x\"}")]
    [InlineData("{\"windows\":{\"name\":\"x.7z\",\"bytes\":1}}")]
    [InlineData("{\"windows\":{\"url\":42}}")]
    public void A_manifest_it_can_not_read_opens_the_release_page(string? json)
    {
        Assert.Null(CoopServerDownload.Parse(json));
        Assert.Equal(CoopInstall.ReleaseUrl, CoopServerDownload.LinkFor(json));
    }

    // Development said "from Coop's GitHub release" for the server .7z, which is not on that release page.
    [Fact]
    public void The_server_texts_name_the_download_linked_from_coops_release()
    {
        string[] lines =
        [
            CoopInstall.ServerGetLine("Set server folder"),
            CoopInstall.ServerBehindLine("v0.1.7", "v0.1.6")!
        ];
        foreach (var line in lines)
        {
            Assert.DoesNotContain("GitHub release", line);
            Assert.Contains("linked at the bottom of Coop's release notes, not under Assets", line);
            Assert.Contains(CoopServerDownload.Button, line);
        }

        // With Coop 0.1.7 on the Workshop the missing server message points at the Workshop folder again, not the download.
        Assert.DoesNotContain("GitHub release", DedicatedServerFinder.MissingMessage);
        Assert.DoesNotContain(CoopServerDownload.Button, DedicatedServerFinder.MissingMessage);
        Assert.Contains("Workshop folder 3770450698\\DedicatedServer", DedicatedServerFinder.MissingMessage);
        // The Coop and AD1259 Server Tools keep the button in the collapsed Advanced: server folder group, which opens
        // with these lines (SetControl, SendsToServerFolder).
        Assert.Contains("Download Coop server under Advanced: server folder on the Settings page", CoopInstall.ServerGetLine("Set server folder"));
        var control = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        var setControl = SourceFiles.Method(control, "private void SetControl(string message, bool done = false, bool failed = false)");
        Assert.Contains("if (SendsToServerFolder(message))", setControl);
        Assert.Contains("ServerFolderPanel.Visibility = Visibility.Visible;", setControl);
        foreach (var project in new[] { "CoodszHost", "BurningEmpiresHost" })
        {
            var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
            Assert.DoesNotContain("GitHub release", xaml);
            Assert.Contains("Content=\"Download Coop server\" ", xaml);
            Assert.Contains("Click=\"DownloadCoopServer_Click\"", xaml);
        }
    }

    [Fact]
    public void The_new_lines_are_in_all_six_languages()
    {
        string[] lines =
        [
            CoopServerDownload.Button,
            CoopServerDownload.OpenedReleaseLine,
            CoopServerDownload.LookingUpLine,
            "Your browser is downloading the Coop dedicated server (about 4.5 GB). Unzip it to a short folder like C:\\CoopServer, then click {0}.",
            "Could not open your browser. Open {0} yourself.",
            "Opens the Coop dedicated server download (about 4.5 GB) in your browser. It is linked at the bottom of Coop's release notes, not under Assets.",
            "The server runs Coop {0}, older than Coop {1} in the game's Modules folder. Click Download Coop server for the matching Coop dedicated server download, linked at the bottom of Coop's release notes, not under Assets. Unzip it to a short folder, then click Set server folder.",
            DedicatedServerFinder.MissingMessage
        ];
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = Translator.Entries(language);
            foreach (var line in lines)
                Assert.True(table.ContainsKey(line), language + " is missing: " + line);
        }
    }
}
