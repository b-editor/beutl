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

        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));

        using var node = CreateTransparentLimiter(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));

        var flushDuration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        using var tail = node.Flush(Context(flushDuration, sampleCount));

        var inData = input.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
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

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

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

        var sourceA = new RangeSineNode(SampleRate);
        using var clipA = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipA.AddInput(sourceA);
        using var withDrain = clipA.Process(Context(TimeSpan.Zero, clipSamples));

        var sourceB = new RangeSineNode(SampleRate);
        using var clipB = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clipB.AddInput(sourceB);
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

        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 330f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));
        using var gain = new GainNode { Gain = Property.CreateAnimatable(50f) };
        gain.AddInput(limiter);

        using var processed = gain.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = gain.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        var inData = input.GetChannelData(0);
        var tailData = tail.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
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
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

        using var processed = clip.Process(Context(TimeSpan.Zero, clipSamples));

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
    public void ShiftNode_Flush_RemapsToUpstreamTime_RecoversTail()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var shift = new ShiftNode { Shift = TimeSpan.FromSeconds(1) };
        shift.AddInput(limiter);
        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(shift);

        using var processed = clip.Process(Context(TimeSpan.Zero, clipSamples));
        using var tail = clip.Flush(Context(TimeSpan.FromSeconds(123.0), L));

        Assert.That(tail.GetChannelData(0)[..L].ToArray(), Has.Some.Not.EqualTo(0f),
            "A shifted latency chain must preserve upstream continuity during flush.");
    }

    [Test]
    public void NestedClipNode_Flush_DrainsFromLastProcessedLocalTime_WhenParentTrimsTheClip()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 8192;
        const int processedSamples = 4096;

        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);
        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

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
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        int pad = L / 2;
        const int clipSamples = 4096;
        var clipDuration = TimeSpan.FromSeconds((double)clipSamples / SampleRate);

        var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);

        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = clipDuration };
        clip.AddInput(limiter);

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
    public void Composer_ContinuesPartiallyDrainedTail_WhenSoundEndsInsideThePreviousWindow()
    {
        const float lookaheadMs = 5f;
        int L = LookaheadSamples(lookaheadMs);
        const int clipSamples = 48000;
        int padSamples = L / 2;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var window1Duration = ExactDuration(clipSamples + padSamples, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };

        var window1 = new TimeRange(TimeSpan.Zero, window1Duration);
        var eligibility = new CompositionEligibility([sound]);
        var frame1 = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            window1,
            default,
            eligibility);
        using var buffer1 = composer.Compose(window1, frame1);

        var window2 = new TimeRange(window1Duration, window1Duration);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var tail = buffer2!.GetChannelData(0);
        for (int k = 0; k < L - padSamples; k++)
        {
            float expected = 0.25f * MathF.Sin(2f * MathF.PI * 200f * (clipSamples - L + padSamples + k) / SampleRate);
            Assert.That(tail[k], Is.EqualTo(expected).Within(1e-5f),
                $"The remaining {L - padSamples} held samples must be flushed into the next window (sample {k}).");
        }
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
    public void Composer_DoesNotFlushCachedTailAfterSoundMoves()
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

        sound.TimeRange = new TimeRange(TimeSpan.FromSeconds(10), oneSecond);

        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var output = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(output[k]), Is.LessThanOrEqualTo(1e-5f),
                $"A moved sound must not flush a tail cached at its former range (sample {k}).");
        }
    }

    [Test]
    public void Composer_DoesNotFlushCachedTailAfterSoundIsEdited()
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

        sound.Gain.CurrentValue = 50f;

        var window2 = new TimeRange(oneSecond, oneSecond);
        var frame2 = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            window2,
            default,
            eligibility);
        using var buffer2 = composer.Compose(window2, frame2);

        Assert.That(buffer2, Is.Not.Null);
        var output = buffer2!.GetChannelData(0);
        for (int k = 0; k < L; k++)
        {
            Assert.That(MathF.Abs(output[k]), Is.LessThanOrEqualTo(1e-5f),
                $"An edited sound must not flush a tail from its dirty cached graph (sample {k}).");
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
        const int sampleCount = 512;
        using var input = CreateBuffer(2, sampleCount, (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate));
        using var node = new EqualizerNode { Bands = [new EqualizerBand()] };
        node.AddInput(new BufferReplayNode(input));

        using var processed = node.Process(Context(TimeSpan.Zero, sampleCount));
        using var tail = node.Flush(Context(TimeSpan.FromSeconds((double)sampleCount / SampleRate), sampleCount));

        Assert.That(tail.ChannelCount, Is.EqualTo(processed.ChannelCount));
        Assert.That(tail.SampleCount, Is.EqualTo(sampleCount));
    }

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
