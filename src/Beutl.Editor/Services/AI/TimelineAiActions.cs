using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>An AI action the timeline offers on an element.</summary>
public enum TimelineAiAction
{
    RemoveBackground,
    Upscale,
    Restyle,
    RemoveObject,
    Outpaint,

    /// <summary>A clip that starts on the picture, placed after it.</summary>
    VideoFromImage,

    /// <summary>A clip that starts on the clip's last frame, placed after it.</summary>
    ContinueVideo,

    /// <summary>The clip's own continuation, placed after it.</summary>
    ExtendVideo,

    /// <summary>The clip remade from a prompt, placed above it.</summary>
    EditVideo,

    /// <summary>Opens the AI tab's subtitle page on the element's sound.</summary>
    Subtitles,
}

/// <summary>Decides which AI actions an element's media allows.</summary>
public static class TimelineAiActions
{
    public static AiImageEditTask? ImageTaskOf(TimelineAiAction action) => action switch
    {
        TimelineAiAction.RemoveBackground => AiImageEditTask.RemoveBackground,
        TimelineAiAction.Upscale => AiImageEditTask.Upscale,
        TimelineAiAction.Restyle => AiImageEditTask.Restyle,
        TimelineAiAction.RemoveObject => AiImageEditTask.RemoveObject,
        TimelineAiAction.Outpaint => AiImageEditTask.Outpaint,
        _ => null,
    };

    /// <summary>The actions for the element, in menu order; empty when its media allows none.</summary>
    public static IReadOnlyList<TimelineAiAction> For(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var actions = new List<TimelineAiAction>();
        if (TimelineFrameCapture.GetImagePath(element) is not null)
        {
            actions.AddRange(
            [
                TimelineAiAction.RemoveBackground,
                TimelineAiAction.Upscale,
                TimelineAiAction.Restyle,
                TimelineAiAction.RemoveObject,
                TimelineAiAction.Outpaint,
                TimelineAiAction.VideoFromImage,
            ]);
        }

        if (TimelineFrameCapture.GetVideoPath(element) is { } video)
        {
            actions.Add(TimelineAiAction.ContinueVideo);
            if (MayExtend(element, video))
                actions.Add(TimelineAiAction.ExtendVideo);
            if (GenerativeInputs.IsSupportedVideoFile(video))
                actions.Add(TimelineAiAction.EditVideo);
        }

        if (FindSound(element) is not null)
            actions.Add(TimelineAiAction.Subtitles);

        return actions;
    }

    /// <summary>
    /// The element whose sound a subtitle request should use: the element itself when it plays
    /// a sound file, or for a video, the sound element that plays the same file beside it.
    /// </summary>
    public static Element? FindSound(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (SoundPathOf(element) is not null)
            return element;

        if (TimelineFrameCapture.GetVideoPath(element) is not { } video
            || element.HierarchicalParent is not Scene scene)
        {
            return null;
        }

        return scene.Children.FirstOrDefault(other =>
            !ReferenceEquals(other, element)
            && other.Range.Intersects(element.Range)
            && string.Equals(SoundPathOf(other), video, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the clip can be extended. The service continues a clip from its file's last
    /// frame, so the element must end there, play at its own pace and not loop, or the
    /// continuation would not follow what is shown. Reads the clip's length, so it is asked
    /// when the popup opens rather than for every menu.
    /// </summary>
    public static bool CanExtend(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (TimelineFrameCapture.GetVideoPath(element) is not { } path || !MayExtend(element, path))
            return false;
        SourceVideo video = element.Objects.OfType<SourceVideo>().First();
        if (!video.TryGetOriginalDuration(out TimeSpan remaining))
            return false;

        TimeSpan frame = element.HierarchicalParent is Scene scene
            ? SceneTimeRangeService.GetFrameDuration(scene)
            : TimeSpan.FromSeconds(1d / 30);
        return (remaining - element.Length).Duration() <= frame;
    }

    // The checks that need no media read, so the menu can offer the action cheaply.
    private static bool MayExtend(Element element, string path)
    {
        if (!GenerativeInputs.IsSupportedVideoFile(path))
            return false;
        SourceVideo video = element.Objects.OfType<SourceVideo>().First();
        return !video.IsLoop.CurrentValue && video.IsLoop.Animation is null
            && video.Speed.Animation is null && Math.Abs(video.Speed.CurrentValue - 100f) <= 0.01f;
    }

    private static string? SoundPathOf(Element element)
        => element.Objects
            .OfType<SourceSound>()
            .Select(sound => sound.Source.CurrentValue)
            .Where(source => source is { HasUri: true } && source.Uri.IsFile)
            .Select(source => source!.Uri.LocalPath)
            .FirstOrDefault();
}
