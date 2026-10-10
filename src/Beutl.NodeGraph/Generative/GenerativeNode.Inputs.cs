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

    // Rasterizes each reference as "reference-{n}", skipping the ones that draw nothing.
    internal static void AddRasterizedReferences(
        List<GenerativeImageInput> into,
        List<RenderNode?> nodes,
        GraphCompositionContext context)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (RasterizeInput(nodes[i], $"reference-{i + 1}", context) is { } input)
                into.Add(input);
        }
    }

    // A copy the caller owns, to encode for upload; the rasterization stays with its renderer.
    private static Bitmap? RenderToBitmap(RenderNode? node, GraphCompositionContext context)
    {
        if (node is null)
            return null;

        try
        {
            return RasterizeCopy(
                node,
                new RenderNodeRenderRequest { Intent = RenderIntent.Preview, ManageCacheLifecycle = false });
        }
        catch (RenderTargetDomainRequiredException) when (context.TargetDomain is { } domain)
        {
            return RasterizeCopy(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = domain,
                ManageCacheLifecycle = false,
            });
        }
    }

    private static Bitmap? RasterizeCopy(RenderNode node, RenderNodeRenderRequest request)
    {
        using var renderer = new RenderNodeRenderer(node, request);
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        return rasterization.Bitmap?.Clone();
    }

    /// <summary>Reads a clip handed to a generation as input.</summary>
    protected static GenerativeFileInput? ReadVideoInput(VideoSource? source, string name)
    {
        if (source is not { HasUri: true } || !source.Uri.IsFile)
            return null;

        return GenerativeInputs.ReadVideoFile(source.Uri.LocalPath, name);
    }
}
