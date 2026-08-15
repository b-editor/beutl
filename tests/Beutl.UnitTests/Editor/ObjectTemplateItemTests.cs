using System.Text;
using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void CreateInstance_ResolvesRelativeUri_AgainstTheTemplateFile()
    {
        // A template that references a bundled material stores the reference as a URI
        // relative to the template file; CreateInstance must resolve it against the
        // template's own location.
        var obj = new TestEngineObjectWithFileSource(
            new TestFileSource(new Uri(Path.Combine(_root, "materials", "pkg", "logo.png"))));
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(obj);

        string json = serialized.ToJsonString()
            .Replace("file://" + Path.Combine(_root, "materials", "pkg", "logo.png"), "../../materials/pkg/logo.png");
        JsonObject jsonObj = JsonNode.Parse(json)!.AsObject();

        string templatePath = Path.Combine(_root, "templates", "pkg", "title.json");
        var item = new ObjectTemplateItem(
            Guid.NewGuid(), typeof(TestEngineObjectWithFileSource), typeof(TestEngineObjectWithFileSource),
            jsonObj, "title", "", templatePath);

        var instance = item.CreateInstance() as TestEngineObjectWithFileSource;

        Assert.That(instance, Is.Not.Null);
        Assert.That(
            (instance!.FileSource.CurrentValue as BlobFileSource)?.Data,
            Is.EqualTo(Encoding.UTF8.GetBytes("png")));
    }

    // `new Uri(path)` reads a `#` in the file name as a fragment and truncates the base
    // path, so the relative reference would resolve against the wrong directory.
    [Test]
    public void CreateInstance_ResolvesRelativeUri_WhenTheTemplatePathHasAReservedCharacter()
    {
        Directory.CreateDirectory(Path.Combine(_root, "templates", "pkg#dark"));
        var obj = new TestEngineObjectWithFileSource(
            new TestFileSource(new Uri(Path.Combine(_root, "materials", "pkg", "logo.png"))));
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(obj);

        string json = serialized.ToJsonString()
            .Replace("file://" + Path.Combine(_root, "materials", "pkg", "logo.png"), "../../materials/pkg/logo.png");
        JsonObject jsonObj = JsonNode.Parse(json)!.AsObject();

        string templatePath = Path.Combine(_root, "templates", "pkg#dark", "title.json");
        var item = new ObjectTemplateItem(
            Guid.NewGuid(), typeof(TestEngineObjectWithFileSource), typeof(TestEngineObjectWithFileSource),
            jsonObj, "title", "", templatePath);

        var instance = item.CreateInstance() as TestEngineObjectWithFileSource;

        Assert.That(instance, Is.Not.Null);
        Assert.That(
            (instance!.FileSource.CurrentValue as BlobFileSource)?.Data,
            Is.EqualTo(Encoding.UTF8.GetBytes("png")));
    }

    [Test]
    public void Preview_RoundTripsThroughJson()
    {
        byte[] preview = [1, 2, 3, 250, 251, 252];
        ObjectTemplateItem item = CreateItem();
        item.Preview = preview;

        ObjectTemplateItem? restored = RoundTrip(item);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Preview, Is.EqualTo(preview));
    }

    // Templates saved before previews existed, and packages authored against the old schema,
    // must keep loading.
    [Test]
    public void FromJson_WithoutPreview_LeavesItNull()
    {
        ObjectTemplateItem? restored = RoundTrip(CreateItem());

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Preview, Is.Null);
        Assert.That(restored.ActualType, Is.EqualTo(typeof(TestEngineObjectWithFileSource)));
    }

    [TestCase("not base64 at all!!")]
    [TestCase("")]
    public void FromJson_WithCorruptPreview_KeepsTheTemplate(string base64)
    {
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = base64;

        ObjectTemplateItem? restored = ObjectTemplateItem.FromJson(
            json, "title", Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Preview, Is.Null);
    }

    // Nothing validates a template file, so an oversized preview must cost the thumbnail, not the
    // template — and must not be allocated before it is rejected.
    [Test]
    public void FromJson_WithOversizedPreview_KeepsTheTemplate()
    {
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = new string('A', 4_000_000);

        ObjectTemplateItem? restored = ObjectTemplateItem.FromJson(
            json, "title", Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Preview, Is.Null);
        Assert.That(restored.ActualType, Is.EqualTo(typeof(TestEngineObjectWithFileSource)));
    }

    [Test]
    public void FromJson_DetachesTheTemplatePayloadFromTheParsedDocument()
    {
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = Convert.ToBase64String([1, 2, 3]);

        ObjectTemplateItem? restored = ObjectTemplateItem.FromJson(
            json, "title", Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Json.Parent, Is.Null);
    }

    [Test]
    public void FromJson_WithNonStringPreview_KeepsTheTemplate()
    {
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = 42;

        ObjectTemplateItem? restored = ObjectTemplateItem.FromJson(
            json, "title", Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Preview, Is.Null);
    }

    private ObjectTemplateItem CreateItem()
    {
        return ObjectTemplateItem.CreateFromInstance(new TestEngineObjectWithFileSource(), "title");
    }

    private ObjectTemplateItem? RoundTrip(ObjectTemplateItem item)
    {
        JsonNode json = ObjectTemplateItem.ToJson(item);
        return ObjectTemplateItem.FromJson(
            json, item.Name.Value, Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);
    }
}

public sealed class TestFileSource : IFileSource
{
    public TestFileSource()
    {
    }

    public TestFileSource(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; private set; } = null!;

    public void ReadFrom(Uri uri)
    {
        Uri = uri;
    }
}

[SuppressResourceClassGeneration]
public sealed class TestEngineObjectWithFileSource : EngineObject
{
    public TestEngineObjectWithFileSource() : this(null)
    {
    }

    public TestEngineObjectWithFileSource(IFileSource? fileSource)
    {
        ScanProperties<TestEngineObjectWithFileSource>();
        FileSource.CurrentValue = fileSource;
    }

    public IProperty<IFileSource?> FileSource { get; } = Property.Create<IFileSource?>();
}
