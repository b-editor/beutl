using System.Diagnostics.CodeAnalysis;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;

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
        long startIndex = context.TimeRange.Start.ToSamples(sampleRate, MidpointRounding.ToZero);
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
        long startIndex = context.TimeRange.Start.ToSamples(sampleRate, MidpointRounding.ToZero);
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
    public static KeyFrameAnimation<float> Ramp(float from, float to, MediaTime end)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Easing = { Value = new LinearEasing() }, Value = { Value = from }, KeyTime = { Value = MediaTime.Zero } });
        animation.KeyFrames.Add(new KeyFrame<float> { Easing = { Value = new LinearEasing() }, Value = { Value = to }, KeyTime = { Value = end } });
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

        property.Value = max + 1000f;
        Assert.That(property.Value, Is.EqualTo(max),
            $"{expectedName}: a value above the max must be coerced to {max}; validator appears unwired.");

        property.Value = min - 1000f;
        Assert.That(property.Value, Is.EqualTo(min),
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
    public static IExpressionProperty<float> Ramp(string name, float from, float to, double seconds)
    {
        var property = Property.CreateAnimatable(from);
        property.SetAttributes(name, []);
        property.SetKeyFrameAnimation(AudioTestAnimations.Ramp(from, to, MediaTime.FromSeconds(seconds)));
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
        MediaTime start, MediaTime duration, int sampleRate = AudioTestBuffers.DefaultSampleRate)
        => new(new TimeRange(start, duration), sampleRate, new AnimationSampler(), null);

    /// <summary>Lasts exactly <paramref name="count"/> samples.</summary>
    public static AudioProcessContext Exact(MediaTime start, int count, int sampleRate)
        => CreateContext(start, ExactDuration(count, sampleRate), sampleRate);

    public static MediaTime ExactDuration(int count, int sampleRate)
        => AudioProcessContext.GetDurationForSampleCount(count, sampleRate);

    /// <summary>
    /// Starts at the tick rounded up from sample <paramref name="startSample"/> and lasts exactly
    /// <paramref name="count"/> samples.
    /// </summary>
    public static AudioProcessContext AtSample(int startSample, int count, int sampleRate, MediaTime? end = null)
        => new(new TimeRange(
                MediaTime.FromTimeSpan(TimeSpan.FromTicks((long)Math.Ceiling(startSample * (double)TimeSpan.TicksPerSecond / sampleRate))),
                ExactDuration(count, sampleRate)),
            sampleRate, new AnimationSampler(), null)
        {
            ProcessEndTime = end
        };

    /// <summary>Lasts <c>count / sampleRate</c> seconds.</summary>
    public static AudioProcessContext Seconds(MediaTime start, int count, int sampleRate)
        => CreateContext(start, MediaTime.FromSeconds((double)count / sampleRate), sampleRate);
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
        (float Left, float Right) value = default;
        WithTemporaryWave(sampleRate, frames, (ch, i) =>
        {
            if (ch == 0) value = frame(i);
            return ch == 0 ? value.Left : value.Right;
        }, body);
    }

    /// <summary>Writes native float channels; a non-null mask selects WAVE_FORMAT_EXTENSIBLE.</summary>
    public static void WithTemporaryWave(
        int sampleRate, int frames, Func<int, int, float> sample, Action<SoundSource> body,
        int channelCount = 2, AudioSpeakers? mask = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-audio-test-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int formatSize = mask.HasValue ? 40 : 16;
                int dataSize = frames * channelCount * sizeof(float);
                writer.Write("RIFF"u8);
                writer.Write(20 + formatSize + dataSize);
                writer.Write("WAVEfmt "u8);
                writer.Write(formatSize);
                writer.Write((ushort)(mask.HasValue ? 0xFFFE : 3));
                writer.Write((ushort)channelCount);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channelCount * sizeof(float));
                writer.Write((ushort)(channelCount * sizeof(float)));
                writer.Write((ushort)32);
                if (mask.HasValue)
                {
                    writer.Write((ushort)22);
                    writer.Write((ushort)32);
                    writer.Write((uint)mask.Value);
                    writer.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());
                }
                writer.Write("data"u8);
                writer.Write(dataSize);
                for (int i = 0; i < frames; i++)
                    for (int ch = 0; ch < channelCount; ch++)
                        writer.Write(sample(ch, i));
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

internal sealed class AudioTestReader(AudioBuffer samples, AudioChannelLayout layout) : MediaReader
{
    public bool FailReads { get; set; }

    public override AudioStreamInfo AudioInfo { get; } = new(
        "test", new Rational(samples.SampleCount, samples.SampleRate), samples.SampleRate, samples.ChannelCount)
    {
        ChannelLayout = layout
    };

    public override VideoStreamInfo VideoInfo => throw new NotSupportedException();

    public override bool HasAudio => true;

    public override bool HasVideo => false;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
    {
        sound = null;
        if (FailReads) return false;
        int count = Math.Min(length, Math.Max(0, samples.SampleCount - start));
        sound = new AudioBuffer(samples.SampleRate, samples.ChannelCount, count);
        for (int ch = 0; ch < samples.ChannelCount; ch++)
            samples.GetChannelData(ch).Slice(Math.Min(start, samples.SampleCount), count).CopyTo(sound.GetChannelData(ch));
        return true;
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        return false;
    }
}
