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
    public void ResampleNode_GetTotalLatencySamples_ConvertsSourceLatencyToOutputRate()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const float lookaheadMs = 5f;
        int sourceLatency = LookaheadSamples(lookaheadMs, sourceSampleRate);
        int expected = (int)Math.Ceiling(sourceLatency * (double)outputSampleRate / sourceSampleRate);

        using var limiter = CreateTransparentLimiter(lookaheadMs);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(limiter);

        Assert.That(resample.GetTotalLatencySamples(outputSampleRate), Is.EqualTo(expected));
    }

    [Test]
    public void ResampleNode_Flush_DrainsUpstreamAtSourceRate()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const float lookaheadMs = 5f;
        const int processSamples = 4096;
        const int flushSamples = 1024;

        using var source = new RangeSineNode(sourceSampleRate);
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(source);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(limiter);

        using var processed = resample.Process(Context(TimeSpan.Zero, processSamples, outputSampleRate));
        using var tail = resample.Flush(Context(
            TimeSpan.FromSeconds((double)processSamples / outputSampleRate),
            flushSamples,
            outputSampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(processed.SampleRate, Is.EqualTo(outputSampleRate));
            Assert.That(tail.SampleRate, Is.EqualTo(outputSampleRate));
            Assert.That(HasNonZero(tail.GetChannelData(0)[..LookaheadSamples(lookaheadMs, outputSampleRate)]), Is.True,
                "ResampleNode.Flush must preserve the upstream limiter tail across the source-rate drain.");
        });
    }

    [Test]
    public void ResampleNode_PartialBlocksKeepExactOutputSampleCount()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;

        using var source = new RecordingFlushRequestNode();
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(source);

        using var processed = resample.Process(Context(TimeSpan.Zero, 1, outputSampleRate));
        using var firstTail = resample.Flush(Context(
            TimeSpan.FromSeconds(1.0 / outputSampleRate),
            1,
            outputSampleRate));
        using var secondTail = resample.Flush(Context(
            TimeSpan.FromSeconds(2.0 / outputSampleRate),
            1,
            outputSampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(processed.SampleCount, Is.EqualTo(1));
            Assert.That(firstTail.SampleCount, Is.EqualTo(1));
            Assert.That(secondTail.SampleCount, Is.EqualTo(1));
            Assert.That(source.TotalFlushedSamples, Is.EqualTo(2),
                "The two one-sample output drain blocks must consume only their non-overlapping source-rate ranges.");
        });
    }

    [Test]
    public void ResampleNode_PartialBlocksPreserveStreamingProgress()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const int blockCount = 64;

        using var source = new IndexedAudioNode(sourceSampleRate);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(source);

        float[] firstSamples = new float[blockCount];
        for (int i = 0; i < blockCount; i++)
        {
            using var output = resample.Process(ExactContext(ExactDuration(i, outputSampleRate), 1, outputSampleRate));
            firstSamples[i] = output.GetChannelData(0)[0];
        }

        Assert.That(firstSamples[^1] - firstSamples[0], Is.GreaterThan(50f),
            "Small output blocks must consume the resampled stream in order instead of replaying a growing pending backlog.");
    }

    [Test]
    public void ResampleNode_ReplacingInput_ResetsStreamingState()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const int blockSamples = 1024;
        const int sourceSamples = 8192;

        using var sourceBufferA = CreateConstantBuffer(0.75f, sourceSamples, sampleRate: sourceSampleRate);
        using var sourceBufferB = CreateConstantBuffer(-0.75f, sourceSamples, sampleRate: sourceSampleRate);
        using var sourceA = new BufferReplayNode(sourceBufferA);
        using var sourceB = new BufferReplayNode(sourceBufferB);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(sourceA);

        using var first = resample.Process(ExactContext(TimeSpan.Zero, blockSamples, outputSampleRate));

        resample.ClearInputs();
        resample.AddInput(sourceB);
        using var second = resample.Process(
            ExactContext(ExactDuration(blockSamples, outputSampleRate), blockSamples, outputSampleRate));

        Assert.That(second.GetChannelData(0)[128..].ToArray(), Has.All.EqualTo(-0.75f).Within(1e-3f),
            "Replacing a same-format source must discard the previous resampler queue and filter history.");
    }

    [Test]
    public void ResampleNode_PartialBlocksRequestNonOverlappingTimestampRanges()
    {
        const int sourceSampleRate = 48000;
        const int outputSampleRate = 44100;
        const int blockCount = 64;

        using var source = new RampInputNode(sourceSampleRate);
        using var resample = new ResampleNode { SourceSampleRate = sourceSampleRate };
        resample.AddInput(source);

        TimeSpan start = TimeSpan.Zero;
        TimeSpan blockDuration = ExactDuration(1, outputSampleRate);
        for (int i = 0; i < blockCount; i++)
        {
            using var output = resample.Process(ExactContext(start, 1, outputSampleRate));
            Assert.That(output.SampleCount, Is.EqualTo(1));
            start += blockDuration;
        }

        Assert.That(source.RequestedRanges, Has.Count.EqualTo(blockCount));
        Assert.That(source.RequestedRanges[0].Start, Is.EqualTo(0));
        for (int i = 1; i < source.RequestedRanges.Count; i++)
        {
            var previous = source.RequestedRanges[i - 1];
            var current = source.RequestedRanges[i];
            Assert.That(current.Start, Is.EqualTo(previous.Start + previous.Count),
                $"Source request {i} must begin where request {i - 1} ended.");
        }
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
    public void MixerNode_Process_DrainsEndedBranchTail()
    {
        const float lookaheadMs = 20f;
        const int processSamples = 4096;
        const int drainSamples = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var branchEnd = ExactDuration(processSamples, SampleRate);

        using var input = CreateBuffer(2, processSamples, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));
        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = branchEnd };
        clip.AddInput(limiter);

        using var mixer = new MixerNode();
        mixer.AddInput(clip);
        mixer.SetBranchEndTime(clip, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, processSamples, SampleRate));
        using var tail = mixer.Process(ExactContext(branchEnd, drainSamples, SampleRate));

        Assert.That(HasNonZero(tail.GetChannelData(0)[..L]), Is.True,
            "A branch that ended at the previous block must drain its retained tail during normal mixer processing.");
    }

    [Test]
    public void MixerNode_Process_AtExactTailBoundary_SkipsZeroLatencyStatefulBranch()
    {
        const int sampleCount = 256;
        var branchEnd = ExactDuration(sampleCount, SampleRate);

        using var sourceBuffer = CreateConstantBuffer(1f, sampleCount);
        using var delay = new DelayNode
        {
            DelayTime = Property.Create(0f),
            Feedback = Property.Create(100f),
            DryMix = Property.Create(0f),
            WetMix = Property.Create(100f),
        };
        delay.AddInput(new BufferReplayNode(sourceBuffer));

        using var mixer = new MixerNode();
        mixer.AddInput(delay);
        mixer.SetBranchEndTime(delay, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, sampleCount, SampleRate));
        using var tail = mixer.Process(ExactContext(branchEnd, sampleCount, SampleRate));

        Assert.That(tail.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
            "At branchEnd + reported latency, the mixer must not flush a zero-latency stateful branch.");
    }

    [Test]
    public void MixerNode_Process_UsesTerminalDrainLatencyForAnimatedBranchDeath()
    {
        const int sampleCount = 4096;
        var branchEnd = ExactDuration(sampleCount, SampleRate);
        var oneSample = ExactDuration(1, SampleRate);

        using var source = new RangeSineNode(SampleRate);
        using var limiter = CreateTransparentLimiter(20f);
        limiter.Lookahead.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 20f, Easing = new LinearEasing() },
                new KeyFrame<float> { KeyTime = branchEnd - oneSample, Value = 0f, Easing = new LinearEasing() },
            },
        };
        limiter.AddInput(source);
        using var delay = new DelayNode
        {
            DelayTime = Property.Create(0f),
            Feedback = Property.Create(100f),
            DryMix = Property.Create(0f),
            WetMix = Property.Create(100f),
        };
        delay.AddInput(limiter);

        using var mixer = new MixerNode();
        mixer.AddInput(delay);
        mixer.SetBranchEndTime(delay, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, sampleCount, SampleRate));
        using var tail = mixer.Process(ExactContext(branchEnd, sampleCount, SampleRate));

        Assert.That(tail.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
            "Mixer branch liveness must use the terminal animated drain latency, not the public worst case.");
    }

    [Test]
    public void MixerNode_Process_AfterSeek_DoesNotDrainStaleBranchTail()
    {
        const float lookaheadMs = 20f;
        const int processSamples = 4096;
        const int seekSamples = 1024;
        int L = LookaheadSamples(lookaheadMs);
        var branchEnd = ExactDuration(processSamples, SampleRate);

        using var input = CreateBuffer(2, processSamples, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));
        using var clip = new ClipNode { Start = TimeSpan.Zero, Duration = branchEnd };
        clip.AddInput(limiter);

        using var mixer = new MixerNode();
        mixer.AddInput(clip);
        mixer.SetBranchEndTime(clip, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, processSamples, SampleRate));
        var seekStart = branchEnd + TimeSpan.FromMilliseconds(1);
        using var seekOutput = mixer.Process(ExactContext(seekStart, seekSamples, SampleRate));
        using var nextOutput = mixer.Process(
            ExactContext(seekStart + ExactDuration(seekSamples, SampleRate), seekSamples, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(seekOutput.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
                "A discontinuous process must clear branch drain state instead of emitting a cached tail at the seek destination.");
            Assert.That(nextOutput.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
                "An ended branch must remain unprocessed after a seek instead of rearming its stale cached tail on the next block.");
        });
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
    public void MixerNode_Flush_KeepsPartiallyDrainedBranchLive()
    {
        const float lookaheadMs = 20f;
        const int sampleCount = 4096;
        int L = LookaheadSamples(lookaheadMs);
        var groupEnd = ExactDuration(sampleCount, SampleRate);
        var branchEnd = groupEnd - TimeSpan.FromMilliseconds(5);

        using var input = CreateBuffer(2, sampleCount, (_, i) =>
            0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var limiter = CreateTransparentLimiter(lookaheadMs);
        limiter.AddInput(new BufferReplayNode(input));

        using var mixer = new MixerNode();
        mixer.AddInput(limiter);
        mixer.SetBranchEndTime(limiter, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, sampleCount, SampleRate));
        using var tail = mixer.Flush(ExactContext(groupEnd, sampleCount, SampleRate));

        Assert.That(HasNonZero(tail.GetChannelData(0)[..L]), Is.True,
            "A branch whose retained latency extends past the group boundary must remain live during flush.");
    }

    [Test]
    public void MixerNode_Process_LimitsPartialFinalBranchFlushToRemainingTail()
    {
        const int latencySamples = 960;
        const int processSamples = 4096;
        const int firstDrainSamples = 720;
        const int finalBlockSamples = 480;
        var branchEnd = ExactDuration(processSamples, SampleRate);

        using var branch = new RecordingLatencyNode(latencySamples);
        using var mixer = new MixerNode();
        mixer.AddInput(branch);
        mixer.SetBranchEndTime(branch, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, processSamples, SampleRate));
        using var firstDrain = mixer.Process(ExactContext(branchEnd, firstDrainSamples, SampleRate));
        using var finalDrain = mixer.Process(
            ExactContext(branchEnd + ExactDuration(firstDrainSamples, SampleRate), finalBlockSamples, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(branch.LastFlushSampleCount, Is.EqualTo(latencySamples - firstDrainSamples),
                "The final branch block must flush only the tail that remains after the prior drain.");
            Assert.That(finalDrain.SampleCount, Is.EqualTo(finalBlockSamples),
                "Mixer output must remain padded to the requested block length after a shortened branch flush.");
        });
    }

    [Test]
    public void MixerNode_Process_BoundsUnknownBranchDrainToOneAttempt()
    {
        const int processSamples = 4096;
        const int drainSamples = 256;
        var branchEnd = ExactDuration(processSamples, SampleRate);

        using var branch = new RecordingLatencyNode(int.MaxValue);
        using var mixer = new MixerNode();
        mixer.AddInput(branch);
        mixer.SetBranchEndTime(branch, branchEnd);

        using var processed = mixer.Process(ExactContext(TimeSpan.Zero, processSamples, SampleRate));
        using var firstDrain = mixer.Process(ExactContext(branchEnd, drainSamples, SampleRate));
        using var secondDrain = mixer.Process(
            ExactContext(branchEnd + ExactDuration(drainSamples, SampleRate), drainSamples, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(branch.FlushCount, Is.EqualTo(1),
                "An unknown-latency branch must receive only one bounded follow-up drain attempt.");
            Assert.That(secondDrain.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
                "The mixer must stop feeding zero blocks through an unknown branch after its bounded attempt.");
        });
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
    public void Composer_DoesNotReflushTailAfterClipInlineDrain()
    {
        const float lookaheadMs = 5f;
        const int clipSamples = SampleRate;
        int inlineDrainSamples = LookaheadSamples(lookaheadMs);
        int windowSamples = clipSamples + inlineDrainSamples;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var windowDuration = ExactDuration(windowSamples, SampleRate);

        var sound = new LimiterDelayTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = SampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, windowDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(0),
            "The terminal ClipNode already drained the limiter's full reported latency into the first window.");

        var secondRange = new TimeRange(windowDuration, windowDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(second, Is.Not.Null);
        Assert.That(HasNonZero(second!.GetChannelData(0)), Is.False,
            "A zero-latency stateful effect after the limiter must not be flushed again after the inline tail is exhausted.");
    }

    [Test]
    public void Composer_OnlyFlushesTheRemainingTailIntoALongerWindow()
    {
        const int clipSamples = SampleRate;
        const int tailSamples = 240;
        const int windowSamples = 960;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var windowDuration = ExactDuration(windowSamples, SampleRate);

        var sound = new LimiterDelayTailSound
        {
            LookaheadMs = 5f,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = SampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(tailSamples));

        var secondRange = new TimeRange(clipDuration, windowDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(second, Is.Not.Null);
        Assert.That(second!.GetChannelData(0)[tailSamples..].ToArray(), Has.All.EqualTo(0f),
            "A zero-reported stateful effect must not receive zero-fed samples beyond the remaining limiter tail.");
    }

    [Test]
    public void Composer_DrainsEachOutputOnlyWhileItsOwnTailRemains()
    {
        const int shortLatency = 240;
        const int longLatency = 960;
        var clipDuration = ExactDuration(SampleRate, SampleRate);
        var drainDuration = ExactDuration(shortLatency, SampleRate);

        var sound = new HeterogeneousOutputTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        HeterogeneousOutputTailSound.ResetFlushCounts();

        using var composer = new Composer { SampleRate = SampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        var secondRange = new TimeRange(clipDuration, drainDuration);
        var emptyFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, emptyFrame);

        Assert.Multiple(() =>
        {
            Assert.That(HeterogeneousOutputTailSound.ShortFlushCount, Is.EqualTo(1));
            Assert.That(HeterogeneousOutputTailSound.LongFlushCount, Is.EqualTo(1));
            Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(longLatency - shortLatency));
        });

        var thirdRange = new TimeRange(clipDuration + drainDuration, drainDuration);
        var thirdFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            thirdRange,
            default,
            eligibility);
        using var third = composer.Compose(thirdRange, thirdFrame);

        Assert.Multiple(() =>
        {
            Assert.That(HeterogeneousOutputTailSound.ShortFlushCount, Is.EqualTo(1),
                "An exhausted short output branch must not be flushed for a longer sibling branch.");
            Assert.That(HeterogeneousOutputTailSound.LongFlushCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Composer_DoesNotRetainUnboundedLatencyAfterOneDrainAttempt()
    {
        const int sampleRate = SampleRate;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var drainDuration = ExactDuration(240, sampleRate);

        var sound = new UnboundedSpeedTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        UnboundedSpeedTailSound.ResetFlushCount();

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);
        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(int.MaxValue),
            "An unknown speed-animation range must report an unbounded drain budget before a drain attempt.");

        var secondRange = new TimeRange(clipDuration, drainDuration);
        var emptyFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, emptyFrame);

        Assert.Multiple(() =>
        {
            Assert.That(UnboundedSpeedTailSound.FlushCount, Is.EqualTo(1));
            Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(0),
                "The unbounded sentinel must not retain an entry for unbounded repeated flushing.");
        });

        var thirdRange = new TimeRange(clipDuration + drainDuration, drainDuration);
        var thirdFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            thirdRange,
            default,
            eligibility);
        using var third = composer.Compose(thirdRange, thirdFrame);

        Assert.That(UnboundedSpeedTailSound.FlushCount, Is.EqualTo(1),
            "An unknown latency budget must be bounded to one drain attempt.");
    }

    [Test]
    public void Composer_UsesTerminalAnimatedLimiterLatencyForInlineDrain()
    {
        const int sampleRate = SampleRate;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var oneSample = ExactDuration(1, sampleRate);
        var drainDuration = ExactDuration(240, sampleRate);

        var sound = new AnimatedLimiterDelayTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration + oneSample);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(0),
            "Composer must use the limiter's terminal animated lookahead, not its worst-case reservation.");

        var secondRange = new TimeRange(firstRange.End, drainDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(HasNonZero(second!.GetChannelData(0)), Is.False,
            "A zero terminal lookahead must not feed unnecessary zero blocks into a stateful delay effect.");
    }

    [Test]
    public void Composer_UsesTerminalAnimatedLimiterLatency_WhenTerminalWindowHasNoInlineCapacity()
    {
        const int sampleRate = SampleRate;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var drainDuration = ExactDuration(240, sampleRate);

        var sound = new AnimatedLimiterDelayTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(0),
            "A terminal block with no trailing capacity must still seed the actual terminal drain latency.");

        var secondRange = new TimeRange(clipDuration, drainDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(HasNonZero(second!.GetChannelData(0)), Is.False,
            "A zero terminal lookahead must not flush a stateful nested delay after an exact-boundary clip.");
    }

    [Test]
    public void Composer_RetainsUnknownTailAfterPartialInlineDrain()
    {
        const int sampleRate = SampleRate;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var oneSample = ExactDuration(1, sampleRate);
        var drainDuration = ExactDuration(240, sampleRate);

        var sound = new UnboundedSpeedTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        UnboundedSpeedTailSound.ResetFlushCount();

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration + oneSample);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(int.MaxValue),
            "A partial inline drain of an unknown budget must retain one bounded follow-up attempt.");
        Assert.That(UnboundedSpeedTailSound.FlushCount, Is.EqualTo(1),
            "The terminal block must account for its one inline drain sample.");

        var secondRange = new TimeRange(firstRange.End, drainDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.Multiple(() =>
        {
            Assert.That(UnboundedSpeedTailSound.FlushCount, Is.EqualTo(2));
            Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(0));
        });
    }

    [Test]
    public void Composer_PreservesUnknownTailAcrossZeroSampleFlush()
    {
        const int sampleRate = SampleRate;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var drainDuration = ExactDuration(240, sampleRate);

        var sound = new UnboundedSpeedTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        UnboundedSpeedTailSound.ResetFlushCount();

        using var composer = new Composer { SampleRate = sampleRate };
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, clipDuration),
            default,
            eligibility);
        using var first = composer.Compose(firstFrame.Time, firstFrame);

        using var emptyFlush = composer.Flush(new TimeRange(clipDuration, TimeSpan.Zero), eligibility);
        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(int.MaxValue),
            "A zero-sample flush must not consume the pending unknown follow-up attempt.");

        using var nextFlush = composer.Flush(new TimeRange(clipDuration, drainDuration), eligibility);

        Assert.Multiple(() =>
        {
            Assert.That(UnboundedSpeedTailSound.FlushCount, Is.EqualTo(2),
                "The positive flush after a zero-duration call must still receive the bounded follow-up drain.");
            Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(0));
        });
    }

    [Test]
    public void Composer_RecordsInlineDrainThroughWrappedOutput()
    {
        const int sampleRate = SampleRate;
        const float lookaheadMs = 5f;
        int latencySamples = LookaheadSamples(lookaheadMs);
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var oneSample = ExactDuration(1, sampleRate);
        var drainDuration = ExactDuration(latencySamples, sampleRate);

        var sound = new WrappedOutputTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        WrappedOutputTailSound.ResetFlushState();

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration + oneSample);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(latencySamples - 1),
            "Inline drain accounting must find the ClipNode beneath a wrapper output.");

        var secondRange = new TimeRange(firstRange.End, drainDuration);
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(WrappedOutputTailSound.LastFlushSampleCount, Is.EqualTo(latencySamples - 1),
            "The wrapped output must be flushed only for the tail not already recovered inline.");
    }

    [Test]
    public void Composer_GetDrainLatencySamples_UsesRemainingInlineBudgetThroughIComposer()
    {
        const float lookaheadMs = 5f;
        int latencySamples = LookaheadSamples(lookaheadMs);
        var clipDuration = ExactDuration(SampleRate, SampleRate);
        var oneSample = ExactDuration(1, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = SampleRate };
        IComposer composerContract = composer;
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration + oneSample);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composerContract.GetDrainLatencySamples(SampleRate), Is.EqualTo(latencySamples - 1),
            "The interface contract must expose the nested composer's remaining, not full, drain budget.");
    }

    [Test]
    public void Composer_TracksInlineDrainPerWrappedFanInBranch()
    {
        const int sampleRate = SampleRate;
        const int longLatency = 960;
        var clipDuration = ExactDuration(sampleRate, sampleRate);
        var oneSample = ExactDuration(1, sampleRate);

        var sound = new HeterogeneousWrappedOutputTailSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var eligibility = new CompositionEligibility([sound]);
        HeterogeneousWrappedOutputTailSound.ResetFlushState();

        using var composer = new Composer { SampleRate = sampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration + oneSample);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        Assert.That(composer.GetTotalLatencySamples(sampleRate), Is.EqualTo(longLatency - 1),
            "A wrapped fan-in output must retain the dominant branch's remaining tail after inline recovery.");

        var secondRange = new TimeRange(firstRange.End, ExactDuration(longLatency, sampleRate));
        var secondFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            secondRange,
            default,
            eligibility);
        using var second = composer.Compose(secondRange, secondFrame);

        Assert.That(HeterogeneousWrappedOutputTailSound.LastFlushSampleCount, Is.EqualTo(longLatency - 1),
            "The wrapped fan-in must not reflush the sample already recovered by each descendant ClipNode.");
    }

    [Test]
    public void Composer_ContinuesPartiallyDrainedTailAcrossMultipleWindows()
    {
        const float lookaheadMs = 20f;
        const int clipSamples = 48000;
        int chunkSamples = LookaheadSamples(lookaheadMs) / 4;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var chunkDuration = ExactDuration(chunkSamples, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };
        var eligibility = new CompositionEligibility([sound]);
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        for (int i = 0; i < 2; i++)
        {
            var range = new TimeRange(clipDuration + (chunkDuration * i), chunkDuration);
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                range,
                default,
                eligibility);
            using var tail = composer.Compose(range, frame);

            Assert.That(tail, Is.Not.Null);
            Assert.That(HasNonZero(tail!.GetChannelData(0)), Is.True,
                $"The retained limiter tail must remain available in partial window {i + 1}.");
        }
    }

    [Test]
    public void Composer_GetTotalLatencySamples_ScalesRetainedTailToRequestedRate()
    {
        const float lookaheadMs = 20f;
        const int clipSamples = 48000;
        int retainedSamples = LookaheadSamples(lookaheadMs) * 3 / 4;
        int chunkSamples = LookaheadSamples(lookaheadMs) / 4;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var chunkDuration = ExactDuration(chunkSamples, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };
        var eligibility = new CompositionEligibility([sound]);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, clipDuration),
            default,
            eligibility);
        using var first = composer.Compose(firstFrame.Time, firstFrame);

        var partialRange = new TimeRange(clipDuration, chunkDuration);
        var partialFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            partialRange,
            default,
            eligibility);
        using var partial = composer.Compose(partialRange, partialFrame);

        Assert.Multiple(() =>
        {
            Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(retainedSamples));
            Assert.That(
                composer.GetTotalLatencySamples(44100),
                Is.EqualTo((int)Math.Ceiling(retainedSamples * 44100d / SampleRate)));
            Assert.That(
                composer.GetTotalLatencySamples(96000),
                Is.EqualTo((int)Math.Ceiling(retainedSamples * 96000d / SampleRate)));
        });
    }

    [Test]
    public void Composer_GetTotalLatencySamples_RejectsNegativeOutputLatency()
    {
        var sound = new NegativeLatencySound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        };
        var resource = sound.ToResource(CompositionContext.Default);
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            range,
            default,
            new CompositionEligibility([sound]));

        using var composer = new Composer { SampleRate = SampleRate };
        using var output = composer.Compose(range, frame);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => composer.GetTotalLatencySamples(SampleRate));
        Assert.That(exception!.Message, Does.Contain("NegativeLatencyNode").And.Contain("-1"));
    }

    [Test]
    public void Composer_DoesNotResurrectPartiallyDrainedTailAfterEligibilityLoss()
    {
        const float lookaheadMs = 20f;
        const int clipSamples = 48000;
        int chunkSamples = LookaheadSamples(lookaheadMs) / 4;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var chunkDuration = ExactDuration(chunkSamples, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };
        var eligibility = new CompositionEligibility([sound]);
        var firstRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var firstFrame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var first = composer.Compose(firstRange, firstFrame);

        var partialRange = new TimeRange(clipDuration, chunkDuration);
        var partialFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            partialRange,
            default,
            eligibility);
        using var partial = composer.Compose(partialRange, partialFrame);
        Assert.That(HasNonZero(partial!.GetChannelData(0)), Is.True,
            "The setup must retain a partially drained tail before eligibility is lost.");

        var mutedRange = new TimeRange(clipDuration + chunkDuration, chunkDuration);
        var mutedFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            mutedRange,
            default,
            CompositionEligibility.Empty);
        using var muted = composer.Compose(mutedRange, mutedFrame);

        var reenabledRange = new TimeRange(clipDuration + (chunkDuration * 2), chunkDuration);
        var reenabledFrame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            reenabledRange,
            default,
            eligibility);
        using var reenabled = composer.Compose(reenabledRange, reenabledFrame);

        Assert.Multiple(() =>
        {
            Assert.That(muted, Is.Not.Null);
            Assert.That(reenabled, Is.Not.Null);
            Assert.That(muted!.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
                "An ineligible window must not emit the retained tail.");
            Assert.That(reenabled!.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
                "A tail made ineligible must be discarded instead of resurfacing after re-enablement.");
        });
    }

    [Test]
    public void Composer_Flush_ContinuesPartialDrainAcrossCalls()
    {
        const float lookaheadMs = 20f;
        const int clipSamples = 48000;
        int chunkSamples = LookaheadSamples(lookaheadMs) / 4;
        var clipDuration = ExactDuration(clipSamples, SampleRate);
        var chunkDuration = ExactDuration(chunkSamples, SampleRate);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, clipDuration),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };
        var eligibility = new CompositionEligibility([sound]);
        var composeRange = new TimeRange(TimeSpan.Zero, clipDuration);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            composeRange,
            default,
            eligibility);
        using var processed = composer.Compose(composeRange, frame);

        var firstRange = new TimeRange(clipDuration, chunkDuration);
        using var firstTail = composer.Flush(firstRange, eligibility);
        var secondRange = new TimeRange(clipDuration + chunkDuration, chunkDuration);
        using var secondTail = composer.Flush(secondRange, eligibility);

        Assert.That(firstTail, Is.Not.Null);
        Assert.That(secondTail, Is.Not.Null);
        Assert.That(HasNonZero(firstTail!.GetChannelData(0)), Is.True,
            "The first partial Composer.Flush must emit the beginning of the retained tail.");
        Assert.That(HasNonZero(secondTail!.GetChannelData(0)), Is.True,
            "A subsequent partial Composer.Flush must continue the retained tail instead of returning silence.");
    }

    [Test]
    public void Composer_Flush_WithoutEligibility_DoesNotDrainStaleTail()
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
        var eligibility = new CompositionEligibility([sound]);

        using var composer = new Composer { SampleRate = SampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            eligibility);
        using var processed = composer.Compose(firstRange, frame);

        using var tail = composer.Flush(new TimeRange(oneSecond, oneSecond));

        Assert.Multiple(() =>
        {
            Assert.That(tail, Is.Not.Null);
            Assert.That(tail!.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
                "A flush without a current eligibility snapshot must fail closed instead of reusing stale eligibility.");
            Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(0));
        });
    }

    [Test]
    public void Composer_Flush_DoesNotDrainAfterNonContiguousRange()
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
        var firstRange = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            new CompositionEligibility([sound]));
        using var processed = composer.Compose(firstRange, frame);

        var seekRange = new TimeRange(TimeSpan.FromSeconds(3), oneSecond);
        using var tail = composer.Flush(seekRange, (CompositionEligibility)frame.Eligibility!);

        Assert.That(tail, Is.Not.Null);
        Assert.That(tail!.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
            "A non-contiguous flush must discard the old tail instead of emitting it at the seek destination.");
        Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(0));
    }

    [Test]
    public void Composer_Flush_DoesNotDrainAfterSubMillisecondSeek()
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
        var firstRange = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            new CompositionEligibility([sound]));
        using var processed = composer.Compose(firstRange, frame);

        var subMillisecondGap = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2);
        var seekRange = new TimeRange(oneSecond + subMillisecondGap, oneSecond);
        using var tail = composer.Flush(seekRange, (CompositionEligibility)frame.Eligibility!);

        Assert.That(tail, Is.Not.Null);
        Assert.That(tail!.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
            "A sub-millisecond seek must discard the old tail instead of draining it at the seek destination.");
        Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(0));
    }

    [Test]
    public void Composer_Flush_DoesNotDrainDirtyEntry()
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
        var firstRange = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            new CompositionEligibility([sound]));
        using var processed = composer.Compose(firstRange, frame);

        sound.Gain.CurrentValue = 50f;

        using var tail = composer.Flush(new TimeRange(oneSecond, oneSecond), (CompositionEligibility)frame.Eligibility!);

        Assert.That(tail, Is.Not.Null);
        Assert.That(tail!.GetChannelData(0)[..L].ToArray(), Has.All.EqualTo(0f),
            "A dirty sound must not emit the tail of its old cached graph through direct Flush.");
    }

    [Test]
    public void Composer_Flush_AfterInvalidationDoesNotUseDisposedCurrentEntries()
    {
        const float lookaheadMs = 5f;
        var oneSecond = TimeSpan.FromSeconds(1);

        var sound = new LimiterTailSound
        {
            LookaheadMs = lookaheadMs,
            TimeRange = new TimeRange(TimeSpan.Zero, oneSecond),
        };
        var resource = sound.ToResource(CompositionContext.Default);

        using var composer = new Composer { SampleRate = SampleRate };
        var firstRange = new TimeRange(TimeSpan.Zero, oneSecond);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            firstRange,
            default,
            new CompositionEligibility([sound]));
        using var processed = composer.Compose(firstRange, frame);

        composer.InvalidateCache();

        Assert.That(composer.GetTotalLatencySamples(SampleRate), Is.EqualTo(0));
        Assert.DoesNotThrow(() =>
        {
            using AudioBuffer? tail = composer.Flush(new TimeRange(oneSecond, oneSecond));
            Assert.That(tail, Is.Not.Null);
        });
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

    private sealed class IndexedAudioNode(int sampleRate) : AudioNode
    {
        private long _nextSample;

        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(sampleRate, 2, count);
            for (int channel = 0; channel < 2; channel++)
            {
                var data = buffer.GetChannelData(channel);
                for (int i = 0; i < count; i++)
                {
                    data[i] = _nextSample + i;
                }
            }

            _nextSample += count;

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

        public int FlushCount { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            LastFlushSampleCount = context.GetSampleCount();
            FlushCount++;
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
