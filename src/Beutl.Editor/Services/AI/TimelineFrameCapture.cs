using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>Takes the pictures and clips a timeline generation starts from out of an element.</summary>
public static class TimelineFrameCapture
{
    /// <summary>The file of the picture an image element shows, or null when it shows none.</summary>
    public static string? GetImagePath(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Objects
            .OfType<SourceImage>()
            .Select(image => image.Source.CurrentValue)
            .Where(source => source is { HasUri: true } && source.Uri.IsFile)
            .Select(source => source!.Uri.LocalPath)
            .FirstOrDefault(File.Exists);
    }

    /// <summary>The file of the clip a video element plays, or null when it plays none.</summary>
    public static string? GetVideoPath(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Objects
            .OfType<SourceVideo>()
            .Select(video => video.Source.CurrentValue)
            .Where(source => source is { HasUri: true } && source.Uri.IsFile)
            .Select(source => source!.Uri.LocalPath)
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Saves the last frame a video element shows as a PNG in <paramref name="directory"/> and
    /// returns its path, or null when there is no frame to read. The frame is the clip's own,
    /// with its trim, speed and loop applied but without the element's transform or effects,
    /// so a clip generated from it continues the footage rather than the composition.
    /// </summary>
    public static async Task<string?> CaptureLastFrameAsync(
        Element element,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        SourceVideo? video = element.Objects
            .OfType<SourceVideo>()
            .FirstOrDefault(candidate => candidate.Source.CurrentValue is { HasUri: true });
        if (video is null)
            return null;

        TimeSpan frame = element.HierarchicalParent is Scene scene
            ? SceneTimeRangeService.GetFrameDuration(scene)
            : TimeSpan.FromSeconds(1d / 30);
        TimeSpan time = element.Range.End - frame;
        if (time < element.Start)
            time = element.Start;

        Ref<Bitmap>? captured = await RenderThread.Dispatcher.InvokeAsync(
            () =>
            {
                using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(time));
                if (resource.Source is not { IsDisposed: false } source)
                    return null;
                TimeSpan position = resource.RequestedPosition + resource.OffsetPosition;
                return source.Read(position, out Ref<Bitmap>? bitmap) ? bitmap : null;
            },
            ct: cancellationToken);
        if (captured is null)
            return null;

        using (captured)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            return captured.Value.Save(path, EncodedImageFormat.Png) ? path : null;
        }
    }
}
