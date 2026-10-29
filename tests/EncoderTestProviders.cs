using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Media;

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

// Primaries expose a wrong YUV matrix, gray exposes a wrong transfer, and black/white expose a wrong range.
internal sealed class ColorBarsFrameProvider : IFrameProvider
{
    public const int Width = 192;
    public const int Height = 64;
    private long _next;
    public long FrameCount => 3;
    public Rational FrameRate => new(30, 1);

    public static Bitmap CreateFrame()
    {
        var bitmap = new Bitmap(Width, Height);
        byte[][] colors = [[0, 0, 255], [0, 255, 0], [255, 0, 0], [128, 128, 128], [0, 0, 0], [255, 255, 255]];
        for (int y = 0; y < Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < Width; x++)
            {
                colors[x / 32].AsSpan().CopyTo(row.Slice(x * 4, 3));
                row[x * 4 + 3] = 255;
            }
        }
        return bitmap;
    }

    public ValueTask<Bitmap?> ReadNextFrame() => ValueTask.FromResult<Bitmap?>(_next++ < FrameCount ? CreateFrame() : null);

    public void Dispose() { }
}

// 440 Hz sine wave in stereo; the right channel is scaled by rightGain.
internal sealed class SineSampleProvider(long sampleCount, long sampleRate, float rightGain = 1) : ISampleProvider
{
    public long SampleCount { get; } = sampleCount;

    public long SampleRate { get; } = sampleRate;

    private long _nextSample;

    public ValueTask<AudioBuffer?> ReadNextSamples(int length)
    {
        long offset = _nextSample;
        if (offset >= SampleCount)
            return ValueTask.FromResult<AudioBuffer?>(null);

        _nextSample += length;
        var audio = new AudioBuffer((int)SampleRate, 2, length);
        Span<float> left = audio.GetChannelData(0);
        Span<float> right = audio.GetChannelData(1);
        const float frequency = 440f;
        float twoPiFOverSr = 2f * MathF.PI * frequency / SampleRate;
        for (int i = 0; i < Math.Min(length, SampleCount - offset); i++)
        {
            float t = MathF.Sin((offset + i) * twoPiFOverSr) * 0.25f;
            left[i] = t;
            right[i] = t * rightGain;
        }
        return ValueTask.FromResult<AudioBuffer?>(audio);
    }

    public void Dispose()
    {
        // No prefetch or owned resources to drain.
    }
}
