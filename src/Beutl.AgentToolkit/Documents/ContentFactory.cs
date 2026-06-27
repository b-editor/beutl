using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media.Source;

namespace Beutl.AgentToolkit.Documents;

public sealed record ContentRequest(
    string Kind,
    string? MediaPath = null,
    string? Text = null,
    string? Shape = null);

public static class ContentFactory
{
    public static EngineObject Create(ContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind.Trim().ToLowerInvariant() switch
        {
            "image" => CreateImage(RequireExistingMedia(request.MediaPath, request.Kind)),
            "video" => CreateVideo(RequireExistingMedia(request.MediaPath, request.Kind)),
            "text" => CreateText(request.Text ?? string.Empty),
            "shape" => CreateShape(request.Shape),
            "group" => new DrawableGroup(),
            "audio" or "sound" => CreateSound(RequireExistingMedia(request.MediaPath, request.Kind)),
            _ => throw new ReconcileException(new ToolError(
                ErrorCode.UnknownType,
                $"Unsupported content kind '{request.Kind}'.",
                request.Kind,
                "Use one of image, video, text, shape, group, or audio."))
        };
    }

    private static SourceImage CreateImage(string path)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(Path.GetFullPath(path)));
        return new SourceImage
        {
            Source = { CurrentValue = source }
        };
    }

    private static SourceVideo CreateVideo(string path)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(Path.GetFullPath(path)));
        return new SourceVideo
        {
            Source = { CurrentValue = source }
        };
    }

    private static TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = { CurrentValue = text }
        };
    }

    private static EngineObject CreateShape(string? shape)
    {
        return (shape ?? "rect").Trim().ToLowerInvariant() switch
        {
            "rect" or "rectangle" => new RectShape(),
            "ellipse" => new EllipseShape(),
            _ => throw new ReconcileException(new ToolError(
                ErrorCode.UnknownType,
                $"Unsupported shape kind '{shape}'.",
                shape,
                "Use rect or ellipse."))
        };
    }

    private static SourceSound CreateSound(string path)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(Path.GetFullPath(path)));
        return new SourceSound
        {
            Source = { CurrentValue = source }
        };
    }

    private static string RequireExistingMedia(string? path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.MediaNotFound,
                $"A media path is required for {kind} content.",
                kind));
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.MediaNotFound,
                $"Media file not found: {fullPath}",
                fullPath));
        }

        return fullPath;
    }
}
