using Beutl.Audio;
using Beutl.ProjectSystem;

namespace Beutl;

public static class SceneRateExtensions
{
    public static AudioChannelLayout GetChannelLayout(this Scene? scene)
        => scene.GetSpeakerLayout().ToAudioChannelLayout();

    // The scene's speaker layout, or stereo when there is no scene.
    public static SpeakerLayout GetSpeakerLayout(this Scene? scene)
        => scene?.ChannelLayout.Value ?? SpeakerLayout.Stereo;

    // The scene's frame rate, or 30 when it is not a positive rate or there is no scene.
    public static Rational GetFrameRate(this Scene? scene)
        => Scene.ReadFrameRate(scene);

    // The scene's sample rate, or 44100 when it is not positive or there is no scene.
    public static int GetSampleRate(this Scene? scene)
        => scene?.SampleRate.Value is > 0 and var rate ? rate : 44100;
}
