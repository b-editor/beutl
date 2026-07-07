using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class SceneSettingsServiceTests
{
    private HistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private SceneSettingsService _service = null!;

    [SetUp]
    public void Setup()
    {
        _scene = new Scene(640, 480, string.Empty)
        {
            Start = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(5),
        };
        _harness = new HistoryHarness(_scene);
        _history = _harness.History;
        _service = new SceneSettingsService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneSettingsService(null!));
    }

    [Test]
    public void Apply_AllFieldsUnchanged_DoesNotCommit()
    {
        int before = _history.UndoCount;

        bool changed = _service.Apply(_scene, _scene.FrameSize, _scene.Start, _scene.Duration);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Apply_OnlyFrameSizeChanged_AppliesAndCommitsOnce()
    {
        int before = _history.UndoCount;
        TimeSpan keepStart = _scene.Start;
        TimeSpan keepDuration = _scene.Duration;
        var newSize = new PixelSize(1920, 1080);

        bool changed = _service.Apply(_scene, newSize, keepStart, keepDuration);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.FrameSize, Is.EqualTo(newSize));
            Assert.That(_scene.Start, Is.EqualTo(keepStart));
            Assert.That(_scene.Duration, Is.EqualTo(keepDuration));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Apply_AllFieldsChanged_CollapsesIntoSingleHistoryEntry()
    {
        int before = _history.UndoCount;
        var newSize = new PixelSize(1280, 720);
        var newStart = TimeSpan.FromSeconds(3);
        var newDuration = TimeSpan.FromSeconds(10);

        bool changed = _service.Apply(_scene, newSize, newStart, newDuration);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.FrameSize, Is.EqualTo(newSize));
            Assert.That(_scene.Start, Is.EqualTo(newStart));
            Assert.That(_scene.Duration, Is.EqualTo(newDuration));
            // The field writes must collapse into one entry, so one Undo reverts the Apply.
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Apply_AfterUndo_RestoresAllFields()
    {
        PixelSize originalSize = _scene.FrameSize;
        TimeSpan originalStart = _scene.Start;
        TimeSpan originalDuration = _scene.Duration;

        _service.Apply(
            _scene,
            new PixelSize(1280, 720),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10));
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.FrameSize, Is.EqualTo(originalSize));
            Assert.That(_scene.Start, Is.EqualTo(originalStart));
            Assert.That(_scene.Duration, Is.EqualTo(originalDuration));
        });
    }
}
