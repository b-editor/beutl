using System.Diagnostics.CodeAnalysis;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class EqualizerNodeTests
{
    private const int SampleRate = 48000;
    private const int ChunkSamples = SampleRate / 10;

    private static readonly MediaTime s_chunkDuration =
        MediaTime.FromSeconds(ChunkSamples / (double)SampleRate);

    private static EqualizerNode CreateNode()
    {
        var band = new EqualizerBand();
        band.Frequency.Value = 1000f;
        band.Gain.Value = 12f;
        band.Q.Value = 1f;
        return new EqualizerNode { Bands = [band] };
    }

    private static AudioProcessContext CreateContext(MediaTime start) =>
        AudioTestContexts.CreateContext(start, s_chunkDuration, SampleRate);

    private static float FirstSampleAfterWarmup(EqualizerNode node, MediaTime followStart)
    {
        using var warmupInput = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        node.Process(CreateContext(MediaTime.Zero)).Dispose();

        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var followOutput = node.Process(CreateContext(followStart));
        return followOutput.GetChannelData(0)[0];
    }

    private static float FirstSampleFromFreshNode()
    {
        using var node = CreateNode();
        using var input = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(MediaTime.Zero));
        return output.GetChannelData(0)[0];
    }

    [Test]
    public void Process_FilterTypeThatChangesOverTime_FollowsTheNewType()
    {
        var band = new EqualizerBand();
        band.FilterType.Expression = new FilterTypeStep(MediaTime.FromSeconds(0.05));
        using var node = new EqualizerNode { Bands = [band] };
        using var input = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(input));

        using AudioBuffer output = node.Process(CreateContext(MediaTime.Zero));

        float[] samples = output.GetChannelData(0).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(samples[SampleRate / 25], Is.EqualTo(0.9f).Within(0.01f), "A low-pass keeps a constant input.");
            Assert.That(samples[^1], Is.EqualTo(0f).Within(0.01f), "A high-pass removes it.");
            Assert.That(samples, Has.All.Matches<float>(float.IsFinite));
        });
    }

    private sealed class FilterTypeStep(MediaTime step) : IExpression<BiQuadFilterType>
    {
        public string ExpressionString => string.Empty;

        public BiQuadFilterType Evaluate(ExpressionContext context)
            => context.Time < step ? BiQuadFilterType.LowPass : BiQuadFilterType.HighPass;

        public bool Validate([NotNullWhen(false)] out string? error)
        {
            error = null;
            return true;
        }
    }

    // One-tick rounding must preserve IIR state to avoid restarting the biquad.
    [TestCase(-1L)]
    [TestCase(1L)]
    public void Process_OneTickBoundaryRounding_PreservesFilterState(long tickOffset)
    {
        using var node = CreateNode();
        float continuing = FirstSampleAfterWarmup(node, s_chunkDuration + MediaTime.FromTimeSpan(TimeSpan.FromTicks(tickOffset)));

        Assert.That(continuing, Is.Not.EqualTo(FirstSampleFromFreshNode()).Within(1e-6f),
            "A one-tick boundary rounding must not reset the filter state.");
    }

    [Test]
    public void Process_Seek_ResetsFilterState()
    {
        using var node = CreateNode();
        float afterSeek = FirstSampleAfterWarmup(node, MediaTime.FromSeconds(5));

        Assert.That(afterSeek, Is.EqualTo(FirstSampleFromFreshNode()).Within(1e-6f),
            "A seek must reset the filter state, so the first sample matches a fresh node's.");
    }
}
