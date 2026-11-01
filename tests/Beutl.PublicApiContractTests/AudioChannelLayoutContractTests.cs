using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public class AudioChannelLayoutContractTests
{
    [Test]
    public void ExistingComposerImplementationsAndCountBasedContextsRemainUsable()
    {
        using IComposer composer = new LegacyComposer();
        using var context = new AudioContext(48000, 6);
        Assert.That(composer.ChannelLayout, Is.EqualTo(AudioChannelLayout.Stereo));
        Assert.That(context.ChannelLayout, Is.EqualTo(AudioChannelLayout.Surround51));
    }

    private sealed class LegacyComposer : IComposer
    {
        public bool IsAudioRendering => false;
        public bool IsDisposed => false;
        public int SampleRate => 48000;
        public AudioBuffer? Compose(TimeRange range, CompositionFrame frame) => null;
        public AudioBuffer? Flush(TimeRange range, CompositionEligibility? eligibility = null) => null;
        public int GetTotalLatencySamples(int sampleRate) => 0;
        public int GetDrainLatencySamples(int sampleRate) => 0;
        public void Dispose() { }
    }
}
