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

    internal EffectInput(RenderTarget target, Rect bounds, EffectiveScale density)
    {
        _target = target;
        Bounds = bounds;
        Density = density;
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
    public Bitmap Snapshot() => _target.Snapshot();

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
}
