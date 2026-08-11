using System.IO.Compression;
using System.Text;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

// The web-side nupkg builder produces a minimal zip (nuspec + content, no OPC parts);
// NuGet must still extract it, since that is what the installer consumes.
[TestFixture]
[NonParallelizable]
public class NupkgFormatCompatibilityTests
{
    private static string InstalledPackagesFile => Path.Combine(Helper.AppRoot, "installedPackages.json");

    private HttpClient _httpClient = null!;
    private InstalledPackageRepository _repository = null!;
    private PackageInstaller _installer = null!;
    private readonly List<string> _createdDirectories = [];

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
        _httpClient = new HttpClient();
        _repository = new InstalledPackageRepository();
        _installer = new PackageInstaller(_httpClient, _repository, apiApplication: null!);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
        foreach (string directory in _createdDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        _createdDirectories.Clear();
        File.Delete(InstalledPackagesFile);
    }

    [Test]
    public async Task MinimalNupkg_IsExtractable_AndReadableByPackageFolderReader()
    {
        const string id = "Beutl.Materials.Test";
        const string version = "1.0.0";
        var identity = new PackageIdentity(id, NuGetVersion.Parse(version));

        using var nupkg = new MemoryStream();
        using (var zip = new ZipArchive(nupkg, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry nuspec = zip.CreateEntry($"{id}.{version}.nuspec");
            using (StreamWriter writer = new(nuspec.Open(), new UTF8Encoding(false)))
            {
                writer.Write($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>{{id}}</id>
                        <version>{{version}}</version>
                        <title>Test Materials</title>
                        <authors>tester</authors>
                        <description>Test materials package</description>
                        <tags>beutl-material</tags>
                      </metadata>
                    </package>
                    """);
            }

            ZipArchiveEntry logo = zip.CreateEntry("materials/logo.png");
            using (StreamWriter writer = new(logo.Open(), new UTF8Encoding(false)))
            {
                writer.Write("not a real png");
            }

            ZipArchiveEntry nested = zip.CreateEntry("materials/audio/sting.wav");
            using (StreamWriter writer = new(nested.Open(), new UTF8Encoding(false)))
            {
                writer.Write("not a real wav");
            }
        }

        nupkg.Position = 0;

        var extractionContext = new PackageExtractionContext(
            PackageSaveMode.Defaultv3,
            XmlDocFileSaveMode.None,
            clientPolicyContext: null,
            NuGet.Common.NullLogger.Instance);

        await PackageExtractor.ExtractPackageAsync(
            "test",
            nupkg,
            Helper.PackagePathResolver,
            extractionContext,
            CancellationToken.None);

        string installedPath = Helper.PackagePathResolver.GetInstalledPath(identity)
            ?? throw new InvalidOperationException("extraction did not produce an install directory");
        _createdDirectories.Add(installedPath);

        var reader = new PackageFolderReader(installedPath);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = installedPath };

        Assert.Multiple(() =>
        {
            Assert.That(localPackage.Name, Is.EqualTo(id));
            Assert.That(localPackage.Tags, Does.Contain("beutl-material"));
            Assert.That(localPackage.Tags.GetPackageKind(), Is.EqualTo(PackageKind.Material));
            Assert.That(File.Exists(Path.Combine(installedPath, "materials", "logo.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(installedPath, "materials", "audio", "sting.wav")), Is.True);
        });
    }
}
