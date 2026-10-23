using System.Globalization;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

// Frames come from MediaTime with the rounding each caller names, at whole and NTSC rates (#2885).
[TestFixture]
public sealed class MediaTimeFrameSelectionTests
{
    private static readonly string[] s_rates = ["24", "30000/1001", "30", "60"];

    [TestCaseSource(nameof(s_rates))]
    public void A_frame_of_the_scene_rate_is_exact(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        var project = new Project();
        project.FrameRate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        var scene = new Scene(100, 100, string.Empty);
        project.Items.Add(scene);

        MediaTime frame = SceneTimeRangeService.GetFrameDuration(scene);

        Assert.Multiple(() =>
        {
            Assert.That(SceneTimeRangeService.GetFrameRate(scene), Is.EqualTo(rate));
            Assert.That(frame, Is.EqualTo(MediaTime.FromFrames(1, rate)));
            Assert.That(new MediaTime(frame.Flicks * 1000), Is.EqualTo(MediaTime.FromFrames(1000, rate)));
        });
    }

    [TestCaseSource(nameof(s_rates))]
    public void RoundToFrame_picks_the_frame_of_a_time_just_off_the_grid(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        var one = new MediaTime(1);
        var mismatches = new List<string>();
        for (int frame = 1; frame <= 600; frame++)
        {
            MediaTime time = MediaTime.FromFrames(frame, rate);
            if (Saved(time).RoundToFrame(rate, MidpointRounding.AwayFromZero) != time
                || time.RoundToFrame(rate, MidpointRounding.ToNegativeInfinity) != time
                || (time - one).RoundToFrame(rate, MidpointRounding.ToNegativeInfinity) != MediaTime.FromFrames(frame - 1, rate)
                || (time + one).RoundToFrame(rate, MidpointRounding.ToPositiveInfinity) != MediaTime.FromFrames(frame + 1, rate))
            {
                mismatches.Add($"frame {frame}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCaseSource(nameof(s_rates))]
    public void Onion_skin_samples_land_on_the_frame_grid(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);

        IReadOnlyList<OnionSkinSample> samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            100, MediaTime.Zero, MediaTime.FromSeconds(60), rate, 3, 3, 0.6f, 0.6f);

        Assert.That(
            samples.Select(sample => sample.Time),
            Is.EqualTo(new[] { 97, 98, 99, 101, 102, 103 }.Select(frame => MediaTime.FromFrames(frame, rate))));
    }

    // A playhead read back from a file falls just short of its frame, and still shows that frame and its neighbours.
    [TestCaseSource(nameof(s_rates))]
    public void An_edit_on_a_neighbouring_frame_redraws_the_onion_skin(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        MediaTime playhead = Saved(MediaTime.FromFrames(100, rate));

        bool Affects(int frame) => OnionSkinHelper.IsEditAffectingPreview(
            [new TimeRange(MediaTime.FromFrames(frame, rate), MediaTime.FromFrames(1, rate))],
            playhead, true, 1, 0.5f, 1, 0.5f, rate, MediaTime.Zero, MediaTime.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(Affects(99), Is.True);
            Assert.That(Affects(101), Is.True);
            Assert.That(Affects(98), Is.False);
            Assert.That(Affects(102), Is.False);
        });
    }

    private static MediaTime Saved(MediaTime time)
        => MediaTime.FromTicks(time.ToTicks(MidpointRounding.ToNegativeInfinity), MidpointRounding.ToPositiveInfinity);
}
