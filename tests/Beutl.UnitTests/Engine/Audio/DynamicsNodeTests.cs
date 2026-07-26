using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

// Pins the DynamicsNode base-class contract that no single derived node can cover on its own.
[TestFixture]
public class DynamicsNodeTests
{
    private const int SampleRate = 48000;
    private const int ChunkSamples = SampleRate / 10;

    private static readonly TimeSpan s_chunk = TimeSpan.FromSeconds(ChunkSamples / (double)SampleRate);

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

    private static AudioProcessContext CreateContext(TimeSpan start) =>
        new(new TimeRange(start, s_chunk), SampleRate, new AnimationSampler(), null);

    [Test]
    public void Process_HookThrows_LeavesTheNextChunkNonContiguous()
    {
        using var input = CreateConstantBuffer(0.5f, ChunkSamples);
        using var node = new RecordingDynamicsNode();
        node.AddInput(new BufferReplayNode(input));

        // Counted as a delta: the opening chunk resets for both the sample-rate and the no-predecessor
        // reason, which is an implementation detail this test must not depend on.
        node.Process(CreateContext(TimeSpan.Zero)).Dispose();
        int afterFirstChunk = node.ResetCount;

        node.ThrowFromHook = true;
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(s_chunk)));

        node.ThrowFromHook = false;
        node.Process(CreateContext(s_chunk + s_chunk)).Dispose();

        Assert.That(node.ResetCount, Is.EqualTo(afterFirstChunk + 1),
            "the chunk after a throwing hook must reset rather than inherit half-mutated state");
    }

    [Test]
    public void Process_HookSucceeds_KeepsTheNextChunkContiguous()
    {
        using var input = CreateConstantBuffer(0.5f, ChunkSamples);
        using var node = new RecordingDynamicsNode();
        node.AddInput(new BufferReplayNode(input));

        node.Process(CreateContext(TimeSpan.Zero)).Dispose();
        int afterFirstChunk = node.ResetCount;

        node.Process(CreateContext(s_chunk)).Dispose();
        node.Process(CreateContext(s_chunk + s_chunk)).Dispose();

        Assert.That(node.ResetCount, Is.EqualTo(afterFirstChunk),
            "contiguous chunks must not reset, or the follower restarts mid-signal");
    }
}
