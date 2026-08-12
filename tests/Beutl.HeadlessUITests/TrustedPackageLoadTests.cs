using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class TrustedPackageLoadTests
{
    [Test]
    public void PackageManager_LoadsAndRegistersOnlyTheVerifiedSnapshotAssembly()
    {
        const string packageName = "Beutl.Package.SnapshotE2e";
        const string packageVersion = "1.0.0";
        const string featureId = "extension/beutl.package.snapshote2e/effect/snapshot-e2e";
        const string pluginAssemblyName = "Beutl.Telemetry.TestHost";
        const string pluginTypeName = "Beutl.Telemetry.TestHost.SnapshotE2eExtension";
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"snapshot-e2e-{Guid.NewGuid():N}");
        var repository = new InstalledPackageRepository();
        ExtensionProvider? extensionProvider = null;
        PackageManager? manager = null;
        LocalPackage? package = null;
        try
        {
            string sourceAssembly = Path.Combine(
                FindRepositoryRoot(),
                "tests",
                "Beutl.Telemetry.TestHost",
                "bin",
                GetBuildConfiguration(),
                "net10.0",
                $"{pluginAssemblyName}.dll");
            Assert.That(File.Exists(sourceAssembly), Is.True, $"Missing test plugin: {sourceAssembly}");

            string packageRoot = Path.Combine(root, "package");
            string libDirectory = Path.Combine(packageRoot, "lib", "net10.0");
            Directory.CreateDirectory(libDirectory);
            string extractedAssembly = Path.Combine(libDirectory, $"{pluginAssemblyName}.dll");
            File.Copy(sourceAssembly, extractedAssembly);
            WriteNuspec(packageRoot, packageName, packageVersion);

            byte[] manifest = Encoding.UTF8.GetBytes(
                $$"""
                {"schemaVersion":1,"features":[{"kind":"effect","key":"snapshot-e2e","types":[{"assembly":"{{pluginAssemblyName}}","type":"{{pluginTypeName}}"}]}]}
                """);
            string manifestSha256 = Convert.ToHexString(SHA256.HashData(manifest));
            string packageSha256 = CreateMarketplaceArtifact(packageRoot, manifest, extractedAssembly);

            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            repository.UpgradePackages(identity);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageSha256, manifestSha256)!);
            extensionProvider = new ExtensionProvider();
            var commandManager = new ContextCommandManager(
                new ContextCommandSettingsStore(),
                new ContextCommandHandlerRegistry());
            manager = new PackageManager(repository, extensionProvider, commandManager, apiApplication: null!);
            package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };

            Assembly[] assemblies = manager.Load(package);
            Extension extension = extensionProvider.AllExtensions.Single();

            Assert.Multiple(() =>
            {
                Assert.That(assemblies, Has.Length.EqualTo(1));
                Assert.That(assemblies[0].Location, Is.Empty);
                Assert.That(extension.GetType().Assembly, Is.SameAs(assemblies[0]));
                Assert.That(Telemetry.GetTrustedFeatureId(extension.GetType()), Is.EqualTo(featureId));
            });
        }
        finally
        {
            if (extensionProvider is not null)
            {
                foreach (Extension extension in extensionProvider.AllExtensions)
                {
                    Telemetry.UnregisterTrustedFeature(extension.GetType());
                }
            }

            if (package is not null && manager is not null)
            {
                manager.Unload(package).GetAwaiter().GetResult();
            }

            repository.RemovePackage(new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion)));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteNuspec(string packageRoot, string packageName, string packageVersion)
    {
        string content = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{{packageName}}</id>
                <version>{{packageVersion}}</version>
                <authors>Test</authors>
                <description>Trusted snapshot test package.</description>
                <dependencies>
                  <group targetFramework="net10.0" />
                </dependencies>
              </metadata>
            </package>
            """;
        File.WriteAllText(Path.Combine(packageRoot, $"{packageName}.nuspec"), content);
    }

    private static string CreateMarketplaceArtifact(
        string packageRoot,
        byte[] manifest,
        string extractedAssembly)
    {
        string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        using (FileStream stream = new(artifactPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry assemblyEntry = archive.CreateEntry("lib/net10.0/Beutl.Telemetry.TestHost.dll");
            using (Stream destination = assemblyEntry.Open())
            using (FileStream source = File.OpenRead(extractedAssembly))
            {
                source.CopyTo(destination);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(AnalyticsFeatureManifest.PackagePath);
            using Stream manifestDestination = manifestEntry.Open();
            manifestDestination.Write(manifest);
        }

        string installedManifestPath = Path.Combine(
            packageRoot,
            "beutl",
            "analytics-features.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(installedManifestPath)!);
        File.WriteAllBytes(installedManifestPath, manifest);

        using FileStream artifact = File.OpenRead(artifactPath);
        return Convert.ToHexString(SHA256.HashData(artifact));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string GetBuildConfiguration()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return Directory.GetParent(baseDirectory)?.Name ?? "Debug";
    }
}
