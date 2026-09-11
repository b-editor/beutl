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
    public void Enter_SearchesWordsAndNavigatesAddresses(string text, string expected)
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        vm.Address.Value = text;
        Assert.That(vm.TryCreateNavigationUri(out Uri uri), Is.True);
        Assert.That(uri.AbsoluteUri, Is.EqualTo(expected));
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
