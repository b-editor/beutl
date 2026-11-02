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

    // Where the compositor plays the video, through any time controllers.
    private MediaTime ComposedPosition(MediaTime time)
    {
        using SceneCompositor compositor = TrimTestMedia.CreateCompositor(_harness.Scene);
        EngineObject.Resource resource = compositor.EvaluateGraphics(time).Objects.Single();
        while (resource is DrawableTimeController.Resource controller)
            resource = controller.Target!;
        var video = (SourceVideo.Resource)resource;
        return video.RequestedPosition + video.OffsetPosition;
    }

    private Element AddVideoUnderASpeedExpression(
        IExpression<float> speed, double offset = 0, SourceVideo? video = null, bool reverse = false)
    {
        video ??= new SourceVideo { Source = { Value = TrimTestMedia.CreateVideoSource(60) } };
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4), video);
        var controller = new DrawableTimeController
        {
            Speed = { Value = 300f },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
            Reverse = { Value = reverse },
        };
        controller.Speed.Expression = speed;
        element.Objects.Add(controller);
        return element;
    }

    // A controller multiplies the time it has played by what its expression gives, which continues from the cut (#3860).
    // A script that fails or gives no finite value plays the stored speed.
    [TestCase("200", 0, 2)]
    [TestCase("100 + 100 * Time", 0, 2)]
    [TestCase("100 + 100 * Time", 1, 4)]
    [TestCase("this does not compile", 0, 3)]
    [TestCase("float.NaN", 0, 3)]
    public void Split_ControllerSpeedExpression_ContinuesFromTheCut(string speed, double offset, double position)
    {
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>(speed), offset);
        MediaTime cut = MediaTime.FromSeconds(1);
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(position).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(position).Within(0.01), "After the cut.");
    }

    [Test]
    public void Split_ConstantControllerSpeedExpression_PlaysOnAsIfUncut()
    {
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("200"));
        MediaTime later = MediaTime.FromSeconds(3);
        MediaTime uncut = ComposedPosition(later);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Assert.That(ComposedPosition(later).ToSeconds(), Is.EqualTo(uncut.ToSeconds()).Within(0.01));
    }

    [Test]
    public void Split_VideoAndControllerSpeedExpressions_ContinueFromTheCut()
    {
        var video = new SourceVideo { Source = { Value = TrimTestMedia.CreateVideoSource(60) } };
        video.Speed.Expression = Expression.Create<float>("100 + 100 * Time");
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("200"), video: video);
        MediaTime cut = MediaTime.FromSeconds(1);
        // The controller plays the video's second 2, where its own expression gives 300%.
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(6).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(6).Within(0.01), "After the cut.");
    }

    // The video's keyframes keep their place in what the controller plays, which the expression decides.
    [Test]
    public void Split_AnimatedSpeedUnderAControllerSpeedExpression_MatchesPlaybackAfterTheCut()
    {
        var video = new SourceVideo { Source = { Value = TrimTestMedia.CreateVideoSource(60) } };
        video.Speed.SetKeyFrameAnimation(TrimTestMedia.CreateSpeedRamp(useGlobalClock: false));
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("200"), video: video);
        MediaTime later = MediaTime.FromSeconds(3);
        MediaTime uncut = ComposedPosition(later);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Assert.That(ComposedPosition(later).ToSeconds(), Is.EqualTo(uncut.ToSeconds()).Within(0.01));
    }

    [Test]
    public void Split_ReverseControllerSpeedExpression_ContinuesFromTheCut()
    {
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("200"), reverse: true);
        MediaTime cut = MediaTime.FromSeconds(1);
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(2).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(2).Within(0.01), "After the cut.");
    }

    // The outer controller plays second 1 at 200%, and so the inner one reads its expression at second 2, not at second 1.
    [Test]
    public void Split_TwoControllerSpeedExpressions_ContinueFromTheCut()
    {
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("100 + 50 * Time"));
        var outer = new DrawableTimeController();
        outer.Speed.Expression = Expression.Create<float>("100 + 100 * Time");
        element.Objects.Add(outer);
        MediaTime cut = MediaTime.FromSeconds(1);
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(4).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(4).Within(0.01), "After the cut.");
    }

    // With no time played, the back piece's controller has no use for its value, which therefore cannot undo the cut.
    [Test]
    public void Split_ControllerSpeedExpressionUndefinedWhereTheBackPieceStarts_ContinuesAtItsValueAtTheCut()
    {
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("Time == 0 ? float.NaN : 200"));

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    // An expression with no finite value falls back to the stored speed, so that has to have none either.
    [Test]
    public void Split_ControllerSpeedWithoutAFiniteValue_ShiftsByLocalTime()
    {
        var video = new SourceVideo { Source = { Value = TrimTestMedia.CreateVideoSource(60) } };
        Element element = AddVideoUnderASpeedExpression(Expression.Create<float>("float.NaN"), video: video);
        element.Objects.OfType<DrawableTimeController>().Single().Speed.Value = float.NaN;

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(BackObject<SourceVideo>(back).OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    // The offset starts the back piece partway into the controller's clock, where it reads the other clip.
    [Test]
    public void SilenceSplit_ControllerSpeedReadingAnotherClip_PlaysOnAcrossTheCut()
    {
        var provider = new SourceSound { Speed = { Value = 200f } };
        AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(1), provider);
        Element element = AddVideoUnderASpeedExpression(
            new ReferenceExpression<float>(provider.Id.Value, nameof(Sound.Speed)), offset: 1);
        MediaTime later = MediaTime.FromSeconds(3.5);
        MediaTime uncut = ComposedPosition(later);

        new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
            [new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1))], SilenceSplitMode.DeleteSilence, false);

        Assert.That(ComposedPosition(later).ToSeconds(), Is.EqualTo(uncut.ToSeconds()).Within(0.01));
    }

    private Element AddVideoUnder(params DrawableTimeController[] controllers)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4),
            new SourceVideo { Source = { Value = TrimTestMedia.CreateVideoSource(60) } });
        foreach (DrawableTimeController controller in controllers)
            element.Objects.Add(controller);
        return element;
    }

    private static void SetExpression(DrawableTimeController controller, string property, string expression)
    {
        switch (property)
        {
            case "OffsetPosition": controller.OffsetPosition.Expression = Expression.Create<MediaTime>(expression); break;
            case "AdjustTimeRange": controller.AdjustTimeRange.Expression = Expression.Create<bool>(expression); break;
            case "Reverse": controller.Reverse.Expression = Expression.Create<bool>(expression); break;
            case "Loop": controller.Loop.Expression = Expression.Create<bool>(expression); break;
            case "HoldFirstFrame": controller.HoldFirstFrame.Expression = Expression.Create<bool>(expression); break;
            case "HoldLastFrame": controller.HoldLastFrame.Expression = Expression.Create<bool>(expression); break;
            case "FrameRate": controller.FrameRate.Expression = Expression.Create<float>(expression); break;
        }
    }

    // A controller's other expressions read like its speed's, at the cut and where the back piece starts, and leave the
    // stored speed in force (#3890). A script that fails plays the stored offset. AdjustTimeRange changes no position
    // here, because the controller and the video share the clip's clock.
    [TestCase("OffsetPosition", "TimeSpan.FromSeconds(3)", 1, 8)]
    [TestCase("OffsetPosition", "TimeSpan.FromSeconds(Time)", 1, 4)]
    [TestCase("OffsetPosition", "this does not compile", 1, 8, 3)]
    [TestCase("AdjustTimeRange", "true", 1, 2)]
    [TestCase("Reverse", "true", 1, 2)]
    [TestCase("Reverse", "Time >= 0.5", 1.5, 1)]
    [TestCase("Loop", "true", 3, 2)]
    [TestCase("HoldFirstFrame", "true", 1, 0, -2)]
    [TestCase("HoldLastFrame", "true", 3, 4)]
    [TestCase("FrameRate", "0.75", 1, 1.333)]
    public void Split_ControllerExpression_ContinuesFromTheCut(string property, string expression, double cutAt, double position, double offset = 0)
    {
        var controller = new DrawableTimeController
        {
            Speed = { Value = 200f },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
        };
        SetExpression(controller, property, expression);
        Element element = AddVideoUnder(controller);
        MediaTime cut = MediaTime.FromSeconds(cutAt);
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(position).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(position).Within(0.01), "After the cut.");
    }

    [TestCase("OffsetPosition", "TimeSpan.FromSeconds(3)")]
    [TestCase("Reverse", "true")]
    public void Split_ConstantControllerExpression_PlaysOnAsIfUncut(string property, string expression)
    {
        var controller = new DrawableTimeController { Speed = { Value = 200f } };
        SetExpression(controller, property, expression);
        Element element = AddVideoUnder(controller);
        MediaTime later = MediaTime.FromSeconds(3);
        MediaTime uncut = ComposedPosition(later);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Assert.That(ComposedPosition(later).ToSeconds(), Is.EqualTo(uncut.ToSeconds()).Within(0.01));
    }

    // The back piece counts its frames from its own start, with a stored rate as with an expression, so this position is
    // a frame behind the uncut clip's 4 s (#3894).
    [TestCase(false)]
    [TestCase(true)]
    public void Split_ControllerFrameRate_CountsFramesFromTheBackPiecesStart(bool expression)
    {
        var controller = new DrawableTimeController { Speed = { Value = 200f } };
        if (expression) controller.FrameRate.Expression = Expression.Create<float>("0.75");
        else controller.FrameRate.Value = 0.75f;
        Element element = AddVideoUnder(controller);

        _service.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Assert.That(ComposedPosition(MediaTime.FromSeconds(2)).ToSeconds(), Is.EqualTo(2.667).Within(0.01));
    }

    // The outer controller plays second 1 at 200%, and so the inner one reads its expression at second 2, not at second 1.
    [Test]
    public void Split_ExpressionOnAnInnerController_ContinuesFromTheCut()
    {
        var inner = new DrawableTimeController();
        inner.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(Time)");
        Element element = AddVideoUnder(inner, new DrawableTimeController { Speed = { Value = 200f } });
        MediaTime cut = MediaTime.FromSeconds(1);
        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(4).Within(0.01), "Before the cut.");

        _service.Split(_harness.Scene, [element], cut);

        Assert.That(ComposedPosition(cut).ToSeconds(), Is.EqualTo(4).Within(0.01), "After the cut.");
    }

    // The back piece reads the other clip where it starts, which only a piece in the scene can do.
    [Test]
    public void SilenceSplit_ControllerOffsetReadingAnotherClip_PlaysOnAcrossTheCut()
    {
        var provider = new SourceSound { OffsetPosition = { Value = MediaTime.FromSeconds(1) } };
        AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(1), provider);
        var controller = new DrawableTimeController { Speed = { Value = 200f } };
        controller.OffsetPosition.Expression = new ReferenceExpression<MediaTime>(provider.Id.Value, nameof(SourceSound.OffsetPosition));
        Element element = AddVideoUnder(controller);
        MediaTime later = MediaTime.FromSeconds(3.5);
        MediaTime uncut = ComposedPosition(later);

        new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
            [new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1))], SilenceSplitMode.DeleteSilence, false);

        Assert.That(ComposedPosition(later).ToSeconds(), Is.EqualTo(uncut.ToSeconds()).Within(0.01));
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
