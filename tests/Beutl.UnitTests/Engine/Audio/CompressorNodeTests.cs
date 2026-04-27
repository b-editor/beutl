using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class CompressorNodeTests
{
    private const int SampleRate = 48000;

    private sealed class StubSourceNode : AudioNode
    {
        public required AudioBuffer Buffer { get; init; }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var copy = new AudioBuffer(Buffer.SampleRate, Buffer.ChannelCount, Buffer.SampleCount);
            Buffer.CopyTo(copy);
            return copy;
        }
    }

    private static AudioBuffer CreateSineBuffer(float amplitude, float frequencyHz, int sampleCount, int channels = 2)
    {
        var buffer = new AudioBuffer(SampleRate, channels, sampleCount);
        for (int ch = 0; ch < channels; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = amplitude * MathF.Sin(2f * MathF.PI * frequencyHz * i / SampleRate);
            }
        }
        return buffer;
    }

    private static float PeakDb(AudioBuffer buffer, int startSample)
    {
        float peak = 0f;
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = startSample; i < buffer.SampleCount; i++)
            {
                float a = MathF.Abs(data[i]);
                if (a > peak) peak = a;
            }
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }

    [Test]
    public void Process_BelowThreshold_LeavesSignalUnchanged()
    {
        // Amplitude 0.05 ≈ -26 dB peak, well below the -20 dB threshold, so output should pass through.
        const int sampleCount = SampleRate / 2;
        var input = CreateSineBuffer(0.05f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(10f),
            Release = Property.CreateAnimatable(100f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(source);

        var ctx = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)),
            SampleRate,
            new AnimationSampler(),
            null);

        var output = node.Process(ctx);

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);

        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(0.5f));
    }

    [Test]
    public void Process_AboveThreshold_AppliesExpectedGainReduction()
    {
        // Steady 0.9 amplitude (~-0.92 dB) sine, threshold -20 dB, ratio 4:1, hard knee.
        // Steady-state gain reduction ≈ (-0.92 - -20) * (1 - 1/4) ≈ 14.3 dB.
        const int sampleCount = SampleRate;
        var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(source);

        var ctx = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
            SampleRate,
            new AnimationSampler(),
            null);

        var output = node.Process(ctx);

        // After the attack settles, the signal should sit around -15 dB.
        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        Assert.That(outputPeakDb, Is.LessThan(-12f));
        Assert.That(outputPeakDb, Is.GreaterThan(-18f));
    }

    [Test]
    public void Process_MakeupGain_RaisesOutputAboveReducedLevel()
    {
        const int sampleCount = SampleRate;
        var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(6f)
        };
        node.AddInput(source);

        var ctx = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
            SampleRate,
            new AnimationSampler(),
            null);

        var output = node.Process(ctx);

        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        // 6 dB makeup applied to ~-15 dB → ~-9 dB.
        Assert.That(outputPeakDb, Is.LessThan(-6f));
        Assert.That(outputPeakDb, Is.GreaterThan(-12f));
    }
}
