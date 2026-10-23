using System.Text.Json.Nodes;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class SceneRoundTripTests
{
    private string _baseDir = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"beutl-e2e-scene_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true);
    }

    private Element NewElement(string fileName, MediaTime start, MediaTime length, int zIndex, bool isEnabled, Color accent)
    {
        return new Element
        {
            Start = { Value = start },
            Length = { Value = length },
            ZIndex = { Value = zIndex },
            IsEnabled = { Value = isEnabled },
            AccentColor = { Value = accent },
            Uri = new Uri(Path.Combine(_baseDir, fileName)),
        };
    }

    [Test]
    public void Scene_with_varied_elements_round_trips_through_disk()
    {
        var scene = new Scene(1280, 720, "round-trip")
        {
            Uri = new Uri(Path.Combine(_baseDir, "main.scene")),
            Start = { Value = MediaTime.FromSeconds(1) },
            Duration = { Value = MediaTime.FromSeconds(42) },
        };

        Element a = NewElement("a.belm", MediaTime.FromSeconds(0), MediaTime.FromSeconds(3), 0, true, Color.FromArgb(255, 10, 20, 30));
        Element b = NewElement("b.belm", MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), 1, false, Color.FromArgb(255, 200, 100, 50));
        Element c = NewElement("c.belm", MediaTime.FromSeconds(7), MediaTime.FromSeconds(5), 2, true, Color.FromArgb(128, 0, 255, 0));
        scene.Children.Add(a);
        scene.Children.Add(b);
        scene.Children.Add(c);

        var shape = new RectShape();
        shape.Width.Value = 320f;
        shape.Height.Value = 180f;
        shape.Opacity.Value = 75f;
        c.Objects.Add(shape);

        CoreSerializer.StoreToUri(scene, scene.Uri!);

        var restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.FrameSize.Value, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(restored.Name.Value, Is.EqualTo("round-trip"));
            Assert.That(restored.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(restored.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(42)));
            Assert.That(restored.Children, Has.Count.EqualTo(3));
        });

        Element ra = restored.Children.Single(e => e.Id.Value == a.Id.Value);
        Element rb = restored.Children.Single(e => e.Id.Value == b.Id.Value);
        Element rc = restored.Children.Single(e => e.Id.Value == c.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(ra.Start.Value, Is.EqualTo(a.Start.Value));
            Assert.That(ra.Length.Value, Is.EqualTo(a.Length.Value));
            Assert.That(ra.ZIndex.Value, Is.EqualTo(0));
            Assert.That(ra.IsEnabled.Value, Is.True);
            Assert.That(ra.AccentColor.Value, Is.EqualTo(a.AccentColor.Value));

            Assert.That(rb.ZIndex.Value, Is.EqualTo(1));
            Assert.That(rb.IsEnabled.Value, Is.False);
            Assert.That(rb.AccentColor.Value, Is.EqualTo(b.AccentColor.Value));

            Assert.That(rc.ZIndex.Value, Is.EqualTo(2));
            Assert.That(rc.Length.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
        });

        RectShape restoredShape = rc.Objects.OfType<RectShape>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(restoredShape.Width.Value, Is.EqualTo(320f));
            Assert.That(restoredShape.Height.Value, Is.EqualTo(180f));
            Assert.That(restoredShape.Opacity.Value, Is.EqualTo(75f));
        });
    }

    [Test]
    public void Scene_round_trips_through_json_string_with_embedded_elements()
    {
        var scene = new Scene(640, 480, "embedded")
        {
            Uri = new Uri(Path.Combine(_baseDir, "embedded.scene")),
        };
        Element only = NewElement("only.belm", MediaTime.FromSeconds(2), MediaTime.FromSeconds(6), 3, false, Color.FromArgb(255, 1, 2, 3));
        scene.Children.Add(only);
        var ellipse = new EllipseShape();
        ellipse.Width.Value = 64f;
        only.Objects.Add(ellipse);

        var options = new CoreSerializerOptions
        {
            BaseUri = scene.Uri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        };
        string json = CoreSerializer.SerializeToJsonString(scene, options);

        var node = JsonNode.Parse(json) as JsonObject;
        Assert.That(node, Is.Not.Null);

        // Uri is not part of the serialized JSON, yet Scene.Deserialize needs it to resolve
        // child paths, so the Uri is set on the target before populating it.
        var restored = new Scene { Uri = scene.Uri };
        CoreSerializer.PopulateFromJsonObject(restored, node!, options);

        Assert.Multiple(() =>
        {
            Assert.That(restored.FrameSize.Value, Is.EqualTo(new PixelSize(640, 480)));
            Assert.That(restored.Name.Value, Is.EqualTo("embedded"));
            Assert.That(restored.Children, Has.Count.EqualTo(1));
        });

        Element restoredElement = restored.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(restoredElement.Id.Value, Is.EqualTo(only.Id.Value));
            Assert.That(restoredElement.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(restoredElement.Length.Value, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(restoredElement.ZIndex.Value, Is.EqualTo(3));
            Assert.That(restoredElement.IsEnabled.Value, Is.False);
            Assert.That(restoredElement.Objects.OfType<EllipseShape>().Single().Width.Value, Is.EqualTo(64f));
        });
    }

    [Test]
    public void Element_round_trips_standalone_through_disk()
    {
        Element element = NewElement("solo.belm", MediaTime.FromSeconds(5), MediaTime.FromSeconds(8), 4, false, Color.FromArgb(255, 9, 8, 7));
        var shape = new RectShape();
        shape.Width.Value = 12f;
        element.Objects.Add(shape);

        CoreSerializer.StoreToUri(element, element.Uri!);
        var restored = CoreSerializer.RestoreFromUri<Element>(element.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id.Value, Is.EqualTo(element.Id.Value));
            Assert.That(restored.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(restored.Length.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
            Assert.That(restored.ZIndex.Value, Is.EqualTo(4));
            Assert.That(restored.IsEnabled.Value, Is.False);
            Assert.That(restored.AccentColor.Value, Is.EqualTo(element.AccentColor.Value));
            Assert.That(restored.Objects.OfType<RectShape>().Single().Width.Value, Is.EqualTo(12f));
        });
    }
}
