using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using TelemetryService = Beutl.Services.Telemetry;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class InstalledPackageRepositoryTests
{
    private static string InstalledPackagesFile => Path.Combine(Helper.AppRoot, "installedPackages.json");

    [SetUp]
    public void SetUp()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
    }

    [TearDown]
    public void TearDown()
    {
        Assert.That(
            Helper.AppRoot,
            Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        File.Delete(InstalledPackagesFile);
    }

    [Test]
    public void GetPackageObservable_EmitsNull_WhenNotInstalled()
    {
        const string name = "Beutl.Package.UpdateTest.None";
        var repo = new InstalledPackageRepository();

        PackageIdentity? emitted = new(name, NuGetVersion.Parse("0.0.0"));
        repo.GetPackageObservable(name).Subscribe(x => emitted = x);

        Assert.That(emitted, Is.Null);
    }

    [Test]
    public void GetPackageObservable_EmitsInstalledIdentity_AfterUpgrade()
    {
        const string name = "Beutl.Package.UpdateTest.Upgrade";
        var repo = new InstalledPackageRepository();

        PackageIdentity? emitted = new(name, NuGetVersion.Parse("0.0.0"));
        repo.GetPackageObservable(name).Subscribe(x => emitted = x);

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")));
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("1.0.0"));

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")));
        Assert.That(emitted?.Version.ToString(), Is.EqualTo("2.0.0"));
    }

    [Test]
    public void GetObservable_Bool_DoesNotFlashFalse_DuringUpgrade()
    {
        const string name = "Beutl.Package.UpdateTest.Flash";
        var repo = new InstalledPackageRepository();
        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")));

        var emissions = new List<bool>();
        repo.GetObservable(name).Subscribe(x => emissions.Add(x));

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse("2.0.0")));

        Assert.That(emissions, Does.Not.Contains(false));
    }

    [Test]
    public void GetObservable_ForVersion_TracksEquivalentIdentityInstances()
    {
        const string name = "Beutl.Package.UpdateTest.Version";
        const string version = "1.0.0";
        var repo = new InstalledPackageRepository();
        var emissions = new List<bool>();
        repo.GetObservable(name, version).Subscribe(emissions.Add);

        repo.UpgradePackages(new PackageIdentity(name, NuGetVersion.Parse(version)));
        repo.RemovePackage(new PackageIdentity(name, NuGetVersion.Parse(version)));

        Assert.That(emissions, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void PackageInstallContext_PersistsOnlyAnActuallyVerifiedMarketplaceArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-package-artifact-{Guid.NewGuid():N}");
        try
        {
            string installed = Path.Combine(root, "installed");
            string packageFile = Path.Combine(root, "source.nupkg");
            Directory.CreateDirectory(installed);
            byte[] manifestBytes = Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.BlurExtension"}]}]}""");
            string manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
            using (FileStream packageStream = File.Create(packageFile))
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(AnalyticsFeatureManifest.PackagePath);
                using Stream entryStream = entry.Open();
                entryStream.Write(manifestBytes);
            }

            byte[] packageBytes = File.ReadAllBytes(packageFile);
            string packageSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
            Assert.That(
                AnalyticsFeatureManifest.TryParse(manifestBytes, manifestSha256, out AnalyticsFeatureManifest? manifest),
                Is.True);
            var context = new PackageInstallContext(
                "Beutl.Package.AnalyticsTrustTest",
                "1.0.0",
                "https://example.test/package")
            {
                MarketplacePackageId = "Beutl.Package.AnalyticsTrustTest",
                NuGetPackageFile = packageFile,
                Asset = new FileResponse
                {
                    Id = "asset",
                    Name = "package.nupkg",
                    ContentType = "application/octet-stream",
                    DownloadUrl = "https://example.test/package",
                    Size = packageBytes.Length,
                    Sha256 = packageSha256
                },
                AnalyticsManifest = manifest
            };

            Assert.That(context.PersistVerifiedAnalyticsArtifact(installed), Is.Null);

            context.HashVerified = true;
            PackageAnalyticsProvenance? provenance = context.PersistVerifiedAnalyticsArtifact(installed);

            Assert.Multiple(() =>
            {
                Assert.That(provenance, Is.Not.Null);
                Assert.That(provenance!.IsVerified, Is.True);
                Assert.That(
                    provenance.CanonicalMarketplacePackageId,
                    Is.EqualTo("beutl.package.analyticstrusttest"));
                Assert.That(provenance.PackageSha256, Is.EqualTo(packageSha256));
                Assert.That(provenance.ApprovedManifestSha256, Is.EqualTo(manifestSha256));
                Assert.That(File.ReadAllBytes(PackageAnalyticsProvenance.GetArtifactPath(installed)),
                    Is.EqualTo(packageBytes));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void VerifiedAnalyticsProvenance_PersistsAcrossRepositoryRestore()
    {
        const string packageName = "Beutl.Package.AnalyticsPersistenceTest";
        const string packageVersion = "1.0.0";
        const string packageSha256 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        const string manifestSha256 = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";
        var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
        var repository = new InstalledPackageRepository();
        repository.UpgradePackages(identity);
        repository.SetAnalyticsProvenance(
            identity,
            PackageAnalyticsProvenance.CreateVerified(packageName, packageSha256, manifestSha256)!);

        var restored = new InstalledPackageRepository();

        Assert.That(restored.TryGetVerifiedAnalyticsProvenance(identity, out PackageAnalyticsProvenance? provenance), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(provenance!.PackageSha256, Is.EqualTo(packageSha256));
            Assert.That(provenance.ApprovedManifestSha256, Is.EqualTo(manifestSha256));
            Assert.That(
                provenance.CanonicalMarketplacePackageId,
                Is.EqualTo("beutl.package.analyticspersistencetest"));
        });
    }

    [Test]
    public async Task TrustedFeatureAttribution_RequiresVerifiedProvenanceAndIsRemovedOnUnload()
    {
        const string packageName = "Beutl.Package.AnalyticsTrustTest";
        const string secondPackageName = "Beutl.Package.SecondAnalyticsTrustTest";
        const string packageVersion = "1.0.0";
        string packageRoot = Path.GetDirectoryName(typeof(TrustedFeatureExtension).Assembly.Location)!;
        string manifestDirectory = Path.Combine(packageRoot, "beutl");
        string manifestPath = Path.Combine(manifestDirectory, "analytics-features.v1.json");
        bool manifestDirectoryExisted = Directory.Exists(manifestDirectory);
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            {"schemaVersion":1,"features":[{"kind":"effect","key":"trusted-blur","types":[{"assembly":"{{typeof(TrustedFeatureExtension).Assembly.GetName().Name}}","type":"{{typeof(TrustedFeatureExtension).FullName}}"},{"assembly":"{{typeof(TrustedFeatureExtensionTwo).Assembly.GetName().Name}}","type":"{{typeof(TrustedFeatureExtensionTwo).FullName}}"}]}]}
            """);

        try
        {
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllBytes(manifestPath, manifest);
            string manifestHash = Convert.ToHexString(SHA256.HashData(manifest));
            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            var secondIdentity = new PackageIdentity(secondPackageName, NuGetVersion.Parse(packageVersion));
            var repository = new InstalledPackageRepository();
            repository.UpgradePackages(identity);
            repository.UpgradePackages(secondIdentity);
            var commandManager = new ContextCommandManager(
                new ContextCommandSettingsStore(),
                new ContextCommandHandlerRegistry());
            var manager = new PackageManager(
                repository,
                new ExtensionProvider(),
                commandManager,
                apiApplication: null!);
            var package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };

            TelemetryService.UnregisterTrustedFeature(typeof(TrustedFeatureExtension));
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(TrustedFeatureExtension)]);

            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(
                    packageName,
                    "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
                    manifestHash)!);
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(TrustedFeatureExtension)]);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
            Directory.CreateDirectory(artifactPath);
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(TrustedFeatureExtension)]);

            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);
            Directory.Delete(artifactPath);

            string packageHash = CreateMarketplaceArtifact(packageRoot, manifest, tamperExpandedAssembly: false);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified("arbitrary.local-name", packageHash, manifestHash)!);
            manager.LoadExtensionsAndRegister(null, package, [], null, [typeof(TrustedFeatureExtension)]);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageHash, manifestHash)!);
            TrustedPackageSnapshot? unboundSnapshot = CreateTrustedSnapshot(
                repository,
                package,
                typeof(TrustedFeatureExtension).Assembly,
                markLoadedAssembly: false);
            Assert.That(unboundSnapshot, Is.Not.Null);
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(TrustedFeatureExtension)],
                unboundSnapshot);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            TrustedPackageSnapshot? trustedSnapshot = CreateTrustedSnapshot(
                repository,
                package,
                typeof(TrustedFeatureExtension).Assembly);
            Assert.That(trustedSnapshot, Is.Not.Null);
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(TrustedFeatureExtension)],
                trustedSnapshot);

            Assert.That(
                TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)),
                Is.EqualTo("extension/beutl.package.analyticstrusttest/effect/trusted-blur"));

            Assert.That(await manager.Unload(package), Is.True);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));

            File.AppendAllText(artifactPath, "tamper");
            manager.LoadExtensionsAndRegister(null, package, [], null, [typeof(TrustedFeatureExtension)]);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            string missingArchiveManifestHash = CreateMarketplaceArtifact(
                packageRoot,
                manifest,
                tamperExpandedAssembly: false,
                includeManifest: false);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, missingArchiveManifestHash, manifestHash)!);
            manager.LoadExtensionsAndRegister(null, package, [], null, [typeof(TrustedFeatureExtension)]);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            string expandedTamperHash = CreateMarketplaceArtifact(
                packageRoot,
                manifest,
                tamperExpandedAssembly: true);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, expandedTamperHash, manifestHash)!);
            manager.LoadExtensionsAndRegister(null, package, [], null, [typeof(TrustedFeatureExtension)]);
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)), Is.EqualTo("generic"));
            Assert.That(await manager.Unload(package), Is.True);

            packageHash = CreateMarketplaceArtifact(packageRoot, manifest, tamperExpandedAssembly: false);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageHash, manifestHash)!);
            repository.SetAnalyticsProvenance(
                secondIdentity,
                PackageAnalyticsProvenance.CreateVerified(secondPackageName, packageHash, manifestHash)!);
            var secondPackage = new LocalPackage
            {
                Name = secondPackageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };
            trustedSnapshot = CreateTrustedSnapshot(
                repository,
                package,
                typeof(TrustedFeatureExtension).Assembly);
            TrustedPackageSnapshot? secondTrustedSnapshot = CreateTrustedSnapshot(
                repository,
                secondPackage,
                typeof(TrustedFeatureExtensionTwo).Assembly);
            Assert.Multiple(() =>
            {
                Assert.That(trustedSnapshot, Is.Not.Null);
                Assert.That(secondTrustedSnapshot, Is.Not.Null);
            });
            manager.LoadExtensionsAndRegister(
                null,
                package,
                [],
                null,
                [typeof(TrustedFeatureExtension)],
                trustedSnapshot);
            manager.LoadExtensionsAndRegister(
                null,
                secondPackage,
                [],
                null,
                [typeof(TrustedFeatureExtensionTwo)],
                secondTrustedSnapshot);

            Assert.Multiple(() =>
            {
                Assert.That(
                    TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtension)),
                    Is.EqualTo("extension/beutl.package.analyticstrusttest/effect/trusted-blur"));
                Assert.That(
                    TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureExtensionTwo)),
                    Is.EqualTo("extension/beutl.package.secondanalyticstrusttest/effect/trusted-blur"));
            });
            Assert.That(await manager.Unload(package), Is.True);
            Assert.That(await manager.Unload(secondPackage), Is.True);
        }
        finally
        {
            TelemetryService.UnregisterTrustedFeature(typeof(TrustedFeatureExtension));
            TelemetryService.UnregisterTrustedFeature(typeof(TrustedFeatureExtensionTwo));
            string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
            if (File.Exists(artifactPath)) File.Delete(artifactPath);
            if (Directory.Exists(artifactPath)) Directory.Delete(artifactPath);
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            if (!manifestDirectoryExisted && Directory.Exists(manifestDirectory))
            {
                Directory.Delete(manifestDirectory);
            }
        }
    }

    [Test]
    public void TrustedPackageSnapshot_RejectsExtractedMismatchAndLoadsItsCapturedBytesAfterReplacement()
    {
        const string packageName = "Beutl.Package.SnapshotRaceTest";
        const string packageVersion = "1.0.0";
        string root = Path.Combine(Path.GetTempPath(), $"beutl-package-snapshot-{Guid.NewGuid():N}");
        try
        {
            string packageRoot = Path.Combine(root, "package");
            string libDirectory = Path.Combine(packageRoot, "lib", "net10.0");
            string sourceAssembly = typeof(SnapshotTrustedFeatureExtension).Assembly.Location;
            string extractedAssembly = Path.Combine(libDirectory, Path.GetFileName(sourceAssembly));
            Directory.CreateDirectory(libDirectory);
            File.Copy(sourceAssembly, extractedAssembly);

            string assemblyName = typeof(SnapshotTrustedFeatureExtension).Assembly.GetName().Name!;
            string typeName = typeof(SnapshotTrustedFeatureExtension).FullName!;
            byte[] manifest = Encoding.UTF8.GetBytes(
                $$"""
                {"schemaVersion":1,"features":[{"kind":"effect","key":"snapshot-race","types":[{"assembly":"{{assemblyName}}","type":"{{typeName}}"}]}]}
                """);
            string manifestHash = Convert.ToHexString(SHA256.HashData(manifest));
            string packageHash = CreateMarketplaceArtifactFromAssembly(packageRoot, manifest, extractedAssembly);

            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            var repository = new InstalledPackageRepository();
            repository.UpgradePackages(identity);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageHash, manifestHash)!);
            var package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };
            var layout = new PackageLoadLayout(null!, libDirectory, [extractedAssembly]);

            File.WriteAllBytes(extractedAssembly, [1, 2, 3, 4]);
            Assert.That(TrustedPackageSnapshot.TryCreate(repository, package, layout), Is.Null);

            File.Copy(sourceAssembly, extractedAssembly, overwrite: true);
            TrustedPackageSnapshot? snapshot = TrustedPackageSnapshot.TryCreate(repository, package, layout);
            Assert.That(snapshot, Is.Not.Null);

            // Simulate a replacement after verification but before the loader starts.
            // A verified load must consume snapshot bytes rather than reopening this path.
            File.WriteAllBytes(extractedAssembly, [5, 6, 7, 8]);
            var context = new PluginLoadContext(libDirectory, null, snapshot);
            Assembly loaded = context.LoadPackageAssembly(extractedAssembly);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.GetName().Name, Is.EqualTo(assemblyName));
                Assert.That(loaded.Location, Is.Empty);
                Assert.That(snapshot!.IsVerifiedAssembly(loaded), Is.True);
                Assert.That(snapshot.Manifest.Find(assemblyName, typeName), Is.Not.Null);
            });
            context.Unload();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void TrustedPackageSnapshot_RejectsNativeRuntimeAssetsForExactAttribution()
    {
        const string packageName = "Beutl.Package.NativeSnapshotTest";
        const string packageVersion = "1.0.0";
        string root = Path.Combine(Path.GetTempPath(), $"beutl-package-native-snapshot-{Guid.NewGuid():N}");
        try
        {
            string packageRoot = Path.Combine(root, "package");
            string libDirectory = Path.Combine(packageRoot, "lib", "net10.0");
            string sourceAssembly = typeof(SnapshotTrustedFeatureExtension).Assembly.Location;
            string extractedAssembly = Path.Combine(libDirectory, Path.GetFileName(sourceAssembly));
            Directory.CreateDirectory(libDirectory);
            File.Copy(sourceAssembly, extractedAssembly);

            string assemblyName = typeof(SnapshotTrustedFeatureExtension).Assembly.GetName().Name!;
            string typeName = typeof(SnapshotTrustedFeatureExtension).FullName!;
            byte[] manifest = Encoding.UTF8.GetBytes(
                $$"""
                {"schemaVersion":1,"features":[{"kind":"effect","key":"native-snapshot","types":[{"assembly":"{{assemblyName}}","type":"{{typeName}}"}]}]}
                """);
            string manifestHash = Convert.ToHexString(SHA256.HashData(manifest));
            string packageHash = CreateMarketplaceArtifactFromAssembly(
                packageRoot,
                manifest,
                extractedAssembly,
                includeNativeRuntimeAsset: true);

            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            var repository = new InstalledPackageRepository();
            repository.UpgradePackages(identity);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageHash, manifestHash)!);
            var package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };

            Assert.That(
                TrustedPackageSnapshot.TryCreate(
                    repository,
                    package,
                    new PackageLoadLayout(null!, libDirectory, [extractedAssembly])),
                Is.Null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void TrustedPackageSnapshot_RejectsArtifactsBeyondItsBoundedMemoryBudget()
    {
        const string packageName = "Beutl.Package.OversizedArtifactTest";
        const string packageVersion = "1.0.0";
        string root = Path.Combine(Path.GetTempPath(), $"beutl-package-oversized-artifact-{Guid.NewGuid():N}");
        try
        {
            string packageRoot = Path.Combine(root, "package");
            string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            using (var artifact = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                artifact.SetLength(TrustedPackageSnapshot.MaxArtifactBytes + 1);
            }

            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            var repository = new InstalledPackageRepository();
            repository.UpgradePackages(identity);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(
                    packageName,
                    new string('A', 64),
                    new string('B', 64))!);
            var package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };

            Assert.That(
                TrustedPackageSnapshot.TryCreate(
                    repository,
                    package,
                    new PackageLoadLayout(null!, Path.Combine(packageRoot, "lib"), [])),
                Is.Null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void TrustedPackageSnapshot_RejectsExpandedManagedBytesBeyondItsBoundedBudget()
    {
        const string packageName = "Beutl.Package.OversizedAssemblyTest";
        const string packageVersion = "1.0.0";
        string root = Path.Combine(Path.GetTempPath(), $"beutl-package-oversized-assembly-{Guid.NewGuid():N}");
        try
        {
            string packageRoot = Path.Combine(root, "package");
            string libDirectory = Path.Combine(packageRoot, "lib", "net10.0");
            string sourceAssembly = typeof(SnapshotTrustedFeatureExtension).Assembly.Location;
            string extractedAssembly = Path.Combine(libDirectory, Path.GetFileName(sourceAssembly));
            Directory.CreateDirectory(libDirectory);
            File.Copy(sourceAssembly, extractedAssembly);

            string assemblyName = typeof(SnapshotTrustedFeatureExtension).Assembly.GetName().Name!;
            string typeName = typeof(SnapshotTrustedFeatureExtension).FullName!;
            byte[] manifest = Encoding.UTF8.GetBytes(
                $$"""
                {"schemaVersion":1,"features":[{"kind":"effect","key":"oversized-assembly","types":[{"assembly":"{{assemblyName}}","type":"{{typeName}}"}]}]}
                """);
            string manifestHash = Convert.ToHexString(SHA256.HashData(manifest));
            string packageHash = CreateMarketplaceArtifactFromAssembly(
                packageRoot,
                manifest,
                extractedAssembly,
                oversizedManagedAssemblyLength: TrustedPackageSnapshot.MaxCapturedAssemblyBytes + 1);

            var identity = new PackageIdentity(packageName, NuGetVersion.Parse(packageVersion));
            var repository = new InstalledPackageRepository();
            repository.UpgradePackages(identity);
            repository.SetAnalyticsProvenance(
                identity,
                PackageAnalyticsProvenance.CreateVerified(packageName, packageHash, manifestHash)!);
            var package = new LocalPackage
            {
                Name = packageName,
                Version = packageVersion,
                InstalledPath = packageRoot
            };
            string oversizedAssemblyPath = Path.Combine(libDirectory, "oversized.dll");

            Assert.That(
                TrustedPackageSnapshot.TryCreate(
                    repository,
                    package,
                    new PackageLoadLayout(null!, libDirectory, [oversizedAssemblyPath])),
                Is.Null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateMarketplaceArtifact(
        string packageRoot,
        byte[] manifest,
        bool tamperExpandedAssembly,
        bool includeManifest = true)
    {
        string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        using (FileStream stream = new(artifactPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            string assemblyPath = typeof(TrustedFeatureExtension).Assembly.Location;
            string relativeAssemblyPath = Path.GetRelativePath(packageRoot, assemblyPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            ZipArchiveEntry assemblyEntry = archive.CreateEntry(relativeAssemblyPath);
            using (Stream destination = assemblyEntry.Open())
            {
                if (tamperExpandedAssembly)
                {
                    destination.Write([1, 2, 3, 4]);
                }
                else
                {
                    using FileStream source = File.OpenRead(assemblyPath);
                    source.CopyTo(destination);
                }
            }

            if (includeManifest)
            {
                ZipArchiveEntry manifestEntry = archive.CreateEntry(AnalyticsFeatureManifest.PackagePath);
                using Stream manifestDestination = manifestEntry.Open();
                manifestDestination.Write(manifest);
            }
        }

        using FileStream artifact = File.OpenRead(artifactPath);
        return Convert.ToHexString(SHA256.HashData(artifact));
    }

    private static string CreateMarketplaceArtifactFromAssembly(
        string packageRoot,
        byte[] manifest,
        string extractedAssembly,
        bool includeNativeRuntimeAsset = false,
        long oversizedManagedAssemblyLength = 0)
    {
        string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        using (FileStream stream = new(artifactPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            string relativeAssemblyPath = Path.GetRelativePath(packageRoot, extractedAssembly)
                .Replace(Path.DirectorySeparatorChar, '/');
            ZipArchiveEntry assemblyEntry = archive.CreateEntry(relativeAssemblyPath);
            using (Stream destination = assemblyEntry.Open())
            using (FileStream source = File.OpenRead(extractedAssembly))
            {
                source.CopyTo(destination);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(AnalyticsFeatureManifest.PackagePath);
            using (Stream manifestDestination = manifestEntry.Open())
            {
                manifestDestination.Write(manifest);
            }

            if (includeNativeRuntimeAsset)
            {
                ZipArchiveEntry nativeEntry = archive.CreateEntry("runtimes/win-x64/native/test-native.dll");
                using Stream nativeDestination = nativeEntry.Open();
                nativeDestination.Write([1, 2, 3, 4]);
            }

            if (oversizedManagedAssemblyLength > 0)
            {
                ZipArchiveEntry oversizedEntry = archive.CreateEntry("lib/net10.0/oversized.dll");
                using Stream oversizedDestination = oversizedEntry.Open();
                WriteZeros(oversizedDestination, oversizedManagedAssemblyLength);
            }
        }

        string manifestPath = Path.Combine(packageRoot, "beutl", "analytics-features.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, manifest);
        using FileStream artifact = File.OpenRead(artifactPath);
        return Convert.ToHexString(SHA256.HashData(artifact));
    }

    private static void WriteZeros(Stream stream, long length)
    {
        byte[] buffer = new byte[8192];
        while (length > 0)
        {
            int count = (int)Math.Min(buffer.Length, length);
            stream.Write(buffer, 0, count);
            length -= count;
        }
    }

    private static TrustedPackageSnapshot? CreateTrustedSnapshot(
        InstalledPackageRepository repository,
        LocalPackage package,
        Assembly assembly,
        bool markLoadedAssembly = true)
    {
        string packageRoot = Path.GetFullPath(package.InstalledPath!);
        var layout = new PackageLoadLayout(
            null!,
            Path.Combine(packageRoot, "lib"),
            [assembly.Location]);
        TrustedPackageSnapshot? snapshot = TrustedPackageSnapshot.TryCreate(repository, package, layout);
        if (markLoadedAssembly)
        {
            snapshot?.RegisterLoadedAssembly(assembly);
        }
        return snapshot;
    }

    [Export]
    private sealed class TrustedFeatureExtension : Extension;

    [Export]
    private sealed class TrustedFeatureExtensionTwo : Extension;

    [Export]
    public sealed class SnapshotTrustedFeatureExtension : Extension;
}
