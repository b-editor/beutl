using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// A read-only view of one materialized input to a <see cref="GeometryNodeDescriptor"/>,
/// <see cref="SplitNodeDescriptor"/> or <see cref="ComputeNodeDescriptor"/> pass (feature 004, data-model §1,
/// research D8). The executor bakes the upstream operation into a pooled render target and hands the pass this
/// view; the pass may sample it (<see cref="AsShader"/>), blit it (<see cref="Draw"/>), or read its pixels
/// (<see cref="Snapshot"/>, for contour/geometry work on the input), but it can neither allocate targets nor
/// mutate the input. The backing buffer is owned and released by the executor — the view never disposes it.
/// </summary>
public sealed class EffectInput
{
    private readonly RenderTarget _target;
    private readonly bool _readbackPrepared;

    internal EffectInput(RenderTarget target, Rect bounds, EffectiveScale density, bool readbackPrepared = false)
    {
        _target = target;
        Bounds = bounds;
        Density = density;
        _readbackPrepared = readbackPrepared;
    }

    /// <summary>The input's logical bounds (position + size in logical units).</summary>
    public Rect Bounds { get; }

    /// <summary>The supply density the input's backing pixels exist at (device px per logical unit).</summary>
    public EffectiveScale Density { get; }

    /// <summary>The input buffer's device-pixel size.</summary>
    public PixelSize DeviceSize => new(_target.Width, _target.Height);

    /// <summary>The executor-owned backing buffer; drawn/sampled through the public members, never disposed by the pass.</summary>
    internal RenderTarget Target => _target;

    /// <summary>The Vulkan texture backing the input, or <see langword="null"/> on a raster context (used by compute passes).</summary>
    internal ITexture2D? Texture => _target.Texture;

    /// <summary>
    /// Builds an image shader over the input surface for sampling inside a whole-source shader or a
    /// <see cref="GeometrySession"/> paint. The caller owns and disposes the returned shader; the underlying
    /// <see cref="SKImage"/> is snapshotted so the shader is valid until disposed.
    /// </summary>
    public SKShader AsShader()
    {
        using SKImage image = _target.Value.Snapshot();
        return image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);
    }

    /// <summary>
    /// Reads the input's pixels into a fresh bitmap (RGBA16F/Premul/LinearSrgb). Geometry passes use this to
    /// analyze the input (contour tracing, tight-bounds detection); it reads the <em>input</em>, not the pass's
    /// own output, so it does not violate the session's no-snapshot rule. The caller disposes the bitmap.
    /// </summary>
    public Bitmap Snapshot()
    {
        if (!_readbackPrepared)
        {
            throw new InvalidOperationException(
                "This node did not declare requiresReadback. Set requiresReadback: true on its descriptor so "
                + "the executor can schedule and count synchronization before the callback.");
        }

        return _target.SnapshotPrepared();
    }

    /// <summary>
    /// Blits the input into <paramref name="canvas"/> at the given device-space point (call inside
    /// <see cref="ImmediateCanvas.PushDeviceSpace"/>). Mirrors today's <c>DrawRenderTarget</c> on a custom-effect
    /// target so migrated geometry effects keep identical device-space math.
    /// </summary>
    public void Draw(ImmediateCanvas canvas, Point devicePoint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.DrawRenderTarget(_target, devicePoint);
    }

    /// <summary>
    /// Draws the input into <paramref name="canvas"/> in <em>logical</em> space at its origin under the current
    /// transform — the honest replacement for the legacy <c>EffectTarget.Draw</c>. A density-1 buffer on a
    /// density-1 canvas point-blits; otherwise the buffer is Mitchell-resampled into its logical footprint
    /// (device size / supply density), so a geometry effect that composites under logical matrices keeps identical
    /// math. Sizing from the buffer footprint (not <see cref="Bounds"/>) is deliberate: bounds may be inflated by a
    /// downstream effect while the pixels still occupy the original footprint.
    /// </summary>
    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if ((Density.IsUnbounded || Density.Value == 1f) && canvas.Density == 1f)
        {
            canvas.DrawRenderTarget(_target, default);
        }
        else
        {
            float density = Density.IsUnbounded ? 1f : Density.Value;
            canvas.DrawRenderTargetScaled(_target, new Rect(0, 0, _target.Width / density, _target.Height / density));
        }
    }
}
