using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
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
    private const string ConfigToMigrate = """
        <configuration>
          <packageSources>
            <clear />
            <add key="Beutl Local Packages" value="oldSource" />
          </packageSources>
        </configuration>
        """;

    private byte[]? _originalConfig;
    private UnixFileMode? _originalUnixMode;
    private FileSecurity? _originalSecurity;
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _createdDirectories = [];

    private static string ConfigPath => Path.Combine(Helper.AppRoot, "nuget.config");

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _originalConfig = File.Exists(ConfigPath) ? File.ReadAllBytes(ConfigPath) : null;
        _originalUnixMode = _originalConfig is not null && !OperatingSystem.IsWindows()
            ? File.GetUnixFileMode(ConfigPath)
            : null;
        _originalSecurity = _originalConfig is not null && OperatingSystem.IsWindows()
            ? new FileInfo(ConfigPath).GetAccessControl(AccessControlSections.Access)
            : null;
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
        {
            File.WriteAllBytes(ConfigPath, _originalConfig);
            if (!OperatingSystem.IsWindows() && _originalUnixMode is { } mode)
                File.SetUnixFileMode(ConfigPath, mode);
            if (OperatingSystem.IsWindows() && _originalSecurity is { } security)
                new FileInfo(ConfigPath).SetAccessControl(security);
        }
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
            Assert.That(Directory.GetDirectories(Helper.AppRoot, ".beutl-output-*"), Is.Empty);
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
            Assert.That(Directory.GetDirectories(Helper.AppRoot, ".beutl-output-*"), Is.Empty);
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

    [TestCase("iso-8859-1")]
    [TestCase("utf-16")]
    [TestCase("utf-16BE")]
    public async Task Migration_HonorsTheDeclaredEncodingWithoutABom(string encodingName)
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
        File.WriteAllBytes(ConfigPath, Encoding.GetEncoding(encodingName).GetBytes(config));
        XDocument expected = XDocument.Parse(config, LoadOptions.PreserveWhitespace);
        expected.Root!.Element("packageSources")!.Elements("add").First().SetAttributeValue("value", "packageSource");
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.That(XNode.DeepEquals(XDocument.Load(ConfigPath, LoadOptions.PreserveWhitespace), expected), Is.True,
            "Migration must preserve non-ASCII comments, paths and credentials using the XML declaration's encoding.");
    }

    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
    [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite)]
    public async Task Migration_PreservesUnixPermissions(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are not supported on Windows.");
            return;
        }

        File.WriteAllText(ConfigPath, ConfigToMigrate);
        File.SetUnixFileMode(ConfigPath, mode);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.That(File.GetUnixFileMode(ConfigPath), Is.EqualTo(mode));
        AssertLocalSourceMigrated(ConfigPath);
    }

    [Test]
    public async Task Migration_PreservesExplicitWindowsAccessRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows access rules are not supported on this platform.");
            return;
        }

        File.WriteAllText(ConfigPath, ConfigToMigrate);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        var configFile = new FileInfo(ConfigPath);
        configFile.SetAccessControl(security);
        string expected = configFile.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.That(configFile.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            Is.EqualTo(expected));
        AssertLocalSourceMigrated(ConfigPath);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Migration_UpdatesTheTargetAndPreservesTheSymbolicLink(bool relativeTarget)
    {
        string targetDirectory = Path.Combine(Helper.AppRoot, $"linked-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);
        _createdDirectories.Add(targetDirectory);
        string targetPath = Path.Combine(targetDirectory, "shared.config");
        File.WriteAllText(targetPath, ConfigToMigrate);
        File.Delete(ConfigPath);
        _createdFiles.Add(ConfigPath);
        string linkTarget = relativeTarget ? Path.GetRelativePath(Helper.AppRoot, targetPath) : targetPath;
        try
        {
            File.CreateSymbolicLink(ConfigPath, linkTarget);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
            || ex is IOException && OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            Assert.Ignore("Symlink creation is not available.");
        }
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        Assert.That(new FileInfo(ConfigPath).LinkTarget, Is.EqualTo(linkTarget));
        AssertLocalSourceMigrated(targetPath);
        var provider = new PackageSourceProvider(new Settings(Helper.AppRoot, "nuget.config"));
        Assert.That(provider.LoadPackageSources().Single(source => source.Name == "Beutl Local Packages").Source,
            Is.EqualTo(Helper.LocalSourcePath));
        Assert.That(Directory.GetDirectories(targetDirectory, ".beutl-output-*"), Is.Empty);
    }

    private static void AssertLocalSourceMigrated(string path)
    {
        Assert.That((string?)XDocument.Load(path).Root!.Element("packageSources")!.Elements("add")
            .Single(source => (string?)source.Attribute("key") == "Beutl Local Packages").Attribute("value"), Is.EqualTo("packageSource"));
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
