using System.Collections.Immutable;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
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

        // A window that extends L samples past the clip end leaves trailing capacity, so AppendFlushedTail
        // drains the limiter's held tail into [clipSamples, clipSamples + L) — the region inline
        // processing drops. A window covering the clip exactly has capacity == 0 and never drains, so the
        // assertion below would pass on the main slice even if recovery were a no-op.
        using var output = clip.Process(Context(TimeSpan.Zero, clipSamples + L));

        var data = output.GetChannelData(0);
        bool tailNonZero = false;
        for (int i = clipSamples; i < clipSamples + L; i++)
        {
            if (MathF.Abs(data[i]) > 1e-5f) { tailNonZero = true; break; }
        }

        Assert.That(tailNonZero, Is.True,
            "The limiter's held tail, normally lost in the delay line, is recovered into the trailing L samples.");
    }

    [Test]
    public void ClipNode_ZeroLookahead_IsNoOp()
    {
        const int clipSamples = 2048;
        const int refOffset = 256;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        // No-latency chain: with L==0 the terminal-window drain must change nothing.
        var sourceA = new RangeSineNode(SampleRate);
        using var clipA = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipA.AddInput(sourceA);
        using var withDrain = clipA.Process(Context(TimeSpan.Zero, clipSamples));

        var sourceB = new RangeSineNode(SampleRate);
        using var clipB = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipB.AddInput(sourceB);
        // The reference stops short of the clip end, so its terminal-drain branch never runs; clipA
        // reaches the end and does enter it. Comparing the overlap gives the assertion discriminating
        // power: if the L==0 terminal path perturbed the main slice, only clipA would differ.
        using var reference = clipB.Process(Context(TimeSpan.Zero, clipSamples - refOffset));

        var a = withDrain.GetChannelData(0);
        var b = reference.GetChannelData(0);
        for (int i = 0; i < clipSamples - refOffset; i++)
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

    [TestCase(false)]
    [TestCase(true)]
    public void SpeedNode_Flush_MapsTheDrainToTheUpstreamSourceTimeline(bool animated)
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 4096;
        int L = LookaheadSamples(lookaheadMs);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);
        var speedProperty = Property.CreateAnimatable(50f);
        if (animated)
        {
            speedProperty.Animation = new KeyFrameAnimation<float>
            {
                KeyFrames =
                {
                    new KeyFrame<float>
                    {
                        KeyTime = TimeSpan.Zero,
                        Value = 50f,
                        Easing = new LinearEasing(),
                    },
                    new KeyFrame<float>
                    {
                        KeyTime = TimeSpan.FromSeconds(10),
                        Value = 50f,
                        Easing = new LinearEasing(),
                    },
                },
            };
        }

        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        using var processed = speed.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = speed.Flush(
            Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(HasNonZero(tail.GetChannelData(0)[..(L * 2)]), Is.True,
            "A non-unity SpeedNode must drain the upstream limiter at the resampler's source cursor, "
            + "not forward the output-domain time and reset the limiter.");
    }

    [TestCase(50f)]
    [TestCase(200f)]
    public void SpeedNode_Flush_AfterUnityProcessAndSpeedChange_UsesTrackedSourceCursor(float drainSpeed)
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 4096;

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);
        var speedProperty = Property.CreateAnimatable(100f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        using var processed = speed.Process(Context(TimeSpan.Zero, sampleCount));
        speedProperty.CurrentValue = drainSpeed;
        using var tail = speed.Flush(
            Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(HasNonZero(tail.GetChannelData(0)), Is.True,
            "A SpeedNode that processed at unity must retain the upstream source cursor when the "
            + "speed changes before its drain.");
    }

    [TestCase(50f)]
    [TestCase(200f)]
    public void SpeedNode_Flush_WhenSpeedChangesToUnity_KeepsTheMappedSourceCursor(float processSpeed)
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 4096;

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);
        var speedProperty = Property.CreateAnimatable(processSpeed);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        using var processed = speed.Process(Context(TimeSpan.Zero, sampleCount));
        speedProperty.CurrentValue = 100f;
        using var tail = speed.Flush(
            Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(HasNonZero(tail.GetChannelData(0)), Is.True,
            "Switching to unity for the drain must not bypass the resampler's retained source cursor.");
    }

    [TestCase(50f)]
    [TestCase(200f)]
    public void SpeedNode_Flush_AfterAnimatedProcessAndStaticUnityTransition_RequestsUnityRate(
        float animatedSpeed)
    {
        const int sampleCount = 4096;

        using var input = new RecordingFlushRequestNode();
        var speedProperty = Property.CreateAnimatable(animatedSpeed);
        speedProperty.Animation = ConstantSpeedAnimation(animatedSpeed);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(input);

        using var processed = speed.Process(Context(TimeSpan.Zero, sampleCount));
        speedProperty.Animation = null;
        speedProperty.CurrentValue = 100f;
        using var tail = speed.Flush(
            Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(input.TotalFlushedSamples, Is.InRange(sampleCount - 256, sampleCount + 256),
            "Switching from animated speed to static unity must reconfigure the resampler to a "
            + "one-source-sample-per-output-sample drain.");
        Assert.That(input.FirstFlushStart, Is.Not.Null);
        Assert.That(input.LastProcessedEnd, Is.Not.Null);
        Assert.That(
            Math.Abs((input.FirstFlushStart!.Value - input.LastProcessedEnd!.Value).Ticks),
            Is.LessThanOrEqualTo(1),
            "The animated-to-static transition must preserve the exact upstream source cursor; "
            + "forwarding the output-domain start would reset a stateful upstream node.");
    }

    [Test]
    public void SpeedNode_Flush_WhenReturningToPreviousStaticSpeed_ReconfiguresAfterAnimation()
    {
        const int sampleCount = 4096;
        var chunkDuration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        using var input = new RecordingFlushRequestNode();
        var speedProperty = Property.CreateAnimatable(50f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(input);

        using var staticChunk = speed.Process(Context(TimeSpan.Zero, sampleCount));
        speedProperty.Animation = ConstantSpeedAnimation(200f);
        using var animatedChunk = speed.Process(Context(chunkDuration, sampleCount));
        speedProperty.Animation = null;
        speedProperty.CurrentValue = 50f;
        using var tail = speed.Flush(Context(chunkDuration + chunkDuration, sampleCount));

        int expectedSourceSamples = sampleCount / 2;
        Assert.That(
            input.TotalFlushedSamples,
            Is.InRange(expectedSourceSamples - 256, expectedSourceSamples + 256),
            "Returning to the same static value used before animation must still restore the static "
            + "rate and filter instead of retaining the animated 200% configuration.");
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
    public void MixerNode_Flush_SkipsBranchThatEndedBeforeTheTerminalSlice()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var earlyEnd = TimeSpan.FromSeconds((double)(sampleCount / 2) / SampleRate);

        // Branch A holds a real limiter tail but its clip ended at the group midpoint — its tail was
        // already recovered at its own end, so the group's terminal flush must NOT re-emit it seconds
        // late. Branch B runs to the group end and is silent. With A correctly skipped, the merge is
        // silent; draining A unconditionally would leak its stale tail into the group pad.
        using var inputA = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiterA = CreateTransparentLimiter(lookaheadMs);
        limiterA.AddInput(new BufferReplayNode(inputA));
        using var limiterB = CreateTransparentLimiter(lookaheadMs);
        using var bufferB = CreateConstantBuffer(0f, sampleCount);
        limiterB.AddInput(new BufferReplayNode(bufferB));

        using var mixer = new MixerNode();
        mixer.AddInput(limiterA);
        mixer.AddInput(limiterB);
        mixer.SetBranchEndTime(limiterA, earlyEnd);
        mixer.SetBranchEndTime(limiterB, groupEnd);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = mixer.Flush(Context(groupEnd, sampleCount));

        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(tailData[k], Is.EqualTo(0f).Within(1e-6f),
                $"Branch A ended before the terminal slice; its stale tail must not be mixed into the group pad (sample {k}).");
        }
    }

    [Test]
    public void MixerNode_RemoveInput_KeepsBranchEndTimeAligned()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var earlyEnd = TimeSpan.FromSeconds((double)(sampleCount / 2) / SampleRate);

        using var inputA = CreateConstantBuffer(0f, sampleCount);
        using var limiterA = CreateTransparentLimiter(lookaheadMs);
        limiterA.AddInput(new BufferReplayNode(inputA));
        using var inputB = CreateBuffer(2, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiterB = CreateTransparentLimiter(lookaheadMs);
        limiterB.AddInput(new BufferReplayNode(inputB));

        using var mixer = new MixerNode();
        mixer.AddInput(limiterA);
        mixer.AddInput(limiterB);
        mixer.SetBranchEndTime(limiterA, earlyEnd);
        mixer.SetBranchEndTime(limiterB, groupEnd);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        mixer.RemoveInput(limiterA);
        using var tail = mixer.Flush(Context(groupEnd, sampleCount));

        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "Removing branch A must move branch B's end time with it so B remains live during flush.");
    }

    [Test]
    public void MixerNode_ClearInputs_DropsStaleBranchEndTimes()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var earlyEnd = TimeSpan.FromSeconds((double)(sampleCount / 2) / SampleRate);

        using var staleInput = CreateConstantBuffer(0f, sampleCount);
        using var staleLimiter = CreateTransparentLimiter(lookaheadMs);
        staleLimiter.AddInput(new BufferReplayNode(staleInput));

        using var liveInput = CreateBuffer(2, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var liveLimiter = CreateTransparentLimiter(lookaheadMs);
        liveLimiter.AddInput(new BufferReplayNode(liveInput));

        using var mixer = new MixerNode();
        mixer.AddInput(staleLimiter);
        mixer.SetBranchEndTime(staleLimiter, earlyEnd);
        mixer.ClearInputs();
        mixer.AddInput(liveLimiter);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = mixer.Flush(Context(groupEnd, sampleCount));

        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "A branch added after ClearInputs must not inherit liveness metadata from the old topology.");
    }

    [Test]
    public void MixerNode_AddInput_WithoutBranchEndTime_RemainsLive()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        using var staleInput = CreateConstantBuffer(0f, sampleCount);
        using var staleLimiter = CreateTransparentLimiter(lookaheadMs);
        staleLimiter.AddInput(new BufferReplayNode(staleInput));

        using var liveInput = CreateBuffer(2, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var liveLimiter = CreateTransparentLimiter(lookaheadMs);
        liveLimiter.AddInput(new BufferReplayNode(liveInput));

        using var mixer = new MixerNode();
        mixer.AddInput(staleLimiter);
        mixer.SetBranchEndTime(staleLimiter, TimeSpan.Zero);
        mixer.AddInput(liveLimiter);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = mixer.Flush(Context(groupEnd, sampleCount));

        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "A dynamically added branch without an end time must remain live during flush.");
    }

    [Test]
    public void MixerNode_ClearBranchEndTime_MakesBranchLiveAgain()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        using var input = CreateBuffer(2, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));

        using var mixer = new MixerNode();
        mixer.AddInput(limiter);
        mixer.SetBranchEndTime(limiter, TimeSpan.Zero);

        Assert.That(mixer.ClearBranchEndTime(limiter), Is.True);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = mixer.Flush(Context(groupEnd, sampleCount));

        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "Clearing a branch end time must return the connected branch to the live state.");
    }

    [Test]
    public void MixerNode_ClearInputs_WhenEmpty_DropsStaleGains()
    {
        const int sampleCount = 64;

        using var input = CreateConstantBuffer(0.25f, sampleCount);
        using var inputNode = new BufferReplayNode(input);
        using var mixer = new MixerNode { Gains = [0f] };

        mixer.ClearInputs();
        mixer.AddInput(inputNode);

        using var processed = mixer.Process(Context(TimeSpan.Zero, sampleCount));

        Assert.That(processed.GetChannelData(0)[0], Is.EqualTo(0.25f).Within(1e-6f),
            "ClearInputs must clear connection metadata even when the mixer is already empty.");
    }

    [Test]
    public void MixerNode_SetBranchEndTime_RejectsDisconnectedInput()
    {
        using var buffer = CreateConstantBuffer(0f, 64);
        using var input = new BufferReplayNode(buffer);
        using var mixer = new MixerNode();

        Assert.That(
            () => mixer.SetBranchEndTime(input, TimeSpan.Zero),
            Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("input"));
    }

    [Test]
    public void AudioContext_Topology_UsesReferenceIdentityForValueEqualNodes()
    {
        using var first = new ValueEqualAudioNode();
        using var second = new ValueEqualAudioNode();
        using var mixer = new MixerNode();
        using var context = new AudioContext(SampleRate, 2);

        context.AddNode(first);
        context.AddNode(second);
        context.Connect(first, mixer);
        context.Connect(second, mixer);

        Assert.That(context.Nodes, Has.Count.EqualTo(3));
        Assert.That(mixer.Inputs, Has.Count.EqualTo(2));
        Assert.That(mixer.Inputs[0], Is.SameAs(first));
        Assert.That(mixer.Inputs[1], Is.SameAs(second));

        context.RemoveNode(second);

        Assert.That(context.Nodes, Has.Count.EqualTo(2));
        Assert.That(mixer.Inputs, Has.Count.EqualTo(1));
        Assert.That(mixer.Inputs[0], Is.SameAs(first));
    }

    [Test]
    public void MixerNode_Dispose_ReleasesBranchEndMetadata()
    {
        (MixerNode mixer, WeakReference inputReference) = CreateDisposedMixerWithBranchEndMetadata();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(inputReference.IsAlive, Is.False);
        GC.KeepAlive(mixer);
    }

    [Test]
    public void MixerNode_AllDeadFlush_PreservesProcessedChannelLayout()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        using var input = CreateBuffer(1, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var inputNode = new BufferReplayNode(input);
        using var mixer = new MixerNode();
        mixer.AddInput(inputNode);
        mixer.SetBranchEndTime(inputNode, TimeSpan.Zero);

        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(mixer);

        using var processed = limiter.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = limiter.Flush(Context(groupEnd, sampleCount));

        Assert.That(processed.ChannelCount, Is.EqualTo(1));
        Assert.That(tail.ChannelCount, Is.EqualTo(1),
            "An all-dead mixer flush must keep the mono layout seen by the downstream limiter.");
        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "Preserving the layout must keep the downstream limiter from resetting and dropping its tail.");
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
    public void NestedClipNode_Flush_ContinuesAfterPartialTailAppend()
    {
        // A terminal window with some trailing pad — but less than the reported latency — drains only
        // part of the held tail and advances the upstream chain past Duration. A later parent flush of
        // the same child must continue from that advanced point; restarting at Duration would step the
        // cached limiter backward, reset it, and drop the rest of the tail.
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        int pad = L / 2;                     // trailing room < L => only a partial append
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

        // Window covers the clip plus `pad` samples: AppendFlushedTail drains `pad` of the L held samples.
        using var processed = clip.Process(Context(TimeSpan.Zero, clipSamples + pad));

        using var tail = clip.Flush(Context(TimeSpan.FromSeconds(77.0), L));

        var tailData = tail.GetChannelData(0);
        bool anyNonZero = false;
        for (int k = 0; k < L - pad; k++)
        {
            if (MathF.Abs(tailData[k]) > 1e-6f) { anyNonZero = true; break; }
        }

        Assert.That(anyNonZero, Is.True,
            "After a partial tail append, the parent flush must continue from the advanced drain position "
            + "so the remaining held samples are recovered, not dropped by a backward-discontinuity reset.");
    }

    [Test]
    public void NestedClipNode_PartialTailAppend_At44100Hz_DoesNotSkipASample()
    {
        const int sampleRate = 44100;
        const int clipSamples = 4096;
        const int pad = 3087;
        const int latency = 4096;
        var clipDuration = ExactDuration(clipSamples, sampleRate);

        using var input = new RecordingLatencyNode(latency);
        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(input);

        using var processed = clip.Process(ExactContext(TimeSpan.Zero, clipSamples + pad, sampleRate));

        Assert.That(input.LastFlushSampleCount, Is.EqualTo(pad),
            "The partial drain must request exactly 3087 samples; a rounded-up 3088th sample would be "
            + "discarded while advancing a stateful input and shift the later flush by one.");
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

    [Test]
    public void Flush_AnimatedLookaheadDroppingToZero_StillRecoversHeldTail()
    {
        const float lookaheadMs = 5f;
        const int sampleCount = 4096;
        int L = LookaheadSamples(lookaheadMs);
        var clipDuration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var oneSample = TimeSpan.FromSeconds(1.0 / SampleRate);

        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));

        // Lookahead holds 5 ms across the whole clip, then keyframes to 0 one sample past the clip end.
        // The last L inputs were buffered at the 5 ms delay; a drain that re-samples the now-zero
        // automation reads delay offset 0 (the flush silence) and drops them. Draining at the lookahead
        // retained from the clip's terminal sample must still recover them.
        var animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = lookaheadMs, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = clipDuration, Value = lookaheadMs, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = clipDuration + oneSample, Value = 0f, Easing = new LinearEasing() },
            },
        };
        var lookahead = Property.CreateAnimatable(lookaheadMs);
        lookahead.Animation = animation;

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = lookahead,
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(clipDuration, sampleCount));

        var inData = input.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(tailData[k], Is.EqualTo(inData[sampleCount - L + k]).Within(1e-5f),
                $"Animated-lookahead drain must recover held tail sample {k} at the retained lookahead, not the decayed value.");
        }
    }

    [Test]
    public void Flush_AnimatedMakeupGain_StaysLiveWithStaticLookahead()
    {
        const float lookaheadMs = 5f;
        const float makeupDb = 6f;
        const int sampleCount = 4096;
        int L = LookaheadSamples(lookaheadMs);
        var clipDuration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        var oneSample = TimeSpan.FromSeconds(1.0 / SampleRate);

        using var input = CreateConstantBuffer(0.1f, sampleCount);

        var animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0f, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = clipDuration - oneSample, Value = 0f, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = clipDuration, Value = makeupDb, Easing = new LinearEasing() },
            },
        };
        var makeup = Property.CreateAnimatable(0f);
        makeup.Animation = animation;

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(lookaheadMs),
            MakeupGain = makeup,
        };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(clipDuration, sampleCount));

        float expected = 0.1f * AudioMath.ConvertDbToLinear(makeupDb);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(tailData[k], Is.EqualTo(expected).Within(1e-5f),
                $"Flush sample {k} must use the drain-range makeup automation while retaining the terminal lookahead.");
        }
    }

    [Test]
    public void Composer_FlushesSoundEndingExactlyAtTheWindowBoundary()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        var oneSecond = TimeSpan.FromSeconds(1);

        // The sound spans exactly [0, 1s) and carries a 5 ms lookahead limiter. Window 1 covers it
        // exactly, so the clip's terminal window is full (capacity 0) and the held tail cannot
        // self-recover. Window 2 excludes the sound (it ended), so only a Composer that flushes the
        // just-ended sound recovers the tail into the start of window 2.
        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, oneSecond),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, oneSecond);
        var eligibility = new CompositionEligibility([sound]);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            eligibility);
        using var buffer1 = composer.Compose(window1, frame1);

        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var tail = buffer2!.GetChannelData(0);
        bool tailNonZero = false;
        for (int k = 0; k < L; k++)
        {
            if (MathF.Abs(tail[k]) > 1e-5f) { tailNonZero = true; break; }
        }

        Assert.That(tailNonZero, Is.True,
            "A sound ending exactly at the window boundary must have its limiter tail flushed into the next window's start.");
    }

    [Test]
    public void Composer_DoesNotFlushSoundThatBecomesIneligibleAtItsNaturalEnd()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        var oneSecond = TimeSpan.FromSeconds(1);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, oneSecond),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            new CompositionEligibility([sound]));
        using var buffer1 = composer.Compose(window1, frame1);

        // The time boundary matches the captured natural end, but the sound is no longer eligible in
        // the scene (removed, disabled, muted, or excluded by solo policy), so its cached tail must stop.
        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            CompositionEligibility.Empty);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var output = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(output[k]), Is.LessThanOrEqualTo(1e-5f),
                $"An ineligible sound must not leak a cached tail at its natural end (sample {k}).");
        }
    }

    [Test]
    public void Composer_DoesNotFlushSoundThatDisappearsBeforeItsNaturalEnd()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        var oneSecond = TimeSpan.FromSeconds(1);
        var twoSeconds = TimeSpan.FromSeconds(2);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, twoSeconds),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            new CompositionEligibility([sound]));
        using var buffer1 = composer.Compose(window1, frame1);

        // The sound is absent from the next contiguous frame even though its captured range continues.
        // This models a mute, disable, or removal rather than a natural clip end.
        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            CompositionEligibility.Empty);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var output = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(output[k]), Is.LessThanOrEqualTo(1e-5f),
                $"A sound removed before its natural end must not leak a cached limiter tail (sample {k}).");
        }
    }

    // Guard path 1: a non-contiguous window (seek/scrub/restart) must suppress the ended-sound flush.
    // Same setup as the boundary test, but window 2 jumps forward so it no longer abuts window 1; the
    // cached limiter reset on the discontinuity anyway, so flushing it would inject a stale tail at the
    // wrong time. This pins the IsContiguous early-return in AppendEndedSoundTails.
    [Test]
    public void Composer_DoesNotFlushEndedSoundTail_AfterNonContiguousSeek()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        var oneSecond = TimeSpan.FromSeconds(1);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, oneSecond),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, oneSecond);
        var eligibility = new CompositionEligibility([sound]);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            eligibility);
        using var buffer1 = composer.Compose(window1, frame1);

        // A forward jump far past the previous window end — not contiguous.
        var window2 = new TimeRange(TimeSpan.FromSeconds(3), oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var tail = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(tail[k]), Is.LessThanOrEqualTo(1e-5f),
                $"A discontinuous window must not inject the previous clip's stale limiter tail (sample {k}).");
        }
    }

    // Guard path 2: InvalidateCache must clear the recorded previous window so a subsequent contiguous
    // window does not flush a stale tail. Identical to the boundary test (contiguous window 2 that
    // normally recovers the tail) except for the InvalidateCache call, which must suppress the flush.
    [Test]
    public void Composer_InvalidateCache_SuppressesEndedSoundTailFlush()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        var oneSecond = TimeSpan.FromSeconds(1);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, oneSecond),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, oneSecond);
        var eligibility = new CompositionEligibility([sound]);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            eligibility);
        using var buffer1 = composer.Compose(window1, frame1);

        composer.InvalidateCache();

        // Contiguous with window 1 — without InvalidateCache this window recovers the tail (boundary test).
        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var tail = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(tail[k]), Is.LessThanOrEqualTo(1e-5f),
                $"InvalidateCache must drop the recorded previous window so no stale tail is flushed (sample {k}).");
        }
    }

    // The three nodes refactored to delegate Process to ProcessTail must still drain correctly on the
    // base Flush path (draining: true). Each guards buffer ownership and shape, not a specific tail value.
    [Test]
    public void DelayNode_Flush_DrainsThroughProcessTail_NoThrow()
    {
        const int sampleCount = 512;
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));
        using var node = new DelayNode
        {
            DelayTime = Property.Create(5f),
            Feedback = Property.Create(50f),
            DryMix = Property.Create(50f),
            WetMix = Property.Create(50f),
        };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(tail.ChannelCount, Is.EqualTo(processed.ChannelCount));
        Assert.That(tail.SampleCount, Is.EqualTo(sampleCount));
    }

    [Test]
    public void CompressorNode_Flush_DrainsThroughProcessTail_NoThrow()
    {
        const int sampleCount = 512;
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));
        using var node = new CompressorNode
        {
            Threshold = Property.Create(-20f),
            Ratio = Property.Create(4f),
            Attack = Property.Create(5f),
            Release = Property.Create(50f),
            Knee = Property.Create(0f),
            MakeupGain = Property.Create(0f),
        };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(tail.ChannelCount, Is.EqualTo(processed.ChannelCount));
        Assert.That(tail.SampleCount, Is.EqualTo(sampleCount));
    }

    [Test]
    public void EqualizerNode_Flush_DrainsThroughProcessTail_NoThrow()
    {
        // A band present takes the fresh-buffer (non-pass-through) ownership path.
        const int sampleCount = 512;
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));
        using var node = new EqualizerNode { Bands = [new EqualizerBand()] };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(tail.ChannelCount, Is.EqualTo(processed.ChannelCount));
        Assert.That(tail.SampleCount, Is.EqualTo(sampleCount));
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

    private static AudioProcessContext ExactContext(TimeSpan start, int sampleCount, int sampleRate)
        => new(
            new TimeRange(start, ExactDuration(sampleCount, sampleRate)),
            sampleRate,
            new AnimationSampler(),
            null);

    private static TimeSpan ExactDuration(int sampleCount, int sampleRate)
        => AudioProcessContext.GetDurationForSampleCount(sampleCount, sampleRate);

    private static bool HasNonZero(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            if (MathF.Abs(sample) > 1e-6f)
                return true;
        }

        return false;
    }

    private static KeyFrameAnimation<float> ConstantSpeedAnimation(float speed)
        => new()
        {
            KeyFrames =
            {
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.Zero,
                    Value = speed,
                    Easing = new LinearEasing(),
                },
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.FromSeconds(10),
                    Value = speed,
                    Easing = new LinearEasing(),
                },
            },
        };

    private sealed class RecordingFlushRequestNode : AudioNode
    {
        public int TotalFlushedSamples { get; private set; }

        public TimeSpan? LastProcessedEnd { get; private set; }

        public TimeSpan? FirstFlushStart { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            LastProcessedEnd = context.TimeRange.End;
            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            FirstFlushStart ??= context.TimeRange.Start;
            int sampleCount = context.GetSampleCount();
            TotalFlushedSamples += sampleCount;
            var buffer = new AudioBuffer(context.SampleRate, 2, sampleCount);
            buffer.GetChannelData(0).Fill(0.25f);
            buffer.GetChannelData(1).Fill(0.25f);
            return buffer;
        }
    }

    private sealed class RecordingLatencyNode(int latencySamples) : AudioNode
    {
        public int LastFlushSampleCount { get; private set; } = -1;

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            LastFlushSampleCount = context.GetSampleCount();
            return new AudioBuffer(context.SampleRate, 2, LastFlushSampleCount);
        }

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (MixerNode Mixer, WeakReference InputReference) CreateDisposedMixerWithBranchEndMetadata()
    {
        var input = new ValueEqualAudioNode();
        var mixer = new MixerNode();
        mixer.AddInput(input);
        mixer.SetBranchEndTime(input, TimeSpan.Zero);
        mixer.Dispose();
        return (mixer, new WeakReference(input));
    }

    private sealed class ValueEqualAudioNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override bool Equals(object? obj) => obj is ValueEqualAudioNode;

        public override int GetHashCode() => 0;
    }
}
