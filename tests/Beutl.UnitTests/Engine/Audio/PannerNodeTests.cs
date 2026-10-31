using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class PannerNodeTests
{
    private const int Rate = 48000;

    private static PannerNode Create(int input, int output, float pan = 0, float surround = 0, float center = 0, float lfe = 0)
        => new()
        {
            InputLayout = AudioChannelLayout.GetDefault(input), OutputLayout = AudioChannelLayout.GetDefault(output),
            Pan = Property.CreateAnimatable(pan), SurroundPan = Property.CreateAnimatable(surround),
            CenterLevel = Property.CreateAnimatable(center), LfeLevel = Property.CreateAnimatable(lfe)
        };

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void Defaults_ReturnTheInputItself(int channels)
    {
        var input = AudioTestBuffers.CreateBuffer(channels, 4, (_, i) => i == 0 ? -0f : i / 8f);
        using var source = new TransferNode(input);
        using var node = Create(channels, channels);
        node.AddInput(source);
        using var result = node.Process(AudioTestContexts.AtSample(0, 4, Rate));
        Assert.That(result, Is.SameAs(input));
        Assert.That(BitConverter.SingleToInt32Bits(result.GetChannelData(0)[0]), Is.EqualTo(int.MinValue));
    }

    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void DefaultMonoDuplication_IsBitExactIncludingSignedZero(int channels)
    {
        using var input = AudioTestBuffers.CreateBuffer(1, 4, (_, i) => i == 0 ? -0f : i / 8f);
        using var node = Create(1, channels);
        node.AddInput(new BufferReplayNode(input));
        using var result = node.Process(AudioTestContexts.AtSample(0, 4, Rate));
        for (int ch = 0; ch < 2; ch++)
            Assert.That(result.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                Is.EqualTo(input.GetChannelData(0).ToArray().Select(BitConverter.SingleToInt32Bits)));
        for (int ch = 2; ch < channels; ch++)
            Assert.That(result.GetChannelData(ch).ToArray(), Is.All.Zero);
    }

    [TestCase(1, -100f, 0.25f, 0f)]
    [TestCase(1, 100f, 0f, 0.25f)]
    [TestCase(2, -100f, 0.25f, 0f)]
    [TestCase(2, 100f, 0f, 0.25f)]
    public void BalanceEndpoints(int channels, float pan, float left, float right)
    {
        using var node = Create(channels, 2, pan, 100, 100, 100);
        node.AddInput(new ConstantInputNode(Rate, channels, 8, 0.25f));
        using var result = node.Process(AudioTestContexts.AtSample(0, 8, Rate));
        Assert.That(result.GetChannelData(0).ToArray(), Is.All.EqualTo(left));
        Assert.That(result.GetChannelData(1).ToArray(), Is.All.EqualTo(right));
    }

    [TestCase(6, 0f, 1f, 0f, 0f)]
    [TestCase(6, 50f, 0.70710677f, 0.70710677f, 0f)]
    [TestCase(6, 100f, 0f, 1f, 0f)]
    [TestCase(8, 0f, 1f, 0f, 0f)]
    [TestCase(8, 50f, 0f, 0f, 1f)]
    [TestCase(8, 100f, 0f, 1f, 0f)]
    public void SurroundPositions(int channels, float position, float front, float back, float side)
    {
        using var node = Create(2, channels, surround: position);
        using var input = AudioTestBuffers.CreateBuffer(2, 8, (ch, _) => ch == 0 ? 0.25f : -0.5f);
        node.AddInput(new BufferReplayNode(input));
        using var result = node.Process(AudioTestContexts.AtSample(0, 8, Rate));
        Assert.That(result.GetChannelData(0)[0], Is.EqualTo(front * 0.25f).Within(1e-7));
        Assert.That(result.GetChannelData(1)[0], Is.EqualTo(front * -0.5f).Within(1e-7));
        Assert.That(result.GetChannelData(4)[0], Is.EqualTo(back * 0.25f).Within(1e-7));
        Assert.That(result.GetChannelData(5)[0], Is.EqualTo(back * -0.5f).Within(1e-7));
        if (channels == 8)
        {
            Assert.That(result.GetChannelData(6)[0], Is.EqualTo(side * 0.25f).Within(1e-7));
            Assert.That(result.GetChannelData(7)[0], Is.EqualTo(side * -0.5f).Within(1e-7));
        }
    }

    [TestCase(0f, 0f, 0f, 0.25f)]
    [TestCase(-100f, 0.25f, 0f, 0f)]
    [TestCase(100f, 0f, 0.25f, 0f)]
    public void CenterHonorsTheBalance(float pan, float left, float right, float center)
    {
        using var node = Create(1, 6, pan, center: 100);
        node.AddInput(new ConstantInputNode(Rate, 1, 8, 0.25f));
        using var result = node.Process(AudioTestContexts.AtSample(0, 8, Rate));
        Assert.That(result.GetChannelData(0)[0], Is.EqualTo(left));
        Assert.That(result.GetChannelData(1)[0], Is.EqualTo(right));
        Assert.That(result.GetChannelData(2)[0], Is.EqualTo(center));
    }

    [Test]
    public void MultichannelInput_OnlyBalancesAfterConversion()
    {
        using var node = Create(8, 6, -100, 100, 100, 100);
        using var input = AudioTestBuffers.CreateBuffer(8, 8, (ch, _) => (ch + 1) / 16f);
        node.AddInput(new BufferReplayNode(input));
        using var expected = AudioChannelMatrix.Create(AudioChannelLayout.Surround71, AudioChannelLayout.Surround51).Apply(input);
        using var result = node.Process(AudioTestContexts.AtSample(0, 8, Rate));
        for (int ch = 0; ch < 6; ch++)
            Assert.That(result.GetChannelData(ch).ToArray(), Is.EqualTo(ch is 1 or 5 ? new float[8] : expected.GetChannelData(ch).ToArray()));
    }

    [Test]
    public void MonoProject_IgnoresAllControls()
    {
        using var node = Create(6, 1, 100, 100, 100, 100);
        using var input = AudioTestBuffers.CreateBuffer(6, 8, (ch, _) => (ch + 1) / 32f);
        node.AddInput(new BufferReplayNode(input));
        using var expected = AudioChannelMatrix.Create(AudioChannelLayout.Surround51, AudioChannelLayout.Mono).Apply(input);
        using var result = node.Process(AudioTestContexts.AtSample(0, 8, Rate));
        Assert.That(result.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [TestCase(40, 0.95, 1.05)]
    [TestCase(2000, 0, 0.01)]
    public void LfeUses120HzLowPass(int frequency, double min, double max)
    {
        using var input = AudioTestBuffers.CreateSineBuffer(0.25f, frequency, Rate, 1);
        using var node = Create(1, 6, lfe: 100);
        node.AddInput(new BufferReplayNode(input));
        using var result = node.Process(AudioTestContexts.AtSample(0, Rate, Rate));
        double power = result.GetChannelData(3)[(Rate / 2)..].ToArray().Average(v => (double)v * v);
        Assert.That(Math.Sqrt(power / (0.25 * 0.25 / 2)), Is.InRange(min, max));
    }

    [Test]
    public void FlushConvertsAndKeepsTheLfeStateUntilADiscontinuity()
    {
        using var node = Create(1, 6, lfe: 100);
        using var source = new ImpulseNode();
        node.AddInput(source);
        using var body = node.Process(AudioTestContexts.AtSample(0, 16, Rate));
        using var tail = node.Flush(AudioTestContexts.AtSample(16, 64, Rate));
        Assert.That(tail.ChannelCount, Is.EqualTo(6));
        Assert.That(tail.GetChannelData(3).ToArray().Any(v => v != 0), Is.True);
        using var afterSeek = node.Flush(AudioTestContexts.AtSample(1000, 64, Rate));
        Assert.That(afterSeek.GetChannelData(3).ToArray(), Is.All.Zero);

        using var continuous = Create(1, 6, lfe: 100);
        continuous.AddInput(new ImpulseNode());
        using var whole = continuous.Process(AudioTestContexts.AtSample(0, 80, Rate));
        Assert.That(tail.GetChannelData(3).ToArray(), Is.EqualTo(whole.GetChannelData(3)[16..].ToArray()));
    }

    [Test]
    public void AnimatedPanSamplesEachChunkOnTheTimelineClock()
    {
        using var node = Create(2, 2);
        node.Pan.SetKeyFrameAnimation(AudioTestAnimations.Ramp(-100, 100, MediaTime.FromSeconds(1)));
        node.AddInput(new ConstantInputNode(Rate, 2, Rate, 0.25f));
        var context = AudioTestContexts.AtSample(0, Rate, Rate);
        using var result = node.Process(context);
        var values = new float[Rate];
        context.AnimationSampler.SampleBuffer(node.Pan, context.TimeRange, Rate, values);
        for (int i = 0; i < Rate; i++)
        {
            Assert.That(result.GetChannelData(0)[i], Is.EqualTo(0.25f * Math.Min(1, 1 - values[i] / 100f)));
            Assert.That(result.GetChannelData(1)[i], Is.EqualTo(0.25f * Math.Min(1, 1 + values[i] / 100f)));
        }
    }

    [Test]
    public void AnimatedZeroBalance_PreservesSignedZero()
    {
        using var input = AudioTestBuffers.CreateBuffer(1, 4096, (_, i) => i % 2 == 0 ? -0f : 0.25f);
        using var node = Create(1, 2);
        node.Pan.SetKeyFrameAnimation(AudioTestAnimations.Ramp(0, 0, MediaTime.FromSeconds(1)));
        node.AddInput(new BufferReplayNode(input));
        using var result = node.Process(AudioTestContexts.AtSample(0, 4096, Rate));
        for (int ch = 0; ch < 2; ch++)
            Assert.That(result.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                Is.EqualTo(input.GetChannelData(0).ToArray().Select(BitConverter.SingleToInt32Bits)));
    }

    [TestCase(1, 2)]
    [TestCase(2, 6)]
    [TestCase(2, 8)]
    [TestCase(6, 8)]
    public void LongAnimatedBuffer_MatchesSmallerContiguousContexts(int inputChannels, int outputChannels)
    {
        PannerNode CreateAnimated()
        {
            var node = Create(inputChannels, outputChannels);
            var duration = MediaTime.FromSeconds(1);
            node.Pan.SetKeyFrameAnimation(AudioTestAnimations.Ramp(-100, 100, duration));
            node.SurroundPan.SetKeyFrameAnimation(AudioTestAnimations.Ramp(0, 100, duration));
            node.CenterLevel.SetKeyFrameAnimation(AudioTestAnimations.Ramp(0, 80, duration));
            node.LfeLevel.SetKeyFrameAnimation(AudioTestAnimations.Ramp(0, 70, duration));
            node.AddInput(new TimelineInputNode(inputChannels));
            return node;
        }

        using var continuous = CreateAnimated();
        using var segmented = CreateAnimated();
        using var expected = continuous.Process(AudioTestContexts.Exact(MediaTime.Zero, Rate, Rate));
        using var actual = new AudioBuffer(Rate, outputChannels, Rate);
        // Deliberately cross the panner's 2048-sample scratch boundaries at different offsets.
        for (int offset = 0; offset < Rate; offset += 3001)
        {
            int count = Math.Min(3001, Rate - offset);
            using var chunk = segmented.Process(AudioTestContexts.Exact(MediaTime.FromSamples(offset, Rate), count, Rate));
            chunk.CopyTo(actual, offset);
        }
        for (int ch = 0; ch < outputChannels; ch++)
            Assert.That(actual.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                Is.EqualTo(expected.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits)));
    }

    [Test]
    public void ReuseUpdatesLayouts()
    {
        var sound = new SourceSound();
        using var context = new AudioContext(Rate, AudioChannelLayout.Surround51);
        var first = context.CreatePannerNode(sound.Pan, sound.SurroundPan, sound.CenterLevel, sound.LfeLevel, AudioChannelLayout.Mono);
        context.BeginUpdate(context.Nodes.ToArray());
        var reused = context.CreatePannerNode(sound.Pan, sound.SurroundPan, sound.CenterLevel, sound.LfeLevel, AudioChannelLayout.Surround51);
        context.EndUpdate();
        Assert.That(reused, Is.SameAs(first));
        Assert.That(reused.InputLayout, Is.EqualTo(AudioChannelLayout.Surround51));
    }

    private sealed class TransferNode(AudioBuffer buffer) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context) => buffer;
    }

    private sealed class TimelineInputNode(int channels) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            int start = (int)context.TimeRange.Start.ToSamples(context.SampleRate, MidpointRounding.ToZero);
            return AudioTestBuffers.CreateBuffer(channels, context.GetSampleCount(),
                (ch, i) => (((start + i) * 17 + ch * 11) % 101 - 50) / 128f, context.SampleRate);
        }
    }

    private sealed class ImpulseNode : AudioNode
    {
        protected override AudioBuffer CreateSilentFlush(AudioProcessContext context)
            => new(context.SampleRate, 1, context.GetSampleCount());
        public override AudioBuffer Process(AudioProcessContext context)
        {
            var buffer = CreateSilentFlush(context);
            if (context.TimeRange.Start == MediaTime.Zero) buffer.GetChannelData(0)[0] = 1;
            return buffer;
        }
    }
}
