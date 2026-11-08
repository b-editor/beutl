using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

// BEUTL_HOME points at a temporary directory for this assembly, so downloads land in its tmp folder.
[TestFixture]
public sealed class FFmpegInstallServiceDownloadTests
{
    private const string AssetName = "ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip";
    private const string DownloadUrl = "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/latest/" + AssetName;
    private const string ApiUrl = "https://api.github.com/repos/FFmpeg-Builds/FFmpeg-Builds/releases/assets/720740577";

    private readonly List<string> _downloaded = [];

    [TearDown]
    public void TearDown()
    {
        foreach (string file in _downloaded)
            File.Delete(file);
        _downloaded.Clear();
    }

    [Test]
    public void Asset_ReadsItsApiUrl()
    {
        GitHubAsset asset = JsonSerializer.Deserialize<GitHubAsset>(
            $$"""{"url":"{{ApiUrl}}","name":"{{AssetName}}","browser_download_url":"{{DownloadUrl}}"}""")!;

        Assert.That(asset.Url, Is.EqualTo(ApiUrl));
    }

    [Test]
    public async Task Download_UsesTheDownloadPageWhenItAnswers()
    {
        var handler = new RecordingHandler(_ => Archive("from the page"));

        string? file = await DownloadAsync(handler, Asset());

        Assert.That(File.ReadAllText(file!), Is.EqualTo("from the page"));
        Assert.That(handler.Requests.Select(r => r.Uri), Is.EqualTo(new[] { DownloadUrl }));
    }

    // GitHub's download pages answered 504 for every asset of this repository while its API served the same files.
    [Test]
    public async Task Download_FetchesTheAssetThroughTheApiWhenTheDownloadPageFails()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsoluteUri == DownloadUrl
            ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            : Archive("from the api"));

        string? file = await DownloadAsync(handler, Asset());

        Assert.That(Path.GetFileName(file), Is.EqualTo(AssetName));
        Assert.That(File.ReadAllText(file!), Is.EqualTo("from the api"));
        Assert.That(handler.Requests.Select(r => r.Uri), Is.EqualTo(new[] { DownloadUrl, ApiUrl }));
        Assert.That(handler.Requests[1].Accept, Is.EqualTo("application/octet-stream"));
        // The API refuses a request without a User-Agent.
        Assert.That(handler.Requests[1].UserAgent, Is.Not.Empty);
    }

    [Test]
    public async Task Download_FailsWhenTheApiFailsToo()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout));

        string? file = await DownloadAsync(handler, Asset());

        Assert.That(file, Is.Null);
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Download_FailsWithoutRetryingWhenTheAssetHasNoApiUrl()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout));

        string? file = await DownloadAsync(handler, Asset(apiUrl: null));

        Assert.That(file, Is.Null);
        Assert.That(handler.Requests.Select(r => r.Uri), Is.EqualTo(new[] { DownloadUrl }));
    }

    private static GitHubAsset Asset(string? apiUrl = ApiUrl)
        => new() { Name = AssetName, BrowserDownloadUrl = DownloadUrl, Url = apiUrl };

    private static HttpResponseMessage Archive(string content)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };

    private async Task<string?> DownloadAsync(HttpMessageHandler handler, GitHubAsset asset)
    {
        MethodInfo download = typeof(FFmpegInstallService).GetMethod(
            "DownloadFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var client = new HttpClient(handler);
        string? file = await (Task<string?>)download.Invoke(new FFmpegInstallService(), [client, asset, CancellationToken.None])!;
        if (file != null)
            _downloaded.Add(file);
        return file;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Uri, string Accept, string UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Accept.ToString(), request.Headers.UserAgent.ToString()));
            return Task.FromResult(respond(request));
        }
    }
}
