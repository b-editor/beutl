using System.Text;
using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

// Scale machinery is runtime-only: existing projects load with zero migration. Non-GPU.
[TestFixture]
public class NoMigrationRegressionTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-no-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void Dilate_DoesNotSerializeScaleMachinery_AndRoundTripsStably()
    {
        var dilate = new Dilate();
        dilate.RadiusX.CurrentValue = 5f;
        dilate.RadiusY.CurrentValue = 7f;

        var json = CoreSerializer.SerializeToJsonObject(dilate);
        string s1 = json.ToJsonString();

        // Scale machinery has no serialized CoreProperty, so none of it leaks into the JSON.
        Assert.That(s1, Does.Not.Contain("WorkingScale"));
        Assert.That(s1, Does.Not.Contain("EffectiveScale"));
        Assert.That(s1, Does.Not.Contain("OutputScale"));

        var restored = (Dilate)CoreSerializer.DeserializeFromJsonObject(json, typeof(Dilate));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable (no migration / format drift)");
    }

    [Test]
    public void BlurredEllipse_RoundTripsStably_AndIsCurrentFormat()
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 80;
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(4, 4);
        shape.FilterEffect.CurrentValue = blur;

        var json = CoreSerializer.SerializeToJsonObject(shape);
        string s1 = json.ToJsonString();

        // Current EngineObject format, not the legacy Operation/Children one that ElementMigration handles.
        Assert.That(s1, Does.Not.Contain("\"Operation\""));
        Assert.That(s1, Does.Not.Contain("WorkingScale"));

        var restored = (EllipseShape)CoreSerializer.DeserializeFromJsonObject(json, typeof(EllipseShape));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable");
    }

    [Test]
    public void JsonSave_always_writes_lf_line_endings()
    {
        string path = Path.Combine(_tempDirectory, "line-endings.json");
        var json = new JsonObject
        {
            ["name"] = "Beutl",
            ["items"] = new JsonArray(1, 2, 3),
        };

        json.JsonSave(path);
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.UTF8.GetString(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("\n"));
            Assert.That(text, Does.Not.Contain("\r\n"));
            Assert.That(bytes, Has.None.EqualTo((byte)'\r'));
        });
    }

    [TestCase(typeof(ProjectItem))]
    [TestCase(typeof(Element))]
    public void Present_unresolvable_sidecar_discriminator_is_not_treated_as_legacy(Type type)
    {
        const string unknownDiscriminator = "[Missing.Extension]Missing.Extension:UnknownItem";
        string path = Path.Combine(_tempDirectory, "unknown-sidecar.json");
        var json = new JsonObject
        {
            ["$type"] = unknownDiscriminator,
        };
        json.JsonSave(path);

        Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.RestoreFromUri(new Uri(path), type));

        JsonObject preserved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That((string?)preserved["$type"], Is.EqualTo(unknownDiscriminator));
    }

    [Test]
    public void Embedding_the_root_of_a_file_cycle_keeps_the_back_edge_as_its_uri()
    {
        int rootSerializations = 0;
        var first = new ReferencedFile
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "first.json")),
            BeforeSerialization = () => rootSerializations++,
        };
        var second = new ReferencedFile { Uri = new Uri(Path.Combine(_tempDirectory, "second.json")), Next = first };
        first.Next = second;

        JsonObject json = CoreSerializer.SerializeToJsonObject(
            first,
            new CoreSerializerOptions
            {
                BaseUri = first.Uri,
                Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
            });

        Assert.Multiple(() =>
        {
            Assert.That(rootSerializations, Is.EqualTo(1));
            Assert.That(json["Next"]!["Next"], Is.InstanceOf<JsonValue>(), "the edge back to the root is a URI");
        });
    }

    // A file-backed object reached through an ordinary property rather than the hierarchy.
    private sealed class ReferencedFile : CoreObject
    {
        public ReferencedFile? Next { get; set; }

        public Action? BeforeSerialization { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            BeforeSerialization?.Invoke();
            base.Serialize(context);
            context.SetValue(nameof(Next), Next);
        }
    }
}
