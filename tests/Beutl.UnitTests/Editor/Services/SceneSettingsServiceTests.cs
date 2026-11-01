using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
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
    public void Apply_ProjectLayoutAndSceneChangesAreOneUndoableEntry()
    {
        var project = new Project { Items = { _scene } };
        PixelSize previous = _scene.FrameSize.Value;
        int before = _history.UndoCount;
        _service.Apply(_scene, new PixelSize(800, 600), _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51);
        Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        Assert.That(project.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
        _history.Undo();
        Assert.That(project.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
        Assert.That(_scene.FrameSize.Value, Is.EqualTo(previous));
        _history.Redo();
        Assert.That(project.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
        Assert.That(_scene.FrameSize.Value, Is.EqualTo(new PixelSize(800, 600)));
        Assert.That(_service.Apply(_scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51), Is.False);
    }

    [Test]
    public void Apply_ProjectLayoutPersistsOnApplyUndoAndRedo()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-layout-").FullName;
        string path = Path.Combine(directory, "project.json");
        _scene.Uri = new Uri(Path.Combine(directory, "scene.scene"));
        var project = new Project { Uri = new Uri(path), Items = { _scene } };
        try
        {
            CoreSerializer.StoreToUri(project, project.Uri);
            int before = _history.UndoCount;
            _service.Apply(_scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            AssertStoredLayout(SpeakerLayout.Surround51);

            Assert.That(_history.Undo(), Is.True);
            AssertStoredLayout(SpeakerLayout.Stereo);
            Assert.That(_history.Redo(), Is.True);
            AssertStoredLayout(SpeakerLayout.Surround51);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        void AssertStoredLayout(SpeakerLayout expected)
        {
            Assert.That(project.ChannelLayout.Value, Is.EqualTo(expected));
            Assert.That(JsonNode.Parse(File.ReadAllText(path))!["ChannelLayout"]!.GetValue<int>(), Is.EqualTo((int)expected));
        }
    }

    [Test]
    public void Apply_ProjectWriteFailureRestoresTheLayoutWithoutRecordingAnEdit()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-layout-failure-").FullName;
        _scene.Uri = new Uri(Path.Combine(directory, "scene.scene"));
        var project = new Project { Uri = new Uri(directory), Items = { _scene } };
        PixelSize previousSize = _scene.FrameSize.Value;
        int before = _history.UndoCount;
        try
        {
            Exception? failure = Assert.Catch<Exception>(() => _service.Apply(_scene, new PixelSize(800, 600),
                _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51));
            Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(project.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
            Assert.That(_scene.FrameSize.Value, Is.EqualTo(previousSize));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Replay_ProjectWriteFailureRestoresTheLayoutAndCanBeRetried(bool redo)
    {
        string directory = Directory.CreateTempSubdirectory("beutl-layout-retry-").FullName;
        string path = Path.Combine(directory, "project.json");
        _scene.Uri = new Uri(Path.Combine(directory, "scene.scene"));
        var project = new Project { Uri = new Uri(path), Items = { _scene } };
        try
        {
            _service.Apply(_scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51);
            if (redo) Assert.That(_history.Undo(), Is.True);
            SpeakerLayout previous = project.ChannelLayout.Value;
            int undoCount = _history.UndoCount;
            int redoCount = _history.RedoCount;
            project.Uri = new Uri(directory);

            Exception? failure = Assert.Catch<Exception>(() =>
            {
                if (redo) _history.Redo(); else _history.Undo();
            });
            Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(project.ChannelLayout.Value, Is.EqualTo(previous));
            Assert.That(_history.UndoCount, Is.EqualTo(undoCount));
            Assert.That(_history.RedoCount, Is.EqualTo(redoCount));
            Assert.That(redo ? _history.CanRedo : _history.CanUndo, Is.True);

            project.Uri = new Uri(path);
            Assert.That(redo ? _history.Redo() : _history.Undo(), Is.True);
            SpeakerLayout expected = redo ? SpeakerLayout.Surround51 : SpeakerLayout.Stereo;
            Assert.That(project.ChannelLayout.Value, Is.EqualTo(expected));
            Assert.That(JsonNode.Parse(File.ReadAllText(path))!["ChannelLayout"]!.GetValue<int>(), Is.EqualTo((int)expected));
            Assert.That(redo ? _history.Undo() : _history.Redo(), Is.True);
            Assert.That(project.ChannelLayout.Value, Is.EqualTo(previous));
            Assert.That(JsonNode.Parse(File.ReadAllText(path))!["ChannelLayout"]!.GetValue<int>(), Is.EqualTo((int)previous));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Undo_LayoutObserverFailureBlocksReplay(bool duringRollback)
    {
        string directory = Directory.CreateTempSubdirectory("beutl-layout-diverged-").FullName;
        string path = Path.Combine(directory, "project.json");
        _scene.Uri = new Uri(Path.Combine(directory, "scene.scene"));
        var project = new Project { Uri = new Uri(path), Items = { _scene } };
        var observerFailure = new InvalidOperationException("layout observer failed");
        void FailOnLayoutChange(object? sender, PropertyValueChangedEventArgs<SpeakerLayout> e)
        {
            if (e.NewValue == (duringRollback ? SpeakerLayout.Surround51 : SpeakerLayout.Stereo))
                throw observerFailure;
        }

        try
        {
            _service.Apply(_scene, _scene.FrameSize.Value, _scene.Start.Value, _scene.Duration.Value, SpeakerLayout.Surround51);
            project.Uri = new Uri(directory);
            project.ChannelLayout.ValueChanged += FailOnLayoutChange;

            HistoryReplayException? failure = Assert.Throws<HistoryReplayException>(() => _history.Undo());
            if (duringRollback)
            {
                Assert.That(failure!.InnerException, Is.TypeOf<ProjectStateDivergedException>());
                var diverged = (ProjectStateDivergedException)failure.InnerException!;
                Assert.That(diverged.InnerException, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                Assert.That(diverged.RollbackException, Is.SameAs(observerFailure));
            }
            else
            {
                Assert.That(failure!.InnerException, Is.SameAs(observerFailure));
            }

            project.ChannelLayout.ValueChanged -= FailOnLayoutChange;
            project.Uri = new Uri(path);
            Assert.That(_history.CanUndo, Is.False);
            Assert.That(_history.CanRedo, Is.False);
            Assert.That(_history.Undo(), Is.False);
            Assert.That(_history.Redo(), Is.False);
        }
        finally
        {
            project.ChannelLayout.ValueChanged -= FailOnLayoutChange;
            Directory.Delete(directory, recursive: true);
        }
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
