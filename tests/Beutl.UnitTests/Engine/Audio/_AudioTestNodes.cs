using Beutl.Audio;
using Beutl.Audio.Graph;

namespace Beutl.UnitTests.Engine.Audio;

/// <summary>
/// Deterministic constant source: every channel is filled with <paramref name="value"/>. Shared by the
/// audio-node fixtures that just need a predictable input of a given shape.
/// </summary>
internal class ConstantInputNode(int sampleRate, int channels, int count, float value) : AudioNode
{
    /// <summary>
    /// Builds the constant buffer this node hands out. Exposed so subclasses (e.g. a capturing variant)
    /// can record the buffers they produce without duplicating the fill loop.
    /// </summary>
    protected AudioBuffer CreateConstantBuffer()
    {
        var buffer = new AudioBuffer(sampleRate, channels, count);
        for (int ch = 0; ch < channels; ch++)
        {
            buffer.GetChannelData(ch).Fill(value);
        }

        return buffer;
    }

    public override AudioBuffer Process(AudioProcessContext context) => CreateConstantBuffer();
}

/// <summary>
/// Deterministic stereo ramp keyed off the requested global sample index: the left channel is
/// <c>(startIndex + i) * 0.01f</c> and the right channel is its negation. Any change in how a downstream
/// node requests source ranges shows up in the output.
/// </summary>
internal sealed class RampInputNode(int sampleRate) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
    {
        int count = context.GetSampleCount();
        var buffer = new AudioBuffer(sampleRate, 2, count);
        Span<float> left = buffer.GetChannelData(0);
        Span<float> right = buffer.GetChannelData(1);
        long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
        for (int i = 0; i < count; i++)
        {
            float v = (startIndex + i) * 0.01f;
            left[i] = v;
            right[i] = -v;
        }

        return buffer;
    }
}

/// <summary>
/// Replays a fixed buffer, returning a fresh copy on every <see cref="Process"/> call. Nodes that emit
/// a new buffer dispose the input they consume, so the copy keeps the original alive for the test's own
/// assertions while still feeding a usable input each frame.
/// </summary>
internal sealed class BufferReplayNode(AudioBuffer buffer) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
    {
        var copy = new AudioBuffer(buffer.SampleRate, buffer.ChannelCount, buffer.SampleCount);
        buffer.CopyTo(copy);
        return copy;
    }
}

/// <summary>
/// Buffer factories shared by the audio-effect fixtures. The default sample rate matches the
/// effect-fixture convention (48 kHz); pass an explicit rate for the resampling tests.
/// </summary>
internal static class AudioTestBuffers
{
    public const int DefaultSampleRate = 48000;

    /// <summary>Fills each channel from <paramref name="generator"/> (channel, sampleIndex) -&gt; value.</summary>
    public static AudioBuffer CreateBuffer(
        int channelCount,
        int sampleCount,
        Func<int, int, float> generator,
        int sampleRate = DefaultSampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, channelCount, sampleCount);
        for (int ch = 0; ch < channelCount; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = generator(ch, i);
            }
        }

        return buffer;
    }

    /// <summary>A constant-amplitude buffer on every channel.</summary>
    public static AudioBuffer CreateConstantBuffer(
        float amplitude,
        int sampleCount,
        int channels = 2,
        int sampleRate = DefaultSampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, channels, sampleCount);
        for (int ch = 0; ch < channels; ch++)
        {
            buffer.GetChannelData(ch).Fill(amplitude);
        }

        return buffer;
    }

    /// <summary>A sine tone of the given amplitude and frequency on every channel.</summary>
    public static AudioBuffer CreateSineBuffer(
        float amplitude,
        float frequencyHz,
        int sampleCount,
        int channels = 2,
        int sampleRate = DefaultSampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, channels, sampleCount);
        for (int ch = 0; ch < channels; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = amplitude * MathF.Sin(2f * MathF.PI * frequencyHz * i / sampleRate);
            }
        }

        return buffer;
    }
}
