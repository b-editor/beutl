using System.Numerics;

using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// A 2D scale ratio carried by <see cref="RenderNodeOperation.CorrectionScale"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RenderScale"/> describes how the bounds of a <see cref="RenderNodeOperation"/>
/// (in the parent's authoring coordinate space) relate to the actual produced raster.
/// The numeric convention is fixed and authoritative:
/// </para>
/// <list type="bullet">
/// <item><description><c>ScaleX = bounds.Width / raster.Width</c> (always ≥ 1).</description></item>
/// <item><description><c>ScaleY = bounds.Height / raster.Height</c> (always ≥ 1).</description></item>
/// <item><description><see cref="Identity"/> = <c>(1, 1)</c> means "raster matches bounds 1:1 (no proxy)".</description></item>
/// <item><description><c>(4, 4)</c> means "the upstream raster is 1/4 the linear size of its bounds; the compositor upscales 4× when blitting".</description></item>
/// </list>
/// <para>
/// Transformers use the <see cref="ToRasterX(float)"/> / <see cref="ToRasterY(float)"/> family to convert
/// authoring-space lengths into the raster coordinate system before invoking Skia.
/// The compositor uses <c>SKCanvas.Scale(ScaleX, ScaleY, ...)</c> to upscale at blit time.
/// </para>
/// </remarks>
public readonly struct RenderScale
    : IEquatable<RenderScale>,
      IEqualityOperators<RenderScale, RenderScale, bool>
{
    /// <summary>
    /// The identity scale, <c>(1, 1)</c>. Means "raster matches bounds 1:1 (no proxy)".
    /// </summary>
    public static readonly RenderScale Identity = new(1f, 1f);

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderScale"/> struct.
    /// </summary>
    /// <param name="scaleX">The horizontal scale ratio (<c>bounds.Width / raster.Width</c>); must be ≥ 1 and finite.</param>
    /// <param name="scaleY">The vertical scale ratio (<c>bounds.Height / raster.Height</c>); must be ≥ 1 and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scaleX"/> or <paramref name="scaleY"/> is not finite, is NaN, or is below 1.</exception>
    public RenderScale(float scaleX, float scaleY)
    {
        if (!float.IsFinite(scaleX) || scaleX < 1f)
            throw new ArgumentOutOfRangeException(nameof(scaleX), scaleX, "RenderScale components must be finite and ≥ 1.");
        if (!float.IsFinite(scaleY) || scaleY < 1f)
            throw new ArgumentOutOfRangeException(nameof(scaleY), scaleY, "RenderScale components must be finite and ≥ 1.");

        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    /// <summary>
    /// Gets the horizontal scale ratio (<c>bounds.Width / raster.Width</c>; always ≥ 1).
    /// </summary>
    public float ScaleX { get; }

    /// <summary>
    /// Gets the vertical scale ratio (<c>bounds.Height / raster.Height</c>; always ≥ 1).
    /// </summary>
    public float ScaleY { get; }

    /// <summary>
    /// Gets a value indicating whether this scale is exactly the identity (no proxy).
    /// </summary>
    public bool IsIdentity => ScaleX == 1f && ScaleY == 1f;

    /// <summary>
    /// Creates a uniform <see cref="RenderScale"/> from a single ratio.
    /// </summary>
    /// <param name="ratio">The uniform ratio (≥ 1).</param>
    public static RenderScale FromRatio(float ratio) => new(ratio, ratio);

    /// <summary>
    /// Creates a <see cref="RenderScale"/> per axis from a raster size and a bounds size.
    /// </summary>
    /// <param name="raster">The raster pixel size (must be &gt; 0 on both axes and ≤ <paramref name="bounds"/>).</param>
    /// <param name="bounds">The bounds pixel size (must be &gt; 0 on both axes).</param>
    /// <returns>A scale equal to <c>(bounds.Width / raster.Width, bounds.Height / raster.Height)</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when either dimension is non-positive or when the raster is larger than the bounds on either axis.</exception>
    public static RenderScale FromFrames(PixelSize raster, PixelSize bounds)
    {
        if (raster.Width <= 0 || raster.Height <= 0)
            throw new ArgumentException("Raster size must be positive on both axes.", nameof(raster));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("Bounds size must be positive on both axes.", nameof(bounds));
        if (raster.Width > bounds.Width || raster.Height > bounds.Height)
            throw new ArgumentException("Raster size must be ≤ bounds on both axes (CorrectionScale represents bounds-over-raster ≥ 1).", nameof(raster));

        return new RenderScale((float)bounds.Width / raster.Width, (float)bounds.Height / raster.Height);
    }

    /// <summary>
    /// Converts an authoring-space length on the X axis to raster-space (smaller).
    /// </summary>
    public float ToRasterX(float lengthAuthoring) => lengthAuthoring / ScaleX;

    /// <summary>
    /// Converts an authoring-space length on the Y axis to raster-space (smaller).
    /// </summary>
    public float ToRasterY(float lengthAuthoring) => lengthAuthoring / ScaleY;

    /// <summary>
    /// Converts an authoring-space length to raster-space using the geometric mean of <see cref="ScaleX"/> and <see cref="ScaleY"/>.
    /// </summary>
    public float ToRasterUniform(float lengthAuthoring) => lengthAuthoring / MathF.Sqrt(ScaleX * ScaleY);

    /// <summary>
    /// Converts an authoring-space size to raster-space (smaller).
    /// </summary>
    public Size ToRaster(Size sizeAuthoring) => new(sizeAuthoring.Width / ScaleX, sizeAuthoring.Height / ScaleY);

    /// <summary>
    /// Converts an authoring-space point to raster-space (smaller).
    /// </summary>
    public Point ToRaster(Point pointAuthoring) => new(pointAuthoring.X / ScaleX, pointAuthoring.Y / ScaleY);

    /// <summary>
    /// Converts a raster-space length on the X axis back to authoring-space (larger).
    /// </summary>
    public float ToAuthoringX(float lengthRaster) => lengthRaster * ScaleX;

    /// <summary>
    /// Converts a raster-space length on the Y axis back to authoring-space (larger).
    /// </summary>
    public float ToAuthoringY(float lengthRaster) => lengthRaster * ScaleY;

    /// <inheritdoc/>
    public bool Equals(RenderScale other) => ScaleX == other.ScaleX && ScaleY == other.ScaleY;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RenderScale other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ScaleX, ScaleY);

    /// <inheritdoc/>
    public override string ToString() => $"RenderScale({ScaleX}, {ScaleY})";

    public static bool operator ==(RenderScale left, RenderScale right) => left.Equals(right);

    public static bool operator !=(RenderScale left, RenderScale right) => !left.Equals(right);
}
