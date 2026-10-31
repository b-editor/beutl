using System.Text.Json;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserProfileTests
{
    [TestCase("https://www.example.com/path?q=hello", "www.example.com", "E")]
    [TestCase("https://docs.beutl.com/reference", "docs.beutl.com", "D")]
    [TestCase("http://localhost:8080/", "localhost", "L")]
    [TestCase("https://www./", "www.", "W")]
    public void Bookmark_DisplayUsesTheHostWithoutChangingItsSavedAddress(string url, string host, string initial)
    {
        var bookmark = new BrowserBookmark(url, "Example");
        Assert.That(bookmark.Host, Is.EqualTo(host));
        Assert.That(bookmark.Initial, Is.EqualTo(initial));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(bookmark));
        Assert.That(json.RootElement.GetProperty("Url").GetString(), Is.EqualTo(url));
        Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "Url", "Title" }));
    }

    [Test]
    public void GeneratedDownloadsKeepTheirPageAndStayMarkedAcrossRestarts()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Path;
        string path = Path.Combine(root, "profile.json");
        var profile = new BrowserProfile(path);
        var page = new Uri("https://github.com/b-editor/beutl/blob/main/README.md");
        profile.AddDownload(new Uri("https://example.com/clip.mp4"), Path.Combine(root, "clip.mp4"));
        profile.AddDownload(page, Path.Combine(root, "README.md"), page, BrowserReferrerPolicy.NoReferrer, generated: true);

        var restored = new BrowserProfile(path);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Downloads.Select(record => (record.Url, record.Generated)), Is.EqualTo(new[]
            {
                (page.AbsoluteUri, true),
                ("https://example.com/clip.mp4", false)
            }));
            // Records saved before the flag existed, and every other download, omit it.
            Assert.That(File.ReadAllText(path), Does.Contain("\"Generated\":true").And.Not.Contain("\"Generated\":false"));
        });
    }

    [Test]
    public void Profile_RoundTripsSettingsBookmarksAndDownloads_AndClearsOnlyHistory()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Path;
        string path = Path.Combine(root, "profile.json");
        var profile = new BrowserProfile(path);
        Assert.That(profile.AddBookmark(new Uri("https://example.com/page"), "Example"), Is.True);
        profile.AddBookmark(new Uri("https://example.com/page"), "Updated");
        string media = Path.Combine(root, "clip.mp4");
        File.WriteAllText(media, "test");
        profile.AddDownload(new Uri("https://example.com/clip.mp4"), media, new Uri("https://referrer.example/page?token=referrer-secret#part"));
        profile.UpdateSettings(BrowserSearchEngine.Bing, false, false);
        profile.AddDownload(new Uri("https://example.com/ignored.mp4"), media);
        var restored = new BrowserProfile(path);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Engine, Is.EqualTo(BrowserSearchEngine.Bing));
            Assert.That(restored.SuggestionsEnabled, Is.False);
            Assert.That(restored.RecordDownloads, Is.False);
            Assert.That(restored.Bookmarks.Single().Title, Is.EqualTo("Updated"));
            Assert.That(restored.Downloads, Has.Count.EqualTo(1));
            Assert.That(restored.Downloads.Single().Referrer, Is.EqualTo("https://referrer.example/"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("referrer-secret"));
        });
        bool cleared = false;
        restored.HistoryCleared += () => cleared = true;
        restored.ClearHistory();
        Assert.Multiple(() =>
        {
            Assert.That(cleared, Is.True);
            Assert.That(new BrowserProfile(path).Downloads, Is.Empty);
            Assert.That(new BrowserProfile(path).Bookmarks, Has.Count.EqualTo(1));
            Assert.That(File.Exists(media), Is.True);
        });
    }

    [TestCase(null, null)]
    [TestCase("file:///private/file", null)]
    [TestCase("https://user:secret@example.com/", null)]
    [TestCase("https://referrer.example/page?private=value", "https://referrer.example/")]
    public void DownloadHistoryAcceptsOldRecordsAndNormalizesSavedReferrers(string? referrer, string? expected)
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.Path;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profile.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 1,
            Downloads = new[] { new BrowserDownloadRecord("https://example.com/clip.mp4", Path.Combine(directory, "clip.mp4"), DateTimeOffset.UtcNow) { Referrer = referrer } }
        }));
        var profile = new BrowserProfile(path);
        Assert.That(profile.Downloads, Has.Count.EqualTo(1));
        Assert.That(profile.Downloads.Single().Referrer, Is.EqualTo(expected));
    }

    [Test]
    public void CorruptProfile_IsPreservedBeforeRecovery_AndUnsafeBookmarksAreRejected()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Path;
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "profile.json");
        File.WriteAllText(path, "broken json");
        var profile = new BrowserProfile(path);
        Assert.That(profile.Error, Is.Not.Null);
        Assert.That(profile.AddBookmark(new Uri("https://user:password@example.com/"), "Unsafe"), Is.False);
        Assert.That(profile.AddBookmark(new Uri("about:blank"), "Blank"), Is.False);
        Assert.That(profile.AddBookmark(new Uri("https://example.com/"), "Safe"), Is.True);
        Assert.That(File.ReadAllText(Directory.GetFiles(root, "*.recovery-*").Single()), Is.EqualTo("broken json"));
        Assert.That(new BrowserProfile(path).Bookmarks, Has.Count.EqualTo(1));
    }

    [TestCase("{\"Index\":2,\"Count\":4}")]
    [TestCase("\"{\\\"Index\\\":2,\\\"Count\\\":4}\"")]
    public void FindResults_AcceptWebKitAndWebView2Encodings(string result)
    {
        Assert.That(BrowserPageTools.ParseFindResult(result), Is.EqualTo(new BrowserPageTools.FindResult(2, 4)));
    }

    [Test]
    public void SearchEngine_EncodesTermsForChosenProvider()
    {
        Assert.That(WebSearchSuggestions.CreateSearchUri("C# & 動画", BrowserSearchEngine.Bing).AbsoluteUri,
            Is.EqualTo("https://www.bing.com/search?q=C%23%20%26%20%E5%8B%95%E7%94%BB"));
    }
}
