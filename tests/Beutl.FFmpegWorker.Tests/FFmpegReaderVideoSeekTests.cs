using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.FFmpegWorker.Encoding;
using Beutl.FFmpegWorker.Handlers;
using Beutl.Media;
using Beutl.Media.Decoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegReaderVideoSeekTests
{
    private string _path = string.Empty;

    [OneTimeSetUp]
    public async Task CreateVideoWithReorderedFrames()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        if (MediaCodec.FindEncoder("libx264") == null)
            Assert.Ignore("The libx264 encoder is not available.");

        _path = Path.Combine(Path.GetTempPath(), $"beutl-video-seek-{Guid.NewGuid():N}.mp4");
        var size = new PixelSize(96, 64);
        var rate = new Rational(30000, 1001);
        var controller = FFmpegTestHelpers.CreateController(_path, size, rate, 44100, 2);
        controller.VideoSettings.Codec.Value = new CodecRecord("libx264", "H.264");
        controller.VideoSettings.Format.Value = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        controller.VideoSettings.Options.Value.Add(new("x264-params", "keyint=50:min-keyint=50:scenecut=0:bframes=3:b-adapt=0"));
        using var frames = new GradientFrameProvider(150, rate, size.Width, size.Height);
        using var samples = new EmptySampleProvider();
        await controller.Encode(frames, samples, CancellationToken.None);

        using var demuxer = MediaDemuxer.Open(_path);
        Assert.That(demuxer.First(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            .CodecparRef.video_delay, Is.GreaterThan(0), "the fixture must contain reordered B frames");
    }

    [OneTimeTearDown]
    public void DeleteVideo()
    {
        if (_path.Length > 0)
            File.Delete(_path);
    }

    [TestCase(1)]
    [TestCase(8)]
    public void SeekingAndResumingPlaybackMatchSequentialDecoding(int threads)
    {
        AssertSeeksMatch(_path, threads);
    }

    [TestCase("late-video.mp4")]
    [TestCase("marker-bframes-no-edit.mp4")]
    [TestCase("marker-bframes.mp4")]
    public void SeekingPreservesContainerOriginAndEditLists(string name)
    {
        AssertSeeksMatch(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), 8);
    }

    [Test]
    public unsafe void PrefetchFollowsPlaybackAndKeepsTheServedFrameIntact()
    {
        var settings = new FFmpegDecodingSettings();
        List<byte[]> expected = ReadSequentialFrames(_path, settings);
        using var reader = new FFmpegReader(_path, new MediaOptions(MediaMode.Video), settings);
        using var gate = new SemaphoreSlim(1, 1);
        int slotSize = expected[0].Length;
        var memory = SharedMemoryBuffer.Create($"beutl-video-prefetch-{Guid.NewGuid():N}", slotSize * 4);
        using var ring = new VideoRingBuffer(4, slotSize, memory, reader, 1, gate, () => 1);

        long Read(int frame)
        {
            RingBufferReadResult result = ring.ReadFrame(frame);
            Assert.That(result.Success, Is.True, $"frame {frame}");
            Assert.That(result.NewSharedMemoryName, Is.Null);
            AssertPixels(result.SlotDataOffset, frame);
            return result.SlotDataOffset;
        }

        void AssertPixels(long offset, int frame)
        {
            byte* data = memory.AcquirePointer();
            try
            {
                Assert.That(new ReadOnlySpan<byte>(data + offset, slotSize).SequenceEqual(expected[frame]),
                    Is.True, $"shared pixels at frame {frame}");
            }
            finally { memory.ReleasePointer(); }
        }

        void WaitForPrefetch(params int[] frames)
        {
            bool ready = SpinWait.SpinUntil(() =>
            {
                if (!gate.Wait(TimeSpan.FromMilliseconds(50)))
                    return false;
                byte* data = memory.AcquirePointer();
                try
                {
                    foreach (int frame in frames)
                    {
                        bool found = false;
                        for (int slot = 0; slot < 4; slot++)
                            found |= new ReadOnlySpan<byte>(data + slot * slotSize, slotSize).SequenceEqual(expected[frame]);
                        if (!found) return false;
                    }

                    return true;
                }
                finally { memory.ReleasePointer(); gate.Release(); }
            }, TimeSpan.FromSeconds(5));
            Assert.That(ready, Is.True, $"prefetched frames {string.Join(", ", frames)}");
        }

        Read(0);
        long served = Read(2);
        WaitForPrefetch(4, 6, 8);
        AssertPixels(served, 2); // prefetch must not overwrite the host's current frame
        Read(3); // a different rate fills the missing frames and resumes contiguous prefetch
        WaitForPrefetch(4, 5, 6);
        Read(5);
        Read(149);
        Assert.That(ring.ReadFrame(150).Success, Is.False);
        Read(147);
        Read(148);
        WaitForPrefetch(149);
        Read(149);
        Read(0);
    }

    private static void AssertSeeksMatch(string path, int threads)
    {
        var settings = new FFmpegDecodingSettings();
        settings.ThreadCount.Value = threads;
        List<byte[]> expected = ReadSequentialFrames(path, settings);
        using var seeking = new FFmpegReader(path, new MediaOptions(MediaMode.Video), settings);
        int last = expected.Count - 1;
        // Cross GOP boundaries, reverse direction, repeat a frame, then resume forward reads.
        int[] targets = [last - 2, last - 1, last, last / 2, last / 2 + 1, 1, 2, 0, 0, last];
        foreach (int frame in targets)
        {
            Assert.That(seeking.ReadVideo(frame, out var image), Is.True, $"seek frame {frame}");
            using (image)
                Assert.That(image!.Value.GetPixelSpan().SequenceEqual(expected[frame]), Is.True, $"pixels at frame {frame}");
        }

        Assert.That(seeking.ReadVideo(expected.Count, out _), Is.False, "EOF");
        var pixels = new byte[expected[0].Length];
        for (int frame = last / 3; frame < last / 3 + 3; frame++)
        {
            Assert.That(seeking.ReadVideo(frame, pixels, out _), Is.True, $"recovery frame {frame}");
            Assert.That(pixels.AsSpan().SequenceEqual(expected[frame]), Is.True, $"recovery pixels at frame {frame}");
        }
    }

    private static List<byte[]> ReadSequentialFrames(string path, FFmpegDecodingSettings settings)
    {
        var expected = new List<byte[]>();
        using (var sequential = new FFmpegReader(path, new MediaOptions(MediaMode.Video), settings))
        {
            for (int frame = 0; frame < sequential.VideoInfo.NumFrames; frame++)
            {
                Assert.That(sequential.ReadVideo(frame, out var image), Is.True, $"reference frame {frame}");
                using (image)
                    expected.Add(image!.Value.GetPixelSpan().ToArray());
            }
        }

        return expected;
    }
}
