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
