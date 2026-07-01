using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.FFmpegWorker.Tests;

// A cancelled Encode must throw OperationCanceledException (both callers already catch it). This
// pins the shared contract on the FFmpeg-native backend: FFmpegEncodingController got the identical
// cancellationToken.ThrowIfCancellationRequested() fix as AVFEncodingController (PR #1894) but had
// no test of its own. Unlike AVFoundation (macOS-only), FFmpeg runs wherever its native libraries
// are present, so this fixture guards on native availability (Assert.Ignore) rather than a fixed
// [Platform]: it exercises the real cancellation path where FFmpeg is installed and self-skips
// where it is not.
[TestFixture]
public class EncodingCancellationTests
{
    // FFmpegLoaderWorker.Initialize() throws FFmpegLibrariesNotFoundException (or another native-load
    // error) when the shared libraries are absent. Probe once; the FFmpeg-native tests self-skip if
    // the natives cannot be loaded on this machine.
    private static readonly Lazy<bool> s_ffmpegAvailable = new(() =>
    {
        try
        {
            FFmpegLoaderWorker.Initialize();
            return true;
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            // A bad architecture / missing transitive dependency also means the FFmpeg-native path
            // cannot run here — self-skip rather than fail the suite on machines without FFmpeg.
            return false;
        }
    });

    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-cancel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void EncodeSurfacesCancellationAsOperationCanceledException()
    {
        if (!s_ffmpegAvailable.Value)
        {
            Assert.Ignore("FFmpeg native libraries are not available; skipping FFmpeg-native cancellation test.");
        }

        string outputPath = Path.Combine(_workDir, "cancelled.mp4");
        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());

        const int width = 64;
        const int height = 64;
        const int sampleRate = 44100;
        const int frameCount = 30;
        const int frameRateNum = 30;
        const int frameRateDen = 1;

        controller.VideoSettings.DestinationSize = new PixelSize(width, height);
        controller.VideoSettings.SourceSize = new PixelSize(width, height);
        controller.VideoSettings.FrameRate = new Rational(frameRateNum, frameRateDen);

        controller.AudioSettings.SampleRate = sampleRate;
        controller.AudioSettings.Channels = 2;

        using var cts = new CancellationTokenSource();
        // Cancel mid-clip (after one frame) so the controller exits its loop on cancellation and
        // reaches the ThrowIfCancellationRequested() guard rather than completing the encode.
        var frameProvider = new CancelAfterFirstFrameProvider(
            cts, frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        var sampleProvider = new SineSampleProvider(sampleRate, sampleRate);

        Assert.That(
            async () => await controller.Encode(frameProvider, sampleProvider, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // Cancels the token right after the first frame, so the loop's next iteration sees cancellation.
    private sealed class CancelAfterFirstFrameProvider(
        CancellationTokenSource cts, long frameCount, Rational frameRate, int width, int height)
        : IFrameProvider
    {
        private readonly GradientFrameProvider _inner = new(frameCount, frameRate, width, height);

        public long FrameCount => _inner.FrameCount;

        public Rational FrameRate => _inner.FrameRate;

        public async ValueTask<Bitmap> RenderFrame(long frame)
        {
            Bitmap bitmap = await _inner.RenderFrame(frame);
            cts.Cancel();
            return bitmap;
        }

        public void Dispose() => _inner.Dispose();
    }

    // Deterministic 64x64 BGRA gradient per frame — a minimal valid frame source for the encoder.
    private sealed class GradientFrameProvider(long frameCount, Rational frameRate, int width, int height)
        : IFrameProvider
    {
        public long FrameCount { get; } = frameCount;

        public Rational FrameRate { get; } = frameRate;

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            var bitmap = new Bitmap(width, height);
            unsafe
            {
                byte* pixels = (byte*)bitmap.Data;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * bitmap.RowBytes) + (x * 4);
                        pixels[i + 0] = (byte)(x * 4 & 0xFF);       // B
                        pixels[i + 1] = (byte)(y * 4 & 0xFF);       // G
                        pixels[i + 2] = (byte)((frame * 8) & 0xFF); // R varies per frame
                        pixels[i + 3] = 0xFF;                       // A
                    }
                }
            }
            return ValueTask.FromResult(bitmap);
        }

        public void Dispose()
        {
            // Stateless test double — no prefetch or owned resources to drain.
        }
    }

    // 440 Hz sine wave, Stereo 32-bit float interleaved.
    private sealed class SineSampleProvider(long sampleCount, long sampleRate) : ISampleProvider
    {
        public long SampleCount { get; } = sampleCount;

        public long SampleRate { get; } = sampleRate;

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
        {
            var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
            var span = pcm.DataSpan;
            const float frequency = 440f;
            float twoPiFOverSr = 2f * MathF.PI * frequency / SampleRate;
            for (int i = 0; i < length; i++)
            {
                float t = MathF.Sin((offset + i) * twoPiFOverSr) * 0.25f;
                span[i] = new Stereo32BitFloat(t, t);
            }
            return ValueTask.FromResult(pcm);
        }

        public void Dispose()
        {
            // Stateless test double — no prefetch or owned resources to drain.
        }
    }
}
