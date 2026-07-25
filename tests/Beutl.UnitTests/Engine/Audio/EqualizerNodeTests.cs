using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class EqualizerNodeTests
{
    private const int SampleRate = 48000;
    private const int ChunkSamples = SampleRate / 10;

    private static readonly TimeSpan s_chunkDuration =
        TimeSpan.FromSeconds(ChunkSamples / (double)SampleRate);

    private static EqualizerNode CreateNode()
    {
        var band = new EqualizerBand();
        band.Frequency.CurrentValue = 1000f;
        band.Gain.CurrentValue = 12f;
        band.Q.CurrentValue = 1f;
        return new EqualizerNode { Bands = [band] };
    }

    private static AudioProcessContext CreateContext(TimeSpan start) =>
        new(new TimeRange(start, s_chunkDuration), SampleRate, new AnimationSampler(), null);

    private static float FirstSampleAfterWarmup(EqualizerNode node, TimeSpan followStart)
    {
        using var warmupInput = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        node.Process(CreateContext(TimeSpan.Zero)).Dispose();

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
        using var output = node.Process(CreateContext(TimeSpan.Zero));
        return output.GetChannelData(0)[0];
    }

    // Independently rounded chunk boundaries can land one tick apart, which is not a seek: the IIR
    // state must survive, or the biquad restarts from zero and clicks.
    [TestCase(-1L)]
    [TestCase(1L)]
    public void Process_OneTickBoundaryRounding_PreservesFilterState(long tickOffset)
    {
        using var node = CreateNode();
        float continuing = FirstSampleAfterWarmup(node, s_chunkDuration + TimeSpan.FromTicks(tickOffset));

        Assert.That(continuing, Is.Not.EqualTo(FirstSampleFromFreshNode()).Within(1e-6f),
            "A one-tick boundary rounding must not reset the filter state.");
    }

    [Test]
    public void Process_Seek_ResetsFilterState()
    {
        using var node = CreateNode();
        float afterSeek = FirstSampleAfterWarmup(node, TimeSpan.FromSeconds(5));

        Assert.That(afterSeek, Is.EqualTo(FirstSampleFromFreshNode()).Within(1e-6f),
            "A seek must reset the filter state, so the first sample matches a fresh node's.");
    }
}
