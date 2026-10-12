using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using NAudio.Wave;

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
    public List<(long Start, int Count)> RequestedRanges { get; } = [];

    public override AudioBuffer Process(AudioProcessContext context)
    {
        int count = context.GetSampleCount();
        var buffer = new AudioBuffer(sampleRate, 2, count);
        Span<float> left = buffer.GetChannelData(0);
        Span<float> right = buffer.GetChannelData(1);
        long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
        RequestedRanges.Add((startIndex, count));
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
/// A 200 Hz, 0.25-amplitude stereo sine keyed off the requested absolute sample position.
/// </summary>
internal sealed class AbsoluteSineNode(int sampleRate) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
    {
        int count = context.GetSampleCount();
        var buffer = new AudioBuffer(sampleRate, 2, count);
        long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
        for (int ch = 0; ch < 2; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < count; i++)
            {
                data[i] = 0.25f * MathF.Sin(2f * MathF.PI * 200f * (startIndex + i) / sampleRate);
            }
        }

        return buffer;
    }
}

/// <summary>
/// A silent stereo source reporting a fixed latency; <paramref name="overrideTotal"/> also reports it as the
/// total latency.
/// </summary>
internal sealed class FixedLatencyNode(int latencySamples, bool overrideTotal = false) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
        => new(context.SampleRate, 2, context.GetSampleCount());

    public override int GetLatencySamples(int sampleRate) => latencySamples;

    public override int GetTotalLatencySamples(int sampleRate)
        => overrideTotal ? latencySamples : base.GetTotalLatencySamples(sampleRate);
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

internal static class AudioTestAnimations
{
    /// <summary>A two-keyframe linear animation from <paramref name="from"/> at zero to <paramref name="to"/> at <paramref name="end"/>.</summary>
    public static KeyFrameAnimation<float> Ramp(float from, float to, TimeSpan end)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = from, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = to, KeyTime = end });
        return animation;
    }
}

internal static class AudioEffectAssertions
{
    // ScanProperties must name each property after its CLR member and attach the [Range] validator,
    // so out-of-range values are clamped at the engine layer, not just in the UI.
    public static void AssertNameAndRange(IProperty<float> property, string expectedName, float min, float max)
    {
        Assert.That(property.Name, Is.EqualTo(expectedName),
            $"ScanProperties must name the property '{expectedName}'.");

        property.CurrentValue = max + 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(max),
            $"{expectedName}: a value above the max must be coerced to {max}; validator appears unwired.");

        property.CurrentValue = min - 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(min),
            $"{expectedName}: a value below the min must be coerced to {min}; validator appears unwired.");
    }

    // Catches an edit that puts a default outside its range or inverts a min/max, at CI time.
    public static void AssertRangeIsConsistent(float min, float def, float max)
    {
        Assert.That(min, Is.LessThan(max), "Min must be strictly less than Max.");
        Assert.That(def, Is.InRange(min, max), "Default must lie within [Min, Max].");
    }
}

internal static class AudioTestLevels
{
    /// <summary>
    /// Peak level in dB (-100 for silence) of up to <paramref name="width"/> samples from <paramref name="start"/>,
    /// over every channel or only <paramref name="channel"/>.
    /// </summary>
    public static float PeakDb(AudioBuffer buffer, int start, int width = int.MaxValue, int? channel = null)
    {
        int end = start + Math.Min(width, buffer.SampleCount - start);
        int firstChannel = channel ?? 0;
        int endChannel = channel is { } only ? only + 1 : buffer.ChannelCount;
        float peak = 0f;
        for (int ch = firstChannel; ch < endChannel; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = start; i < end; i++)
            {
                float a = MathF.Abs(data[i]);
                if (a > peak) peak = a;
            }
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }
}

internal static class AudioTestProperties
{
    /// <summary>
    /// An animatable property named <paramref name="name"/> with a linear ramp from <paramref name="from"/> at zero
    /// to <paramref name="to"/> at <paramref name="seconds"/>.
    /// </summary>
    public static IProperty<float> Ramp(string name, float from, float to, double seconds)
    {
        var property = Property.CreateAnimatable(from);
        property.SetAttributes(name, []);
        property.Animation = AudioTestAnimations.Ramp(from, to, TimeSpan.FromSeconds(seconds));
        return property;
    }
}

/// <summary>A linear easing that does not override <see cref="Easing.TryGetOutputRange"/>, so consumers treat it as unbounded.</summary>
internal sealed class UnknownRangeEasing : Easing
{
    public override float Ease(float progress) => progress;
}

/// <summary>
/// AudioProcessContext factories shared by the audio fixtures. They differ in how they round the start and the length,
/// so each fixture keeps the rounding it was written against.
/// </summary>
internal static class AudioTestContexts
{
    public static AudioProcessContext CreateContext(
        TimeSpan start, TimeSpan duration, int sampleRate = AudioTestBuffers.DefaultSampleRate)
        => new(new TimeRange(start, duration), sampleRate, new AnimationSampler(), null);

    /// <summary>Lasts exactly <paramref name="count"/> samples.</summary>
    public static AudioProcessContext Exact(TimeSpan start, int count, int sampleRate)
        => CreateContext(start, ExactDuration(count, sampleRate), sampleRate);

    public static TimeSpan ExactDuration(int count, int sampleRate)
        => AudioProcessContext.GetDurationForSampleCount(count, sampleRate);

    /// <summary>
    /// Starts at the tick rounded up from sample <paramref name="startSample"/> and lasts exactly
    /// <paramref name="count"/> samples.
    /// </summary>
    public static AudioProcessContext AtSample(int startSample, int count, int sampleRate, TimeSpan? end = null)
        => new(new TimeRange(
                TimeSpan.FromTicks((long)Math.Ceiling(startSample * (double)TimeSpan.TicksPerSecond / sampleRate)),
                ExactDuration(count, sampleRate)),
            sampleRate, new AnimationSampler(), null)
        {
            ProcessEndTime = end
        };

    /// <summary>Lasts <c>count / sampleRate</c> seconds.</summary>
    public static AudioProcessContext Seconds(TimeSpan start, int count, int sampleRate)
        => CreateContext(start, TimeSpan.FromSeconds((double)count / sampleRate), sampleRate);
}

internal static class AudioTestWaves
{
    /// <summary>
    /// Writes a stereo float WAV of <paramref name="frames"/> frames from <paramref name="frame"/>, and runs
    /// <paramref name="body"/> with a source reading it while the WAV decoder is registered.
    /// </summary>
    public static void WithTemporaryWave(
        int sampleRate, int frames, Func<int, (float Left, float Right)> frame, Action<SoundSource> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-audio-test-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2)))
            {
                for (int i = 0; i < frames; i++)
                {
                    (float left, float right) = frame(i);
                    writer.WriteSample(left);
                    writer.WriteSample(right);
                }
            }

            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            body(source);
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }
}
