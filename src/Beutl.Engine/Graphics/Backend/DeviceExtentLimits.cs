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

    /// <summary>Refuses an extent past what a device can make of an image created with attachment usage.</summary>
    /// <param name="maxImageDimension">The device's image limit for the kind, or zero or less when unknown.</param>
    /// <param name="maxFramebufferWidth">The device's framebuffer width limit, or zero or less when unknown.</param>
    /// <param name="maxFramebufferHeight">The device's framebuffer height limit, or zero or less when unknown.</param>
    /// <remarks>
    /// Every texture a context creates carries a colour or depth attachment usage bit, and Vulkan bounds
    /// such an image by the framebuffer limits at creation (VUID-VkImageCreateInfo-usage-00964), whether
    /// or not it is ever attached. So a material map that is only sampled is still held to them here; a
    /// texture that may be wider than a framebuffer would need to be created without attachment usage.
    /// The framebuffer limits may differ per axis, so each is measured against its own.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="InvalidOperationException">The extent exceeds the image limit or a framebuffer limit.</exception>
    public static void ThrowIfCannotMakeAttachableImage(
        int maxImageDimension, int maxFramebufferWidth, int maxFramebufferHeight, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (maxImageDimension > 0 && (width > maxImageDimension || height > maxImageDimension))
        {
            throw new InvalidOperationException(
                $"A {width}x{height} pixel texture exceeds the {maxImageDimension} pixels this device can make "
                + "of an image.");
        }

        ThrowIfCannotBuildFramebuffer(maxFramebufferWidth, maxFramebufferHeight, width, height);
    }

    /// <summary>Refuses a framebuffer extent past what a device can build, axis by axis.</summary>
    /// <param name="maxFramebufferWidth">The device's framebuffer width limit, or zero or less when unknown.</param>
    /// <param name="maxFramebufferHeight">The device's framebuffer height limit, or zero or less when unknown.</param>
    /// <remarks>
    /// The two framebuffer limits may differ, so a framebuffer is measured against each rather than
    /// against the square <see cref="IGraphicsContext.MaxAttachmentDimension"/>, which would refuse a
    /// framebuffer the device can build whenever one axis is allowed more than the other.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="InvalidOperationException">An axis exceeds its framebuffer limit.</exception>
    public static void ThrowIfCannotBuildFramebuffer(
        int maxFramebufferWidth, int maxFramebufferHeight, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (maxFramebufferWidth > 0 && width > maxFramebufferWidth)
        {
            throw new InvalidOperationException(
                $"A {width}x{height} pixel attachment is wider than the {maxFramebufferWidth} pixels this "
                + "device can build a framebuffer of.");
        }

        if (maxFramebufferHeight > 0 && height > maxFramebufferHeight)
        {
            throw new InvalidOperationException(
                $"A {width}x{height} pixel attachment is taller than the {maxFramebufferHeight} pixels this "
                + "device can build a framebuffer of.");
        }
    }

    /// <summary>Whether an extent is one a device with <paramref name="budget"/> can attach.</summary>
    /// <param name="budget">The device's attachment limit, or zero or less when it reported none.</param>
    /// <remarks>
    /// The question <see cref="ThrowIfCannotAttach"/> asks, separated from the context that answers it, so a
    /// caller holding only the limit asks the same one. A recording is such a caller: it may not reach the
    /// process for a device, but it is given the limit as request state and has to decide whether the
    /// allocation it is describing is one the execution will refuse. An unreported limit refuses nothing.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    public static bool CanAttach(int budget, int width, int height)
    {
        // The backend casts a dimension to uint on the way to the driver, so a negative one would arrive
        // as an enormous extent and step past the budget below, and a zero one is an image the driver
        // may not build at all. Either is a caller error, not a device limit, and an attachment that a 3D
        // node reports as its extent has to be positive for (0, 0) to keep meaning "none".
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return budget <= 0 || (width <= budget && height <= budget);
    }

    /// <summary>Refuses a 2D extent past what <paramref name="context"/> can attach.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">The extent exceeds the device's attachment limit.</exception>
    public static void ThrowIfCannotAttach(IGraphicsContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);
        int budget = context.MaxAttachmentDimension;
        if (CanAttach(budget, width, height))
            return;

        throw new InvalidOperationException(
            $"A {width}x{height} pixel 3D attachment exceeds the {budget} pixels this device can attach.");
    }

    /// <summary>Refuses a cube face past what <paramref name="context"/> can build as a cube image.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The face size is negative.</exception>
    /// <exception cref="InvalidOperationException">The face exceeds the device's cube image limit.</exception>
    public static void ThrowIfCannotMakeCubeFace(IGraphicsContext context, int faceSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegative(faceSize);
        int budget = context.MaxCubeFaceDimension;
        if (budget <= 0 || faceSize <= budget)
            return;

        throw new InvalidOperationException(
            $"A {faceSize} pixel cube face exceeds the {budget} pixels this device can make of a cube map.");
    }

    /// <summary>
    /// Refuses a cube face that cannot be both built as a cube image and rendered through a 2D attachment.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The face size is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">The face exceeds either device limit.</exception>
    public static void ThrowIfCannotAttachCubeFaces(IGraphicsContext context, int faceSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(faceSize);
        int budget = ResolveCubeFaceAttachmentBudget(context);
        if (budget <= 0 || faceSize <= budget)
            return;

        throw new InvalidOperationException(
            $"A {faceSize} pixel shadow cube face exceeds the {budget} pixels this device can render into "
            + "a cube map.");
    }
}
