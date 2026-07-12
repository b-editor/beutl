using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementSlipServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementSlipService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_slip", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementSlipService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementSlipService(null!));
    }

    [Test]
    public void Slip_NullScene_Throws()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Throws<ArgumentNullException>(() => _service.Slip(null!, [element], TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Slip_NullElements_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Slip(_scene, null!, TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Slip_ElementsContainingNull_Throws()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Throws<ArgumentNullException>(() => _service.Slip(_scene, [element, null!], TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Slip_LockedElement_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        element.Objects.Add(new SourceVideo());
        element.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_LockedLayer_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 3);
        element.Objects.Add(new SourceVideo());
        _scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_ElementNotInScene_NoCommit()
    {
        var element = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(2) };
        element.Objects.Add(new SourceVideo());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_ZeroDelta_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        element.Objects.Add(new SourceVideo());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_NoSplittableMedia_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_SourceVideo_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_SourceVideo_ClampsToUsableSourceDuration()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource }
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_SourceSound_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var sound = new SourceSound();
        element.Objects.Add(sound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_SourceSound_ClampsToUsableSourceDuration()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3)));
        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource }
        };
        element.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_FallbackSound_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        element.Objects.Add(new FallbackSound());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_SoundGroup_ShiftsSourceChildrenAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var source = new SourceSound();
        var group = new SoundGroup();
        group.Children.Add(source);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(source.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(group.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_MultipleMedia_ShiftsAllAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        var sound = new SourceSound();
        element.Objects.Add(video);
        element.Objects.Add(sound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_NegativeDelta_ShiftsBackward()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(3);
        element.Objects.Add(video);

        _service.Slip(_scene, [element], TimeSpan.FromSeconds(-1));

        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Slip_NegativeDeltaAtZero_NoCommit()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_NegativeDeltaPastZero_ClampsAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromMilliseconds(500);
        element.Objects.Add(video);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_UndoRestoresOffsetPosition()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);
        _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));

        _history.Undo();

        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Slip_LinkedStreamsWithDifferentBounds_ShiftsAllByTheTighterDelta()
    {
        // Video source allows a 1s offset (3s source - 2s element); audio source allows 3s
        // (5s - 2s). A +5s request must land both at the tighter 1s so the streams stay in sync.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo { Source = { CurrentValue = videoSource } };
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
        var sound = new SourceSound { Source = { CurrentValue = soundSource } };
        element.Objects.Add(video);
        element.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_VideoNestedInDrawableGroup_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        var group = new DrawableGroup();
        group.Children.Add(video);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_VideoNestedInDrawablePresenter_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = video;
        element.Objects.Add(presenter);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_VideoNestedInDrawableTimeController_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        var controller = new DrawableTimeController();
        controller.Target.CurrentValue = video;
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_SharedSourceReachableViaTwoPresenters_ShiftsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        var firstPresenter = new DrawablePresenter();
        firstPresenter.Target.CurrentValue = video;
        var secondPresenter = new DrawablePresenter();
        secondPresenter.Target.CurrentValue = video;
        element.Objects.Add(firstPresenter);
        element.Objects.Add(secondPresenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_SharedSourceAcrossElements_ShiftsOnce()
    {
        Element first = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        first.Objects.Add(video);

        Element second = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = video;
        second.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [first, second], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_MultipleElements_ShiftsAllAndCommitsOnce()
    {
        Element first = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        Element second = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var video = new SourceVideo();
        var sound = new SourceSound();
        first.Objects.Add(video);
        second.Objects.Add(sound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [first, second], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_MultipleElements_ClampsToTightestElement()
    {
        // First element's video source only allows a 1s slip (3s source - 2s element); the
        // second element's media is unbounded. The shared delta must land both at 1s.
        Element first = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo { Source = { CurrentValue = videoSource } };
        first.Objects.Add(video);

        Element second = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var sound = new SourceSound();
        second.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [first, second], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_MultipleElements_NegativeDelta_ClampsToTightestOffset()
    {
        Element first = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(3);
        first.Objects.Add(video);

        Element second = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var sound = new SourceSound();
        sound.OffsetPosition.CurrentValue = TimeSpan.FromMilliseconds(500);
        second.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [first, second], TimeSpan.FromSeconds(-2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_LockedMember_IsDroppedNotBlocking()
    {
        Element unlocked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        unlocked.Objects.Add(video);

        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var lockedVideo = new SourceVideo();
        locked.Objects.Add(lockedVideo);
        locked.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [unlocked, locked], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(lockedVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_DuplicateElement_ShiftsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element, element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_EmptyElements_NoCommit()
    {
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
