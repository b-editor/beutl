using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// Playback starts the audio at the playhead and the video at PlaybackRange.StartFrame, so the start frame must be the
// frame the paused preview shows there. Frame-aligned times are whole ticks just short of the frame boundary at 24,
// 30 and 60 fps, which the truncation playback used to apply turned into the frame before.
[TestFixture]
public sealed class PlaybackRangeTests
{
    private static readonly int[] s_projectRates = [24, 25, 30, 50, 60];

    private static readonly TimeSpan s_longScene = TimeSpan.FromHours(1);

    // Playback leaves the playhead at frame.ToTimeSpan(rate) when it pauses, and the ruler snaps it to
    // TimeSpan.FromSeconds(frame / rate) through RoundToRate
    [Test]
    public void Starts_at_the_frame_of_a_frame_aligned_playhead([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        for (int frame = 1; frame <= 3000; frame++)
        {
            Check(frame.ToTimeSpan(rate), "paused");
            Check(TimeSpan.FromSeconds(frame / (double)rate), "snapped");

            void Check(TimeSpan playhead, string source)
            {
                int start = PlaybackRange.From(playhead, TimeSpan.Zero, s_longScene, rate).StartFrame;
                if (start != frame)
                    mismatches.Add($"{source} frame {frame} ({playhead.Ticks} ticks) started at {start}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    // Next frame adds TimeSpan.FromSeconds(1d / rate), 333,333 ticks at 30 fps, to the playhead
    [Test]
    public void Starts_at_the_frame_reached_with_next_frame([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        TimeSpan playhead = TimeSpan.Zero;
        for (int presses = 1; presses <= 300; presses++)
        {
            playhead += TimeSpan.FromSeconds(1d / rate);
            int start = PlaybackRange.From(playhead, TimeSpan.Zero, s_longScene, rate).StartFrame;
            if (start != presses)
                mismatches.Add($"frame {presses} ({playhead.Ticks} ticks) started at {start}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCase(1.4, 1)]
    [TestCase(1.6, 2)]
    [TestCase(0.4, 0)]
    public void Rounds_a_playhead_between_frames_to_the_nearest_frame(double frames, int expected)
    {
        const int rate = 30;
        TimeSpan playhead = TimeSpan.FromSeconds(frames / rate);

        Assert.That(PlaybackRange.From(playhead, TimeSpan.Zero, s_longScene, rate).StartFrame, Is.EqualTo(expected));
    }

    // Dragging the scene start sets Start to a RoundToRate value and Duration to the end minus it, so the end frame
    // must come from the end itself, not from a truncated start plus the length
    [Test]
    public void Ends_at_the_frame_at_the_end_of_a_moved_scene([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        for (int startFrame = 0; startFrame <= 300; startFrame++)
        {
            TimeSpan sceneStart = TimeSpan.FromSeconds((startFrame + 0.25) / rate).RoundToRate(rate);
            for (int length = 1; length <= 300; length++)
            {
                int endFrame = startFrame + length;
                TimeSpan sceneEnd = TimeSpan.FromSeconds((endFrame + 0.25) / rate).RoundToRate(rate);
                PlaybackRange range = PlaybackRange.From(sceneStart, sceneStart, sceneEnd - sceneStart, rate);
                if (range.StartFrame != startFrame || range.EndFrame != endFrame)
                    mismatches.Add($"frames {startFrame} to {endFrame} played as {range.StartFrame} to {range.EndFrame}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }
}
