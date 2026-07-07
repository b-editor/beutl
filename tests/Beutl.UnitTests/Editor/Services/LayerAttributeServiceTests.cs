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

    private bool SetFlag(string flag, int zIndex, bool value)
    {
        return flag switch
        {
            "locked" => _service.SetLocked(_scene, zIndex, value),
            "audio-muted" => _service.SetAudioMuted(_scene, zIndex, value),
            "video-muted" => _service.SetVideoMuted(_scene, zIndex, value),
            "solo" => _service.SetSolo(_scene, zIndex, value),
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
        };
    }

    private static bool GetFlag(TimelineLayer layer, string flag)
    {
        return flag switch
        {
            "locked" => layer.IsLocked,
            "audio-muted" => layer.IsAudioMuted,
            "video-muted" => layer.IsVideoMuted,
            "solo" => layer.IsSolo,
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
        };
    }

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

    [Test]
    public void SetLocked_NoExistingModel_CreatesLayerAndCommits()
    {
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;

        bool changed = _service.SetLocked(_scene, zIndex: 3, isLocked: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers + 1));
            Assert.That(_scene.Layers.First(l => l.ZIndex == 3).IsLocked, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetLocked_ExistingModel_UpdatesInPlace()
    {
        var existing = new TimelineLayer { ZIndex = 7, IsLocked = false };
        _scene.Layers.Add(existing);
        int beforeLayers = _scene.Layers.Count;

        bool changed = _service.SetLocked(_scene, zIndex: 7, isLocked: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers));
            Assert.That(existing.IsLocked, Is.True);
        });
    }

    [Test]
    public void SetLocked_NoChange_DoesNotCommit()
    {
        var existing = new TimelineLayer { ZIndex = 4, IsLocked = true };
        _scene.Layers.Add(existing);
        int before = _history.UndoCount;

        bool changed = _service.SetLocked(_scene, zIndex: 4, isLocked: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase("locked")]
    [TestCase("audio-muted")]
    [TestCase("video-muted")]
    [TestCase("solo")]
    public void SetFlag_NoExistingModel_FalseValue_DoesNotCreateOrCommit(string flag)
    {
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;

        bool changed = SetFlag(flag, zIndex: 8, value: false);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [TestCase("audio-muted")]
    [TestCase("video-muted")]
    [TestCase("solo")]
    public void SetFlag_ExistingModel_UpdatesInPlaceAndCommits(string flag)
    {
        var existing = new TimelineLayer { ZIndex = 7 };
        _scene.Layers.Add(existing);
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;

        bool changed = SetFlag(flag, zIndex: 7, value: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers));
            Assert.That(GetFlag(existing, flag), Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [TestCase("audio-muted")]
    [TestCase("video-muted")]
    [TestCase("solo")]
    public void SetFlag_NoChange_DoesNotCommit(string flag)
    {
        var existing = new TimelineLayer { ZIndex = 4 };
        _scene.Layers.Add(existing);
        SetFlag(flag, zIndex: 4, value: true);
        int beforeUndo = _history.UndoCount;
        int beforeLayers = _scene.Layers.Count;

        bool changed = SetFlag(flag, zIndex: 4, value: true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_scene.Layers.Count, Is.EqualTo(beforeLayers));
            Assert.That(GetFlag(existing, flag), Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [TestCase("locked")]
    [TestCase("audio-muted")]
    [TestCase("video-muted")]
    [TestCase("solo")]
    public void SetFlag_ClearingLastFlag_PrunesEmptyModel(string flag)
    {
        SetFlag(flag, zIndex: 5, value: true);
        Assert.That(_scene.Layers.Any(l => l.ZIndex == 5), Is.True);
        int beforeUndo = _history.UndoCount;

        bool changed = SetFlag(flag, zIndex: 5, value: false);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.Layers.Any(l => l.ZIndex == 5), Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetFlag_ClearingFlag_KeepsCustomizedModel()
    {
        var named = new TimelineLayer { ZIndex = 5, Name = "BG" };
        _scene.Layers.Add(named);
        var colored = new TimelineLayer { ZIndex = 6, Color = Colors.Red };
        _scene.Layers.Add(colored);
        var multiFlag = new TimelineLayer { ZIndex = 7, IsSolo = true };
        _scene.Layers.Add(multiFlag);
        _service.SetLocked(_scene, zIndex: 5, isLocked: true);
        _service.SetLocked(_scene, zIndex: 6, isLocked: true);
        _service.SetLocked(_scene, zIndex: 7, isLocked: true);

        _service.SetLocked(_scene, zIndex: 5, isLocked: false);
        _service.SetLocked(_scene, zIndex: 6, isLocked: false);
        _service.SetLocked(_scene, zIndex: 7, isLocked: false);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Layers, Does.Contain(named));
            Assert.That(_scene.Layers, Does.Contain(colored));
            Assert.That(_scene.Layers, Does.Contain(multiFlag));
            Assert.That(named.IsLocked, Is.False);
        });
    }

    [Test]
    public void SetAudioMuted_CreatesModelAndCommits()
    {
        int beforeUndo = _history.UndoCount;

        bool changed = _service.SetAudioMuted(_scene, zIndex: 6, isMuted: true);

        TimelineLayer layer = _scene.Layers.First(l => l.ZIndex == 6);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(layer.IsAudioMuted, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetVideoMuted_CreatesModelAndCommits()
    {
        int beforeUndo = _history.UndoCount;

        bool changed = _service.SetVideoMuted(_scene, zIndex: 6, isMuted: true);

        TimelineLayer layer = _scene.Layers.First(l => l.ZIndex == 6);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(layer.IsVideoMuted, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }

    [Test]
    public void SetSolo_CreatesModelAndCommits()
    {
        int beforeUndo = _history.UndoCount;

        bool changed = _service.SetSolo(_scene, zIndex: 6, isSolo: true);

        TimelineLayer layer = _scene.Layers.First(l => l.ZIndex == 6);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(layer.IsSolo, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo + 1));
        });
    }
}
