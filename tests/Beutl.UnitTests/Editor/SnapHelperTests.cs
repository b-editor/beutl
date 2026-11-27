using Beutl.Editor.Components.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class SnapHelperTests
{
    // A frame every 1/24 s puts every eighth of a second on a frame.
    private static readonly Rational s_24fps = new(24);

    // A grid of 120 BPM, a beat every half second, unless told otherwise.
    private static BpmGridOptions Grid(int subdivisions = 1, double offset = 0, double bpm = 120, bool enabled = true)
        => new(bpm, subdivisions, MediaTime.FromSeconds(offset), enabled);

    // The timeline scale at which a second is this many pixels wide.
    private static float ScaleAt(double pixels) => (float)(pixels / FrameNumberHelper.SecondWidth);

    private static MediaTime[] GridLines(BpmGridOptions grid, double seconds, float scale)
        => [.. SnapHelper.CollectGridCandidates(grid, MediaTime.FromSeconds(seconds), s_24fps, scale)];

    private static MediaTime[] Seconds(params double[] seconds) => [.. seconds.Select(MediaTime.FromSeconds)];

    [Test]
    public void The_grid_offers_the_beat_on_either_side_of_the_time()
    {
        Assert.That(GridLines(Grid(), 1.3, ScaleAt(150)), Is.EqualTo(Seconds(1, 1.5)));
    }

    [Test]
    public void The_grid_counts_its_beats_from_the_offset()
    {
        Assert.That(GridLines(Grid(offset: 0.125), 1.3, ScaleAt(150)), Is.EqualTo(Seconds(1.125, 1.625)));
    }

    // At 120 BPM a quarter of a beat is 1/8 s, which BpmGridOverlay draws while it is at least 3 px wide.
    [TestCase(150d, new[] { 1.25, 1.375 })]
    [TestCase(25d, new[] { 1.25, 1.375 })]
    [TestCase(20d, new[] { 1.0, 1.5 })]
    public void The_grid_offers_its_subdivisions_only_while_they_are_drawn(double pixelsPerSecond, double[] expected)
    {
        Assert.That(GridLines(Grid(subdivisions: 4), 1.3, ScaleAt(pixelsPerSecond)), Is.EqualTo(Seconds(expected)));
    }

    [Test]
    public void The_grid_offers_nothing_while_it_is_off_or_its_beats_are_too_close_to_draw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GridLines(Grid(enabled: false), 1.3, ScaleAt(150)), Is.Empty);
            Assert.That(GridLines(Grid(bpm: 0), 1.3, ScaleAt(150)), Is.Empty);
            Assert.That(GridLines(Grid(bpm: double.NaN), 1.3, ScaleAt(150)), Is.Empty);
            // Half a second is 0.9 px here, under the 1 px BpmGridOverlay draws beats from.
            Assert.That(GridLines(Grid(), 1.3, ScaleAt(1.8)), Is.Empty);
        });
    }

    [Test]
    public void The_grid_offers_no_line_before_zero()
    {
        Assert.That(GridLines(Grid(offset: 0.25), 0.1, ScaleAt(150)), Is.EqualTo(Seconds(0.25)));
    }

    [Test]
    public void A_beat_between_frames_is_offered_on_the_nearest_frame()
    {
        // At 128 BPM a beat is 0.46875 s, 14.0625 frames at 30 fps.
        var rate = new Rational(30);
        MediaTime[] lines = [.. SnapHelper.CollectGridCandidates(
            Grid(bpm: 128), MediaTime.FromSeconds(0.4), rate, ScaleAt(150))];

        Assert.That(lines, Is.EqualTo(new[] { MediaTime.Zero, MediaTime.FromFrames(14, rate) }));
    }

    // At 150 px per second the reach of 8 px is 0.0533 s.
    [Test]
    public void A_target_in_reach_wins_over_a_closer_grid_line()
    {
        // The target is 6 px away, the beat at 1 s 1.5 px.
        MediaTime target = MediaTime.FromSeconds(1.05);

        SnapResult snap = SnapHelper.Snap(MediaTime.FromSeconds(1.01), [target], Grid(), s_24fps, ScaleAt(150));

        Assert.That(snap, Is.EqualTo(new SnapResult(target, true)));
    }

    [Test]
    public void The_grid_catches_the_time_when_no_target_is_in_reach()
    {
        // The target is 15 px away, the beat at 1 s 1.5 px.
        SnapResult snap = SnapHelper.Snap(
            MediaTime.FromSeconds(1.01), Seconds(1.11), Grid(), s_24fps, ScaleAt(150));

        Assert.That(snap, Is.EqualTo(new SnapResult(MediaTime.FromSeconds(1), true)));
    }

    [Test]
    public void A_time_out_of_reach_of_everything_stays_where_it_is()
    {
        // Halfway between two beats, 37.5 px from each.
        MediaTime time = MediaTime.FromSeconds(1.25);

        SnapResult snap = SnapHelper.Snap(time, Seconds(2), Grid(), s_24fps, ScaleAt(150));

        Assert.That(snap, Is.EqualTo(new SnapResult(time, false)));
    }

    [Test]
    public void The_scene_offers_its_markers()
    {
        var scene = new Scene();
        scene.Markers.Add(new SceneMarker(MediaTime.FromSeconds(3)));
        scene.Markers.Add(new SceneMarker(MediaTime.FromSeconds(7)));

        Assert.That(SnapHelper.CollectSceneCandidates(scene, MediaTime.FromSeconds(1)), Is.SupersetOf(Seconds(3, 7)));
    }
}
