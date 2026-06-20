using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Source;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceNodeTests
{
    // Regression: a SoundSource whose media failed to open (moved / deleted / unsupported file) keeps
    // SampleRate == 0 because the MediaReader never opened. SourceNode must return a silent buffer for it,
    // not throw from AudioMath.TimeToSampleIndex(.., 0) and leak the rented buffer allocated before the
    // try block. Pre-fix this regressed the prior "missing audio file -> silence" behavior into an uncaught
    // render-pipeline exception plus a pooled-buffer leak.
    [Test]
    public void Process_UnloadedSourceWithZeroSampleRate_ReturnsSilenceInsteadOfThrowing()
    {
        var resource = new SoundSource.Resource();
        Assume.That(resource.SampleRate, Is.Zero, "precondition: an unloaded resource has SampleRate 0");

        var node = new SourceNode { Source = (resource, 0) };
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
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
