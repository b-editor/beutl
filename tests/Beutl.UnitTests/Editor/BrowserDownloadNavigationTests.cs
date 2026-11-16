using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserDownloadNavigationTests
{
    // A form can only GET or POST. WebKit hands the received response to a download either way, so neither is sent
    // again; any other or unknown method is left to WebKit.
    [TestCase("GET", "GET")]
    [TestCase("POST", "POST")]
    [TestCase("HEAD", null)]
    [TestCase("PUT", null)]
    [TestCase("PATCH", null)]
    [TestCase("DELETE", null)]
    [TestCase("get", null)]
    [TestCase("post", null)]
    [TestCase(null, null)]
    public void OnlyCompleteGetAndPostResponsesAreEligible(string? method, string? expected)
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.Null);
        navigation.RecordRequest(uri, method, true);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.EqualTo(expected));
    }

    // WebKit decides each redirect as a request of its own: a 303 turns the POST into a GET of the new address.
    [Test]
    public void PostRedirectedToGetIsAGetAtTheFinalUrl()
    {
        var form = new Uri("https://page.example/generate");
        var media = new Uri("https://files.example/download?filename=Morning.mp3");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(form, "POST", true);
        Assert.That(navigation.GetCompleteResponseMethod(form, 200), Is.EqualTo("POST"));
        navigation.RecordRequest(media, "GET", true);
        Assert.Multiple(() =>
        {
            Assert.That(navigation.GetCompleteResponseMethod(media, 200), Is.EqualTo("GET"));
            Assert.That(navigation.GetCompleteResponseMethod(form, 200), Is.Null);
        });
    }

    // A 307 or 308 keeps the method and the form body, so the answer at the new address is still a POST's.
    [Test]
    public void PostKeptAcrossARedirectIsAPostAtTheFinalUrl()
    {
        var form = new Uri("https://page.example/generate");
        var media = new Uri("https://files.example/render");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(form, "POST", true);
        navigation.RecordRequest(media, "POST", true);
        Assert.Multiple(() =>
        {
            Assert.That(navigation.GetCompleteResponseMethod(media, 200), Is.EqualTo("POST"));
            Assert.That(navigation.GetCompleteResponseMethod(form, 200), Is.Null);
        });
    }

    [TestCase("GET", "POST", "GET")]
    [TestCase("POST", "GET", "POST")]
    public void SubframeRequestsCannotReplaceTheMainFrameMethod(string mainMethod, string frameMethod, string expected)
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, mainMethod, true);
        navigation.RecordRequest(uri, frameMethod, false);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.EqualTo(expected));
    }

    [Test]
    public void EachRequestReplacesTheMethodOfThePreviousOne()
    {
        var uri = new Uri("https://files.example/download");
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, "GET", true);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.EqualTo("GET"));
        navigation.RecordRequest(uri, "POST", true);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.EqualTo("POST"));
        navigation.RecordRequest(uri, "GET", true);
        navigation.RecordRequest(null, null, true);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.Null);
    }

    [TestCase("https://files.example/download?token=other")]
    [TestCase("https://other.example/download")]
    public void UnrelatedResponsesCannotReuseTheRequest(string response)
    {
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(new Uri("https://files.example/download"), "POST", true);
        Assert.That(navigation.GetCompleteResponseMethod(new Uri(response), 200), Is.Null);
    }

    [TestCase("https://user:password@files.example/download", "GET")]
    [TestCase("https://user:password@files.example/download", "POST")]
    [TestCase("file:///tmp/Morning.mp3", "GET")]
    public void UnsafeUrisAreNotEligible(string address, string method)
    {
        var uri = new Uri(address);
        var navigation = new BrowserDownloadNavigation();
        navigation.RecordRequest(uri, method, true);
        Assert.That(navigation.GetCompleteResponseMethod(uri, 200), Is.Null);
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
        foreach (string method in new[] { "GET", "POST" })
        {
            navigation.RecordRequest(uri, method, true);
            Assert.That(navigation.GetCompleteResponseMethod(uri, status), Is.EqualTo(expected ? method : null));
        }
    }
}
