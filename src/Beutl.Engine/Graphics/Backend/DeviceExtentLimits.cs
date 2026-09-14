using Beutl.Graphics3D.Nodes;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Refuses an extent the device cannot make before its allocator is asked.
/// </summary>
/// <remarks>
/// Neither shipped driver reports an over-limit image as a failed allocation: SwiftShader builds a
/// framebuffer past its own limit and answers success, and MoltenVK aborts the process on a Metal
/// assertion. Neither reaches a <c>catch</c>, so the extent has to be refused on the way in. Each resource
/// kind answers to its own device limit, so the question is asked per kind rather than through one number.
/// A limit of zero or less means the device did not answer, and the allocator is left to decide, as
/// <see cref="Rendering.RenderScaleUtilities.ResolveMaxBufferDimension(IGraphicsContext?)"/> does for 2D.
/// </remarks>
internal static class DeviceExtentLimits
{
    /// <summary>
    /// The largest cube face the device can both build as a cube image and render through per-face 2D
    /// attachments, or the larger of the two limits when one was not reported.
    /// </summary>
    /// <remarks>
    /// <see cref="PointShadowPass"/> draws each face into a 2D attachment and copies it into the cube, so a
    /// face has to fit both <see cref="IGraphicsContext.MaxCubeFaceDimension"/> and
    /// <see cref="IGraphicsContext.MaxAttachmentDimension"/>, and a device may set the two differently.
    /// </remarks>
    public static int ResolveCubeFaceAttachmentBudget(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int cube = context.MaxCubeFaceDimension;
        int attachment = context.MaxAttachmentDimension;
        if (cube <= 0) return attachment;
        if (attachment <= 0) return cube;
        return Math.Min(cube, attachment);
    }

    /// <summary>Refuses a 2D extent past what <paramref name="context"/> can attach.</summary>
    /// <exception cref="InvalidOperationException">The extent exceeds the device's attachment limit.</exception>
    public static void ThrowIfCannotAttach(IGraphicsContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);
        int budget = context.MaxAttachmentDimension;
        if (budget <= 0 || (width <= budget && height <= budget))
            return;

        throw new InvalidOperationException(
            $"A {width}x{height} pixel 3D attachment exceeds the {budget} pixels this device can attach.");
    }

    /// <summary>Refuses a cube face past what <paramref name="context"/> can build as a cube image.</summary>
    /// <exception cref="InvalidOperationException">The face exceeds the device's cube image limit.</exception>
    public static void ThrowIfCannotMakeCubeFace(IGraphicsContext context, int faceSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        int budget = context.MaxCubeFaceDimension;
        if (budget <= 0 || faceSize <= budget)
            return;

        throw new InvalidOperationException(
            $"A {faceSize} pixel cube face exceeds the {budget} pixels this device can make of a cube map.");
    }

    /// <summary>
    /// Refuses a cube face that cannot be both built as a cube image and rendered through a 2D attachment.
    /// </summary>
    /// <exception cref="InvalidOperationException">The face exceeds either device limit.</exception>
    public static void ThrowIfCannotAttachCubeFaces(IGraphicsContext context, int faceSize)
    {
        int budget = ResolveCubeFaceAttachmentBudget(context);
        if (budget <= 0 || faceSize <= budget)
            return;

        throw new InvalidOperationException(
            $"A {faceSize} pixel shadow cube face exceeds the {budget} pixels this device can render into "
            + "a cube map.");
    }
}
