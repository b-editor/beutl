namespace Beutl.Audio;

internal static class AudioPlaybackOutput
{
    public static (AudioChannelLayout Layout, AudioChannelMatrix? Matrix) Create(
        AudioChannelLayout project, AudioChannelLayout device)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(project.ChannelCount, nameof(project));

        // Mono also uses a stereo buffer to avoid the backend's positional mono gain.
        AudioChannelLayout output = AudioChannelLayout.Stereo;
        if (project.ChannelCount > 2 && device.ChannelCount > 2 && device.HasPositions)
        {
            output = (device.Speakers & project.Speakers) == project.Speakers ? project : device;
        }

        return (output, project == output ? null : AudioChannelMatrix.Create(project, output));
    }
}
