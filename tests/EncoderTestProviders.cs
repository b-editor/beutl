using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl;

// Frame and sample sources for the encoder tests. Beutl.FFmpegWorker.Tests and Beutl.Extensions.AVFoundation.Tests
// link this file.

// Deterministic BGRA gradient per frame — lets us both create an input clip and later
// verify we decoded roughly what we encoded (exact match is not guaranteed because H.264 is lossy).
internal sealed class GradientFrameProvider(long frameCount, Rational frameRate, int width, int height)
    : IFrameProvider
{
    private long _nextFrame;

    public long FrameCount { get; } = frameCount;

    public Rational FrameRate { get; } = frameRate;

    public ValueTask<Bitmap?> ReadNextFrame()
    {
        if (_nextFrame >= FrameCount)
            return ValueTask.FromResult<Bitmap?>(null);

        long frame = _nextFrame++;
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
        return ValueTask.FromResult<Bitmap?>(bitmap);
    }

    public void Dispose()
    {
        // No prefetch or owned resources to drain.
    }
}

// Cancels the token right after the first frame, so the encode loop's next iteration sees cancellation.
internal sealed class CancelAfterFirstFrameProvider(
    CancellationTokenSource cts, long frameCount, Rational frameRate, int width, int height)
    : IFrameProvider
{
    private readonly GradientFrameProvider _inner = new(frameCount, frameRate, width, height);

    public long FrameCount => _inner.FrameCount;

    public Rational FrameRate => _inner.FrameRate;

    public async ValueTask<Bitmap?> ReadNextFrame()
    {
        Bitmap? bitmap = await _inner.ReadNextFrame();
        cts.Cancel();
        return bitmap;
    }

    public void Dispose() => _inner.Dispose();
}

// Every frame is one opaque color at the given size.
internal sealed class SolidFrameProvider(PixelSize size, long frameCount = 1) : IFrameProvider
{
    private long _nextFrame;

    public long FrameCount => frameCount;

    public Rational FrameRate => new(30, 1);

    public static Bitmap CreateFrame(PixelSize size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        for (int y = 0; y < size.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < size.Width; x++)
            {
                row[x * 4] = 40;
                row[x * 4 + 1] = 160;
                row[x * 4 + 2] = 200;
                row[x * 4 + 3] = 255;
            }
        }

        return bitmap;
    }

    public ValueTask<Bitmap?> ReadNextFrame()
        => ValueTask.FromResult<Bitmap?>(_nextFrame++ < frameCount ? CreateFrame(size) : null);

    public void Dispose()
    {
    }
}

// 440 Hz sine wave, Stereo 32-bit float interleaved; the right channel is scaled by rightGain.
internal sealed class SineSampleProvider(long sampleCount, long sampleRate, float rightGain = 1) : ISampleProvider
{
    public long SampleCount { get; } = sampleCount;

    public long SampleRate { get; } = sampleRate;

    private long _nextSample;

    public ValueTask<Pcm<Stereo32BitFloat>?> ReadNextSamples(int length)
    {
        long offset = _nextSample;
        if (offset >= SampleCount)
            return ValueTask.FromResult<Pcm<Stereo32BitFloat>?>(null);

        _nextSample += length;
        var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, length);
        var span = pcm.DataSpan;
        const float frequency = 440f;
        float twoPiFOverSr = 2f * MathF.PI * frequency / SampleRate;
        for (int i = 0; i < Math.Min(length, SampleCount - offset); i++)
        {
            float t = MathF.Sin((offset + i) * twoPiFOverSr) * 0.25f;
            span[i] = new Stereo32BitFloat(t, t * rightGain);
        }
        return ValueTask.FromResult<Pcm<Stereo32BitFloat>?>(pcm);
    }

    public void Dispose()
    {
        // No prefetch or owned resources to drain.
    }
}
