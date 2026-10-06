using System.Runtime.Versioning;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture, NonParallelizable]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationFallbackIntegrationTests
{
    private string _workDir = string.Empty;
    private IDecoderInfo[] _previousDecoders = [];
    private TrackingDecoder _mf = null!;
    private TrackingDecoder _ffmpeg = null!;

    [OneTimeSetUp]
    public void EnsureWorkerAvailable()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
        {
            Assert.Ignore("FFmpeg worker binary is unavailable.");
        }

        try
        {
            FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives are unavailable: {ex.Message}");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-mf-fallback-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        _previousDecoders = DecoderRegistry.EnumerateDecoder().ToArray();
        foreach (IDecoderInfo decoder in _previousDecoders)
        {
            DecoderRegistry.Unregister(decoder);
        }

        _mf = new TrackingDecoder(new MFDecoderInfo(new MFDecodingExtension()));
        _ffmpeg = new TrackingDecoder(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
        DecoderRegistry.Register(_mf);
        DecoderRegistry.Register(_ffmpeg);
    }

    [TearDown]
    public async Task TearDown()
    {
        DecoderRegistry.Unregister(_mf);
        DecoderRegistry.Unregister(_ffmpeg);
        foreach (IDecoderInfo decoder in _previousDecoders)
        {
            DecoderRegistry.Register(decoder);
        }

        // FFmpegReaderProxy closes the worker's reader asynchronously; wait for its file handle.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_workDir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50);
            }
        }
    }

    [TestCase(".mp4")]
    [TestCase(".m4v")]
    [TestCase(".M4V")]
    [TestCase(".asf")]
    [TestCase(".3gp")]
    [TestCase(".3gp2")]
    [TestCase(".3gpp")]
    public void Open_WhenMediaFoundationRejectsByteStream_FallsBackToFFmpegAndDecodesFrame(string extension)
    {
        // Deliberately use a Media Foundation extension for FLV data: MF cannot recognize the byte
        // stream, while FFmpeg probes the contents and can decode the H.264 video.
        string file = Path.Combine(_workDir, "video" + extension);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mf-unsupported.flv"), file);

        using MediaReader reader = MediaReader.Open(file, new MediaOptions(MediaMode.Video));

        Assert.Multiple(() =>
        {
            Assert.That(_mf.OpenCount, Is.EqualTo(1));
            Assert.That(_mf.LastOpenReturnedNull, Is.True);
            Assert.That(_ffmpeg.OpenCount, Is.EqualTo(1));
            Assert.That(reader, Is.TypeOf<FFmpegReaderProxy>());
            Assert.That(reader.HasVideo, Is.True);
        });

        bool decoded = reader.ReadVideo(0, out var image);
        using (image)
        {
            Assert.That(decoded, Is.True);
            Assert.That(image, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(image!.Value.Width, Is.EqualTo(64));
                Assert.That(image.Value.Height, Is.EqualTo(64));
            });
        }
    }

    [Test]
    public void Open_WhenAllCandidatesFail_ThrowsOnlyAfterTryingBothDecoders()
    {
        string file = Path.Combine(_workDir, "unsupported.mp4");
        File.WriteAllText(file, "This is not a recognized media byte stream.");

        MediaReader? unexpectedReader = null;
        try
        {
            Assert.That(() => unexpectedReader = MediaReader.Open(file, new MediaOptions(MediaMode.Video)),
                Throws.TypeOf<UnsupportedMediaException>());
        }
        finally
        {
            unexpectedReader?.Dispose();
        }
        Assert.Multiple(() =>
        {
            Assert.That(_mf.OpenCount, Is.EqualTo(1));
            Assert.That(_mf.LastOpenReturnedNull, Is.True);
            Assert.That(_ffmpeg.OpenCount, Is.EqualTo(1));
            Assert.That(_ffmpeg.LastOpenReturnedNull, Is.True);
        });
    }

    private sealed class TrackingDecoder(IDecoderInfo inner) : IDecoderInfo
    {
        public int OpenCount { get; private set; }

        public bool LastOpenReturnedNull { get; private set; }

        public string Name => inner.Name;

        public bool IsSupported(string file) => inner.IsSupported(file);

        public IEnumerable<string> AudioExtensions() => inner.AudioExtensions();

        public IEnumerable<string> VideoExtensions() => inner.VideoExtensions();

        public MediaReader? Open(string file, MediaOptions options)
        {
            OpenCount++;
            MediaReader? reader = inner.Open(file, options);
            LastOpenReturnedNull = reader is null;
            return reader;
        }
    }
}
