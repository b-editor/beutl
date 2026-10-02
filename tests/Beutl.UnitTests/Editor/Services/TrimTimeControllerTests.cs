using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Configuration;
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
public class TrimTimeControllerTests
{
    private SceneHistoryHarness _harness = null!;
    private ElementSlipService _slip = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_trim_controller", start: TimeSpan.Zero, duration: Seconds(30));
        _slip = new ElementSlipService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

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
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(1));
        TimeSpan? maximumDuration = SlippableMedia.GetMaximumDuration(element);

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(4.5).Within(0.000001));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(0.25).Within(0.000001));
            Assert.That(maximumDuration?.TotalSeconds, Is.EqualTo(2.25).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(0.5).Within(0.000001));
            Assert.That(element.Start, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length, Is.EqualTo(Seconds(2)));
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
        controller.Speed.Animation = CreateGlobalSpeedRamp();
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
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(1.6).Within(0.005));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(beforeShiftedStart).Within(0.005));
            Assert.That(element.Start, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length, Is.EqualTo(Seconds(2)));
        });
    }

    [Test]
    public void Slip_NestedControllers_UsesAnchoredTargetAndAdjustTimeRange()
    {
        var video = CreateVideo(20, speed: 200);
        video.IsTimeAnchor = true;
        video.TimeRange = new TimeRange(Seconds(1), Seconds(10));
        var inner = CreateController(video, speed: 50, offsetSeconds: 0.25);
        inner.IsTimeAnchor = true;
        inner.TimeRange = new TimeRange(Seconds(2), Seconds(4));
        inner.AdjustTimeRange.CurrentValue = true;
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
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(outer, 5), Is.EqualTo(beforeShiftedStart).Within(0.000001));
            Assert.That(inner.TimeRange, Is.EqualTo(new TimeRange(Seconds(2), Seconds(4))));
            Assert.That(video.TimeRange, Is.EqualTo(new TimeRange(Seconds(1), Seconds(10))));
            Assert.That(element.Start, Is.EqualTo(Seconds(5)));
        });
    }

    [Test]
    public void Slip_ReverseController_ClampsPositiveTimelineDeltaAtSourceInPoint()
    {
        var video = CreateVideo(8, offsetSeconds: 1);
        video.IsTimeAnchor = true;
        video.TimeRange = new TimeRange(TimeSpan.Zero, Seconds(6));
        var controller = CreateController(video, speed: 200);
        controller.Reverse.CurrentValue = true;
        Element element = AddElement(5, 2, controller);
        double beforeShiftedStart = ReadPosition(controller, 5.5);

        // Reversed 200% playback needs -2s source per +1s timeline. The 1s source
        // offset therefore permits only +0.5s before the offset would become negative.
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(7).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(6).Within(0.000001));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(0.5).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(0).Within(0.000001));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(beforeShiftedStart).Within(0.000001));
        });
    }

    [Test]
    public void Slip_LoopingController_ClampsAgainstFramesInsideTheWindow()
    {
        var video = CreateVideo(5, offsetSeconds: 2);
        video.IsTimeAnchor = true;
        video.TimeRange = new TimeRange(TimeSpan.Zero, Seconds(2));
        var controller = CreateController(video, speed: 200);
        controller.Loop.CurrentValue = true;
        Element element = AddElement(5, 2, controller);

        // Window endpoints both wrap to source 2s, but interior frames approach 4s.
        // Only +1s source remains, permitting +0.5s rather than the requested +0.75s.
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(0.75));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(2).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(2).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(3.5).Within(0.000001));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(0.5).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(0.75));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(3).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(4.5).Within(0.000001));
        });
    }

    [TestCase(true, false, -1d, 1d)]
    [TestCase(false, true, 5d, 5d)]
    public void Slip_HeldControllerFrame_DoesNotChangeOffsetOrCommit(
        bool holdFirst, bool holdLast, double controllerOffset, double expectedPosition)
    {
        var video = CreateVideo(6, offsetSeconds: 1);
        video.IsTimeAnchor = true;
        video.TimeRange = new TimeRange(TimeSpan.Zero, Seconds(4));
        var controller = CreateController(video, offsetSeconds: controllerOffset);
        controller.HoldFirstFrame.CurrentValue = holdFirst;
        controller.HoldLastFrame.CurrentValue = holdLast;
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
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(expectedPosition).Within(0.000001));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void MaximumDuration_NegativeControllerOffset_UsesActualAnimatedPlaybackWindow()
    {
        var video = CreateVideo(10, offsetSeconds: 1);
        video.IsTimeAnchor = true;
        video.TimeRange = new TimeRange(Seconds(5), Seconds(2));
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = false };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 100,
            Easing = new LinearEasing()
        });
        video.Speed.Animation = animation;
        var controller = CreateController(video, offsetSeconds: -1.5);
        controller.TimeRange = new TimeRange(Seconds(5), Seconds(2));
        Element element = AddElement(5, 2, controller);

        // The controller requests video-local -1.5..0.5s. Actual animated playback
        // holds negative time at zero, so adding the 1s source offset gives 1..1.5s.
        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 6.5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 7), Is.EqualTo(1.5).Within(0.000001));
        });

        TimeSpan? maximumDuration = SlippableMedia.GetMaximumDuration(element);

        Assert.Multiple(() =>
        {
            // 1.5s held at the source in-point plus 9s of remaining source gives 10.5s.
            Assert.That(maximumDuration?.TotalSeconds, Is.EqualTo(10.5).Within(0.000001));
            Assert.That(ReadPosition(controller, 15.5), Is.EqualTo(10).Within(0.000001));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(video.TimeRange, Is.EqualTo(new TimeRange(Seconds(5), Seconds(2))));
            Assert.That(element.Length, Is.EqualTo(Seconds(2)));
        });
    }

    [Test]
    public void Slip_QuantizedController_UsesPlaybackFrameStep()
    {
        var video = CreateVideo(10);
        var controller = CreateController(video, offsetSeconds: 0.25);
        controller.FrameRate.CurrentValue = 2;
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
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(0.5).Within(0.000001));
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
            Speed = { CurrentValue = 200 },
            OffsetPosition = { CurrentValue = Seconds(0.25) }
        };
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(_harness.Scene)
        {
            DisableResourceShare = true,
            ForceOriginalSource = true
        };

        double ReadFlowPosition(double localSeconds)
        {
            CompositionFrame frame = compositor.EvaluateGraphics(element.Start + Seconds(localSeconds));
            Assert.That(frame.Objects, Has.Length.EqualTo(1));
            Assert.That(frame.Objects[0], Is.TypeOf<DrawableTimeController.Resource>());
            return ReadPosition(frame.Objects[0]);
        }

        var targets = SlippableMedia.Collect(element);
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(1));
        TimeSpan? maximumDuration = SlippableMedia.GetMaximumDuration(element);
        double beforeShiftedStart = ReadFlowPosition(0.25);

        Assert.Multiple(() =>
        {
            Assert.That(controller.Target.CurrentValue, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(1), "The consumed video must not also be collected without its controller.");
            Assert.That(ReadFlowPosition(0), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(beforeShiftedStart, Is.EqualTo(1).Within(0.000001));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(0.25).Within(0.000001));
            Assert.That(maximumDuration?.TotalSeconds, Is.EqualTo(2.25).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(0.5).Within(0.000001));
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
        var controller = new DrawableTimeController { Speed = { CurrentValue = 200 } };
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
            Assert.That(controller.Target.CurrentValue, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets.Select(target => target.Offset),
                Is.EquivalentTo(new[] { first.OffsetPosition, second.OffsetPosition }));
            Assert.That(first.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(2)));
            Assert.That(second.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(2)));
            Assert.That(element.Start, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length, Is.EqualTo(Seconds(2)));
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
            IsEnabled = false,
            Speed = { CurrentValue = 50 }
        };
        element.Objects.Add(controller);
        int before = _harness.History.UndoCount;
        var targets = SlippableMedia.Collect(element);
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(5));

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(5));

        using var compositor = new SceneCompositor(_harness.Scene)
        {
            DisableResourceShare = true,
            ForceOriginalSource = true
        };
        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(6.5));
        Assert.That(frame.Objects, Has.Length.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(controller.Target.CurrentValue, Is.Null);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(2).Within(0.000001));
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(2).Within(0.000001));
            Assert.That(frame.Objects[0], Is.TypeOf<SourceVideo.Resource>(),
                "Playback skips the disabled controller and renders the video directly.");
            Assert.That(ReadPosition(frame.Objects[0]), Is.EqualTo(3.5).Within(0.000001));
            // The exact out-point is outside scene selection, so evaluate the same video resource path directly.
            Assert.That(ReadPosition(video, 7), Is.EqualTo(4).Within(0.000001),
                "Applying the disabled 50% clock would allow offset 2.5 and request source time 4.5.");
            Assert.That(element.Start, Is.EqualTo(Seconds(5)));
            Assert.That(element.Length, Is.EqualTo(Seconds(2)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_LoopingSourceVideo_UsesWrappedPlaybackWindowBeforeSourceOffset()
    {
        var video = CreateVideo(5);
        video.IsLoop.CurrentValue = true;
        var controller = CreateController(video, offsetSeconds: 6);
        Element element = AddElement(5, 1, controller);

        // SourceVideo wraps controller time 6..7 to 1..2 before adding OffsetPosition.
        // There are 3s of source headroom; the unwrapped 6..7 window would reject the edit.
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(
            SlippableMedia.Collect(element), Seconds(3.5));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(1).Within(0.000001));
            Assert.That(ReadPosition(controller, 6), Is.EqualTo(2).Within(0.000001));
            Assert.That(clamped.TotalSeconds, Is.EqualTo(3).Within(0.000001));
        });

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(3.5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(3).Within(0.000001));
            Assert.That(ReadPosition(controller, 5), Is.EqualTo(4).Within(0.000001));
            Assert.That(ReadPosition(controller, 5.75), Is.EqualTo(4.75).Within(0.000001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_LinkedLoopingVideo_RechecksBoundsAfterAnotherStreamReducesDelta(bool reverseOrder)
    {
        var video = CreateVideo(5, offsetSeconds: 1);
        video.IsLoop.CurrentValue = true;
        Element videoElement = AddElement(5, 1, video);
        Element audioElement = _harness.AddElement(Seconds(5), Seconds(1), zIndex: 1);
        var sound = new SceneSound
        {
            ReferencedScene = { CurrentValue = new Scene { Duration = Seconds(5) } },
            Speed = { CurrentValue = 100 }
        };
        audioElement.Objects.Add(sound);
        Element[] elements = reverseOrder ? [audioElement, videoElement] : [videoElement, audioElement];
        int before = _harness.History.UndoCount;

        // +6s wraps the video to a valid offset 2. Audio first limits it to +4s,
        // which would put video at offset 5 and request 5..6s. Rechecking must reduce
        // the shared delta to +3s, yielding video offset 4 and audio offset 3 in either order.
        var targets = elements.SelectMany(element => SlippableMedia.Collect(element)).ToArray();
        TimeSpan clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(6));

        bool applied = _slip.Slip(_harness.Scene, elements, Seconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(clamped.TotalSeconds, Is.EqualTo(3).Within(0.000001));
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(4).Within(0.000001));
            Assert.That(sound.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(3).Within(0.000001));
            foreach (double localSeconds in new[] { 0d, 0.5d, 1d })
            {
                double position = ReadPosition(video, 5 + localSeconds);
                Assert.That(position, Is.EqualTo(4 + localSeconds).Within(0.000001));
                Assert.That(position, Is.InRange(0d, 5d), $"Looping video source time at local {localSeconds}s");
            }
            Assert.That(sound.OffsetPosition.CurrentValue + audioElement.Length, Is.LessThanOrEqualTo(Seconds(5)));
            foreach (Element element in elements)
            {
                Assert.That(element.Start, Is.EqualTo(Seconds(5)));
                Assert.That(element.Length, Is.EqualTo(Seconds(1)));
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
            controller.Speed.Animation = new KeyFrameAnimation<float>();
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
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
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
            Speed = { CurrentValue = 50 },
            OffsetPosition = { CurrentValue = Seconds(4) },
            Loop = { CurrentValue = true }
        });
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        var service = new ElementResizeService(_harness.History);
        bool originalClamp = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = true;
            Assert.That(constraints.GetMaximumDuration()?.TotalSeconds, Is.EqualTo(2).Within(0.000001));
            // At length 1.5 the new loop period requests source 0.5..1.25, even
            // though both the original length 1 and the maximum length 2 are safe.
            TimeSpan preview = constraints.ClampLength(Seconds(1.5));
            service.Resize(_harness.Scene, [new ElementResizeRequest(element, element.Start, Seconds(1.5), 0)], ripple);
            Assert.Multiple(() =>
            {
                Assert.That(preview, Is.EqualTo(Seconds(1)));
                Assert.That(element.Length, Is.EqualTo(preview));
                Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            });

            service.Resize(_harness.Scene, [new ElementResizeRequest(element, element.Start, Seconds(2), 0)], ripple);
            using var compositor = new SceneCompositor(_harness.Scene) { DisableResourceShare = true, ForceOriginalSource = true };
            CompositionFrame frame = compositor.EvaluateGraphics(element.Range.End - TimeSpan.FromTicks(1));
            Assert.Multiple(() =>
            {
                Assert.That(element.Length, Is.EqualTo(Seconds(2)));
                Assert.That(ReadPosition(frame.Objects.Single()), Is.LessThanOrEqualTo(1));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = originalClamp;
        }
    }

    private Element AddElement(double startSeconds, double lengthSeconds, Drawable drawable)
    {
        Element element = _harness.AddElement(Seconds(startSeconds), Seconds(lengthSeconds));
        element.Objects.Add(drawable);
        // Target is a reference, so these explicit-target fixtures must supply its clock.
        // Preserve the distinct ranges deliberately assigned by the anchored-target tests.
        while (drawable is DrawableTimeController controller && controller.Target.CurrentValue is { } target)
        {
            if (!target.IsTimeAnchor && target.TimeRange.Duration == TimeSpan.Zero)
                target.TimeRange = controller.TimeRange;
            drawable = target;
        }
        return element;
    }

    private static SourceVideo CreateVideo(int durationSeconds, float speed = 100, double offsetSeconds = 0)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), durationSeconds * 30)));
        return new SourceVideo
        {
            Source = { CurrentValue = source },
            Speed = { CurrentValue = speed },
            OffsetPosition = { CurrentValue = Seconds(offsetSeconds) }
        };
    }

    private static DrawableTimeController CreateController(Drawable target, float speed = 100, double offsetSeconds = 0)
        => new()
        {
            Target = { CurrentValue = target },
            Speed = { CurrentValue = speed },
            OffsetPosition = { CurrentValue = Seconds(offsetSeconds) }
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
            Assert.That(controller.Target!.RequireOriginal().TimeRange.Duration, Is.GreaterThan(TimeSpan.Zero),
                "The fixture must give every target a duration so playback evaluates the controller instead of bypassing it.");
            resource = controller.Target!;
        }

        Assert.That(resource, Is.TypeOf<SourceVideo.Resource>());
        var video = (SourceVideo.Resource)resource;
        // OnDraw adds the source offset after RequestedPosition has undergone speed/loop mapping.
        return (video.RequestedPosition + video.OffsetPosition).TotalSeconds;
    }

    private static KeyFrameAnimation<float> CreateGlobalSpeedRamp()
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 100,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = Seconds(10),
            Value = 200,
            Easing = new LinearEasing()
        });
        return animation;
    }

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);
}
