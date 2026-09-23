using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserDownloadNavigationTests
{
    [TestCase("GET", true)]
    [TestCase("POST", false)]
    [TestCase("HEAD", false)]
    [TestCase("PUT", false)]
    [TestCase("PATCH", false)]
    [TestCase("DELETE", false)]
    [TestCase("get", false)]
    [TestCase(null, false)]
    public void OnlyKnownCompleteGetResponsesAreEligible(string? method, bool expected)
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.False);
        navigation.RecordRequest(uri, method, true);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.EqualTo(expected));
    }

    [Test]
    public void PostRedirectToGetIsEligibleAtTheFinalUrl()
    {
        var form = new Uri("https://page.example/generate");
        var media = new Uri("https://files.example/download?filename=Morning.mp3");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(form, "POST", true);
        Assert.That(navigation.IsCompleteGetResponse(form, 200), Is.False);
        navigation.RecordRequest(media, "GET", true);
        Assert.That(navigation.IsCompleteGetResponse(media, 200), Is.True);
        Assert.That(navigation.IsCompleteGetResponse(form, 200), Is.False);
    }

    [TestCase("GET", "POST", true)]
    [TestCase("POST", "GET", false)]
    public void SubframeRequestsCannotReplaceTheMainFrameMethod(string mainMethod, string frameMethod, bool expected)
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, mainMethod, true);
        navigation.RecordRequest(uri, frameMethod, false);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.EqualTo(expected));
    }

    [Test]
    public void ANewPostOrUnknownRequestDoesNotReuseAPreviousGet()
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, "GET", true);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.True);
        navigation.RecordRequest(uri, "POST", true);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.False);
        navigation.RecordRequest(uri, "GET", true);
        navigation.RecordRequest(null, null, true);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.False);
    }

    [TestCase("https://files.example/download?token=other")]
    [TestCase("https://other.example/download")]
    public void UnrelatedResponsesCannotReuseTheGetRequest(string response)
    {
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(new Uri("https://files.example/download"), "GET", true);
        Assert.That(navigation.IsCompleteGetResponse(new Uri(response), 200), Is.False);
    }

    [TestCase("https://user:password@files.example/download")]
    [TestCase("file:///tmp/Morning.mp3")]
    public void UnsafeGetUrisAreNotEligible(string address)
    {
        var uri = new Uri(address);
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, "GET", true);
        Assert.That(navigation.IsCompleteGetResponse(uri, 200), Is.False);
    }

    [TestCase(200, true)]
    [TestCase(201, false)]
    [TestCase(204, false)]
    [TestCase(206, false)]
    [TestCase(299, false)]
    public void PartialOrNonstandardResponsesAreNotOfferedAsCompleteMedia(int status, bool expected)
    {
        var uri = new Uri("https://files.example/clip.mp3");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, "GET", true);
        Assert.That(navigation.IsCompleteGetResponse(uri, status), Is.EqualTo(expected));
    }
}
