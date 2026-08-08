using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Process-level IPC lifetime test: a frame handed back by <c>FFmpegReaderProxy.ReadVideo</c> must
/// keep its pixels for as long as the caller holds it.
/// <para>
/// The worker decodes into a small ring of shared-memory slots and recycles them knowing only the most
/// recently served one, so a frame aliasing its slot has its pixels replaced by a later read: a
/// valid-looking bitmap whose picture belongs to a different time, which the frame cache then stores
/// under the original frame number.
/// </para>
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyFrameLifetimeTests
{
    // DecodingHandler.DefaultSlotCount is 4; reading well past that wraps the ring several times.
    private const int RingSlots = 4;
    private const int ReadsAfterHold = 12;

    // More frames than the ring has slots, so a response has to displace an earlier one.
    private const int ConcurrentFrames = 6;

    [Test]
    public void A_held_frame_keeps_its_pixels_while_later_frames_are_read()
    {
        MediaReader reader = OpenFixtureReader();
        using MediaReader _ = reader;

        Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? held), Is.True, "frame 0 must decode");
        using (held)
        {
            byte[] pixelsWhenRead = held!.Value.GetPixelSpan().ToArray();

            int recycled = 0;
            for (int frame = 1; frame <= ReadsAfterHold; frame++)
            {
                if (reader.ReadVideo(frame, out Ref<Bitmap>? other))
                {
                    other.Dispose();
                    recycled++;
                }
            }

            // Fewer reads than the ring has slots would never come back around to the held one.
            Assert.That(recycled, Is.GreaterThanOrEqualTo(RingSlots),
                "the fixture did not serve enough frames to wrap the ring");

            byte[] pixelsAfterLaterReads = held.Value.GetPixelSpan().ToArray();

            Assert.That(pixelsAfterLaterReads, Is.EqualTo(pixelsWhenRead),
                "the held frame's pixels changed while later frames were read: the returned bitmap is "
                + "aliasing a ring-buffer slot the worker recycled");
        }
    }

    private static MediaReader OpenFixtureReader()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");
        if (!WorkerBinaryPresent() || !File.Exists(fixture))
        {
            Assert.Ignore("FFmpeg worker binary or video fixture not present; skipping the test.");
        }

        try
        {
            FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping the test.");
        }

        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        MediaReader? reader = decoderInfo.Open(fixture, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null, "the worker started with FFmpeg natives loaded, so Open must succeed");
        Assert.That(reader!.HasVideo, Is.True, "the fixture must expose a video stream");
        return reader;
    }

    // CopyWorkerBinary lays the worker flat into the test bin dir, but Nuke publish isolates it under an
    // FFmpegWorker/ subdir, so probe both — mirroring FFmpegWorkerProcess.ResolveWorkerCommand.
    private static bool WorkerBinaryPresent()
    {
        string baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "Beutl.FFmpegWorker.dll"))
            || File.Exists(Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker.dll"));
    }

    [Test]
    public void Concurrent_reads_of_different_frames_each_return_their_own_picture()
    {
        MediaReader reader = OpenFixtureReader();
        using (reader)
        {
            var expected = new byte[ConcurrentFrames][];
            for (int frame = 0; frame < ConcurrentFrames; frame++)
            {
                Assert.That(reader.ReadVideo(frame, out Ref<Bitmap>? baseline), Is.True,
                    $"frame {frame} must decode");
                using (baseline)
                {
                    expected[frame] = baseline!.Value.GetPixelSpan().ToArray();
                }
            }

            // Differing frames, so a response moves the worker's served slot while an earlier copy is
            // still running: unserialized, prefetch is free to recycle the slot being copied.
            var mismatch = 0;
            Parallel.For(0, ConcurrentFrames * 8, i =>
            {
                int frame = i % ConcurrentFrames;
                if (!reader.ReadVideo(frame, out Ref<Bitmap>? read)) return;
                using (read)
                {
                    if (!read!.Value.GetPixelSpan().SequenceEqual(expected[frame]))
                    {
                        Interlocked.Increment(ref mismatch);
                    }
                }
            });

            Assert.That(mismatch, Is.Zero, "a concurrent read returned another frame's pixels");
        }
    }
}
