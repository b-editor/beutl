using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

// The update endpoint steers where the download goes and which file is installed, so the
// downloader trusts only HTTPS and a server-supplied name that cannot leave its folder.
[TestFixture, NonParallelizable]
public sealed class UpdateDownloadTests
{
    private const string Url = "https://downloads.example.test/update.zip";
    private string _root = null!;

    [SetUp]
    public void SetUp() =>
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "beutl-update-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private static UpdateDialogViewModel ViewModel(string url, string? sha256 = null) => new(new AppUpdateResponse
    {
        LatestVersion = "2.0.0",
        Url = null,
        DownloadUrl = url,
        Sha256 = sha256,
        IsLatest = false,
        MustLatest = false,
    }, false);

    private static HttpResponseMessage Response(params byte[] content) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content),
    };

    [Test]
    public async Task HttpDownloadUrlIsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel("http://downloads.example.test/update.zip");
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(viewModel.ProgressText.Value, Is.EqualTo(MessageStrings.DownloadFailed));
        });
    }

    [Test]
    public async Task RedirectToHttpIsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://downloads.example.test/update.zip");
        response.RequestMessage = request;
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.Null);
        Assert.That(File.Exists(path), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContentDispositionPathIsReducedToItsFileName(bool windowsSeparators)
    {
        string escaped = $"evil-{Guid.NewGuid():N}.exe";
        string fileName = windowsSeparators ? $@"..\..\{escaped}" : $"../../{escaped}";
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        string expected = Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", escaped);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(expected), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(File.Exists(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "..", escaped)), Is.False);
        });
    }

    [Test]
    public async Task ContentDispositionNameWithDirectoriesKeepsOnlyTheFileName()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "nested/dir/update.zip" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "update.zip")));
    }

    [Test]
    public async Task DownloadCannotOverwriteTheUpdateScript()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "update.sh" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "update.sh")));
        Assert.That(result, Is.Not.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh")));
    }

    [Test]
    public async Task ContentDispositionNameCannotCarryShellSyntax()
    {
        using var response = Response(1, 2, 3, 4);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "$(touch pwned).deb" };
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url);

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "__touch_pwned_.deb")));
    }

    [Test]
    public async Task UrlNameIsUsedWithoutItsQueryWhenTheServerGivesNoName()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel("https://downloads.example.test/releases/Beutl-2.0.zip?token=opaque");

        string? result = await viewModel.DownloadFile(null, client);

        Assert.That(result, Is.EqualTo(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", "Beutl-2.0.zip")));
        Assert.That(File.Exists(result), Is.True);
    }

    [Test]
    public async Task ContentThatDoesNotMatchTheAdvertisedSha256IsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, new string('0', 64));
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(Directory.GetFiles(_root), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContentMatchingTheAdvertisedSha256IsAccepted(bool prefixed)
    {
        byte[] content = [1, 2, 3, 4];
        string hash = Convert.ToHexString(SHA256.HashData(content));
        using var response = Response(content);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, prefixed ? "sha256:" + hash : hash);
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.EqualTo(path));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(content));
    }

    [Test]
    public async Task MalformedSha256IsRejected()
    {
        using var response = Response(1, 2, 3, 4);
        using var client = new HttpClient(new Handler(response));
        var viewModel = ViewModel(Url, "not-a-hash");
        string path = Path.Combine(_root, "update.zip");

        string? result = await viewModel.DownloadFile(path, client);

        Assert.That(result, Is.Null);
        Assert.That(File.Exists(path), Is.False);
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(response);
    }
}
