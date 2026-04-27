using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class LimiterNodeTests
{
    private const int SampleRate = 48000;
    private const int LookaheadMs = 5;

    private static AudioProcessContext CreateContext(int sampleCount)
    {
        var duration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var range = new TimeRange(TimeSpan.Zero, duration);
        return new AudioProcessContext(range, SampleRate, new AnimationSampler(), null);
    }

    private static AudioBuffer CreateBuffer(int channelCount, int sampleCount, Func<int, int, float> generator)
    {
        var buffer = new AudioBuffer(SampleRate, channelCount, sampleCount);
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

    private static LimiterNode CreateNode(
        float thresholdDb = -1f,
        float releaseMs = 50f,
        float lookaheadMs = LookaheadMs,
        float makeupGainDb = 0f)
    {
        return new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = Property.CreateAnimatable(lookaheadMs),
            MakeupGain = Property.CreateAnimatable(makeupGainDb),
        };
    }

    private sealed class StubInputNode(AudioBuffer buffer) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context) => buffer;
    }

    [Test]
    public void Process_BelowThreshold_PassesThroughUnchanged()
    {
        const int sampleCount = 4096;
        // Threshold = -1 dB ~= 0.891. Use peak 0.5 (~ -6 dB).
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // After the lookahead delay, the output should match the delayed input exactly
        // (no gain reduction applied because the signal stays below threshold).
        for (int ch = 0; ch < 2; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i - lookaheadSamples]).Within(1e-5f),
                    $"Channel {ch} sample {i} should pass through unchanged.");
            }
        }
    }

    [Test]
    public void Process_AboveThreshold_PeakDoesNotExceedThreshold()
    {
        const int sampleCount = 4096;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        // Hot sine well above threshold (peak 2.0 ~= +6 dB).
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // After the lookahead window has fully filled, every output sample must be
        // at or below threshold (modulo tiny float epsilon).
        const float epsilon = 1e-4f;
        for (int ch = 0; ch < 2; ch++)
        {
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(outData[i]), Is.LessThanOrEqualTo(thresholdLin + epsilon),
                    $"Channel {ch} sample {i} ({outData[i]}) exceeded threshold {thresholdLin}.");
            }
        }
    }

    [Test]
    public void Process_MakeupGain_AppliedToOutput()
    {
        const int sampleCount = 2048;
        const float makeupDb = 6f;
        float makeupLin = MathF.Pow(10f, makeupDb / 20f);

        // Signal stays well below threshold so only makeup gain is applied.
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        // Use a generous threshold so the limiter never engages.
        using var node = CreateNode(thresholdDb: 0f, makeupGainDb: makeupDb);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        for (int ch = 0; ch < 2; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                float expected = inData[i - lookaheadSamples] * makeupLin;
                Assert.That(outData[i], Is.EqualTo(expected).Within(1e-4f),
                    $"Channel {ch} sample {i} should equal input * makeup gain.");
            }
        }
    }

    [Test]
    public void Process_StereoLink_ReducesBothChannelsTogether()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        // Only the left channel exceeds the threshold; the right is well below.
        using var input = CreateBuffer(2, sampleCount, (ch, i) =>
        {
            float baseSig = MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
            return ch == 0 ? 2.0f * baseSig : 0.1f * baseSig;
        });

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // The right channel should have been attenuated proportionally even though it
        // never crossed the threshold on its own.
        var inR = input.GetChannelData(1);
        var outR = output.GetChannelData(1);

        // Find a region with a large left peak to confirm the right channel was scaled.
        bool foundReduction = false;
        const float epsilon = 1e-3f;
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            float originalRight = inR[i - lookaheadSamples];
            if (MathF.Abs(originalRight) < 0.05f) continue;

            if (MathF.Abs(outR[i]) < MathF.Abs(originalRight) - epsilon)
            {
                foundReduction = true;
                break;
            }
        }

        Assert.That(foundReduction, Is.True,
            "Right channel should be attenuated due to stereo-linked gain reduction.");

        // And the left channel must still respect the threshold.
        var outL = output.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(outL[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
        }
    }
}
