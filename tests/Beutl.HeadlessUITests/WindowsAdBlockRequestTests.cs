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
        var rules = BrowserAdBlockRules.Parse("||ads.example^$subdocument");
        var previousPage = new Uri("https://publisher.test/previous");
        var request = new Uri("https://ads.example/new-document");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, previousPage, 1, "cross-site", destination), Is.EqualTo(blocked));
    }

    [Test]
    public void FrameCanRequestTheSameUrlAsTheTopLevelPage()
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example^$subdocument");
        var page = new Uri("https://ads.example/page");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, page, page, 1, "same-origin", "iframe"), Is.True);
    }

    [Test]
    public void NonDocumentRequestsAreNotTreatedAsChildFrames()
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example^$subdocument");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, new Uri("https://ads.example/image.png"),
            new Uri("https://publisher.test/"), 3, "cross-site", "iframe"), Is.False);
    }

    [TestCase("same-origin", false)]
    [TestCase("same-site", false)]
    [TestCase("cross-site", true)]
    public void ResourceTypeAndThirdPartyMetadataStillApply(string site, bool blocked)
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example^$script,third-party");
        var page = new Uri("https://publisher.test/");
        var request = new Uri("https://ads.example/script.js");
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, page, 6, site, "script"), Is.EqualTo(blocked));
        Assert.That(WindowsAdBlockBackend.ShouldBlockRequest(rules, request, page, 3, site, "image"), Is.False);
    }
}
