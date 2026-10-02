using Beutl.Animation;
using Beutl.Animation.Easings;
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

[TestFixture]
[NonParallelizable]
public class TrimSpeedTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementSlipService _slip = null!;
    private ElementResizeService _resize = null!;
    private bool _originalClampResizeToOriginalLength;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_trim_speed", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _slip = new ElementSlipService(_history);
        _resize = new ElementResizeService(_history);
        _originalClampResizeToOriginalLength = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = true;
    }

    [TearDown]
    public void TearDown()
    {
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = _originalClampResizeToOriginalLength;
        _harness.Dispose();
    }

    [TestCase(nameof(SourceVideo), 200f, 2d)]
    [TestCase(nameof(SourceVideo), 50f, 0.5d)]
    [TestCase(nameof(SourceSound), 200f, 2d)]
    [TestCase(nameof(SourceSound), 50f, 0.5d)]
    [TestCase(nameof(SceneSound), 200f, 2d)]
    [TestCase(nameof(SceneSound), 50f, 0.5d)]
    public void Slip_ConstantSpeed_ConvertsTimelineDeltaToSourceOffset(
        string mediaType, float speed, double expectedSourceDelta)
    {
        Element element = AddElement(5, 2);
        var media = AddMedia(element, mediaType, speed, offsetSeconds: 1);
        int before = _history.UndoCount;

        bool applied = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1 + expectedSourceDelta)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slip_FastClip_ClampsUsingRawSourceDuration(string mediaType)
    {
        Element element = AddElement(5, 2);
        var fast = AddMedia(element, mediaType, 200, durationSeconds: 5);
        var slow = AddMedia(element, nameof(SourceSound), 50);
        int before = _history.UndoCount;

        // The clip consumes 4s of the 5s source. Its 1s source headroom permits only
        // +0.5s on the timeline, which advances the linked 50% stream by 0.25s.
        bool applied = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(fast.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(slow.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        bool appliedAtEnd = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(appliedAtEnd, Is.False);
            Assert.That(fast.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(slow.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slip_NegativeDelta_ClampsInTimelineUnits(string mediaType)
    {
        Element element = AddElement(5, 2);
        var fast = AddMedia(element, mediaType, 200, offsetSeconds: 1);
        var slow = AddMedia(element, nameof(SourceSound), 50, offsetSeconds: 1);

        // A 1s source offset at 200% permits -0.5s, so the linked 50% stream loses 0.25s.
        bool applied = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(-2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(fast.Offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(slow.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.75)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_GroupedMixedSpeeds_ApplyTheSameClampedTimelineDelta(bool reverseOrder)
    {
        Element fastElement = AddElement(5, 2, zIndex: 0);
        Element slowElement = AddElement(5, 2, zIndex: 1);
        Element normalElement = AddElement(5, 2, zIndex: 2);
        var fast = AddMedia(fastElement, nameof(SourceVideo), 200);
        var slow = AddMedia(slowElement, nameof(SourceSound), 50, offsetSeconds: 0.75, durationSeconds: 2);
        var normal = AddMedia(normalElement, nameof(SceneSound), 100, offsetSeconds: 2);
        Element[] elements = reverseOrder
            ? [normalElement, slowElement, fastElement]
            : [fastElement, slowElement, normalElement];
        int before = _history.UndoCount;

        // The slow clip has 2 - 0.75 - (2 * 0.5) = 0.25s source headroom:
        // every selected element must take +0.5s, even though the source deltas differ.
        bool applied = _slip.Slip(_scene, elements, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(fast.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(slow.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(normal.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            foreach (Element element in elements)
            {
                Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            }
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Roll_FastBack_AdvancesSourceOffsetByTwiceTheTimelineDelta(string mediaType)
    {
        Element front = AddElement(3, 2);
        Element back = AddElement(5, 3);
        var media = AddMedia(back, mediaType, 200, offsetSeconds: 1);

        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slide_FastBack_AdvancesSourceOffsetAndPreservesMiddleContent(string mediaType)
    {
        Element front = AddElement(0, 2);
        Element middle = AddElement(2, 3);
        Element back = AddElement(5, 3);
        var middleMedia = AddMedia(middle, mediaType, 200, offsetSeconds: 1);
        var backMedia = AddMedia(back, mediaType, 200, offsetSeconds: 1);

        bool applied = _resize.Slide(
            _scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(backMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middleMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Roll_FastFront_ClampsOutPointUsingSourceOffsetAndSpeed(string mediaType)
    {
        Element front = AddElement(0, 2);
        Element back = AddElement(2, 3);
        var frontMedia = AddMedia(front, mediaType, 200, offsetSeconds: 1, durationSeconds: 6);
        var backMedia = AddMedia(back, mediaType, 200);

        // 6s total - 1s offset - 4s consumed leaves 1s source / 0.5s timeline headroom.
        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(backMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slide_FastFront_ClampsOutPointUsingSourceOffsetAndSpeed(string mediaType)
    {
        Element front = AddElement(0, 2);
        Element middle = AddElement(2, 1);
        Element back = AddElement(3, 3);
        var frontMedia = AddMedia(front, mediaType, 200, offsetSeconds: 1, durationSeconds: 6);
        var backMedia = AddMedia(back, mediaType, 200);

        bool applied = _resize.Slide(
            _scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(backMedia.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(3.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(6)));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Roll_FastBack_ClampsNegativeDeltaToSourceInPoint(string mediaType)
    {
        Element front = AddElement(0, 2);
        Element back = AddElement(2, 2);
        var media = AddMedia(back, mediaType, 200, offsetSeconds: 1, durationSeconds: 5);

        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(4)));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slide_FastBack_ClampsNegativeDeltaToSourceInPoint(string mediaType)
    {
        Element front = AddElement(0, 2);
        Element middle = AddElement(2, 1);
        Element back = AddElement(3, 2);
        var media = AddMedia(back, mediaType, 200, offsetSeconds: 1, durationSeconds: 5);

        bool applied = _resize.Slide(
            _scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [TestCase(nameof(SourceVideo), false, 1.05d)]
    [TestCase(nameof(SourceVideo), true, 1.55d)]
    [TestCase(nameof(SourceSound), false, 1.05d)]
    [TestCase(nameof(SourceSound), true, 1.55d)]
    [TestCase(nameof(SceneSound), false, 1.05d)]
    [TestCase(nameof(SceneSound), true, 1.55d)]
    public void Slip_AnimatedSpeed_IntegratesFromElementStart(
        string mediaType, bool useGlobalClock, double expectedSourceDelta)
    {
        Element element = AddElement(5, 2);
        var media = AddMedia(element, mediaType, 50, offsetSeconds: 1);
        media.Speed.Animation = CreateSpeedRamp(useGlobalClock);

        // speed(t) / 100 = 1 + t/10: integral 0..1 = 1.05 (local), 5..6 = 1.55 (global).
        bool applied = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue.TotalSeconds,
                Is.EqualTo(1 + expectedSourceDelta).Within(0.005));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [TestCase(nameof(SourceVideo), false, 1.05d)]
    [TestCase(nameof(SourceVideo), true, 1.55d)]
    [TestCase(nameof(SourceSound), false, 1.05d)]
    [TestCase(nameof(SourceSound), true, 1.55d)]
    [TestCase(nameof(SceneSound), false, 1.05d)]
    [TestCase(nameof(SceneSound), true, 1.55d)]
    public void Roll_AnimatedBackSpeed_IntegratesFromOriginalElementStart(
        string mediaType, bool useGlobalClock, double expectedSourceDelta)
    {
        Element front = AddElement(3, 2);
        Element back = AddElement(5, 3);
        var media = AddMedia(back, mediaType, 50, offsetSeconds: 1);
        media.Speed.Animation = CreateSpeedRamp(useGlobalClock);

        // Use the original start at 5s for global 5..6, not the updated start at 6s.
        // The local curve always uses 0..1, whose integral is 1.05s.
        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue.TotalSeconds,
                Is.EqualTo(1 + expectedSourceDelta).Within(0.005));
            Assert.That(front.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });
    }

    [Test]
    public void Roll_FastBack_UndoRedoRestoresGeometryAndSourceOffsetTogether()
    {
        Element front = AddElement(3, 2);
        Element back = AddElement(5, 3);
        var media = AddMedia(back, nameof(SourceVideo), 200, offsetSeconds: 1);
        _history.Commit("Seed trim clips");
        int before = _history.UndoCount;

        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        Assert.That(_history.Undo(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Contain(front));
            Assert.That(_scene.Children, Does.Contain(back));
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
            Assert.That(_history.RedoCount, Is.EqualTo(1));
        });

        Assert.That(_history.Redo(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            Assert.That(_history.RedoCount, Is.Zero);
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slip_BackwardAcrossNegativeLocalKeys_IntegratesPreroll(string mediaType)
    {
        Element element = AddElement(5, 2);
        var media = AddMedia(element, mediaType, 100, offsetSeconds: 2);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(-1),
            Value = 100,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 200,
            Easing = new LinearEasing()
        });
        media.Speed.Animation = animation;

        Assert.That(_slip.Slip(_scene, [element], TimeSpan.FromSeconds(-0.5)), Is.True);
        // Integral of 1.5 + 0.5t over -0.5..0 is 0.6875 seconds of source.
        Assert.That(media.Offset.CurrentValue.TotalSeconds, Is.EqualTo(1.3125).Within(0.005));
    }

    [TestCase(nameof(SourceVideo), false)]
    [TestCase(nameof(SourceVideo), true)]
    [TestCase(nameof(SourceSound), false)]
    [TestCase(nameof(SourceSound), true)]
    [TestCase(nameof(SceneSound), false)]
    [TestCase(nameof(SceneSound), true)]
    public void Trim_DecreasingLocalSpeed_ClampsInteriorDeltaAndMatchesPreview(string mediaType, bool slide)
    {
        Element front = AddElement(0, 4);
        Element? middle = slide ? AddElement(4, 1) : null;
        Element back = AddElement(slide ? 5 : 4, 4);
        TimeSpan originalStart = back.Start;
        TimeSpan originalEnd = back.Range.End;
        var media = AddMedia(back, mediaType, 300, durationSeconds: 9);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 300, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(4), Value = 100, Easing = new LinearEasing() });
        media.Speed.Animation = animation;
        TimeSpan requested = TimeSpan.FromSeconds(2);
        var constraints = ElementResizeService.CreateTrimConstraints(_scene, [new ElementTrimPair(front, back)]);
        TimeSpan preview = constraints.Clamp(requested);

        bool applied = slide
            ? _resize.Slide(_scene, [new ElementSlideLane(front, [middle!], back)], requested)
            : _resize.Roll(_scene, [new ElementTrimPair(front, back)], requested);

        TimeSpan sourceEnd;
        if (back.Objects[0] is SourceVideo video)
        {
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(back.Range.End));
            sourceEnd = resource.RequestedPosition + resource.OffsetPosition;
        }
        else
        {
            using var integrator = new SpeedIntegrator(44100);
            integrator.EnsureCache(animation);
            sourceEnd = media.Offset.CurrentValue + integrator.Integrate(back.Length, animation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(preview.TotalSeconds, Is.InRange(0.5, 0.6),
                "Safe endpoint deltas must not hide the unsafe 2s interior request.");
            Assert.That(back.Start - originalStart, Is.EqualTo(preview));
            Assert.That(back.Range.End, Is.EqualTo(originalEnd));
            Assert.That(sourceEnd, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(9)));
        });
    }

    [Test]
    public void Roll_SharedSourceWithIncompatibleController_PreviewAndCommitBothRefuse()
    {
        Element frontA = AddElement(0, 2, 0);
        Element backA = AddElement(2, 3, 0);
        Element frontB = AddElement(0, 2, 1);
        Element backB = AddElement(2, 3, 1);
        var video = new SourceVideo();
        backA.Objects.Add(video);
        var presenter = new DrawablePresenter { Target = { CurrentValue = video } };
        backB.Objects.Add(presenter);
        backB.Objects.Add(new DrawableTimeController { Speed = { CurrentValue = 200 } });
        ElementTrimPair[] pairs = [new(frontA, backA), new(frontB, backB)];
        int before = _history.UndoCount;

        TimeSpan preview = ElementResizeService.CreateTrimConstraints(_scene, pairs).Clamp(TimeSpan.FromSeconds(1));
        bool applied = _resize.Roll(_scene, pairs, TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(TimeSpan.Zero));
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(backA.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(backB.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slide_GlobalAnimatedMiddle_ClampsMovedWindowWithoutChangingOffset(string mediaType)
    {
        Element front = AddElement(0, 5);
        Element middle = AddElement(5, 2);
        Element back = AddElement(7, 4);
        var media = AddMedia(middle, mediaType, 100, durationSeconds: 5);
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(10), Value = 300, Easing = new LinearEasing() });
        media.Speed.Animation = animation;
        TimeSpan requested = TimeSpan.FromSeconds(2);
        TimeSpan preview = ElementResizeService.CreateTrimConstraints(_scene,
            [new ElementTrimPair(front, back)], [middle]).Clamp(requested);

        Assert.That(_resize.Slide(_scene, [new ElementSlideLane(front, [middle], back)], requested), Is.True);

        TimeSpan sourceEnd;
        if (middle.Objects[0] is SourceVideo video)
        {
            using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(middle.Range.End));
            sourceEnd = resource.RequestedPosition + resource.OffsetPosition;
        }
        else
        {
            using var integrator = new SpeedIntegrator(44100);
            integrator.EnsureCache(animation);
            sourceEnd = integrator.Integrate(middle.Range.End, animation) - integrator.Integrate(middle.Start, animation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(preview.TotalSeconds, Is.InRange(1.49, 1.52));
            Assert.That(middle.Start - TimeSpan.FromSeconds(5), Is.EqualTo(preview));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(media.Offset.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sourceEnd, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(11)));
        });
    }

    [Test]
    public void OriginalDuration_LongGlobalAudioCurve_BoundsInterpolationWork()
    {
        Element element = AddElement(600, 2);
        var sound = new SceneSound { ReferencedScene = { CurrentValue = new Scene { Duration = TimeSpan.FromSeconds(900) } } };
        var easing = new CountingEasing();
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1200), Value = 300, Easing = easing });
        sound.Speed.Animation = animation;
        element.Objects.Add(sound);
        TimeSpan? maximum = null;

        Assert.DoesNotThrow(() => maximum = SlippableMedia.GetMaximumDuration(element));
        TestContext.WriteLine($"Speed interpolations: {easing.Calls}; maximum timeline seconds: {maximum?.TotalSeconds}");
        Assert.Multiple(() =>
        {
            Assert.That(easing.Calls, Is.LessThan(20000));
            Assert.That(maximum?.TotalSeconds, Is.EqualTo(Math.Sqrt(2520000) - 1200).Within(0.002));
        });
    }

    private sealed class CountingEasing : Easing
    {
        public int Calls { get; private set; }

        public override float Ease(float progress)
        {
            if (++Calls > 100000) throw new InvalidOperationException("Interactive integration exceeded its evaluation budget.");
            return progress;
        }

        public override bool TryGetOutputRange(out float minimum, out float maximum)
        {
            minimum = 0;
            maximum = 1;
            return true;
        }
    }

    private Element AddElement(double startSeconds, double lengthSeconds, int zIndex = 0)
        => _harness.AddElement(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(lengthSeconds), zIndex);

    private static (IProperty<float> Speed, IProperty<TimeSpan> Offset) AddMedia(
        Element element, string mediaType, float speed, double offsetSeconds = 0, int durationSeconds = 20)
    {
        EngineObject media;
        IProperty<float> speedProperty;
        IProperty<TimeSpan> offsetProperty;
        switch (mediaType)
        {
            case nameof(SourceVideo):
                var videoSource = new VideoSource();
                videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(
                    100, 100, new Rational(30, 1), durationSeconds * 30)));
                var video = new SourceVideo { Source = { CurrentValue = videoSource } };
                media = video;
                speedProperty = video.Speed;
                offsetProperty = video.OffsetPosition;
                break;
            case nameof(SourceSound):
                var soundSource = new SoundSource();
                soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: durationSeconds)));
                var sound = new SourceSound { Source = { CurrentValue = soundSource } };
                media = sound;
                speedProperty = sound.Speed;
                offsetProperty = sound.OffsetPosition;
                break;
            case nameof(SceneSound):
                var sceneSound = new SceneSound
                {
                    ReferencedScene = { CurrentValue = new Scene { Duration = TimeSpan.FromSeconds(durationSeconds) } }
                };
                media = sceneSound;
                speedProperty = sceneSound.Speed;
                offsetProperty = sceneSound.OffsetPosition;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unknown media type.");
        }

        speedProperty.CurrentValue = speed;
        offsetProperty.CurrentValue = TimeSpan.FromSeconds(offsetSeconds);
        element.Objects.Add(media);
        return (speedProperty, offsetProperty);
    }

    private static KeyFrameAnimation<float> CreateSpeedRamp(bool useGlobalClock)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = useGlobalClock };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 100,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(10),
            Value = 200,
            Easing = new LinearEasing()
        });
        return animation;
    }
}
