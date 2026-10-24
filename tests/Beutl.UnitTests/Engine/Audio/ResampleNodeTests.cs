using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

using static Beutl.UnitTests.Engine.Audio.AudioTestContexts;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ResampleNodeTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [TestCase(false, 44100, 48000)]
    [TestCase(true, 44100, 48000)]
    [TestCase(false, 48000, 44100)]
    [TestCase(true, 48000, 44100)]
    public void Compose_UnchangedRebuild_PreservesStreamingState(bool grouped, int sourceRate, int outputRate)
    {
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: sourceRate, durationSeconds: 1);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var child = new SourceSound { Source = { Value = source }, TimeRange = { Value = range } };
        Sound sound = child;
        if (grouped)
        {
            var group = new SoundGroup { TimeRange = { Value = range } };
            group.Children.Add(child);
            sound = group;
        }

        using var resource = sound.ToResource(CompositionContext.Default);
        using var uninterrupted = new AudioContext(outputRate, 2);
        using var rebuilt = new AudioContext(outputRate, 2);
        sound.Compose(uninterrupted, resource);
        sound.Compose(rebuilt, resource);
        var resample = rebuilt.Nodes.OfType<ResampleNode>().Single();

        MediaTime start = MediaTime.Zero;
        foreach (int count in new[] { 257, 511, 1, 1024 })
        {
            if (start != MediaTime.Zero)
            {
                rebuilt.BeginUpdate(rebuilt.Nodes);
                sound.Compose(rebuilt, resource);
                rebuilt.EndUpdate();
                Assert.That(rebuilt.Nodes.OfType<ResampleNode>().Single(), Is.SameAs(resample));
            }

            var context = Exact(start, count, outputRate);
            using var expected = uninterrupted.GetOutputNodes().Single().Process(context);
            using var actual = rebuilt.GetOutputNodes().Single().Process(context);
            AssertBuffersEqual(actual, expected);
            start = context.TimeRange.End;
        }
    }

    [Test]
    public void Rebuild_ReplacingSourceBehindReusedShift_ResetsStreamingState()
    {
        using var graph = new AudioContext(48000, 2);
        var source = graph.AddNode(new FormatInputNode { Value = 0.75f });
        var shift = graph.CreateShiftNode(MediaTime.Zero);
        graph.Connect(source, shift);
        var resample = graph.CreateResampleNode(44100);
        graph.Connect(shift, resample);
        using var first = resample.Process(Exact(MediaTime.Zero, 257, 48000));

        graph.BeginUpdate(graph.Nodes);
        var replacement = graph.AddNode(new FormatInputNode { Value = -0.75f });
        var reusedShift = graph.CreateShiftNode(MediaTime.Zero);
        graph.Connect(replacement, reusedShift);
        var reusedResample = graph.CreateResampleNode(44100);
        graph.Connect(reusedShift, reusedResample);
        graph.EndUpdate();

        Assert.That(reusedShift, Is.SameAs(shift));
        Assert.That(reusedResample, Is.SameAs(resample));
        using var fresh = new ResampleNode { SourceSampleRate = 44100 };
        fresh.AddInput(replacement);
        var context = Exact(ExactDuration(257, 48000), 511, 48000);
        using var expected = fresh.Process(context);
        using var actual = reusedResample.Process(context);
        AssertBuffersEqual(actual, expected);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReconnectingSameInput_PreservesProcessAndFlushState(bool draining)
    {
        using var source = new AbsoluteSineNode(44100);
        using var uninterrupted = new ResampleNode { SourceSampleRate = 44100 };
        using var reconnected = new ResampleNode { SourceSampleRate = 44100 };
        uninterrupted.AddInput(source);
        reconnected.AddInput(source);
        var firstContext = Exact(MediaTime.Zero, 257, 48000);
        using var firstExpected = uninterrupted.Process(firstContext);
        using var firstActual = reconnected.Process(firstContext);

        reconnected.RemoveInput(source);
        reconnected.AddInput(source);
        var context = Exact(firstContext.TimeRange.End, 511, 48000);
        using var expected = draining ? uninterrupted.Flush(context) : uninterrupted.Process(context);
        using var actual = draining ? reconnected.Flush(context) : reconnected.Process(context);
        AssertBuffersEqual(actual, expected);
    }

    [TestCase(0)]
    [TestCase(4096)]
    public void ReconnectingSameInput_SeekResetsStreamingState(int startSample)
    {
        using var source = new AbsoluteSineNode(44100);
        using var resample = new ResampleNode { SourceSampleRate = 44100 };
        resample.AddInput(source);
        using var first = resample.Process(Exact(MediaTime.Zero, 257, 48000));

        resample.ClearInputs();
        resample.AddInput(source);
        using var fresh = new ResampleNode { SourceSampleRate = 44100 };
        fresh.AddInput(source);
        var context = Exact(ExactDuration(startSample, 48000), 511, 48000);
        using var expected = fresh.Process(context);
        using var actual = resample.Process(context);
        AssertBuffersEqual(actual, expected);
    }

    [TestCase(22050, 48000, 2)]
    [TestCase(44100, 32000, 2)]
    [TestCase(44100, 44100, 2)]
    [TestCase(44100, 48000, 1)]
    public void FormatChange_ResetsStreamingState(int sourceRate, int outputRate, int channels)
    {
        using var source = new FormatInputNode { Value = 0.75f };
        using var resample = new ResampleNode { SourceSampleRate = 44100 };
        resample.AddInput(source);
        var firstContext = Exact(MediaTime.Zero, 480, 48000);
        using var first = resample.Process(firstContext);

        source.ChannelCount = channels;
        source.Value = -0.75f;
        resample.SourceSampleRate = sourceRate;
        using var fresh = new ResampleNode { SourceSampleRate = sourceRate };
        fresh.AddInput(source);
        var context = Exact(firstContext.TimeRange.End, 511, outputRate);
        using var expected = fresh.Process(context);
        using var actual = resample.Process(context);
        AssertBuffersEqual(actual, expected);
    }

    private static void AssertBuffersEqual(AudioBuffer actual, AudioBuffer expected)
    {
        Assert.That(actual.SampleRate, Is.EqualTo(expected.SampleRate));
        Assert.That(actual.SampleCount, Is.EqualTo(expected.SampleCount));
        Assert.That(actual.ChannelCount, Is.EqualTo(expected.ChannelCount));
        for (int channel = 0; channel < expected.ChannelCount; channel++)
        {
            Assert.That(actual.GetChannelData(channel).ToArray(), Is.EqualTo(expected.GetChannelData(channel).ToArray()));
        }
    }

    private sealed class FormatInputNode : AudioNode
    {
        public int ChannelCount { get; set; } = 2;

        public float Value { get; set; }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var buffer = new AudioBuffer(context.SampleRate, ChannelCount, context.GetSampleCount());
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                buffer.GetChannelData(channel).Fill(Value);
            }

            return buffer;
        }
    }
}
