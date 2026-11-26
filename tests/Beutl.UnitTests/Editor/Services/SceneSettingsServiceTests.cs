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
            Start = { Value = MediaTime.FromSeconds(1) },
            Duration = { Value = MediaTime.FromSeconds(5) },
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
    public void Apply_SceneRatesLayoutAndSizeChangesAreOneUndoableEntry()
    {
        PixelSize previousSize = _scene.FrameSize.Value;
        Rational previousFrameRate = _scene.FrameRate.Value;
        int previousSampleRate = _scene.SampleRate.Value;
        int before = _history.UndoCount;

        bool applied = _service.Apply(
            _scene,
            new PixelSize(800, 600),
            _scene.Start.Value,
            _scene.Duration.Value,
            new Rational(60),
            48000,
            SpeakerLayout.Surround51,
            75f);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(_scene.FrameRate.Value, Is.EqualTo(new Rational(60)));
            Assert.That(_scene.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(_scene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            Assert.That(_scene.MasterVolume.Value, Is.EqualTo(75f));
        });

        _history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(previousSize));
            Assert.That(_scene.FrameRate.Value, Is.EqualTo(previousFrameRate));
            Assert.That(_scene.SampleRate.Value, Is.EqualTo(previousSampleRate));
            Assert.That(_scene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
            Assert.That(_scene.MasterVolume.Value, Is.EqualTo(100f));
        });

        _history.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(_scene.FrameRate.Value, Is.EqualTo(new Rational(60)));
            Assert.That(_scene.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(_scene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            Assert.That(_scene.MasterVolume.Value, Is.EqualTo(75f));
        });

        Assert.That(_service.Apply(
            _scene,
            _scene.FrameSize.Value,
            _scene.Start.Value,
            _scene.Duration.Value,
            new Rational(60),
            48000,
            SpeakerLayout.Surround51,
            75f), Is.False);
    }

    [Test]
    public void Apply_MasterVolumeIsOneUndoableEntry()
    {
        int before = _history.UndoCount;

        bool applied = _service.Apply(
            _scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Stereo, 50f);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(_scene.MasterVolume.Value, Is.EqualTo(50f));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
        _history.Undo();
        Assert.That(_scene.MasterVolume.Value, Is.EqualTo(100f));
        _history.Redo();
        Assert.That(_scene.MasterVolume.Value, Is.EqualTo(50f));
    }

    [Test]
    public void Apply_SameMasterVolume_CommitsNothing()
    {
        int before = _history.UndoCount;

        bool applied = _service.Apply(
            _scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Stereo, 100f);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
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

        bool changed = _service.Apply(_scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value);

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
        MediaTime keepStart = _scene.Start.Value;
        MediaTime keepDuration = _scene.Duration.Value;
        var newSize = new PixelSize(1920, 1080);

        bool changed = _service.Apply(_scene, newSize, keepStart, keepDuration);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(newSize));
            Assert.That(_scene.Start.Value, Is.EqualTo(keepStart));
            Assert.That(_scene.Duration.Value, Is.EqualTo(keepDuration));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Apply_AllFieldsChanged_CollapsesIntoSingleHistoryEntry()
    {
        int before = _history.UndoCount;
        var newSize = new PixelSize(1280, 720);
        var newStart = MediaTime.FromSeconds(3);
        var newDuration = MediaTime.FromSeconds(10);

        bool changed = _service.Apply(_scene, newSize, newStart, newDuration);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(newSize));
            Assert.That(_scene.Start.Value, Is.EqualTo(newStart));
            Assert.That(_scene.Duration.Value, Is.EqualTo(newDuration));
            // The field writes must collapse into one entry, so one Undo reverts the Apply.
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Apply_AfterUndo_RestoresAllFields()
    {
        PixelSize originalSize = _scene.FrameSize.Value;
        MediaTime originalStart = _scene.Start.Value;
        MediaTime originalDuration = _scene.Duration.Value;

        _service.Apply(
            _scene,
            new PixelSize(1280, 720),
            MediaTime.FromSeconds(3),
            MediaTime.FromSeconds(10));
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(originalSize));
            Assert.That(_scene.Start.Value, Is.EqualTo(originalStart));
            Assert.That(_scene.Duration.Value, Is.EqualTo(originalDuration));
        });
    }
}
