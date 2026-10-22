using Beutl.Audio;
using Beutl.Composition;
using Beutl.Configuration;
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
public class ElementResizeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementResizeService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_resize", start: MediaTime.Zero, duration: MediaTime.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementResizeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    // The delta window the timeline view reads for roll and slide drags.
    private (MediaTime Min, MediaTime Max) TrimDeltaBounds(params ElementTrimPair[] pairs)
    {
        var constraints = ElementResizeService.CreateTrimConstraints(_scene, pairs);
        return (constraints.Min, constraints.Max);
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    // Slides the middle element when there is one and rolls the cut between front and back otherwise.
    private bool RollOrSlide(Element front, Element? middle, Element back, MediaTime delta)
        => middle == null
            ? _service.Roll(_scene, [new ElementTrimPair(front, back)], delta)
            : _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], delta);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementResizeService(null!));
    }

    [Test]
    public void Resize_NullScene_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Resize(null!, []));
    }

    [Test]
    public void Resize_NullRequests_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Resize(_scene, null!));
    }

    [Test]
    public void Resize_SingleElement_AppliesNewSizeAndCommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, MediaTime.FromSeconds(1), MediaTime.FromSeconds(5), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_MultipleElements_CommitsSingleHistoryEntry()
    {
        Element e1 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);
        Element e2 = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 1);
        int before = _history.UndoCount;

        _service.Resize(_scene,
        [
            new ElementResizeRequest(e1, MediaTime.FromSeconds(1), MediaTime.FromSeconds(4), 0),
            new ElementResizeRequest(e2, MediaTime.FromSeconds(1), MediaTime.FromSeconds(6), 1),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(e1.Length, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(e2.Length, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_EmptyList_DoesNotCommit()
    {
        int before = _history.UndoCount;

        _service.Resize(_scene, []);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Resize_ZIndexChange_AppliesAndCommits()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 2)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.ZIndex, Is.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_NonRipple_NegativeStart_ClampsToZeroInsteadOfThrowing()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        Assert.DoesNotThrow(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, MediaTime.FromSeconds(-2), MediaTime.FromSeconds(1), 0)]));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Resize_NullElementInRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(null!, MediaTime.Zero, MediaTime.FromSeconds(1), 0)]));
    }

    [TestCase("0", 23_520_000)]
    [TestCase("-1", 23_520_000)]
    [TestCase("invalid", 23_520_000)]
    [TestCase("24", 29_400_000)]
    [TestCase("30", 23_520_000)]
    [TestCase("60", 11_760_000)]
    [TestCase("2147483647", 1)]
    public void Resize_FrameRate_AlwaysProducesPositiveLength(string rate, long expectedFlicks)
    {
        var project = new Project();
        project.Variables[ProjectVariableKeys.FrameRate] = rate;
        project.Items.Add(_scene);
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        _service.Resize(_scene, [new ElementResizeRequest(element, element.Start, MediaTime.Zero, 0)]);

        Assert.That(element.Length.Flicks, Is.EqualTo(expectedFlicks));
    }

    // A zero length once reached Scene.MoveChild and crashed the app (found by telemetry).
    [TestCase(false, 0)]
    [TestCase(false, -1)]
    [TestCase(false, 10)]
    [TestCase(true, 0)]
    [TestCase(true, -1)]
    [TestCase(true, 10)]
    public void Resize_ShortLength_ClampsAndCanBeUndone(bool ripple, int milliseconds)
    {
        _scene.Start = MediaTime.FromSeconds(5);
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        _service.Resize(_scene,
            [new ElementResizeRequest(element, element.Start, MediaTime.FromSeconds(milliseconds / 1000d), 0)], ripple);

        Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)), "Scene.Start is not the timeline origin.");
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))));
        _history.Undo();
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Resize_RippleOn_NegativeStart_ClampsToZeroAndApplies()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        Assert.DoesNotThrow(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, MediaTime.FromSeconds(-1), MediaTime.FromSeconds(3), 0)],
                ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(MediaTime.Zero),
                "the service floors the start to the timeline start");
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_RippleLeftEdgeCrossesOrigin_PreservesRightEdgeAndFollowers()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element follower = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        MediaTime requestedStart = MediaTime.FromSeconds(-1);
        MediaTime fixedEnd = element.Range.End;

        _service.Resize(_scene,
            [new ElementResizeRequest(element, requestedStart, fixedEnd - requestedStart, 0)], ripple: true);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Range.End, Is.EqualTo(fixedEnd));
            Assert.That(follower.Start, Is.EqualTo(fixedEnd));
        });
        _history.Undo();
        Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(follower.Start, Is.EqualTo(fixedEnd));
    }

    [TestCase(false, 0)]
    [TestCase(false, 10)]
    [TestCase(true, 0)]
    [TestCase(true, 10)]
    public void Resize_LeftEdgeBelowMinimum_PreservesRightEdge(bool ripple, int milliseconds)
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Element follower = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        MediaTime end = element.Range.End;
        MediaTime length = MediaTime.FromSeconds(milliseconds / 1000d);

        _service.Resize(_scene, [new ElementResizeRequest(element, end - length, length, 0)], ripple);

        Assert.That(element.Length, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))));
        Assert.That(element.Range.End, Is.EqualTo(end));
        Assert.That(follower.Start, Is.EqualTo(end));
        _history.Undo();
        Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [TestCase(-2, 1)]
    [TestCase(0, 0.51)]
    public void Resize_RippleLockedBarrier_RetainsMinimumLength(double start, double length)
    {
        Element barrier = AddElement(MediaTime.Zero, MediaTime.FromSeconds(0.5));
        barrier.IsLocked = true;
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));

        _service.Resize(_scene,
            [new ElementResizeRequest(element, MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), 0)], ripple: true);

        Assert.That(element.Start, Is.EqualTo(barrier.Range.End));
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))));
        Assert.That(barrier.Start, Is.EqualTo(MediaTime.Zero));
        Assert.That(barrier.Length, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        _history.Undo();
        Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [TestCase(-2, 1)]
    [TestCase(-2, -1)]
    public void Resize_RippleRequestedEndBeforeOrigin_UsesMinimumLength(int start, int length)
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        _service.Resize(_scene,
            [new ElementResizeRequest(element, MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), 0)], ripple: true);

        Assert.That(element.Start, Is.EqualTo(MediaTime.Zero));
        Assert.That(element.Length, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))));
    }

    [Test]
    public void Resize_RippleOn_ZeroLengthSecondRequest_ClampsAndAppliesBoth()
    {
        Element valid = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        Element invalid = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), zIndex: 1);
        int before = _history.UndoCount;

        Assert.DoesNotThrow(() =>
            _service.Resize(_scene,
            [
                new ElementResizeRequest(valid, MediaTime.FromSeconds(1), MediaTime.FromSeconds(5), 0),
                new ElementResizeRequest(invalid, MediaTime.FromSeconds(4), MediaTime.Zero, 1),
            ],
            ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(valid.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(valid.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(invalid.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(invalid.Length, Is.EqualTo(MediaTime.FromFrames(1, new Rational(30))),
                "the zero length is floored to one frame instead of rejecting the batch");
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_RippleOn_AutoAdjustsSceneDurationAfterFollowerShift()
    {
        bool original = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration = true;

        try
        {
            _scene.Duration = MediaTime.FromSeconds(4);
            Element target = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
            Element follower = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 0);

            _service.Resize(_scene,
                [new ElementResizeRequest(target, MediaTime.Zero, MediaTime.FromSeconds(5), 0)],
                ripple: true);

            Assert.Multiple(() =>
            {
                Assert.That(target.Range.End, Is.EqualTo(MediaTime.FromSeconds(5)));
                Assert.That(follower.Range.End, Is.EqualTo(MediaTime.FromSeconds(7)));
                Assert.That(_scene.Duration, Is.EqualTo(MediaTime.FromSeconds(7)));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration = original;
        }
    }

    [Test]
    public void Roll_NullArguments_Throw()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Roll(null!, [new ElementTrimPair(front, back)], MediaTime.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Roll(_scene, [new ElementTrimPair(null!, back)], MediaTime.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Roll(_scene, [new ElementTrimPair(front, null!)], MediaTime.Zero));
        });
    }

    [Test]
    public void Roll_SameElement_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, front)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_NotAdjacent_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        // Gap of 1s between front.End (2s) and back.Start (4s).
        Element back = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_Adjacent_AppliesAndCommitsOnce()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(front.Length + back.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Roll_LockedNeighbour_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        back.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_LockedLayer_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 4);
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 4);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 4, IsLocked = true });
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_NegativeDelta_ShrinksFrontGrowsBack()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));

        _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void Roll_DeltaClampedToMinFrame_DoesNotUndersizeFront()
    {
        // 30fps default: 1 frame ~ 33.3ms. front is 1 frame; asking to roll -1s
        // would empty front — must clamp so front keeps >= 1 frame.
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1d / 30));
        Element back = AddElement(MediaTime.FromSeconds(1d / 30), MediaTime.FromSeconds(2));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(-1));

        // Clamped to -(front.Length - 1 frame) = 0, so no effective delta.
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1d / 30)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void Roll_DeltaClampedToMinFrame_DoesNotUndersizeBack()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1d / 30));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        // Clamped to back.Length - 1 frame = 0, so no effective delta.
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Roll_AlreadyShorterThanMinFrame_DoesNotFlipDelta()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(0.001));
        Element back = AddElement(MediaTime.FromSeconds(0.001), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(0.001)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(0.001)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_UndoRestoresBothClips()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        });

        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void Roll_SourceBackedBack_AdvancesInPointByDelta()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        var video = new SourceVideo();
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            // Back in-point advances so its content stays anchored to the timeline.
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Roll_UndoRestoresBackInPoint()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        var video = new SourceVideo();
        back.Objects.Add(video);
        _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        _history.Undo();

        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Roll_PositiveDelta_ClampedByFrontSourceTail()
    {
        // Front source is 3s; with a 2s element only 1s of tail remains, so a +5s roll that the
        // adjacent lengths would allow must clamp to +1s rather than extend past the media.
        var frontSource = CreateVideoSource(3);
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        front.Objects.Add(new SourceVideo { Source = { Value = frontSource } });
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(9)));
        });
    }

    [Test]
    public void Roll_FixedNestedVideo_StillBoundsRenderedParentWindow()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(2)
        };
        var group = new DrawableGroup();
        group.Children.Add(video);
        front.Objects.Add(group);
        var backVideo = new SourceVideo();
        back.Objects.Add(backVideo);
        _history.Commit();
        int before = _history.UndoCount;

        var pair = new ElementTrimPair(front, back);
        (MediaTime _, MediaTime max) = TrimDeltaBounds(pair);
        bool applied = _service.Roll(_scene, [pair], MediaTime.FromSeconds(5));
        using var resource = (DrawableGroup.Resource)group.ToResource(
            new CompositionContext(front.Range.End - MediaTime.FromSeconds(0.1)));
        var sampled = (SourceVideo.Resource)resource.Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(applied, Is.True);
            Assert.That(front.Range, Is.EqualTo(TimeRange.FromSeconds(5)));
            Assert.That(back.Range, Is.EqualTo(TimeRange.FromSeconds(5, 7)));
            Assert.That(video.TimeRange, Is.EqualTo(TimeRange.FromSeconds(2)));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        _history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(front.Range, Is.EqualTo(TimeRange.FromSeconds(2)));
            Assert.That(back.Range, Is.EqualTo(TimeRange.FromSeconds(2, 10)));
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [TestCase(false, 100f, 0, 7, 4)]
    [TestCase(false, 100f, 1, 1, 1)]
    [TestCase(false, 100f, 2, 0, 0)]
    [TestCase(true, 100f, 0, 7, 4)]
    [TestCase(true, 100f, 1, 1, 1)]
    [TestCase(true, 100f, 2, 0, 0)]
    [TestCase(false, 50f, 0.25, 1.5, 1.5)]
    [TestCase(true, 50f, 0.25, 1.5, 1.5)]
    [TestCase(false, 200f, 1, 1.5, 1.5)]
    [TestCase(true, 200f, 1, 1.5, 1.5)]
    public void Trim_VideoBeforeItsStart_StopsAtWrappedSourceBoundary(
        bool slide, float speed, double offset, double expectedRoom, double expectedDelta)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 2 : 1);
        Element back = AddElement(backStart, MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            Speed = { Value = speed },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(3, 2)
        };
        var group = new DrawableGroup();
        group.Children.Add(video);
        front.Objects.Add(group);
        _history.Commit();
        int before = _history.UndoCount;
        var pair = new ElementTrimPair(front, back);
        if (!slide)
        {
            (_, MediaTime max) = TrimDeltaBounds(pair);
            Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(expectedRoom)));
        }

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(4));

        // The last frame alone can miss a bad interval just before local time reaches zero.
        MediaTime sampleTime = (front.Range.End < video.Start ? front.Range.End : video.Start)
                              - MediaTime.FromSeconds(0.1);
        using var resource = (DrawableGroup.Resource)group.ToResource(new CompositionContext(sampleTime));
        var sampled = (SourceVideo.Resource)resource.Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedDelta > 0));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1 + expectedDelta)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(expectedDelta)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(10 - expectedDelta)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(offset)));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedDelta > 0 ? 1 : 0)));
            if (middle is not null)
                Assert.That(middle.Start, Is.EqualTo(front.Range.End));
        });
    }

    [TestCase(false, 3, 1)]
    [TestCase(true, 3, 1)]
    [TestCase(false, 2, 0)]
    [TestCase(true, 2, 0)]
    [TestCase(false, 0, 3)]
    [TestCase(true, 0, 3)]
    public void RollOrSlide_AnchoredIdentityController_UsesMappedOutPoint(bool slide, int controllerStart, int expectedDelta)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 2 : 1);
        Element back = AddElement(backStart, MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            OffsetPosition = { Value = MediaTime.FromSeconds(1) },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(10, 2)
        };
        var controller = new DrawableTimeController
        {
            Target = { Value = video },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(controllerStart, 2)
        };
        var group = new DrawableGroup();
        group.Children.Add(controller);
        front.Objects.Add(group);
        var linked = new SourceSound();
        back.Objects.Add(linked);
        _history.Commit();
        int before = _history.UndoCount;
        var pair = new ElementTrimPair(front, back);
        (_, MediaTime max) = TrimDeltaBounds(pair);

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(4));

        MediaTime sampleTime = controller.Start > MediaTime.Zero && controller.Start < front.Range.End
            ? controller.Start : front.Range.End;
        using var resource = (DrawableGroup.Resource)group.ToResource(
            new CompositionContext(sampleTime - MediaTime.FromSeconds(0.1)));
        var controlled = (DrawableTimeController.Resource)resource.Children.Single();
        var sampled = (SourceVideo.Resource)controlled.Target!;
        Assert.Multiple(() =>
        {
            if (!slide) Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(expectedDelta)));
            Assert.That(applied, Is.EqualTo(expectedDelta > 0));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1 + expectedDelta)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(expectedDelta)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(10 - expectedDelta)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(linked.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedDelta)));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedDelta > 0 ? 1 : 0)));
            if (middle != null) Assert.That(middle.Start, Is.EqualTo(front.Range.End));
        });
    }

    [TestCase(false, 3, 0, 7)]
    [TestCase(true, 3, 0, 7)]
    [TestCase(false, 3, 1, 1)]
    [TestCase(true, 3, 1, 1)]
    [TestCase(false, 3, 2, 0)]
    [TestCase(true, 3, 2, 0)]
    [TestCase(false, 5, 0, 7)]
    [TestCase(true, 5, 0, 7)]
    [TestCase(false, 5, 1, 3)]
    [TestCase(true, 5, 1, 3)]
    [TestCase(false, 5, 2, 2)]
    [TestCase(true, 5, 2, 2)]
    public void Trim_LoopingVideo_StopsBeforeOffsetMovesWrappedFramesPastSourceEnd(
        bool slide, int videoStart, int offset, int expectedDelta)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 2 : 1);
        Element back = AddElement(backStart, MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsLoop = { Value = true },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(videoStart, 2)
        };
        var group = new DrawableGroup();
        group.Children.Add(video);
        front.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(7));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedDelta > 0));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1 + expectedDelta)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(expectedDelta)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(10 - expectedDelta)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedDelta > 0 ? 1 : 0)));
            foreach (MediaTime time in new[]
                     {
                         front.Start,
                         front.Range.End - MediaTime.FromSeconds(0.1),
                         video.Start - MediaTime.FromSeconds(0.1)
                     }.Where(front.Range.Contains))
            {
                using var resource = (DrawableGroup.Resource)group.ToResource(new CompositionContext(time));
                var sampled = (SourceVideo.Resource)resource.Children.Single();
                Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            }
        });
    }

    [Test]
    public void Slide_FixedNestedSound_DoesNotConstrainParentOutPoint()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        Element middle = AddElement(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(12), MediaTime.FromSeconds(10));
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
        var sound = new SourceSound
        {
            Source = { Value = source },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(2, 2)
        };
        var group = new SoundGroup();
        group.Children.Add(sound);
        front.Objects.Add(group);
        var backSound = new SourceSound();
        back.Objects.Add(backSound);
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Range, Is.EqualTo(TimeRange.FromSeconds(15)));
            Assert.That(middle.Range, Is.EqualTo(TimeRange.FromSeconds(15, 2)));
            Assert.That(back.Range, Is.EqualTo(TimeRange.FromSeconds(17, 5)));
            Assert.That(sound.TimeRange, Is.EqualTo(TimeRange.FromSeconds(2, 2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(backSound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(false, 0, 10, 3)]
    [TestCase(true, 0, 10, 3)]
    [TestCase(false, 1, 10, 4)]
    [TestCase(true, 1, 10, 4)]
    [TestCase(false, 0, 4, 3)]
    [TestCase(true, 0, 4, 5)]
    public void Roll_PartiallyVisibleFixedMedia_OnlyBoundsNewlyExposedSource(
        bool audio, int sourceStart, int sourceLength, int expectedDelta)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));
        if (audio)
        {
            var source = new SoundSource();
            source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
            var sound = new SourceSound
            {
                Source = { Value = source },
                IsTimeAnchor = true,
                TimeRange = TimeRange.FromSeconds(sourceStart, sourceLength)
            };
            var group = new SoundGroup();
            group.Children.Add(sound);
            front.Objects.Add(group);
        }
        else
        {
            var source = CreateVideoSource(5);
            var video = new SourceVideo
            {
                Source = { Value = source },
                IsTimeAnchor = true,
                TimeRange = TimeRange.FromSeconds(sourceStart, sourceLength)
            };
            var group = new DrawableGroup();
            group.Children.Add(video);
            front.Objects.Add(group);
        }

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2 + expectedDelta)));
            Assert.That(back.Start, Is.EqualTo(front.Range.End));
            Assert.That(back.Range.End, Is.EqualTo(MediaTime.FromSeconds(12)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Roll_NestedVideo_BoundsParentEvenWhenItsTimeAnchorIsFixed(bool fixedInnerGroup)
    {
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(_scene);
        const int FrontLength = 2;
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(FrontLength));
        Element back = AddElement(MediaTime.FromSeconds(FrontLength), MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo { Source = { Value = source } };
        var inner = new DrawableGroup
        {
            IsTimeAnchor = fixedInnerGroup,
            TimeRange = TimeRange.FromSeconds(1)
        };
        inner.Children.Add(video);
        var outer = new DrawableGroup();
        outer.Children.Add(inner);
        front.Objects.Add(outer);
        Assert.That(video.TimeRange, Is.EqualTo(TimeRange.FromSeconds(fixedInnerGroup ? 1 : 2)));

        Assert.That(_service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(5)), Is.True);
        Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
        Assert.That(video.Duration, Is.EqualTo(MediaTime.FromSeconds(fixedInnerGroup ? 1 : 5)));
    }

    [Test]
    public void Roll_PositiveDelta_ClampDisabled_ExtendsPastFrontSource()
    {
        // With ClampResizeToOriginalLength off, Roll must not clamp the front out-point to its
        // source tail — the clip may run past its media, matching the normal edge-resize path.
        bool original = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = false;
        try
        {
            var frontSource = CreateVideoSource(3);
            Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
            front.Objects.Add(new SourceVideo { Source = { Value = frontSource } });
            Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));

            bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(7)));
                Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(7)));
                Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = original;
        }
    }

    [Test]
    public void Roll_NegativeDelta_ClampedByBackSourceHead()
    {
        // Back source in-point sits at 0.5s, so a -5s roll can only pull it back to 0 (-0.5s).
        var backSource = CreateVideoSource(3);
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        var video = new SourceVideo { Source = { Value = backSource } };
        video.OffsetPosition.Value = MediaTime.FromSeconds(0.5);
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(-5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    [Test]
    public void Roll_NegativeDelta_UnknownDurationBack_ClampsInPointAtZero()
    {
        // Back media has no loadable source, so its duration is unknown. The in-point still cannot
        // go below zero, so a large negative roll must clamp the offset at 0, not drive it negative.
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(0.5);
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(-5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    // --- Slide ---

    [Test]
    public void Slide_NullArguments_Throw()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Slide(null!, [new ElementSlideLane(front, [middle], back)], MediaTime.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(null!, [middle], back)], MediaTime.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(front, [null!], back)], MediaTime.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(front, [middle], null!)], MediaTime.Zero));
        });
    }

    [Test]
    public void Slide_DuplicateElements_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], middle)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NotAdjacentFrontMiddle_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2)); // gap before
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NotAdjacentMiddleBack_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2)); // gap before
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_AdjacentTriplet_ShiftsMiddlePreservesTotalLength()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(middle.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            // Total length preserved: 2+3+2 = 7 before; 3+3+1 = 7 after.
            Assert.That(front.Length + middle.Length + back.Length, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slide_LockedParticipant_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        middle.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NegativeDelta_ShiftsMiddleLeft()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));

        _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(middle.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(front.Length + middle.Length + back.Length, Is.EqualTo(MediaTime.FromSeconds(7)));
        });
    }

    [Test]
    public void Slide_DeltaClamped_DoesNotUndersizeFront()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1d / 30));
        Element middle = AddElement(MediaTime.FromSeconds(1d / 30), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(1d / 30) + MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Slide_DeltaClamped_DoesNotUndersizeBack()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1d / 30));

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Slide_AlreadyShorterThanMinFrame_DoesNotFlipDelta()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(0.001));
        Element middle = AddElement(MediaTime.FromSeconds(0.001), MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2.001), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(0.001)));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(0.001)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2.001)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_UndoRestoresAllThreeClips()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(middle.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_SourceBackedBack_AdvancesInPointByDelta()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        back.Objects.Add(video);

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            // Middle only shifts in time; the back clip is trimmed at its head, so its in-point advances.
            Assert.That(middle.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Roll_DifferentLayers_NoCommit()
    {
        // Time-adjacent clips on different layers do not share an editable cut.
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_ElementOutsideScene_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var back = new Element { Start = MediaTime.FromSeconds(2), Length = MediaTime.FromSeconds(3) };

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_DifferentLayers_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2), zIndex: 0);

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_SpeedExpression_RejectsBeforeMutation(bool slide)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Expression = Expression.Create<float>("200") } };
        back.Objects.Add(video);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(backStart));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_TimeRemappedBack_UsesPlaybackSpeed(bool slide)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        back.Objects.Add(video);
        back.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            Target = { Value = video }
        });
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(1)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            if (middle != null)
                Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_TimeControllerBeforeVideo_DoesNotBlockLaterSource(bool slide)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        back.Objects.Add(new DrawableTimeController { Speed = { Value = 50 } });
        back.Objects.Add(video);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(1)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            if (middle != null)
                Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void TrimDeltaBounds_NoSourceMedia_BoundsByMinFrameLengths()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3));

        (MediaTime min, MediaTime max) = TrimDeltaBounds(new ElementTrimPair(front, back));

        MediaTime minDuration = MediaTime.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(minDuration - MediaTime.FromSeconds(2)));
            Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(3) - minDuration));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RollOrSlide_ContainerController_DoesNotRemapSiblingVideo(bool slide, bool decorator)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        Drawable container = decorator ? new DrawableDecorator() : new DrawableGroup();
        var children = container is DrawableDecorator d ? d.Children : ((DrawableGroup)container).Children;
        children.Add(new DrawableTimeController { Speed = { Value = 50 } });
        children.Add(video);
        back.Objects.Add(container);

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        Assert.That(applied, Is.True);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(1)));
        Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_DisabledControllerTarget_ClampsGrowthBeforeReenabling(bool slide)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = true,
            TimeRange = TimeRange.FromSeconds(10, 2)
        };
        var controller = new DrawableTimeController
        {
            IsEnabled = false,
            Speed = { Value = 200 },
            Target = { Value = video }
        };
        front.Objects.Add(controller);
        var linked = new SourceSound();
        back.Objects.Add(linked);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        int after = _history.UndoCount;
        controller.IsEnabled = true;
        using var resource = (DrawableTimeController.Resource)controller.ToResource(
            new CompositionContext(front.Range.End - MediaTime.FromSeconds(0.1)));
        var sampled = (SourceVideo.Resource)resource.Target!;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(backStart + MediaTime.FromSeconds(0.5)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(1.5)));
            Assert.That(linked.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(after, Is.EqualTo(before + 1));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            if (middle != null)
                Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RollOrSlide_DisabledControllerReferencedByPresenter_RejectsActiveMapping(bool slide)
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element? middle = slide ? AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) : null;
        MediaTime backStart = MediaTime.FromSeconds(slide ? 4 : 2);
        Element back = AddElement(backStart, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        var controller = new DrawableTimeController
        {
            IsEnabled = false,
            Speed = { Value = 50 },
            Target = { Value = video }
        };
        back.Objects.Add(video);
        back.Objects.Add(controller);
        back.Objects.Add(new DrawablePresenter { Target = { Value = controller } });
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = RollOrSlide(front, middle, back, MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(backStart));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void TrimDeltaBounds_SourceBackedFront_MaxClampedToSourceTail()
    {
        // Same shape as Roll_PositiveDelta_ClampedByFrontSourceTail: 3s source, 2s clip →
        // 1s of tail; the bounds must report the same +1s ceiling the Roll commit applies.
        var frontSource = CreateVideoSource(3);
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        front.Objects.Add(new SourceVideo { Source = { Value = frontSource } });
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));

        (MediaTime _, MediaTime max) = TrimDeltaBounds(new ElementTrimPair(front, back));

        Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void TrimDeltaBounds_BackInPoint_MinClampedToSourceHead()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(0.5);
        back.Objects.Add(video);

        (MediaTime min, MediaTime _) = TrimDeltaBounds(new ElementTrimPair(front, back));

        Assert.That(min, Is.EqualTo(MediaTime.FromSeconds(-0.5)));
    }

    [Test]
    public void TrimDeltaBounds_NegativeBackOffset_WindowStillSpansZero()
    {
        // A negative media offset is invalid state owned elsewhere, but the bounds contract
        // (Min ≤ 0 ≤ Max) must hold structurally.
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(-1);
        back.Objects.Add(video);

        (MediaTime min, MediaTime max) = TrimDeltaBounds(new ElementTrimPair(front, back));

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(MediaTime.Zero));
            Assert.That(max, Is.GreaterThanOrEqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void TrimDeltaBounds_SubFrameClip_ReturnsZeroWindow()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(0.001));
        Element back = AddElement(MediaTime.FromSeconds(0.001), MediaTime.FromSeconds(2));

        (MediaTime min, MediaTime max) = TrimDeltaBounds(new ElementTrimPair(front, back));

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(MediaTime.Zero));
            Assert.That(max, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void TrimDeltaBounds_EmptyPairs_ReturnsZeroWindow()
    {
        (MediaTime min, MediaTime max) = TrimDeltaBounds();

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(MediaTime.Zero));
            Assert.That(max, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void TrimDeltaBounds_MultiplePairs_ReturnsIntersection()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(5), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 1);

        (MediaTime min, MediaTime max) = TrimDeltaBounds(new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB));

        MediaTime minDuration = MediaTime.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            // Min is bounded by the shorter front (A, 2s); Max by the shorter back (B, 1s).
            Assert.That(min, Is.EqualTo(minDuration - MediaTime.FromSeconds(2)));
            Assert.That(max, Is.EqualTo(MediaTime.FromSeconds(1) - minDuration));
        });
    }

    // --- Roll / Slide: grouped multi-lane operations ---

    [Test]
    public void Roll_EmptyPairs_NoCommit()
    {
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_TwoPairs_AppliesBothAndCommitsOnce()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        var backVideo = new SourceVideo();
        backA.Objects.Add(backVideo);
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backA.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backA.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(frontB.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backB.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backB.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(backVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Roll_TwoPairs_SharedDeltaClampedByTightestPair()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            MediaTime.FromSeconds(5));

        MediaTime expected = MediaTime.FromSeconds(1) - MediaTime.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(2) + expected));
            Assert.That(frontB.Length, Is.EqualTo(MediaTime.FromSeconds(2) + expected));
            Assert.That(backB.Length, Is.EqualTo(MediaTime.FromSeconds(1) - expected));
        });
    }

    [Test]
    public void Roll_SharedSourceAcrossBacks_ShiftsOffsetOnce()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        var video = new SourceVideo();
        backA.Objects.Add(video);
        var presenter = new DrawablePresenter();
        presenter.Target.Value = video;
        backB.Objects.Add(presenter);

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Roll_SharedSourceBetweenFrontAndBack_RejectsOnlyRequiredOffsetWrites(bool backOwnsSource)
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 1);
        var video = new SourceVideo();
        (backOwnsSource ? backB : frontA).Objects.Add(video);
        var presenter = new DrawablePresenter();
        presenter.Target.Value = video;
        (backOwnsSource ? frontA : backB).Objects.Add(presenter);
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(!backOwnsSource));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(backOwnsSource ? 2 : 3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (backOwnsSource ? 0 : 1)));
        });
    }

    [Test]
    public void Roll_OneInvalidPair_RejectsWholeOperation()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(backA.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_ElementInTwoPairs_NoCommit()
    {
        Element first = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element second = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element third = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(first, second), new ElementTrimPair(second, third)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_EmptyLanes_NoCommit()
    {
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_EmptyMiddles_Throws()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element back = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));

        Assert.Throws<ArgumentException>(
            () => _service.Slide(_scene, [new ElementSlideLane(front, [], back)], MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Slide_TwoLanes_AppliesBothAndCommitsOnce()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element middleB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(frontA, [middleA], backA),
                new ElementSlideLane(frontB, [middleB], backB)
            ],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(middleA.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backA.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(backA.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(frontB.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(middleB.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(backB.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(backB.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slide_MultipleMiddlesInLane_ShiftsAllPreservingTotalLength()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element firstMiddle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element secondMiddle = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(3));

        bool applied = _service.Slide(
            _scene,
            [new ElementSlideLane(front, [firstMiddle, secondMiddle], back)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(firstMiddle.Start, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(secondMiddle.Start, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(back.Start, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(back.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_NonContiguousMiddles_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element firstMiddle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element secondMiddle = AddElement(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [new ElementSlideLane(front, [firstMiddle, secondMiddle], back)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_ElementSharedAcrossLanes_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(front, [middle], back),
                new ElementSlideLane(front, [middle], back)
            ],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_SharedSourceBetweenMiddleAndBack_NoCommit()
    {
        Element front = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        Element middle = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Element back = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3));
        var video = new SourceVideo();
        middle.Objects.Add(video);
        var presenter = new DrawablePresenter();
        presenter.Target.Value = video;
        back.Objects.Add(presenter);
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [new ElementSlideLane(front, [middle], back)],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(middle.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_OneInvalidLane_RejectsWholeOperation()
    {
        Element frontA = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2), zIndex: 1);
        Element middleB = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), zIndex: 1);
        Element backB = AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(3), zIndex: 1);
        backB.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(frontA, [middleA], backA),
                new ElementSlideLane(frontB, [middleB], backB)
            ],
            MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(frontA.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(middleA.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void ResizeToOriginalDuration_SetsLengthToMediaDurationFlooredToFrameRate()
    {
        // 37 frames at 25 fps is 1.48 s, which floors to 44 frames at the scene's 30 fps.
        Element element = AddVideoElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(5), new Rational(25, 1), 37);
        IElementResizeService service = _service;
        int before = _history.UndoCount;

        bool resized = service.ResizeToOriginalDuration(_scene, element);

        Assert.Multiple(() =>
        {
            Assert.That(resized, Is.True);
            Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(44d / 30)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void ResizeToOriginalDuration_WithoutRipple_StopsAtNextClip()
    {
        Element element = AddVideoElement(MediaTime.Zero, MediaTime.FromSeconds(1), new Rational(30, 1), 90);
        Element next = AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        IElementResizeService service = _service;

        service.ResizeToOriginalDuration(_scene, element);

        Assert.Multiple(() =>
        {
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(next.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public void ResizeToOriginalDuration_LockedOrWithoutMedia_NoCommit()
    {
        Element withoutMedia = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        Element locked = AddVideoElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), new Rational(30, 1), 90);
        locked.IsLocked = true;
        IElementResizeService service = _service;
        int before = _history.UndoCount;

        Assert.Multiple(() =>
        {
            Assert.That(service.ResizeToOriginalDuration(_scene, withoutMedia), Is.False);
            Assert.That(service.ResizeToOriginalDuration(_scene, locked), Is.False);
            Assert.That(withoutMedia.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(locked.Length, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    private Element AddVideoElement(MediaTime start, MediaTime length, Rational frameRate, int frameCount)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(16, 16, frameRate, frameCount)));
        Element element = AddElement(start, length);
        element.Objects.Add(new SourceVideo { Source = { Value = source } });
        return element;
    }
}
