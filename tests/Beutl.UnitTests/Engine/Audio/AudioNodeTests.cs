using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioNodeTests
{
    [Test]
    public void AddInput_WhenHookThrows_RestoresTopologyAndAllowsRetry()
    {
        using var node = new ThrowingHookNode();
        using var existing = new ValueNode();
        using var input = new ValueNode();
        node.AddInput(existing);
        node.ThrowOnAdd = true;

        Assert.Throws<InvalidOperationException>(() => node.AddInput(input));
        Assert.That(node.Inputs, Has.Count.EqualTo(1));
        Assert.That(node.Inputs[0], Is.SameAs(existing));
        Assert.That(node.MetadataInputs, Has.Count.EqualTo(1));
        Assert.That(node.MetadataInputs[0], Is.SameAs(existing));

        node.ThrowOnAdd = false;
        node.AddInput(input);

        Assert.That(node.Inputs, Has.Count.EqualTo(2));
        Assert.That(node.Inputs[0], Is.SameAs(existing));
        Assert.That(node.Inputs[1], Is.SameAs(input));
        Assert.That(node.MetadataInputs, Has.Count.EqualTo(2));
        Assert.That(node.MetadataInputs[0], Is.SameAs(existing));
        Assert.That(node.MetadataInputs[1], Is.SameAs(input));
    }

    [Test]
    public void AudioContextConnect_WhenAddHookThrows_AllowsAConsistentRetry()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var destination = new ThrowingHookNode { ThrowOnAdd = true };

        Assert.Throws<InvalidOperationException>(() => context.Connect(source, destination));
        Assert.That(destination.Inputs, Is.Empty);
        Assert.That(destination.MetadataInputs, Is.Empty);

        destination.ThrowOnAdd = false;
        context.Connect(source, destination);

        Assert.That(destination.Inputs, Has.Count.EqualTo(1));
        Assert.That(destination.Inputs[0], Is.SameAs(source));
        Assert.That(destination.MetadataInputs[0], Is.SameAs(source));

        context.RemoveNode(source);
        Assert.That(destination.Inputs, Is.Empty,
            "The successful retry must be recorded by AudioContext so later topology changes stay aligned.");
    }

    [Test]
    public void AudioContextRemoveNode_WhenRemoveHookThrows_PreservesContextForRetry()
    {
        using var context = new AudioContext(48000, 2);
        using var prefix = new ValueNode();
        using var source = new ValueNode();
        using var suffix = new ValueNode();
        using var destination = new MixerNode { Gains = [0.25f, 0.75f, 0.5f] };
        using var throwing = new ThrowingHookNode { ThrowOnRemove = true };
        context.Connect(prefix, destination);
        context.Connect(source, destination);
        context.Connect(suffix, destination);
        context.Connect(source, throwing);
        destination.SetBranchEndTime(source, TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() => context.RemoveNode(source));
        Assert.That(context.Nodes, Has.Count.EqualTo(5));
        Assert.That(destination.Inputs, Has.Count.EqualTo(3));
        Assert.That(destination.Inputs[0], Is.SameAs(prefix));
        Assert.That(destination.Inputs[1], Is.SameAs(source));
        Assert.That(destination.Inputs[2], Is.SameAs(suffix));
        Assert.That(destination.Gains, Is.EqualTo(new[] { 0.25f, 0.75f, 0.5f }).AsCollection);
        Assert.That(destination.ClearBranchEndTime(source), Is.True);

        throwing.ThrowOnRemove = false;
        using var sink = new ValueNode();
        Assert.DoesNotThrow(() => context.Connect(source, sink));
        Assert.That(sink.Inputs, Has.Count.EqualTo(1));

        Assert.DoesNotThrow(() => context.RemoveNode(source));
        Assert.That(destination.Inputs, Has.Count.EqualTo(2));
    }

    [Test]
    public void AudioContextRemoveNode_WhenRollbackHookThrows_ContinuesRestoringOtherDestinations()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var first = new ThrowingHookNode();
        using var rollbackFailure = new ThrowingHookNode();
        using var removalFailure = new ThrowingHookNode { ThrowOnRemove = true };
        context.Connect(source, first);
        context.Connect(source, rollbackFailure);
        context.Connect(source, removalFailure);

        rollbackFailure.ThrowOnAdd = true;

        Assert.Throws<AggregateException>(() => context.RemoveNode(source));
        Assert.Multiple(() =>
        {
            Assert.That(first.Inputs, Has.Count.EqualTo(1),
                "A rollback failure in a later destination must not prevent earlier destinations from being restored.");
            Assert.That(rollbackFailure.Inputs, Is.Empty);
            Assert.That(removalFailure.Inputs, Has.Count.EqualTo(1));
        });

        rollbackFailure.ThrowOnAdd = false;
        removalFailure.ThrowOnRemove = false;
        Assert.DoesNotThrow(() => context.RemoveNode(source));
        Assert.That(first.Inputs, Is.Empty);
        Assert.That(removalFailure.Inputs, Is.Empty);
    }

    [Test]
    public void AudioContextClearConnections_WhenHookThrows_RestoresGraphForRetry()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var first = new ThrowingHookNode();
        using var failing = new ThrowingHookNode { ThrowOnClear = true };
        context.Connect(source, first);
        context.Connect(source, failing);
        context.MarkAsOutput(first);
        context.SetCurrent(first);

        Assert.Throws<InvalidOperationException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(first.Inputs, Has.Count.EqualTo(1));
            Assert.That(first.Inputs[0], Is.SameAs(source));
            Assert.That(failing.Inputs, Has.Count.EqualTo(1));
            Assert.That(failing.Inputs[0], Is.SameAs(source));
            Assert.That(context.GetOutputNodes(), Has.Member(first),
                "Output bookkeeping must remain unchanged when clearing hooks fail.");
        });

        using var retrySink = new ValueNode();
        Assert.DoesNotThrow(() => context.ConnectTo(retrySink),
            "The current node and source connection must remain usable after a failed clear.");

        failing.ThrowOnClear = false;
        Assert.DoesNotThrow(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(first.Inputs, Is.Empty);
            Assert.That(failing.Inputs, Is.Empty);
            Assert.That(retrySink.Inputs, Is.Empty);
            Assert.That(context.GetOutputNodes(), Is.Empty);
        });
    }

    [Test]
    public void AudioContextRemoveNode_WhenHookMutatesContext_RejectsReentrantMutation()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var auxiliary = new ValueNode();
        using var destination = new ReentrantClearNode();
        context.Connect(source, destination);
        destination.OnRemove = () => context.Connect(source, auxiliary);

        Assert.Throws<InvalidOperationException>(() => context.RemoveNode(source));
        Assert.Multiple(() =>
        {
            Assert.That(destination.Inputs, Has.Count.EqualTo(1));
            Assert.That(destination.Inputs[0], Is.SameAs(source));
            Assert.That(context.Nodes, Does.Not.Contain(auxiliary));
        });

        destination.OnRemove = null;
        Assert.DoesNotThrow(() => context.RemoveNode(source));
        Assert.That(destination.Inputs, Is.Empty);
    }

    [Test]
    public void AudioContextClearConnections_WhenHookAddsInput_RollsBackReentrantTopology()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var auxiliary = new ValueNode();
        using var node = new ReentrantClearNode();
        context.Connect(source, node);
        context.MarkAsOutput(node);
        context.SetCurrent(node);
        node.OnClear = () => node.AddInput(auxiliary);

        Assert.Throws<InvalidOperationException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(node.Inputs, Has.Count.EqualTo(1));
            Assert.That(node.Inputs[0], Is.SameAs(source));
            Assert.That(context.GetOutputNodes(), Has.Member(node));
        });

        node.OnClear = null;
        Assert.DoesNotThrow(() => context.ClearConnections());
        Assert.That(node.Inputs, Is.Empty);
    }

    [Test]
    public void AudioContextClearConnections_WhenHookMutatesContext_RejectsReentrantMutation()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var auxiliary = new ValueNode();
        using var node = new ReentrantClearNode();
        context.Connect(source, node);
        context.MarkAsOutput(node);
        context.SetCurrent(node);
        node.OnClear = () => context.Connect(node, auxiliary);

        Assert.Throws<InvalidOperationException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(node.Inputs, Has.Count.EqualTo(1));
            Assert.That(node.Inputs[0], Is.SameAs(source));
            Assert.That(context.Nodes, Does.Not.Contain(auxiliary));
            Assert.That(context.GetOutputNodes(), Has.Member(node));
        });
    }

    [Test]
    public void AudioContextClearConnections_WhenHookReordersInputs_RestoresOriginalOrder()
    {
        using var context = new AudioContext(48000, 2);
        using var first = new ValueNode();
        using var second = new ValueNode();
        using var node = new ReentrantClearNode();
        context.Connect(first, node);
        context.Connect(second, node);
        node.OnClear = () =>
        {
            node.AddInput(second);
            node.AddInput(first);
        };

        Assert.Throws<InvalidOperationException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(node.Inputs, Has.Count.EqualTo(2));
            Assert.That(node.Inputs[0], Is.SameAs(first));
            Assert.That(node.Inputs[1], Is.SameAs(second));
        });

        node.OnClear = null;
        Assert.DoesNotThrow(() => context.ClearConnections());
    }

    [Test]
    public void AudioContextClearConnections_WhenRollbackRestoresResampleInput_PreservesStreamingState()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const int blockSamples = 128;
        const int sourceSamples = 4096;

        using var sourceBuffer = AudioTestBuffers.CreateBuffer(
            2,
            sourceSamples,
            static (_, index) => MathF.Sin(index * 0.013f),
            sourceSampleRate);
        using var source = new BufferReplayNode(sourceBuffer);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        using var failing = new ThrowingHookNode { ThrowOnClear = true };

        using var context = new AudioContext(outputSampleRate, 2);
        context.Connect(source, resample);
        context.Connect(resample, failing);

        AudioProcessContext firstContext = ExactContext(TimeSpan.Zero, blockSamples, outputSampleRate);
        AudioProcessContext secondContext = ExactContext(
            ExactDuration(blockSamples, outputSampleRate),
            blockSamples,
            outputSampleRate);
        using var first = resample.Process(firstContext);

        Assert.Throws<InvalidOperationException>(() => context.ClearConnections());
        failing.ThrowOnClear = false;

        using var actual = resample.Process(secondContext);

        using var controlSource = new BufferReplayNode(sourceBuffer);
        using var control = new ResampleNode { SourceSampleRate = sourceSampleRate };
        control.AddInput(controlSource);
        using var controlFirst = control.Process(firstContext);
        using var expected = control.Process(secondContext);

        Assert.Multiple(() =>
        {
            Assert.That(actual.ChannelCount, Is.EqualTo(expected.ChannelCount));
            Assert.That(actual.SampleCount, Is.EqualTo(expected.SampleCount));
            for (int channel = 0; channel < actual.ChannelCount; channel++)
            {
                Assert.That(actual.GetChannelData(channel).ToArray(),
                    Is.EqualTo(expected.GetChannelData(channel).ToArray()).Within(1e-5f),
                    $"Resample channel {channel} must continue from the pre-rollback stream position.");
            }
        });
    }

    [Test]
    public void AudioContextClearConnections_WhenCommitHookThrows_CommitsTopologyAndReportsFailure()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var node = new CommitThrowingNode { ThrowOnCommit = true };
        context.Connect(source, node);

        Assert.Throws<AggregateException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(node.Inputs, Is.Empty,
                "A commit-hook failure must not roll the graph back into a partially disconnected state.");
            Assert.That(context.GetOutputNodes(), Is.Empty);
        });

        node.ThrowOnCommit = false;
        Assert.DoesNotThrow(() => context.ClearConnections());
    }

    [Test]
    public void AudioContextClearConnections_WhenCommitHookMutatesInputs_RejectsMutation()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var auxiliary = new ValueNode();
        using var target = new ValueNode();
        using var node = new CommitMutatingNode
        {
            InputToAdd = auxiliary,
            Target = target,
            MutateOnCommit = true,
        };
        context.Connect(source, node);
        context.AddNode(target);

        Assert.Throws<AggregateException>(() => context.ClearConnections());
        Assert.Multiple(() =>
        {
            Assert.That(node.Inputs, Is.Empty);
            Assert.That(target.Inputs, Is.Empty,
                "A commit callback must not mutate another node that is completing the same transaction.");
            Assert.That(context.Nodes, Does.Not.Contain(auxiliary));
        });

        node.MutateOnCommit = false;
        Assert.DoesNotThrow(() => context.ClearConnections());
    }

    [Test]
    public void AudioContextRemoveNode_WhenCommitHookMutatesUnaffectedNode_RejectsMutation()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var target = new ValueNode();
        using var affected = new CommitMutatingNode
        {
            InputToAdd = source,
            Target = target,
            MutateOnCommit = true,
        };
        context.Connect(source, affected);
        context.AddNode(target);

        Assert.Throws<AggregateException>(() => context.RemoveNode(source));
        Assert.Multiple(() =>
        {
            Assert.That(context.Nodes, Does.Not.Contain(source));
            Assert.That(affected.Inputs, Is.Empty);
            Assert.That(target.Inputs, Is.Empty,
                "An unaffected context node must remain guarded during removal callbacks.");
        });
    }

    [Test]
    public void AudioContextRemoveNode_WhenRollbackRestoresResampleInput_PreservesStreamingState()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const int blockSamples = 128;
        const int sourceSamples = 4096;

        using var sourceBuffer = AudioTestBuffers.CreateBuffer(
            2,
            sourceSamples,
            static (_, index) => MathF.Sin(index * 0.013f),
            sourceSampleRate);
        using var source = new BufferReplayNode(sourceBuffer);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        using var failing = new ThrowingHookNode { ThrowOnRemove = true };

        using var context = new AudioContext(outputSampleRate, 2);
        context.Connect(source, resample);
        context.Connect(source, failing);

        AudioProcessContext firstContext = ExactContext(TimeSpan.Zero, blockSamples, outputSampleRate);
        AudioProcessContext secondContext = ExactContext(
            ExactDuration(blockSamples, outputSampleRate),
            blockSamples,
            outputSampleRate);
        using var first = resample.Process(firstContext);

        Assert.Throws<InvalidOperationException>(() => context.RemoveNode(source));
        failing.ThrowOnRemove = false;

        using var actual = resample.Process(secondContext);

        using var controlSource = new BufferReplayNode(sourceBuffer);
        using var control = new ResampleNode { SourceSampleRate = sourceSampleRate };
        control.AddInput(controlSource);
        using var controlFirst = control.Process(firstContext);
        using var expected = control.Process(secondContext);

        for (int channel = 0; channel < actual.ChannelCount; channel++)
        {
            Assert.That(actual.GetChannelData(channel).ToArray(),
                Is.EqualTo(expected.GetChannelData(channel).ToArray()).Within(1e-5f),
                $"Resample channel {channel} must continue after a failed RemoveNode rollback.");
        }
    }

    [Test]
    public void TransformingNodeFlush_PreservesMonoLayoutAfterInputRemoval()
    {
        const int sampleRate = 48000;
        const int sampleCount = 64;

        using var input = new MonoValueNode();
        using var gain = new GainNode { Gain = Property.Create(50f) };
        gain.AddInput(input);

        using var processed = gain.Process(ExactContext(TimeSpan.Zero, sampleCount, sampleRate));
        gain.RemoveInput(input);
        using var flushed = gain.Flush(ExactContext(ExactDuration(sampleCount, sampleRate), sampleCount, sampleRate));

        Assert.That(processed.ChannelCount, Is.EqualTo(1));
        Assert.That(flushed.ChannelCount, Is.EqualTo(1),
            "A transforming node must retain the layout it emitted when its input is no longer connected.");
    }

    [Test]
    public void RemoveInput_WhenHookThrows_RestoresTopologyAndAllowsRetry()
    {
        using var node = new ThrowingHookNode();
        using var input = new ValueNode();
        node.AddInput(input);
        node.ThrowOnRemove = true;

        Assert.Throws<InvalidOperationException>(() => node.RemoveInput(input));
        Assert.That(node.Inputs, Has.Count.EqualTo(1));
        Assert.That(node.Inputs[0], Is.SameAs(input));
        Assert.That(node.MetadataInputs[0], Is.SameAs(input));

        node.ThrowOnRemove = false;
        node.RemoveInput(input);

        Assert.That(node.Inputs, Is.Empty);
        Assert.That(node.MetadataInputs, Is.Empty);
    }

    [Test]
    public void ClearInputs_WhenHookThrows_RestoresTopologyAndAllowsRetry()
    {
        using var node = new ThrowingHookNode();
        using var first = new ValueNode();
        using var second = new ValueNode();
        node.AddInput(first);
        node.AddInput(second);
        node.ThrowOnClear = true;

        Assert.Throws<InvalidOperationException>(() => node.ClearInputs());
        Assert.That(node.Inputs, Has.Count.EqualTo(2));
        Assert.That(node.Inputs[0], Is.SameAs(first));
        Assert.That(node.Inputs[1], Is.SameAs(second));
        Assert.That(node.MetadataInputs[0], Is.SameAs(first));
        Assert.That(node.MetadataInputs[1], Is.SameAs(second));

        node.ThrowOnClear = false;
        node.ClearInputs();

        Assert.That(node.Inputs, Is.Empty);
        Assert.That(node.MetadataInputs, Is.Empty);
    }

    private sealed class ThrowingHookNode : ValueNode
    {
        private readonly List<AudioNode> _metadataInputs = [];

        public IReadOnlyList<AudioNode> MetadataInputs => _metadataInputs;

        public bool ThrowOnAdd { get; set; }

        public bool ThrowOnRemove { get; set; }

        public bool ThrowOnClear { get; set; }

        protected override void OnInputAdded(AudioNode input, int index)
        {
            _metadataInputs.Insert(index, input);
            if (ThrowOnAdd)
            {
                _metadataInputs.RemoveAt(index);
                throw new InvalidOperationException("Add hook failure.");
            }
        }

        protected override void OnInputRemoved(AudioNode input, int index)
        {
            AudioNode metadata = _metadataInputs[index];
            _metadataInputs.RemoveAt(index);
            if (ThrowOnRemove)
            {
                _metadataInputs.Insert(index, metadata);
                throw new InvalidOperationException("Remove hook failure.");
            }
        }

        protected override void OnInputsCleared()
        {
            AudioNode[] metadata = [.. _metadataInputs];
            _metadataInputs.Clear();
            if (ThrowOnClear)
            {
                _metadataInputs.AddRange(metadata);
                throw new InvalidOperationException("Clear hook failure.");
            }
        }
    }

    private sealed class ReentrantClearNode : ValueNode
    {
        public Action? OnClear { get; set; }

        public Action? OnRemove { get; set; }

        protected override void OnInputsCleared() => OnClear?.Invoke();

        protected override void OnInputRemoved(AudioNode input, int index) => OnRemove?.Invoke();
    }

    private class ValueNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }

    private sealed class MonoValueNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            RecordProcessedChannelCount(1);
            return new AudioBuffer(context.SampleRate, 1, context.GetSampleCount());
        }
    }

    private sealed class CommitThrowingNode : ValueNode
    {
        public bool ThrowOnCommit { get; set; }

        protected override void OnInputClearTransactionCommitted()
        {
            if (ThrowOnCommit)
                throw new InvalidOperationException("Commit hook failure.");
        }
    }

    private sealed class CommitMutatingNode : ValueNode
    {
        public AudioNode? InputToAdd { get; set; }

        public AudioNode? Target { get; set; }

        public bool MutateOnCommit { get; set; }

        protected override void OnInputClearTransactionCommitted()
        {
            if (MutateOnCommit)
                Target!.AddInput(InputToAdd!);
        }
    }

    private static AudioProcessContext ExactContext(TimeSpan start, int sampleCount, int sampleRate)
        => new(
            new Beutl.Media.TimeRange(start, AudioProcessContext.GetDurationForSampleCount(sampleCount, sampleRate)),
            sampleRate,
            new Beutl.Animation.AnimationSampler(),
            null);

    private static TimeSpan ExactDuration(int sampleCount, int sampleRate)
        => AudioProcessContext.GetDurationForSampleCount(sampleCount, sampleRate);
}
