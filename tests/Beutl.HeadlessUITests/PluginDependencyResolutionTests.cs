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

        Assert.That(Path.GetFullPath(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common"))!),
            Is.EqualTo(Path.GetFullPath(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll"))));
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

        Assert.That(Path.GetFullPath(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common"))!),
            Is.EqualTo(Path.GetFullPath(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll"))));
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
        Assert.That(Path.GetFullPath(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common"))!),
            Is.EqualTo(Path.GetFullPath(Path.Combine(current, "lib", "net10.0", _prefix + "Common.dll"))));
    }

    // SDKs up to 2.0.0-preview.8 packed the build-time source generator as a dependency of every
    // extension, and no source the installer searches publishes it (#2730).
    [Test]
    public async Task Installer_ignores_the_source_generator_that_older_sdks_listed_as_a_dependency()
    {
        string root = CreatePackageDependingOn("Root", "1.0.0", ("Beutl.Engine.SourceGenerators", "2.0.0-preview.8"));
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);
        PackageInstallContext context = installer.PrepareForInstall(_prefix + "Root", "1.0.0");

        await installer.ResolveDependencies(context, NuGet.Common.NullLogger.Instance);

        Assert.That(context.Phase, Is.EqualTo(PackageInstallPhase.ResolvedDependencies));
        using var reader = new PackageFolderReader(root);
        Assert.That(ResolvedPackageDependencies.Load(reader, Helper.GetFrameworkName()), Is.EqualTo(new[] { Identity("Root", "1.0.0") }));
    }

    // PackageTools.UI deletes the downloaded nupkg from the local source after installing (#2832).
    [Test]
    public async Task Re_resolution_uses_the_installed_package_after_its_download_is_deleted()
    {
        CreatePackage("Common", "1.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        DeleteDownloads();
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        await installer.ReResolveDependencies(Identity("Root", "1.0.0"), NuGet.Common.NullLogger.Instance);

        Assert.That(File.Exists(Path.Combine(root, ResolvedPackageDependencies.FileName)), Is.True);
        using var reader = new PackageFolderReader(root);
        Assert.That(ResolvedPackageDependencies.Load(reader, Helper.GetFrameworkName()),
            Is.EquivalentTo(new[] { Identity("Root", "1.0.0"), Identity("Common", "1.0.0") }));
    }

    [Test]
    public async Task Re_resolution_of_an_installed_graph_does_not_ask_other_sources()
    {
        CreatePackage("Common", "1.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        DeleteDownloads();
        // A source that cannot be reached, as nuget.org is when offline.
        int port;
        using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }

        new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "test"), new XAttribute("value", Helper.LocalSourcePath)),
            new XElement("add", new XAttribute("key", "unreachable"), new XAttribute("value", $"http://127.0.0.1:{port}/v3/index.json"))))
            .Save(Path.Combine(Helper.AppRoot, "nuget.config"));
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, new InstalledPackageRepository(), null!);

        await installer.ReResolveDependencies(Identity("Root", "1.0.0"), NuGet.Common.NullLogger.Instance);

        Assert.That(File.Exists(Path.Combine(root, ResolvedPackageDependencies.FileName)), Is.True);
    }

    [Test]
    public async Task Startup_re_resolution_records_the_current_Beutl_version_after_the_download_is_deleted()
    {
        CreatePackage("Common", "1.0.0");
        CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        DeleteDownloads();
        var repository = new InstalledPackageRepository();
        repository.UpgradePackages(Identity("Root", "1.0.0"));
        repository.SetResolvedBeutlVersion(_prefix + "Root", "1.0.0-older");
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, repository, null!);

        var task = new Beutl.Services.StartupTasks.ResolvePackageDependenciesTask(repository, installer);
        await task.Task;

        Assert.That(task.Failures, Is.Empty);
        Assert.That(repository.GetPackagesNeedingDependencyReResolution(), Is.Empty);
    }

    [TestCase("invalid-json")]
    [TestCase("missing-list")]
    [TestCase("wrong-root")]
    [TestCase("missing-dependency")]
    public void An_invalid_snapshot_falls_back_to_the_usable_installed_graph(string failure)
    {
        string dependency = CreatePackage("Common", "2.0.0");
        string current = CreatePackage("Common", "3.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "2.0.0"));
        ResolvedPackageDependencies.Save(root, Helper.GetFrameworkName(), [Identity("Root", "1.0.0"), Identity("Common", "2.0.0")]);
        string snapshot = Path.Combine(root, ResolvedPackageDependencies.FileName);
        if (failure == "invalid-json") File.WriteAllText(snapshot, "{");
        else if (failure == "missing-dependency") Directory.Delete(dependency, recursive: true);
        else
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(snapshot))!;
            if (failure == "missing-list") json["Packages"] = null;
            else json["Root"]!["Id"] = _prefix + "Other";
            File.WriteAllText(snapshot, json.ToJsonString());
        }
        using var reader = new PackageFolderReader(root);
        var resolver = new PluginDependencyResolver(Path.Combine(root, "lib", "net10.0"), reader);

        Assert.That(Path.GetFullPath(resolver.ResolveAssemblyToPath(new AssemblyName(_prefix + "Common"))!),
            Is.EqualTo(Path.GetFullPath(Path.Combine(failure == "missing-dependency" ? current : dependency, "lib", "net10.0", _prefix + "Common.dll"))));
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
    public async Task Cleanup_keeps_compatible_versions_when_the_selected_version_cannot_be_recovered(bool corrupt)
    {
        CreatePackage("Common", "1.0.0");
        CreatePackage("Common", "2.0.0");
        string root = CreatePackage("Root", "1.0.0", ("Common", "1.0.0"));
        if (corrupt) File.WriteAllText(Path.Combine(root, ResolvedPackageDependencies.FileName), "{");
        var repository = new InstalledPackageRepository();
        repository.UpgradePackages(Identity("Root", "1.0.0"));
        using var client = new HttpClient();
        await using var installer = new PackageInstaller(client, repository, null!);

        PackageCleanContext context = installer.PrepareForClean();

        Assert.That(context.UnnecessaryPackages, Does.Not.Contain(Identity("Common", "1.0.0")));
        Assert.That(context.UnnecessaryPackages, Does.Not.Contain(Identity("Common", "2.0.0")));
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

    // Removes the nupkgs these tests put in the local source; the installed package folders keep their copies.
    private void DeleteDownloads()
    {
        foreach (string archive in _archives) File.Delete(archive);
    }

    private string CreatePackage(string name, string version, params (string Name, string Version)[] dependencies)
        => CreatePackageDependingOn(name, version, dependencies.Select(dependency => (_prefix + dependency.Name, dependency.Version)).ToArray());

    private string CreatePackageDependingOn(string name, string version, params (string Id, string Version)[] dependencies)
    {
        PackageIdentity identity = Identity(name, version);
        string directory = Helper.PackagePathResolver.GetInstallPath(identity);
        _directories.Add(directory);
        Directory.CreateDirectory(directory);
        new XElement("package", new XElement("metadata", new XElement("id", identity.Id), new XElement("version", version),
            new XElement("authors", "tests"), new XElement("description", "Package resolution fixture"),
            new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", "net10.0"),
                dependencies.Select(dependency => new XElement("dependency", new XAttribute("id", dependency.Id),
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
