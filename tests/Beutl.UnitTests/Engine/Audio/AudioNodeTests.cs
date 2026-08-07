using Beutl.Audio;
using Beutl.Audio.Graph;

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

    private class ValueNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }
}
