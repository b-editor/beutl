using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// Playback starts the audio at the playhead and the video at PlaybackRange.StartFrame, so the start frame must be the
// frame the paused preview shows there, also for a time that falls just short of the frame boundary.
[TestFixture]
public sealed class PlaybackRangeTests
{
    private static readonly int[] s_projectRates = [24, 25, 30, 50, 60];

    private static readonly MediaTime s_longScene = MediaTime.FromSeconds(3600);

    // Playback leaves the playhead at MediaTime.FromFrames(frame, rate) when it pauses, and the ruler snaps it to
    // MediaTime.FromSeconds(frame / rate) through RoundToFrame
    [Test]
    public void Starts_at_the_frame_of_a_frame_aligned_playhead([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        for (int frame = 1; frame <= 3000; frame++)
        {
            Check(MediaTime.FromFrames(frame, new Rational(rate)), "paused");
            Check(MediaTime.FromSeconds(frame / (double)rate), "snapped");

            void Check(MediaTime playhead, string source)
            {
                int start = PlaybackRange.From(playhead, MediaTime.Zero, s_longScene, new Rational(rate)).StartFrame;
                if (start != frame)
                    mismatches.Add($"{source} frame {frame} ({playhead.Flicks} flicks) started at {start}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    // Next frame adds one frame to the playhead
    [Test]
    public void Starts_at_the_frame_reached_with_next_frame([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        MediaTime playhead = MediaTime.Zero;
        for (int presses = 1; presses <= 300; presses++)
        {
            playhead += MediaTime.FromFrames(1, new Rational(rate));
            int start = PlaybackRange.From(playhead, MediaTime.Zero, s_longScene, new Rational(rate)).StartFrame;
            if (start != presses)
                mismatches.Add($"frame {presses} ({playhead.Flicks} flicks) started at {start}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCase(1.4, 1)]
    [TestCase(1.6, 2)]
    [TestCase(0.4, 0)]
    public void Rounds_a_playhead_between_frames_to_the_nearest_frame(double frames, int expected)
    {
        const int rate = 30;
        MediaTime playhead = MediaTime.FromSeconds(frames / rate);

        Assert.That(PlaybackRange.From(playhead, MediaTime.Zero, s_longScene, new Rational(rate)).StartFrame, Is.EqualTo(expected));
    }

    // Dragging the scene start sets Start to a RoundToFrame value and Duration to the end minus it, so the end frame
    // must come from the end itself, not from a truncated start plus the length
    [Test]
    public void Ends_at_the_frame_at_the_end_of_a_moved_scene([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        for (int startFrame = 0; startFrame <= 300; startFrame++)
        {
            MediaTime sceneStart = MediaTime.FromSeconds((startFrame + 0.25) / rate)
                .RoundToFrame(new Rational(rate), MidpointRounding.AwayFromZero);
            for (int length = 1; length <= 300; length++)
            {
                int endFrame = startFrame + length;
                MediaTime sceneEnd = MediaTime.FromSeconds((endFrame + 0.25) / rate)
                    .RoundToFrame(new Rational(rate), MidpointRounding.AwayFromZero);
                PlaybackRange range = PlaybackRange.From(sceneStart, sceneStart, sceneEnd - sceneStart, new Rational(rate));
                if (range.StartFrame != startFrame || range.EndFrame != endFrame)
                    mismatches.Add($"frames {startFrame} to {endFrame} played as {range.StartFrame} to {range.EndFrame}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }
}
