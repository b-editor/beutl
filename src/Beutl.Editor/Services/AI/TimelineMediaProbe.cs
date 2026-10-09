using Beutl.Media.Decoding;

namespace Beutl.Editor.Services.AI;

/// <summary>Reads what a timeline generation needs to know about a media file.</summary>
public interface ITimelineMediaProbe
{
    /// <summary>The length of a clip's picture, or null when it cannot be read.</summary>
    TimeSpan? GetVideoDuration(string path);

    bool HasAudio(string path);
}

/// <summary>Reads media through the registered decoders.</summary>
public sealed class TimelineMediaProbe : ITimelineMediaProbe
{
    public static TimelineMediaProbe Instance { get; } = new();

    public TimeSpan? GetVideoDuration(string path)
    {
        try
        {
            using var reader = MediaReader.Open(path, new MediaOptions(MediaMode.Video));
            if (!reader.HasVideo)
                return null;
            double seconds = reader.VideoInfo.Duration.ToDouble();
            return double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool HasAudio(string path)
    {
        try
        {
            using var reader = MediaReader.Open(path, new MediaOptions(MediaMode.Audio));
            return reader.HasAudio;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
