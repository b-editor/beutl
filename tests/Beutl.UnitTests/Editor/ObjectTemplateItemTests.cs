using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.IO;
using Beutl.JsonConverters;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
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
            (instance!.FileSource.Value as BlobFileSource)?.Data,
            Is.EqualTo(Encoding.UTF8.GetBytes("png")));
    }

    // The template file's references resolve against this URI, so a folder named with "%XX" keeps its name (#3407).
    [TestCase("pkg%41")]
    [TestCase("50%20off")]
    public void ToFileUri_KeepsPercentInThePath(string templateDirectory)
    {
        string path = Path.Combine(_root, "templates", templateDirectory, "title.json");

        Assert.That(ObjectTemplateItem.ToFileUri(path).LocalPath, Is.EqualTo(path));
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

    // A template records no version, and one from before 2.0.0-preview.9 names time keyframes over TimeSpan (#2885).
    [Test]
    public void A_template_saved_before_flick_counts_keeps_the_keyframes_of_a_node_time_port()
    {
        var sceneNode = new SceneNode();
        var animation = new KeyFrameAnimation<MediaTime>();
        animation.KeyFrames.Add(new KeyFrame<MediaTime> { KeyTime = MediaTime.FromSeconds(1), Value = MediaTime.FromSeconds(3) });
        ((IAnimatablePropertyAdapter<MediaTime>)sceneNode.Time.Property!).Animation = animation;
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add(sceneNode);
        var element = new Element();
        element.AddObject(drawable);
        JsonNode file;
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            file = ObjectTemplateItem.ToJson(ObjectTemplateItem.CreateFromInstance(element, "clip"));
        string text = file.ToJsonString().Replace("[Beutl.Core]Beutl:MediaTime", "[System.Private.CoreLib]System:TimeSpan");
        Assert.That(text, Does.Contain("[System.Private.CoreLib]System:TimeSpan"));

        ObjectTemplateItem item = ObjectTemplateItem.FromJson(
            JsonNode.Parse(text)!, "clip", Path.Combine(_root, "templates", "clip.json"), NullLogger.Instance)!;
        var instance = (Element)item.CreateInstance()!;
        GraphModel graph = ((NodeGraphDrawable)instance.Objects.Single()).Model.Value!;
        var keyFrames = ((IAnimatablePropertyAdapter<MediaTime>)graph.Nodes.OfType<SceneNode>().Single().Time.Property!).Animation;

        Assert.That(keyFrames?.KeyFrames.OfType<KeyFrame<MediaTime>>().Select(k => (k.KeyTime, k.Value)),
            Is.EqualTo(new[] { (MediaTime.FromSeconds(1), MediaTime.FromSeconds(3)) }));
    }
}
