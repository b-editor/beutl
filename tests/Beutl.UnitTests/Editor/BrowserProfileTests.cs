using System.Text.Json;

using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserProfileTests
{
    [Test]
    public void Profile_RoundTripsSettingsBookmarksAndDownloads_AndClearsOnlyHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "profile.json");
        try
        {
            var profile = new BrowserProfile(path);
            Assert.That(profile.AddBookmark(new Uri("https://example.com/page"), "Example"), Is.True);
            profile.AddBookmark(new Uri("https://example.com/page"), "Updated");
            string media = Path.Combine(root, "clip.mp4");
            File.WriteAllText(media, "test");
            profile.AddDownload(new Uri("https://example.com/clip.mp4"), media);
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
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void CorruptProfile_IsPreservedBeforeRecovery_AndUnsafeBookmarksAreRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "profile.json");
        try
        {
            File.WriteAllText(path, "broken json");
            var profile = new BrowserProfile(path);
            Assert.That(profile.Error, Is.Not.Null);
            Assert.That(profile.AddBookmark(new Uri("https://user:password@example.com/"), "Unsafe"), Is.False);
            Assert.That(profile.AddBookmark(new Uri("about:blank"), "Blank"), Is.False);
            Assert.That(profile.AddBookmark(new Uri("https://example.com/"), "Safe"), Is.True);
            Assert.That(File.ReadAllText(Directory.GetFiles(root, "*.recovery-*").Single()), Is.EqualTo("broken json"));
            Assert.That(new BrowserProfile(path).Bookmarks, Has.Count.EqualTo(1));
        }
        finally { Directory.Delete(root, true); }
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
