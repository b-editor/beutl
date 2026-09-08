using System.Runtime.Versioning;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageInstallerDataTests
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
    public void GetPackageKind_ReadsTheReservedTag()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new[] { PackageKinds.MaterialTag }.GetPackageKind(), Is.EqualTo(PackageKind.Material));
            Assert.That(new[] { "fonts", PackageKinds.TemplateTag }.GetPackageKind(), Is.EqualTo(PackageKind.Template));
            Assert.That(new[] { "blur", "effect" }.GetPackageKind(), Is.EqualTo(PackageKind.Extension));
            // LocalPackage yields [""] for a nuspec with no <tags> element.
            Assert.That(new[] { "" }.GetPackageKind(), Is.EqualTo(PackageKind.Extension));
            Assert.That(((IEnumerable<string>?)null).GetPackageKind(), Is.EqualTo(PackageKind.Extension));
        });
    }

    [Test]
    public void GetPackageKind_DoesNotTreatABareMaterialTagAsTheKindMarker()
    {
        // "material" is an ordinary tag plenty of unrelated packages carry (e.g. Material
        // Design themes), so only the prefixed marker classifies a package.
        Assert.That(new[] { "material", "design", "theme" }.GetPackageKind(), Is.EqualTo(PackageKind.Extension));
    }

    [Test]
    public void GetPackageKind_ReturnsBoth_WhenBothReservedTagsArePresent()
    {
        Assert.That(
            new[] { PackageKinds.TemplateTag, PackageKinds.MaterialTag }.GetPackageKind(),
            Is.EqualTo(PackageKind.Both));
    }

    [Test]
    public void VisibleTags_DropsOnlyTheReservedTags()
    {
        Assert.That(
            new[] { PackageKinds.MaterialTag, "fonts", "cc0" }.VisibleTags(),
            Is.EqualTo(new[] { "fonts", "cc0" }));
    }

    [Test]
    public void ToQueryValue_MatchesTheServerVocabulary()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PackageKindFilter.All.ToQueryValue(), Is.EqualTo("all"));
            Assert.That(PackageKindFilter.Extension.ToQueryValue(), Is.EqualTo("extension"));
            Assert.That(PackageKindFilter.Material.ToQueryValue(), Is.EqualTo("material"));
            Assert.That(PackageKindFilter.Template.ToQueryValue(), Is.EqualTo("template"));
        });
    }

    [Test]
    public void InstallDataPackage_CopiesTemplatesIntoTheWatchedDirectory()
    {
        LocalPackage package = CreateDataPackage(
            "Beutl.Package.DataTest.Templates",
            PackageKinds.TemplateTag,
            ("templates/title.json", "{}"),
            ("templates/lower-thirds/name.json", "{}"));

        _installer.InstallDataPackage(package);

        string root = TemplatesDirectoryOf(package.Name);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(root, "title.json")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(root, "lower-thirds", "name.json")), Is.True,
                "nested template files must survive the copy");
        });
    }

    [Test]
    public void InstallDataPackage_CopiesMaterialsRecursively()
    {
        LocalPackage package = CreateDataPackage(
            "Beutl.Package.DataTest.Materials",
            PackageKinds.MaterialTag,
            ("materials/logo.png", "png"),
            ("materials/audio/sting.wav", "wav"));

        _installer.InstallDataPackage(package);

        string root = MaterialsDirectoryOf(package.Name);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(root, "logo.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "audio", "sting.wav")), Is.True);
            Assert.That(
                Directory.Exists(TemplatesDirectoryOf(package.Name)), Is.False,
                "a material package must not create a templates directory");
        });
    }

    [Test]
    public void InstallDataPackage_CopiesBothPayloads_WhenBothKindsAreTagged()
    {
        LocalPackage package = CreateDataPackage(
            "Beutl.Package.DataTest.Both",
            [PackageKinds.MaterialTag, PackageKinds.TemplateTag],
            "1.0.0",
            [("materials/logo.png", "png"), ("templates/title.json", "{}")]);

        _installer.InstallDataPackage(package);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(MaterialsDirectoryOf(package.Name), "logo.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(TemplatesDirectoryOf(package.Name), "title.json")), Is.True);
        });
    }

    [Test]
    public void InstallDataPackage_DropsFilesTheNewVersionNoLongerShips()
    {
        const string Name = "Beutl.Package.DataTest.Update";
        LocalPackage first = CreateDataPackage(
            Name, PackageKinds.TemplateTag, ("templates/old.json", "{}"), ("templates/kept.json", "{}"));
        _installer.InstallDataPackage(first);

        LocalPackage second = CreateDataPackage(
            Name, PackageKinds.TemplateTag, version: "2.0.0", files: [("templates/kept.json", "{ \"v\": 2 }")]);
        _installer.InstallDataPackage(second);

        string root = TemplatesDirectoryOf(Name);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(root, "old.json")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(root, "kept.json")), Is.EqualTo("{ \"v\": 2 }"));
        });
    }

    [Test]
    public void InstallDataPackage_LeavesNothingBehind_WhenThePackageShipsNoPayload()
    {
        LocalPackage package = CreateDataPackage("Beutl.Package.DataTest.Empty", PackageKinds.MaterialTag);

        Assert.DoesNotThrow(() => _installer.InstallDataPackage(package));

        Assert.That(Directory.Exists(MaterialsDirectoryOf(package.Name)), Is.False);
    }

    [Test]
    public void InstallDataPackage_Rejects_AnExtensionPackage()
    {
        LocalPackage package = CreateDataPackage(
            "Beutl.Package.DataTest.Extension", tag: "effects", files: [("templates/a.json", "{}")]);

        Assert.Throws<ArgumentException>(() => _installer.InstallDataPackage(package));
    }

    [Test]
    public void UninstallDataPackage_RemovesBothPayloadDirectories()
    {
        const string Name = "Beutl.Package.DataTest.Uninstall";
        LocalPackage templates = CreateDataPackage(Name, PackageKinds.TemplateTag, ("templates/a.json", "{}"));
        _installer.InstallDataPackage(templates);
        Directory.CreateDirectory(MaterialsDirectoryOf(Name));

        bool removed = _installer.UninstallDataPackage(Name);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(Directory.Exists(TemplatesDirectoryOf(Name)), Is.False);
            Assert.That(Directory.Exists(MaterialsDirectoryOf(Name)), Is.False);
        });
    }

    [Test]
    public void UninstallDataPackage_Succeeds_WhenThereIsNothingToRemove()
    {
        Assert.That(_installer.UninstallDataPackage("Beutl.Package.DataTest.Absent"), Is.True);
    }

    [Test]
    public void UninstallDataPackage_KeepsReportingFailure_WhileThePayloadCannotBeRemoved()
    {
        const string Name = "Beutl.Package.DataTest.Undeletable";
        LocalPackage materials = CreateDataPackage(Name, PackageKinds.MaterialTag, ("materials/a.png", "png"));
        _installer.InstallDataPackage(materials);

        string payload = MaterialsDirectoryOf(Name);
        using (BlockDeletion(payload))
        {
            Assert.Multiple(() =>
            {
                // Every attempt must keep reporting failure: the caller drops the
                // package's repository entry — the only record of this payload — as
                // soon as removal reports success.
                Assert.That(_installer.UninstallDataPackage(Name), Is.False);
                Assert.That(_installer.UninstallDataPackage(Name), Is.False);
                Assert.That(Directory.Exists(payload), Is.True);
            });
        }

        Assert.That(_installer.UninstallDataPackage(Name), Is.True);
    }

    // Makes the directory's contents undeletable: on Unix by clearing the parent's write
    // permission, on Windows by holding an exclusive handle on the file inside it.
    private static IDisposable BlockDeletion(string directory)
    {
        string file = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).First();
        if (OperatingSystem.IsWindows())
        {
            return File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        UnixFileMode original = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, original & ~UnixFileMode.UserWrite);
        return new UnixModeRestore(directory, original);
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class UnixModeRestore(string directory, UnixFileMode mode) : IDisposable
    {
        public void Dispose() => File.SetUnixFileMode(directory, mode);
    }

    [TestCase("..")]
    [TestCase("../evil")]
    [TestCase("")]
    public void UninstallDataPackage_Rejects_ANameThatEscapesTheHomeDirectory(string name)
    {
        Assert.Throws<ArgumentException>(() => _installer.UninstallDataPackage(name));
    }

    [Test]
    public void Clean_KeepsThePayload_WhenANewerIdentityOfTheSameIdSurvives()
    {
        // An update cleans the old identity while the new one keeps the payload
        // directory, which is keyed by package id alone.
        const string Name = "Beutl.Package.DataTest.CleanUpdate";
        var oldIdentity = new PackageIdentity(Name, NuGetVersion.Parse("1.0.0"));
        var newIdentity = new PackageIdentity(Name, NuGetVersion.Parse("2.0.0"));
        LocalPackage old = CreateDataPackage(Name, [PackageKinds.MaterialTag], "1.0.0", [("materials/a.png", "old")]);
        LocalPackage current = CreateDataPackage(Name, [PackageKinds.MaterialTag], "2.0.0", [("materials/a.png", "new")]);
        _installer.InstallDataPackage(current);
        _repository.AddPackage(oldIdentity);
        _repository.AddPackage(newIdentity);

        _installer.Clean(new PackageCleanContext([oldIdentity], 0), new Progress<double>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(MaterialsDirectoryOf(Name)), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(MaterialsDirectoryOf(Name), "a.png")), Is.EqualTo("new"));
            Assert.That(Directory.Exists(old.InstalledPath!), Is.False);
        });
    }

    [Test]
    public void Clean_KeepsTheExtractedPackage_WhenThePayloadCannotBeRemoved()
    {
        // PrepareForClean rediscovers candidates from extracted package directories, so
        // dropping the directory or the repository entry on a failed payload deletion
        // would leave nothing to retry from.
        const string Name = "Beutl.Package.DataTest.CleanRetry";
        var identity = new PackageIdentity(Name, NuGetVersion.Parse("1.0.0"));
        LocalPackage package = CreateDataPackage(Name, [PackageKinds.MaterialTag], "1.0.0", [("materials/a.png", "x")]);
        _installer.InstallDataPackage(package);
        _repository.AddPackage(identity);

        var context = new PackageCleanContext([identity], 0);
        using (BlockDeletion(MaterialsDirectoryOf(Name)))
        {
            _installer.Clean(context, new Progress<double>());
        }

        Assert.Multiple(() =>
        {
            Assert.That(context.FailedPackages, Is.Not.Empty);
            Assert.That(Directory.Exists(package.InstalledPath), Is.True);
            Assert.That(_repository.ExistsPackage(identity), Is.True);
            Assert.That(Directory.Exists(MaterialsDirectoryOf(Name)), Is.True);
        });

        // The retry now succeeds and takes everything with it.
        _installer.Clean(new PackageCleanContext([identity], 0), new Progress<double>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(MaterialsDirectoryOf(Name)), Is.False);
            Assert.That(Directory.Exists(package.InstalledPath), Is.False);
            Assert.That(_repository.ExistsPackage(identity), Is.False);
        });
    }

    [Test]
    public void Clean_RemovesThePayload_WhenTheLastIdentityGoes()
    {
        const string Name = "Beutl.Package.DataTest.CleanLast";
        LocalPackage package = CreateDataPackage(Name, [PackageKinds.MaterialTag], "1.0.0", [("materials/a.png", "x")]);
        _installer.InstallDataPackage(package);
        _repository.AddPackage(new PackageIdentity(Name, new NuGetVersion("1.0.0")));

        _installer.Clean(
            new PackageCleanContext([new PackageIdentity(Name, new NuGetVersion("1.0.0"))], 0),
            new Progress<double>());

        Assert.That(Directory.Exists(MaterialsDirectoryOf(Name)), Is.False);
    }

    [Test]
    public void Uninstall_AlsoRemovesThePayloadDirectory()
    {
        var identity = new PackageIdentity("Beutl.Package.DataTest.FullUninstall", NuGetVersion.Parse("1.0.0"));
        LocalPackage package = CreateDataPackage(
            identity.Id, PackageKinds.MaterialTag, ("materials/logo.png", "png"));
        _installer.InstallDataPackage(package);
        _repository.UpgradePackages(identity);

        _installer.Uninstall(
            new PackageUninstallContext(identity, package.InstalledPath)
            {
                UnnecessaryPackages = [identity],
                SizeToBeReleased = 1
            },
            new Progress<double>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(MaterialsDirectoryOf(identity.Id)), Is.False);
            Assert.That(Directory.Exists(package.InstalledPath), Is.False);
            Assert.That(_repository.ExistsPackage(identity), Is.False);
        });
    }

    [Test]
    public void Uninstall_ReportsAFailedPackage_WhenThePayloadCannotBeRemoved()
    {
        // UninstallViewModel keys its failure state off FailedPackages, so a payload that
        // survived has to appear there or the queued uninstall is dropped as successful.
        var identity = new PackageIdentity("Beutl.Package.DataTest.UninstallLocked", NuGetVersion.Parse("1.0.0"));
        LocalPackage package = CreateDataPackage(
            identity.Id, PackageKinds.MaterialTag, ("materials/logo.png", "png"));
        _installer.InstallDataPackage(package);
        _repository.UpgradePackages(identity);

        var context = new PackageUninstallContext(identity, package.InstalledPath)
        {
            UnnecessaryPackages = [identity],
            SizeToBeReleased = 1
        };
        using (BlockDeletion(MaterialsDirectoryOf(identity.Id)))
        {
            _installer.Uninstall(context, new Progress<double>());
        }

        Assert.Multiple(() =>
        {
            Assert.That(context.FailedPackages, Is.Not.Empty);
            Assert.That(Directory.Exists(MaterialsDirectoryOf(identity.Id)), Is.True);
        });
    }

    [Test]
    public void Uninstall_RemovesThePayload_EvenWhenTheExtractedPackageIsGone()
    {
        var identity = new PackageIdentity("Beutl.Package.DataTest.Orphan", NuGetVersion.Parse("1.0.0"));
        LocalPackage package = CreateDataPackage(
            identity.Id, PackageKinds.TemplateTag, ("templates/a.json", "{}"));
        _installer.InstallDataPackage(package);
        _repository.UpgradePackages(identity);

        string installedPath = package.InstalledPath;
        Directory.Delete(installedPath, recursive: true);

        _installer.Uninstall(
            new PackageUninstallContext(identity, installedPath) { UnnecessaryPackages = [identity] },
            new Progress<double>());

        Assert.That(Directory.Exists(TemplatesDirectoryOf(identity.Id)), Is.False);
    }

    private static string TemplatesDirectoryOf(string packageName)
    {
        return Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), packageName);
    }

    private static string MaterialsDirectoryOf(string packageName)
    {
        return Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), packageName);
    }

    private LocalPackage CreateDataPackage(
        string name,
        string tag,
        params (string RelativePath, string Content)[] files)
    {
        return CreateDataPackage(name, tag, "1.0.0", files);
    }

    private LocalPackage CreateDataPackage(
        string name,
        string tag,
        string version,
        (string RelativePath, string Content)[] files)
    {
        return CreateDataPackage(name, [tag], version, files);
    }

    private LocalPackage CreateDataPackage(
        string name,
        string[] tags,
        string version,
        (string RelativePath, string Content)[] files)
    {
        string directory = Path.Combine(Helper.InstallPath, $"{name}.{version}");
        Directory.CreateDirectory(directory);
        Track(directory);
        Track(TemplatesDirectoryOf(name));
        Track(MaterialsDirectoryOf(name));

        foreach ((string relativePath, string content) in files)
        {
            string file = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        // GetInstalledPath keys off the package's .nupkg, so InstalledPackageRepository
        // cannot register the identity without one.
        File.WriteAllText(Path.Combine(directory, $"{name}.{version}.nupkg"), "");

        return new LocalPackage
        {
            Name = name,
            Version = version,
            Tags = [.. tags],
            InstalledPath = directory
        };
    }

    private void Track(string directory)
    {
        if (!_createdDirectories.Contains(directory))
        {
            _createdDirectories.Add(directory);
        }
    }
}
