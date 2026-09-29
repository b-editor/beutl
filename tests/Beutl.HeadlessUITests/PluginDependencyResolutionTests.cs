using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class PluginDependencyResolutionTests
{
    private string _prefix = null!;
    private readonly List<string> _directories = [];
    private readonly List<string> _archives = [];
    private readonly Dictionary<string, byte[]?> _originalFiles = [];

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        _prefix = "Beutl.PluginTest." + Guid.NewGuid().ToString("N") + ".";
        Directory.CreateDirectory(Helper.LocalSourcePath);
        foreach (string name in new[] { "nuget.config", "installedPackages.json" })
        {
            string path = Path.Combine(Helper.AppRoot, name);
            _originalFiles.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
        File.Delete(Path.Combine(Helper.AppRoot, "installedPackages.json"));
        new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "test"), new XAttribute("value", Helper.LocalSourcePath))))
            .Save(Path.Combine(Helper.AppRoot, "nuget.config"));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in _directories.Where(Directory.Exists)) Directory.Delete(directory, recursive: true);
        foreach (string archive in _archives) File.Delete(archive);
        foreach ((string path, byte[]? original) in _originalFiles)
        {
            if (original is null) File.Delete(path);
            else File.WriteAllBytes(path, original);
        }
        _directories.Clear();
        _archives.Clear();
        _originalFiles.Clear();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Older_installs_resolve_the_whole_diamond_regardless_of_traversal_order(bool reverse)
    {
        var (root, _, current) = CreateDiamond(reverse);
        using var reader = new PackageFolderReader(root);
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);

        Assert.That(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common")),
            Is.EqualTo(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll")));
    }

    [Test]
    public void Snapshot_keeps_the_selected_version_when_an_older_compatible_version_is_installed()
    {
        CreatePackage("Common", "1.0.0");
        string current = CreatePackage("Common", "2.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        ResolvedPackageDependencies.Save(root, Helper.GetFrameworkName(), [Identity("Root", "1.0.0"), Identity("Common", "2.0.0")]);
        using var reader = new PackageFolderReader(root);
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);

        Assert.That(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common")),
            Is.EqualTo(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll")));
    }

    [Test]
    public async Task Installer_persists_its_resolution_for_subsequent_loads()
    {
        var (root, _, current) = CreateDiamond(reverse: false);
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);
        PackageInstallContext context = installer.PrepareForInstall(_prefix + "Root", "1.0.0");

        await installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance);

        Assert.That(context.Phase, Is.EqualTo(PackageInstallPhase.ResolvedDependencies));
        Assert.That(File.Exists(Path.Combine(root, ResolvedPackageDependencies.FileName)), Is.True);
        using var reader = new PackageFolderReader(root);
        Assert.That(ResolvedPackageDependencies.Load(reader, Helper.GetFrameworkName()), Does.Contain(Identity("Common", "2.0.0")));
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);
        Assert.That(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common")),
            Is.EqualTo(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll")));
    }

    [Test]
    public async Task Cleanup_retains_the_pinned_dependency_and_removes_the_unused_version()
    {
        string old = CreatePackage("Common", "1.0.0");
        string current = CreatePackage("Common", "2.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        ResolvedPackageDependencies.Save(root, Helper.GetFrameworkName(), [Identity("Root", "1.0.0"), Identity("Common", "2.0.0")]);
        var repository = new InstalledPackageRepository();
        repository.UpgradePackages(Identity("Root", "1.0.0"));
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, repository, null!);

        PackageCleanContext context = installer.PrepareForClean();

        Assert.That(context.UnnecessaryPackages, Does.Contain(Identity("Common", "1.0.0")));
        Assert.That(context.UnnecessaryPackages, Does.Not.Contain(Identity("Common", "2.0.0")));
        Assert.That(context.UnnecessaryPackages, Does.Not.Contain(Identity("Root", "1.0.0")));
        installer.Clean(new PackageCleanContext(context.UnnecessaryPackages.Where(package => package.Id.StartsWith(_prefix)).ToArray(),
            context.SizeToBeReleased), new Progress<double>());
        Assert.That(Directory.Exists(old), Is.False);
        Assert.That(Directory.Exists(current), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Native_assets_are_resolved_from_the_package_root_for_the_current_runtime(bool generic)
    {
        string root = CreatePackage("Native", "1.0.0");
        string runtime = RuntimeInformation.RuntimeIdentifier;
        string foreign = runtime[..runtime.LastIndexOf('-')] + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-x64" : "-arm64");
        string fileName = OperatingSystem.IsWindows() ? "beutl-test.dll" : OperatingSystem.IsMacOS() ? "libbeutl-test.dylib" : "libbeutl-test.so";
        WriteAsset(root, $"runtimes/{foreign}/native/{fileName}");
        string expected = WriteAsset(root, $"runtimes/{(generic ? "any" : runtime)}/native/{fileName}");
        using var reader = new PackageFolderReader(root);
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);

        Assert.That(resolver.ResolveUnmanagedDllToPath("beutl-test"), Is.EqualTo(expected));
    }

    [Test]
    public void Satellite_assets_are_resolved_without_repeating_the_culture_directory()
    {
        string root = CreatePackage("Localized", "1.0.0");
        string expected = WriteAsset(root, "lib/net10.0/ja/Localized.resources.dll");
        using var reader = new PackageFolderReader(root);
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);

        Assert.That(resolver.ResolveAssemblyToPath(new AssemblyName("Localized.resources") { CultureName = "ja" }), Is.EqualTo(expected));
        Assert.That(resolver.ResolveAssemblyToPath(new AssemblyName("Localized.resources") { CultureName = "fr" }), Is.Null);
    }

    private (string Root, string Old, string Current) CreateDiamond(bool reverse)
    {
        string old = CreatePackage("Common", "1.0.0");
        string current = CreatePackage("Common", "2.0.0");
        CreatePackage("Left", "1.0.0", ("Common", "1.0.0"));
        CreatePackage("Right", "1.0.0", ("Common", "2.0.0"));
        (string, string)[] dependencies = [("Left", "1.0.0"), ("Right", "1.0.0")];
        if (reverse) Array.Reverse(dependencies);
        return (CreatePackage("Root", "1.0.0", dependencies), old, current);
    }

    private PackageIdentity Identity(string name, string version) => new(_prefix + name, NuGetVersion.Parse(version));

    private string CreatePackage(string name, string version, params (string Name, string Version)[] dependencies)
    {
        PackageIdentity identity = Identity(name, version);
        string directory = Helper.PackagePathResolver.GetInstallPath(identity);
        _directories.Add(directory);
        Directory.CreateDirectory(directory);
        new XElement("package", new XElement("metadata", new XElement("id", identity.Id), new XElement("version", version),
            new XElement("authors", "tests"), new XElement("description", "Package resolution fixture"),
            new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", "net10.0"),
                dependencies.Select(dependency => new XElement("dependency", new XAttribute("id", _prefix + dependency.Name),
                    new XAttribute("version", $"[{dependency.Version},)")))))))
            .Save(Path.Combine(directory, identity.Id + ".nuspec"));
        WriteAsset(directory, $"lib/net10.0/{identity.Id}.dll");
        string archive = Path.Combine(Helper.LocalSourcePath, Helper.PackagePathResolver.GetPackageFileName(identity));
        _archives.Add(archive);
        ZipFile.CreateFromDirectory(directory, archive);
        File.Copy(archive, Path.Combine(directory, Path.GetFileName(archive)));
        return directory;
    }

    private static string WriteAsset(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "asset");
        return path;
    }
}
