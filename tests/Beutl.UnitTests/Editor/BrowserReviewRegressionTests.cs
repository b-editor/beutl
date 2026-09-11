using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Extensibility;

using Moq;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserReviewRegressionTests
{
    [TestCase("http://user:password@example.com/")]
    [TestCase("https://user:password@example.com/")]
    [TestCase("https://:password@example.com/")]
    public void CredentialsAreRejectedAtAddressEntry(string address)
    {
        Assert.That(WebBrowserTabViewModel.TryNormalizeAddress(address, out _), Is.False);
    }

    [TestCase("constructor")]
    [TestCase("begin")]
    [TestCase("complete")]
    [TestCase("restore")]
    public void CredentialsNeverEnterPersistedTabState(string path)
    {
        var secret = new Uri("https://user:password@example.com/page");
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object,
            path == "constructor" ? secret : WebBrowserTabViewModel.BlankPage);
        if (path == "begin") vm.BeginNavigation(secret);
        if (path == "complete") vm.CompleteNavigation(secret, true, false, false);
        if (path == "restore") vm.ReadFromJson(new JsonObject { ["source"] = secret.AbsoluteUri });
        var saved = new JsonObject();
        vm.WriteToJson(saved);
        Assert.That(vm.CurrentUri.UserInfo, Is.Empty);
        Assert.That(saved.ToJsonString(), Does.Not.Contain("password").And.Not.Contain("user:"));
    }

    [TestCase(null, "utf8")]
    [TestCase("application/octet-stream", "utf8")]
    [TestCase("application/octet-stream", "utf16")]
    [TestCase("video/mp4", "utf8")]
    [TestCase("audio/mpeg", "utf8")]
    [TestCase("image/png", "utf16")]
    public void HtmlResponsesAreRejectedBeforePublishingAFile(string? mediaType, string encoding)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Encoding codec = encoding == "utf16" ? Encoding.Unicode : new UTF8Encoding(true);
        byte[] body = [.. codec.GetPreamble(), .. codec.GetBytes(" \r\n<!-- login -->\n<!DOCTYPE html><html>Login</html>")];
        using var client = new HttpClient(new BodyHandler(body, mediaType));
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://example.com/movie.mp4"), root, null, null, default));
            Assert.That(Directory.Exists(root) ? Directory.GetFiles(root) : [], Is.Empty);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestCase("add-bookmark")]
    [TestCase("replace-bookmark")]
    [TestCase("remove-bookmark")]
    [TestCase("add-download")]
    [TestCase("remove-download")]
    [TestCase("settings")]
    [TestCase("clear")]
    public void FailedProfileWritesDoNotPublishMutations(string operation)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "profile.json");
        try
        {
            var profile = new BrowserProfile(file);
            profile.AddBookmark(new Uri("https://example.com/"), "Original");
            profile.AddDownload(new Uri("https://example.com/media.mp4"), Path.Combine(root, "media.mp4"));
            var bookmarks = profile.Bookmarks.ToArray();
            var downloads = profile.Downloads.ToArray();
            int notifications = 0;
            profile.Bookmarks.CollectionChanged += (_, _) => notifications++;
            profile.Downloads.CollectionChanged += (_, _) => notifications++;
            profile.SettingsChanged += () => notifications++;
            profile.HistoryCleared += () => notifications++;
            File.Delete(file);
            Directory.CreateDirectory(file);
            bool saved = operation switch
            {
                "add-bookmark" => profile.AddBookmark(new Uri("https://other.example/"), "New"),
                "replace-bookmark" => profile.AddBookmark(new Uri("https://example.com/"), "Replacement"),
                "remove-bookmark" => profile.RemoveBookmark(bookmarks[0]),
                "add-download" => profile.AddDownload(new Uri("https://example.com/other.mp4"), Path.Combine(root, "other.mp4")),
                "remove-download" => profile.RemoveDownload(downloads[0]),
                "settings" => profile.UpdateSettings(BrowserSearchEngine.Bing, false, false),
                _ => profile.ClearHistory()
            };
            Assert.That(saved, Is.False);
            Assert.That(profile.Error, Is.Not.Empty);
            Assert.That(profile.Bookmarks, Is.EqualTo(bookmarks));
            Assert.That(profile.Downloads, Is.EqualTo(downloads));
            Assert.That(profile.Engine, Is.EqualTo(BrowserSearchEngine.Google));
            Assert.That(profile.SuggestionsEnabled, Is.True);
            Assert.That(profile.RecordDownloads, Is.True);
            Assert.That(notifications, Is.Zero);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void ReferrerMigrationRemovesSensitiveValuesFromDisk()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "profile.json");
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                Version = 1,
                Downloads = new[] { new BrowserDownloadRecord("https://example.com/a.mp3", Path.Combine(root, "a.mp3"), DateTimeOffset.UtcNow)
                { Referrer = "https://source.example/private?secret=old-value#fragment" } }
            }));
            var profile = new BrowserProfile(file);
            Assert.That(profile.Downloads.Single().Referrer, Is.EqualTo("https://source.example/"));
            Assert.That(File.ReadAllText(file), Does.Not.Contain("old-value").And.Not.Contain("/private"));
            Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void FailedReferrerMigrationReportsTheErrorAndKeepsRuntimeDataSanitized()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "profile.json");
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                Version = 1,
                Downloads = new[] { new BrowserDownloadRecord("https://example.com/a.mp3", Path.Combine(root, "a.mp3"), DateTimeOffset.UtcNow)
                { Referrer = "https://source.example/private?secret=old-value" } }
            }));
            var profile = new BrowserProfile(file, _ => throw new IOException("write denied"));
            Assert.That(profile.Error, Does.Contain("write denied"));
            Assert.That(profile.Downloads.Single().Referrer, Is.EqualTo("https://source.example/"));
            Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("binary")]
    [TestCase("svg")]
    public async Task InspectingAmbiguousMediaPreservesEveryByte(string kind)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        byte[] body = kind == "svg"
            ? Encoding.UTF8.GetBytes("\uFEFF<?xml version='1.0'?><!-- media --><svg xmlns='http://www.w3.org/2000/svg'/>")
            : Enumerable.Range(0, 9000).Select(value => (byte)value).ToArray();
        using var client = new HttpClient(new BodyHandler(body, "application/octet-stream"));
        try
        {
            string file = await new BrowserMediaDownload(client).DownloadAsync(
                new Uri(kind == "svg" ? "https://example.com/image.svg" : "https://example.com/movie.mp4"), root, null, null, default);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(body));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class BodyHandler(byte[] body, string? mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            if (mediaType != null) content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
