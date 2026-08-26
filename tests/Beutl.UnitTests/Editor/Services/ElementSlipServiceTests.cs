using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
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

    private static TimeSpan GetOutPointRoom(Element element)
    {
        TimeSpan maximumRoom = TimeSpan.FromSeconds(30);
        return SlippableMedia.OutPointRoom(
            SlippableMedia.Collect(element, maximumRoom),
            element.Length,
            maximumRoom);
    }

    private static IReadOnlyList<PresenterTargetState> CreatePresenterTargetStates(
        TimeRange range,
        CoreObject? initialTarget,
        params (TimeSpan Time, CoreObject? Target)[] transitions)
    {
        var states = new List<PresenterTargetState>();
        CoreObject? current = initialTarget;
        TimeSpan cursor = range.Start;
        foreach ((TimeSpan time, CoreObject? target) in transitions.OrderBy(x => x.Time))
        {
            if (time <= range.Start)
            {
                current = target;
                continue;
            }

            if (time >= range.End)
                break;

            states.Add(new PresenterTargetState(new TimeRange(cursor, time - cursor), current));
            cursor = time;
            current = target;
        }

        states.Add(new PresenterTargetState(new TimeRange(cursor, range.End - cursor), current));
        return states;
    }

    private static IReadOnlyList<PresenterTargetState> CreateAlternatingPresenterTargetStates(
        TimeRange range,
        TimeSpan interval,
        CoreObject first,
        CoreObject second)
    {
        var states = new List<PresenterTargetState>();
        TimeSpan cursor = range.Start;
        long stateIndex = cursor.Ticks / interval.Ticks;
        while (cursor < range.End)
        {
            long nextTicks = (stateIndex + 1) * interval.Ticks;
            TimeSpan next = TimeSpan.FromTicks(Math.Min(nextTicks, range.End.Ticks));
            states.Add(new PresenterTargetState(
                new TimeRange(cursor, next - cursor),
                stateIndex % 2 == 0 ? first : second));
            cursor = next;
            stateIndex++;
        }

        return states;
    }

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
    public void Slip_SourceVideo_AtMediaEnd_RemainsBounded()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 60)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(2) },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Slip_SourceVideo_NegativeMappedPositionUsesSourceTail()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(-2),
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_SourceVideo_NegativeToPositiveMappedRangeReservesSourceTail()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(-2),
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [TestCase(50f, 2d, true)]
    [TestCase(200f, 0d, false)]
    public void Slip_SourceVideo_SpeedAdjustsSourceBounds(float speed, double expectedDelta, bool expectedApplied)
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { CurrentValue = speed },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedApplied));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedDelta)));
        });
    }

    [Test]
    public void Slip_SourceVideo_ZeroConsumptionReservesLastSourceFrame()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { CurrentValue = 0f },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));
        TimeSpan frameDuration = TimeSpan.FromSeconds(1d / 30);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3) - frameDuration));
        });
    }

    [Test]
    public void Slip_SourceVideo_SubFrameConsumptionReservesLastSourceFrame()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { CurrentValue = 1f },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));
        TimeSpan frameDuration = TimeSpan.FromSeconds(1d / 30);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3) - frameDuration));
        });
    }

    [Test]
    public void Slip_SourceVideo_AnimatedSpeedAdjustsSourceBounds()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var speed = new KeyFrameAnimation<float>();
        speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 50f });
        speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(10), Value = 50f });
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { Animation = speed },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)).Within(TimeSpan.FromTicks(1)));
        });
    }

    [Test]
    public void Slip_SourceVideo_OvershootingSpeedEasingDoesNotAssumeEndpointMaximum()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 30)));
        var speed = new KeyFrameAnimation<float>();
        speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f });
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 10f,
            Easing = new BackEaseOut(),
        });
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { Animation = speed },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(0.88) },
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(0.1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.88)));
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
    public void Slip_TimeMappedSourceSoundUsesMappedConsumptionAndTailRoom()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3)));
        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource },
        };
        var presenter = new TestSourceSoundTimeMappingPresenter
        {
            Target = { CurrentValue = sound },
            Scale = 2,
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);
        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(room, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_TimeMappedSourceSoundUsesMappedStartPosition()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3)));
        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestSourceSoundTimeMappingPresenter
        {
            Target = { CurrentValue = sound },
            TargetOffset = TimeSpan.FromSeconds(2),
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);
        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(room, Is.EqualTo(TimeSpan.Zero));
            Assert.That(applied, Is.False);
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_ExpressionBackedSourceSoundSourceFailsClosed()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        var longSource = new SoundSource();
        longSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 10)));
        var shortSource = new SoundSource();
        shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3)));
        var sound = new SourceSound
        {
            Source = { CurrentValue = longSource },
        };
        sound.Source.Expression = new ConstantSoundSourceExpression(shortSource);
        element.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_SceneSound_ShiftsOffsetPositionAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var sceneSound = new SceneSound();
        element.Objects.Add(sceneSound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sceneSound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_SceneSound_ClampsToReferencedSceneDuration()
    {
        // A 3s referenced scene with a 2s clip leaves 1s of headroom, like a 3s file source.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var referenced = new Scene { Duration = TimeSpan.FromSeconds(3) };
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = referenced;
        element.Objects.Add(sceneSound);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sceneSound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_ExpressionBackedMediaOffsetsFailClosed()
    {
        var video = new SourceVideo();
        var sourceSound = new SourceSound();
        var sceneSound = new SceneSound();
        video.OffsetPosition.Expression = new TimeSpanAtOrAfterExpression(TimeSpan.Zero, TimeSpan.Zero);
        sourceSound.OffsetPosition.Expression = new TimeSpanAtOrAfterExpression(TimeSpan.Zero, TimeSpan.Zero);
        sceneSound.OffsetPosition.Expression = new TimeSpanAtOrAfterExpression(TimeSpan.Zero, TimeSpan.Zero);
        int before = _history.UndoCount;

        Element videoElement = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 0);
        videoElement.Objects.Add(video);
        Element sourceSoundElement = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 1);
        sourceSoundElement.Objects.Add(sourceSound);
        Element sceneSoundElement = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 2);
        sceneSoundElement.Objects.Add(sceneSound);

        bool videoApplied = _service.Slip(_scene, [videoElement], TimeSpan.FromSeconds(1));
        bool sourceSoundApplied = _service.Slip(_scene, [sourceSoundElement], TimeSpan.FromSeconds(1));
        bool sceneSoundApplied = _service.Slip(_scene, [sceneSoundElement], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(videoApplied, Is.False);
            Assert.That(sourceSoundApplied, Is.False);
            Assert.That(sceneSoundApplied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sourceSound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sceneSound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
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
    public void Slip_VideoNestedInTimeMappingPresenter_UsesMappedSourceRange()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 150)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_VideoNestedInSpecializedTimeMappingPresenter_IsCollected()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var video = new SourceVideo();
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_VideoNestedInExpressionBackedSpecializedPresenter_IsResolvedAtCompositionTime()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var video = new SourceVideo();
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            TargetStateResolver = range => [new PresenterTargetState(range, video)],
        };
        presenter.Target.Expression = new ConstantSourceVideoExpression(video);
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_ExpressionBackedPresenterWithoutCompleteTargetStatesFailsClosed()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var video = new SourceVideo();
        var presenter = new TestSourceVideoTimeMappingPresenter();
        presenter.Target.Expression = new ConstantSourceVideoExpression(video);
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_DrawableTimeControllerWithExpressionTargetFailsClosed()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var video = new SourceVideo();
        var controller = new DrawableTimeController();
        controller.Target.Expression = new ConstantSourceVideoExpression(video);
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_NarrowExpressionTargetStateConstrainsSharedDelta()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var longSource = new VideoSource();
        longSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var shortSource = new VideoSource();
        shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 3)));
        var longVideo = new SourceVideo
        {
            Source = { CurrentValue = longSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var shortVideo = new SourceVideo
        {
            Source = { CurrentValue = shortSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(100)),
        };
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            TargetStateResolver = range => CreatePresenterTargetStates(
                range,
                longVideo,
                (TimeSpan.FromMilliseconds(200), shortVideo),
                (TimeSpan.FromMilliseconds(300), longVideo)),
        };
        presenter.Target.Expression = new NarrowSourceVideoExpression(
            longVideo,
            shortVideo,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(300));
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(longVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(shortVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_ExpressionBackedTimeControllerMappingUsesQueriedCompositionTime()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var controller = new DrawableTimeController
        {
            TimeRange = element.Range,
            Target = { CurrentValue = video },
        };
        controller.OffsetPosition.Expression = new TimeSpanAtOrAfterExpression(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(9));
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_NarrowControllerMappingExpressionFailsClosed()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var controller = new DrawableTimeController
        {
            Target = { CurrentValue = video },
        };
        controller.OffsetPosition.Expression = new NarrowTimeSpanExpression(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromSeconds(9));
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_ExpressionBackedSourceVideoStatesFailClosed()
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        Element sourceElement = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 0);
        var sourceVideo = new SourceVideo
        {
            Source = { CurrentValue = source },
        };
        sourceVideo.Source.Expression = new SwitchingVideoSourceExpression(
            source, source, TimeSpan.FromMilliseconds(200));
        sourceElement.Objects.Add(sourceVideo);

        Element loopElement = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1), zIndex: 1);
        var loopVideo = new SourceVideo
        {
            Source = { CurrentValue = source },
        };
        loopVideo.IsLoop.Expression = new ConstantBoolExpression(true);
        loopElement.Objects.Add(loopVideo);

        bool sourceApplied = _service.Slip(_scene, [sourceElement], TimeSpan.FromSeconds(1));
        bool loopApplied = _service.Slip(_scene, [loopElement], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(sourceApplied, Is.False);
            Assert.That(loopApplied, Is.False);
            Assert.That(sourceVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loopVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ResizeBounds_FuturePresenterTargetConstrainsTailAfterKeyframe()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(4));
        var currentSource = new VideoSource();
        currentSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var futureSource = new VideoSource();
        futureSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 150)));
        var currentVideo = new SourceVideo
        {
            Source = { CurrentValue = currentSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var futureVideo = new SourceVideo
        {
            Source = { CurrentValue = futureSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        var targetAnimation = new KeyFrameAnimation<SourceVideo?>();
        targetAnimation.KeyFrames.Add(new KeyFrame<SourceVideo?>
        {
            KeyTime = TimeSpan.Zero,
            Value = currentVideo,
        });
        targetAnimation.KeyFrames.Add(new KeyFrame<SourceVideo?>
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Value = futureVideo,
        });
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            Target = { CurrentValue = currentVideo, Animation = targetAnimation },
            TargetStateResolver = range => CreatePresenterTargetStates(
                range,
                currentVideo,
                (TimeSpan.FromSeconds(5), futureVideo)),
        };
        presenter.Target.Expression = new SwitchingSourceVideoExpression(
            currentVideo, futureVideo, TimeSpan.FromSeconds(5));
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ResizeBounds_ProceduralPresenterQueriesOnlyReachableRange()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var longSource = new VideoSource();
        longSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var shortSource = new VideoSource();
        shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 45)));
        var longVideo = new SourceVideo
        {
            Source = { CurrentValue = longSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var shortVideo = new SourceVideo
        {
            Source = { CurrentValue = shortSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            TargetStateResolver = range => CreateAlternatingPresenterTargetStates(
                range,
                TimeSpan.FromSeconds(1),
                longVideo,
                shortVideo),
        };
        front.Objects.Add(presenter);
        var resizeService = new ElementResizeService(_history);

        (TimeSpan _, TimeSpan max) = resizeService.GetTrimDeltaBounds(
            _scene,
            [new ElementTrimPair(front, back)]);

        Assert.Multiple(() =>
        {
            Assert.That(max, Is.EqualTo(TimeSpan.FromMilliseconds(500)).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(presenter.ObservedTargetStateRanges, Is.Not.Empty);
            Assert.That(
                presenter.ObservedTargetStateRanges.All(range => range.End < TimeSpan.MaxValue),
                Is.True);
        });
    }

    [Test]
    public void Collect_ReversedFutureNestedPresenterSelectsPrecedingBoundaryState()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var video = new SourceVideo();
        var nested = new TestSourceVideoTimeMappingPresenter
        {
            TargetStateResolver = range => [new PresenterTargetState(range, video)],
        };
        var current = new DrawableGroup();
        var outer = new TestTimeMappingPresenter
        {
            MapRangeBackward = true,
            ReverseSelector = _ => true,
            TargetStateResolver = range => CreatePresenterTargetStates(
                range,
                current,
                (TimeSpan.FromSeconds(1), nested)),
        };
        element.Objects.Add(outer);

        SlippableMedia.TargetCollection? targets = null;
        Assert.DoesNotThrow(() =>
            targets = SlippableMedia.Collect(element, TimeSpan.FromSeconds(2)));

        Assert.Multiple(() =>
        {
            Assert.That(targets, Is.Not.Null);
            Assert.That(targets!.IsComplete, Is.True);
            Assert.That(targets, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Slip_FuturePresenterTargetIsNotShiftedBeforeItBecomesActive()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(4));
        var currentVideo = new SourceVideo();
        var futureVideo = new SourceVideo();
        var targetAnimation = new KeyFrameAnimation<SourceVideo?>();
        targetAnimation.KeyFrames.Add(new KeyFrame<SourceVideo?>
        {
            KeyTime = TimeSpan.Zero,
            Value = currentVideo,
        });
        targetAnimation.KeyFrames.Add(new KeyFrame<SourceVideo?>
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Value = futureVideo,
        });
        var presenter = new TestSourceVideoTimeMappingPresenter
        {
            Target = { CurrentValue = currentVideo, Animation = targetAnimation },
            TargetStateResolver = range => CreatePresenterTargetStates(
                range,
                currentVideo,
                (TimeSpan.FromSeconds(5), futureVideo)),
        };
        presenter.Target.Expression = new SwitchingSourceVideoExpression(
            currentVideo, futureVideo, TimeSpan.FromSeconds(5));
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(currentVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(futureVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_AnimatedSourceUsesTightestActiveSourceBound()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(6));
        var longSource = new VideoSource();
        longSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var shortSource = new VideoSource();
        shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 240)));
        var sourceAnimation = new KeyFrameAnimation<VideoSource?>();
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.Zero,
            Value = longSource,
        });
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Value = shortSource,
        });
        var video = new SourceVideo
        {
            Source = { CurrentValue = longSource, Animation = sourceAnimation },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void ResizeBounds_AnimatedSourceSkipsSourceLessStateBeforeNextSource()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var currentSource = new VideoSource();
        currentSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 600)));
        var futureSource = new VideoSource();
        futureSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 600)));
        var sourceAnimation = new KeyFrameAnimation<VideoSource?>();
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.Zero,
            Value = currentSource,
        });
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Value = null,
        });
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(6),
            Value = futureSource,
        });
        var video = new SourceVideo
        {
            Source = { CurrentValue = currentSource, Animation = sourceAnimation },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        element.Objects.Add(video);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.FromSeconds(19)));
    }

    [Test]
    public void ResizeBounds_LoopedSourceNormalizesMappedPositionBeforeOffset()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = source },
            IsLoop = { CurrentValue = true },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(1) },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(-2),
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ResizeBounds_TimeMappingPresenterUsesRangeAwareReversal()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = source },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(-2),
            ReverseSelector = range => range.Start == TimeSpan.Zero,
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ResizeBounds_ReversedMappingScansEarlierSourceStates()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var shortSource = new VideoSource();
        shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 90)));
        var longSource = new VideoSource();
        longSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var sourceAnimation = new KeyFrameAnimation<VideoSource?>();
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(4),
            Value = shortSource,
        });
        sourceAnimation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(5),
            Value = longSource,
        });
        var video = new SourceVideo
        {
            Source = { CurrentValue = longSource, Animation = sourceAnimation },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(4),
            ReverseSelector = _ => true,
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ResizeBounds_ReversedLoopStopsAtPreviousWrap()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
            100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = source },
            IsLoop = { CurrentValue = true },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(1) },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(20)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.FromSeconds(11),
            ReverseSelector = _ => true,
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        TimeSpan room = GetOutPointRoom(element);

        Assert.That(room, Is.EqualTo(TimeSpan.FromSeconds(1)).Within(TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public void ResizeBounds_AnimatedLoopUsesLoopStateAtMappedInterval()
    {
        Element element = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var loopAnimation = new KeyFrameAnimation<bool>();
        loopAnimation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.Zero, Value = false });
        loopAnimation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(0.1), Value = true });
        var video = new SourceVideo
        {
            Source = { CurrentValue = source },
            IsLoop = { CurrentValue = false, Animation = loopAnimation },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        element.Objects.Add(video);
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
        var resizeService = new ElementResizeService(_history);

        (TimeSpan _, TimeSpan max) = resizeService.GetTrimDeltaBounds(
            _scene,
            [new ElementTrimPair(element, back)]);

        TimeSpan minDuration = TimeSpan.FromSeconds(1d / 30);
        Assert.That(max, Is.EqualTo(TimeSpan.FromSeconds(10) - minDuration));
    }

    [Test]
    public void Slip_NestedTimeMappingPresenter_PropagatesUnboundedDuration()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            Speed = { CurrentValue = 0f },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        var presenter = new TestTimeMappingPresenter
        {
            MappedStart = TimeSpan.Zero,
            ThrowOnUnboundedDuration = true,
            Target = { CurrentValue = video },
        };
        element.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(presenter.TimelineDurationCallCount, Is.Zero);
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
    public void Slip_VideoNestedInOffsetTimeController_UsesAbsoluteSourceEndpoint()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var controller = new DrawableTimeController
        {
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(5) },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(4)));
        });
    }

    [Test]
    public void Slip_SharedSource_MergesZeroConsumptionFrameReservation()
    {
        Element element = AddElement(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        var quantizedController = new DrawableTimeController
        {
            FrameRate = { CurrentValue = 1f },
            Target = { CurrentValue = video },
        };
        var shortController = new DrawableTimeController
        {
            Speed = { CurrentValue = 10f },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(quantizedController);
        element.Objects.Add(shortController);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));
        TimeSpan frameDuration = TimeSpan.FromSeconds(1d / 30);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3) - frameDuration));
        });
    }

    [Test]
    public void Slip_VideoNestedInLoopingTimeController_UsesRenderedSourceRange()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        var controller = new DrawableTimeController
        {
            Loop = { CurrentValue = true },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Slip_VideoNestedInLoopingTimeController_ConsumesFullCycle()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 270)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        var controller = new DrawableTimeController
        {
            Loop = { CurrentValue = true },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(8));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(6)));
        });
    }

    [Test]
    public void Slip_LoopedSourceVideo_NormalizesMappedSourcePosition()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            IsLoop = { CurrentValue = true },
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var controller = new DrawableTimeController
        {
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(10) },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void Slip_LoopedSourceVideo_ReservesFullCycleWhenMappedRangeWraps()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            IsLoop = { CurrentValue = true },
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var controller = new DrawableTimeController
        {
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(9.5) },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(controller);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Slip_SharedSourceThroughControllers_MergesTightestPathBounds()
    {
        Element element = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo
        {
            Source = { CurrentValue = videoSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)),
        };
        var shortController = new DrawableTimeController
        {
            Speed = { CurrentValue = 200f },
            Target = { CurrentValue = video },
        };
        var longController = new DrawableTimeController
        {
            Speed = { CurrentValue = 900f },
            Target = { CurrentValue = video },
        };
        element.Objects.Add(shortController);
        element.Objects.Add(longController);

        bool applied = _service.Slip(_scene, [element], TimeSpan.FromSeconds(5));

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

