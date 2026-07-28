using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ElementFileNamingTests
{
    private string _tempDirectory = null!;
    private Uri _sceneUri = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-element-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _sceneUri = new Uri(Path.Combine(_tempDirectory, "project.scene"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void GetUri_uses_lowercase_n_format_id()
    {
        Guid id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        Uri result = ElementFileNaming.GetUri(_sceneUri, id);

        Assert.That(result.LocalPath,
            Is.EqualTo(Path.Combine(_tempDirectory, "0123456789abcdef0123456789abcdef.belm")));
    }

    [Test]
    public void GetUri_appends_first_available_collision_index()
    {
        Guid id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        File.WriteAllText(Path.Combine(_tempDirectory, $"{id:N}.belm"), "");
        File.WriteAllText(Path.Combine(_tempDirectory, $"{id:N}-1.belm"), "");

        Uri result = ElementFileNaming.GetUri(_sceneUri, id);

        Assert.That(result.LocalPath, Is.EqualTo(Path.Combine(_tempDirectory, $"{id:N}-2.belm")));
    }

    [Test]
    public void GetUri_rejects_non_file_uri()
    {
        Assert.That(
            () => ElementFileNaming.GetUri(new Uri("https://example.test/project.scene"), Guid.NewGuid()),
            Throws.ArgumentException);
    }
}
