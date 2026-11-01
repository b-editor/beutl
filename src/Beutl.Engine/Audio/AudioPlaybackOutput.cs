using Microsoft.Extensions.Logging;

namespace Beutl.Audio;

internal static class AudioPlaybackOutput
{
    public static bool UseMultichannelBackend(AudioChannelLayout project) => project.ChannelCount > 2;

    // A factory seam keeps backend selection and initialization-failure coverage device-free.
    public static T? TryCreateAVSampleBufferRenderer<T>(AudioChannelLayout project, bool isMacOS,
        Func<T> createRenderer, ILogger logger) where T : class
    {
        if (!isMacOS || !UseMultichannelBackend(project)) return null;
        try
        {
            return createRenderer();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create AVSampleBufferAudioRenderer; falling back to OpenAL Soft.");
            return null;
        }
    }

    public static (AudioChannelLayout Layout, AudioChannelMatrix? Matrix) Create(
        AudioChannelLayout project, AudioChannelLayout device)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(project.ChannelCount, nameof(project));

        // Mono also uses a stereo buffer to avoid the backend's positional mono gain.
        AudioChannelLayout output = AudioChannelLayout.Stereo;
        if (UseMultichannelBackend(project) && device.ChannelCount > 2 && device.HasPositions)
        {
            output = (device.Speakers & project.Speakers) == project.Speakers ? project : device;
        }

        return (output, project == output ? null : AudioChannelMatrix.Create(project, output));
    }
}
