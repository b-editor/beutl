using Beutl.Api.Objects;
using Beutl.Api.Services;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageUpdateAvailabilityTests
{
    private readonly StoreClients _store = new();

    [OneTimeTearDown]
    public async Task TearDown() => await _store.DisposeAsync();

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
        Release? release = releaseVersion is null ? null : _store.CreateRelease(releaseVersion);
        NuGetVersion? installed = installedVersion is null ? null : NuGetVersion.Parse(installedVersion);

        Assert.That(PackageUpdate.IsAvailable(release, installed), Is.EqualTo(expected));
    }
}
