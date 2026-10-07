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
/// <see cref="Rendering.BufferDimensionBudget.ForDevice(IGraphicsContext?)"/> does for 2D.
/// </remarks>
internal static class DeviceExtentLimits
{
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

    /// <summary>
    /// Refuses a cube face that cannot be both built as a cube image and rendered through a 2D attachment.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The face size is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">The face exceeds either device limit.</exception>
    public static void ThrowIfCannotAttachCubeFaces(IGraphicsContext context, int faceSize)
    {
        Device3DExtentBudget budget = Device3DExtentBudget.FromContext(context);
        if (budget.CanAttachCubeFaces(faceSize))
            return;

        throw new InvalidOperationException(
            $"A {faceSize} pixel shadow cube face exceeds the {budget.ResolveCubeFaceAttachmentBudget()} "
            + "pixels this device can render into a cube map.");
    }
}