internal sealed partial class TestTimeMappingPresenter : Drawable, ITimeMappingPresenter<Drawable>
{
    public IProperty<Drawable?> Target { get; } = Property.Create<Drawable?>();

    public Func<TimeRange, IReadOnlyList<PresenterTargetState>?>? TargetStateResolver { get; set; }

    public TimeSpan MappedStart { get; set; } = TimeSpan.FromSeconds(4);

    public bool MapRangeBackward { get; set; }

    public bool ThrowOnUnboundedDuration { get; set; }

    public bool ReportsUnboundedTail { get; set; }

    public Func<TimeRange, bool>? ReverseSelector { get; set; }

    public int TimelineDurationCallCount { get; private set; }

    public bool TryGetTargetStates(
        TimeRange compositionRange,
        out IReadOnlyList<PresenterTargetState> states)
    {
        if (TargetStateResolver is { } resolver)
        {
            IReadOnlyList<PresenterTargetState>? resolved = resolver(compositionRange);
            states = resolved ?? [];
            return resolved != null;
        }

        if (compositionRange.IsEmpty)
        {
            states = [];
            return true;
        }

        if (Target.HasExpression || Target.Animation != null)
        {
            states = [];
            return false;
        }

        states = [new PresenterTargetState(compositionRange, Target.CurrentValue)];
        return true;
    }

