using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensions.AVFoundation.Tests;

// Deterministic 64x64 RGBA gradient per frame — lets us both create an input clip and later
// verify we decoded roughly what we encoded (exact match is not guaranteed because H.264 is lossy).
internal sealed class GradientFrameProvider(long frameCount, Rational frameRate, int width, int height)
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
                    pixels[i + 0] = (byte)(x * 4 & 0xFF);                      // B
                    pixels[i + 1] = (byte)(y * 4 & 0xFF);                      // G
                    pixels[i + 2] = (byte)((frame * 8) & 0xFF);                // R varies per frame
                    pixels[i + 3] = 0xFF;                                      // A
                }
            }
        }
        return ValueTask.FromResult(bitmap);
    }
}

// 440 Hz sine wave, Stereo 32-bit float interleaved.
internal sealed class SineSampleProvider(long sampleCount, long sampleRate) : ISampleProvider
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
}
