using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Audio;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

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

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Split_SceneDrawable_PreservesExistingOffsetUnderATimeController(bool batch, bool referenceExpression)
    {
        var drawable = new SceneDrawable { OffsetPosition = { Value = MediaTime.FromSeconds(1) } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), drawable);
        if (referenceExpression)
            drawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(_harness.Scene, new Scene());
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200f } });

        if (batch)
            new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
                [new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1))], SilenceSplitMode.DeleteSilence, false);
        else
            _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(2));

        Element back = _harness.Scene.Children.Single(child => child != element);
        var copied = BackObject<SceneDrawable>(back);
        Assert.That(copied.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
        ObjectRegenerator.Regenerate(back, out Element roundTrip);
        Assert.That(BackObject<SceneDrawable>(roundTrip).OffsetPosition.Value, Is.EqualTo(copied.OffsetPosition.Value));
        Assert.That(drawable.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    // The editor stores a scene reference as an expression, which must not drop the speed from the cut (#3642).
    [TestCase(false, 200f, 2.0)]
    [TestCase(false, 50f, 0.5)]
    [TestCase(true, 200f, 2.0)]
    public void Split_SceneSoundReferencingAScene_AdvancesTheBackOffsetByTheSourceIntegral(
        bool batch, float speed, double expected)
    {
        var sound = new SceneSound { Speed = { Value = speed } };
        sound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(_harness.Scene, new Scene());
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound);

        if (batch)
            new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
                [new TimeRange(MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(0.5))], SilenceSplitMode.DeleteSilence, false);
        else
            _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.Multiple(() =>
        {
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(BackObject<SceneSound>(back).OffsetPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(expected)));
        });
    }

    // A source type needs nothing but the contract for its cut to follow its speed.
    [Test]
    public void Split_ASourceImplementingOnlyTheContract_AdvancesTheBackOffsetByTheSourceIntegral()
    {
        var sound = new ContractOnlySound { Speed = { Value = 200f } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<ContractOnlySound>(back).OffsetPosition.Value,
            Is.EqualTo(MediaTime.FromSeconds(2)));
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

    // The back half's offset can pass one cycle of the source. The loop reads it wrapped, so playback continues at the cut.
    [Test]
    public void Split_LoopingVideoWithAnOffset_ContinuesFromTheCut()
    {
        var video = new SourceVideo
        {
            Source = { Value = TrimTestMedia.CreateVideoSource(2) },
            IsLoop = { Value = true },
            Speed = { Value = 300f },
            OffsetPosition = { Value = MediaTime.FromSeconds(1.5) },
        };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), video);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        SourceVideo backVideo = BackObject<SourceVideo>(back);
        using var resource = (SourceVideo.Resource)backVideo.ToResource(
            new CompositionContext(back.Range.Start));
        Assert.Multiple(() =>
        {
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(resource.RequestedPosition + resource.OffsetPosition,
                Is.EqualTo(MediaTime.FromSeconds(0.5)));
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

    private static MediaTime PlaybackPosition(SourceVideo video, MediaTime time)
    {
        using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(time));
        return resource.RequestedPosition + resource.OffsetPosition;
    }

    // Playback multiplies the elapsed time by what the expression gives, whatever speed is stored (#3851).
    [Test]
    public void Split_VideoSpeedExpression_MatchesPlaybackAfterTheCut()
    {
        var video = new SourceVideo { Speed = { Value = 300f } };
        video.Speed.Expression = Expression.Create<float>("200");
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        MediaTime before = PlaybackPosition(video, MediaTime.FromSeconds(3));

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(PlaybackPosition(BackObject<SourceVideo>(back), back.Range.Start + MediaTime.FromSeconds(2)).ToSeconds(),
            Is.EqualTo(before.ToSeconds()).Within(0.01));
    }

    // The back piece plays what the uncut clip played after the cut. The test decoder's 440 Hz sine looks the same a
    // whole second away, so this plays a ramp that differs at every position.
    [Test]
    public void Split_SoundSpeedExpression_PlaysOnAcrossTheCut()
    {
        const int rate = 44100;
        const int frames = rate * 4;
        AudioTestWaves.WithTemporaryWave(rate, frames, i => ((float)i / frames, 0f), source =>
        {
            var sound = new SourceSound { Source = { Value = source } };
            sound.Speed.Expression = Expression.Create<float>("200");
            Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), sound);
            var afterTheCut = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
            float[] uncut = Play(afterTheCut);

            _service.Split(_harness.Scene, [element], afterTheCut.Start);

            Assert.Multiple(() =>
            {
                // Half a second into the window, 1.5 s at 200% is three seconds up the four-second ramp.
                Assert.That(uncut[rate / 2], Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(Play(afterTheCut), Is.EqualTo(uncut).Within(1e-6f));
            });
        });

        float[] Play(TimeRange range)
        {
            using var compositor = new SceneCompositor(_harness.Scene);
            using var composer = new Composer { SampleRate = rate };
            using AudioBuffer? buffer = composer.Compose(range, compositor.EvaluateAudio(range));
            return buffer!.GetChannelData(0).ToArray();
        }
    }

    // Under a controller the video plays the time the controller maps the cut to, so that is when the expression is read.
    [Test]
    public void Split_ChangingVideoSpeedExpressionUnderAController_ContinuesFromTheCut()
    {
        var video = new SourceVideo();
        video.Speed.Expression = Expression.Create<float>("100 + 100 * Time");
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200f } });
        MediaTime atTheCut = PlaybackPosition(video, MediaTime.FromSeconds(2));

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value.ToSeconds(), Is.EqualTo(atTheCut.ToSeconds()).Within(0.01));
    }

    // A controller's offset starts the back piece partway into its own clock, where its expression gives another value
    // than the original did at the cut.
    [Test]
    public void Split_ChangingVideoSpeedExpressionUnderAnOffsetController_ContinuesFromTheCut()
    {
        var video = new SourceVideo();
        video.Speed.Expression = Expression.Create<float>("100 + 100 * Time");
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController { OffsetPosition = { Value = MediaTime.FromSeconds(1) } });
        MediaTime atTheCut = PlaybackPosition(video, MediaTime.FromSeconds(2));

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(PlaybackPosition(BackObject<SourceVideo>(back), MediaTime.FromSeconds(2)).ToSeconds(),
            Is.EqualTo(atTheCut.ToSeconds()).Within(0.01));
    }

    // That read happens while the back piece is new, and a speed that reads another clip finds nothing outside the scene.
    [Test]
    public void SilenceSplit_SpeedReadingAnotherClipUnderAnOffsetController_PlaysOnAcrossTheCut()
    {
        var provider = new SourceSound { Speed = { Value = 200f } };
        Element providerElement = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(1), provider);
        var video = new SourceVideo();
        video.Speed.Expression = new ReferenceExpression<float>(provider.Id.Value, nameof(Sound.Speed));
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController { OffsetPosition = { Value = MediaTime.FromSeconds(1) } });
        // Half a second after the last cut, at 3 s, which the controller's offset moves on by a second.
        MediaTime uncut = PlaybackPosition(video, MediaTime.FromSeconds(4.5));

        new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
            [new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1))], SilenceSplitMode.DeleteSilence, false);

        Element back = _harness.Scene.Children.Single(child => child != element && child != providerElement);
        Assert.That(PlaybackPosition(BackObject<SourceVideo>(back), MediaTime.FromSeconds(4.5)).ToSeconds(),
            Is.EqualTo(uncut.ToSeconds()).Within(0.01));
    }

    [Test]
    public void Split_LoopingVideoWithASpeedExpression_KeepsTheWrappedSourcePosition()
    {
        var video = new SourceVideo
        {
            Source = { Value = TrimTestMedia.CreateVideoSource(2) },
            IsLoop = { Value = true },
        };
        video.Speed.Expression = Expression.Create<float>("250");
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), video);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(PlaybackPosition(BackObject<SourceVideo>(back), back.Range.Start).ToSeconds(), Is.EqualTo(0.5).Within(0.01));
    }

    // A child anchored in time keeps its own clock through the cut, so nothing moves it, at any speed.
    [Test]
    public void Split_AnchoredChildWithASpeedExpression_KeepsItsOffset()
    {
        var child = new SourceSound { IsTimeAnchor = { Value = true }, TimeRange = { Value = TimeRange.FromSeconds(2, 4) } };
        child.Speed.Expression = Expression.Create<float>("200");
        var group = new SoundGroup();
        group.Children.Add(child);
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10), group);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(3));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SoundGroup>(back).Children.Single().OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Split_ControllerWithCloselySpacedSpeedKeys_KeepsTheirOrder()
    {
        var video = new SourceVideo();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100 },
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(0.25) },
            Value = { Value = 200 },
        });
        video.Speed.SetKeyFrameAnimation(animation);
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 50f } });

        Assert.DoesNotThrow(() => _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1)));

        Element back = _harness.Scene.Children.Single(child => child != element);
        IList<IKeyFrame> keys = BackObject<SourceVideo>(back).Speed.GetKeyFrameAnimation()!.KeyFrames;
        Assert.That(keys.Select(key => key.KeyTime.Value),
            Is.EqualTo(new[] { MediaTime.FromSeconds(-0.5), MediaTime.FromSeconds(-0.25) }));
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

// Stands in for an extension's source: Sound supplies the offset and speed, and the type adds only its length.
public sealed partial class ContractOnlySound : Sound, ITimeMappedSource
{
    public ContractOnlySound() => ScanProperties<ContractOnlySound>();

    MediaTime? ITimeMappedSource.GetSourceDuration() => MediaTime.FromSeconds(10);

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
