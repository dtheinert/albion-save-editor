using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;

namespace AlbionSaveEditor.Tests;

/// <summary>EditorHost as used by the desktop app: real Kestrel on 127.0.0.1:0 with the embedded UI.</summary>
public class HostTests
{
    private static string WwwRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "AlbionSaveEditor", "wwwroot"));

    [Fact]
    public async Task DesktopHost_ServesEmbeddedUiAndApiOnRandomPort()
    {
        var emptyRoot = Directory.CreateTempSubdirectory("albion-host-").FullName; // no wwwroot on disk
        await using var app = EditorHost.Build(["--Albion:AutoDetect=false", "--Albion:GamePath="], new EditorHostOptions
        {
            Port = 0,
            EmbeddedAssetsOnly = true,
            ContentRootPath = emptyRoot,
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var url = app.Urls.Single();
            Assert.StartsWith("http://127.0.0.1:", url);
            Assert.NotEqual(0, new Uri(url).Port); // the actual port is read back after start
            using var http = new HttpClient { BaseAddress = new Uri(url) };

            var index = await http.GetByteArrayAsync("/", TestContext.Current.CancellationToken);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(WwwRoot(), "index.html"), TestContext.Current.CancellationToken), index);
            var appJs = await http.GetAsync("/app.js", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, appJs.StatusCode);
            Assert.Equal(new FileInfo(Path.Combine(WwwRoot(), "app.js")).Length, (await appJs.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);

            var meta = (await http.GetFromJsonAsync<JsonObject>("/api/meta", TestContext.Current.CancellationToken))!;
            Assert.False((bool)meta["gameDataConfigured"]!);

            var about = (await http.GetFromJsonAsync<JsonObject>("/api/about", TestContext.Current.CancellationToken))!;
            Assert.Equal("1.0.0", (string?)about["version"]);
            var credits = about["credits"]!.AsArray();
            Assert.Equal("AlbLib", (string?)credits[0]!["name"]);
            Assert.Contains("MIT License", (string?)credits[0]!["licenseText"]);
            Assert.Contains("IllidanS4", (string?)credits[0]!["licenseText"]);
            Assert.Equal("UAlbion", (string?)credits[1]!["name"]);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            Directory.Delete(emptyRoot, true);
        }
    }

    [Fact]
    public async Task About_IsAvailableInWebProject()
    {
        using var factory = new EditorFactory();
        var about = (await factory.CreateClient().GetFromJsonAsync<JsonObject>("/api/about", TestContext.Current.CancellationToken))!;
        Assert.Equal("Albion Save Editor", (string?)about["name"]);
    }
}
