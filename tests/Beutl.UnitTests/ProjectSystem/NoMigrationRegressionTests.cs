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
    public void Project_plain_resave_preserves_loaded_app_version_byte_for_byte()
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var source = new Project
        {
            Uri = new Uri(path),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["appVersion"] = "3.1.4";
        json["minAppVersion"] = "2.0.0-preview.1";
        json.JsonSave(path);
        byte[] before = File.ReadAllBytes(path);

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(path));
        CoreSerializer.StoreToUri(restored, new Uri(path));
        byte[] after = File.ReadAllBytes(path);

        Assert.Multiple(() =>
        {
            Assert.That(restored.AppVersion, Is.EqualTo("3.1.4"));
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public void Project_marked_as_migrated_advances_app_version()
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var source = new Project
        {
            Uri = new Uri(path),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["appVersion"] = "3.1.4";
        json.JsonSave(path);
        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(path));

        restored.MarkAsMigrated();
        JsonObject migrated = CoreSerializer.SerializeToJsonObject(restored);

        Assert.That((string?)migrated["appVersion"], Is.EqualTo(BeutlApplication.Version));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Project_with_legacy_sidecar_discriminator_advances_app_version(
        bool removeSceneDiscriminator,
        bool removeElementDiscriminator)
    {
        string projectPath = Path.Combine(_tempDirectory, "project.bep");
        string scenePath = Path.Combine(_tempDirectory, "scene", "scene.scene");
        string elementPath = Path.Combine(_tempDirectory, "scene", "element.belm");
        var source = new Project { Uri = new Uri(projectPath) };
        var scene = new Scene { Uri = new Uri(scenePath) };
        var element = new Element { Uri = new Uri(elementPath) };
        scene.AddChild(element);
        source.Items.Add(scene);
        CoreSerializer.StoreToUri(
            source,
            source.Uri,
            CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);

        JsonObject projectJson = JsonNode.Parse(File.ReadAllText(projectPath))!.AsObject();
        projectJson["appVersion"] = "1.0.0";
        projectJson.JsonSave(projectPath);
        if (removeSceneDiscriminator)
        {
            JsonObject sceneJson = JsonNode.Parse(File.ReadAllText(scenePath))!.AsObject();
            sceneJson.Remove("$type");
            sceneJson.JsonSave(scenePath);
        }

        if (removeElementDiscriminator)
        {
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            elementJson.Remove("$type");
            elementJson.JsonSave(elementPath);
        }

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));

        Assert.That(restored.AppVersion, Is.EqualTo(BeutlApplication.Version));
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
}
