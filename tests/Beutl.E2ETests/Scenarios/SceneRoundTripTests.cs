using System.Text.Json.Nodes;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.E2ETests.Scenarios;

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

    private Element NewElement(string fileName, TimeSpan start, TimeSpan length, int zIndex, bool isEnabled, Color accent)
    {
        return new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            IsEnabled = isEnabled,
            AccentColor = accent,
            Uri = new Uri(Path.Combine(_baseDir, fileName)),
        };
    }

    [Test]
    public void Scene_with_varied_elements_round_trips_through_disk()
    {
        var scene = new Scene(1280, 720, "round-trip")
        {
            Uri = new Uri(Path.Combine(_baseDir, "main.scene")),
            Start = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(42),
        };

        Element a = NewElement("a.belm", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3), 0, true, Color.FromArgb(255, 10, 20, 30));
        Element b = NewElement("b.belm", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), 1, false, Color.FromArgb(255, 200, 100, 50));
        Element c = NewElement("c.belm", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5), 2, true, Color.FromArgb(128, 0, 255, 0));
        scene.Children.Add(a);
        scene.Children.Add(b);
        scene.Children.Add(c);

        var shape = new RectShape();
        shape.Width.CurrentValue = 320f;
        shape.Height.CurrentValue = 180f;
        shape.Opacity.CurrentValue = 75f;
        c.Objects.Add(shape);

        CoreSerializer.StoreToUri(scene, scene.Uri!);

        var restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.FrameSize, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(restored.Name, Is.EqualTo("round-trip"));
            Assert.That(restored.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(restored.Duration, Is.EqualTo(TimeSpan.FromSeconds(42)));
            Assert.That(restored.Children, Has.Count.EqualTo(3));
        });

        Element ra = restored.Children.Single(e => e.Id == a.Id);
        Element rb = restored.Children.Single(e => e.Id == b.Id);
        Element rc = restored.Children.Single(e => e.Id == c.Id);

        Assert.Multiple(() =>
        {
            Assert.That(ra.Start, Is.EqualTo(a.Start));
            Assert.That(ra.Length, Is.EqualTo(a.Length));
            Assert.That(ra.ZIndex, Is.EqualTo(0));
            Assert.That(ra.IsEnabled, Is.True);
            Assert.That(ra.AccentColor, Is.EqualTo(a.AccentColor));

            Assert.That(rb.ZIndex, Is.EqualTo(1));
            Assert.That(rb.IsEnabled, Is.False);
            Assert.That(rb.AccentColor, Is.EqualTo(b.AccentColor));

            Assert.That(rc.ZIndex, Is.EqualTo(2));
            Assert.That(rc.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });

        RectShape restoredShape = rc.Objects.OfType<RectShape>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(restoredShape.Width.CurrentValue, Is.EqualTo(320f));
            Assert.That(restoredShape.Height.CurrentValue, Is.EqualTo(180f));
            Assert.That(restoredShape.Opacity.CurrentValue, Is.EqualTo(75f));
        });
    }

    [Test]
    public void Scene_round_trips_through_json_string_with_embedded_elements()
    {
        var scene = new Scene(640, 480, "embedded")
        {
            Uri = new Uri(Path.Combine(_baseDir, "embedded.scene")),
        };
        Element only = NewElement("only.belm", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), 3, false, Color.FromArgb(255, 1, 2, 3));
        scene.Children.Add(only);
        var ellipse = new EllipseShape();
        ellipse.Width.CurrentValue = 64f;
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
            Assert.That(restored.FrameSize, Is.EqualTo(new PixelSize(640, 480)));
            Assert.That(restored.Name, Is.EqualTo("embedded"));
            Assert.That(restored.Children, Has.Count.EqualTo(1));
        });

        Element restoredElement = restored.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(restoredElement.Id, Is.EqualTo(only.Id));
            Assert.That(restoredElement.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(restoredElement.Length, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(restoredElement.ZIndex, Is.EqualTo(3));
            Assert.That(restoredElement.IsEnabled, Is.False);
            Assert.That(restoredElement.Objects.OfType<EllipseShape>().Single().Width.CurrentValue, Is.EqualTo(64f));
        });
    }

    [Test]
    public void Element_round_trips_standalone_through_disk()
    {
        Element element = NewElement("solo.belm", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), 4, false, Color.FromArgb(255, 9, 8, 7));
        var shape = new RectShape();
        shape.Width.CurrentValue = 12f;
        element.Objects.Add(shape);

        CoreSerializer.StoreToUri(element, element.Uri!);
        var restored = CoreSerializer.RestoreFromUri<Element>(element.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(element.Id));
            Assert.That(restored.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(restored.Length, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(restored.ZIndex, Is.EqualTo(4));
            Assert.That(restored.IsEnabled, Is.False);
            Assert.That(restored.AccentColor, Is.EqualTo(element.AccentColor));
            Assert.That(restored.Objects.OfType<RectShape>().Single().Width.CurrentValue, Is.EqualTo(12f));
        });
    }
}
