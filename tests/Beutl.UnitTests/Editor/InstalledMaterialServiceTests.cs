using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class InstalledMaterialServiceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-materials-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Scan_ReturnsNothing_WhenTheDirectoryIsMissing()
    {
        Assert.That(InstalledMaterialService.Scan(Path.Combine(_root, "absent")), Is.Empty);
    }

    [Test]
    public void Scan_AttributesEachFileToTheInstallingPackage()
    {
        Write("Beutl.Materials.Fonts/regular.ttf");
        Write("Beutl.Materials.Photos/nested/city.png");

        InstalledMaterial[] items = InstalledMaterialService.Scan(_root);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(x => x.PackageName),
                Is.EqualTo(new[] { "Beutl.Materials.Fonts", "Beutl.Materials.Photos" }));
            Assert.That(items.Select(x => x.Name), Is.EqualTo(new[] { "regular.ttf", "city.png" }));
            Assert.That(items[1].FilePath, Is.EqualTo(Path.Combine(_root, "Beutl.Materials.Photos", "nested", "city.png")));
        });
    }

    [Test]
    public void Scan_LeavesThePackageEmpty_ForAFileAtTheRoot()
    {
        Write("loose.png");

        InstalledMaterial[] items = InstalledMaterialService.Scan(_root);

        Assert.That(items.Single().PackageName, Is.Empty);
    }

    [Test]
    public void Scan_OrdersByPackageThenName()
    {
        Write("b-package/b.png");
        Write("b-package/a.png");
        Write("a-package/z.png");

        InstalledMaterial[] items = InstalledMaterialService.Scan(_root);

        Assert.That(
            items.Select(x => $"{x.PackageName}/{x.Name}"),
            Is.EqualTo(new[] { "a-package/z.png", "b-package/a.png", "b-package/b.png" }));
    }

    [TestCase("a.png", InstalledMaterialKind.Image)]
    [TestCase("a.JPEG", InstalledMaterialKind.Image)]
    [TestCase("a.wav", InstalledMaterialKind.Audio)]
    [TestCase("a.mp3", InstalledMaterialKind.Audio)]
    [TestCase("a.mp4", InstalledMaterialKind.Video)]
    [TestCase("a.ttf", InstalledMaterialKind.Font)]
    [TestCase("a.otf", InstalledMaterialKind.Font)]
    [TestCase("a.txt", InstalledMaterialKind.Other)]
    [TestCase("a", InstalledMaterialKind.Other)]
    public void ClassifyByExtension_GroupsByMediaType(string fileName, InstalledMaterialKind expected)
    {
        Assert.That(InstalledMaterialService.ClassifyByExtension(fileName), Is.EqualTo(expected));
    }

    [Test]
    public void Scan_ListsAFileOfAnUnknownKind()
    {
        // The drop target decides what it accepts, so an unrecognized file is still listed.
        Write("pack/readme.txt");

        InstalledMaterial[] items = InstalledMaterialService.Scan(_root);

        Assert.That(items.Single().Kind, Is.EqualTo(InstalledMaterialKind.Other));
    }

    private void Write(string relativePath)
    {
        string file = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "content");
    }
}
