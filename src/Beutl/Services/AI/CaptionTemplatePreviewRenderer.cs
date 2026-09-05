using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal static class CaptionTemplatePreviewRenderer
{
    public static async ValueTask<byte[]?> RenderPngAsync(
        IReadOnlyList<Element> elements,
        Beutl.Media.PixelSize frameSize,
        CancellationToken cancellationToken)
    {
        if (elements.Count == 0 || frameSize.Width <= 0 || frameSize.Height <= 0)
            return null;

        return await ObjectTemplatePreviewRenderer.RenderElementsPngAsync(elements, frameSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
