using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

// Pins the AudioNode buffer-ownership contract: a node that emits a NEW buffer must dispose the
// input it consumed (an undisposed input leaks its MemoryPool<float>.Shared lease); a pass-through
// node returns the input untouched, transferring ownership to the caller. Disposal is observed via
// the public surface — AudioBuffer.GetChannelData throws ObjectDisposedException once disposed.
[TestFixture]
public class AudioNodeBufferDisposalTests
{
    private const int SampleRate = 100;
    private const int SampleCount = 100;

    // Records every buffer it hands out so a test can assert its post-Process disposal state.
    private sealed class CapturingInputNode(int sampleRate, int channels, int count, float value) : AudioNode
    {
        public List<AudioBuffer> Produced { get; } = new();

        public AudioBuffer Last => Produced[^1];

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var buffer = new AudioBuffer(sampleRate, channels, count);
            for (int ch = 0; ch < channels; ch++)
            {
                buffer.GetChannelData(ch).Fill(value);
            }

            Produced.Add(buffer);
            return buffer;
        }
    }

    private static AudioProcessContext CreateContext(int sampleRate = SampleRate) =>
        new(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sampleRate, new AnimationSampler(), null);

    private static bool IsDisposed(AudioBuffer buffer)
    {
        try
        {
            buffer.GetChannelData(0);
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static IProperty<float> Static(float value) => Property.Create(value);

    private static IProperty<float> AnimatedGain(float fromPercent, float toPercent, double durationSeconds)
    {
        var property = Property.CreateAnimatable(fromPercent);
        property.SetAttributes("Gain", []);

        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = fromPercent, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(durationSeconds), Value = toPercent, Easing = new LinearEasing() });
        property.Animation = animation;
        return property;
    }

    private static IProperty<float> AnimatedSpeed(float fromPercent, float toPercent, double durationSeconds)
    {
        var property = Property.CreateAnimatable(fromPercent);
        property.SetAttributes("Speed", []);

        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = fromPercent, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(durationSeconds), Value = toPercent, Easing = new LinearEasing() });
        property.Animation = animation;
        return property;
    }

    [Test]
    public void CompressorNode_DisposesConsumedInput()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new CompressorNode
        {
            Threshold = Static(-20f),
            Ratio = Static(4f),
            Attack = Static(10f),
            Release = Static(100f),
            Knee = Static(6f),
            MakeupGain = Static(0f),
        };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False, "compressor must emit a new buffer, not the input");
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void GainNode_NonUnityGain_DisposesConsumedInput()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new GainNode { Gain = Static(50f) };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void GainNode_AnimatedGain_DisposesConsumedInput()
    {
        var inputNode = new CapturingInputNode(SampleRate, 1, SampleCount, 1.0f);
        using var node = new GainNode { Gain = AnimatedGain(0f, 100f, 1.0) };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void GainNode_UnityGain_PassesInputThroughWithoutDisposing()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new GainNode { Gain = Static(100f) };
        node.AddInput(inputNode);

        AudioBuffer result = node.Process(CreateContext());

        Assert.That(ReferenceEquals(result, inputNode.Last), Is.True, "unity gain must pass the input through");
        Assert.That(IsDisposed(result), Is.False, "pass-through must not dispose the buffer the caller now owns");
        result.Dispose();
    }

    [Test]
    public void EqualizerNode_WithBands_DisposesConsumedInput()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new EqualizerNode { Bands = [new EqualizerBand()] };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void EqualizerNode_NoBands_PassesInputThroughWithoutDisposing()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new EqualizerNode { Bands = [] };
        node.AddInput(inputNode);

        AudioBuffer result = node.Process(CreateContext());

        Assert.That(ReferenceEquals(result, inputNode.Last), Is.True, "no bands must pass the input through");
        Assert.That(IsDisposed(result), Is.False, "pass-through must not dispose the buffer the caller now owns");
        result.Dispose();
    }

    [Test]
    public void DelayNode_DisposesConsumedInput()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new DelayNode
        {
            DelayTime = Static(200f),
            Feedback = Static(50f),
            DryMix = Static(60f),
            WetMix = Static(40f),
        };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void MixerNode_DisposesAllConsumedInputs()
    {
        var a = new CapturingInputNode(SampleRate, 2, SampleCount, 0.25f);
        var b = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new MixerNode { Gains = [1f, 1f] };
        node.AddInput(a);
        node.AddInput(b);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, a.Last), Is.False);
            Assert.That(ReferenceEquals(result, b.Last), Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(IsDisposed(a.Last), Is.True, "first consumed input must be disposed");
            Assert.That(IsDisposed(b.Last), Is.True, "second consumed input must be disposed");
        });
    }

    [Test]
    public void ResampleNode_Resampling_DisposesConsumedInput()
    {
        // Input produces a 100 Hz buffer; the node is asked to deliver 200 Hz, so it must resample
        // (allocate a new buffer) and dispose the consumed input.
        var inputNode = new CapturingInputNode(100, 2, SampleCount, 0.5f);
        using var node = new ResampleNode { SourceSampleRate = 100 };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext(200)))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(IsDisposed(inputNode.Last), Is.True, "consumed input buffer must be disposed");
    }

    [Test]
    public void ResampleNode_MatchingRate_PassesInputThroughWithoutDisposing()
    {
        // Input already at 200 Hz: the node short-circuits and returns the input as-is.
        var inputNode = new CapturingInputNode(200, 2, SampleCount, 0.5f);
        using var node = new ResampleNode { SourceSampleRate = 200 };
        node.AddInput(inputNode);

        AudioBuffer result = node.Process(CreateContext(200));

        Assert.That(ReferenceEquals(result, inputNode.Last), Is.True, "matching rate must pass the input through");
        Assert.That(IsDisposed(result), Is.False, "pass-through must not dispose the buffer the caller now owns");
        result.Dispose();
    }

    // SpeedNode's resampler reads the upstream through SpeedProcessor.Read, which calls Inputs[0].Process
    // once per resampler iteration. Each call leases a fresh pooled AudioBuffer that is read and dropped;
    // every one must be disposed.
    [Test]
    public void SpeedNode_NonUnitySpeed_DisposesConsumedInputs()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new SpeedNode { Speed = Static(200f) };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False, "a speed change must emit a new buffer");
        }

        Assert.That(inputNode.Produced, Is.Not.Empty, "the resampler must have read at least one input buffer");
        foreach (AudioBuffer produced in inputNode.Produced)
            Assert.That(IsDisposed(produced), Is.True, "every consumed input buffer must be disposed");
    }

    [Test]
    public void SpeedNode_AnimatedSpeed_DisposesConsumedInputs()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new SpeedNode { Speed = AnimatedSpeed(50f, 200f, 1.0) };
        node.AddInput(inputNode);

        using (AudioBuffer result = node.Process(CreateContext()))
        {
            Assert.That(ReferenceEquals(result, inputNode.Last), Is.False);
        }

        Assert.That(inputNode.Produced, Is.Not.Empty, "the resampler must have read at least one input buffer");
        foreach (AudioBuffer produced in inputNode.Produced)
            Assert.That(IsDisposed(produced), Is.True, "every consumed input buffer must be disposed");
    }

    [Test]
    public void SpeedNode_UnitySpeed_PassesInputThroughWithoutDisposing()
    {
        var inputNode = new CapturingInputNode(SampleRate, 2, SampleCount, 0.5f);
        using var node = new SpeedNode { Speed = Static(100f) };
        node.AddInput(inputNode);

        AudioBuffer result = node.Process(CreateContext());

        Assert.That(ReferenceEquals(result, inputNode.Last), Is.True, "unity speed must pass the input through");
        Assert.That(IsDisposed(result), Is.False, "pass-through must not dispose the buffer the caller now owns");
        result.Dispose();
    }
}
