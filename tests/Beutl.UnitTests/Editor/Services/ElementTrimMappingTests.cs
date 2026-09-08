using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementTrimMappingTests
{
    private SceneHistoryHarness _harness = null!;
    private ElementResizeService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("trim_mapping", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _service = new ElementResizeService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    private (Element Front, Element Middle, Element Back) AddClips(bool slide)
    {
        Element front = _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element middle = _harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), slide ? 0 : 1);
        Element back = _harness.AddElement(TimeSpan.FromSeconds(slide ? 5 : 3), TimeSpan.FromSeconds(3));
        return (front, middle, back);
    }

    private bool Apply(bool slide, Element front, Element middle, Element back, double delta)
        => slide
            ? _service.Slide(_harness.Scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(delta))
            : _service.Roll(_harness.Scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(delta));

    [TestCase(false, 200f)]
    [TestCase(true, 200f)]
    [TestCase(false, 50f)]
    [TestCase(true, 50f)]
    [TestCase(false, 0f)]
    [TestCase(true, 0f)]
    public void ConstantSpeed_PreservesSourceTimeAndUndo(bool slide, float speed)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo { Speed = { CurrentValue = speed } };
        back.Objects.Add(video);
        TimeSpan originalStart = back.Start;
        var context = new CompositionContext(back.Start + TimeSpan.FromSeconds(2));
        using var before = (SourceVideo.Resource)video.ToResource(context);
        TimeSpan oldSourceTime = before.RequestedPosition + before.OffsetPosition;

        Assert.That(Apply(slide, front, middle, back, 1), Is.True);
        using var after = (SourceVideo.Resource)video.ToResource(context);
        TimeSpan newSourceTime = after.RequestedPosition + after.OffsetPosition;
        Assert.That(newSourceTime, Is.EqualTo(oldSourceTime));
        Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(speed / 100d));
        _harness.History.Undo();
        Assert.That(back.Start, Is.EqualTo(originalStart));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NegativeDelta_ConvertsSourceHeadroomToTimeline(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo
        {
            Speed = { CurrentValue = 200f },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(0.5) },
        };
        back.Objects.Add(video);
        TimeSpan originalStart = back.Start;
        Assert.That(Apply(slide, front, middle, back, -1), Is.True);
        Assert.That(back.Start, Is.EqualTo(originalStart - TimeSpan.FromSeconds(0.25)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedSpeed_RejectsWithoutGeometryOffsetOrHistoryWrites(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(3) });
        var video = new SourceVideo { Speed = { Animation = animation } };
        back.Objects.Add(video);
        TimeSpan originalStart = back.Start;
        int undoCount = _harness.History.UndoCount;

        Assert.That(Apply(slide, front, middle, back, 1), Is.False);
        Assert.That(back.Start, Is.EqualTo(originalStart));
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FlowController_WithDeclaredTargetIsStillRejected(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo { Speed = { CurrentValue = 200f } };
        var controller = new DrawableTimeController
        {
            Speed = { CurrentValue = 200f },
            Target = { CurrentValue = video },
        };
        back.Objects.Add(controller);
        Assert.That(Apply(slide, front, middle, back, 1), Is.False);
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IndependentAnchor_DoesNotAdvanceOffset(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo
        {
            IsTimeAnchor = true,
            TimeRange = new Beutl.Media.TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(20)),
        };
        back.Objects.Add(new DrawablePresenter { Target = { CurrentValue = video } });
        Assert.That(Apply(slide, front, middle, back, 1), Is.True);
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(false, "loop")]
    [TestCase(true, "loop")]
    [TestCase(false, "reverse")]
    [TestCase(true, "reverse")]
    [TestCase(false, "hold")]
    [TestCase(true, "hold")]
    [TestCase(false, "quantized")]
    [TestCase(true, "quantized")]
    public void NonlinearController_RejectsAtomically(bool slide, string kind)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo();
        var controller = new DrawableTimeController { Target = { CurrentValue = video } };
        controller.Loop.CurrentValue = kind == "loop";
        controller.Reverse.CurrentValue = kind == "reverse";
        controller.HoldLastFrame.CurrentValue = kind == "hold";
        controller.FrameRate.CurrentValue = kind == "quantized" ? 30 : 0;
        back.Objects.Add(controller);
        int undoCount = _harness.History.UndoCount;
        TimeSpan originalStart = back.Start;
        Assert.That(Apply(slide, front, middle, back, 1), Is.False);
        Assert.That(back.Start, Is.EqualTo(originalStart));
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }

    [Test]
    public void FlowController_RejectsAllPairs()
    {
        var (front, middle, back) = AddClips(false);
        Element otherBack = _harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), 1);
        var video = new SourceVideo();
        back.Objects.Add(video);
        otherBack.Objects.Add(new DrawableTimeController
        {
            Speed = { CurrentValue = 200f },
            Target = { CurrentValue = video },
        });
        int undoCount = _harness.History.UndoCount;
        Assert.That(_service.Roll(_harness.Scene,
            [new ElementTrimPair(front, back), new ElementTrimPair(middle, otherBack)],
            TimeSpan.FromSeconds(1)), Is.False);
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }

    [Test]
    public void Slide_AnimatedMiddleRejectsEntireLane()
    {
        var (front, middle, back) = AddClips(true);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f, KeyTime = TimeSpan.FromSeconds(3) });
        middle.Objects.Add(new SourceVideo { Speed = { Animation = animation } });
        int undoCount = _harness.History.UndoCount;
        Assert.That(Apply(true, front, middle, back, 1), Is.False);
        Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PositiveDelta_ClampsToRepresentableSourceOffset(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo
        {
            Speed = { CurrentValue = 200f },
            OffsetPosition = { CurrentValue = TimeSpan.FromTicks(long.MaxValue - 100) },
        };
        back.Objects.Add(video);
        TimeSpan originalStart = back.Start;
        Assert.That(Apply(slide, front, middle, back, 1), Is.True);
        Assert.That(back.Start, Is.EqualTo(originalStart + TimeSpan.FromTicks(50)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.MaxValue));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LinkedVideoAndSound_UseIndividualSourceSpeeds(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo { Speed = { CurrentValue = 200f } };
        var sound = new Beutl.Audio.SourceSound { Speed = { CurrentValue = 50f } };
        back.Objects.Add(video);
        back.Objects.Add(sound);
        Assert.That(Apply(slide, front, middle, back, 1), Is.True);
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public void GetTrimDeltaBounds_UsesTimelineSpeedAndRejectsAnimation()
    {
        var (front, _, back) = AddClips(false);
        var video = new SourceVideo
        {
            Speed = { CurrentValue = 200f },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(0.5) },
        };
        back.Objects.Add(video);
        var pairs = new[] { new ElementTrimPair(front, back) };
        Assert.That(_service.GetTrimDeltaBounds(_harness.Scene, pairs).Min,
            Is.EqualTo(TimeSpan.FromSeconds(-0.25)));
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f, KeyTime = TimeSpan.FromSeconds(2) });
        video.Speed.Animation = animation;
        Assert.That(_service.GetTrimDeltaBounds(_harness.Scene, pairs),
            Is.EqualTo((TimeSpan.Zero, TimeSpan.Zero)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FixedIdentityReferences_PreserveSharedSourceTime(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var video = new SourceVideo { Speed = { CurrentValue = 200f } };
        back.Objects.Add(video);
        var outer = new DrawablePresenter();
        back.Objects.Add(outer);
        var inner = new DrawablePresenter();
        outer.Target.CurrentValue = inner;
        inner.Target.CurrentValue = video;
        Assert.That(Apply(slide, front, middle, back, 1), Is.True);
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ImplicitFlowTarget_RejectsWithoutWrites(bool portal)
    {
        var (front, middle, back) = AddClips(false);
        var video = new SourceVideo();
        back.Objects.Add(video);
        if (portal)
            back.Objects.Add(new PortalObject());
        else
            back.Objects.Add(new DrawableTimeController { Speed = { CurrentValue = 200f } });
        int undoCount = _harness.History.UndoCount;
        Assert.That(Apply(false, front, middle, back, 1), Is.False);
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }

    [Test]
    public void SharedFrontBackOffset_HasNoPreviewWindow()
    {
        var (front, _, back) = AddClips(false);
        var video = new SourceVideo();
        front.Objects.Add(video);
        back.Objects.Add(new DrawablePresenter { Target = { CurrentValue = video } });
        Assert.That(_service.GetTrimDeltaBounds(_harness.Scene, [new ElementTrimPair(front, back)]),
            Is.EqualTo((TimeSpan.Zero, TimeSpan.Zero)));
    }

    [Test]
    public void UnreachableLocalAnimationBoundary_DoesNotOverflowCollection()
    {
        var (_, _, back) = AddClips(false);
        var animation = new KeyFrameAnimation<bool>();
        animation.KeyFrames.Add(new KeyFrame<bool> { Value = false });
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.MaxValue, Value = true });
        var video = new SourceVideo { IsLoop = { Animation = animation } };
        back.Objects.Add(video);
        Assert.That(SlippableMedia.Collect(back).IsComplete, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedSource_RejectsWithoutWrites(bool slide)
    {
        var (front, middle, back) = AddClips(slide);
        var animation = new KeyFrameAnimation<Beutl.Media.Source.VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<Beutl.Media.Source.VideoSource?> { Value = null });
        var video = new SourceVideo { Source = { Animation = animation } };
        back.Objects.Add(video);
        int undoCount = _harness.History.UndoCount;
        Assert.That(Apply(slide, front, middle, back, 1), Is.False);
        Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(undoCount));
    }
}
