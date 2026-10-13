using System.Text;
using System.Xml.Linq;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Resolver;

namespace Beutl.HeadlessUITests;

// Dependency resolution reads Beutl.dll's dependency context, so these tests need the app assembly.
[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerNuGetConfigTests
{
    private const string ConfigWithStaleSource = """
        <configuration>
          <packageSources>
            <clear />
            <add key="Beutl Local Packages" value="oldSource" />
          </packageSources>
        </configuration>
        """;

    private AppRootFileBackup _config = null!;
    private readonly List<string> _cleanup = [];

    private static string ConfigPath => Path.Combine(Helper.AppRoot, "nuget.config");

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _config = new AppRootFileBackup("nuget.config");
        Directory.CreateDirectory(Helper.LocalSourcePath);
    }

    [TearDown]
    public void TearDown()
    {
        TestPackages.Delete(_cleanup);
        _config.Dispose();
    }

    [Test]
    public async Task RelocatedHome_ResolvesDownloadedPackagesAndPreservesOtherSettings()
    {
        string oldSource = Path.Combine(Helper.AppRoot, "old-home", "packageSource");
        string customSource = Path.Combine(Helper.AppRoot, "customSource");
        Directory.CreateDirectory(customSource);
        _cleanup.Add(customSource);
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
        byte[] original = File.ReadAllBytes(ConfigPath);
        PackageIdentity identity = CreateLocalPackage();
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);
        PackageInstallContext context = installer.PrepareForInstall(identity.Id, identity.Version.ToString());

        await installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance);

        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(identity);
        Assert.Multiple(() =>
        {
            Assert.That(context.Phase, Is.EqualTo(PackageInstallPhase.ResolvedDependencies));
            Assert.That(installedPath, Is.Not.Null);
            Assert.That(XNode.DeepEquals(document, XDocument.Load(ConfigPath)), Is.True,
                "Custom feeds, disabled sources, settings and comments must survive.");
            Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(original));
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
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LegacySymbolicLink_IsReplacedWithoutChangingTheSharedTarget(bool relativeTarget)
    {
        string targetDirectory = Path.Combine(Helper.AppRoot, $"legacy-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);
        _cleanup.Add(targetDirectory);
        string targetPath = Path.Combine(targetDirectory, "shared.config");
        const string LegacyConfig = """
            <configuration>
              <packageSources><add key="Custom" value="sharedSource" /></packageSources>
              <config><add key="maxHttpRequestsPerSource" value="8" /></config>
            </configuration>
            """;
        File.WriteAllText(targetPath, LegacyConfig);
        File.Delete(ConfigPath);
        _cleanup.Add(ConfigPath);
        string linkTarget = relativeTarget ? Path.GetRelativePath(Helper.AppRoot, targetPath) : targetPath;
        CreateSymbolicLinkOrIgnore(ConfigPath, linkTarget);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(ConfigPath).LinkTarget, Is.Null);
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo(LegacyConfig));
            AssertDefaultSource();
        });
    }

    [TestCase(UnixFileMode.UserRead)]
    [TestCase(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    public async Task ReadOnlyLegacyConfig_CanBeRegenerated(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This regression uses Unix read-only permissions.");
            return;
        }

        File.WriteAllText(ConfigPath, "<configuration />");
        File.SetUnixFileMode(ConfigPath, mode);
        try
        {
            using var client = new HttpClient();
            await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

            AssertDefaultSource();
        }
        finally
        {
            if (File.Exists(ConfigPath))
                File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [TestCase("Custom", "customSource")]
    [TestCase("Beutl Local Packages", "packageSource")]
    [TestCase("Beutl Local Packages", "oldSource")]
    public async Task ExistingConfig_IsNotRewritten(string name, string source)
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

    [TestCase("iso-8859-1")]
    [TestCase("utf-16")]
    [TestCase("utf-16BE")]
    public async Task ExistingConfig_HonorsTheDeclaredEncodingWithoutABom(string encodingName)
    {
        string config = $$"""
            <?xml version="1.0" encoding="{{encodingName}}"?>
            <configuration>
              <!-- Déjà configuré -->
              <packageSources>
                <clear />
                <add key="Beutl Local Packages" value="oldSource" />
                <add key="Custom" value="références" />
              </packageSources>
              <packageSourceCredentials>
                <Custom>
                  <add key="Username" value="café" />
                  <add key="ClearTextPassword" value="clé-synthétique" />
                </Custom>
              </packageSourceCredentials>
            </configuration>
            """;
        byte[] original = Encoding.GetEncoding(encodingName).GetBytes(config);
        File.WriteAllBytes(ConfigPath, original);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        await AssertLocalPackageResolves(installer);

        Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(original),
            "Non-ASCII comments, paths and credentials must survive with the original encoding.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingConfig_PreservesTheSymbolicLinkAndTarget(bool relativeTarget)
    {
        string targetDirectory = Path.Combine(Helper.AppRoot, $"linked-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);
        _cleanup.Add(targetDirectory);
        string targetPath = Path.Combine(targetDirectory, "shared.config");
        File.WriteAllText(targetPath, ConfigWithStaleSource);
        File.Delete(ConfigPath);
        _cleanup.Add(ConfigPath);
        string linkTarget = relativeTarget ? Path.GetRelativePath(Helper.AppRoot, targetPath) : targetPath;
        CreateSymbolicLinkOrIgnore(ConfigPath, linkTarget);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        await AssertLocalPackageResolves(installer);

        Assert.That(new FileInfo(ConfigPath).LinkTarget, Is.EqualTo(linkTarget));
        Assert.That(File.ReadAllText(targetPath), Is.EqualTo(ConfigWithStaleSource));
    }

    [Test]
    public async Task DisabledManagedSource_DoesNotResolveLocalPackages()
    {
        XDocument document = XDocument.Parse(ConfigWithStaleSource);
        document.Root!.Add(new XElement("disabledPackageSources",
            new XElement("add", new XAttribute("key", "Beutl Local Packages"), new XAttribute("value", "true"))));
        document.Save(ConfigPath);
        PackageIdentity identity = CreateLocalPackage();
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);
        PackageInstallContext context = installer.PrepareForInstall(identity.Id, identity.Version.ToString());

        await Assert.ThrowsAsync<NuGetResolverInputException>(() => installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance));
    }

    private async Task AssertLocalPackageResolves(PackageInstaller installer)
    {
        PackageIdentity identity = CreateLocalPackage();
        PackageInstallContext context = installer.PrepareForInstall(identity.Id, identity.Version.ToString());
        await installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance);

        Assert.That(context.Phase, Is.EqualTo(PackageInstallPhase.ResolvedDependencies));
        string? installedPath = Helper.PackagePathResolver.GetInstalledPath(identity);
        Assert.That(installedPath, Is.Not.Null);
        Assert.That(File.ReadAllText(Path.Combine(installedPath!, "content/payload.txt")), Is.EqualTo("local payload"));
    }

    private static void AssertDefaultSource()
    {
        var provider = new PackageSourceProvider(new Settings(Helper.AppRoot, "nuget.config"));
        Assert.That(provider.LoadPackageSources().Single(source => source.Name == "Beutl Local Packages").Source,
            Is.EqualTo(Helper.LocalSourcePath));
        Assert.That(XDocument.Load(ConfigPath).Root!.Element("packageSources")!.Element("clear"), Is.Not.Null);
        Assert.That(Directory.GetFiles(Helper.AppRoot, "nuget.config.*.tmp"), Is.Empty);
    }

    private static void CreateSymbolicLinkOrIgnore(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
            || ex is IOException && OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            Assert.Ignore("Symlink creation is not available.");
        }
    }

    private PackageIdentity CreateLocalPackage() => TestPackages.CreateNupkg("Beutl.RelocatedHomeTest", "1.0.0", _cleanup);
}
