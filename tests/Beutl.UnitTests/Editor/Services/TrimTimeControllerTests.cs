using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Engine;
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
public class TrimTimeControllerTests
{
    private SceneHistoryHarness _harness = null!;
    private ElementSlipService _slip = null!;
    private ClampResizeToOriginalLengthScope _clamp = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _clamp = new ClampResizeToOriginalLengthScope(true);
        _harness = new SceneHistoryHarness("beutl_trim_controller", start: MediaTime.Zero, duration: Seconds(30));
        _slip = new ElementSlipService(_harness.History);
    }

    [TearDown]
    public void TearDown()
    {
        _clamp.Dispose();
        _harness.Dispose();
    }

    [Test]
    public void Slip_StaticControllerSpeedAndOffset_ClampsAndMatchesPlaybackWindow()
    {
        var video = CreateVideo(5);
        var controller = CreateController(video, speed: 200, offsetSeconds: 0.25);
        Element element = AddElement(5, 2, controller);
        var targets = SlippableMedia.Collect(element);

        // Controller time consumes source 0.5..4.5, leaving 0.5s source / 0.25s timeline.
        double[] localSamples = [0, 0.5, 1.5];
        double[] shiftedPlayback = localSamples.Select(t => ReadPosition(controller, 5 + t + 0.25)).ToArray();
        MediaTime clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(1));
        MediaTime? maximumDuration = SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration();

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(4.5).Within(0.000001));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(0.25).Within(0.000001));
            Assert.That(maximumDuration?.ToSeconds(), Is.EqualTo(2.25).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            for (int i = 0; i < localSamples.Length; i++)
            {
                Assert.That(ReadPosition(controller, 5 + localSamples[i]),
                    Is.EqualTo(shiftedPlayback[i]).Within(0.000001), $"Playback at local {localSamples[i]}s");
            }
        });
    }

    [Test]
    public void Slip_GlobalAnimatedControllerWithOffset_IntegratesTheShiftedGlobalClock()
    {
        var video = CreateVideo(20);
        var controller = CreateController(video, speed: 50, offsetSeconds: 0.5);
        controller.Speed.SetKeyFrameAnimation(CreateSpeedRamp(useGlobalClock: true));
        Element element = AddElement(5, 2, controller);
        double beforeStart = ReadPosition(controller, 5);
        double beforeShiftedStart = ReadPosition(controller, 6);

        // At the original start the controller integrates global 5..5.5. A +1s slip
        // adds integral 5.5..6.5 of (1 + t/10), i.e. 1.6s, including its offset.
        Assert.Multiple(() =>
        {
            Assert.That(beforeStart, Is.EqualTo(0.7625).Within(0.005));
            Assert.That(beforeShiftedStart - beforeStart, Is.EqualTo(1.6).Within(0.005));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(1.6).Within(0.005));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(beforeShiftedStart).Within(0.005));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
        });
    }

    [Test]
    public void Slip_NestedControllers_UsesAnchoredTargetAndAdjustTimeRange()
    {
        var video = CreateVideo(20, speed: 200);
        video.IsTimeAnchor.Value = true;
        video.TimeRange.Value = new TimeRange(Seconds(1), Seconds(10));
        var inner = CreateController(video, speed: 50, offsetSeconds: 0.25);
        inner.IsTimeAnchor.Value = true;
        inner.TimeRange.Value = new TimeRange(Seconds(2), Seconds(4));
        inner.AdjustTimeRange.Value = true;
        var outer = CreateController(inner, speed: 200, offsetSeconds: 0.5);
        Element element = AddElement(5, 2, outer);

        // At scene 5s: outer requests 3s; inner adjusts against target start 1s,
        // requesting 1 + (3 - 1 + 0.25) * 0.5 = 2.125s; video speed gives 2.25s.
        double beforeShiftedStart = ReadPosition(outer, 5.5);
        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(outer, 5), Is.EqualTo(2.25).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(3.25).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(outer, 5), Is.EqualTo(beforeShiftedStart).Within(0.000001));
            Assert.That(inner.TimeRange.Value, Is.EqualTo(new TimeRange(Seconds(2), Seconds(4))));
            Assert.That(video.TimeRange.Value, Is.EqualTo(new TimeRange(Seconds(1), Seconds(10))));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
        });
    }

    [Test]
    public void Slip_ReverseController_ClampsPositiveTimelineDeltaAtSourceInPoint()
    {
        var video = CreateVideo(8, offsetSeconds: 1);
        video.IsTimeAnchor.Value = true;
        video.TimeRange.Value = new TimeRange(MediaTime.Zero, Seconds(6));
        var controller = CreateController(video, speed: 200);
        controller.Reverse.Value = true;
        Element element = AddElement(5, 2, controller);
        double beforeShiftedStart = ReadPosition(controller, 5.5);

        // Reversed 200% playback needs -2s source per +1s timeline. The 1s source
        // offset therefore permits only +0.5s before the offset would become negative.
        MediaTime clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(7).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(6).Within(0.000001));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(0.5).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(0).Within(0.000001));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(beforeShiftedStart).Within(0.000001));
        });
    }

    [Test]
    public void Slip_LoopingController_ClampsAgainstFramesInsideTheWindow()
    {
        var video = CreateVideo(5, offsetSeconds: 2);
        video.IsTimeAnchor.Value = true;
        video.TimeRange.Value = new TimeRange(MediaTime.Zero, Seconds(2));
        var controller = CreateController(video, speed: 200);
        controller.Loop.Value = true;
        Element element = AddElement(5, 2, controller);

        // Window endpoints both wrap to source 2s, but interior frames approach 4s.
        // Only +1s source remains, permitting +0.5s rather than the requested +0.75s.
        MediaTime clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(0.75));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(2).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(2).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(3.5).Within(0.000001));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(0.5).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(0.75));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(3).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(4.5).Within(0.000001));
        });
    }

    [TestCase(true, false, -1d, 1d)]
    [TestCase(false, true, 5d, 5d)]
    public void Slip_HeldControllerFrame_DoesNotChangeOffsetOrCommit(
        bool holdFirst, bool holdLast, double controllerOffset, double expectedPosition)
    {
        var video = CreateVideo(6, offsetSeconds: 1);
        video.IsTimeAnchor.Value = true;
        video.TimeRange.Value = new TimeRange(MediaTime.Zero, Seconds(4));
        var controller = CreateController(video, offsetSeconds: controllerOffset);
        controller.HoldFirstFrame.Value = holdFirst;
        controller.HoldLastFrame.Value = holdLast;
        Element element = AddElement(5, 2, controller);
        int before = _harness.History.UndoCount;

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(expectedPosition).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.5), Is.EqualTo(expectedPosition).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(expectedPosition).Within(0.000001));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void MaximumDuration_NegativeControllerOffset_UsesActualAnimatedPlaybackWindow()
    {
        var video = CreateVideo(10, offsetSeconds: 1);
        video.IsTimeAnchor.Value = true;
        video.TimeRange.Value = new TimeRange(Seconds(5), Seconds(2));
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = false } };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100 },
            Easing = { Value = new LinearEasing() }
        });
        video.Speed.SetKeyFrameAnimation(animation);
        var controller = CreateController(video, offsetSeconds: -1.5);
        controller.TimeRange.Value = new TimeRange(Seconds(5), Seconds(2));
        Element element = AddElement(5, 2, controller);

        // The controller requests video-local -1.5..0.5s. Actual animated playback
        // holds negative time at zero, so adding the 1s source offset gives 1..1.5s.
        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 6.5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(1.5).Within(0.000001));
        });

        MediaTime? maximumDuration = SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration();

        Assert.Multiple(() =>
        {
            // 1.5s held at the source in-point plus 9s of remaining source gives 10.5s.
            Assert.That(maximumDuration?.ToSeconds(), Is.EqualTo(10.5).Within(0.000001));
            Assert.That(ReadPosition(controller, 15.5), Is.EqualTo(10).Within(0.000001));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
            Assert.That(video.TimeRange.Value, Is.EqualTo(new TimeRange(Seconds(5), Seconds(2))));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
        });
    }

    [Test]
    public void Slip_QuantizedController_UsesPlaybackFrameStep()
    {
        var video = CreateVideo(10);
        var controller = CreateController(video, offsetSeconds: 0.25);
        controller.FrameRate.Value = 2;
        Element element = AddElement(5, 2, controller);
        double beforeShiftedStart = ReadPosition(controller, 5.3);

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(0).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(0.5).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(0.3));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(beforeShiftedStart).Within(0.000001));
        });
    }

    [Test]
    public void Slip_FlowControllerWithNullTarget_UsesConsumedVideoAndPlaybackBounds()
    {
        var video = CreateVideo(5);
        Element element = AddElement(5, 2, video);
        var controller = new DrawableTimeController
        {
            Speed = { Value = 200 },
            OffsetPosition = { Value = Seconds(0.25) }
        };
        element.Objects.Add(controller);
        using var compositor = CreateCompositor(_harness.Scene);

        double ReadFlowPosition(double localSeconds)
        {
            CompositionFrame frame = compositor.EvaluateGraphics(element.Start.Value + Seconds(localSeconds));
            Assert.That(frame.Objects, Has.Length.EqualTo(1));
            Assert.That(frame.Objects[0], Is.TypeOf<DrawableTimeController.Resource>());
            return ReadPosition(frame.Objects[0]);
        }

        var targets = SlippableMedia.Collect(element);
        MediaTime clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(1));
        MediaTime? maximumDuration = SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration();
        double beforeShiftedStart = ReadFlowPosition(0.25);

        Assert.Multiple(() =>
        {
            Assert.That(controller.Target.Value, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(1), "The consumed video must not also be collected without its controller.");
            Assert.That(ReadFlowPosition(0), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(1).Within(0.000001));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(0.25).Within(0.000001));
            Assert.That(maximumDuration?.ToSeconds(), Is.EqualTo(2.25).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(ReadFlowPosition(0), Is.EqualTo(beforeShiftedStart).Within(0.000001));
            Assert.That(ReadFlowPosition(1.5), Is.EqualTo(4).Within(0.000001));
        });
    }

    [Test]
    public void Slip_ControllerAfterFlowGroup_AdvancesBothConsumedVideosAtControllerSpeed()
    {
        var first = CreateVideo(10);
        var second = CreateVideo(10);
        Element element = AddElement(5, 2, first);
        var group = new DrawableGroup();
        var controller = new DrawableTimeController { Speed = { Value = 200 } };
        element.Objects.Add(second);
        element.Objects.Add(group);
        element.Objects.Add(controller);
        int before = _harness.History.UndoCount;

        // The empty group consumes both preceding videos from flow. The following
        // controller retimes that entire group, so neither video keeps a 100% clock.
        var targets = SlippableMedia.Collect(element);
        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(group.Children, Is.Empty);
            Assert.That(controller.Target.Value, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets.Select(target => target.Offset),
                Is.EquivalentTo(new[] { first.OffsetPosition, second.OffsetPosition }));
            Assert.That(first.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
            Assert.That(second.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_DisabledFlowController_ClampsToTheUnmodifiedVideoClock()
    {
        var video = CreateVideo(4);
        Element element = AddElement(5, 2, video);
        var controller = new DrawableTimeController
        {
            IsEnabled = { Value = false },
            Speed = { Value = 50 }
        };
        element.Objects.Add(controller);
        int before = _harness.History.UndoCount;
        var targets = SlippableMedia.Collect(element);
        MediaTime clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(5));

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(5));

        using var compositor = CreateCompositor(_harness.Scene);
        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(6.5));
        Assert.That(frame.Objects, Has.Length.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(controller.Target.Value, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(2).Within(0.000001));
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(2).Within(0.000001));
            Assert.That(frame.Objects[0], Is.TypeOf<SourceVideo.Resource>(),
                "Playback skips the disabled controller and renders the video directly.");
            Assert.That(ReadPosition(frame.Objects[0]), Is.EqualTo(3.5).Within(0.000001));
            // The exact out-point is outside scene selection, so evaluate the same video resource path directly.
            Assert.That(ReadPosition(video, 7), Is.EqualTo(4).Within(0.000001),
                "Applying the disabled 50% clock would allow offset 2.5 and request source time 4.5.");
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_LoopingSourceVideo_UsesWrappedPlaybackWindowBeforeSourceOffset()
    {
        var video = CreateVideo(5);
        video.IsLoop.Value = true;
        var controller = CreateController(video, offsetSeconds: 6);
        Element element = AddElement(5, 1, controller);

        // SourceVideo wraps controller time 6..7 to 1..2 before adding OffsetPosition.
        // There are 3s of source headroom; the unwrapped 6..7 window would reject the edit.
        MediaTime clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(3.5));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 6), Is.EqualTo(2).Within(0.000001));
            Assert.That(clamped.ToSeconds(), Is.EqualTo(3).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(3.5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(3).Within(0.000001));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(4).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(4.75).Within(0.000001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_LinkedLoopingVideo_RechecksBoundsAfterAnotherStreamReducesDelta(bool reverseOrder)
    {
        var video = CreateVideo(5, offsetSeconds: 1);
        video.IsLoop.Value = true;
        Element videoElement = AddElement(5, 1, video);
        Element audioElement = _harness.AddElement(Seconds(5), Seconds(1), zIndex: 1);
        var sound = new SceneSound
        {
            ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(5) } } },
            Speed = { Value = 100 }
        };
        audioElement.Objects.Add(sound);
        Element[] elements = reverseOrder ? [audioElement, videoElement] : [videoElement, audioElement];
        int before = _harness.History.UndoCount;

        // The source wraps playback before adding the offset. Audio allows +4s,
        // but video permits only +3s, yielding offsets 4 and 3 in either order.
        var targets = elements.SelectMany(element => SlippableMedia.Collect(element)).ToArray();
        MediaTime clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(6));

        bool applied = _slip.Slip(_harness.Scene, elements, Seconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(clamped.ToSeconds(), Is.EqualTo(3).Within(0.000001));
            Assert.That(video.OffsetPosition.Value.ToSeconds(), Is.EqualTo(4).Within(0.000001));
            Assert.That(sound.OffsetPosition.Value.ToSeconds(), Is.EqualTo(3).Within(0.000001));
            foreach (double localSeconds in new[] { 0d, 0.5d, 1d })
            {
                double position = ReadPosition(video, 5 + localSeconds);
                Assert.That(position, Is.EqualTo(4 + localSeconds).Within(0.000001));
                Assert.That(position, Is.InRange(0d, 5d), $"Looping video source time at local {localSeconds}s");
            }
            Assert.That(sound.OffsetPosition.Value + audioElement.Length.Value, Is.LessThanOrEqualTo(Seconds(5)));
            foreach (Element element in elements)
            {
                Assert.That(element.Start.Value, Is.EqualTo(Seconds(5)));
                Assert.That(element.Length.Value, Is.EqualTo(Seconds(1)));
            }
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_ZeroControllerSpeedOrEmptyAnimation_MatchesFrozenPlayback(bool emptyAnimation)
    {
        var video = CreateVideo(6, offsetSeconds: 1);
        var controller = CreateController(video, speed: emptyAnimation ? 200 : 0, offsetSeconds: 0.5);
        if (emptyAnimation)
            controller.Speed.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        Element element = AddElement(5, 2, controller);
        int before = _harness.History.UndoCount;

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 6), Is.EqualTo(1).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_LoopPeriodDependsOnLength_ValidatesDurationBelowSafeMaximum(bool ripple)
    {
        var video = CreateVideo(1);
        Element element = AddElement(5, 1, video);
        element.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = Seconds(4) },
            Loop = { Value = true }
        });
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        var service = new ElementResizeService(_harness.History);
        Assert.That(constraints.GetMaximumDuration()?.ToSeconds(), Is.EqualTo(2).Within(0.000001));
        // At length 1.5 the new loop period requests source 0.5..1.25, even
        // though both the original length 1 and the maximum length 2 are safe.
        MediaTime preview = constraints.ClampLength(Seconds(1.5));
        service.Resize(_harness.Scene, [new ElementResizeRequest(element, element.Start.Value, Seconds(1.5), 0)], ripple);
        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(Seconds(1)));
            Assert.That(element.Length.Value, Is.EqualTo(preview));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });

        service.Resize(_harness.Scene, [new ElementResizeRequest(element, element.Start.Value, Seconds(2), 0)], ripple);
        using var compositor = CreateCompositor(_harness.Scene);
        CompositionFrame frame = compositor.EvaluateGraphics(element.Range.End - MediaTime.FromTimeSpan(TimeSpan.FromTicks(1)));
        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(ReadPosition(frame.Objects.Single()), Is.LessThanOrEqualTo(1));
        });
    }

    [TestCase(false, 9.5, 3, 1, 0, 4, 3)]
    [TestCase(true, 9.5, 3, 1, 0, 4, 3)]
    [TestCase(false, -9.5, -4, 4, 15, 0, 11)]
    [TestCase(true, -9.5, -4, 4, 15, 0, 11)]
    public void Slip_LinkedLoopingSource_DoesNotWrapTheOffsetWrite(bool reverseOrder, double requested,
        double expectedDelta, double videoOffset, double audioOffset, double expectedVideo, double expectedAudio)
    {
        var video = CreateVideo(5, offsetSeconds: videoOffset);
        video.IsLoop.Value = true;
        Element videoElement = AddElement(0, 1, video);
        Element audioElement = _harness.AddElement(MediaTime.Zero, Seconds(1), 1);
        var audio = new SceneSound
        {
            ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(30) } } },
            OffsetPosition = { Value = Seconds(audioOffset) }
        };
        audioElement.Objects.Add(audio);
        Element[] elements = reverseOrder ? [audioElement, videoElement] : [videoElement, audioElement];

        Assert.That(_slip.Slip(_harness.Scene, elements, Seconds(requested)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(expectedVideo)));
            Assert.That(audio.OffsetPosition.Value, Is.EqualTo(Seconds(expectedAudio)));
            Assert.That(audio.OffsetPosition.Value - Seconds(audioOffset), Is.EqualTo(Seconds(expectedDelta)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trim_SharedFronts_GrowThePresentedOwnersClockTogether(bool slide)
    {
        var video = CreateVideo(5, offsetSeconds: 1);
        Element frontA = AddElement(0, 2, video);
        Element frontB = _harness.AddElement(MediaTime.Zero, Seconds(2), 1);
        var controller = CreateController(video);
        controller.Reverse.Value = true;
        frontB.Objects.Add(controller);
        Element? middleA = slide ? _harness.AddElement(Seconds(2), Seconds(1), 0) : null;
        Element? middleB = slide ? _harness.AddElement(Seconds(2), Seconds(1), 1) : null;
        Element backA = _harness.AddElement(Seconds(slide ? 3 : 2), Seconds(4), 0);
        Element backB = _harness.AddElement(Seconds(slide ? 3 : 2), Seconds(4), 1);
        var resize = new ElementResizeService(_harness.History);

        bool applied = slide
            ? resize.Slide(_harness.Scene,
                [new(frontA, [middleA!], backA), new(frontB, [middleB!], backB)], Seconds(1))
            : resize.Roll(_harness.Scene, [new(frontA, backA), new(frontB, backB)], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length.Value, Is.EqualTo(Seconds(3)));
            Assert.That(frontB.Length.Value, Is.EqualTo(Seconds(3)));
            Assert.That(ReadPosition(controller, 0), Is.EqualTo(4));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
        });
    }

    [TestCase(100f, 5d)]
    [TestCase(200f, 2.5d)]
    [TestCase(0f, 5d)]
    public void OriginalDuration_LoopingVideo_OffersOneCycleOrFrozenSourceLength(float speed, double expected)
    {
        var video = CreateVideo(5, speed);
        video.IsLoop.Value = true;
        Element element = AddElement(0, 1, video);

        Assert.Multiple(() =>
        {
            Assert.That(SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration(), Is.Null);
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.True);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(expected)));
        });
    }

    [Test]
    public void Resize_RippleBarrier_RevalidatesTheFinalLoopPeriod()
    {
        var video = CreateVideo(1);
        Element element = AddElement(5, 1, video);
        element.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = Seconds(4) },
            Loop = { Value = true }
        });
        Element locked = _harness.AddElement(Seconds(6.5), Seconds(1));
        locked.IsLocked.Value = true;
        var service = new ElementResizeService(_harness.History);
        service.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(2), 0)], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(1)));
            Assert.That(locked.Start.Value, Is.EqualTo(Seconds(6.5)));
            Assert.That(element.Range.End, Is.LessThanOrEqualTo(locked.Start.Value));
        });
    }

    [Test]
    public void Resize_AnimatedLoopFalse_DoesNotUseTheStoredTrueValue()
    {
        var video = CreateVideo(5);
        video.IsLoop.Value = true;
        var animation = new KeyFrameAnimation<bool>();
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = false } });
        video.IsLoop.SetKeyFrameAnimation(animation);
        Element element = AddElement(0, 2, video);

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(10), 0)]);

        Assert.That(element.Length.Value, Is.EqualTo(Seconds(5)));
    }

    [Test]
    public void Slip_OvershootingSpeed_RejectsUnprovenMonotonicBounds()
    {
        var video = CreateVideo(1);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 20 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = Seconds(1) }, Value = { Value = 120 }, Easing = { Value = new BackEaseIn() } });
        video.Speed.SetKeyFrameAnimation(animation);
        Element element = AddElement(0, 1, video);
        int before = _harness.History.UndoCount;

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(_harness.History.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void MaximumDuration_LaterLoopPhase_IsNotCappedByTheFirstFailure()
    {
        var video = CreateVideo(1);
        Element element = AddElement(0, 1, video);
        element.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = Seconds(10) },
            Loop = { Value = true }
        });
        MediaTime? maximum = SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration();
        Assert.That(maximum?.ToSeconds(), Is.GreaterThanOrEqualTo(1.6));
        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(1.6)), Is.EqualTo(Seconds(1.6)));
    }

    [TestCase("resize")]
    [TestCase("roll")]
    [TestCase("slide")]
    public void Trim_NearestLaterLoopLength_IsPreserved(string mode)
    {
        var video = CreateVideo(1);
        Element front = AddElement(0, 1, video);
        front.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = Seconds(4) },
            Loop = { Value = true }
        });
        var service = new ElementResizeService(_harness.History);
        if (mode == "resize")
            service.Resize(_harness.Scene, [new(front, front.Start.Value, Seconds(2.8), 0)]);
        else if (mode == "roll")
        {
            Element back = _harness.AddElement(Seconds(1), Seconds(4));
            Assert.That(service.Roll(_harness.Scene, [new(front, back)], Seconds(1.8)), Is.True);
        }
        else
        {
            Element middle = _harness.AddElement(Seconds(1), Seconds(1));
            Element back = _harness.AddElement(Seconds(2), Seconds(4));
            Assert.That(service.Slide(_harness.Scene, [new(front, [middle], back)], Seconds(1.8)), Is.True);
        }
        Assert.That(front.Length.Value, Is.EqualTo(Seconds(2)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_LoopKeyBeyondWindow_DoesNotLimitCurrentLoop(bool globalClock)
    {
        var video = CreateVideo(1);
        var animation = new KeyFrameAnimation<bool> { UseGlobalClock = { Value = globalClock } };
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = true } });
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = Seconds(100) }, Value = { Value = false } });
        video.IsLoop.SetKeyFrameAnimation(animation);
        Element element = AddElement(5, 1, video);

        Assert.That(ReadPosition(video, 14.5), Is.EqualTo(0.5));
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(10), 0)]);

        Assert.That(element.Length.Value, Is.EqualTo(Seconds(10)));
    }

    [Test]
    public void Slip_ControllerExpression_RejectsLinkedEditBeforeAnyMutation()
    {
        var video = CreateVideo(20);
        Element element = AddElement(0, 2, video);
        var controller = new DrawableTimeController();
        controller.Speed.Expression = Expression.Create<float>("200");
        element.Objects.Add(controller);
        var linkedVideo = CreateVideo(20);
        Element linked = AddElement(5, 2, linkedVideo);
        using (var compositor = CreateCompositor(_harness.Scene))
            Assert.That(ReadPosition(compositor.EvaluateGraphics(Seconds(1)).Objects.Single()), Is.EqualTo(2));
        int history = _harness.History.UndoCount;

        Assert.That(_slip.Slip(_harness.Scene, [linked, element], Seconds(1)), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(linkedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [Test]
    public void Slip_ControllerOffsetExpression_RejectsSharedReference()
    {
        var video = CreateVideo(20);
        Element element = AddElement(0, 2, video);
        var controller = CreateController(video);
        controller.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(3)");
        Element controlled = AddElement(0, 2, controller);
        Assert.That(ReadPosition(controller, 1), Is.EqualTo(4));

        Assert.That(_slip.Slip(_harness.Scene, [element, controlled], Seconds(1)), Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    // Without the empty animation this is the case of ElementSlipServiceTests.Slip_SpeedExpression_RejectsBeforeMutation.
    [Test]
    public void Slip_VideoSpeedExpression_RejectsUnevaluatedMapping()
    {
        var video = CreateVideo(20);
        video.Speed.Expression = Expression.Create<float>("200");
        video.Speed.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        Element element = AddElement(0, 2, video);
        Assert.That(ReadPosition(video, 1), Is.EqualTo(2));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trim_AnimatedVideoSource_RejectsBoundsFromStoredSource(bool baseSource)
    {
        var video = CreateVideo(10);
        if (!baseSource) video.Source.Value = null;
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = CreateVideo(1).Source.Value } });
        video.Source.SetKeyFrameAnimation(animation);
        Element element = AddElement(0, 0.5, video);
        using (var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default))
            Assert.That(resource.Source!.Duration, Is.EqualTo(Seconds(1)));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(2)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(2), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(0.5)));
        });
    }

    [TestCase("roll", false)]
    [TestCase("roll", true)]
    [TestCase("slide", false)]
    [TestCase("slide", true)]
    public void Trim_FrontControllerExpression_RejectsGeometryChangesRegardlessOfClamp(string mode, bool clamp)
    {
        var frontVideo = CreateVideo(20);
        Element front = AddElement(0, 2, frontVideo);
        var controller = new DrawableTimeController();
        controller.Reverse.Expression = Expression.Create<bool>("true");
        front.Objects.Add(controller);
        Element? middle = mode == "slide" ? _harness.AddElement(Seconds(2), Seconds(1)) : null;
        var backVideo = CreateVideo(20);
        Element back = AddElement(mode == "roll" ? 2 : 3, 4, backVideo);
        TimeRange before = back.Range;
        int history = _harness.History.UndoCount;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value = clamp;
        var service = new ElementResizeService(_harness.History);

        bool applied = mode == "roll"
            ? service.Roll(_harness.Scene, [new(front, back)], Seconds(1))
            : service.Slide(_harness.Scene, [new(front, [middle!], back)], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length.Value, Is.EqualTo(Seconds(2)));
            if (middle != null) Assert.That(middle.Start.Value, Is.EqualTo(Seconds(2)));
            Assert.That(back.Range, Is.EqualTo(before));
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [Test]
    public void Slip_UnconsumedControllerExpression_DoesNotBlockLaterVideo()
    {
        var controller = new DrawableTimeController();
        controller.Speed.Expression = Expression.Create<float>("200");
        Element element = AddElement(0, 2, controller);
        var video = CreateVideo(20);
        element.Objects.Add(video);

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.True);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
    }

    [Test]
    public void Resize_LoopFutureEasingCanSwitchEarly_StillChecksPlainSourceBounds()
    {
        var video = CreateVideo(1);
        var animation = new KeyFrameAnimation<bool>();
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = true } });
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = Seconds(10) }, Value = { Value = false }, Easing = { Value = new BackEaseOut() } });
        video.IsLoop.SetKeyFrameAnimation(animation);
        Element element = AddElement(0, 1, video);
        Assert.That(ReadPosition(video, 7), Is.EqualTo(7));

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(8), 0)]);

        Assert.That(element.Length.Value, Is.EqualTo(Seconds(1)));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Resize_SharedTargetOwner_ResizesBothClocksTogether(bool leftEdge, bool reverseOrder)
    {
        var video = CreateVideo(5, speed: 200);
        Element owner = AddElement(5, 2, video);
        owner.ZIndex.Value = 1;
        Element presented = _harness.AddElement(Seconds(5), Seconds(2), 0);
        var controller = CreateController(video);
        controller.Reverse.Value = true;
        presented.Objects.Add(new PortalObject { Count = { Value = 1 } });
        presented.Objects.Add(controller);
        MediaTime start = Seconds(leftEdge ? 4 : 5);
        ElementResizeRequest[] requests = [new(owner, start, Seconds(3), 1), new(presented, start, Seconds(3), 0)];
        if (reverseOrder) Array.Reverse(requests);
        using var compositor = CreateCompositor(_harness.Scene);
        Assert.That(ReadPosition(compositor.EvaluateGraphics(owner.Start.Value).Objects.OfType<DrawableTimeController.Resource>().Single()), Is.EqualTo(4));

        new ElementResizeService(_harness.History).Resize(_harness.Scene, requests);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Start.Value, Is.EqualTo(Seconds(leftEdge ? 4.5 : 5)));
            Assert.That(owner.Length.Value, Is.EqualTo(Seconds(2.5)));
            Assert.That(presented.Range, Is.EqualTo(owner.Range));
            Assert.That(ReadPosition(compositor.EvaluateGraphics(presented.Start.Value).Objects.OfType<DrawableTimeController.Resource>().Single()),
                Is.EqualTo(5).Within(0.000001));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
        _harness.History.Undo();
        Assert.That(owner.Range, Is.EqualTo(new TimeRange(Seconds(5), Seconds(2))));
        Assert.That(presented.Range, Is.EqualTo(owner.Range));
        _harness.History.Redo();
        Assert.That(presented.Range, Is.EqualTo(owner.Range));
        Assert.That(owner.Length.Value, Is.EqualTo(Seconds(2.5)));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Resize_SharedTargetOwner_UsesTheTighterPeerLimit(bool ripple, bool barrier)
    {
        var video = CreateVideo(5, speed: 200);
        Element owner = AddElement(5, 1, video);
        owner.ZIndex.Value = 1;
        if (barrier)
            _harness.AddElement(Seconds(6.25), Seconds(1), 1).IsLocked.Value = true;
        else
            owner.Objects.Add(new SceneSound { ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(1.5) } } } });
        Element presented = _harness.AddElement(Seconds(5), Seconds(1), 0);
        var controller = CreateController(video);
        controller.Reverse.Value = true;
        presented.Objects.Add(new PortalObject { Count = { Value = 1 } });
        presented.Objects.Add(controller);

        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(owner, owner.Start.Value, Seconds(3), 1), new(presented, presented.Start.Value, Seconds(3), 0)], ripple);

        Assert.That(owner.Length.Value, Is.EqualTo(Seconds(barrier ? 1.25 : 1.5)));
        Assert.That(presented.Range, Is.EqualTo(owner.Range));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_ControllerTargetExpression_RejectsTheEntireLinkedEdit(bool emptyStoredTarget)
    {
        var evaluated = CreateVideo(1);
        AddElement(5, 1, evaluated);
        var stored = CreateVideo(20);
        var controller = CreateController(stored);
        if (emptyStoredTarget) controller.Target.Value = null;
        controller.Target.Expression = new ReferenceExpression<Drawable?>(evaluated.Id.Value);
        Element element = AddElement(0, 0.5, controller);
        var linkedVideo = CreateVideo(20);
        Element linked = AddElement(3, 0.5, linkedVideo);
        // Reference expressions resolve below the scene's hierarchy root.
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(_harness.Scene);
        var context = CompositionContext.Default;
        using (var resource = (DrawableTimeController.Resource)controller.ToResource(context))
            Assert.That(resource.Target!.GetOriginal(), Is.SameAs(evaluated));
        int history = _harness.History.UndoCount;

        Assert.That(_slip.Slip(_harness.Scene, [linked, element], Seconds(0.25)), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(stored.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(evaluated.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(linkedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [Test]
    public void Slip_ControllerTargetExpression_DoesNotOverrideConsumedFlow()
    {
        var evaluated = CreateVideo(1);
        AddElement(5, 1, evaluated);
        var consumed = CreateVideo(20);
        Element element = AddElement(0, 2, consumed);
        var controller = new DrawableTimeController();
        controller.Target.Expression = new ReferenceExpression<Drawable?>(evaluated.Id.Value);
        element.Objects.Add(controller);
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = (DrawableTimeController.Resource)compositor.EvaluateGraphics(Seconds(1)).Objects.Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(consumed));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.True);
        Assert.That(consumed.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
        Assert.That(evaluated.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slide_MiddleReferencesFront_RejectsSourceOverrun(bool reverseOrder)
    {
        var video = CreateVideo(11, offsetSeconds: 1);
        Element frontA = AddElement(0, 10, video);
        frontA.ZIndex.Value = 1;
        Element middleA = _harness.AddElement(Seconds(10), Seconds(2), 1);
        Element backA = _harness.AddElement(Seconds(12), Seconds(4), 1);
        Element frontB = _harness.AddElement(MediaTime.Zero, Seconds(2), 0);
        Element middleB = _harness.AddElement(Seconds(2), Seconds(2), 0);
        Element backB = _harness.AddElement(Seconds(4), Seconds(4), 0);
        var controller = CreateController(video, offsetSeconds: 8);
        controller.Reverse.Value = true;
        middleB.Objects.Add(new PortalObject { Count = { Value = 1 } });
        middleB.Objects.Add(controller);
        ElementSlideLane[] lanes = [new(frontA, [middleA], backA), new(frontB, [middleB], backB)];
        if (reverseOrder) Array.Reverse(lanes);
        int history = _harness.History.UndoCount;
        MediaTime preview = ElementResizeService.CreateTrimConstraints(_harness.Scene,
            lanes.Select(lane => new ElementTrimPair(lane.Front, lane.Back)).ToArray(), [middleA, middleB]).Clamp(Seconds(-1));

        bool applied = new ElementResizeService(_harness.History).Slide(_harness.Scene, lanes, Seconds(-1));
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(middleB.Start.Value + Seconds(1.5)).Objects
            .OfType<DrawableTimeController.Resource>().Single();
        double position = ReadPosition(resource);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(preview, Is.EqualTo(MediaTime.Zero));
            Assert.That(position, Is.LessThanOrEqualTo(11), "The middle must not seek past the shared front source.");
            Assert.That(frontA.Length.Value, Is.EqualTo(Seconds(10)));
            Assert.That(middleB.Start.Value, Is.EqualTo(Seconds(2)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slide_FrontReferencesMiddle_AllowsValidTargetClockShift(bool reverseOrder)
    {
        Element frontA = _harness.AddElement(MediaTime.Zero, Seconds(2), 1);
        var video = CreateVideo(12);
        Element middleA = AddElement(2, 2, video);
        middleA.ZIndex.Value = 1;
        Element backA = _harness.AddElement(Seconds(4), Seconds(4), 1);
        Element frontB = _harness.AddElement(MediaTime.Zero, Seconds(5), 0);
        Element middleB = _harness.AddElement(Seconds(5), Seconds(1), 0);
        Element backB = _harness.AddElement(Seconds(6), Seconds(4), 0);
        var controller = CreateController(video, speed: 200, offsetSeconds: 3);
        controller.AdjustTimeRange.Value = true;
        frontB.Objects.Add(new PortalObject { Count = { Value = 1 } });
        frontB.Objects.Add(controller);
        ElementSlideLane[] lanes = [new(frontA, [middleA], backA), new(frontB, [middleB], backB)];
        if (reverseOrder) Array.Reverse(lanes);

        MediaTime preview = ElementResizeService.CreateTrimConstraints(_harness.Scene,
            lanes.Select(lane => new ElementTrimPair(lane.Front, lane.Back)).ToArray(), [middleA, middleB]).Clamp(Seconds(1));
        Assert.That(new ElementResizeService(_harness.History).Slide(_harness.Scene, lanes, Seconds(1)), Is.True);
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(4.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(frontB.Length.Value, Is.EqualTo(Seconds(6)));
            Assert.That(preview, Is.EqualTo(Seconds(1)));
            Assert.That(middleA.Start.Value, Is.EqualTo(Seconds(3)));
            Assert.That(middleA.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(middleB.Start.Value, Is.EqualTo(Seconds(6)));
            Assert.That(ReadPosition(resource), Is.EqualTo(9).Within(0.000001));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_PortalInput_UsesInjectedVideoInsteadOfStoredTarget(bool emptyStoredTarget)
    {
        var imported = CreateVideo(6);
        Element owner = AddElement(0, 1, imported);
        owner.ZIndex.Value = 1;
        var stored = CreateVideo(20);
        var controller = CreateController(stored, speed: 200);
        if (emptyStoredTarget) controller.Target.Value = null;
        Element element = AddElement(0, 1, controller);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(imported));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(3)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(imported.OffsetPosition.Value, Is.EqualTo(Seconds(4)));
            Assert.That(stored.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(1)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_PortalInput_DoesNotLimitTheDisplayInterval(bool emptyStoredTarget)
    {
        var imported = CreateVideo(6);
        Element owner = AddElement(0, 5, imported);
        owner.ZIndex.Value = 1;
        var stored = CreateVideo(20);
        var controller = CreateController(stored, speed: 200);
        if (emptyStoredTarget) controller.Target.Value = null;
        Element element = AddElement(0, 1, controller);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(5), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(5)));
            Assert.That(imported.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(stored.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trim_AmbiguousPortal_RejectsSlipButAllowsResizing(bool expressionCount)
    {
        var first = CreateVideo(10);
        Element ownerA = AddElement(0, 1, first);
        ownerA.ZIndex.Value = 1;
        if (!expressionCount)
        {
            Element ownerB = AddElement(1, 1, CreateVideo(10));
            ownerB.ZIndex.Value = 1;
        }
        var controller = CreateController(first);
        Element element = AddElement(0, 2, controller);
        var portal = new PortalObject { Count = { Value = 1 } };
        if (expressionCount) portal.Count.Expression = Expression.Create<int>("1");
        element.Objects.Insert(0, portal);
        var linkedVideo = CreateVideo(10);
        Element linked = AddElement(0, 2, linkedVideo);
        linked.ZIndex.Value = 2;

        Assert.That(_slip.Slip(_harness.Scene, [linked, element], Seconds(0.25)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, element.Start.Value, Seconds(3), 0), new(linked, linked.Start.Value, Seconds(3), 2)]);
        Assert.Multiple(() =>
        {
            Assert.That(first.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(linkedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
            Assert.That(linked.Length.Value, Is.EqualTo(Seconds(3)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_PortalClear_DoesNotReuseRemovedInputs(bool importedInput)
    {
        var video = CreateVideo(10);
        var controller = CreateController(video);
        Element element = AddElement(0, 2, controller);
        if (importedInput)
        {
            Element owner = AddElement(0, 2, video);
            owner.ZIndex.Value = 1;
            element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        }
        else element.Objects.Insert(0, video);
        element.Objects.Insert(1, new PortalObject { Clear = { Value = true }, Count = { Value = importedInput ? 1 : 0 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target, Is.Null);

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(0.25)), Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_LeftEdge_FindsTheNearestLaterLoopLength(bool ripple)
    {
        var video = CreateVideo(1);
        Element element = AddElement(4, 1, video);
        element.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = Seconds(4) },
            Loop = { Value = true }
        });
        MediaTime requestedStart = Seconds(2.2);
        MediaTime end = element.Range.End;
        MediaTime preview = SlippableMedia.CreateResizeConstraints(element).ClampStart(requestedStart);

        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, requestedStart, end - requestedStart, 0)], ripple);

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(Seconds(3)));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(3)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(element.Range.End, Is.EqualTo(end));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(0d, false)]
    [TestCase(2d, false)]
    [TestCase(5d, false)]
    [TestCase(2d, true)]
    public void Portal_Empty_MaximumDurationIsUnbounded(double startSeconds, bool includePeer)
    {
        Element element = _harness.AddElement(Seconds(startSeconds), Seconds(5));
        element.Objects.Add(new PortalObject());
        var peers = new HashSet<Element> { element };
        if (includePeer)
        {
            // A longer peer shares the right edge but starts earlier.
            peers.Add(_harness.AddElement(MediaTime.Zero, Seconds(startSeconds + 5), zIndex: 1));
        }

        var constraints = SlippableMedia.CreateResizeConstraints(element, peers);

        Assert.That(constraints.GetMaximumDuration(), Is.Null);
        Assert.That(constraints.ClampLength(Seconds(10)), Is.EqualTo(Seconds(10)));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Portal_MultipleInputs_OnlyLocalMediaDefinesOriginalDuration(bool localMedia, bool nestedMedia)
    {
        AddElement(0, 2, CreateVideo(2)).ZIndex.Value = 1;
        AddElement(3, 2, CreateVideo(2)).ZIndex.Value = 5;
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(1));
        element.Objects.Add(new PortalObject { Count = { Value = 10 } });
        var group = new DrawableGroup();
        element.Objects.Add(group);
        if (localMedia)
        {
            if (nestedMedia) group.Children.Add(CreateVideo(6));
            else element.Objects.Add(CreateVideo(6));
        }

        var constraints = SlippableMedia.CreateResizeConstraints(element);

        Assert.Multiple(() =>
        {
            Assert.That(constraints.ClampLength(Seconds(10)), Is.EqualTo(Seconds(localMedia ? 6 : 10)));
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.EqualTo(localMedia));
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(localMedia ? Seconds(6) : (MediaTime?)null));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Portal_AudioProviders_PreserveTheLocalDrawableClock(bool importedVideo)
    {
        Element first = _harness.AddElement(MediaTime.Zero, Seconds(10), zIndex: 1);
        Element second = _harness.AddElement(MediaTime.Zero, Seconds(10), zIndex: 5);
        foreach (Element provider in new[] { first, second })
            provider.Objects.Add(new SceneSound { ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(10) } } } });
        var imported = CreateVideo(20);
        if (importedVideo) first.Objects.Add(imported);
        var local = CreateVideo(6);
        Element element = AddElement(0, 1, local);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 10 } });
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(importedVideo ? imported : local));

        MediaTime expected = Seconds(importedVideo ? 6 : 3);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(expected));
            Assert.That(constraints.ClampLength(Seconds(10)), Is.EqualTo(expected));
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(expected));
        });

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(10), 0)]);
        resource = compositor.EvaluateGraphics(element.Range.End - Seconds(0.1)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(element.Length.Value, Is.EqualTo(expected));
        Assert.That(ReadPosition(resource), Is.LessThanOrEqualTo(importedVideo ? 20 : 6));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Portal_IntermittentDrawableInputs_RejectUnprovenLocalVideoResize(bool disabledController)
    {
        var first = CreateVideo(20);
        AddElement(0, 1, first).ZIndex.Value = 1;
        AddElement(2, 1, CreateVideo(20)).ZIndex.Value = 5;
        var local = CreateVideo(6);
        Element element = AddElement(0, 6, local);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 10 } });
        var controller = new DrawableTimeController { Speed = { Value = 200 } };
        element.Objects.Add(controller);
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(first));
        resource = compositor.EvaluateGraphics(Seconds(4)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(local));
        Assert.That(ReadPosition(resource), Is.GreaterThan(6), "Extending into the provider gap would overrun the local video.");

        element.Length.Value = Seconds(3);
        controller.IsEnabled.Value = !disabledController;
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
            Assert.That(constraints.ClampLength(Seconds(6)), Is.EqualTo(Seconds(3)));
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.False);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.Null);
        });

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(6), 0)]);
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
        Assert.That(local.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Portal_IntermittentDrawableInputs_DoNotBlockUnrelatedAudio(bool localAudio)
    {
        AddElement(0, 1, CreateVideo(20)).ZIndex.Value = 1;
        AddElement(2, 1, CreateVideo(20)).ZIndex.Value = 5;
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(3));
        element.Objects.Add(new PortalObject { Count = { Value = 10 } });
        if (localAudio)
            element.Objects.Add(new SceneSound { ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(6) } } } });
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);

        Assert.That(constraints.ClampLength(Seconds(6)), Is.EqualTo(Seconds(6)));
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(localAudio ? Seconds(6) : (MediaTime?)null));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Portal_DisabledControllerLimit_OnlyConstrainsLocalMedia(bool localMedia)
    {
        AddElement(0, 10, CreateVideo(10)).ZIndex.Value = 1;
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(1));
        element.Objects.Add(new PortalObject { Count = { Value = 10 } });
        if (localMedia) element.Objects.Add(CreateVideo(6));
        for (int i = 0; i < 8; i++)
            element.Objects.Add(new DrawableTimeController { IsEnabled = { Value = false }, Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(localMedia ? Seconds(1) : (MediaTime?)null));
            Assert.That(constraints.ClampLength(Seconds(10)), Is.EqualTo(Seconds(localMedia ? 1 : 10)));
            Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.False);
        });
    }

    [Test]
    public void Portal_DisabledProvider_DoesNotBecomeATrimTarget()
    {
        var video = CreateVideo(1);
        Element owner = AddElement(0, 1, video);
        owner.ZIndex.Value = 1;
        owner.IsEnabled.Value = false;
        var controller = CreateController(video);
        Element element = AddElement(0, 0.5, controller);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.25)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target, Is.Null);

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(0.25)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(2), 0)]);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
    }

    [TestCase(0, 1.5, 2.5)]
    [TestCase(1, 2.5, 1.5)]
    [TestCase(4, 2, 1.5)]
    [TestCase(5, 2, 2.5)]
    public void Portal_InactiveProvider_DoesNotBlockEmptyClipResize(double providerStart, double newStart, double newLength)
    {
        var video = CreateVideo(1);
        Element owner = AddElement(providerStart, 1, video);
        owner.ZIndex.Value = 1;
        var controller = CreateController(video);
        Element element = AddElement(2, 2, controller);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        Assert.That(compositor.EvaluateGraphics(Seconds(3)).Objects.OfType<DrawableTimeController.Resource>().Single().Target, Is.Null);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        MediaTime preview = newStart == 2 ? constraints.ClampLength(Seconds(newLength)) : constraints.ClampStart(Seconds(newStart));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(0.25)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, Seconds(newStart), Seconds(newLength), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(Seconds(newStart == 2 ? newLength : newStart)));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(newStart)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(newLength)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(compositor.EvaluateGraphics(element.Start.Value + new MediaTime(element.Length.Value.Flicks / 2)).Objects
                .OfType<DrawableTimeController.Resource>().Single().Target, Is.Null);
        });
    }

    [TestCase("slip", false)]
    [TestCase("slip", true)]
    [TestCase("roll", false)]
    [TestCase("roll", true)]
    [TestCase("slide", false)]
    [TestCase("slide", true)]
    public void Portal_LockedProvider_RejectsOffsetWritesButAllowsResize(string operation, bool layerLocked)
    {
        var video = CreateVideo(20, offsetSeconds: 2);
        Element owner = AddElement(0, 10, video);
        owner.ZIndex.Value = 1;
        var sound = new SceneSound
        {
            ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(20) } } },
            OffsetPosition = { Value = Seconds(2) }
        };
        owner.Objects.Add(sound);
        TimelineLayer layer = _harness.AddLayer(1);
        owner.IsLocked.Value = !layerLocked;
        layer.IsLocked.Value = layerLocked;
        Element front = _harness.AddElement(MediaTime.Zero, Seconds(operation == "slide" ? 2 : 4), 0);
        Element? middle = operation == "slide" ? _harness.AddElement(Seconds(2), Seconds(2), 0) : null;
        Element element = AddElement(4, 2, new DrawableTimeController());
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        var linkedVideo = CreateVideo(20);
        Element linked = AddElement(4, 2, linkedVideo);
        linked.ZIndex.Value = 2;
        using var compositor = CreateCompositor(_harness.Scene);
        Assert.That(compositor.EvaluateGraphics(Seconds(4.5)).Objects.OfType<DrawableTimeController.Resource>()
            .Single().Target!.GetOriginal(), Is.SameAs(video), "Locks restrict editing, not playback.");
        var resize = new ElementResizeService(_harness.History);
        int history = _harness.History.UndoCount;
        MediaTime preview = operation == "slip"
            ? SlippableMedia.ClampSharedDelta(SlippableMedia.Collect(element), Seconds(0.5))
            : ElementResizeService.CreateTrimConstraints(_harness.Scene, [new(front, element)],
                middle == null ? [] : [middle]).Clamp(Seconds(0.5));

        bool Apply() => operation switch
        {
            "slip" => _slip.Slip(_harness.Scene, [linked, element], Seconds(0.5)),
            "roll" => resize.Roll(_harness.Scene, [new(front, element)], Seconds(0.5)),
            _ => resize.Slide(_harness.Scene, [new(front, [middle!], element)], Seconds(0.5))
        };

        bool applied = Apply();
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(preview, Is.EqualTo(MediaTime.Zero));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
            Assert.That(linkedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(front.Length.Value, Is.EqualTo(Seconds(operation == "slide" ? 2 : 4)));
            if (middle != null) Assert.That(middle.Start.Value, Is.EqualTo(Seconds(2)));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(4)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });

        // Bounds remain usable when only the unlocked consumer's geometry changes.
        resize.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(3), 0)]);
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
        Assert.That(sound.OffsetPosition.Value, Is.EqualTo(Seconds(2)));

        owner.IsLocked.Value = false;
        layer.IsLocked.Value = false;
        Assert.That(Apply(), Is.True, "Unlocking the provider must restore offset editing.");
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(2.5)));
        Assert.That(sound.OffsetPosition.Value, Is.EqualTo(Seconds(operation == "slip" ? 2.5 : 2)));
    }

    [Test]
    public void Portal_DisabledCompetitor_DoesNotBlockAValidInput()
    {
        var video = CreateVideo(6);
        Element owner = AddElement(0, 1, video);
        owner.ZIndex.Value = 2;
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(1), 1);
        element.Objects.Add(new PortalObject { Count = { Value = 1 } });
        element.Objects.Add(new DrawableTimeController());
        Element disabled = _harness.AddElement(MediaTime.Zero, Seconds(1), 0);
        disabled.IsEnabled.Value = false;
        disabled.Objects.Add(new PortalObject { Count = { Value = 2 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.25)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(video));

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.True);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_LeftGlobalLoop_UsesEachCandidatesStart(bool ripple)
    {
        var video = CreateVideo(1, speed: 106);
        Element element = AddElement(4.2, 0.8, video);
        var speed = new KeyFrameAnimation<float> { UseGlobalClock = { Value = true } };
        speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 10 } });
        speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = Seconds(20) }, Value = { Value = 50 } });
        var controller = new DrawableTimeController
        {
            OffsetPosition = { Value = Seconds(10) },
            Loop = { Value = true }
        };
        controller.Speed.SetKeyFrameAnimation(speed);
        element.Objects.Add(controller);
        MediaTime end = element.Range.End;
        using var playback = new SpeedIntegrator(60);
        playback.EnsureCache(speed);
        long low = Seconds(2.49).Flicks;
        long high = Seconds(2.51).Flicks;
        while (high - low > 1)
        {
            var length = new MediaTime(low + (high - low) / 2);
            MediaTime start = end - length;
            MediaTime phase = playback.Integrate(start + Seconds(10), speed) - playback.Integrate(start, speed);
            if (phase >= length) low = length.Flicks;
            else high = length.Flicks;
        }
        var expected = new MediaTime(low);
        MediaTime requestedStart = Seconds(2.2);
        MediaTime preview = SlippableMedia.CreateResizeConstraints(element).ClampStart(requestedStart);

        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, requestedStart, end - requestedStart, 0)], ripple);

        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value.Flicks, Is.EqualTo(expected.Flicks).Within(212));
            Assert.That(element.Start.Value, Is.EqualTo(preview));
            Assert.That(element.Range.End, Is.EqualTo(end));
        });
    }

    [TestCase("mute")]
    [TestCase("solo")]
    [TestCase("missing-layer")]
    public void Portal_InvisibleVideoLayer_DoesNotSupplyTrimMedia(string visibility)
    {
        var video = CreateVideo(2);
        Element owner = AddElement(0, 1, video);
        owner.ZIndex.Value = 1;
        _harness.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsSolo = { Value = visibility != "mute" } });
        if (visibility != "missing-layer")
            _harness.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsVideoMuted = { Value = visibility == "mute" } });
        var controller = CreateController(video);
        Element element = AddElement(0, 0.5, controller);
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        Assert.That(compositor.EvaluateGraphics(Seconds(0.25)).Objects.OfType<DrawableTimeController.Resource>().Single().Target, Is.Null);

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(0.25)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(3), 0)]);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trim_PresenterTargetExpression_RejectsTheEvaluatedSourceMismatch(bool emptyStoredTarget)
    {
        var evaluated = CreateVideo(1);
        AddElement(5, 1, evaluated);
        var stored = CreateVideo(20);
        var presenter = new DrawablePresenter { Target = { Value = emptyStoredTarget ? null : stored } };
        presenter.Target.Expression = new ReferenceExpression<Drawable?>(evaluated.Id.Value);
        Element element = AddElement(0, 0.5, presenter);
        // Reference expressions resolve below the scene's hierarchy root.
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(_harness.Scene);
        var context = CompositionContext.Default;
        using (var resource = (DrawablePresenter.Resource)presenter.ToResource(context))
            Assert.That(resource.Target!.GetOriginal(), Is.SameAs(evaluated));
        var linkedVideo = CreateVideo(20);
        Element linked = AddElement(3, 0.5, linkedVideo);

        Assert.That(_slip.Slip(_harness.Scene, [linked, element], Seconds(1)), Is.False);
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(3), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(0.5)));
            Assert.That(stored.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(evaluated.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(linkedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Portal_LayerMute_OnlyCollectsTheUnmutedMediaKind(bool videoMuted)
    {
        var video = CreateVideo(6);
        Element owner = AddElement(0, 1, video);
        owner.ZIndex.Value = 1;
        var sound = new SceneSound { ReferencedScene = { Value = new Scene { Duration = { Value = Seconds(6) } } } };
        owner.Objects.Add(sound);
        _harness.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsVideoMuted = { Value = videoMuted }, IsAudioMuted = { Value = !videoMuted } });
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(1), 0);
        element.Objects.Add(new PortalObject { Count = { Value = 1 } });
        element.Objects.Add(new DrawableTimeController());

        Assert.That(_slip.Slip(_harness.Scene, [element], Seconds(1)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(videoMuted ? 0 : 1)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(Seconds(videoMuted ? 1 : 0)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Portal_InactiveSiblings_DoNotMakeCurrentFlowAmbiguous(bool hasActiveProvider)
    {
        var before = CreateVideo(1);
        var after = CreateVideo(1);
        AddElement(0, 1, before).ZIndex.Value = 1;
        AddElement(12, 1, after).ZIndex.Value = 1;
        var active = CreateVideo(6);
        if (hasActiveProvider) AddElement(2, 8, active).ZIndex.Value = 1;
        Element element = AddElement(2, 2, new DrawableTimeController());
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(3)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target?.GetOriginal(), Is.SameAs(hasActiveProvider ? active : null));

        bool slipped = _slip.Slip(_harness.Scene, [element], Seconds(0.5));
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(3), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(slipped, Is.EqualTo(hasActiveProvider));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
            Assert.That(active.OffsetPosition.Value, Is.EqualTo(Seconds(hasActiveProvider ? 0.5 : 0)));
            Assert.That(before.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(after.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Trim_SharedFrozenOffset_AllowsGeometryWithoutAnOffsetWrite(bool slide, bool sharedMiddle)
    {
        var video = CreateVideo(20, speed: 0, offsetSeconds: 1);
        Element front = _harness.AddElement(MediaTime.Zero, Seconds(2));
        Element? middle = slide ? _harness.AddElement(Seconds(2), Seconds(2)) : null;
        Element back = AddElement(slide ? 4 : 2, 4, video);
        (sharedMiddle ? middle! : front).Objects.Add(new DrawablePresenter { Target = { Value = video } });
        var resize = new ElementResizeService(_harness.History);
        MediaTime oldBackStart = back.Start.Value;
        _harness.History.Commit();
        MediaTime preview = ElementResizeService.CreateTrimConstraints(_harness.Scene, [new(front, back)],
            middle == null ? [] : [middle]).Clamp(Seconds(0.5));

        bool applied = slide
            ? resize.Slide(_harness.Scene, [new(front, [middle!], back)], Seconds(0.5))
            : resize.Roll(_harness.Scene, [new(front, back)], Seconds(0.5));
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(preview, Is.EqualTo(Seconds(0.5)));
            Assert.That(front.Length.Value, Is.EqualTo(Seconds(2.5)));
            Assert.That(back.Start.Value, Is.EqualTo(oldBackStart + Seconds(0.5)));
            Assert.That(back.Length.Value, Is.EqualTo(Seconds(3.5)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
            if (middle != null) Assert.That(middle.Start.Value, Is.EqualTo(Seconds(2.5)));
        });
        _harness.History.Undo();
        Assert.That(back.Start.Value, Is.EqualTo(oldBackStart));
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
        _harness.History.Redo();
        Assert.That(back.Start.Value, Is.EqualTo(oldBackStart + Seconds(0.5)));
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(Seconds(1)));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Resize_PortalNewOverlap_PreservesTheRequestedDisplayInterval(bool leftEdge, bool ripple)
    {
        var video = CreateVideo(leftEdge ? 2 : 6);
        AddElement(leftEdge ? 0 : 2, leftEdge ? 3 : 8, video).ZIndex.Value = 1;
        Element element = AddElement(leftEdge ? 4 : 0, 1, new DrawableTimeController { Speed = { Value = 200 } });
        element.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        using var compositor = CreateCompositor(_harness.Scene);
        Assert.That(compositor.EvaluateGraphics(element.Start.Value + Seconds(0.5)).Objects
            .OfType<DrawableTimeController.Resource>().Single().Target, Is.Null);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        MediaTime preview = leftEdge ? constraints.ClampStart(Seconds(1)) : constraints.ClampLength(Seconds(5));
        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, Seconds(leftEdge ? 1 : 0), Seconds(leftEdge ? 4 : 5), 0)], ripple);

        var resource = compositor.EvaluateGraphics(Seconds(2.9)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(Seconds(leftEdge ? 1 : 5)));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(leftEdge ? 1 : 0)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(leftEdge ? 4 : 5)));
            Assert.That(resource.Target!.GetOriginal(), Is.SameAs(video));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    private Element AddElement(double startSeconds, double lengthSeconds, Drawable drawable)
    {
        Element element = _harness.AddElement(Seconds(startSeconds), Seconds(lengthSeconds));
        element.Objects.Add(drawable);
        // Target is a reference, so these explicit-target fixtures must supply its clock.
        // Preserve the distinct ranges deliberately assigned by the anchored-target tests.
        while (drawable is DrawableTimeController controller && controller.Target.Value is { } target)
        {
            if (!target.IsTimeAnchor.Value && target.TimeRange.Value.Duration == MediaTime.Zero)
                target.TimeRange.Value = controller.TimeRange.Value;
            drawable = target;
        }
        return element;
    }

    private static DrawableTimeController CreateController(Drawable target, float speed = 100, double offsetSeconds = 0)
        => new()
        {
            Target = { Value = target },
            Speed = { Value = speed },
            OffsetPosition = { Value = Seconds(offsetSeconds) }
        };

    private static double ReadPosition(Drawable drawable, double absoluteSeconds)
    {
        using var resource = drawable.ToResource(new CompositionContext(Seconds(absoluteSeconds))
        {
            DisableResourceShare = true
        });
        return ReadPosition(resource);
    }

    private static double ReadPosition(EngineObject.Resource resource)
    {
        while (resource is DrawableTimeController.Resource controller)
        {
            Assert.That(controller.Target, Is.Not.Null, "Playback must resolve a target for every controller.");
            Assert.That(controller.Target!.RequireOriginal().TimeRange.Value.Duration, Is.GreaterThan(MediaTime.Zero),
                "The fixture must give every target a duration so playback evaluates the controller instead of bypassing it.");
            resource = controller.Target!;
        }

        Assert.That(resource, Is.TypeOf<SourceVideo.Resource>());
        var video = (SourceVideo.Resource)resource;
        // OnDraw adds the source offset after RequestedPosition has undergone speed/loop mapping.
        return (video.RequestedPosition + video.OffsetPosition).ToSeconds();
    }
}
