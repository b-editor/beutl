using System.IO.Compression;
using System.Text;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

// PackageInstaller.ResolveDependencies hands every NuGet download result to
// ExtractDownloadedPackageAsync. These tests use the result shapes NuGet produces.
[TestFixture]
[NonParallelizable]
public class PackageInstallerDownloadExtractionTests
{
    private const string SourceName = "https://packages.example/v3/index.json";
    private const string PayloadPath = "content/payload.txt";

    private readonly List<string> _createdDirectories = [];

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in _createdDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        _createdDirectories.Clear();
    }

    [Test]
    public async Task ExtractDownloadedPackage_ExtractsTheStream()
    {
        PackageIdentity identity = CreateIdentity();
        using var result = new DownloadResourceResult(CreateNupkg(identity), SourceName);

        await ExtractAsync(result, identity);

        AssertInstalled(identity);
    }

    [Test]
    public async Task ExtractDownloadedPackage_ExtractsTheStream_WhenTheResultHasNoSource()
    {
        // This is how NuGet returns a package that is already in the global packages folder.
        PackageIdentity identity = CreateIdentity();
        using var result = new DownloadResourceResult(CreateNupkg(identity), source: null);

        await ExtractAsync(result, identity);

        AssertInstalled(identity);
    }

    [Test]
    public async Task ExtractDownloadedPackage_ExtractsTheReader_WhenThePackageIsAvailableWithoutAStream()
    {
        // This is how a plugin-backed source returns a package it has available.
        PackageIdentity identity = CreateIdentity();
        using var result = new DownloadResourceResult(
            new PackageArchiveReader(CreateNupkg(identity)),
            SourceName);
        Assert.That(result.Status, Is.EqualTo(DownloadResourceResultStatus.AvailableWithoutStream));

        await ExtractAsync(result, identity);

        AssertInstalled(identity);
    }

    [TestCase(DownloadResourceResultStatus.NotFound)]
    [TestCase(DownloadResourceResultStatus.Cancelled)]
    public void ExtractDownloadedPackage_ReportsThePackageAndStatus_WhenNothingWasDownloaded(
        DownloadResourceResultStatus status)
    {
        PackageIdentity identity = CreateIdentity();
        using var result = new DownloadResourceResult(status);

        InvalidOperationException? thrown = Assert.ThrowsAsync<InvalidOperationException>(
            () => ExtractAsync(result, identity));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain($"{identity.Id} {identity.Version}"));
            Assert.That(thrown.Message, Does.Contain(SourceName));
            Assert.That(thrown.Message, Does.Contain(status.ToString()));
            Assert.That(Helper.PackagePathResolver.GetInstalledPath(identity), Is.Null);
        });
    }

    [Test]
    public void ExtractDownloadedPackage_ThrowsCancellation_WhenTheCallerCancelledTheDownload()
    {
        PackageIdentity identity = CreateIdentity();
        using var result = new DownloadResourceResult(DownloadResourceResultStatus.Cancelled);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => ExtractAsync(result, identity, cancellation.Token));
    }

    private PackageIdentity CreateIdentity()
    {
        var identity = new PackageIdentity(
            $"Beutl.DownloadExtractionTest.{Guid.NewGuid():N}",
            NuGetVersion.Parse("1.0.0"));
        _createdDirectories.Add(Helper.PackagePathResolver.GetInstallPath(identity));
        return identity;
    }

    private static Task ExtractAsync(
        DownloadResourceResult result,
        PackageIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var extractionContext = new PackageExtractionContext(
            PackageSaveMode.Defaultv3,
            XmlDocFileSaveMode.None,
            clientPolicyContext: null,
            NuGet.Common.NullLogger.Instance);

        return PackageInstaller.ExtractDownloadedPackageAsync(
            result,
            identity,
            SourceName,
            extractionContext,
            cancellationToken);
    }

    private static void AssertInstalled(PackageIdentity identity)
    {
        // GetInstalledPath looks for the .nupkg, so this also checks that the package file was saved.
        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(identity);
        Assert.That(installedPath, Is.Not.Null);
        Assert.That(File.ReadAllText(Path.Combine(installedPath!, PayloadPath)), Is.EqualTo("payload"));
    }

    private static MemoryStream CreateNupkg(PackageIdentity identity)
    {
        var nupkg = new MemoryStream();
        using (var zip = new ZipArchive(nupkg, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry nuspec = zip.CreateEntry($"{identity.Id}.nuspec");
            using (StreamWriter writer = new(nuspec.Open(), new UTF8Encoding(false)))
            {
                writer.Write($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>{{identity.Id}}</id>
                        <version>{{identity.Version}}</version>
                        <authors>tester</authors>
                        <description>Download extraction test package</description>
                      </metadata>
                    </package>
                    """);
            }

            ZipArchiveEntry payload = zip.CreateEntry(PayloadPath);
            using (StreamWriter writer = new(payload.Open(), new UTF8Encoding(false)))
            {
                writer.Write("payload");
            }
        }

        nupkg.Position = 0;
        return nupkg;
    }
}
