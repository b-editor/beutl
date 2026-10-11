using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// Dependency resolution reads Beutl.dll's dependency context, so these tests need the app assembly.
[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerNuGetConfigTests
{
    private byte[]? _originalConfig;
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _createdDirectories = [];

    private static string ConfigPath => Path.Combine(Helper.AppRoot, "nuget.config");

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _originalConfig = File.Exists(ConfigPath) ? File.ReadAllBytes(ConfigPath) : null;
        Directory.CreateDirectory(Helper.LocalSourcePath);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string file in _createdFiles)
            File.Delete(file);
        foreach (string directory in _createdDirectories.Where(Directory.Exists))
            Directory.Delete(directory, recursive: true);
        _createdFiles.Clear();
        _createdDirectories.Clear();

        if (_originalConfig is null)
            File.Delete(ConfigPath);
        else
            File.WriteAllBytes(ConfigPath, _originalConfig);
    }

    [Test]
    public async Task RelocatedHome_ResolvesDownloadedPackagesAndPreservesOtherSettings()
    {
        string oldSource = Path.Combine(Helper.AppRoot, "old-home", "packageSource");
        string customSource = Path.Combine(Helper.AppRoot, "customSource");
        Directory.CreateDirectory(customSource);
        _createdDirectories.Add(customSource);
        var document = new XDocument(
            new XElement("configuration",
                new XComment("Keep the user's other package settings."),
                new XElement("packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "Beutl Local Packages"), new XAttribute("value", oldSource)),
                    new XElement("add", new XAttribute("key", "Custom"), new XAttribute("value", "customSource")),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"),
                        new XAttribute("protocolVersion", "3"))),
                new XElement("disabledPackageSources", new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "true"))),
                new XElement("config", new XElement("add", new XAttribute("key", "maxHttpRequestsPerSource"), new XAttribute("value", "8")))));
        document.Save(ConfigPath);
        PackageIdentity identity = CreateLocalPackage();
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);
        PackageInstallContext context = installer.PrepareForInstall(identity.Id, identity.Version.ToString());

        await installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance);

        document.Root!.Element("packageSources")!.Elements("add").First().SetAttributeValue("value", "packageSource");
        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(identity);
        Assert.Multiple(() =>
        {
            Assert.That(context.Phase, Is.EqualTo(PackageInstallPhase.ResolvedDependencies));
            Assert.That(installedPath, Is.Not.Null);
            Assert.That(XNode.DeepEquals(document, XDocument.Load(ConfigPath)), Is.True,
                "Only the managed local source should change; custom feeds, disabled sources and comments must survive.");
            Assert.That(Directory.GetFiles(Helper.AppRoot, "nuget.config.*.tmp"), Is.Empty);
        });
        Assert.That(File.ReadAllText(Path.Combine(installedPath!, "content/payload.txt")), Is.EqualTo("local payload"));
    }

    [TestCase(null)]
    [TestCase("<configuration><packageSources><add key=\"Legacy\" value=\"oldSource\" /></packageSources></configuration>")]
    [TestCase("<configuration>")]
    public async Task DefaultConfig_ResolvesItsLocalSourceRelativeToTheHome(string? previousConfig)
    {
        File.Delete(ConfigPath);
        if (previousConfig is not null)
            File.WriteAllText(ConfigPath, previousConfig);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        var provider = new PackageSourceProvider(new Settings(Helper.AppRoot, "nuget.config"));
        PackageSource localSource = provider.LoadPackageSources().Single(source => source.Name == "Beutl Local Packages");
        XElement sources = XDocument.Load(ConfigPath).Root!.Element("packageSources")!;
        Assert.Multiple(() =>
        {
            Assert.That(sources.Element("clear"), Is.Not.Null);
            Assert.That((string?)sources.Elements("add").First().Attribute("value"), Is.EqualTo("packageSource"));
            Assert.That(localSource.Source, Is.EqualTo(Helper.LocalSourcePath),
                "NuGet must resolve the relative source against nuget.config's directory, regardless of the working directory.");
            Assert.That(provider.LoadPackageSources().Select(source => source.Name), Is.EquivalentTo(new[] { "Beutl Local Packages", "nuget.org" }));
            Assert.That(Directory.GetFiles(Helper.AppRoot, "nuget.config.*.tmp"), Is.Empty);
        });
    }

    [TestCase("Custom", "customSource")]
    [TestCase("Beutl Local Packages", "packageSource")]
    public async Task ConfigNeedingNoMigration_IsNotRewritten(string name, string source)
    {
        new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", name), new XAttribute("value", source))))
            .Save(ConfigPath);
        byte[] original = File.ReadAllBytes(ConfigPath);
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddMinutes(-10));
        DateTime lastWriteTime = File.GetLastWriteTimeUtc(ConfigPath);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(original));
            Assert.That(File.GetLastWriteTimeUtc(ConfigPath), Is.EqualTo(lastWriteTime));
        });
    }

    private PackageIdentity CreateLocalPackage()
    {
        var identity = new PackageIdentity($"Beutl.RelocatedHomeTest.{Guid.NewGuid():N}", NuGetVersion.Parse("1.0.0"));
        string sourcePath = Helper.GetNupkgFilePath(identity.Id, identity.Version.ToString());
        _createdFiles.Add(sourcePath);
        _createdDirectories.Add(Helper.PackagePathResolver.GetInstallPath(identity));
        using var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create);
        using (StreamWriter writer = new(archive.CreateEntry($"{identity.Id}.nuspec").Open(), new UTF8Encoding(false)))
        {
            writer.Write($$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{{identity.Id}}</id>
                    <version>{{identity.Version}}</version>
                    <authors>tester</authors>
                    <description>A package downloaded into the relocated home</description>
                  </metadata>
                </package>
                """);
        }
        using (StreamWriter writer = new(archive.CreateEntry("content/payload.txt").Open(), new UTF8Encoding(false)))
            writer.Write("local payload");
        return identity;
    }
}
