namespace Beutl.Audio;

public static class SpeakerLayoutExtensions
{
    public static AudioChannelLayout ToAudioChannelLayout(this SpeakerLayout layout) => layout switch
    {
        SpeakerLayout.Mono => AudioChannelLayout.Mono,
        SpeakerLayout.Surround51 => AudioChannelLayout.Surround51,
        SpeakerLayout.Surround71 => AudioChannelLayout.Surround71,
        _ => AudioChannelLayout.Stereo
    };
}
