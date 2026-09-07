using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal static class CaptionTemplatePreviewRenderer
{
    private static readonly PixelSize s_outputSize = new(1024, 576);

    public static async ValueTask<byte[]?> RenderPngAsync(
        IReadOnlyList<Element> elements,
        Beutl.Media.PixelSize frameSize,
        CancellationToken cancellationToken)
    {
        if (elements.Count == 0 || frameSize.Width <= 0 || frameSize.Height <= 0)
            return null;

        // Render captions at 4x the object-template thumbnail density. The UI scales this bitmap
        // down to its display size, preserving glyph detail that would otherwise be lost at 256x144.
        return await ObjectTemplatePreviewRenderer.RenderElementsPngAsync(
                elements,
                frameSize,
                s_outputSize,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
