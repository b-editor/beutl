using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

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
        _harness = new SceneHistoryHarness("beutl_resize", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementResizeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

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
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_MultipleElements_CommitsSingleHistoryEntry()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        _service.Resize(_scene,
        [
            new ElementResizeRequest(e1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), 0),
            new ElementResizeRequest(e2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6), 1),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(e1.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(e2.Length, Is.EqualTo(TimeSpan.FromSeconds(6)));
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
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 2)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.ZIndex, Is.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_RippleOn_NegativeStart_ThrowsBeforeMutation()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2), 0)],
                ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Resize_RippleOn_ZeroLength_ThrowsBeforeMutation()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.Zero, 0)],
                ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Resize_RippleOn_InvalidSecondRequest_ThrowsBeforeAnyMutation()
    {
        Element valid = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 0);
        Element invalid = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), zIndex: 1);
        int before = _history.UndoCount;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Resize(_scene,
            [
                new ElementResizeRequest(valid, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 0),
                new ElementResizeRequest(invalid, TimeSpan.FromSeconds(4), TimeSpan.Zero, 1),
            ],
            ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(valid.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(valid.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(invalid.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(invalid.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Resize_RippleOn_AutoAdjustsSceneDurationAfterFollowerShift()
    {
        bool original = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration = true;

        try
        {
            _scene.Duration = TimeSpan.FromSeconds(4);
            Element target = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
            Element follower = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 0);

            _service.Resize(_scene,
                [new ElementResizeRequest(target, TimeSpan.Zero, TimeSpan.FromSeconds(5), 0)],
                ripple: true);

            Assert.Multiple(() =>
            {
                Assert.That(target.Range.End, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(follower.Range.End, Is.EqualTo(TimeSpan.FromSeconds(7)));
                Assert.That(_scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(7)));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration = original;
        }
    }

    // --- Roll ---

    [Test]
    public void Roll_NullArguments_Throw()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Roll(null!, [new ElementTrimPair(front, back)], TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Roll(_scene, [new ElementTrimPair(null!, back)], TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Roll(_scene, [new ElementTrimPair(front, null!)], TimeSpan.Zero));
        });
    }

    [Test]
    public void Roll_SameElement_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, front)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_NotAdjacent_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        // Gap of 1s between front.End (2s) and back.Start (4s).
        Element back = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_Adjacent_AppliesAndCommitsOnce()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            // Total length preserved.
            Assert.That(front.Length + back.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Roll_LockedNeighbour_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        back.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_LockedLayer_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 4);
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 4);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 4, IsLocked = true });
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_NegativeDelta_ShrinksFrontGrowsBack()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

        _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void Roll_DeltaClampedToMinFrame_DoesNotUndersizeFront()
    {
        // 30fps default: 1 frame ~ 33.3ms. front is 1 frame; asking to roll -1s
        // would empty front — must clamp so front keeps >= 1 frame.
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 30));
        Element back = AddElement(TimeSpan.FromSeconds(1d / 30), TimeSpan.FromSeconds(2));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-1));

        // Clamped to -(front.Length - 1 frame) = 0, so no effective delta.
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(1d / 30)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Roll_DeltaClampedToMinFrame_DoesNotUndersizeBack()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1d / 30));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        // Clamped to back.Length - 1 frame = 0, so no effective delta.
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Roll_AlreadyShorterThanMinFrame_DoesNotFlipDelta()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Element back = AddElement(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_UndoRestoresBothClips()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });

        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void Roll_SourceBackedBack_AdvancesInPointByDelta()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        var video = new SourceVideo();
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            // Back in-point advances so its content stays anchored to the timeline.
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Roll_UndoRestoresBackInPoint()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        var video = new SourceVideo();
        back.Objects.Add(video);
        _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        _history.Undo();

        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Roll_PositiveDelta_ClampedByFrontSourceTail()
    {
        // Front source is 3s; with a 2s element only 1s of tail remains, so a +5s roll that the
        // adjacent lengths would allow must clamp to +1s rather than extend past the media.
        var frontSource = new VideoSource();
        frontSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        front.Objects.Add(new SourceVideo { Source = { CurrentValue = frontSource } });
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(9)));
        });
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
            var frontSource = new VideoSource();
            frontSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
            Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
            front.Objects.Add(new SourceVideo { Source = { CurrentValue = frontSource } });
            Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

            bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));
                Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
                Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
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
        var backSource = new VideoSource();
        backSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        var video = new SourceVideo { Source = { CurrentValue = backSource } };
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(0.5);
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
        });
    }

    [Test]
    public void Roll_NegativeDelta_UnknownDurationBack_ClampsInPointAtZero()
    {
        // Back media has no loadable source, so its duration is unknown. The in-point still cannot
        // go below zero, so a large negative roll must clamp the offset at 0, not drive it negative.
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(0.5);
        back.Objects.Add(video);

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(-5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
        });
    }

    // --- Slide ---

    [Test]
    public void Slide_NullArguments_Throw()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.Slide(null!, [new ElementSlideLane(front, [middle], back)], TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(null!, [middle], back)], TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(front, [null!], back)], TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => _service.Slide(_scene, [new ElementSlideLane(front, [middle], null!)], TimeSpan.Zero));
        });
    }

    [Test]
    public void Slide_DuplicateElements_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], middle)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NotAdjacentFrontMiddle_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)); // gap before
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NotAdjacentMiddleBack_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)); // gap before
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_AdjacentTriplet_ShiftsMiddlePreservesTotalLength()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            // Total length preserved: 2+3+2 = 7 before; 3+3+1 = 7 after.
            Assert.That(front.Length + middle.Length + back.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slide_LockedParticipant_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        middle.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_NegativeDelta_ShiftsMiddleLeft()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(front.Length + middle.Length + back.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));
        });
    }

    [Test]
    public void Slide_DeltaClamped_DoesNotUndersizeFront()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 30));
        Element middle = AddElement(TimeSpan.FromSeconds(1d / 30), TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(1d / 30) + TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Slide_DeltaClamped_DoesNotUndersizeBack()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1d / 30));

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(1d / 30)));
        });
    }

    [Test]
    public void Slide_AlreadyShorterThanMinFrame_DoesNotFlipDelta()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Element middle = AddElement(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromMilliseconds(2001), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromMilliseconds(2001)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_UndoRestoresAllThreeClips()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_SourceBackedBack_AdvancesInPointByDelta()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        back.Objects.Add(video);

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(1)));
            // Middle only shifts in time; the back clip is trimmed at its head, so its in-point advances.
            Assert.That(middle.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void Roll_DifferentLayers_NoCommit()
    {
        // Time-adjacent clips on different layers do not share an editable cut.
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_ElementOutsideScene_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        var back = new Element { Start = TimeSpan.FromSeconds(2), Length = TimeSpan.FromSeconds(3) };

        bool applied = _service.Roll(_scene, [new ElementTrimPair(front, back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_DifferentLayers_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 1);
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), zIndex: 0);

        bool applied = _service.Slide(_scene, [new ElementSlideLane(front, [middle], back)], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(middle.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    // --- GetTrimDeltaBounds ---

    [Test]
    public void GetTrimDeltaBounds_NullArguments_Throw()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.GetTrimDeltaBounds(null!, [new ElementTrimPair(front, back)]));
            Assert.Throws<ArgumentNullException>(() => _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(null!, back)]));
            Assert.Throws<ArgumentNullException>(() => _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, null!)]));
        });
    }

    [Test]
    public void GetTrimDeltaBounds_NoSourceMedia_BoundsByMinFrameLengths()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        (TimeSpan min, TimeSpan max) = _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, back)]);

        TimeSpan minDuration = TimeSpan.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(minDuration - TimeSpan.FromSeconds(2)));
            Assert.That(max, Is.EqualTo(TimeSpan.FromSeconds(3) - minDuration));
        });
    }

    [Test]
    public void GetTrimDeltaBounds_SourceBackedFront_MaxClampedToSourceTail()
    {
        // Same shape as Roll_PositiveDelta_ClampedByFrontSourceTail: 3s source, 2s clip →
        // 1s of tail; the bounds must report the same +1s ceiling the Roll commit applies.
        var frontSource = new VideoSource();
        frontSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 90)));
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        front.Objects.Add(new SourceVideo { Source = { CurrentValue = frontSource } });
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

        (TimeSpan _, TimeSpan max) = _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, back)]);

        Assert.That(max, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void GetTrimDeltaBounds_BackInPoint_MinClampedToSourceHead()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(0.5);
        back.Objects.Add(video);

        (TimeSpan min, TimeSpan _) = _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, back)]);

        Assert.That(min, Is.EqualTo(TimeSpan.FromSeconds(-0.5)));
    }

    [Test]
    public void GetTrimDeltaBounds_NegativeBackOffset_WindowStillSpansZero()
    {
        // A negative media offset is invalid state owned elsewhere, but the bounds contract
        // (Min ≤ 0 ≤ Max) must hold structurally — the View's per-move ClampDelta throws on
        // an inverted window.
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(-1);
        back.Objects.Add(video);

        (TimeSpan min, TimeSpan max) = _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, back)]);

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(TimeSpan.Zero));
            Assert.That(max, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void GetTrimDeltaBounds_SubFrameClip_ReturnsZeroWindow()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Element back = AddElement(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));

        (TimeSpan min, TimeSpan max) = _service.GetTrimDeltaBounds(_scene, [new ElementTrimPair(front, back)]);

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(TimeSpan.Zero));
            Assert.That(max, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void GetTrimDeltaBounds_EmptyPairs_ReturnsZeroWindow()
    {
        (TimeSpan min, TimeSpan max) = _service.GetTrimDeltaBounds(_scene, []);

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(TimeSpan.Zero));
            Assert.That(max, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void GetTrimDeltaBounds_MultiplePairs_ReturnsIntersection()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(5), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), zIndex: 1);

        (TimeSpan min, TimeSpan max) = _service.GetTrimDeltaBounds(
            _scene, [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)]);

        TimeSpan minDuration = TimeSpan.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            // Min is bounded by the shorter front (A, 2s); Max by the shorter back (B, 1s).
            Assert.That(min, Is.EqualTo(minDuration - TimeSpan.FromSeconds(2)));
            Assert.That(max, Is.EqualTo(TimeSpan.FromSeconds(1) - minDuration));
        });
    }

    // --- Roll / Slide: grouped multi-lane operations ---

    [Test]
    public void Roll_EmptyPairs_NoCommit()
    {
        int before = _history.UndoCount;

        bool applied = _service.Roll(_scene, [], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_TwoPairs_AppliesBothAndCommitsOnce()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 1);
        var backVideo = new SourceVideo();
        backA.Objects.Add(backVideo);
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backA.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backA.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(frontB.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backB.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backB.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(backVideo.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Roll_TwoPairs_SharedDeltaClampedByTightestPair()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            TimeSpan.FromSeconds(5));

        TimeSpan expected = TimeSpan.FromSeconds(1) - TimeSpan.FromSeconds(1d / 30);
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(TimeSpan.FromSeconds(2) + expected));
            Assert.That(frontB.Length, Is.EqualTo(TimeSpan.FromSeconds(2) + expected));
            Assert.That(backB.Length, Is.EqualTo(TimeSpan.FromSeconds(1) - expected));
        });
    }

    [Test]
    public void Roll_OneInvalidPair_RejectsWholeOperation()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(frontA, backA), new ElementTrimPair(frontB, backB)],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(frontA.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(backA.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Roll_ElementInTwoPairs_NoCommit()
    {
        Element first = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element second = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Element third = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Roll(
            _scene,
            [new ElementTrimPair(first, second), new ElementTrimPair(second, third)],
            TimeSpan.FromSeconds(1));

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

        bool applied = _service.Slide(_scene, [], TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_EmptyMiddles_Throws()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element back = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Assert.Throws<ArgumentException>(
            () => _service.Slide(_scene, [new ElementSlideLane(front, [], back)], TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Slide_TwoLanes_AppliesBothAndCommitsOnce()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element middleB = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 1);
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(frontA, [middleA], backA),
                new ElementSlideLane(frontB, [middleB], backB)
            ],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(frontA.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middleA.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backA.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(backA.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(frontB.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(middleB.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(backB.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(backB.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slide_MultipleMiddlesInLane_ShiftsAllPreservingTotalLength()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element firstMiddle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element secondMiddle = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3));

        bool applied = _service.Slide(
            _scene,
            [new ElementSlideLane(front, [firstMiddle, secondMiddle], back)],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(firstMiddle.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(secondMiddle.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(back.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(back.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Slide_NonContiguousMiddles_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element firstMiddle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element secondMiddle = AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [new ElementSlideLane(front, [firstMiddle, secondMiddle], back)],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_ElementSharedAcrossLanes_NoCommit()
    {
        Element front = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element middle = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Element back = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(front, [middle], back),
                new ElementSlideLane(front, [middle], back)
            ],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slide_OneInvalidLane_RejectsWholeOperation()
    {
        Element frontA = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 0);
        Element middleA = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
        Element backA = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 0);
        Element frontB = AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(2), zIndex: 1);
        Element middleB = AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
        Element backB = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), zIndex: 1);
        backB.IsLocked = true;
        int before = _history.UndoCount;

        bool applied = _service.Slide(
            _scene,
            [
                new ElementSlideLane(frontA, [middleA], backA),
                new ElementSlideLane(frontB, [middleB], backB)
            ],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(frontA.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(middleA.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
