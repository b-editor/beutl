using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture("video")]
[TestFixture("sound")]
[TestFixture("scene")]
public class TrimSpeedTests(string mediaKind)
{
    private SceneHistoryHarness _harness = null!;
    private ElementSlipService _slip = null!;
    private ElementResizeService _resize = null!;
    private bool _originalClamp;

    private Scene Scene => _harness.Scene;
    private HistoryManager History => _harness.History;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_trim_speed", duration: TimeSpan.FromSeconds(30));
        _slip = new ElementSlipService(History);
        _resize = new ElementResizeService(History);
        _originalClamp = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = true;
    }

    [TearDown]
    public void TearDown()
    {
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = _originalClamp;
        _harness.Dispose();
    }

    [TestCase(50f, 1, 2.5)]
    [TestCase(100f, 1, 3)]
    [TestCase(200f, 1, 4)]
    [TestCase(50f, -1, 1.5)]
    [TestCase(100f, -1, 1)]
    [TestCase(200f, -1, 0)]
    public void Slip_ConvertsTimelineDeltaToSourceTime(float speed, double delta, double expectedOffset)
    {
        Element element = AddElement(1, 1);
        var offset = AddMedia(element, speed, 2);
        History.Commit();
        int before = History.UndoCount;

        bool applied = _slip.Slip(Scene, [element], TimeSpan.FromSeconds(delta));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(History.UndoCount, Is.EqualTo(before + 1));
        });
        History.Undo();
        Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        History.Redo();
        Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
    }

    [TestCase(50f, 5, 8)]
    [TestCase(100f, 4, 3)]
    [TestCase(200f, 2, 0.5)]
    public void Slip_SourceTailClampsLinkedMediaInTimelineTime(
        float speed, double expectedOffset, double expectedDelta)
    {
        Element element = AddElement(1, 2);
        var offset = AddMedia(element, speed, 1);
        var linked = new SourceSound(); // Unknown duration, normal speed.
        element.Objects.Add(linked);
        History.Commit();

        bool applied = _slip.Slip(Scene, [element], TimeSpan.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
            Assert.That(linked.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedDelta)));
        });
        int before = History.UndoCount;
        Assert.That(_slip.Slip(Scene, [element], TimeSpan.FromSeconds(1)), Is.False);
        Assert.That(History.UndoCount, Is.EqualTo(before));
    }

    [TestCase(50f, 3)]
    [TestCase(100f, 4)]
    [TestCase(200f, 4.5)]
    public void Slip_SourceHeadClampsAcrossElementsInTimelineTime(float speed, double expectedLinkedOffset)
    {
        Element element = AddElement(1, 2);
        var offset = AddMedia(element, speed, 1);
        Element other = AddElement(1, 2, 1);
        var linked = new SourceSound { OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(5) } };
        other.Objects.Add(linked);

        bool applied = _slip.Slip(Scene, [element, other], TimeSpan.FromSeconds(-20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(linked.OffsetPosition.CurrentValue,
                Is.EqualTo(TimeSpan.FromSeconds(expectedLinkedOffset)));
        });
    }

    [TestCase(false, 50f, 1, 2.5)]
    [TestCase(false, 200f, 1, 4)]
    [TestCase(false, 50f, -1, 1.5)]
    [TestCase(false, 200f, -1, 0)]
    [TestCase(true, 50f, 1, 2.5)]
    [TestCase(true, 200f, 1, 4)]
    [TestCase(true, 50f, -1, 1.5)]
    [TestCase(true, 200f, -1, 0)]
    [TestCase(false, 0f, 1, 2)]
    [TestCase(true, 0f, -1, 2)]
    public void RollOrSlide_ConvertsBackInPointAndPreservesHistory(
        bool slide, float speed, double delta, double expectedOffset)
    {
        Element front = AddElement(0, 4);
        Element? middle = slide ? AddElement(4, 2) : null;
        var middleOffset = middle != null ? AddMedia(middle, 200, 1) : null;
        double backStart = slide ? 6 : 4;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, speed, 2);
        History.Commit();
        int before = History.UndoCount;

        bool applied = Trim(front, middle, back, delta);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(4 + delta)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + delta)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2 - delta)));
            Assert.That(History.UndoCount, Is.EqualTo(before + 1));
            if (middle != null)
            {
                Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(4 + delta)));
                Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(middleOffset!.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            }
        });
        History.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
        History.Redo();
        Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
        Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + delta)));
    }

    [TestCase(false, 50f, 10)]
    [TestCase(false, 100f, 5)]
    [TestCase(false, 200f, 2.5)]
    [TestCase(true, 50f, 10)]
    [TestCase(true, 100f, 5)]
    [TestCase(true, 200f, 2.5)]
    public void RollOrSlide_SourceTailBoundsUseTimelineTime(bool slide, float speed, double expectedLength)
    {
        Element front = AddElement(0, 2);
        var offset = AddMedia(front, speed, 1);
        Element? middle = slide ? AddElement(2, 2) : null;
        double backStart = slide ? 4 : 2;
        Element back = AddElement(backStart, 20);

        var bounds = _resize.GetTrimDeltaBounds(Scene, [new ElementTrimPair(front, back)]);
        Assert.That(bounds.Max, Is.EqualTo(TimeSpan.FromSeconds(expectedLength - 2)));

        bool applied = Trim(front, middle, back, 15);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(expectedLength)));
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + expectedLength - 2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(22 - expectedLength)));
        });
    }

    [TestCase(false, 50f, -2)]
    [TestCase(false, 100f, -1)]
    [TestCase(false, 200f, -0.5)]
    [TestCase(true, 50f, -2)]
    [TestCase(true, 100f, -1)]
    [TestCase(true, 200f, -0.5)]
    public void RollOrSlide_SourceHeadBoundsUseTimelineTime(bool slide, float speed, double expectedDelta)
    {
        Element front = AddElement(0, 4);
        Element? middle = slide ? AddElement(4, 2) : null;
        double backStart = slide ? 6 : 4;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, speed, 1);

        var bounds = _resize.GetTrimDeltaBounds(Scene, [new ElementTrimPair(front, back)]);
        Assert.That(bounds.Min, Is.EqualTo(TimeSpan.FromSeconds(expectedDelta)));

        bool applied = Trim(front, middle, back, -10);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(4 + expectedDelta)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + expectedDelta)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2 - expectedDelta)));
        });
    }

    [Test]
    public void Slip_ZeroSpeed_DoesNotChangeOffsetOrCommit()
    {
        Element element = AddElement(1, 2);
        var offset = AddMedia(element, 0, 1);
        History.Commit();
        int before = History.UndoCount;

        bool applied = _slip.Slip(Scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(History.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(200f, 100f, 2, true)]
    [TestCase(0f, 100f, 2, true)]
    [TestCase(100f, 50f, 1.5, true)]
    [TestCase(100f, 200f, 3, true)]
    [TestCase(200f, 0f, 1, false)]
    public void Slip_ConstantSpeedAnimation_UsesAnimatedValue(
        float baseSpeed, float animatedSpeed, double expectedOffset, bool expectedApplied)
    {
        Element element = AddElement(1, 1);
        var offset = AddMedia(element, baseSpeed, 1, AnimateSpeed(animatedSpeed, animatedSpeed));
        History.Commit();
        int before = History.UndoCount;

        bool applied = _slip.Slip(Scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedApplied));
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedOffset)));
            Assert.That(History.UndoCount, Is.EqualTo(before + (expectedApplied ? 1 : 0)));
        });
    }

    [TestCase(0f)]
    [TestCase(100f)]
    public void Slip_VaryingSpeedAnimation_RejectsEntireGroup(float baseSpeed)
    {
        Element first = AddElement(1, 1);
        var firstOffset = AddMedia(first, 100, 1);
        Element second = AddElement(1, 1, 1);
        var secondOffset = AddMedia(second, baseSpeed, 1, AnimateSpeed(100, 200));
        History.Commit();
        int before = History.UndoCount;

        bool applied = _slip.Slip(Scene, [first, second], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(firstOffset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(secondOffset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(History.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void RollOrSlide_VaryingSpeedAnimation_RejectsBeforeMutation(
        bool slide, bool animateFront, bool clampToSource)
    {
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = clampToSource;
        Element front = AddElement(0, 2);
        Element? middle = slide ? AddElement(2, 2) : null;
        double backStart = slide ? 4 : 2;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(animateFront ? front : back, 100, 1, AnimateSpeed(100, 200));
        History.Commit();
        int before = History.UndoCount;

        var bounds = _resize.GetTrimDeltaBounds(Scene, [new ElementTrimPair(front, back)]);
        bool applied = Trim(front, middle, back, 1);

        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.EqualTo((TimeSpan.Zero, TimeSpan.Zero)));
            Assert.That(applied, Is.False);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(History.UndoCount, Is.EqualTo(before));
            if (middle != null)
                Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_ConstantSpeedAnimation_UsesAnimatedValue(bool slide)
    {
        Element front = AddElement(0, 2);
        Element? middle = slide ? AddElement(2, 2) : null;
        double backStart = slide ? 4 : 2;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, 200, 1, AnimateSpeed(100, 100));

        bool applied = Trim(front, middle, back, 1);

        Assert.That(applied, Is.True);
        Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + 1)));
    }

    private static KeyFrameAnimation<float> AnimateSpeed(float first, float last)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = first });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = last });
        return animation;
    }

    [Test]
    public void Slip_EmptySpeedAnimation_DoesNotBlockLinkedMedia()
    {
        Element element = AddElement(0, 2);
        var offset = AddMedia(element, 200, 1, new KeyFrameAnimation<float>());
        if (element.Objects[0] is SourceVideo video)
        {
            using var playback = (SourceVideo.Resource)video.ToResource(new CompositionContext(TimeSpan.FromSeconds(1)));
            Assert.That(playback.Speed, Is.Zero);
            Assert.That(playback.RequestedPosition, Is.EqualTo(TimeSpan.Zero));
        }
        var linked = new SourceSound();
        element.Objects.Add(linked);

        bool applied = _slip.Slip(Scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(linked.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_EmptySpeedAnimation_DoesNotBlockTrim(bool slide)
    {
        Element front = AddElement(0, 2);
        Element? middle = slide ? AddElement(2, 2) : null;
        double backStart = slide ? 4 : 2;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, 200, 1, new KeyFrameAnimation<float>());

        bool applied = Trim(front, middle, back, 1);

        Assert.That(applied, Is.True);
        Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + 1)));
    }

    [TestCase("Slip", false)]
    [TestCase("Slip", true)]
    [TestCase("Roll", false)]
    [TestCase("Roll", true)]
    [TestCase("Slide", false)]
    [TestCase("Slide", true)]
    public void Trim_DrawableController_DoesNotInvalidateAudio(string operation, bool explicitTarget)
    {
        Element front = AddElement(0, 2);
        Element? middle = operation == "Slide" ? AddElement(2, 2) : null;
        double backStart = middle != null ? 4 : 2;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, 100, 1);
        back.Objects.Add(new DrawableTimeController
        {
            Speed = { CurrentValue = 50 },
            Target = { CurrentValue = explicitTarget ? new DrawableGroup() : null }
        });

        bool applied = operation == "Slip"
            ? _slip.Slip(Scene, [back], TimeSpan.FromSeconds(1))
            : Trim(front, middle, back, 1);

        // The video is consumed by drawable Flow; audio never enters that flow.
        bool expectedApplied = mediaKind != "video";
        double geometryDelta = expectedApplied && operation != "Slip" ? 1 : 0;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedApplied));
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(expectedApplied ? 2 : 1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2 + geometryDelta)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + geometryDelta)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2 - geometryDelta)));
        });
    }

    [TestCase("Slip", false)]
    [TestCase("Slip", true)]
    [TestCase("Roll", false)]
    [TestCase("Roll", true)]
    [TestCase("Slide", false)]
    [TestCase("Slide", true)]
    public void Trim_DisabledOrFrozenTimeController_DoesNotBlockLinkedMedia(string operation, bool frozen)
    {
        Element front = AddElement(0, 2);
        Element? middle = operation == "Slide" ? AddElement(2, 2) : null;
        double backStart = middle != null ? 4 : 2;
        Element back = AddElement(backStart, 2);
        var offset = AddMedia(back, frozen ? 0 : 200, 0);
        var controller = new DrawableTimeController
        {
            Speed = { CurrentValue = 50 },
            IsEnabled = frozen
        };
        back.Objects.Add(controller);
        var linked = new SourceSound();
        back.Objects.Add(linked);
        if (!frozen)
        {
            var playbackObjects = new List<EngineObject>();
            back.CollectObjects(controller.GetCompositionTarget(), playbackObjects);
            Assert.That(playbackObjects, Does.Not.Contain(controller));
        }
        History.Commit();
        int before = History.UndoCount;

        bool applied = operation == "Slip"
            ? _slip.Slip(Scene, [back], TimeSpan.FromSeconds(1))
            : Trim(front, middle, back, 1);

        double geometryDelta = operation == "Slip" ? 0 : 1;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(frozen ? 0 : 2)));
            Assert.That(linked.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2 + geometryDelta)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(backStart + geometryDelta)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2 - geometryDelta)));
            Assert.That(History.UndoCount, Is.EqualTo(before + 1));
        });
    }

    private Element AddElement(double start, double length, int zIndex = 0)
        => _harness.AddElement(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(length), zIndex);

    private bool Trim(Element front, Element? middle, Element back, double delta)
        => middle == null
            ? _resize.Roll(Scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(delta))
            : _resize.Slide(Scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(delta));

    private IProperty<TimeSpan> AddMedia(
        Element element, float speed, double offsetSeconds, KeyFrameAnimation<float>? animation = null)
    {
        EngineObject media;
        IProperty<TimeSpan> offset;
        if (mediaKind == "video")
        {
            var source = new VideoSource();
            source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 180)));
            var video = new SourceVideo { Source = { CurrentValue = source }, Speed = { CurrentValue = speed } };
            video.Speed.Animation = animation;
            media = video;
            offset = video.OffsetPosition;
        }
        else
        {
            Sound sound;
            if (mediaKind == "sound")
            {
                var source = new SoundSource();
                source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 6)));
                sound = new SourceSound { Source = { CurrentValue = source } };
            }
            else
            {
                sound = new SceneSound
                {
                    ReferencedScene = { CurrentValue = new Scene { Duration = TimeSpan.FromSeconds(6) } }
                };
            }

            sound.Speed.CurrentValue = speed;
            sound.Speed.Animation = animation;
            media = sound;
            offset = sound.OffsetPosition;
        }

        offset.CurrentValue = TimeSpan.FromSeconds(offsetSeconds);
        element.Objects.Add(media);
        return offset;
    }
}
