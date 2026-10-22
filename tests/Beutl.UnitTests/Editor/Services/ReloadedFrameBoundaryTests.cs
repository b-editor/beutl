using System.Globalization;
using System.Text.Json;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// A time read back from a file falls up to a tick short of its frame. Edits that check frames compare on the frame
// grid, so a reopened clip or scene takes the same edits as one made in this session (#2885).
[TestFixture]
public sealed class ReloadedFrameBoundaryTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];

    [TestCaseSource(nameof(s_rates))]
    public void A_reopened_two_frame_clip_splits_at_its_middle_frame_only(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element clip = AddReopened(harness, 0, 2, rate);
        var service = new ElementStructureService(harness.History);

        service.Split(harness.Scene, [clip], MediaTime.FromFrames(2, rate));
        int afterCutAtEnd = harness.Scene.Children.Count;
        service.Split(harness.Scene, [clip], MediaTime.FromFrames(1, rate));

        Assert.Multiple(() =>
        {
            Assert.That(afterCutAtEnd, Is.EqualTo(1));
            Assert.That(harness.Scene.Children, Has.Count.EqualTo(2));
        });
    }

    [TestCaseSource(nameof(s_rates))]
    public void A_reopened_one_frame_clip_rolls_and_slides(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element rollFront = AddReopened(harness, 0, 1, rate);
        Element rollBack = AddReopened(harness, 1, 2, rate);
        Element slideFront = AddReopened(harness, 0, 1, rate, zIndex: 1);
        Element slideMiddle = AddReopened(harness, 1, 1, rate, zIndex: 1);
        Element slideBack = AddReopened(harness, 2, 2, rate, zIndex: 1);
        var service = new ElementResizeService(harness.History);
        MediaTime frame = MediaTime.FromFrames(1, rate);

        bool rolled = service.Roll(harness.Scene, [new ElementTrimPair(rollFront, rollBack)], frame);
        bool slid = service.Slide(harness.Scene, [new ElementSlideLane(slideFront, [slideMiddle], slideBack)], frame);

        Assert.Multiple(() =>
        {
            Assert.That(rolled, Is.True);
            Assert.That(slid, Is.True);
            Assert.That(rollFront.Length.ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(2));
            Assert.That(slideMiddle.Start.ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(2));
        });
    }

    [TestCaseSource(nameof(s_rates))]
    public void Setting_a_reopened_scene_start_at_its_end_keeps_its_last_frame(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        harness.Scene.Duration = Reopen(MediaTime.FromFrames(2, rate));

        new SceneTimeRangeService(harness.History).SetStart(harness.Scene, MediaTime.FromFrames(2, rate));

        Assert.That(harness.Scene.Start, Is.EqualTo(MediaTime.FromFrames(1, rate)));
    }

    [TestCaseSource(nameof(s_rates))]
    public void The_previous_keyframe_from_a_reopened_keyframe_is_the_one_before_it(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element element = harness.AddElement(MediaTime.Zero, MediaTime.FromFrames(30, rate));
        var shape = new RectShape();
        element.AddObject(shape);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = Reopen(MediaTime.FromFrames(1, rate)), Value = 1 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = Reopen(MediaTime.FromFrames(2, rate)), Value = 2 });
        shape.Width.SetKeyFrameAnimation(animation);

        MediaTime? previous = KeyFrameNavigationHelper.FindAdjacentKeyFrame([element], MediaTime.FromFrames(2, rate), forward: false);

        Assert.That(previous, Is.EqualTo(MediaTime.FromFrames(1, rate)));
    }

    [TestCaseSource(nameof(s_rates))]
    public void A_gap_starting_at_the_playhead_is_found_after_reopening(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        AddReopened(harness, 0, 2, rate);
        AddReopened(harness, 4, 2, rate);

        SceneGap? gap = harness.Scene.FindNextGap(
            MediaTime.FromFrames(2, rate), new TimeRange(MediaTime.Zero, MediaTime.FromFrames(30, rate)));

        Assert.That(gap?.Range.Start.ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(2));
    }

    private static SceneHistoryHarness Open(string rateText, out Rational rate)
    {
        var harness = new SceneHistoryHarness("beutl_reopened_frames", duration: MediaTime.FromSeconds(60));
        var project = new Project();
        project.Variables[ProjectVariableKeys.FrameRate] = rateText;
        project.Items.Add(harness.Scene);
        rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        return harness;
    }

    // A clip from frame to frame + count as a file gives it back: both ends a little short of their frames.
    private static Element AddReopened(SceneHistoryHarness harness, int frame, int count, Rational rate, int zIndex = 0)
    {
        MediaTime start = Reopen(MediaTime.FromFrames(frame, rate));
        MediaTime end = Reopen(MediaTime.FromFrames(frame + count, rate));
        return harness.AddElement(start, end - start, zIndex);
    }

    private static MediaTime Reopen(MediaTime time) => JsonSerializer.Deserialize<MediaTime>(JsonSerializer.Serialize(time));
}
