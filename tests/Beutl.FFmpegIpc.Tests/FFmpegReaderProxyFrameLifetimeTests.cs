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
/// The worker decodes into a small ring of shared-memory slots and its prefetch thread recycles them
/// knowing only the single most recently served slot, so a frame that aliased its slot would have its
/// pixels replaced by a later read — the caller would still be holding a valid-looking bitmap whose
/// picture now belongs to a different time. The preview then draws that picture, and with the frame
/// cache on it is stored under the original frame number and shown from then on.
/// </para>
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyFrameLifetimeTests
{
    // DecodingHandler.DefaultSlotCount is 4; reading well past that wraps the ring several times.
    private const int ReadsAfterHold = 12;

    [Test]
    public void A_held_frame_keeps_its_pixels_while_later_frames_are_read()
    {
        MediaReader reader = OpenFixtureReader();
        using MediaReader _ = reader;

        Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? held), Is.True, "frame 0 must decode");
        using (held)
        {
            byte[] pixelsWhenRead = held!.Value.GetPixelSpan().ToArray();

            for (int frame = 1; frame <= ReadsAfterHold; frame++)
            {
                if (reader.ReadVideo(frame, out Ref<Bitmap>? other))
                {
                    other.Dispose();
                }
            }

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
    public void Concurrent_reads_of_one_frame_return_the_same_picture()
    {
        MediaReader reader = OpenFixtureReader();
        using (reader)
        {
            Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? first), Is.True, "frame 0 must decode");
            byte[] expected;
            using (first)
            {
                expected = first!.Value.GetPixelSpan().ToArray();
            }

            // The worker releases its reader lock before the response arrives and protects only the
            // slot it served last, so two unserialized readers of one proxy can have prefetch
            // recycle the slot one of them is still copying out of.
            var mismatch = 0;
            Parallel.For(0, 24, _ =>
            {
                if (!reader.ReadVideo(0, out Ref<Bitmap>? frame)) return;
                using (frame)
                {
                    if (!frame!.Value.GetPixelSpan().SequenceEqual(expected))
                    {
                        Interlocked.Increment(ref mismatch);
                    }
                }
            });

            Assert.That(mismatch, Is.Zero, "concurrent reads of the same frame returned different pixels");
        }
    }
}
