using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class DynamicsNodeTests
{
    private const int SampleRate = 48000;
    private const int ChunkSamples = SampleRate / 10;

    private static readonly MediaTime s_chunk = MediaTime.FromSeconds(ChunkSamples / (double)SampleRate);

    private sealed class RecordingDynamicsNode : DynamicsNode
    {
        private static readonly ILogger s_logger = Log.CreateLogger<RecordingDynamicsNode>();

        public bool ThrowFromHook { get; set; }

        public int ResetCount { get; private set; }

        protected override ILogger Logger => s_logger;

        protected override string DiagnosticName => "Recording";

        protected override bool HasAnimatedParameters => false;

        protected override AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
        {
            if (ThrowFromHook)
                throw new InvalidOperationException("hook failed");

            return new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        }

        protected override AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
            => ProcessStatic(input, context);

        protected override void ResetDspState() => ResetCount++;
    }

    private static AudioProcessContext CreateContext(MediaTime start) =>
        AudioTestContexts.CreateContext(start, s_chunk, SampleRate);

    // The production nodes, for the base-class behavior none of them overrides.
    private static IEnumerable<TestCaseData> ProductionNodes()
    {
        yield return new TestCaseData(new Func<DynamicsNode>(() => CompressorNodeTests.CreateNode()))
            .SetArgDisplayNames("Compressor");
        yield return new TestCaseData(new Func<DynamicsNode>(() => GateNodeTests.CreateNode()))
            .SetArgDisplayNames("Gate");
    }

    // A +Infinity head sample on every channel drives the envelope non-finite, firing the one-shot warning.
    private static AudioBuffer MakeInfinityHeadBuffer(int sampleCount, int sampleRate = SampleRate)
    {
        var buffer = CreateConstantBuffer(0.9f, sampleCount, 2, sampleRate);
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            buffer.GetChannelData(ch)[0] = float.PositiveInfinity;
        }
        return buffer;
    }

    [Test]
    public void Process_HookThrows_LeavesTheNextChunkNonContiguous()
    {
        using var input = CreateConstantBuffer(0.5f, ChunkSamples);
        using var node = new RecordingDynamicsNode();
        node.AddInput(new BufferReplayNode(input));

        // Compare the delta because the opening chunk resets for two independent reasons.
        node.Process(CreateContext(MediaTime.Zero)).Dispose();
        int afterFirstChunk = node.ResetCount;

        node.ThrowFromHook = true;
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(s_chunk)));

        node.ThrowFromHook = false;
        node.Process(CreateContext(s_chunk + s_chunk)).Dispose();

        Assert.That(node.ResetCount, Is.EqualTo(afterFirstChunk + 1),
            "the chunk after a throwing hook must reset rather than inherit half-mutated state");
    }

    [Test]
    public void Process_HookThrows_RetryingSameChunkResetsDspState()
    {
        using var input = CreateConstantBuffer(0.5f, ChunkSamples);
        using var node = new RecordingDynamicsNode();
        node.AddInput(new BufferReplayNode(input));

        node.Process(CreateContext(MediaTime.Zero)).Dispose();
        int afterFirstChunk = node.ResetCount;

        node.ThrowFromHook = true;
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(s_chunk)));

        node.ThrowFromHook = false;
        node.Process(CreateContext(s_chunk)).Dispose();

        Assert.That(node.ResetCount, Is.EqualTo(afterFirstChunk + 1),
            "retrying a failed chunk must reset rather than inherit state mutated before the exception");
    }

    [Test]
    public void Process_HookSucceeds_KeepsTheNextChunkContiguous()
    {
        using var input = CreateConstantBuffer(0.5f, ChunkSamples);
        using var node = new RecordingDynamicsNode();
        node.AddInput(new BufferReplayNode(input));

        node.Process(CreateContext(MediaTime.Zero)).Dispose();
        int afterFirstChunk = node.ResetCount;

        node.Process(CreateContext(s_chunk)).Dispose();
        node.Process(CreateContext(s_chunk + s_chunk)).Dispose();

        Assert.That(node.ResetCount, Is.EqualTo(afterFirstChunk),
            "contiguous chunks must not reset, or the follower restarts mid-signal");
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_NoInputs_Throws(Func<DynamicsNode> create)
    {
        using var node = create();
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(MediaTime.Zero)));
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_TooManyInputs_Throws(Func<DynamicsNode> create)
    {
        using var bufA = CreateConstantBuffer(0.1f, ChunkSamples);
        using var bufB = CreateConstantBuffer(0.1f, ChunkSamples);
        using var node = create();
        node.AddInput(new BufferReplayNode(bufA));
        node.AddInput(new BufferReplayNode(bufB));
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(MediaTime.Zero)));
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_ZeroLengthInput_ReturnsEmptyBuffer(Func<DynamicsNode> create)
    {
        // A zero-length chunk must not divide by zero or stackalloc[0].
        using var input = new AudioBuffer(SampleRate, 2, 0);
        using var node = create();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(new AudioProcessContext(
            new TimeRange(MediaTime.Zero, MediaTime.Zero), SampleRate, new AnimationSampler(), null));

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        Assert.That(output.SampleRate, Is.EqualTo(SampleRate));
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_NonFiniteSampleLatch_SurvivesSeekDiscontinuity(Func<DynamicsNode> create)
    {
        // A seek resets DSP state only; re-arming here would re-log a persistent fault on every scrub.
        using var node = create();

        using var first = MakeInfinityHeadBuffer(ChunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(MediaTime.Zero));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "The first non-finite sample must emit exactly one warning.");

        node.ClearInputs();
        using var second = MakeInfinityHeadBuffer(ChunkSamples);
        node.AddInput(new BufferReplayNode(second));
        using var seekedOut = node.Process(CreateContext(MediaTime.FromSeconds(5.0)));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "A seek discontinuity must not re-arm the latch, so no second warning should be emitted.");
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_NonFiniteSampleLatch_ReArmsOnSampleRateChange(Func<DynamicsNode> create)
    {
        // A sample-rate change is a session boundary, so Process() re-arms the latch.
        using var node = create();

        using var first = MakeInfinityHeadBuffer(ChunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(MediaTime.Zero));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1));

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var second = MakeInfinityHeadBuffer(altSampleRate / 10, altSampleRate);
        node.AddInput(new BufferReplayNode(second));
        using var secondOut = node.Process(new AudioProcessContext(
            new TimeRange(s_chunk, MediaTime.FromSeconds(0.1)),
            altSampleRate, new AnimationSampler(), null));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(2),
            "A sample-rate change is a session boundary, so the recurring fault must warn again.");
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Reset_ReArmsNonFiniteSampleLatch(Func<DynamicsNode> create)
    {
        // The second chunk stays time-contiguous so only the explicit Reset() can re-arm the latch.
        using var node = create();

        using var first = MakeInfinityHeadBuffer(ChunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(MediaTime.Zero));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1));

        node.Reset();
        node.ClearInputs();
        using var second = MakeInfinityHeadBuffer(ChunkSamples);
        node.AddInput(new BufferReplayNode(second));
        using var secondOut = node.Process(CreateContext(s_chunk));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(2),
            "Explicit Reset() re-arms the latch, so the recurring fault must warn a second time.");
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_EmptyChunkWithDuration_DoesNotMaskLaterDiscontinuity(Func<DynamicsNode> create)
    {
        using var node = create();
        using var warmupInput = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = node.Process(CreateContext(MediaTime.Zero));

        node.ClearInputs();
        using var emptyInput = new AudioBuffer(SampleRate, 2, 0);
        node.AddInput(new BufferReplayNode(emptyInput));
        using var emptyOutput = node.Process(CreateContext(s_chunk));
        Assert.That(emptyOutput.SampleCount, Is.EqualTo(0));

        node.ClearInputs();
        using var afterHoleInput = CreateConstantBuffer(0.9f, ChunkSamples);
        node.AddInput(new BufferReplayNode(afterHoleInput));
        using var afterHoleOutput = node.Process(CreateContext(s_chunk + s_chunk));

        using var nodeFresh = create();
        using var freshInput = CreateConstantBuffer(0.9f, ChunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(CreateContext(MediaTime.Zero));

        Assert.That(
            MathF.Abs(afterHoleOutput.GetChannelData(0)[0]),
            Is.EqualTo(MathF.Abs(freshOutput.GetChannelData(0)[0])).Within(1e-4f),
            "The chunk after an empty-but-timed chunk must start from a reset state.");
    }

    [TestCaseSource(nameof(ProductionNodes))]
    public void Process_InputSampleRateMismatch_Throws(Func<DynamicsNode> create)
    {
        // This node cannot resample a buffer whose rate differs from the processing context.
        const int altSampleRate = 44100;
        using var input = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
        using var node = create();
        node.AddInput(new BufferReplayNode(input));

        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(MediaTime.Zero)));
    }
}
