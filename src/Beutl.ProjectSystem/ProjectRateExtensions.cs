using Beutl.Audio;

namespace Beutl;

public static class ProjectRateExtensions
{
    public static AudioChannelLayout GetChannelLayout(this Project? project)
        => (project?.ChannelLayout.Value ?? SpeakerLayout.Stereo).ToAudioChannelLayout();

    // The project's frame rate, or 30 when it is not a positive rate or there is no project.
    public static Rational GetFrameRate(this Project? project) => Project.ReadFrameRate(project);

    // The project's sample rate, or 44100 when it is not positive or there is no project.
    public static int GetSampleRate(this Project? project) => project?.SampleRate.Value is > 0 and var rate ? rate : 44100;
}
