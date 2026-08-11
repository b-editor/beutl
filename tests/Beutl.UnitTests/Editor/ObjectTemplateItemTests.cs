using System.Text;
using System.Text.Json;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ObjectTemplateItemTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "materials", "pkg"));
        Directory.CreateDirectory(Path.Combine(_root, "templates", "pkg"));
        File.WriteAllText(Path.Combine(_root, "materials", "pkg", "logo.png"), "png");
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
    public void FileSourceJsonConverter_ResolvesRelativeUri_AgainstTheParentBaseUri()
    {
        // A template that references a bundled material stores the reference as a URI
        // relative to the template file; the converter must resolve it against the
        // template file's URI, which ObjectTemplateItem.CreateInstance now provides.
        var context = new JsonSerializationContext(
            typeof(TestEngineObjectWithFileSource),
            options: new CoreSerializerOptions
            {
                BaseUri = new Uri(Path.Combine(_root, "templates", "pkg", "title.json")),
            });

        using (ThreadLocalSerializationContext.Enter(context))
        {
            var source = JsonSerializer.Deserialize<IFileSource>(
                "\"../../materials/pkg/logo.png\"", JsonHelper.SerializerOptions) as BlobFileSource;

            // A BlobFileSource reads the resolved file, so the bytes prove the
            // relative URI landed on the bundled material.
            Assert.That(source, Is.Not.Null);
            Assert.That(Encoding.UTF8.GetString(source!.Data), Is.EqualTo("png"));
        }
    }
}

[SuppressResourceClassGeneration]
public sealed class TestEngineObjectWithFileSource : EngineObject
{
    public TestEngineObjectWithFileSource(IFileSource? fileSource)
    {
        ScanProperties<TestEngineObjectWithFileSource>();
        FileSource.CurrentValue = fileSource;
    }

    public IProperty<IFileSource?> FileSource { get; } = Property.Create<IFileSource?>();
}
