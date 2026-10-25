using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// A split must move the back half's source offset by the source time the front half
// played, not by the timeline duration: at 200% a one-second cut advances the source
// by two seconds.
[TestFixture]
public sealed class ElementSplitSpeedTests
{
    private SceneHistoryHarness _harness = null!;
    private ElementStructureService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_split_speed", start: MediaTime.Zero,
            duration: MediaTime.FromSeconds(60));
        _service = new ElementStructureService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    private Element AddElement(MediaTime start, MediaTime length, params EngineObject[] objects)
    {
        Element element = _harness.AddElement(start, length);
        foreach (EngineObject obj in objects)
            element.AddObject(obj);
        return element;
    }

    private static T BackObject<T>(Element back) where T : class
        => back.Objects.OfType<T>().Single();

    [TestCase(200f, 2.0)]
    [TestCase(50f, 0.5)]
    [TestCase(100f, 1.0)]
    [TestCase(0f, 0.0)]
    public void Split_ConstantSpeedSound_AdvancesTheBackOffsetByTheSourceIntegral(float speed, double expected)
    {
        var sound = new SourceSound { Speed = { Value = speed } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.Multiple(() =>
        {
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(BackObject<SourceSound>(back).OffsetPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(expected)));
        });
    }

    [TestCase(200f, 2.0)]
    [TestCase(50f, 0.5)]
    [TestCase(0f, 0.0)]
    public void Split_ConstantSpeedVideo_AdvancesTheBackOffsetByTheSourceIntegral(float speed, double expected)
    {
        var video = new SourceVideo { Speed = { Value = speed } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), video);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value,
            Is.EqualTo(MediaTime.FromSeconds(expected)));
    }

    [Test]
    public void Split_UsesTheForwardDurationFromTheElementStart()
    {
        var sound = new SourceSound { Speed = { Value = 200f } };
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), sound);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(2));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceSound>(back).OffsetPosition.Value,
            Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Split_AddsTheIntegralToAnExistingOffset()
    {
        var sound = new SourceSound
        {
            Speed = { Value = 200f },
            OffsetPosition = { Value = MediaTime.FromSeconds(3) },
        };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceSound>(back).OffsetPosition.Value,
            Is.EqualTo(MediaTime.FromSeconds(5)));
    }

    [TestCase(nameof(SourceVideo), false, 1.05d)]
    [TestCase(nameof(SourceVideo), true, 1.55d)]
    [TestCase(nameof(SourceSound), false, 1.05d)]
    [TestCase(nameof(SourceSound), true, 1.55d)]
    public void Split_AnimatedSpeed_IntegratesFromTheOriginalElementStart(
        string mediaType, bool useGlobalClock, double expected)
    {
        EngineObject media = mediaType == nameof(SourceVideo) ? new SourceVideo() : new SourceSound();
        IProperty<float> speed = media switch
        {
            SourceVideo video => video.Speed,
            SourceSound sound => sound.Speed,
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
        };
        Element element = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2), media);
        speed.SetKeyFrameAnimation(TrimTestMedia.CreateSpeedRamp(useGlobalClock));

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(6));

        Element back = _harness.Scene.Children.Single(child => child != element);
        MediaTime offset = mediaType == nameof(SourceVideo)
            ? BackObject<SourceVideo>(back).OffsetPosition.Value
            : BackObject<SourceSound>(back).OffsetPosition.Value;
        Assert.That(offset.ToSeconds(), Is.EqualTo(expected).Within(0.005));
    }

    [Test]
    public void Split_SourceUnderATimeController_UsesTheControllerMapping()
    {
        var video = new SourceVideo();
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), video);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200f } });

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value,
            Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [TestCase(100f, 0.0)]
    [TestCase(0f, 1.0)]
    public void Split_ReverseController_CompensatesForTheBackDuration(float speed, double expectedOffset)
    {
        var video = new SourceVideo();
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = speed },
            Reverse = { Value = true },
        });

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        SourceVideo backVideo = BackObject<SourceVideo>(back);
        using var resource = (SourceVideo.Resource)backVideo.ToResource(
            new CompositionContext(back.Range.Start + MediaTime.FromSeconds(3)));
        Assert.Multiple(() =>
        {
            Assert.That(backVideo.OffsetPosition.Value.ToSeconds(),
                Is.EqualTo(expectedOffset).Within(0.005));
            Assert.That((resource.RequestedPosition + resource.OffsetPosition).ToSeconds(),
                Is.EqualTo(3 + expectedOffset).Within(0.005));
        });
    }

    [Test]
    public void Split_LoopingVideo_KeepsTheWrappedSourcePosition()
    {
        var video = new SourceVideo
        {
            Source = { Value = TrimTestMedia.CreateVideoSource(2) },
            IsLoop = { Value = true },
            Speed = { Value = 300f },
        };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), video);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        SourceVideo backVideo = BackObject<SourceVideo>(back);
        using var resource = (SourceVideo.Resource)backVideo.ToResource(
            new CompositionContext(back.Range.Start));
        Assert.Multiple(() =>
        {
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(resource.RequestedPosition + resource.OffsetPosition,
                Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Split_AnimatedSpeedUnderAController_MatchesPlaybackAfterTheCut()
    {
        var video = new SourceVideo();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100 },
            Easing = { Value = new LinearEasing() },
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(4) },
            Value = { Value = 140 },
            Easing = { Value = new LinearEasing() },
        });
        video.Speed.SetKeyFrameAnimation(animation);
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200f } });

        using var original = (SourceVideo.Resource)video.ToResource(
            new CompositionContext(MediaTime.FromSeconds(3)));
        MediaTime before = original.RequestedPosition + original.OffsetPosition;

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        SourceVideo backVideo = BackObject<SourceVideo>(back);
        using var after = (SourceVideo.Resource)backVideo.ToResource(
            new CompositionContext(back.Range.Start + MediaTime.FromSeconds(1)));

        Assert.That((after.RequestedPosition + after.OffsetPosition).ToSeconds(),
            Is.EqualTo(before.ToSeconds()).Within(0.01));
    }

    [Test]
    public void Split_SourcesWithDifferentSpeeds_EachUsesItsOwnIntegral()
    {
        var sound = new SourceSound { Speed = { Value = 200f } };
        var video = new SourceVideo { Speed = { Value = 50f } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound, video);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.Multiple(() =>
        {
            Assert.That(BackObject<SourceSound>(back).OffsetPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(0.5)));
        });
    }
}
