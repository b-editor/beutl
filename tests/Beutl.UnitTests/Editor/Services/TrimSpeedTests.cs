using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.TestInfrastructure.TrimTestMedia;

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
    private ClampResizeToOriginalLengthScope _clamp = null!;

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
        _clamp = new ClampResizeToOriginalLengthScope(true);
    }

    [TearDown]
    public void TearDown()
    {
        _clamp.Dispose();
        _harness.Dispose();
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
            Assert.That(fast.Offset.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(slow.Offset.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(normal.Offset.Value, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            foreach (Element element in elements)
            {
                Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            }
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
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
        media.Speed.SetKeyFrameAnimation(CreateSpeedRamp(useGlobalClock));

        // speed(t) / 100 = 1 + t/10: integral 0..1 = 1.05 (local), 5..6 = 1.55 (global).
        bool applied = _slip.Slip(_scene, [element], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.Value.TotalSeconds,
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
        media.Speed.SetKeyFrameAnimation(CreateSpeedRamp(useGlobalClock));

        // Use the original start at 5s for global 5..6, not the updated start at 6s.
        // The local curve always uses 0..1, whose integral is 1.05s.
        bool applied = _resize.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.Value.TotalSeconds,
                Is.EqualTo(1 + expectedSourceDelta).Within(0.005));
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
        media.Speed.SetKeyFrameAnimation(animation);

        Assert.That(_slip.Slip(_scene, [element], TimeSpan.FromSeconds(-0.5)), Is.True);
        // Integral of 1.5 + 0.5t over -0.5..0 is 0.6875 seconds of source.
        Assert.That(media.Offset.Value.TotalSeconds, Is.EqualTo(1.3125).Within(0.005));
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
        media.Speed.SetKeyFrameAnimation(animation);
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
            sourceEnd = media.Offset.Value + integrator.Integrate(back.Length, animation);
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
        var presenter = new DrawablePresenter { Target = { Value = video } };
        backB.Objects.Add(presenter);
        backB.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });
        ElementTrimPair[] pairs = [new(frontA, backA), new(frontB, backB)];
        int before = _history.UndoCount;

        TimeSpan preview = ElementResizeService.CreateTrimConstraints(_scene, pairs).Clamp(TimeSpan.FromSeconds(1));
        bool applied = _resize.Roll(_scene, pairs, TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(TimeSpan.Zero));
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(TimeSpan.Zero));
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
        media.Speed.SetKeyFrameAnimation(animation);
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
            Assert.That(media.Offset.Value, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sourceEnd, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(11)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OriginalDuration_LongGlobalAudioCurve_BoundsInterpolationWork(bool customEasing)
    {
        Element element = AddElement(600, 2);
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = TimeSpan.FromSeconds(900) } } };
        var easing = new CountingEasing();
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1200),
            Value = 300,
            Easing = customEasing ? easing : new LinearEasing()
        });
        sound.Speed.SetKeyFrameAnimation(animation);
        element.Objects.Add(sound);
        TimeSpan? maximum = null;

        Assert.DoesNotThrow(() => maximum = SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration());
        Assert.Multiple(() =>
        {
            Assert.That(easing.Calls, Is.LessThan(20000));
            Assert.That(maximum?.TotalSeconds, Is.EqualTo(customEasing ? 300 : Math.Sqrt(2520000) - 1200).Within(0.002));
        });
    }

    [Test]
    public void Resize_CustomNarrowSpeedPeak_UsesDeclaredBounds()
    {
        Element element = AddElement(0, 2);
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = TimeSpan.FromSeconds(300) } } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1000), Value = 200, Easing = new NarrowPeakEasing() });
        sound.Speed.SetKeyFrameAnimation(animation);
        element.Objects.Add(sound);
        _resize.Resize(_scene, [new(element, element.Start, TimeSpan.FromSeconds(250), 0)]);
        using var playback = new SpeedIntegrator(44100);
        playback.EnsureCache(animation);
        Assert.That(playback.Integrate(element.Length, animation), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(300)));
    }

    [Test]
    public void Roll_GlobalAudioAtSourceEnd_AllowsNominallyPreservedWindow()
    {
        Element front = AddElement(0, 2);
        Element back = AddElement(2, 2);
        var animation = CreateSpeedRamp(useGlobalClock: true);
        using var playback = new SpeedIntegrator(44100);
        playback.EnsureCache(animation);
        TimeSpan duration = playback.Integrate(back.Range.End, animation) - playback.Integrate(back.Start, animation);
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = duration } } };
        sound.Speed.SetKeyFrameAnimation(animation);
        back.Objects.Add(sound);
        Assert.That(_resize.Roll(_scene, [new(front, back)], TimeSpan.FromSeconds(0.5)), Is.True);
        Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
        Assert.That(back.Range.End, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [TestCase("slip")]
    [TestCase("roll")]
    [TestCase("slide")]
    public void Trim_CustomNarrowSpeedPeak_DoesNotWriteAnUncertainOffset(string mode)
    {
        Element front = AddElement(0, 1);
        Element middle = AddElement(1, 1);
        Element back = AddElement(2, 300);
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = TimeSpan.FromSeconds(100000) } } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1000), Value = 200, Easing = new NarrowPeakEasing() });
        sound.Speed.SetKeyFrameAnimation(animation);
        back.Objects.Add(sound);
        using var playback = new SpeedIntegrator(44100);
        playback.EnsureCache(animation);
        Assert.That(playback.Integrate(TimeSpan.FromSeconds(250), animation).TotalSeconds, Is.GreaterThan(340));
        int history = _history.UndoCount;

        bool applied = mode switch
        {
            "slip" => _slip.Slip(_scene, [back], TimeSpan.FromSeconds(250)),
            "roll" => _resize.Roll(_scene, [new(middle, back)], TimeSpan.FromSeconds(250)),
            _ => _resize.Slide(_scene, [new(front, [middle], back)], TimeSpan.FromSeconds(250))
        };

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(TimeSpan.Zero));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(300)));
            Assert.That(_history.UndoCount, Is.EqualTo(history));
        });
    }

    [Test]
    public void Resize_PureMoveWithGlobalSpeed_PreservesDuration()
    {
        Element element = AddElement(0, 2);
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = TimeSpan.FromSeconds(3) } } };
        sound.Speed.SetKeyFrameAnimation(CreateSpeedRamp(useGlobalClock: true));
        element.Objects.Add(sound);

        _resize.Resize(_scene, [new(element, TimeSpan.FromSeconds(8), element.Length, 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [TestCase(nameof(SourceSound), false)]
    [TestCase(nameof(SourceSound), true)]
    [TestCase(nameof(SceneSound), false)]
    [TestCase(nameof(SceneSound), true)]
    public void Trim_AudioSourceExpression_RejectsStoredDuration(string mediaType, bool hasStoredSource)
    {
        // Reference expressions resolve below the scene's hierarchy root.
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(_scene);
        Element element = AddElement(0, 0.5);
        Element providerElement = AddElement(5, 0.5);
        Sound sound;
        if (mediaType == nameof(SourceSound))
        {
            var shortSource = new SoundSource();
            shortSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 1)));
            var provider = new SourceSound { Source = { Value = shortSource } };
            providerElement.Objects.Add(provider);
            var source = new SourceSound();
            if (hasStoredSource)
            {
                var stored = new SoundSource();
                stored.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 10)));
                source.Source.Value = stored;
            }
            source.Source.Expression = new ReferenceExpression<SoundSource?>(provider.Id, nameof(SourceSound.Source));
            element.Objects.Add(source);
            using var resource = (SourceSound.Resource)source.ToResource(CompositionContext.Default);
            Assert.That(resource.Source!.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
            sound = source;
        }
        else
        {
            var provider = new SceneSound { ReferencedScene = { Value = new Scene { Duration = TimeSpan.FromSeconds(1) } } };
            providerElement.Objects.Add(provider);
            var source = new SceneSound();
            if (hasStoredSource) source.ReferencedScene.Value = new Scene { Duration = TimeSpan.FromSeconds(10) };
            source.ReferencedScene.Expression = new ReferenceExpression<Scene?>(provider.Id, nameof(SceneSound.ReferencedScene));
            element.Objects.Add(source);
            using var resource = (SceneSound.Resource)source.ToResource(CompositionContext.Default);
            Assert.That(resource.ReferencedScene!.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
            sound = source;
        }

        Assert.That(_slip.Slip(_scene, [element], TimeSpan.FromSeconds(2)), Is.False);
        _resize.Resize(_scene, [new(element, element.Start, TimeSpan.FromSeconds(2), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(TimeSpan.Zero));
        });
    }

    private sealed class NarrowPeakEasing : Easing
    {
        public override float Ease(float progress) => progress is >= 0.1995f and < 0.2005f ? 100 : 0;
        public override bool TryGetOutputRange(out float minimum, out float maximum) { minimum = 0; maximum = 100; return true; }
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
}
