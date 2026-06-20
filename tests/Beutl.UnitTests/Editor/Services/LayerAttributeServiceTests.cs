using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class LayerAttributeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private LayerAttributeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_lattr");
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new LayerAttributeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(int zIndex, bool isEnabled = true)
        => _harness.AddElement(zIndex, isEnabled);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayerAttributeService(null!));
    }

    [Test]
    public void SetEnabled_FlipsOnlyDifferingElements_AndCommits()
    {
        Element a = AddElement(2, isEnabled: true);
        Element b = AddElement(2, isEnabled: false);
        Element other = AddElement(5, isEnabled: true);
        int before = _history.UndoCount;

        bool changed = _service.SetEnabled(_scene, 2, newEnabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            // Only the differing element flipped; already-false and other-layer ones untouched.
            Assert.That(a.IsEnabled, Is.False);
            Assert.That(b.IsEnabled, Is.False);
            Assert.That(other.IsEnabled, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetEnabled_AllAlreadyMatch_DoesNotCommit()
    {
        Element a = AddElement(2, isEnabled: true);
        Element b = AddElement(2, isEnabled: true);
        int before = _history.UndoCount;

        bool changed = _service.SetEnabled(_scene, 2, newEnabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(a.IsEnabled, Is.True);
            Assert.That(b.IsEnabled, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SetColor_NoExistingModel_CreatesLayerAndCommits()
    {
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;
        var color = Color.FromArgb(255, 10, 20, 30);

        bool changed = _service.SetColor(_scene, zIndex: 3, color, defaultName: "Layer 3");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers + 1));
            TimelineLayer created = _scene.Layers.First(l => l.ZIndex == 3);
            Assert.That(created.Color, Is.EqualTo(color));
            Assert.That(created.Name, Is.EqualTo("Layer 3"));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetColor_ExistingModel_UpdatesInPlace()
    {
        var existing = new TimelineLayer
        {
            Name = "Existing",
            ZIndex = 7,
            Color = Color.FromArgb(255, 1, 2, 3),
        };
        _scene.Layers.Add(existing);
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;
        var target = Color.FromArgb(255, 200, 100, 50);

        bool changed = _service.SetColor(_scene, zIndex: 7, target, defaultName: "should-not-be-used");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            // Same model object, color mutated in place.
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers));
            Assert.That(existing.Color, Is.EqualTo(target));
            Assert.That(existing.Name, Is.EqualTo("Existing"));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetColor_NoChange_DoesNotCommit()
    {
        var color = Color.FromArgb(255, 50, 60, 70);
        var existing = new TimelineLayer { Name = "L", ZIndex = 4, Color = color };
        _scene.Layers.Add(existing);
        int before = _history.UndoCount;

        bool changed = _service.SetColor(_scene, zIndex: 4, color, defaultName: "L");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
