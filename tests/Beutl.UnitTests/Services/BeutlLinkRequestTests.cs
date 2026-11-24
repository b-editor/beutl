using Beutl.Services;

namespace Beutl.UnitTests.Services;

[TestFixture]
public sealed class BeutlLinkRequestTests
{
    private const string Id = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
    private const string Link = "beutl://repo/open?id=" + Id;
    private const string Install = "beutl://install?package=Beutl.Example&version=1.2.0-preview.1";

    [TestCase(Link, null)]
    [TestCase(Link + "&ref=feature%2F%2Brepair", "feature/+repair")]
    [TestCase(Link + "&ref=0123456789012345678901234567890123456789", "0123456789012345678901234567890123456789")]
    [TestCase("BEUTL://REPO/open?ref=%E4%BD%9C%E5%93%81&id=" + Id, "作品")]
    public void Parses_repository_links(string url, string? revision)
    {
        Assert.That(BeutlLinkRequest.TryParse(url, out var request), Is.True);
        Assert.That(request, Is.EqualTo(new RepositoryOpenRequest(Guid.Parse(Id), revision)));
    }

    [Test]
    public void Parses_web_link_with_an_escaped_non_ascii_ref_and_ampersand()
    {
        const string revision = "scene/春のオープニング&v1";
        Assert.That(BeutlLinkRequest.TryParse(Link + "&ref=" + Uri.EscapeDataString(revision), out var request), Is.True);
        Assert.That(request, Is.EqualTo(new RepositoryOpenRequest(Guid.Parse(Id), revision)));
    }

    [TestCase("beutl://repo/open")]
    [TestCase("beutl://repo/open?ref=main")]
    [TestCase("beutl://repo/open?id=A3A8927C-BCF4-4F6E-91F4-64897320378E")]
    [TestCase("beutl://repo/open?id=00000000-0000-0000-0000-000000000000")]
    [TestCase("beutl://repo/open?id=" + Id + "&id=" + Id)]
    [TestCase("beutl://repo/open?ID=" + Id)]
    [TestCase("beutl://repo/open/?id=" + Id)]
    [TestCase("beutl://repo/./open?id=" + Id)]
    [TestCase("beutl://repo:0/open?id=" + Id)]
    [TestCase("beutl://token@repo/open?id=" + Id)]
    [TestCase("beutl://@repo/open?id=" + Id)]
    [TestCase("https://repo/open?id=" + Id)]
    [TestCase(Link + "#")]
    [TestCase(Link + "&token=secret")]
    [TestCase(Link + "&ref=")]
    [TestCase(Link + "&ref=main&ref=other")]
    [TestCase(Link + "&ref=x%0Ay")]
    [TestCase(Link + "&ref=x%2e%2e%2fy")]
    [TestCase(Link + "&ref=%2fmain")]
    [TestCase(Link + "&ref=tag.lock")]
    [TestCase(Link + "&ref=main%7E1")]
    [TestCase(Link + "&ref=main%FF")]
    [TestCase(Link + "&")]
    public void Rejects_invalid_repository_links(string url)
        => Assert.That(BeutlLinkRequest.TryParse(url, out _), Is.False);

    [Test]
    public void Applies_revision_and_uri_length_limits()
    {
        Assert.That(BeutlLinkRequest.TryParse(Link + "&ref=" + new string('a', 255), out _), Is.True);
        Assert.That(BeutlLinkRequest.TryParse(Link + "&ref=" + new string('a', 256), out _), Is.False);
        // A 255-character multi-byte Japanese ref percent-encodes past 2,048 chars and must still parse.
        string longJapaneseRef = new('雪', 255);
        Assert.That(BeutlLinkRequest.TryParse(Link + "&ref=" + Uri.EscapeDataString(longJapaneseRef), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(new RepositoryOpenRequest(Guid.Parse(Id), longJapaneseRef)));
        Assert.That(BeutlLinkRequest.TryParse(Link + "&ref=" + new string('a', BeutlLinkRequest.MaxUriLength), out _), Is.False);
    }

    [Test]
    public async Task Launcher_forwards_repository_and_package_links_to_the_same_instance()
    {
        Assert.That(BeutlLinkRequest.TryParse(Install, out var package), Is.True);
        Assert.That(package, Is.EqualTo(new PackageInstallRequest("Beutl.Example", "1.2.0-preview.1")));
        string pipeName = "btl-" + Guid.NewGuid().ToString("N")[..8];
        string repository = Link + "&ref=" + Uri.EscapeDataString(new string('雪', 255));
        var first = await PackageLinkLauncher.PrepareAsync([repository], pipeName);
        using var broker = first.Broker;
        Assert.That(first.Action, Is.EqualTo(PackageLinkLaunchAction.StartApplication));
        Assert.That(first.Arguments, Is.EqualTo(new[] { repository }));

        var result = await PackageLinkLauncher.PrepareAsync([repository, Install], pipeName);
        Assert.That(result.Action, Is.EqualTo(PackageLinkLaunchAction.Forwarded));
        var delivered = new List<string>();
        broker!.SetHandler(delivered.Add);
        Assert.That(delivered, Is.EqualTo(new[] { repository, Install }));
        Assert.That(await PackageLinkBroker.TryForwardAsync(Link + "&token=secret", pipeName), Is.False);
    }
}
