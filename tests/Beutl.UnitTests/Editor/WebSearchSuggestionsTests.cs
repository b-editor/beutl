using System.Net;
using System.Net.Http;
using System.Text;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Extensibility;

using Moq;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class WebSearchSuggestionsTests
{
    [Test]
    public async Task Request_EncodesQueryAndParsesGoogleSuggestions()
    {
        var handler = new SuggestionHandler();
        using var client = new HttpClient(handler);
        var service = new WebSearchSuggestions(client);
        IReadOnlyList<string> result = await service.GetSuggestionsAsync("動画 編集", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestUri!.Host, Is.EqualTo("suggestqueries.google.com"));
            Assert.That(handler.RequestUri.Query, Does.Contain(Uri.EscapeDataString("動画 編集")));
            Assert.That(result, Is.EqualTo(new[] { "動画 編集 ソフト", "動画 編集 方法" }));
        });
    }

    [TestCase("https://user:password@example.com/private?q=secret")]
    [TestCase("example.com/path")]
    [TestCase("person@example.com")]
    [TestCase("localhost")]
    [TestCase("[::1]")]
    [TestCase("[::1]:5000")]
    [TestCase("[2001:db8::1]")]
    [TestCase("")]
    public async Task Addresses_DoNotSendSuggestionRequests(string text)
    {
        var handler = new SuggestionHandler();
        using var client = new HttpClient(handler);
        Assert.That(await new WebSearchSuggestions(client).GetSuggestionsAsync(text, CancellationToken.None), Is.Empty);
        Assert.That(handler.RequestUri, Is.Null);
    }

    [Test]
    public void Parser_IgnoresMalformedEntriesAndDuplicates()
    {
        Assert.That(WebSearchSuggestions.ParseResponse("[\"q\",[\"one\",null,42,\"\",\"one\",\"two\"]]"),
            Is.EqualTo(new[] { "one", "two" }));
        Assert.That(WebSearchSuggestions.ParseResponse("{}"), Is.Empty);
    }

    [TestCase("動画 編集", "https://www.google.com/search?q=%E5%8B%95%E7%94%BB%20%E7%B7%A8%E9%9B%86")]
    [TestCase("avalonia", "https://www.google.com/search?q=avalonia")]
    [TestCase("C#", "https://www.google.com/search?q=C%23")]
    [TestCase("example.com/path", "https://example.com/path")]
    [TestCase("[::1]", "https://[::1]/")]
    [TestCase("[::1]:5000", "https://[::1]:5000/")]
    [TestCase("[2001:db8::1]", "https://[2001:db8::1]/")]
    [TestCase("[::1]:5000/path", "https://[::1]:5000/path")]
    [TestCase("[topic]", "https://www.google.com/search?q=%5Btopic%5D")]
    public void Enter_SearchesWordsAndNavigatesAddresses(string text, string expected)
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        vm.Address.Value = text;
        Assert.That(vm.TryCreateNavigationUri(out Uri uri), Is.True);
        Assert.That(uri.AbsoluteUri, Is.EqualTo(expected));
    }

    [TestCase(0, "www.google.com")]
    [TestCase(1, "www.bing.com")]
    public void LongQuestionsRemainSearches(int engineIndex, string expectedHost)
    {
        string text = string.Join(' ', Enumerable.Repeat("Explain this lengthy error message", 12));
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(directory, "profile.json"));
        try
        {
            Assert.That(profile.UpdateSettings((BrowserSearchEngine)engineIndex, true, true), Is.True);
            using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, WebBrowserTabViewModel.BlankPage, profile);
            vm.Address.Value = text;
            Assert.That(vm.TryCreateNavigationUri(out Uri uri), Is.True);
            Assert.That(uri.Host, Is.EqualTo(expectedHost));
            Assert.That(Uri.UnescapeDataString(uri.Query[3..]), Is.EqualTo(text));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task LongSearchesDoNotRequestRemoteSuggestions()
    {
        var handler = new SuggestionHandler();
        using var client = new HttpClient(handler);
        string text = string.Join(' ', Enumerable.Repeat("long question", 30));
        Assert.That(await new WebSearchSuggestions(client).GetSuggestionsAsync(text, CancellationToken.None), Is.Empty);
        Assert.That(handler.RequestUri, Is.Null);
    }

    [Test]
    public void VeryLongQuestionsRemainSearches()
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        vm.Address.Value = new string('x', 100000) + " question";
        Assert.That(vm.TryCreateNavigationUri(out Uri uri), Is.True);
        Assert.That(uri.Host, Is.EqualTo("www.google.com"));
        Assert.That(Uri.UnescapeDataString(uri.Query[3..]), Is.EqualTo(vm.Address.Value));
    }

    [TestCase(0, "person@example.com", "www.google.com")]
    [TestCase(1, "person+tag@example.com", "www.bing.com")]
    public void SubmittedEmailLikeTextIsSearched(int engineIndex, string text, string expectedHost)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(directory, "profile.json"));
        try
        {
            profile.UpdateSettings((BrowserSearchEngine)engineIndex, true, true);
            using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, WebBrowserTabViewModel.BlankPage, profile);
            vm.Address.Value = text;
            Assert.That(vm.TryCreateNavigationUri(out Uri uri), Is.True);
            Assert.That(uri.Host, Is.EqualTo(expectedHost));
            Assert.That(Uri.UnescapeDataString(uri.Query[3..]), Is.EqualTo(text));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase("https://user:password@example.com/")]
    [TestCase("http://user@example.com/")]
    [TestCase("user:password@example.com")]
    public void CredentialUrlsAreNotConvertedToSearches(string text)
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        vm.Address.Value = text;
        Assert.That(vm.TryCreateNavigationUri(out _), Is.False);
    }

    private sealed class SuggestionHandler : HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[\"動画 編集\",[\"動画 編集 ソフト\",\"動画 編集 方法\"],[]]", Encoding.UTF8)
            });
        }
    }
}
