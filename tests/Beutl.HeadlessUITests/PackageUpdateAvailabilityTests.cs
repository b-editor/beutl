using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageUpdateAvailabilityTests
{
    [TestCase(null, "1.0.0", false)]
    [TestCase("2.0.0", null, false)]
    [TestCase("not-a-version", "1.0.0", false)]
    [TestCase("1.0.0", "1.0.0", false)]
    [TestCase("0.9.0", "1.0.0", false)]
    [TestCase("2.0.0", "1.0.0", true)]
    public void IsAvailable_RequiresInstalledPackageAndNewerValidVersion(
        string? releaseVersion,
        string? installedVersion,
        bool expected)
    {
        PackageIdentity? installed = installedVersion is null
            ? null
            : new PackageIdentity("Package", NuGetVersion.Parse(installedVersion));

        Assert.That(PackageUpdateAvailability.IsAvailable(releaseVersion, installed), Is.EqualTo(expected));
    }
}
