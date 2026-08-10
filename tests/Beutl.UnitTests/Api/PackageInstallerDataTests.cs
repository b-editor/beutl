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
            Assert.That(new[] { "material" }.GetPackageKind(), Is.EqualTo(PackageKind.Material));
            Assert.That(new[] { "fonts", "template" }.GetPackageKind(), Is.EqualTo(PackageKind.Template));
            Assert.That(new[] { "blur", "effect" }.GetPackageKind(), Is.EqualTo(PackageKind.Extension));
            // LocalPackage yields [""] for a nuspec with no <tags> element.
            Assert.That(new[] { "" }.GetPackageKind(), Is.EqualTo(PackageKind.Extension));
            Assert.That(((IEnumerable<string>?)null).GetPackageKind(), Is.EqualTo(PackageKind.Extension));
        });
    }

    [Test]
    public void GetPackageKind_PrefersMaterial_WhenBothReservedTagsArePresent()
    {
        Assert.That(new[] { "template", "material" }.GetPackageKind(), Is.EqualTo(PackageKind.Material));
    }

    [Test]
    public void VisibleTags_DropsOnlyTheReservedTags()
    {
        Assert.That(
            new[] { "material", "fonts", "cc0" }.VisibleTags(),
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

    [TestCase("..")]
    [TestCase("../evil")]
    [TestCase("")]
    public void UninstallDataPackage_Rejects_ANameThatEscapesTheHomeDirectory(string name)
    {
        Assert.Throws<ArgumentException>(() => _installer.UninstallDataPackage(name));
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

        return new LocalPackage
        {
            Name = name,
            Version = version,
            Tags = [tag],
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
