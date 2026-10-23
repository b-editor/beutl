using System.Globalization;
using System.Text.Json;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// A time read outside a project, as a template's is, or written by hand can fall up to a tick short of its frame.
// Edits that check frames compare on the frame grid, so such a clip or scene takes the edits an exact one does (#2885).
[TestFixture]
public sealed class TickShortFrameBoundaryTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];

    [TestCaseSource(nameof(s_rates))]
    public void A_two_frame_clip_a_tick_short_splits_at_its_middle_frame_only(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element clip = AddTickShort(harness, 0, 2, rate);
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
    public void A_one_frame_clip_a_tick_short_rolls_and_slides(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element rollFront = AddTickShort(harness, 0, 1, rate);
        Element rollBack = AddTickShort(harness, 1, 2, rate);
        Element slideFront = AddTickShort(harness, 0, 1, rate, zIndex: 1);
        Element slideMiddle = AddTickShort(harness, 1, 1, rate, zIndex: 1);
        Element slideBack = AddTickShort(harness, 2, 2, rate, zIndex: 1);
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
    public void Setting_the_start_of_a_scene_ending_a_tick_short_at_its_end_keeps_its_last_frame(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        harness.Scene.Duration = TickShort(MediaTime.FromFrames(2, rate));

        new SceneTimeRangeService(harness.History).SetStart(harness.Scene, MediaTime.FromFrames(2, rate));

        Assert.That(harness.Scene.Start, Is.EqualTo(MediaTime.FromFrames(1, rate)));
    }

    [TestCaseSource(nameof(s_rates))]
    public void The_previous_keyframe_from_a_keyframe_a_tick_short_is_the_one_before_it(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        Element element = harness.AddElement(MediaTime.Zero, MediaTime.FromFrames(30, rate));
        var shape = new RectShape();
        element.AddObject(shape);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TickShort(MediaTime.FromFrames(1, rate)), Value = 1 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TickShort(MediaTime.FromFrames(2, rate)), Value = 2 });
        shape.Width.SetKeyFrameAnimation(animation);

        MediaTime? previous = KeyFrameNavigationHelper.FindAdjacentKeyFrame([element], MediaTime.FromFrames(2, rate), forward: false);

        Assert.That(previous, Is.EqualTo(MediaTime.FromFrames(1, rate)));
    }

    [TestCaseSource(nameof(s_rates))]
    public void A_gap_starting_a_tick_short_of_the_playhead_is_found(string rateText)
    {
        using var harness = Open(rateText, out Rational rate);
        AddTickShort(harness, 0, 2, rate);
        AddTickShort(harness, 4, 2, rate);

        SceneGap? gap = harness.Scene.FindNextGap(
            MediaTime.FromFrames(2, rate), new TimeRange(MediaTime.Zero, MediaTime.FromFrames(30, rate)));

        Assert.That(gap?.Range.Start.ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(2));
    }

    private static SceneHistoryHarness Open(string rateText, out Rational rate)
    {
        var harness = new SceneHistoryHarness("beutl_tick_short_frames", duration: MediaTime.FromSeconds(60));
        var project = new Project();
        project.FrameRate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        project.Items.Add(harness.Scene);
        rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        return harness;
    }

    // A clip from frame to frame + count with both ends a little short of their frames.
    private static Element AddTickShort(SceneHistoryHarness harness, int frame, int count, Rational rate, int zIndex = 0)
    {
        MediaTime start = TickShort(MediaTime.FromFrames(frame, rate));
        MediaTime end = TickShort(MediaTime.FromFrames(frame + count, rate));
        return harness.AddElement(start, end - start, zIndex);
    }

    // A time as its TimeSpan string reads outside a project: rounded down to a tick, then up to a flick.
    private static MediaTime TickShort(MediaTime time)
    {
        string text;
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            text = JsonSerializer.Serialize(time);
        return JsonSerializer.Deserialize<MediaTime>(text);
    }
}
