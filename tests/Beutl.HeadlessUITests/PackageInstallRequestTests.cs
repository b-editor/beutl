using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageInstallRequestTests
{
    [TestCase("beutl://install?package=Beutl.Sample&version=1.2.3", "1.2.3")]
    [TestCase("beutl://install/?package=Beutl.Sample", null)]
    [TestCase("beutl://install?package=Beutl.Sample&version=1.2.3-preview.1%2Bbuild.5", "1.2.3-preview.1+build.5")]
    public void ReadsOnlyThePackageAndOptionalVersion(string uri, string? version)
    {
        Assert.That(PackageInstallRequest.TryParse(uri, out var request), Is.True);
        Assert.That(request, Is.EqualTo(new PackageInstallRequest("Beutl.Sample", version)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("https://install?package=Sample")]
    [TestCase("beutl://uninstall?package=Sample")]
    [TestCase("beutl://install/path?package=Sample")]
    [TestCase("beutl://user@install?package=Sample")]
    [TestCase("beutl://install:80?package=Sample")]
    [TestCase("beutl://install?package=Sample#fragment")]
    [TestCase("beutl://install?package=Sample&url=https://example.com/payload")]
    [TestCase("beutl://install")]
    [TestCase("beutl://install?package=")]
    [TestCase("beutl://install?package=../Sample")]
    [TestCase("beutl://install?package=..")]
    [TestCase("beutl://install?package=%2Ftmp%2FSample")]
    [TestCase("beutl://install?package=Sample%26other")]
    [TestCase("beutl://install?package=Sample%00")]
    [TestCase("beutl://install?package=Sample&package=Other")]
    [TestCase("beutl://install?package=Sample&version=")]
    [TestCase("beutl://install?package=Sample&version=latest")]
    [TestCase("beutl://install?package=Sample&version=1.0.0&version=2.0.0")]
    [TestCase("beutl://install?package=Sample&version=1.0.0%20")]
    public void RejectsUnrecognizedOrAmbiguousLinks(string? uri)
    {
        Assert.That(PackageInstallRequest.TryParse(uri, out var request), Is.False);
        Assert.That(request, Is.Null);
    }

    [Test]
    public void RejectsOversizedInput()
    {
        Assert.That(PackageInstallRequest.TryParse("beutl://install?package=" + new string('a', 2048), out _), Is.False);
    }
}
