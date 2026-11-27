using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Starts the real <c>Beutl.FFmpegWorker</c> and pins that <c>ReadVideo</c> on an audio-only file throws
/// <see cref="FFmpegWorkerException"/>. Skips when the worker or FFmpeg is missing.
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyReadVideoContractTests
{
    private string _audioOnlyPath = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _audioOnlyPath = Path.Combine(
            Path.GetTempPath(), $"beutl-ffmpeg-audioonly-{Guid.NewGuid():N}.wav");
        TestWav.WriteSilentPcmWav(_audioOnlyPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (_audioOnlyPath != null && File.Exists(_audioOnlyPath))
                File.Delete(_audioOnlyPath);
        }
        catch (Exception)
        {
            // Best-effort cleanup of the temp fixture; a leftover file must not fail the run.
        }
    }

    [Test]
    public void ReadVideo_OnAudioOnlyFile_ThrowsFFmpegWorkerException()
    {
        // Skips only when FFmpeg is missing; once the worker is up, a failed Open is a regression.
        DecodingWorkerTestHelpers.RequireDecodingWorker();

        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());

        using MediaReader? reader = decoderInfo.Open(_audioOnlyPath, new MediaOptions(MediaMode.AudioVideo));
        Assert.That(
            reader, Is.Not.Null,
            "the worker started with FFmpeg natives loaded, so Open must succeed; a null result is a real OpenFile/proxy regression, not an unavailable prerequisite");

        // Sanity: the fixture must be audio-only, so the reader exercises the HandleReadVideo
        // audio-only error branch (not the "unknown reader" or ring-buffer paths).
        Assert.Multiple(() =>
        {
            Assert.That(reader!.HasAudio, Is.True, "the WAV fixture must expose an audio stream");
            Assert.That(reader.HasVideo, Is.False, "the WAV fixture must be audio-only (no video stream)");
        });

        Assert.That(
            () => reader!.ReadVideo(0, out _),
            Throws.TypeOf<FFmpegWorkerException>(),
            "ReadVideo on an audio-only reader must surface the worker's CreateError as a thrown FFmpegWorkerException, not a silent false");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThumbnailReadReturnsReducedPixelsButKeepsTheSourceDimensions(bool deinterlace)
    {
        DecodingWorkerTestHelpers.RequireDecodingWorker();
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "deinterlace-tff.mp4");
        var decoder = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        using MediaReader? reader = decoder.Open(path, new MediaOptions(MediaMode.Video)
        {
            VideoThumbnailHeight = 24,
            Deinterlace = deinterlace
                ? new(DeinterlaceMode.On, DeinterlaceFieldOrder.Auto, DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field)
                : default,
        });
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.VideoInfo.FrameSize.Height, Is.GreaterThan(24));
        Assert.That(reader.VideoInfo.DeinterlaceStatus, Is.EqualTo(
            deinterlace ? DeinterlaceApplicationStatus.Applied : DeinterlaceApplicationStatus.Off));
        for (int frame = 0; frame < 5; frame++)
        {
            Assert.That(reader.ReadVideoThumbnail(frame, allowKeyframe: deinterlace, out var image), Is.True);
            using (image)
            {
                Assert.That(image!.Value.Height, Is.EqualTo(24));
                Assert.That(image.Value.Width, Is.EqualTo((int)Math.Round(24d * reader.VideoInfo.FrameSize.Width / reader.VideoInfo.FrameSize.Height)));
            }
        }
    }
}
