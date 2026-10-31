using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WindowsAdBlockRequestTests
{
    [TestCase("document", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("image", false)]
    [TestCase("iframe", true)]
    [TestCase("frame", true)]
    public void SubdocumentRulesUseDestinationInsteadOfComparingNavigationUrls(string? destination, bool blocked)
    {
        var rules = BrowserAdBlockTestRules.Parse("||ads.example^$subdocument");
        var previousPage = new Uri("https://publisher.test/previous");
        var request = new Uri("https://ads.example/new-document");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, previousPage, 1, "cross-site", destination), Is.EqualTo(blocked));
    }

    [Test]
    public void FrameCanRequestTheSameUrlAsTheTopLevelPage()
    {
        var rules = BrowserAdBlockTestRules.Parse("||ads.example^$subdocument");
        var page = new Uri("https://ads.example/page");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, page, page, 1, "same-origin", "iframe"), Is.True);
    }

    [Test]
    public void NonDocumentRequestsAreNotTreatedAsChildFrames()
    {
        var rules = BrowserAdBlockTestRules.Parse("||ads.example^$subdocument");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, new Uri("https://ads.example/image.png"),
            new Uri("https://publisher.test/"), 3, "cross-site", "iframe"), Is.False);
    }

    [TestCase("same-origin", false)]
    [TestCase("same-site", false)]
    [TestCase("cross-site", true)]
    public void ResourceTypeAndThirdPartyMetadataStillApply(string site, bool blocked)
    {
        var rules = BrowserAdBlockTestRules.Parse("||ads.example^$script,third-party");
        var page = new Uri("https://publisher.test/");
        var request = new Uri("https://ads.example/script.js");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, page, 6, site, "script"), Is.EqualTo(blocked));
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, page, 3, site, "image"), Is.False);
    }

    // WebView2 names a page as it commits, NativeWebView only once it has loaded.
    [TestCase("https://next.test/article", "https://next.test/article")]
    [TestCase("about:blank", "https://opened.test/")]
    [TestCase(null, "https://opened.test/")]
    [TestCase("not an address", "https://opened.test/")]
    public void TheCommittedDocumentIsThePageWhenItHasAWebAddress(string? committed, string expected)
    {
        Assert.That(WindowsAdBlockBackend.ChoosePage(committed, () => new Uri("https://opened.test/")),
            Is.EqualTo(new Uri(expected)));
    }

    // A page reached through a link loads its resources before NativeWebView names it, and its own exceptions apply.
    [Test]
    public void ALoadingPagesRequestsMatchItsOwnDomainRules()
    {
        var rules = BrowserAdBlockTestRules.Parse("||cdn.example^\n@@||cdn.example^$domain=next.test");
        var request = new Uri("https://cdn.example/app.js");
        Uri page = WindowsAdBlockBackend.ChoosePage("https://next.test/article", () => new Uri("https://previous.test/"));
        Assert.Multiple(() =>
        {
            Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, page, 6, "cross-site", "script"), Is.False);
            Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, new Uri("https://previous.test/"), 6,
                "cross-site", "script"), Is.True);
        });
    }
}
