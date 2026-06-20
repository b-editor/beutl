using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SpeedNodeTests
{
    private const int SampleRate = 100;

    private static IProperty<float> AnimatedSpeed(float fromPercent, float toPercent, double durationSeconds)
    {
        var property = Property.CreateAnimatable(fromPercent);
        property.SetAttributes("Speed", []);

        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = fromPercent,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(durationSeconds),
            Value = toPercent,
            Easing = new LinearEasing()
        });
        property.Animation = animation;
        return property;
    }

    private static SpeedNode CreateAnimatedSpeedNode()
    {
        var node = new SpeedNode { Speed = AnimatedSpeed(50f, 200f, 1.0) };
        node.AddInput(new RampInputNode(SampleRate));
        return node;
    }

    private static AudioProcessContext CreateContext()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        return new AudioProcessContext(range, SampleRate, new AnimationSampler(), null);
    }

    private static IProperty<float> StaticSpeed(float percent)
    {
        var property = Property.CreateAnimatable(percent);
        property.SetAttributes("Speed", []);
        return property;
    }

    // Renders consecutive 1-second chunks through the SAME node, as the player feeds the composer
    // during playback.
    private static List<float> RenderChunks(SpeedNode node, int sampleRate, int channel, params double[] startSeconds)
    {
        var sampler = new AnimationSampler();
        var samples = new List<float>();
        foreach (double start in startSeconds)
        {
            var range = new TimeRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(1));
            using AudioBuffer buffer = node.Process(new AudioProcessContext(range, sampleRate, sampler, null));
            samples.AddRange(buffer.GetChannelData(channel).ToArray());
        }

        return samples;
    }

    // Regression for the "click at every chunk boundary at half speed" bug: the old code re-seeked the
    // source every chunk, leaving a discontinuity at each seam. Here the ramp must stay continuous
    // (per-sample slope ~0.005) right across the boundaries.
    [Test]
    public void ProcessStaticSpeed_HalfSpeed_IsContinuousAcrossChunkBoundaries()
    {
        const int sr = 1000;
        const float expectedSlope = 0.005f; // ramp slope 0.01/source-sample * 0.5 speed
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        node.AddInput(new RampInputNode(sr));

        List<float> left = RenderChunks(node, sr, channel: 0, 0.0, 1.0, 2.0);

        // Skip the filter warm-up; every later step (including the seams at 1000/2000) must stay near
        // the steady-state slope, i.e. no click.
        for (int i = 200; i < left.Count; i++)
        {
            float diff = left[i] - left[i - 1];
            Assert.That(diff, Is.EqualTo(expectedSlope).Within(0.01f),
                $"discontinuity at sample {i} (boundary at multiples of {sr}): diff={diff}");
        }
    }

    // The stream must be correct, not just smooth: half speed maps output sample n to source position
    // n*0.5, so the left ramp reads n*0.005 and the right channel is its negation.
    [Test]
    public void ProcessStaticSpeed_HalfSpeed_ProducesExpectedRampValues()
    {
        const int sr = 1000;
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        node.AddInput(new RampInputNode(sr));

        List<float> left = RenderChunks(node, sr, channel: 0, 0.0, 1.0);
        using var node2 = new SpeedNode { Speed = StaticSpeed(50f) };
        node2.AddInput(new RampInputNode(sr));
        List<float> right = RenderChunks(node2, sr, channel: 1, 0.0, 1.0);

        for (int i = 200; i < left.Count; i++)
        {
            Assert.That(left[i], Is.EqualTo(i * 0.005f).Within(0.05f), $"left ramp wrong at {i}");
            Assert.That(right[i], Is.EqualTo(-left[i]).Within(1e-4f), $"right channel not negated at {i}");
        }
    }

    // After a non-contiguous jump (scrub / loop), the stream must re-anchor to the new position, not
    // keep streaming from where the previous chunk left off.
    [Test]
    public void ProcessStaticSpeed_HalfSpeed_ReanchorsAfterSeek()
    {
        const int sr = 1000;
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        node.AddInput(new RampInputNode(sr));

        var sampler = new AnimationSampler();

        // Warm up at the start, then seek far ahead to output [10s, 11s).
        using (AudioBuffer _ = node.Process(
                   new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sr, sampler, null)))
        {
        }

        using AudioBuffer seeked = node.Process(
            new AudioProcessContext(new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)), sr, sampler, null));

        Span<float> left = seeked.GetChannelData(0);
        // Output sample 10500 -> source 5250 -> value 52.5. Without re-anchoring it would continue from
        // ~0.5s of source (~value 5), so the tolerance is tight.
        Assert.That(left[500], Is.EqualTo((10000 + 500) * 0.005f).Within(0.05f));
    }

    // The animated path shares the same streaming loop. A constant 50% keyframed speed must stay as
    // continuous across seams as the constant-speed path — guards the removal of the hand-rolled cursor.
    [Test]
    public void ProcessAnimatedSpeed_ConstantHalfSpeed_IsContinuousAndCorrectAcrossChunks()
    {
        const int sr = 1000;
        const float expectedSlope = 0.005f;
        using var node = new SpeedNode { Speed = AnimatedSpeed(50f, 50f, 100.0) };
        node.AddInput(new RampInputNode(sr));

        List<float> left = RenderChunks(node, sr, channel: 0, 0.0, 1.0, 2.0);

        for (int i = 200; i < left.Count; i++)
        {
            float diff = left[i] - left[i - 1];
            Assert.That(diff, Is.EqualTo(expectedSlope).Within(0.01f),
                $"discontinuity at sample {i} (boundary at multiples of {sr}): diff={diff}");
            Assert.That(left[i], Is.EqualTo(i * 0.005f).Within(0.05f), $"ramp wrong at {i}");
        }
    }

    // Deterministic finite stereo source: a ramp for the first _length samples, silence beyond. Models
    // SourceNode zero-filling past end-of-source.
    private sealed class FiniteRampInputNode : AudioNode
    {
        private readonly int _sampleRate;
        private readonly int _length;

        public FiniteRampInputNode(int sampleRate, int length)
        {
            _sampleRate = sampleRate;
            _length = length;
        }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(_sampleRate, 2, count);
            Span<float> left = buffer.GetChannelData(0);
            Span<float> right = buffer.GetChannelData(1);
            long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, _sampleRate);
            for (int i = 0; i < count; i++)
            {
                long idx = startIndex + i;
                float v = idx >= 0 && idx < _length ? idx * 0.01f : 0f;
                left[i] = v;
                right[i] = -v;
            }

            return buffer;
        }
    }

    // Past end-of-source the stream must decay to silence, not loop, hold, or emit stale resampler
    // data. Half speed maps the 300-sample source onto output samples [0, 600).
    [Test]
    public void ProcessStaticSpeed_HalfSpeed_FiniteSource_DecaysToSilenceAfterEnd()
    {
        const int sr = 1000;
        const int sourceLength = 300; // source samples available
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        node.AddInput(new FiniteRampInputNode(sr, sourceLength));

        var sampler = new AnimationSampler();
        using AudioBuffer buffer = node.Process(
            new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sr, sampler, null));

        Span<float> left = buffer.GetChannelData(0);
        // All finite, no NaN/Inf leaking from the resampler flush.
        for (int i = 0; i < left.Length; i++)
            Assert.That(float.IsFinite(left[i]), Is.True, $"sample {i} not finite ({left[i]})");

        // Well before end-of-source the ramp is present...
        Assert.That(left[400], Is.EqualTo(400 * 0.005f).Within(0.05f), "ramp missing before end-of-source");

        // ...and the entire tail past the mapped end-of-source (output 600 plus resampler latency)
        // must be silent — no residual, oscillation, or looped data.
        for (int i = 700; i < left.Length; i++)
            Assert.That(Math.Abs(left[i]), Is.LessThan(0.05f), $"residual at {i} after end-of-source ({left[i]})");
    }

    private sealed class SilentInputNode : AudioNode
    {
        private readonly int _sampleRate;

        public SilentInputNode(int sampleRate) => _sampleRate = sampleRate;

        public override AudioBuffer Process(AudioProcessContext context)
            => new(_sampleRate, 2, context.GetSampleCount());
    }

    // Forwards Process unchanged. Stands in for the ResampleNode the graph keeps between SpeedNode and
    // the source, reused by sample rate so its instance survives a source/offset change.
    private sealed class PassthroughNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context) => Inputs[0].Process(context);
    }

    // Re-anchor on a static-speed change across contiguous chunks: BeginStream's output-time comparison
    // cannot see it, so without the re-anchor the read cursor keeps marching at the old speed instead
    // of jumping to outputStart * newSpeed.
    [Test]
    public void ProcessStaticSpeed_ReanchorsWhenStaticSpeedChangesMidStream()
    {
        const int sr = 1000;
        var speedProp = StaticSpeed(50f);
        using var node = new SpeedNode { Speed = speedProp };
        node.AddInput(new RampInputNode(sr));
        var sampler = new AnimationSampler();

        // Build up the streaming cursor/filter state over one contiguous half-speed chunk.
        using (AudioBuffer _ = node.Process(
                   new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sr, sampler, null)))
        {
        }

        // Change the static speed while playback continues onto the next contiguous chunk.
        speedProp.CurrentValue = 200f;

        using AudioBuffer after = node.Process(
            new AudioProcessContext(new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), sr, sampler, null));

        Span<float> left = after.GetChannelData(0);
        // Output sample i -> source position (1 + i/sr) * 2 s -> ramp value (sourcePos * sr) * 0.01.
        // Skip the warm-up after re-anchor. Without re-anchoring it would read from ~0.5s of source
        // (value ~5 + 0.02*i), so the tolerance is tight.
        for (int i = 300; i < left.Length; i++)
        {
            float expected = (float)((1.0 + i / (double)sr) * 2.0) * sr * 0.01f;
            Assert.That(left[i], Is.EqualTo(expected).Within(0.1f),
                $"static-speed change did not re-anchor at sample {i} (value={left[i]}, expected={expected})");
        }
    }

    // Guards the transitive-upstream identity check: a graph update can reuse the intermediate
    // ResampleNode (so Inputs[0] is unchanged) while recreating the source behind it. The processor
    // must still reset, or the old source's filter history and read cursor bleed into the new stream.
    [Test]
    public void Process_ResetsStreamingStateWhenUpstreamBehindReusedNodeIsSwapped()
    {
        const int sr = 1000;
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        var reused = new PassthroughNode();
        reused.AddInput(new RampInputNode(sr));
        node.AddInput(reused);
        var sampler = new AnimationSampler();

        // Build up resampler filter state from the loud ramp source over one contiguous chunk.
        using (AudioBuffer _ = node.Process(
                   new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sr, sampler, null)))
        {
        }

        // Swap the source two levels up while keeping the immediate input (reused) unchanged.
        reused.ClearInputs();
        reused.AddInput(new SilentInputNode(sr));

        using AudioBuffer after = node.Process(
            new AudioProcessContext(new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), sr, sampler, null));

        Span<float> left = after.GetChannelData(0);
        for (int i = 0; i < left.Length; i++)
            Assert.That(Math.Abs(left[i]), Is.LessThan(0.05f), $"stale ramp history leaked at {i} ({left[i]})");
    }

    // Guards the upstream identity check: a graph update can reuse the node but swap its upstream. Even
    // though the next chunk is time-contiguous, the old ramp source's filter history must NOT bleed into
    // the new (silent) source — without the reset it would ring out as non-zero samples.
    [Test]
    public void Process_ResetsStreamingStateWhenUpstreamIsSwapped()
    {
        const int sr = 1000;
        using var node = new SpeedNode { Speed = StaticSpeed(50f) };
        node.AddInput(new RampInputNode(sr));
        var sampler = new AnimationSampler();

        // Build up resampler filter state from the loud ramp source over one contiguous chunk.
        using (AudioBuffer _ = node.Process(
                   new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), sr, sampler, null)))
        {
        }

        // Swap the upstream (as AudioContext.CreateSpeedNode does via ClearInputs) to a silent source.
        node.ClearInputs();
        node.AddInput(new SilentInputNode(sr));

        using AudioBuffer after = node.Process(
            new AudioProcessContext(new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), sr, sampler, null));

        Span<float> left = after.GetChannelData(0);
        for (int i = 0; i < left.Length; i++)
            Assert.That(Math.Abs(left[i]), Is.LessThan(0.05f), $"stale ramp history leaked at {i} ({left[i]})");
    }

    [Test]
    public void ProcessAnimatedSpeed_ProducesExpectedLengthAndFiniteSamples()
    {
        using var node = CreateAnimatedSpeedNode();
        var context = CreateContext();

        using AudioBuffer result = node.Process(context);

        Assert.That(result.SampleCount, Is.EqualTo(context.GetSampleCount()));
        Assert.That(result.ChannelCount, Is.EqualTo(2));

        for (int ch = 0; ch < result.ChannelCount; ch++)
        {
            Span<float> data = result.GetChannelData(ch);
            for (int i = 0; i < data.Length; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"channel {ch} sample {i} was not finite ({data[i]}).");
            }
        }
    }

    // Guards the ArrayPool usage in ProcessAnimatedSpeed: two fresh nodes fed identical input must
    // produce identical output, so a pooled speed buffer with leftover stale data would diverge.
    // A node is stateful across renders (WdlResampler filter state), hence a fresh node per render.
    [Test]
    public void ProcessAnimatedSpeed_IsDeterministicAcrossFreshInstances()
    {
        float[][] RenderFresh()
        {
            using var node = CreateAnimatedSpeedNode();
            var context = CreateContext();
            using AudioBuffer buffer = node.Process(context);
            return
            [
                buffer.GetChannelData(0).ToArray(),
                buffer.GetChannelData(1).ToArray(),
            ];
        }

        float[][] first = RenderFresh();
        float[][] second = RenderFresh();

        Assert.Multiple(() =>
        {
            Assert.That(second[0], Is.EqualTo(first[0]).AsCollection);
            Assert.That(second[1], Is.EqualTo(first[1]).AsCollection);
        });
    }
}
