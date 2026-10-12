using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.IO;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
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

    // With "pkg#dark", `new Uri(path)` reads a `#` in the file name as a fragment and truncates the
    // base path, so the relative reference would resolve against the wrong directory.
    [TestCase("pkg")]
    [TestCase("pkg#dark")]
    public void CreateInstance_ResolvesRelativeUri_AgainstTheTemplateFile(string templateDirectory)
    {
        // A template that references a bundled material stores the reference as a URI
        // relative to the template file; CreateInstance must resolve it against the
        // template's own location.
        Directory.CreateDirectory(Path.Combine(_root, "templates", templateDirectory));
        var obj = new TestEngineObjectWithFileSource(
            new TestFileSource(new Uri(Path.Combine(_root, "materials", "pkg", "logo.png"))));
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(obj);

        string json = serialized.ToJsonString()
            .Replace("file://" + Path.Combine(_root, "materials", "pkg", "logo.png"), "../../materials/pkg/logo.png");
        JsonObject jsonObj = JsonNode.Parse(json)!.AsObject();

        string templatePath = Path.Combine(_root, "templates", templateDirectory, "title.json");
        var item = new ObjectTemplateItem(
            Guid.NewGuid(), typeof(TestEngineObjectWithFileSource), typeof(TestEngineObjectWithFileSource),
            jsonObj, "title", "", templatePath);

        var instance = item.CreateInstance() as TestEngineObjectWithFileSource;

        Assert.That(instance, Is.Not.Null);
        Assert.That(
            (instance!.FileSource.CurrentValue as BlobFileSource)?.Data,
            Is.EqualTo(Encoding.UTF8.GetBytes("png")));
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

    // Nothing validates a template file, and a store-installed one is third-party content, so a preview
    // that cannot be used must cost the thumbnail, not the template.
    private static IEnumerable<TestCaseData> UnusablePreviews()
    {
        yield return Case("not base64", () => "not base64 at all!!");
        yield return Case("empty", () => "");
        yield return Case("over the encoded-length cap, rejected before it is allocated", () => new string('A', 4_000_000));
        yield return Case(
            "a flat PNG under the length cap declaring dimensions that cost gigabytes to decode",
            () => Convert.ToBase64String(PngHeader(30000, 30000)));
        yield return Case(
            "dimensions whose product wraps negative in a signed 64-bit size check",
            () => Convert.ToBase64String(PngHeader(uint.MaxValue, uint.MaxValue)));
        yield return Case("not a PNG", () => Convert.ToBase64String("not a png at all, just bytes"u8.ToArray()));
        yield return Case("not a string", () => 42);

        static TestCaseData Case(string name, Func<JsonNode> preview) => new TestCaseData(preview).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(UnusablePreviews))]
    public void FromJson_WithAnUnusablePreview_DropsOnlyThePreview(Func<JsonNode> preview)
    {
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = preview();

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
    public void FromJson_WithAPreviewWithinBounds_KeepsIt()
    {
        byte[] preview = PngHeader(256, 144);
        JsonObject json = ObjectTemplateItem.ToJson(CreateItem()).AsObject();
        json["Preview"] = Convert.ToBase64String(preview);

        ObjectTemplateItem? restored = ObjectTemplateItem.FromJson(
            json, "title", Path.Combine(_root, "templates", "pkg", "title.json"), NullLogger.Instance);

        Assert.That(restored!.Preview, Is.EqualTo(preview));
    }

    // Signature + the IHDR width/height, which is all the size check reads.
    private static byte[] PngHeader(uint width, uint height)
    {
        byte[] bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
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
