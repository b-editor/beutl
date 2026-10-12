namespace Beutl.Graphics.Backend;

/// <summary>What a device can attach for 3D rendering, as numbers a recording may read.</summary>
/// <param name="MaxAttachmentDimension">
/// The device's <see cref="IGraphicsContext.MaxAttachmentDimension"/>, or zero or less when it reported none.
/// </param>
/// <param name="MaxCubeFaceDimension">
/// The device's <see cref="IGraphicsContext.MaxCubeFaceDimension"/>, or zero or less when it reported none.
/// </param>
/// <remarks>
/// Both limits are kept because a device may set them differently and a cube face answers to both. A limit of
/// zero or less means the device reported none, and nothing is refused on it.
/// </remarks>
public readonly record struct Device3DExtentBudget(int MaxAttachmentDimension, int MaxCubeFaceDimension)
{
    /// <summary>A budget no device has answered for, which refuses nothing.</summary>
    public static Device3DExtentBudget Unreported => default;

    /// <summary>Whether this device can attach a <paramref name="width"/> by <paramref name="height"/> extent.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    public bool CanAttach(int width, int height)
        => DeviceExtentLimits.CanAttach(MaxAttachmentDimension, width, height);

    /// <summary>
    /// The largest cube face this device can both build as a cube image and render through per-face 2D
    /// attachments, or the one limit it reported, or zero when it reported neither.
    /// </summary>
    public int ResolveCubeFaceAttachmentBudget()
    {
        int cube = MaxCubeFaceDimension;
        int attachment = MaxAttachmentDimension;
        if (cube <= 0) return attachment > 0 ? attachment : 0;
        if (attachment <= 0) return cube;
        return Math.Min(cube, attachment);
    }

    /// <summary>Whether this device can both build and render into a cube of <paramref name="faceSize"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The face size is zero or negative.</exception>
    public bool CanAttachCubeFaces(int faceSize)
        => DeviceExtentLimits.CanAttach(ResolveCubeFaceAttachmentBudget(), faceSize, faceSize);

    /// <summary>Reads the limits <paramref name="context"/> reports.</summary>
    public static Device3DExtentBudget FromContext(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Device3DExtentBudget(context.MaxAttachmentDimension, context.MaxCubeFaceDimension);
    }
}
