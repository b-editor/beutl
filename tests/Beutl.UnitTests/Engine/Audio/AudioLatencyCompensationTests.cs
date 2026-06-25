using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioLatencyCompensationTests
{
    private const int SampleRate = 48000;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Log.LoggerFactory is null)
        {
            Log.LoggerFactory = LoggerFactory.Create(_ => { });
        }
    }

    private static int LookaheadSamples(float lookaheadMs, int sampleRate = SampleRate)
        => (int)(lookaheadMs / 1000f * sampleRate);

    // Threshold high enough that a unit-scale signal never trips limiting, so the limiter is a pure
    // lookahead delay and the tail-recovery math is exact.
    private static LimiterNode CreateTransparentLimiter(float lookaheadMs)
        => new()
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(lookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };

    private static AudioProcessContext Context(TimeSpan start, int sampleCount, int sampleRate = SampleRate)
    {
        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        return new AudioProcessContext(new TimeRange(start, duration), sampleRate, new AnimationSampler(), null);
    }

    [Test]
    public void Flush_RecoversTheTailHeldInTheDelayLine()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 4096;
        int L = LookaheadSamples(lookaheadMs);

        // A ramp makes every input index identifiable in the (delayed) output.
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));

        using var node = CreateTransparentLimiter(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        // Process the clip: output[i] = input[i - L]; the last L inputs stay stuck in the delay line.
        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));

        // Flush, contiguous with the processed chunk, must emit those last L inputs.
        var flushDuration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        using var tail = node.Flush(Context(flushDuration, sampleCount));

        var inData = input.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            // The j-th flushed sample carries input[sampleCount - L + k].
            Assert.That(tailData[k], Is.EqualTo(inData[sampleCount - L + k]).Within(1e-5f),
                $"Flushed tail sample {k} must equal the input sample lost off the processed tail.");
        }
    }

    [Test]
    public void ProcessThenFlush_ConcatenatesToTheFullDelayedInput_NoLoss()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 2048;
        int L = LookaheadSamples(lookaheadMs);

        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 330f * i / SampleRate));

        using var node = CreateTransparentLimiter(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        // processed[i] = input[i-L] for i>=L; tail[k] = input[sampleCount-L+k]. Concatenating processed
        // then the first L of tail reproduces input delayed by L with NO samples dropped.
        var inData = input.GetChannelData(0);
        var procData = processed.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int i = L; i < sampleCount; i++)
        {
            Assert.That(procData[i], Is.EqualTo(inData[i - L]).Within(1e-5f));
        }
        for (int k = 0; k < L; k++)
        {
            Assert.That(tailData[k], Is.EqualTo(inData[sampleCount - L + k]).Within(1e-5f));
        }
    }

    [Test]
    public void Flush_DefaultPassThrough_ReturnsSilence()
    {
        // A node with no latency and a leaf input drains to silence (nothing held).
        using var gain = new GainNode { Gain = Property.CreateAnimatable(100f) };
        using var buffer = CreateConstantBuffer(0.3f, 64);
        gain.AddInput(new BufferReplayNode(buffer));

        using var tail = gain.Flush(Context(TimeSpan.FromSeconds(64.0 / SampleRate), 32));

        var data = tail.GetChannelData(0);
        for (int i = 0; i < tail.SampleCount; i++)
        {
            Assert.That(data[i], Is.EqualTo(0f), "A latency-free chain flushes to silence.");
        }
    }

    [Test]
    public void Flush_DoesNotResetTheLimiter_StaysContiguousWithProcess()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);

        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var node = CreateTransparentLimiter(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        // If Flush were treated as a discontinuity, the limiter would Reset() and emit silence (cold
        // delay line) instead of the retained tail. A non-silent tail proves contiguity held.
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        var tailData = tail.GetChannelData(0);
        bool anyNonZero = false;
        for (int k = 0; k < L; k++)
        {
            if (MathF.Abs(tailData[k]) > 1e-6f) { anyNonZero = true; break; }
        }

        Assert.That(anyNonZero, Is.True, "Flush stayed contiguous; the delay line drained real audio, not a post-reset silence.");
    }

    [Test]
    public void ClipNode_TerminalWindow_AppendsRecoveredTail()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        // Source feeds the clip-local range; a sine so the recovered tail is identifiable and non-zero.
        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

        // A window that exactly covers the whole clip: it reaches the clip's true end, so ClipNode
        // drains the limiter tail into the trailing samples.
        using var output = clip.Process(Context(TimeSpan.Zero, clipSamples));

        var data = output.GetChannelData(0);
        bool tailNonZero = false;
        for (int i = clipSamples - L; i < clipSamples; i++)
        {
            if (MathF.Abs(data[i]) > 1e-5f) { tailNonZero = true; break; }
        }

        Assert.That(tailNonZero, Is.True,
            "The clip's final L samples, normally lost in the delay line, are recovered by the tail flush.");
    }

    [Test]
    public void ClipNode_ZeroLookahead_IsNoOp()
    {
        const int clipSamples = 2048;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        // No-latency chain: with L==0 the terminal-window drain must change nothing.
        var sourceA = new RangeSineNode(SampleRate);
        using var clipA = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipA.AddInput(sourceA);
        using var withDrain = clipA.Process(Context(TimeSpan.Zero, clipSamples));

        var sourceB = new RangeSineNode(SampleRate);
        using var clipB = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipB.AddInput(sourceB);
        // A mid-clip window (does not reach the end) never drains; compare its overlapping region.
        using var reference = clipB.Process(Context(TimeSpan.Zero, clipSamples));

        var a = withDrain.GetChannelData(0);
        var b = reference.GetChannelData(0);
        for (int i = 0; i < clipSamples; i++)
        {
            Assert.That(a[i], Is.EqualTo(b[i]).Within(1e-6f), $"L==0 drain must not perturb sample {i}.");
        }
    }

    [Test]
    public void Flush_AppliesDownstreamProcessing_ToTheTail()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 2048;
        int L = LookaheadSamples(lookaheadMs);

        // leaf -> Limiter(delay) -> Gain(0.5). The recovered tail must be scaled by the downstream
        // gain, not handed back raw — the regression guard for the bypassing default Flush.
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 330f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));
        using var gain = new GainNode { Gain = Property.CreateAnimatable(50f) }; // 50% = 0.5x
        gain.AddInput(limiter);

        using var processed = gain.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = gain.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        var inData = input.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            // Tail carries input[sampleCount-L+k] (limiter delay) scaled by 0.5 (downstream gain).
            Assert.That(tailData[k], Is.EqualTo(inData[sampleCount - L + k] * 0.5f).Within(1e-5f),
                $"Flushed tail sample {k} must have the downstream gain applied.");
        }
    }

    [Test]
    public void MixerNode_Flush_MergesBranchTails()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);

        // Branch A holds a limiter tail; branch B is silent. The mixer flush must surface A's tail.
        using var inputA = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiterA = CreateTransparentLimiter(lookaheadMs);
        limiterA.AddInput(new BufferReplayNode(inputA));
        using var silentB = new GainNode { Gain = Property.CreateAnimatable(100f) };
        using var bufferB = CreateConstantBuffer(0f, sampleCount);
        silentB.AddInput(new BufferReplayNode(bufferB));

        using var mixer = new MixerNode();
        mixer.AddInput(limiterA);
        mixer.AddInput(silentB);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = mixer.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        var tailData = tail.GetChannelData(0);
        bool anyNonZero = false;
        for (int k = 0; k < L; k++)
        {
            if (MathF.Abs(tailData[k]) > 1e-6f) { anyNonZero = true; break; }
        }

        Assert.That(anyNonZero, Is.True, "The mixer flush merged branch A's drained tail instead of returning silence.");
    }

    [Test]
    public void NestedClipNode_Flush_RemapsToClipLocalTime_RecoversTail()
    {
        // A SoundGroup mixes child clips and then flushes them through the group's own clip in the
        // GROUP's time domain. A nested ClipNode must remap that drain to its clip-local frame (as
        // Process does) or the child's cached limiter sees a discontinuity, Reset()s, and drops the tail.
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

        // A window ending exactly at the clip end is terminal but leaves no trailing room, so the
        // clip's own AppendFlushedTail cannot run (capacity == 0) and the limiter keeps holding its tail.
        using var processed = clip.Process(Context(TimeSpan.Zero, clipSamples));

        // The parent flushes in a time domain deliberately unrelated to the child's clip-local time.
        // The base Flush would forward this start to the limiter, trip the discontinuity guard, and
        // emit post-reset silence; the clip-local remap keeps the limiter contiguous and drains the tail.
        using var tail = clip.Flush(Context(TimeSpan.FromSeconds(123.0), L));

        var tailData = tail.GetChannelData(0);
        bool anyNonZero = false;
        for (int k = 0; k < L; k++)
        {
            if (MathF.Abs(tailData[k]) > 1e-6f) { anyNonZero = true; break; }
        }

        Assert.That(anyNonZero, Is.True,
            "A nested ClipNode flushed in a parent's time domain must remap to clip-local time so the "
            + "cached limiter stays contiguous and drains its held tail instead of resetting to silence.");
    }

    [Test]
    public void NestedClipNode_Flush_DrainsFromLastProcessedLocalTime_WhenParentTrimsTheClip()
    {
        // A SoundGroup window can stop before a child's own Duration, trimming it. The child's last
        // Process then ended at the trim boundary, not its natural end, so the flush must drain from that
        // last processed local time — draining from Duration would jump past the cached limiter, reset it,
        // and drop the tail held at the trim boundary.
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 8192;        // the child's natural Duration
        const int processedSamples = 4096;   // the parent trims the child here, before its end

        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);
        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

        // Only the first processedSamples are pulled, so the limiter's last processed local end is there
        // and its delay line holds the tail from around that trim boundary.
        using var processed = clip.Process(Context(TimeSpan.Zero, processedSamples));

        using var tail = clip.Flush(Context(TimeSpan.FromSeconds(99.0), L));

        var tailData = tail.GetChannelData(0);
        bool anyNonZero = false;
        for (int k = 0; k < L; k++)
        {
            if (MathF.Abs(tailData[k]) > 1e-6f) { anyNonZero = true; break; }
        }

        Assert.That(anyNonZero, Is.True,
            "A clip trimmed by its parent must flush from its last processed local time so the cached "
            + "limiter stays contiguous and drains the tail held at the trim boundary, not at Duration.");
    }

    [Test]
    public void Flush_FanInWithoutOverride_Throws()
    {
        // A bare multi-input node has no merge semantics; the base Flush must fail loudly, not drop tails.
        using var node = new GainNode { Gain = Property.CreateAnimatable(100f) };
        using var a = CreateConstantBuffer(0.1f, 16);
        using var b = CreateConstantBuffer(0.1f, 16);
        node.AddInput(new BufferReplayNode(a));
        node.AddInput(new BufferReplayNode(b));

        Assert.Throws<InvalidOperationException>(() => node.Flush(Context(TimeSpan.Zero, 16)));
    }

    [Test]
    public void LimiterNode_AnimatedLookahead_ReportsWorstCaseLatency()
    {
        // Base 0 ms but automation rising to 20 ms must reserve the full worst-case drain, or the tail
        // is dropped. A static 0 ms (no animation) still reports 0.
        var animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0f, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 20f, Easing = new LinearEasing() },
            },
        };
        var lookahead = Property.CreateAnimatable(0f);
        lookahead.Animation = animation;

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = lookahead,
            MakeupGain = Property.CreateAnimatable(0f),
        };

        Assert.That(node.GetLatencySamples(SampleRate),
            Is.EqualTo(LookaheadSamples(LimiterParameters.MaxLookaheadMs)),
            "Animated lookahead must report the worst case so the drain reserves enough room.");

        // The effect-level API a host queries before graph construction must agree with the node.
        var effect = new LimiterEffect();
        effect.Lookahead.Animation = animation;
        Assert.That(effect.GetLatencySamples(SampleRate),
            Is.EqualTo(LookaheadSamples(LimiterParameters.MaxLookaheadMs)));
    }

    // Source that honors the requested clip-local range: sample value keyed to the absolute clip-local
    // index so a downstream node's output is identifiable, and reads past the clip end return silence
    // (mirrors SourceNode returning silence for out-of-clip reads, the precondition Flush relies on).
    private sealed class RangeSineNode(int sampleRate) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(sampleRate, 2, count);
            long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
            for (int ch = 0; ch < 2; ch++)
            {
                var data = buffer.GetChannelData(ch);
                for (int i = 0; i < count; i++)
                {
                    data[i] = 0.25f * MathF.Sin(2f * MathF.PI * 200f * (startIndex + i) / sampleRate);
                }
            }

            return buffer;
        }
    }
}