    public bool IsReversed(TimeRange timeRange, Drawable target)
        => ReverseSelector?.Invoke(timeRange) ?? false;

    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, Drawable target)
        => MapRangeBackward
            ? new(MappedStart - timeRange.Duration, timeRange.Duration)
            : new(MappedStart, timeRange.Duration);

    public bool HasUnboundedTail(TimeRange timeRange, Drawable target, bool reverse = false)
        => ReportsUnboundedTail;

    public TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        Drawable target,
        bool reverse = false)
    {
        TimelineDurationCallCount++;
        if (ThrowOnUnboundedDuration && targetDuration == TimeSpan.MaxValue)
            throw new InvalidOperationException("The unbounded duration sentinel must not reach the presenter.");

        return targetDuration;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class TestSourceVideoTimeMappingPresenter : Drawable, ITimeMappingPresenter<SourceVideo>
{
    public IProperty<SourceVideo?> Target { get; } = Property.CreateAnimatable<SourceVideo?>();

    public Func<TimeRange, IReadOnlyList<PresenterTargetState>?>? TargetStateResolver { get; set; }

    public List<TimeRange> ObservedTargetStateRanges { get; } = [];

    public bool TryGetTargetStates(
        TimeRange compositionRange,
        out IReadOnlyList<PresenterTargetState> states)
    {
        ObservedTargetStateRanges.Add(compositionRange);
        if (TargetStateResolver is { } resolver)
        {
            IReadOnlyList<PresenterTargetState>? resolved = resolver(compositionRange);
            states = resolved ?? [];
            return resolved != null;
        }

        if (compositionRange.IsEmpty)
        {
            states = [];
            return true;
        }

        if (Target.HasExpression || Target.Animation != null)
        {
            states = [];
            return false;
        }

        states = [new PresenterTargetState(compositionRange, Target.CurrentValue)];
        return true;
    }

    public bool IsReversed(TimeRange timeRange, SourceVideo target) => false;

    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, SourceVideo target)
        => timeRange;

    public bool HasUnboundedTail(TimeRange timeRange, SourceVideo target, bool reverse = false)
        => false;

    public TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        SourceVideo target,
        bool reverse = false)
        => targetDuration;

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class TestSourceSoundTimeMappingPresenter : Drawable, ITimeMappingPresenter<SourceSound>
{
    public IProperty<SourceSound?> Target { get; } = Property.Create<SourceSound?>();

    public double Scale { get; set; } = 1;

    public TimeSpan TargetOffset { get; set; }

    public bool IsReversed(TimeRange timeRange, SourceSound target) => false;

    public TimeRange CalculateTargetTimeRange(TimeRange timeRange, SourceSound target)
        => new(
            timeRange.Start + TargetOffset,
            TimeSpan.FromTicks((long)(timeRange.Duration.Ticks * Scale)));

    public bool HasUnboundedTail(TimeRange timeRange, SourceSound target, bool reverse = false)
        => false;

    public TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        SourceSound target,
        bool reverse = false)
        => TimeSpan.FromTicks((long)(targetDuration.Ticks / Scale));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class ConstantSourceVideoExpression(SourceVideo target) : IExpression<SourceVideo?>
{
    public string ExpressionString => "constant-source-video";

    public Type ResultType => typeof(SourceVideo);

    public SourceVideo? Evaluate(ExpressionContext context) => target;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class TimeSpanAtOrAfterExpression(TimeSpan threshold, TimeSpan value) : IExpression<TimeSpan>
{
    public string ExpressionString => "time-span-at-or-after";

    public Type ResultType => typeof(TimeSpan);

    public TimeSpan Evaluate(ExpressionContext context)
        => context.Time >= threshold ? value : TimeSpan.Zero;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class NarrowTimeSpanExpression(
    TimeSpan start,
    TimeSpan end,
    TimeSpan value) : IExpression<TimeSpan>
{
    public string ExpressionString => "narrow-time-span";

    public Type ResultType => typeof(TimeSpan);

    public TimeSpan Evaluate(ExpressionContext context)
        => context.Time >= start && context.Time < end ? value : TimeSpan.Zero;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class ConstantBoolExpression(bool value) : IExpression<bool>
{
    public string ExpressionString => "constant-bool";

    public Type ResultType => typeof(bool);

    public bool Evaluate(ExpressionContext context) => value;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class ConstantSoundSourceExpression(SoundSource source) : IExpression<SoundSource?>
{
    public string ExpressionString => "constant-sound-source";

    public Type ResultType => typeof(SoundSource);

    public SoundSource Evaluate(ExpressionContext context) => source;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class SwitchingSourceVideoExpression(
    SourceVideo before,
    SourceVideo after,
    TimeSpan threshold) : IExpression<SourceVideo?>
{
    public string ExpressionString => "switching-source-video";

    public Type ResultType => typeof(SourceVideo);

    public SourceVideo Evaluate(ExpressionContext context)
        => context.Time < threshold ? before : after;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class SwitchingVideoSourceExpression(
    VideoSource before,
    VideoSource after,
    TimeSpan threshold) : IExpression<VideoSource?>
{
    public string ExpressionString => "switching-video-source";

    public Type ResultType => typeof(VideoSource);

    public VideoSource Evaluate(ExpressionContext context)
        => context.Time < threshold ? before : after;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class NarrowSourceVideoExpression(
    SourceVideo outside,
    SourceVideo inside,
    TimeSpan start,
    TimeSpan end) : IExpression<SourceVideo?>
{
    public string ExpressionString => "narrow-source-video";

    public Type ResultType => typeof(SourceVideo);

    public SourceVideo Evaluate(ExpressionContext context)
        => context.Time >= start && context.Time < end ? inside : outside;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}
