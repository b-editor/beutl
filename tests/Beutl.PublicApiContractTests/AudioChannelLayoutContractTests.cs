using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Extensibility;
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

    [Test]
    public void ExistingSampleProvidersDefaultToStereo()
    {
        using ISampleProvider provider = new LegacySampleProvider();
        Assert.That(provider.ChannelLayout, Is.EqualTo(AudioChannelLayout.Stereo));
    }

    // Deliberately omits ChannelLayout to pin compatibility with existing extensions.
    private sealed class LegacySampleProvider : ISampleProvider
    {
        public long SampleCount => 0;
        public long SampleRate => 44100;
        public ValueTask<AudioBuffer?> ReadNextSamples(int length) => ValueTask.FromResult<AudioBuffer?>(null);
        public void Dispose() { }
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
