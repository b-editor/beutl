using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class EncodingOutputFormatTests
{
    private string _workDir = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            Assert.Ignore("FFmpeg native libraries are not available.");
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("FFmpeg native library dependencies are not available.");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-format-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [TestCase("unknown.beutl-unrecognized-format", false)]
    [TestCase("unknown.beutl-unrecognized-format", true)]
    [TestCase("missing-extension", false)]
    [TestCase("missing-extension", true)]
    public async Task Encode_UnrecognizedOutputFormat_FailsBeforeOpeningOutputFile(string fileName, bool existingOutput)
    {
        string outputPath = Path.Combine(_workDir, fileName);
        byte[] originalContents = [0x42, 0x65, 0x75, 0x74, 0x6c];
        if (existingOutput)
        {
            File.WriteAllBytes(outputPath, originalContents);
        }

        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());
        using var frameProvider = new UnusedFrameProvider();
        using var sampleProvider = new UnusedSampleProvider();

        // GuessFormat can return a non-null wrapper around a null native format. The old
        // null-coalescing check let it reach MediaMuxer.Create and truncate the output file.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await controller.Encode(frameProvider, sampleProvider, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("Could not determine FFmpeg output format from the file extension:"));
            Assert.That(File.Exists(outputPath), Is.EqualTo(existingOutput));
            if (existingOutput)
            {
                Assert.That(File.ReadAllBytes(outputPath), Is.EqualTo(originalContents));
            }
        });
    }

    private sealed class UnusedFrameProvider : IFrameProvider
    {
        public long FrameCount => throw new AssertionException("Invalid output formats must be rejected before reading frames.");

        public Rational FrameRate => throw new AssertionException("Invalid output formats must be rejected before reading frames.");

        public ValueTask<Bitmap> RenderFrame(long frame) =>
            throw new AssertionException("Invalid output formats must be rejected before rendering frames.");

        public void Dispose()
        {
        }
    }

    private sealed class UnusedSampleProvider : ISampleProvider
    {
        public long SampleCount => throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public long SampleRate => throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length) =>
            throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public void Dispose()
        {
        }
    }
}
