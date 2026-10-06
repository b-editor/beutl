using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Generative;

public abstract partial class GenerativeNode
{
    /// <summary>
    /// Renders a picture input the way the preview does and encodes it for upload.
    /// Returns null when there is nothing to draw.
    /// </summary>
    protected static GenerativeImageInput? RasterizeInput(RenderNode? node, string name, GraphCompositionContext context)
    {
        using Bitmap? bitmap = RenderToBitmap(node, context);
        if (bitmap is null)
            return null;

        using var stream = new MemoryStream();
        if (!bitmap.Save(stream, EncodedImageFormat.Png))
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_InputRenderFailed);
        return new GenerativeImageInput($"{name}.png", stream.ToArray());
    }

    // A copy the caller owns, to encode for upload; the rasterization stays with its renderer.
    private static Bitmap? RenderToBitmap(RenderNode? node, GraphCompositionContext context)
    {
        if (node is null)
            return null;

        try
        {
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRenderRequest { Intent = RenderIntent.Preview, ManageCacheLifecycle = false });
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            return rasterization.Bitmap?.Clone();
        }
        catch (RenderTargetDomainRequiredException) when (context.TargetDomain is { } domain)
        {
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = domain,
                ManageCacheLifecycle = false,
            });
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            return rasterization.Bitmap?.Clone();
        }
    }

    /// <summary>
    /// The largest clip read into a request: the service's source limit. Anything larger is
    /// refused before it is read, rather than loaded whole only to be refused later.
    /// </summary>
    internal const long MaxVideoInputBytes = 32L * 1024 * 1024;

    /// <summary>Reads a clip handed to a generation as input.</summary>
    protected static GenerativeFileInput? ReadVideoInput(VideoSource? source, string name)
    {
        if (source is not { HasUri: true } || !source.Uri.IsFile)
            return null;

        string path = source.Uri.LocalPath;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string mediaType = extension switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable),
        };
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxVideoInputBytes)
                throw new GenerativeExecutionException(Strings.AiFileTooLarge);
            byte[] content = new byte[stream.Length];
            stream.ReadExactly(content);
            return new GenerativeFileInput($"{name}{extension}", mediaType, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable, ex);
        }
    }
}
