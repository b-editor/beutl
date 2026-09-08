using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class TimeMappingValidationTests
{
    private sealed class UnknownDipEasing : Easing
    {
        public override float Ease(float progress) => progress is 0 or 1 ? progress : -3;
    }

    [Test]
    public void UnknownEasingRange_DoesNotCertifyPositiveEndpointSpeeds()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 200f,
            Easing = new UnknownDipEasing(),
        });
        video.Speed.Animation = animation;
        Assert.That(SpeedIntegrator.HasInvalidSpeed(animation), Is.True);
        Assert.That(video.CalculateVideoDuration(TimeSpan.Zero, TimeSpan.FromSeconds(1), resource),
            Is.EqualTo(TimeSpan.MaxValue));
        Assert.That(video.CalculateTimelineDuration(TimeSpan.Zero, TimeSpan.FromSeconds(1), resource),
            Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void AnimatedInverse_UnrepresentableTerminalDistanceReturnsUnbounded()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        video.Speed.Animation = animation;
        Assert.That(video.CalculateTimelineDuration(TimeSpan.MinValue, TimeSpan.FromSeconds(1), resource),
            Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void AnimatedDuration_YearLongTerminalTailIsAnalytic()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        video.Speed.Animation = animation;
        Assert.That(video.CalculateVideoDuration(TimeSpan.Zero, TimeSpan.FromDays(365), resource),
            Is.EqualTo(TimeSpan.FromDays(365)));
    }

    [Test]
    public void AnimatedDuration_ConstantRangeBeforeDistantLastKeyIsAnalytic()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromDays(100000), Value = 100f });
        video.Speed.Animation = animation;
        Assert.That(video.CalculateVideoDuration(TimeSpan.FromDays(99999), TimeSpan.FromSeconds(1), resource),
            Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void AnimatedDuration_CancelsDistantPrefixBeforeTerminalTail()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromDays(365), Value = 200f });
        video.Speed.Animation = animation;
        Assert.That(video.CalculateVideoDuration(TimeSpan.FromDays(365), TimeSpan.FromDays(365), resource),
            Is.EqualTo(TimeSpan.FromDays(730)));
    }

    [Test]
    public void AnimatedDuration_TerminalTailPreservesZeroOriginForNegativeClocks()
    {
        var video = new SourceVideo();
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        video.Speed.Animation = animation;
        Assert.That(video.CalculateVideoDuration(TimeSpan.FromDays(-1), TimeSpan.FromDays(2), resource),
            Is.EqualTo(TimeSpan.FromDays(1)));
    }

    [TestCase("offset")]
    [TestCase("global")]
    [TestCase("target")]
    public void Completeness_RejectsUnrepresentableIntermediateMapping(string mode)
    {
        var controller = new DrawableTimeController();
        var target = new SourceVideo { TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };
        var range = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        if (mode == "offset") controller.OffsetPosition.CurrentValue = TimeSpan.MaxValue;
        if (mode == "target") target.TimeRange = new TimeRange(TimeSpan.MaxValue - TimeSpan.FromTicks(2), TimeSpan.FromTicks(1));
        if (mode == "global")
        {
            range = new TimeRange(TimeSpan.MaxValue - TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
            controller.TimeRange = range;
            var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
            animation.KeyFrames.Add(new KeyFrame<float> { Value = 200f });
            controller.Speed.Animation = animation;
        }
        Assert.That(controller.CanProvideCompleteTimeMapping(range, target), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Completeness_RejectsUnrepresentableSpeedConsumption(bool animated)
    {
        var controller = new DrawableTimeController();
        controller.Speed.CurrentValue = float.MaxValue;
        if (animated)
        {
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { Value = float.MaxValue });
            controller.Speed.Animation = animation;
        }
        var target = new SourceVideo { TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };
        Assert.That(controller.CanProvideCompleteTimeMapping(target.TimeRange, target), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Inverse_ZeroTraversalRoomRejectsBeforeClockMapping(bool reverse, bool suppliedResource)
    {
        var target = new SourceVideo { TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };
        var controller = new DrawableTimeController
        {
            Target = { CurrentValue = target },
            OffsetPosition = { CurrentValue = TimeSpan.FromTicks(reverse ? -1 : 1) },
        };
        using var resource = (DrawableTimeController.Resource)controller.ToResource(CompositionContext.Default);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        controller.Speed.Animation = animation;
        TimeSpan start = reverse ? TimeSpan.MinValue : TimeSpan.MaxValue;
        TimeSpan result = suppliedResource
            ? controller.CalculateTimelineDuration(start, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), target, resource, reverse)
            : controller.CalculateTimelineDuration(start, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), target, reverse);
        Assert.That(result, Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void OverflowingRange_IsRejectedBeforeResourceEvaluation()
    {
        var controller = new DrawableTimeController();
        var target = new SourceVideo { TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 100f });
        controller.Speed.Animation = animation;
        var range = new TimeRange(TimeSpan.MaxValue, TimeSpan.FromTicks(2));

        Assert.That(controller.CanProvideCompleteTimeMapping(range, target), Is.False);
        Assert.That(controller.HasUnboundedTail(range, target), Is.False);
        Assert.That(controller.TryGetTargetStates(range, out _), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.CalculateTargetTimeRange(range, target));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.IsReversed(range, target));
        using var resource = (DrawableTimeController.Resource)controller.ToResource(CompositionContext.Default);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.CalculateTargetTimeRange(range, target, resource));
    }
}
