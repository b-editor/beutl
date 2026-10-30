using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceNodeTests
{
    [TestCase(-2000L, false)]
    [TestCase(2000L, true)]
    public void Process_ReportsUnavailableOffsetsWithoutChangingPlaybackFallback(long offset, bool failed)
    {
        TestMediaHelper.RegisterTestDecoder();
        var source = SoundSource.Open(TestMediaHelper.CreateTestAudioFile(192000, durationSeconds: 28800));
        using var resource = source.ToResource(CompositionContext.Default);
        using var node = new SourceNode { Source = (resource, 0) };
        using var reads = new AudioReadScope();
        var range = new TimeRange(MediaTime.FromSamples(int.MaxValue + offset, 192000), MediaTime.FromSeconds(0.01));
        using var buffer = node.Process(new AudioProcessContext(range, 192000, new AnimationSampler(), null));
        Assert.That(reads.HasFailures, Is.EqualTo(failed));
        Assert.That(buffer.GetChannelData(0).ToArray().All(value => value == 0), Is.EqualTo(failed));
    }

    // Regression: a source whose media failed to open has SampleRate == 0. SourceNode must return a
    // silent buffer for it, not throw from TimeToSampleIndex on the zero rate.
    [Test]
    public void Process_UnloadedSourceWithZeroSampleRate_ReturnsSilenceInsteadOfThrowing()
    {
        var resource = new SoundSource.Resource();
        Assume.That(resource.SampleRate, Is.Zero, "precondition: an unloaded resource has SampleRate 0");

        var node = new SourceNode { Source = (resource, 0) };
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var context = new AudioProcessContext(range, 44100, new AnimationSampler(), null);

        AudioBuffer? buffer = null;
        Assert.DoesNotThrow(() => buffer = node.Process(context));

        using (buffer)
        {
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer!.SampleCount, Is.EqualTo(context.GetSampleCount()));

            float[] left = buffer.GetChannelData(0).ToArray();
            float[] right = buffer.GetChannelData(1).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(left, Is.All.EqualTo(0f), "left channel must be silent");
                Assert.That(right, Is.All.EqualTo(0f), "right channel must be silent");
            });
        }
    }
}
