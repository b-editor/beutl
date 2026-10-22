using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Composition;

namespace Beutl.UnitTests.Engine.Audio;

// A sound or audio effect whose type did not load, for example from a plugin that is not installed, leaves the
// audio as it would be without it.
public class FallbackAudioTests
{
    [Test]
    public void AFallbackSound_IsSilent()
    {
        var sound = new FallbackSound();
        using var resource = (Sound.Resource)sound.ToResource(CompositionContext.Default);
        using var context = new AudioContext(48000, 2);

        sound.Compose(context, resource);

        Assert.That(context.GetOutputNodes(), Is.Empty);
    }

    [Test]
    public void AFallbackAudioEffect_PassesItsInputOn()
    {
        using var context = new AudioContext(48000, 2);
        AudioNode input = context.CreateMixerNode();

        Assert.That(new FallbackAudioEffect().CreateNode(context, input), Is.SameAs(input));
    }
}
